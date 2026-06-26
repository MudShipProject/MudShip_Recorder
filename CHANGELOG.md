# Changelog

All notable changes to this package will be documented in this file.

## [0.2.0] - 2026-06-26

### Added
- `.msrc → .anim` 変換ツール（Editor）`MsrcAnimConverter`。
  - メニュー **Tools ▸ MudShip Recorder ▸ Convert .msrc to .anim…**（ファイル選択）。
  - Assets 内 `.msrc` の右クリック変換。
  - 全フレームキー＋Linear tangent のロスレス変換。`Keyframe[]` 一括生成で重さを抑制。
- `MotionRecorderBehaviour` の出力先フォルダを Inspector の「参照…」ボタン（フォルダ選択ダイアログ）で設定可能に。

### Changed
- パッケージ名を **MudShip Recorder** に変更（id: `com.mudship.motion-recorder` → `com.mudship.recorder`）。モーション以外のストリームも扱う前提のため。

## [0.1.0] - 2026-06-26

### Added
- 初版。ランタイム `.msrc` レコーダー。
  - `SkeletonDefinition`：root ベース全走査によるボーン列挙・root 相対パス生成。
  - `MsrcStreamWriter`：チャンク方式（ダブルバッファ＋IO スレッド）の GC フリー書き込み。
  - `MotionRecorderSession`：1 スケルトン → 1 `.msrc` の記録エンジン（MonoBehaviour 非依存）。
  - `MotionRecorderBehaviour`：Animator リストを持つシーン配置用コンポーネント。
  - カスタムインスペクタ：プレイモード中の録画開始/停止ボタンと状態表示。
