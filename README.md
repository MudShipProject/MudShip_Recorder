# MudShip Recorder

## 概要

MudShip Recorder は、Unity 上のモーション、BlendShape、Transform、Camera、Audio を記録するレコーダーです。

記録中はランタイムで生バイナリ（`.msr*`）を書き出すことに専念し、`.anim` / `.wav` への変換は Editor 上の
オフライン処理として分離しています。これにより、記録中の処理負荷を抑えています。記録した値は、Editor 上の
変換機能で `AnimationClip`（`.anim`）または `.wav` に変換してご利用いただけます。

- パッケージ ID: `com.mudship.recorder`
- 対応 Unity: 6000.0 以降
- マスターコンポーネント: `MS_Recorder`

## インストール

```
https://github.com/MudShipProject/MudShip_Recorder.git
```

## 使い方（コンポーネント）

### 1. コンポーネントの追加

シーン上の任意の GameObject に、**Add Component ▸ MudShip ▸ MS Recorder**（`MS_Recorder`）を追加します。

### 2. ファイル名の設定（File Naming）

- **Name Prefix**：すべての出力ファイルに付与する接頭辞です（任意）。`<Take>` と記述すると、Take の値が
  3 桁ゼロ埋めで展開されます（例：`Shoot<Take>` ＋ Take=3 → `Shoot003`）。
- **Take**：テイク番号です。`<Take>` の展開に使用します。

出力ファイル名は `(prefix)_(type)_(object)_(date)` の形式になります。

### 3. 記録スロットの設定（Recording Slots）

リスト右下の **＋** でスロットを追加します。各スロットの共通項目は次のとおりです。

- **Type**：記録の種別（`Character` / `Transform` / `Camera` / `Audio`）。
- **Output Dir**：出力先フォルダ（**参照...** ボタンで選択できます。未指定の場合は `persistentDataPath/MudShipRecordings`）。
- **Settings**：`Nominal Fps` / `Chunk Bytes` / `Pooled Chunk Count`。

種別に応じて、以下の項目が表示されます。

**Character**（`.msrm`（モーション）と `.msrf`（表情）を同時出力します）
- *Motion*
  - **Animator**：記録対象です（Humanoid を前提とします）。
  - **Hip Bone**：`localPosition` を記録する腰ボーンです（未指定の場合は Humanoid の Hips を自動採用します）。
  - **Add Bones**：Humanoid 定義外で回転も記録したい追加ボーンです（ツイストボーン等）。
- *Facial*
  - **Face Renderers**：表情を記録する `SkinnedMeshRenderer` 群です（Animator の GameObject 配下に配置してください）。

**Transform**：`Transform Target` の Position / Rotation / Scale を記録します（`.msrt`）。
**Camera**：`Camera Target` の Position / Rotation / Scale に加え、`fieldOfView` を記録します（`.msrc`）。
**Audio**：`Audio Device`（入力デバイス）の音声を 16-bit PCM で記録します（`.msra`）。

- Transform / Camera は **Record Space** で記録空間を選択できます。`World`（既定、親の影響を含む値）/ `Local`（親基準の値）。
- Audio で記録できるのは入力デバイス（マイク、ライン入力、オーディオインターフェース）のみです。PC の再生音を
  記録する場合は、仮想オーディオケーブル（VB-Audio Cable 等）を介して入力デバイスとして取り込んでください。

### 4. 記録の実行

プレイモードに入り、Inspector の記録開始 / 停止ボタンを操作します。ファイルは出力先へ保存され、Character の場合は
`Motion`（`.msrm`）と `Facial`（`.msrf`）に分かれます。出力先が Assets 配下の場合は、停止時に `AssetDatabase.Refresh()` を自動実行します。

### 5. `.anim` / `.wav` への変換

Assets 内の `.msr*` を選択し、右クリックメニューの **MudShip ▸ Convert recording** から変換します（複数選択に対応します。
出力は元ファイルと同じフォルダへ行います。全フレームにキーを設定するロスレス変換です）。

## ファイル形式

`msr` は MudShip Recording を表し、末尾 1 文字がストリーム種別を示します
（**m** = Motion / **f** = Facial / **t** = Transform / **c** = Camera / **a** = Audio）。

詳細な設計については [`Documentation~/recorder-architecture.md`](Documentation~/recorder-architecture.md) をご参照ください。
