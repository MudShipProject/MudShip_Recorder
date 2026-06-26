using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// シーンに配置して使う録画コンポーネント。複数の Animator を対象に取り、録画開始で
    /// 各 Animator ごとに 1 つの .msrc を生成する。録画中は LateUpdate で各セッションへ
    /// フレームを書き込むだけ (メインスレッドはコピーのみ、IO はバックグラウンド)。
    ///
    /// 他クラスからは <see cref="StartRecording"/> / <see cref="StopRecording"/> を呼ぶか、
    /// <see cref="RecordingStarted"/> / <see cref="RecordingStopped"/> を購読して連携する。
    /// より低レベルに触りたい場合は <see cref="MotionRecorderSession"/> を直接利用できる。
    /// </summary>
    [AddComponentMenu("MudShip/Motion Recorder")]
    [DisallowMultipleComponent]
    public class MotionRecorderBehaviour : MonoBehaviour
    {
        /// <summary>
        /// 記録対象 1 件分の設定。Animator と、その localPosition も記録するボーン (通常は腰) のペア。
        /// </summary>
        [Serializable]
        public class Target
        {
            [Tooltip("記録対象の Animator。Transform 以下を全走査して localRotation を記録する。")]
            public Animator animator;

            [Tooltip("localPosition を記録する腰ボーン。\n" +
                     "空の場合: Humanoid なら Hips を自動採用。Humanoid 以外では腰の位置を記録せず警告。\n" +
                     "Generic リグや腰の自動取得が効かない場合はここに腰ボーンを明示指定する。")]
            public Transform hipBone;

            [Tooltip("腰に加えて localPosition も記録する追加ボーン (ツイストボーン等)。\n" +
                     "回転だけでなく位置も必要なボーンがあればここに指定する。通常は空でよい。")]
            public List<Transform> positionBones = new List<Transform>();
        }

        [Tooltip("記録対象一覧。各 Animator の Transform 以下を全走査して記録する。")]
        [SerializeField] List<Target> _targets = new List<Target>();

        [Tooltip("出力先フォルダ。空なら persistentDataPath/MotionRecordings を使う。")]
        [SerializeField] string _outputDirectory = "";

        [Tooltip("root (Animator の Transform) 自身も記録対象に含めるか。通常 false。")]
        [SerializeField] bool _includeRoot = false;

        [SerializeField] RecorderSettings _settings = RecorderSettings.Default;

        readonly List<MotionRecorderSession> _sessions = new List<MotionRecorderSession>();
        double _startTime;

        /// <summary>記録対象一覧 (実行時に編集可能)。各要素は Animator＋位置記録ボーンのペア。</summary>
        public IList<Target> Targets => _targets;

        /// <summary>記録中か。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>現在 (または直近) のセッション一覧。状態表示や後処理に使える。</summary>
        public IReadOnlyList<MotionRecorderSession> Sessions => _sessions;

        /// <summary>録画開始時に発火。</summary>
        public event Action RecordingStarted;

        /// <summary>録画停止時に発火。</summary>
        public event Action RecordingStopped;

        /// <summary>実際の出力先フォルダを解決する。</summary>
        public string ResolveOutputDirectory()
            => string.IsNullOrEmpty(_outputDirectory)
                ? Path.Combine(Application.persistentDataPath, "MotionRecordings")
                : _outputDirectory;

        /// <summary>録画を開始する。対象 Animator ごとに .msrc セッションを生成する。</summary>
        public void StartRecording()
        {
            if (IsRecording)
                return;

            DisposeSessions();

            string dir = ResolveOutputDirectory();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            foreach (var entry in _targets)
            {
                var animator = entry?.animator;
                if (animator == null)
                    continue;

                try
                {
                    var skeleton = SkeletonDefinition.FromAnimator(animator, entry.hipBone, entry.positionBones, _includeRoot);
                    WarnPositionBones(animator, entry, skeleton);

                    var session = new MotionRecorderSession(skeleton, _settings);
                    string fileName = $"{MakeSafeFileName(animator.gameObject.name)}_{stamp}{MsrcFormat.Extension}";
                    session.Start(Path.Combine(dir, fileName));
                    _sessions.Add(session);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }

            if (_sessions.Count == 0)
            {
                Debug.LogWarning("[MotionRecorder] 記録対象が 1 つもありません。Targets に Animator を設定してください。", this);
                return;
            }

            _startTime = Time.timeAsDouble;
            IsRecording = true;
            RecordingStarted?.Invoke();
        }

        /// <summary>録画を停止し、全 .msrc を確定する。</summary>
        public void StopRecording()
        {
            if (!IsRecording)
                return;

            IsRecording = false;

            foreach (var session in _sessions)
            {
                try
                {
                    session.Stop();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }

            RecordingStopped?.Invoke();

#if UNITY_EDITOR
            // 出力先がプロジェクト (Assets) 配下なら、書き出した .msrc をエディタに取り込む。
            // session.Stop() は IO スレッドの join まで行うので、ここでファイルは確定済み。
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
                var session = _sessions[i];
                session.CaptureFrame(t);
                if (session.Faulted)
                {
                    Debug.LogException(session.FaultException ?? new IOException("Recording faulted."), this);
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
            foreach (var session in _sessions)
            {
                try { session.Dispose(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
            _sessions.Clear();
        }

        /// <summary>位置記録ボーンの解決結果を検査し、取りこぼし・未指定を警告する。</summary>
        void WarnPositionBones(Animator animator, Target entry, SkeletonDefinition skeleton)
        {
            // 要求した位置記録ボーン数 (腰の明示指定 + 追加ボーン)。腰の自動採用 (Humanoid Hips) は含めない。
            int requested = entry.hipBone != null ? 1 : 0;
            if (entry.positionBones != null)
                foreach (var t in entry.positionBones)
                    if (t != null) requested++;

            int recorded = skeleton.PositionBoneIndices.Length;

            if (requested > 0 && recorded < requested)
                Debug.LogWarning(
                    $"[MotionRecorder] '{animator.name}': 指定した位置記録ボーンの一部が Animator の階層外のため除外されました ({recorded}/{requested})。",
                    this);

            if (recorded == 0)
                Debug.LogWarning(
                    $"[MotionRecorder] '{animator.name}': localPosition を記録するボーンがありません (回転のみ記録)。" +
                    "Humanoid 以外、または腰を記録したい場合は対象の Hip Bone に腰ボーンを指定してください。",
                    this);
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
