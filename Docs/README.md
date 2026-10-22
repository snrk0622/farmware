# Farmware ドキュメント案内

- 最終更新日: 2026-10-21
- 目的: 仕様、制作資料、開発手順の参照先を一か所にまとめる。

## ゲーム仕様

- [ゲーム仕様 v0.2 — 生物探索・採集サンドボックス](./GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- [ランダムマップ・可変地形仕様 v0.2](./GameSpecification/RandomMapGenerationSpecification.md)
- [バイオーム・生態コンテンツ仕様 v0.53](./GameSpecification/BiomeEcologyContentSpecification.md)

2026-10-19に、直接操作プレイヤー、可変地形、地上・海中・深海、図鑑研究を中心とする方針へ改訂した。バイオーム・生態コンテンツ仕様には、TERRAIN-01Bで採用した地上18バイオーム、水域17生息地、気候とBiomeの責務、モチーフ地域、地上同士の隣接、水域の内包・海岸境界・深度遷移、初期スポーン規則、鳥類を分けた初回登場候補、作物・牧場動物の入手地域、全地上バイオーム共通の木材・石材・鉄鉱石と特徴鉱物を記録する。v0.42では、同じ地形ブロックItemを穴・液体へ置くとGround、既存Ground上へ置くとWallになるCore Keeper型の設置・回収に加え、地形ブロックを再配置専用Item、クラフト素材を独立した有限資源Itemとした。v0.43では、見出しの「確定」が候補案内部の値を指すこと、移行帯対象表が全隣接セルへの禁止条件ではないこと、海洋影響の代表計算値を明記した。v0.44では、寒冷・乾燥・低地で候補が重なるステップとツンドラ・極地荒原の双方向直接隣接を追加した。v0.45では、冷涼・中湿度・低地で候補が重なる温帯草原とタイガの双方向直接隣接、同一入力で成立する橋候補の検証、Biome・地質混在窓の適格セル面積で按分する資源密度式を追加した。v0.46では、高温多湿な山地で候補が重なるモンスーン林と高山岩地の双方向直接隣接、および初回28資源の変更禁止な配置優先度と競合解決全順序を追加した。地質資源は露出、Ground内、Wall内、巨大鉱床の4形態で生成し、壁内鉱床はBlockと資源を同時回収、Ground内鉱床は地面を維持、巨大鉱床は将来の電力不要採掘設備へ接続する。採用済み仕様と段階実装の差は各TERRAIN-01C実装記録で明示し、着手順は現行ロードマップを正とする。

v0.48では「環境として自然なら直接接してよい」を採用した。通常比較は最適候補を選び、第三Biomeの強制を廃止する。接続例は非排他的な景観資料とし、湿原の同一セルHost条件は専用表へ分離する。境界の自然さは気候・地形・地質・水文から作り、全153ペアの環境差を監査する。[隣接監査](./Development/BiomeAdjacencyAudit.md)に旧橋方式の17組の問題、新方針の選択結果、実生成での未検証事項を記録する。コードへの反映はTERRAIN-01C系列で行う。

v0.49では、海岸の9地点平均標高が中心タイルの陸海判定と食い違う場合でも自然に海へ排水できるよう、隣接する海洋流出口を代表標高にかかわらず最優先で選ぶ。海洋流出口は下流なしの終端とし、海洋以外の高標高セルへの流出は禁止したまま循環を防ぐ。コードへの反映と回帰確認はTERRAIN-01C3で行う。

v0.50では、エンドレスワールドの上流全域をオンデマンド走査しないよう、対象までの距離・流向キー・座標で正規化した最大4,096水文セルを集水面積と流出寄与の共通入力にする。Level 0〜6の階層Summaryは同じ結果をまとめる最適化とし、空間境界で河川値をリセットしない。打切り状態を保存・診断し、キャッシュなしでも1論理チャンクの探索量を有限にする。コードへの反映と計測はTERRAIN-01C3で行う。

v0.51では、下流終端の探索も最大4,096流向辺へ制限し、Level 0〜12の二分Downstream Summaryで同じ経路を再利用する。上限内で海または閉鎖末端へ届かないセルは`LongDrainage`へ固定し、下流チャンクのロード後も分類を変えない。これにより、上流・下流の両方向で1論理チャンクのオンデマンド水文作業量を有限にする。コードへの反映と計測はTERRAIN-01C3で行う。

v0.52では、上流集合の4,096件目についても直接逆辺8件を必ず検査し、フロンティアが空でも未選択の上流が見つかれば`CatchmentTruncated = true`とする。階層Summaryも外部入口セルを保持し、一本鎖の4,097件目を見落として不完全な閉鎖流域から固定湖面を作らない。逆辺判定数の既存上限は増やさない。コードへの反映と回帰確認はTERRAIN-01C3で行う。

v0.53では、8×8水文セルをそのまま水面にせず、安定Anchorと有限経路探索による河道、閉鎖流域末端からのFlood Fillによる湖面候補へ細分化する契約を追加した。地下水用の局所地質補正は、亀裂、風化帯、粘土層、帯水層を独立Saltの連続値として生成し、基礎地質Definitionを書き換えず透水性・保持力へ合成する。コードへの反映と回帰確認はTERRAIN-01C4で行う。

## アート制作

- [タイルマップスプライト制作チェックリスト](./Art/TilemapSpriteProductionChecklist.md)

## 開発・検証

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
