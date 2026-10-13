# Unity Editorツール

- 最終更新日: 2026-10-13
- 対象: `Assets/Farmware/Editor/WorldGeneration/`

## 1. 用途

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

- 上面16pxと断面8pxを結合した16×24の仮色土地Tile、16×16の仮色液体・資源Tileを生成または更新する。
- 上面16pxと側面12pxを結合した16×28岩壁Tile、淡水・海水・マグマ各4枚の透明フレームとAnimatedTileを生成または更新する。Sprite PivotはSprite Editor Data Provider経由で設定する。
- 土地7種を同じTextureページへ収める`GroundTerrainAtlas.spriteatlasv2`と、岩壁用の`RockWallAtlas.spriteatlasv2`を生成または更新し、現在のBuild Target向けにパックする。
- `WorldGenerationSettingsSubScene`へ生成設定のAuthoringを配置する。
- `SampleScene`へCamera、Grid、Ground・静止液面・液面アニメーション・岩壁・岩壁背面資源・通常前面資源・岩壁上資源の7枚のTilemap、表示用Component、SubScene参照を接続する。静止液面と液面アニメーションは土地より8px下、岩壁上資源は土地基準面より12px上へ設定する。GroundとRock WallはIndividual ModeかつTop Left順で描画する。通常資源は直前方の岩壁有無からOrder 2の背面用TilemapとOrder 10の前面用Tilemapへ振り分け、遠方座標でも岩壁との前後関係を固定レイヤーで保証する。
- Pixel Perfect Cameraの基準解像度480×270を含む、再現可能な確認環境を構築する。

#### 実行後の注意

このツールはアセットとSceneを保存する。実行後は必ずGit差分を確認し、意図しないScene全体の書き換え、不要アセット、`.meta`のGUID変更がないことを確認する。

#### 次の修正をしたら確認する

- `WorldGenerationAuthoring`の設定項目、既定値、Baker変換を変更した。
- バイオーム、地面、水域、マグマ、岩壁、資源の種類またはフォールバック色を変更した。
- Ground、Liquid Base、Liquid Surface Animation、Rock Wall、Resource、Rock Wall ResourceのTilemapレイヤー構成、Transform、描画Mode、Sorting Order、参照関係を変更した。
- Camera、Pixel Perfect Camera、480×270の基準解像度、黒帯処理を変更した。
- `SampleScene`または`WorldGenerationSettingsSubScene`の生成構成を変更した。
- 土地・岩壁の元Texture、Sprite Slice、Atlas容量、Pixel Art向けAtlas設定を変更した。

#### 確認内容

1. Consoleに例外または新規警告が出ていない。
2. 必要なPNG、Tile、AnimatedTile、Sprite Atlas、`.meta`が揃っている。全土地Spriteが`GroundTerrainAtlas`の同じパック後Textureを参照し、岩壁が`RockWallAtlas`へ分離されている。
3. SampleSceneとSubSceneの参照がMissingになっていない。
4. PlayModeで土地が16×24、岩壁が16×28の一体型ブロックとして表示される。静止液面と透明アニメーションは同じ8px下、岩壁内部影・露出資源は12px上へ表示される。液体はGround Tilemapへ重複せず、液面下辺と奥側ブロックの断面・側面は手前ブロックに隠れる。岩壁セルでも下層の土地断面が欠けない。直前方が岩壁の通常資源は背面用Tilemapへ入り岩壁に隠れ、それ以外は前面用Tilemapへ入って岩壁より前に見える。
5. Gameビューの地形描画領域が480×270基準の黒帯内へ収まり、HUDとF4表示も同じ領域を使用する。

### 2.2 World Generation Sprite Atlas Prebuild

- 実装: `WorldGenerationSpriteAtlasPrebuildGenerator`
- 実行: Playerビルド直前にUnity Build Pipelineから自動実行する。専用メニューは持たない。

#### 用途

- Sprite Packer Modeを`Sprite Atlas V2`へ統一する。
- 土地7種の元Textureを`GroundTerrainAtlas`、岩壁の元Textureを`RockWallAtlas`のPackableへ登録する。Texture単位で登録するため、同じPNGを4×4へSliceした将来の16差分も自動収録される。
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

| モード | 確認できる内容 |
| --- | --- |
| `Off` | 開発者用着色を解除した通常表示 |
| `Biome` | 草原、森林、砂漠、岩地、湿地、雪原、火山の論理バイオーム |
| `Elevation` | 標高の連続値 |
| `Temperature` | 気温の連続値 |
| `Moisture` | 湿度の連続値 |
| `Weirdness` | 特殊地形の候補を決める連続値 |
| `Water Salinity` | 陸地、淡水、海水 |
| `Water Body` | 陸地、川、池、海 |
| `Biome Transition` | 論理バイオームと表示地面が異なる陸上移行帯 |
| `Rock Wall` | 非岩壁、露出面、内部影の濃度 |
| `Magma` | マグマ本体と淡水を抑制する火山影響域 |
| `Resources` | 配置なし、木、石、砂の最終配置 |

#### 次の修正をしたら確認する

- ノイズ、バイオーム分類、移行帯、標高、気温、湿度、Weirdnessを変更した。
- 川、池、海、淡水・海水、マグマ、岩壁、資源の生成規則または列挙値を変更した。
- `GeneratedTile`、ECS Buffer、Presentationへ渡すデータを変更した。
- 可視化の色、凡例、HUD、Tilemap、Camera、黒帯、表示範囲を変更した。
- 新しい生成要素を追加し、通常表示だけでは内部判定を確認できなくなった。

#### 確認内容

1. すべてのモードが例外なく切り替わり、選択中メニューへチェックが付く。
2. 凡例の色とマップ上の色が一致し、文字が欠けない。
3. 黒帯へ着色や凡例がはみ出さない。
4. チャンク境界をまたいでも分類やグラデーションが不自然に途切れない。
5. `Off`へ戻すと通常表示へ完全に戻る。

### 2.4 Analyze Distribution

- メニュー: `Farmware > World Generation > Analyze Distribution`
- 実装: `WorldGenerationDistributionAuditMenu`、`WorldGenerationDistributionAnalyzer`
- PlayMode: 不要。EditModeとPlayModeのどちらからでも開始できるが、比較条件を揃えるためEditModeを推奨する。

#### 用途

固定した3シードを32タイル間隔の129×129地点で調べ、調整前後の広域分布を同じ条件で比較する。
重い純粋計算はバックグラウンドで実行し、Unity Editorの操作停止を避ける。

Consoleレポートには、次の比較材料を出力する。

- バイオーム、Ground、水域の件数と比率。
- 直接隣接するバイオームの組み合わせ。
- 強く隣接抑制する組み合わせが直接接した件数。
- 32タイル間隔で見た概算領域数、平均面積、孤立領域数。
- 64×64論理チャンク境界でバイオームが変化した比率。
- 同じ入力から同じ結果が得られたか比較する署名。

このレポートはバランスの合否を自動決定しない。変更前後の差と、Gameビューで見た地形の遊びやすさを合わせて判断する。

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

### 2.5 Map Viewer

- メニュー: `Farmware > World Generation > Map Viewer`
- 実装: `WorldGenerationMapViewerWindow`、`WorldGenerationMapSampler`
- PlayMode: 不要。PlayMode中に開いた場合は、実行中ECSの`WorldGenerationSettings`を優先する。

#### 用途

MinecraftのBiome Finderのように、無限ワールドの広い範囲を独立したEditorWindowで確認する。
シード、中心タイル座標、表示モード、ズームを指定でき、ドラッグ移動とカーソル中心のホイールズームに対応する。

Farmwareのワールドは無限なので「全体」を有限画像へ収めることはできない。最大ズームでは、画像1ピクセルにつき256タイルを代表サンプルとして、現在位置を中心に非常に広い範囲を確認する。

#### 実マップとの一致

- EditModeでは`WorldGenerationSettingsSubScene`をHierarchyへ影響しないPreview Sceneとして読み込む。
- `WorldGenerationAuthoring.CreateWorldGenerationSettingsSnapshot`をBakerとMap Viewerで共有する。
- PlayModeでは、`ChunkGenerationSystem`が使用中のECS Singletonを直接読む。
- 各代表タイルは`DeterministicChunkGenerator.GenerateTile`で生成し、ビューア専用の簡易バイオーム計算は持たない。
- 実設定を取得できない場合は生成を停止し、別の既定値へフォールバックしない。

`Terrain`は7バイオームの地面に淡水、海水、マグマ、岩壁を重ねた最終地形を表示する。
`Biome`は水域や障害物の下も含む論理バイオームを表示する。木、石、砂の資源分布は現時点では対象外である。

#### 画面上部の入力欄・ボタン

| 項目 | 説明 |
| --- | --- |
| `Seed` | 確認するワールドのシード文字列。値を変更して`移動・再生成`を押すと、Sceneの設定を変更せずに別シードを比較できる。 |
| `X` / `Y` | 画像中央に表示するタイル座標。負座標も指定できる。 |
| `表示` | `Terrain`は最終地形、`Biome`は地形で覆われる前の論理バイオームへ切り替える。変更すると現在位置を再生成する。 |
| `Grid` | 16×16タイルのサブチャンク境界と64×64タイルの論理チャンク境界の表示を切り替える。生成結果自体は変化しない。 |
| `移動・再生成` | 入力中のSeedとX/Yを確定し、その地点の画像を生成し直す。 |
| `実設定を再読込` | EditModeではAuthoring、PlayModeでは実行中ECSから生成設定とSeedを読み直し、現在位置を再生成する。入力した一時的なSeedは破棄される。 |
| `原点` | 中心座標を`(0, 0)`へ戻し、現在のSeedと表示モードで再生成する。 |
| `＋` | 中心座標を保ったまま拡大し、より細かな地形を表示する。 |
| `－` | 中心座標を保ったまま縮小し、より広い範囲を表示する。 |

地図上をドラッグすると表示範囲を移動できる。マウスホイールでは、カーソルが指している地点を基準に拡大・縮小できる。

#### 次の修正をしたら確認する

- `WorldGenerationAuthoring`の地形設定、Baker、設定SubSceneを変更した。
- `DeterministicChunkGenerator`、`GeneratedTile`、Biome、Ground、水域の分類を変更した。
- チャンク・サブチャンク寸法、負座標変換、シード変換、GeneratorVersionを変更した。
- Map Viewerの色、凡例、座標変換、パン、ズーム、非同期処理を変更した。

#### 確認内容

1. 原点付近と既知の遠距離座標で、Map ViewerとGameビューのバイオーム・水域・マグマ・岩壁が一致する。
2. `実設定を再読込`後のSeed、WorldSeed、GeneratorVersion、設定取得元が想定どおり表示される。
3. 負座標への移動、ドラッグ、ホイール、＋・－Buttonで中心と縮尺が正しく変化する。
4. 同じSeed・座標・縮尺へ戻ると同じ画像になる。
5. 連続操作で古い非同期結果が後から上書きされず、Editorが長時間応答不能にならない。

## 3. 変更時の選び方

| 変更内容 | 最低限確認するEditorツール |
| --- | --- |
| Authoring、Scene、Tileアセット、Camera、Tilemapレイヤー | Build Fallback Map Prototype |
| 個別タイルの分類、色、境界、凡例 | Debug Visualization |
| バイオームや地形の広域分布、隣接関係、決定性 | Analyze Distribution |
| 広域形状、特定Seed・遠距離座標の探索 | Map Viewer |
| 生成規則と表示の両方 | Analyze Distribution、Map Viewer、Debug Visualization |

Editorツールだけで検証を完了しない。対応する自動テストは[テスト](./Testing.md)に従って実行し、見た目に関係する変更はGameビューでも確認する。

## 4. このドキュメントの更新条件

Editorメニューの追加、削除、名称変更、実行条件変更、出力項目変更、確認対象変更を行った場合は、同じcommitにこの文書の更新を含める。
