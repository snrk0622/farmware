# Farmware PixelLab GUI制作リファレンス

- 確認日: 2026-10-24
- 最終更新日: 2026-10-25
- 対象: PixelLab Web GUI、PixelLab内蔵Pixelorama、仕上げ編集に使うデスクトップ版Aseprite
- 対象外: PixelLab API、MCP、Aseprite拡張
- 目的: PixelLabの現行GUIで実際に選択できる項目だけを制作指示に使う
- 公式資料: [PixelLab Documentation](https://www.pixellab.ai/docs)、[Create tiles (Pro)](https://www.pixellab.ai/docs/tools/create-tiles-pro)、[Create S-XL image (Pro)](https://www.pixellab.ai/docs/tools/create-sl-image-pro)、[Pixelorama](https://www.pixellab.ai/docs/introduction-pixelorama)、[Aseprite Documentation](https://www.aseprite.org/docs/)

> **現行の材質タイル制作方針（2026-10-25）**
>
> 材質タイルはAsepriteまたはPixeloramaで手描きする。PixelLabは材質タイルの生成、Prompt作成、配色決定には使用しない。材質タイルの部位、寸法、パレット、境界の有無は[地形タイル表現仕様](./TerrainTileArtSpecification.md)を正とする。

## 1. この文書の使い方

本書は、動物、固定資源、施設など、材質タイル以外のSpriteでPixelLab GUIを利用するときの確認資料として残す。材質タイルの制作手順とPromptは本書に持たず、AsepriteまたはPixeloramaによる手描き仕様は[地形タイル表現仕様](./TerrainTileArtSpecification.md)へ集約する。Unity側のSprite分割、Atlas格納、色・Alpha合成、TilemapまたはMeshによる配置、Shaderによる表示処理は別工程として扱う。

PixelLabは更新頻度が高く、公式ドキュメントにもGUIより記載が遅れる場合があると明記されている。そのため、制作指示の根拠は次の順に扱う。

1. 制作日にWeb GUIで表示された項目。
2. 現行のPixelLab公式ドキュメント。
3. 本書の2026-10-24確認記録。

GUIに存在しない項目は、Promptで表現できる美術要件と、Unityで設定する表示要件に分ける。別ツールや旧GUIに存在した同名項目を、現行GUIの入力欄として扱わない。

## 2. 地形制作での位置付け

PixelLabの`Tiles`、`Paths`、`Tileset`、`Building`は、材質タイル制作には使用しない。材質タイルの`GroundTop`、任意の透明な`FaceBoundaryOverlay`、`Face`はAsepriteまたはPixeloramaで1 px単位に手描きする。Wall上面は専用Spriteを作らず、同じ材質の`GroundTop`を表示時だけ係数0.85で暗色化する。Ground／Wallの形状境界には既存14形状の共通境界ハイライトを使い、異なる材質のWall同士も接続扱いにする。断面は1段目から`Face`を使い、境界ありではEditorがGround用の元色Overlay版とWall用の暗色化Overlay版を事前合成する。41材質の境界有無は境界あり17材質・なし24材質で確定し、境界あり材質は同じOverlay入力をGround崖面とWall前面で共有する。細道や建築物など、材質タイル以外の用途でPixelLabを使うかは、該当機能の制作開始時に別途判断する。

## 3. Creator — Create image

### 3.1 2026-10-24にWeb GUIで確認した項目

`Creator` → `Create` → `Create image`には、次の項目が表示された。

- モデル選択: `Pro`、`Pixen`。
- `Pro`のMode: `Pro`、`Pro Flash (Beta)`。
- `Description`。
- `Remove background`。
- `Width`、`Height`。
- `Custom size (Beta)`。
- `Reference images`: 最大4枚。GUI表示では入力画像を最大1024×1024へ自動調整する。
- `Style image`: 1枚。GUI表示では最大256×256で、指定時は出力寸法がStyle imageを基準に設定される。
- `Color palette (Beta)`。
- `Max colours`。
- `Auto-add to`プロジェクト。

公式ドキュメントでは`Create S-XL image (Pro)`は最小16×16 px、非正方形を含む任意寸法に対応し、最大寸法はアスペクト比で変わる。GUIのカスタム寸法欄では16×40 pxの入力を受け付けることを確認した。ただし、生成結果とUnityでの実寸確認までは未実施である。

出力バリエーション数は最大辺で変わる。正方形の場合の公式値は次のとおりで、非正方形は総グリッド内容とアスペクト比でコストが変わる。

| 最大辺 | 公式ドキュメント上の出力 |
| ---: | --- |
| 16〜32 px | 8×8、64 variations |
| 33〜42 px | 8×8、64 variations |
| 43〜64 px | 4×4、16 variations |
| 65〜85 px | 4×4、16 variations |
| 86〜128 px | 2×2、4 variations |
| 129〜170 px | 2×2、4 variations |
| 171 px以上 | 1 variation |

### 3.2 材質タイルへの非適用

`Create image`の設定とPromptは材質タイルへ使用しない。材質ごとの配色、模様、差分、接続規則は[地形タイル表現仕様](./TerrainTileArtSpecification.md)に従って手描きする。

### 3.3 この画面に存在するものとして指示しない項目

2026-10-24の`Create image (Pro)`初期画面では、次の独立入力欄を確認できなかった。

- Negative prompt。
- Camera view、Direction。
- Shading、Details。
- Outline thicknessの数値。
- 透明度・Alphaの数値。
- PNG/JPEGなどのファイル形式選択。
- Seed入力欄。
- Normal Map生成または法線強度の設定欄。

禁止事項、視点、光源、輪郭、ディテール量は`Description`へ書く。透明背景は`Remove background`を使う。淡水・海水の`Liquid深度Overlay`の正確な半透明度と深度暗化はPixelLabの設定値として指示せず、UnityのPresentationで合成して決める。マグマは半透明の深度表現を使わず、不透明なBaseとして扱う。

公式ドキュメントにはSeedとStyle Optionsの記載がある。SeedはPixelLab共通設定ではAdvanced options時だけ表示される旨も記載されているが、今回のWeb Creator初期画面では確認できなかった。GUIに表示された場合だけ記録し、表示されていない制作手順で「Seedを入力する」と指示しない。

## 4. Create tiles (Pro)

Web GUIには`Tiles`、`Paths (Beta)`、`Tileset (Beta)`、`Building (Beta)`の4タブがある。タブごとに目的、Shape、選べる寸法が異なる。

### 4.1 Tiles

同じ材質の独立したタイル差分を生成する。

GUIで確認した項目:

- `Description`。
- `Style tiles`: 任意、最大6枚、各画像は最大128×128。
- `Tile shape`: `Hex flat-top`、`Hex pointy-top`、`Isometric`、`Square top-down`。`Oblique`はこのタブでは無効だった。
- `Square top-down`の`Tile size`: `16px`、`16×32`、`32px`、`32×64`、`48px`、`48×96`、`64px`、`64×128`、`96px`、`128px`。
- `View angle`: 5°のside寄りから90°のtop-downまで。
- `Thickness`: 0%のflat topから100%まで。
- `Outline` / `No outline`。
- `Color palette (Beta)`、`Max colours`。

材質タイルには使用しない。材質タイル以外で独立タイル候補が必要になった場合だけ、対象仕様を確認して利用する。

### 4.2 Paths (Beta)

道路または道を、直線、角、T字、十字、行き止まりを含む18構成の接続セットとして生成する。

GUIで確認した項目:

- `Description`。
- Shapeは`Isometric`と`Square top-down`。HexとObliqueは無効だった。
- 確認時の`Tile size`選択肢は`32px`だけだった。
- `View angle`、`Thickness`、`Outline`、`Color palette (Beta)`、`Max colours`。

これはGroundやWallの露出辺を作る機能ではない。1セル幅の道をゲームに必要とするかは後で判断し、必要と決まるまで生成しない。

### 4.3 Tileset (Beta)

2地形の接続遷移を生成する。正方形、Isometric、Obliqueでは16-tile corner set、Hexでは32-tile edge setになる。

GUIで確認した主な項目:

- 2地形を記述する`Description`。記述順があり、先頭を主地形として扱う。
- `Tile shape`: Hex 2種、Isometric、Oblique、Square top-down。
- `Tile size`: `16px`、`32px`、`48px`、`64px`、`96px`。
- `Uneven boundary`。
- `Terrain height`。
- `View angle`またはObliqueの`Wall tilt`。
- `Thickness`、`Outline`、`Color palette (Beta)`、`Max colours`。

32px・Square top-downで確認した`Terrain height`のGUI上限は4px、32px・Obliqueでは3pxだった。選択条件により上限が変わる可能性があるため、他寸法へ一般化しない。少なくとも、20〜32pxの独立したGround崖面やWall前面をこの値だけで作る前提にはしない。

また、現行のFarmware仕様は一般的な16-tile corner setを材質ごとに生成しない。GroundとWallは同じ材質の`GroundTop` Spriteを共有し、Wall表示だけRGBを初期値0.85で暗色化する。形状境界には全材質共通の既存14形状境界ハイライトを使い、8近傍47状態を基本形と90度回転から選ぶ。材質が異なるWall同士も接続扱いとし、共有辺へ境界を出さない。動的な投影影はURP 2D Rendererの`Light 2D`と`Shadow Caster 2D`で表現する。

### 4.4 Building (Beta)

床、接続壁、出入口、柱、階段または坂、屋根を含む建築キットを生成する。

GUIで確認した主な項目:

- `Wall material`、`Floor material`、任意の`Roof / upper floor`。
- Shape: `Isometric`、`Oblique`、`Square top-down`。Hexは無効だった。
- `Wall height`: 1〜3 tiles。
- `Roof profile`: `Classic V`、`Rounded`。
- `Shaped textures`。
- Square top-downでは`View angle`、`Wall angle`、`Floor thickness`。
- Obliqueでは`Wall tilt`、`Floor thickness`。
- `Outline`、`Color palette (Beta)`、`Max colours`。

Wallを1〜3タイル高くする指定は可能だが、地形Wall一枚ではなく建築キット全体を生成する機能である。採掘可能な地形Wallには使わない。

## 5. PixeloramaまたはAseprite

PixelLab公式は、PixeloramaをPixelLabのAI生成ツールを統合したブラウザ内ピクセルアートエディターとして案内している。デスクトップブラウザ専用である。

AsepriteはPixelLabとは別のデスクトップ用ピクセルアート編集ソフトである。材質タイルはAsepriteまたはPixeloramaで最初から手描きし、レイヤー編集、透明ピクセル確認、接続位置の固定、Sprite sheet整理を行う。AsepriteのTiled Modeは上下・左右の反復を見ながら継ぎ目を確認する用途に使える。PixelLab用Aseprite拡張は材質タイル制作の必須要件としない。

材質タイル制作ではPixeloramaまたはAsepriteを次に使う。

- 16×16 pxの`GroundTop`、任意の透明な`FaceBoundaryOverlay`、`Face`を1 px単位に手描きする。Wall上面は`GroundTop`を再利用するため、専用素材を作らない。
- 左右または上下へ並べたときの継ぎ目を確認し、同じ編集ツールで修正する。
- `GroundTop`、`Face`、`FaceBoundaryOverlay`の差分数は固定せず、材質定義の可変長配列へ必要な数だけ登録する。配列内の候補は均等確率で選ぶ。`GrassLoam`の独立した1 pxドットは共通外周を使わず、境界で密度や色が急変しないよう全組合せを検品する。大きなヒビや岩形状が境界を横断する材質は、すべての任意差分同士が接続できるよう、必要な接続座標と幅だけを合わせる。
- `FaceBoundaryOverlay`は左右、`Face`は上下左右へ並べ、任意の差分とOverlayを合成しても継ぎ目が破綻しないことを確認する。さらにWall前面から同材質のGround崖面へ`Face`を連続配置して検品する。
- `Top`、透明な`FaceBoundaryOverlay`、`Face`を別レイヤーまたは別ファイルとして管理し、Editor合成後も上面と断面が余分な輪郭線を挟まず直線境界でつながることを確認する。
- 透明背景と半透明ピクセルの有無を確認する。

PixeloramaとAsepriteの画像編集操作は、どちらもPixelLab Web Creatorの生成設定とは別である。材質タイルの制作記録には、使用した編集ソフトと手描き・修正内容を記録する。

## 6. 出力形式とUnityへ渡す前の確認

Web CreatorのGalleryには`Download image`があるが、2026-10-24に確認した画面には出力形式を選ぶ欄はなかった。このため「PixelLabでPNGを選択する」とは指示しない。

Farmwareへ採用するファイルは次を満たすものだけとする。

- ダウンロード実物の拡張子とColor Typeを確認し、UnityへはPNGとして保存する。
- 透明背景が必要なSpriteはAlpha channelを保持する。
- 可視ピクセルを拡大縮小しない。寸法変更が必要なら最初から正しい`Width`と`Height`で再生成するか、PixeloramaまたはAsepriteでキャンバスだけを調整する。
- Sprite sheetの場合は、1セルの幅、高さ、列数、行数を制作記録へ残す。
- Unity Importの`Filter Mode`、`Compression`、`Pixels Per Unit`はPixelLabの設定ではなくUnity側の設定として記録する。

## 7. 制作指示で禁止する架空設定

次の語は美術上の要望としてPromptに書けるが、現行GUIの独立設定項目としては指示しない。

- `Shading: Low`、`Details: Low`。
- `Direction: South`、`Camera view: Top-down`。
- `Thin dark outline`、`Outline 1px`のような未確認の選択肢。
- `Negative prompt`欄への貼り付け。
- `Alpha 40%`、`Opacity 60%`。
- `Tile thickness: 8px`のようなピクセル単位指定。
- `Output: PNG`というGUI選択。
- 表示されていない`Seed`の入力。

必要な見た目は`Description`へ書き、PixelLabが生成後に保証できない寸法、接続、透明度、PivotはPixelorama、AsepriteまたはUnityで検証する。

## 8. 材質タイルの最小試作手順

1. AsepriteまたはPixeloramaを選び、代表材質`GrassLoam`の16×16 px `GroundTop`を、比較に必要な数だけ手描きして可変長配列へ登録する。
2. 同じ編集ソフトで16×16 pxの透明な`FaceBoundaryOverlay`と`Face`を必要な数だけ手描きする。各配列内の候補は均等確率で選ぶ。専用`WallTop`、材質別外周、共通暗色内部は作らない。
3. 指定色、Alpha、接続、反復感を検品する。
4. Unityで同じ`GroundTop`をGroundとWallへ接続し、Wall表示だけを0.85で暗色化する。同じ`Face`と、必要に応じて元色または暗色化した`FaceBoundaryOverlay`をEditor合成した1段目用完成SpriteをGround崖面とWall前面へ仮接続する。既存14形状の共通境界ハイライトをGround／Wall領域の外周・内角へ重ね、Wall接続では材質IDを比較しない。
5. `Face`の表示段数、液体越しの見え方、GroundとWallの識別性をGameビューで比較する。
6. `GrassLoam`が成立してから、`DesertSand`、`Sandstone`、`Granite`、`Basalt`へ広げる。

この初期手順では、セル境界から出入りする不規則な凹凸輪郭、輪郭Mask、膨らみ用のセル外余白を作らない。凹凸を後から採用する場合も、論理地形から分離したUnityのPresentation表現として扱う。

## 9. 材質タイル用Promptの廃止

`GrassLoam`、`ClaySoil`を含む材質タイル用のPixelLab設定とPromptは廃止し、本書から削除した。手描き時の色、模様、差分、接続条件は[地形タイル表現仕様](./TerrainTileArtSpecification.md)を参照する。

材質タイル以外でPixelLabを使用する場合だけ、本書のGUI項目を制作指示へ適用する。
