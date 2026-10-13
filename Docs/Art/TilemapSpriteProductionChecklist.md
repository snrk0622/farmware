# Farmware タイルマップ用スプライト制作・導入チェックリスト v0.14

- 作成日: 2026-10-11
- 最終更新日: 2026-10-12
- 対象: ランダムマップ生成の地形・資源と、後続のドローン・開始コア
- 関連仕様: [ランダムマップ生成仕様](../GameSpecification/RandomMapGenerationSpecification.md)
- 関連仕様: [ゲームコンセプト仕様](../GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- 基準解像度: 1タイル = 16×16 px、Gameビュー = 480×270 px

## 1. この文書の目的

PixelLabだけで制作する製品用スプライトを漏れなく管理し、画風の固定、生成、Unityへのインポート、表示設定、実機確認を段階別に記録する。

Editorの構築メニューが `Assets/Farmware/Art/WorldGeneration/Fallback/` へ生成する、11種類の地形Tile、3種類の資源Tile、5種類の前面断面・岩壁側面Tile、淡水・海水・マグマの各4フレームAnimatedTileは表示を成立させるための仮素材である。本書のチェックは、PixelLabで制作した本番候補へ置き換えた時点から付ける。仮素材が存在するだけではチェックしない。

2026-10-12時点で、`ChunkTilemapPresenter`には前面断面、土地より8 px下へ投影する静止液面・液面アニメーション、基準面から12 px上へ積む岩壁上面・側面、岩壁上資源を16×16サブチャンク単位で表示・消去する仕組みを実装済みである。岩壁が水・マグマへ面する場所では、下層の土地断面と上層の岩壁側面を両方表示する。`Build Fallback Map Prototype`を実行すると必要レイヤーと仮素材を再生成できる。本番候補を用意する前にGameビューで高さ、重なり、速度を検証し、採用後は参照するTileだけを差し替える。

### 1.1 チェック欄の意味

各アセットは次の4段階を個別に確認する。

- `生成`: PixelLabからPNGまたはスプライトシートを書き出し、目視検品した。
- `取込`: PNGと対応する`.meta`をUnityプロジェクトへ追加し、Import Settingsを設定した。
- `設定`: Tile、AnimationClip、Prefabなど、ゲームから参照できるアセットへ接続した。
- `確認`: 対象Sceneで継ぎ目、可読性、拡大縮小時のにじみを確認した。

チェック例:

```text
- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認
```

### 1.2 次の作業: 画風を固定してから行うマップ全体の世界観検証

以前に生成した草原、森林地面、砂漠、淡水面、岩壁上面は、素材間で模様密度、コントラスト、ピクセル塊の大きさが揃っていなかったため、製品候補としては採用せず一から生成し直す。
本番スプライトへの差し替えは、すべての差分、境界、アニメーションを完成させてから一度に確認するのではなく、次の3段階で行う。
すべての段階で本番候補の可視ピクセルはPixelLabから生成し、採用画像、Seed、設定を制作記録へ残す。Style referenceに対応するツールでは、使用した参照画像も記録する。

#### 段階0: マスタースタイルの固定

最初に草原だけを生成し、16×16 pxで使用するピクセル塊、色数、コントラスト、模様密度の基準を決める。
草原を採用するまでは、森林、砂漠、水面、岩壁などの量産へ進まない。

- [ ] 草原を16差分生成し、12枚の基本差分が十分に静かであることを確認
- [ ] 4×4に並べた草原だけで、タイル境界と市松模様が見えないことを確認
- [ ] 木、設備、物流リンク、配置表示を重ねられる背景密度であることを確認
- [ ] 採用した草原画像、Seed、PixelLab設定をマスタースタイルとして制作記録へ登録
- [ ] 以降の全地形で、採用した草原画像をStyle referenceへ指定

草原以外の素材は、色や材質だけを変更し、ピクセル塊の大きさ、模様密度、陰影段階、輪郭方針を変更しない。
Style referenceを指定しても描画密度が揃わない場合は採用せず、プロンプトや設定を調整して再生成する。

#### 段階A: 全体配色とシルエットの確認

ランダムマップ全体を本番候補の色と形で埋め、バイオームの識別、画面の情報量、資源の可読性を先に判断する。
この段階では現在の`ChunkTilemapPresenter`が扱える各分類1枚のTileを使用する。断面と液面アニメーションはフォールバック素材で先に表示できるが、本番候補の完成条件には含めない。

- [ ] 草原、森林、砂漠、岩地、湿地、雪原、火山の代表地面を各1枚選定
- [ ] 淡水、海水、マグマ、岩壁上面の代表地形を各1枚選定
- [ ] 木、石、砂資源の代表スプライトを各1枚選定
- [ ] 代表地形11種類と資源3種類をUnityへ取り込み、既存の全分類へ割り当て
- [ ] 原点付近、海岸、川、岩壁、火山、雪原を含む複数地点でGameビューを撮影
- [ ] 480×270の実表示で、地形同士と資源の識別性、彩度、明度、模様密度を確認
- [ ] F4境界表示を一時的に重ね、16×16サブチャンクと64×64チャンクをまたぐ表示欠けがないことを確認

段階Aでは同じTileの繰り返しが見えることを既知の未完成項目として扱う。基本地形同士の直線的な切り替わりと、島や岸の直角を含む単純な輪郭は完成方針の一部として評価する。
ここで評価するのは、Foragerのように明るく即座に地形を識別できる配色、工業設備を置ける背景密度、バイオームの見分けやすさである。ただし既存作品のSprite、地形配置、固有意匠は複製しない。

#### 段階B: 製品表示としての接続と反復の確認

段階Aで採用した色と形を基準に、画面手前側だけの直線断面、液面アニメーション、装飾レイヤーを追加する。複数の地面差分をUnityへ登録して選択する方式は、土地上面と断面を別Spriteにするか16×24 pxで一体化するかの検証後に決める。

- [ ] 通常地形の画面下側が淡水・海水・マグマの場合だけ、横一直線の前面断面を表示
- [ ] 岩壁の画面下側が岩壁以外の場合だけ、横一直線の岩壁側面を表示
- [ ] 岩壁の画面下側が淡水・海水・マグマの場合、下層の土地断面と上層の岩壁側面を同時に表示
- [ ] 水面・マグマの岸の位置を固定したまま、内部の波紋と明暗だけをアニメーション
- [ ] 地面装飾を低密度で別レイヤーへ追加
- [ ] 連続する模様、境界の欠け、ちらつき、Tilemapの継ぎ目がないことを確認

本番候補は`Assets/Farmware/Art/WorldGeneration/Fallback/`へ上書きしない。
このフォルダは構築メニューが再生成する仮素材専用であるため、本番候補は9章の推奨配置先へ追加し、採用後にSceneまたは表示設定から参照する。

### 1.3 初期制作数と制作順

最初から全素材を同時に生成しない。地形差分では前段階の採用画像を次段階のStyle referenceへ使用し、前面断面の生成でStyle referenceを使えない場合は採用色と形状ルールをプロンプトへ引き継ぐ。

| 優先度 | 素材 | 内訳 | 初期必要数 |
|---|---|---|---:|
| 0 | 草原マスタースタイル | 基本12、軽い模様3、アクセント1 | 16 Sprite |
| 1 | 残りのBase地形 | 森林、砂漠、岩地、湿地、雪原、火山、淡水、海水、マグマ、岩壁上面の10種類×16差分 | 160 Sprite |
| 2 | 前面断面・岩壁側面 | 土、砂、岩、雪の低地断面4系統＋岩壁専用の横一直線側面1形状 | 5 Sprite |
| 3 | 基礎資源 | 木3、石3、砂資源3 | 9 Sprite |
| 4 | 地面装飾 | 草原8、森林8、砂漠8、水面6 | 30 Sprite |
| 5 | ドローン | 8方向×待機4フレーム、8方向×移動4フレーム | 64 Sprite frame |
| 6 | 開始コア | 待機4フレーム、稼働4フレーム | 8 Sprite frame |

初期必須合計は、静止Spriteとアニメーションフレームを合わせて292点とする。水面・マグマの任意アニメーション、反復確認後に追加する前面断面・岩壁側面差分、後続段階の設備や固有資源はこの合計へ含めない。
最初の世界観確認に必要な最小セットは、11地形の代表各1枚と木・石・砂資源の代表各1枚の合計14点である。

Base地形は合計11種類×16差分の176 Spriteとなる。段階Aの世界観確認では各地形の代表1枚だけを先にUnityへ接続し、全差分の実装を待たずに配色を確認する。
低地境界の前面断面4 Spriteと岩壁側面1 Spriteは全Base地形の採用後に制作する。外角、内角、左右の側面、孤立形状は制作しない。追加差分は初期必須数へ含めず、長い境界で明確な反復が確認された系統だけ追加する。

次の表現には、初期段階で専用Spriteを制作しない。

- 基本地形同士の境界: Base Tileを直接隣接させる。
- 淡水、海水、マグマ同士の境界: 低地同士としてBase Tileを直接隣接させる。
- 水際の接触影: 初期段階では専用効果を作らず、前面断面の下端色だけで高さを示す。
- マグマの焦げと照り返し: 初期段階では作らず、必要性をGameビューで確認してから追加する。
- 岩壁内部影: 既存仕様の連続アルファMeshを使用し、手前側の岩壁側面とは別に扱う。

## 2. 共通アート方針

### 2.1 視点と形状

- Foragerのように、正方形グリッド上で島や地形の輪郭を直角かつ単純に読める斜め見下ろし表現を使用する。アイソメトリックの菱形グリッドにはしない。
- 地面は16×16 pxの正方形へ継ぎ目なく接続する平面として描き、木、岩、設備、ドローンなどの立体物では上面と正面側が読める角度を使用する。
- 草原、砂漠などの通常地形上面は論理セルへ16×16で一致させる。淡水、海水、マグマも論理上は同じセルを使用するが、静止Base Tileと透明アニメーションを専用Tilemapで画面下へ8 px移動し、低い液面として投影する。土地断面は上面へ重ならずセル直下へ8 px伸び、移動した液面の上辺と接する。液面の下辺が手前の土地へ重なる場合は、土地上面を前に描いて隠す。
- 岩壁だけを基準面より1段高い地形とし、16×16の岩壁上面を専用Tilemapで12 px上へ移動する。上面16 px：側面12 pxを1：0.75の基準比率とする。岩壁側面は論理セル内の下端から上方向へ12 px描き、土地断面のようにセル下へはみ出させない。
- 通常地面には側面、外周線、タイル境界線、投影影を描かない。
- 岩壁だけは、画面下側の隣接セルが岩壁以外の場合に、横一直線の専用側面を表示する。連続する岩壁内部は別Tileで隠さず、同じ岩壁Tileの上へPresentationが連続アルファの黒い影Meshを重ねる。
- 木、岩、設備などは1×1タイルを論理的に占有しても、接地点をセル内へ置いたまま、上部を画面上方向のセルへ視覚的にはみ出してよい。ゲーム上の占有範囲とは分離する。
- 1×1物体の標準キャンバスは16×24 pxまたは16×32 pxを候補とする。横幅も必要な木や主要設備は24×32 pxまたは32×32 pxを使用できるが、論理占有は別途指定する。
- 立体物の基準点は、幹、脚、土台などが地面へ接する位置の中央とする。前後関係はこの接地点のワールドY座標で決める。
- 上方向へのはみ出し量、左右へのはみ出し量、接地点を制作記録へ残す。サブチャンク境界や画面端で欠けないことを検品する。

### 2.2 色、明度、情報量

- 明るく親しみやすい自然色を基本にする。Foragerの地表のような高い彩度差、広い色面、遠目での識別しやすさを配色の参考にするが、既存Sprite、輪郭、模様、地形配置は複製しない。
- 単純な輪郭を採用してもチープな単色面にはしない。各Base地形は従来どおり16差分を用意し、主色を揃えたまま低コントラストの色むら、材質に合う小さな模様、低頻度アクセントで奥行きを保つ。
- 配色の基準関係は、草原を明るい自然な緑、海を草原と明確に分離する鮮やかな青、前面断面を両者より暗い暖色の土色とする。個別のRGB値は採用画像から決め、既存作品のパレットをそのまま抽出しない。
- 1枚のBase Tileでは主色に加えて2〜3段階の近似色を使い、平坦さを抑える。黒に近い輪郭は地形外周や前面断面の下端などシルエットを読む箇所へ限定し、地面模様の一つ一つには付けない。
- 草原は自然光の下の若草色を基準とし、黄緑へ寄せすぎない緑主体の中高明度・中彩度にする。蛍光ライム、蛍光黄色、白に近い広い色面、全画面へ灰色や茶色を重ねたようなくすんだ色調、暗いオリーブ色、陰鬱な照明は避ける。
- 明るさは白飛び、蛍光色、高コントラストを意味しない。地面の広い色面を明るく保ち、模様のコントラストと密度を抑えることで、工業設備、資源、物流リンクを読みやすくする。
- バイオームは色だけに依存せず、草、落ち葉、砂粒などの模様でも区別する。
- 同じ地形の差分は平均明度と主色を近づけ、ランダム配置時に市松模様を作らない。
- 目立つ花、ひび、大石などは通常地面へ大量に焼き込まず、低密度の装飾オーバーレイへ分離する。
- 地面には方向性の強い影を置かない。自然物と設備の光源は左上に統一する。
- 16×16 pxでは1 px単位の形を優先し、アンチエイリアス、半透明の縁、縮小前提の細部を使用しない。
- 通常地面では、個々の石、土塊、草へ黒い輪郭を付けない。暗い線で素材を細かく区切る表現は、石畳やタイル床に見えるため使用しない。
- 地形ごとに色相と平均明度は変えてよいが、1タイル内の明暗差、陰影段階、模様の大きさはマスタースタイルへ合わせる。
- 単独の1 pxを無数に散らさず、1〜3 px程度の小さなまとまりを疎らに配置する。意味のない高周波ノイズで材質を表現しない。
- 森林地面、岩地、火山などは草原より暗くしてよいが、画面全体を重くするほど黒くしない。岩壁上面も細かな石の集合ではなく、連続した一枚岩として表現する。

### 2.3 バリエーションの配置比率

地面差分を実装するときの初期比率は以下を目安にする。最終比率はマップ上で確認して調整する。

| 種類 | 目安 | 用途 |
|---|---:|---|
| 基本差分 | 12枚 / 16枚（75%） | 低コントラストの自然な色むらを持ち、設備の背景になる地面 |
| 軽い模様 | 3枚 / 16枚（18.75%） | 小さな色むら、草、葉、砂粒 |
| 目立つ模様 | 1枚 / 16枚（6.25%） | 小花、小石、ひびなどの低頻度アクセント |

差分の選択は描画側の非決定的な乱数ではなく、ワールドシードとタイル座標から得た安定したハッシュで行う。同じセーブを読み込んだときに模様が変わらないようにする。

## 3. PixelLab共通設定

PixelLabのUI更新により項目名や段階数が変わった場合は、次表の意味に最も近い値を選び、実際に使った値とSeedを「制作記録」へ追記する。

4章以降の各`Prompt`コードブロックは、共通文、素材別の指示、重要な禁止事項をすべて展開したコピー用全文である。識別子をPixelLabへ入力したり、複数のコードブロックを手作業で連結したりする必要はない。ネガティブプロンプト欄がある場合だけ、3.2の共通ネガティブプロンプトも追加でそのまま入力する。

### 3.1 地形Promptへ展開する草原共通方針

地形Promptへ展開する共通方針の参照用である。このコードブロック単体は入力せず、4.1の全文Promptを使用する。

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Each ground tile is exactly 16 by 16 pixels and forms a flat seamless surface. Use bright, clearly separated biome colors and broad calm color fields with the immediate readability of a cheerful island-building game, while creating original terrain textures and motifs. Keep the ground comfortably less bright than pale UI highlights, placement overlays, and machine indicator lights. Nothing on the ground should look luminous, fluorescent, or self-illuminated. Use two or three restrained value steps, crisp hand-placed pixel clusters, and sparse material-specific texture. Across sixteen variations, preserve a consistent main color while varying subtle grass, soil, sand, water, snow, or rock clusters. The ground must remain subordinate to small machines, logistics links, resource icons, and placement overlays. No outline around ordinary ground details and no visible tile boundary.
```

### 3.2 共通ネガティブプロンプト

ネガティブプロンプト欄があるツールでは、その欄へ次のコードブロックだけをそのまま入力する。欄がない場合は、4章以降の各Promptに必要な禁止事項を展開済みなので、手作業で連結しなくてよい。

```text
isometric diamond grid, side-scroller view, strong perspective, voxel block, cube terrain, 3D block, visible sides on ordinary flat ground, thick bevel, tile border, grid line, black seam, outlined pebble, outlined soil clump, cobblestone, brick pattern, gravel, long directional cast shadow, gloomy lighting, overall dull gray cast, muddy desaturation, fluorescent green, neon lime, acid green, chartreuse-dominant palette, highlighter yellow, oversaturated yellow-green, glowing ground, self-illumination, large near-white areas, clipped highlights, gradient, blur, anti-aliasing, smooth vector art, photorealism, text, UI, building, machine, character, large centered landmark, high visual noise, dense speckle, random single-pixel noise
```

### 3.3 地形Promptへ展開するスタイル一致方針

草原以外の地形Promptへ展開する共通方針の参照用である。このコードブロック単体は入力せず、4.2以降の各素材に記載した全文Promptを使用する。生成時は採用済み草原画像をStyle referenceへ指定する。

```text
Match the approved grassland reference exactly in average brightness, moderate saturation, pixel-cluster size, texture density, contrast range, shading complexity, outline policy, and cheerful natural-daylight color clarity. Change only the material and its biome palette. Preserve clear natural colors without making them fluorescent, luminous, dull gray, muddy brown, or nearly black. Use the same visual scale and the same amount of quiet surface. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the reference. Across sixteen variations, make twelve quiet common tiles with two to four subtle low-contrast 1-to-3-pixel color clusters each, totaling approximately four to eight changed pixels per common tile; three lightly textured tiles; and only one noticeably accented tile. No common tile may be completely flat or solid-colored.
```

### 3.4 地形バリエーション共通設定

- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- Shape: `Square top-down`
- Tile size: `16×16`
- View angle: `Top-down`。地面自体には立体物用の正面を描かない
- Tile thickness: `0`または最小
- Outline mode: `No outline`
- Shading: `Low`
- Details: `Low`。マスタースタイルより情報量が減りすぎる場合だけ`Medium`を比較する
- Seed: 生成ごとに記録する
- Style reference: 最初に採用した草原タイルを基準画像とし、それ以降の地形へ使用する
- 出力数: 各地形16差分。基本12、軽い模様3、目立つ模様1として選別する
- 出力配置: 4×4。各Spriteは同確率で選び、基本12・軽い模様3・アクセント1という枚数構成によって種類別の比率を維持する
- 基本差分の密度: 各Tileへ1〜3 pxの低コントラストな色むらを2〜4か所、合計4〜8 px程度入れる
- 禁止: 完全な単色Tile、孤立した暗い1 pxの大量散布、平均明度が他の差分から外れる大きな模様、全体を灰色や茶色へ寄せるくすんだ色調

`Create tiles (Pro)`のTiles機能は独立した地面差分生成に使う。地形同士を接続するTilesetやRuleTileは制作しない。

### 3.5 低地境界用の直線前面断面 共通設定

- PixelLabツール: 透明背景を扱える`Map Objects`または`Create Object`
- PixelLab生成物: 横幅16 px、可視高8 pxの水平な縁と断面。角、縦辺、内角、外角、地面や水面の塗りつぶしを含めない
- Style reference: 採用済みBase地形を指定できる場合は使用し、主色、影色、ピクセル塊、輪郭の太さを合わせる
- Final canvas: `16×24`。上側16×16を通常地形セルの領域として完全透明に保ち、下側8 pxだけを低地側へはみ出す断面領域として使用する
- 配置: 通常地形上面へ1 pxも重ねず、上面領域の直下から可視部分を8 px出す。左右端は必ず0 pxと15 pxで同じ高さに揃える
- 断面高: 上端1 pxと側面7 pxを合わせた`8 px`。土、砂、岩、雪の全系統で統一する
- 輪郭: 上端に1 pxの明暗境界、下端に1 pxの暗色を許可する。真黒な太線、ベベル、立体ブロック状の厚い影は避ける
- 形状: 完全な横一直線。凹凸、丸い入り江、斜線、縦断面、角形状は作らない
- 変換: 生成後の機械処理は透明余白の追加、切り抜き、配置だけとし、可視ピクセルを描き足さない
- Seed: 生成ごとに記録する

断面は通常地形セルと同じ座標の専用`Terrain Front Edge Tilemap`へ重ねる。通常地形の画面下、すなわちワールドY-1の隣接セルが淡水、海水、マグマのいずれかである場合だけ表示する。上下左右の接続Tileset、4bitマスク、RuleTile、回転・反転による形状生成は使用しない。

### 3.6 静止オブジェクト共通設定

- PixelLabツール: `Map Objects`
- Camera view: `Top-down`の斜め見下ろし。上面と正面側が読める角度
- Remove background / Transparent background: `On`
- Outline: `Thin dark outline`。16×16で潰れる場合は`No outline`
- Shading: `Low`から`Medium`
- Details: `Low`から`Medium`
- Direction: `South`固定。回転が必要な物だけ4方向または8方向を別途作る
- Canvas: 1×1占有物は16×24 pxまたは16×32 pxを基本とし、接地点より上側へ高さを確保する
- Anchor / Pivot想定位置: 接地点の中央。画像の`Bottom Center`そのものではなく、接地点より下に影や前面がある場合はその分を空ける
- Style reference: 採用済みの地形と最初の自然物を使用する
- Seed: 生成ごとに記録する

### 3.7 動くオブジェクト共通設定

- 基準絵: `Create object`または`Create character`
- アニメーション: `Animate with text`
- Transparent background: `On`
- Camera view: `Top-down`の斜め見下ろし。上面と正面側が読める角度
- フレーム間で輪郭、サイズ、中心位置、光源方向が変化しないことを優先する
- Unityでは各フレームをPointフィルターで表示し、補間による中間画像を作らない

### 3.8 水面・マグマの地形アニメーション共通設定

水面やマグマは波紋、流れ、明暗の帯を上下へ1 px程度動かし、液面の高さが変わって見える表現を使用してよい。ただし、論理セルと岸の境界は全フレームで固定する。16差分のBase Tileは静止画のまま`Liquid Base Tilemap`へ置き、土地より画面下へ8 px移動する。その上の`Liquid Surface Animation Tilemap`も同じ8 px下へ移動し、透明背景の波紋・明暗だけを重ねる。

- 個々のTileや論理セルは移動させず、静止液面とアニメーションのTilemap Transformを一律`-8/16セル`へ設定する。
- 各アニメーションフレームの外周1 pxは完全透明とし、隣接Tileとの継ぎ目や岸の輪郭を動かさない。
- 岸、前面断面、岩壁側面へ波や発光ピクセルをはみ出させない。
- 動かすのはTile内部の短い波紋、反射、低コントラストの明暗帯に限定する。
- 4フレームの低速Loopを初期値とし、最近傍表示で1 px単位に切り替える。フレーム補間、Spriteの拡大縮小、サブピクセル移動は使用しない。
- アニメーションはPresentation専用とし、`GeneratedTile`、当たり判定、建築可否、採取、経路、セーブデータを変更しない。
- 大量のタイルごとにGameObjectやSpriteRendererを生成せず、導入済みのTilemap Extrasに含まれる`AnimatedTile`または同等のTilemap一括表示で実装する。透明オーバーレイの種類は座標ハッシュで決定論的に選び、採用方式はProfilerで確認する。

実際に岸線を1 px上下させるアニメーションは、前面断面との重なり、隣接セルとの継ぎ目、マスク処理が別途必要になるため、初期方針には含めない。

### 3.9 生成直後の受け入れ確認

Unityへ取り込む前に、PixelLabの出力ごとに次を確認する。

- [ ] 16枚を4×4で並べ、基本12枚が同じ平均明度と模様密度に見える
- [ ] 基本12枚の各Tileに低コントラストな色むらがあり、完全な単色Tileがない
- [ ] 草原が明るい黄緑を保ち、灰緑、暗いオリーブ、茶色のフィルターが全体へ掛かって見えない
- [ ] 草原以外も昼間の明快な色調を保ち、Style referenceより不必要に暗く、灰色に、またはくすんでいない
- [ ] 草原のマスタースタイルと並べ、ピクセル塊の大きさとコントラストが一致する
- [ ] 16×16の各辺を接続しても、線、色面、模様が不自然に途切れない
- [ ] 黒い区切り線がタイル画像自体へ描かれていない
- [ ] 67×67 pxなど1 pxガター付きの4×4出力では、各タイルが16×16、間隔が1 pxであることを記録する
- [ ] 4×4を最近傍表示で拡大し、アンチエイリアス、半透明の縁、意図しない中間色がない
- [ ] 不採用画像もSeedと却下理由を制作記録へ残し、同じ失敗条件を繰り返さない

## 4. 地形ベーススプライト

### 4.1 草原 `terrain_grassland`

- 必要数: 基本12、軽い草3、小花または小さな土斑点1の計16差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.4の共通設定。最初はStyle referenceを使わず、この結果からマスタースタイルを決める
- Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless meadow ground variations, each exactly 16 by 16 pixels, for a cheerful automation world in clear natural daylight. Every tile must form the same flat seamless surface with no visible tile boundary, side face, thickness, outline, or cast shadow. Use bright, clearly separated biome colors, broad calm color fields, two or three restrained value steps, crisp hand-placed pixel clusters, and sparse material-specific texture. Keep the ground comfortably less bright than pale UI highlights, placement overlays, machine indicator lights, resource icons, and logistics links. Nothing on the ground should look luminous, fluorescent, or self-illuminated.

Use a fresh spring-meadow green base that is only slightly yellow-leaning and remains unmistakably green. Keep the base at a medium-light value with moderate saturation: brighter than forest ground, but clearly darker and less saturated than pale UI highlights or placement overlays. Reserve the lightest green for a few tiny highlight pixels rather than broad areas. Make twelve calm common tiles with two to four subtle clusters of 1-to-3-pixel low-contrast color variation, totaling approximately four to eight changed pixels per common tile; three lightly textured tiles with the same ground variation plus a few sparse short-grass clusters; and one accent tile with the same ground variation plus either one tiny warm-cream flower cluster or one very small warm-soil patch. No tile may be completely flat or solid-colored, but most of every tile must remain a broad clean color area. Keep nearly identical average brightness and color balance across all sixteen tiles. Do not outline individual grass blades, flowers, soil marks, or pebbles. Avoid isometric diamond grid, side-scroller view, strong perspective, voxel block, cube terrain, 3D block, visible sides, thick bevel, tile border, grid line, black seam, cobblestone, brick pattern, gravel, long directional shadow, gloomy lighting, gray or brown color cast, fluorescent green, neon lime, acid green, chartreuse-dominant color, yellow-dominant grass, highlighter yellow, near-white ground, clipped highlights, gradient, blur, anti-aliasing, dense speckles, random isolated dark pixels, and recognizable repeated landmarks.
```

- 再生成理由（2026-10-12）: 最初の候補はGameビューで蛍光ライムに近く、他地形や将来の配置表示より強く見えたため不採用。Style referenceには使用しない。

- [ ] 生成
- [ ] マスタースタイルとして採用
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.2 森林地面 `terrain_forest_floor`

- 必要数: 基本12、落ち葉または苔3、小さな根またはシダ1の計16差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.4の共通設定。採用した草原をStyle referenceへ指定する
- Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless moist forest-floor variations, each exactly 16 by 16 pixels. Every tile must form the same flat seamless surface with no visible tile boundary, side face, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use a clear medium leaf green with warm readable brown soil accents. Keep the average value only moderately darker than the grassland reference and never make the surface muddy, dull gray, nearly black, or gloomy. Make twelve quiet common forest-ground tiles with two to four subtle low-contrast clusters of 1-to-3-pixel variation, totaling approximately four to eight changed pixels per common tile; three lightly textured tiles with the same ground variation plus a few leaf-green or moss-green clusters; and one accent tile with one very small shallow root or fern sprout. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Avoid isometric diamond grid, visible sides, tile border, grid line, black seam, outlined leaf, outlined pebble, dense speckle, tree, trunk, large plant, deep hole, cobblestone, brick pattern, gravel, strong directional shadow, fluorescent color, glowing ground, muddy brown cast, gray filter, gradient, blur, anti-aliasing, and high visual noise.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.3 砂漠 `terrain_desert_sand`

- 必要数: 基本12、濃淡の砂粒3、小石または短い砂紋1の計16差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.4の共通設定。採用した草原をStyle referenceへ指定する
- Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless compact inland-desert ground variations, each exactly 16 by 16 pixels. Every tile must form the same flat seamless surface with no visible tile boundary, side face, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use bright warm ochre, clear golden sand, and a small amount of pale sunlit yellow. Make twelve quiet common sand tiles with two to four subtle low-contrast clusters of 1-to-3-pixel variation, totaling approximately four to eight changed pixels per common tile; three lightly textured tiles with the same ground variation plus a few small darker or lighter sand clusters; and one accent tile with either one tiny pebble cluster or one short shallow wind ripple. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Do not outline individual grains or pebbles. Avoid isometric diamond grid, visible sides, tile border, grid line, black seam, dull beige, gray-brown dust, muddy desaturation, tropical beach cyan, long continuous lines, large dunes, footprints, bones, shells, dense speckles, high-contrast patterns, cobblestone, brick pattern, strong directional shadow, fluorescent color, glowing ground, gradient, blur, and anti-aliasing.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.4 淡水面 `terrain_fresh_water`

- 必要数: 静止画16差分。基本水面12、弱い波紋3、淡い反射1。アニメーションを採用する場合は、採用した低ノイズ2種類×4フレームを初期値とする
- PixelLabツール: 静止画は`Create tiles (Pro)` → `Tiles (variations)`、アニメーションは採用静止画を`Animate with text`へ渡す
- 静止画設定: 3.4の共通設定。採用した草原をStyle referenceへ指定し、Tile thicknessは0、Outlineなし
- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art water surface for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless calm freshwater surface variations viewed directly from above, each exactly 16 by 16 pixels. Every tile must form the same flat seamless surface with no visible tile boundary, shore, side face, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use clear medium blue-green with a small amount of fresh cyan-blue and pale sunlit reflection. Keep the surface bright and clean without becoming neon, mirror-like, or self-illuminated. Make twelve quiet common water tiles with two to four subtle low-contrast blue or blue-green clusters totaling approximately four to eight changed pixels per tile; three tiles with the same variation plus one or two small irregular ripple clusters; and one accent tile with a tiny broken pale reflection. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Ripples must be short broken pixel clusters rather than symbols or repeated lines. Avoid isometric diamond grid, visible sides, tile border, grid line, black seam, gray or muddy water, outlined ripples, parallel wave stripes, ocean swell, foam, shoreline, fish, boats, floating objects, large reflections, strong directional highlights, fluorescent cyan, glowing water, gradient, blur, anti-aliasing, and dense speckles.
```

- Animation Prompt（全文をそのまま入力）:

```text
Create a transparent four-frame pixel-art overlay that animates only tiny water ripples and one-pixel light-dark clusters, creating a gentle illusion that the surface rises and falls without moving the shoreline. Keep the outermost one-pixel border completely transparent in every frame. Preserve the reference water palette, pixel density, scale, and camera. Do not include the filled water base, translate the whole tile, or change the tile silhouette. No camera movement, new object, foam, or large wave.
```

- Animation settings: `4 frames`、Loop、動きの強さ`Low`
- [ ] 静止差分を生成
- [ ] アニメーション採用可否を決定
- [ ] 全フレームで外周1 pxが完全透明で、岸の位置が変化しない
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.5 海水面 `terrain_salt_water`

- 必要数: 静止画16差分。基本水面12、弱い波3、淡い反射1。アニメーションを採用する場合は、採用した低ノイズ2種類×4フレームを初期値とする
- PixelLabツール: 静止画は`Create tiles (Pro)` → `Tiles (variations)`、アニメーションは採用静止画を`Animate with text`へ渡す
- 静止画設定: 3.4の共通設定。採用した草原と淡水面をStyle referenceへ指定し、Tile thicknessは0、Outlineなし
- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art water surface for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless calm saltwater surface variations viewed directly from above, each exactly 16 by 16 pixels. Every tile must form the same flat seamless surface with no visible tile boundary, shore, side face, thickness, outline, or cast shadow. Match the approved grassland and freshwater references exactly in pixel-cluster size, texture density, contrast range, shading complexity, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and ocean palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the references.

Use a clear medium ocean blue that is slightly deeper than the freshwater reference while remaining bright and colorful in daylight. Make twelve quiet common ocean tiles with two to four subtle low-contrast blue clusters totaling approximately four to eight changed pixels per tile; three tiles with the same variation plus one or two small broken wave clusters; and one accent tile with a tiny pale reflection. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Keep the surface readable as ocean water without drawing foam or a shoreline. Avoid isometric diamond grid, visible sides, tile border, grid line, black seam, gray navy, murky water, tropical neon cyan, outlined waves, large ocean swell, parallel stripes, foam, shoreline, fish, boats, floating objects, large reflections, strong directional highlights, glowing water, gradient, blur, anti-aliasing, and dense speckles.
```

- Animation Prompt（全文をそのまま入力）:

```text
Create a transparent four-frame pixel-art overlay that animates only small saltwater wave clusters and one-pixel light-dark bands, creating a gentle illusion that the surface rises and falls without moving the shoreline. Keep the outermost one-pixel border completely transparent in every frame. Preserve the reference ocean palette, pixel density, scale, and camera. Do not include the filled water base, translate the whole tile, or change the tile silhouette. No camera movement, shore foam, large wave, or new object.
```

- Animation settings: `4 frames`、Loop、動きの強さ`Low`
- [ ] 静止差分を生成
- [ ] アニメーション採用可否を決定
- [ ] 全フレームで外周1 pxが完全透明で、岸の位置が変化しない
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.6 岩地・湿地・雪原・火山・マグマ

生成基盤では7バイオームとマグマまで実装済みのため、次の本番候補も草原・森林・砂漠と同じ16×16 pxの交換可能な地面Tileとして制作する。

| 用途 | 推奨ファイル接頭辞 | 主な見分け方 |
| --- | --- | --- |
| 岩地 | `terrain_rockland` | 岩壁より低く平坦な灰褐色地面。建築可能地と読める密度にする。 |
| 湿地 | `terrain_wetland` | 草原より少し深い緑の湿潤土と、明るい苔。灰色や黒へ寄せず淡水面と区別する。 |
| 雪原 | `terrain_snowfield` | 青みのある雪面。完全な白を避け、HUDや配置表示の白を残す。 |
| 火山 | `terrain_volcanic` | 深い赤褐色の火山岩。黒く潰さず、マグマの橙色より低彩度にする。 |
| マグマ | `terrain_magma` | 高彩度の橙と黄。大面積でもちらつかない模様密度にする。 |

各地面は基本12、軽い模様3、目立つ模様1の16差分とし、採用した草原を必ずStyle referenceへ指定する。色と材質だけを変更し、明るく明快な昼間の色調、広い静かな色面、4〜8 pxの基本色むらを維持する。マスタースタイルより細かな模様、強い輪郭、灰色のフィルターを追加しない。マグマをアニメーションさせる場合も、3.8に従って外周と岸を固定し、内部の流れと明暗だけを動かす。

#### 4.6.1 岩地 `terrain_rockland`

- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless flat rockland ground variations, each exactly 16 by 16 pixels. Every tile must form the same buildable flat surface with no visible tile boundary, cliff side, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use readable medium warm gray, cool gray, and restrained gray-brown tones. Keep this ground clearly lighter, flatter, and less massive than the unbuildable rock-wall top. Make twelve quiet common tiles with two to four subtle broad 1-to-3-pixel color clusters totaling approximately four to eight changed pixels per tile; three lightly textured tiles with one or two shallow stone-color patches or very short hairline cracks; and one accent tile with one tiny mineral-colored speck or compact shallow crack. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. The surface must read as compact natural rock ground, not separate stones. Avoid isometric diamond grid, visible cliff side, tile border, grid line, black seam, cobblestone, brick pattern, gravel, dark grout, outlined stones, dense cracks, loose boulder, cave, crater, black surface, dull gray filter, high-contrast veins, long shadow, gradient, blur, anti-aliasing, and dense speckles.
```

#### 4.6.2 湿地 `terrain_wetland`

- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless moist wetland-ground variations, each exactly 16 by 16 pixels. Every tile must form the same buildable flat surface with no visible tile boundary, water hole, side face, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use a clear medium deep green for moist soil with restrained blue-green and bright moss-green accents. Keep the average value only moderately darker than grassland, while remaining colorful and readable in daylight. Make twelve quiet common tiles with two to four subtle low-contrast 1-to-3-pixel moist-soil or moss color clusters totaling approximately four to eight changed pixels per tile; three lightly textured tiles with a few small moss or damp-ground clusters; and one accent tile with one tiny reed sprout or shallow dark-green damp patch. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. The surface must remain ground rather than open water. Avoid isometric diamond grid, visible sides, tile border, grid line, black seam, muddy black soil, gray-green filter, large puddle, reflective water, foam, bubbles, outlined mud clumps, dense moss carpet, large reeds, lily pad, tree root network, strong shadow, fluorescent green, gradient, blur, anti-aliasing, and dense speckles.
```

#### 4.6.3 雪原 `terrain_snowfield`

- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless flat snowfield variations, each exactly 16 by 16 pixels. Every tile must form the same buildable flat surface with no visible tile boundary, snow bank side, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use pale blue-white snow with restrained light blue-gray shadows, keeping pure white highlights extremely limited so white UI, placement overlays, and machine indicators remain readable. Make twelve quiet common tiles with two to four subtle low-contrast 1-to-3-pixel blue-white variation clusters totaling approximately four to eight changed pixels per tile; three lightly textured tiles with a few short wind-softened snow marks; and one accent tile with one tiny exposed blue-gray ice or muted soil speck. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Avoid isometric diamond grid, visible snow-bank side, tile border, grid line, black seam, completely pure-white tile, clipped highlights, reflective ice sheet, crystalline cave, icicle, deep footprint, long wind stripe, snowman, rock pile, strong blue outline, dark shadow, gradient, blur, anti-aliasing, and dense speckles.
```

#### 4.6.4 火山地面 `terrain_volcanic`

- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art ground for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless flat volcanic-ground variations, each exactly 16 by 16 pixels. Every tile must form the same buildable flat surface with no visible tile boundary, cliff side, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and biome palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use deep but readable red-brown, warm charcoal-brown, and restrained muted rust tones. Keep the ground clearly less saturated and less bright than magma, and do not let the large surface collapse into black. Make twelve quiet common tiles with two to four subtle broad 1-to-3-pixel color clusters totaling approximately four to eight changed pixels per tile; three lightly textured tiles with one or two very short cooled-rock seams or muted rust patches; and one accent tile with one tiny dim ember-colored mineral speck or shallow crack. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Avoid isometric diamond grid, visible cliff side, tile border, grid line, black seam, glowing lava line, magma pool, bright yellow crack, ash cloud, smoke, crater, separate outlined stones, cobblestone, brick pattern, gravel, nearly black surface, dense cracks, strong shadow, gradient, blur, anti-aliasing, and dense speckles.
```

#### 4.6.5 マグマ `terrain_magma`

- Static Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art molten magma surface for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless magma-surface variations viewed directly from above, each exactly 16 by 16 pixels. Every tile must form the same flat lowland liquid surface with no visible tile boundary, shore, side face, thickness, outline, or cast shadow. Match the approved grassland and volcanic-ground references in pixel-cluster size, texture density, contrast range, shading complexity, broad quiet areas, and clear natural-daylight readability. Change only the material and magma palette. Use three controlled value steps and crisp hand-placed pixel clusters. Do not introduce finer details, heavier outlines, or more isolated single-pixel noise than the references.

Use a strong orange base, warm red-orange darker clusters, and a very limited amount of pale yellow highlights. Keep broad calm color fields so a large magma area does not flicker or overpower machines and UI. Make twelve quiet common magma tiles with two to four subtle internal 1-to-3-pixel flow-color clusters totaling approximately four to eight changed pixels per tile; three lightly textured tiles with one or two short broken orange-yellow flow bands; and one accent tile with one tiny brighter molten knot. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. Avoid isometric diamond grid, visible sides, tile border, grid line, black seam, shoreline, crust edge, white-hot broad area, neon yellow, flashing highlight, long continuous lava stripe, large eruption, splash, smoke, flying ember, rock island, outlined crack network, strong bloom, gradient, blur, anti-aliasing, and dense speckles.
```

- Animation Prompt（全文をそのまま入力）:

```text
Create a transparent four-frame pixel-art overlay that animates only small internal magma flow clusters and one-pixel orange-yellow light bands, creating a gentle illusion that the molten surface rises and falls without moving the shoreline. Keep the outermost one-pixel border completely transparent in every frame. Preserve the reference magma palette balance, pixel density, scale, and camera. Do not include the filled magma base, translate the whole tile, or change the tile silhouette. No eruption, splash, smoke, flying ember, large white flash, shore glow, or new object.
```

- [ ] 岩地を生成・取込・設定・確認
- [ ] 湿地を生成・取込・設定・確認
- [ ] 雪原を生成・取込・設定・確認
- [ ] 火山を生成・取込・設定・確認
- [ ] マグマを生成・取込・設定・確認
- [ ] マグマをアニメーションさせる場合、全フレームで外周1 pxが完全透明で、岸の位置が変化しない

### 4.7 岩壁上面 `terrain_rock_top`

- 必要数: 基本12、弱いひび3、鉱物色の点1の計16差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.4の共通設定。採用した草原をStyle referenceへ指定し、地面単体には側面を付けない
- Prompt（全文をそのまま入力）:

```text
Top-down 2D pixel art rock-wall top surface for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create sixteen interchangeable seamless variations of one continuous natural rock mass viewed directly from above, each exactly 16 by 16 pixels. Every tile must form the same flat unbuildable rock-wall top with no visible tile boundary, baked-in cliff side, thickness, outline, or cast shadow. Match the approved grassland reference exactly in pixel-cluster size, texture density, contrast range, shading complexity, outline policy, broad quiet areas, and cheerful natural-daylight color clarity. Change only the material and rock palette. Use two or three restrained value steps and crisp hand-placed pixel clusters. Do not introduce finer details, darker outlines, additional shading levels, or more isolated single-pixel noise than the grassland reference.

Use readable medium cool gray, warm gray-brown, and limited charcoal accents while keeping the overall surface bright enough for a cheerful daylight world. Make twelve quiet common rock surfaces with two to four subtle broad 1-to-3-pixel color clusters totaling approximately four to eight changed pixels per tile; three tiles with the same surface variation plus one subtle short crack; and one accent tile with a tiny clear mineral-colored speck. No common tile may be completely flat or solid-colored. Keep nearly identical average brightness and color balance across all sixteen tiles. The material must read as one continuous solid unbuildable rock mass, not a collection of separate stones. Never outline individual rock pieces. Avoid isometric diamond grid, baked-in cliff side, tile border, grid line, black seam, overall black or dull gray surface, cobblestone, brick pattern, gravel, dark grout, dense cracks, cave opening, loose boulder, large centered crack, high-contrast vein, strong directional shadow, gradient, blur, anti-aliasing, and dense speckles.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.8 岩壁内部影

岩壁内部専用のスプライトは制作しない。露出面と内部で同じ岩壁Tileを使用し、Presentationが境界から内側へ連続的に濃くなる黒い影Meshを後付けする。

- [ ] 岩壁Tile単体に黒い内部影を焼き込んでいない
- [ ] 影Meshの下でも岩壁Tileの模様が境界付近に残る
- [ ] チャンク境界で影の濃さと勾配が途切れない

### 4.9 岩壁前面側面 `wall_rock_front`

岩壁上面とは別に、画面手前側だけへ重ねる横一直線の側面を1 Sprite制作する。岩壁セルの画面下側、すなわちワールドY-1の隣接セルが岩壁以外の場合だけ表示する。隣接先が通常地形、淡水、海水、マグマのどれであっても同じ側面を使用する。

- PixelLabツール: 透明背景を扱える`Map Objects`または`Create Object`
- 必要数: 横一直線1枚
- Final canvas: `16×16`。下側12 pxを岩壁側面、上側4 pxを完全透明とする
- 可視高: `12 px`。論理セル下端から上方向へ描き、12 px上へ移動した岩壁上面の下端へ接続する
- 接続: 左右端の高さと色を揃え、同じSpriteを横へ並べたときだけ継ぎ目なく接続する
- 禁止: 外角、内角、左右の側面、斜線、洞窟、個別の石積み、上面の塗り直し
- Style reference: 採用済みの岩壁上面と`edge_rock_front`を指定し、前者の材質と後者の輪郭太さを合わせる

- Prompt（全文をそのまま入力）:

```text
A single straight horizontal front-facing side of one continuous natural rock wall for a cheerful top-down island and automation game on a sixteen-pixel orthogonal grid. Transparent background. The final canvas is exactly sixteen by sixteen pixels. Draw the visible rock face only in the bottom twelve pixel rows, starting at the logical cell bottom and building upward. Keep the upper four pixel rows completely transparent because a separate rock-top tile is displayed twelve pixels higher. The sixteen-pixel rock-top surface and twelve-pixel rock face use a one-to-zero-point-seven-five visual ratio. Add a one-pixel upper rim on the twelfth row and a readable vertical rock face below it. Keep the left and right endpoints at exactly the same height and color so copies connect seamlessly in a long straight row. Match the approved rock-wall top in material, medium cool gray and warm gray-brown palette, broad pixel-cluster size, and restrained contrast. Use two or three controlled value steps and at most two short subtle horizontal seams. It must read as solid bedrock, not separate stones. Original design, not a copy of any existing game asset. No artwork below the logical cell, no downward overhang, no corner, vertical side, curve, cave, arch, pillar, cobblestone, brick pattern, gravel, dark grout, dense cracks, terrain fill, water fill, cast shadow, gradient, blur, anti-aliasing, or isometric diamond grid.
```

- [ ] 生成
- [ ] 取込
- [ ] 岩壁専用の水平直線1枚を表示定義へ設定
- [ ] 岩壁上面と側面の境界が自然に接続
- [ ] 16タイル以上横へ並べても継ぎ目が出ない
- [ ] 左右・画面上側・角用の岩壁側面を追加していない

## 5. 通常地形と低地をつなぐ断面表現

基本地形同士は同じ高さとして扱い、草原と森林、草原と砂漠などの境界をオートタイルで混ぜない。
異なる基本地形はBase Tileの色と模様が直角に切り替わる表現を仕様とし、輪郭を丸めたりブレンドしたりしない。必要なら後続段階で低密度の装飾によって境界の硬さを和らげる。

低地断面を追加するのは、土地ブロックと、淡水、海水、マグマなど一段低く見せる地形が接するときだけとする。岩壁セルにもBiome由来の土地ブロックが下層として存在する。
土地Base Tileは論理セルへ維持し、淡水・海水・マグマのBase Tileとアニメーションだけを専用Tilemapで画面下へ8 px移動する。画面手前側の岸に当たる土地セルでは、同じ座標の`Terrain Front Edge Tilemap`へ透明な水平断面Spriteを配置する。断面は通常地形上面へ1 pxも重ならず、論理セル下端の直下から8 px伸びて、移動後の液面上辺へ接する。中心セルが岩壁でも手前が低地ならこの土地断面を表示し、その手前へ岩壁側面を重ねる。

岩壁の前面側面はこの低地断面とは別の表現である。岩壁セルの画面下側が岩壁以外なら、通常地形や低地の区別に関係なく`Rock Wall Front Face Tilemap`へ`wall_rock_front`を重ねる。側面はセル下端から上へ12 px描き、`Rock Wall Top Tilemap`の上面と、`Rock Wall Resource Tilemap`の露出資源を同じ12 pxだけ上へ移動する。どちらも画面下側1セルだけを見る単純な1bit判定だが、Sprite、表示高、Tilemapを共用しない。

### 5.0 単純な前面断面の役割

断面Spriteは横一直線の前面形状だけを使用する。通常地形セルの画面下側、すなわちワールドY-1の隣接セルが低地なら表示し、それ以外では表示しない。左右や画面奥側に低地があっても断面を出さない。

外角、内角、縦方向の側面、細い低地、孤立した低地に対応する接続形状は作らない。地形輪郭が階段状または直角に見えることを、Foragerを参考にした簡潔な世界表現として許容する。

オートタイル、RuleTile、4bit接続マスク、47形状Tilesetは使用しない。必要な判定は「画面下の1セルが低地か」という1bitの条件だけとし、ECSの`GeneratedTile`をPresentationが読み取って決める。

影や発光は初期実装へ含めない。土、砂、岩、雪の材質と高さは前面断面Sprite自身の8 pxで表現する。岩壁では12 pxの専用側面と、既存の連続アルファ内部影Meshを併用する。

- [ ] 基本地形同士の境界に断面SpriteやブレンドTileを表示しない
- [ ] 通常地形の画面下側が低地であるセルだけ前面断面Spriteを表示する
- [ ] 左右または画面上側だけが低地のセルには断面Spriteを表示しない
- [ ] 低地のBase Tileを断面Spriteで全面塗りつぶさない
- [ ] 外角、内角、縦側面を補うオートタイルを追加していない
- [ ] 岩壁の画面下側が岩壁以外の場合だけ、岩壁専用の水平側面を表示する
- [ ] 岩壁側面を低地断面SpriteやTerrain Front Edge Tilemapと共用していない
- [ ] 岩壁内部影を低地断面とは独立した既存Meshで表示する

### 5.1 表示用の高さ分類

高さ分類は断面を表示するためのPresentation情報であり、移動、建築可否、採取、物流などのゲームルールを変更しない。

| 表示分類 | 対象地形 | 断面表示 |
|---|---|---|
| 通常地形 | 草原、森林、砂漠、岩地、湿地、雪原、火山 | 画面下の隣接セルが低地の場合だけ、自身のセルへ水平な前面断面を置く |
| 低地 | 淡水、海水、マグマ | 論理セルは維持したまま液面を画面下へ8 px投影する。画面奥側にある通常地形の前面断面と接し、低地セル自身には断面を置かない |
| 高地・障害物 | 岩壁 | Biome由来の土地基盤を下層へ維持し、画面下が低地なら土地断面も置く。加えて、画面下の隣接セルが岩壁以外の場合だけ岩壁専用側面を置く |

淡水と海水、淡水とマグマなど低地同士が接する場合も、高低差の断面は表示しない。
通常地形の種類が変わる境界と、低地の種類が変わる境界は、Base Tileが直接切り替わる。

- [ ] 全`GroundType`を通常地形、低地、高地・障害物のいずれかへ分類
- [ ] 高さ分類がSimulationの移動・建築・生成結果を変更しない
- [ ] 淡水、海水、マグマ同士の境界に断面が表示されない
- [ ] チャンク外周でも画面下側1セルの地形を同じ規則で取得できる

### 5.2 必要な断面スプライト一覧

上側の通常地形を、見える断面素材が共通する4系統へまとめる。低地側の淡水、海水、マグマはBase Tileで見分け、前面断面Sprite自体は共用する。

| Asset ID | 使用する通常地形 | 断面の見た目 | 初期必須数 |
|---|---|---|---:|
| `edge_soil_front` | 草原、森林、湿地 | 暖色の土、暗い湿潤土、少量の根または小石 | 水平直線1枚 |
| `edge_sand_front` | 砂漠 | 締まった砂と淡い土の層。崩れた大粒砂を避ける | 水平直線1枚 |
| `edge_rock_front` | 岩地、火山 | 連続した岩盤断面。石積みや個別の岩輪郭を避ける | 水平直線1枚 |
| `edge_snow_front` | 雪原 | 薄い雪の縁と、その下の青灰色の凍結土または岩 | 水平直線1枚 |

初期必須数は4系統×水平直線1枚の合計4 Spriteとする。
淡水用、海水用、マグマ用へ複製しない。低地ごとの差は低地Base Tileの色で示し、前面断面は上側の材質だけを表す。

#### 差分の追加順

Base地形の16差分は、単純な輪郭でも画面がチープになりすぎないための必須要件として維持する。一方、前面断面は占有面積が小さいため、最初は各系統1枚で検証する。
長い境界で反復が目立つ場合だけ、次の順で差分を追加する。

1. 土の水平断面を3差分へ増やす。
2. 砂、岩、雪のうち反復が目立つ系統だけ2〜3差分へ増やす。
3. 追加差分も左右端の高さと色を揃え、どの組み合わせでも継ぎ目なく連続させる。
4. 角、縦辺、内角、外角用Spriteは追加しない。

- [ ] 土の水平前面断面を1枚生成
- [ ] 砂の水平前面断面を1枚生成
- [ ] 岩の水平前面断面を1枚生成
- [ ] 雪の水平前面断面を1枚生成
- [ ] 追加差分はマップ上の反復を確認してから必要な材質だけ生成

### 5.3 水平前面断面Promptへ展開する共通方針

低地断面Promptへ展開する共通方針の参照用である。このコードブロック単体は入力せず、5.4〜5.7の各素材に記載した全文Promptを使用する。

```text
A single straight horizontal front-facing terrain edge for a cheerful top-down island and automation game on a sixteen-pixel orthogonal grid. Transparent background. The visible artwork is exactly sixteen pixels wide and exactly eight pixels tall: a one-pixel upper rim and a seven-pixel cut face below it. The entire sixteen-by-sixteen logical terrain area above the cut face must remain fully transparent; the upper rim begins on the first pixel row below it and must never cover or repaint the base tile. Keep the left and right endpoints at exactly the same height and color so copies connect seamlessly in a long straight row. Use a bright readable palette, two or three controlled value steps, crisp hand-placed pixels, and minimal material detail. Original design, not a copy of any existing game asset. No corner, vertical side, inner corner, outer corner, curve, inlet, terrain fill, water fill, tall cliff, cave, waterfall, foam, cast shadow, gradient, blur, anti-aliasing, or isometric diamond grid.
```

共通ネガティブプロンプト:

```text
corner, vertical edge, curved shore, inlet, island silhouette, water fill, terrain fill, wave, foam, magma glow, tall cliff, cave, waterfall, large rubble, separate outlined stones, brick wall, cobblestone, thick black contour, long cast shadow, gradient, blur, anti-aliasing, isometric diamond grid
```

- [ ] 可視部分が水平な上端1 pxと側面7 pxの合計8 pxだけである
- [ ] 16×16の論理上面領域が完全透明で、断面の可視ピクセルが1 pxも重なっていない
- [ ] 左右端の高さと色が一致し、同じSpriteを横へ並べても継ぎ目が出ない
- [ ] 地面、低地、角、縦辺の塗りつぶしが含まれていない
- [ ] 完成Spriteの縁と断面以外が完全透明になっている
- [ ] 低地のBase Tileを隠す不要な面積がない

### 5.4 土断面 `edge_soil_front`

- 対象: 草原、森林、湿地から淡水、海水、マグマへ接する境界
- PixelLabツールと設定: 3.5の共通設定
- 色基準: 採用済み草原、森林地面、湿地の代表Tileから主要色と影色を記録して使用する
- Prompt（全文をそのまま入力）:

```text
A single straight horizontal front-facing soil edge for a cheerful top-down island and automation game on a sixteen-pixel orthogonal square grid. Transparent background. The visible artwork is exactly sixteen pixels wide and exactly eight pixels tall: a one-pixel upper rim and a seven-pixel cut face below it. The entire sixteen-by-sixteen logical terrain area above the cut face must remain fully transparent; the upper rim begins on the first pixel row below it and must never cover or repaint the base tile. Keep the left and right endpoints at exactly the same height and color so copies connect seamlessly in a long straight row. Use a bright readable palette, two or three controlled value steps, crisp hand-placed pixels, and minimal material detail. The cut face is compact warm brown soil with one darker compressed horizontal layer and only one or two tiny muted root or pebble clusters. Keep the face calm and readable at sixteen pixels. Original design, not a copy of any existing game asset. No grass carpet, tree root network, separate rock pile, mud drip, corner, vertical edge, inner corner, outer corner, curve, inlet, terrain fill, water fill, wave, foam, magma glow, tall cliff, cave, waterfall, thick black contour, cast shadow, gradient, blur, anti-aliasing, or isometric diamond grid.
```

- [ ] 生成
- [ ] 取込
- [ ] 水平直線1枚を表示定義へ設定
- [ ] 草原、森林、湿地の上面へ断面ピクセルを一切重ねない
- [ ] 淡水、海水、マグマの全背景で断面を識別できる

### 5.5 砂断面 `edge_sand_front`

- 対象: 砂漠から淡水、海水、マグマへ接する境界
- PixelLabツールと設定: 3.5の共通設定
- 色基準: 採用済み砂漠と土断面から主要色、影色、断面高を記録して使用する
- Prompt（全文をそのまま入力）:

```text
A single straight horizontal front-facing sand edge for a cheerful top-down island and automation game on a sixteen-pixel orthogonal square grid. Transparent background. The visible artwork is exactly sixteen pixels wide and exactly eight pixels tall: a one-pixel upper rim and a seven-pixel cut face below it. The entire sixteen-by-sixteen logical terrain area above the cut face must remain fully transparent; the upper rim begins on the first pixel row below it and must never cover or repaint the base tile. Keep the left and right endpoints at exactly the same height and color so copies connect seamlessly in a long straight row. Use a bright readable palette, two or three controlled value steps, crisp hand-placed pixels, and minimal material detail. The cut face is compact inland sand over a slightly darker firm horizontal soil layer. Use warm ochre and muted gold with only a few small clustered grains. Match the approved soil cut-face height and left-right endpoint colors exactly. The material must look stable rather than like a collapsing dune. Original design, not a copy of any existing game asset. No tropical beach, shells, large loose grains, flowing sand, footprint, corner, vertical edge, inner corner, outer corner, curve, inlet, terrain fill, water fill, wave, foam, magma glow, tall cliff, cave, waterfall, thick black contour, cast shadow, gradient, blur, anti-aliasing, or isometric diamond grid.
```

- [ ] 生成
- [ ] 取込
- [ ] 水平直線1枚を表示定義へ設定
- [ ] 土断面と同じ高さ・接続点になっている
- [ ] 通常の砂漠地面より暗い断面として読める

### 5.6 岩断面 `edge_rock_front`

- 対象: 岩地、火山から淡水、海水、マグマへ接する境界
- PixelLabツールと設定: 3.5の共通設定
- 色基準: 採用済み岩地、火山、岩壁上面、土断面から主要色、影色、断面高を記録して使用する
- Prompt（全文をそのまま入力）:

```text
A single straight horizontal front-facing rock edge for a cheerful top-down island and automation game on a sixteen-pixel orthogonal square grid. Transparent background. The visible artwork is exactly sixteen pixels wide and exactly eight pixels tall: a one-pixel upper rim and a seven-pixel cut face below it. The entire sixteen-by-sixteen logical terrain area above the cut face must remain fully transparent; the upper rim begins on the first pixel row below it and must never cover or repaint the base tile. Keep the left and right endpoints at exactly the same height and color so copies connect seamlessly in a long straight row. Use a bright readable palette, two or three controlled value steps, crisp hand-placed pixels, and minimal material detail. The cut face is one continuous natural horizontal rock layer using charcoal gray, cool gray, and muted brown. Use broad low-contrast pixel clusters and at most one short subtle seam. Match the approved soil cut-face height and left-right endpoint colors exactly. It must read as solid bedrock, not separate stones. Original design, not a copy of any existing game asset. No cobblestone, brick pattern, gravel, dark grout, dense cracks, lava glow, corner, vertical edge, inner corner, outer corner, curve, inlet, terrain fill, water fill, wave, foam, tall cliff, cave, waterfall, thick black contour, cast shadow, gradient, blur, anti-aliasing, or isometric diamond grid.
```

- [ ] 生成
- [ ] 取込
- [ ] 水平直線1枚を表示定義へ設定
- [ ] 岩地と火山の両方で断面が自然に見える
- [ ] 個別の石積みではなく連続した岩盤として読める

### 5.7 雪断面 `edge_snow_front`

- 対象: 雪原から淡水、海水、マグマへ接する境界
- PixelLabツールと設定: 3.5の共通設定
- 色基準: 採用済み雪原、草原、土断面から主要色、影色、断面高を記録して使用する
- Prompt（全文をそのまま入力）:

```text
A single straight horizontal front-facing snow edge for a cheerful top-down island and automation game on a sixteen-pixel orthogonal square grid. Transparent background. The visible artwork is exactly sixteen pixels wide and exactly eight pixels tall: a one-pixel upper rim and a seven-pixel cut face below it. The entire sixteen-by-sixteen logical terrain area above the cut face must remain fully transparent; the upper rim begins on the first pixel row below it and must never cover or repaint the base tile. Keep the left and right endpoints at exactly the same height and color so copies connect seamlessly in a long straight row. Use a bright readable palette, two or three controlled value steps, crisp hand-placed pixels, and minimal material detail. The cut face has a thin muted blue-white horizontal snow cap above a compact blue-gray frozen-soil or rock layer. Match the approved soil cut-face height and left-right endpoint colors exactly. Keep pure white highlights extremely limited so HUD and placement indicators remain readable. Original design, not a copy of any existing game asset. No icicle, deep snow bank, crystalline cave, reflective ice wall, corner, vertical edge, inner corner, outer corner, curve, inlet, terrain fill, water fill, wave, foam, magma glow, tall cliff, cave, waterfall, heavy blue outline, thick black contour, cast shadow, gradient, blur, anti-aliasing, or isometric diamond grid.
```

- [ ] 生成
- [ ] 取込
- [ ] 水平直線1枚を表示定義へ設定
- [ ] 雪の縁がHUDや配置表示の白と競合しない
- [ ] 他の断面系統と同じ高さ・接続点になっている

### 5.8 水際影とマグマ照り返し

初期実装では水際影とマグマ照り返しを追加しない。水平前面断面の上端と下端の色だけで、通常地形と低地の高低差を読ませる。

Gameビューで高さが読めない場合だけ、水平前面断面と同じ1bit判定からチャンク単位のMeshまたはバッチ描画を追加する。タイルごとのGameObjectやSpriteRendererは作成しない。

| 低地 | 後から検討する補助効果 | 上限 |
|---|---|---|
| 淡水 | 青緑を含む暗い接触線 | 幅1 px相当、低不透明度 |
| 海水 | 淡水より青く暗い接触線 | 幅1 px相当、低不透明度 |
| マグマ | 暗赤色の焦げまたは弱い橙色の照り返し | 幅1 px相当。白や黄色へ飽和させない |

追加する場合も色、幅、不透明度は設定値として一か所へまとめ、水平断面から外へ広げない。

- [ ] 補助効果専用Spriteを初期必須素材へ追加していない
- [ ] 効果を無効にしても断面形状を識別できる
- [ ] 必要性をGameビューで確認する前に影や照り返しを追加していない
- [ ] 補助効果がSimulationの地形やゲーム結果を変更しない

### 5.9 Presentation実装と検品

静止した水・マグマは`Ground Tilemap`から分けた`Liquid Base Tilemap`へ、透明アニメーションは`Liquid Surface Animation Tilemap`へ反映し、両方を画面下へ8 px移動する。断面表示は`Terrain Front Edge Tilemap`へ、同じ16×16サブチャンク単位で反映する。
各土地基盤セルについて画面下側、すなわちワールドY-1の1セルだけを調べ、低地なら水平断面を表示し、それ以外なら空にする。中心が岩壁でも下層にはBiome由来の土地基盤があるため判定対象に含める。
断面系統は断面を置く中心セル自身から、土、砂、岩、雪のいずれかを決める。低地の種類と岩壁の有無は断面素材の選択へ使用しない。

隣接する通常地形セルの材質が異なる場合でも、左右端の断面高を統一して直線として接続する。角を覆うSpriteや地形ペア専用Spriteは作成しない。

前面断面の差分を後から追加した場合は、ワールドSeed、タイル座標、断面系統を入力した安定ハッシュで選択する。標準RuleTileや描画フレームごとの乱数は使用しない。

現在は土、砂、岩、雪のフォールバック各1枚を接続済みである。チャンク内は生成済み`GeneratedTile`を読み、チャンク外のワールドY-1だけを`DeterministicChunkGenerator.GenerateTile`で同じ設定から再生成するため、未ロードの隣接チャンクでも判定を連続させる。フォールバック確認は実装確認であり、以下の本番素材チェックは未完了のまま扱う。

- [ ] Ground、Liquid Base、Liquid Surface Animation、Terrain Front Edgeの各Tilemapが分離されている
- [ ] 静止液面とアニメーションが同じ`-8/16セル`へ移動し、Ground Tilemapの同一論理セルへ液体Tileが重複していない
- [ ] 移動後の液面上辺と8 px断面下辺に隙間や重なりがない
- [ ] 液面下辺が手前の土地セルへ重なる場所では、土地上面が前に描かれて液面を隠している
- [ ] 通常地形同士ではTerrain Front Edge Tilemapが空になる
- [ ] 画面下側が低地でない通常地形セル、および低地セル自体ではTerrain Front Edge Tilemapが空になる
- [ ] 画面下側が低地の通常地形セルだけ水平断面が選ばれる
- [ ] 画面下側が低地の岩壁セルでは、土地断面と岩壁側面が同じ座標へ両方選ばれる
- [ ] 左右や画面上側の低地に対する縦辺・角Spriteが存在しない
- [ ] 断面Spriteと液面の最大8 pxのはみ出しをTilemapRendererとカリング範囲が含む
- [ ] 負座標とチャンク境界でも同じ1セル判定になる
- [ ] チャンクのアンロード時に断面Tileと補助効果を同じ単位で消去する
- [ ] 再ロード後も同じ座標へ同じ断面系統が出る
- [ ] 16タイル以上続く断面で規則的な反復が目立つ場合だけ追加差分を制作する

### 5.10 岩壁前面側面のPresentation実装と検品

岩壁側面は`Rock Wall Front Face Tilemap`へ16×16サブチャンク単位で反映する。各岩壁セルについて画面下側、すなわちワールドY-1の1セルだけを調べ、隣接セルが岩壁以外なら`wall_rock_front`を表示し、岩壁なら空にする。

低地断面と同じ隣接情報を利用できるが、通常地形・低地という高さ分類ではなく「岩壁か否か」だけで判定する。岩壁上面、内部影Mesh、岩壁側面は同じ`GeneratedTile`を読むPresentationであり、どれもSimulationの地形状態を変更しない。

現在は可視高12 pxのフォールバック側面を接続済みである。岩壁側面は16×16セル内の下端から上へ描き、岩壁上面、岩壁内部影、露出資源は12 px上へ移動する。低地断面と同じサブチャンク更新・消去へ含めるが、Sprite、Tilemap、Sorting Orderは共用しない。

- [ ] Ground Tilemap、Rock Wall Front Face、Rock Wall Top、Rock Wall Shadow、Rock Wall Resourceの各表示が分離されている
- [ ] 岩壁セルの画面下側が岩壁以外の場合だけ、水平な岩壁側面が選ばれる
- [ ] 岩壁が上下に連続する内部セルでは側面を表示しない
- [ ] 左右や画面上側の非岩壁セルに対する縦辺・角Spriteが存在しない
- [ ] 岩壁側面が論理セル下へはみ出さず、岩壁上面・内部影・岩壁上資源が同じ12 pxだけ上へ移動している
- [ ] 負座標とチャンク境界でも同じ1セル判定になる
- [ ] チャンクのアンロード時に岩壁側面を同じ単位で消去する
- [ ] 岩壁内部影が側面を不必要に暗くせず、上面から側面への境界が読める

## 6. 地面装飾オーバーレイ

装飾は透明背景とし、ゲームルール用の`GroundType`を変更しない。木、石などの採取資源とも別レイヤーにする。

### 6.1 草原装飾 `decor_grassland`

- 必要数: 草束2、小花2、小石2、土斑点2
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 16×16、Square top-down、Transparent background、Outlineなし、Details Low
- Prompt（全文をそのまま入力）:

```text
Eight separate sparse top-down pixel-art ground overlays for a warm meadow: 1) short grass tuft, 2) bent grass tuft, 3) two tiny pale flowers, 4) three tiny yellow flowers, 5) two small gray pebbles, 6) one warm brown pebble, 7) a tiny irregular bare-soil speckle, 8) a second subtle soil speckle. Exactly one quiet decoration per transparent 16 by 16 tile. No filled background, border, shadow, or large object.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 低密度配置を確認

### 6.2 森林装飾 `decor_forest`

- 必要数: 落ち葉2、苔2、根2、小シダ2
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 6.1と同じ
- Prompt（全文をそのまま入力）:

```text
Eight separate sparse top-down pixel-art forest-floor overlays: 1) two muted fallen leaves, 2) a tiny leaf cluster, 3) a small moss patch, 4) a second irregular moss patch, 5) a thin shallow root, 6) a forked tiny root, 7) a very small fern sprout, 8) a second low fern sprout. Exactly one quiet decoration per transparent 16 by 16 tile. No filled background, tree, trunk, mushroom, shadow, or border.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 低密度配置を確認

### 6.3 砂漠装飾 `decor_desert`

- 必要数: 小石3、乾いた草2、砂紋3
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 6.1と同じ
- Prompt（全文をそのまま入力）:

```text
Eight separate sparse top-down pixel-art inland desert overlays: 1) two tiny dark pebbles, 2) one warm brown pebble, 3) three tiny pale stones, 4) a small dry grass tuft, 5) a second bent dry grass tuft, 6) a short shallow wind ripple, 7) two subtle sand arcs, 8) a tiny irregular darker-grain patch. Exactly one quiet decoration per transparent 16 by 16 tile. No filled background, cactus, bones, footprints, large dune, shadow, or border.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 低密度配置を確認

### 6.4 水面装飾 `decor_water`

- 必要数: 波紋2、水草2、淡い反射2
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 6.1と同じ。半透明を避け、不透明色の疎なピクセルで描く
- Prompt（全文をそのまま入力）:

```text
Six separate sparse top-down pixel-art freshwater overlays: 1) a tiny circular ripple, 2) a broken irregular ripple, 3) two small submerged water-grass tips, 4) one sparse aquatic plant, 5) a tiny pale reflection cluster, 6) a second broken reflection cluster. Transparent 16 by 16 tiles, low contrast, no filled water background, foam, fish, lily pad, large wave, or border.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 水面アニメーションとの干渉を確認

## 7. 第1段階で必要な資源・主要オブジェクト

### 7.1 草原の木 `resource_tree_basic`

- 必要数: 成木3差分。占有は1×1タイル、描画枠は32×32 pxを初期値とする
- PixelLabツール: `Map Objects`
- 設定: 3.6の共通設定、Canvas `32×32`、Transparent background
- Prompt（全文をそのまま入力）:

```text
A small friendly deciduous resource tree for a cozy agricultural automation game, top-down oblique pixel-art view on an orthogonal square grid, with the crown top and the front of the trunk both readable. Rounded leafy crown, warm medium green leaves, muted brown trunk, and crisp pixel clusters. The trunk contact point occupies one 16 by 16 ground cell while the crown extends upward into the tile above. Place the ground contact point near the lower center of the canvas. Thin dark outline, upper-left light, transparent background. No isometric diamond base, fruit, face, planter, grass base, long cast shadow, or scenery.
```

- [ ] 3差分を生成
- [ ] 取込
- [ ] 資源Prefabまたは表示定義へ設定
- [ ] 隣接する木と設備の重なり順を確認

### 7.2 石資源 `resource_stone_basic`

- 必要数: 3差分。論理占有は1×1、描画枠は16×24 pxを初期値とする
- PixelLabツール: `Map Objects`
- 設定: 3.6の共通設定、Canvas `16×24`、Transparent background
- Prompt（全文をそのまま入力）:

```text
A small mineable stone deposit for a cozy agricultural automation game, top-down oblique pixel-art view on an orthogonal square grid, showing the top and short front faces of two or three rounded charcoal-gray and muted brown rocks. The ground contact point occupies one 16 by 16 cell while the rock tops may extend upward into the tile above. Place the contact point near the lower center of a 16 by 24 canvas. Crisp 1-pixel clusters, thin dark outline, upper-left light, transparent background. No isometric diamond base, grass base, ore crystal, long cast shadow, text, or scenery.
```

- [ ] 3差分を生成
- [ ] 取込
- [ ] 資源Prefabまたは表示定義へ設定
- [ ] 草原、森林、砂漠の全背景で確認

### 7.3 砂資源パッチ `resource_sand_patch`

- 必要数: 3差分。16×16 px
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: Square top-down、16×16、Transparent background、Outlineなし、Details Low
- Prompt（全文をそのまま入力）:

```text
Three separate mineable sand-resource overlays for a top-down 16 by 16 pixel tile: compact irregular piles of slightly brighter coarse sand grains with a few darker grains, clearly distinct from ordinary desert ground but still low profile. Transparent background, no container, shovel, footprint, dune, border, text, or cast shadow.
```

- [ ] 3差分を生成
- [ ] 取込
- [ ] 資源Prefabまたは表示定義へ設定
- [ ] 通常の砂漠地面と識別できることを確認

### 7.4 水資源表示

淡水・海水は地形属性そのものを固定資源として扱うため、第1段階では専用オブジェクトを追加しない。採取可能地点と水種別を示す必要が生じた場合は、採取ノードの配置プレビューまたはUIアイコンで示し、常時表示のマーカーを地形へ焼き込まない。

- [ ] 専用スプライト不要であることを実装と確認

### 7.5 ドローン `player_drone`

- 必要数: 8方向の待機画像、待機4フレーム、移動4フレームを初期値とする
- PixelLabツール: 基準絵は`Create character`、方向差分は同ツールの8方向生成、動きは`Animate with text`
- 設定: Canvas `24×32`または`32×32`で比較、Top-downの斜め見下ろし、Transparent background、Outline Thin、Shading Medium、Details Medium
- Base Prompt（全文をそのまま入力）:

```text
A small friendly utility drone for a cozy agricultural and industrial automation game, top-down oblique pixel art on an orthogonal square grid, showing its upper surface and short front face. Rounded compact body, two small side thrusters, warm off-white painted metal with muted teal panels and one amber status light, readable mechanical silhouette. Its logical position is one 16 by 16 ground cell, but its body may extend upward into the tile above. Keep its ground projection centered near the lower center of the canvas. No isometric diamond base, humanoid limbs, weapon, text, or background. It must remain clearly readable over green grass, dark forest floor, yellow sand, blue water, and charcoal rock.
```

- Idle Animation Prompt（全文をそのまま入力）:

```text
Create a subtle seamless four-frame idle hover loop. The drone rises and falls by at most one pixel, with a gentle alternating amber status light. Preserve body shape, direction, palette, center position, and transparent background.
```

- Move Animation Prompt（全文をそのまま入力）:

```text
Create a seamless four-frame movement loop in the current direction. Show a restrained one-pixel thruster pulse and a slight body tilt without changing the silhouette size, palette, or center alignment. Transparent background, no particles outside the canvas.
```

- [ ] 基準絵を生成
- [ ] 8方向を生成
- [ ] 待機アニメーションを生成
- [ ] 移動アニメーションを生成
- [ ] 取込
- [ ] Animatorまたは表示システムへ設定
- [ ] 全地形とズーム段階で確認

### 7.6 開始コア `node_starting_core`

- 必要数: 待機4フレーム、稼働4フレーム。占有は1×1タイル、描画枠は24×24または32×32 px
- PixelLabツール: 基準絵は`Create object` → `1 direction`、動きは`Animate with text`
- 設定: Top-downの斜め見下ろし、Transparent background、Outline Thin、Shading Medium、Details Medium
- Base Prompt（全文をそのまま入力）:

```text
A compact starting core machine for a cozy agricultural and industrial automation game, top-down oblique pixel art on an orthogonal square grid, showing its top surface and readable front face. Its base logically occupies one 16 by 16 ground cell while the upper body extends upward into the tile above. Place the ground contact point near the lower center of the canvas. Warm off-white painted metal, muted teal panels, one amber energy window, four readable logical connection sides, friendly repaired technology rather than military machinery, crisp pixel clusters, transparent background. No isometric diamond base, text, logo, weapon, painted ground tile, long shadow, or cable attached.
```

- Idle Animation Prompt（全文をそのまま入力）:

```text
Create a subtle seamless four-frame powered idle loop. Animate only a soft amber pulse and one small mechanical breathing motion. Keep the machine footprint, ports, outline, palette, center, and transparent background perfectly stable.
```

- Active Animation Prompt（全文をそのまま入力）:

```text
Create a readable seamless four-frame active production loop with a brighter amber pulse and a small rotating or reciprocating top component. Keep all four connection sides, footprint, palette, outline, and center fixed. No sparks, smoke, text, or camera movement.
```

- [ ] 基準絵を生成
- [ ] 待機アニメーションを生成
- [ ] 稼働アニメーションを生成
- [ ] 取込
- [ ] Prefabまたは表示定義へ設定
- [ ] 配置プレビューとポート可読性を確認

## 8. Unityインポート設定

全PNGまたはスプライトシートで確認する。

- [ ] Texture Typeを`Sprite (2D and UI)`へ設定
- [ ] Sprite Modeを単体画像は`Single`、シートは`Multiple`へ設定
- [ ] Pixels Per Unitを`16`へ設定
- [ ] Filter Modeを`Point (no filter)`へ設定
- [ ] Compressionを`None`へ設定
- [ ] Generate Mip Mapsを`Off`へ設定
- [ ] Anti Aliasingを無効化し、CameraのMSAAとDynamic Resolutionも`Off`になっていることを確認
- [ ] Wrap Modeを地形テクスチャは用途に応じて`Clamp`、単純な繰り返しテクスチャだけ`Repeat`へ設定
- [ ] Alpha Is Transparencyを透明背景アセットで`On`へ設定
- [ ] Sprite Mesh Typeを原則`Full Rect`へ設定
- [ ] Pivotを地面は`Center`、1×1占有オブジェクトは地面との接地点へ統一
- [ ] 各立体物の接地点、論理占有サイズ、上下左右の最大描画はみ出し量を表示定義へ記録
- [ ] SpriteRendererのSort Pointを`Pivot`にし、接地点のワールドY座標で前後関係を決定
- [ ] ガターなしのスプライトシートは16×16 pxのGrid by Cell SizeでSlice
- [ ] 67×67 pxの4×4シートなど1 pxガター付き出力は、Cell Size 16×16、Padding 1×1、Offset 0×0でSlice
- [ ] Base地形はSlice後の各Spriteが16×16 pxで、黒い区切り線を含まないことをSprite Editorで確認
- [ ] PixelLabで生成した前面断面・岩壁側面へ、拡大縮小、補間、可視ピクセルの描き足しを行っていない
- [ ] 低地断面Spriteは16×24 pxで、上側16×16が完全透明かつ論理セルへ一致し、下側8 pxがすべて断面として可視である
- [ ] 岩壁側面Spriteは16×16 pxで、下側12 pxだけに側面があり、上側4 pxが完全透明である
- [ ] 低地断面のPivotは論理セル中心がTilemapセル中心へ一致するCustom `(0.5, 0.6)`を初期値とし、1 pxのずれがないことをSprite Editorで確認
- [ ] 岩壁側面のPivotはCenter `(0.5, 0.5)`で、論理セル下端から上へ12 px表示されることをSprite Editorで確認
- [ ] PNGと`.meta`を一緒にバージョン管理対象へ追加
- [ ] SceneまたはPrefabに意図しない全面差分がないことを確認

## 9. Unity側の表示アセット構成

推奨配置先:

```text
Assets/Farmware/Art/WorldGeneration/
├── Terrain/
│   ├── Base/
│   ├── Edges/
│   └── Decorations/
├── Resources/
├── Player/
└── Nodes/
```

ファイル名は`種別_用途_差分番号.png`を基本とする。

```text
terrain_grassland_01.png
terrain_grassland_02.png
edge_soil_front.png
edge_sand_front.png
edge_rock_front.png
edge_snow_front.png
wall_rock_front.png
overlay_water_ripple_01.png
overlay_magma_flow_01.png
decor_forest_fern_01.png
resource_tree_basic_01.png
player_drone_idle_south.png
node_starting_core_active.png
```

現在の`ChunkTilemapPresenter`は、7バイオーム地面を基準面の`Ground Tilemap`、淡水・海水・マグマを`Liquid Base Tilemap`へ排他的に表示する。静止液面と透明アニメーションは論理セルを変えず画面下へ8 px移動する。岩壁セルにもBiome由来の土地基準面を残し、岩壁上面を`Rock Wall Top Tilemap`、側面を`Rock Wall Front Face Tilemap`、露出資源を`Rock Wall Resource Tilemap`へ分離する。上面・資源・内部影は基準面より12 px上、側面は16×16セル下端から上へ12 pxとなる。`Terrain Front Edge Tilemap`の土地断面は上面へ重ならず液面上辺へ接し、移動した液面の下辺は手前土地と重なる部分だけ土地上面に隠れる。岩壁が低地へ面する場所でも土地断面を省略せず、その手前へ岩壁側面を重ねる。

- [x] 通常地形と低地の高さ分類をPresentationで判定できる
- [x] 静止液面と透明アニメーションをGround Tilemapから分離し、同じ8 pxだけ画面下へ移動できる
- [x] 液体セルをGround Tilemapへ重複配置せず、移動した液面下辺を手前土地で隠せる
- [x] Terrain Front Edge TilemapをGround Tilemapと分離して表示・消去できる
- [x] 通常地形セルの画面下側1セルが低地かを判定し、水平前面断面と断面系統を選択できる
- [x] 基本地形同士、および低地同士の境界では断面Tileを表示しない
- [x] 左右・画面上側・角へ補完用の断面Tileを表示しない
- [ ] 同じ前面断面の複数Spriteを追加した場合、ワールドSeedと座標を含む安定ハッシュで選択できる
- [x] チャンク境界の外側にある画面下側1セルを参照しても、ロード順で断面表示が変化しない
- [x] Rock Wall Front Face Tilemapを他の地形表示と分離して表示・消去できる
- [x] 岩壁セルの画面下側1セルが岩壁以外の場合だけ、水平な岩壁側面を表示できる
- [x] Rock Wall Top TilemapとRock Wall Resource Tilemapを基準面より12 px上へ表示・消去できる
- [x] 持ち上げた岩壁上面へ後付けした連続影で、内部と露出面を表現できる
- [x] Liquid Surface Animation Tilemapで、水面・マグマのBase Tileを維持したまま透明アニメーションを重ねられる
- [x] 液面アニメーションを無効化しても地形分類と岸の位置が変わらない
- [ ] 装飾を地面と別Tilemapまたはバッチ描画レイヤーへ表示できる
- [ ] 論理占有1×1と、上方向へはみ出す描画Boundsを別々に保持できる
- [ ] 画面下側のセルに接地する物体を手前へ描くYソートが機能する
- [ ] サブチャンクとカメラのカリング範囲が最大描画はみ出し量を含む
- [ ] 木、石、砂をECS状態からResource Tilemapへ反映できる
- [ ] ドローン、開始コアをECS状態からPresentationへ反映できる
- [ ] TilemapやGameObjectをゲーム状態の正として使用していない

## 10. 検品用テストマップ

最低でも以下を1画面に配置し、採用前にスクリーンショットを残す。

- [ ] 各地形を16×16タイル以上の面積で敷き、繰り返し模様を確認
- [ ] 同じ差分が縦横に連続した場合でも継ぎ目が出ないことを確認
- [ ] 通常地形の画面下側が低地の場所だけ、水平な前面断面が表示され、上面へ1 pxも重ならないことを確認
- [ ] 左右・画面上側の岸、外角、内角に補完断面が表示されず、輪郭が直角のままであることを確認
- [ ] 基本地形同士、および淡水・海水・マグマ同士の境界に断面が出ないことを確認
- [ ] 土、砂、岩、雪の各断面を淡水・海水・マグマへ重ねて確認
- [ ] 16タイル以上続く水平断面で継ぎ目や過度な反復がないことを確認
- [ ] 岩壁の画面下側が岩壁以外の場所だけ、12 pxの水平な岩壁側面が表示されることを確認
- [ ] 岩壁上面、内部影、岩壁上資源が基準面より12 px上に揃い、側面がセル下端から上へ接続することを確認
- [ ] 岩壁が淡水・海水・マグマへ面する場所で、土地断面が欠けず岩壁側面の奥へ連続することを確認
- [ ] 岩壁の左右・画面上側・角には補完側面が表示されず、画面下側の直線だけで読めることを確認
- [ ] 同じ岩壁Tileの上で、内部だけが滑らかな黒い影になり、露出面の模様が読めることを確認
- [ ] 淡水、海水、マグマの4フレームを確認し、外周1 px、岸、低地断面、岩壁側面の位置が動かないことを確認
- [ ] 液面の波紋が上下に動いて見えても、隣接Tileとの継ぎ目や白いフラッシュが出ないことを確認
- [ ] 木、石、砂資源を全地形背景に重ね、輪郭を識別できることを確認
- [ ] 1×1占有物を上下に隣接配置し、上方向へのはみ出しとYソートが自然に見えることを確認
- [ ] 接地点は画面外だが上部だけ画面内に見える物体が、カリングで消えないことを確認
- [ ] 立体物が16×16サブチャンク境界をまたいでも欠けないことを確認
- [ ] ドローン、開始コア、配置可能・不可表示を各地形へ重ねて確認
- [ ] カメラの最小・標準・最大ズームでピクセルがにじまないことを確認
- [ ] Game Viewの実解像度で細部がちらつかないことを確認
- [ ] 64×64チャンク境界および16×16サブチャンク境界に見た目の継ぎ目がないことを確認

## 11. 制作記録

採用した生成物ごとに追記する。Seedと設定を残すことで、同じ画風の追加素材を作りやすくする。

| 日付 | Asset ID | PixelLab Tool | Seed | 主な設定変更 | 出力ファイル | 採用・却下理由 |
|---|---|---|---|---|---|---|
|  |  |  |  |  |  |  |

## 12. 後続段階で追加するスプライト

次の項目は第1段階の地形生成検証には含めず、仕様上の導入段階が来たときに個別チェックリストとプロンプトを追加する。

- [ ] 小麦、牛など草原固有資源
- [ ] 硬木、樹液、薬草など森林固有資源
- [ ] サボテン、塩など砂漠固有資源
- [ ] 採取機、加工機、保管箱、分配器
- [ ] 物流リンク、リレー点、搬送アイテム
- [ ] 稼働、停止、詰まり、破損の各設備状態
- [ ] 敵オートマトンと防衛ノード
- [ ] バイオーム固有の粒子、環境演出、希少資源変異

## 13. PixelLab参照資料

- [Create tiles (Pro)](https://www.pixellab.ai/docs/tools/create-tiles-pro): Base地形の16差分に使用する。接続Tilesetや地形オートタイルには使用しない
- [Create texture](https://www.pixellab.ai/docs/tools/create-texture): Tilesetと組み合わせる地表テクスチャ
- [PixelLab API](https://api.pixellab.ai/v2/docs): Map Objects、Object、Character、Animationを含むツール一覧
