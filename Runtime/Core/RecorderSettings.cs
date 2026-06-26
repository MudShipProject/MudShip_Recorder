using System;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// レコーダーの動作設定。Inspector からも編集できるよう Serializable。
    /// </summary>
    [Serializable]
    public struct RecorderSettings
    {
        [Tooltip("ヘッダに書く公称フレームレート。実時刻は各フレームの timestamp が正となる。")]
        public float NominalFps;

        [Tooltip("ディスクへ書き出すチャンクの目安バイト数。実際はストライドの整数倍に丸められる。")]
        public int ChunkBytes;

        [Tooltip("使い回すチャンク数。IO スレッドが詰まったときの余裕。多いほど耐性が上がるがメモリを使う。")]
        public int PooledChunkCount;

        /// <summary>既定値 (60fps / 256KB チャンク / 4 枚プール)。</summary>
        public static RecorderSettings Default => new RecorderSettings
        {
            NominalFps = 60f,
            ChunkBytes = 256 * 1024,
            PooledChunkCount = 4,
        };

        /// <summary>不正値を既定にクランプした正規化済み設定を返す。</summary>
        public RecorderSettings Normalized()
        {
            var s = this;
            if (s.NominalFps <= 0f) s.NominalFps = 60f;
            if (s.ChunkBytes < 4096) s.ChunkBytes = 4096;
            if (s.PooledChunkCount < 2) s.PooledChunkCount = 2;
            return s;
        }
    }
}
