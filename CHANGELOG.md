# Changelog

All notable changes to this package will be documented in this file.

## [0.3.0] - 2026-06-26

### Fixed
- **位置記録ボーンの誤選択を修正**。Humanoid 以外（Generic リグ等）で `localPosition` の記録対象が
  腰ではなく走査先頭の root 系ボーンになり、ほぼ定数の無意味なデータが記録される不具合を修正。
  root ボーンを勝手に拾うフォールバックを撤去し、解決できない場合は位置を記録せず警告するようにした。

### Added
- `MotionRecorderBehaviour.Target`：記録対象を **Animator ＋ 位置記録ボーン（任意）** のペアで指定可能に。
  Generic リグや腰以外を狙う場合は Inspector の **Position Bones** に対象 Transform を明示指定する。
  空なら Humanoid の Hips を自動採用（従来どおり）。
- `SkeletonDefinition.FromAnimator(Animator, IReadOnlyList<Transform>, bool)` オーバーロード
  （位置記録ボーンの明示指定版）。

### Changed
- **（破壊的）** `MotionRecorderBehaviour` の `_targets` を `List<Animator>` → `List<Target>` に変更。
  公開プロパティ `Targets` の型も `IList<Animator>` → `IList<Target>` に変更。
  既存シーンの Targets 割り当ては失われるため、Animator を再アサインすること。

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
