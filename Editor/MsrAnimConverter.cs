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
        // ---- メニュー: Assets 内で選択した .msrm/.msrf を右クリック変換 (複数選択可) ----

        [MenuItem("Assets/MudShip/Convert recording", true)]
        static bool ConvertSelectedValidate()
        {
            foreach (string guid in Selection.assetGUIDs)
                if (IsSupported(AssetDatabase.GUIDToAssetPath(guid)))
                    return true;
            return false;
        }

        [MenuItem("Assets/MudShip/Convert recording", false, 2000)]
        static void ConvertSelected()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath); // .../<Project>

            var targets = new System.Collections.Generic.List<string>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (IsSupported(p))
                    targets.Add(p);
            }
            if (targets.Count == 0)
                return;

            int success = 0;
            string lastOut = null;

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    string assetPath = targets[i];
                    EditorUtility.DisplayProgressBar("MudShip Recorder",
                        $"変換中… {Path.GetFileName(assetPath)} ({i + 1}/{targets.Count})",
                        (float)i / targets.Count);
                    try
                    {
                        lastOut = ConvertAsset(assetPath, projectRoot);
                        success++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!string.IsNullOrEmpty(lastOut))
            {
                var saved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lastOut);
                if (saved != null)
                {
                    EditorGUIUtility.PingObject(saved);
                    Selection.activeObject = saved;
                }
            }

            Debug.Log($"[MudShip Recorder] {success}/{targets.Count} 件を変換しました。");
        }

        static bool IsSupported(string path)
            => !string.IsNullOrEmpty(path)
            && (path.EndsWith(MsrmFormat.Extension, StringComparison.OrdinalIgnoreCase)
             || path.EndsWith(MsrfFormat.Extension, StringComparison.OrdinalIgnoreCase)
             || path.EndsWith(MsrtFormat.Extension, StringComparison.OrdinalIgnoreCase)
             || path.EndsWith(MsrcFormat.Extension, StringComparison.OrdinalIgnoreCase)
             || path.EndsWith(MsraFormat.Extension, StringComparison.OrdinalIgnoreCase));

        // ---- 変換本体 (元ファイルと同じフォルダへ出力。保存先ポップアップは出さない) ----

        /// <summary>1 つの録画ファイルを変換し、同じフォルダに成果物 (.anim / .wav) を作って保存先パスを返す。</summary>
        static string ConvertAsset(string assetPath, string projectRoot)
        {
            string fullPath = Path.Combine(projectRoot, assetPath);
            string ext = Path.GetExtension(assetPath);
            var oic = StringComparison.OrdinalIgnoreCase;

            // 音声は AnimationClip ではなく .wav を直接書き出す。
            if (ext.Equals(MsraFormat.Extension, oic))
            {
                byte[] wav = BuildWav(fullPath);
                string wavPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.Combine(Path.GetDirectoryName(assetPath),
                        Path.GetFileNameWithoutExtension(assetPath) + ".wav").Replace('\\', '/'));
                File.WriteAllBytes(Path.Combine(projectRoot, wavPath), wav);
                AssetDatabase.ImportAsset(wavPath);
                return wavPath;
            }

            AnimationClip clip;
            string suffix = "";
            if (ext.Equals(MsrmFormat.Extension, oic))
                clip = BuildMotion(fullPath, out _);
            else if (ext.Equals(MsrfFormat.Extension, oic))
            {
                clip = BuildFace(fullPath, out _);
                suffix = "_face"; // モーションと名前が衝突しないように
            }
            else if (ext.Equals(MsrtFormat.Extension, oic))
                clip = BuildTransform(fullPath, out _);
            else if (ext.Equals(MsrcFormat.Extension, oic))
                clip = BuildCamera(fullPath, out _);
            else
                throw new InvalidDataException($"未対応の拡張子です: {ext}");

            // 元ファイルと同じ Assets フォルダへ出力。
            string dir = Path.GetDirectoryName(assetPath);
            string baseName = Path.GetFileNameWithoutExtension(assetPath) + suffix;
            string outPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(dir, baseName + ".anim").Replace('\\', '/'));

            AssetDatabase.CreateAsset(clip, outPath);
            return outPath;
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

        // ---- .msrt (Transform) --------------------------------------------------

        static AnimationClip BuildTransform(string path, out int frameCount)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            byte[] magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'M' || magic[1] != 'S' || magic[2] != 'R' || magic[3] != 'T')
                throw new InvalidDataException("MSRT マジックが一致しません。.msrt ファイルではない可能性があります。");

            ushort version = br.ReadUInt16();
            uint flags = br.ReadUInt32();
            float nominalFps = br.ReadSingle();
            uint headerFrameCount = br.ReadUInt32();

            bool hasTimestamp = (flags & MsrtFormat.FlagHasTimestamp) != 0;
            int stride = (hasTimestamp ? 8 : 0) + 12 + 16 + 12;
            frameCount = ResolveFrameCount(fs, headerFrameCount, stride);

            var times = new float[frameCount];
            var pos = NewChannels(3, frameCount);
            var rot = NewChannels(4, frameCount);
            var scl = NewChannels(3, frameCount);

            for (int f = 0; f < frameCount; f++)
            {
                if ((f & 0x3FF) == 0)
                    EditorUtility.DisplayProgressBar("MudShip Recorder", $"読み込み中… {f}/{frameCount}", (float)f / frameCount);

                times[f] = hasTimestamp ? (float)br.ReadDouble() : f / Mathf.Max(1f, nominalFps);
                for (int a = 0; a < 3; a++) pos[a][f] = br.ReadSingle();
                for (int a = 0; a < 4; a++) rot[a][f] = br.ReadSingle();
                for (int a = 0; a < 3; a++) scl[a][f] = br.ReadSingle();
            }

            var clip = new AnimationClip { frameRate = nominalFps > 0f ? nominalFps : 60f };
            EditorUtility.DisplayProgressBar("MudShip Recorder", "カーブ生成中…", 0.9f);
            SetTrsCurves(clip, times, pos, rot, scl);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        // ---- .msrc (Camera) -----------------------------------------------------

        static AnimationClip BuildCamera(string path, out int frameCount)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            byte[] magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'M' || magic[1] != 'S' || magic[2] != 'R' || magic[3] != 'C')
                throw new InvalidDataException("MSRC マジックが一致しません。.msrc ファイルではない可能性があります。");

            ushort version = br.ReadUInt16();
            uint flags = br.ReadUInt32();
            float nominalFps = br.ReadSingle();
            uint headerFrameCount = br.ReadUInt32();

            bool hasTimestamp = (flags & MsrcFormat.FlagHasTimestamp) != 0;
            int stride = (hasTimestamp ? 8 : 0) + 12 + 16 + 12 + 4;
            frameCount = ResolveFrameCount(fs, headerFrameCount, stride);

            var times = new float[frameCount];
            var pos = NewChannels(3, frameCount);
            var rot = NewChannels(4, frameCount);
            var scl = NewChannels(3, frameCount);
            var fov = new float[frameCount];

            for (int f = 0; f < frameCount; f++)
            {
                if ((f & 0x3FF) == 0)
                    EditorUtility.DisplayProgressBar("MudShip Recorder", $"読み込み中… {f}/{frameCount}", (float)f / frameCount);

                times[f] = hasTimestamp ? (float)br.ReadDouble() : f / Mathf.Max(1f, nominalFps);
                for (int a = 0; a < 3; a++) pos[a][f] = br.ReadSingle();
                for (int a = 0; a < 4; a++) rot[a][f] = br.ReadSingle();
                for (int a = 0; a < 3; a++) scl[a][f] = br.ReadSingle();
                fov[f] = br.ReadSingle();
            }

            var clip = new AnimationClip { frameRate = nominalFps > 0f ? nominalFps : 60f };
            EditorUtility.DisplayProgressBar("MudShip Recorder", "カーブ生成中…", 0.9f);
            SetTrsCurves(clip, times, pos, rot, scl);
            clip.SetCurve("", typeof(Camera), "field of view", BuildLinearCurve(times, fov));
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        // ---- .msra (Audio) → WAV ------------------------------------------------

        static byte[] BuildWav(string msraPath)
        {
            using var fs = new FileStream(msraPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            byte[] magic = br.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != 'M' || magic[1] != 'S' || magic[2] != 'R' || magic[3] != 'A')
                throw new InvalidDataException("MSRA マジックが一致しません。.msra ファイルではない可能性があります。");

            ushort version = br.ReadUInt16();
            uint flags = br.ReadUInt32();
            int sampleRate = (int)br.ReadUInt32();
            int channels = br.ReadUInt16();
            int bits = br.ReadUInt16();
            double startOffset = br.ReadDouble();
            uint headerFrameCount = br.ReadUInt32();

            int stride = channels * (bits / 8);
            if (stride <= 0)
                throw new InvalidDataException("不正なチャンネル/ビット深度です。");

            long dataStart = fs.Position;
            long dataBytes = fs.Length - dataStart;
            int framesFromSize = (int)(dataBytes / stride);
            int frames = headerFrameCount > 0 ? Mathf.Min((int)headerFrameCount, framesFromSize) : framesFromSize;
            if (frames <= 0)
                throw new InvalidDataException("有効なサンプルがありません。");

            int pcmBytes = frames * stride;
            byte[] pcm = br.ReadBytes(pcmBytes);

            return WavBytes(pcm, sampleRate, channels, bits);
        }

        static byte[] WavBytes(byte[] pcm, int sampleRate, int channels, int bits)
        {
            int blockAlign = channels * (bits / 8);
            int byteRate = sampleRate * blockAlign;
            int dataLen = pcm.Length;

            using var ms = new MemoryStream(44 + dataLen);
            using var bw = new BinaryWriter(ms);

            WriteTag(bw, "RIFF");
            bw.Write(36 + dataLen);
            WriteTag(bw, "WAVE");
            WriteTag(bw, "fmt ");
            bw.Write(16);                  // PCM fmt chunk size
            bw.Write((short)1);            // audio format = PCM
            bw.Write((short)channels);
            bw.Write(sampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write((short)bits);
            WriteTag(bw, "data");
            bw.Write(dataLen);
            bw.Write(pcm);
            bw.Flush();
            return ms.ToArray();
        }

        static void WriteTag(BinaryWriter bw, string tag)
        {
            for (int i = 0; i < tag.Length; i++)
                bw.Write((byte)tag[i]);
        }

        // ---- 共通 ---------------------------------------------------------------

        static float[][] NewChannels(int channels, int frameCount)
        {
            var a = new float[channels][];
            for (int c = 0; c < channels; c++) a[c] = new float[frameCount];
            return a;
        }

        /// <summary>path 空（対象 GameObject 自身）に localPosition/localRotation/localScale カーブを張る。</summary>
        static void SetTrsCurves(AnimationClip clip, float[] times, float[][] pos, float[][] rot, float[][] scl)
        {
            string[] posP = { "localPosition.x", "localPosition.y", "localPosition.z" };
            string[] rotP = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
            string[] sclP = { "localScale.x", "localScale.y", "localScale.z" };
            for (int a = 0; a < 3; a++) clip.SetCurve("", typeof(Transform), posP[a], BuildLinearCurve(times, pos[a]));
            for (int a = 0; a < 4; a++) clip.SetCurve("", typeof(Transform), rotP[a], BuildLinearCurve(times, rot[a]));
            for (int a = 0; a < 3; a++) clip.SetCurve("", typeof(Transform), sclP[a], BuildLinearCurve(times, scl[a]));
        }

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
