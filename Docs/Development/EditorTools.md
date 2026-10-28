# Unity Editorツール

- 最終更新日: 2026-10-28
- 対象: `Assets/Farmware/Editor/WorldGeneration/`

## 1. 用途

本書のメニュー・設定・表示は、TERRAIN-01C2-Bまでのdevelop、TERRAIN-01C3-D2、水文Lake表示へ先行反映したTERRAIN-01C4-C、派生水文分布監査、Gameビュー診断、Map Viewer診断を追加したTERRAIN-01C4-E3、最終18Biomeを接続したTERRAIN-01C5-C、Map Viewerモード契約とBiome選択診断を整理したTERRAIN-01C5-D、完成チャンクの分布・境界監査へ置換したTERRAIN-01C5-E、可変Blockを通常表示・診断・Map Viewerへ接続するTERRAIN-01C6-D5の現行実装境界を説明する。C3-A〜D1がSimulationへ追加した固定水文の基礎格子、D8流向、固定下流終端、集水面積、自然流出、閉鎖流域境界、基礎湖面を、C3-D2で複数Seed分布監査、Debug Visualization、Map Viewerへ接続した。C4-E1bは地下水、土壌飽和、最終湖区分、泉、自然冠水、湿原候補を複数Seed分布監査へ接続し、C4-E2とE3は残る5分類をGameビューとMap Viewerへ接続した。C5-CはC4完成チャンクを18Biomeへ確定し、GameビューとMap ViewerのTerrain・Biome表示へ接続した。C5-Dは製品分類器と同じ最終理由・通常上位候補・8軸適性を両画面の中央情報へ追加し、Map Viewer全27モードの生成契約を単一Catalogへ集約した。C5-Eは停止中だった分布メニューを再有効化し、製品と同じ完成チャンクから候補・面積・境界・領域を集計する。C6-D5ではGameビューと開発者可視化が自然生成と永続差分を合成した同じ有効地形近傍を読み、Map Viewerは同じ生成設定の実行中Worldがある場合だけ差分Snapshotを適用する。Level 0〜6集約Summaryの利用・分解・キャッシュ統計は性能テストだけで記録し、Editor表示へは渡さない。

開発者専用のUnity Editorツールは、ワールド生成結果の調査、調整前後の比較、確認用Sceneとフォールバック素材の構築に使用する。
これらは製品版のプレイヤー操作へ含めず、ランタイムのゲーム状態を変更するデバッグ操作にも使用しない。

プレイヤーへ提供するF3座標表示とF4グリッド表示はEditorツールではない。
両機能の仕様は[ランダムマップ生成仕様](../GameSpecification/RandomMapGenerationSpecification.md)を参照する。

## 2. ツール一覧

`Farmware > World Generation`直下は、広域確認用の`Map Viewer`と、用途別の`Analysis`、`Debug Visualization`、`Generation`、`Inspection Scenes`へ分ける。分布集計は`Analysis`、素材・Scene生成は`Generation`、固定検品・比較Sceneは`Inspection Scenes`から実行する。`Inspection Scenes > Open ...`は入力Assetを検証して保存済みSceneを開くだけで、SceneやMaterialを再保存しない。素材または配置規則を変更した場合だけ、`Generation > Rebuild ...`を実行して追跡対象の生成物を更新する。保存済みSceneが存在しない初回だけは、`Open ...`も不足Sceneを構築する。

### 2.1 Build Fallback Map Prototype

- メニュー: `Farmware > World Generation > Generation > Build Fallback Map Prototype`
- 実装: `WorldGenerationSceneBootstrap`
- PlayMode: 不要。EditModeで実行する。

#### 用途

- 上面16pxと断面8pxを結合した16×24の仮色土地Tile、16×16の仮色液体・資源Tileを生成または更新する。土地上面は安定`BiomeId` 1〜18に対応する18種類の仮色だけで塗り、未制作Biomeを別BiomeのTileへフォールバックさせない。草、葉、砂紋などの固有模様や旧土壌分類別の派生Tileは生成しない。旧7種の土地フォールバックと旧14枚の肥沃・砂質派生アセットが存在する場合は削除する。
- 上面16pxと側面12pxを結合した16×28フォールバック岩壁Tile、液体の静止基礎Tileを生成または更新する。液面アニメーション用の旧4フレーム素材は生成しない。フォールバックは構築確認用として維持し、本番用`terrain_rock_wall_01.png`を上書きしない。
- 本番用`terrain_rock_wall_01.png`へPoint、PPU 16、Mip Maps Off、Compression None、Full Rect、Pivot Y `8/28`を設定し、専用Tileを作成または更新する。Sprite PivotはSprite Editor Data Provider経由で設定する。
- 新18Biomeの仮色土地Spriteを同じTextureページへ収める`GroundTerrainAtlas.spriteatlasv2`、本番用岩壁を収める`RockWallAtlas.spriteatlasv2`、共通境界14 Spriteを収める`BoundaryHighlightAtlas.spriteatlasv2`を生成または更新し、現在のBuild Target向けにパックする。C6のBlock用本番素材が未制作の間だけ、18枚の仮色土地を表示契約とAtlas検証に使う。境界PNGの画素と14分割は専用の境界生成ツールが担当し、このツールはImport済みTextureの検証・Atlas登録・同一ページ検証を担当する。
- `WorldGenerationSettingsSubScene`へ生成設定のAuthoringと、最初の動物種である鶏の共通定義・生成条件を配置する。鶏は草原の通常陸地、群れ3〜6羽、有効地形4096タイル当たり8羽、待機20〜80固定Tick、1タイル移動5固定Tick、最大徘徊8タイル、外見差分3種類を開発用初期値とする。
- `DefinitionCatalogAuthoring`の専用Inspectorでは、`animalSpeciesDefinitions`の各要素を共通定義GUIDで照合し、`0: Chicken`のように`definitions`側の表示名を見出しへ表示する。表示名を動物種固有設定へ重複保存しないため、名称変更は`definitions`側だけで行う。GUID未設定または参照切れの場合は、見出しへ未解決理由と短縮GUIDを表示する。固定3液体とC6-Bの41材質に属するItem・Ground・Wall Definitionに加え、18Biome表層と8地質の露出Ground・自然WallパレットはBakerが固定表から自動登録するため、Inspectorの`definitions`へ重複追加しない。置換済みの共通Ground・Wall互換Definitionは登録しない。パレットは同じBaker内で材質・地形カタログへ解決し、行欠落、重複、重み合計、分類、自然Wall属性に不整合があればBakeを失敗させる。
- `SampleScene`へCamera、原点固定のGrid、Ground・静止液面・マグマ模様・岩壁・岩壁背面資源・通常前面資源・岩壁上資源のTilemap、表示用Component、SubScene参照を接続する。Cameraの`CameraPanController`を全Tilemap Presenterの共通表示原点として接続し、絶対初期位置をTransformへ直接保存しない。通常移動では512タイル未満の間はCameraの相対座標だけを更新し、閾値へ達するとGridを動かさず表示原点を所属チャンク境界へ自動更新する。構築途中の静止液面は仮配置し、製品接続後は静止液面とマグマ模様を論理セル原点へ固定する。岩壁上資源は製品接続後にWall根元から2タイル上へ揃える。GroundとRock WallはIndividual ModeかつTop Left順で描画する。土地境界はOrder 1、岩壁境界はOrder 4とし、生成済みの共通境界14 Spriteを両方へ接続する。通常資源は直前方の岩壁有無からOrder 2の背面用TilemapとOrder 10の前面用Tilemapへ振り分ける。
- 上記の一体型地形・12px・14形状境界の説明は構築途中のフォールバックに限定する。その後、41材質を製品カタログへ公開して製品地形表示を接続する。最終Sceneでは論理Wallセルを最下段Faceの根元とし、Wall上面・前面・折れ目影・Wall上資源を+2Yへ、固定接地影をその基準から手前3 pxへ配置する。通常資源は手前側2セル以内のWall根元で遮蔽される場合だけ背面用Tilemapへ振り分ける。
- Pixel Perfect Cameraの基準解像度480×270を含む、再現可能な確認環境を構築する。
- `Use Chunk Local Tilemaps`を既定ON、空き表示プール上限を8とする。自動原点変更では既存Tilemap・影と画面外の先行表示を再配置する。Presenterは可視範囲の外側1チャンクまで準備し、反映・消去合計4サブチャンク/フレームを維持する。OFFは旧共有方式との診断・比較用であり、原点変更直後の表示遅れが残る。

#### 実行後の注意

メニューから実行すると、アセットを変更する前に、未保存Sceneがある場合は保存確認を表示する。キャンセルした場合は構築を開始しない。
実行前に開いていたScene、各Sceneの読込状態、Active Sceneは退避され、構築の成功・失敗にかかわらず復元される。構築対象の`SampleScene`と`WorldGenerationSettingsSubScene`自体は保存されるため、復元後に同じSceneを開いていた場合は更新後の内容になる。
バッチモードの`WorldGenerationSceneBootstrap.Execute`は対話確認を行わないため、未保存Sceneを持たない専用Editorプロセスで実行する。

このツールはアセットとSceneを保存する。実行後は必ずGit差分を確認し、意図しないScene全体の書き換え、不要アセット、`.meta`のGUID変更がないことを確認する。

#### 次の修正をしたら確認する

- `WorldGenerationAuthoring`の設定項目、既定値、Baker変換を変更した。
- `DefinitionCatalogAuthoring`または鶏の定義GUID、生成条件、移動パラメータ、外見差分を変更した。
- バイオーム、地面、水域、マグマ、岩壁、資源の種類またはフォールバック色を変更した。
- Ground、Liquid Base、Magma Surface Pattern、Land Boundary Highlight、Rock Wall、Rock Wall Boundary Highlight、Resource、Rock Wall ResourceのTilemapレイヤー構成、Transform、描画Mode、Sorting Order、参照関係を変更した。
- Camera、Pixel Perfect Camera、共通表示原点、480×270の基準解像度、黒帯処理を変更した。
- `SampleScene`または`WorldGenerationSettingsSubScene`の生成構成を変更した。
- 土地・岩壁の元Texture、Sprite Slice、Atlas容量、Pixel Art向けAtlas設定を変更した。境界ハイライトの再制作時は、Sprite、Slice、Atlas、Scene参照を同じ変更で確認する。

#### 確認内容

1. Consoleに例外または新規警告が出ていない。
2. メニュー実行をキャンセルした場合はアセットやSceneが変更されず、実行した場合は成功・失敗のどちらでも元の複数Scene構成、読込状態、Active Sceneへ戻る。
3. 必要なPNG、Tile、Material、Sprite Atlas、`.meta`が揃っている。18枚の土地フォールバックは安定`BiomeId` 1〜18と一対一で、上面はBiome別の仮色一色とし、草・葉・砂紋やSoil模様を含まない。旧7種の土地フォールバックは存在せず、18枚が`GroundTerrainAtlas`の同じパック後Textureを参照する。本番用`terrain_rock_wall_01.png`と専用Tileが16×28、Pivot Y `8/28`で同じSpriteを参照し、`RockWallAtlas`へ収録される。共通境界PNGは16×16の14区画を持ち、`BoundaryHighlightAtlas`の同じパック後Textureへ収録され、Sceneの境界Sprite配列へ14件すべて接続される。
4. SampleSceneとSubSceneの参照がMissingになっていない。`WorldGenerationSettingsSubScene`の`DefinitionCatalogAuthoring`が鶏の定義GUIDと開発用初期値を保持し、共通定義の分類が`AnimalSpecies`になっている。Inspectorの`animalSpeciesDefinitions`を展開すると要素見出しが`0: Chicken`になり、GUIDが未設定または不一致なら未解決表示になる。World GridのTransformが原点・無回転・等倍で、Cameraの`CameraPanController`が`ChunkTilemapPresenter`と`InitialCameraPositionController`へ接続され、自動Floating Origin距離が512タイルになっている。
5. PlayModeで土地が16×24、岩壁が16×28の一体型ブロックとして表示される。土地は論理Biomeだけを反映し、同じBiome内では標準・肥沃・砂質のいずれも同じTileに見える。製品の静止液面・マグマ模様は同じ論理セル原点、岩壁内部影・露出資源は12px上へ表示される。Ground／Wallの境界ハイライトは14基本形と回転から選ばれ、20%のAlphaで表示される。液体はGround Tilemapへ重複せず、液面下辺と奥側ブロックの断面・側面は手前ブロックに隠れる。直前方が岩壁の通常資源は背面用Tilemapへ入り、それ以外は前面用へ入る。カメラをサブチャンク・チャンク境界へ寄せても立体Spriteが途中で消えない。
   ただし上記の一体型Wall・12px・14形状境界は中間フォールバックの検査である。最終製品Sceneでは41材質のWall上面と前面2段を使い、論理Wall根元が最下段Face、Y+1が1段目Face、Y+2がWall上面とWall上資源になること、動的Casterが論理根元と一致することを確認する。
6. Gameビューの地形描画領域が480×270基準の黒帯内へ収まり、HUDとF4表示も同じ領域を使用する。F3には現在タイルのサブチャンク座標、Biome、GeneratorVersion、16桁16進数のワールドシードが表示される。
7. 最大ズームアウトで通常移動とShift移動を行い、512タイル境界の前後でCamera、Tilemap、F3、F4、開発者用可視化が同じ絶対セルを示し、空白や旧セルの残留が見えない。Profilerでは`Farmware.WorldGeneration.FloatingOriginRebase`、`Farmware.WorldGeneration.TilemapPresentation`、`Farmware.WorldGeneration.ChunkStreaming`を記録し、原点変更フレームのCPU時間、GC Alloc、生成待ち数を変更前後で比較する。

ちらつき修正の回帰確認では、初期スポーン`(-45,-19)`からY=-18.5を保って右へ移動し、絶対X=448と960（チャンク`(7,-1)`と`(15,-1)`への進入）を確認する。F3／F4を表示し、地形だけが一度背景色へ抜けて矩形単位で復帰する現象がないこと、原点更新フレームに可視要求が8チャンク先へ飛ばないことを確認する。別シードではスポーンが異なるため、現在の表示原点から512タイルの境界を使う。

性能の一次計測条件と結果は[MAP-02D 性能計測](./Map02DPerformanceReport.md)を参照する。計測用の拡大Viewportは製品の最大ズームではない。また、原点変更なしの対照や60FPS目標は計測時だけの条件であり、Sceneへ保存しない。通常の再現確認は既定の512タイル閾値と12／36タイル/秒で行う。

Profilerでは、全体の`Farmware.WorldGeneration.TilemapPresentation`と原点変更本体の`Farmware.WorldGeneration.FloatingOriginRebase`に加え、`DisplayOriginSynchronization`、`PresentationWorkCollection`、`SubChunkClassification`、`SubChunkTilemapWrite`、`SubChunkTilemapClear`、`RockWallShadowBuild`を確認できる。岩壁影は`RockWallShadowMask`、`RockWallShadowOpacity`、`RockWallShadowGeometry`、`RockWallShadowUpload`へ分かれ、自動原点変更で既存Meshを移した時間は`RockWallShadowReposition`へ記録する。いずれも名前空間接頭辞は`Farmware.WorldGeneration.`である。子Markerは親Markerの時間に含まれるため、合算時に二重計上しない。C6-D5以降は旧`TerrainNeighborHaloBuild`を廃止し、有効地形の近傍参照費用を`SubChunkClassification`と`RockWallShadowMask`へ含める。

地形表示再利用の比較は、PlayMode中に`World Grid / Ground Tilemap`の`ChunkTilemapPresenter`で`Use Chunk Local Tilemaps`をONにして行う。既定はOFFで、比較用変更をSceneへ保存しない。切替直後は旧表示を消去して通常予算で再反映するため、表示完了を待ってから移動を始める。新方式では実行時の`Chunk Local Terrain`以下へチャンク内0〜63のTilemapを作り、自動原点変更は`ChunkTilemapReposition`で位置更新だけを計測する。消去済みの空きプール上限は`Maximum Pooled Chunk Presentations`（候補8）であり、可視範囲やECS保持数の上限ではない。停止・無効化・World切替で実行時表示を破棄する。

比較方式では、通常の原点変更2地点に加え、Y=64付近の上下チャンク境界、岩壁と影、水面、F3/F4、開発者可視化を確認する。現行URP 2DのOrthographic距離ソートへ小さい相対Z差を与えて上下順を保っているため、Camera方向や透明ソート設定を変える場合は再検証する。最大ズームとGPU / SetPass評価が残るため、既定方式への採用は未確定である。

生成の非同期公開比較では、`ChunkGenerationPublish`が完了確認後のECSコピー、`ChunkGenerationSchedule`が作業枠確保・Job投入を計測する。接頭辞はいずれも`Farmware.WorldGeneration.`である。`PresentationWorkCollection`と`ChunkStreaming`も同時に確認し、生成Jobの待ちが別の区間へ移っていないことを調べる。Workerの生成は`GenerateChunkJob (Burst)`で確認する。開発者可視化は従来どおり`ReadyToApply`／`Active`の完成Bufferだけを読むため、生成中の作業配列は表示しない。新規メニューやプレイヤー用キーは追加しない。

ANIMAL-01Bの個体群Worldでは、通常フレームで座標順の有限バッチを先行投入する。バッチが未完了なら`FixedTickReadinessState`で固定Tick自体を延期し、Main ThreadでJobを待たない。完成後の固定Tick入口で1バッチを一括公開して次を投入し、同じ描画フレームの追加追いつきTickは止める。待機開始時に到来済みのTickは最大1件だけ保持し、それを超える壁時計遅延は通常の追いつき上限と同様に破棄する。C3-C1では1チャンクの水文8行と地形・資源を9作業へ分け、行ごとに独立したHeap領域を使ってWorker上で並列生成する。`ChunkGenerationBoundaryWait`、`ChunkGenerationHydrologyBaseWait`、`ChunkGenerationLakeWait`、`ChunkGenerationTerrainWait`は、`IsCompleted`確認後の完成済みHandle確定費用を計測し、未完了Jobの待機時間は含めない。最大ズーム・通常／Shift移動・停止中移動で地形が順次表示され、再開後に`Animal Population`の確定位置が追加されることを確認する。候補条件・色・凡例・HUDは変更せず、生成中の専用配列も可視化へ渡さない。

`Animal Population`の確定位置は、全個体を毎Repaintで配列化せず、ECS Chunkの範囲・位置変更版・構造変更版を使って可視位置だけをキャッシュする。これは64×64論理地域とは異なる格納Chunk単位の粗い索引で、範囲が画面と重なるChunk内では個体ごとの座標判定を行う。初回・位置書込・構造変更時には該当Chunkの全座標を読み直すが、不変の画面外Chunkは範囲だけで除外する。Camera移動、個体移動、個体の削除、World切替で色が古いまま残らないことを確認する。広域個体が同じECS Chunkに混在する場合や全個体が毎Tick移動する場合の費用まで解消したとは扱わない。

岩壁影マスクの改善後も同じ`RockWallShadowMask`で比較する。80×80のマスク作成は自然地形と疎な差分を合成する同期ループであり、Workerへ未完了Jobを残さない。親の`RockWallShadowBuild`、後続の`RockWallShadowOpacity`も確認し、有効地形参照で後続処理へ負荷が移っていないか調べる。停止・再開時のNativeメモリ寿命、最終Water／Magmaと変更済みWallを挟むチャンク境界の影、部分表示時に完成入力を持つ領域だけへ影が出ること、原点変更前後の位置も確認する。部分反映のたびに公開Maskを更新する影の費用も同じMarkerへ含める。Rock Wall開発者可視化も製品表示と同じ有効地形近傍を読み、凡例とキー／メニューは変更しない。

影なしの停止を調べる場合は、`SubChunkClassification`と`SubChunkTilemapWrite`の同フレーム内の回数も確認する。書込MarkerにはTilemapの取得・再利用処理も含まれ、子の`ITilemap.FindAllRefreshPositions`だけを全書込時間とはしない。フレーム間隔はMain／Render ThreadのTimelineで`WaitForTargetFPS`、描画コマンド待ち、Renderer更新と照合し、表示Markerの時間やGPU時間と混同しない。CSV／PNGの出力と終了時の大きな停止は移動区間から除外する。詳しい帰属と制約は[影なしのスパイク切り分け](./Map02DPerformanceReport.md#影構築なしの表示スパイクとフレーム待機の切り分け)を参照する。製品の目標FPSや予算の既定値は、この診断だけで変更しない。

2026-10-28の現行地形表示は、通常12／高速36タイル毎秒をDevelopment Playerでも比較した。表示待ちとCPU・GPU・GC・メモリの費用、停止後の収束、観測コードの負荷、対象範囲は[地形表示の遅延調査](./Terrain01C6D5PerformanceReport.md)の第8章を参照する。計測は一時コピーの専用Componentで行い、製品メニュー・Scene・操作キーは追加していない。

分類用近傍の改善後は、`SubChunkClassification`を中心に比較する。内部・外周とも周囲8チャンクの完成自然地形と疎な差分から同じ有効地形を直接読み、旧260要素Haloの構築・フレーム間再利用は行わない。基礎生成へ戻るフォールバックはなく、ワールド内の必要近傍が未完成なら、その入力を読むサブチャンクだけ反映を待つ。通常表示は参照半径11タイルの必要Maskで部分ごとに判定し、待機座標をQueueの末尾へ戻す。Simulation側の不足要求も、可視矩形からの距離を優先して、先読み角より可視辺の入力を先に準備する。新しい近傍の到着時にも反映済み部分を維持し、地形編集や使用済みEntity世代の変更時には再反映する。最終Water／Magmaと変更済みGround／Wallの境界、露出岩壁の資源、岩壁手前の資源レイヤー、負座標、方式切替・停止再開を確認する。キー、メニュー、カメラ、開発者可視化の凡例は変更しない。

空レイヤーへの書込省略は比較ONのチャンク別Tilemapだけに適用する。`SubChunkTilemapWrite`は配列の空判定も含めて測り、`SubChunkTilemapClear`の全レイヤー消去は従来どおりである。以前タイルがあった領域へのnull書込は省かない。資源・液面・境界が残らないこと、部分反映からの消去とプール再利用、正負の原点変更、比較OFFへの切替を確認する。見た目の間引きやレイヤー削除ではなく、既知の空領域へ同じ空配列を再送しない処理である。

### 2.2 World Generation Sprite Atlas Prebuild

- 実装: `WorldGenerationSpriteAtlasPrebuildGenerator`
- 実行: Playerビルド直前にUnity Build Pipelineから自動実行する。専用メニューは持たない。

#### 用途

- Sprite Packer Modeを`Sprite Atlas V2`へ統一する。
- 新18Biomeの仮色土地PNG 18枚を`GroundTerrainAtlas`、旧フォールバック確認用`terrain_rock_wall_01.png`を`RockWallAtlas`のPackableへ登録する。続いて製品公開処理を実行し、41材質の通常候補328枚と境界あり11材質の完成候補352枚を`TerrainMaterialAtlas`へ登録する。削除済みの旧共通境界TextureとAtlasは再生成しない。
- Built-in配信、Point、Mip Maps Off、Read/Write Off、RGBA32、無圧縮、Padding 4 px、Rotation/Tight Packing/Alpha Dilation Off、Max Texture Size 512を再適用する。
- 対象Build Target向けにパックし、各Atlas内のSpriteが1枚のTextureページへ収まらない場合はビルドを中止する。製品`TerrainMaterialAtlas`は最大1024 px、そのほかは最大512 pxで検証する。

#### 確認内容

1. `WorldGenerationSpriteAtlasTests`が成功する。
2. Playerビルド前処理後もAtlasのPackableとImporter設定が維持される。
3. Gameビューで異なる土地バイオームが上下に接する場所でも、奥側断面が手前側上面を隠さない。

### 2.3 Debug Visualization

- メニュー: `Farmware > World Generation > Debug Visualization`
- 実装: `WorldGenerationDebugVisualizationMenu`
- PlayMode: 必須。対象Overlayが存在するSampleSceneの再生中だけ選択できる。

#### 用途

ワールド生成の内部値を色で表示し、通常の地形色だけでは発見しにくい分類ミス、境界、分布の偏りを調べる。
可視化は生成済みデータを読むだけで、ECS Simulationや生成結果を変更しない。
Gameビューでは`CameraPanController`の表示原点を使い、CameraとGridから得た相対セルを絶対タイル座標へ戻してECSを検索する。色面を描画するときは逆に絶対タイル座標から表示原点を引き、現在の相対セルへ投影する。独立したEditorWindowであるMap Viewerはこの変換境界を通らず、従来どおり絶対座標を直接扱う。

| モード | 確認できる内容 |
| --- | --- |
| `Off` | 開発者用着色を解除した通常表示 |
| `Biome` | 現行18種の論理バイオーム。中央HUDには最終選択理由、通常首位・次点、候補数、Score、Variation、両候補の8軸適性を表示 |
| `Ground Block` | 完成済み自然生成セルへ永続差分を重ねた有効地形が参照するGroundBlock Runtime ID |
| `Elevation` | 標高の連続値 |
| `Environment > Base Temperature` | 標高寒冷補正前の基礎気温 |
| `Environment > Terrain Temperature` | 標高寒冷補正後にBiome分類へ渡す地形気温 |
| `Environment > Base Moisture` | 現在土壌水分・水域と独立した基礎湿度 |
| `Environment > Woody Vegetation Potential` | 配置済み植生と独立した木本植生ポテンシャル |
| `Environment > Terrain Ruggedness` | 固定9標高サンプルから求めた局所起伏 |
| `Environment > Coastal Influence` | 標高だけから求めた海への近さと開放方向数の合成値 |
| `Environment > Volcanic Activity Potential` | 旧`Weirdness`と別Saltで生成する火山活動度 |
| `Geology > Class` | 8分類の固定地質 |
| `Geology > Permeability` | 地質Definition由来の透水性 |
| `Geology > Groundwater Storage` | 地質Definition由来の地下水保持力 |
| `Geology > Erosion Resistance` | 地質Definition由来の侵食抵抗 |
| `Geology > Flags` | カルスト適性、活動的火山適性、該当なし |
| `Geology > Dominant Site` | 支配サイトのマクロセル座標から作る安定色 |
| `Geology > Site Boundary` | 上下左右1タイルで支配サイトが変わるセル |
| `Liquid` | 液体なし、淡水、海水、マグマのLiquid Runtime ID |
| `Rock Wall` | 非岩壁、露出面、内部影の濃度 |
| `Magma` | 段階5の火山中核・追加活動度下限・形状密度をすべて満たす候補、最終マグマ本体、候補外 |
| `Resources` | 配置なし、木、石、砂の最終配置 |
| `Terrain Placement` | 共通配置APIで通常設備、耐熱性を問わない橋、耐熱橋だけ、全対象の配置不可となる地形を色分けし、中央HUDへ3要求の判定結果と耐熱不足理由を表示。補助物流のベルト可否はこの地形診断へ含めない |
| `Animal Population` | 全動物種のBiome・Ground条件を満たし資源予約のない候補タイル、候補外、初期補充で確定した個体位置を色分け |
| `Hydrology > Flow Direction` | 8方向の固定D8流向と流向なし終端 |
| `Hydrology > Terminal` | 海洋、閉鎖流域、4,096辺で確定できない長距離排水 |
| `Hydrology > Catchment` | 1〜4,096セルの集水面積を対数色で表示し、打切りセルを別色で表示 |
| `Hydrology > Surface Water` | 固定結果の流れなし、渓流、河川 |
| `Hydrology > Lake` | 閉鎖流域外、解決済み乾燥流域、打切り流域、固定湖候補、一時湖、恒久塩湖、閉鎖淡水湖、越流淡水湖を別色で表示 |
| `Hydrology > Groundwater` | 固定地下水ポテンシャル0〜65,535と接続境界32,768 |
| `Hydrology > Natural Soil Saturation` | 現在土壌水分と独立した固定自然土壌飽和ポテンシャル0〜65,535 |
| `Hydrology > Spring` | 最終固定表流水の泉流と、それ以外のタイル |
| `Hydrology > Natural Flooding` | 現在水位と独立した固定自然冠水ポテンシャル0〜65,535 |
| `Hydrology > Wetland Candidate` | 湿原条件外、局所資格タイル、4水文セル以上の広域湿原規模 |

流向から地下水までの水文セル表示は8×8タイル単位、自然土壌飽和、泉、自然冠水、湿原候補は1タイル単位で表示する。中央HUDは従来の水文セル値に加え、固定地下水、自然土壌飽和、自然冠水、泉、淡水接続、湿原資格、広域湿原規模を表示する。`BasinTruncated`は固定湖候補と誤認しないマゼンタ、固定湖候補は濃青、一時湖は茶、恒久塩湖は紫、閉鎖淡水湖は青、越流淡水湖はシアンで区別する。最終区分は実際の湖面セルだけへ設定するため、流域共通の`Overflow`を持つ湖面より高い上流セルをシアンにはしない。派生5モードは生成済みの`GeneratedHydrologyCell`、`GeneratedSurfaceWaterTile`、`GeneratedHydrologyTile`だけを読み、現在降雨、灌水、排水、現在土壌水分、現在水位を固定生成色へ混ぜない。

#### 次の修正をしたら確認する

- ノイズ、バイオーム分類、標高、基礎・地形気温、基礎湿度、木本植生、起伏、海洋影響、火山活動度、地質分類・特性・サイトを変更した。
- 川、池、海、淡水・海水、マグマ、岩壁、資源、地形配置属性の生成規則または列挙値を変更した。
- `GeneratedTile`、ECS Buffer、Presentationへ渡すデータを変更した。
- 可視化の色、凡例、HUD、Tilemap、Camera、黒帯、表示範囲を変更した。
- `CameraPanController`、`DisplayOrigin`、Gridと絶対タイル座標の変換境界を変更した。
- 動物種の対応バイオーム・有効地形、地域生成、個体位置を変更した。
- 新しい生成要素を追加し、通常表示だけでは内部判定を確認できなくなった。

#### 確認内容

1. すべてのモードが例外なく切り替わり、選択中メニューへチェックが付く。
2. 凡例の色とマップ上の色が一致し、文字が欠けない。
3. 黒帯へ着色や凡例がはみ出さない。
4. チャンク境界をまたいでも分類やグラデーションが不自然に途切れず、`Terrain Placement`で陸地・水・マグマ・岩壁の契約を判別できる。中央HUDでは陸地が設備、水が非耐熱・耐熱の両橋、マグマが耐熱橋だけを`Allowed`とし、マグマの非耐熱橋を`Heat Required`と表示する。`Magma`では活動度段階4の火山周縁を候補色にせず、段階5・追加下限・形状密度を満たす色面だけが中央HUDの`Candidate=Yes`と一致する。ベルト固有の判定や凡例は表示しない。
5. 正負の遠方へ明示的にCameraを移した場合も、色面が表示中のTilemapと同じ絶対セルへ重なり、中央HUDがCamera中央の絶対タイル情報を示す。
6. `Animal Population`で候補外、候補タイル、確定個体が凡例どおりの3色になり、確定個体色が候補色より優先される。草原陸地でも木・石などの資源があるセルは候補外になる。
7. 地質7モードで分類・3特性・フラグ・支配サイト・サイト境界が個別表示され、中央HUDの分類、フラグ、特性、サイト座標と一致する。サイト境界は同じ支配サイト内部へ出ず、隣接1タイルでサイトが変わる箇所へ出る。
8. 基礎水文5モードがチャンク境界をまたいでも8×8セル単位で連続し、中央HUDと凡例が生成済み`GeneratedHydrologyCell`に一致する。Lakeでは打切り流域と固定湖候補が別色になる。
9. 派生水文5モードが個別に切り替わり、地下水は接続境界、自然飽和・自然冠水は連続値、泉は最終`SpringStream`、湿原候補は条件外・局所候補・広域規模を凡例どおりに表示する。中央HUDが生成済み3 Bufferと一致し、現在水文を表示しない。高速移動で1チャンク先読みを超えた場合は一時的な表示遅延を許容するが、停止後に生成済みチャンクの表示へ収束し、例外や永続的な欠落がないことを確認する。
10. `Off`へ戻すと通常表示へ完全に戻る。

TERRAIN-01C1で、旧7Biomeの代表土壌との差だけを見る`Biome Transition`と、旧7Biome互換値だけを見る`Weirdness`モードを削除した。TERRAIN-01C5-Cで旧`Weirdness`の生成・保持・状態ハッシュも削除し、旧モードととも再利用しない。

TERRAIN-01C2-Aでは地質DefinitionとSimulation生成結果だけを先に接続し、C2-Bで上記の地質7モード、凡例、中央HUDを追加した。地質結果は既存`Biome`、`Terrain`、`Volcanic Activity Potential`表示へ暗黙に混ぜず、選択した地質モードだけで表示する。

### 2.4 Analyze Distribution

- メニュー: `Farmware > World Generation > Analysis > Analyze Distribution`
- 実装: `WorldGenerationDistributionAuditMenu`、`BiomeDistributionAnalyzer`
- PlayMode: 不要。EditModeとPlayModeのどちらからでも開始できるが、比較条件を揃えるためEditModeを推奨する。

#### 用途

C5-Eで、完成水文より前の単一タイル生成を使う旧分布監査を削除し、C3〜C5-Cを製品と同じ順序で実行する完成チャンク監査へ置換してメニューを再有効化した。既定条件は`biome-audit-temperate`、`biome-audit-hydrology`、`biome-audit-volcanic`の3 Seed、それぞれ原点を中心とする3×3チャンクである。処理はバックグラウンドで直列実行し、同じメニューの多重実行を禁止する。実行中もEditor操作は継続でき、完了時にConsoleへレポートと所要秒を表示する。全153ペアをConsoleの1メッセージ上限で欠落させないよう、レポートは行を分断せず約12,000文字ごとの`part`へ分ける。

Consoleレポートには、次の比較材料を出力する。

- 18Biomeごとの最終面積率、通常候補率、通常首位率、候補8軸の平均適性とDefinition重み込みの平均Score寄与。
- 4近傍の領域数、最大面積、1〜3タイルの小領域数、監査窓端へ接した領域数。
- 全153異種Biomeペアをゼロ件も省略せず、境界辺数、全陸地辺比、異種辺比として記録した台帳。
- 各異種ペアの地形気温、基礎湿度、標高、木本植生、起伏、火山活動度、海洋影響の平均絶対差と最大絶対差。
- 通常首位、7軸の離散区分、地質、最終選択理由、固定水文の差を重複可能な11理由として数えた接続根拠。
- 火山中核・通常候補の火山周縁、淡水湿原・湿地林への水文Host昇格件数、同じ入力から同じ結果が得られたか比較する署名。

候補なし、完成Bufferと再分類の不一致、火山中核条件、水文特殊Host契約、完成チャンク生成のいずれかが失敗した場合は、部分的な正常レポートを出さず例外としてConsoleへ記録する。海域は`BiomeId.None`の正常値として地上候補監査から分離する。通常候補資格を持たない水文特殊2Biomeは、候補率ではなくHost昇格件数と最終面積で確認する。

このレポートはバランスの合否を自動決定しない。C6の最終Block表示後にC7で同一条件を比較し、Gameビューで見た地形の遊びやすさと合わせて合格範囲を決める。

#### 次の修正をしたら確認する

- バイオームのノイズ、スコア、閾値、隣接推奨・隣接抑制、移行帯を変更した。
- 川、池、海、マグマ、岩壁、資源など、地形面積へ影響する生成規則を変更した。
- シード、GeneratorVersion、PurposeSalt、座標ハッシュ、チャンクサイズを変更した。
- 生成アルゴリズムの性能または決定性へ影響する処理を変更した。

#### 確認内容

1. Consoleへ開始ログと完了レポートが1回ずつ出る。
2. すべての想定バイオームと地形が0件になっていないか確認する。
3. 強い隣接抑制の直接接続、孤立領域、領域サイズが変更意図に沿う。
4. 同一コード・同一条件で再実行した署名が一致する。
5. 大きな比率変化がある場合は、ランダムマップ生成仕様の調整値と説明も更新する。

### 2.5 Analyze Geology Distribution

- メニュー: `Farmware > World Generation > Analysis > Analyze Geology Distribution`
- 実装: `GeologyDistributionAuditMenu`、`GeologyDistributionAnalyzer`
- PlayMode: 不要。既定ではバックグラウンドで実行する。

#### 用途

既存の分布監査と同じ固定3シードを32タイル間隔の129×129代表格子で生成し、地質8分類の比率と地域形状を同条件で比較する。シード同士を連結せず、4近傍の領域数、1サンプル孤立領域、最大概算面積、外接矩形対角による最大概算直径、異種地質の概算境界長を出力する。標高区分と火山活動度5段階のクロス件数をConsoleへ出力する。C5-C以降のBiomeは完成水文を必要とするため、この基礎地質監査ではBiomeクロスを偽集計せず、C5-Eの完成Biome監査へ分離する。

面積、直径、境界長は代表格子による比較用の概算であり、全タイル走査の厳密値ではない。分布の合否閾値は設定せず、調整前後の同条件レポート、Gameビュー、Map Viewerを合わせて判断する。実行中は同じ監査を二重起動できない。

#### 確認内容

1. Consoleへ開始ログと完了レポートが1回ずつ出る。
2. 8分類の件数合計が全代表サンプル数と一致し、標高・火山活動度の各クロス件数も分類件数と一致する。BiomeクロスはC5-E前に出力されない。
3. 同一コード・同一条件で再実行した署名と集計値が一致する。
4. 比率、領域、孤立、境界の変化をGameビューとMap Viewerで確認し、任意の数値だけで合否を決めない。

### 2.6 Analyze Animal Population

- メニュー: `Farmware > World Generation > Analysis > Analyze Animal Population`
- 実装: `AnimalPopulationDistributionAuditMenu`、`AnimalPopulationDistributionAnalyzer`
- PlayMode: 不要。既定ではバックグラウンドで実行する。

#### 用途

固定した3組の地形Seed・個体群Seedについて、原点周囲9×9地域、合計243地域と約100万タイルを調べる。実際の地形・資源生成器と`AnimalPopulationGenerator.TryAppendSpeciesPopulation`を使用し、鶏の有効タイル数、面積比例の目標数、生成候補数、未充足地域、群れサイズ・徘徊距離違反、位置重複、比較署名をConsoleへ出力する。

資源のあるセルは生成候補から除外するが、報告する有効タイル数と目標数はBiome・Ground条件に合う地形面積を基準とする。資源により候補が減っても密度の基準を変更せず、配置不能分は未充足として集計する。

この監査は密度8を最終的な合格値とは扱わない。調整前後の同一条件比較と、Gameビューの`Animal Population`表示を組み合わせて判断する。動物種の初期値を変更した場合は、監査が使う定義値も同じ変更で更新する。

#### 確認内容

1. Consoleへ開始ログと完了レポートが1回ずつ出る。
2. 不正群れと重複位置が0件である。
3. 同一コード・同一条件で再実行した署名と集計値が一致する。
4. 抽選した群れを先に置くと後続の残数を有効な群れへ分割できない場合は、有限バックトラックで別の群れサイズ・中心・メンバーの組合せが再探索される。中心とメンバーのタイル選択は合計4,096回までに制限される。完全配置が不可能または探索上限へ達した後も残る目標個体数と確定候補数の差、および未充足地域数が地形分断と群れ最小数の方針に合う。

### 2.7 Analyze Hydrology Distribution

- メニュー: `Farmware > World Generation > Analysis > Analyze Hydrology Distribution`
- 実装: `HydrologyDistributionAuditMenu`、`HydrologyDistributionAnalyzer`
- PlayMode: 不要。既定ではバックグラウンドで実行する。

#### 用途

固定3 Seedについて原点周囲の5×5チャンクを実ゲームと同じ`DeterministicHydrologyGenerator`で生成し、合計4,800水文セルの流向、終端、集水打切り、渓流・河川、閉鎖流域、固定湖候補、湖面、越流を集計する。流域数はSeedと下流終端座標で重複除去し、全固定分類から比較署名を作る。分布の合否閾値は設定せず、調整前後の同条件レポートと2種類の可視化を合わせて判断する。実行中は同じ監査を二重起動できない。

#### 確認内容

1. Consoleへ開始ログと完了レポートが1回ずつ出る。
2. 流向別件数と終端別件数がそれぞれ全セル数に一致し、河川数が渓流数以下、湖面数が固定湖候補数以下になる。
3. 解決済み、打切り、固定湖候補の流域数と、集水面積・流出相当値・流域面積の最大値が表示される。
4. 同一コード・同一条件で再実行した署名と集計値が一致する。

### 2.8 Analyze Derived Hydrology Distribution

- メニュー: `Farmware > World Generation > Analysis > Analyze Derived Hydrology Distribution`
- 実装: `HydrologyDistributionAuditMenu`、`DerivedHydrologyDistributionAnalyzer`、`HydrologyDiagnosticChunk`
- PlayMode: 不要。既定ではバックグラウンドで実行する。

#### 用途

固定3 Seedについて原点の各1チャンクをC3、C4-A〜Dの製品Generatorと同じ順序で完成生成し、合計192水文セルと12,288タイルを集計する。地下水接続、自然土壌飽和、最終湖4区分、泉流、自然冠水、湿原資格、広域湿原規模の件数・割合、3ポテンシャルの最大値、監査対象値の比較署名をConsoleへ表示する。Scene、Prefab、ECS World、生成アセットは変更しない。

一時湖、塩湖、閉鎖淡水湖、泉などが既定の有限試料で0件でも監査失敗にはしない。0件を省略せず表示し、Seedまたは生成設定を変えた同条件のレポート、C4-E2／E3の可視化、境界値の固定テストを合わせて判断する。既定3 Seedの実行中は同じ監査を二重起動できない。

#### 確認内容

1. Consoleへ開始ログと完了レポートが1回ずつ出る。
2. 3チャンク、192水文セル、12,288タイルを集計し、各分類の0件も明示する。
3. 地下水、自然飽和、自然冠水、湿原資格、広域規模の件数が対応する母数以内で、最終湖4区分の合計が水文セル総数以内になる。
4. 同一コード・同一条件でMain ThreadとThreadPoolから再実行した署名とレポートが一致する。

### 2.9 Map Viewer

- メニュー: `Farmware > World Generation > Map Viewer`
- 実装: `WorldGenerationMapViewerWindow`、`WorldGenerationMapSampler`、`WorldGenerationMapViewModeCatalog`
- PlayMode: 不要。PlayMode中に開いた場合は、実行中ECSの`WorldGenerationSettings`を優先する。

#### 用途

MinecraftのBiome Finderのように、無限ワールドの広い範囲を独立したEditorWindowで確認する。
シード、中心タイル座標、表示モード、ズームを指定でき、ドラッグ移動とカーソル中心のホイールズームに対応する。

Farmwareのワールドは無限なので「全体」を有限画像へ収めることはできない。最大ズームでは、画像1ピクセルにつき256タイルを代表サンプルとして、現在位置を中心に非常に広い範囲を確認する。

#### 実マップとの一致

- EditModeでは`WorldGenerationSettingsSubScene`をHierarchyへ影響しないPreview Sceneとして読み込む。
- `WorldGenerationAuthoring.CreateWorldGenerationSettingsSnapshot`をBakerとMap Viewerで共有する。
- 設定SubSceneの全Root・全子孫を検査し、`WorldGenerationAuthoring`が0件または複数件なら生成を停止する。同じRoot配下の兄弟GameObjectにある重複も設定エラーとして扱う。
- PlayModeでは、`ChunkGenerationSystem`が使用中のECS Singletonを直接読む。
- `Terrain`と`Biome`は`HydrologyDiagnosticChunk`でC4水文まで完成したチャンクを生成し、製品ECSと同じC5-C最終化を適用する。連続環境軸と基礎地質の表示は用途に応じた純粋生成を使い、ビューア専用の簡易Biome分類は持たない。
- 全27表示モードの表示名、実行経路、解像度上限、進捗方式、Hover用データ契約は`WorldGenerationMapViewModeCatalog`を正とする。完成チャンク、C3基礎水文セル、単一タイルの経路を用途別に維持し、WindowとSamplerがモード範囲や負荷上限を別々に推測しない。
- 表示領域が生成Textureの上限を超える場合は、縦横共通の縮小率でTextureを生成し、正方形のタイルとグリッドを保つ。連続環境軸・地質の通常モードは最大768サンプル、C3基礎水文5モードは1サンプルごとに最大4,096セルの集水・流域探索を行うため最大64サンプルとする。`Terrain`、`Biome`、C4派生水文5モードは1チャンク数秒の完成診断生成を行うため、縮尺ごとに代表タイルの所有チャンクが各軸最大3件となる1〜64サンプルへさらに制限する。サンプラー入口でも同じ上限を検証し、Windowを経由しない呼出しから数百チャンクを生成しない。派生水文で担当8×8セルが正側隣接チャンクに属する場合はその完成チャンクも生成するため、完了表示には実際に使った完成水文チャンク数を示す。Windowリサイズで縦横比が変わった場合は、操作停止後に新しい比率のTextureを自動再生成する。生成TextureとCanvasの表示倍率はPointer座標、パン、ズーム、境界線、原点表示へ反映する。Pointer座標とカーソル中心ズームは、Point Filterで実際に表示されるTextureピクセルの代表タイルへ一致させる。カーソル中心ズームの次解像度も表示生成と同じモード別上限を使い、完成チャンク使用モードを未描画の768サンプルとして逆算しない。グリッドと原点表示は、Texture解像度が奇数の場合も地形生成と同じ整数中央のサンプル境界へ揃える。ツールバーの＋・－ButtonはCanvas中央をPointerへ変換せず、現在の中心タイル座標を直接維持する。再生成直後のHover情報もCanvas中央の境界をPointerとして解釈せず、Side Panelと同じ中心タイルを直接表示する。
- 完成自然地形はWindow単位で最大32チャンクのManaged値コピーを保持し、パン・ズーム・モード切替で重なる範囲を再利用する。上限超過時は古い登録順に除去する。Seed・GeneratorVersionを含む全固定設定の変更、Window終了、再コンパイル後のUI再作成で失効する。各要求へ独立したNative配列を復元し、要求終了時に破棄する。編集差分は毎回最新Snapshotから適用し、自然地形キャッシュへ混ぜない。生成開始前に今回使う既存チャンクを除去順の末尾へ移し、未訪問分の追加で同じ要求の入力が失われないようにする。未訪問のSurface完成地形は、画像要求だけが所有する専用Editor Worldで製品の`ChunkGenerationSystem`のBurst Jobを1件ずつ実行して用意する。Player LoopやDefault Worldへ接続せず、固定Tick・動物地域初期化を実行しない。完成後にManagedコピーを保持し、サンプリングは事前生成済みデータを必須として、同期診断生成へフォールバックしない。キャッシュ不足分の初回生成と重複範囲の再表示をそれぞれ短縮する。計測と残件は[地形表示の遅延調査](./Terrain01C6D5PerformanceReport.md)を参照する。
- 実設定を取得できない場合は生成を停止し、別の既定値へフォールバックしない。
- 初回はCanvasの両辺が有限値かつ64ポイント以上になるまで生成を待ち、未確定の`NaN`寸法から1×1画像を作らない。画像完成までは地図領域の中央と画面下部へ生成進捗の割合を表示する。連続環境軸・地質の通常モードと基礎水文は完成行数、`Terrain`、`Biome`、派生水文は処理時間の大半を占める完成チャンク数へ95%、色・Hoverコピーを作る画像行へ残り5%を割り当てる。完成チャンク使用モードはGate待機開始時に1%を通知し、16行未満の低解像度でも完成まで0%のままにしない。再生成時は前の画像を維持し、下部の進捗を更新する。Hover情報は初回からBiome選択診断を含む最大17行分の高さを確保し、完成後に情報欄が地図を縮めて重い完成チャンクを二度生成しないようにする。Window最小寸法は720×700ポイントとし、地図の最小240px、Hover診断280px、ヘッダー、操作欄、状態欄を同時に表示して、親Panelのクリップで地図や診断を欠けさせない。

`Terrain`は完成チャンクの`GeneratedTerrainCell`へ任意の`TerrainCellDelta`を適用し、Liquid、Wall、Groundの優先順で現在の有効地形を表示する。Ground／WallはRuntime IDを41材質の安定IDへ解決した決定論的な診断色とし、Wallは同じ色相を暗くする。凡例は41材質、淡水、海水、マグマを示す。長い材質名はサイドパネル幅に合わせて折り返し、14pxの色見本は縮小しない。Hoverには同じセルのGround／Wall Runtime ID、Ground／Wall安定材質ID、Liquid、TerrainSurface Runtime IDを表示し、環境タイルから旧地形分類を再構築しない。PlayModeでMap ViewerのSeed・GeneratorVersion・全設定が実行中ECSと一致する場合は、永続差分Bufferをメインスレッドで不変Snapshotへコピーしてバックグラウンド生成へ渡す。取得元欄には「実行中Worldの有効地形」と実際に適用した代表サンプル数を表示する。設定が異なる場合やEditModeでは「自然生成（同じ設定の実行中Worldなし）」と明示し、自然生成を現在地形と誤表示しない。PlayModeでは`Debug Visualization > Ground Block`と`Liquid`で有効Block参照を個別に確認できる。
`Biome`は水域や障害物の下も含む論理バイオームを表示する。`Terrain`と`Biome`のHover／中央情報は、地上タイルについて製品最終化と同じ固定入力から得た最終選択理由、通常首位・次点、候補数、Score、Variation、両候補の8軸適性を表示する。最終IDと理由は2行、各候補の名前と順位値は2行、8軸適性は候補ごとに3軸・3軸・2軸の3行へ分ける。診断全体を常に12行とし、430px幅のGameビュー内容領域で右端を切らず、Map Viewerの自動折り返しに依存しない。GameビューのDebug Visualizationは全モード共通の右上1パネルを使い、内容が描画領域の高さを超える場合だけ右端の縦スクロールバーを表示して末尾まで確認する。高さが収まる場合はバー用の幅を予約しない。ゲーム側のホイール操作と競合させないため、HUDはマウスホイールを取得せず、表示中のバーをドラッグまたはクリックした場合だけ移動する。スクロール中は文字Styleを`Clip`とし、HUDの上下枠より各2px内側へ固定した明示クリップ範囲で文字と凡例を囲んで外へ描かない。モード変更時は先頭へ戻る。パネル高は実際に描く文字列の行数から求め、末尾には6pxの余白だけを設ける。Biomeでは18Biome凡例、`Geology`・`Volcanic`・`Coastal`を含む選択診断、資源・暫定土壌までの共通詳細を同じ順序で保持し、省略しない。海域は地上Biome選択の対象外と明示する。診断値は画像生成時にManaged値へコピーし、Pointer移動ごとに完成チャンクや分類を再実行しない。`Elevation`、`Base Temperature`、`Terrain Temperature`、`Base Moisture`、`Woody Vegetation Potential`、`Terrain Ruggedness`、`Coastal Influence`、`Volcanic Activity Potential`はGameビューのDebug Visualizationと同じRGBで連続環境軸を表示する。`Geology Class`、3基礎特性、`Geology Flags`、`Geology Dominant Site`、`Geology Site Boundary`もGameビューと同じ色定義を使う。`Geology Dominant Site`の凡例色は固定の分類色ではなく、サイト座標から生成される実際の色の例であるため、地図には凡例と異なる色も表示される。同じサイトは同じ色を保ち、正確な境界確認には`Geology Site Boundary`を使う。サイト境界は広域縮小でも確認できるよう、上下左右の隣接代表サンプルとのサイト差を表示するため、Gameビューの隣接1タイル判定より太く見える場合がある。

Gameビューの開発者HUDは`OnGUI`内の`UNITY_EDITOR`条件で描画開始を製品版から除外する。一方、Map Viewerと共有する副作用のないBiome診断Formatterは、Playerにも含まれるPresentation Assembly内の呼び出し元と同じコンパイル範囲へ置き、非Editorコンパイルで未定義参照を作らない。

C3基礎水文5モードはGameビューと同じRGBで、流向、終端、集水面積、渓流・河川、流域・湖を表示する。`Hydrology Lake`だけはC4-Cの水収支まで生成し、一時湖、恒久塩湖、閉鎖淡水湖、越流淡水湖をGameビューと同じ色で表示する。C4派生水文5モードは完成チャンクを非同期に生成・再利用し、`Hydrology Groundwater`、`Hydrology Natural Soil Saturation`、`Hydrology Spring`、`Hydrology Natural Flooding`、`Hydrology Wetland Candidate`を表示する。RGBと分類条件はC4-E2のGameビュー色変換APIを共有する。完成チャンク生成はEditorプロセス内で同時に1要求へ制限し、連続操作で取消済み要求と最新要求の大容量Native配列が重複しないようにする。取消済み要求は実行中1チャンクの完成後に終了するため、最新要求の開始まで数秒待つ場合があるが、Editorメインスレッドは停止しない。完成Blockセルに必要なDefinition・Terrain・材質・Palette Blobは、Unity EntitiesのBlob完成処理が内部でMain Thread限定の`Allocator.Temp`を使うため、Job投入と`Task.Run`へ入る前にEditorメインスレッドで1組だけ構築する。完成済みの読み取り専用Blobを同じ画像要求の全診断チャンクで共有し、専用Worldの破棄とThreadPool処理の終了を待ってから破棄する。取消時も投入済み1件が完了するまでEditorへ制御を返し、次のJobを投入せず専用Worldを破棄する。診断チャンクのNative配列は`Persistent`で所有し、画像生成終了時に破棄する。生成中に前の画像を維持する場合、Hover診断もDropdownの新モードではなく画面へ適用済みの旧画像モードを参照し、未完成の派生水文を取得失敗として表示しない。Hover／中央情報には生成時にManaged値へコピーした地下水、接続状態、自然飽和、泉、自然冠水、淡水接続、湿原資格、広域規模を表示するため、Pointer移動で重い再生成を行わない。現在降雨・灌水・排水・現在土壌水分・現在水位は表示しない。基礎水文のHoverと中央情報には固定水文セルの全診断値と最終湖区分を追加し、Lake凡例では打切り流域を`Truncated basin (not a fixed lake)`として固定湖候補と区別する。木、石、砂の資源分布は現時点では対象外である。

#### 画面上部の入力欄・ボタン

| 項目 | 説明 |
| --- | --- |
| `Seed` | 確認するワールドのシード文字列。値を変更して`移動・再生成`を押すと、Sceneの設定を変更せずに別シードを比較できる。 |
| `X` / `Y` | 画像中央に表示するタイル座標。負座標も指定できる。 |
| `表示` | `Terrain`は最終地形、`Biome`は論理バイオーム、TERRAIN-01C1の8連続環境軸、TERRAIN-01C2の地質7モード、TERRAIN-01C3の基礎水文5モード、TERRAIN-01C4の派生水文5モードへ切り替える。変更すると現在位置を再生成する。 |
| `Grid` | 16×16タイルのサブチャンク境界と64×64タイルの論理チャンク境界の表示を切り替える。生成結果自体は変化しない。 |
| `移動・再生成` | 入力中のSeedとX/Yを確定し、その地点の画像を生成し直す。 |
| `実設定を再読込` | EditModeではAuthoring、PlayModeでは実行中ECSから生成設定とSeedを読み直し、現在位置を再生成する。入力した一時的なSeedは破棄される。 |
| `原点` | 中心座標を`(0, 0)`へ戻し、現在のSeedと表示モードで再生成する。 |
| `＋` | 中心座標を保ったまま拡大し、より細かな地形を表示する。 |
| `－` | 中心座標を保ったまま縮小し、より広い範囲を表示する。 |

地図上をドラッグすると表示範囲を移動できる。マウスホイールでは、カーソルが指している地点を基準に拡大・縮小できる。

#### 次の修正をしたら確認する

- `WorldGenerationAuthoring`の地形設定、Baker、設定SubSceneを変更した。
- `DeterministicChunkGenerator`、`GeneratedTile`、Biome、Ground、水域、地質分類・特性・サイトの生成を変更した。
- `HydrologyDiagnosticChunk`、地下水、自然飽和、泉、自然冠水、湿原候補の固定値または分類条件を変更した。
- チャンク・サブチャンク寸法、負座標変換、シード変換、GeneratorVersionを変更した。
- Map Viewerの色、凡例、モードCatalog、Hoverデータ契約、地形差分Snapshot、座標変換、パン、ズーム、非同期処理を変更した。

#### 確認内容

1. 原点付近と既知の遠距離座標で、Map ViewerとGameビューのバイオーム・水域・マグマ・岩壁、8つの連続環境軸、地質7モード、C3基礎水文5モード、C4派生水文5モードが一致する。Biomeモードでは同じ地上タイルの最終理由、通常首位・次点、候補数、8軸適性が両画面で一致し、海域は対象外と表示される。同じBiome内で表層パレットのGround／Wall材質が切り替わる場所は、Map Viewerの材質色・Hover安定IDと`Debug Visualization > Ground Block`のRuntime ID色で区別できる。実行中Worldに地形差分がある場合は取得元欄が有効地形となり、Gameビュー、開発者中央情報、Map Viewer Hoverが同じ変更後Blockを示す。サイト境界は、Gameビューでは隣接1タイル、Map Viewerでは隣接代表サンプルという縮尺差を踏まえて同じ支配サイト変化を示す。水文Lakeでは打切り流域と固定湖候補が別色になり、一時湖、恒久塩湖、閉鎖淡水湖、越流淡水湖の色・凡例・最終区分表示が両画面で一致する。派生水文では5つの凡例、色面、Hover／中央固定値が選択モードと一致する。
2. `実設定を再読込`後のSeed、WorldSeed、GeneratorVersion、設定取得元が想定どおり表示される。
3. 負座標への移動、ドラッグ、ホイール、＋・－Buttonで中心と縮尺が正しく変化する。＋・－ButtonではTexture解像度が奇数・偶数のどちらでも中心タイル座標を動かさない。再生成直後のHover情報は、偶数解像度でもSide Panelと同じ中心座標を示す。Windowを768ポイントより大きくした場合と、生成Textureの幅または高さが奇数になる大きさにした場合のどちらでも、カーソル下の座標、グリッド、原点印が地形とずれず、正方形のグリッドが維持される。基礎水文を通常サイズで表示し、中央以外へPointerを置いてホイールズームした場合も、64サンプルのTexture上でPointer下の代表タイルを維持する。派生水文では縮尺に応じた低解像度へ切り替わっても同じ地点を維持し、連続操作時は前の要求をキャンセルして最新画像だけを反映する。
4. 同じSeed・座標・縮尺へ戻ると同じ画像になる。
5. 開いた直後に単色の1×1画像を引き伸ばさず、地図領域に生成進捗が表示され、完成後に画像へ切り替わる。連続操作で古い非同期結果や進捗が後から上書きされず、Editorが長時間応答不能にならない。

### 2.10 Terrain Tile Art Generation Settings Inspector

#### 用途

地形タイル試作工程の16×16 px手描き入力から、`Face`と`FaceBoundaryOverlay`を事前合成した断面1段目Spriteを生成する。`GroundTop`はGround／Wallで同じ入力Spriteを共有し、生成ツールでは複製しない。Runtime表示やSimulation状態は変更せず、後続の地形表示が参照する境界合成PNGの制作工程だけを担当する。

Projectビューの`Create > Farmware > Terrain Tile Art Generation Settings`から設定Assetを作成する。Inspectorでは次を編集・確認できる。

`TerrainTileArtGenerationSettings`と材質入力型は`Farmware.WorldGeneration.Editor` Assemblyに置き、Player対応のAuthoring Assemblyへ含めない。設定Asset、手描き制作入力、検品用生成物、Development Scene／Shaderは製品Build Sceneから参照しない。`TerrainProductIsolationTests.EnabledBuildScenes_DoNotDependOnDevelopmentOnlyAssets`が、Build Settingsで有効な全Sceneの再帰依存を通常のEditModeテストで検査する。

#### 材質入力PNGの自動Import

`Assets/Farmware/Art/WorldGeneration/Source/`配下へ64×48 pxのPNGを追加すると、材質専用Texture Postprocessorが`.meta`のない初回Importを記録し、Project変更後の監視がPoint、16 PPU、非圧縮、Mip Mapなし、Clamp、sRGB、Read/Write有効、`Full Rect`、Sprite Multipleを自動適用して16×16 pxを4列×3行へ分割する。制作画像の上段左から`<ファイル名>_0`～`_11`の順とし、上段4枚を`GroundTop`、中段4枚を`Face`、下段4枚を`FaceBoundaryOverlay`として使用する。Asepriteから新規PNGを直にExportした直後、Unityが透明領域基準の仮区画を作った場合も、同じ初回Import中に限って仮区画を破棄し、規定の12区画へ置き換える。

既存のSprite区画とSprite IDは、Sceneや設定Assetの参照を維持するため自動で作り直さない。境界なしの既存材質は上2行だけの8区画、境界候補を持つ材質は全3行の12区画を許可する。初回Import完了後に規定位置、中央Pivot、規定名と一致しなくなった場合は、自動修復せずConsoleへ理由を表示する。`.meta`を削除せず、画素修正は同じPNGパスへの上書きで行う。別フォルダーのPNG、液体、動物、植物、UI、Animation Spriteにはこの規則を適用しない。

#### マグマHDR発光・Bloom・岸照明

2026-10-28にHDR色、Bloom Intensity 0.4、岸の橙色照明を採用した。接続は`Connect Product Terrain Presentation`の`ConfigureMagmaBloom`で行う。製品CameraのHDRとPost Processingを有効にし、Default LayerのGlobal Volume `Terrain Magma Bloom`へ`Assets/Settings/TerrainMagmaBloom.asset`を割り当てる。再接続は同じVolumeとProfileを再利用し、ProfileのInspector調整値を上書きしない。採用後の一時比較ウィンドウと専用テストは削除した。

表面の色は`TerrainMagmaSurfacePattern.mat`のHDR Colorで調整する。採用RGBは暗部`(0.5, 0.06, 0.008)`、面`(1, 0.26, 0.018)`、帯`(2, 0.8, 0.045)`、芯`(3.2, 2.1, 0.18)`。Bloom ProfileはThreshold 1.15、Intensity 0.4、Scatter 0.25、最大3反復、暖色Tint `(1, 0.72, 0.35)`、Half解像度、高品質フィルタとする。全体照明の0.85倍調整は採用せず、元のGlobal Light強度を維持する。

岸照明はGround Tilemap上の`TerrainMagmaShoreLighting`で調整する。初期値は固定3×3タイル区画から1灯、橙色`(1, 0.22, 0.035)`、Intensity 0.12、内半径0.25・外半径3ワールド単位、静止時の可視範囲再確認間隔0.5実時間秒（カメラ移動・ズーム・地形変更時は即時）。既存の全体照明を暗くせず光を足すため、Renderer2DのAdditive Blend Style（index 1）でDefault・TerrainWallを照らす。今回は動的投影影を有効にしない。

`ChunkTilemapPresenter`がDirtyサブチャンクの差分込み有効地形を分類するとき、Wall前面・上面によって画面上で隠れるマグマを除外し、異種地形または投影されたWallと接する可視の岸セルだけを絶対座標で記録する。Wallの表示高は共通の2タイルを使い、隠れたマグマからWall中央へ点状の光を出さない。光源の配置は絶対座標の3×3タイル区画ごとに決め、中心に最も近い可視の岸を1セル選ぶ。同距離はY・X順で固定し、チャンク境界は完成近傍で参照する。カメラは配置には使わず、可視矩形に光が届く固定光源をすべて表示する。カメラ移動・ズームの同じフレームで範囲を更新し、URP Light2DのLateUpdateより前に位置を反映する。Camera距離による16灯への絞り込み・最小間隔による選び直しは廃止する。必要灯数は画面へ届く固定区画数以下で、プールは過去の最大表示灯数まで増加して再利用する。Tilemapの全走査や全Object検索は行わない。チャンク消去・WorldLayer変更・Presenter無効化で候補を除去し、Floating Originのフレームは選択周期を待たず同じ絶対地点へ光源を相対配置し直す。光源GameObjectは増加時だけ生成して再利用し、Component破棄時に所有光源を破棄する。Simulationや保存値へ書き戻さない。

SampleSceneをPlayして、マグマのない場所の全体照明、網目の動き、岸へのにじみ、地面・壁への橙色の光、チャンク移動後の光の消え残りを確認する。既定Seedの確認地点は絶対タイル`(144, -476)`だが、Seedや設定変更後のマグマ存在は保証しない。CLIの比較画像は合成済みフレームの`capture_game_view --source screen`を使い、LDR RenderTextureへCameraを直接描く`screenshot`は使わない。照明のCPU処理は`Farmware.WorldGeneration.MagmaShoreLighting` Markerで計測できる。Development PlayerでのGPU負荷・製品最大Viewport・全岸の均等な照明や動的遮蔽の評価は別途行う。

#### 液体製品Assetと水際Shaderの公開

- メニュー: `Farmware > World Generation > Generation > Publish Liquid Presentation Assets`
- 実装: `TerrainLiquidProductPublisher`
- PlayMode: 停止中に実行する。追加の手描き水際素材は不要である。

公開処理は`Liquids/Published/Surfaces/`へ16×16 px・16 PPUの製品Surface SpriteとTile、`Liquids/Published/TerrainLiquidPresentationCatalog.asset`へ3液体のSurface参照・水際色・深度色を保存する。カタログ形式はVersion 2とし、旧AnimatedTile配列を持つ形式は再公開する。共有水際Materialは`Materials/TerrainLiquidShoreline.mat`へ初回だけ作り、再公開では調整値を保持する。

淡水・海水のSurface画素はAlpha 0、マグマはAlpha 255とする。通常Face、深度勾配、反射は既存の専用レイヤーを使い、水際Materialは最後に1px接触線だけを描く。現在の有効地形から奥・左・右・左右内角を選び、縦岸を16pxごとに切らず、内角の3px不足を補う。奥が同種液体で斜め奥に岸がない場合は、地面の裏側へ回り込む終端として該当する側線の上端3 pxだけを隠す。左右は独立に判定し、連続する縦岸や反対側を切らない。

旧`Generate Selected Liquid Edge Waves`メニュー、方向別4フレーム入力、専用Importer、組合せAtlasと21 AnimatedTileは廃止した。形状素材やアニメーションフレームを手作業で作らない。製品Scene接続メニューで水際Tilemapへ専用Materialを割り当てる。

調整は水際MaterialのInspectorで行う。初期値は最低濃度0、最大濃度0.9、模様長さ目安36px、透明を含む濃度4階調（Alpha 0／0.3／0.6／0.9）、寄せ引きの基準周期3秒、周期ばらつき25%（実周期2.25〜3.75秒）、明暗更新4回/秒とする。最低濃度0では最も弱い部分が完全透明になる。常時線を残したい場合だけ最低濃度を上げる。濃度階調数2〜8、基準周期1〜12秒、周期ばらつき0〜50%、更新数は整数1〜12回/秒で調整できる。波の固定中心ごとに位相・周期を決め、周囲4中心の固定空間形状を重ねる。一方向の移動速度や全岸共通の主・副2周期は使わない。ばらつき0でも開始位相は場所ごとに異なる。素材1px内の濃度とコマ間の状態を保持し、水中の深度勾配・反射には階調化を掛けない。再生・動作確認はPlayModeで行う。

水面の短い筋模様は`TerrainWaterSurfacePattern.mat`のInspectorで別調整する。接続メニューは未作成時だけ専用Materialと`Water Surface Pattern Tilemap`を作成し、再接続では既存の調整値を保持する。淡水・海水色、最大濃度、密度、長さ、出現秒、休止秒、時間ばらつき、FPS、階調数を調整できる。「アニメーションを有効にする」を外すと最大形状の静止確認になる。初期値は長さ3〜8px、密度0.65、最大濃度0.18、出現4秒・休止3秒±35%、2fps・4階調。水際Materialは維持する。Playで短い線がその場で非同期に伸縮・消失し、陸地とマグマへ出ず、奥岸上端3pxだけが隠れ、負座標・チャンク境界・原点変更で配置が変わらないことを確認する。Runtimeは専用Material1件を全表示チャンクで共有する。

カタログの水際色は液体種ごとの基準RGBである。RuntimeではMaterialを1件だけ複製して全チャンクで共有し、表示原点の変更時だけ画素オフセットを更新する。Presenterは毎描画フレームに共有表示時間（秒）1件を渡し、Shaderがコマ数に合わせて切り捨てる。Project内のMaterialへ原点や実行時の時刻を書き戻さない。

製品Scene接続時は、カタログの淡水・海水深度色を床あり・床なしMaterialへ同期する。淡水は`#184860`、海水は`#102A48`の収束色を維持する。接続メニューは`TerrainSubmergedGroundFace.mat`と水中Ground断面専用Tilemapも作り、乾燥崖用レイヤーには水中断面を置かない。床有無の分類は既存の2 Tilemapで保持するが、どちらも同じ一定の基底色をWaterSurface Order 0へ描く。白い全面Tileを1件だけ共有し、断面TileもSpriteと白Tintだけを共有する。セル色はR=その列の投影段数/6、G=床有無、B=海水印、A=奥岸なら128/255・それ以外1とする。水色は段数を濃度へ変換せず、断面だけが光学的な被覆率を読む。断面と直下床はWaterSurface Order 1、短い水面模様は2、反射は3、水際は4へ配置する。直下床はLitと可視Alpha 50%を保ち、Global Lightの対象にWaterSurfaceを含める。水域の基底色は淡水・海水ごとに一定として先に不透明描画し、その上へ実際の奥岸の真下だけに水中断面を投影する。各断面の岸からの距離を素材1px中心で評価し、可視Alphaを「元SpriteのAlpha×(1−水の被覆率)」とする。被覆率は上端3pxの下から床あり45pxで50%、床なし93pxで100%へ増え、既存の直線岸の見た目を維持する。異なる岸のY位置を平均せず、隣列からの断面延長、共有四隅距離、陸地専用段数、左右だけの追加減衰は使わない。水色だけの出力は岸のY位置によらず均一となる一方、実際の断面の端そのものは正しい形状として残す。上端3pxを切るのは水面側だけで、そこに見える地面の縁は残す。直下床は48px以深へLit表示を維持した可視Alpha 50%で重ねる。乾燥崖の48px減衰は水中断面へ適用しない。反射対象は同じWorldLayerの水面より上に立つ垂直画像とし、奥側1タイルへの隣接を条件にしない。接地点・画像の高さ・縦圧縮率から元の位置で反転投影し、有効地形の淡水・海水と交差する部分だけを描く。水際へ像を吸着させたり、陸地・マグマ・水中断面へ描いたりしない。現在の供給元はWallだけで、32pxの前面を0.75倍し、論理根元から3px低い位置を起点に24pxで消す。地面1セルを挟むWallも、届く最後8pxだけを表示する。木・設備・生物の供給元は今回は追加しないが、共通の`TerrainWaterReflectionGeometry.TryProjectToWater`は任意の接地点・画像矩形・高さに対応する。 揺れは絶対画素位置に固定した周囲4中心を混ぜ、各中心に異なる位相・周期・強さを与える。その場で往復する横変位だけを素材1px単位へ丸め、初期値は最大±1px、横のまとまり32px・縦4px、基準周期4秒±35%、2fps、付け根2pxは変位0とする。色と最大Alpha、24pxの減衰は時間で変えない。製品`TerrainWaterReflection.mat`は2026-10-28のユーザー調整で最大Alpha 0.8・最大揺れ幅±1pxを採用する。Shaderの最大Alpha初期値0.22は変更しない。Inspectorは同Materialで調整する。起点・水位・反射の縦位置は揺らさない。画像の色とAlphaを同じ横変位で読み、外周と透明な穴も一緒に動かす。最大2pxの余白を持つ投影像を受け手の水セルで切り抜き、陸地や岸の上端3pxへは描かない。

深度色だけが変わるDirty更新でも描画を確定するため、共通の反映処理は対象サブチャンクの非空セルだけをRefreshしてから色付き一括書込する。順序を逆転すると断面共有Tileの色固定が深度色を白へ戻す。全Tilemapの毎フレーム再構築は行わない。

確認では、縦岸、奥岸、左右の内角・外角、1タイル幅の水路、チャンク境界、負座標、Floating Origin後の接続を調べる。地形変更後にも旧水深が残らず、最低濃度が残ったまま明るい部分の長さや強さが変わり、地形・水面位置・水深が揺れないことをGameビューで確認する。短い水面模様は独立Material `TerrainWaterSurfacePattern.mat`で調整する。2026-10-28のユーザー調整を採用し、候補採用率0.3、出現5秒・休止4秒を製品値とする。長さ3〜8px、最大Alpha 0.18、時間ばらつき35%、毎秒2コマ、4階調は維持する。Shader初期値の候補採用率0.65・出現4秒・休止3秒とは区別する。反射は接地点からの投影と水域交差、上下2画像とセル境界の連続性、Atlas境界、場所別周期、付け根の固定、原点変更を確認する。

淡水と海水が接する場所でも、水中断面・被覆率の距離・Wall反射元の探索は継続する。水種境界の列だけ断面が欠落して濃い溝にならないことを確認する。固定の再現例はSeed `0x3EE49B3100677454`・GeneratorVersion 2のSurface `(-290,204)`付近で、同じ列のY=206が淡水、Y=205・204が海水、Y=207がGroundとなる。水色の違い自体は残し、マグマ・未知液体・空セル・未取得チャンクを越えて断面を借りない。Materialの再公開や生成設定の変更は不要である。

材質PNGを修正した後の流れは次のとおりである。

1. AsepriteまたはPixeloramaから同じPNGパスへ上書きし、Unityの自動再Importを待つ。
2. `Terrain Tile Art Generation Settings`の対象材質で入力Spriteと検証結果を確認する。
3. `FaceBoundaryOverlay`が0件の境界なし材質では、`GroundTop`と`Face`を直接使用するため派生PNG生成は行わない。
4. `FaceBoundaryOverlay`が1件以上の境界あり材質では、`Face`、`FaceBoundaryOverlay`、`DarkenWallSurface`、`WallDarkenFactor`のいずれかを変更した後に`対象材質の境界Spriteを更新`を実行する。`GroundTop`だけの変更では境界合成PNGを更新しない。
5. 境界ありから境界なしへ変更した場合は、Inspectorの`不要な境界Spriteを整理`を一度実行し、以前その材質が所有していた合成PNGだけを削除する。
6. `Publish Terrain Material Assets`で製品用Spriteとカタログを更新し、SampleSceneの製品表示で境界あり／なしの1段目と差分の接続を確認する。

- `Assets/`配下の出力ルート。
- 材質ID、日本語確認名、全候補を均等確率で選ぶ可変長の`GroundTop`、`Face`、任意の`FaceBoundaryOverlay`。個別Weightや出現率Overrideは持たない。
- `DarkenWallSurface`と`WallDarkenFactor`。新規材質の初期値は暗色化ON、係数0.85とする。
- 選択材質の手描き入力数、境界生成数、Runtime候補数、Ground／Wall用境界の内訳、要再生成状態。
- 選択材質で生成済みのGround用境界とWall用境界の画像一覧。各画像をクリックするとProjectビューで対応するSpriteを選択する。

`対象材質を検証`／`対象材質の境界Spriteを更新`は、ほかの未制作材質が空でも代表材質を1件ずつ試作できる。`全材質を検証`／`全材質の境界Spriteを更新`は登録済み全材質を先に検証し、総出力枚数の確認後に一括更新する。入力Spriteは16×16 px、Read/Write有効、Point、非圧縮、Mip Mapなし、sRGB、Alpha 0または255とする。不正なID、重複ID、空の必須配列、未設定Sprite、範囲外の暗色化係数も生成前に日本語で報告する。

各配列内の完成Sprite候補はC6-Dで均等確率かつ決定論的に選ぶ。生成設定AssetとInspectorにはWeight調整を設けない。`Face`と`FaceBoundaryOverlay`の内容・順序は境界合成Fingerprintと要更新状態へ反映し、派生PNGへ使わない`GroundTop`は直接反映する。

出力先は`<Output Root>/<Material ID>/FaceBoundary/`である。Ground／Wall境界は`Face`番号と`FaceBoundaryOverlay`番号の全組合せを用途別に生成する。16×16画像は64×64の整数4倍で表示し、再コンパイルやEditor再起動後も保存済み生成パスからSpriteを再読込する。暗色化が無効ならWall用境界PNGを重複生成せずGround用を再利用する。同名PNGの再生成ではGUIDを維持し、差分数を減らした場合は前回記録された同材質の不要PNGだけを削除する。旧`WallTop`生成フォルダーは新方式への移行時に削除済みである。

#### 41材質の製品公開

- メニュー: `Farmware > World Generation > Generation > Publish Terrain Material Assets`
- 実装: `TerrainMaterialProductPublisher`
- PlayMode: 不要。41材質の設定検証と境界合成を先に完了してから実行する。

公開処理は今回完成した64×48 px画像と設定アセットを正として、境界あり11材質・なし30材質の集合を検証する。上段4枚の`GroundTop`と中段4枚の`Face`は、製品Sceneが`Source`フォルダーへ依存しないよう、`Terrain/Published/Materials/<Material ID>/`へ16×16 pxの単一Spriteとして書き出す。境界あり11材質は`Art/Generated/TerrainTiles/`のGround用・Wall用合成済みSpriteを使用する。公開済み通常候補328枚と境界完成候補352枚を`TerrainMaterialAtlas.spriteatlasv2`へ収録し、Point、Mip Mapなし、非圧縮、回転なし、Tight Packingなし、Padding 4 px、最大1024 pxで1ページへパックできることを実行中に検証する。

`TerrainMaterialPresentationCatalog.asset`は安定材質ID順の41行を保持し、上面4候補、通常断面4候補、境界候補数0または4、用途別の境界完成候補を参照する。同名PNGと既存Catalog／Atlasは同じパスへ更新して`.meta`とGUIDを維持する。公開処理に失敗した場合は製品Sceneへ接続せず、Consoleに表示された材質ID、候補数、Atlas分割の原因を修正してから再実行する。

#### 製品Sceneへの接続

- メニュー: `Farmware > World Generation > Setup > Connect Product Terrain Presentation`
- 実装: `TerrainProductSceneConnector`
- PlayMode: 不要。公開処理の成功後に実行し、保存したSampleSceneをGameビューで確認する。

反射設定見本のMaterialは`TerrainWaterReflection.mat`とし、元画像の色補正・反射色・最大濃度、整数pxの最大振幅、横と縦のまとまり、基準周期、周期ばらつき、更新FPS、付け根を固定する長さを調整できる。初期設定は最大±1px、横32px・縦4px、4秒±35%、2fps、付け根2px。振幅0で揺れを止め、切り抜き位置と減衰を確認する。横変位は模様だけでなく外周・透明な穴にも適用する。付け根と受け手の水域境界は固定する。永続調整は停止中にAssetへ行う。Play中は全反射Meshが共有する実行時Materialを使い、Assetへ時刻や原点を書き戻さない。

接続処理は公開済み41材質カタログと液体3種カタログをSampleSceneの`ChunkTilemapPresenter`へ設定する。地形側はGround断面、Wall前面、乾燥崖深部黒、固定接地影2枚、32px折れ目影2枚を作成または再利用する。液体側は既存`Liquid Base Tilemap`を論理セル原点へ戻して製品Surface Materialを割り当て、床あり基底色、床なし基底色、直下床、水中断面、短い水面模様、反射設定見本、水際の描画設定を追加する。反射は製品Atlasも明示設定し、実描画は水域で切り抜いたMeshを使う。`WaterSurface` Layer内はSurfaceと基底色0、直下床と水中断面1、水面模様またはマグマ網状模様2、反射3、水際4の順とする。`Magma Surface Pattern Tilemap`を製品名から作成または再利用し、Presenterの`magmaSurfacePatternTilemap`へ接続する。Rendererへ`TerrainMagmaSurfacePattern.mat`を割り当て、マグマセルだけを描く。マグマ岸の地面の縁3pxは水中断面レイヤーへ専用の分類印で描き、Wall前面は通常のWallレイヤーへ残す。新しいSceneレイヤーや専用Spriteは追加しない。網目6px、芯の半幅0.25px、橙色帯の半幅0.75px、4fpsを採用初期値とし、再接続時も既存のMaterial調整値を保持する。旧AnimatedTile・4フレーム生成と選択経路、一時的なマグマ候補移動メニューは削除済みである。

論理Wallセルを最下段Faceの根元とし、Wall上面・前面・折れ目影・Wall上資源は+2Y、固定接地影は+2Yを基準に手前3 pxへ揃える。動的Casterは論理Wall根元へ追加オフセットなしで置く。Wall上面・前面・折れ目影は`TerrainWall` Sorting Layerへ置き、既存Global Light 2Dの対象へ同Layerを追加する。画面手前側2セル以内にWall根元がある資源だけは`Default` LayerのWall背後へ残し、通常資源とWall上資源は`TerrainWall` Layer内でWall本体より大きいSorting Orderへ揃える。これにより、Lit MaterialのWallが無照明で黒くなることと、Sorting Layerの違いによって通常資源までWallの背後へ回ることを防ぐ。

#### 次の修正をしたら確認する

- 暗色化の色空間、丸め、対象部位、Alpha規則を変更した。
- 入力配列、境界合成Fingerprint、生成ファイル名、出力先、材質入力の自動Import設定または4×3分割規則を変更した。
- C6-Dで完成Spriteの参照方法やPivotを確定・変更した。

#### 確認内容

1. 専用EditModeテストが、暗色化、Overlay合成、半透明拒否、可変Sprite数、専用WallTop設定の不存在を保証する。
2. 材質Source配下へ新規64×48 px PNGをImportすると、共通設定と12区画が自動適用され、再検査でSprite IDが変化しない。規約外寸法と不正な既存分割は参照を壊さず拒否される。
3. `GrassLoam`で対象材質の検証・更新を行い、必要なGround／Wall境界合成Spriteだけが所定フォルダーへ出る。境界なし材質は生成不要と表示される。
4. 生成PNGのImporterがPoint、16 PPU、非圧縮、Mip Mapなし、sRGB、`Full Rect`、中央Pivotになり、再生成後もGUIDが変わらない。
5. `Face`、`FaceBoundaryOverlay`、暗化の有効状態または係数を変更すると要更新表示へ変わり、更新後に最新表示へ戻る。`GroundTop`だけの変更では不要な境界再生成を要求しない。
6. Inspectorの生成結果プレビューにGround／Wall境界の全`F×B`組合せが別一覧に64×64の整数4倍で表示され、画像をクリックすると対応するSpriteをProjectビューで選択する。
7. Wall上面は生成PNGではなく同じ`GroundTop`参照へ表示時Tintを掛けること、`Face`自体は暗色化されないことをGameビューで確認する。
8. Build Settingsで有効な製品Sceneが、設定Asset、手描き制作入力、検品用Material、Development Scene／Shaderへ依存していないことを`TerrainProductIsolationTests`で確認する。

### 2.11 削除済みの地形比較・検品ツール

41材質と採用済み表示を製品Sceneへ接続したため、地形タイル、Wall暗色化、Wall影、折れ目影、旧共通境界、水面反射の比較・検品Scene、Builder、Editorメニュー、Development Shader、生成Material、専用テストはC6-D6で削除した。採用値と判断履歴は[地形タイル表現仕様](../Art/TerrainTileArtSpecification.md)および[実装ロードマップ](./ImplementationRoadmap.md)へ残し、今後の回帰確認は製品カタログ、製品Material／Shader、SampleScene、製品表示テストを対象とする。

## 3. 変更時の選び方

| 変更内容 | 最低限確認するEditorツール |
| --- | --- |
| Authoring、Scene、Tileアセット、Camera、Tilemapレイヤー | Build Fallback Map Prototype |
| 個別タイルの分類、色、境界、凡例 | Debug Visualization |
| バイオームや地形の広域分布、隣接関係、決定性 | Analyze Distribution |
| 動物の候補地形、確定位置、群れ・密度・重複 | Animal Population表示、Analyze Animal Population |
| 水文の流向・終端・集水・河川・湖分布 | Hydrology表示、Analyze Hydrology Distribution、Map Viewer |
| 地下水、自然飽和、最終湖区分、泉、自然冠水、湿原の分布 | Analyze Derived Hydrology Distribution、Map Viewer、Debug Visualization |
| 広域形状、特定Seed・遠距離座標の探索 | Map Viewer |
| 地形タイル試作工程の材質Sprite入力、境界合成、均等差分選択、Wall暗色化 | Terrain Tile Art Generation Settings Inspector |
| 全41材質、境界あり11材質、Ground／Wall断面、32px折れ目影、固定・動的影 | Terrain Tile Art Generation Settings Inspector、Publish Terrain Material Assets、Connect Product Terrain Presentation、SampleScene |
| 生成規則と表示の両方 | Analyze Distribution、Map Viewer、Debug Visualization |

Editorツールだけで検証を完了しない。対応する自動テストは[テスト](./Testing.md)に従って実行し、見た目に関係する変更はGameビューでも確認する。

## 4. このドキュメントの更新条件

### 仕様転換に伴う実装時の更新予定

- WORLD-01CでLayerキーをビューア・中央情報・可視化の検索へ接続済み。Map Viewerの`Layer`欄、または`Farmware > World Generation > Debug Visualization > Layer`からSurface／Underwater／Deep Seaを選ぶ。Surface以外の自然生成はTERRAIN-01Cまで未接続のため、Map Viewerは暗色と未接続メッセージ、Gameビュー可視化は空表示を返す。
- TERRAIN-01AではBlock定義と有効セルAPIをSimulationへ追加するが、色判定は既存`GeneratedTile`互換のままとする。仕様転換に合わせて`Terrain Placement`からベルト固有のHUDと凡例だけを除き、差分込み表示、Blockパレット、自然生成と実ワールドの表示切替はTERRAIN-01Cで追加する。
- TERRAIN-01Bで採用したバイオーム構成と安定ID方針に基づき、TERRAIN-01C1〜C5で環境軸・地質・水文・Biome診断、TERRAIN-01C6でBlockパレットと変更後地形、TERRAIN-01C7で完成タイル上のBiome・水系・Block景観比較、TERRAIN-01C8で資源診断、TERRAIN-01Dでスポーン許可・探索失敗理由を追加する。地形タイル試作工程では[地形タイル表現仕様](../Art/TerrainTileArtSpecification.md)に従い、16×16 `GroundTop`、材質ごとに任意の透明な16×16 `FaceBoundaryOverlay`、Ground崖面とWall前面で共用する16×16 `Face`を代表5材質で手制作・試作してから量産する。Wall上面は同じ材質の`GroundTop`を共有し、通常材質では表示時に`WallDarkenFactor` 0.85を乗算する。Wallの識別には`Face`、左右各1px・手前3px・各レイヤーAlpha 22%の固定接地影、設置面起点の動的影、Wall前面2段を通してAlpha 35%から0%へ連続減衰する32px折れ目影を使う。8px／16px折れ目影と1px・14形状境界ハイライトは不採用とする。Wall同士は材質を問わず接続扱いとする。断面は1段目から`Face`を使い、境界ありではGround用に元色Overlay、Wall用に同じ係数で暗色化したOverlayを`Face`へ事前合成する。`Face`自体は暗色化しない。各部位は全候補を均等確率で選ぶ可変長配列とし、個別Weightや出現率Override、固定差分数は設けない。全`Face`差分は相互接続可能とする。淡水・海水では通常断面または直下床へ半透明の青い`Liquid深度Overlay`を重ね、上端3 pxをクリップして1 px単位の連続Alpha勾配を作る。直下床ありは45 pxでAlpha 50%、床なしは現在材質の`Face`を背後へ続けて93 pxでAlpha 100%へ到達させ、黒暗化は使わない。乾燥崖は穴上端から48 pxかけて`Face`の可視Alphaを100%から0%へ連続変化させ、4段目以降を完全な暗黒とする。淡水・海水は画面手前を除く奥・左・右の接触辺へ1 px・2色の波線を整数Pixel単位で流す。マグマは接触する最初の段から不透明にして背後の断面を隠す。動的な影は`Light 2D`と、Wall領域からサブチャンク単位で作る`Shadow Caster 2D`で表現する。DeepSeaの内容をSurfaceへ透視させず、液体専用断面も作らない。細道の要否は試作後に別途判断する。Map Viewerの18Biome識別と自然生成・実ワールド差分の明示は、最終BiomeとBlockを接続するC5・C6で更新する。C7では同じSeed・範囲の変更前後を比較できる出力を維持し、Biome分類、水文、Blockのどの入力を調整したかを個別モードで判別できるようにする。
- 41材質の`GroundTop`色・使用比率・模様方針は、地形タイル表現仕様第7章の確定値として扱う。Wall上面は同じ`GroundTop` Spriteを参照し、通常材質では表示時にsRGB各成分へ0.85を乗算する。Alphaと使用比率は変更しない。Wall用`FaceBoundaryOverlay`の不透明画素にも同じ係数を使い、Ground用Overlayと`Face`は変更しない。`FaceBoundaryOverlay`と`Face`の元パレットは`GrassLoam`の試作を人間確認後、同じ明度関係を残り40材質へ適用して確定する。
- 地形タイル試作工程の材質生成設定Inspectorは2.10節のとおり、`GroundTop`・`Face`・任意`FaceBoundaryOverlay`とWall暗色化だけを扱う方式へ更新済みである。専用`WallTop`、7外周部品、共通暗色内部、47枚の材質別Wall完成Spriteは削除した。WorldLayer・絶対座標・断面段・安定材質ID・用途別SaltによるRuntimeの決定論的な均等差分選択は、表示へ接続するC6-Dで実装する。
- TERRAIN-01C3-D2では、生成済み固定水文セルの流向・下流座標・終端・辺数・集水面積・打切り・最遠寄与距離・流出・流域・湖をGameビューとMap Viewerへ接続した。海洋流出口は流向なしの`Marine`終端、4,096辺または4,096セルで確定できない結果は`LongDrainage`または打切り色として表示する。Level 0〜6集約SummaryとLevel 0〜12 Downstream Summaryの利用・分解・キャッシュ統計は固定生成結果ではない比較実装の内部診断なので、性能テストだけで記録し表示へ混ぜない。打切られた大規模閉鎖流域は固定湖候補と別色・別理由で表示する。
- TERRAIN-01C5では、環境として自然な直接接続を認めるv0.48の方針に合わせ、安定`BiomeId` 1〜18、全18Biomeの153ペアの境界長・接触率、両側の気温・湿度・標高と差、植生・地質・火山段階・水文・海岸条件、最終選択理由を境界診断へ追加する。C5前の旧7種や旧IDを選べるモード、凡例、配色、中央情報は残さず、旧版データは表示で補完せず版不一致として拒否する。TERRAIN-01C1で削除した旧7Biome向けの`Biome Transition`を復活させず、第三Biome強制の仕様へ戻さない。新方式では橋不足や接続例への未記載を異常表示にせず、不正候補・必須ゲート違反と、景観品質の確認事項を区別する。湿原の専用Host許可・不許可とOverlay理由、火山中核の地質・活動度を表示し、周縁リングの欠落だけをエラーにしない。153ペア監査と最終選択理由はC5の更新予定であり、C1時点で実装済みとは扱わない。
- TERRAIN-01C6-C3で最終Block・Liquid表示へ入力を統一し、旧水域分類、旧River・Pond生成、旧Biome直結配色を読むモード、凡例、中央情報、互換変換を削除した。C6-Dでは、廃止を決定した14形状の接続選択処理、Ground／Wall用Tilemap、白い境界Sprite、専用Atlas、Scene参照、専用テストも旧経路として同じ変更単位で削除し、空配列やフォールバックを残さない。Wall接続では材質IDを比較せず、連続したWall領域へサブチャンク単位の固定接地影を表示し、差分込み有効地形近傍の南隣セルから求めた露出前縁をWall前面段数だけ手前へ投影して動的影を作る。全Wallの投影によるWall上面の自己遮蔽は作らない。比較用の旧表示切替は残さず、固定水文・有効地形だけを診断の入力にする。F4のタイル・サブチャンク・チャンク境界診断は地形アートとは別機能なので維持する。
- TERRAIN-01C／GATHER-01／AUTO-01D: 編集後の隣接差分、穴・液体、反応・保留キューに加え、`LooseSurface`／`GroundEmbedded`／`WallEmbedded`／`MassiveDeposit`の形態、Host、採掘済み差分、巨大鉱床の残量・占有を表示する。自然生成Block、配置済みBlock、クラフト用資源を区別し、元生成を現在地形と誤表示しない。
- ECO-01／JOURNAL-01: 永続／匿名、地域会計、収容数・回復、証拠判定を確認可能にする。
- DIVE-01／VEHICLE-01: 現在Layerと直下プレビュー、遷移失敗、高速先読み・生成待ちを診断する。
- 構築ツールが既存の本番参照・採用GUID・未保存Sceneを守り、新プレイヤー・保存設定へ更新できることを各PRで確認する。

これらは予定であり、実装PRで具体的なメニュー・用途・確認手順へ置換する。

Editorメニューの追加、削除、名称変更、実行条件変更、出力項目変更、確認対象変更を行った場合は、同じcommitにこの文書の更新を含める。
