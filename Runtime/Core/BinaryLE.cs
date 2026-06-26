using System;
using System.Collections.Generic;
using System.Text;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// リトルエンディアンのバイナリ書き込みヘルパ。
    /// フレームデータ用の Span 書き込み (非アロケーション) と、ヘッダ構築用の List 追記を提供する。
    /// Span 書き込みはリトルエンディアン環境を前提とする (呼び出し側で保証)。
    /// </summary>
    internal static class BinaryLE
    {
        // ---- フレームデータ用 (Span へ直接・非アロケーション) ----

        public static void WriteF32(Span<byte> dst, float v) => BitConverter.TryWriteBytes(dst, v);

        public static void WriteF64(Span<byte> dst, double v) => BitConverter.TryWriteBytes(dst, v);

        // ---- ヘッダ構築用 (List<byte> へ追記。ファイルごとに1回なのでアロケーション可) ----
        // U16/U32 は明示的にバイトを並べるためプラットフォーム非依存。

        public static void U16(List<byte> b, ushort v)
        {
            b.Add((byte)v);
            b.Add((byte)(v >> 8));
        }

        public static void U32(List<byte> b, uint v)
        {
            b.Add((byte)v);
            b.Add((byte)(v >> 8));
            b.Add((byte)(v >> 16));
            b.Add((byte)(v >> 24));
        }

        public static void F32(List<byte> b, float v)
        {
            Span<byte> tmp = stackalloc byte[4];
            BitConverter.TryWriteBytes(tmp, v);
            if (!BitConverter.IsLittleEndian)
                tmp.Reverse();
            b.Add(tmp[0]); b.Add(tmp[1]); b.Add(tmp[2]); b.Add(tmp[3]);
        }

        public static void Bytes(List<byte> b, byte[] s) => b.AddRange(s);

        /// <summary>uint16 の長さ接頭辞付きで文字列を追記する。</summary>
        public static void StringU16(List<byte> b, string s, Encoding enc)
        {
            byte[] bytes = enc.GetBytes(s ?? string.Empty);
            U16(b, (ushort)bytes.Length);
            b.AddRange(bytes);
        }
    }
}
