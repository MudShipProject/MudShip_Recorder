using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// MudShip Recording（.msrm / .msrf）を読み込み、Editor 上で <see cref="AnimationClip"/> (.anim) に
    /// 変換する。段1 (ロスレス): 全フレームにキーを打ち、tangent は Linear。各フレーム時刻の値は
    /// 元データと完全一致する。
    ///
    /// - .msrm（モーション）: Transform パスに紐づく localRotation / localPosition カーブ。
    /// - .msrf（表情）: SkinnedMeshRenderer パスに紐づく blendShape.&lt;名前&gt; カーブ。
    ///
    /// 生成されるのは記録時と同じ root 相対パス構造のリグでのみ正しく再生される generic クリップ
    /// （リターゲット不可）。注意: 全フレームキーのため尺が長いとキー数が多く重い（軽量化は段2）。
    /// </summary>
    public static class MsrAnimConverter
    {
        // ---- メニュー: Assets 内で選択した .msrm/.msrf を右クリック変換 ----------------

        [MenuItem("Assets/MudShip/Convert recording to .anim", true)]
        static bool ConvertSelectedValidate()
        {
            var obj = Selection.activeObject;
            if (obj == null)
                return false;
            string path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path) && IsSupported(path);
        }

        [MenuItem("Assets/MudShip/Convert recording to .anim", false, 2000)]
        static void ConvertSelected()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            string projectRoot = Path.GetDirectoryName(Application.dataPath); // .../<Project>
            string fullPath = Path.Combine(projectRoot, assetPath);
            Convert(fullPath);
        }

        static bool IsSupported(string path)
            => path.EndsWith(MsrmFormat.Extension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(MsrfFormat.Extension, StringComparison.OrdinalIgnoreCase);

        // ---- 変換本体 -----------------------------------------------------------

        public static void Convert(string srcPath)
        {
            try
            {
                AnimationClip clip;
                int frameCount;

                string ext = Path.GetExtension(srcPath);
                if (ext.Equals(MsrmFormat.Extension, StringComparison.OrdinalIgnoreCase))
                    clip = BuildMotion(srcPath, out frameCount);
                else if (ext.Equals(MsrfFormat.Extension, StringComparison.OrdinalIgnoreCase))
                    clip = BuildFace(srcPath, out frameCount);
                else
                    throw new InvalidDataException($"未対応の拡張子です: {ext}");

                string defaultName = Path.GetFileNameWithoutExtension(srcPath);
                string savePath = EditorUtility.SaveFilePanelInProject(
                    ".anim の保存先を選択 (Assets 内)", defaultName, "anim",
                    "生成した AnimationClip の保存先を選んでください");

                if (string.IsNullOrEmpty(savePath))
                    return; // キャンセル

                AssetDatabase.CreateAsset(clip, savePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(savePath);

                var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(savePath);
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;

                Debug.Log($"[MudShip Recorder] 変換完了: {savePath} ({frameCount} frames, {clip.length:F2}s)");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("変換失敗", $"変換に失敗しました:\n{e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ---- .msrm (モーション) -------------------------------------------------

        static AnimationClip BuildMotion(string msrmPath, out int frameCount)
        {
            using var fs = new FileStream(msrmPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // --- Header ---
            byte[] magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'M' || magic[1] != 'S' || magic[2] != 'R' || magic[3] != 'M')
                throw new InvalidDataException("MSRM マジックが一致しません。.msrm ファイルではない可能性があります。");

            ushort version = br.ReadUInt16();
            uint flags = br.ReadUInt32();
            float nominalFps = br.ReadSingle();
            int boneCount = br.ReadUInt16();
            int posBoneCount = br.ReadUInt16();
            uint headerFrameCount = br.ReadUInt32();

            bool hasTimestamp = (flags & MsrmFormat.FlagHasTimestamp) != 0;

            var posIndices = new int[posBoneCount];
            for (int i = 0; i < posBoneCount; i++)
                posIndices[i] = br.ReadUInt16();

            var paths = new string[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                int len = br.ReadUInt16();
                byte[] bytes = br.ReadBytes(len);
                paths[i] = MsrmFormat.PathEncoding.GetString(bytes);
            }

            // --- フレーム数の確定 ---
            int stride = (hasTimestamp ? 8 : 0) + posBoneCount * 12 + boneCount * 16;
            frameCount = ResolveFrameCount(fs, headerFrameCount, stride);

            // --- 値の読み出し (カーブごとの配列に展開) ---
            var times = new float[frameCount];
            var posValues = new float[posBoneCount * 3][];
            for (int c = 0; c < posValues.Length; c++) posValues[c] = new float[frameCount];
            var rotValues = new float[boneCount * 4][];
            for (int c = 0; c < rotValues.Length; c++) rotValues[c] = new float[frameCount];

            for (int f = 0; f < frameCount; f++)
            {
                if ((f & 0x3FF) == 0)
                    EditorUtility.DisplayProgressBar("MudShip Recorder", $"読み込み中… {f}/{frameCount}", (float)f / frameCount);

                times[f] = hasTimestamp ? (float)br.ReadDouble() : f / Mathf.Max(1f, nominalFps);

                for (int pi = 0; pi < posBoneCount; pi++)
                {
                    posValues[pi * 3 + 0][f] = br.ReadSingle();
                    posValues[pi * 3 + 1][f] = br.ReadSingle();
                    posValues[pi * 3 + 2][f] = br.ReadSingle();
                }
                for (int bi = 0; bi < boneCount; bi++)
                {
                    rotValues[bi * 4 + 0][f] = br.ReadSingle();
                    rotValues[bi * 4 + 1][f] = br.ReadSingle();
                    rotValues[bi * 4 + 2][f] = br.ReadSingle();
                    rotValues[bi * 4 + 3][f] = br.ReadSingle();
                }
            }

            // --- AnimationClip 構築 ---
            var clip = new AnimationClip { frameRate = nominalFps > 0f ? nominalFps : 60f };

            string[] rotProps = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
            string[] posProps = { "localPosition.x", "localPosition.y", "localPosition.z" };

            EditorUtility.DisplayProgressBar("MudShip Recorder", "カーブ生成中…", 0.9f);

            for (int pi = 0; pi < posBoneCount; pi++)
            {
                string path = paths[posIndices[pi]];
                for (int a = 0; a < 3; a++)
                    clip.SetCurve(path, typeof(Transform), posProps[a], BuildLinearCurve(times, posValues[pi * 3 + a]));
            }
            for (int bi = 0; bi < boneCount; bi++)
            {
                string path = paths[bi];
                for (int a = 0; a < 4; a++)
                    clip.SetCurve(path, typeof(Transform), rotProps[a], BuildLinearCurve(times, rotValues[bi * 4 + a]));
            }

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        // ---- .msrf (表情) -------------------------------------------------------

        static AnimationClip BuildFace(string msrfPath, out int frameCount)
        {
            using var fs = new FileStream(msrfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // --- Header ---
            byte[] magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'M' || magic[1] != 'S' || magic[2] != 'R' || magic[3] != 'F')
                throw new InvalidDataException("MSRF マジックが一致しません。.msrf ファイルではない可能性があります。");

            ushort version = br.ReadUInt16();
            uint flags = br.ReadUInt32();
            float nominalFps = br.ReadSingle();
            int rendererCount = br.ReadUInt16();
            int totalShapeCount = (int)br.ReadUInt32();
            uint headerFrameCount = br.ReadUInt32();

            bool hasTimestamp = (flags & MsrfFormat.FlagHasTimestamp) != 0;

            var rendererPaths = new string[rendererCount];
            var shapeNames = new string[rendererCount][];
            for (int r = 0; r < rendererCount; r++)
            {
                int len = br.ReadUInt16();
                rendererPaths[r] = MsrfFormat.PathEncoding.GetString(br.ReadBytes(len));
                int sc = br.ReadUInt16();
                var names = new string[sc];
                for (int i = 0; i < sc; i++)
                {
                    int nl = br.ReadUInt16();
                    names[i] = MsrfFormat.PathEncoding.GetString(br.ReadBytes(nl));
                }
                shapeNames[r] = names;
            }

            // --- フレーム数の確定 ---
            int stride = (hasTimestamp ? 8 : 0) + totalShapeCount * 4;
            frameCount = ResolveFrameCount(fs, headerFrameCount, stride);

            // --- 値の読み出し ---
            var times = new float[frameCount];
            var weights = new float[totalShapeCount][];
            for (int c = 0; c < totalShapeCount; c++) weights[c] = new float[frameCount];

            for (int f = 0; f < frameCount; f++)
            {
                if ((f & 0x3FF) == 0)
                    EditorUtility.DisplayProgressBar("MudShip Recorder", $"読み込み中… {f}/{frameCount}", (float)f / frameCount);

                times[f] = hasTimestamp ? (float)br.ReadDouble() : f / Mathf.Max(1f, nominalFps);
                for (int c = 0; c < totalShapeCount; c++)
                    weights[c][f] = br.ReadSingle();
            }

            // --- AnimationClip 構築 ---
            var clip = new AnimationClip { frameRate = nominalFps > 0f ? nominalFps : 60f };

            EditorUtility.DisplayProgressBar("MudShip Recorder", "カーブ生成中…", 0.9f);

            int g = 0; // 全体での shape 通し番号
            for (int r = 0; r < rendererCount; r++)
            {
                string path = rendererPaths[r];
                var names = shapeNames[r];
                for (int i = 0; i < names.Length; i++, g++)
                    clip.SetCurve(path, typeof(SkinnedMeshRenderer), $"blendShape.{names[i]}", BuildLinearCurve(times, weights[g]));
            }

            return clip;
        }

        // ---- 共通 ---------------------------------------------------------------

        /// <summary>ヘッダの frameCount（0 ならファイル長から復元）をファイル長以内にクランプして返す。</summary>
        static int ResolveFrameCount(FileStream fs, uint headerFrameCount, int stride)
        {
            long dataStart = fs.Position;
            long dataBytes = fs.Length - dataStart;
            int countFromSize = stride > 0 ? (int)(dataBytes / stride) : 0;
            int frameCount = headerFrameCount > 0 ? (int)headerFrameCount : countFromSize;
            frameCount = Mathf.Min(frameCount, countFromSize); // ファイル長を超えない
            if (frameCount <= 0)
                throw new InvalidDataException("有効なフレームがありません。");
            return frameCount;
        }

        /// <summary>各キーが値を完全に通る区分線形カーブ (Linear tangent) を生成する。</summary>
        static AnimationCurve BuildLinearCurve(float[] times, float[] values)
        {
            int n = times.Length;
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float inT = 0f, outT = 0f;
                if (i > 0)
                {
                    float dt = times[i] - times[i - 1];
                    if (dt > 1e-9f) inT = (values[i] - values[i - 1]) / dt;
                }
                if (i < n - 1)
                {
                    float dt = times[i + 1] - times[i];
                    if (dt > 1e-9f) outT = (values[i + 1] - values[i]) / dt;
                }
                if (i == 0) inT = outT;
                if (i == n - 1) outT = inT;

                keys[i] = new Keyframe(times[i], values[i], inT, outT);
            }
            return new AnimationCurve(keys);
        }
    }
}
