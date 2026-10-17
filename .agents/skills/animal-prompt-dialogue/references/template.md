# 動物Promptテンプレート参照

## 角括弧

- `[ANIMAL]`: 動物種の英語名。
- `[ALL POSES IN ORDER]`: `idle, move, eat, sleep`の後ろへ動物種固有ポーズを追加した一覧。
- `[GROUND-CONTACT ALIGNMENT]`: 全フレームで固定する論理接地点の相対位置。通常は出力枠の下中央付近とし、Prompt本文へOutput sizeや数値のピクセル座標は書かない。
- `[SPECIES FEATURES]`: 種を判別するための身体特徴。
- `[SPECIES-SPECIFIC POSE REQUIREMENTS]`: 体型に合う共通・固有ポーズの説明。
- `[SPECIES-SPECIFIC PROHIBITIONS]`: 種固有の禁止事項。不要なら、この角括弧プレースホルダーだけを削除する。同じ段落にある直前の共通禁止文は必ず残す。

PixelLab Create S-XL Image (Pro)の総フレーム数は、`Output size`の最大辺に応じて64、16、4、1 framesのいずれかになる。`Output size`、1Spriteの寸法、出力シートのgrid寸法、総フレーム数、基本バリエーション数、出力シート上の行列配置は角括弧にもPrompt本文にも入れない。画角を示す`orthogonal square grid`と、基本バリエーション内のポーズ順序はPromptへ含める。

## 1回生成用Prompt

```text
Create one organized set of pixel-art sprites of a small [ANIMAL]. Use one generation to create every available base variation and every required pose.

For every base variation, create one complete pose set in this order: [ALL POSES IN ORDER]. Keep each variation's complete pose set together. Do not group all variations by pose. Fill the available output with as many complete base variations as possible. If the available frames do not divide evenly by the pose count, leave only the final remainder frames transparent and unused.

View every animal from a left-facing three-quarter side angle for an orthogonal square grid, much closer to a side view than a front view. Keep both the head and body facing toward the left side of the screen. The head may turn only slightly toward the viewer to keep the animal's facial features readable, but never point the face directly toward the viewer and never use a straight-on front view. Keep the silhouette side-dominant. [SPECIES FEATURES] [GROUND-CONTACT ALIGNMENT] Align every base variation and every pose to this fixed logical ground-contact anchor at the same pixel coordinate, and keep the same frame-space position. Do not independently center, vertically shift, or horizontally shift a pose because its silhouette is lower, taller, narrower, or wider. Each base variation may differ in coat color, markings, or minor body proportions. Within the same variation block, keep the exact same facial expression, colors, marking shapes and positions, body proportions, viewing angle, facing direction, scale, palette, ground-contact anchor, frame-space position, and solid soft-black #333333 outline, except for the required eye closure in sleep. Change only the pose between frames, plus this required sleep eye closure. Use vertically elongated solid-black eyes in every pose except sleep. Close both eyes in the sleep pose; this is the only allowed eye-shape change within a base variation.

Pose requirements: idle is a calm neutral pose; move is one readable in-progress movement pose toward the left; eat is a natural feeding pose without drawing food, grass, a container, or a ground tile; sleep is a natural resting pose with both eyes closed and without bedding, a speech bubble, or a Z symbol. [SPECIES-SPECIFIC POSE REQUIREMENTS]

Cheerful compact pixel-art animals for a cozy agricultural automation game. Broad readable color areas, simplified anatomy, crisp native pixels, upper-left lighting, transparent background. No bell, hat, bandana, collar, decorative accessory, ground tile, cast shadow, scenery, text, motion blur, repeated pose, missing pose, animation sequence, or inconsistent character design within a variation. [SPECIES-SPECIFIC PROHIBITIONS]
```

## 最終確認

- 角括弧が0個である。
- `Output size`の最大辺から決めた総フレーム数、完全な基本バリエーション数、未使用フレーム数に矛盾がない。
- 全基本バリエーションに同じポーズ一覧が一度ずつ存在する。
- 全基本バリエーションと全ポーズで論理接地点が同じピクセル座標にあり、ポーズごとの中央揃えや上下左右の位置ずれがない。
- Prompt本文に`Output size`、1Spriteの寸法、出力シートのgrid寸法、総フレーム数、基本バリエーション数、出力シート上の行列配置が含まれていない。画角を示す`orthogonal square grid`と、基本バリエーション内のポーズ順序は含まれている。
- 種固有の禁止事項がない場合も、共通禁止文が残っている。
- Prompt本文で、すべての動物を正面より横向きに近い左向きの斜め横から描き、頭と胴体をともに画面左へ向け、頭をカメラ正面へ向けないよう指定している。
- Prompt本文に右向き、または向きを選択させる指示が含まれていない。
- Prompt本文で、通常ポーズの目を黒い縦長にし、`sleep`だけは両目を閉じるよう指定している。
