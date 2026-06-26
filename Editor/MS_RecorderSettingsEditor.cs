using System.IO;
using UnityEditor;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// <see cref="MS_RecorderSettings"/> 用のカスタムインスペクタ。type ドロップダウン＋共通 Settings を
    /// 表示し、出力先は「参照…」ボタン（プロジェクトルート起点のフォルダ選択）で設定できる。
    /// </summary>
    [CustomEditor(typeof(MS_RecorderSettings))]
    public class MS_RecorderSettingsEditor : UnityEditor.Editor
    {
        SerializedProperty _type;
        SerializedProperty _outputDirectory;
        SerializedProperty _settings;

        void OnEnable()
        {
            _type = serializedObject.FindProperty("_type");
            _outputDirectory = serializedObject.FindProperty("_outputDirectory");
            _settings = serializedObject.FindProperty("_settings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_type);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_outputDirectory);
                if (GUILayout.Button("参照…", GUILayout.Width(56)))
                    BrowseOutputDirectory();
            }
            if (string.IsNullOrEmpty(_outputDirectory.stringValue))
                EditorGUILayout.LabelField(" ", "未設定の場合は persistentDataPath/MudShipRecordings を使用", EditorStyles.miniLabel);

            EditorGUILayout.PropertyField(_settings, true);

            if (_type.enumValueIndex != (int)MS_RecorderSettings.RecorderType.Character)
                EditorGUILayout.HelpBox("Character 以外のタイプは未実装です（枠のみ）。", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        void BrowseOutputDirectory()
        {
            // プロジェクトのルート (Assets の親) を起点に開く。
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            string selected = EditorUtility.OpenFolderPanel("録画の出力先フォルダを選択", projectRoot, "");
            if (string.IsNullOrEmpty(selected))
                return; // キャンセル

            _outputDirectory.stringValue = selected;
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI(); // OpenFolderPanel 後のレイアウト不整合を防ぐ
        }
    }
}
