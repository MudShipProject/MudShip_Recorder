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

- **`MS_Recorder`（シーンの MonoBehaviour）= スロットのリスト**。設定はすべてこのコンポーネント＝シーンに保存する（ScriptableObject は使わない）。各スロット共通：
  - `Type`（`Character` / `Transform` / `Camera`）
  - `Output Directory`（参照…ボタン付き） / `Settings`（`Nominal Fps` / `Chunk Bytes` / `Pooled Chunk Count`）
- **`Character`**：1 スロットで `.msrm`（モーション）＋ `.msrf`（表情）を同時出力。
  - `Animator` … 記録対象
  - `Hip Bone` … `localPosition` を記録する腰ボーン（空なら Humanoid の Hips を自動採用）
  - `Add Bones` … 腰に加えて位置も記録する追加ボーン（ツイスト等。回転は全ボーンで記録）
  - `Face Renderers` … 表情を記録する `SkinnedMeshRenderer` 群（**Animator の GameObject 配下**にあること）
- **`Transform`**：`Transform Target` の Pos / Rot / Scale を記録（`.msrt`）。
- **`Camera`**：`Camera Target` の Transform（Pos/Rot/Scale）＋ `fieldOfView` を記録（`.msrc`）。
- Transform / Camera は **`Record Space`** で記録空間を選択：
  - `World`（既定）… `position` / `rotation` / `lossyScale`。**親（リグ/ドリー）の下にあるカメラ等はこちら**。`.anim` は親なしオブジェクトに適用する前提。
  - `Local` … `localPosition` / `localRotation` / `localScale`。同じ親構造に適用する用途向け。
- **`Audio`**：`Audio Device`（PC の入力デバイス）を選んで PCM を記録（`.msra`、16-bit）。`Sample Rate` で軽量化も可。
  - Unity が録れるのは**入力デバイス**（マイク／ライン／オーディオ I/F）のみ。**PC の再生音（システム出力）を録るには仮想オーディオケーブル（VB-Audio Cable 等）**が必要（入れると入力デバイスとして選べる）。

## 使い方（コンポーネント）

1. シーンの GameObject に **MudShip ▸ MS Recorder**（`MS_Recorder`）を追加。
2. （任意）**Name Prefix** に全ファイル共通の接頭辞を入力（`<Take>` でテイク番号を 3 桁ゼロ詰め展開）、**Take** にテイク番号。
3. **スロットを追加**し、`Type` を選んで対象（Character なら `Animator` 等、Camera/Transform なら Target、Audio なら Device）を設定。
4. **プレイモードに入り**、Inspector の「● 録画開始」→「■ 停止」。
5. **`(prefix)_(type)_(object)_(date).<ext>`** 形式で出力先（既定 `persistentDataPath/MudShipRecordings`）に保存される。例: `Shoot003_Camera_MainCamera_20260627_153012.msrc`。Character は **`Motion`（.msrm）と `Facial`（.msrf）** に type が分かれる。出力先がプロジェクト（Assets 配下）なら停止時に自動 `AssetDatabase.Refresh()`。

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

## → .anim 変換（Editor）

録画したファイルを `AnimationClip` (.anim) に変換できる（オフライン後処理）。
全フレームにキーを打つ**ロスレス**変換で、各フレーム時刻の値は元データと完全一致する。

- Assets 内の `.msrm` / `.msrf` / `.msrt` / `.msrc` / `.msra` を**右クリック ▸ MudShip ▸ Convert recording**（**複数選択可**。元ファイルと同じフォルダに出力）。
  - `.msrm` → Transform パスの `localRotation` / `localPosition` カーブ（.anim）。
  - `.msrf` → SkinnedMeshRenderer パスの `blendShape.<名前>` カーブ（`_face` 付きで出力）。
  - `.msrt` → 対象自身（path 空）の `localPosition` / `localRotation` / `localScale` カーブ。
  - `.msrc` → 上記に加え `Camera` の `field of view` カーブ。
  - `.msra` → **`.wav`**（音声は AnimationClip ではなく WAV を書き出す）。

記録時と同じ構造（Character は root 相対パス、Transform/Camera は対象 GameObject 自身）に適用する前提
（リターゲット不可）。尺が長いとキー数が多く重くなる点に注意（軽量化＝キーフレーム削減は今後対応）。

## 公開 API

| 型 | 役割 |
|---|---|
| `MS_Recorder` | マスターコンポーネント。録画スロット（種別・出力先・Settings・シーン配線）リスト＋録画ボタン。 |
| `IRecorderSession` | 全セッション共通インターフェイス（`MS_Recorder` はこの形で駆動）。 |
| `MotionRecorderSession` | 1 スケルトン → 1 `.msrm` の記録エンジン。 |
| `FaceRecorderSession` | SMR 群 → 1 `.msrf` の記録エンジン。 |
| `TransformRecorderSession` | 1 Transform → 1 `.msrt`（Pos/Rot/Scale）。 |
| `CameraRecorderSession` | 1 Camera → 1 `.msrc`（Pos/Rot/Scale＋FOV）。 |
| `AudioRecorderSession` | 入力デバイス → 1 `.msra`（PCM 16-bit）。 |
| `SkeletonDefinition` / `FaceDefinition` | モーション／表情の記録対象定義。 |
| `RecorderSettings` | fps・チャンクサイズ・プール数の設定。 |
| `MsrmFormat` / `MsrfFormat` / `MsrtFormat` / `MsrcFormat` / `MsraFormat` | 各フォーマット定数とレイアウト仕様。 |

## ファイル形式

`msr` = MudShip Recording、末尾 1 文字 = ストリーム種別（**m** = Motion / **f** = Facial / **t** = Transform / **c** = Camera / **a** = Audio）。
リトルエンディアン、固定ストライド。詳細は各 `Msr*Format.cs` の doc コメント参照。

```
.msrm  [Header] magic"MSRM" / version u16 / flags u32 / nominalFps f32 /
                boneCount u16 / posBoneCount u16 / frameCount u32 / posIndices u16[posBoneCount]
       [BoneTable] (boneCount) pathLen u16 + path utf8     ※root 相対パス
       [Frames] timestamp f64 / positions (f32x3)xposBoneCount / rotations (f32x4)xboneCount

.msrf  [Header] magic"MSRF" / version u16 / flags u32 / nominalFps f32 /
                rendererCount u16 / totalShapeCount u32 / frameCount u32
       [RendererTable] (rendererCount) pathLen u16 + path utf8 / shapeCount u16 / (nameLen u16 + name utf8)
       [Frames] timestamp f64 / weights (f32)xtotalShapeCount

.msrt  [Header] magic"MSRT" / version u16 / flags u32 / nominalFps f32 / frameCount u32
       [Frames] timestamp f64 / pos (f32x3) / rot (f32x4) / scale (f32x3)

.msrc  [Header] magic"MSRC" / version u16 / flags u32 / nominalFps f32 / frameCount u32
       [Frames] timestamp f64 / pos (f32x3) / rot (f32x4) / scale (f32x3) / fov f32

.msra  [Header] magic"MSRA" / version u16 / flags u32 / sampleRate u32 / channels u16 /
                bitsPerSample u16 / startOffset f64 / sampleFrameCount u32
       [Data]   PCM int16 interleaved（× sampleFrameCount × channels）
```

詳細な設計は [`Documentation~/recorder-architecture.md`](Documentation~/recorder-architecture.md) を参照。

## 今後の予定

- DMX ストリーム
- 直再生器（記録した各ストリームを Transform / SMR / Camera へ直接適用）
- `.anim` のキーフレーム削減（段2・誤差許容つき軽量化）
