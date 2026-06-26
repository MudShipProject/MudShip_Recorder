using System;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 1 スケルトンを 1 つの .msrc ファイルへ記録するセッション。MonoBehaviour に依存しないので
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
    public sealed class MotionRecorderSession : IDisposable
    {
        readonly RecorderSettings _settings;
        MsrcStreamWriter _writer;

        /// <summary>記録対象のスケルトン定義。</summary>
        public SkeletonDefinition Skeleton { get; }

        /// <summary>出力先 .msrc パス (Start 後に有効)。</summary>
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

            _writer = new MsrcStreamWriter(filePath, Skeleton, _settings);
            FilePath = filePath;
            IsRecording = true;
        }

        /// <summary>現在の Transform 値を 1 フレーム分書き込む。記録中でなければ何もしない。</summary>
        /// <param name="timestamp">記録開始からの経過秒。</param>
        public void CaptureFrame(double timestamp)
        {
            if (!IsRecording)
                return;

            _writer.WriteFrame(timestamp, Skeleton);

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
    }
}
