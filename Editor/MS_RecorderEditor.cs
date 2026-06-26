using System.IO;
using UnityEditor;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// <see cref="MS_Recorder"/> 用のカスタムインスペクタ。スロットをカード表示し、参照中の
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

            EditorGUILayout.Space(2);

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

            EditorGUILayout.Space(10);

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

        // ---- スロット ------------------------------------------------------------

        void DrawSlots()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Recording Slots ({_slots.arraySize})", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("＋ 追加", GUILayout.Width(72)))
                    AddSlot();
            }

            if (_slots.arraySize == 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("スロットがありません。「＋ 追加」で録画対象を追加してください。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);

            int removeAt = -1;
            for (int i = 0; i < _slots.arraySize; i++)
            {
                DrawSlotCard(i, ref removeAt);
                EditorGUILayout.Space(6);
            }

            if (removeAt >= 0)
                _slots.DeleteArrayElementAtIndex(removeAt);
        }

        void DrawSlotCard(int i, ref int removeAt)
        {
            var slot = _slots.GetArrayElementAtIndex(i);
            var settingsProp = slot.FindPropertyRelative("settings");
            var so = settingsProp.objectReferenceValue as MS_RecorderSettings;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ヘッダ行: 折りたたみ / バッジ / 削除
                using (new EditorGUILayout.HorizontalScope())
                {
                    slot.isExpanded = EditorGUILayout.Foldout(slot.isExpanded, $"Slot {i + 1}", true);
                    GUILayout.FlexibleSpace();

                    string badge = so == null ? "プロファイル未設定" : $"{so.Type}";
                    GUILayout.Label(badge, EditorStyles.miniLabel);

                    if (GUILayout.Button("✕", GUILayout.Width(22)))
                        removeAt = i;
                }

                if (!slot.isExpanded)
                    return;

                EditorGUILayout.Space(2);
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(settingsProp, new GUIContent("Settings (Profile)"));

                if (so == null)
                {
                    EditorGUILayout.HelpBox("MS_RecorderSettings（プロファイル）を割り当ててください。", MessageType.Warning);
                }
                else if (so.Type == MS_RecorderSettings.RecorderType.Character)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Motion", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(slot.FindPropertyRelative("animator"));
                    EditorGUILayout.PropertyField(slot.FindPropertyRelative("hipBone"));
                    EditorGUILayout.PropertyField(slot.FindPropertyRelative("addBones"), true);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Facial", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(slot.FindPropertyRelative("faceRenderers"), true);
                }
                else
                {
                    EditorGUILayout.HelpBox($"type = {so.Type} は未実装です（枠のみ）。", MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }
        }

        void AddSlot()
        {
            int idx = _slots.arraySize;
            _slots.InsertArrayElementAtIndex(idx);

            // 直前要素のコピーになるため、参照を空にして新規スロットとして扱う。
            var added = _slots.GetArrayElementAtIndex(idx);
            added.isExpanded = true;
            added.FindPropertyRelative("settings").objectReferenceValue = null;
            added.FindPropertyRelative("animator").objectReferenceValue = null;
            added.FindPropertyRelative("hipBone").objectReferenceValue = null;
            added.FindPropertyRelative("addBones").ClearArray();
            added.FindPropertyRelative("faceRenderers").ClearArray();
        }

        // ---- 録画ボタン・状態 ----------------------------------------------------

        static void DrawRecordButton(MS_Recorder recorder)
        {
            Color prev = GUI.backgroundColor;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(2);
                if (!recorder.IsRecording)
                {
                    GUI.backgroundColor = new Color(0.85f, 0.30f, 0.30f);
                    if (GUILayout.Button("● 録画開始", GUILayout.Height(36)))
                        recorder.StartRecording();
                }
                else
                {
                    GUI.backgroundColor = new Color(0.45f, 0.45f, 0.45f);
                    if (GUILayout.Button("■ 停止", GUILayout.Height(36)))
                        recorder.StopRecording();
                }
                GUILayout.Space(2);
            }
            GUI.backgroundColor = prev;
        }

        static void DrawStatus(MS_Recorder recorder)
        {
            int total = recorder.MotionSessions.Count + recorder.FaceSessions.Count;
            if (total == 0)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(recorder.IsRecording ? "録画中" : "直近の記録", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var s in recorder.MotionSessions)
                    DrawSessionLine("M", s.FilePath, s.FrameCount, s.Faulted);
                foreach (var s in recorder.FaceSessions)
                    DrawSessionLine("F", s.FilePath, s.FrameCount, s.Faulted);
            }
        }

        static void DrawSessionLine(string tag, string path, int frames, bool faulted)
        {
            string file = string.IsNullOrEmpty(path) ? "-" : Path.GetFileName(path);
            string state = faulted ? "  (エラー)" : "";
            EditorGUILayout.LabelField($"[{tag}] {file}", $"{frames} frames{state}");
        }
    }
}
