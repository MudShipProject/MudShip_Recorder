using System.Text;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// .msrf (MudShip Recording - Facial) バイナリフォーマットの定数と仕様。
    /// SkinnedMeshRenderer 群の BlendShape ウェイトを記録する。
    ///
    /// レイアウト (リトルエンディアン):
    /// <code>
    /// [Header]
    ///   magic           char[4]  "MSRF"
    ///   version         uint16
    ///   flags           uint32   bit0: hasTimestamp
    ///   nominalFps      float32
    ///   rendererCount   uint16
    ///   totalShapeCount uint32   全 renderer の BlendShape 総数 (= 1フレームの weight 数)
    ///   frameCount      uint32   停止時に確定 (記録中は 0)
    /// [RendererTable]  (rendererCount 回)
    ///   pathLen         uint16
    ///   path            utf8[pathLen]   キャラ root 相対パス (root 名は含めない)
    ///   shapeCount      uint16
    ///   shapes          (nameLen uint16 + name utf8) x shapeCount
    /// [Frames]  (frameCount 回, 固定ストライド)
    ///   timestamp       float64        開始からの秒
    ///   weights         float32 x totalShapeCount   renderer 順 -> renderer 内 shape 順
    /// </code>
    /// stride = 8 + totalShapeCount*4 バイト。
    /// </summary>
    public static class MsrfFormat
    {
        /// <summary>マジックバイト "MSRF"。</summary>
        public static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'R', (byte)'F' };

        /// <summary>現在のフォーマットバージョン。</summary>
        public const ushort Version = 1;

        /// <summary>各フレームに timestamp(double) を持つことを示すフラグ。</summary>
        public const uint FlagHasTimestamp = 1u << 0;

        /// <summary>推奨ファイル拡張子。</summary>
        public const string Extension = ".msrf";

        /// <summary>パス文字列のエンコーディング (BOM なし UTF-8)。</summary>
        public static readonly Encoding PathEncoding = new UTF8Encoding(false);

        /// <summary>1フレームのバイト数を計算する。</summary>
        public static int ComputeStride(int totalShapeCount)
            => 8 + totalShapeCount * 4;
    }
}
