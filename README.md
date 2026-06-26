# MudShip Recorder

モーション（スケルトンの Transform）と表情（BlendShape）を **低負荷・GC フリー** で記録する
マルチモーダル・レコーダー。録画中はバイナリのダンプのみを行い、メインプロジェクトの
フレームレートに寄生しない設計。`.msrm`/`.msrf → .anim` 変換ツールを同梱。将来的に Camera /
Transform / DMX など他ストリームの記録にも拡張予定。

- Package id: `com.mudship.recorder`
- マスターコンポーネント: `MS_Recorder`（録画スロットと開始/停止ボタン）

## 設計の要点

- **ランタイムは生バイナリ（`.msrm` / `.msrf`）を書くだけ**。`.anim` 等への変換は一切しない（重い処理・Editor 専用 API・GC を録画経路から排除）。再生や `.anim` 化はオフラインの後処理に分離する。
- **GC フリー**：毎フレームは事前確保した `byte[]` チャンクへコピーするだけ。満杯チャンクをバックグラウンド IO スレッドへ渡し、チャンクはプールで使い回す（`ChunkedStreamWriter` を全ストリームで共用）。→ 録画時間が伸びても GC スキャンコストが増えず、フレームレートが劣化しない。
- **共通タイムコード同期**：`MS_Recorder` が録画開始時に基準 `startTime` を 1 つ確定し、全ストリームへ同じ `Time.timeAsDouble - startTime` を timestamp として書く。ファイルを跨いだフレーム対応が取れる。
- **モーション記録値**：root（Animator の Transform）以下を決定的順序で全走査した全ボーンの `localRotation` ＋ 指定ボーンの `localPosition` ＋ `timestamp(double)`。位置記録ボーンは既定で Humanoid の Hips を自動採用（root ボーンを勝手に拾うことはしない）。
- **表情記録値**：指定 `SkinnedMeshRenderer` 群の全 BlendShape ウェイト ＋ `timestamp(double)`。

> Transform / SMR 直記録のため **リターゲット不可**（記録時と同じ root 相対パス構造のリグでのみ再生可能）。

## 構成

- **`MS_RecorderSettings`（ScriptableObject）= 再利用可能な設定プロファイル**
  - `Type`（`Character` / `Camera` / `Transform`。Camera・Transform は枠のみ・未実装）
  - `Output Directory`（参照…ボタン付き） / `Nominal Fps` / `Chunk Bytes` / `Pooled Chunk Count`
  - シーン参照は持たない（SO アセットはシーン参照を保存できないため）。
- **`MS_Recorder`（シーンの MonoBehaviour）= スロットのリスト**
  - 各スロット ＝ プロファイル（SO 参照）＋シーン配線。`Character` のとき次を表示：
    - `Animator` … 記録対象
    - `Hip Bone` … `localPosition` を記録する腰ボーン（空なら Humanoid の Hips を自動採用）
    - `Add Bones` … 腰に加えて位置も記録する追加ボーン（ツイスト等。回転は全ボーンで記録）
    - `Face Renderers` … 表情を記録する `SkinnedMeshRenderer` 群（**Animator の GameObject 配下**にあること）

`Character` スロットは 1 つのプロファイルで `.msrm`（モーション）＋ `.msrf`（表情）を同時に出力する。

## 使い方（コンポーネント）

1. **プロファイルを作成**：Project で右クリック ▸ **Create ▸ MudShip ▸ Recorder Settings**。`Type = Character`、出力先・fps 等を設定。
2. シーンの GameObject に **MudShip ▸ MS Recorder**（`MS_Recorder`）を追加。
3. **スロットを追加**し、`Settings (Profile)` に 1. の SO を割り当て、`Animator`（必要なら `Hip Bone` / `Add Bones` / `Face Renderers`）を設定。
4. **プレイモードに入り**、Inspector の「● 録画開始」→「■ 停止」。
5. `<キャラ名>_<stamp>.msrm` /（表情指定時）`.msrf` が出力先（既定 `persistentDataPath/MudShipRecordings`）に保存される。出力先がプロジェクト（Assets 配下）なら停止時に自動で `AssetDatabase.Refresh()` され取り込まれる。

## 使い方（コードから）

```csharp
using MudShip.MotionRecorder;

// モーション
var skeleton = SkeletonDefinition.FromAnimator(animator);              // 腰は Humanoid の Hips を自動
var motion = new MotionRecorderSession(skeleton, RecorderSettings.Default);
motion.Start(motionPath);

// 表情
var face = FaceDefinition.FromRenderers(animator.transform, faceRenderers);
var facial = new FaceRecorderSession(face, RecorderSettings.Default);
facial.Start(facePath);

// 毎 LateUpdate（同じ timestamp で同期）:
double t = Time.timeAsDouble - startTime;
motion.CaptureFrame(t);
facial.CaptureFrame(t);

// 終了:
motion.Stop(); motion.Dispose();
facial.Stop(); facial.Dispose();
```

## .msrm / .msrf → .anim 変換（Editor）

録画したファイルを `AnimationClip` (.anim) に変換できる（オフライン後処理）。
全フレームにキーを打つ**ロスレス**変換で、各フレーム時刻の値は元データと完全一致する。

- Assets 内の `.msrm` / `.msrf` を**右クリック ▸ MudShip ▸ Convert recording to .anim**。
  - `.msrm` → Transform パスの `localRotation` / `localPosition` カーブ。
  - `.msrf` → SkinnedMeshRenderer パスの `blendShape.<名前>` カーブ。

記録時と同じ root 相対パス構造のリグでのみ正しく再生される（リターゲット不可）。尺が長いと
キー数が多く重くなる点に注意（軽量化＝キーフレーム削減は今後対応）。

## 公開 API

| 型 | 役割 |
|---|---|
| `MS_Recorder` | マスターコンポーネント。録画スロット（プロファイル＋シーン配線）リスト＋録画ボタン。 |
| `MS_RecorderSettings` | 録画プロファイル（ScriptableObject）。type と Settings を保持。 |
| `MotionRecorderSession` | 1 スケルトン → 1 `.msrm` の記録エンジン（MonoBehaviour 非依存）。 |
| `FaceRecorderSession` | SMR 群 → 1 `.msrf` の記録エンジン（MonoBehaviour 非依存）。 |
| `SkeletonDefinition` | root 全走査によるボーン列・パス・位置記録ボーンの定義。 |
| `FaceDefinition` | SMR 群の列挙・root 相対パス・BlendShape 名表の定義。 |
| `RecorderSettings` | fps・チャンクサイズ・プール数の設定。 |
| `MsrmFormat` / `MsrfFormat` | `.msrm` / `.msrf` フォーマット定数とレイアウト仕様。 |

## ファイル形式

`msr` = MudShip Recording、末尾 1 文字 = ストリーム種別（**m** = Motion / **f** = Facial）。
リトルエンディアン、固定ストライド。詳細は `MsrmFormat.cs` / `MsrfFormat.cs` の doc コメント参照。

```
.msrm  [Header] magic"MSRM" / version u16 / flags u32 / nominalFps f32 /
                boneCount u16 / posBoneCount u16 / frameCount u32 / posIndices u16[posBoneCount]
       [BoneTable] (boneCount) pathLen u16 + path utf8     ※root 相対パス
       [Frames] timestamp f64 / positions (f32x3)xposBoneCount / rotations (f32x4)xboneCount

.msrf  [Header] magic"MSRF" / version u16 / flags u32 / nominalFps f32 /
                rendererCount u16 / totalShapeCount u32 / frameCount u32
       [RendererTable] (rendererCount) pathLen u16 + path utf8 / shapeCount u16 / (nameLen u16 + name utf8)
       [Frames] timestamp f64 / weights (f32)xtotalShapeCount
```

詳細な設計は [`Documentation~/recorder-architecture.md`](Documentation~/recorder-architecture.md) を参照。

## 今後の予定

- `Camera` / `Transform` ストリームの実装（dropdown は先行追加済み）
- DMX ストリーム
- `.msrm` / `.msrf` 直再生器（Transform / SMR へ直接適用）
- `.anim` のキーフレーム削減（段2・誤差許容つき軽量化）
