using System.IO;
using UnityEditor;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// <see cref="MS_Recorder"/> 用のカスタムインスペクタ。スロットごとに、参照中の
    /// <see cref="MS_RecorderSettings"/> の type に応じてシーン参照フィールドを出し分ける。
    /// プレイモード中は録画開始/停止ボタンと各セッションの状態を表示する。
    /// </summary>
    [CustomEditor(typeof(MS_Recorder))]
    public class MS_RecorderEditor : UnityEditor.Editor
    {
        SerializedProperty _slots;
        double _lastRepaint;

        void OnEnable()
        {
            _slots = serializedObject.FindProperty("_slots");
            EditorApplication.update += ThrottledRepaint;
        }

        void OnDisable()
        {
            EditorApplication.update -= ThrottledRepaint;
        }

        // 録画中の状態表示更新は ~10Hz に間引く。毎フレーム Repaint するとインスペクタの
        // 再描画コストがゲームのフレームレートを大きく削るため。
        void ThrottledRepaint()
        {
            var recorder = target as MS_Recorder;
            if (recorder == null || !recorder.IsRecording)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint >= 0.1)
            {
                _lastRepaint = now;
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            var recorder = (MS_Recorder)target;

            // 録画中はスロット一覧の描画 (ObjectField/リスト) を丸ごとスキップし、
            // ボタンと状態表示だけにする。これが録画中のインスペクタ負荷の主因。
            if (Application.isPlaying && recorder.IsRecording)
            {
                DrawRecordButton(recorder);
                DrawStatus(recorder);
                return;
            }

            serializedObject.Update();
            DrawSlots();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "録画はプレイモード中のみ可能です。アニメーション適用後の最終ポーズを LateUpdate で記録します。",
                    MessageType.Info);
                return;
            }

            DrawRecordButton(recorder);
            DrawStatus(recorder);
        }

        void DrawSlots()
        {
            EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);

            int removeAt = -1;
            for (int i = 0; i < _slots.arraySize; i++)
            {
                var slot = _slots.GetArrayElementAtIndex(i);
                var settingsProp = slot.FindPropertyRelative("settings");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"Slot {i + 1}", EditorStyles.boldLabel);
                        if (GUILayout.Button("削除", GUILayout.Width(48)))
                            removeAt = i;
                    }

                    EditorGUILayout.PropertyField(settingsProp, new GUIContent("Settings (Profile)"));

                    var so = settingsProp.objectReferenceValue as MS_RecorderSettings;
                    if (so == null)
                    {
                        EditorGUILayout.HelpBox("MS_RecorderSettings（プロファイル）を割り当ててください。", MessageType.Warning);
                    }
                    else if (so.Type == MS_RecorderSettings.RecorderType.Character)
                    {
                        EditorGUILayout.PropertyField(slot.FindPropertyRelative("animator"));
                        EditorGUILayout.PropertyField(slot.FindPropertyRelative("hipBone"));
                        EditorGUILayout.PropertyField(slot.FindPropertyRelative("addBones"), true);
                        EditorGUILayout.PropertyField(slot.FindPropertyRelative("faceRenderers"), true);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox($"type = {so.Type} は未実装です（枠のみ）。", MessageType.Info);
                    }
                }
            }

            if (removeAt >= 0)
                _slots.DeleteArrayElementAtIndex(removeAt);

            if (GUILayout.Button("＋ スロット追加"))
            {
                _slots.InsertArrayElementAtIndex(_slots.arraySize);
                // 直前要素のコピーになるため、参照を空にして新規スロットとして扱う。
                var added = _slots.GetArrayElementAtIndex(_slots.arraySize - 1);
                added.FindPropertyRelative("settings").objectReferenceValue = null;
                added.FindPropertyRelative("animator").objectReferenceValue = null;
                added.FindPropertyRelative("hipBone").objectReferenceValue = null;
                added.FindPropertyRelative("addBones").ClearArray();
                added.FindPropertyRelative("faceRenderers").ClearArray();
            }
        }

        static void DrawRecordButton(MS_Recorder recorder)
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

        static void DrawStatus(MS_Recorder recorder)
        {
            int total = recorder.MotionSessions.Count + recorder.FaceSessions.Count;
            if (total == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(recorder.IsRecording ? "録画中" : "直近の記録", EditorStyles.boldLabel);

            foreach (var s in recorder.MotionSessions)
                DrawSessionLine("[M]", s.FilePath, s.FrameCount, s.Faulted);
            foreach (var s in recorder.FaceSessions)
                DrawSessionLine("[F]", s.FilePath, s.FrameCount, s.Faulted);
        }

        static void DrawSessionLine(string tag, string path, int frames, bool faulted)
        {
            string file = string.IsNullOrEmpty(path) ? "-" : Path.GetFileName(path);
            string state = faulted ? " (エラー)" : "";
            EditorGUILayout.LabelField($"{tag} {file}", $"{frames} frames{state}");
        }
    }
}
