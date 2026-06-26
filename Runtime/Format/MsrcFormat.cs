namespace MudShip.MotionRecorder
{
    /// <summary>
    /// .msrc (MudShip Recording - Camera) バイナリフォーマットの定数と仕様。
    /// 単一の Camera の localPosition / localRotation / localScale ＋ fieldOfView を記録する
    /// (Transform ストリーム .msrt に Fov を足したもの)。
    ///
    /// レイアウト (リトルエンディアン):
    /// <code>
    /// [Header]
    ///   magic        char[4]  "MSRC"
    ///   version      uint16
    ///   flags        uint32   bit0: hasTimestamp
    ///   nominalFps   float32
    ///   frameCount   uint32   停止時に確定 (記録中は 0)
    /// [Frames]  (frameCount 回, 固定ストライド)
    ///   timestamp    float64
    ///   position     float32 x3
    ///   rotation     float32 x4   quaternion(x,y,z,w)
    ///   scale        float32 x3
    ///   fov          float32      Camera.fieldOfView (度)
    /// </code>
    /// stride = 8 + 12 + 16 + 12 + 4 = 52 バイト。
    /// </summary>
    public static class MsrcFormat
    {
        /// <summary>マジックバイト "MSRC"。</summary>
        public static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'R', (byte)'C' };

        /// <summary>現在のフォーマットバージョン。</summary>
        public const ushort Version = 1;

        /// <summary>各フレームに timestamp(double) を持つことを示すフラグ。</summary>
        public const uint FlagHasTimestamp = 1u << 0;

        /// <summary>値がワールド空間 (position/rotation/lossyScale) であることを示すフラグ。立っていなければローカル。</summary>
        public const uint FlagWorldSpace = 1u << 1;

        /// <summary>推奨ファイル拡張子。</summary>
        public const string Extension = ".msrc";

        /// <summary>1フレームのバイト数 (timestamp + pos + rot + scale + fov)。</summary>
        public static int ComputeStride() => 8 + 12 + 16 + 12 + 4;
    }
}
