# MudShip Recorder アーキテクチャ設計メモ

> ステータス: 確定版ドラフト（実装前）。本書は「録画系の再設計（MS_Recorder 化）＋表情ストリーム追加」の合意内容をまとめたもの。

## 0. 目的・前提

- Facial / Motion（将来は Camera / Transform / DMX）を記録する**マルチモーダル・レコーダー**。
- 最重要制約は不変：**録画がメインプロジェクトの FPS を落とさない**（毎フレーム・ゼロアロケーション、長時間でもメモリ肥大なし）。GC フリーのチャンク書き込み機構を全ストリームで共用する。
- ランタイムは生バイナリ（`.msrm` / `.msrf` …）を書くだけ。`.anim` 変換等の重い後処理は Editor 側オフラインに分離（既存方針を踏襲）。

## 1. 全体構成

### MS_Recorder（シーンの MonoBehaviour・マスター）
- **スロットのリスト**を保持。設定はすべてこのコンポーネント＝シーンに保存する（ScriptableObject は使わない）。
- 各スロットが持つもの：
  - **type ドロップダウン**：`Character` / `Camera` / `Transform`（Camera / Transform は枠だけ・機能は後日）
  - `Output Directory`（＋参照…ボタン）/ `Settings`（`Nominal Fps` / `Chunk Bytes` / `Pooled Chunk Count`）
  - `Character` のときのシーン配線：`Animator` / `Hip Bone` / `Add Bones` / `Face Renderers`
- **録画開始/停止ボタンを Inspector に集約**（現 `MotionRecorderBehaviour` から移行）。
- Play 中、録画開始時に**基準 `startTime` を 1 つ確定**し、全スロットの全ストリームへ同じ `Time.timeAsDouble - startTime` を渡す＝**共通タイムコード同期**の土台。

> 設計の経緯：当初は設定を再利用可能な ScriptableObject（MS_RecorderSettings）に分離する案 A を採用したが、
> 後に**SO を廃止し、全設定を MS_Recorder のスロット（シーン）に直接持つ案 B に変更**した。
> シーン参照（Animator 等）と設定が 1 か所にまとまり、Unity 終了後もシーンに永続する。

## 2. データの置き場所（案B・確定）

すべて **MS_Recorder のスロット（シーン）** に持つ。

| 項目 | 置き場所 |
|---|---|
| type / Output Directory / Fps / Chunk / Pool | **MS_Recorder のスロット（シーン）** |
| Animator / Hip Bone / Add Bones / SkinnedMeshRenderer 群 | **MS_Recorder のスロット（シーン）** |

- スロット単位でキャラ・種別・出力先・Settings を独立に設定できる。出力先をキャラ別にしたいなら各スロットの Output Directory を変えるだけ。
- ScriptableObject を使わないので、シーン参照も含めシーンファイルに一括保存される（Unity 終了後も保持）。

## 3. タイプ：Character（モーション＋表情）

1 つの Character スロットが、1 つの SO（fps/chunk/dir）で **`.msrm`（モーション）＋ `.msrf`（表情）を同時に**出力する。
→ そのキャラの motion と face は自動的に同じ fps・同じ出力先・同じ timestamp 基準になり、同期が一貫する。

### スロットに表示されるシーン参照フィールド
- **Animator**（記録対象。Transform 以下を全走査して `localRotation` を記録）
- **Hip Bone**（`localPosition` を記録する腰ボーン。空なら Humanoid の Hips を自動採用）
- **Add Bones**（腰に加えて `localPosition` も記録する追加ボーン。回転は全ボーン共通で記録）
- **Face Renderers**（`List<SkinnedMeshRenderer>`。指定した各 SMR の**全 BlendShape を記録**）

### 制約
- **Face Renderers は Animator root（＝Animator が付く GameObject）の配下にあること**。
  `.msrf` には SMR のパスを「キャラ root 相対」で記録するため。root 外の SMR は解決できないので弾く＋警告。
- 同名キャラのファイル衝突は、衝突時にスロット index を付与して回避（`<名>_<stamp>_2`）。

## 4. ファイル命名・拡張子の体系

- **`msr` = MudShip Recording**、**末尾 1 文字 = ストリーム種別**。
  - `.msrm`（magic `MSRM`）… **m = Motion**（スケルトン。旧 `.msrc` から改称）
  - `.msrf`（magic `MSRF`）… **f = Facial**（新規）
  - 将来：Camera / Transform / DMX も `msr` ＋種別文字で拡張。
- 出力名：`<キャラ名>_<yyyyMMdd_HHmmss>.msrm` / `.msrf`（衝突時 `_<index>` 付与）。

## 5. `.msrf` フォーマット（表情・新規）

リトルエンディアン、固定ストライド（`.msrm` と同じ思想）。

```
[Header]
  magic           char[4]  "MSRF"
  version         uint16
  flags           uint32   bit0: hasTimestamp
  nominalFps      float32
  rendererCount   uint16
  totalShapeCount uint32   全 renderer の BlendShape 総数（= 1 フレームの weight 数）
  frameCount      uint32   停止時に確定（記録中は 0、Finish で書き戻し）
[RendererTable]  (rendererCount 回)
  pathLen         uint16
  path            utf8[pathLen]   キャラ root 相対パス（root 名は含めない）
  shapeCount      uint16
  shapes          (nameLen uint16 + name utf8) × shapeCount
[Frames]  (frameCount 回, 固定ストライド)
  timestamp       float64        開始からの秒
  weights         float32 × totalShapeCount   renderer 順 → 各 renderer 内 shape 順
```
- `stride = 8 + totalShapeCount * 4` バイト。
- weight は Unity の BlendShape ウェイト（0..100、範囲外も保持）。
- 取得は `SkinnedMeshRenderer.GetBlendShapeWeight(i)`（float 返し・非アロケーション）を LateUpdate で memcpy するのみ。

## 6. 同期モデル

- MS_Recorder が録画開始時に基準 `startTime`（`Time.timeAsDouble`）を 1 つ確定。
- 全スロット・全ストリーム（`.msrm`/`.msrf`）の各フレームに同じ `Time.timeAsDouble - startTime` を timestamp として書く。
- ファイルを跨いだフレーム対応は timestamp で取る（nominalFps はヘッダのメタ情報で、実時刻は timestamp が正）。

## 7. ランタイムのクラス構成

- 既存（再利用部品として残す）
  - `MsrmFormat` … `.msrm` 形式定数。
  - `SkeletonDefinition` … root 全走査でボーン列・パス・位置記録ボーンを定義。
  - `ChunkedStreamWriter`（internal）… GC フリー・チャンク書き込み本体。
  - `MotionRecorderSession`（public）… 1 スケルトン → 1 `.msrm`。
- 追加
  - `MsrfFormat` … `.msrf` 形式定数。
  - `FaceDefinition` … 指定 SMR 群を列挙し、root 相対パス・BlendShape 名表を定義（`SkeletonDefinition` に対応）。
  - `FaceRecorderSession`（public）… SMR 群 → 1 `.msrf`（`MotionRecorderSession` に対応）。
  - **チャンク書き込みの共通化**：`ChunkedStreamWriter` のチャンク機構（ダブルバッファ＋IO スレッド＋プール、frameCount 後追いパッチ）を、ヘッダ/ストライドに依存しない汎用ライタへ抽出して両形式で共用する。
- 置換
  - `MotionRecorderBehaviour`（コンポーネント）は**廃止**し、`MS_Recorder` に統合。記録エンジン（Session/Writer/Definition）は残す。

## 8. Editor 要件

- **`MS_Recorder` のカスタムエディタ**
  - スロットをカード（折りたたみ）表示。各スロットで type に応じてフィールドを出し分け（Character なら Animator/Hip Bone/Add Bones/Face Renderers）。
  - Output Directory は「参照…」ボタンで**プロジェクトルート起点**のフォルダ選択。Settings（fps/chunk/pool）もスロット内に表示。
  - Play 中は録画開始/停止ボタン＋各セッションの状態（フレーム数・ファイル名）を表示。録画中はスロット描画をスキップし、再描画を ~10Hz に間引いてフレームレート低下を防ぐ。
- 録画停止時に `AssetDatabase.Refresh()`（Editor のみ）。出力が Assets 配下なら `.msrm`/`.msrf` を自動取り込み。
- `.msrf → .anim` 変換：SMR パス＋property `blendShape.<名前>` のカーブにロスレス変換（`MsrAnimConverter` と同型。右クリック `Assets ▸ MudShip ▸ …`）。

## 9. 破壊的変更・移行

- 録画ボタンが `MotionRecorderBehaviour` → `MS_Recorder` へ移動。
- `MotionRecorderBehaviour` の `Targets`（複数キャラ）→ `MS_Recorder` のスロット（1 キャラ = 1 スロット、設定もスロット内）に再編。
- 既存シーンの録画設定は作り直しになる。

## 10. 実装フェーズ（案）

1. `.msrf` 形式・`MsrfFormat`、チャンクライタの共通化（汎用ライタ抽出）。
2. `FaceDefinition` / `FaceRecorderSession`（表情記録エンジン）。
3. `MS_Recorder`（スロットに type＋出力先＋Settings＋シーン配線を保持、スロット駆動・共通 startTime・LateUpdate 配信）＋ `MotionRecorderBehaviour` 廃止。
4. カスタムエディタ（MS_Recorder：カード表示・録画ボタン・状態表示・出力先参照）。
6. `.msrf → .anim` 変換。
7. README / CHANGELOG 更新。

## 11. 将来

- Camera / Transform ストリームの実装（dropdown は先行追加済み）。
- DMX ストリーム。
- 全ストリーム共通タイムコード同期の高度化。
- `.msrm` / `.msrf` 直再生器、`.anim` キーフレーム削減（段2）。
