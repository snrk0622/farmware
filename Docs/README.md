# Farmware ドキュメント案内

- 最終更新日: 2026-10-28
- 目的: 仕様、制作資料、開発手順の参照先を一か所にまとめる。

## ゲーム仕様

- [ゲーム仕様 v0.2 — 生物探索・採集サンドボックス](./GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- [ランダムマップ・可変地形仕様 v0.2](./GameSpecification/RandomMapGenerationSpecification.md)
- [バイオーム・生態コンテンツ仕様 v0.54](./GameSpecification/BiomeEcologyContentSpecification.md)

2026-10-19に、直接操作プレイヤー、可変地形、地上・海中・深海、図鑑研究を中心とする方針へ改訂した。バイオーム・生態コンテンツ仕様には、TERRAIN-01Bで採用した地上18バイオーム、水域17生息地、気候とBiomeの責務、モチーフ地域、地上同士の隣接、水域の内包・海岸境界・深度遷移、初期スポーン規則、鳥類を分けた初回登場候補、作物・牧場動物の入手地域、全地上バイオーム共通の木材・石材・鉄鉱石と特徴鉱物を記録する。v0.42では、同じ地形ブロックItemを穴・液体へ置くとGround、既存Ground上へ置くとWallになるCore Keeper型の設置・回収に加え、地形ブロックを再配置専用Item、クラフト素材を独立した有限資源Itemとした。v0.43では、見出しの「確定」が候補案内部の値を指すこと、移行帯対象表が全隣接セルへの禁止条件ではないこと、海洋影響の代表計算値を明記した。v0.44では、寒冷・乾燥・低地で候補が重なるステップとツンドラ・極地荒原の双方向直接隣接を追加した。v0.45では、冷涼・中湿度・低地で候補が重なる温帯草原とタイガの双方向直接隣接、同一入力で成立する橋候補の検証、Biome・地質混在窓の適格セル面積で按分する資源密度式を追加した。v0.46では、高温多湿な山地で候補が重なるモンスーン林と高山岩地の双方向直接隣接、および初回28資源の変更禁止な配置優先度と競合解決全順序を追加した。地質資源は露出、Ground内、Wall内、巨大鉱床の4形態で生成し、壁内鉱床はBlockと資源を同時回収、Ground内鉱床は地面を維持、巨大鉱床は将来の電力不要採掘設備へ接続する。採用済み仕様と段階実装の差は各TERRAIN-01C実装記録で明示し、着手順は現行ロードマップを正とする。

v0.48では「環境として自然なら直接接してよい」を採用した。通常比較は最適候補を選び、第三Biomeの強制を廃止する。接続例は非排他的な景観資料とし、湿原の同一セルHost条件は専用表へ分離する。境界の自然さは気候・地形・地質・水文から作り、全153ペアの環境差を監査する。[隣接監査](./Development/BiomeAdjacencyAudit.md)に旧橋方式の17組の問題、新方針の選択結果、実生成での未検証事項を記録する。コードへの反映はTERRAIN-01C系列で行う。

v0.49では、海岸の9地点平均標高が中心タイルの陸海判定と食い違う場合でも自然に海へ排水できるよう、隣接する海洋流出口を代表標高にかかわらず最優先で選ぶ。海洋流出口は下流なしの終端とし、海洋以外の高標高セルへの流出は禁止したまま循環を防ぐ。コードへの反映と回帰確認はTERRAIN-01C3で行う。

v0.50では、エンドレスワールドの上流全域をオンデマンド走査しないよう、対象までの距離・流向キー・座標で正規化した最大4,096水文セルを集水面積と流出寄与の共通入力にする。Level 0〜6の階層Summaryは同じ結果をまとめる最適化とし、空間境界で河川値をリセットしない。打切り状態を保存・診断し、キャッシュなしでも1論理チャンクの探索量を有限にする。コードへの反映と計測はTERRAIN-01C3で行う。

v0.51では、下流終端の探索も最大4,096流向辺へ制限し、Level 0〜12の二分Downstream Summaryで同じ経路を再利用する。上限内で海または閉鎖末端へ届かないセルは`LongDrainage`へ固定し、下流チャンクのロード後も分類を変えない。これにより、上流・下流の両方向で1論理チャンクのオンデマンド水文作業量を有限にする。コードへの反映と計測はTERRAIN-01C3で行う。

v0.52では、上流集合の4,096件目についても直接逆辺8件を必ず検査し、フロンティアが空でも未選択の上流が見つかれば`CatchmentTruncated = true`とする。階層Summaryも外部入口セルを保持し、一本鎖の4,097件目を見落として不完全な閉鎖流域から固定湖面を作らない。逆辺判定数の既存上限は増やさない。コードへの反映と回帰確認はTERRAIN-01C3で行う。

v0.53では、8×8水文セルをそのまま水面にせず、安定Anchorと有限経路探索による河道、閉鎖流域末端からのFlood Fillによる湖面候補へ細分化する契約を追加した。地下水用の局所地質補正は、亀裂、風化帯、粘土層、帯水層を独立Saltの連続値として生成し、基礎地質Definitionを書き換えず透水性・保持力へ合成する。TERRAIN-01C4-Aで表流水、C4-B1で局所地質補正、C4-B2で地下水・自然土壌飽和、C4-Cで湖水収支・泉のECS接続まで反映した。自然冠水・湿原規模・診断表示は後続項目で行う。

v0.54では地上18種を維持し、アフリカサバンナを熱帯サバンナ・疎林、亜熱帯島嶼林を亜熱帯海洋性林、乾燥低木地・ユーカリ疎林を乾燥低木地・疎林へ改称した。地域モチーフは`RegionVariant`へ分離する。TERRAIN-01C5で新18種へ安定`BiomeId` 1〜18を割り当て直し、C5前の開発用ワールドと生成キャッシュは移行せず再生成する。旧7種の分類・表示・動物Mask・互換経路は残さず、新方式へ置換する。

## アート制作

- [地形タイル表現仕様 — 掘削世界・立体地形版](./Art/TerrainTileArtSpecification.md)
- [PixelLab GUI制作リファレンス](./Art/PixelLabGuiToolReference.md)
- [タイルマップスプライト制作チェックリスト](./Art/TilemapSpriteProductionChecklist.md)

現行の材質タイルは、`GroundTop`、任意の透明な`FaceBoundaryOverlay`、Ground崖面／Wall前面で共用する`Face`の三部構成とする。各部位は固定差分数ではなく、可変長配列で管理して候補を均等確率で選び、同じワールド位置では毎回同じ差分を選ぶ。Wall上面は同じ材質の`GroundTop`を使い、表示時だけsRGB各成分を係数0.85で暗色化する。断面は1段目から`Face`を使い、境界ありではEditorがGround用の元色Overlay版とWall用の暗色化Overlay版を事前合成し、Runtimeで用途に対応する完成Spriteを1枚描画する。パレットは全体に低コントラストとし、各HEXの横へ用途と使用比率を記載する。Ground／Wallの形状境界ハイライトは製品表示へ使わず、Wallの読み分けには暗色化、Face、固定接地影、`Shadow Caster 2D`に加え、Wall前面2段をまたいでAlpha 35%から0%へ連続減衰する32pxの共通折れ目影を使う。Wallの接続判定では材質IDを比較せず、異なる材質のWall同士も一つの連続領域として扱い、共有辺へ境界、Face、影の切れ目を出さない。Ground崖面は3段配置し、乾燥崖は穴上端から48 pxかけて断面の可視Alphaを100%から0%へ連続変化させ、4段目以降を完全な暗黒とする。淡水・海水は黒暗化せず、直下床ありでは青Overlayを上端3 pxのクリップ後45 pxでAlpha 50%、床なしでは現在材質の断面を6段目まで背後へ続けて93 pxでAlpha 100%へ連続変化させ、SurfaceからDeepSeaの内容を透視させない。淡水・海水・マグマ面はタイル自体をずらさず、画面奥側がGroundまたはWallへ接する先頭セルだけ上端3 pxをクリップして低く投影する。手前を除く奥・左・右の接触辺には、液体種別の基準色を使った1 pxの静止接触線へShaderの明暗変化を重ねる。縦岸は16 px連続とし、左右内角と裏側終端クリップを含む7bit接続形状を有効地形から生成する。奥が同種液体で斜め奥に岸がない場合は、地面の裏側へ回り込む終端として該当する側線の上端3 pxだけを隠す。左右は独立に判定し、連続する縦岸や反対側を切らない。水際は素材1px単位の透明を含む濃度4階調・毎秒4コマで、固定した波の中心ごとに位相・周期をずらしてその場で寄せ引きする明暗とし、一方向へ流さない。水域の基底色は淡水・海水ごとに一定として先に不透明描画し、その上へ実際の奥岸の真下だけに水中断面を投影する。各断面の岸からの距離を素材1px中心で評価し、可視Alphaを「元SpriteのAlpha×(1−水の被覆率)」とする。被覆率は上端3pxの下から床あり45pxで50%、床なし93pxで100%へ増え、既存の直線岸の見た目を維持する。異なる岸のY位置を平均せず、隣列からの断面延長、共有四隅距離、陸地専用段数、左右だけの追加減衰は使わない。水色だけの出力は岸のY位置によらず均一となる一方、実際の断面の端そのものは正しい形状として残す。上端3pxを切るのは水面側だけで、そこに見える地面の縁は残す。直下床は48px以深へLit表示を維持した可視Alpha 50%で重ねる。乾燥崖の48px減衰は水中断面へ適用しない。反射は対象の接地点・高さから投影して水面との交差だけをMesh描画し、固定中心ごとに位相・周期を変える輪郭を含む低FPS・整数pxの横揺れを加え、固定した水域内だけへ描く。水際の追加手描き素材や4フレームAnimatedTileは使わない。41材質の境界有無は、今回の64×48 px制作画像を正とする境界あり11材質・なし30材質の確定表に従い、同じ境界Overlay入力をGround崖面とWall前面で共有する。淡水・海水の半透明深度表現と不透明なマグマは地形タイル表現仕様を正とする。材質タイルはAsepriteまたはPixeloramaで1 px単位に手描きし、PixelLabは使用しない。PixelLab GUI制作リファレンスは、動物、固定資源、施設など材質タイル以外のSprite制作で必要な場合だけ参照する。従来チェックリストの16×24土地、16×28岩壁、不透明液面8px投影、専用`WallTop`、共通暗色内部の記載は既存実装の履歴として参照する。

41材質の`GroundTop`、`FaceBoundaryOverlay`、`Face`の色・使用比率・模様方針は、地形タイル表現仕様第7章を正とする。通常材質のWall暗色化係数は0.85とし、Wall上面とWall用`FaceBoundaryOverlay`へ同じ係数を使う。`FaceBoundaryOverlay`と`Face`は`GrassLoam`で上面より暗く、部位内コントラストを抑えた配色を比較試作する。上面と同じ色相へ統一せず、材質構造を維持したまま、人間確認後に同じ明度関係を残り40材質へ適用する。

## 開発・検証

C6-Dの旧表示削除・製品表示の最終AI確認と人間確認結果は[製品切替の最終検証](./Development/Terrain01C6DFinalizationReport.md)に記録する。製品カタログ必須のチャンク別表示へ統一し、Map Viewerの編集差分は定義GUIDで診断用IDへ対応付ける。

通常カメラ移動とMap Viewerの遅延は[地形表示の遅延調査](./Development/Terrain01C6D5PerformanceReport.md)に計測条件・変更・未解決範囲を記録する。通常表示は必要入力が揃ったサブチャンクから反映し、Map Viewerは製品Burst Jobによる初回生成と最大32チャンクの自然地形再利用へ変更した。通常表示の改善は人間確認済みで、Development Buildの通常・高速の局所比較も実施した。通常移動の表示待ちは0となり、高速で残る待ちとCPU・GC・表示メモリ増加を記録した。表示の最終AI確認と人間確認を経てC6-D5は完了した。正式な対象機器・最大Viewportの性能評価と高速乗り物への対応は後続項目として分離する。

TERRAIN-01C7への申し送りとして、火山バイオームなどでの水の温泉化、マグマと水の接触による黒曜石などへの自動変換を未確定の追加仕様として検討する。[実装ロードマップ](./Development/ImplementationRoadmap.md)と[マップ仕様](./GameSpecification/RandomMapGenerationSpecification.md)に検討範囲を記録している。

マグマはHDR表面色とBloom Intensity 0.4を採用し、Wallに隠れない表示上の岸に、絶対座標の固定3×3タイル区画ごとの橙色照明（強度0.12）を再利用する。カメラ移動で同じ岸の光源位置・強度を変えない。全体照明の明るさは維持する。調整先と動作確認は[Editorツール](./Development/EditorTools.md#マグマhdr発光bloom岸照明)、採用色と描画契約は[地形タイル表現仕様](./Art/TerrainTileArtSpecification.md#マグマ網状模様)を参照する。

短い水面模様は淡水・海水専用の独立Shader・Tilemap・Materialで追加画像なしに描画する。素材1px・低FPSで固定位置の線が非同期に出現・消失する。水際の調整済みMaterialと水中断面の合成は維持する。調整先は`TerrainWaterSurfacePattern.mat`とし、詳細は地形タイル表現仕様の「短い水面模様」、検証はTestingの`TerrainLiquidRenderingTests`を参照する。

- [実装ロードマップ](./Development/ImplementationRoadmap.md)
- [シミュレーション技術仕様](./Development/SimulationArchitectureSpecification.md)
- [WORLD-01A 多層水域の設計試作](./Development/World01AMultiLayerWaterPrototype.md)
- [WORLD-01B 多層水域の製品契約](./Development/World01BMultiLayerWaterContract.md)
- [WORLD-01C レイヤー付き座標の実装記録](./Development/World01CLayeredCoordinates.md)
- [WORLD-01D 操作主体とワールド境界の実装記録](./Development/World01DWorldPlayerBoundary.md)
- [TERRAIN-01A ブロック定義と有効セルの実装記録](./Development/Terrain01AEffectiveTerrain.md)
- [TERRAIN-01C1 連続環境軸の実装記録](./Development/Terrain01C1ContinuousEnvironmentAxes.md)
- [TERRAIN-01C2 地質基盤の実装記録](./Development/Terrain01C2GeologyFoundation.md)
- [TERRAIN-01C3 表面水系の実装記録](./Development/Terrain01C3SurfaceHydrology.md)
- [TERRAIN-01C4 地下水・湿地水文の実装記録](./Development/Terrain01C4GroundwaterWetlandHydrology.md)
- [TERRAIN-01C5 Biome分類と環境境界の実装記録](./Development/Terrain01C5BiomeClassification.md)
- [Codexスキル](./Development/CodexSkills.md)
- [Unity Editorツール](./Development/EditorTools.md)
- [テスト](./Development/Testing.md)
- [Biome隣接の全ペア監査](./Development/BiomeAdjacencyAudit.md)
- [MAP-02D 性能計測](./Development/Map02DPerformanceReport.md)
- [PERF-GATE-A 終了時ComputeBuffer警告の切り分け](./Development/PerfGateAComputeBufferReport.md)

## 仕様転換前の履歴

以下は過去の判断・実装・検証を残す資料であり、新規実装の要件には使用しない。

- [旧工業ゲーム仕様 v0.1](./GameSpecification/2026-10-19_factory_concept_archive.md)
- [旧マップ生成基盤 v1・設定・表示の記録](./GameSpecification/RandomMapGenerationSpecification_v1_Archive.md)
- [旧実装ロードマップ・commit／PR別の実装検証記録](./Development/ImplementationRoadmap_2026-10-19_Archive.md)

## 更新ルール

commit前には、コードだけでなく、この一覧から変更の影響を受けるドキュメントをすべて確認して最新化する。
更新対象がない場合も、仕様、Editorツール、テスト、アート要件、開発手順へ影響しないことを確認する。
具体的な必須手順は、リポジトリ直下の [AGENTS.md](../AGENTS.md) に従う。
