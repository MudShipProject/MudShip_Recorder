# Changelog

All notable changes to this package will be documented in this file.

## [0.3.0] - 2026-06-26

### Fixed
- **位置記録ボーンの誤選択を修正**。Humanoid 以外（Generic リグ等）で `localPosition` の記録対象が
  腰ではなく走査先頭の root 系ボーンになり、ほぼ定数の無意味なデータが記録される不具合を修正。
  root ボーンを勝手に拾うフォールバックを撤去し、解決できない場合は位置を記録せず警告するようにした。

### Added
- `MotionRecorderBehaviour.Target`：記録対象を **Animator ＋ Hip Bone ＋ Position Bones** で指定可能に。
  - **Hip Bone**：localPosition を記録する腰ボーン。空なら Humanoid の Hips を自動採用。
  - **Position Bones**：腰に加えて位置も記録する追加ボーン（ツイストボーン等）。
- `SkeletonDefinition.FromAnimator(Animator, Transform hipBone, IReadOnlyList<Transform> extraPositionBones, bool)`
  オーバーロード（腰＋追加位置ボーンの明示指定版）。
- 録画停止時に `AssetDatabase.Refresh()`（Editor のみ）。出力先が Assets 配下なら `.msrc` が自動で取り込まれる。

### Changed
- **（破壊的）** `MotionRecorderBehaviour` の `_targets` を `List<Animator>` → `List<Target>` に変更。
  公開プロパティ `Targets` の型も `IList<Animator>` → `IList<Target>` に変更。
  既存シーンの Targets 割り当ては失われるため、Animator を再アサインすること。
- `.msrc → .anim` の右クリックメニューを **Assets ▸ MudShip Recorder ▸ …** → **Assets ▸ MudShip ▸ …** に変更。
- 出力先フォルダの「参照…」ダイアログの起点をプロジェクトルート（Assets の親）に変更。

### Removed
- `.msrc → .anim` の **Tools ▸ MudShip Recorder** メニュー（ファイル選択変換）。右クリック変換に一本化。

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
