namespace MudShip.MotionRecorder
{
    /// <summary>
    /// .msrt (MudShip Recording - Transform) バイナリフォーマットの定数と仕様。
    /// 単一の Transform の localPosition / localRotation / localScale を記録する。
    ///
    /// レイアウト (リトルエンディアン):
    /// <code>
    /// [Header]
    ///   magic        char[4]  "MSRT"
    ///   version      uint16
    ///   flags        uint32   bit0: hasTimestamp
    ///   nominalFps   float32
    ///   frameCount   uint32   停止時に確定 (記録中は 0)
    /// [Frames]  (frameCount 回, 固定ストライド)
    ///   timestamp    float64
    ///   position     float32 x3
    ///   rotation     float32 x4   quaternion(x,y,z,w)
    ///   scale        float32 x3
    /// </code>
    /// stride = 8 + 12 + 16 + 12 = 48 バイト。対象は 1 つなのでパステーブルは持たない
    /// (.anim 変換では path 空＝対象 GameObject 自身に適用)。
    /// </summary>
    public static class MsrtFormat
    {
        /// <summary>マジックバイト "MSRT"。</summary>
        public static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'R', (byte)'T' };

        /// <summary>現在のフォーマットバージョン。</summary>
        public const ushort Version = 1;

        /// <summary>各フレームに timestamp(double) を持つことを示すフラグ。</summary>
        public const uint FlagHasTimestamp = 1u << 0;

        /// <summary>推奨ファイル拡張子。</summary>
        public const string Extension = ".msrt";

        /// <summary>1フレームのバイト数 (timestamp + pos + rot + scale)。</summary>
        public static int ComputeStride() => 8 + 12 + 16 + 12;
    }
}
