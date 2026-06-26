using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MudShip.MotionRecorder.Editor
{
    /// <summary>
    /// <see cref="MS_Recorder"/> 用のカスタムインスペクタ。スロットを ReorderableList で表示し、
    /// type に応じてフィールドを出し分け、種別ごとに左端のカラーで色分けする。
    /// プレイモード中は録画開始/停止ボタンと各セッションの状態を表示する。
    /// </summary>
    [CustomEditor(typeof(MS_Recorder))]
    public class MS_RecorderEditor : UnityEditor.Editor
    {
        SerializedProperty _slots;
        SerializedProperty _fileNamePrefix;
        SerializedProperty _take;
        ReorderableList _list;
        double _lastRepaint;

        void OnEnable()
        {
            _slots = serializedObject.FindProperty("_slots");
            _fileNamePrefix = serializedObject.FindProperty("_fileNamePrefix");
            _take = serializedObject.FindProperty("_take");

            _list = new ReorderableList(serializedObject, _slots, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "Recording Slots"),
                elementHeightCallback = i => DrawOrMeasure(new Rect(0, 0, 0, 0), i, false),
                drawElementCallback = (r, i, active, focused) => DrawOrMeasure(r, i, true),
                onAddCallback = OnAdd,
            };

            EditorApplication.update += ThrottledRepaint;
        }

        void OnDisable()
        {
            EditorApplication.update -= ThrottledRepaint;
        }

        // 録画中の状態表示更新は ~10Hz に間引く（毎フレーム Repaint はフレームレートを削るため）。
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

            // 録画中は重いリスト描画をスキップし、ボタンと状態表示だけにする。
            if (Application.isPlaying && recorder.IsRecording)
            {
                DrawRecordButton(recorder);
                DrawStatus(recorder);
                return;
            }

            serializedObject.Update();

            // --- ファイル命名 ---
            EditorGUILayout.LabelField("File Naming", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_fileNamePrefix, new GUIContent("Name Prefix", "全ファイル名の先頭。<Take> でテイク番号を埋め込み"));
            EditorGUILayout.PropertyField(_take, new GUIContent("Take"));
            EditorGUILayout.LabelField(" ", "形式: (prefix)_(type)_(object)_(date)", EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            _list.DoLayoutList();

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

        // ---- スロット要素の描画/高さ計算（共通） --------------------------------

        static GUIStyle _badgeStyle;
        static GUIStyle BadgeStyle =>
            _badgeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };

        float DrawOrMeasure(Rect rect, int index, bool draw)
        {
            var el = _slots.GetArrayElementAtIndex(index);
            var typeProp = el.FindPropertyRelative("type");
            var labelProp = el.FindPropertyRelative("label");
            int t = typeProp.enumValueIndex;

            float line = EditorGUIUtility.singleLineHeight;
            float sp = 4f;        // 行間（詰まり過ぎ対策で広め）
            float padTop = 5f;
            float padBottom = 7f;
            float stripW = 3f;
            float gap = 16f;      // カラーラインと内容の間隔（三角との被り対策）

            float y = rect.y + padTop;
            float x = rect.x + stripW + gap;
            float w = rect.width - stripW - gap - 6f;

            if (draw)
            {
                var strip = new Rect(rect.x, rect.y + 1f, stripW, rect.height - 2f);
                EditorGUI.DrawRect(strip, TypeColor(t));
            }

            Rect Row(float h)
            {
                var r = new Rect(x, y, w, h);
                y += h + sp;
                return r;
            }

            // ヘッダ行: 折りたたみ三角 ＋ 名前(編集可) ＋ 種別バッジ
            {
                var r = Row(line);
                if (draw)
                {
                    var foldR = new Rect(r.x, r.y, 14f, r.height);
                    el.isExpanded = EditorGUI.Foldout(foldR, el.isExpanded, GUIContent.none, true);

                    const float badgeW = 74f;
                    var nameR = new Rect(r.x + 16f, r.y, r.width - 16f - badgeW - 4f, r.height);
                    EditorGUI.PropertyField(nameR, labelProp, GUIContent.none);

                    var badgeR = new Rect(r.xMax - badgeW, r.y, badgeW, r.height);
                    var prevC = GUI.color;
                    GUI.color = TypeColor(t);
                    EditorGUI.LabelField(badgeR, typeProp.enumDisplayNames[t], BadgeStyle);
                    GUI.color = prevC;
                }
            }

            // 折りたたみ時はヘッダだけ
            if (!el.isExpanded)
                return (y - rect.y) + padBottom - sp;

            void Prop(string name)
            {
                var p = el.FindPropertyRelative(name);
                float h = EditorGUI.GetPropertyHeight(p, true);
                var r = Row(h);
                if (draw) EditorGUI.PropertyField(r, p, true);
            }

            // Type
            {
                var r = Row(line);
                if (draw) EditorGUI.PropertyField(r, typeProp, new GUIContent("Type"));
            }

            // Output Directory + 参照
            {
                var r = Row(line);
                if (draw)
                {
                    var outProp = el.FindPropertyRelative("outputDirectory");
                    const float bw = 50f;
                    var f = new Rect(r.x, r.y, r.width - bw - 2f, r.height);
                    var b = new Rect(r.xMax - bw, r.y, bw, r.height);
                    EditorGUI.PropertyField(f, outProp, new GUIContent("Output Dir"));
                    if (GUI.Button(b, "参照…")) BrowseOutputDirectory(outProp);
                }
            }

            Prop("settings");

            if (t == (int)MS_Recorder.RecorderType.Character)
            {
                Prop("animator");
                Prop("hipBone");
                Prop("addBones");
                Prop("faceRenderers");
            }
            else if (t == (int)MS_Recorder.RecorderType.Transform)
            {
                Prop("transformTarget");
                Prop("space");
            }
            else if (t == (int)MS_Recorder.RecorderType.Camera)
            {
                Prop("cameraTarget");
                Prop("space");
            }
            else if (t == (int)MS_Recorder.RecorderType.Audio)
            {
                var r = Row(line);
                if (draw) DrawAudioDeviceRect(r, el.FindPropertyRelative("audioDevice"));
                Prop("audioSampleRate");
            }

            return (y - rect.y) + padBottom - sp;
        }

        static Color TypeColor(int type) => type switch
        {
            (int)MS_Recorder.RecorderType.Character => new Color(0.30f, 0.55f, 0.85f), // 青
            (int)MS_Recorder.RecorderType.Camera    => new Color(0.90f, 0.55f, 0.25f), // 橙
            (int)MS_Recorder.RecorderType.Transform => new Color(0.35f, 0.70f, 0.40f), // 緑
            (int)MS_Recorder.RecorderType.Audio     => new Color(0.70f, 0.40f, 0.78f), // 紫
            _ => Color.gray,
        };

        static void DrawAudioDeviceRect(Rect r, SerializedProperty deviceProp)
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                EditorGUI.LabelField(r, "Audio Device", "（入力デバイスなし）");
                return;
            }
            int cur = Mathf.Max(0, System.Array.IndexOf(devices, deviceProp.stringValue));
            int sel = EditorGUI.Popup(r, "Audio Device", cur, devices);
            if (sel >= 0 && sel < devices.Length)
                deviceProp.stringValue = devices[sel];
        }

        void OnAdd(ReorderableList list)
        {
            int idx = _slots.arraySize;
            _slots.InsertArrayElementAtIndex(idx);

            var added = _slots.GetArrayElementAtIndex(idx);
            added.isExpanded = true;
            added.FindPropertyRelative("label").stringValue = "";
            added.FindPropertyRelative("type").enumValueIndex = (int)MS_Recorder.RecorderType.Character;
            added.FindPropertyRelative("space").enumValueIndex = (int)MS_Recorder.RecordSpace.World;
            added.FindPropertyRelative("outputDirectory").stringValue = "";

            var st = added.FindPropertyRelative("settings");
            st.FindPropertyRelative("NominalFps").floatValue = 60f;
            st.FindPropertyRelative("ChunkBytes").intValue = 256 * 1024;
            st.FindPropertyRelative("PooledChunkCount").intValue = 4;

            added.FindPropertyRelative("animator").objectReferenceValue = null;
            added.FindPropertyRelative("hipBone").objectReferenceValue = null;
            added.FindPropertyRelative("addBones").ClearArray();
            added.FindPropertyRelative("faceRenderers").ClearArray();
            added.FindPropertyRelative("transformTarget").objectReferenceValue = null;
            added.FindPropertyRelative("cameraTarget").objectReferenceValue = null;
            added.FindPropertyRelative("audioDevice").stringValue = "";
            added.FindPropertyRelative("audioSampleRate").intValue = 48000;
        }

        void BrowseOutputDirectory(SerializedProperty outputDirProp)
        {
            // プロジェクトのルート (Assets の親) を起点に開く。
            string projectRoot = Path.GetDirectoryName(Application.dataPath);

            string selected = EditorUtility.OpenFolderPanel("録画の出力先フォルダを選択", projectRoot, "");
            if (string.IsNullOrEmpty(selected))
                return;

            outputDirProp.stringValue = selected;
            serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
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
                    // ファイル名の拡張子（.msrm/.msrf/.msrt/.msrc/.msra）が種別を表す。
                    string file = string.IsNullOrEmpty(s.FilePath) ? "-" : Path.GetFileName(s.FilePath);
                    string state = s.Faulted ? "  (エラー)" : "";

                    string detail;
                    if (s is AudioRecorderSession a)
                    {
                        // 音声は frames がサンプル数で桁違いになるため「秒@Hz」で表示。
                        float secs = a.SampleRate > 0 ? a.FrameCount / (float)a.SampleRate : 0f;
                        detail = $"{secs:F1}s @{a.SampleRate}Hz, peak {a.Peak:F3}";
                    }
                    else
                    {
                        detail = $"{s.FrameCount} frames";
                    }

                    EditorGUILayout.LabelField(file, detail + state);
                }
            }
        }
    }
}
