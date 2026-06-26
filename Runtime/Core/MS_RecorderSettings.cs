using System.IO;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 録画プロファイル (ScriptableObject)。type と共通 Settings を保持する再利用可能なアセット。
    /// Unity を閉じても保持される。シーン参照は持たない (それは <see cref="MS_Recorder"/> のスロット側)。
    /// </summary>
    [CreateAssetMenu(menuName = "MudShip/Recorder Settings", fileName = "MS_RecorderSettings")]
    public class MS_RecorderSettings : ScriptableObject
    {
        /// <summary>記録の種別。Camera / Transform は枠のみ (機能は後日)。</summary>
        public enum RecorderType
        {
            Character,
            Camera,
            Transform,
        }

        [Tooltip("記録の種別。Character はモーション＋表情。Camera / Transform は未実装 (枠のみ)。")]
        [SerializeField] RecorderType _type = RecorderType.Character;

        [Tooltip("出力先フォルダ。空なら persistentDataPath/MudShipRecordings を使う。")]
        [SerializeField] string _outputDirectory = "";

        [SerializeField] RecorderSettings _settings = RecorderSettings.Default;

        /// <summary>記録の種別。</summary>
        public RecorderType Type => _type;

        /// <summary>fps・チャンク・プールの設定。</summary>
        public RecorderSettings Settings => _settings;

        /// <summary>設定された出力先 (未解決の生値。空なら既定を使う)。</summary>
        public string OutputDirectory => _outputDirectory;

        /// <summary>実際の出力先フォルダを解決する。</summary>
        public string ResolveOutputDirectory()
            => string.IsNullOrEmpty(_outputDirectory)
                ? Path.Combine(Application.persistentDataPath, "MudShipRecordings")
                : _outputDirectory;
    }
}
