using System;
using System.Collections.Generic;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 1 つの表情定義 (SkinnedMeshRenderer 群) を 1 つの .msrf ファイルへ記録するセッション。
    /// MonoBehaviour に依存しない。毎フレーム <see cref="CaptureFrame"/> を呼ぶ側 (通常は LateUpdate) が
    /// timestamp を渡す。<see cref="MotionRecorderSession"/> と対になる表情版。
    /// </summary>
    public sealed class FaceRecorderSession : IDisposable
    {
        readonly RecorderSettings _settings;
        ChunkedStreamWriter _writer;

        /// <summary>記録対象の表情定義。</summary>
        public FaceDefinition Face { get; }

        /// <summary>出力先 .msrf パス (Start 後に有効)。</summary>
        public string FilePath { get; private set; }

        /// <summary>記録中か。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>これまでに記録したフレーム数。</summary>
        public int FrameCount => _writer?.FrameCount ?? 0;

        /// <summary>IO エラー等で失敗したか。</summary>
        public bool Faulted => _writer?.Faulted ?? false;

        /// <summary>失敗時の例外 (なければ null)。</summary>
        public Exception FaultException => _writer?.FaultException;

        public FaceRecorderSession(FaceDefinition face, RecorderSettings settings)
        {
            Face = face ?? throw new ArgumentNullException(nameof(face));
            _settings = settings;
        }

        /// <summary>記録を開始し、ヘッダと renderer テーブルを書き出す。</summary>
        public void Start(string filePath)
        {
            if (IsRecording)
                throw new InvalidOperationException("Session is already recording.");

            var settings = _settings.Normalized();
            byte[] header = BuildHeader(Face, settings.NominalFps, out long frameCountPos);
            int stride = MsrfFormat.ComputeStride(Face.TotalShapeCount);

            _writer = new ChunkedStreamWriter(filePath, header, frameCountPos, stride, settings);
            FilePath = filePath;
            IsRecording = true;
        }

        /// <summary>現在の BlendShape ウェイトを 1 フレーム分書き込む。記録中でなければ何もしない。</summary>
        /// <param name="timestamp">記録開始からの経過秒。</param>
        public void CaptureFrame(double timestamp)
        {
            if (!IsRecording)
                return;

            Span<byte> span = _writer.BeginFrame();
            if (span.IsEmpty)
            {
                if (_writer.Faulted)
                    Stop();
                return;
            }

            int o = 0;
            BinaryLE.WriteF64(span.Slice(o), timestamp); o += 8;

            var renderers = Face.Renderers;
            var counts = Face.ShapeCounts;
            for (int r = 0; r < renderers.Length; r++)
            {
                var smr = renderers[r];
                int n = counts[r];
                for (int i = 0; i < n; i++)
                {
                    BinaryLE.WriteF32(span.Slice(o), smr.GetBlendShapeWeight(i)); o += 4;
                }
            }

            _writer.CommitFrame();

            if (_writer.Faulted)
                Stop();
        }

        /// <summary>記録を確定して停止する。frameCount がヘッダに書き戻される。</summary>
        public void Stop()
        {
            if (!IsRecording)
                return;

            IsRecording = false;
            _writer.Finish();
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            finally
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        static byte[] BuildHeader(FaceDefinition face, float nominalFps, out long frameCountPos)
        {
            var b = new List<byte>(256);
            BinaryLE.Bytes(b, MsrfFormat.Magic);
            BinaryLE.U16(b, MsrfFormat.Version);
            BinaryLE.U32(b, MsrfFormat.FlagHasTimestamp);
            BinaryLE.F32(b, nominalFps);
            BinaryLE.U16(b, (ushort)face.Renderers.Length);
            BinaryLE.U32(b, (uint)face.TotalShapeCount);

            frameCountPos = b.Count;
            BinaryLE.U32(b, 0); // frameCount プレースホルダ (Finish で確定)

            for (int r = 0; r < face.Renderers.Length; r++)
            {
                BinaryLE.StringU16(b, face.RendererPaths[r], MsrfFormat.PathEncoding);
                BinaryLE.U16(b, (ushort)face.ShapeCounts[r]);
                var names = face.ShapeNames[r];
                for (int i = 0; i < names.Length; i++)
                    BinaryLE.StringU16(b, names[i], MsrfFormat.PathEncoding);
            }

            return b.ToArray();
        }
    }
}
