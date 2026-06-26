# MudShip Motion Recorder

ヒューマノイド／スケルトンの Transform を **低負荷・GC フリー** で記録する `.msrc` レコーダー。
録画中はバイナリのダンプのみを行い、メインプロジェクトのフレームレートに寄生しない設計。

## 設計の要点

- **ランタイムは `.msrc`（生バイナリ）を書くだけ**。`.anim` 等への変換は一切しない（重い処理・Editor 専用 API・GC を録画経路から排除）。再生や `.anim` 化はオフラインの後処理に分離する。
- **GC フリー**：毎フレームは事前確保した `byte[]` チャンクへコピーするだけ。満杯チャンクをバックグラウンド IO スレッドへ渡し、チャンクはプールで使い回す。→ 録画時間が伸びても GC スキャンコストが増えず、フレームレートが劣化しない。
- **root ベース全走査**：指定 root（Animator の Transform）以下を決定的順序で全走査。Humanoid 限定ではないので髪・スカート等の追加ボーンも記録でき、非ヒューマノイドにも対応。
- **記録値**：全ボーンの `localRotation`（quaternion）＋ Hip（Humanoid）の `localPosition` ＋ `timestamp(double)`。
- **マルチテイク**：1 テイク = 1 ファイル。停止はファイルを閉じるだけ。テイク間もチャンク再利用で GC ゼロ。

> Transform 直記録のため **リターゲット不可**（記録時と同じ root 相対パス構造のリグでのみ再生可能）。

## 使い方（コンポーネント）

1. シーンの GameObject に **MudShip ▸ Motion Recorder**（`MotionRecorderBehaviour`）を追加。
2. Inspector の **Targets** に記録したい `Animator` を登録。
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

## 公開 API

| 型 | 役割 |
|---|---|
| `MotionRecorderBehaviour` | シーン配置用コンポーネント。Animator リスト＋録画ボタン。 |
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
- `.anim` エクスポータ（オフライン・`Keyframe[]` 一括生成でロスレス）→ キーフレーム削減
- Facial（BlendShape）／DMX ストリーム、共通タイムコード同期
