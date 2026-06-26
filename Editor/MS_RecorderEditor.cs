using System.IO;
using UnityEditor;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// <see cref="MS_Recorder"/> 用のカスタムインスペクタ。スロットをカード表示し、
    /// スロットの type に応じてシーン参照フィールドを出し分ける。
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
            var typeProp = slot.FindPropertyRelative("type");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // ヘッダ行: 折りたたみ / バッジ / 削除
                using (new EditorGUILayout.HorizontalScope())
                {
                    slot.isExpanded = EditorGUILayout.Foldout(slot.isExpanded, $"Slot {i + 1}", true);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(typeProp.enumDisplayNames[typeProp.enumValueIndex], EditorStyles.miniLabel);
                    if (GUILayout.Button("✕", GUILayout.Width(22)))
                        removeAt = i;
                }

                if (!slot.isExpanded)
                    return;

                EditorGUILayout.Space(2);
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(typeProp, new GUIContent("Type"));

                // 出力先 + 参照ボタン
                var outDirProp = slot.FindPropertyRelative("outputDirectory");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(outDirProp, new GUIContent("Output Directory"));
                    if (GUILayout.Button("参照…", GUILayout.Width(56)))
                        BrowseOutputDirectory(outDirProp);
                }
                if (string.IsNullOrEmpty(outDirProp.stringValue))
                    EditorGUILayout.LabelField(" ", "未設定なら persistentDataPath/MudShipRecordings", EditorStyles.miniLabel);

                EditorGUILayout.PropertyField(slot.FindPropertyRelative("settings"), new GUIContent("Settings"), true);

                int typeIndex = typeProp.enumValueIndex;
                if (typeIndex == (int)MS_Recorder.RecorderType.Character)
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
                else if (typeIndex == (int)MS_Recorder.RecorderType.Transform)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Transform（Pos / Rot / Scale）", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(slot.FindPropertyRelative("transformTarget"), new GUIContent("Transform Target"));
                }
                else if (typeIndex == (int)MS_Recorder.RecorderType.Camera)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Camera（Pos / Rot / Scale / FOV）", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(slot.FindPropertyRelative("cameraTarget"), new GUIContent("Camera Target"));
                }

                EditorGUI.indentLevel--;
            }
        }

        void BrowseOutputDirectory(SerializedProperty outputDirProp)
        {
            // プロジェクトのルート (Assets の親) を起点に開く。
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            string selected = EditorUtility.OpenFolderPanel("録画の出力先フォルダを選択", projectRoot, "");
            if (string.IsNullOrEmpty(selected))
                return; // キャンセル

            outputDirProp.stringValue = selected;
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI(); // OpenFolderPanel 後のレイアウト不整合を防ぐ
        }

        void AddSlot()
        {
            int idx = _slots.arraySize;
            _slots.InsertArrayElementAtIndex(idx);

            // 直前要素のコピーになるため、新規スロットとして既定値にリセットする。
            var added = _slots.GetArrayElementAtIndex(idx);
            added.isExpanded = true;
            added.FindPropertyRelative("type").enumValueIndex = (int)MS_Recorder.RecorderType.Character;
            added.FindPropertyRelative("outputDirectory").stringValue = "";

            var st = added.FindPropertyRelative("settings");
            st.FindPropertyRelative("NominalFps").floatValue = 60f;
            st.FindPropertyRelative("ChunkBytes").intValue = 256 * 1024;
            st.FindPropertyRelative("PooledChunkCount").intValue = 4;

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
            var sessions = recorder.Sessions;
            if (sessions.Count == 0)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(recorder.IsRecording ? "録画中" : "直近の記録", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var s in sessions)
                {
                    // ファイル名の拡張子（.msrm/.msrf/.msrt/.msrc）が種別を表す。
                    string file = string.IsNullOrEmpty(s.FilePath) ? "-" : Path.GetFileName(s.FilePath);
                    string state = s.Faulted ? "  (エラー)" : "";
                    EditorGUILayout.LabelField(file, $"{s.FrameCount} frames{state}");
                }
            }
        }
    }
}
