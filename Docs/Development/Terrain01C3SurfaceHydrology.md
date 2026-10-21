# TERRAIN-01C3 表面水系 実装記録

- 更新日: 2026-10-21
- 対象PR: TERRAIN-01C3-A/B
- 状態: C3-A完了、C3-B実装・AI確認完了（人間による動作確認前）

## 1. この変更単位の範囲

TERRAIN-01C3全体は、基礎格子、下流終端、集水・流出、閉鎖流域・湖、診断表示を一度に変更せず、次の小さい変更単位へ分ける。

- C3-A: 8×8タイルの固定水文セル、固定9地点の代表標高、中心タイルによる海洋流出口、専用Saltの`PlateauRank`、循環しないD8流向、ECS Buffer、状態ハッシュ、純粋計算テスト。
- C3-B: 最大4,096辺の下流終端探索、`Marine`・`ClosedBasin`・`LongDrainage`、Level 0〜12の`HydrologyDownstreamSummary`。
- C3-C: 最大4,096セルの上流集合、Level 0〜6の集約Summary、`CatchmentAreaCells`、`CatchmentTruncated`、基礎湿度と自然流出。
- C3-D: 閉鎖流域、基礎湖面、流域境界、複数Seed監査、Debug Visualization、Map Viewer。

C3-Bまでで流向グラフの一辺と固定下流終端を確定する。集水面積、河川・湖、地下水、自然冠水・飽和、最終18Biome、河川のタイル細分化はまだ生成しない。既存の旧`River`・`Pond`タイル生成と表示も、この変更単位では置き換えない。

## 2. 固定水文セルと代表値

水文セル座標は正仕様どおり`floorDiv(WorldTile + 4, 8)`で求め、セル中心は`HydrologyCell × 8`とする。負座標は床方向へ除算し、座標加算・中心乗算・9地点サンプル・D8隣接は64bit中間値で`int`範囲を検証する。範囲外を反対端へ周回させたり、端へ飽和させたりしない。

代表標高`HydrologyHeight`は、中心からX・Yとも`-3`、`0`、`+3`タイルの9地点で既存の連続標高を再評価し、64bit合計を9で整数除算する。中心標高`CenterElevation`は別に保持し、海面閾値未満なら`OceanOutlet`を設定する。9地点の一部が海面未満でも海洋流出口にはせず、中心タイルだけを陸海判定へ使う。

`HydrologyGenerationSettings`はセル寸法8タイルとサンプル距離3タイルを`WorldGenerationSettings`へ値として含める。`WorldGenerationAuthoring`のSnapshotは共有の既定値で初期化し、既存Sceneの未初期化シリアライズ値から不正な水文設定を作らない。流向同点用の`HydrologyFlowTie`は既存の川・池や他の生成用途と共有しない。

## 3. D8流向と循環防止

`FlowKey`は`HydrologyHeight`、`PlateauRank`の辞書順とする。64bitハッシュが衝突した場合だけ、セル座標X、Yを最終比較へ加えて全順序を作る。候補の列挙順、チャンク生成順、Job実行順、ロード範囲は選択へ使わない。

陸上セルは次の順で下流を一つだけ選ぶ。

1. 隣接する海洋流出口があれば、代表標高にかかわらず最小Flow順の海洋流出口を選ぶ。
2. 海洋がなければ、自身より低い代表標高の候補から最小Flow順を選ぶ。
3. 低い候補もなければ、同じ代表標高で自身より小さい`PlateauRank`の候補から最小Flow順を選ぶ。
4. 候補がなければ、陸上の流向なしセルとして保持する。

海洋流出口は自身を下流座標として保持しつつ`FlowDirection = None`の終端にする。陸上辺は座標衝突時を含む安定全順序が必ず小さくなる場合だけ許可するため、有限経路内に循環を作らない。海洋への例外辺も終端へだけ向かう。

## 4. ECS結果、生成境界、状態ハッシュ

`GeneratedHydrologyCell`は、セル座標、1辺先の下流座標、下流終端または打切り位置、実際に進んだ辺数、自然接続フラグ、代表標高、中心標高、`PlateauRank`、D8方向、海洋フラグを持つ48 bytesのunmanaged Buffer要素である。1論理チャンクEntityへ、中心がそのチャンクに属する8×8件を行優先で格納する。4096タイルへ水文値を複製せず、地形・資源Bufferと分離する。

`ChunkStreamingSystem`は新規Surfaceチャンクへ水文Bufferを追加する。`ChunkGenerationSystem`の各再利用枠は地形4096件、資源4096件、水文64件を専用Native配列へ生成する。JobはECSへ直接書き込まず、3配列がすべて完成してから同じ公開境界でBufferへコピーし、`ReadyToApply`へ進める。不正設定または座標範囲外では部分水文を公開せず、チャンク生成を`Failed`にする。

チャンク状態ハッシュは既存4096地形タイルの後へ、行優先の64水文セルと全固定値を追加する。C3-Bでは終端座標、辺数、接続フラグも状態ハッシュへ含める。単一セルAPI、チャンクJob、状態ハッシュは同じ純粋Generatorを使い、キャッシュやECS Bufferを生成入力にしない。

## 5. C3-BのDownstream Summaryと固定終端

`HydrologyDownstreamSummary`は開始座標、到達座標、実辺数、Level、途中終端を持つunmanaged値である。Level 0は最大1辺、Level 12は最大4,096辺を表す。共有キャッシュがない基準経路はD8一辺生成を最大`2^Level`回だけ反復し、海洋流出口または陸上の流向なしセルへ着いた時点で停止する。同じLevelで前半が完全長、後半の開始座標が前半の到達座標と一致するSummaryは`TryCombine`で一段上へ合成できる。キャッシュの有無、Job順、ロード済みチャンクは分類入力にしない。

Level 12で海洋流出口へ到達したセルは`Marine`、陸上の流向なしセルへ到達したセルは`ClosedBasin`、4,096辺をすべて進んでもどちらにも着かないセルは`LongDrainage`へ固定する。開始セル自身が終端なら実辺数は0、`LongDrainage`なら必ず4,096とする。不完全な非終端Summaryや不連続な合成は失敗させ、ロード不足を終端分類へ読み替えない。

`HydrologyConnectionFlags`は将来の`Stream`、`River`、`Lake`、`Groundwater`と、C3-Bの相互排他的な`Marine`、`ClosedBasin`、`LongDrainage`へ安定したビットを割り当てる。C3-Bでは終端3種のうち一つだけを書き込み、集水・河川・湖・地下水のビットは後続変更まで設定しない。

## 6. 検証範囲

C3-A/Bの自動テストは次を対象にする。

- 既定設定、セル数、最大4,096辺・Level 12、要素サイズ48 bytes。
- `-4〜+3`の所有範囲、負座標の床方向除算、`int`座標端の明示失敗。
- 固定9地点の整数平均、中心標高、専用Salt由来の`PlateauRank`。
- 海洋流出口の最優先、候補列挙順の不変、同標高・ハッシュ衝突時の座標順。
- 実生成格子で、海洋終端、D8隣接、陸上辺の安定順減少、有限追跡時の循環なし。
- 1チャンク64件の行優先順、負チャンク、単一セルAPIとの一致。
- Level 0同士の二分合成と直接Level 1生成の一致、不連続・不完全Summaryの拒否。
- `Marine`・`ClosedBasin`・`LongDrainage`の相互排他、実辺数、到達座標、4,096辺上限。
- 終端情報を含む状態ハッシュの再現性とSeed差。
- ECS生成開始時の未公開状態、完成時の地形4096件・資源4096件・水文64件の一括公開、および終端情報の純粋Generatorとの一致。

Gameビュー、Map Viewer、分布監査の水文モードはC3-Dで追加する。C3-Bでも表示モードやEditorメニューを増やさないため、目視だけで終端分類の正しさを判定せず純粋計算とECS Bufferを検証する。

2026-10-21のC3-A AI確認では、Unity 6000.3.8f1の一時複製プロジェクトで最新差分をコンパイルし、C#エラーと警告がないことを確認した。`DeterministicHydrologyGeneratorTests`は21件、`ChunkGenerationSystem_`回帰は4件、`AnimalPopulationGenerationTests`は22件、Salt一意性とチャンクハッシュ順序不変の既存回帰は2件がすべて成功した。PlayMode Assemblyは56件中55件が成功し、不成功1件は生成・再表示・再訪の確認後に、`-batchmode -nographics`の`ProfilerRecorder`が`ChunkStreaming`の最大値0nsを返した既存`SampleScene_RepeatedAutomaticRebasesPreserveStreamingAcrossRoundTripAndNegativeCrossing`である。単独再実行でも同じProfiler値のみ不成功した。通常EditorではSampleSceneのPlay、カメラ移動、移動先チャンク表示を確認し、Play終了後もConsoleは情報・警告・エラー0件だった。Profiler Markerの自動テスト自体は、通常Editorでの人間確認に残す。

2026-10-21のC3-B AI確認では、Unity 6000.3.8f1の接続済みEditorでコンパイルエラーがないことを確認した。`DeterministicHydrologyGeneratorTests`は24件、`ChunkGenerationSystem_`回帰は4件、World Generation EditMode Assemblyは371件、PlayMode Assemblyは56件がすべて成功した。PlayModeにはSampleSceneの生成・再生成、チャンク越境、負座標横断、Profiler Marker回帰を含む。レビュー対応で追加した明示実行の局所性能テストも1件成功し、1,000／10,000／100,000セルで最大実辺数23／29／30、現在Threadのmanaged allocation 0 bytes、`LongDrainage` 0件を記録した。レビュー対応後のWorld Generation EditMode Assembly再実行も371件すべて成功し、Consoleエラーとコンパイル失敗は0件だった。全Assembly実行中には変更前から存在するSystem Group順序属性警告2種類が各3回出たが、今回変更した水文性能テスト単独および通常水文回帰からの新規警告はなかった。

## 7. 性能と残る確認

水文要素48 bytes×64件は1チャンク3,072 bytesである。生成作業枠の増加、ECS Buffer容量、1,000／10,000／100,000セルの局所比較は[性能計測](./Map02DPerformanceReport.md#terrain-01c3-b後の生成配列容量)へ記録する。C3-Bの基準経路は各開始セルから最大4,096辺を有限に進み、無限水系やロード済み全チャンクを走査しない。固定10 Seedの計測では最大実辺数30で、4,096辺が連続する事例を再現しなかったため、共有Summaryキャッシュは常駐させない。将来追加する場合も、具体的な長経路入力を固定し、分類結果を変えないことを二分合成テストで保証する。

製品最大Viewport、対象機器、正式なFPS・メモリ合格値は未決定である。今回の局所比較はmanaged C#をEditorで測った診断値であり、生成Job全体、Burst、Development Playerの性能合格を意味しない。具体的なSeed・設定で長経路やフレームスパイクが再現した場合は、その入力を固定してキャッシュ案を同条件比較する。人間の確認ではSampleSceneをPlayし、チャンク生成・カメラ移動・負座標越境でConsoleエラーがないことを確認する。C3-Bは非表示データのため、水文の色分けや終端表示がないことは未実装ではない。

2026-10-21に人間がC3-AのSampleSceneのPlay、チャンク越境、負座標側の表示、対象PlayModeテスト、Consoleを確認し、動作確認完了と判断した。C3-Bの人間確認は未実施である。
