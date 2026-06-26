using System.IO;
using UnityEditor;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// <see cref="MotionRecorderBehaviour"/> 用のカスタムインスペクタ。
    /// 標準フィールド (Animator リスト等) に加え、プレイモード中に録画開始/停止ボタンと
    /// 各セッションの状態 (フレーム数・ファイル名) を表示する。
    /// </summary>
    [CustomEditor(typeof(MotionRecorderBehaviour))]
    public class MotionRecorderBehaviourEditor : UnityEditor.Editor
    {
        SerializedProperty _targets;
        SerializedProperty _outputDirectory;
        SerializedProperty _includeRoot;
        SerializedProperty _settings;

        void OnEnable()
        {
            _targets = serializedObject.FindProperty("_targets");
            _outputDirectory = serializedObject.FindProperty("_outputDirectory");
            _includeRoot = serializedObject.FindProperty("_includeRoot");
            _settings = serializedObject.FindProperty("_settings");
        }

        public override void OnInspectorGUI()
        {
            DrawFields();

            var recorder = (MotionRecorderBehaviour)target;

            EditorGUILayout.Space();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "録画はプレイモード中のみ可能です。アニメーション適用後の最終ポーズを LateUpdate で記録します。",
                    MessageType.Info);
                DrawOutputPath(recorder);
                return;
            }

            DrawRecordButton(recorder);
            DrawStatus(recorder);
            DrawOutputPath(recorder);

            if (recorder.IsRecording)
                Repaint(); // 録画中はフレーム数をライブ更新
        }

        void DrawFields()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_targets, true);

            // 出力先フォルダ: テキスト欄 + フォルダ選択ボタン
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_outputDirectory);
                if (GUILayout.Button("参照…", GUILayout.Width(56)))
                    BrowseOutputDirectory();
            }
            if (string.IsNullOrEmpty(_outputDirectory.stringValue))
                EditorGUILayout.LabelField(" ", "未設定の場合は persistentDataPath/MotionRecordings を使用", EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(_includeRoot);
            EditorGUILayout.PropertyField(_settings, true);

            serializedObject.ApplyModifiedProperties();
        }

        void BrowseOutputDirectory()
        {
            var recorder = (MotionRecorderBehaviour)target;
            string current = recorder.ResolveOutputDirectory();
            if (!Directory.Exists(current))
                current = Application.persistentDataPath;

            string selected = EditorUtility.OpenFolderPanel("録画の出力先フォルダを選択", current, "");
            if (string.IsNullOrEmpty(selected))
                return; // キャンセル

            _outputDirectory.stringValue = selected;
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI(); // OpenFolderPanel 後のレイアウト不整合を防ぐ
        }

        static void DrawRecordButton(MotionRecorderBehaviour recorder)
        {
            Color prev = GUI.backgroundColor;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!recorder.IsRecording)
                {
                    GUI.backgroundColor = new Color(0.85f, 0.30f, 0.30f);
                    if (GUILayout.Button("● 録画開始", GUILayout.Height(32)))
                        recorder.StartRecording();
                }
                else
                {
                    GUI.backgroundColor = new Color(0.45f, 0.45f, 0.45f);
                    if (GUILayout.Button("■ 停止", GUILayout.Height(32)))
                        recorder.StopRecording();
                }
            }
            GUI.backgroundColor = prev;
        }

        static void DrawStatus(MotionRecorderBehaviour recorder)
        {
            var sessions = recorder.Sessions;
            if (sessions == null || sessions.Count == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(recorder.IsRecording ? "録画中" : "直近の記録", EditorStyles.boldLabel);

            foreach (var session in sessions)
            {
                string file = string.IsNullOrEmpty(session.FilePath) ? "-" : Path.GetFileName(session.FilePath);
                string state = session.Faulted ? " (エラー)" : "";
                EditorGUILayout.LabelField(file, $"{session.FrameCount} frames{state}");
            }
        }

        static void DrawOutputPath(MotionRecorderBehaviour recorder)
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("出力先", recorder.ResolveOutputDirectory());
                if (GUILayout.Button("開く", GUILayout.Width(48)))
                {
                    string dir = recorder.ResolveOutputDirectory();
                    Directory.CreateDirectory(dir);
                    EditorUtility.RevealInFinder(dir);
                }
            }
        }
    }
}
