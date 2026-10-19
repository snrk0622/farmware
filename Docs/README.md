# Farmware ドキュメント案内

- 最終更新日: 2026-10-19
- 目的: 仕様、制作資料、開発手順の参照先を一か所にまとめる。

## ゲーム仕様

- [ゲーム仕様 v0.2 — 生物探索・採集サンドボックス](./GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- [ランダムマップ・可変地形仕様 v0.2](./GameSpecification/RandomMapGenerationSpecification.md)

2026-10-19に、直接操作プレイヤー、可変地形、地上・海中・深海、図鑑研究を中心とする方針へ改訂した。記載した仮仕様と現在の実装は区別し、着手順は現行ロードマップを正とする。

## アート制作

- [タイルマップスプライト制作チェックリスト](./Art/TilemapSpriteProductionChecklist.md)

## 開発・検証

- [実装ロードマップ](./Development/ImplementationRoadmap.md)
- [シミュレーション技術仕様](./Development/SimulationArchitectureSpecification.md)
- [WORLD-01A 多層水域の設計試作](./Development/World01AMultiLayerWaterPrototype.md)
- [Codexスキル](./Development/CodexSkills.md)
- [Unity Editorツール](./Development/EditorTools.md)
- [テスト](./Development/Testing.md)
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
