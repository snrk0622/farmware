---
name: fixed-resource-prompt-dialogue
description: Farmware向けPixelLab Create S-XL Image (Pro)固定資源スプライトPromptを、ユーザーとの対話で完成させる。木・岩など施設配置を妨害する採取資源と、草など配置時に自動除去される景観資源を分類し、Output size、採取後などの状態、全バリエーションの積雪版、輪郭、接地点、禁止事項を決めるときに使う。動物、地形Base、アニメーション、画像生成やUnityへの取り込み自体には使わない。
---

# 固定資源Prompt対話作成

Farmwareの静止した固定資源について、初心者でも答えやすい短い対話から、PixelLabへ一度だけ入力できる英語Promptを作る。

## 最初に読む資料

1. Farmwareリポジトリ内で作業しており、`Docs/Art/TilemapSpriteProductionChecklist.md`が存在する場合は、同文書の「固定資源スプライト共通フォーマット」を読む。これを最新の正とする。
2. リポジトリ文書を参照できない場合、または分類別テンプレートを確認するときは、[references/template.md](references/template.md)を読む。

文書と参照ファイルが異なる場合は、Farmwareリポジトリの文書を優先する。差異が完成Promptへ影響する場合だけ、ユーザーへ短く知らせる。

## 対話の進め方

- 会話内ですでに確定している値を再質問しない。
- 未確定項目を一度に並べず、原則として一問ずつ日本語で質問する。
- ユーザーが「おまかせ」と答えた場合は、対象を読み取りやすくする最小限の内容を選ぶ。
- 必要情報が最初から揃っている場合は、確認質問を省略して完成結果を出す。

次の順で不足情報を集める。

1. `Resource`と、必要なら安定した`Resource ID`。
2. `Placement Class`。次のどちらか一つを選ぶ。
   - `blocking_resource`: 木や岩など、同じタイルへの施設配置を妨害し、実際に採取・利用される資源。
   - `auto_clear_scenery`: 草など、機能を持たず、同じタイルへ施設を配置すると自動除去される景観資源。
3. PixelLabで選ぶ`Output size`と論理占有タイル数。Create S-XL Image (Pro)が返す総フレーム数は`Output size`の最大辺から自動計算し、ユーザーには質問しない。`Output size`と総フレーム数は設定一覧と計算には使うが、完成Prompt本文には絶対に書かない。立体物では、地面との接地点、上方向や隣接セルへの描画上のはみ出しも確認する。
4. `Resource States`。`blocking_resource`では、通常状態に加えて採取後など、その資源で必要になり得る静止状態をスキル側から先に提案し、枚数への影響を示して採否を確認する。`auto_clear_scenery`は通常状態だけとし、状態を質問しない。
5. `Resource Features`。材質、色、形、構成要素など、対象を判別するために必要な特徴だけを扱う。
6. `State Requirements`。採用した各状態を、アニメーション途中ではなく一枚で読める完成した静止状態として記述する。
7. `Variation Rules`。論理占有、接地点、主要配色を保ったまま、基本バリエーション間で変えてよい形状、傾き、色幅などを決める。
8. `Resource-specific Prohibitions`。誤生成されやすい物、地面、容器、文字などを追加する。

## 分類とアウトライン

分類は見た目だけでなくゲーム上の施設配置ルールを表す。判断できない場合は、採取・利用され、配置前に明示的な除去を要するものを`blocking_resource`、純粋な景観で配置時に消えてよいものを`auto_clear_scenery`とする。二つを同じPromptに混在させない。プレイヤーが配置する床、道、模様、舗装、耕地は固定資源に含めない。

### `blocking_resource`

- 地面から露出している外周へ、ソフトブラック`#333333`の不透明な実線アウトラインを付ける。
- 地面との接地点にはアウトラインを付けない。幹や岩の底に黒い横線、台座、囲み枠を作らない。
- 接地点を出力枠の下中央付近へ置き、論理占有セルを読み取れるようにする。

### `auto_clear_scenery`

- 外周、内部、地面との接点を含め、アウトラインを一切付けない。
- 施設配置時に消えても不自然にならない、小さく簡潔な景観要素として描く。
- 背景へ完全に埋没しない範囲で、採取資源より控えめなシルエットとコントラストにする。

## 固定資源の状態提案

`blocking_resource`の`Output size`が決まった後、通常状態以外に必要な静止状態を検討する。

- 資源の採取や除去後にもオブジェクトが残る場合だけ、0～2件の追加状態を提案する。
- 例として木には`normal`と`after_harvest`（切り株）、採掘後も残骸が残る岩には`normal`と`depleted`を提案できる。
- 成長途中、揺れ、採取モーション、破片が飛ぶ瞬間などのアニメーションフレームは提案しない。
- ゲーム仕様に存在しない破損、枯死、再成長、品質段階を暗黙に追加しない。
- 追加状態ごとに英語の`State ID`と日本語の説明を示し、状態候補の組み合わせごとに積雪版を含む`Outputs per Base Variation`、`Base Variation Count`、`Unused Frames`を示す。
- 提案後、採用する状態一覧を一問で確認する。ユーザーの選択前に確定しない。

`auto_clear_scenery`は施設配置時に即時除去されるため、状態は`normal`だけとする。

## Create S-XL Image、計算、配置

- 両方の`Placement Class`でPixelLabの`Create S-XL Image (Pro)`を使用する。
- `Max Dimension = max(Output Width, Output Height)`として、次のPixelLab公式サイズ帯から`Total Frames`を決める。
  - `16～42 px`: 64 frames。
  - `43～85 px`: 16 frames。
  - `86～170 px`: 4 frames。
  - `171 px以上`: 1 frame。上限はPixelLabが各アスペクト比で許可する最大寸法に従う。
- `256 ÷ Output Width`や`256 ÷ Output Height`では計算しない。表示上のgridと非正方形の配置はPixelLab出力に従う。
- 全固定資源で`Environment Versions = standard, snow-covered`の2種類を必須とする。
- `Outputs per Base Variation = State Count × 2`。
- `Base Variation Count = floor(Total Frames ÷ Outputs per Base Variation)`。
- `Unused Frames = Total Frames - Base Variation Count × Outputs per Base Variation`。
- `Total Frames < Outputs per Base Variation`の場合は、一度の生成で完全な基本バリエーションを作れない。完成Promptを出さず、より小さい`Output size`または状態数の削減をユーザーへ提案する。
- 各基本バリエーション内では、`state 1 standard, state 1 snow-covered, state 2 standard, state 2 snow-covered...`の順で一まとまりにする。状態別や積雪有無別に全基本バリエーションをまとめない。
- 余りフレームがある場合は末尾だけを透明な未使用フレームにする。採用する全状態と積雪有無の枚数比率は均等にする。
- `Output size`、1Spriteの画像寸法、grid、総フレーム数、基本バリエーション数、grid上の行列配置はPrompt本文へ書かない。基本バリエーション内の状態と環境版の順序は書く。

例として`Output size: 32×32`は最大辺32 pxなので総出力64 framesとなる。木で`normal`と`after_harvest`の2状態を採用すると、1基本バリエーション当たり`2状態 × 2環境版 = 4 frames`、16基本バリエーション、未使用0 framesとなる。これは寸法や状態数の固定規則ではない。

## 積雪版の共通規則

- すべての基本バリエーションと採用状態に、`standard`と`snow-covered`のペアを用意する。
- 積雪色は全固定資源で、明部`#EDF3F2`、影色`#C5D6DE`の2色に固定する。純白は使用しない。
- 積雪は上向きの面へ薄く載せ、左上を明部、右下の縁を影色にする。元の材質と主要色が十分に見える量に留める。
- ペア内では、積雪以外の形、状態、材質、色、構成要素、接地点、縮尺、画角、輪郭を完全に一致させる。
- 雪原の地面、雪だまり、降雪粒子、雪片、吹雪、氷柱、足跡をSpriteへ追加しない。
- `blocking_resource`の積雪版でも露出外周だけを`#333333`で囲み、地面との接地点にはアウトラインを付けない。
- `auto_clear_scenery`の積雪版にもアウトラインを付けない。

## 全固定資源で統一する画風

- 見下ろし斜めのorthogonal square grid向けPixel Artとし、isometric diamond baseにはしない。
- cozy agricultural automation game向けの明るく親しみやすい雰囲気にする。
- 広く読みやすい色面、簡略化した形、crisp native pixels、左上光源を使う。
- アンチエイリアス、半透明の輪郭、地面Tile、草の台座、投影影、背景、風景、文字を含めない。
- 背景は透明にし、1枚ごとに一つの独立した静止Spriteとして生成する。
- 全状態と全バリエーションで用途、論理占有、接地点、縮尺、画角、光源方向、輪郭規則、主要パレットを揃える。状態による形の変化は`State Requirements`で明記した部分だけに限定する。
- 顔、目、手足、擬人化表現は、対象の仕様として明示された場合を除いて追加しない。

## 完成時の出力

すべての値が揃ったら、次を日本語で簡潔に出力する。

1. `設定`: Resource、Resource ID、Placement Class、PixelLab tool、Output size、最大辺、総フレーム数、Resource States、Environment Versions、1基本バリエーション当たりの出力数、基本バリエーション数、未使用フレーム数、論理占有、アウトライン規則。
2. `PixelLab Prompt`: 選択した分類に対応する英語の完成Promptを、一つの`text`コードブロックで出す。

完成Promptでは次を必ず満たす。

- 角括弧、TODO、未解決の選択肢を残さない。
- 一度の生成で、全基本バリエーション、採用した全状態、それぞれの通常環境版と積雪版を作るよう明記する。
- 各基本バリエーションの全状態と積雪有無を一まとまりにし、種類ごとに全基本バリエーションをまとめない。
- バリエーションや状態をアニメーションフレームとして扱わない。
- `Output size`、1Spriteの画像寸法、grid、総フレーム数、基本バリエーション数、grid上の行列配置をPrompt本文へ書かない。基本バリエーション内の状態と環境版の順序は含める。
- 積雪版へ固定色`#EDF3F2`と`#C5D6DE`を使用し、対応する通常環境版から積雪以外を変えないよう明記する。
- `blocking_resource`には、露出外周の`#333333`アウトラインと、地面との接地点にアウトラインを付けない指示を含める。
- `auto_clear_scenery`には、どの部分にもアウトラインを付けない指示を含める。
- 共通画風、透明背景、地面Tileなし、投影影なしの規則を含める。

Promptの作成だけが依頼されている場合、画像生成、ファイル編集、UnityへのImport、commitは行わない。
