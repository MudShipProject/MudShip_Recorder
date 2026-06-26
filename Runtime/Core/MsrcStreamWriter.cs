using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// .msrc ストリームライタ。メインスレッドは事前確保した byte[] チャンクへ書き込むだけで
    /// (アロケーション・GC ゼロ)、満杯になったチャンクをバックグラウンド IO スレッドへ渡して
    /// ディスクへ書き出す。チャンクはプールで使い回すため定常状態でゴミを出さない。
    ///
    /// メインスレッド: ヘッダ書き込み(開始時1回) → <see cref="WriteFrame"/> → <see cref="Finish"/>。
    /// IO スレッド  : 満杯チャンクのディスク書き出しのみ (Unity API は一切呼ばない)。
    /// </summary>
    internal sealed class MsrcStreamWriter : IDisposable
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

        readonly byte[] _hdr = new byte[8]; // ヘッダ用の使い回しバッファ

        byte[] _cur;
        int _pos;
        int _frameCount;
        bool _finished;

        volatile bool _faulted;
        public bool Faulted => _faulted;
        public Exception FaultException { get; private set; }
        public int FrameCount => _frameCount;
        public int Stride => _stride;

        public MsrcStreamWriter(string filePath, SkeletonDefinition skeleton, RecorderSettings settings)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("filePath is null or empty.", nameof(filePath));

            if (!BitConverter.IsLittleEndian)
                throw new NotSupportedException("MSRC writer assumes a little-endian platform.");

            settings = settings.Normalized();

            int boneCount = skeleton.Bones.Length;
            int posCount = skeleton.PositionBoneIndices.Length;
            _stride = MsrcFormat.ComputeStride(boneCount, posCount);

            int framesPerChunk = Math.Max(1, settings.ChunkBytes / _stride);
            _chunkBytes = framesPerChunk * _stride;

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            WriteHeader(skeleton, settings.NominalFps, out _frameCountFieldPos);

            for (int i = 0; i < settings.PooledChunkCount; i++)
                _pool.Enqueue(new byte[_chunkBytes]);

            _cur = Rent();
            _pos = 0;

            _ioThread = new Thread(IoLoop) { IsBackground = true, Name = "MSRC-IO" };
            _ioThread.Start();
        }

        /// <summary>1フレーム分の値を書き込む (メインスレッド)。アロケーションは発生しない。</summary>
        public void WriteFrame(double timestamp, SkeletonDefinition skeleton)
        {
            if (_faulted || _finished)
                return;

            Span<byte> span = _cur.AsSpan(_pos, _stride);
            int o = 0;

            WriteF64(span.Slice(o), timestamp);
            o += 8;

            var bones = skeleton.Bones;
            var posIdx = skeleton.PositionBoneIndices;

            for (int i = 0; i < posIdx.Length; i++)
            {
                Vector3 p = bones[posIdx[i]].localPosition;
                WriteF32(span.Slice(o), p.x); o += 4;
                WriteF32(span.Slice(o), p.y); o += 4;
                WriteF32(span.Slice(o), p.z); o += 4;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion q = bones[i].localRotation;
                WriteF32(span.Slice(o), q.x); o += 4;
                WriteF32(span.Slice(o), q.y); o += 4;
                WriteF32(span.Slice(o), q.z); o += 4;
                WriteF32(span.Slice(o), q.w); o += 4;
            }

            _pos += _stride;
            _frameCount++;

            if (_pos == _chunkBytes)
                RotateChunk();
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
                    BinaryPrimitives.WriteUInt32LittleEndian(_hdr, (uint)_frameCount);
                    _fs.Write(_hdr, 0, 4);
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

        void WriteHeader(SkeletonDefinition skeleton, float nominalFps, out long frameCountFieldPos)
        {
            _fs.Write(MsrcFormat.Magic, 0, 4);
            PutU16(MsrcFormat.Version);
            PutU32(MsrcFormat.FlagHasTimestamp);
            PutF32(nominalFps);
            PutU16((ushort)skeleton.Bones.Length);
            PutU16((ushort)skeleton.PositionBoneIndices.Length);

            frameCountFieldPos = _fs.Position;
            PutU32(0); // frameCount プレースホルダ (Finish で確定)

            var posIdx = skeleton.PositionBoneIndices;
            for (int i = 0; i < posIdx.Length; i++)
                PutU16((ushort)posIdx[i]);

            var paths = skeleton.Paths;
            for (int i = 0; i < paths.Length; i++)
            {
                byte[] bytes = MsrcFormat.PathEncoding.GetBytes(paths[i]);
                PutU16((ushort)bytes.Length);
                _fs.Write(bytes, 0, bytes.Length);
            }
        }

        // .NET Standard 2.1 の BinaryPrimitives には float/double のLE版が無いため、
        // BitConverter.TryWriteBytes (非アロケーション・マシンエンディアン) を使う。
        // リトルエンディアン環境はコンストラクタで保証済み。
        static void WriteF32(Span<byte> dst, float v)
        {
            BitConverter.TryWriteBytes(dst, v);
        }

        static void WriteF64(Span<byte> dst, double v)
        {
            BitConverter.TryWriteBytes(dst, v);
        }

        void PutU16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_hdr, v);
            _fs.Write(_hdr, 0, 2);
        }

        void PutU32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_hdr, v);
            _fs.Write(_hdr, 0, 4);
        }

        void PutF32(float v)
        {
            WriteF32(_hdr, v);
            _fs.Write(_hdr, 0, 4);
        }
    }
}
