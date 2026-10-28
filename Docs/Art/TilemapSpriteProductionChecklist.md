# Farmware タイルマップ用スプライト制作・導入チェックリスト v0.30

- 作成日: 2026-10-11
- 最終更新日: 2026-10-25
- 状態: TERRAIN-01C6前の制作・実装履歴。新規地形制作の手順書として使用しない
- 対象: 地形・資源・生物、直接操作プレイヤー、海中・深海、施設、乗り物、補助物流
- 関連仕様: [ランダムマップ生成仕様](../GameSpecification/RandomMapGenerationSpecification.md)
- 関連仕様: [ゲームコンセプト仕様](../GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- 基準解像度: 1タイル = 16×16 px、Gameビュー = 480×270 px

> **現行地形仕様への注意（2026-10-24）**
>
> 本書のGround 16×24、Wall 16×28、不透明な液体、オートタイルを使わない方針は、TERRAIN-01C6前の実装記録である。14形状の白い境界ハイライトも比較履歴として残る旧方式であり、製品表示には採用しない。TERRAIN-01C6以降の地形制作では、[地形タイル表現仕様](./TerrainTileArtSpecification.md)を正とする。本書は既存アセットの由来と、地形以外の制作候補を確認する資料として保持し、その他の旧地形要件を新素材へ引き継がない。
>
> 現行の材質タイルはAsepriteまたはPixeloramaで1 px単位に手描きし、`GroundTop`、任意の透明な`FaceBoundaryOverlay`、`Face`の三部構成とする。Wall上面専用Spriteは作らず、同じ材質の`GroundTop`を0.85で暗色化して表示する。各部位は固定差分数ではなく可変長配列へ必要な数だけ登録し、配列内の全候補を均等確率で選ぶ。個別Weightや出現率Overrideは持たせない。`Face`の全差分は任意の上下左右組合せで接続できるようにする。断面は境界の有無にかかわらず1段目から`Face`を使う。境界ありではEditorがGround用の元色Overlay合成結果と、Wall用にOverlayだけを暗色化した合成結果を生成し、Runtimeは用途に応じた完成Spriteを1枚描画する。Wallの識別には0.85暗色化、`Face`、左右各1px・手前3px・各レイヤーAlpha 22%の固定接地影、設置面起点の`Shadow Caster 2D`動的影を使い、14形状の境界ハイライトは製品表示へ使用しない。材質が異なるWall同士も接続扱いとし、共有辺へ固定接地影・動的影・`Face`の切れ目を出さない。差分選択はワールド位置から決定論的に行う。Ground崖面とWall前面は同じ`Face`とOverlay入力を使うが、境界の合成結果はGround用とWall用に分かれる。41材質の境界有無は今回の64×48 px制作画像を正とする境界あり11材質・なし30材質で確定する。本書のPixelLab必須、境界なし、旧一体型寸法の記載は現行制作へ引き継がない。材質タイルにはPixelLabを使用しない。

> Wall影の専用比較Sceneでは、本番想定に合わせて16×16 pxのWall前面を縦2段で表示する。1段目だけにWall用`FaceBoundaryOverlay`合成結果、2段目に通常`Face`を使う。比較結果はC（固定接地影＋動的影）を採用した。固定接地影は上下段の継ぎ目に置かず、2段目の下端だけに置く。
>
> 41材質分の`GroundTop`色・使用比率・模様方針とWall表示時の暗色化規則は、地形タイル表現仕様第7章に従う。`FaceBoundaryOverlay`と`Face`は、まず`GrassLoam`で上面より暗く、部位内コントラストを抑えた配色を試す。上面と同じ色相へ統一せず、GrassLoamの`Face`は茶色い土層を維持する。Gameビュー確認後に同じ明度関係を残り40材質へ適用する。実Spriteで調整が必要になった場合は、個別Spriteだけを変更せず、基準表を先に更新する。

## 1. この文書の目的

2026-10-19の仕様転換に伴い、ドローン・開始コアの制作計画を撤回し、第7.5・7.6節をプレイヤー・水中・乗り物へ置換した。既存のART-00受け入れ、16pxセル、土地16×24、岩壁16×28、接地点・Atlasの規則は再利用する。

第4章等の7枚のBiome土地と単一岩壁はART-00時点の制作・受け入れ記録である。C5-Cで旧7Biome用土地はSceneとAtlasから取り外し、未制作の新18Biomeそれぞれに専用の仮色Tileを割り当てた。これらはC5-C分類の識別と欠落検出のための暫定表示であり、本番素材の制作完了を意味しない。今後は固定BiomeとBlock素材を分け、同Biome内にも複数地面・壁を表示する。新しい通常表示はBlock定義を選択キーにし、「Biomeだけで常に同じTile」というC6前の暫定規則を置換する。意味のないランダム模様を避ける方針は維持するが、異なる材質の識別を禁止しない。各地形材質は共通の所持品から設置先に応じてGround形態またはWall形態になるため、自然生成パレットに片方しか現れない材質も両形態のSpriteを制作対象とする。地形ブロックItemは再配置専用であり、クラフトに使う石材・鉱石等の資源Itemとは別物として制作する。同名材質でも、地形ブロックのItemアイコンと資源Itemアイコンを色だけの差分にせず、輪郭、割れ方、粒度または鉱脈表現で判別できるようにする。

積雪版は雪原バイオームや地域の雪表現の素材であり、四季の追加を意味しない。夜は環境光、海中はLayerと水の表現を使い、夜だけの視界半径制限を画像で付けない。本書の改訂だけでScene・素材・Import設定を変更したとは扱わない。

旧方式でPixelLab中心に制作した製品用スプライトの画風、生成、Unityへのインポート、表示設定、実機確認を履歴として記録する。現行の材質タイル制作手順には使用しない。

Editorの構築メニューが `Assets/Farmware/Art/WorldGeneration/Fallback/` へ生成する、上面をBiome別の仮色一色で塗った18種類の16×24土地Tile、3種類の16×16液体Tile、1種類の16×28岩壁Tile、3種類の資源Tileは表示を成立させるための仮素材である。液面模様は製品Shaderへ置き換え、旧4フレームAnimatedTileは廃止した。新18Biome土地の本番素材はまだなく、SampleSceneと`GroundTerrainAtlas`は18枚の仮色Tileを一対一で参照する。未制作Biomeを旧草原・森林等の近似Tileへフォールバックしない。本書のチェックは、本番候補へ置き換えた素材から付け、仮素材が存在するだけではチェックしない。

2026-10-26時点で、`ChunkTilemapPresenter`には上面16 pxと断面8 pxを結合した土地、上面16 pxと側面12 pxを結合した岩壁、土地より8 px下へ投影する静止液面、岩壁上資源を16×16サブチャンク単位で表示・消去する旧Scene経路が残っている。C6-C3で表示入力は`GeneratedTerrainCell`のGround、Wall、Liquidへ統一した。C6-D2では、完全な41材質製品カタログと専用3 Tilemapを割り当てた場合だけ、同じDirtyサブチャンク、カリング、Floating Originへ16×16の`GroundTop`、Ground崖面3段、Wall前面2段、乾燥穴の48 px減衰と深部黒を接続する製品経路を追加した。SpriteとTintの組を共有Tileへキャッシュし、セルごとのSpriteやGameObjectは作らない。41材質SpriteとAtlasを製品Sceneへ割り当てるC6-D6まではSampleSceneを旧表示のまま維持し、不完全な製品カタログを暫定Biome Tileへフォールバックさせない。旧経路の断面は隣接判定で出し分けず、画面手前の一体型ブロックが奥側の断面を隠す。`Build Fallback Map Prototype`を実行すると旧経路に必要なレイヤーと仮素材を再生成し、仮色土地と岩壁をSampleSceneへ再接続できる。

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

以前に生成した草原、森林地面、砂漠、淡水面、岩壁上面は、素材間で配色、コントラスト、輪郭方針が揃っていなかったため、製品候補としては採用せず一から生成し直す。
本番スプライトへの差し替えは、すべての素材、境界、アニメーションを完成させてから一度に確認するのではなく、次の3段階で行う。
すべての段階で本番候補の可視ピクセルはPixelLabから生成し、採用画像、Seed、設定を制作記録へ残す。Style referenceに対応するツールでは、使用した参照画像も記録する。

#### 段階0: マスタースタイルの固定

最初に草原だけを生成し、16×24 pxの上面16 pxと断面8 pxで使用する代表色、断面色、コントラストの基準を決める。
草原を採用するまでは、森林、砂漠、水面、岩壁などの量産へ進まない。

- [x] 草原を1枚生成し、設備の背景として十分に静かであることを確認
- [x] 同じ草原Tileを連続配置し、タイル境界や目立つ反復模様が見えないことを確認
- [ ] 木、複数タイル設備、ベルト、柵、施設領域表示を重ねられる背景密度であることを確認
- [ ] 採用した草原画像、Seed、PixelLab設定をマスタースタイルとして制作記録へ登録
- [ ] 以降の全地形で、採用した草原画像をStyle referenceへ指定

草原以外の通常土地はBiome代表色と断面色だけを変更し、単色面、輪郭、寸法の方針を変更しない。
通常土地Baseへ材質模様やランダムな色むらが生成された場合は採用せず、単色面になるよう修正または再出力する。

#### 段階A: 全体配色とシルエットの確認

ランダムマップ全体を本番候補の色と形で埋め、バイオームの識別、画面の情報量、資源の可読性を先に判断する。
この段階では現在の`ChunkTilemapPresenter`が扱える各分類1枚のTileを使用する。断面はフォールバック素材で先に表示できるが、本番候補の完成条件には含めない。

- [x] 草原、森林、砂漠、岩地、湿地、雪原、火山の代表地面を各1枚選定
- [ ] 淡水、海水、マグマ、岩壁上面の代表地形を各1枚選定
- [ ] 木、石、砂資源の代表スプライトを各1枚選定
- [ ] 代表地形11種類と資源3種類をUnityへ取り込み、既存の全分類へ割り当て
- [ ] 原点付近、海岸、川、岩壁、火山、雪原を含む複数地点でGameビューを撮影
- [ ] 480×270の実表示で、地形同士と資源の識別性、彩度、明度、単色面の読みやすさを確認
- [x] F4境界表示を一時的に重ね、16×16サブチャンクと64×64チャンクをまたぐ表示欠けがないことを確認

段階Aでは、通常土地や液体で同じTileの意図しない繰り返しが見えることを既知の未完成項目として扱う。岩壁上面の菱形模様はセル単位で反復させる固定意匠であり、この未完成項目には含めない。基本地形同士の直線的な切り替わりと、島や岸の直角を含む単純な輪郭は完成方針の一部として評価する。
ここで評価するのは、Foragerのように明るく即座に地形を識別できる配色、工業設備を置ける背景密度、バイオームの見分けやすさである。ただし既存作品のSprite、地形配置、固有意匠は複製しない。

##### ART-00A 受け入れ記録（2026-10-15）

- [x] `terrain_biomes_base.png`を112×24 px、左からBiome ID順の16×24 px×7枚として取り込んだ
- [x] 土地SpriteをPPU 16、Point、Mip Maps Off、Compression None、Clamp、Full Rect、Pivot `(0.5, 0.6666667)`へ設定した
- [x] 7枚の同名Tileを作成し、SampleSceneのBiome参照をフォールバック土地から本番用土地へ置き換えた
- [x] 7枚を`GroundTerrainAtlas`の同じTextureページへ収録し、岩壁用Atlasと分離した
- [x] EditMode 16件とPlayMode 25件でImport、Slice、Tile、Atlas、Scene参照、Biome割り当てを確認した
- [x] Gameビューで継ぎ目、断面の重なり、にじみ、ちらつき、サブチャンク・チャンク境界の表示を確認した
- [x] SimulationのBiome分類、生成規則、配置可否を変更していない

#### 段階B: 製品表示としての接続と反復の確認

段階Aで採用した色と形を基準に、16×24土地と16×28岩壁の一体型ブロック、Shaderによる液面模様、装飾レイヤーを追加する。
ロードマップ上の`ART-00B`と`ART-00C`は完了している。`ART-00B`は2026-10-15に、関連EditMode 23件とSampleScene PlayMode 25件の成功、Consoleエラー0件をAIが確認し、14形状と各回転、斜め内角、土地と岩壁の表示高さ、チャンク往復後の残留がないことを含むGameビュー確認を開発者が承認した。`ART-00C`はPR #4（merge commit `8a69fe9d`）で本番PNG、専用Tile、Atlas、SampleScene、再構築処理、関連テストを導入し、2026-10-16に岩壁の側面遮蔽、表示高、下層土地断面、にじみ、チャンク境界を開発者がGameビューで確認して問題なしと承認した。

- [ ] 全11種類のBase地形を各1枚用意し、同じ分類では常に同じTileを表示
- [x] 通常地形は上面16 pxと断面8 pxを一枚にし、手前の土地が奥側の断面を自然に隠す
- [x] 岩壁は上面16 pxと側面12 pxを一枚にし、手前の岩壁が奥側の側面を自然に隠す
- [x] 岩壁セルでも下層の16×24土地ブロックを残し、水・マグマに面した場所で土地断面が欠けない
- [ ] 水面・マグマの岸の位置を固定したまま、内部の波紋と明暗だけをアニメーション
- [ ] 資源、植生、動物、地形障害物など、配置に意味を持つ要素を別レイヤーへ追加
- [x] 水・マグマに接する土地の外周辺・内角と、岩壁領域の外周辺・内角へ、白い共通デザインの境界ハイライトを透明な専用オーバーレイとして追加
- [ ] 境界の欠け、ちらつき、透明な隙間、位置ずれによるTilemapの継ぎ目がないことを確認。岩壁上面の菱形模様とセル単位の反復は意図したデザインとして許容する

本番候補は`Assets/Farmware/Art/WorldGeneration/Fallback/`へ上書きしない。
このフォルダは構築メニューが再生成する仮素材専用であるため、本番候補は9章の推奨配置先へ追加し、採用後にSceneまたは表示設定から参照する。

### 1.3 初期制作数と制作順

最初から全素材を同時に生成しない。地形素材では前段階の採用画像を次段階のStyle referenceへ使用し、前面断面の生成でStyle referenceを使えない場合は採用色と形状ルールをプロンプトへ引き継ぐ。

| 優先度 | 素材 | 内訳 | 初期必要数 |
|---|---|---|---:|
| 0 | 草原マスタースタイル | 設備の背景として静かな基準Tile | 1 Sprite |
| 1 | 残りのBase地形 | 森林、砂漠、岩地、湿地、雪原、火山は16×24、淡水、海水、マグマは16×16、岩壁は16×28。各1枚 | 10 Sprite |
| 2 | 土地・岩壁境界ハイライト | 土地上面と岩壁上面で共有する16×16の透明素材。14形状を90度回転して47接続状態へ展開 | 14 Sprite |
| 3 | 基礎資源 | 木、石、砂資源を各3基本バリエーション。各基本バリエーションに通常版と積雪版を用意 | 18 Sprite |
| 4 | 直接操作プレイヤー | 第7.5節。操作試作後に方向・状態・装備差分を決定 | 未定 |
| 5 | 穴・追加Block・水中・乗り物 | 第7.6節。機能の縦切りごとに制作 | 未定 |

現行の静止素材小計はBase11＋境界14＋基礎資源18＝43点とする。プレイヤー、追加Block、水中、乗り物が未確定のため、製品全体の必要合計は算出し直す。基礎資源の状態を一つ増やす場合は3基本バリエーション×通常版・積雪版の6枚を追加する。境界14枚は土地・岩壁で共有し、透明状態は配置しない。
最初の世界観確認に必要な最小セットは、11地形の代表各1枚と、木・石・砂資源の代表基本バリエーションについて通常版・積雪版を各1組用意した6枚の、合計17点である。

Base地形は合計11種類×1枚の11 Spriteとなる。段階Aの世界観確認では各地形をUnityへ接続し、配色と情報量を確認する。
土地と岩壁の断面は各Base地形へ最初から含める。Base Spriteと前面断面には外角、内角、左右の側面、孤立形状を制作せず、内角を含む接続表現は5.2の境界ハイライト専用オーバーレイだけで扱う。

次の表現には、初期段階で専用Spriteを制作しない。

- 基本地形同士の境界: Base Tileを直接隣接させ、材質を混ぜる接続Spriteは制作しない。
- 淡水、海水、マグマ同士の境界: 低地同士としてBase Tileを直接隣接させる。
- 水際の接触影: 初期段階では専用効果を作らず、前面断面の下端色だけで高さを示す。
- マグマの焦げと照り返し: 初期段階では作らず、必要性をGameビューで確認してから追加する。
- 岩壁内部影: 既存仕様の連続アルファMeshを使用し、手前側の岩壁側面とは別に扱う。

## 2. 共通アート方針

### 2.1 視点と形状

- Foragerのように、正方形グリッド上で島や地形の輪郭を直角かつ単純に読める斜め見下ろし表現を使用する。アイソメトリックの菱形グリッドにはしない。
- 地面は上面16×16 pxと前面断面16×8 pxを縦に結合した16×24 pxで描く。論理グリッドは正方形のまま、断面だけをセル直下へはみ出させる。
- 淡水、海水、マグマは16×16pxの正方形を維持する。製品液面と模様は専用Tilemapを論理セル原点に固定し、奥岸上端3pxだけShaderで切り抜いて低く投影する。模様は水面・マグマごとの専用Shaderで動かす。
- 岩壁は上面16×16 pxと側面16×12 pxを縦に結合した16×28 pxとする。上面16 px：側面12 pxを1：0.75の基準比率とし、Sprite Pivotで基準面から上へ積まれた一段を表す。
- 通常地面のBase Spriteには側面、外周線、タイル境界線、投影影を描かない。土地・岩壁境界ハイライトはBase Spriteへ焼き込まず、専用オーバーレイへ分離する。
- 土地と岩壁の前面は全Spriteへ含める。奥側の前面は画面手前側のブロックがYソートで隠し、隣接判定や専用断面Tilemapでは出し分けない。岩壁内部は同じ岩壁Tileの上へPresentationが連続アルファの黒い影Meshを重ねる。
- 木、岩、1×1設備は、接地点をセル内へ置いたまま、上部を画面上方向のセルへ視覚的にはみ出してよい。複数タイル設備も論理占有範囲と描画範囲を分離し、全占有セルと基準接地点を制作記録へ残す。
- 1×1物体の標準キャンバスは16×24 pxまたは16×32 pxを候補とする。1×2、2×2などの設備は占有形状に応じた16 px単位の幅を基準とし、上方向へのはみ出しを追加できる。横幅や高さが必要でも、Sprite寸法から論理占有を推測せず別途指定する。
- 立体物の基準点は、幹、脚、土台などが地面へ接する位置の中央とする。前後関係はこの接地点のワールドY座標で決める。
- 上方向へのはみ出し量、左右へのはみ出し量、接地点を制作記録へ残す。サブチャンク境界や画面端で欠けないことを検品する。

### 2.2 色、明度、情報量

- 明るく親しみやすい自然色を基本にする。Foragerの地表のような高い彩度差、広い色面、遠目での識別しやすさを配色の参考にするが、既存Sprite、輪郭、模様、地形配置は複製しない。
- 通常土地のBase Tileは、設備、物流、資源、配置表示を読みやすくする単色背景として意図的に簡素化する。上面はBiome代表色一色、断面は対応する一色とし、低コントラストであっても材質模様やランダムな色むらを焼き込まない。
- 配色の基準関係は、草原を明るい自然な緑、海を草原と明確に分離する鮮やかな青、前面断面を両者より暗い暖色の土色とする。個別のRGB値は採用画像から決め、既存作品のパレットをそのまま抽出しない。
- 1枚の通常土地Base Tileでは上面色と断面色を明確に分ける。必要な輪郭は地形外周や前面断面の下端などシルエットを読む箇所へ限定し、上面へ模様や輪郭を追加しない。
- 草原は自然光の下の若草色を基準とし、黄緑へ寄せすぎない緑主体の中高明度・中彩度にする。蛍光ライム、蛍光黄色、白に近い広い色面、全画面へ灰色や茶色を重ねたようなくすんだ色調、暗いオリーブ色、陰鬱な照明は避ける。
- 明るさは白飛び、蛍光色、高コントラストを意味しない。地面の広い単色面を明るく保ち、工業設備、資源、ベルト、柵を読みやすくする。
- Base Tile上ではバイオームを代表色で区別し、マップ全体では資源、植生、動物、地形障害物、環境演出など意味を持つ要素も組み合わせて識別する。
- 旧土壌分類は削除済みであり、標準・肥沃・砂質の派生Tileや共通模様マスクを制作しない。土地素材はGroundBlockの材質とBiomeパレットから設計し、Editor専用Debug VisualizationではGround Block Runtime IDを確認する。
- 通常土地と液体は、同じ地形を連続配置しても規則的な明暗格子や強い方向性が見えないようにする。岩壁上面だけは、地形の高さと硬さを読みやすくするため、1セル全体を使った規則的な菱形の面構成を意図的に反復してよい。
- 花、ひび、大石、草束などは通常土地Baseへ焼き込まない。追加する場合は、採取、通行、環境状態、プレイヤー配置などの意味を定義した別要素へ分離する。
- 地面には方向性の強い影を置かない。自然物と設備のSprite内へ描く基準光は左右へ偏らない上方光に統一するが、床へ落ちる長い投影影は焼き込まず、TIME-01Bの光源方向に追従するPresentationへ分離する。Normal Mapは全素材の必須納品物にせず、固定上方光との競合、16px基準の輪郭・色段階、Pixel Perfect Cameraでのぼけ・揺れを試作確認してから対象Spriteと制作規則を決める。
- 16×16 pxでは1 px単位の形を優先し、アンチエイリアス、半透明の縁、縮小前提の細部を使用しない。
- 通常地面では、個々の石、土塊、草へ黒い輪郭を付けない。暗い線で素材を細かく区切る表現は、石畳やタイル床に見えるため使用しない。
- 地形ごとに色相と平均明度は変えてよいが、通常土地上面は1色、断面も1色に統一し、タイル内へ陰影段階や模様を追加しない。
- 意味のない高周波ノイズ、ランダムな1 px、材質らしさだけを目的とした模様を通常土地Baseへ配置しない。
- 森林地面、岩地、火山などは草原より暗くしてよいが、画面全体を重くするほど黒くしない。岩壁上面は細かな石の集合にはせず、連続した岩盤の表面を大きな菱形の面で構成した意匠として表現する。

### 2.3 Base Tileと意味を持つ配置要素の分離

各Base地形は分類ごとに1枚だけを持ち、同じBiomeではワールド座標、Seed、Entity生成順、チャンクのロード順にかかわらず同じTileを表示する。

- 通常土地Base Tileには花、大石、大きなひび、強い反射、材質模様などのアクセントを入れない。
- 通常土地の上面と断面はそれぞれ単色にして、縦横へ連続配置しても境界線や方向性が見えないようにする。岩壁上面にはこの通常土地向け規則を適用しない。
- マップへ見た目の変化を加える場合は、Base地形の差分や座標だけで散布する純粋装飾ではなく、資源、植生、動物、地形障害物、環境状態、プレイヤー配置物など意味を持つ別データとして追加する。
- 表示要素の種類、位置、状態はECSまたはセーブデータを正とし、Base TileやTilemapの画素からゲーム上の意味を逆算しない。

## 3. PixelLab共通設定

PixelLabのUI更新により項目名や段階数が変わった場合は、次表の意味に最も近い値を選び、実際に使った値とSeedを「制作記録」へ追記する。

4章以降の各`Prompt`コードブロックは、共通文、素材別の指示、重要な禁止事項をすべて展開したコピー用全文である。識別子をPixelLabへ入力したり、複数のコードブロックを手作業で連結したりする必要はない。ネガティブプロンプト欄がある場合だけ、3.2の共通ネガティブプロンプトも追加でそのまま入力する。

### 3.1 地形Promptへ展開する草原共通方針

地形Promptへ展開する共通方針の参照用である。このコードブロック単体は入力せず、4.1の全文Promptを使用する。

```text
Top-down oblique 2D pixel art ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Each land tile is exactly 16 by 24 pixels: the upper 16 by 16 pixels are one completely solid biome color and the lower 16 by 8 pixels are one completely solid, darker front-face color belonging to the same block. Create one repeatable Base Tile for the biome. Do not add texture, material motifs, random color variation, highlights, speckles, grass, leaves, sand ripples, stones, cracks, or landmarks. The ground is a quiet background for machines, logistics links, resources, animals, and placement overlays. No visible tile boundary, no corner, and no left or right side face.
```

### 3.2 共通ネガティブプロンプト

ネガティブプロンプト欄があるツールでは、その欄へ次のコードブロックだけをそのまま入力する。欄がない場合は、4章以降の各Promptに必要な禁止事項を展開済みなので、手作業で連結しなくてよい。

```text
isometric diamond grid, side-scroller view, strong perspective, isometric voxel cube, beveled 3D cube, left side face, right side face, thick bevel, tile border, grid line, black seam, outlined pebble, outlined soil clump, cobblestone, brick pattern, gravel, long directional cast shadow, gloomy lighting, overall dull gray cast, muddy desaturation, fluorescent green, neon lime, acid green, chartreuse-dominant palette, highlighter yellow, oversaturated yellow-green, glowing ground, self-illumination, large near-white areas, clipped highlights, gradient, blur, anti-aliasing, smooth vector art, photorealism, text, UI, building, machine, character, large centered landmark, high visual noise, dense speckle, random single-pixel noise
```

### 3.3 地形Promptへ展開するスタイル一致方針

草原以外の地形Promptへ展開する共通方針の参照用である。このコードブロック単体は入力せず、4.2以降の各素材に記載した全文Promptを使用する。生成時は採用済み草原画像をStyle referenceへ指定する。

```text
Match the approved grassland reference in canvas size, top-and-face layout, outline policy, and cheerful natural-daylight color clarity. Change only the biome's solid top color and solid front-face color. Preserve clear natural colors without making them fluorescent, luminous, dull gray, muddy brown, or nearly black. Do not add texture, random variation, shading clusters, isolated pixels, or landmarks. The normal-land Base Tile must remain completely flat and solid-colored within each of its two planes.
```

### 3.4 Base地形共通設定

- PixelLabツール: `Create tiles (Pro)`
- Shape: `Square top-down`
- Tile size: 土地は`16×24`、液体は`16×16`、岩壁は`16×28`
- View angle: `Top-down`。土地と岩壁では下側に直線的な前面を含める
- Tile thickness: 土地8 px、岩壁12 px。液体は0
- Outline mode: `No outline`
- Shading: `Low`
- Details: 通常土地は`None`または最小。液体と岩壁は用途に応じて`Low`を比較する
- Seed: 生成ごとに記録する
- Style reference: 最初に採用した草原タイルを基準画像とし、それ以降の地形へ使用する
- 出力数: 各地形1枚
- 出力配置: 土地7バイオームはBiome ID順に左から並べた112×24 pxの横一列シートへまとめる。液体と岩壁は各1枚の個別画像を基本とする
- 通常土地の色: 上面はBiome代表色一色、断面は対応する一色に統一する
- 通常土地で禁止: 色むら、材質模様、ランダムな1 px、固有模様、グラデーション、上面内の陰影段階

`Create tiles (Pro)`のTiles機能は独立したBase地形生成に使う。PixelLabが16×24または16×28を直接生成できない場合は、同じ生成内の上面と前面を縦に結合して最終寸法へ整えるが、可視ピクセルの拡大縮小や補間は行わない。Base地形を相互に混ぜる接続Tilesetは制作せず、オートタイルによる接続表現は`ART-00B`の土地・岩壁境界ハイライト専用オーバーレイに限定する。

### 3.5 一体型ブロックの共通設定

- 土地のFinal canvasは`16×24`。上側16×16を上面、下側16×8を前面断面とする
- 岩壁のFinal canvasは`16×28`。上側16×16を上面、下側16×12を前面側面とする
- 前面の左右端は同じ高さと色へ揃え、横に並べたときに継ぎ目を作らない
- 土地断面は上側4 pxを上面が影になった近似暗色、下側4 pxを土・砂・岩・雪の地盤として描き分けてもよい
- 岩壁上面は16×16 px全体を使う大きな菱形の面構成を固定意匠とする。同じTileを並べた際にセル単位で模様が反復することは意図した見た目であり、透明な隙間や位置ずれによる描画不良とは区別する
- 岩壁側面の16×12 pxは全画素を同じ暗い無彩色で塗る。上端の境界線、水平層理、陰影、材質模様を描かず、最前列に露出した場合も平坦な一色面として見せる
- Base Spriteと前面断面には、外角、内角、縦側面、斜線、透明な接続形状を作らない。内角を含む透明な接続形状は5.2の境界ハイライトとして別に制作する
- SeedとStyle referenceを上面と同じ制作記録へ残す

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

液面は製品カタログとShaderで構成する。Tilemapは論理セル原点に固定し、奥岸セルの上端3pxだけを切る。淡水・海水の短い水面模様、反射、水際、マグマの網状模様は、それぞれ専用Shaderで動かす。静止Surfaceは公開ツールで生成し、4フレームOverlay画像やAnimatedTileは制作しない。

- マグマの網目は約6pxを基準とし、16pxの1タイル幅に約2〜3個を収める。明るい芯の半幅0.25px、橙色帯の半幅0.75px、4fpsを初期値とする。
- 模様は絶対画素座標から生成し、タイル・チャンク境界で再開しない。岸、液面位置、前面断面はアニメーションで動かさない。
- Presentation専用とし、ECSの有効地形、当たり判定、建築可否、液体量、経路、保存データを変更しない。
- 材料調整は停止中に専用Materialへ行う。実行時はPresenterが所有するコピーを全表示チャンクで共有する。

詳細は[地形タイル表現仕様](./TerrainTileArtSpecification.md#マグマ網状模様)を正とする。

### 3.9 固定資源スプライト共通フォーマット

`fixed-resource-prompt-dialogue`スキルを使用して、木、岩、草などの静止した固定資源をPixelLab Create S-XL Image (Pro)で新規制作または作り直す場合は、本節を正とする。7章では必要素材と個別検品項目だけを定め、完成Promptは同スキルで作成する。

- 固定資源は、採取や除去を終えるまで施設配置を妨げる`blocking_resource`と、施設配置時に自動除去される非機能の景観要素`auto_clear_scenery`へ分ける。床、道、舗装、耕地、地形Base、動物は含めない。
- PixelLabツールは`Create S-XL Image (Pro)`とする。`Output size`の最大辺が16～42 pxなら64 frames、43～85 pxなら16 frames、86～170 pxなら4 frames、171 px以上なら1 frameとして総フレーム数を決める。上限はPixelLabが各アスペクト比で許可する最大寸法に従う。`256 ÷ Output Width`や`256 ÷ Output Height`では計算しない。
- PixelLabの`No Background`を有効にする。Prompt本文の透明背景指定だけに依存せず、生成設定でも背景を無効化する。
- 最初の自然物では、PixelLabの`Style Image`へ採用済みの対象バイオーム地形を指定する。その自然物を採用した後は、以降の固定資源で最初の自然物を`Style Image`、対象バイオーム地形を`Reference Images`へ指定し、地形と自然物の画風を引き継ぐ。Style Optionsでは地形の輪郭なし設定をコピーせず、`blocking_resource`と`auto_clear_scenery`の輪郭規則を優先する。使用した参照画像、Style Options、Seedを制作記録へ残す。
- `Outputs per Base Variation = State Count × 2`、`Base Variation Count = floor(Total Frames ÷ Outputs per Base Variation)`、`Unused Frames = Total Frames - Base Variation Count × Outputs per Base Variation`とする。完全な基本バリエーションを1組も作れない組み合わせは使用しない。
- 完成Prompt本文には`Output size`、1 Spriteの寸法、出力シートのgrid寸法、総フレーム数、基本バリエーション数、出力シート上の行列配置を書かない。画角を示す`orthogonal square grid`と、基本バリエーション内の状態・環境版の順序は書く。
- 全基本バリエーションと全状態に`standard`と`snow-covered`を一対で用意する。積雪には明部`#EDF3F2`と影色`#C5D6DE`だけを使い、上向きの面へ薄く載せる。対応する通常版から、雪以外の形、状態、材質、配色、接地点、縮尺、画角、輪郭を変えない。
- `blocking_resource`は、地面から露出する外周だけを不透明な`#333333`で囲み、地面との接地点には輪郭、黒い横線、台座を付けない。通常状態に加えて伐採後や枯渇後の静止状態がゲーム仕様上必要かを確認し、必要な状態だけを同じ生成へ含める。
- `auto_clear_scenery`は通常状態だけとし、外周、内部、接地点のいずれにも輪郭を付けない。採取資源より控えめなシルエットとコントラストにする。
- 各基本バリエーションでは、状態ごとに`standard`、`snow-covered`の順でまとめる。余りフレームが出る場合は末尾だけを透明な未使用フレームとし、状態と積雪有無の枚数比率を崩さない。
- 全出力を斜め見下ろしの正方形グリッド向けPixel Art、左上光源、透明背景へ統一する。地面Tile、草の台座、投影影、風景、文字、アンチエイリアス、半透明の輪郭、未指定の顔や手足を追加しない。
- 論理占有タイル数、地面との接地点、上下左右への最大描画はみ出し量を制作記録へ残す。Unityへ取り込むときは8章と9章に従い、論理状態をECSまたはセーブデータ、表示をPresentation側で管理する。

### 3.10 動物スプライト共通フォーマット

`animal-prompt-dialogue`スキルを使用して、牛などの動物について全基本バリエーションと全ポーズをPixelLab Create S-XL Image (Pro)で一度に生成する場合は、本節を正とする。画像生成、UnityへのImport、アニメーション制作は同スキルの対象外とする。

- PixelLabツールは`Create S-XL Image (Pro)`とする。`Output size`の最大辺が16～42 pxなら64 frames、43～85 pxなら16 frames、86～170 pxなら4 frames、171 px以上なら1 frameとして総フレーム数を決める。上限はPixelLabが各アスペクト比で許可する最大寸法に従い、`256 ÷ Output Width`や`256 ÷ Output Height`では計算しない。
- PixelLabの`No Background`を有効にする。Prompt本文の透明背景指定だけに依存せず、生成設定でも背景を無効化する。
- 必須ポーズは`idle`、`move`、`eat`、`sleep`とする。種固有ポーズは、共通ポーズと重複せず、小物なしでシルエットから読め、ゲーム仕様に存在するものだけを0～3件追加する。
- `Pose Count = 4 + Species-specific Pose Count`、`Base Variation Count = floor(Total Frames ÷ Pose Count)`、`Unused Frames = Total Frames - Base Variation Count × Pose Count`とする。全必須ポーズを持つ基本バリエーションを1組も作れない組み合わせは使用しない。
- 各基本バリエーションでは、全ポーズを`idle, move, eat, sleep, 種固有ポーズ...`の順で一まとまりにする。余りフレームが出る場合は末尾だけを透明な未使用フレームとし、ポーズ種類ごとに全基本バリエーションをまとめない。
- 完成Prompt本文には`Output size`、1 Spriteの寸法、出力シートのgrid寸法、総フレーム数、基本バリエーション数、出力シート上の行列配置を書かない。画角を示す`orthogonal square grid`と、基本バリエーション内のポーズ順序は書く。
- 全動物を正面より横向きに近い左向きの斜め横から描き、頭と胴体をともに画面左へ向ける。顔の特徴を読める範囲だけ頭をわずかに手前へ振ってよいが、正面向きにはしない。
- `idle`、`move`、`eat`、種固有ポーズでは黒い縦長の目、`sleep`では閉じた両目を使用する。白目、ハイライト、まつ毛、複雑な表情は付けない。
- 地面との論理接地点を出力枠の下中央付近にある同一ピクセル座標へ固定し、全基本バリエーションと全ポーズを同じフレーム内位置へ揃える。`eat`や`sleep`でシルエットが低くなっても、ポーズごとに中央揃えや上下左右の移動を行わない。
- 同じ基本バリエーションでは、`sleep`の目を閉じることとポーズに必要な姿勢以外、表情、毛色、模様、体格、画角、向き、縮尺、パレット、輪郭を変えない。
- 全ポーズをソフトブラック`#333333`の不透明な実線輪郭、左上光源、透明背景へ統一する。鈴、帽子、バンダナ、首輪、餌、容器、寝具、地面Tile、投影影、風景、文字、モーションブラーを追加しない。
- 種固有の禁止事項がない場合はテンプレートの角括弧プレースホルダーだけを削除し、同じ段落の共通禁止文を残す。

### 3.11 生成直後の受け入れ確認

Unityへ取り込む前に、PixelLabの出力ごとに次を確認する。

- [ ] 各通常土地Base Tileの代表色と平均明度が、隣接する分類と意図どおり区別できる
- [ ] 各通常土地Base Tileの上面と断面がそれぞれ完全な単色で、色むら、材質模様、ランダムな画素を含まない
- [ ] 草原が明るい黄緑を保ち、灰緑、暗いオリーブ、茶色のフィルターが全体へ掛かって見えない
- [ ] 草原以外も昼間の明快な色調を保ち、Style referenceより不必要に暗く、灰色に、またはくすんでいない
- [ ] 草原のマスタースタイルと並べ、上面・断面の色分離とコントラストが一致する
- [ ] 16×16の各辺を接続しても、線や色面が不自然に途切れない
- [ ] 黒い区切り線がタイル画像自体へ描かれていない
- [ ] 7バイオーム土地シートは左から草原、森林、砂漠、岩地、湿地、雪原、火山の順に並び、各Spriteが16×24 pxである
- [ ] 同じTileを縦横に連続配置して最近傍表示で拡大し、アンチエイリアス、半透明の縁、意図しない中間色、目立つ明暗格子がない。岩壁上面の意図した菱形の明暗反復は除外する
- [ ] 不採用画像もSeedと却下理由を制作記録へ残し、同じ失敗条件を繰り返さない

## 4. 地形ベーススプライト

### 4.1 草原 `terrain_grassland`

- 必要数: 単色Base Tile 1枚。小花や土斑点はBaseへ含めず、意味を持つ資源・植生などへ分離する
- PixelLabツール: `Create tiles (Pro)`
- 設定: 3.4の共通設定。最初はStyle referenceを使わず、この結果からマスタースタイルを決める
- Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable meadow land block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid fresh spring-meadow green. Fill the lower 16 by 8 pixels with one solid darker warm-earth color. Keep both horizontal endpoints identical. Do not add texture, shading clusters, highlights, grass blades, flowers, soil marks, pebbles, random pixels, gradients, outlines, tile borders, corners, left or right side faces, cast shadows, or transparent gaps. The result must be a flat two-color block that remains subordinate to resources, machines, logistics, and placement overlays.
```

- 再生成理由（2026-10-12）: 最初の候補はGameビューで蛍光ライムに近く、他地形や将来の配置表示より強く見えたため不採用。Style referenceには使用しない。

- [ ] 生成
- [ ] マスタースタイルとして採用
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.2 森林地面 `terrain_forest_floor`

- 必要数: 単色Base Tile 1枚。根やシダはBaseへ含めず、意味を持つ資源・植生などへ分離する
- PixelLabツール: `Create tiles (Pro)`
- 設定: 3.4の共通設定。採用した草原をStyle referenceへ指定する
- Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art forest ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable forest land block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid medium forest green that is darker than the approved grassland. Fill the lower 16 by 8 pixels with one solid darker earth color. Keep both horizontal endpoints identical. Do not add texture, shading clusters, highlights, leaves, moss, roots, ferns, stones, random pixels, gradients, outlines, tile borders, corners, left or right side faces, cast shadows, or transparent gaps.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.3 砂漠 `terrain_desert_sand`

- 必要数: 単色Base Tile 1枚。小石や砂紋はBaseへ含めず、意味を持つ資源・地形要素などへ分離する
- PixelLabツール: `Create tiles (Pro)`
- 設定: 3.4の共通設定。採用した草原をStyle referenceへ指定する
- Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art desert ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable desert land block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid bright warm ochre or golden-sand color. Fill the lower 16 by 8 pixels with one solid darker compact-sand color. Keep both horizontal endpoints identical. Do not add texture, shading clusters, highlights, grains, pebbles, ripples, dunes, footprints, random pixels, gradients, outlines, tile borders, corners, left or right side faces, cast shadows, or transparent gaps.
```

- [ ] 生成
- [ ] 取込
- [ ] 設定
- [ ] 確認

### 4.4 淡水面 `terrain_fresh_water`

製品Surfaceは公開ツールが16×16px・Alpha 0で生成する。水色は深度Material、短い水面模様は`TerrainWaterSurfacePattern.mat`、岸の明暗は`TerrainLiquidShoreline.mat`で調整する。静止画像へ模様を焼き込まず、アニメーション画像は制作しない。

### 4.5 海水面 `terrain_salt_water`

淡水と同じ製品経路を使い、カタログとMaterialの海水色で区別する。Surfaceは16×16px・Alpha 0とし、海水専用の4フレーム素材は制作しない。

### 4.6 岩地・湿地・雪原・火山・マグマ

生成基盤では7バイオームとマグマまで実装済みである。岩地、湿地、雪原、火山は草原・森林・砂漠と同じ16×24 pxの一体型土地Tile、マグマは16×16 pxの液体Tileとして制作する。

| 用途 | 推奨ファイル接頭辞 | 主な見分け方 |
| --- | --- | --- |
| 岩地 | `terrain_rockland` | 岩壁より低く平坦な灰色地面。建築可能地と読める明度にする。 |
| 湿地 | `terrain_wetland` | 草原より深い緑の湿潤地面。灰色や黒へ寄せず淡水面と区別する。 |
| 雪原 | `terrain_snowfield` | 青みのある雪面。完全な白を避け、HUDや配置表示の白を残す。 |
| 火山 | `terrain_volcanic` | 深い赤褐色の火山岩。黒く潰さず、マグマの橙色より低彩度にする。 |
| マグマ | `terrain_magma` | 高彩度の橙と黄。大面積でもちらつかない情報量にする。 |

岩地、湿地、雪原、火山の通常土地は、上面をBiome代表色一色、断面を対応する一色にする。材質模様、色むら、ランダムな画素、固有アクセントを入れない。マグマは液体として別扱いとし、アニメーションさせる場合も3.8に従って外周と岸を固定し、内部の流れと明暗だけを動かす。

#### 4.6.1 岩地 `terrain_rockland`

- Static Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art rockland ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable buildable rockland block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid readable medium gray. Fill the lower 16 by 8 pixels with one solid darker rock color. Keep this ground lighter than the unbuildable rock wall and keep both horizontal endpoints identical. Do not add texture, shading clusters, stones, cracks, mineral specks, random pixels, gradients, outlines, tile borders, corners, side faces, cast shadows, or transparent gaps.
```

#### 4.6.2 湿地 `terrain_wetland`

- Static Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art wetland ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable buildable wetland block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid clear deep green that remains distinct from grassland and freshwater. Fill the lower 16 by 8 pixels with one solid darker damp-earth color. Keep both horizontal endpoints identical. Do not add texture, shading clusters, moss, reeds, puddles, roots, random pixels, gradients, outlines, tile borders, corners, side faces, cast shadows, or transparent gaps.
```

#### 4.6.3 雪原 `terrain_snowfield`

- Static Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art snowfield ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable snowfield block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid pale blue-white that remains darker than white UI and placement overlays. Fill the lower 16 by 8 pixels with one solid darker frozen-earth color. Keep both horizontal endpoints identical. Do not add texture, shading clusters, highlights, ice, footprints, wind stripes, random pixels, gradients, outlines, tile borders, corners, side faces, cast shadows, or transparent gaps.
```

#### 4.6.4 火山地面 `terrain_volcanic`

- Static Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art volcanic ground block for a cozy agricultural and industrial automation game, using an orthogonal square grid rather than an isometric diamond grid. Create one repeatable buildable volcanic block exactly 16 by 24 pixels. Fill the upper 16 by 16 pixels with one solid deep but readable red-brown that is less bright and less saturated than magma. Fill the lower 16 by 8 pixels with one solid darker volcanic-rock color. Keep both horizontal endpoints identical. Do not add texture, shading clusters, embers, cracks, lava lines, stones, random pixels, gradients, outlines, tile borders, corners, side faces, cast shadows, or transparent gaps.
```

#### 4.6.5 マグマ `terrain_magma`

製品Surfaceは公開ツールが16×16px・Alpha 255で生成する。全面の模様は`TerrainMagmaSurfacePattern.mat`と`Magma Surface Pattern Tilemap`で描く。Voronoi境界を明るい網、内部を暗い面にし、特徴点を動かして網目そのものを変形させる。ドット絵の静止模様・4フレームOverlayは追加しない。

見本画像全体が1タイル分であるため、網目6pxを基準とする。色、網目寸法、芯と橙色帯の幅、移動量、周期、FPS、内部階調、明滅量は専用Materialで調整する。基礎の不透明面と奥岸3pxクリップを維持し、発光によって岸や液面の高さを変えない。詳細は[地形タイル表現仕様](./TerrainTileArtSpecification.md#マグマ網状模様)を参照する。

- [x] マグマ面のShader生成・縮尺・網目変形・見た目を確認
- [x] マグマ奥岸のGroundの縁3pxとWall前面が見え、液面より下の断面は透けていない（ユーザー確認済み）
- [x] ユーザー比較を経てHDR表面色・Bloom Intensity 0.4・岸の橙色照明を採用し、製品Sceneへ接続する（全体照明の暗色化は採用しない）
- [x] 製品経路への接続と照明修正後、網目・岸のにじみ・地面と壁への光・カメラ移動時の見た目をユーザーが確認し、採用を承認（2026-10-28）
- [ ] 岩地を生成・取込・設定・確認
- [ ] 湿地を生成・取込・設定・確認
- [ ] 雪原を生成・取込・設定・確認
- [ ] 火山を生成・取込・設定・確認

### 4.7 一体型岩壁 `terrain_rock_wall`

- 必要数: Base Tile 1枚。上面にはセル全体を使う大きな菱形の面構成を固定で入れ、目立つひびや鉱物色の点は装飾または資源表示へ分離する
- 本番配置先: `Assets/Farmware/Art/WorldGeneration/Terrain/Base/terrain_rock_wall_01.png`。`Fallback/RockWall.png`は構築確認用として残し、本番候補で上書きしない
- PixelLabツール: `Create tiles (Pro)`
- 設定: 3.4の共通設定。採用した草原と岩地をStyle referenceへ指定し、上面と側面を一枚へ含める
- Prompt（全文をそのまま入力）:

```text
Top-down oblique 2D pixel art rock-wall block for a cozy agricultural and industrial automation game. The gameplay and Tilemap use an orthogonal square grid, while the rock surface itself deliberately uses a large diamond-shaped faceted motif. Create exactly one 16 by 28 pixel block. The upper 16 by 16 pixels are one unbuildable rock-wall top divided into four broad triangular faces that meet as a centered diamond-like form. This full-cell motif must repeat clearly when the same tile is placed in adjacent square-grid cells. The lower 16 by 12 pixels are one straight front-facing rock wall belonging to the same block. Fill every pixel of this 16 by 12 face with exactly one uniform dark neutral-gray color, and connect it directly to the top without a transparent gap.

Use readable medium cool gray, warm gray-brown, and limited charcoal accents on the upper rock-wall top while keeping the overall block bright enough for a cheerful daylight world. Use two or three controlled value steps across the broad top faces so the deliberate diamond facet is immediately readable. Keep the entire lower face perfectly flat and solid-colored: no top border line, horizontal strata, texture, shading, gradient, highlights, or isolated pixels. Do not put a unique crack, mineral speck, or random landmark in the Base Tile. Never outline individual small rock pieces. The diamond is a surface motif only and must not change the square outer silhouette, Sprite bounds, or orthogonal gameplay grid. Avoid transparent gaps, corner cutouts, separate left or right side faces, cobblestone, brick pattern, gravel, dark grout, dense cracks, cave opening, loose boulder, high-contrast vein, blur, anti-aliasing, and dense speckles.
```

この菱形は岩壁の表面意匠であり、アイソメトリックの論理グリッド、菱形セル、接続RuleTileを意味しない。隣接セルで同じ菱形が反復して見えることを採用画像どおりの仕様とする。側面12 pxは全画素が同じ色であることを仕様とし、境界線や層理がない状態を欠落扱いにしない。検品では透明な隙間、Sprite位置ずれ、側面色の不一致だけを不具合として扱う。

- [x] 生成
- [x] 取込
- [x] 設定
- [x] 確認
- [x] `RockWallAtlas`、専用Tile、SampleSceneが本番用Spriteを参照し、Editor構築ツールの再実行後もFallback参照へ戻らない
- [x] 連続岩壁の側面が境界線や層理のない一色面として表示され、奥側の側面が手前ブロックに隠れることをGameビューで確認
- [x] 内部影、岩壁上資源、境界ハイライト、水・マグマに面した下層土地断面をGameビューで確認

### 4.8 岩壁内部影

岩壁内部専用のスプライトは制作しない。露出面と内部で同じ岩壁Tileを使用し、Presentationが境界から内側へ連続的に濃くなる黒い影Meshを後付けする。

- [ ] 岩壁Tile単体に黒い内部影を焼き込んでいない
- [ ] 影Meshの下でも岩壁Tileの模様が境界付近に残る
- [ ] チャンク境界で影の濃さと勾配が途切れない

## 5. 通常地形と低地をつなぐ断面表現

基本地形同士は同じ高さとして扱い、草原と森林、草原と砂漠などのBase Tileをオートタイルで混ぜない。
異なる基本地形はBase Tileの色と模様が直角に切り替わる表現を維持し、輪郭を丸めたり材質をブレンドしたりしない。`ART-00B`では、Base Tileとは独立した白い透明ハイライトを、水・マグマに接する土地の外周辺・内角と、岩壁領域の外周辺・内角へ重ねて輪郭を読みやすくする。

土地は上面と断面を16×24へ一体化し、全ての通常地形セルへ同じ形式で配置する。淡水、海水、マグマのBase Tileとアニメーションだけを専用Tilemapで画面下へ8 px移動するため、奥の土地断面は低地に面した場所だけ見え、土地が連続する場所では手前の土地ブロックに隠れる。

草原、森林、砂漠、岩地、湿地、雪原、火山の全土地Spriteは、`terrain_biomes_base.png`から16×24 pxで7分割し、`GroundTerrainAtlas.spriteatlasv2`の同じTextureページへ収める。`TilemapRenderer.Mode.Individual`のYソートをバイオーム境界でも連続させるため、土地Atlasの複数ページ化は許可しない。岩壁は土地から独立した`RockWallAtlas.spriteatlasv2`へ収録する。

岩壁も上面と側面を16×28へ一体化し、全ての岩壁セルへ配置する。岩壁セルにはBiome由来の16×24土地ブロックを下層として残すため、水・マグマに面しても土地断面が欠けない。岩壁上資源と内部影は上面位置に合わせて12 px上へ置く。

### 5.0 一体型前面の役割

前面は横一直線だけを使用し、全ての土地・岩壁Spriteへ含める。奥側の前面を表示するかどうかは専用の隣接判定ではなく、`TilemapRenderer.Mode.Individual`のYソートと手前のブロックによる遮蔽で決まる。

前面断面の出し分けには、オートタイル、RuleTile、4bit接続マスク、47形状Tileset、専用のTerrain Front Edge Tilemap、Rock Wall Front Face Tilemapを使用しない。`ART-00B`の土地・岩壁境界ハイライトは前面断面ではなく、上面へ重ねるPresentation専用の透明オーバーレイとして別に扱う。Base Spriteと前面断面には外角、内角、縦方向の側面を作らず、地形輪郭が直角または階段状に見えることを簡潔な世界表現として許容する。

- [x] 全土地Spriteが上面16 pxと断面8 pxを一枚に含む
- [x] 全岩壁Spriteが上面16 pxと側面12 pxを一枚に含む
- [x] 土地と岩壁の左右端が同じ高さ・色で接続する
- [x] Base Spriteと前面断面へ、外角、内角、縦側面を補うオートタイルを追加していない
- [x] 連続する土地・岩壁では奥側の前面が手前ブロックに隠れる
- [x] 岩壁内部影を一体型Spriteとは独立した既存Meshで表示する
- [x] 全土地Spriteが`GroundTerrainAtlas`の同じTextureページへパックされている
- [x] 岩壁Spriteが土地とは別の`RockWallAtlas`へパックされている

### 5.1 表示用の高さ分類

高さ分類は断面を表示するためのPresentation情報であり、移動、建築可否、採取、物流などのゲームルールを変更しない。

| 表示分類 | 対象地形 | 断面表示 |
|---|---|---|
| 通常地形 | 草原、森林、砂漠、岩地、湿地、雪原、火山 | 16×24の一体型ブロック。上面16 px、断面8 px |
| 低地 | 淡水、海水、マグマ | 16×16の正方形Tile。論理セルは維持したまま液面を画面下へ8 px投影する |
| 高地・障害物 | 岩壁 | Biome由来の16×24土地を下層へ維持し、その上に16×28の岩壁ブロックを重ねる |

淡水と海水、淡水とマグマなど低地同士が接する場合も、高低差の断面は表示しない。
通常地形の種類が変わる境界と、低地の種類が変わる境界は、Base Tileが直接切り替わる。土地・岩壁境界ハイライトを表示する場合も、Base Tile自体の選択と高さ分類は変更しない。

- [ ] 全GroundBlock・WallBlock定義を通常地形、低地、高地・障害物のいずれかへ分類
- [ ] 高さ分類がSimulationの移動・建築・生成結果を変更しない
- [ ] 淡水、海水、マグマ同士の境界に断面が表示されない
- [ ] 高さ表現のための隣接セル再生成を行っていない

### 5.2 土地・岩壁境界ハイライト

`ART-00B`では、上下左右と斜め4方向の合計8方向から接続状態を求め、16×16 pxの透明ハイライトを上面へ重ねる。土地セルでは、水またはマグマに接する上下左右の辺を外周とし、角を挟む2つの上下左右セルがどちらも土地側で、斜めセルだけが水またはマグマなら内角を表示する。異なるバイオームの土地同士には表示しない。岩壁セルでは、岩壁以外に接する上下左右の辺を外周とし、角を挟む2セルが岩壁で斜めセルだけが岩壁ではない場合に内角を表示する。

接続判定は、斜めをその角に隣接する上下左右2方向が接続済みの場合だけ有効にする47状態の参照表とする。方向による色・陰影差を付けず、90度回転だけを使って47状態を14枚の可視Spriteから構成する。左右反転は使用しない。周囲8方向がすべて同じ領域でハイライトが不要な状態は、透明Spriteを置かずTilemapセルを空にする。

制作する14形状は次のとおりとする。`N`、`E`、`S`、`W`は上、右、下、左の外周辺、`NE`、`SE`、`SW`、`NW`は内角を表す。表にない向きは90度回転で作る。

| 番号 | 外周辺 | 内角 | 用途 |
| ---: | --- | --- | --- |
| 1 | N・E・S・W | なし | 孤立した領域 |
| 2 | E・S・W | なし | 外周3辺 |
| 3 | E・W | なし | 向かい合う外周2辺 |
| 4 | S・W | なし | 隣接する外周2辺 |
| 5 | S・W | NE | 隣接する外周2辺と内角1つ |
| 6 | N | なし | 外周1辺 |
| 7 | N | SE | 外周1辺と右側の内角 |
| 8 | N | SW | 外周1辺と左側の内角 |
| 9 | N | SE・SW | 外周1辺と内角2つ |
| 10 | なし | NE | 内角1つ |
| 11 | なし | NE・SE | 隣接する内角2つ |
| 12 | なし | NE・SW | 向かい合う内角2つ |
| 13 | なし | NE・SE・SW | 内角3つ |
| 14 | なし | NE・SE・SW・NW | 内角4つ |

Asepriteの原稿とUnity用PNGは、14形状を左から番号順へ並べた224×16 pxの横一列とする。各セルは16×16 px、Padding 0×0、Offset 0×0とし、予約セルやガターは設けない。Unityでは`Assets/Farmware/Art/WorldGeneration/Terrain/Highlights/terrain_boundary_highlights.png`へ配置し、構築ツールがSprite Editor Data Provider経由で安定名`terrain_boundary_highlight_01`〜`14`へ分割する。拡大縮小、補間、半透明のぼかしは行わない。

この節は旧素材の制作記録である。現在はEditor生成ツールが、8近傍47状態を14基本形と回転へ割り当てる論理契約から、224×16 pxのPNG、14区画、`BoundaryHighlightAtlas`を再生成する。現行の色・線幅・内角形状は地形タイル表現仕様を正とする。

ハイライトの可視ピクセルは、被せるバイオーム、土地Sprite、岩壁Spriteに依存しない不透明白の共通デザインとし、同じ14枚を共有する。輪郭の1 px形状をぼかさずBase Tileへ自然に馴染ませるため、PNGへ半透明画素を直接混ぜず、Presentationが実行時Tileカラーへ一様な不透明度20%を掛ける。Ground用は白RGBのまま、Wall用はWall上面と同じ既定係数0.85をRGBへ掛け、暗いWall上で白い境界だけが浮くことを防ぐ。土地と岩壁では上面の表示高さとTintが異なるため、素材を共有しても土地上面用と岩壁上面用のTilemapは分離する。

ハイライトはBase地形の材質を混ぜるためではなく、土地と岩壁の輪郭を読みやすくするためのPresentationである。Base Spriteへ焼き込まず、Simulationのバイオーム、地形種別、建築可否を変更しない。更新はDirtyなサブチャンクと、その境界判定に必要な隣接セルへ限定する。

- [x] ハイライト素材が透明背景で、Base Tileの模様と断面を置き換えない
- [x] ハイライトの色とデザインが全バイオーム・岩壁で共通の白一種類になっている
- [x] PNGの可視画素は不透明白のまま、実行時Tileカラーの不透明度20%でBase Tileへ自然に馴染んでいる
- [x] 14枚の可視Spriteと90度回転だけで47接続状態を表し、左右反転を使用していない
- [x] ハイライト不要状態では透明Spriteではなく空のTilemapセルを使用している
- [x] 土地では水・マグマとの境界だけに表示され、異なる土地バイオーム間には表示されない
- [x] 土地では角を挟む上下左右2セルが土地側で斜めだけが水・マグマの場合に、対応する内角が表示される
- [x] 岩壁では上下左右の隣接セルが岩壁ではない辺と、斜めだけが岩壁ではない内角に表示される
- [x] 14形状と各回転で、欠け、隙間、過度な二重線がない
- [x] 16×16サブチャンク境界、64×64チャンク境界、負座標で同じ接続結果になる
- [x] チャンクのロード順にかかわらず、隣接セルの追加・除去後にハイライトが更新される
- [x] ハイライトを無効にしてもSimulationの生成結果とゲームルールが変化しない
- [x] 画面外を含む全セルを毎フレーム再判定していない

### 5.8 水際影とマグマ照り返し

初期実装では水際影とマグマ照り返しを追加しない。一体型土地ブロックの断面色だけで、通常地形と低地の高低差を読ませる。

Gameビューで高さが読めない場合だけ、チャンク単位のMeshまたはバッチ描画による補助効果を検討する。タイルごとのGameObjectやSpriteRendererは作成しない。

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

製品の水・マグマSurfaceは`Ground Tilemap`から分けた`Liquid Base Tilemap`へ反映し、マグマ模様は`Magma Surface Pattern Tilemap`へ重ねる。両方を論理セル原点へ固定し、奥岸3pxだけShaderで切る。16×24土地は`Ground Tilemap`、16×28岩壁は`Rock Wall Tilemap`へ同じ論理座標で反映する。

`Ground Tilemap`と`Rock Wall Tilemap`は、セルごとの重なりをY座標で解決するため`TilemapRenderer.Mode.Individual`を使用する。Sort Orderは`Top Left`とし、画面奥に当たる大きいワールドYから先に描いて、画面手前の小さいワールドYを後から重ねる。通常資源は、直前方に当たるワールドYが1小さいセルが岩壁なら`Resource Behind Rock Wall Tilemap`へ置き、岩壁より小さいSorting Orderで先に描く。それ以外は`Resource Tilemap`へ置き、岩壁より大きいSorting Orderで後から描く。これにより遠方の大きな絶対座標を透明ソート値へ使わず、奥の木・石・砂を手前岩壁で隠せる。別断面Tilemapは不要である。液体セルではGroundを空にし、土地・岩壁セルでは一体型Spriteの前面が手前側へはみ出す。

- [ ] Ground、Liquid Base、Magma Surface Pattern、Rock Wall、Resource Behind Rock Wall、Resource、Rock Wall Resourceの各Tilemapが分離されている
- [ ] 製品液面と模様が同じ論理セル原点にあり、Ground Tilemapの同一論理セルへ液体Tileが重複していない
- [ ] 奥岸上端3pxの切抜きと地面の縁に、不自然な隙間や模様のはみ出しがない
- [ ] 液面下辺が手前の土地セルへ重なる場所では、土地上面が前に描かれて液面を隠している
- [ ] GroundとRock WallがIndividual ModeかつTop Left順で、連続するブロックの奥側前面を手前ブロックが隠している
- [ ] 直前方が岩壁の通常資源だけがResource Behind Rock Wallへ入り、岩壁より先に描かれて重なる下部が隠れる
- [ ] それ以外の通常資源がResourceへ入り、岩壁より後に描かれて前面へ見える
- [ ] 岩壁セルの同じ論理座標にBiome由来の土地ブロックも残っている
- [x] 土地16×24と岩壁16×28のはみ出しをTilemapRendererとカメラの可視チャンク範囲が含む
- [ ] チャンクのアンロード時に土地・岩壁を同じサブチャンク単位で消去する
- [ ] 再ロード後も同じ座標へ同じ一体型Tileが出る

## 6. 意味を持つ地表要素とオーバーレイ

プレイヤーが配置する床、道、模様、舗装、耕地など、配置や状態に意味を持つ平面的な要素は、専用の`Ground Overlay Tilemap`へ表示する。土地より前、岩壁、木、岩、動物、柵、作業台、機械などの立体物より後ろに描く。ワールド生成では単色Base Tileの反復を隠すことだけを目的に、草束、小花、小石、土斑点をランダム散布しない。採取、通行、環境状態、バイオーム固有性などの役割が決まった要素だけを追加する。

装飾は16×16 pxの透明背景とし、ゲームルール用のGroundBlock、WallBlock、Liquid、TerrainSurfaceを変更しない。木、石などの採取資源とも別レイヤーにする。装飾の配置座標、種類、回転、所有者、ゲーム効果はECSまたはセーブデータを正とし、Tilemapは16×16サブチャンク単位で再構築できる表示キャッシュとする。

- `Ground Overlay Tilemap`へ置くのは厚みを持たない地表装飾だけとする。
- 柵、作業台、機械、樹木など、状態、アニメーション、接地点、上方向へのはみ出しを持つものは置かない。
- 同じセルに地表装飾、自然資源、配置設備が共存しても、下層の論理データを削除しない。
- ゲーム効果を持つ床や耕地は、見た目と効果Componentを分離する。Tilemap画像の種類から効果を逆算しない。
- プレイヤーによる配置、変更、撤去では、変更セルを含むDirtyサブチャンクだけを更新する。

### 6.1 草原の意味を持つ地表要素 `decor_grassland`

以下の候補は、ゲーム上または世界表現上の役割と配置データを定義したものだけ制作する。純粋に画面を埋めるためのランダム装飾としては使用しない。

- 必要数: 草束2、小花2、小石2、土斑点2
- PixelLabツール: `Create tiles (Pro)`
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
- PixelLabツール: `Create tiles (Pro)`
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
- PixelLabツール: `Create tiles (Pro)`
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
- PixelLabツール: `Create tiles (Pro)`
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

- 初期採用数: 成木3基本バリエーション×通常版・積雪版の6 Sprite。占有は1×1タイル、`Output size`は32×32 pxを初期値とする
- Placement Class: `blocking_resource`
- PixelLabツール: `Create S-XL Image (Pro)`
- Style Image: 採用済みの草原地形。地形の輪郭なし設定はコピーしない。採用した成木は、7.2以降の固定資源で最初の自然物の基準画像として使用する
- Prompt作成: `fixed-resource-prompt-dialogue`を使用する。状態は`normal`を必須とし、伐採後に切り株を残すゲーム仕様を採用する場合だけ`after_harvest`を追加する
- 外見要件: 丸みのある葉冠、暖かい中明度の緑葉、落ち着いた茶色の幹。幹の接地点を枠の下中央付近へ置き、葉冠は上方向へはみ出してよい
- 固有禁止事項: 果実、顔、植木鉢、草の台座、長い投影影、周辺風景
- [ ] スキルが算出した全基本バリエーション、全採用状態、通常版・積雪版を一度の生成で作成
- [ ] 通常版と積雪版の対応関係、露出外周の`#333333`、接地点に輪郭がないことを確認
- [ ] 採用する成木3基本バリエーションを選定
- [ ] 取込
- [ ] 資源Prefabまたは表示定義へ設定
- [ ] 隣接する木と設備の重なり順を確認

### 7.2 石資源 `resource_stone_basic`

- 初期採用数: 3基本バリエーション×通常版・積雪版の6 Sprite。論理占有は1×1、`Output size`は16×24 pxを初期値とする
- Placement Class: `blocking_resource`
- PixelLabツール: `Create S-XL Image (Pro)`
- Style Image: 7.1で採用した成木。Reference Images: 採用済みの岩地
- Prompt作成: `fixed-resource-prompt-dialogue`を使用する。状態は`normal`を必須とし、採掘後に残骸を残すゲーム仕様を採用する場合だけ`depleted`を追加する
- 外見要件: 木炭色と落ち着いた茶色の丸い岩を2～3個まとめ、上面と短い正面を読める形にする。接地点を枠の下中央付近へ置く
- 固有禁止事項: 草の台座、鉱石結晶、長い投影影、文字、周辺風景
- [ ] スキルが算出した全基本バリエーション、全採用状態、通常版・積雪版を一度の生成で作成
- [ ] 通常版と積雪版の対応関係、露出外周の`#333333`、接地点に輪郭がないことを確認
- [ ] 採用する3基本バリエーションを選定
- [ ] 取込
- [ ] 資源Prefabまたは表示定義へ設定
- [ ] 草原、森林、砂漠の全背景で確認

### 7.3 砂資源パッチ `resource_sand_patch`

- 初期採用数: 3基本バリエーション×通常版・積雪版の6 Sprite。論理占有は1×1、`Output size`は16×16 pxを初期値とする
- Placement Class: 採取または除去まで施設配置を妨げる`blocking_resource`
- PixelLabツール: `Create S-XL Image (Pro)`
- Style Image: 7.1で採用した成木。Reference Images: 採用済みの砂漠地形
- Prompt作成: `fixed-resource-prompt-dialogue`を使用する。状態は`normal`を必須とし、採取後にパッチを残すゲーム仕様を採用する場合だけ`depleted`を追加する
- 外見要件: 通常の砂漠地面より少し明るい粗い砂粒と少数の暗い粒で、低く不規則な砂の集積を表す
- 固有禁止事項: 容器、シャベル、足跡、砂丘、文字、投影影
- [ ] スキルが算出した全基本バリエーション、全採用状態、通常版・積雪版を一度の生成で作成
- [ ] 通常版と積雪版の対応関係、露出外周の`#333333`、接地点に輪郭がないことを確認
- [ ] 採用する3基本バリエーションを選定
- [ ] 取込
- [ ] 資源Prefabまたは表示定義へ設定
- [ ] 通常の砂漠地面と識別できることを確認

### 7.4 水資源表示

淡水・海水は地形属性そのものを固定資源として扱うため、第1段階では専用オブジェクトを追加しない。採取可能地点と水種別を示す必要が生じた場合は、採取設備の配置プレビューまたはUIアイコンで示し、常時表示のマーカーを地形へ焼き込まない。

- [ ] 専用スプライト不要であることを実装と確認

### 7.5 直接操作プレイヤー（新規制作）

旧ドローンの制作計画を置換する。まだ本番画像・アニメーションを採用済みとは扱わない。

- [ ] 歩行・待機・採掘・攻撃・観察・泳ぎについて必要な方向と状態をPLAYER-01の試作から決める
- [ ] キャンバス、占有、接地点、頭上へのはみ出し、装備表示の分割を決める
- [ ] 16pxセルと480×270の画面で対象タイル・移動方向・向きが読める仮絵を確認
- [ ] 地上、夜間照明、海中・深海で輪郭・装備の可読性を確認
- [ ] 本番生成・取込・設定・人間確認を個別に記録

8方向入力は8方向の専用Spriteを必ず量産する意味ではない。左右反転や方向共有の採否を確認し、実際の表示方式が決まってから制作枚数を算出する。画像生成Promptの個別作成はこの文書改訂には含めない。

### 7.6 新地形・水中・乗り物（段階制作）

開始コアの画像は制作しない。新しい対象をロードマップの試作順に追加する。

- [ ] 同一バイオーム内の複数地面・壁Blockを見分ける配色と一覧を作る
- [ ] 全地形材質について同じ材質と認識できるGround／Wallの対を作り、共通Itemアイコン、地面配置プレビュー、壁配置プレビューとの対応を一覧化する
- [ ] `TerrainBlockItem`と、石材・鉱石等のクラフト用資源Itemを所持品内で区別できるアイコン規則を作る。同名材質でも色だけに依存しない
- [ ] 通常鉱床は`LooseSurface`、`GroundEmbedded`、`WallEmbedded`の3形態を共通資源と認識できる系列で制作する。Ground用Overlayは16×24 px、Wall用Overlayは16×28 pxのHostと接地点・断面位置を合わせ、鉱脈を地形Baseへ焼き込まない
- [ ] `MassiveDeposit`は複数セル占有、採掘中、枯渇直前、枯渇後の占有解除が読める表示を試作する。セルごとの独立オブジェクトに見えない一体形状とし、採掘設備の接続位置を確認する
- [ ] 空の穴、掘削・設置・採掘進捗、破損道具の表示を決める
- [ ] 海中・深海の背景、海底、生物、直下プレビューの透過・遮蔽を確認
- [ ] 水密壁・扉・窓、空気室、浸水・排水・酸素警告の表示を確認
- [ ] バイク、車、ボート、潜水艇、ヘリについて乗降・占有・接地点・飛行影を試作後に制作
- [ ] プレイヤー、装備・個体アイコン、図鑑証拠・研究ランク・燃料・耐久・死亡期限の表示を追加

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
- [ ] Pivotを土地はCustom `(0.5, 0.6666667)`、岩壁はCustom `(0.5, 0.2857143)`、液体はCenterへ統一
- [ ] 各立体物の接地点、論理占有サイズ、上下左右の最大描画はみ出し量を表示定義へ記録
- [ ] SpriteRendererのSort Pointを`Pivot`にし、接地点のワールドY座標で前後関係を決定
- [ ] 土地シートはCell Size 16×24、岩壁シートは16×28、液体シートは16×16でSliceする
- [ ] 本番用バイオーム土地シートは112×24 pxを16×24 pxで左から7分割し、Padding 0×0、Offset 0×0を設定する。境界ハイライトは224×16 pxを16×16 pxで左から14分割し、Padding 0×0、Offset 0×0を設定する
- [x] 土地はSlice後の各Spriteが16×24 pxで、上側16 pxが上面、下側8 pxが可視断面になっている
- [ ] 岩壁はSlice後の各Spriteが16×28 pxで、上側16 pxが上面、下側12 pxが可視側面になっている
- [ ] PixelLabで生成した一体型ブロックへ、拡大縮小、補間、可視ピクセルの描き足しを行っていない
- [ ] PNGと`.meta`を一緒にバージョン管理対象へ追加
- [x] Sprite Packer Modeを`Sprite Atlas V2`へ設定
- [x] 7枚のBiome土地Spriteを含む`terrain_biomes_base.png`を`GroundTerrainAtlas.spriteatlasv2`、岩壁の元Textureを`RockWallAtlas.spriteatlasv2`のPackableへ設定
- [x] 両AtlasをBuilt-in（Include in Build）とし、Point、Mip Maps Off、Read/Write Off、RGBA32、Compression Noneへ設定
- [x] 両AtlasをPadding 4 px、Rotation Off、Tight Packing Off、Alpha Dilation Off、Max Texture Size 512へ設定
- [x] 土地Atlasが1ページに収まり、全土地Spriteのパック後Textureが同一であることを確認
- [x] SceneまたはPrefabに意図しない全面差分がないことを確認

## 9. Unity側の表示アセット構成

推奨配置先:

```text
Assets/Farmware/Art/WorldGeneration/
├── Atlases/
│   ├── GroundTerrainAtlas.spriteatlasv2
│   ├── RockWallAtlas.spriteatlasv2
│   └── BoundaryHighlightAtlas.spriteatlasv2
├── Terrain/
│   ├── Base/
│   ├── Highlights/
│   │   └── terrain_boundary_highlights.png
│   └── Decorations/
├── Resources/
├── Player/
└── Equipment/
```

単体画像のファイル名は`種別_用途_番号.png`を基本とし、複数Biomeをまとめる本番用土地シートは`terrain_biomes_base.png`とする。

```text
terrain_biomes_base.png
terrain_grassland.asset
terrain_forest.asset
terrain_rock_wall_01.png
overlay_water_ripple_01.png
overlay_magma_flow_01.png
decor_forest_fern_01.png
resource_tree_basic_01.png
player_drone_idle_south.png
equipment_starting_core_active.png
```

製品カタログ未設定時の`ChunkTilemapPresenter`は、18Biomeの16×24仮色土地を`Ground Tilemap`、淡水・海水・マグマを`Liquid Base Tilemap`へ排他的に表示する。Biome配列は安定ID 1〜18順の18要素を必須とし、欠落時に他BiomeのTileを表示しない。製品カタログ未設定の静止液面だけは論理セルを変えず画面下へ8px移動する。製品の液面と模様は原点に固定し、奥岸3pxだけをShaderで切る。岩壁セルにもBiome由来の土地ブロックを残し、16×28岩壁を`Rock Wall Tilemap`へ同じ論理座標で重ねる。GroundとRock WallはIndividual ModeかつTop Left順でセルごとにYソートし、画面奥を先、手前を後に描いて奥側の断面・側面を手前ブロックで隠す。通常Resourceは直前方の岩壁有無から背面用と前面用の固定Tilemapへ振り分け、遠方座標でも手前岩壁との前後関係を維持する。岩壁の内部影と露出資源は岩壁上面に合わせて12 px上へ表示する。境界用Tilemapには再制作した14 Spriteを接続し、8近傍47状態の選択結果を20%のAlphaで描画する。この旧表示はC6-D6の製品Scene切替と回帰確認後に、旧境界素材・選択処理と同じ変更単位で削除する。

Editor構築ツールとPlayerビルド前処理は、18Biomeの仮色土地PNGを`GroundTerrainAtlas`の1ページ、岩壁を`RockWallAtlas`、再制作した14境界Spriteを`BoundaryHighlightAtlas`へ自動登録・再パックする。境界生成ツールはPNGの実画素、14分割、安定Sprite IDを更新し、SampleScene構築は同じ14参照をPresenterへ保存する。各Atlasが複数ページへ分割された場合は、テストまたはビルドを失敗させる。C6でBlock用本番素材を導入する際は、仮色Packableを同じ変更単位で置換する。

- [x] 通常地形と低地の高さ分類をPresentationで判定できる
- [x] 製品液面とShader模様をGround Tilemapから分離し、奥岸上端3pxだけを切り抜ける
- [x] 液体セルをGround Tilemapへ重複配置せず、移動した液面下辺を手前土地で隠せる
- [x] 16×24土地TileをGround Tilemapへ同じ論理セル座標で表示・消去できる
- [x] 16×28岩壁TileをRock Wall Tilemapへ同じ論理セル座標で表示・消去できる
- [x] GroundとRock WallをIndividual ModeかつTop Left順で描画し、奥側の前面を手前ブロックで隠せる
- [x] 通常Resourceを直前方の岩壁有無から背面用Order 2と前面用Order 10へ振り分け、奥の資源を手前岩壁で隠せる
- [x] 7枚のBiome土地を同じ`GroundTerrainAtlas`ページへ、岩壁を別の`RockWallAtlas`へパックできる
- [x] 岩壁セルにもBiome由来の土地ブロックを残せる
- [x] Terrain Front Edge、Rock Wall Front Face、Rock Wall Topの旧TilemapをSceneから削除済みである
- [x] Rock Wall Resource Tilemapを基準面より12 px上へ表示・消去できる
- [x] 持ち上げた岩壁上面へ後付けした連続影で、内部と露出面を表現できる
- [x] 水面模様・マグマ模様の専用Shaderで、Base Tileを維持したまま絶対座標に連続する模様を動かせる
- [x] 液面アニメーションを無効化しても地形分類と岸の位置が変わらない
- [ ] 平面的な地表装飾を`Ground Overlay Tilemap`へ表示し、土地より前、全立体物より後ろへ描画できる
- [ ] 地表装飾をECSまたはセーブ差分から再構築でき、Tilemapを配置状態の正にしていない
- [ ] 1×1および複数タイルの論理占有と、上方向へはみ出す描画Boundsを別々に保持できる
- [ ] 画面下側のセルに接地する物体を手前へ描くYソートが機能する
- [ ] サブチャンクとカメラのカリング範囲が最大描画はみ出し量を含む
- [ ] 木、石、砂をECS状態からResource Tilemapへ反映できる
- [ ] プレイヤー、装備、生物、乗り物をECS状態からPresentationへ反映できる
- [ ] TilemapやGameObjectをゲーム状態の正として使用していない

## 10. 検品用テストマップ

最低でも以下を1画面に配置し、採用前にスクリーンショットを残す。

- [ ] 各地形を16×16タイル以上の面積で敷き、繰り返し模様を確認
- [x] 各Biomeが座標、Seed、チャンクの生成・ロード順にかかわらず、対応する同じBase Tileを表示することを確認
- [x] 同じ単色Base Tileを16×16タイル以上並べても継ぎ目が出ず、マップの賑わいを資源など意味を持つ配置要素で補えることを確認
- [x] 連続する土地では奥側の8 px断面が手前ブロックに隠れ、水・マグマに面した場所では断面が露出することを確認
- [x] Base Spriteと前面断面には外角、内角、左右端の補完Spriteがなく、輪郭が直角のままであることを確認
- [x] 境界ハイライトでは14形状の全外周・内角と90度回転を確認し、斜めだけが欠ける地形でも内角線が途切れないことを確認
- [ ] 土、砂、岩、雪の断面色を持つ16×24土地を淡水・海水・マグマへ重ねて確認
- [x] 16タイル以上続く土地断面で継ぎ目や過度な反復がないことを確認
- [x] 連続する岩壁では奥側の12 px側面が手前ブロックに隠れ、最前列では側面が読めることを確認
- [x] 最前列に露出する岩壁側面の全12 pxが一色で、上端境界線、水平層理、陰影、材質模様を含まないことを確認
- [x] 岩壁上面の大きな菱形模様がセル単位で意図どおり反復し、論理上の正方形グリッドと混同されないことを確認
- [x] 岩壁上面、内部影、岩壁上資源が基準面より12 px上に揃うことを確認
- [x] 岩壁が淡水・海水・マグマへ面する場所で、下層の土地断面が欠けないことを確認
- [x] 同じ岩壁Tileの上で、内部だけが滑らかな黒い影になり、露出面の模様が読めることを確認
- [ ] 淡水・海水の水面Shaderとマグマの網状Shaderを確認し、模様が動いても岸・土地断面・岩壁側面の位置が変わらないことを確認
- [ ] 液面の波紋が上下に動いて見えても、隣接Tileとの継ぎ目や白いフラッシュが出ないことを確認
- [ ] 木、石、砂資源を全地形背景に重ね、輪郭を識別できることを確認
- [ ] 1×1占有物と複数タイル設備を上下に隣接配置し、上方向へのはみ出しとYソートが自然に見えることを確認
- [ ] 接地点は画面外だが上部だけ画面内に見える物体が、カリングで消えないことを確認
- [ ] 立体物が16×16サブチャンク境界をまたいでも欠けないことを確認
- [ ] プレイヤー、装備、配置可能・不可表示を各地形・レイヤーへ重ねて確認
- [x] カメラの最小・標準・最大ズームでピクセルがにじまないことを確認
- [x] Game Viewの実解像度で細部がちらつかないことを確認
- [x] 64×64チャンク境界および16×16サブチャンク境界に、透明な隙間や位置ずれによる継ぎ目がないことを確認。岩壁上面の意図した菱形反復は継ぎ目に含めない

## 11. 制作記録

採用した生成物ごとに追記する。Seedと設定を残すことで、同じ画風の追加素材を作りやすくする。

| 日付 | Asset ID | PixelLab Tool | Seed | 主な設定変更 | 出力ファイル | 採用・却下理由 |
|---|---|---|---|---|---|---|
| 2026-10-15 | `terrain_biomes_base` | 未共有 | 未共有 | 112×24 px、16×24 px×7枚、上面・断面は各一色、Base差分なし | `Terrain/Base/terrain_biomes_base.png` | 単色Base方針、7バイオームへの割り当て、Gameビュー確認に問題がないため採用 |
| 2026-10-15 | `terrain_rock_wall_01` | 未共有 | 未共有 | 16×28 px、上面16 px＋側面12 px。上面全体は大きな菱形の面構成、側面全体は境界線や層理のない一色面 | `Terrain/Base/terrain_rock_wall_01.png` | 菱形模様のセル単位反復と単色側面を意図したデザインとして採用。Import・専用Tile・Atlas・SampleSceneへの適用と、2026-10-16のGameビュー受け入れ確認まで完了 |

## 12. 後続段階で追加するスプライト

次の項目は第1段階の地形生成検証には含めず、仕様上の導入段階が来たときに個別チェックリストとプロンプトを追加する。

- [ ] 小麦と、最初の動物種である鶏など草原固有要素
- [ ] 硬木、樹液、薬草など森林固有資源
- [ ] サボテン、塩など砂漠固有資源
- [ ] 1×1および複数タイルの採取設備、加工設備、保管箱、分配設備
- [ ] 直線・曲がり・分岐・合流ベルト、方向表示、搬送アイテム、水・マグマ用の橋
- [ ] 柵、壁、扉、通常境界から自動変形する搬入口・搬出口
- [ ] 不定形施設の領域輪郭、成立・条件不足・境界破損の各表示
- [ ] 稼働、停止、詰まり、破損の各設備状態
- [ ] 敵対する野生生物、追跡・逃走・威嚇・構造物損傷、任意の防衛設備
- [ ] バイオーム固有の粒子、環境演出、希少資源変異

## 13. PixelLab参照資料

- [Create S-XL image (Pro)](https://www.pixellab.ai/docs/tools/create-sl-image-pro): 固定資源の`Output size`最大辺から64、16、4、1 framesのいずれかを決める
- [Create tiles (Pro)](https://www.pixellab.ai/docs/tools/create-tiles-pro): 各分類1枚のBase地形に使用する。Base地形を混ぜる接続Tilesetには使用せず、`ART-00B`の土地・岩壁境界ハイライトは透明な別素材として制作する
- [Create texture](https://www.pixellab.ai/docs/tools/create-texture): 接続素材と組み合わせる地表テクスチャ。土地・岩壁境界ハイライトへ使用する場合もBase地形へ焼き込まない
- [PixelLab API](https://api.pixellab.ai/v2/docs): Map Objects、Object、Character、Animationを含むツール一覧
