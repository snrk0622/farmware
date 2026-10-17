# 固定資源Promptテンプレート参照

## 角括弧

- `[RESOURCE]`: 資源または景観要素の英語名。
- `[ALL OUTPUTS IN ORDER]`: 各状態について`standard, snow-covered`を並べた一覧。
- `[PLACEMENT]`: 論理占有、接地点、描画上のはみ出し。Output sizeや画像寸法は書かない。
- `[RESOURCE FEATURES]`: 材質、配色、形など、対象を判別する特徴。
- `[STATE REQUIREMENTS]`: 採用した状態それぞれの静止画としての見た目。
- `[VARIATION RULES]`: 基本バリエーション間で変えてよい内容と維持する内容。
- `[RESOURCE-SPECIFIC PROHIBITIONS]`: 対象固有の禁止事項。不要なら文ごと削除する。

PixelLab Create Imageのキャンバスは256×256 px固定であり、gridと総出力数は`Output size`から決まる。`Output size`、1Spriteの寸法、grid、総出力数、基本バリエーション数、セル順は角括弧にもPrompt本文にも入れない。

## 施設配置を妨害する採取資源

`blocking_resource`へ使用する。

```text
Create one organized set of static pixel-art sprites of [RESOURCE] for a cozy agricultural automation game. These are functional harvestable resources that block facility placement until removed. Use one generation to create every available base variation, every selected resource state, and both environment versions of every state.

For every base variation, create one complete output set in this order: [ALL OUTPUTS IN ORDER]. Keep each base variation's complete output set together. Do not group all base variations by state or by environment version. Fill the available output with as many complete base variations as possible. If the available cells do not divide evenly by the complete output-set size, leave only the final remainder cells transparent and unused.

[PLACEMENT] Show the object in a top-down oblique view for an orthogonal square grid, not on an isometric diamond base. [RESOURCE FEATURES] [STATE REQUIREMENTS] Each selected state is a finished static gameplay state, not an animation frame or an in-between action.

For every standard sprite, create a matching snow-covered sprite of the exact same base variation and resource state. Change only the added snow. Keep the underlying shape, state, materials, colors, component positions, logical footprint, ground-contact point, scale, viewing angle, lighting direction, outline treatment, and main palette identical within each pair. Place a thin readable snow cap only on upward-facing surfaces, using exactly pale blue-white #EDF3F2 for lit snow and pale blue-gray #C5D6DE for snow shadow. Put the lighter snow toward the upper-left and the shadow color along the lower-right snow edge. Leave enough original material visible. Do not recolor the whole object as snow.

Use a solid opaque soft-black #333333 outline only around the exposed outer silhouette in both environment versions. Do not draw an outline along the edge where the resource touches the ground. Do not add a black baseline, pedestal, enclosed base, ground tile, grass base, or cast shadow. [VARIATION RULES] Keep the same purpose, state meanings, logical footprint, ground-contact point, scale, viewing angle, lighting direction, outline treatment, and main palette across every base variation.

Use broad readable color areas, simplified compact forms, crisp native pixels, upper-left lighting, and a transparent background. No snowfield ground, snow pile at the base, falling snow, snowflake, blizzard, icicle, footprint, scenery, text, border, anti-aliasing, semi-transparent outline, face, eyes, limbs, personification, unlisted state, animation sequence, repeated duplicate, or inconsistent design. [RESOURCE-SPECIFIC PROHIBITIONS]
```

## 施設配置時に自動除去される景観資源

`auto_clear_scenery`へ使用する。状態は`normal`だけとする。

```text
Create one organized set of static pixel-art sprites of [RESOURCE] for a cozy agricultural automation game. These are small non-functional scenery elements that are automatically removed when a facility is placed on the same tile. Use one generation to create every available base variation and both environment versions.

For every base variation, create one complete output set in this order: normal standard, normal snow-covered. Keep each base variation's two environment versions together. Do not group all base variations by environment version. Fill the available output with as many complete base variations as possible. If the available cells do not divide evenly by two, leave only the final remainder cells transparent and unused.

[PLACEMENT] Show the element in a top-down oblique view for an orthogonal square grid, not on an isometric diamond base. [RESOURCE FEATURES]

For every standard sprite, create a matching snow-covered sprite of the exact same base variation. Change only the added snow. Keep the underlying shape, materials, colors, component positions, logical footprint, contact point, scale, viewing angle, lighting direction, no-outline treatment, and main palette identical within each pair. Place a thin readable snow cap only on upward-facing surfaces, using exactly pale blue-white #EDF3F2 for lit snow and pale blue-gray #C5D6DE for snow shadow. Put the lighter snow toward the upper-left and the shadow color along the lower-right snow edge. Leave enough original material visible. Do not recolor the whole element as snow.

Use no outline anywhere in either environment version: no outer outline, internal outline, contact outline, border, or black baseline. Do not add a pedestal, enclosed base, filled ground tile, grass base, or cast shadow. [VARIATION RULES] Keep the same scenery role, logical footprint, contact point, scale, viewing angle, lighting direction, no-outline treatment, and main palette across every base variation.

Use broad readable color areas, simplified compact forms, crisp native pixels, upper-left lighting, restrained silhouette and contrast, and a transparent background. No snowfield ground, snow pile at the base, falling snow, snowflake, blizzard, icicle, footprint, scenery beyond the requested element, text, border, anti-aliasing, face, eyes, limbs, personification, extra state, animation sequence, repeated duplicate, or inconsistent design. [RESOURCE-SPECIFIC PROHIBITIONS]
```

## 最終確認

- 角括弧が0個である。
- 分類が`blocking_resource`または`auto_clear_scenery`の一方だけである。
- 一度の生成で、全基本バリエーション、全採用状態、各状態の通常環境版と積雪版を生成する。
- 各基本バリエーションの全出力を一まとまりにし、状態別や積雪有無別にまとめていない。
- Prompt本文に`Output size`、1Spriteの寸法、grid、総出力数、基本バリエーション数、セル順がない。
- 積雪版が通常環境版と対になり、積雪以外の形、状態、材質、色、構成要素、接地点、縮尺、画角、輪郭が一致する。
- 雪は`#EDF3F2`と`#C5D6DE`だけを使い、上向きの面へ薄く載っている。
- `blocking_resource`では露出外周だけが`#333333`で、地面との接地点にアウトラインがない。
- `auto_clear_scenery`では通常環境版と積雪版のどちらにもアウトラインがない。
- 透明背景で、地面Tile、草の台座、投影影、雪原地面、降雪、風景、文字がない。
