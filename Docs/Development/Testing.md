# テスト

- 最終更新日: 2026-10-12
- 対象: `Assets/Farmware/Tests/`

## 1. 用途

自動テストは、ワールド生成の決定性、チャンク境界、地形分類、ECS System、Sceneと表示の接続が、修正によって壊れていないことを短時間で検出するために使用する。
地形バランスや見た目の良さを自動テストだけで決定せず、[Unity Editorツール](./EditorTools.md)とGameビューの確認を組み合わせる。

## 2. 実行方法

1. Unity Editorで `Window > General > Test Runner` を開く。
2. 純粋計算、設定、ECS Systemは`EditMode`タブから対象テストまたは対象Assemblyを実行する。
3. Scene、SubScene、Camera、Tilemap、HUD、表示連携は`PlayMode`タブから対象テストまたは対象Assemblyを実行する。
4. 失敗時は最初の原因となった例外とStack Traceを確認し、後続の連鎖的な失敗だけで判断しない。
5. 実行後はConsoleにテスト外の新規例外や警告が残っていないことも確認する。

Unity Editorを開いたまま外部プロセスから同じプロジェクトをバッチ起動しない。Libraryロック、再Import、未保存Sceneとの競合を避けるため、接続済みUnity CLIまたは開いているEditorのTest Runnerを使用する。

## 3. 現在のテスト構成

### 3.1 EditMode: `Farmware.WorldGeneration.Tests`

#### `WorldGenerationTests`

ワールド生成の純粋計算とECS Systemを、Sceneを開かずに検証する主要テスト群である。

| 分類 | 用途 |
| --- | --- |
| 座標・シード・ハッシュ | 負座標の床除算、文字列シード、生成順に依存しないハッシュを保証する。 |
| ノイズ・チャンク境界 | ノイズの連続性と、隣接チャンクが同じワールド座標規則を使うことを保証する。 |
| バイオーム | 7バイオームの分類、既定スケール、移行帯、軽量サンプルでの出現を保証する。 |
| 水域 | 気候による川・池の補正と、森林・湿地で池が確認できる分布を保証する。 |
| マグマ | 火山のマグマ比率、陸地の残存、淡水抑制、岩地の希少マグマ、歪な輪郭を保証する。 |
| 岩壁 | 水域上の抑制、適度な被覆率、チャンク境界、凹凸、通路を保証する。 |
| 資源 | 地形適性、最低間隔、群生半径、競合解決、決定性を保証する。 |
| 建築可能領域・初期スポーン | 地形を上書きせず、必要面積と資源条件を満たす地点を決定論的に記録する。 |
| ECS生成・ストリーミング | 生成要求、同時生成上限、範囲外キャンセル、保持、非表示化、再有効化を保証する。 |

`NodeBuildableRegionLocator_OneThousandSeedsFindDefaultRegion`は、狭い既定探索を1,000シードで調べる通常回帰テストである。
`NodeBuildableRegionLocator_OneHundredSeedSoakTest`は、最大17×17チャンクの資源探索を100シードで行うため`Explicit`指定された負荷テストであり、通常の修正確認では自動実行しない。

#### `WorldGenerationDistributionAuditTests`

Editorの分布監査と同じAnalyzerを小さな固定サンプルで2回実行し、集計漏れがないことと、署名・レポートが決定論的に一致することを保証する。
広域のバランス確認はテストへ組み込まず、`Farmware > World Generation > Analyze Distribution`から実行する。

#### `WorldGenerationMapViewerTests`

Map Viewerの全ピクセルが同じ座標へ直接`DeterministicChunkGenerator.GenerateTile`した結果と一致することを保証する。
あわせて、負座標を含むPointer座標変換、カーソル中心ズーム、Bakerと共有するAuthoring設定Snapshotのシード変換を検証する。

### 3.2 PlayMode: `Farmware.WorldGeneration.PlayModeTests`

#### `WorldGenerationScenePlayModeTests`

| 分類 | 用途 |
| --- | --- |
| Windowbox | 480×270の整数倍表示と黒帯内の描画矩形を保証する。 |
| F3 | 現在座標と記録済み初期スポーン座標のHUD文字列を保証する。 |
| F4 | 初期スポーンの赤いTile・Chunk表示と、縦横グリッド線の矩形計算を保証する。 |
| 開発者用可視化 | バイオーム、ノイズ、水域、移行帯、岩壁、マグマ、資源の色写像がSimulationを変更しないことを保証する。 |
| 岩壁表示 | チャンク境界をまたぐ内部影が連続したグラデーションになることを保証する。 |
| SampleScene縦切り | SubSceneロード、初期カメラ、チャンク生成、Tilemap表示、アンロード、同一地形の再生成を保証する。 |

## 4. 変更内容ごとの実行範囲

| 変更内容 | 必須の自動確認 | 追加の確認 |
| --- | --- | --- |
| 座標、ハッシュ、ノイズ、生成規則、調整値 | 関連するEditModeテストとMap Viewerテスト | 分布監査、Map Viewer、Debug Visualization |
| バイオーム、水域、マグマ、岩壁、資源 | 該当分類のEditModeテスト | 分布監査とGameビュー |
| Component、Buffer、System、Job | 関連するECS EditModeテスト | Safety Check、必要に応じてProfiler |
| Camera、Tilemap、HUD、F3/F4、可視化 | 関連するPlayModeテスト | 複数アスペクト比のGameビュー |
| Scene、SubScene、Authoring、Baker、参照 | SampleScene縦切りPlayModeテストとMap Viewerテスト | Scene内のMissing参照確認 |
| asmdef、パッケージ、プラットフォーム条件 | 影響するEditModeとPlayMode Assembly | Playerビルドへの混入確認 |

不具合修正では、可能なら不具合を再現するテストを先に追加し、そのテストと既存の関連テストが成功することを確認する。

## 5. 実行時間と負荷の方針

- 修正のたびに実行する通常テストは、Editorを長時間応答不能にしない規模へ保つ。
- 広域分布の比較はバックグラウンド実行する分布監査へ分離する。
- 多数シード、広大な探索範囲、性能計測は`Explicit`テストまたは専用ベンチマークとし、通常実行へ混ぜない。
- 重いテストを追加するときは、目的、件数、想定時間、通常実行から分離する理由を日本語コメントとこの文書へ記載する。
- 性能の合否はEditorの体感だけで決めず、同一条件のProfilerまたはDevelopment Build計測で比較する。

## 6. 失敗時に記録する情報

- 失敗した完全なテスト名。
- EditModeまたはPlayModeの別。
- 使用したシード、GeneratorVersion、座標、変更した調整値。
- 最初の例外とStack Trace。
- Unityバージョンと、BurstまたはSafety Checkの有無。
- 再現が見た目に関係する場合は、Gameビューの解像度、アスペクト比、選択中の可視化モード。

## 7. このドキュメントの更新条件

テストファイル、テストAssembly、通常・`Explicit`の区分、実行方法、検証範囲、負荷方針を追加・削除・変更した場合は、同じcommitにこの文書の更新を含める。
