using System;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 1 ストリーム → 1 ファイルを記録するセッションの共通インターフェイス。
    /// <see cref="MS_Recorder"/> は種別を問わずこの形で各セッションを駆動する。
    /// 毎フレーム <see cref="CaptureFrame"/> を呼ぶ側 (通常は LateUpdate) が timestamp を渡す。
    /// </summary>
    public interface IRecorderSession : IDisposable
    {
        /// <summary>出力先パス (Start 後に有効)。</summary>
        string FilePath { get; }

        /// <summary>記録中か。</summary>
        bool IsRecording { get; }

        /// <summary>これまでに記録したフレーム数。</summary>
        int FrameCount { get; }

        /// <summary>IO エラー等で失敗したか。</summary>
        bool Faulted { get; }

        /// <summary>失敗時の例外 (なければ null)。</summary>
        Exception FaultException { get; }

        /// <summary>現在の値を 1 フレーム分書き込む。記録中でなければ何もしない。</summary>
        void CaptureFrame(double timestamp);

        /// <summary>記録を確定して停止する。</summary>
        void Stop();
    }
}
