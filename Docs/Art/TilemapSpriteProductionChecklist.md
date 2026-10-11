# Farmware タイルマップ用スプライト制作・導入チェックリスト v0.1

- 作成日: 2026-10-11
- 対象: ランダムマップ生成の第1段階（草原、森林、砂漠、淡水、海水、岩壁、木、石、砂、ドローン、開始コア）
- 関連仕様: [ランダムマップ生成仕様](../GameSpecification/RandomMapGenerationSpecification.md)
- 関連仕様: [ゲームコンセプト仕様](../GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- 基準解像度: 1タイル = 16×16 px（試作値。設備、リンク、ドローンを重ねた可読性確認後に確定する）

## 1. この文書の目的

PixelLabで制作するスプライトを漏れなく管理し、生成、Unityへのインポート、表示設定、実機確認を段階別に記録する。

Editorの構築メニューが `Assets/Farmware/Art/WorldGeneration/Fallback/` へ生成する6種類の16×16地形PNGは、地形表示を成立させるための仮素材である。本書のチェックは、PixelLabで制作した本番候補へ置き換えた時点から付ける。仮素材が存在するだけではチェックしない。

### 1.1 チェック欄の意味

各アセットは次の4段階を個別に確認する。

- `生成`: PixelLabからPNGまたはスプライトシートを書き出し、目視検品した。
- `取込`: PNGと対応する`.meta`をUnityプロジェクトへ追加し、Import Settingsを設定した。
- `設定`: Tile、RuleTile、AnimationClip、Prefabなど、ゲームから参照できるアセットへ接続した。
- `確認`: 対象Sceneで継ぎ目、可読性、拡大縮小時のにじみを確認した。

チェック例:

```text
- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認
```

## 2. 共通アート方針

### 2.1 視点と形状

- Stardew ValleyやCore Keeperのような斜め見下ろし表現を使用する。地面グリッドは正方形のままとし、アイソメトリックの菱形グリッドにはしない。
- 地面は16×16 pxの正方形へ継ぎ目なく接続する平面として描き、木、岩、設備、ドローンなどの立体物では上面と正面側が読める角度を使用する。
- 通常地面には側面、外周線、タイル境界線、投影影を描かない。
- 岩壁だけは地面との境界で短い側面を表示してよい。連続する岩壁内部は別の黒塗りタイルで隠す。
- 木、岩、設備などは1×1タイルを論理的に占有しても、接地点をセル内へ置いたまま、上部を画面上方向のセルへ視覚的にはみ出してよい。ゲーム上の占有範囲とは分離する。
- 1×1物体の標準キャンバスは16×24 pxまたは16×32 pxを候補とする。横幅も必要な木や主要設備は24×32 pxまたは32×32 pxを使用できるが、論理占有は別途指定する。
- 立体物の基準点は、幹、脚、土台などが地面へ接する位置の中央とする。前後関係はこの接地点のワールドY座標で決める。
- 上方向へのはみ出し量、左右へのはみ出し量、接地点を制作記録へ残す。サブチャンク境界や画面端で欠けないことを検品する。

### 2.2 色、明度、情報量

- 温かく親しみやすい自然色を基本にし、工業設備を載せても地面が主張しすぎない明度と彩度にする。
- バイオームは色だけに依存せず、草、落ち葉、砂粒などの模様でも区別する。
- 同じ地形の差分は平均明度と主色を近づけ、ランダム配置時に市松模様を作らない。
- 目立つ花、ひび、大石などは通常地面へ大量に焼き込まず、低密度の装飾オーバーレイへ分離する。
- 地面には方向性の強い影を置かない。自然物と設備の光源は左上に統一する。
- 16×16 pxでは1 px単位の形を優先し、アンチエイリアス、半透明の縁、縮小前提の細部を使用しない。

### 2.3 バリエーションの配置比率

地面差分を実装するときの初期比率は以下を目安にする。最終比率はマップ上で確認して調整する。

| 種類 | 目安 | 用途 |
|---|---:|---|
| 基本差分 | 75% | 低ノイズで設備の背景になる地面 |
| 軽い模様 | 20% | 小さな色むら、草、葉、砂粒 |
| 目立つ模様 | 5% | 小花、小石、ひびなどの低頻度アクセント |

差分の選択は描画側の非決定的な乱数ではなく、ワールドシードとタイル座標から得た安定したハッシュで行う。同じセーブを読み込んだときに模様が変わらないようにする。

## 3. PixelLab共通設定

PixelLabのUI更新により項目名や段階数が変わった場合は、次表の意味に最も近い値を選び、実際に使った値とSeedを「制作記録」へ追記する。

### 3.1 共通スタイルプロンプト `STYLE-TERRAIN`

地面、境界、地面装飾の各プロンプトの先頭へ追加する。

```text
Top-down oblique 2D pixel art terrain for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Each ground tile is exactly 16 by 16 pixels and forms a flat seamless ground surface that matches raised objects drawn with visible top and front faces. Crisp hand-placed pixel clusters, limited cohesive palette, warm natural colors, simple readable shapes, subtle organic texture, and low visual noise. The terrain must remain clearly readable underneath small machines, logistics links, resource icons, and placement overlays. Seamless edges with no visible grid lines.
```

### 3.2 共通ネガティブプロンプト `NEG-TERRAIN`

ネガティブプロンプト欄があるツールでは必ず設定する。欄がない場合は、同内容を説明文の末尾へ `Avoid ...` として追加する。

```text
isometric diamond grid, side-scroller view, strong perspective, visible sides on ordinary flat ground, thick bevel, tile border, grid line, long directional cast shadow, gradient, blur, anti-aliasing, smooth vector art, photorealism, text, UI, building, machine, character, large centered landmark, high visual noise
```

### 3.3 地形バリエーション共通設定

- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- Shape: `Square top-down`
- Tile size: `16×16`
- View angle: `Top-down`。地面自体には立体物用の正面を描かない
- Tile thickness: `0`または最小
- Outline mode: `No outline`
- Shading: `Low`
- Details: `Low`から`Medium`
- Seed: 生成ごとに記録する
- Style reference: 最初に採用した草原タイルを基準画像とし、それ以降の地形へ使用する
- 出力数: 地形ごとに最低8候補を生成し、必要数を選別する

`Create tiles (Pro)`のTiles機能は独立した差分生成に使う。接続可能な境界が必要な箇所では、後述の`Create Tileset`を使う。

### 3.4 オートタイル共通設定

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- Tile size: `16×16`
- Edge shape: `Round`
- Roundness: `Medium`
- Raggedness / Border jitter: `Low`から`Medium`
- Enhance descriptions with AI: 初回は`On`。側面や余計な物体が追加される場合は`Off`で再生成する
- Export: 元のWang tilesetを保存したうえで、Unity用に`3×3`または`dual-grid 15 tileset`も書き出す
- Seed: 生成ごとに記録する

地形の上下は見た目の前後関係であり、ゲームルール上の優先順位ではない。生成後はUnityのRuleTile規則と四隅が一致することを小さなテストマップで確認する。

### 3.5 静止オブジェクト共通設定

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

### 3.6 動くオブジェクト共通設定

- 基準絵: `Create object`または`Create character`
- アニメーション: `Animate with text`
- Transparent background: `On`
- Camera view: `Top-down`の斜め見下ろし。上面と正面側が読める角度
- フレーム間で輪郭、サイズ、中心位置、光源方向が変化しないことを優先する
- Unityでは各フレームをPointフィルターで表示し、補間による中間画像を作らない

## 4. 地形ベーススプライト

### 4.1 草原 `terrain_grassland`

- 必要数: 基本4、軽い草2、土の斑点1、小花1の計8差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.3の共通設定
- Prompt: `STYLE-TERRAIN`に続けて以下を入力する。

```text
Create eight interchangeable seamless meadow ground variations: four very plain common tiles, two tiles with sparse short grass blades, one tile with a tiny bare-soil patch, and one tile with one or two very small pale flowers. Use muted fresh green and warm yellow-green pixels over compact soil. Keep nearly identical average brightness and color balance across every variation. Keep tile centers quiet and avoid recognizable repeated landmarks.
```

- [x] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.2 森林地面 `terrain_forest_floor`

- 必要数: 基本4、落ち葉2、根1、小さなシダ1の計8差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.3の共通設定。草原をStyle referenceへ指定する
- Prompt:

```text
STYLE-TERRAIN. Create eight interchangeable seamless moist forest-floor variations: four plain mossy ground tiles, two with sparse fallen leaves, one with tiny shallow roots, and one with a very small fern sprout. Use deep moss green and muted brown soil. The floor should feel humid but remain calm and readable below machines. No tree, trunk, large plant, hole, or strong shadow.
```

- [x] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.3 砂漠 `terrain_desert_sand`

- 必要数: 基本4、濃い砂粒2、小石1、弱い砂紋1の計8差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.3の共通設定。草原をStyle referenceへ指定する
- Prompt:

```text
STYLE-TERRAIN. Create eight interchangeable seamless compact desert-ground variations: four plain sand tiles, two with sparse darker grains, one with a tiny pebble cluster, and one with a very subtle shallow wind ripple. Use warm ochre and pale muted gold. It is dry inland soil rather than a bright tropical beach. Avoid long continuous lines, large dunes, footprints, bones, and high-contrast patterns.
```

- [x] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.4 淡水面 `terrain_fresh_water`

- 必要数: 静止画の場合6差分。アニメーションを採用する場合は、低ノイズ2種類×4フレームを初期値とする
- PixelLabツール: 静止画は`Create tiles (Pro)` → `Tiles (variations)`、アニメーションは採用静止画を`Animate with text`へ渡す
- 静止画設定: 3.3の共通設定。Tile thicknessは0、Outlineなし
- Static Prompt:

```text
STYLE-TERRAIN. Create six interchangeable seamless calm freshwater surface variations viewed directly from above. Use muted blue, blue-green, and a very small amount of pale reflected light. Draw tiny irregular ripple clusters with matching edge colors. Avoid parallel wave stripes, ocean swell, foam, shore, fish, floating objects, and strong directional highlights.
```

- Animation Prompt:

```text
Animate only the tiny water ripples in a subtle seamless four-frame loop. Keep the palette, brightness, tile edges, pixel density, and camera perfectly fixed. No camera movement, no new objects, no foam, and no large waves.
```

- Animation settings: `4 frames`、Loop、動きの強さ`Low`
- [x] 静止差分を生成
- [ ] アニメーション採用可否を決定
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.5 海水面 `terrain_salt_water`

- 必要数: 静止画の場合6差分。アニメーションを採用する場合は、低ノイズ2種類×4フレームを初期値とする
- PixelLabツール: 静止画は`Create tiles (Pro)` → `Tiles (variations)`、アニメーションは採用静止画を`Animate with text`へ渡す
- 静止画設定: 3.3の共通設定。Tile thicknessは0、Outlineなし。淡水面をStyle referenceへ指定する
- Static Prompt:

```text
STYLE-TERRAIN. Create six interchangeable seamless calm saltwater surface variations viewed directly from above. Use a slightly deeper and darker blue than the freshwater reference, with sparse small wave clusters and a very limited amount of pale reflected light. Keep the surface readable as ocean water without drawing foam or a shoreline. Avoid tropical cyan, large ocean swell, parallel stripes, fish, boats, floating objects, and strong directional highlights.
```

- Animation Prompt:

```text
Animate only the small saltwater wave clusters in a subtle seamless four-frame loop. Keep the darker ocean palette, brightness, tile edges, pixel density, and camera perfectly fixed. No camera movement, shore foam, large waves, or new objects.
```

- Animation settings: `4 frames`、Loop、動きの強さ`Low`
- [ ] 静止差分を生成
- [ ] アニメーション採用可否を決定
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.6 岩壁上面 `terrain_rock_top`

- 必要数: 基本3、弱いひび2、鉱物色の点1の計6差分
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 3.3の共通設定。地面単体には側面を付けない
- Prompt:

```text
STYLE-TERRAIN. Create six interchangeable seamless solid natural rock-top variations viewed directly from above: three plain dense stone tiles, two with subtle short cracks, and one with a tiny muted mineral-colored speck. Use charcoal gray, cool gray, and muted brown. The material must read as heavy unbuildable rock. No visible cliff side, cave opening, loose boulder, large centered crack, or border outline.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.7 岩壁内部 `terrain_rock_interior_black`

- 必要数: 1枚。必要なら黒に近い2差分まで
- PixelLabツール: PixelLabを使用せず、Unityまたはドット絵エディタで16×16の不透明単色PNGを作成する
- 色: 完全な黒を初期値とする。画面上でUIの黒と混同する場合だけ、わずかに青または紫を含む黒へ調整する
- 理由: AI生成では不要な模様や圧縮色が混ざりやすく、単色画像に生成コストを使う意味がない
- [ ] 作成
- [ ] 取込
- [ ] 設定
- [ ] 確認

## 5. 地形オートタイル

### 5.1 草原と水 `autotile_grassland_water`

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- 設定: Transition size `1`、Side wall thickness `0`、3.4の共通設定
- Inner / Lower terrain:

```text
Calm muted blue-green freshwater viewed directly from above, with tiny irregular low-contrast ripples.
```

- Outer / Upper terrain:

```text
Soft warm green meadow grass over compact brown soil, viewed directly from above, with sparse tiny grass clusters and a quiet center.
```

- Transition:

```text
An irregular natural freshwater shoreline with a narrow band of damp dark soil and a few isolated grass pixels. No beach, foam, rocks, cliff height, or visible tile grid.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 全16近傍パターンを確認

### 5.2 砂漠と水 `autotile_desert_water`

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- 設定: Transition size `1`、Side wall thickness `0`、3.4の共通設定
- Inner / Lower terrain:

```text
Calm muted blue-green freshwater viewed directly from above, with tiny irregular low-contrast ripples.
```

- Outer / Upper terrain:

```text
Compact warm ochre inland desert sand with pale muted gold grains and very low visual noise.
```

- Transition:

```text
A narrow irregular wet-sand shoreline, slightly darker beside the water and fading quickly into dry compact sand. No tropical beach, shells, foam, cliff, or large stones.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 全16近傍パターンを確認

### 5.3 森林地面と水 `autotile_forest_water`

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- 設定: Transition size `1`、Side wall thickness `0`、3.4の共通設定
- Inner / Lower terrain:

```text
Calm muted blue-green freshwater viewed directly from above, with tiny irregular low-contrast ripples.
```

- Outer / Upper terrain:

```text
Moist dark forest floor with moss green and muted brown soil, sparse leaf litter, and no trees.
```

- Transition:

```text
An irregular shaded freshwater bank with damp dark soil, small moss pixels, and sparse leaf litter. No tree roots crossing the water, logs, rocks, foam, or cliff height.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 全16近傍パターンを確認

### 5.4 草原と砂漠 `autotile_grassland_desert`

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- 設定: Transition size `1`から`2`、Side wall thickness `0`、3.4の共通設定
- Inner / Lower terrain:

```text
Compact warm ochre desert sand with subtle clustered grains and low contrast.
```

- Outer / Upper terrain:

```text
Soft warm green meadow grass over compact brown soil with sparse tiny grass clusters.
```

- Transition:

```text
A gradual dry grassland edge where grass becomes sparse and exposes warm sandy soil. Irregular organic boundary with no cliff, border outline, or hard color seam.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 全16近傍パターンを確認

### 5.5 草原と森林地面 `autotile_grassland_forest`

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- 設定: Transition size `1`から`2`、Side wall thickness `0`、3.4の共通設定
- Inner / Lower terrain:

```text
Moist dark forest floor with moss, muted brown soil, and sparse tiny leaf litter.
```

- Outer / Upper terrain:

```text
Open warm green meadow grass with a simple low-noise surface.
```

- Transition:

```text
A soft ecological transition with increasingly dark grass, scattered moss pixels, and a few tiny fallen leaves. No trees, large plants, hard line, or height difference.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 全16近傍パターンを確認

### 5.6 草原と岩壁 `autotile_grassland_rock_wall`

- PixelLabツール: `Create Tileset` → `Pro` → `Top-Down`。高さ調整が不安定な場合はStandard版も比較する
- 設定: Tile size `16×16`、Transition height `3 px相当`、Step slope `0`、Boundary spread `Low`、Raggedness `Low`から`Medium`
- Main / Upper terrain:

```text
Solid dense charcoal-gray natural rock top, heavy and unbuildable, viewed directly from above.
```

- Second / Lower terrain:

```text
Walkable warm green meadow ground, flat and viewed directly from above.
```

- Transition:

```text
An exposed rocky cliff edge. The rock top remains visible along the rim, with a short dark vertical stone face toward the open ground. Crisp readable silhouette, no grass hanging over the wall, no cave opening, and no loose boulders.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 外角、内角、1セル幅の岩壁を確認

### 5.7 砂漠と岩壁 `autotile_desert_rock_wall`

- PixelLabツール: `Create Tileset` → `Pro` → `Top-Down`
- 設定: 5.6と同じ。岩の主色と壁面の高さを5.6へ合わせる
- Main / Upper terrain:

```text
Solid dense charcoal-gray and muted brown natural rock top, heavy and unbuildable, viewed directly from above.
```

- Second / Lower terrain:

```text
Compact warm ochre inland desert sand with low visual noise.
```

- Transition:

```text
A short exposed rocky cliff face meeting dry compact sand. Add only a few dark sand grains at the foot of the wall. Match the grassland rock-wall height and rock palette. No cave, rubble pile, or large boulder.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 草原側の岩壁と並べて高さを確認

### 5.8 森林地面と岩壁 `autotile_forest_rock_wall`

- PixelLabツール: `Create Tileset` → `Pro` → `Top-Down`
- 設定: 5.6と同じ。岩の主色と壁面の高さを5.6へ合わせる
- Main / Upper terrain:

```text
Solid dense charcoal-gray natural rock top, heavy and unbuildable, viewed directly from above.
```

- Second / Lower terrain:

```text
Moist dark forest floor with moss green and muted brown soil, no trees.
```

- Transition:

```text
A short exposed rocky cliff face meeting moist forest ground, with only a few moss pixels near the foot. Match the grassland rock-wall height and rock palette. No hanging roots, cave, large fern, or rubble pile.
```

- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 草原側の岩壁と並べて高さを確認

### 5.9 森林地面と砂漠 `autotile_forest_desert`（低優先）

マップ生成では極端なバイオームが直接接しにくくするが、生成結果として接触する可能性を完全に排除しない場合に用意する。

- PixelLabツール: `Create Tileset` → `Standard` → `Top-Down`
- 設定: Transition size `2`、Side wall thickness `0`、3.4の共通設定
- Inner / Lower terrain:

```text
Compact warm ochre inland desert sand with subtle clustered grains.
```

- Outer / Upper terrain:

```text
Moist dark forest floor with moss green, muted brown soil, and sparse leaf litter.
```

- Transition:

```text
A broad dry woodland transition made of sparse dark grass, exposed brown soil, dry leaf fragments, and gradually increasing warm sand. No sharp line, tree, cliff, or large plant.
```

- [ ] 必要性をマップ生成結果で確認
- [ ] 生成
- [ ] 取込
- [ ] RuleTile設定
- [ ] 全16近傍パターンを確認

## 6. 地面装飾オーバーレイ

装飾は透明背景とし、ゲームルール用の`GroundType`を変更しない。木、石などの採取資源とも別レイヤーにする。

### 6.1 草原装飾 `decor_grassland`

- 必要数: 草束2、小花2、小石2、土斑点2
- PixelLabツール: `Create tiles (Pro)` → `Tiles (variations)`
- 設定: 16×16、Square top-down、Transparent background、Outlineなし、Details Low
- Prompt:

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
- Prompt:

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
- Prompt:

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
- Prompt:

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
- 設定: 3.5の共通設定、Canvas `32×32`、Transparent background
- Prompt:

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
- 設定: 3.5の共通設定、Canvas `16×24`、Transparent background
- Prompt:

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
- Prompt:

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
- Base Prompt:

```text
A small friendly utility drone for a cozy agricultural and industrial automation game, top-down oblique pixel art on an orthogonal square grid, showing its upper surface and short front face. Rounded compact body, two small side thrusters, warm off-white painted metal with muted teal panels and one amber status light, readable mechanical silhouette. Its logical position is one 16 by 16 ground cell, but its body may extend upward into the tile above. Keep its ground projection centered near the lower center of the canvas. No isometric diamond base, humanoid limbs, weapon, text, or background. It must remain clearly readable over green grass, dark forest floor, yellow sand, blue water, and charcoal rock.
```

- Idle Animation Prompt:

```text
Create a subtle seamless four-frame idle hover loop. The drone rises and falls by at most one pixel, with a gentle alternating amber status light. Preserve body shape, direction, palette, center position, and transparent background.
```

- Move Animation Prompt:

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
- Base Prompt:

```text
A compact starting core machine for a cozy agricultural and industrial automation game, top-down oblique pixel art on an orthogonal square grid, showing its top surface and readable front face. Its base logically occupies one 16 by 16 ground cell while the upper body extends upward into the tile above. Place the ground contact point near the lower center of the canvas. Warm off-white painted metal, muted teal panels, one amber energy window, four readable logical connection sides, friendly repaired technology rather than military machinery, crisp pixel clusters, transparent background. No isometric diamond base, text, logo, weapon, painted ground tile, long shadow, or cable attached.
```

- Idle Animation Prompt:

```text
Create a subtle seamless four-frame powered idle loop. Animate only a soft amber pulse and one small mechanical breathing motion. Keep the machine footprint, ports, outline, palette, center, and transparent background perfectly stable.
```

- Active Animation Prompt:

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
- [ ] Wrap Modeを地形テクスチャは用途に応じて`Clamp`、単純な繰り返しテクスチャだけ`Repeat`へ設定
- [ ] Alpha Is Transparencyを透明背景アセットで`On`へ設定
- [ ] Sprite Mesh Typeを原則`Full Rect`へ設定
- [ ] Pivotを地面は`Center`、1×1占有オブジェクトは地面との接地点へ統一
- [ ] 各立体物の接地点、論理占有サイズ、上下左右の最大描画はみ出し量を表示定義へ記録
- [ ] SpriteRendererのSort Pointを`Pivot`にし、接地点のワールドY座標で前後関係を決定
- [ ] スプライトシートを16pxグリッドまたは制作時に定めたセル寸法で正しくSlice
- [ ] PNGと`.meta`を一緒にバージョン管理対象へ追加
- [ ] SceneまたはPrefabに意図しない全面差分がないことを確認

## 9. Unity側の表示アセット構成

推奨配置先:

```text
Assets/Farmware/Art/WorldGeneration/
├── Terrain/
│   ├── Base/
│   ├── AutoTiles/
│   └── Decorations/
├── Resources/
├── Player/
└── Nodes/
```

ファイル名は`種別_用途_差分番号.png`を基本とする。

```text
terrain_grassland_01.png
terrain_grassland_02.png
autotile_grassland_water.png
decor_forest_fern_01.png
resource_tree_basic_01.png
player_drone_idle_south.png
node_starting_core_active.png
```

現在の`ChunkTilemapPresenter`は、草原、森林、砂漠、水、岩壁にそれぞれ単一の`TileBase`を割り当てる。以下はスプライト制作だけでは完了せず、Presentation実装の拡張が必要である。

- [ ] 座標ハッシュで地面差分を決定できる
- [ ] 地形の隣接状態に応じてRuleTileまたは同等の境界タイルを選択できる
- [ ] 岩壁内部の黒塗りと露出した壁面を分離できる
- [ ] 装飾を地面と別Tilemapまたはバッチ描画レイヤーへ表示できる
- [ ] 論理占有1×1と、上方向へはみ出す描画Boundsを別々に保持できる
- [ ] 画面下側のセルに接地する物体を手前へ描くYソートが機能する
- [ ] サブチャンクとカメラのカリング範囲が最大描画はみ出し量を含む
- [ ] 資源、ドローン、開始コアをECS状態からPresentationへ反映できる
- [ ] TilemapやGameObjectをゲーム状態の正として使用していない

## 10. 検品用テストマップ

最低でも以下を1画面に配置し、採用前にスクリーンショットを残す。

- [ ] 各地形を16×16タイル以上の面積で敷き、繰り返し模様を確認
- [ ] 同じ差分が縦横に連続した場合でも継ぎ目が出ないことを確認
- [ ] オートタイルの外角、内角、細い1セル地形、孤立1セルを確認
- [ ] 草原、森林、砂漠それぞれと水域・岩壁の境界を確認
- [ ] 岩壁内部が黒く隠れ、空間へ露出した辺だけ壁面が見えることを確認
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
- [ ] 鉱山、湿地、雪原、火山、海の追加5バイオーム
- [ ] バイオーム固有の粒子、環境演出、希少資源変異

## 13. PixelLab参照資料

- [Create Tileset](https://www.pixellab.ai/docs/tools/create-tileset): 接続可能なWang tileset、16px、Top-Down、境界設定
- [Create tiles (Pro)](https://www.pixellab.ai/docs/tools/create-tiles-pro): 地面差分、パス、地形境界、建築キット
- [Create texture](https://www.pixellab.ai/docs/tools/create-texture): Tilesetと組み合わせる地表テクスチャ
- [PixelLab API](https://api.pixellab.ai/v2/docs): Map Objects、Object、Character、Animationを含むツール一覧
