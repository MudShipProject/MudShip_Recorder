# MudShip Recorder

ヒューマノイド／スケルトンの Transform を **低負荷・GC フリー** で記録する `.msrc` レコーダーと、
`.msrc → .anim` 変換ツール。録画中はバイナリのダンプのみを行い、メインプロジェクトの
フレームレートに寄生しない設計。将来的に Facial / DMX など他ストリームの記録にも拡張予定。

- Package id: `com.mudship.recorder`
- 主な録画モジュール: `MotionRecorderBehaviour`（モーション）

## 設計の要点

- **ランタイムは `.msrc`（生バイナリ）を書くだけ**。`.anim` 等への変換は一切しない（重い処理・Editor 専用 API・GC を録画経路から排除）。再生や `.anim` 化はオフラインの後処理に分離する。
- **GC フリー**：毎フレームは事前確保した `byte[]` チャンクへコピーするだけ。満杯チャンクをバックグラウンド IO スレッドへ渡し、チャンクはプールで使い回す。→ 録画時間が伸びても GC スキャンコストが増えず、フレームレートが劣化しない。
- **root ベース全走査**：指定 root（Animator の Transform）以下を決定的順序で全走査。Humanoid 限定ではないので髪・スカート等の追加ボーンも記録でき、非ヒューマノイドにも対応。
- **記録値**：全ボーンの `localRotation`（quaternion）＋ 指定ボーンの `localPosition` ＋ `timestamp(double)`。位置記録ボーンは既定で Humanoid の Hips を自動採用。Generic リグや腰以外を狙う場合は明示指定する（root ボーンを勝手に拾うことはしない）。
- **マルチテイク**：1 テイク = 1 ファイル。停止はファイルを閉じるだけ。テイク間もチャンク再利用で GC ゼロ。

> Transform 直記録のため **リターゲット不可**（記録時と同じ root 相対パス構造のリグでのみ再生可能）。

## 使い方（コンポーネント）

1. シーンの GameObject に **MudShip ▸ Motion Recorder**（`MotionRecorderBehaviour`）を追加。
2. Inspector の **Targets** に記録対象を登録。各要素で `Animator` を指定する。
   - **Humanoid**：そのままで OK（Hips の `localPosition` を自動記録）。
   - **Generic／腰以外を狙う**：その要素の **Position Bones** に位置記録したい腰ボーン等の `Transform` を明示指定する。
3. **プレイモードに入り**、Inspector の「● 録画開始」→「■ 停止」。
4. `.msrc` が出力先（既定 `persistentDataPath/MotionRecordings`）に保存される。

## 使い方（コードから）

```csharp
using MudShip.MotionRecorder;

// コンポーネント経由
recorderBehaviour.StartRecording();
recorderBehaviour.StopRecording();
recorderBehaviour.RecordingStopped += () => { /* ... */ };

// あるいは低レベル API を直接
var skeleton = SkeletonDefinition.FromAnimator(animator);
var session = new MotionRecorderSession(skeleton, RecorderSettings.Default);
session.Start(path);
// 毎 LateUpdate:
session.CaptureFrame(Time.timeAsDouble - startTime);
// 終了:
session.Stop();
session.Dispose();
```

## .msrc → .anim 変換（Editor）

録画した `.msrc` を `AnimationClip` (.anim) に変換できる（オフライン後処理）。
全フレームにキーを打つ**ロスレス**変換で、各フレーム時刻の値は元データと完全一致する。

- **Tools ▸ MudShip Recorder ▸ Convert .msrc to .anim…** — ファイルを選んで変換（既定で `persistentDataPath/MotionRecordings` を開く）。
- Assets 内の `.msrc` を**右クリック ▸ MudShip Recorder ▸ Convert .msrc to .anim**。

生成されるのは Transform パスに紐づく **generic クリップ**で、記録時と同じ root 相対パス構造の
リグでのみ正しく再生される（リターゲット不可）。尺が長いとキー数が多く重くなる点に注意
（軽量化＝キーフレーム削減は今後対応）。

## 公開 API

| 型 | 役割 |
|---|---|
| `MotionRecorderBehaviour` | シーン配置用コンポーネント。記録対象（Animator＋位置記録ボーン）リスト＋録画ボタン。 |
| `MotionRecorderSession` | 1 スケルトン → 1 ファイルの記録エンジン（MonoBehaviour 非依存）。 |
| `SkeletonDefinition` | root 全走査によるボーン列・パス・位置記録ボーンの定義。 |
| `RecorderSettings` | fps・チャンクサイズ・プール数の設定。 |
| `MsrcFormat` | `.msrc` フォーマット定数とレイアウト仕様。 |

## `.msrc` フォーマット

リトルエンディアン。詳細は `MsrcFormat.cs` の doc コメント参照。

```
[Header]  magic"MSRC" / version u16 / flags u32 / nominalFps f32 /
          boneCount u16 / posBoneCount u16 / frameCount u32 / posIndices u16[posBoneCount]
[BoneTable] (boneCount) pathLen u16 + path utf8     ※root 相対パス、root 名は含めない
[Frames] (frameCount, 固定ストライド)
          timestamp f64 / positions (f32x3)xposBoneCount / rotations (f32x4)xboneCount
stride = 8 + posBoneCount*12 + boneCount*16
```

## 今後の予定

- `.msrc` 直再生器（Transform へ直接適用）
- `.anim` のキーフレーム削減（段2・誤差許容つき軽量化）
- Facial（BlendShape）／DMX ストリーム、共通タイムコード同期
