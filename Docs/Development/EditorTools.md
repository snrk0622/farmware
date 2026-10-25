# Unity Editorツール

- 最終更新日: 2026-10-25
- 対象: `Assets/Farmware/Editor/WorldGeneration/`

## 1. 用途

本書のメニュー・設定・表示は、TERRAIN-01C2-Bまでのdevelop、TERRAIN-01C3-D2、水文Lake表示へ先行反映したTERRAIN-01C4-C、派生水文分布監査、Gameビュー診断、Map Viewer診断を追加したTERRAIN-01C4-E3、最終18Biomeを接続したTERRAIN-01C5-C、Map Viewerモード契約とBiome選択診断を整理したTERRAIN-01C5-D、完成チャンクの分布・境界監査へ置換したTERRAIN-01C5-Eの現行実装境界を説明する。C3-A〜D1がSimulationへ追加した固定水文の基礎格子、D8流向、固定下流終端、集水面積、自然流出、閉鎖流域境界、基礎湖面を、C3-D2で複数Seed分布監査、Debug Visualization、Map Viewerへ接続した。C4-E1bは地下水、土壌飽和、最終湖区分、泉、自然冠水、湿原候補を複数Seed分布監査へ接続し、C4-E2とE3は残る5分類をGameビューとMap Viewerへ接続した。C5-CはC4完成チャンクを18Biomeへ確定し、GameビューとMap ViewerのTerrain・Biome表示へ接続した。C5-Dは製品分類器と同じ最終理由・通常上位候補・8軸適性を両画面の中央情報へ追加し、Map Viewer全27モードの生成契約を単一Catalogへ集約した。C5-Eは停止中だった分布メニューを再有効化し、製品と同じ完成チャンクから候補・面積・境界・領域を集計する。Level 0〜6集約Summaryの利用・分解・キャッシュ統計は性能テストだけで記録し、Editor表示へは渡さない。2026-10-19の[改訂マップ仕様](../GameSpecification/RandomMapGenerationSpecification.md)の可変Blockと水中文脈表示はまだツールに実装されていない。

開発者専用のUnity Editorツールは、ワールド生成結果の調査、調整前後の比較、確認用Sceneとフォールバック素材の構築に使用する。
これらは製品版のプレイヤー操作へ含めず、ランタイムのゲーム状態を変更するデバッグ操作にも使用しない。

プレイヤーへ提供するF3座標表示とF4グリッド表示はEditorツールではない。
両機能の仕様は[ランダムマップ生成仕様](../GameSpecification/RandomMapGenerationSpecification.md)を参照する。

## 2. ツール一覧

### 2.1 Build Fallback Map Prototype

- メニュー: `Farmware > World Generation > Build Fallback Map Prototype`
- 実装: `WorldGenerationSceneBootstrap`
- PlayMode: 不要。EditModeで実行する。

#### 用途

- 上面16pxと断面8pxを結合した16×24の仮色土地Tile、16×16の仮色液体・資源Tileを生成または更新する。土地上面は安定`BiomeId` 1〜18に対応する18種類の仮色だけで塗り、未制作Biomeを別BiomeのTileへフォールバックさせない。草、葉、砂紋などの固有模様やSoilType別派生Tileは生成しない。旧7種の土地フォールバックと旧14枚の肥沃・砂質派生アセットが存在する場合は削除する。
- 上面16pxと側面12pxを結合した16×28フォールバック岩壁Tile、淡水・海水・マグマ各4枚の透明フレームとAnimatedTileを生成または更新する。フォールバックは構築確認用として維持し、本番用`terrain_rock_wall_01.png`を上書きしない。
- 本番用`terrain_rock_wall_01.png`へPoint、PPU 16、Mip Maps Off、Compression None、Full Rect、Pivot Y `8/28`を設定し、専用Tileを作成または更新する。Sprite PivotはSprite Editor Data Provider経由で設定する。
- 新18Biomeの仮色土地Spriteを同じTextureページへ収める`GroundTerrainAtlas.spriteatlasv2`、本番用岩壁を収める`RockWallAtlas.spriteatlasv2`、共通境界14 Spriteを収める`BoundaryHighlightAtlas.spriteatlasv2`を生成または更新し、現在のBuild Target向けにパックする。C6のBlock用本番素材が未制作の間だけ、18枚の仮色土地を表示契約とAtlas検証に使う。境界PNGの画素と14分割は専用の境界生成ツールが担当し、このツールはImport済みTextureの検証・Atlas登録・同一ページ検証を担当する。
- `WorldGenerationSettingsSubScene`へ生成設定のAuthoringと、最初の動物種である鶏の共通定義・生成条件を配置する。鶏は草原の通常陸地、群れ3〜6羽、有効地形4096タイル当たり8羽、待機20〜80固定Tick、1タイル移動5固定Tick、最大徘徊8タイル、外見差分3種類を開発用初期値とする。
- `DefinitionCatalogAuthoring`の専用Inspectorでは、`animalSpeciesDefinitions`の各要素を共通定義GUIDで照合し、`0: Chicken`のように`definitions`側の表示名を見出しへ表示する。表示名を動物種固有設定へ重複保存しないため、名称変更は`definitions`側だけで行う。GUID未設定または参照切れの場合は、見出しへ未解決理由と短縮GUIDを表示する。
- `SampleScene`へCamera、原点固定のGrid、Ground・静止液面・液面アニメーション・土地境界・岩壁・岩壁境界・岩壁背面資源・通常前面資源・岩壁上資源の9枚のTilemap、表示用Component、SubScene参照を接続する。Cameraの`CameraPanController`を全Tilemap Presenterの共通表示原点として接続し、絶対初期位置をTransformへ直接保存しない。通常移動では512タイル未満の間はCameraの相対座標だけを更新し、閾値へ達するとGridを動かさず表示原点を所属チャンク境界へ自動更新する。静止液面と液面アニメーションは土地より8px下、岩壁境界と岩壁上資源は土地基準面より12px上へ設定する。GroundとRock WallはIndividual ModeかつTop Left順で描画する。土地境界はOrder 1、岩壁境界はOrder 4とし、生成済みの共通境界14 Spriteを両方へ接続する。通常資源は直前方の岩壁有無からOrder 2の背面用TilemapとOrder 10の前面用Tilemapへ振り分ける。
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
- Ground、Liquid Base、Liquid Surface Animation、Land Boundary Highlight、Rock Wall、Rock Wall Boundary Highlight、Resource、Rock Wall ResourceのTilemapレイヤー構成、Transform、描画Mode、Sorting Order、参照関係を変更した。
- Camera、Pixel Perfect Camera、共通表示原点、480×270の基準解像度、黒帯処理を変更した。
- `SampleScene`または`WorldGenerationSettingsSubScene`の生成構成を変更した。
- 土地・岩壁の元Texture、Sprite Slice、Atlas容量、Pixel Art向けAtlas設定を変更した。境界ハイライトの再制作時は、Sprite、Slice、Atlas、Scene参照を同じ変更で確認する。

#### 確認内容

1. Consoleに例外または新規警告が出ていない。
2. メニュー実行をキャンセルした場合はアセットやSceneが変更されず、実行した場合は成功・失敗のどちらでも元の複数Scene構成、読込状態、Active Sceneへ戻る。
3. 必要なPNG、Tile、AnimatedTile、Sprite Atlas、`.meta`が揃っている。18枚の土地フォールバックは安定`BiomeId` 1〜18と一対一で、上面はBiome別の仮色一色とし、草・葉・砂紋やSoil模様を含まない。旧7種の土地フォールバックは存在せず、18枚が`GroundTerrainAtlas`の同じパック後Textureを参照する。本番用`terrain_rock_wall_01.png`と専用Tileが16×28、Pivot Y `8/28`で同じSpriteを参照し、`RockWallAtlas`へ収録される。共通境界PNGは16×16の14区画を持ち、`BoundaryHighlightAtlas`の同じパック後Textureへ収録され、Sceneの境界Sprite配列へ14件すべて接続される。
4. SampleSceneとSubSceneの参照がMissingになっていない。`WorldGenerationSettingsSubScene`の`DefinitionCatalogAuthoring`が鶏の定義GUIDと開発用初期値を保持し、共通定義の分類が`AnimalSpecies`になっている。Inspectorの`animalSpeciesDefinitions`を展開すると要素見出しが`0: Chicken`になり、GUIDが未設定または不一致なら未解決表示になる。World GridのTransformが原点・無回転・等倍で、Cameraの`CameraPanController`が`ChunkTilemapPresenter`と`InitialCameraPositionController`へ接続され、自動Floating Origin距離が512タイルになっている。
5. PlayModeで土地が16×24、岩壁が16×28の一体型ブロックとして表示される。土地は論理Biomeだけを反映し、同じBiome内では標準・肥沃・砂質のいずれも同じTileに見える。静止液面と透明アニメーションは同じ8px下、岩壁内部影・露出資源は12px上へ表示される。Ground／Wallの境界ハイライトは14基本形と回転から選ばれ、20%のAlphaで表示される。液体はGround Tilemapへ重複せず、液面下辺と奥側ブロックの断面・側面は手前ブロックに隠れる。直前方が岩壁の通常資源は背面用Tilemapへ入り、それ以外は前面用へ入る。カメラをサブチャンク・チャンク境界へ寄せても立体Spriteが途中で消えない。
6. Gameビューの地形描画領域が480×270基準の黒帯内へ収まり、HUDとF4表示も同じ領域を使用する。F3には現在タイルのサブチャンク座標、Biome、GeneratorVersion、16桁16進数のワールドシードが表示される。
7. 最大ズームアウトで通常移動とShift移動を行い、512タイル境界の前後でCamera、Tilemap、F3、F4、開発者用可視化が同じ絶対セルを示し、空白や旧セルの残留が見えない。Profilerでは`Farmware.WorldGeneration.FloatingOriginRebase`、`Farmware.WorldGeneration.TilemapPresentation`、`Farmware.WorldGeneration.ChunkStreaming`を記録し、原点変更フレームのCPU時間、GC Alloc、生成待ち数を変更前後で比較する。

ちらつき修正の回帰確認では、初期スポーン`(-45,-19)`からY=-18.5を保って右へ移動し、絶対X=448と960（チャンク`(7,-1)`と`(15,-1)`への進入）を確認する。F3／F4を表示し、地形だけが一度背景色へ抜けて矩形単位で復帰する現象がないこと、原点更新フレームに可視要求が8チャンク先へ飛ばないことを確認する。別シードではスポーンが異なるため、現在の表示原点から512タイルの境界を使う。

性能の一次計測条件と結果は[MAP-02D 性能計測](./Map02DPerformanceReport.md)を参照する。計測用の拡大Viewportは製品の最大ズームではない。また、原点変更なしの対照や60FPS目標は計測時だけの条件であり、Sceneへ保存しない。通常の再現確認は既定の512タイル閾値と12／36タイル/秒で行う。

Profilerでは、全体の`Farmware.WorldGeneration.TilemapPresentation`と原点変更本体の`Farmware.WorldGeneration.FloatingOriginRebase`に加え、`DisplayOriginSynchronization`、`PresentationWorkCollection`、`TerrainNeighborHaloBuild`、`SubChunkClassification`、`SubChunkTilemapWrite`、`SubChunkTilemapClear`、`RockWallShadowBuild`を確認できる。岩壁影は`RockWallShadowMask`、`RockWallShadowOpacity`、`RockWallShadowGeometry`、`RockWallShadowUpload`へ分かれ、自動原点変更で既存Meshを移した時間は`RockWallShadowReposition`へ記録する。いずれも名前空間接頭辞は`Farmware.WorldGeneration.`である。子Markerは親Markerの時間に含まれるため、合算時に二重計上しない。

地形表示再利用の比較は、PlayMode中に`World Grid / Ground Tilemap`の`ChunkTilemapPresenter`で`Use Chunk Local Tilemaps`をONにして行う。既定はOFFで、比較用変更をSceneへ保存しない。切替直後は旧表示を消去して通常予算で再反映するため、表示完了を待ってから移動を始める。新方式では実行時の`Chunk Local Terrain`以下へチャンク内0〜63のTilemapを作り、自動原点変更は`ChunkTilemapReposition`で位置更新だけを計測する。消去済みの空きプール上限は`Maximum Pooled Chunk Presentations`（候補8）であり、可視範囲やECS保持数の上限ではない。停止・無効化・World切替で実行時表示を破棄する。

比較方式では、通常の原点変更2地点に加え、Y=64付近の上下チャンク境界、岩壁と影、水面、F3/F4、開発者可視化を確認する。現行URP 2DのOrthographic距離ソートへ小さい相対Z差を与えて上下順を保っているため、Camera方向や透明ソート設定を変える場合は再検証する。最大ズームとGPU / SetPass評価が残るため、既定方式への採用は未確定である。

生成の非同期公開比較では、`ChunkGenerationPublish`が完了確認後のECSコピー、`ChunkGenerationSchedule`が作業枠確保・Job投入を計測する。接頭辞はいずれも`Farmware.WorldGeneration.`である。`PresentationWorkCollection`と`ChunkStreaming`も同時に確認し、生成Jobの待ちが別の区間へ移っていないことを調べる。Workerの生成は`GenerateChunkJob (Burst)`で確認する。開発者可視化は従来どおり`ReadyToApply`／`Active`の完成Bufferだけを読むため、生成中の作業配列は表示しない。新規メニューやプレイヤー用キーは追加しない。

ANIMAL-01Bの個体群Worldでは、通常フレームで座標順の有限バッチを先行投入する。バッチが未完了なら`FixedTickReadinessState`で固定Tick自体を延期し、Main ThreadでJobを待たない。完成後の固定Tick入口で1バッチを一括公開して次を投入し、同じ描画フレームの追加追いつきTickは止める。待機開始時に到来済みのTickは最大1件だけ保持し、それを超える壁時計遅延は通常の追いつき上限と同様に破棄する。C3-C1では1チャンクの水文8行と地形・資源を9作業へ分け、行ごとに独立したHeap領域を使ってWorker上で並列生成する。`ChunkGenerationBoundaryWait`、`ChunkGenerationHydrologyBaseWait`、`ChunkGenerationLakeWait`、`ChunkGenerationTerrainWait`は、`IsCompleted`確認後の完成済みHandle確定費用を計測し、未完了Jobの待機時間は含めない。最大ズーム・通常／Shift移動・停止中移動で地形が順次表示され、再開後に`Animal Population`の確定位置が追加されることを確認する。候補条件・色・凡例・HUDは変更せず、生成中の専用配列も可視化へ渡さない。

`Animal Population`の確定位置は、全個体を毎Repaintで配列化せず、ECS Chunkの範囲・位置変更版・構造変更版を使って可視位置だけをキャッシュする。これは64×64論理地域とは異なる格納Chunk単位の粗い索引で、範囲が画面と重なるChunk内では個体ごとの座標判定を行う。初回・位置書込・構造変更時には該当Chunkの全座標を読み直すが、不変の画面外Chunkは範囲だけで除外する。Camera移動、個体移動、個体の削除、World切替で色が古いまま残らないことを確認する。広域個体が同じECS Chunkに混在する場合や全個体が毎Tick移動する場合の費用まで解消したとは扱わない。

岩壁影マスクの改善後も同じ`RockWallShadowMask`で比較する。80×80のマスク作成は同期Burst `IJob.Run`でメインスレッド上に完了し、Workerへ未完了Jobを残す方式ではない。親の`RockWallShadowBuild`、後続の`RockWallShadowOpacity`も確認し、Native配列参照への変更で後続処理へ負荷が移っていないか調べる。停止・再開時のNativeメモリ寿命、最終Water／Magmaを挟むチャンク境界の影、3×3完成待ち、原点変更前後の位置も確認する。Rock Wall開発者可視化も製品表示と同じ完成近傍を読み、凡例とキー／メニューは変更しない。

影なしの停止を調べる場合は、`SubChunkClassification`と`SubChunkTilemapWrite`の同フレーム内の回数も確認する。書込MarkerにはTilemapの取得・再利用処理も含まれ、子の`ITilemap.FindAllRefreshPositions`だけを全書込時間とはしない。フレーム間隔はMain／Render ThreadのTimelineで`WaitForTargetFPS`、描画コマンド待ち、Renderer更新と照合し、表示Markerの時間やGPU時間と混同しない。CSV／PNGの出力と終了時の大きな停止は移動区間から除外する。詳しい帰属と制約は[影なしのスパイク切り分け](./Map02DPerformanceReport.md#影構築なしの表示スパイクとフレーム待機の切り分け)を参照する。製品の目標FPSや予算の既定値は、この診断だけで変更しない。

分類用外周の改善後は、前処理の`TerrainNeighborHaloBuild`と後続の`SubChunkClassification`を合わせて比較する。現在フレームで同じチャンクへ反映する連続サブチャンクの外周だけを、同期Burstで周囲8チャンクの完成Bufferから複写して同じバッチで再利用する。フレーム間の値は再使用せず、内部セルは常に現在のECS Bufferを読む。基礎生成へ戻るフォールバックはなく、ワールド内の必要近傍が未完成なら反映を待つ。最終Water／Magmaとの境界ハイライト、露出岩壁の資源、岩壁手前の資源レイヤー、負座標、方式切替・停止再開を確認する。キー、メニュー、カメラ、開発者可視化の凡例は変更しない。

空レイヤーへの書込省略は比較ONのチャンク別Tilemapだけに適用する。`SubChunkTilemapWrite`は配列の空判定も含めて測り、`SubChunkTilemapClear`の全レイヤー消去は従来どおりである。以前タイルがあった領域へのnull書込は省かない。資源・液面・境界が残らないこと、部分反映からの消去とプール再利用、正負の原点変更、比較OFFへの切替を確認する。見た目の間引きやレイヤー削除ではなく、既知の空領域へ同じ空配列を再送しない処理である。

### 2.2 World Generation Sprite Atlas Prebuild

- 実装: `WorldGenerationSpriteAtlasPrebuildGenerator`
- 実行: Playerビルド直前にUnity Build Pipelineから自動実行する。専用メニューは持たない。

#### 用途

- Sprite Packer Modeを`Sprite Atlas V2`へ統一する。
- 新18Biomeの仮色土地PNG 18枚を`GroundTerrainAtlas`、本番用`terrain_rock_wall_01.png`を`RockWallAtlas`、14区画の`terrain_boundary_highlights.png`を`BoundaryHighlightAtlas`のPackableへ登録する。旧7種の`terrain_biomes_base.png`と撤去済みの旧境界TextureはSceneやAtlasから参照しない。
- Built-in配信、Point、Mip Maps Off、Read/Write Off、RGBA32、無圧縮、Padding 4 px、Rotation/Tight Packing/Alpha Dilation Off、Max Texture Size 512を再適用する。
- 対象Build Target向けにパックし、各Atlas内のSpriteが1枚のTextureページへ収まらない場合はビルドを中止する。特に土地の複数ページ化は、バイオーム境界をまたぐIndividual Yソートを壊すため許可しない。

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
| `Soil` | 通常表示へ出さない標準、肥沃、砂質の`SoilType` |
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
| `Water Salinity` | 陸地、淡水、海水 |
| `Water Body` | 陸地、川、池、海 |
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

- メニュー: `Farmware > World Generation > Analyze Distribution`
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

- メニュー: `Farmware > World Generation > Analyze Geology Distribution`
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

- メニュー: `Farmware > World Generation > Analyze Animal Population`
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

- メニュー: `Farmware > World Generation > Analyze Hydrology Distribution`
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

- メニュー: `Farmware > World Generation > Analyze Derived Hydrology Distribution`
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
- 実設定を取得できない場合は生成を停止し、別の既定値へフォールバックしない。
- 初回はCanvasの両辺が有限値かつ64ポイント以上になるまで生成を待ち、未確定の`NaN`寸法から1×1画像を作らない。画像完成までは地図領域の中央と画面下部へ生成進捗の割合を表示する。連続環境軸・地質の通常モードと基礎水文は完成行数、`Terrain`、`Biome`、派生水文は処理時間の大半を占める完成チャンク数へ95%、色・Hoverコピーを作る画像行へ残り5%を割り当てる。完成チャンク使用モードはGate待機開始時に1%を通知し、16行未満の低解像度でも完成まで0%のままにしない。再生成時は前の画像を維持し、下部の進捗を更新する。Hover情報は初回からBiome選択診断を含む最大17行分の高さを確保し、完成後に情報欄が地図を縮めて重い完成チャンクを二度生成しないようにする。Window最小寸法は720×700ポイントとし、地図の最小240px、Hover診断280px、ヘッダー、操作欄、状態欄を同時に表示して、親Panelのクリップで地図や診断を欠けさせない。

`Terrain`はC5-Cの完成チャンクを使い、18Biomeの仮色、淡水、海水、マグマ、岩壁を優先した最終地形を表示する。C6前のためSoilType別の本番Block差分は反映せず、土壌分布はPlayMode中の`Debug Visualization > Soil`で確認する。
`Biome`は水域や障害物の下も含む論理バイオームを表示する。`Terrain`と`Biome`のHover／中央情報は、地上タイルについて製品最終化と同じ固定入力から得た最終選択理由、通常首位・次点、候補数、Score、Variation、両候補の8軸適性を表示する。最終IDと理由は2行、各候補の名前と順位値は2行、8軸適性は候補ごとに3軸・3軸・2軸の3行へ分ける。診断全体を常に12行とし、430px幅のGameビュー内容領域で右端を切らず、Map Viewerの自動折り返しに依存しない。GameビューのDebug Visualizationは全モード共通の右上1パネルを使い、内容が描画領域の高さを超える場合だけ右端の縦スクロールバーを表示して末尾まで確認する。高さが収まる場合はバー用の幅を予約しない。ゲーム側のホイール操作と競合させないため、HUDはマウスホイールを取得せず、表示中のバーをドラッグまたはクリックした場合だけ移動する。スクロール中は文字Styleを`Clip`とし、HUDの上下枠より各2px内側へ固定した明示クリップ範囲で文字と凡例を囲んで外へ描かない。モード変更時は先頭へ戻る。パネル高は実際に描く文字列の行数から求め、末尾には6pxの余白だけを設ける。Biomeでは18Biome凡例、`Geology`・`Volcanic`・`Coastal`を含む選択診断、資源・暫定土壌までの共通詳細を同じ順序で保持し、省略しない。海域は地上Biome選択の対象外と明示する。診断値は画像生成時にManaged値へコピーし、Pointer移動ごとに完成チャンクや分類を再実行しない。`Elevation`、`Base Temperature`、`Terrain Temperature`、`Base Moisture`、`Woody Vegetation Potential`、`Terrain Ruggedness`、`Coastal Influence`、`Volcanic Activity Potential`はGameビューのDebug Visualizationと同じRGBで連続環境軸を表示する。`Geology Class`、3基礎特性、`Geology Flags`、`Geology Dominant Site`、`Geology Site Boundary`もGameビューと同じ色定義を使う。`Geology Dominant Site`の凡例色は固定の分類色ではなく、サイト座標から生成される実際の色の例であるため、地図には凡例と異なる色も表示される。同じサイトは同じ色を保ち、正確な境界確認には`Geology Site Boundary`を使う。サイト境界は広域縮小でも確認できるよう、上下左右の隣接代表サンプルとのサイト差を表示するため、Gameビューの隣接1タイル判定より太く見える場合がある。

Gameビューの開発者HUDは`OnGUI`内の`UNITY_EDITOR`条件で描画開始を製品版から除外する。一方、Map Viewerと共有する副作用のないBiome診断Formatterは、Playerにも含まれるPresentation Assembly内の呼び出し元と同じコンパイル範囲へ置き、非Editorコンパイルで未定義参照を作らない。

C3基礎水文5モードはGameビューと同じRGBで、流向、終端、集水面積、渓流・河川、流域・湖を表示する。`Hydrology Lake`だけはC4-Cの水収支まで生成し、一時湖、恒久塩湖、閉鎖淡水湖、越流淡水湖をGameビューと同じ色で表示する。C4派生水文5モードはC4-E1aの完成チャンクをバックグラウンドで生成・再利用し、`Hydrology Groundwater`、`Hydrology Natural Soil Saturation`、`Hydrology Spring`、`Hydrology Natural Flooding`、`Hydrology Wetland Candidate`を表示する。RGBと分類条件はC4-E2のGameビュー色変換APIを共有する。完成チャンク生成はEditorプロセス内で同時に1要求へ制限し、連続操作で取消済み要求と最新要求の大容量Native配列が重複しないようにする。取消済み要求は実行中1チャンクの完成後に終了するため、最新要求の開始まで数秒待つ場合があるが、Editorメインスレッドは停止しない。生成中に前の画像を維持する場合、Hover診断もDropdownの新モードではなく画面へ適用済みの旧画像モードを参照し、未完成の派生水文を取得失敗として表示しない。完成チャンクのNative配列は画像生成後に破棄し、Hover／中央情報には生成時にManaged値へコピーした地下水、接続状態、自然飽和、泉、自然冠水、淡水接続、湿原資格、広域規模を表示するため、Pointer移動で重い再生成を行わない。現在降雨・灌水・排水・現在土壌水分・現在水位は表示しない。基礎水文のHoverと中央情報には固定水文セルの全診断値と最終湖区分を追加し、Lake凡例では打切り流域を`Truncated basin (not a fixed lake)`として固定湖候補と区別する。木、石、砂の資源分布は現時点では対象外である。

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
- Map Viewerの色、凡例、モードCatalog、Hoverデータ契約、座標変換、パン、ズーム、非同期処理を変更した。

#### 確認内容

1. 原点付近と既知の遠距離座標で、Map ViewerとGameビューのバイオーム・水域・マグマ・岩壁、8つの連続環境軸、地質7モード、C3基礎水文5モード、C4派生水文5モードが一致する。Biomeモードでは同じ地上タイルの最終理由、通常首位・次点、候補数、8軸適性が両画面で一致し、海域は対象外と表示される。同じBiome内では標準・肥沃・砂質の土地表示が同じであり、`Debug Visualization > Soil`へ切り替えた場合だけ3種類を別色で確認できる。サイト境界は、Gameビューでは隣接1タイル、Map Viewerでは隣接代表サンプルという縮尺差を踏まえて同じ支配サイト変化を示す。水文Lakeでは打切り流域と固定湖候補が別色になり、一時湖、恒久塩湖、閉鎖淡水湖、越流淡水湖の色・凡例・最終区分表示が両画面で一致する。派生水文では5つの凡例、色面、Hover／中央固定値が選択モードと一致する。
2. `実設定を再読込`後のSeed、WorldSeed、GeneratorVersion、設定取得元が想定どおり表示される。
3. 負座標への移動、ドラッグ、ホイール、＋・－Buttonで中心と縮尺が正しく変化する。＋・－ButtonではTexture解像度が奇数・偶数のどちらでも中心タイル座標を動かさない。再生成直後のHover情報は、偶数解像度でもSide Panelと同じ中心座標を示す。Windowを768ポイントより大きくした場合と、生成Textureの幅または高さが奇数になる大きさにした場合のどちらでも、カーソル下の座標、グリッド、原点印が地形とずれず、正方形のグリッドが維持される。基礎水文を通常サイズで表示し、中央以外へPointerを置いてホイールズームした場合も、64サンプルのTexture上でPointer下の代表タイルを維持する。派生水文では縮尺に応じた低解像度へ切り替わっても同じ地点を維持し、連続操作時は前の要求をキャンセルして最新画像だけを反映する。
4. 同じSeed・座標・縮尺へ戻ると同じ画像になる。
5. 開いた直後に単色の1×1画像を引き伸ばさず、地図領域に生成進捗が表示され、完成後に画像へ切り替わる。連続操作で古い非同期結果や進捗が後から上書きされず、Editorが長時間応答不能にならない。

### 2.10 Terrain Tile Art Generation Settings Inspector

#### 用途

地形タイル試作工程の16×16 px手描き入力から、`Face`と`FaceBoundaryOverlay`を事前合成した断面1段目Spriteを生成する。`GroundTop`はGround／Wallで同じ入力Spriteを共有し、生成ツールでは複製しない。Runtime表示やSimulation状態は変更せず、後続の地形表示が参照する境界合成PNGの制作工程だけを担当する。

Projectビューの`Create > Farmware > Terrain Tile Art Generation Settings`から設定Assetを作成する。Inspectorでは次を編集・確認できる。

- `Assets/`配下の出力ルート。
- 材質ID、日本語確認名、全候補を均等確率で選ぶ可変長の`GroundTop`、`Face`、任意の`FaceBoundaryOverlay`。個別Weightや出現率Overrideは持たない。
- `DarkenWallSurface`と`WallDarkenFactor`。新規材質の初期値は暗色化ON、係数0.85とする。
- 選択材質の手描き入力数、境界生成数、Runtime候補数、Ground／Wall用境界の内訳、要再生成状態。
- 選択材質で生成済みのGround用境界とWall用境界の画像一覧。各画像をクリックするとProjectビューで対応するSpriteを選択する。

`対象材質を検証`／`対象材質を生成`は、ほかの未制作材質が空でも代表材質を1件ずつ試作できる。`全材質を検証`／`全材質を生成`は登録済み全材質を先に検証し、総出力枚数の確認後に一括生成する。入力Spriteは16×16 px、Read/Write有効、Point、非圧縮、Mip Mapなし、sRGB、Alpha 0または255とする。不正なID、重複ID、空の必須配列、未設定Sprite、範囲外の暗色化係数も生成前に日本語で報告する。

各配列内の完成Sprite候補はC6-Dで均等確率かつ決定論的に選ぶ。生成設定AssetとInspectorにはWeight調整を設けず、配列内容と順序の変更は画像Fingerprintと要再生成状態へ反映する。

出力先は`<Output Root>/<Material ID>/FaceBoundary/`である。Ground／Wall境界は`Face`番号と`FaceBoundaryOverlay`番号の全組合せを用途別に生成する。16×16画像は64×64の整数4倍で表示し、再コンパイルやEditor再起動後も保存済み生成パスからSpriteを再読込する。暗色化が無効ならWall用境界PNGを重複生成せずGround用を再利用する。同名PNGの再生成ではGUIDを維持し、差分数を減らした場合は前回記録された同材質の不要PNGだけを削除する。旧`WallTop`生成フォルダーは新方式への移行時に削除済みである。

#### 次の修正をしたら確認する

- 暗色化の色空間、丸め、対象部位、Alpha規則を変更した。
- 入力配列、画像Fingerprint、生成ファイル名、出力先、Import設定を変更した。
- C6-Dで完成Spriteの参照方法やPivotを確定・変更した。

#### 確認内容

1. 専用EditModeテストが、暗色化、Overlay合成、半透明拒否、可変Sprite数、専用WallTop設定の不存在を保証する。
2. `GrassLoam`で対象材質の検証・生成を行い、必要なGround／Wall境界合成Spriteだけが所定フォルダーへ出る。
3. 生成PNGのImporterがPoint、16 PPU、非圧縮、Mip Mapなし、sRGB、`Full Rect`、中央Pivotになり、再生成後もGUIDが変わらない。
4. 入力画像、配列順、暗化の有効状態または係数を変更すると要再生成表示へ変わり、再生成後に最新表示へ戻る。
5. Inspectorの生成結果プレビューにGround／Wall境界の全`F×B`組合せが別一覧に64×64の整数4倍で表示され、画像をクリックすると対応する生成SpriteをProjectビューで選択する。
6. Wall上面は生成PNGではなく同じ`GroundTop`参照へ表示時Tintを掛けること、`Face`自体は暗色化されないことをGameビューで確認する。

### 2.11 Open Terrain Tile Inspection Map

- メニュー: `Farmware > World Generation > Open Terrain Tile Inspection Map`
- 実装: `TerrainTileArtInspectionMapBuilder`
- PlayMode: 不要。EditModeでSceneを構築し、Gameビューで確認する。
- 出力: `Assets/Scenes/Development/TerrainTileArtInspection.unity`

#### 用途

`TerrainTileArtGenerationSettings`に登録した`GrassLoam`と、その設定から生成済みの完成PNGを読み、次の検品項目を480×270・16 PPUのPixel Perfect Camera内へ固定配置する。

- `GroundTop`を座標Hashで選択した12×7セル。セル枠、周期的な縞、明暗ノイズ、タイル境界の見切れを確認する。
- Groundの上へ同じ`GroundTop`を重ね、材質設定の`WallDarkenFactor`をTintとして掛けた5×3 Wallパッチ。手前列には通常`Face`を1段表示する。
- 全登録`Face`×全登録`FaceBoundaryOverlay`の組合せを、Ground用とWall用で別々に並べた一覧。現行`GrassLoam`では4×4の各16枚であり、Wall用だけ境界Overlayが暗色化され、`Face`本体は変わらないことを比較する。
- Ground用とWall用の境界合成結果を上下2列に並べ、Wall用だけOverlayが暗く、`Face`本体は変わらないことを確認する。

メニュー実行前に、設定アセット、`GrassLoam`の入力、Ground用境界16枚、暗化有効時のWall用境界16枚、全入力の16×16 px・Import設定を検証する。不足があればSceneを閉じず、先にInspectorから`GrassLoam`を再生成するよう案内する。

未保存Sceneがある場合は保存確認を表示し、キャンセル時は何も変更しない。続行時は固定検品Sceneを新規構築して同じパスへ保存し、そのSceneを開いたままにする。これは生成物なのでSceneへ手修正を加えず、素材または配置規則を変えた場合はメニューを再実行する。

#### 確認手順

1. `TerrainTileArtGenerationSettings`のInspectorで`GrassLoam`が「生成済み」になっていることを確認する。
2. メニューを実行し、Gameビューを480×270またはその整数倍にする。
3. Hierarchyの`Ground and Wall x0.85`と`Face Boundary Rows`を順に確認する。
4. `GroundTop`のコントラスト、GroundとWallの識別、Face内コントラスト、GroundからFaceへの材質変化、境界合成、Pixel Perfect表示を記録する。

#### 現段階で対象外の確認

この固定Sceneは`GrassLoam`単一材質のアート試作を対象とし、共通境界ハイライトの47状態、異材質Wall境界、`Shadow Caster 2D`、チャンク境界、Liquid、Runtimeの本番差分選択は扱わない。共通境界ハイライトは専用の`Open Boundary Highlight Inspection Map`で確認し、残りは本番表示を接続する後続工程で追加する。

### 2.12 Open Wall Surface Darken Comparison

- メニュー: `Farmware > World Generation > Open Wall Surface Darken Comparison`
- 実装: `TerrainTileArtInspectionMapBuilder`
- PlayMode: 不要。EditModeで比較Sceneを構築し、Gameビューで確認する。
- 出力: `Assets/Scenes/Development/TerrainWallSurfaceDarkenComparison.unity`

#### 用途

`GrassLoam`の`GroundTop`を地面とWall上面で共有し、Wall上面全体を暗化した場合の視認性を比較する。比較の結果、正仕様では係数0.85、共通Wall境界ハイライト、`Face`、`Light 2D`と`Shadow Caster 2D`の組合せを採用した。現行Sceneは暗化係数とFaceだけを比較し、境界ハイライトは専用の47状態検品Scene、動的影は後続のShadow Caster検品で確認する。

各比較領域では全面へ通常の`GroundTop`を描画し、固定Wall形状のセルだけ、直下のGroundと同じSprite参照をWall層へ重ねる。Wall層の`SpriteRenderer.color`へRGB係数を乗算し、左から0.85、0.70、0.55の3条件を同じ差分配置で並べる。手前が空いているWallには通常`Face`を1段だけGroundより手前へ重ね、上面の区別と立体感を同時に比較する。未制作の内角Sprite、47接続の生成PNG、入力Fingerprintには依存しないため、Wall方式の変更前でも実行できる。

#### 確認手順

1. メニューを実行し、Gameビューを480×270またはその整数倍にする。
2. 左0.85、中央0.70、右0.55を見比べ、GroundとWallの境界、Wall内部の模様、Faceとのつながりを確認する。
3. 暗い洞窟背景でもWall形状を読めるか、0.55で材質色が失われないか、0.85でGroundと混同しないかを確認する。
4. 採用値0.85とWall境界ハイライトの組合せは専用47状態検品Sceneで確認する。`Shadow Caster 2D`を追加した後、光源移動、異材質Wallの共有辺、チャンク境界を確認する。

### 2.13 Generate／Open Boundary Highlight Art

- 生成メニュー: `Farmware > World Generation > Generate Boundary Highlight Art`
- 検品メニュー: `Farmware > World Generation > Open Boundary Highlight Inspection Map`
- 実装: `TerrainBoundaryHighlightArtGenerator`、`TerrainBoundaryHighlightInspectionMapBuilder`
- 出力PNG: `Assets/Farmware/Art/WorldGeneration/Terrain/Highlights/terrain_boundary_highlights.png`
- 出力Atlas: `Assets/Farmware/Art/WorldGeneration/Atlases/BoundaryHighlightAtlas.spriteatlasv2`
- 出力Scene: `Assets/Scenes/Development/TerrainBoundaryHighlightInspection.unity`

#### 用途

Runtimeの8近傍47状態と同じ特徴マスクから、全材質共通の境界ハイライト14基本形を再現可能に生成する。PNGは224×16 pxで、16×16 pxの14区画を横一列へ配置する。外周は不透明白1 pxの連続線、内角は欠けた対角側の角に置く不透明白1 pxの点とし、実表示ではTile側のAlpha 20%を掛ける。GroundハイライトのRGBは白、WallハイライトのRGBはWall上面と同じ既定係数0.85で暗化し、いずれもBaseより後から描画する。ImporterはPoint、16 PPU、非圧縮、Mip Mapなし、`Full Rect`、中央Pivotに固定し、専用Atlasの同一ページへ収録する。

固定検品Sceneは正規化済み47状態を8列で並べ、左へ通常の`GrassLoam` Ground、右へ同じSpriteを0.85暗化したWallを表示する。各状態のHierarchy名には接続Mask、14基本形番号、時計回り90度回転数を含める。完全接続の1状態だけは境界なし、残り46状態には境界が表示される。これは形状と暗化時の見え方を比較するSceneであり、`Face`、Liquid、`Light 2D`、`Shadow Caster 2D`は含めない。

#### 確認手順

1. 生成メニューを実行し、PNGが14 Spriteへ分割され、`BoundaryHighlightAtlas`の同一ページへ収録されることを確認する。
2. 検品メニューを実行し、Gameビューを480×270または整数倍にする。
3. 左のGround 47状態と右のWall 47状態で、外周1 px、内角1 px、90度回転、完全接続だけが境界なしであることを確認する。右側ではBaseとハイライトRGBの両方へ0.85が掛かり、白い線だけが浮いて見えないことも確認する。
4. 20%の白線が明るく浮きすぎず、0.85暗化Wall上でも形状を読めることを確認する。
5. `Build Fallback Map Prototype`を実行した場合は、SampleSceneのGround／Wall境界Sprite配列が14件で、Missing参照がないことも確認する。

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
| `GrassLoam`の反復、Ground／Wall共通上面、Wall暗色化、通常Face、Ground／Wall用境界合成のGameビュー検品 | Open Terrain Tile Inspection Map |
| GroundTopをWall上面へ再利用した場合の暗化係数とGround上への重なり比較 | Open Wall Surface Darken Comparison |
| 共通境界14 Spriteの再生成、Atlas登録、Ground／Wall各47状態の外周・内角確認 | Generate Boundary Highlight Art、Open Boundary Highlight Inspection Map |
| 生成規則と表示の両方 | Analyze Distribution、Map Viewer、Debug Visualization |

Editorツールだけで検証を完了しない。対応する自動テストは[テスト](./Testing.md)に従って実行し、見た目に関係する変更はGameビューでも確認する。

## 4. このドキュメントの更新条件

### 仕様転換に伴う実装時の更新予定

- WORLD-01CでLayerキーをビューア・中央情報・可視化の検索へ接続済み。Map Viewerの`Layer`欄、または`Farmware > World Generation > Debug Visualization > Layer`からSurface／Underwater／Deep Seaを選ぶ。Surface以外の自然生成はTERRAIN-01Cまで未接続のため、Map Viewerは暗色と未接続メッセージ、Gameビュー可視化は空表示を返す。
- TERRAIN-01AではBlock定義と有効セルAPIをSimulationへ追加するが、色判定は既存`GeneratedTile`互換のままとする。仕様転換に合わせて`Terrain Placement`からベルト固有のHUDと凡例だけを除き、差分込み表示、Blockパレット、自然生成と実ワールドの表示切替はTERRAIN-01Cで追加する。
- TERRAIN-01Bで採用したバイオーム構成と安定ID方針に基づき、TERRAIN-01C1〜C5で環境軸・地質・水文・Biome診断、TERRAIN-01C6でBlockパレットと変更後地形、TERRAIN-01C7で完成タイル上のBiome・水系・Block景観比較、TERRAIN-01C8で資源診断、TERRAIN-01Dでスポーン許可・探索失敗理由を追加する。地形タイル試作工程では[地形タイル表現仕様](../Art/TerrainTileArtSpecification.md)に従い、16×16 `GroundTop`、材質ごとに任意の透明な16×16 `FaceBoundaryOverlay`、Ground崖面とWall前面で共用する16×16 `Face`を代表5材質で手制作・試作してから量産する。Wall上面は同じ材質の`GroundTop`を共有し、通常材質では表示時に`WallDarkenFactor` 0.85を乗算する。Ground／Wallの形状境界には既存の全材質共通14形状境界ハイライトを使い、Wall同士は材質を問わず接続扱いとする。断面は1段目から`Face`を使い、境界ありではGround用に元色Overlay、Wall用に同じ係数で暗色化したOverlayを`Face`へ事前合成する。`Face`自体は暗色化しない。各部位は全候補を均等確率で選ぶ可変長配列とし、個別Weightや出現率Override、固定差分数は設けない。全`Face`差分は相互接続可能とする。淡水・海水では通常断面へ半透明の`Liquid深度Overlay`を重ね、マグマは接触する最初の段から不透明にして背後の断面を隠す。動的な影は`Light 2D`と、Wall領域からサブチャンク単位で作る`Shadow Caster 2D`で表現する。穴と崖面表示深度より下には不透明な`DeepBackground` `#0D1016`を表示し、液体専用断面は作らない。細道の要否は試作後に別途判断する。Map Viewerの18Biome識別と自然生成・実ワールド差分の明示は、最終BiomeとBlockを接続するC5・C6で更新する。C7では同じSeed・範囲の変更前後を比較できる出力を維持し、Biome分類、水文、Blockのどの入力を調整したかを個別モードで判別できるようにする。
- 41材質の`GroundTop`色・使用比率・模様方針は、地形タイル表現仕様第7章の確定値として扱う。Wall上面は同じ`GroundTop` Spriteを参照し、通常材質では表示時にsRGB各成分へ0.85を乗算する。Alphaと使用比率は変更しない。Wall用`FaceBoundaryOverlay`の不透明画素にも同じ係数を使い、Ground用Overlayと`Face`は変更しない。`FaceBoundaryOverlay`と`Face`の元パレットは`GrassLoam`の試作を人間確認後、同じ明度関係を残り40材質へ適用して確定する。
- 地形タイル試作工程の材質生成設定Inspectorは2.10節のとおり、`GroundTop`・`Face`・任意`FaceBoundaryOverlay`とWall暗色化だけを扱う方式へ更新済みである。専用`WallTop`、7外周部品、共通暗色内部、47枚の材質別Wall完成Spriteは削除した。WorldLayer・絶対座標・断面段・安定材質ID・用途別SaltによるRuntimeの決定論的な均等差分選択は、表示へ接続するC6-Dで実装する。
- TERRAIN-01C3-D2では、生成済み固定水文セルの流向・下流座標・終端・辺数・集水面積・打切り・最遠寄与距離・流出・流域・湖をGameビューとMap Viewerへ接続した。海洋流出口は流向なしの`Marine`終端、4,096辺または4,096セルで確定できない結果は`LongDrainage`または打切り色として表示する。Level 0〜6集約SummaryとLevel 0〜12 Downstream Summaryの利用・分解・キャッシュ統計は固定生成結果ではない比較実装の内部診断なので、性能テストだけで記録し表示へ混ぜない。打切られた大規模閉鎖流域は固定湖候補と別色・別理由で表示する。
- TERRAIN-01C5では、環境として自然な直接接続を認めるv0.48の方針に合わせ、安定`BiomeId` 1〜18、全18Biomeの153ペアの境界長・接触率、両側の気温・湿度・標高と差、植生・地質・火山段階・水文・海岸条件、最終選択理由を境界診断へ追加する。C5前の旧7種や旧IDを選べるモード、凡例、配色、中央情報は残さず、旧版データは表示で補完せず版不一致として拒否する。TERRAIN-01C1で削除した旧7Biome向けの`Biome Transition`を復活させず、第三Biome強制の仕様へ戻さない。新方式では橋不足や接続例への未記載を異常表示にせず、不正候補・必須ゲート違反と、景観品質の確認事項を区別する。湿原の専用Host許可・不許可とOverlay理由、火山中核の地質・活動度を表示し、周縁リングの欠落だけをエラーにしない。153ペア監査と最終選択理由はC5の更新予定であり、C1時点で実装済みとは扱わない。
- TERRAIN-01C6では、最終Block・Liquid表示への置換と同時に、旧`GeneratedTile.WaterBodyType`、旧River・Pond生成、旧Biome直結配色を読むモード、凡例、中央情報、互換変換を削除する。14形状の接続選択処理、Ground／Wall用Tilemap、再制作した白い境界Spriteと専用Atlasを維持し、新しい有効地形へ接続する。Sceneの境界Sprite配列は14件を必須とし、Missingや空配列を許可しない。Wall接続では材質IDを比較せず、連続したWall領域の外周・内角だけへ表示する。比較用の旧表示切替は残さず、新しい固定水文・有効地形だけを診断の入力にする。F4のタイル・サブチャンク・チャンク境界診断は地形アートとは別機能なので維持する。
- TERRAIN-01C／GATHER-01／AUTO-01D: 編集後の隣接差分、穴・液体、反応・保留キューに加え、`LooseSurface`／`GroundEmbedded`／`WallEmbedded`／`MassiveDeposit`の形態、Host、採掘済み差分、巨大鉱床の残量・占有を表示する。自然生成Block、配置済みBlock、クラフト用資源を区別し、元生成を現在地形と誤表示しない。
- ECO-01／JOURNAL-01: 永続／匿名、地域会計、収容数・回復、証拠判定を確認可能にする。
- DIVE-01／VEHICLE-01: 現在Layerと直下プレビュー、遷移失敗、高速先読み・生成待ちを診断する。
- 構築ツールが既存の本番参照・採用GUID・未保存Sceneを守り、新プレイヤー・保存設定へ更新できることを各PRで確認する。

これらは予定であり、実装PRで具体的なメニュー・用途・確認手順へ置換する。

Editorメニューの追加、削除、名称変更、実行条件変更、出力項目変更、確認対象変更を行った場合は、同じcommitにこの文書の更新を含める。
