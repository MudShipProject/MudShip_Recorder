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
        /// <summary>記録の種別。Camera / Transform は枠のみ（機能は後日）。</summary>
        public enum RecorderType
        {
            Character,
            Camera,
            Transform,
        }

        /// <summary>
        /// 録画対象 1 件分のスロット。種別・出力先・Settings・シーン配線をすべてここに持つ
        /// （ScriptableObject は使わず、シーンに直接保存する）。Character タイプのとき Animator 以下を使う。
        /// </summary>
        [Serializable]
        public class Slot
        {
            [Tooltip("記録の種別。Character はモーション＋表情。Camera / Transform は未実装（枠のみ）。")]
            public RecorderType type = RecorderType.Character;

            [Tooltip("出力先フォルダ。空なら persistentDataPath/MudShipRecordings を使う。")]
            public string outputDirectory = "";

            public RecorderSettings settings = RecorderSettings.Default;

            [Tooltip("記録対象の Animator。Transform 以下を全走査して localRotation を記録する。")]
            public Animator animator;

            [Tooltip("localPosition を記録する腰ボーン。\n空なら Humanoid の Hips を自動採用。Generic 等はここに腰ボーンを明示指定する。")]
            public Transform hipBone;

            [Tooltip("腰に加えて localPosition も記録する追加ボーン（ツイストボーン等）。回転は全ボーンで記録される。")]
            public List<Transform> addBones = new List<Transform>();

            [Tooltip("表情を記録する SkinnedMeshRenderer 群（各 SMR の全 BlendShape を記録）。\nAnimator の GameObject 配下にあること。空なら表情は記録しない。")]
            public List<SkinnedMeshRenderer> faceRenderers = new List<SkinnedMeshRenderer>();

            /// <summary>実際の出力先フォルダを解決する。</summary>
            public string ResolveOutputDirectory()
                => string.IsNullOrEmpty(outputDirectory)
                    ? Path.Combine(Application.persistentDataPath, "MudShipRecordings")
                    : outputDirectory;
        }

        [Tooltip("録画スロット一覧。各スロット = プロファイル（SO）＋シーン配線。")]
        [SerializeField] List<Slot> _slots = new List<Slot>();

        readonly List<MotionRecorderSession> _motion = new List<MotionRecorderSession>();
        readonly List<FaceRecorderSession> _face = new List<FaceRecorderSession>();
        double _startTime;

        /// <summary>録画スロット一覧（実行時に編集可能）。</summary>
        public IList<Slot> Slots => _slots;

        /// <summary>録画中か。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>現在（または直近）のモーションセッション一覧。</summary>
        public IReadOnlyList<MotionRecorderSession> MotionSessions => _motion;

        /// <summary>現在（または直近）の表情セッション一覧。</summary>
        public IReadOnlyList<FaceRecorderSession> FaceSessions => _face;

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

            if (_motion.Count == 0 && _face.Count == 0)
            {
                Debug.LogWarning("[MS_Recorder] 記録対象が 1 つもありません。スロットに type=Character と Animator を設定してください。", this);
                return;
            }

            _startTime = Time.timeAsDouble;
            IsRecording = true;
            RecordingStarted?.Invoke();
        }

        void StartSlot(Slot slot, int index, string stamp, HashSet<string> usedNames)
        {
            if (slot.type != RecorderType.Character)
            {
                Debug.Log($"[MS_Recorder] Slot {index + 1}: type={slot.type} は未実装のためスキップします。", this);
                return;
            }

            var animator = slot.animator;
            if (animator == null)
            {
                Debug.LogWarning($"[MS_Recorder] Slot {index + 1}: Animator 未設定のためスキップします。", this);
                return;
            }

            string dir = slot.ResolveOutputDirectory();
            var settings = slot.settings;
            string fileBase = UniqueName($"{MakeSafeFileName(animator.gameObject.name)}_{stamp}", usedNames);

            // モーション (.msrm)
            var skeleton = SkeletonDefinition.FromAnimator(animator, slot.hipBone, slot.addBones);
            WarnPositionBones(animator, slot, skeleton);
            var motion = new MotionRecorderSession(skeleton, settings);
            motion.Start(Path.Combine(dir, fileBase + MsrmFormat.Extension));
            _motion.Add(motion);

            // 表情 (.msrf) — SMR 指定があるときのみ
            if (slot.faceRenderers != null && slot.faceRenderers.Count > 0)
            {
                var face = FaceDefinition.FromRenderers(animator.transform, slot.faceRenderers);
                WarnFace(animator, slot, face);
                if (!face.IsEmpty)
                {
                    var faceSession = new FaceRecorderSession(face, settings);
                    faceSession.Start(Path.Combine(dir, fileBase + MsrfFormat.Extension));
                    _face.Add(faceSession);
                }
            }
        }

        /// <summary>録画を停止し、全ストリームを確定する。</summary>
        public void StopRecording()
        {
            if (!IsRecording)
                return;

            IsRecording = false;

            foreach (var s in _motion)
            {
                try { s.Stop(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            foreach (var s in _face)
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

            for (int i = 0; i < _motion.Count; i++)
            {
                var s = _motion[i];
                s.CaptureFrame(t);
                if (s.Faulted)
                {
                    Debug.LogException(s.FaultException ?? new IOException("Motion recording faulted."), this);
                    faulted = true;
                }
            }

            for (int i = 0; i < _face.Count; i++)
            {
                var s = _face[i];
                s.CaptureFrame(t);
                if (s.Faulted)
                {
                    Debug.LogException(s.FaultException ?? new IOException("Face recording faulted."), this);
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
            foreach (var s in _motion)
            {
                try { s.Dispose(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            foreach (var s in _face)
            {
                try { s.Dispose(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            _motion.Clear();
            _face.Clear();
        }

        /// <summary>位置記録ボーンの解決結果を検査し、取りこぼし・未指定を警告する。</summary>
        void WarnPositionBones(Animator animator, Slot slot, SkeletonDefinition skeleton)
        {
            int requested = slot.hipBone != null ? 1 : 0;
            if (slot.addBones != null)
                foreach (var t in slot.addBones)
                    if (t != null) requested++;

            int recorded = skeleton.PositionBoneIndices.Length;

            if (requested > 0 && recorded < requested)
                Debug.LogWarning(
                    $"[MS_Recorder] '{animator.name}': 指定した位置記録ボーンの一部が Animator の階層外のため除外されました ({recorded}/{requested})。",
                    this);

            if (recorded == 0)
                Debug.LogWarning(
                    $"[MS_Recorder] '{animator.name}': localPosition を記録するボーンがありません (回転のみ記録)。" +
                    "Humanoid 以外、または腰を記録したい場合はスロットの Hip Bone に腰ボーンを指定してください。",
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
