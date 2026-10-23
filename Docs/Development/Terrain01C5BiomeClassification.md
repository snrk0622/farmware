# TERRAIN-01C5 Biome分類と環境境界の実装記録

- 最終更新日: 2026-10-23
- 状態: C5-A・C5-B完了
- 対象ブランチ: `terrain-01c5b-biome-classification`
- 正仕様: [バイオーム・生態コンテンツ仕様](../GameSpecification/BiomeEcologyContentSpecification.md)、[ランダムマップ・可変地形仕様](../GameSpecification/RandomMapGenerationSpecification.md)

## 1. C5の目的

TERRAIN-01C5は、旧7Biomeを延命せず、地上18Biomeの候補抽出、特殊優先、整数適性、安定した同点解決、環境境界監査へ一括置換する。C5前の開発用ワールドと生成キャッシュは移行せず、新しい保存スキーマとGeneratorVersionの境界で拒否して再生成する。

最終状態では`BiomeId.None = 0`、新18種を仕様表順の1〜18とする。旧値と数値が一致する1〜7も意味の互換性を持たず、旧enum名、旧7bit動物Mask、旧分類器、橋Biome遷移、旧配色、既存`Weirdness`、互換フォールバック、旧実装専用テストを残さない。

## 2. 変更単位

| 項目 | 目的 | 製品生成への影響 |
| --- | --- | --- |
| C5-A | 新18Biome Definitionと読込検証を固定する | 旧7Biomeの生成結果はまだ変更しない |
| C5-B1 | 通常候補のゲート・AxisFit・重み・候補別Variationを純粋計算として実装する | ECS Bufferへはまだ接続しない |
| C5-B2 | 全通常候補の安定順位・上位2候補・候補なしを純粋計算として実装する | ECS Bufferへはまだ接続しない |
| C5-B3 | 火山中核・水文特殊の優先と最終選択を純粋計算として実装する | ECS Bufferへはまだ接続しない |
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

## 6. C5-B1の実装契約

C5-Bは初心者向けコメント、境界テスト、文書を含めると差分約500行を大きく超えるため、通常候補の個別評価、通常候補の安定順位、特殊優先と最終選択の3 commit単位へ分ける。C5-B1では、1つの通常Definitionを独立に評価する純粋計算だけを固定する。

- 海面未満、通常スコアを使わないDefinition、水文特殊Definition、無効な安定ID・地質・Variation設定、各連続軸の許容範囲外を候補から除外する。
- 亜熱帯海洋性林は海洋影響段階5、火山・地熱地帯は活動的火山適性を持つ地質と火山活動段階4を必須とする。段階5の火山中核はC5-B3の特殊優先へ残し、通常候補として受理しない。
- `AxisFit`は許容範囲端を0、代表範囲を65,535とする両端包含の台形型とし、片側傾斜がない範囲でも0除算しない。
- 8軸の積と加算は符号付き64bitで行い、重み合計256の理論最大16,776,960を超えない。許容端でScoreが0でも候補資格は維持する。
- `BiomeVariation`は既存Biome Warp後座標へ256タイル・2オクターブを適用する。既存`SampleFractalNoise`の半減比重により256・128タイルを2：1で合成し、Definition固有Saltを使う。
- 評価結果は安定ID、8軸適性、Score、Variationを保持し、C5-B2の順位決定とC5-Dの診断が同じ計算結果を再利用できるようにする。

C5-B1はECS生成、旧7Biome分類、状態ハッシュ、Scene、Prefab、Presentationを変更しない。通常候補全件の順位と候補なしはC5-B2、火山中核・水文特殊・最終選択はC5-B3で実装する。

## 7. C5-B1の検証

`DeterministicBiomeClassifierTests`は、台形型の許容端・傾斜・代表範囲・範囲外、片側傾斜なし、最大64bit Score、0点候補、Warp後負座標とDefinition固有SaltのVariation、海岸・火山地質ゲート、海面直下と閾値上、不正な役割・設定・地質IDの拒否と部分結果非公開を検証する。`BiomeDefinitionCatalogTests`はVariation格子256タイルと2オクターブの共有既定値、および不正値の拒否を追加確認する。

2026-10-23の接続済みUnity 6000.3.8f1 Editorでは、厳格再コンパイルがエラー0件・警告0件、`DeterministicBiomeClassifierTests` 8件、`BiomeDefinitionCatalogTests` 8件、World Generation EditMode 565件がすべて成功した。全体テスト中の警告6件は既知のテスト用System Group順序属性であり、分類器のJob・Allocator警告ではない。Unity CLI `1.0.0-beta.14`がPipeline `0.8.0-exp.1`へ旧コマンド名を照会した互換エラー7件も内容確認後にConsoleから消去し、最終実状態はコンパイル失敗なし、エラー0件、警告0件である。

差分レビューではP0・P1のブロッカー、空白エラー、旧7Biome生成への接続、Scene・Prefab・Presentation変更がないことを確認した。純粋計算だけの変更であるためPlayModeと性能計測は実行せず、旧7Biome表示へ意図しない変化がないことの人間確認を残す。

C5-B1は2026-10-23に人間確認とcommit許可を受け、commit `390fa41e`へ記録した。

## 8. C5-B2の実装契約

C5-B2では、C5-B1が返す全通常候補を次の変更禁止順序で比較し、最終選択前の首位と診断用次点を純粋計算として保持する。

1. `Score`降順。
2. `Score`が完全同点の場合だけ`CandidateVariation`降順。
3. 両方が同点の場合は安定Definition ID昇順。

- VariationをScoreへ加算せず、Score差が1でもある候補の順位を地域ノイズで逆転させない。
- 全順序で1件ずつ挿入し、追加配列、管理確保、全件ソートを使わずに首位と次点だけを更新する。3件目以降も診断用の候補総数へ数える。
- Definition登録順を反転しても、首位、次点、各候補の軸適性、Score、Variation、候補総数が一致する。
- 通常候補が0件の場合は`false`と既定値を返し、部分的な首位や件数を公開しない。候補1件の場合は首位だけを有効とし、次点なしを明示する。
- タイルごとのHot Pathで固定表全体を二重走査しないため、`BiomeClassificationSettings`はC5-Aの読込時に`ValidateSettings`を通す契約とする。分類時はDefinition件数とVariation固定値を軽量確認し、各Definitionの候補評価と順位挿入を1回の走査で行う。

C5-B2はECS Buffer、旧7Biome分類、状態ハッシュ、Scene、Prefab、Presentationへ接続しない。活動的火山中核、水文特殊Biome、通常首位からの最終選択はC5-B3へ残す。

## 9. C5-B2の検証

`DeterministicBiomeClassifierTests`へ、Score差1とVariation最大値の比較、Score同点時のVariation降順、Variation同点時の安定ID昇順、最高Scoreが3候補で完全同点の異なる挿入順、実Definition 3候補の登録逆順、上位2候補と候補総数、海面下の通常候補なしと結果既定値を追加した。

2026-10-23の接続済みUnity 6000.3.8f1 Editorでは、厳格再コンパイルが失敗なし、`DeterministicBiomeClassifierTests` 12件、World Generation EditMode 569件がすべて成功した。全体テスト中の警告6件は既知のテスト用System Group順序属性であり、分類器のJob・Allocator警告ではない。Unity CLIがPipeline `0.8.0-exp.1`へ旧コマンド名を照会した既知の互換エラーも内容確認後にConsoleから消去し、最終実状態はコンパイル失敗なし、エラー0件、警告0件である。

差分レビューではP0・P1のブロッカー、空白エラー、仕様と異なる順位分岐、旧7Biome生成への接続、Scene・Prefab・Presentation変更がないことを確認した。ゲーム仕様は既存の確定順位を実装しただけで変更せず、`EditorTools.md`はEditor機能変更なし、`Docs/README.md`は文書追加・移動なしのため更新不要と判断した。

純粋計算だけの変更で製品生成と表示を変えないため、PlayModeと性能計測は実行しない。2026-10-23の人間確認では、旧7Biome表示へC5-B2起因の意図しない変化がないことを確認し、commit許可を得た。Shift高速移動時の一時的な描画空白は、製品生成・表示へ未接続のC5-B2による回帰ではなく、既存の1チャンク先読みを移動が上回る条件として切り分けた。速度別先読みと生成待ちの対応は`VEHICLE-01B`・`VEHICLE-01F`およびロードマップ末尾の性能確認方針に既に含まれるため、本変更へ混在させない。

## 10. C5-B3の実装契約

C5-B3では、C5-B2の通常候補順位を診断用に保持したまま、1タイルの最終Biomeを次の変更禁止順序で選ぶ。

1. 火山・地熱地帯Definitionで適合度が正の地質、`SupportsActiveVolcanism`、火山活動度段階5のすべてが成立した場合は、活動的火山中核を最優先する。
2. 火山中核が不成立で、`FreshwaterConnected`・`WetlandTileEligible`・`LargeWetlandExtent`の3フラグがすべて成立した場合だけ、水文特殊Biomeを評価する。
3. 木本植生ポテンシャル39,320以下は淡水湿原・氾濫原、39,321以上は湿地林・沼沢林を対象とする。対象Definitionの地形気温・基礎湿度・標高・木本植生許容範囲と、特殊判定前の通常首位が専用Host集合に含まれることを追加条件とする。
4. 特殊条件が不成立なら通常首位を維持する。通常候補が0件なら`None`へ読み替えず、既定結果を伴う明示失敗を返す。

最終結果は安定ID、`BiomeSelectionReason`、特殊判定前の通常首位・次点・候補総数を保持する。水文昇格は隣接Biome、現在の降雨、人工水路、現在の土壌水分を参照せず、固定自然水文と同一タイルの通常首位だけで決めるため、Definition登録順、候補列挙順、チャンク生成順、ロード範囲へ依存しない。

C5-B3はECS Buffer、旧7Biome分類、状態ハッシュ、Scene、Prefab、Presentationへ接続しない。新18Biomeへの生成切替と旧経路削除はC5-C、選択理由を含む診断表示はC5-Dで一括して行う。

## 11. C5-B3の検証

`DeterministicBiomeClassifierTests`へ、火山中核が水文特殊より優先されること、火山Definitionの地質適合・活動的火山特性・活動度段階5の3条件、木本植生39,320／39,321境界、淡水接続・湿原タイル資格・広域規模、対象Definitionの気候許容範囲、許可Hostと非許可Host、通常候補なし、Definition登録逆順での不変を追加した。特殊選択後も通常首位・次点・候補総数が維持されることを同じ結果から確認する。

2026-10-23の接続済みUnity 6000.3.8f1 Editorでは、再コンパイルが失敗なし、`DeterministicBiomeClassifierTests` 18件、World Generation EditMode 575件がすべて成功した。最終Console実状態はコンパイル失敗なし、エラー0件、警告0件である。純粋計算だけの変更で製品生成と表示を変えないため、PlayModeと性能計測は実行しない。

差分レビューではP0・P1のブロッカー、空白エラー、旧7Biome生成への接続、Scene・Prefab・Presentation変更がないことを確認した。ゲーム仕様は既存の特殊優先、Host、環境境界を実装しただけで変更せず、`EditorTools.md`はEditor機能変更なし、`Docs/README.md`は文書追加・移動なしのため更新不要と判断した。人間確認ではConsoleに新規エラー・警告がなく、旧7Biome表示へ意図しない変化がないことを確認する。

C5-B3は2026-10-23に人間確認とcommit許可を受け、C5-B全体を完了した。

## 12. PR #45レビュー修正: 海面下限とBiome標高範囲の整合

PR #45のレビューで、`SeaLevelThreshold`を20,000未満へ設定すると、海面以上20,000未満に地上18Biomeの候補が存在しないことを確認した。地上Biomeの確定標高範囲を保存済み海面に合わせて暗黙拡張せず、仕様表の固定下限20,000を正とする。

- `WorldGenerationSettings.MinimumSupportedSeaLevelThreshold = 20,000`を設定、分類器、Authoringで共有する。
- 連続環境設定検証は20,000未満の海面を拒否し、分類器を直接呼ぶ経路でも同じ入力を明示失敗にする。
- Inspectorは海面入力範囲と`OnValidate`補正を20,000以上へ揃え、見かけ上選べる値が後続生成だけを失敗させる状態を残さない。
- 海面を20,000より高くする既存の調整範囲は、固定Biome標高範囲内の陸地だけが残るため維持する。上限は従来どおり保存済み低地上限（既定36,000）とする。
- 海岸の湖Anchorと岩壁境界の既存テストは、無効な低海面へ依存せず同じ責務を検証する入力へ更新する。

回帰確認は、海面19,999の設定・個別候補・全候補順位の拒否、Inspector値0から20,000への補正、海面20,000以上を使う海岸Anchorと岩壁境界を対象とする。新18BiomeはまだECS生成へ未接続のため、旧7Biomeの製品表示結果を変更しない。

2026-10-23の接続済みUnity 6000.3.8f1 Editorでは、厳格再コンパイルがエラー0件・警告0件、`DeterministicBiomeClassifierTests` 19件、`DeterministicEnvironmentGeneratorTests` 13件、`DeterministicSurfaceWaterGeneratorTests` 10件、Authoring海面補正1件、岩壁境界PlayMode 1件がすべて成功した。World Generation全EditModeも実行したが、既存`AnimalPopulationGenerationTests.GenerationBoundary_MatchesAcrossFramePartitionsAndRequestCreationOrder`の実行中にPipeline既定300秒を超えて中断され、全体件数の合否は得られなかった。これは個別テスト失敗ではなく実行基盤のタイムアウトであり、同テスト中の既知のSystem Group順序属性警告以外に新しいコンパイル警告は確認していない。

## 13. C5-CのECS生成切替契約

C5-Cで`GeneratorVersion`を2へ更新し、C5前の版を移行・読み替えせず設定検証で拒否する。1チャンクの生成順は次で固定する。

1. `DeterministicChunkGenerator`で連続環境、地質、陸海、岩壁候補だけを生成する。この中間値は`BiomeId.None`とし、新18Biomeを推測しない。
2. C3の基礎水文とC4の河道・湖・地下水・泉・自然冠水・湿原規模を完成する。
3. 最終Jobで`DeterministicBiomeClassifier.TrySelectFinalBiome`を呼び、火山中核、水文特殊、通常首位の順に地上Biome 1〜18を確定する。海面下だけは`BiomeId.None`と海水を公開する。
4. C6までの暫定表現として、火山中核のマグマ、C4固定表流水、基礎岩壁、陸地の順に`GroundType`へ反映し、木・石・砂の旧3資源を最終地形から生成する。C6／C8でBlock・Liquid・28資源へ置換するまでの有限な中間契約である。

1タイルで旧7Biome、淡水、マグマまで同時生成していた経路、第三Biomeを強制する橋遷移、旧`Weirdness`の設定・保持・ハッシュ、旧岩地マグマ特例、12×8建築可能領域と近傍資源を要求する初期スポーン探索は削除する。初期スポーンは、原点から最大半径2チャンクを段階的に要求し、完成済みECS Bufferの初期スポーン許可Biome、乾いた通行可能地面、資源なしを近い順に選ぶ。チャンク0がタイル0〜63だけを含む非対称性を考慮し、候補発見後も次の未探索リングが持ち得る最短距離が現在候補以下なら探索を継続して、負側を含む走査範囲全体の最近傍を確定する。見つからない場合はScene座標や分類前地形へ退行せず明示失敗とする。

新18Biomeの本番Tileは未制作であるため、C5-Cでは安定ID 1〜18に専用の16×24仮色Tileを各1枚用意する。`ChunkTilemapPresenter`は18要素すべてを必須とし、未設定IDを旧7Biomeの近似Tileや先頭Tileへフォールバックしない。旧7枚のフォールバック土地は削除し、`GroundTerrainAtlas`も18枚の仮色Spriteへ置換する。C6でBlockパレットと本番素材を導入する際に、このBiome直結の仮表示を置換する。

C5-Cの新IDを製品生成へ接続した時点で旧型のままではコンパイルまたは誤判定になる利用側に限り、`AnimalBiomeMask`の18bit化、鶏Definition、状態ハッシュ、Gameビューの基本色、Map Viewerの`Terrain`／`Biome`を同じ変更単位で更新する。これは新旧IDの並行経路を残さないための最小垂直切片であり、C5-D全体の完了ではない。候補・選択理由を含む診断表示はC5-D、153ペア監査はC5-Eへ残す。旧基礎タイルを使うBiome分布監査はC5-Eまで明示停止し、地質監査のBiomeクロスも偽の`None`を出力しない。

## 14. C5-Cの検証状態

2026-10-23の接続済みUnity 6000.3.8f1 Editorで、厳格再コンパイルのエラー0件・警告0件を確認した。World Generation EditMode初回は565件中561件が成功し、4件が旧`GeneratorVersion = 1`のテスト入力と旧版の特定座標分布への依存により失敗した。旧版を受理する互換分岐は追加せず、現行版を明示入力し、有限範囲から最初の閉鎖流域を選ぶようテストを更新した。さらに動物分布監査の入口でも旧版を拒否する1件を追加し、最終再実行ではEditMode 566件が283.05秒で全件成功した。

PlayMode初回は56件中53件が成功し、SampleScene統合3件が旧契約に依存して失敗した。初期スポーンへ近傍資源を要求しない現行仕様に合わせて開始チャンクの資源表示を完了条件から外し、鶏が新`TemperateGrassland`に存在しない領域では0羽も正常とした。GeneratorVersion 1固定の期待値を現在版2へ更新し、C4水文Jobへ実時間を渡しながら初期チャンクと左隣の先読み完了を待つようにした。最終再実行ではPlayMode 56件が49.18秒で全件成功した。

旧7Biome名・旧7bit Mask・`Weirdness`・旧開始領域探索・GeneratorVersion 1の製品経路が残っていないこと、`git diff --check`に問題がないことを検索と差分確認で検証した。最終Console実状態もコンパイル失敗なし、エラー0件、警告0件である。Gameビューの18仮色、淡水・海水・マグマ・岩壁の優先、初期スポーン、チャンク境界、Map Viewerとの一致は2026-10-23に人間確認を完了した。確認時に判明したMap Viewerの`Terrain`／`Biome`だけが旧768サンプル上限を使う問題も、完成チャンク使用モード共通の上限へ統一して修正済みである。

PR #46の追加レビューでは、境界Halo、岩壁影、Rock Wall開発者可視化がC5-C最終化後の隣接Bufferではなく、基礎のLand／RockWall候補を再生成していたことを確認した。製品表示と診断表示は中心と周囲8チャンクの完成Bufferを借用する共通近傍へ切り替え、ワールド内の必要Bufferが揃うまで表示を待ち、intワールド端だけを非接続として扱う。これにより隣接チャンクで最終Water／Magmaになったセルへ土地境界を欠落させたり、液体上へ岩壁影を延ばしたりしない。C5-Dには候補・選択理由を含む残りの利用側整理とMap Viewerモード定義の集約を残し、C5-Eで分布と全153ペア境界を監査する。

追加レビュー対応後は、厳格再コンパイルがエラー0件・警告0件、World Generation EditMode 556件が343.99秒、PlayMode 56件が141.53秒で全件成功した。全PlayModeの早い生成順で実行時チャンクTilemapが既に存在する場合も、Scene保存されたWorld Grid直下9テンプレートだけを数えるようテスト契約を明確化した。製品のScene、Prefab、生成設定、パッケージは変更していない。Gameビューで最終Water／Magmaを挟むチャンク境界のハイライト・影・Rock Wall診断が一致することは人間確認へ残す。

PR #46の後続レビューでは、段階4でも通常候補になる火山・地熱地帯へ、既定値0の追加活動度下限だけで露出マグマを生成していたことを確認した。露出マグマは仕様どおり活動度段階5を必須とし、Inspectorの追加下限は段階5内をさらに狭める場合だけ使う。製品最終化、`Magma`診断の色面、中央HUDは段階5・追加活動度下限・形状密度を評価する共通判定へ揃え、候補色の凡例を削除済みの淡水抑制表現から`Candidate`へ更新する。

後続レビュー対応の自動確認では、厳格再コンパイルがエラー0件・警告0件、`DeterministicBiomeChunkFinalizerTests` 7件、製品チャンク最終化を含む`WorldGenerationTests` 58件、Magma色を含む可視化PlayMode 1件がすべて成功した。Gameビューの`Magma`モードで段階4の火山周縁が候補色にならず、候補色と中央HUDの`Candidate=Yes`が一致することは人間確認へ残す。

## 15. C5-D1のMap Viewerモード契約

C5-D1では、Map Viewerの全27モードについて、表示名、画像1ピクセルを作る実行経路、解像度上限、進捗方式、Hoverへ保持するデータ種別を`WorldGenerationMapViewModeCatalog`へ集約した。実行経路は用途に応じて、軽量な単一タイル生成、C3基礎水文セル、C5-C完成地形チャンク、C4完成水文チャンクの4区分とする。完成チャンクを使わないモードまで重い診断経路へ統合しない。

Windowの初回生成、リサイズ、カーソル中心ズーム、ToolbarズームはCatalogの同じ解像度方針を参照する。Samplerは同じCatalogから実行経路と進捗方式を決め、Hoverは適用済み画像のモードが宣言するデータ契約だけを読む。これにより、`Terrain`または`Biome`だけが旧768サンプル上限へ戻る、画像とHoverが異なるモードを読む、完成チャンク生成中に行進捗だけを表示する、といった利用側の分岐漏れを防ぐ。

## 16. C5-D2のBiome選択診断

C5-D2では、製品最終化済みの地上タイルに対し、同じSeed、GeneratorVersion、固定環境、地質、C4自然水文、18Biome Definitionを使って`FinalBiomeSelection`を再取得する純粋な診断入口を追加した。再取得した安定IDが完成タイルの`BiomeId`と一致する場合だけ結果を公開し、不一致をもっともらしい診断として表示しない。海域は地上Biome選択の対象外と明示する。

Gameビューは既存`Biome`モードの中央1タイルだけを必要時に再評価する。Map Viewerは`Terrain`／`Biome`画像生成時に最終理由、候補数、通常首位・次点、Score、Variation、両候補の8軸適性をManaged値へコピーし、Hover移動ごとの完成チャンク再生成を避ける。両画面は同じ文字列整形を共有し、表示側の簡易分類、旧7Biome近似、ECSへ常駐する診断専用Componentを追加しない。

C5-Cですでに`AnimalBiomeMask`の独立18bit化、状態ハッシュ、18Biome基本色、旧変換switchと旧表示フォールバックの削除を完了しているため、C5-Dはそれらを重複実装しない。C5-Eには複数Seed分布、全153ペアの境界環境差・接続理由、旧経路不存在の最終監査を残す。

2026-10-23の接続済みUnity 6000.3.8f1 Editorでは、再コンパイルが成功し、`WorldGenerationMapViewerTests` 56件が117.19秒、World Generation EditMode 560件が275.26秒、PlayMode 56件が134.72秒で全件成功した。旧7Biome列挙名・旧7bit Mask・表示モード範囲分岐を検索し、現行製品コードに残っていないことと、`git diff --check`に問題がないことを確認した。最終Console実状態もコンパイル失敗なし、エラー0件、警告0件である。2026-10-23に、実Map ViewerとGameビューの通常サンプル、同じ地上タイルの選択理由・候補・8軸適性の一致、海域の対象外表示について人間確認を完了した。その後のPR #47レビューで、通常の5桁適性値では8軸1行の右端がGameビューで欠け、Map Viewerでは自動折り返しが事前予約高を超えることを確認した。最終IDと理由、候補名と順位値を意味単位の2行、各候補の8軸を3軸・3軸・2軸の3行へ分け、診断を常に12行、Map Viewer Hoverを最大17行の予約へ更新した。修正後は再コンパイル、対象57件、12pt実フォント幅371.06px／内容幅414px、Consoleエラー・警告0を確認した。実Gameビューの960×540描画領域では、追加した12行と既存中央情報を18Biome凡例の下へ縦積みすると下端が見切れたため、全モード共通の右上1パネルを画面高へクリップし、内容が超過する場合だけ表示する縦スクロールバーのドラッグまたはクリックで末尾まで確認する方式へ変更した。ホイール入力はHUDで取得せず、モード変更時は先頭へ戻す。内容高は実際の文字列の行数から求め、固定行数とのずれによる末尾の空白を除いた。配置回帰では長いBiome内容の超過量、短い一般モードでバーを隠すことと移動量0を固定し、対象59件が119.428秒で全件成功した。実Gameビューでは短い`Soil`モードのバー非表示と最終行直下の余白、Biome超過時のバー表示を確認し、Consoleはエラー0件・警告0件だった。Biome末尾の最終余白について人間の実表示再確認を残す。

同日の追加レビューで、スクロール中の文字がHUD外へ出ることを確認した。内容座標だけを移動する初回修正では実表示が改善しなかったため、文字Styleの既定`Overflow`とパネル枠ぎりぎりのクリップ範囲も原因候補として除いた。文字Styleを`Clip`へ変更し、上下枠より2px内側へ固定した明示的なクリップ範囲で文字と凡例を囲む方式へ変更した。クリップ契約を含む対象3件が0.011秒、更新後の対象60件が119.526秒で全件成功した。スクロール中の実表示について人間確認を残す。

続くレビューで、Map Viewerの旧最小高480pxでは地図最小240pxと17行Hover診断280pxだけでも収まらず、ヘッダー、操作欄、状態欄を含むと親Panelのクリップで地図または診断が欠けることを確認した。WindowとUXML Rootの最小寸法を720×700ポイントへ統一し、最小サイズ契約をEditModeテストへ追加した。厳格再コンパイルはエラー0件・警告0件、追加テスト1件は0.04秒、更新後の`WorldGenerationMapViewerTests` 61件は118.3秒で全件成功した。実Windowでは最小サイズでヘッダー、操作欄、地図、Hover予約領域、状態欄が同時表示されることをAI確認し、17行の実データ表示について人間確認を残す。
