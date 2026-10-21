# TERRAIN-01C3 表面水系 実装記録

- 更新日: 2026-10-21
- 対象PR: TERRAIN-01C3-A/B/C1/C2/D
- 状態: C3-A/B/C1/C2はdevelopへマージ済み、C3-D1「閉鎖流域・基礎湖面」は人間確認とcommit `7d398e9`まで完了、C3-D2「監査・診断表示」もAI実装・自動検証・人間確認まで完了

## 1. この変更単位の範囲

TERRAIN-01C3全体は、基礎格子、下流終端、集水・流出、閉鎖流域・湖、診断表示を一度に変更せず、次の小さい変更単位へ分ける。

- C3-A: 8×8タイルの固定水文セル、固定9地点の代表標高、中心タイルによる海洋流出口、専用Saltの`PlateauRank`、循環しないD8流向、ECS Buffer、状態ハッシュ、純粋計算テスト。
- C3-B: 最大4,096辺の下流終端探索、`Marine`・`ClosedBasin`・`LongDrainage`、Level 0〜12の`HydrologyDownstreamSummary`。
- C3-C1: 最大4,096セルの上流集合を求めるキャッシュ非依存の基準経路、`CatchmentAreaCells`、`CatchmentTruncated`、固定9地点基礎湿度、自然流出、ECS Buffer、状態ハッシュ。
- C3-C2: Level 0〜6の集約Summary、外部入口と分解条件、キャッシュ同値性、局所・Chunk生成性能比較。
- C3-D1: 閉鎖流域、安定した流域境界・越流辺、基礎湖面、連続湖面セル、ECS結果、状態ハッシュ。
- C3-D2: 複数Seed監査、Debug Visualization、Map Viewer、凡例、中央セル情報。

C3-D1までで流向グラフの一辺、固定下流終端、集水面積、基礎湿度由来の`Stream`・`River`接続、Level 0〜6集約Summary、閉鎖流域境界、固定湖候補と水文セル単位の連続湖面を確定する。集約経路は性能判断用のMain Thread実装で、既定のBurst Chunk生成Jobへは接続しない。タイル単位の河川・湖面細分化、地下水、自然冠水・飽和、湖水収支による淡水・塩水・一時湖の最終区分、最終18Biomeはまだ生成しない。既存の旧`River`・`Pond`タイル生成と表示も、この変更単位では置き換えない。

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

`GeneratedHydrologyCell`は、セル座標、1辺先の下流座標、下流終端または打切り位置、実際に進んだ辺数、自然接続フラグ、代表標高、中心標高、`PlateauRank`、D8方向、海洋フラグに加え、C3-C1の基礎湿度、集水セル数、上流湿度合計、流出相当値、最遠寄与距離、打切りフラグ、C3-D1の閉鎖流域面積、越流辺、湖底・越流・湖面標高、充填率、湖判定フラグを持つ96 bytesのunmanaged Buffer要素である。1論理チャンクEntityへ、中心がそのチャンクに属する8×8件を行優先で格納する。4096タイルへ水文値を複製せず、地形・資源Bufferと分離する。

`ChunkStreamingSystem`は新規Surfaceチャンクへ水文Bufferを追加する。`ChunkGenerationSystem`の各再利用枠は地形4096件、資源4096件、水文64件を専用Native配列へ生成する。8水文行は`IJobParallelFor`で出力8要素と独立した上流Heap領域を1行ずつ使う。その完了後に準備Jobが閉鎖末端ごとの対象セルMaskを作り、最大8つの湖担当が専用ワーク領域で異なる流域を並列解決する。独立した地形・資源Jobはこの水文系列と同時に実行する。JobはECSへ直接書き込まず、並列湖Jobと地形・資源JobのHandleを結合する。個体群Worldでは未完了中の固定Tickを延期し、全件の`IsCompleted`確認後だけ完成済みHandleを確定して3 Bufferへコピーし、`ReadyToApply`へ進める。不正設定または座標範囲外では部分結果を公開せず、チャンク生成を`Failed`にする。

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
- 対象セル自身を含む1〜4,096セルの有限集水、同入力での件数・湿度合計・最遠距離・打切り状態の再現。
- 代表標高と同じ固定9地点の`BaseMoisture`平均、上流集合の湿度合計、65,535による整数除算。
- `RunoffEquivalentCells` 15／16と127／128の境界、および`River`が必ず`Stream`を伴うこと。
- ECS公開と状態ハッシュへ集水セル数、基礎湿度、湿度合計、流出相当値、最遠距離、接続・打切りフラグが含まれること。
- 個体群Worldの通常フレームでは生成Jobを開始するが固定Tick入口までBufferを公開せず、固定Tick入口で地形4096件・資源4096件・水文64件を一括公開すること。
- 個体群Worldの生成Jobが未完了なら固定Tickを延期し、準備完了後に到来済みTickを最大1件だけ再開して、待機中の壁時計遅延を一括追いつきしないこと。
- 水文8行の並列生成後も、単一セルAPI、チャンクBuffer、状態ハッシュ、フレーム分割・要求作成順を変えた個体群状態が一致すること。

Gameビュー、Map Viewer、分布監査の水文モードはC3-D2で追加した。C3-D1の固定結果は引き続き純粋計算とECS Bufferを正とし、診断表示は生成結果を変更しないPresentationとして読み取る。

2026-10-21のC3-A AI確認では、Unity 6000.3.8f1の一時複製プロジェクトで最新差分をコンパイルし、C#エラーと警告がないことを確認した。`DeterministicHydrologyGeneratorTests`は21件、`ChunkGenerationSystem_`回帰は4件、`AnimalPopulationGenerationTests`は22件、Salt一意性とチャンクハッシュ順序不変の既存回帰は2件がすべて成功した。PlayMode Assemblyは56件中55件が成功し、不成功1件は生成・再表示・再訪の確認後に、`-batchmode -nographics`の`ProfilerRecorder`が`ChunkStreaming`の最大値0nsを返した既存`SampleScene_RepeatedAutomaticRebasesPreserveStreamingAcrossRoundTripAndNegativeCrossing`である。単独再実行でも同じProfiler値のみ不成功した。通常EditorではSampleSceneのPlay、カメラ移動、移動先チャンク表示を確認し、Play終了後もConsoleは情報・警告・エラー0件だった。Profiler Markerの自動テスト自体は、通常Editorでの人間確認に残す。

2026-10-21のC3-B AI確認では、Unity 6000.3.8f1の接続済みEditorでコンパイルエラーがないことを確認した。`DeterministicHydrologyGeneratorTests`は24件、`ChunkGenerationSystem_`回帰は4件、World Generation EditMode Assemblyは371件、PlayMode Assemblyは56件がすべて成功した。PlayModeにはSampleSceneの生成・再生成、チャンク越境、負座標横断、Profiler Marker回帰を含む。レビュー対応で追加した明示実行の局所性能テストも1件成功し、1,000／10,000／100,000セルで最大実辺数23／29／30、現在Threadのmanaged allocation 0 bytes、`LongDrainage` 0件を記録した。レビュー対応後のWorld Generation EditMode Assembly再実行も371件すべて成功し、Consoleエラーとコンパイル失敗は0件だった。全Assembly実行中には変更前から存在するSystem Group順序属性警告2種類が各3回出たが、今回変更した水文性能テスト単独および通常水文回帰からの新規警告はなかった。

2026-10-21にC3-Bは人間確認を経てPR #35としてdevelopへマージされた。ロードマップと本書に残っていた「人間確認前」の記載はC3-C1着手時にGit履歴と同期した。

## 7. C3-C1の基準集水・自然流出

`DeterministicHydrologyCatchmentResolver`は対象セル自身を距離0の候補として開始し、選択済みセルを直接指す周囲8セルだけを逆向きに発見する。候補は`FlowDistanceFromTarget`、`HydrologyHeight`、`PlateauRank`、Cell X、Cell Yの順で最小Heapから取り出す。D8グラフでは各セルの下流が高々一つなので、同じ上流セルは別の選択済みセルから重複発見されず、managedなHashSetをHot Pathへ持ち込まない。

最大4,096件を選択し、4,096件目でも周囲8セルの確定D8辺を検査する。検査前からフロンティアが残るか、新しい直接上流を一つでも見つけた場合だけ`CatchmentTruncated`を設定する。座標加算、D8一辺、固定9地点サンプルのどれかが対応範囲外なら部分結果へ丸めず生成を失敗させる。

`HydrologyMoisture`は代表標高と同じ9地点で`BaseMoisture`を整数平均する。上流選択と同じループで各値を`RunoffMoistureSum`へ一度だけ加え、65,535で整数除算した`RunoffEquivalentCells`が16以上なら`Stream`、128以上なら`Stream | River`を追加する。下流終端の`Marine`・`ClosedBasin`・`LongDrainage`は保持し、現在降雨、土壌水分、旧River表示を入力へ戻さない。

`GeneratedHydrologyCell`は64 bytesとなり、集水・流出の全固定値を状態ハッシュへ追加した。初回実装ではChunk生成作業枠が32,768件の上流候補配列を所有し、64セルで順次再利用した。チャンク越境時の待ち改善では、8行が同時使用できる262,144件へ拡張し、各行は自分の32,768件領域を8セルで順次再利用する。所有SystemはJob完了後まで配列を保持し、World終了時に破棄する。容量と境界待ちの比較は[性能計測](./Map02DPerformanceReport.md#terrain-01c3-c1後の生成配列容量)へ記録した。

## 8. C3-C2の空間集約Summaryとキャッシュ境界

`HydrologyCatchmentAggregationSummary`は、Level 0の1×1からLevel 6の64×64まで、2の累乗へ整列した正方形領域を表す。固定入力から領域セルを行優先で再生成し、セル数、基礎湿度合計、領域外へ流れる唯一の出口、出口までの最大内部距離、外周1セル帯から内部へ直接流入する外部入口を保持する。外部入口には入口座標、最初の内部セル、出口までの距離、代表標高、基礎湿度、`PlateauRank`を持たせ、Summary境界を越えてもC3-C1と同じ優先度付き上流探索を継続できるようにした。

領域内終端、複数出口、領域外へ出た下流の再進入、4,096辺で再進入なしを確定できない不完全経路は、親Summaryを一括利用しない明示的な分解理由として保持する。集水上限4,096セルをまたぐSummaryも下位Levelへ分解し、選択数がちょうど4,096へ達する場合はC3-C1基準Resolverで全体を再計算する。これにより、外部入口や別枝が残る境界で集約順を打切り判定へ混ぜず、4,095／4,096／4,097件の正規化順と`CatchmentTruncated`を基準経路へ一致させる。

`HydrologyAggregationSummaryCache`はSeed・GeneratorVersion・生成設定を寿命中固定し、絶対座標とLevelをキーにSummaryと外部入口を保持する。構築作業配列、現在入口List、上流HeapはPersistentで一度だけ確保し、呼出しごとのmanaged allocationを避ける。Disposeが全Nativeコンテナの破棄責任を持つ。比較用Resolverは空キャッシュ、命中済みキャッシュ、逆順構築キャッシュのいずれでも基準結果と一致する。キャッシュはECS状態の正ではなく、Job間でmutableコンテナを共有しないMain Thread比較実装である。

## 9. 性能と残る確認

水文要素はC3-D1で96 bytes×64件、1チャンク6,144 bytesとなる。既定のChunk生成は8行の独立Heapで基礎水文を並列生成し、その完了後に閉鎖末端ごとの重複しない対象セルMaskを作る。最大8湖担当は、流域セル4,096件と座標Hash表8,192件を1領域ずつ使い、同じ流域をチャンク内で一度だけ構築して全対象セルへ適用する。D8下流で代表標高が増加しない不変条件から湖面未満セルの末端連結を保証できるため、湖面Queueは所有しない。地形・資源Jobは水文系列と独立して走らせ、結合Handleが未完了なら固定Tickを延期する。集約キャッシュは製品Jobへ接続していない。固定作業容量は[性能計測](./Map02DPerformanceReport.md#terrain-01c3-d1後の閉鎖流域作業容量)へ、C3-C2比較用キャッシュの結果と制約は[集約Summary比較](./Map02DPerformanceReport.md#terrain-01c3-c2の集約summary比較)へ記録した。

製品最大Viewport、対象機器、正式なFPS・メモリ合格値は未決定である。今回の局所比較はmanaged C#をEditorで測った診断値であり、生成Job全体、Burst、Development Playerの性能合格を意味しない。具体的なSeed・設定で長経路やフレームスパイクが再現した場合は、その入力を固定してキャッシュ案を同条件比較する。人間の確認ではSampleSceneをPlayし、チャンク生成・カメラ移動・負座標越境でConsoleエラーがないことを確認する。C3-D1は非表示データのため、水文の色分けや湖表示がないことは未実装ではない。

2026-10-21に人間がC3-AのSampleSceneのPlay、チャンク越境、負座標側の表示、対象PlayModeテスト、Consoleを確認し、動作確認完了と判断した。C3-Bも人間確認後にPR #35としてマージ済みである。

C3-C1の初回AI確認では、Unity 6000.3.8f1でゲーム仕様どおり`RunoffMoistureSum`を符号付き64bitへ揃えた差分を再コンパイルし、エラー・警告0件を確認した。`DeterministicHydrologyGeneratorTests` 31件、`ChunkGenerationSystem_`回帰4件、World Generation EditMode Assembly 378件、PlayMode Assembly 56件がすべて成功した。

その後のチャンク越境時の引っ掛かり調査では、同じ長距離移動PlayModeテストへ`ChunkGenerationBoundaryWait`を追加し、直列水文生成の最大59.804msを記録した。通常フレーム先行投入だけでは42.762msだったため、水文8行と地形・資源を9作業へ並列化した。並列化後は最大19.478msとなり、初期値比で約67.4%短縮した。生成値と公開順を変えないことは、水文31件、個体群境界23件、`WorldGenerationTests` 68件、World Generation EditMode Assembly 379件、PlayMode Assembly 56件の成功で確認した。全EditMode実行中の警告6件は既知のテスト用System Group順序属性2種類が各3回出たもので、エラーとコンパイル失敗は0件だった。2026-10-21に人間が通常EditorのSampleSceneで通常移動とShift移動によるチャンク越境を確認し、境界待ちは許容範囲と判断した。製品最大Viewport・対象機器・Development Playerの正式性能合格値は未決定であり、Editor PlayModeの単一実行値を製品保証にはしない。

C3-C2ではUnity 6000.3.8f1の一時複製プロジェクトでRuntime AssemblyとTest Assemblyを再コンパイルし、エラー・警告0件を確認した。集約関連の通常テスト9件と、通常回帰から分離した明示性能テスト1件が成功した。性能テストは256セルの固定作業集合を温め、1,000／10,000／100,000要求で基準経路と集約経路のチェックサムを一致させた。100,000要求は基準140,632.070ms、集約22,305.776ms、比率0.159、両方の現在Thread managed allocationは0 bytesだった。16 Chunk・1,024水文セルは基準2,003.252ms、集約954.973ms、比率0.477で、時間外の全セル比較も一致した。最終差分の全EditModeは484件中480件成功・失敗0件・明示4件除外で、うちWorld Generation Assemblyは392件中388件成功・明示4件除外だった。PlayMode Assemblyは56件すべて成功した。既存下流性能テストもPersistent Heap再利用後に単独1件成功し、一時Allocator枯渇がないことを確認した。これはEditor上の温め済みMain Thread比較であり、キャッシュ構築時間、Nativeコンテナ容量、Burst Job化、Development Player、対象機器を含む製品性能合格値ではない。既定Jobへ直ちに採用せず、C3-D以降で実際の生成待ちが再び問題になった場合に、所有権とメモリ上限を決めたうえで接続を判断する。

## 10. C3-D1の閉鎖流域と基礎湖面

閉鎖流域は、対象セルの`DownstreamReachCell`で確定した流向なし陸上末端について、C3-C1と同じ優先度・4,096件上限・4,096件目の逆辺検査で正規化済み上流集合を列挙する。打切り集合は`ClosedBasin`を維持するが、境界を推測せず`BasinTruncated`として固定湖候補から除外する。打切りのない集合は固定容量Hash表へ座標を登録し、流域内セルの全8近傍から外側となる辺を列挙する。

越流辺は`max(InsideHydrologyHeight, OutsideHydrologyHeight)`の昇順、外側セルの`FlowKey`、内側X・Y、外側X・Yの順で一つに決める。外側セルは比較に必要な代表標高と順位だけを生成し、未使用のD8隣接8セルを再生成しない。流域最低4セル、末端の`RunoffEquivalentCells`が8以上、`SpillHeight > LakeBottomHeight`をすべて満たした場合だけ`FixedLake`を設定する。充填率は`min(65,535, RunoffEquivalentCells × 2,048)`、湖面は湖底から越流標高までを充填率で整数補間する。流域内のD8下流辺は`HydrologyHeight`を増加させず必ず同じ末端へ到達するため、`HydrologyHeight < LakeSurfaceHeight`のセルは下流鎖全体も湖面未満となる。この不変条件を湖底との連結証明として`SurfaceCell`と`Lake`を設定し、同標高の別流域を含めない。32以上の最大充填時だけ`Overflow`を設定し、記録した越流辺を有効にする。元のD8下流座標、集水面積、流出相当値は変更しない。

Chunk生成の水文系列は三段階とする。最初の`IJobParallelFor`は水文8行へ既存Heap 32,768件を各1領域ずつ割り当て、湖面を除く64セルを生成する。依存する準備Jobは同じ閉鎖末端のセルを一つの64bit Maskへまとめる。最後の`IJobParallelFor`は最大8担当へ、Heap 32,768件、流域セル4,096件、Hashスロット8,192件を各1領域ずつ割り当て、飛び石状に受け持つ異なる流域を並列解決する。Mask同士は重ならないため別担当が同じ水文セルへ書かない。System破棄時は最終Handle完了後に全Persistent配列を破棄する。単一セルAPIと状態ハッシュは同じResolverとチャンク一括規則を`Allocator.Temp`の呼出し単位ワーク領域で逐次使用する。

C3-D1のRuntime Assemblyと水文Test Assemblyは、既存Editorが生成したUnity 6000.3.8f1のC#プロジェクトを使い、警告・エラー0で静的コンパイルを確認した。開いているEditorへ影響しない一時複製プロジェクトでは、全EditMode 501件中496件成功・失敗0件・明示5件除外、うちC3-D1対象18件、レビュー修正3件、Chunk生成System回帰4件が成功した。C3-C2集約性能テストも湖探索を除いた基礎水文比較として174.472秒で成功し、100,000要求の基準130,832.745ms、集約20,691.857ms、16 Chunkの基準1,859.997ms、集約884.179ms、現在Thread managed allocation 0 bytes、全チェックサム一致を確認した。

全PlayModeは56件中55件成功した。残る1件は生成・表示・再訪・負座標横断の状態Assertを通過後、既知の`-batchmode -nographics`環境差により`ChunkStreaming` Profiler Recorderが0nsとなったもので、単独再実行でも同じ箇所だけ失敗した。通常EditorでのSampleScene、チャンク越境、負座標側、Console、および湖処理追加後の`ChunkGenerationBoundaryWait`は人間確認へ残す。

C3-D1の明示負荷テストは415.097秒で成功した。同じ100×100セル作業集合を使う1,000／10,000／100,000要求は2,806.833ms／37,403.692ms／374,180.121ms、生成失敗0、現在Thread managed allocation 0 bytesだった。100,000要求では固定湖48,670セル、実湖面2,840セル、打切り流域0セルを記録した。これはUnity Editor上の温め済みMain Thread局所診断であり、Burst Job、チャンク境界待ち、Development Player、対象機器の正式性能合格値ではない。

人間確認でC3-D1追加後のチャンク越境待ちが許容範囲外と報告されたため、閉鎖流域の再利用境界を行内の連続セルからチャンク全体へ広げた。明示比較`ChunkLakeBatching_ReportsComparisonWithRowLocalResolution`は同じ16チャンク・1,024セルについて、修正前相当の行別解決8,404.443ms、新しいチャンク一括解決3,084.233ms、比率0.367を記録した。閉鎖流域セル737件、チャンクごとの一意な末端合計42件で、両経路の全固定値は一致した。製品Jobではさらに、地形・資源Jobを水文系列と並行させ、異なる閉鎖流域を最大8担当で並列解決する。グラフィックス有効の隔離PlayModeでは、単一湖Jobの境界待ち最大38.565msに対し、並列湖Jobの3実行が31.249／37.170／33.717msだった。セッション間変動が大きく、通常Editorでの体感合格を代替しないため、修正後のSampleScene越境を再度人間確認へ戻す。

並列湖Jobまで含む最終差分の全EditModeは159.586秒で503件中497件成功・失敗0件・明示6件除外だった。全PlayModeは6.315秒で56件中55件成功し、残る1件は生成・表示・再訪・負座標横断の状態確認後、既知の`-batchmode -nographics`でProfiler値0nsとなる2130行だけが失敗した。閉鎖末端Maskの排他性と逐次結果一致、湖面整合、Chunk生成Systemの実Job回帰は単独でも成功し、コンパイルエラー、新規C#警告、Job安全性エラーは0件だった。接続済み通常Editorはコンパイル失敗なし、Consoleエラー0件・警告0件だったが、Pipeline 0.8.0-exp.1の`run_tests`が一覧上の対象名を0件として終了したため、通常Editorの対象テストと体感確認は人間確認へ残す。

並列化後も通常Editorで境界の引っ掛かりが残り、同一セッションの内訳計測では`ChunkGenerationBoundaryWait`最大26.380ms、基礎水文13.412ms、湖12.951ms、地形0.004msだった。この結果から、Worker処理量だけでなく固定Tick入口の`Complete()`同期が描画停止を作っていると判断した。修正後は`FixedTickReadinessState`に未完了状態を通知し、Rate Managerが固定Tickを延期して描画へ制御を返す。完成確認後だけHandleを確定し、公開後に次バッチを投入した場合も同一描画フレームの追加Tickを止める。接続済みUnity 6000.3.8f1 Editorの長距離往復PlayModeテストは1件成功し、`ChunkGenerationBoundaryWait`最大0.149ms、基礎水文0.143ms、湖0.001ms、地形0.001msだった。固定Tickテスト8件と個体群境界テスト23件も全成功した。最終差分の全EditModeは138秒で498件、全PlayModeは6.16秒で56件がすべて成功し、Console実状態はコンパイル失敗なし・エラー0・警告0だった。単一Editor実行の診断値であり、通常操作の体感、Development Player、対象機器の性能合格は人間確認へ残す。

2026-10-21に人間が通常Editorでチャンク越境時の引っ掛かり解消を確認し、C3-D1をcommit `7d398e9`として完了した。D1とD2は同じPRへ含めるが、変更責務と人間確認地点を分けるためcommitは分離する。

## 11. C3-D2の複数Seed監査と診断表示

Editorメニュー`Farmware > World Generation > Analyze Hydrology Distribution`は、既定3 Seedの原点周囲5×5チャンクを実ゲームと同じ固定Generatorで生成し、4,800水文セルの流向、終端、集水打切り、渓流・河川、閉鎖流域、固定湖候補、実湖面、越流を集計する。解決済み・打切り・固定湖候補の流域数はSeedと下流終端座標で重複除去し、全固定分類から再実行比較用の64bit署名を作る。Editor停止を避けるためバックグラウンドで実行し、同じ監査の二重起動を拒否する。分布値は調整前後の比較材料であり、件数や面積率の合否閾値は設定しない。

Gameビューの`Debug Visualization > Hydrology`とMap Viewerへ、`Flow Direction`、`Terminal`、`Catchment`、`Surface Water`、`Lake`の5モードを追加した。流向は8方向と終端、終端は`Marine`・`ClosedBasin`・`LongDrainage`、集水は1〜4,096セルの対数色と打切り、表流水は流れなし・`Stream`・`River`、湖は閉鎖流域外・解決済み乾燥流域・打切り流域・固定湖候補・実湖面・越流を個別表示する。打切り流域は固定湖候補ではないことを明示するマゼンタとし、固定湖候補の濃青、実湖面の青、越流のシアンと重ならない。

Gameビューは生成済み`GeneratedHydrologyCell` Bufferだけを読み、現在のWorldLayerに属するロード済みチャンクを絶対水文セル座標へ索引化する。水文セルの担当範囲が中心から`-4〜+3`タイルで論理チャンク境界とずれるため、現在レイヤーの全ロード済みBufferをRepaint中だけ参照し、正端4タイルでも隣接チャンクのセルを表示できるようにする。中央HUDは固定水文セルの座標、代表標高、流向、下流座標、終端、辺数、集水面積、打切り、湿度、流出相当値、接続、流域・湖の全固定値を表示する。

Map Viewerは同じRGB色写像を共有し、選択した代表タイルに対応する固定水文セルを純粋Generatorで生成する。同じ8×8セルは描画またはHover中のキャッシュで再利用する。バックグラウンド生成ではMain Thread限定か4フレーム制限を持つTemp系Allocatorの簡易APIを避け、Editor Assemblyだけがアクセスできる内部Resolverへ`Allocator.Persistent`の上流Heap・流域セル・Hash表を渡し、1回の画像生成中に再利用して`using`で最後に破棄する。複数Seed監査も同じ所有方式でチャンク間の作業領域を再利用する。1サンプルが最大4,096セルの集水・流域探索を伴うため、水文5モードだけ生成Textureを最大64サンプルへ制限し、通常モードの最大768サンプルは変更しない。縮小時も縦横共通の比率を使い、Viewportの縦横比と正方形グリッドを維持する。

自動テストは、監査の64セル集計・分類不変条件・署名再現性、打切り流域と固定湖候補・実湖面・越流の色分離、GameビューとMap ViewerのRGB一致、小画像の直接Generator結果一致、64サンプル制限での縦横比を対象にする。GameビューとMap Viewerの実際の凡例、中央・Hover情報、チャンク境界、負座標、操作応答性は通常Editorで人間が確認する。

2026-10-21のUnity 6000.3.8f1接続済みEditorでは、Runtime・Editor・Test Assemblyをコンパイルエラー0で再読み込みし、`HydrologyDistributionAuditTests` 1件、`HydrologyVisualizationTests` 5件、`WorldGenerationMapViewerTests` 51件が成功した。World Generation EditMode Assemblyは130.74秒で411件すべて成功した。全Assembly中の警告6件は変更前からあるテスト用WorldのSystem Group順序属性2種類が各3回出たもので、D2のAllocator・Job安全性警告ではない。既定3 Seed・各5×5チャンクの監査も21秒で4,800セルを完走し、署名`0x82F06B77D1BB8E48`、海洋終端1,930、閉鎖流域2,870、渓流237、河川11、解決済み流域46、固定湖候補流域46、湖面271セルを記録した。これは分布比較値であり合否閾値ではない。

実Map ViewerはHydrology Lakeを64×28＝1,792サンプル、2.77秒で生成し、打切り流域を固定湖候補と別色・別文言にした凡例、中央セルの流向・終端・集水・湖情報、正方形グリッドを確認した。SampleSceneのGameビューでもHydrology Lakeの2段凡例、8×8セル色面、中央HUDを確認し、両確認後のConsoleはエラー0・警告0だった。初回実UI確認でThreadPoolからMain Thread限定`Allocator.Temp`を呼ぶエラーと、4フレームを超えた`Allocator.TempJob`寿命警告を検出したため、最終差分はEditor Assemblyから内部Resolverへ`Allocator.Persistent`作業領域を渡して`using`で破棄する方式へ修正し、ThreadPoolテストと実UIを再実行した。

Pipeline 0.8.0-exp.1はPlayMode 56件を一覧取得できる一方、`run_tests`がPlayMode対象を0件として終了したため、全PlayMode Assemblyの自動再実行は未検証である。D2で変更したGameビュー経路は通常EditorのSampleScene PlayとConsoleでAI確認し、2026-10-21に人間が各水文モード、チャンク境界、負座標移動、Map Viewerを確認して問題なしと判断した。
