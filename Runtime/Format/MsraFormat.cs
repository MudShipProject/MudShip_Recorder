namespace MudShip.MotionRecorder
{
    /// <summary>
    /// .msra (MudShip Recording - Audio) バイナリフォーマットの定数と仕様。
    /// 入力デバイス (Microphone) から取り込んだ PCM サンプル列を記録する。
    ///
    /// レイアウト (リトルエンディアン):
    /// <code>
    /// [Header]
    ///   magic            char[4]  "MSRA"
    ///   version          uint16
    ///   flags            uint32   予約 (0)
    ///   sampleRate       uint32   Hz
    ///   channels         uint16
    ///   bitsPerSample    uint16   16 固定 (v1)
    ///   startOffset      float64  共通 startTime から最初のサンプルまでの秒 (同期用。v1 は 0)
    ///   sampleFrameCount uint32   サンプルフレーム数 (= 総サンプル / channels)。停止時に確定
    /// [Data]
    ///   pcm              int16[sampleFrameCount * channels]   インターリーブ・リトルエンディアン
    /// </code>
    /// 1 サンプルフレームのバイト数 (stride) = channels * 2。
    /// </summary>
    public static class MsraFormat
    {
        /// <summary>マジックバイト "MSRA"。</summary>
        public static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'R', (byte)'A' };

        /// <summary>現在のフォーマットバージョン。</summary>
        public const ushort Version = 1;

        /// <summary>サンプルのビット深度 (v1 は 16 固定)。</summary>
        public const int BitsPerSample = 16;

        /// <summary>推奨ファイル拡張子。</summary>
        public const string Extension = ".msra";

        /// <summary>1 サンプルフレームのバイト数。</summary>
        public static int ComputeStride(int channels) => channels * (BitsPerSample / 8);
    }
}
