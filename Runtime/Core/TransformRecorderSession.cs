using System;
using System.Collections.Generic;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 単一の Transform の localPosition / localRotation / localScale を 1 つの .msrt へ記録する
    /// セッション。MonoBehaviour に依存しない。<see cref="MotionRecorderSession"/> と同じ作り。
    /// </summary>
    public sealed class TransformRecorderSession : IRecorderSession
    {
        readonly RecorderSettings _settings;
        readonly Transform _target;
        ChunkedStreamWriter _writer;

        /// <summary>記録対象の Transform。</summary>
        public Transform Target => _target;

        public string FilePath { get; private set; }
        public bool IsRecording { get; private set; }
        public int FrameCount => _writer?.FrameCount ?? 0;
        public bool Faulted => _writer?.Faulted ?? false;
        public Exception FaultException => _writer?.FaultException;

        public TransformRecorderSession(Transform target, RecorderSettings settings)
        {
            _target = target != null ? target : throw new ArgumentNullException(nameof(target));
            _settings = settings;
        }

        public void Start(string filePath)
        {
            if (IsRecording)
                throw new InvalidOperationException("Session is already recording.");

            var settings = _settings.Normalized();
            byte[] header = BuildHeader(settings.NominalFps, out long frameCountPos);
            _writer = new ChunkedStreamWriter(filePath, header, frameCountPos, MsrtFormat.ComputeStride(), settings);
            FilePath = filePath;
            IsRecording = true;
        }

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

            Vector3 p = _target.localPosition;
            BinaryLE.WriteF32(span.Slice(o), p.x); o += 4;
            BinaryLE.WriteF32(span.Slice(o), p.y); o += 4;
            BinaryLE.WriteF32(span.Slice(o), p.z); o += 4;

            Quaternion q = _target.localRotation;
            BinaryLE.WriteF32(span.Slice(o), q.x); o += 4;
            BinaryLE.WriteF32(span.Slice(o), q.y); o += 4;
            BinaryLE.WriteF32(span.Slice(o), q.z); o += 4;
            BinaryLE.WriteF32(span.Slice(o), q.w); o += 4;

            Vector3 s = _target.localScale;
            BinaryLE.WriteF32(span.Slice(o), s.x); o += 4;
            BinaryLE.WriteF32(span.Slice(o), s.y); o += 4;
            BinaryLE.WriteF32(span.Slice(o), s.z); o += 4;

            _writer.CommitFrame();

            if (_writer.Faulted)
                Stop();
        }

        public void Stop()
        {
            if (!IsRecording)
                return;
            IsRecording = false;
            _writer.Finish();
        }

        public void Dispose()
        {
            try { Stop(); }
            finally { _writer?.Dispose(); _writer = null; }
        }

        static byte[] BuildHeader(float nominalFps, out long frameCountPos)
        {
            var b = new List<byte>(32);
            BinaryLE.Bytes(b, MsrtFormat.Magic);
            BinaryLE.U16(b, MsrtFormat.Version);
            BinaryLE.U32(b, MsrtFormat.FlagHasTimestamp);
            BinaryLE.F32(b, nominalFps);
            frameCountPos = b.Count;
            BinaryLE.U32(b, 0); // frameCount プレースホルダ
            return b.ToArray();
        }
    }
}
