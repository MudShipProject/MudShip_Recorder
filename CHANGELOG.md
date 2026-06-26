# Changelog

All notable changes to this package will be documented in this file.

## [0.4.0] - 2026-06-26

録画系を `MS_Recorder`（マスター）へ再設計し、Motion / Facial / Transform / Camera の各ストリームを追加。
設定はすべて `MS_Recorder` のスロット（シーン）に保持する。詳細設計は `Documentation~/recorder-architecture.md`。

### Added
- **`MS_Recorder`（MonoBehaviour）**：録画スロットのリスト＋録画ボタン。各スロットに種別
  （`Type` = Character/Transform/Camera）・出力先・Settings（fps/chunk/pool）・シーン配線をすべて持つ
  （設定はシーンに保存。ScriptableObject は使わない）。録画開始時の共通 `startTime` で全ストリームを同期。
- **表情記録**：`MsrfFormat`（`.msrf`）/ `FaceDefinition` / `FaceRecorderSession`。
  指定 `SkinnedMeshRenderer` 群の全 BlendShape ウェイトを GC フリーで記録。
- **Transform 記録**：`MsrtFormat`（`.msrt`）/ `TransformRecorderSession`。対象 Transform の Pos/Rot/Scale。
- **Camera 記録**：`MsrcFormat`（`.msrc`）/ `CameraRecorderSession`。Transform に加えて `fieldOfView`。
- `IRecorderSession`：全セッション共通インターフェイス。`MS_Recorder` は種別を問わず統一して駆動する。
- `ChunkedStreamWriter`：形式非依存の汎用チャンクライタ（旧 `MsrcStreamWriter` を抽出・共通化）。
- `.msrf`/`.msrt`/`.msrc → .anim` 変換（表情は `blendShape.<名前>`、Transform/Camera は対象自身の TRS、Camera は `field of view` も）。

### Changed
- **（破壊的）モーションの拡張子・形式を改称**：旧モーション `.msrc`/`MSRC`/`MsrcFormat` → `.msrm`/`MSRM`/`MsrmFormat`（m = Motion）。
  体系：`msr` ＝ MudShip Recording、末尾 1 文字 ＝ ストリーム種別（**m**=Motion / **f**=Facial / **t**=Transform / **c**=Camera）。
  ※ `.msrc`/`MSRC`/`MsrcFormat` の名前は新たに **Camera** ストリームへ転用（旧モーションの `.msrc` とは非互換）。
- **（破壊的）`MotionRecorderBehaviour` を廃止**し `MS_Recorder` に統合（記録エンジンは再利用部品として存続）。
  録画設定はシーンの Targets からスロットへ再編。既存シーンは作り直しになる。
- `.anim` 変換メニューを **Assets ▸ MudShip ▸ Convert recording to .anim**（`.msrm`/`.msrf`/`.msrt`/`.msrc` 対応）に統合。
  **複数選択をまとめて変換可能**にし、保存先ポップアップを廃止（元ファイルと同じフォルダへ出力。表情は `_face` 付与）。
- `MS_Recorder` インスペクタ UI を整理（スロットをカード／折りたたみ表示、種別ごとのフィールド出し分け、余白調整）。
- 録画中はインスペクタのスロット描画をスキップし、状態表示の再描画を ~10Hz に間引き（録画開始時のフレームレート低下を解消）。

### Removed
- 旧モーション `.msrc` 形式。旧 `.msrc`（モーション）ファイルは新コンバータでは読めない（現在の `.msrc` は Camera 用）。

## [0.3.0] - 2026-06-26

### Fixed
- **位置記録ボーンの誤選択を修正**。Humanoid 以外（Generic リグ等）で `localPosition` の記録対象が
  腰ではなく走査先頭の root 系ボーンになり、ほぼ定数の無意味なデータが記録される不具合を修正。
  root ボーンを勝手に拾うフォールバックを撤去し、解決できない場合は位置を記録せず警告するようにした。

### Added
- `MotionRecorderBehaviour.Target`：記録対象を **Animator ＋ Hip Bone ＋ Add Bones** で指定可能に。
  - **Hip Bone**：localPosition を記録する腰ボーン。空なら Humanoid の Hips を自動採用。
  - **Add Bones**：腰に加えて localPosition も記録する追加ボーン（ツイストボーン等）。localRotation は全ボーンで記録される。
- `SkeletonDefinition.FromAnimator(Animator, Transform hipBone, IReadOnlyList<Transform> addBones)`
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
- `MotionRecorderBehaviour` の **Include Root** オプション（`_includeRoot`）。
  `SkeletonDefinition.FromHierarchy` / `FromAnimator` の `includeRoot` 引数も廃止（root 自身は常に記録対象外）。

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
