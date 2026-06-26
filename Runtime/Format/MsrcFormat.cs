using System.Text;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// .msrc (MudShip ReCording) バイナリフォーマットの定数と仕様。
    /// ランタイムが書き出す唯一の形式であり、記録の「正本」。
    ///
    /// レイアウト (リトルエンディアン):
    /// <code>
    /// [Header]
    ///   magic        char[4]  "MSRC"
    ///   version      uint16
    ///   flags        uint32   bit0: hasTimestamp
    ///   nominalFps   float32  公称フレームレート (実時刻は各frameのtimestampが正)
    ///   boneCount    uint16
    ///   posBoneCount uint16   localPosition も記録するボーン数 (通常は Hip の 1)
    ///   frameCount   uint32   停止時に確定 (記録中は 0)
    ///   posIndices   uint16[posBoneCount]   位置記録ボーンの BoneTable インデックス
    /// [BoneTable]  (boneCount 回)
    ///   pathLen      uint16
    ///   path         utf8[pathLen]   指定 root 相対パス (root 名は含めない) 例 "Hips/Spine/Chest"
    /// [Frames]  (frameCount 回, 固定ストライド)
    ///   timestamp    float64        開始からの秒
    ///   positions    (float32 x3) x posBoneCount   posIndices 順
    ///   rotations    (float32 x4) x boneCount      BoneTable 順 quaternion(x,y,z,w)
    /// </code>
    /// stride = 8 + posBoneCount*12 + boneCount*16 バイト。
    /// </summary>
    public static class MsrcFormat
    {
        /// <summary>マジックバイト "MSRC"。</summary>
        public static readonly byte[] Magic = { (byte)'M', (byte)'S', (byte)'R', (byte)'C' };

        /// <summary>現在のフォーマットバージョン。</summary>
        public const ushort Version = 1;

        /// <summary>各フレームに timestamp(double) を持つことを示すフラグ。</summary>
        public const uint FlagHasTimestamp = 1u << 0;

        /// <summary>推奨ファイル拡張子。</summary>
        public const string Extension = ".msrc";

        /// <summary>パス文字列のエンコーディング (BOM なし UTF-8)。</summary>
        public static readonly Encoding PathEncoding = new UTF8Encoding(false);

        /// <summary>1フレームのバイト数を計算する。</summary>
        public static int ComputeStride(int boneCount, int positionBoneCount)
            => 8 + positionBoneCount * 12 + boneCount * 16;
    }
}
