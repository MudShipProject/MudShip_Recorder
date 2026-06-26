using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 形式非依存の GC フリー・チャンク書き込み。メインスレッドは事前確保した byte[] チャンクへ
    /// 1 フレーム分をコピーするだけ (アロケーション・GC ゼロ)、満杯になったチャンクをバックグラウンド
    /// IO スレッドへ渡してディスクへ書き出す。チャンクはプールで使い回す。
    ///
    /// ヘッダ (ファイル先頭) は呼び出し側が構築した byte[] をそのまま書き出す。frameCount は記録中 0 で、
    /// <see cref="Finish"/> 時にヘッダ内の所定オフセットへ書き戻す。これにより .msrm/.msrf など
    /// 任意の固定ストライド形式で共用できる。
    ///
    /// メインスレッド: ctor(ヘッダ書き込み) -> <see cref="BeginFrame"/>/<see cref="CommitFrame"/> -> <see cref="Finish"/>。
    /// IO スレッド  : 満杯チャンクのディスク書き出しのみ (Unity API は一切呼ばない)。
    /// </summary>
    internal sealed class ChunkedStreamWriter : IDisposable
    {
        struct Segment
        {
            public byte[] Buffer;
            public int Length;
        }

        readonly FileStream _fs;
        readonly int _stride;
        readonly int _chunkBytes;
        readonly long _frameCountFieldPos;

        readonly BlockingCollection<Segment> _full = new BlockingCollection<Segment>();
        readonly ConcurrentQueue<byte[]> _pool = new ConcurrentQueue<byte[]>();
        readonly Thread _ioThread;

        readonly byte[] _u32 = new byte[4]; // frameCount 書き戻し用

        byte[] _cur;
        int _pos;
        int _frameCount;
        bool _finished;

        volatile bool _faulted;
        public bool Faulted => _faulted;
        public Exception FaultException { get; private set; }
        public int FrameCount => _frameCount;
        public int Stride => _stride;

        /// <param name="filePath">出力先パス。</param>
        /// <param name="header">ファイル先頭に書き出すヘッダ全体。</param>
        /// <param name="frameCountFieldPos">header 内の frameCount(uint32) の先頭オフセット。Finish で書き戻す。</param>
        /// <param name="stride">1 フレームのバイト数。</param>
        /// <param name="settings">チャンクサイズ/プール数等。</param>
        public ChunkedStreamWriter(string filePath, byte[] header, long frameCountFieldPos, int stride, RecorderSettings settings)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("filePath is null or empty.", nameof(filePath));
            if (header == null)
                throw new ArgumentNullException(nameof(header));
            if (stride <= 0)
                throw new ArgumentOutOfRangeException(nameof(stride));
            if (!BitConverter.IsLittleEndian)
                throw new NotSupportedException("ChunkedStreamWriter assumes a little-endian platform.");

            settings = settings.Normalized();

            _stride = stride;
            int framesPerChunk = Math.Max(1, settings.ChunkBytes / _stride);
            _chunkBytes = framesPerChunk * _stride;
            _frameCountFieldPos = frameCountFieldPos;

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _fs.Write(header, 0, header.Length);

            for (int i = 0; i < settings.PooledChunkCount; i++)
                _pool.Enqueue(new byte[_chunkBytes]);

            _cur = Rent();
            _pos = 0;

            _ioThread = new Thread(IoLoop) { IsBackground = true, Name = "MSR-IO" };
            _ioThread.Start();
        }

        /// <summary>
        /// 次フレームの書き込み先 span (長さ = stride) を返す。呼び出し側がここへ 1 フレーム分を
        /// 書き込み、<see cref="CommitFrame"/> を呼ぶこと。faulted/finished 時は空 span を返す。
        /// </summary>
        public Span<byte> BeginFrame()
        {
            if (_faulted || _finished)
                return default;
            return _cur.AsSpan(_pos, _stride);
        }

        /// <summary><see cref="BeginFrame"/> で得た span への書き込みを確定し、次フレームへ進める。</summary>
        public void CommitFrame()
        {
            if (_faulted || _finished)
                return;

            _pos += _stride;
            _frameCount++;

            if (_pos == _chunkBytes)
                RotateChunk();
        }

        /// <summary>
        /// 複数フレーム分のバイト列をまとめて書き込む（音声などストリーム的なデータ用）。
        /// <paramref name="src"/> の長さは stride の整数倍であること。チャンク境界をまたいでコピーする。
        /// </summary>
        public void WriteFrames(ReadOnlySpan<byte> src)
        {
            if (_faulted || _finished || src.Length == 0)
                return;

            int srcOff = 0;
            while (srcOff < src.Length)
            {
                int space = _chunkBytes - _pos;
                int n = Math.Min(space, src.Length - srcOff);
                src.Slice(srcOff, n).CopyTo(_cur.AsSpan(_pos, n));
                _pos += n;
                srcOff += n;
                if (_pos == _chunkBytes)
                    RotateChunk();
            }

            _frameCount += src.Length / _stride;
        }

        /// <summary>記録を確定し、IO スレッドを終了させ、ヘッダの frameCount を書き戻す。</summary>
        public void Finish()
        {
            if (_finished)
                return;
            _finished = true;

            // 書きかけチャンクをフラッシュ
            if (_pos > 0)
            {
                _full.Add(new Segment { Buffer = _cur, Length = _pos });
                _cur = null;
                _pos = 0;
            }

            _full.CompleteAdding();
            _ioThread.Join();

            // frameCount をヘッダに書き戻す (IO スレッド完了後なので排他不要)
            if (!_faulted)
            {
                try
                {
                    _fs.Seek(_frameCountFieldPos, SeekOrigin.Begin);
                    BinaryPrimitives.WriteUInt32LittleEndian(_u32, (uint)_frameCount);
                    _fs.Write(_u32, 0, 4);
                    _fs.Flush(true);
                }
                catch (Exception e)
                {
                    _faulted = true;
                    FaultException = e;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_finished)
                    Finish();
            }
            finally
            {
                _fs?.Dispose();
            }
        }

        void IoLoop()
        {
            try
            {
                foreach (var seg in _full.GetConsumingEnumerable())
                {
                    _fs.Write(seg.Buffer, 0, seg.Length);
                    _pool.Enqueue(seg.Buffer); // プールへ返却
                }
                _fs.Flush(true);
            }
            catch (Exception e)
            {
                _faulted = true;
                FaultException = e;
            }
        }

        byte[] Rent()
            => _pool.TryDequeue(out var b) ? b : new byte[_chunkBytes]; // 枯渇時のみ確保 (ディスク停滞時の保険)

        void RotateChunk()
        {
            _full.Add(new Segment { Buffer = _cur, Length = _pos });
            _cur = Rent();
            _pos = 0;
        }
    }
}
