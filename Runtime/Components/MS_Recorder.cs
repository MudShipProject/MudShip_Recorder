using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 録画のマスターコンポーネント。スロット（種別・出力先・Settings・シーン配線）のリストを保持し、
    /// 録画開始で各スロットの全ストリームを生成・駆動する。設定はすべてこのコンポーネント＝シーンに保存する。
    ///
    /// 録画開始時に基準 startTime を 1 つ確定し、全ストリーム（.msrm / .msrf）へ同じ
    /// <c>Time.timeAsDouble - startTime</c> を渡す（＝共通タイムコード同期の土台）。録画中は
    /// LateUpdate で各セッションへフレームを書き込むだけ（メインスレッドはコピーのみ、IO はバックグラウンド）。
    /// </summary>
    [AddComponentMenu("MudShip/MS Recorder")]
    [DisallowMultipleComponent]
    public class MS_Recorder : MonoBehaviour
    {
        /// <summary>記録の種別。</summary>
        public enum RecorderType
        {
            Character,
            Camera,
            Transform,
            Audio,
        }

        /// <summary>Transform / Camera の記録空間。</summary>
        public enum RecordSpace
        {
            World,
            Local,
        }

        /// <summary>
        /// 録画対象 1 件分のスロット。種別・出力先・Settings・シーン配線をすべてここに持つ
        /// （ScriptableObject は使わず、シーンに直接保存する）。type に応じて使うフィールドが変わる。
        /// </summary>
        [Serializable]
        public class Slot
        {
            [Tooltip("インスペクタ表示用の名前（任意）。スロットの識別用で、ファイル名には影響しない。")]
            public string label = "";

            [Tooltip("記録の種別。Character はモーション＋表情。")]
            public RecorderType type = RecorderType.Character;

            [Tooltip("出力先フォルダ。空なら persistentDataPath/MudShipRecordings を使う。")]
            public string outputDirectory = "";

            public RecorderSettings settings = RecorderSettings.Default;

            [Tooltip("記録対象の Animator。Transform 以下を全走査して localRotation を記録する。")]
            public Animator animator;

            [Tooltip("localPosition を記録する腰ボーン。\n空なら Humanoid の Hips を自動採用。Generic 等はここに腰ボーンを明示指定する。")]
            public Transform hipBone;

            [Tooltip("ヒューマノイド定義に含まれないが回転を記録したい追加ボーン（ツイストボーン等）。\nヒューマノイドボーン＋ここで指定したボーンの localRotation を記録する。位置は Hip のみ。")]
            public List<Transform> addBones = new List<Transform>();

            [Tooltip("表情を記録する SkinnedMeshRenderer 群（各 SMR の全 BlendShape を記録）。\nAnimator の GameObject 配下にあること。空なら表情は記録しない。")]
            public List<SkinnedMeshRenderer> faceRenderers = new List<SkinnedMeshRenderer>();

            [Tooltip("[Transform タイプ] 記録対象の Transform。Pos / Rot / Scale を記録する。")]
            public Transform transformTarget;

            [Tooltip("[Camera タイプ] 記録対象の Camera。Transform に加えて fieldOfView も記録する。")]
            public Camera cameraTarget;

            [Tooltip("[Transform / Camera] 記録する空間。\nWorld = 親の影響込みのワールド値（position/rotation/lossyScale）。リグ/ドリーの下のカメラはこちら。\nLocal = 親基準のローカル値（localPosition 等）。")]
            public RecordSpace space = RecordSpace.World;

            [Tooltip("[Audio タイプ] 録音する入力デバイス名（Microphone.devices）。空なら既定の先頭デバイス。\nPC の再生音を録るには仮想オーディオケーブル等が必要。")]
            public string audioDevice = "";

            [Tooltip("[Audio タイプ] サンプルレート(Hz)。デバイス対応範囲にクランプ。0/既定は 48000。音合わせ用途なら下げて軽量化も可。")]
            public int audioSampleRate = 48000;

            /// <summary>実際の出力先フォルダを解決する。</summary>
            public string ResolveOutputDirectory()
                => string.IsNullOrEmpty(outputDirectory)
                    ? Path.Combine(Application.persistentDataPath, "MudShipRecordings")
                    : outputDirectory;
        }

        [Tooltip("全ファイル名の先頭に付く文字列。空可。<Take> でテイク番号を 3 桁ゼロ詰めで埋め込める。\n最終形式: (prefix)_(type)_(object)_(date)")]
        [SerializeField] string _fileNamePrefix = "";

        [Tooltip("テイク番号。プレフィックス内の <Take> に展開される。")]
        [SerializeField] int _take = 1;

        [Tooltip("録画スロット一覧。各スロット = 種別・出力先・Settings・シーン配線。")]
        [SerializeField] List<Slot> _slots = new List<Slot>();

        readonly List<IRecorderSession> _sessions = new List<IRecorderSession>();
        double _startTime;

        /// <summary>録画スロット一覧（実行時に編集可能）。</summary>
        public IList<Slot> Slots => _slots;

        /// <summary>録画中か。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>現在（または直近）の全セッション一覧（種別混在）。</summary>
        public IReadOnlyList<IRecorderSession> Sessions => _sessions;

        /// <summary>録画開始時に発火。</summary>
        public event Action RecordingStarted;

        /// <summary>録画停止時に発火。</summary>
        public event Action RecordingStopped;

        /// <summary>録画を開始する。各スロットからストリームセッションを生成する。</summary>
        public void StartRecording()
        {
            if (IsRecording)
                return;

            DisposeSessions();

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var usedNames = new HashSet<string>();

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot == null)
                    continue;

                try
                {
                    StartSlot(slot, i, stamp, usedNames);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }

            if (_sessions.Count == 0)
            {
                Debug.LogWarning("[MS_Recorder] 記録対象が 1 つもありません。スロットに種別と対象を設定してください。", this);
                return;
            }

            _startTime = Time.timeAsDouble;
            IsRecording = true;
            RecordingStarted?.Invoke();
        }

        void StartSlot(Slot slot, int index, string stamp, HashSet<string> usedNames)
        {
            switch (slot.type)
            {
                case RecorderType.Character: StartCharacter(slot, index, stamp, usedNames); break;
                case RecorderType.Transform: StartTransform(slot, index, stamp, usedNames); break;
                case RecorderType.Camera: StartCamera(slot, index, stamp, usedNames); break;
                case RecorderType.Audio: StartAudio(slot, index, stamp, usedNames); break;
            }
        }

        void StartCharacter(Slot slot, int index, string stamp, HashSet<string> usedNames)
        {
            var animator = slot.animator;
            if (animator == null)
            {
                Debug.LogWarning($"[MS_Recorder] Slot {index + 1} (Character): Animator 未設定のためスキップします。", this);
                return;
            }

            string dir = slot.ResolveOutputDirectory();
            var settings = slot.settings;
            string objectName = animator.gameObject.name;

            // モーション (.msrm) — type 部分は "Motion"
            var skeleton = SkeletonDefinition.FromAnimator(animator, slot.hipBone, slot.addBones);
            WarnPositionBones(animator, slot, skeleton);
            var motion = new MotionRecorderSession(skeleton, settings);
            string motionBase = UniqueName(BuildFileBase("Motion", objectName, stamp), usedNames);
            motion.Start(Path.Combine(dir, motionBase + MsrmFormat.Extension));
            _sessions.Add(motion);

            // 表情 (.msrf) — type 部分は "Facial"。SMR 指定があるときのみ
            if (slot.faceRenderers != null && slot.faceRenderers.Count > 0)
            {
                var face = FaceDefinition.FromRenderers(animator.transform, slot.faceRenderers);
                WarnFace(animator, slot, face);
                if (!face.IsEmpty)
                {
                    var faceSession = new FaceRecorderSession(face, settings);
                    string faceBase = UniqueName(BuildFileBase("Facial", objectName, stamp), usedNames);
                    faceSession.Start(Path.Combine(dir, faceBase + MsrfFormat.Extension));
                    _sessions.Add(faceSession);
                }
            }
        }

        void StartTransform(Slot slot, int index, string stamp, HashSet<string> usedNames)
        {
            var target = slot.transformTarget;
            if (target == null)
            {
                Debug.LogWarning($"[MS_Recorder] Slot {index + 1} (Transform): Transform Target 未設定のためスキップします。", this);
                return;
            }

            string dir = slot.ResolveOutputDirectory();
            string fileBase = UniqueName(BuildFileBase("Transform", target.gameObject.name, stamp), usedNames);

            var session = new TransformRecorderSession(target, slot.settings, slot.space == RecordSpace.World);
            session.Start(Path.Combine(dir, fileBase + MsrtFormat.Extension));
            _sessions.Add(session);
        }

        void StartCamera(Slot slot, int index, string stamp, HashSet<string> usedNames)
        {
            var cam = slot.cameraTarget;
            if (cam == null)
            {
                Debug.LogWarning($"[MS_Recorder] Slot {index + 1} (Camera): Camera Target 未設定のためスキップします。", this);
                return;
            }

            string dir = slot.ResolveOutputDirectory();
            string fileBase = UniqueName(BuildFileBase("Camera", cam.gameObject.name, stamp), usedNames);

            var session = new CameraRecorderSession(cam, slot.settings, slot.space == RecordSpace.World);
            session.Start(Path.Combine(dir, fileBase + MsrcFormat.Extension));
            _sessions.Add(session);
        }

        void StartAudio(Slot slot, int index, string stamp, HashSet<string> usedNames)
        {
            string dir = slot.ResolveOutputDirectory();
            string label = string.IsNullOrEmpty(slot.audioDevice) ? "Mic" : slot.audioDevice;
            string fileBase = UniqueName(BuildFileBase("Audio", label, stamp), usedNames);

            var session = new AudioRecorderSession(slot.audioDevice, slot.audioSampleRate, slot.settings);
            session.Start(Path.Combine(dir, fileBase + MsraFormat.Extension));
            _sessions.Add(session);
        }

        /// <summary>録画を停止し、全ストリームを確定する。</summary>
        public void StopRecording()
        {
            if (!IsRecording)
                return;

            IsRecording = false;

            foreach (var s in _sessions)
            {
                try { s.Stop(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }

            RecordingStopped?.Invoke();

#if UNITY_EDITOR
            // 出力先がプロジェクト (Assets) 配下なら、書き出したファイルをエディタに取り込む。
            // Stop() は IO スレッドの join まで行うので、ここでファイルは確定済み。
            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        void LateUpdate()
        {
            if (!IsRecording)
                return;

            double t = Time.timeAsDouble - _startTime;
            bool faulted = false;

            for (int i = 0; i < _sessions.Count; i++)
            {
                var s = _sessions[i];
                s.CaptureFrame(t);
                if (s.Faulted)
                {
                    Debug.LogException(s.FaultException ?? new IOException("Recording faulted."), this);
                    faulted = true;
                }
            }

            if (faulted)
                StopRecording();
        }

        void OnDisable()
        {
            if (IsRecording)
                StopRecording();
        }

        void OnApplicationQuit()
        {
            if (IsRecording)
                StopRecording();
        }

        void OnDestroy()
        {
            DisposeSessions();
        }

        void DisposeSessions()
        {
            foreach (var s in _sessions)
            {
                try { s.Dispose(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            _sessions.Clear();
        }

        /// <summary>解決したスケルトンを検査し、記録対象なし・腰未解決を警告する。</summary>
        void WarnPositionBones(Animator animator, Slot slot, SkeletonDefinition skeleton)
        {
            if (skeleton.Bones.Length == 0)
            {
                Debug.LogWarning(
                    $"[MS_Recorder] '{animator.name}': 記録対象ボーンが 0 本です。" +
                    "ヒューマノイドの Avatar が未設定か、Generic で Add Bones も未指定の可能性があります。" +
                    "Generic の場合はスロットの Add Bones に記録したいボーンを指定してください。",
                    this);
                return;
            }

            if (skeleton.PositionBoneIndices.Length == 0)
                Debug.LogWarning(
                    $"[MS_Recorder] '{animator.name}': localPosition を記録する腰ボーンがありません（回転のみ記録）。" +
                    "Humanoid 以外、または腰位置を記録したい場合はスロットの Hip Bone に腰ボーンを指定してください。",
                    this);
        }

        /// <summary>表情記録対象の解決結果を検査し、取りこぼしを警告する。</summary>
        void WarnFace(Animator animator, Slot slot, FaceDefinition face)
        {
            int requested = 0;
            foreach (var r in slot.faceRenderers)
                if (r != null) requested++;

            int recorded = face.Renderers.Length;

            if (requested > 0 && recorded < requested)
                Debug.LogWarning(
                    $"[MS_Recorder] '{animator.name}': 指定した SkinnedMeshRenderer の一部が記録対象外でした ({recorded}/{requested})。" +
                    "Animator の GameObject 配下にあり、BlendShape を持つ SMR のみ記録します。",
                    this);
        }

        /// <summary>ファイル名のベース「(prefix)_(type)_(object)_(date)」を生成する（拡張子なし）。</summary>
        string BuildFileBase(string typeLabel, string objectName, string stamp)
        {
            string prefix = ResolvePrefix();
            string name = string.IsNullOrEmpty(prefix)
                ? $"{typeLabel}_{objectName}_{stamp}"
                : $"{prefix}_{typeLabel}_{objectName}_{stamp}";
            return MakeSafeFileName(name);
        }

        /// <summary>プレフィックス中の &lt;Take&gt; を 3 桁ゼロ詰めのテイク番号へ展開する。</summary>
        string ResolvePrefix()
            => ReplaceCaseInsensitive(_fileNamePrefix ?? "", "<Take>", _take.ToString("000")).Trim();

        static string ReplaceCaseInsensitive(string s, string token, string value)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var sb = new System.Text.StringBuilder(s.Length);
            int start = 0, idx;
            while ((idx = s.IndexOf(token, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                sb.Append(s, start, idx - start);
                sb.Append(value);
                start = idx + token.Length;
            }
            sb.Append(s, start, s.Length - start);
            return sb.ToString();
        }

        static string UniqueName(string baseName, HashSet<string> used)
        {
            string name = baseName;
            int i = 2;
            while (!used.Add(name))
            {
                name = $"{baseName}_{i}";
                i++;
            }
            return name;
        }

        static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Unnamed";

            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
