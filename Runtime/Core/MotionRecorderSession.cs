using System;
using System.Collections.Generic;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 1 スケルトンを 1 つの .msrm ファイルへ記録するセッション。MonoBehaviour に依存しないので
    /// 任意のクラスから直接生成・駆動できる。毎フレーム <see cref="CaptureFrame"/> を
    /// 呼ぶ側 (通常は LateUpdate) が timestamp を渡す。
    /// </summary>
    /// <example>
    /// <code>
    /// var skel = SkeletonDefinition.FromAnimator(animator);
    /// var session = new MotionRecorderSession(skel, RecorderSettings.Default);
    /// session.Start(path);
    /// // 毎 LateUpdate:
    /// session.CaptureFrame(Time.timeAsDouble - startTime);
    /// // 終了:
    /// session.Stop();
    /// session.Dispose();
    /// </code>
    /// </example>
    public sealed class MotionRecorderSession : IRecorderSession
    {
        readonly RecorderSettings _settings;
        ChunkedStreamWriter _writer;

        /// <summary>記録対象のスケルトン定義。</summary>
        public SkeletonDefinition Skeleton { get; }

        /// <summary>出力先 .msrm パス (Start 後に有効)。</summary>
        public string FilePath { get; private set; }

        /// <summary>記録中か。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>これまでに記録したフレーム数。</summary>
        public int FrameCount => _writer?.FrameCount ?? 0;

        /// <summary>IO エラー等で失敗したか。</summary>
        public bool Faulted => _writer?.Faulted ?? false;

        /// <summary>失敗時の例外 (なければ null)。</summary>
        public Exception FaultException => _writer?.FaultException;

        public MotionRecorderSession(SkeletonDefinition skeleton, RecorderSettings settings)
        {
            Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
            _settings = settings;
        }

        /// <summary>記録を開始し、ヘッダとボーンテーブルを書き出す。</summary>
        public void Start(string filePath)
        {
            if (IsRecording)
                throw new InvalidOperationException("Session is already recording.");

            var settings = _settings.Normalized();
            byte[] header = BuildHeader(Skeleton, settings.NominalFps, out long frameCountPos);
            int stride = MsrmFormat.ComputeStride(Skeleton.Bones.Length, Skeleton.PositionBoneIndices.Length);

            _writer = new ChunkedStreamWriter(filePath, header, frameCountPos, stride, settings);
            FilePath = filePath;
            IsRecording = true;
        }

        /// <summary>現在の Transform 値を 1 フレーム分書き込む。記録中でなければ何もしない。</summary>
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

            var bones = Skeleton.Bones;
            var posIdx = Skeleton.PositionBoneIndices;

            for (int i = 0; i < posIdx.Length; i++)
            {
                Vector3 p = bones[posIdx[i]].localPosition;
                BinaryLE.WriteF32(span.Slice(o), p.x); o += 4;
                BinaryLE.WriteF32(span.Slice(o), p.y); o += 4;
                BinaryLE.WriteF32(span.Slice(o), p.z); o += 4;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion q = bones[i].localRotation;
                BinaryLE.WriteF32(span.Slice(o), q.x); o += 4;
                BinaryLE.WriteF32(span.Slice(o), q.y); o += 4;
                BinaryLE.WriteF32(span.Slice(o), q.z); o += 4;
                BinaryLE.WriteF32(span.Slice(o), q.w); o += 4;
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

        static byte[] BuildHeader(SkeletonDefinition skel, float nominalFps, out long frameCountPos)
        {
            var b = new List<byte>(256);
            BinaryLE.Bytes(b, MsrmFormat.Magic);
            BinaryLE.U16(b, MsrmFormat.Version);
            BinaryLE.U32(b, MsrmFormat.FlagHasTimestamp);
            BinaryLE.F32(b, nominalFps);
            BinaryLE.U16(b, (ushort)skel.Bones.Length);
            BinaryLE.U16(b, (ushort)skel.PositionBoneIndices.Length);

            frameCountPos = b.Count;
            BinaryLE.U32(b, 0); // frameCount プレースホルダ (Finish で確定)

            var posIdx = skel.PositionBoneIndices;
            for (int i = 0; i < posIdx.Length; i++)
                BinaryLE.U16(b, (ushort)posIdx[i]);

            var paths = skel.Paths;
            for (int i = 0; i < paths.Length; i++)
                BinaryLE.StringU16(b, paths[i], MsrmFormat.PathEncoding);

            return b.ToArray();
        }
    }
}
