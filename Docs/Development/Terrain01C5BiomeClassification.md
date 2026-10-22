# TERRAIN-01C5 Biome分類と環境境界の実装記録

- 最終更新日: 2026-10-22
- 状態: C5-A 完了
- 対象ブランチ: `terrain-01c5a-biome-definition-validation`
- 正仕様: [バイオーム・生態コンテンツ仕様](../GameSpecification/BiomeEcologyContentSpecification.md)、[ランダムマップ・可変地形仕様](../GameSpecification/RandomMapGenerationSpecification.md)

## 1. C5の目的

TERRAIN-01C5は、旧7Biomeを延命せず、地上18Biomeの候補抽出、特殊優先、整数適性、安定した同点解決、環境境界監査へ一括置換する。C5前の開発用ワールドと生成キャッシュは移行せず、新しい保存スキーマとGeneratorVersionの境界で拒否して再生成する。

最終状態では`BiomeId.None = 0`、新18種を仕様表順の1〜18とする。旧値と数値が一致する1〜7も意味の互換性を持たず、旧enum名、旧7bit動物Mask、旧分類器、橋Biome遷移、旧配色、既存`Weirdness`、互換フォールバック、旧実装専用テストを残さない。

## 2. 変更単位

| 項目 | 目的 | 製品生成への影響 |
| --- | --- | --- |
| C5-A | 新18Biome Definitionと読込検証を固定する | 旧7Biomeの生成結果はまだ変更しない |
| C5-B | 候補・AxisFit・重み・特殊優先・同点解決を純粋計算として実装する | ECS Bufferへはまだ接続しない |
| C5-C | ECS生成順を新分類器へ切り替え、旧生成経路を削除する | 新18Biomeへ非互換切替する |
| C5-D | 動物Mask、状態ハッシュ、Presentation、Editor診断を新IDへ移行する | 全利用側を新18IDへ統一する |
| C5-E | 複数Seed分布、全153ペア境界、旧経路不存在を監査する | C5全体の完了を判定する |

各項目は実装、初心者向け日本語コメント、関連テスト、影響文書、AIレビューを同じ変更単位に含める。人間確認とcommit許可を受けるまでcommitせず、次項目へ自動着手しない。

## 3. C5-Aの実装契約

C5-Aでは、新18種の数値と固定表を旧7種の実行型から分離して先に確定する。

- `BiomeStableIds`へ`None = 0`と新18種の1〜18を定義する。C5-Cの一括切替前は旧`BiomeId`へキャストせず、同じ数値を旧意味で読まない。
- `BiomeDefinition`は地形気温、基礎湿度、標高、木本植生、起伏、火山活動度、海洋影響の許容・代表範囲、8軸重み、8地質適合度、明示フラグ、Definition固有`VariationSalt`をunmanagedな値として保持する。
- 通常比較16種は軸重み合計256、水文特殊2種は合計0とする。淡水湿原・氾濫原と湿地林・沼沢林のHost集合は通常Definitionから独立して保持する。
- `VariationSalt`は18件で一意かつ0以外とし、既存の地形・水文・資源用Saltを再利用しない。
- `BiomeClassificationSettings`は分類器専用のECSシングルトンとする。約4KBの固定表を`WorldGenerationSettings`へ埋め込まず、無関係な既存生成Jobのスケジュール時に値コピーしない。
- `WorldGenerationAuthoring`のBakerは共有既定値を独立Componentとして追加し、Inspectorへ144個の地質適合値を重複保持しない。
- 読込検証はID欠落・重複・範囲外、範囲逆転、気温・湿度・標高の確定許容範囲不一致、重み合計不正、地質欠落、火山地質ゲート不正、Salt重複、Host欠落・重複・水文特殊ID混入を拒否する。

C5-Aでは新Definitionを旧7Biome分類器、ECS生成Buffer、Presentation、動物Maskへ接続しない。これは互換運用ではなく、C5-Cで旧経路を削除して一括切替する前の検証可能な実装境界である。

## 4. C5-Aの検証

追加する`BiomeDefinitionCatalogTests`は次を対象とする。

- 安定ID 0と1〜18の固定値、18件の欠落なし検索。
- 全18行の軸重み、全18×8地質適合度、海洋性林・水文特殊・高山・火山の境界値。
- Definition登録順を反転しても検証と安定ID検索が変わらないこと。
- ID、Salt、範囲、重み、地質、Hostを壊した設定が明示的に拒否されること。
- `VariationSalt`が既存の`WorldGenerationPurposeSalts`と重複しないこと。
- 独立した`BiomeClassificationSettings`の共有既定値に完全なDefinitionが含まれること。

表示、Scene、Prefab、実際の生成BiomeはC5-Aで変更しないため、Gameビューの見た目確認とPlayMode追加テストは対象外とする。人間確認ではUnity Consoleに新規エラー・警告がなく、既存の旧7Biome表示が意図せず変化していないことを確認する。

## 5. 現在の検証記録

- Unity CLI `1.0.0-beta.14`が最新版であることを確認した。
- Unity 6000.3.8f1の再コンパイルは、NUnit互換修正後の最終構成で失敗なしとなった。
- `BiomeDefinitionCatalogTests`は8件すべて成功した。
- World Generation EditMode全体は557件すべて成功し、PlayMode全体は56件すべて成功した。
- 最終Consoleの実状態はコンパイル失敗なし、エラー0件、警告0件だった。Pipelineの履歴Bufferには修正済みの途中コンパイルエラーと既存テスト中の警告が残るが、現在のConsole件数には含まれない。
- Pipeline package `0.8.0-exp.1`には`0.9.0-exp.1`の更新があるが、本変更に不要なため更新していない。
- 2026-10-23に、旧7Biome表示へ意図しない変化がないことの人間確認を完了した。

差分レビューではP0・P1のブロッカーなし、空白エラーなし、C5-Aの旧7Biome生成結果への接続なしを確認した。
