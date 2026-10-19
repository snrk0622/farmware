# テスト

- 最終更新日: 2026-10-19
- 対象: `Assets/Farmware/Tests/`

2026-10-19にゲーム方針を改訂した。第3章・第5章の既存テストと計測履歴は当時の実装に対する記録として保持し、今後の適用範囲は第4章と[新ロードマップ](./ImplementationRoadmap.md)を正とする。旧ANIMAL-01C〜Eの未着手計画はECO-01へ移し、PERF-GATE-BはECO-01C後・ECO-01E前、Cは段階的な縦切り、DはRELEASE-01Bで実施する。

## 1. 用途

自動テストは、共通Assemblyの依存方向、ECS Systemの更新境界、ワールド生成の決定性、チャンク境界、地形分類、Sceneと表示の接続が、修正によって壊れていないことを短時間で検出するために使用する。
地形バランスや見た目の良さを自動テストだけで決定せず、[Unity Editorツール](./EditorTools.md)とGameビューの確認を組み合わせる。

## 2. 実行方法

1. Unity Editorで `Window > General > Test Runner` を開く。
2. 純粋計算、設定、ECS Systemは`EditMode`タブから対象テストまたは対象Assemblyを実行する。
3. Scene、SubScene、Camera、Tilemap、HUD、表示連携は`PlayMode`タブから対象テストまたは対象Assemblyを実行する。
4. 失敗時は最初の原因となった例外とStack Traceを確認し、後続の連鎖的な失敗だけで判断しない。
5. 実行後はConsoleにテスト外の新規例外や警告が残っていないことも確認する。

Unity Editorを開いたまま外部プロセスから同じプロジェクトをバッチ起動しない。Libraryロック、再Import、未保存Sceneとの競合を避けるため、接続済みUnity CLIまたは開いているEditorのTest Runnerを使用する。

## 3. 現在のテスト構成

### 3.1 EditMode: `Farmware.Tests.EditMode`

#### `SystemGroupBoundaryTests`

`Farmware.Simulation`、`Farmware.Input`、`Farmware.Presentation`、`Farmware.Authoring`の直接参照をUnityのコンパイル情報から検査し、Simulationへの一方向依存を保証する。各境界型が意図したAssemblyへコンパイルされること、共通Initialization／Simulation／固定Tick／Command Validation／Command Commit／Gameplay／Presentation Groupと関連Systemが対応する親Groupへ1回だけ所属し、CommandからGameplayまでの先頭・末尾・前後順序を宣言すること、`Farmware.Tests.EditMode`がPlayer向けコンパイルから除外されることも検証する。

#### `FixedTickSimulationTests`

Sceneを使わない最小ECS Worldへ描画フレーム相当のDeltaTimeを直接与え、固定Tick用Singletonが各1件作られること、10 Tick/秒でTick未満の端数を保持すること、1フレーム最大4 Tickを超えた整数Tick分の遅延を破棄すること、一時停止中の時間を再開後へ持ち越さないことを検証する。合計時間が同じで追いつき上限内なら、細かい描画フレームと粗い描画フレームで`SimulationTickState.CurrentTick`が一致すること、Tick境界の許容誤差で先行した不足時間を負の端数として保持すること、0の設定値が安全な既定値へ正規化されることも保証する。

#### `PersistentIdTests`

ワールド共通永続IDが1から単調増加し、削除済みIDを再利用しないこと、`ulong.MaxValue`を最後の有効IDとして1回発行した後に停止することを検証する。同一Tickの複数要求は`ScopeOrder`、`MajorOrder`、`MinorOrder`、`Sequence`の安定キー順で割り当て、入力順が変わっても結果が一致すること、同じ地域X/Yと受付順でも異なるLayer相当Scopeなら衝突しないこと、重複キーやID不足では一部だけIDを消費しないこと、次回発行IDと上限到達状態をSnapshotへ保存・復元できることも保証する。

#### `DefinitionCatalogTests`

32桁とハイフン付きのGUID文字列が同じ128bit値へ正規化され、全0と不正文字列を拒否することを検証する。定義入力順に依存せずGUID昇順で1始まりの実行時IDを割り当て、定義間のGUID参照を整数IDへ解決すること、重複GUID、参照切れ、参照先分類不一致、連結されていない参照範囲ではBlob Assetを作成しないことも保証する。複数定義の参照を平坦化した後も、検証エラーの参照番号はInspector上の定義内添字へ戻す。テスト内で作成したPersistent Blob Assetは各テストが所有し、終了時にDisposeする。

#### `WorldSessionBoundaryTests`

WORLD-01Dの128bit WorldId、ワールド内PlayerId、開発用単独プレイヤー初期化、ローカルInput Context、共通主体検証、停止・再開窓口をSceneなしで検証する。GUID表記の正規化と無効値拒否、外部から一部だけ供給されたセッション状態を推測で補修しないこと、別WorldId、未登録PlayerId、重複PlayerIdを状態変更前に拒否すること、正規プレイヤーが停止中にも再開できること、不正主体の停止要求がPause状態を変更しないことを保証する。

#### `SimulationCommandQueueTests`

Sceneを使わない最小ECS Worldで、受付番号が1から単調増加し、`ulong.MaxValue`を1回使用した後は0へ周回しないことを検証する。別WorldId、未登録PlayerId、一時停止中はCommand Entityも受付番号も消費せず、正常な次Commandが未使用番号を取得する。同一固定Tickへ成功、競合失敗、成功の3件を順に投入し、各CommandがValidation、Commit、ECB再生、結果確定まで完了してから次へ進むこと、失敗Commandが状態を部分変更しないこと、Gameplay Groupが同Tick内の最終確定状態だけを読むことを保証する。Validation／Commit担当不在と複数Validation担当の拒否、未完了Commitが記録したECBの非再生、未完了Commitが連続した後も成功Commandだけを反映できること、完了結果を現在の描画フレーム中保持して次フレーム先頭で破棄する寿命も確認する。

#### `SimulationStateHashTests`

`xxHash3-128`へ渡す整数の固定バイト表現、セクションIDの昇順・重複禁止、Complete後の再利用禁止を検証する。共通状態では必須Singletonの欠落・重複、正規化前の固定Tick設定、未処理Command、無効な永続IDアロケーター、定義カタログ、WorldId、PlayerId一覧を検査し、不完全な値を比較へ使わないことを保証する。Player Entityの作成順はハッシュへ影響せず、WorldId／PlayerId変更は検出し、無効または重複PlayerIdを拒否する。同じ定義を異なる入力順から構築した場合もGUID順に同じハッシュとなることを確認する。さらに、同じCommand列とTick数を、Entity IndexとChunk配置をずらした2つのWorldで`Run`と`ScheduleParallel`へ分けて実行し、永続ID順へ正規化した機能状態ハッシュが一致すること、論理値の変更と永続ID重複を検出することを保証する。初回リリース前で永続化した比較基準がない現在は、WORLD-01CのLayer追加とWORLD-01DのWorld／Player追加もVersion 1の定義更新として扱う。リリース後、または保存済みハッシュを異なるビルド間で比較する運用を始めた後は、入力形式の変更時にSchema Versionを増やす。

#### `AnimalPopulationStateHashTests`

動物個体群Seed、Layer付き地域別乱数状態・次回受付順・初期化Tick、個体の永続ID・種類・Layer付き整数位置・群れID・外見差分を機能固有セクション1025へ追加する。地域Entityと個体Entityの作成順を逆転しても同じハッシュになること、同じ地域X/Yの別Layerを重複扱いしないこと、Layerだけの変更と将来の抽選結果を変える地域乱数状態の変更を検出すること、存在しない群れ先頭ID、循環する所属、別群れのメンバーへの所属、別種・別Layerの群れ先頭への所属、必須Componentが欠けた野生動物、動物種カタログに存在しない種ID、種定義の上限外となる外見差分番号を正式な状態として受理しないことを検証する。群れ先頭自身が自分の永続IDを群れIDに持つ正常な自己参照は受理する。

### 3.2 EditMode: `Farmware.WorldGeneration.Tests`

#### `WorldLayerWaterContractTests`

WORLD-01Bの製品用`WorldLayer`と`WorldLayerWaterContract`を、Sceneを使わない純粋計算で検証する。固定保存ID、固定3層の隣接関係、川・池を通常水域に固定して海の沖合だけを深水域にする分類、陸地・通常水域・沖合深水域の各層媒体、未知値拒否、地形優先の上下境界合成を確認する。地上水の直下を1階層掘ると同じ固定Tickで自動浸水し、更新後にUnderwaterへ遷移できること、固体は排水や外水接続だけでは掘削されないことも保証する。

構造的に妥当な未ロード先だけを`NeedsPreparation`として再判定へ分離する。未ロードでも固定レイヤーID、座標、隣接関係を先に検証し、不正レイヤー、地上から深海への直接移動、異なるX/Y、水平の遠隔座標について不要な生成・読込を要求しない。同X/Yの隣接水域だけが上下移動でき、固体、乾燥室、地形・水密境界を拒否する。同一レイヤーの水平境界は共有辺だけを候補とし、int端で差分を周回させない。水と乾燥室の水平移動は排水稼働中だけ直接許可し、停止時は媒体更新を先に要求する。PLAYER-01の連続8方向移動、DIVE-01の装備・占有・保留Commandは後続テストで追加する。

#### `WorldGenerationTests`

ワールド生成の純粋計算とECS Systemを、Sceneを開かずに検証する主要テスト群である。

| 分類 | 用途 |
| --- | --- |
| 座標・シード・ハッシュ | 負座標の床除算、文字列シード、生成順に依存しないハッシュ、全用途のPurposeSaltが一意であることを保証する。 |
| ノイズ・チャンク境界 | ノイズの連続性と、隣接チャンクが同じワールド座標規則を使うことを保証する。 |
| バイオーム | 7バイオームの分類、既定スケール、移行帯、軽量サンプルでの出現を保証する。 |
| 水域 | 気候による川・池の補正と、森林・湿地で池が確認できる分布を保証する。 |
| マグマ | 火山のマグマ比率、陸地の残存、淡水抑制、岩地の希少マグマ、歪な輪郭を保証する。 |
| 岩壁 | 水域上の抑制、適度な被覆率、チャンク境界、凹凸、通路を保証する。 |
| 地形配置属性 | 陸地は通常設備＋通常ベルト、水は橋、マグマは橋＋耐熱必須、岩壁と未知IDは配置不可になることを純粋計算で保証する。 |
| 資源 | 地形適性、最低間隔、群生半径、競合解決、決定性を保証する。 |
| 建築可能領域・初期スポーン | 地形を上書きせず、必要面積と資源条件を満たす地点を決定論的に記録する。初期位置用設定が3×3未満の領域を拒否し、記録された中心周囲9セルがすべて通常設備建築可能であることも検証する。探索Job実行中に既存地点が現れた場合も、Job完了後に一時結果を解放してSingletonを二重作成しないことを保証する。 |
| ECS生成・ストリーミング | 生成要求、同時生成上限、範囲外キャンセル、保持、非表示化、再有効化を保証する。同じチャンクX/Yの別Layerを既存要求として再利用せず、Surface要求へ別層Entityを混ぜないことも確認する。旧Observerの未設定Layerと逆順矩形をSurfaceの正規化結果としてSingletonへ反映し、未生成レイヤーへの切替では実行中Jobの完了を待って旧Surfaceチャンクを解放し、新規要求を作らないことを検証する。Camera Bridgeは外部から選択されたObserver Layerをカメラ移動後も保持し、PresenterはLayer変更フレーム内に旧Tilemapと途中Queueを即時消去する。32bit絶対タイル座標域の正負端では、先読み・保持余白を安全に変換できる論理チャンク範囲へ収め、反対側へ整数周回したチャンクを要求しない。境界ハイライトの1セル近傍と岩壁影の8セル余白は範囲検査付き座標変換を使い、ワールド外を反対端の地形として参照せず非接続にする。初期スポーン、チャンク要求、チャンク生成がUnity標準Groupへ直接所属せず、用途に対応するFarmware共通Groupを使うことも確認する。 |

`EquipmentBuildableRegionLocator_OneThousandSeedsFindDefaultRegion`は、狭い既定探索を1,000シードで調べる通常回帰テストである。
`EquipmentBuildableRegionLocator_OneHundredSeedSoakTest`は、最大17×17チャンクの資源探索を100シードで行うため`Explicit`指定された負荷テストであり、通常の修正確認では自動実行しない。

#### `TerrainNeighborHaloUtilityTests`

分類用外周1セルの一括生成を検証する。通常・負座標・遠方・int端の四隅の7地点について、連続サブチャンク範囲136通りで必要な外周セルを毎回準備し、旧セル単位生成と全フィールドが一致することを保証する。各回で出力を不正値にして未更新参照を検出する。別シード・別チャンクでの同一配列再利用、内部ECS確定値の優先、不正な配列長と範囲の拒否も確認する。4サブチャンクずつの反映では生成数100／36／36／100、内部1サブチャンクだけなら0となり、外周全260セルを毎回生成する退行も検出する。ワールド端の非接続値は`WorldGenerationTests.TerrainNeighborHalo_WorldEdge_TreatsOutsideNeighborAsDisconnected`で検証する。比較計測は前処理時間を除外せず、見た目と停止感は別途人間が確認する。

#### `WorldCoordinateConversionTests`

MAP-02Aの純粋な座標変換について、表示原点が相対セル`(0, 0)`へ対応すること、正負座標とint最小・最大の差を失わないこと、絶対タイルから相対セルを経由した往復で元の値を復元することを保証する。WORLD-01Cでは同じX/Yのセル・チャンクがLayerごとに別値となり、Layer付き変換でも負座標の床方向除算を維持することを追加確認する。連続した相対ワールド座標はセル境界の正負両側で床方向へ丸め、表示原点を変更しても逆変換後の絶対タイルが変わらない。NaN、無限大、表示原点加算後にint範囲外となる入力は誤った座標へ周回させず拒否する。

#### `TerrainPlacementRulesTests`

通常設備、通常ベルト、非耐熱橋、耐熱橋を陸地、水域、マグマ、岩壁へ配置する全16組を、生成済み`TileGameplayFlags`と共通`TerrainPlacementRules`だけから判定する。水域では両方の橋、マグマでは耐熱橋だけを許可し、非耐熱橋は通常の地形不可と区別できる`HeatResistanceRequired`を返すことを保証する。未指定・未定義の配置対象と、`AllowsBridge`を持たず耐熱要件だけを持つ不整合な入力も許可せず、入力した`GeneratedTile`を変更しないことを確認する。

#### `WorldGenerationDistributionAuditTests`

Editorの分布監査と同じAnalyzerを小さな固定サンプルで2回実行し、バイオーム・基礎地形・土壌・水域の集計漏れがないことと、署名・レポートが決定論的に一致することを保証する。
広域のバランス確認はテストへ組み込まず、`Farmware > World Generation > Analyze Distribution`から実行する。

#### `AnimalPopulationGenerationTests`

WORLD-01Cでは、同じSeed・地域X/YでもLayerが異なれば独立した地域乱数状態となり、Surface候補がSurface位置を保持することを検証する。既存Surfaceの乱数系列は変更しない。

新規Worldへ非0の個体群Seedを1件だけ作り、復元済みSeedを上書きしないことを確認する。全タイルが有効な地域では密度8の目標数、3〜6羽の群れ、最大徘徊距離、位置重複なし、外見差分範囲を検証する。6羽を一群で置けない3タイルずつの分断地形でも、群れサイズを3羽まで縮めて2群を作り、目標6羽を補充できることを回帰確認する。さらに、4羽を先に確定すると1羽不足する5タイル＋3タイルの分断地形では、有限バックトラックにより5羽＋3羽へ戻して目標8羽を満たすことを確認する。さらにSeed 1と8つの適格座標を使い、先行メンバーが後続群れの必須タイルを塞ぐケースでも、メンバーの組合せを選び直して全8羽を配置すること、群れサイズ・徘徊距離・重複回避・受付順・外見範囲が有効で、再実行時の候補と地域乱数状態が一致することを保証する。完全配置できず部分補充へ戻る地形では、既存候補と既存占有を保持し、失敗分岐の候補や受付順が残らないことも検証する。同じ個体群Seedと地域状態では候補が一致し、地形が同じでも個体群Seedが異なれば配置が変わること、負座標の地域位置、BiomeとGroundの両条件、面積比例を確認する。ECS Systemでは逆順に作った地形チャンクへ地域座標順の永続IDを割り当て、初期化済み地域の再ロードで個体やIDを追加しないことを保証する。固定Tick停止中にPresentationが完成チャンクを`Active`または`Inactive`へ進めた場合も、再開後に地域状態、8羽、処理済みタグを作る回帰テストを含める。

木・石・砂で予約されたセルを避け、有効地形面積から求めた目標数は変えず、空いている8セルへ8羽を配置することをSystemテストで検証する。資源Bufferの欠落・4095件の未完成状態では地域状態・処理済みタグ・個体・IDを確定せず、4096件へ完成した後のTickで補充できることも保証する。

`GenerationBoundary_KeepsPopulationIndependentOfWorkerCompletion`は、実際のBurst地形Jobを2件ずつ2バッチ投入し、片方を先に完了・もう片方を先に完了・強制完了なしの3条件を、通常進行と一時停止で比較する。sleepによるタイミング推測はせず、反射は実物JobのHandle読取に限り、本番の公開経路と固定Tick境界を実行する。通常フレームでは完成枠だけを公開せず、停止中もバッチ全件を公開してから枠を解放すること、IDはその時点で消費されないこと、Tick 12・13の各地域の初期化時刻と、全個体ID・群れID・抽選状態・次回IDを含むハッシュが一致することを保証する。`GenerationBoundary_RunsAtFixedTickEntry`は、公開Systemの所属とTick加算後・Command前の順序属性を確認する。地形単独Worldの既存非同期テストは引き続き通常回帰へ含める。

再レビューのフレーム分割反例には`GenerationBoundary_MatchesAcrossFramePartitionsAndRequestCreationOrder`を追加した。最初から4つの同じ生成要求を用意し、親Simulation Groupと実物のRate Managerを通して0.2秒×1フレーム・0.1秒×2フレーム・0.05秒×4フレームを比較する。最後の条件では要求Entity作成順も逆転する。各条件で2 Tick後に4地域が揃い、座標順の先頭2地域がTick 1・残り2地域がTick 2に初期化され、ID・群れID・抽選状態を含むハッシュが一致することを保証する。修正前は単一フレーム条件で期待4地域に対し2地域となる失敗を確認した。

#### `AnimalPositionViewportCacheTests`

同じX/YにSurface／Underwater個体が存在しても、選択Layer側だけを可視キャッシュへ含めることを検証する。

1,000／10,000／100,000の画面外個体と4つの同Chunk内個体を用意し、可視矩形内の3タイルだけを保持すること、初回以外は不変Chunkの範囲更新が0件・可視Chunkの位置読取が4件となることを確認する。負座標・包含境界・Camera範囲変更・Simulation Systemによる位置変更・Entity破棄・索引消去も検証する。`VisibleCache_ReportsScalingComparison`は`Explicit`の局所計測で、旧全件配列化と新しいECS Chunk範囲索引を同じWorld・矩形で3回暖機後24回比較し、CPU平均・managed allocation・読取件数を記録する。通常回帰では実行せず、開発者表示の変更時に明示実行する。人工配置・Editorの結果であり、全個体移動・実際の探索順によるChunk混在・製品Playerの性能合格へ外挿しない。

#### 生成境界のPlayer検証履歴と再検証

この自動回帰とEditor局所計測だけでは、固定Tick公開境界のDevelopment Player性能を確認済みにしない。2026-10-19に修正commit`abb0e373`後の次作業として、修正前`b2713628`との同条件Player比較を10ケース・17,915前面フレームで実施した。通常／拡大Viewport、通常／高速移動、停止10秒・再開20秒で可視未完成は0件・0秒、停止中のTick・次回ID・個体群は不変、再開後の追加も確認した。生成境界待ち最大12.656msとフレーム間隔最大約32.0msを含む費用を記録し、性能改善・製品性能合格とは扱わない。製品コードは修正commitと完全一致へ戻したため、同commitで成功したEditMode 296件・PlayMode 53件は再実行せず、比較ビルドと復元後のコンパイル確認を追加検証とした。既知のビルド警告・終了時ComputeBuffer警告は両版で残る。続いて修正後Playerの5ケースを開発者向けに再生し、「問題ないように見えます」との確認とcommit・push許可を得た。今回PRの局所回帰と表示・停止感の受け入れは完了とし、製品最大Viewport・長時間・対象機器の性能合格とは分離する。条件・終了条件は[ロードマップのANIMAL-01B残検証](./ImplementationRoadmap_2026-10-19_Archive.md#animal-01b-残検証生成公開境界のdevelopment-player回帰)、実測値・成果物・制約は[性能計測の追補](./Map02DPerformanceReport.md#修正commit後のdevelopment-player比較条件)を参照する。

2026-10-19の投入境界・可視キャッシュ修正後は、World Generation EditMode 225件・Common EditMode 76件・PlayMode 53件と、明示局所計測1件が成功した。同日、開発者から「問題ありません」とcommit許可を得て、今回修正のEditorでの人間確認も完了した。上記`abb0e373`のPlayer比較・人間確認は同commitの履歴であり、今回変更のPlayer性能を確認済みとはしない。

続いて修正commit`b401ab41`後の最初の検証作業として、比較元`45401b9e`とのPlayer再比較を10ケース・17,879前面フレームで実施した。可視未完成・計測中エラー・警告は全ケース0で、停止中のTick・ID・地域・個体数不変と再開後の初期化を確認した。最大間隔61.368msと境界待ち最大12.690msを記録し、性能合格とはしない。初期地域・個体数と実時間の停止解除Tickには前後差があり、同一Tick入力の決定論試験と混同しない。製品コードとテストは修正commitと一致へ戻したため、上記の自動回帰は再実行せず、ビルド・Player計測・復元後コンパイル確認を追加検証とした。同日、開発者から「問題ないように見えます」と今回Playerの表示・停止感の確認とcommit・push・レビュー返信・スレッド解決の許可を得て、最大間隔・待機費用の制約を残したうえで今回範囲を受け入れた。PR #23マージ前の今回検証は完了とするが、後続ゲートの製品性能合格とは分離する。条件・費用・成果物・制約は[性能レポート](./Map02DPerformanceReport.md#固定tick投入修正後のdevelopment-player再比較)、完了条件は[ロードマップ](./ImplementationRoadmap_2026-10-19_Archive.md#animal-01b-再レビュー追補フレーム分割と可視位置キャッシュ)を参照する。

#### `AnimalPopulationDistributionAuditTests`

Runtimeと同じ資源予約を生成して候補から除外するEditorのAnalyzerを、負座標を含む2組のSeed・18地域で2回実行し、適格タイル・目標数・生成候補が存在すること、群れ契約違反と位置重複が0件であること、署名と集計値が一致することを保証する。約100万タイルの既定監査は通常テストへ含めず、`Farmware > World Generation > Analyze Animal Population`から明示実行する。

#### `WorldGenerationMapViewerTests`

WORLD-01Cでは、UnderwaterがLayer付きの未生成結果となり、Surface Generatorを複製せず固定の未接続色を返すことと、UXMLにLayer選択欄が存在することを検証する。

Map Viewerの全ピクセルが同じ座標へ直接`DeterministicChunkGenerator.GenerateTile`した結果と一致することを保証する。Terrain表示とBiome表示はどちらもSoilTypeを反映せず、同じ論理Biomeなら標準・肥沃・砂質のいずれも同じ所属色を保つことを検証する。
あわせて、負座標とY反転を含むPointer座標変換、同じ表示ピクセル内のPointerが同じ代表タイルを返すこと、生成TextureとCanvasの表示解像度が異なるときの縦横比維持とリサイズ後の再生成判定、Pointer・パン・グリッド用座標変換とカーソル中心ズーム、ツールバーの＋・－Buttonによる中心座標不変のズーム、偶数寸法Textureを再生成した直後のHoverがSide Panelと同じ中心タイルを示すこと、奇数・偶数Textureでのグリッド・原点と地形サンプル境界の一致、Bakerと共有するAuthoring設定Snapshotのシード変換、新規・旧丸め値のどちらでもAuthoringと純粋計算の主要な既定気候閾値が一致することを検証する。湿度閾値をInspector上限へ設定した場合にも、乾燥・森林・湿地の境界が厳密な昇順を保つことを保証する。

#### `TerrainLayerPresentationUtilityTests`

16×24土地の上面16px・断面8px・Pivot Y 2/3、16×28岩壁の上面16px・側面12px・Pivot Y 2/7、液面を8px下へ移す`-0.5セル`をSceneなしの純粋計算として検証する。淡水・海水・マグマを別の液面アニメーションへ分類する規則も保証する。

#### `TerrainBoundaryHighlightUtilityTests`

8方向の256入力が斜め接続制約によって47状態へ正規化され、完全接続を除く46状態が14基本形と時計回り90度回転へ割り当てられることを検証する。左右反転を使わない回転方向、画像の左から右への形状表、異なる土地Biome・岩壁を陸続きとして扱う規則、水・マグマ境界、斜めだけが欠ける内角、岩壁領域の境界もSceneなしの純粋計算として保証する。

#### `WorldGenerationFallbackArtTests`

Editor構築ツールが生成した7種類の16×24土地PNGと16×28岩壁PNGを直接読み込み、上面と前面の全ピクセルが一枚の不透明ブロックとして存在することを検証する。旧土・砂・岩・雪断面と旧岩壁側面PNGが残っていないことも保証する。

#### `WorldGenerationSpriteAtlasTests`

Sprite Packer ModeがV2であること、土地Atlasが7バイオームの本番用Textureだけ、岩壁Atlasが岩壁Textureだけ、境界Atlasが224×16の境界TextureだけをPackableに持つこと、3 AtlasがBuilt-in配信とPixel Art向け設定を持つことを検証する。本番用土地シートは左からBiome ID順の7枚へ16×24でSliceされ、各SpriteがPivot Y 2/3を持ち、同名のTileから参照されることも保証する。境界シートは左から安定名の14枚へ16×16でSliceされ、中心Pivot、透明設定、Full Rect、Collider用Fallback Shapeなしを保証する。PNG原稿も直接読み、14区画すべてに可視画素があり、可視画素が不透明白、背景が完全透明で、半透明ぼかしや別色を含まないことを確認する。各区画の外周辺と内角の実画素が`TerrainBoundaryHighlightUtility`の特徴マスクと一致し、北をTexture上端として上下反転していないことも保証する。さらに実際のパック結果を読み、土地7枚と境界14枚がそれぞれのAtlasで同じTextureページへ収まることを確認する。

#### `WorldGenerationSceneBootstrapTests`

`Build Fallback Map Prototype`の対話メニューについて、保存確認をキャンセルした場合は構築を開始しないこと、構築成功後は退避したScene構成を復元すること、構築中に例外が発生しても復元後に例外を呼び出し元へ返すことを保証する。加えて、設定用GameObjectへ`DefinitionCatalogAuthoring`と鶏の共通定義・生成条件を設定する内部処理、および専用Inspectorが動物種GUIDから`0: Chicken`の見出しを解決し、参照切れでは短縮GUID付きの未解決表示を返すことをEditModeで検証する。Scene復元テストでは実際のSceneやアセットを変更せず、保存確認、構築、復元をDelegateで差し替えて制御フローだけを確認する。

### 3.3 PlayMode: `Farmware.WorldGeneration.PlayModeTests`

#### `ChunkLocalTilemapPresentationTests`

`PresentationPreload_`の5ケースは、通常・負座標・遠方について可視優先の予約、外側1チャンクまでの先行準備、Inactive完成データの再利用、画面へ入った表示の維持、範囲外の消去予約、生成中の非公開、同期原点再構築の可視限定と予算4を確認する。共有・ローカル両方式を含む。`NewPresenter_DefaultsToChunkLocalTilemaps`は新規Componentの既定ONを保証する。SampleScene縦切りテストは、設定SubSceneからBakeされた鶏の共通実行時ID、草原・通常陸地条件、群れ、密度、固定Tick移動、外見差分も実際のDefault Worldで確認する。以下の既定OFFという記載は比較時点の履歴であり、現在は既定ONを採用している。

外周地形配列の寿命テストは、Awakeで260要素を確保し、停止・再開では保持し、PresenterのOnDestroyで解放することを実際のComponentで確認する。

`SparseWrite_`の追加6ケースは、実際のPresenter書込経路で空→空だけを省くこと、末尾1セルを含む非空配列を書き込むこと、非空→空の実消去、レイヤーとサブチャンクごとの履歴分離、部分消去とプール再貸出、正負原点変更後の履歴維持、共有方式の無条件書込と方式切替を保証する。空判定時間も性能比較へ含め、見た目・GPUの合否はこれらのテストだけで決定しない。

既定OFFの比較方式について、正方向・負方向と斜め・遠方の自動原点変更で同じTilemap・セル内容・完成済み集合・部分反映番号を保持することを検証する。消去途中ではプールへ返さず、全16サブチャンク消去後だけ空の表示単位を別チャンクへ再利用すること、プール上限0、方式切替、World切替で表示とプールを残さないことも保証する。上下チャンクの相対Z順は現行Orthographic描画の継ぎ目対策であり、XY位置とSorting Orderを変えないことを確認する。実際の描画ピクセル、GPU、メモリ帰属は別途Gameビューと性能計測で確認する。

2026-10-17に比較方式の7ケースを含むPlayMode全38件と、関連ワールド生成EditMode 163件がUnity 6000.3.8f1で成功した。原点変更前後と拡大・負座標・遠方の44画像確認、前面EditorのCPU比較、未検証事項は[性能計測レポート](./Map02DPerformanceReport.md)を参照する。

#### `WorldGenerationScenePlayModeTests`

`SampleScene_StreamsAndDeterministicallyRegeneratesTerrainTiles`は、保存された既定ONを確認し、見本Tilemapではなく実際のチャンク内Tilemapのセルを読む。初期表示、岩壁と下層土地、資源、正負の遠方、アンロード、帰還時の決定論、液面、F3/F4・開発者可視化の既存契約を維持する。さらに、実際のDefault Worldで非0の`AnimalPopulationSeed`、地域状態、共通永続ID・種類・整数位置・群れID・外見差分を持つ野生動物Entityが作られ、`Animal Population`可視化が候補・確定位置Queryを例外なく描画できることを確認する。実際の生成設定と個体座標を照合して資源予約との重複がないこと、候補表示が木のセルを除外し資源なしの草原陸地を受理することも確認する。`SampleScene_SharedModeStreamsAndRegeneratesTerrainTiles`も同じ検証経路をOFFで実行し、旧共有方式の回帰を残す。テスト内の反射は表示参照と候補判定の読取検証に限り、未割当表示をテストから生成しない。

| 分類 | 用途 |
| --- | --- |
| Windowbox | 480×270の整数倍表示と黒帯内の描画矩形を保証する。 |
| F3 | 現在座標、サブチャンク座標、ECSのBiome・GeneratorVersion・ワールドシード、記録済み初期スポーン座標を保証する。初期スポーン探索失敗時は探索中表示を残さず、Scene保存位置を使用中であることを示すHUD文字列も検証する。 |
| F4 | 初期スポーンの赤いTile・Chunk表示と、縦横グリッド線の矩形計算を保証する。32bit絶対タイル座標域の正負端では、最大タイルの外周を含む境界線数を64bitで有限に求め、整数周回による終了しない描画ループを起こさないことも確認する。 |
| Floating Origin | 初期スポーンと明示的な絶対座標移動では所属する64タイル境界を表示原点に選び、負座標では床方向の境界を選ぶことを保証する。Cameraの相対Viewportへ表示原点を加えたObserverが正しい絶対可視チャンク範囲を要求し、SampleSceneの遠方移動では新しい相対Tilemapセルへ地形が表示され、旧原点の資源セルが残らないことも確認する。正負の遠方表示原点では開発者用可視化の絶対Viewport範囲、中央絶対タイル、Tilemapセル、色面が一致する。通常移動では512タイル未満で原点を維持し、閾値到達時は正負とも所属チャンク境界へ自動更新して絶対Camera位置とObserver範囲を維持する。旧原点セルの分割消去が、新原点側の同じ論理チャンクを消さないことも単独テストで保証する。MAP-02Eでは複数回の自動原点変更、東西往復、0境界横断、保持範囲外からの再訪を一つの通常PlayModeテストで継続し、同一座標Entityの非重複、再生成した地形・資源の代表セル一致、生成設定・初期スポーンの不変性を確認する。最大ズームアウトと高速パン時の無停止性は自動テストだけで合否を決めず、ProfilerとGameビューで追加確認する。 |
| 初期スポーン失敗 | Scene保存位置を維持し、失敗状態の確定後にチャンクストリーミングを開始できることを保証する。 |
| Presentation寿命 | Camera GameObjectを維持したDefault World再作成時に新しい初期スポーンを再適用する。Camera Bridgeは新Worldの初期スポーン探索中でも旧WorldのObserverを直ちに破棄し、初期位置の確定前は新Observerを作らず、確定後に1件だけ作り直す。同一Worldでカメラ制御を再有効化した場合はプレイヤーの移動後位置を保つ。正負座標のGrid 64タイル境界へViewport右上端が一致しても、上限側の面積を持たない隣接チャンクを要求しない。Tilemap PresenterがWorld切替または無効化時に表示済み・反映途中・消去途中の旧表示を残さないことも保証する。 |
| 開発者用可視化 | バイオーム、ノイズ、水域、移行帯、岩壁、マグマ、資源、共通配置APIの判定結果を使う地形配置マトリクスの色写像がSimulationを変更しないことを保証する。正負の遠方表示原点でも、相対Viewportから得た絶対範囲と中央タイルを検索し、絶対タイルを正しい相対セルへ投影することを確認する。 |
| 岩壁表示 | チャンク境界をまたぐ内部影が連続したグラデーションになり、一体型岩壁の上面・内部影・露出資源が同じ12px高へ揃うことを保証する。 |
| SampleScene縦切り | SubSceneロード、Authoringと共通既定閾値、初期カメラ、通常12タイル/秒・Shift併用36タイル/秒のカメラ操作設定、512タイルの自動Floating Origin設定、チャンク生成、境界2層を含む9枚のTilemap、Ground・Rock WallのIndividual ModeおよびTop Left順、土地境界Order 1・岩壁境界Order 4と表示高、14 Sprite参照、境界の既定不透明度20%と実行時Tileへの反映、同じBiomeなら標準・肥沃・砂質のいずれも同じプレイヤー向けTileを選ぶこと、SampleSceneの7バイオーム参照が本番用`terrain_biomes_base.png`の同名Spriteへ対応すること、岩壁参照と全表示セルが本番用`terrain_rock_wall_01.png`の専用Tileへ対応すること、Editor専用Soil可視化が3種類を別色にすること、通常資源の岩壁背面用Order 2・前面用Order 10への振り分け、静止液面・アニメーションの-8pxオフセット、液体セルのGround Tilemapとの排他表示、岩壁セルの下層土地、岩壁上資源の12pxオフセット、土地・岩壁Spriteの上下はみ出しを含む可視チャンク範囲、液面4fps設定、アンロード、同一地形の再生成を保証する。EditModeでは、境界選択と資源背面分類の純粋計算を保証する。 |

`AutomaticRebase_PreviousPixelPerfectCameraMatrixDoesNotShiftObserver`は、URP Pixel Perfect Cameraが前回描画時の`worldToCameraMatrix`を保持する条件を明示代入で再現する。Camera Transformと表示原点を更新した後、Bridgeが現在のTransformに基づく行列へ戻してからViewportを変換することを検証する。実際にちらついた絶対X=448・960（チャンク7・15への進入）と負方向の3条件で、可視要求が8チャンク先へ飛ばず正しい範囲を示すことを保証する。描画イベントの発火時刻やGameビューの解像度へ依存せず、前フレームの行列が残っていることも前提条件として検査する。

長距離ストリーミングのPlayMode待機ループは、BatchModeで描画フレームだけが固定Tickの実時間より速く進む場合に限り、各未完了フレームで1msだけWorkerへ譲る。300フレーム上限は維持し、製品SystemのTickや生成予算は変更しない。

`SampleScene_RepeatedAutomaticRebasesPreserveStreamingAcrossRoundTripAndNegativeCrossing`は、採用済みのチャンク別Tilemap方式で絶対X=32.5、544.5、1056.5、479.5、-96.5、416.5、1056.5、-96.5、32.5の9地点を順に確認する。移動は明示テレポートではなく通常移動と同じ相対座標APIを使い、表示原点6種類、自動変更、東西往復、0境界横断、3チャンク再訪を一つのScene寿命で起こす。各地点のCamera、Observer、Activeチャンク、Tilemap表示、座標重複を検証し、再訪時には旧Entityのアンロードと地形・資源の代表3セルの決定論的一致を確認する。全4096セルの生成一致は既存EditModeテストへ任せ、通常回帰の実行時間を抑える。

## 4. 変更内容ごとの実行範囲

第3章は現存するテストの説明である。以下は2026-10-19のゲーム仕様改訂に対応する実装時の検証方針で、未実装機能のテストが存在することを示さない。現在の地形・個体群・表示回帰を維持し、該当PRで新しいテスト名・実行区分を追記する。旧コア・ドローン・内容物全消失・固定施設性能のテスト計画は撤回する。

| 変更内容／系列 | 必須の自動確認 | 人間・計測で確認する内容 |
| --- | --- | --- |
| 既存の共通基盤 | SystemGroupBoundaryTests、FixedTickSimulationTests、PersistentIdTests、DefinitionCatalogTests、WorldSessionBoundaryTests、SimulationCommandQueueTests、SimulationStateHashTestsから関連範囲。失敗時無変更、安定順、上限、停止・再開を維持 | Console、Assembly依存、必要時Player |
| WORLD-01 | Layer付きセル・チャンク・索引、同X/Yの分離、負座標・int端点、PlayerIdと操作主体。多層試作の遷移元・先・水密条件 | 人工海中、上下遮断、乾燥室への出入りの試作 |
| TERRAIN-01 | 同Seed・版・設定の地形、固定Biomeと可変Blockの分離、複数パレット、差分適用、元生成不変、変更済みHalo・影境界 | 分布監査、Map Viewer、Debug Visualization、ブロック識別 |
| 新スポーン | バイオーム許可、占有・通行、段階探索、全禁止・候補なしの明示失敗、禁止バイオームへ妥協しないこと。3×3コア・12×8・近傍資源条件を要求しない | 周辺難易度・希少度の不変、隣接危険地域も生成されること |
| PLAYER-01 | 連続8方向、斜め速度、壁・穴・液体・設備衝突、到達距離、壁越し作業、Layer・セル内位置保存 | 両入力、カメラ、操作応答、既定10 Tick/秒の試作 |
| ITEM-01 | 9＋27枠、装備別枠、スタック互換、数量・耐久・個体参照、満杯・競合、所有権の一意性 | 両入力で分割・結合・装備・収納、満杯ドロップ |
| GATHER-01 | 硬度・耐久消費、壁と地面の1:1回収・設置、水のない穴、埋立→再掘削、配置物競合、採掘済み鉱物の非復活 | 採掘・採集の対象選択、地形境界表示、保存往復 |
| BUILD-01／橋 | 占有・回転、所持品精算、全成功／全失敗、合意した内容物回収、足場と下層維持、マグマの耐熱性、上層依存 | 到達範囲、プレビュー、理由表示、素材名によらない橋の判定 |
| CRAFT-01 | 共通レシピ、材料・出力・予約・中断、出力満杯、屋外作業台、固定Tick進捗、修理・最大耐久維持 | レシピ一覧、屋内に置いただけで速度・数量が増えないこと |
| FAC-01／FAC-02 | 自然壁・設置壁・扉、共有壁、不定形、面積上限、屋内と施設と水密の分離、候補選択、Dirty範囲、ID・在庫の対応付け | 領域輪郭、不足条件、空室、排水・漏水・室内酸素、耐圧、破損 |
| ECO-01 | 既存AnimalSpeciesCatalogTests・AnimalPopulationGenerationTests・AnimalPopulationStateHashTests等に加え、移動、永続／匿名、捕獲・退避の会計、昇格、ゼロからの再定着、保存往復 | 鶏の通常Sprite、可視カリング、画面内の突然消滅防止、1,000／10,000／100,000件 |
| SURVIVAL-01 | 空腹ゼロで直接ダメージなし、攻撃・軽減・耐久、レベル補正なし、全所持品散乱、絶対ExpireTick、未ロード失効、独立死亡履歴、ベッド破損・安全位置 | 警告、回収期限、追跡時のみの大型種構造破壊、遠隔拠点無襲撃 |
| TIME-01 | WorldClockとSimulationTick、通常Tickと睡眠ジャンプの区別、次の起床帯、停止・オフライン非進行、就寝条件・中断 | 夜に環境光以外の視界制限なし、照明、メニューと状態変更の関係 |
| 睡眠横断回帰 | 同じTick数で成長・加工・個体群回復・液体・空腹・酸素・耐久・燃料・消失期限が飛ばした時刻分だけ進まないこと | 起床直後の天候・活動帯、退避・休息、捕獲継続 |
| JOURNAL-01 | 現在Layer・遮蔽・視認、行動観察、野生／人工証拠、正式取得、段階の一意キー、初回報酬・ランク・解禁条件の循環拒否 | 両入力図鑑、センサー・下層プレビューだけで登録しないこと、研究追跡 |
| PLANT-01 | 種子・根・成長・開花・結実、適地、根ごと採集、再定着、飼育目標、画面外・保存復元 | 栽培・植林・展示、定時一括復活なし |
| WEATHER-01 | 地域天候の再現・境界・予報、睡眠後の現在条件、寒冷・高温曝露、装備破損・料理・室内対策 | 季節なし、短い偵察可能、専用装備の警告と保護 |
| TERRAIN-02 | 水種優先度、固化反応、同一セル地面と液体の排他、伝播予算・安定順、未ロード保留・保存、無制限反応防止 | バケツ、自然海岸接触、未完成表示、キューの診断 |
| DIVE-01 | 同座標の潜水・浮上、Layer存在／未ロード区別、移動先衝突、酸素・耐圧・破損、Layer死亡・回収 | 地上→海中→深海の移動、直下プレビュー、酸素補給、入口不要 |
| VEHICLE-01 | 乗降・収納所有、Layer位置、燃料距離／維持消費、耐久、搭乗中死亡、車種別燃料切れ、安全な乗降先 | 実移動、道路幅、接岸・離着陸、脱出、速度別先読み |
| AUTO-01／LOG | レシピ・有限バッファ、個体情報、FIFO、合流・分配・選別・交差、停止理由、個別／集計で順序・在庫・詰まり・進捗・到着Tickの等価性 | 電力なし、遠隔稼働、1,000／10,000／100,000件、長時間・保存反復 |
| EDIT-01 | 採用した範囲だけ、所持品・到達範囲・占有の一括検証。Undo・コピーで個体・アイテム状態・進捗を複製しない | 操作試作と合意。旧「最低10件」「全消失」「開始時全機能」は前提にしない |
| SAVE-01／SAVE-02 | 一貫したSnapshot、Layer・PlayerId、固定生成版・設定、世代切替、中断・破損・旧スキーマ拒否、複数ワールド、所有者別1回保存、バックアップ復帰 | 新規・自動保存・終了・再開、100チャンク以上・大規模保存、ファイル世代整合 |
| Floating Origin・表示 | WorldCoordinateConversionTests等の既存回帰、Layer追加後の絶対位置・Observer・診断・入力一致、編集後の境界と影 | 最大Viewport、正負往復、車両、3層の空白・ちらつき・揺れ |
| アート・Atlas・Scene・Baker | 既存Import・Slice・Pivot・Atlas・参照の回帰。Block参照移行で旧Biome割当テストを新契約へ更新し、再構築後も本番素材を維持 | 16px単位の識別、接地点、夜・水中の見え方、両入力HUD |
| 文書のみ | リンク・見出し・PR依存・採用仕様と旧履歴の区別、差分・空白を静的確認 | 文面レビュー。コードやSceneを変えなければUnity再実行は不要 |

不具合修正では可能なら再現テストを追加する。通常テストは短時間で完了する規模に保ち、性能負荷はExplicit等へ分離する。予定機能の空テストを先回りして作らない。

## 5. 実行時間と負荷の方針

### 動作環境の開発目標と評価の区別

[ゲーム仕様の動作環境目標](../GameSpecification/2026-10-11_casual_factory_concept_v0.1.md#13-保存単独プレイ性能)に従い、SteamのWindows／macOSを対象とし、最低搭載RAM 8GB・推奨16GBを開発目標にする。8GBでは標準設定・通常規模、16GBではより広い表示・大きな工場での快適性を目指すが、設定・規模の具体値と正式な合格基準は実測を通して確定する。搭載RAMとゲーム単体の使用量上限を混同しない。

M3／24GBなど手元の環境での計測は比較データとして残し、8GB／16GB機器やWindowsでの合格へ外挿しない。大容量機で使用量が8GB未満でも8GB機での動作保証にはならず、設備・物流を含むゲーム全体のCPU・GPU・メモリ・停止感を対象環境で確認する。

### 通常テストと個別性能計測

- 修正のたびに実行する通常テストは、Editorを長時間応答不能にしない規模へ保つ。
- 広域分布の比較はバックグラウンド実行する分布監査へ分離する。
- 多数シード、広大な探索範囲、性能計測は`Explicit`テストまたは専用ベンチマークとし、通常実行へ混ぜない。
- 重いテストを追加するときは、目的、件数、想定時間、通常実行から分離する理由を日本語コメントとこの文書へ記載する。
- 性能の合否はEditorの体感だけで決めず、同一条件のProfilerまたはDevelopment Build計測で比較する。

2026-10-17の`MAP-02D`一次性能計測は、通常回帰テストへ組み込まず、PlayMode中の一時ハーネスと`ProfilerRecorder`で実行した。[計測レポート](./Map02DPerformanceReport.md)に、通常／高速／試験用速度、拡大Viewport、負方向・遠方・斜め移動、60FPS目標、原点更新なしの対照条件を記録する。詳細再計測では、Tilemap書き込み、分類、旧セル消去、岩壁影のマスク・不透明度・形状・Mesh反映を個別Markerで取得した。`AutomaticRebase_ReusesRockWallShadowForSameLogicalChunk`は、原点変更前後で同じ岩壁影GameObjectとMeshを保持し、新相対位置へ移動して未使用影の掃除から除外できることを保証する。追加テスト単独1件とPlayMode全31件はUnity 6000.3.8f1で成功した。

岩壁影再利用後の比較もAssetへ計測コードを追加せず、Git対象外の一時ハーネスで実行した。通常12は3,000フレーム、高速36は1,920フレーム、縦横4倍・高速36は1,914フレームを60FPS目標で取得し、原点変更時の岩壁影再構築が0ms、位置更新が最大0.012292msであることを確認した。Editorが背面で約10FPSへ制限された予備試行は比較へ採用しない。完成済み集合に未登録の可視チャンク数は部分適用も含むため、そのまま画面の黒抜け件数として扱わない。製品最大ズームとDevelopment Buildでの測定、人間によるちらつき確認は別途必要であり、この計測によって`MAP-02D`完了状態は変更しない。

チャンク単位Tilemap比較実装の`d114156a`後は、Gameビューの実矩形も1920×1080へ固定し、通常2チャンク／拡大9チャンクの静止状態をOFF/ON各2回、5秒ウォームアップ＋10秒記録で比較した。合計4,804フレームの描画CounterとFrameTiming、所有Native Object、64チャンク移動時のプールを記録した。Frame Debuggerは時間計測と分離した。GPU値の遅延・変動と帰属、Native内部容量の保持があるため、合格判定やリークなしの証明には使用しない。条件、計測中のCLI確認1回の混入、未検証事項は[性能計測レポート](./Map02DPerformanceReport.md#commit後の描画gpu個別メモリ一次評価)を参照する。この追加作業は製品コードや回帰テストを変更せず、Development Buildと長時間比較を残す。

`7d65b5ac`後の追加計測は、同版UnityのmacOS Development Player（Mono／Universal出力をArm64で実行）で行った。静止4ケース、速度36で32秒の自動原点変更、128チャンクの往路・復路・再往路の合成試験を自動ハーネスで実行し、GPUタイムスタンプの重複を除いて集計した。Sceneを保存せず、一時C#と.metaはビルド後に削除した。最初のOFF静止は背景、ONは前面、OFFの再試行もfocusが混在したため、公平な静止CPU／GPUの対照比較は未完了である。これは回帰テストの追加や既存38件／163件の再実行ではなく、MAP-02Dの独立した性能診断である。ビルド警告1件、片付けAPIのEditorエラー2件と復元方法、実測値、残る検証は[Development Playerの一次比較](./Map02DPerformanceReport.md)を参照する。

`9f6311dd`後のOFF再計測では、ユーザーがPlayerを前面に保ち、静止597／600／600／598フレームと速度36の移動1,905フレーム、計4,300フレームすべてfocus=trueだった。全フレーム前面の先行ON記録と比較し、ViewportとGPUタイムスタンプの重複排除件数も確認した。今回は同一ビルドの再利用で、ONを再実行したり、複数プロセスで順序を入れ替えて反復したりはしていない。通常描画CPUとSetPassの増加、原点変更以外に残る停止も記録し、GPU中央値が近いだけで合格とはしない。製品コード・テスト・設定変更はなく、既存回帰テストも再実行していない。詳しい範囲と制約は[前面を維持した追加比較](./Map02DPerformanceReport.md#前面を維持した追加比較)を参照する。

`3ea6c19f`後の通常移動スパイク調査は、まずEditorの既存Markerを有効1,197フレーム記録し、続いて同一Development PlayerをONで再利用してバイナリProfiler記録を取得した。Player移動1,901フレームはすべて背景であり、前面静止比較には混ぜない。読み込めた最後の2,000フレームから親子サンプルとWorker時刻を照合し、表示対象収集時の生成Job待ちを特定した。Deep Profileや新規製品Marker、再ビルド、回帰テストの追加・再実行は行っていない。生成結果の非同期公開を実装する際には、決定論、未完了結果の非公開、アンロード・World破棄時のJobとNativeメモリ寿命、同時生成上限、待機先がStreaming側へ移っていないことを検証する。詳細は[原因特定レポート](./Map02DPerformanceReport.md#通常移動中に残る停止の原因特定)を参照する。

2026-10-17の非同期公開比較では、`ChunkGenerationSystem_ProducesBufferAndDisablesRequest`を、開始更新では生成中・両Bufferが空・要求無効、後続更新で完成結果を公開するテストへ更新した。全4096セルの地形・資源を直接計算と比較する。追加の`ChunkGenerationSystem_RespectsConcurrentLimitAndReusesSlot`、`ChunkGenerationSystem_DestroyedTargetDoesNotPublishToReplacement`、`ChunkGenerationSystem_WorldDisposeCompletesOwnedJobs`、`ChunkStreamingSystem_RetainsRunningGenerationUntilCompletion`は、枠上限と再利用、破棄済みEntityへの誤公開防止、World終了時のJob／Native配列解放、保持外の生成枠予約と完成後アンロードを保証する。公開待ちのテスト補助はWorkerへCPUを譲りつつ後続System更新を進め、30秒で失敗する有限待機とし、未完成Jobをテストから強制公開しない。最終ソースの関連EditMode 167件、PlayMode 38件が成功した。Development Playerでも速度36の32秒移動を2試行し、収集・公開・投入・Streamingを同時取得した。両試行とも全フレーム前面のため背景だった旧原因特定記録と改善率を比較せず、読み込めたTimeline範囲で長い生成Job待ちが再現しないことと、残る影・分類・書込の停止を分けて記録した。条件・後片付け・未検証事項は[非同期公開の計測](./Map02DPerformanceReport.md#生成結果の非同期公開比較実装)を参照する。人間の表示受け入れは別途必要である。

岩壁影マスクの同期Burst処理は`TerrainLayerPresentationUtilityTests.FillShadowMask_MatchesBufferAndDirectHalo`の10ケースで全6400セルを従来のBuffer参照／外周直接判定と比較する。別Seed、負座標、遠方、ワールドの四隅を含み、中心をあえて生成式と異なる地形にして確定済みBufferが優先されることも確認する。同じ出力配列を異なる中心地形で再利用し、全件上書きと入力不変を保証する。`FillShadowMask_InvalidArrayLengths_RejectsBeforeRunning`の3ケースでは不正な配列長をJob実行前に拒否する。関連EditModeは180件、PlayModeは38件が成功した。既存PlayMode回帰は影の原点変更時の再利用とPresenter停止・破棄も含む。性能比較と人間の表示受け入れは別途記録し、自動テストの成功だけで停止感の解消を保証しない。

同変更のPresentation asmdefにUnity.Burst参照を追加したため、`SystemGroupBoundaryTests` 4件も実行して全成功した。追加パッケージはなく、開発者可視化の式や凡例、Player向けのAssembly分離は維持する。

2026-10-17、ユーザーが影改善後の比較ONで通常移動と`(7,-1)`／`(15,-1)`付近の影の継ぎ目・濃さ・停止感を確認し、問題なさそうと回答した。今回の局所改善の動作確認済みとして扱い、製品の性能合格や比較方式の既定ON採用とは区別する。詳細は[性能計測の受け入れ記録](./Map02DPerformanceReport.md#岩壁影マスク構築の同期burst化)を参照する。

分類用外周の一括生成後はUnity 6000.3.8f1で関連EditMode 196件、PlayMode 39件が成功した。外周の追加16ケースと寿命の追加1ケースを含む。Development Playerでも新旧同一入力の前処理込み比較と前面移動2試行を実施し、開始地点の新旧PNG一致を確認した。これは移動中の全ピクセルや停止感を自動保証するものではない。2026-10-17にユーザーが比較ONの通常移動と`(7,-1)`／`(15,-1)`付近の影の継ぎ目・濃さ・停止感を確認し、問題なさそうと回答してcommitを許可した。今回の確認範囲は動作確認済みとし、最終Viewport・負方向・資源レイヤー等の網羅確認や既定ON採用とは区別する。条件と残る負荷は[外周生成の性能計測](./Map02DPerformanceReport.md#分類用外周地形のバッチ生成)を参照する。

`58d10b16`後の残余停止調査では、同じ検証Playerを再ビルドせず詳細Profiler引数付きで再実行し、移動1907フレームすべて前面だった。読み込めた最後の2000フレームから、影なしの表示スパイクを分類・Tilemap書込へ帰属し、長いフレーム内のFPS待機とは分けて記録した。Editorの固定入力による8近傍取得の補助計測は反射・boxingを含むため、製品時間やGC量の比較には使わない。製品コード・テストは変更しておらず、180件＋依存境界4件／PlayMode 38件を今回再実行したわけではない。起動／出力／終了処理を移動値へ混ぜないこと、記録の退避・復元、未検証事項は[影なしのスパイク切り分け](./Map02DPerformanceReport.md#影構築なしの表示スパイクとフレーム待機の切り分け)を参照する。

空レイヤー書込省略の変更では、追加6ケースを含むPlayMode45件と、関連EditMode196件が成功した。同一プロセスの合成4条件で旧無条件書込と新省略を交互比較し、空判定を含むCPU時間・呼出回数・局所GC確保・全セル一致を確認した。前面Player移動も2試行し、フレーム時間と残る消去処理を別に記録した。ユーザーは比較ONの通常移動と`(7,-1)`／`(15,-1)`付近の表示・停止感に問題ないと回答した。この申告範囲は動作確認済みだが、資源・液面・境界の全条件や負方向・遠方までの網羅確認、製品の最終性能評価とは区別する。条件と制約は[書込省略の計測](./Map02DPerformanceReport.md#空レイヤーへの重複書込の省略)を参照する。

`f7db201c`後は一時ハーネス入りのDevelopment Playerを新規ビルドし、M3／24GB・1920×1080・検証用60FPS目標で静止ON/OFF、通常12、高速36の独立反復、縦横4倍、負方向・遠方、有限往復を測定した。背面混在の予備1条件・静止3条件を除外し、採用12条件の21,918フレームはすべて前面だった。原点変更のCPU、GPUの重複除外、SetPass、総GC、メモリの使用／予約とNative Object代理値を区別して記録した。拡大での未完成集合の最長約123msは部分反映を含むため黒抜けと断定せず、連続表示の人間確認を残す。8GB／16GB環境・Windowsの合格には外挿しない。Assetsの一時C#と.metaを除去し、Scene・4設定は事前SHAと一致、Atlasの空白差分は明示承認後に復元した。製品コードと回帰テストは変更しておらず、196件＋依存境界4件／PlayMode45件を今回再実行したわけではない。条件・生データの場所・既存警告・未検証範囲は[暫定総合評価](./Map02DPerformanceReport.md#development-playerの暫定総合評価f7db201c後)を参照する。

総合評価のcommit（`cf3710ad`）後、同じDevelopment Playerの拡大高速移動をON/OFFで実行し、実ウィンドウの画像を各240枚／305枚取得した。抽出した連続画像を目視し、両方式で右端の矩形状の未描画と、その後の反映を確認した。共通Presenterの可視判定後の分割反映を改善候補として切り分け、既定ON化は再確認後とした。取得は概ね約20枚/秒で全描画フレームの検証ではなく、ONは背面混在のため性能比較にも使わない。ユーザーによる拡大条件の受け入れが完了したとは扱わない。コード・テストは未変更で自動テストは再実行していない。条件・原画像・制約は[拡大表示確認](./Map02DPerformanceReport.md#拡大条件の実画面確認と既定方式の判断)を参照する。

先行表示と既定ON化の最終回帰は関連EditMode196件・依存境界4件・PlayMode52件が成功した。Scene縦切りは実際の描画先を読むよう更新し、同じ契約を共有OFFでも実行する。Development Playerは通常ON・拡大ON・拡大OFFの計4,749フレームがすべて前面で、通常／拡大ONの3,799フレームは可視未完成0だった。OFFは原点変更直後に15フレームの未完成集合が残るため完全解消とはしない。画像取得の空白や起動・明示移動中の背景を区別し、製品性能合格や人間の受け入れとは別に扱う。[先行表示と採用記録](./Map02DPerformanceReport.md#画面外の先行表示と既定on採用)を参照する。

2026-10-17、既定ONでの通常・拡大移動と`(7,-1)`／`(15,-1)`付近の画面端・影・停止感の確認依頼に対し、ユーザーは問題ないと回答してcommitを許可した。この依頼範囲の確認済みとして記録し、未検証機器や長時間の性能合格とは区別する。commit時の変更は受け入れ記録のみで、上記196件＋4件／52件を再実行したわけではない。

`1d05dd3f`後の最終補助確認では、保存済み既定ONを強制変更しないDevelopment Playerで、拡大120×67.5タイル・速度36の負方向32秒、正負10億タイル付近32秒、同じ512タイル区間の有限往復86秒を実行した。計8,877フレームはすべて前面、可視未完成0、原点変更計5回、Playerの完了記録はすべてerrors=0だった。表示単位最大20・プール最大4、3帰還の使用メモリ中央値約386〜389MiBを確認したが、予約メモリは約4MiB増えており、長時間のリーク不存在とはしない。実画面は保存画像の抜粋確認で、全フレーム検査ではない。今回の製品コード・テストは未変更で196件＋4件／52件は再実行せず、一時コード除去後のコンパイル成功を確認した。

この限定Player確認と既存回帰・人間の受け入れからMAP-02Dの今回範囲を完了と判断した。MAP-02D完了時点では継続回帰の追加をMAP-02Eへ残し、Windows・8GB／16GB機・製品最大Viewport・正式FPS／メモリ予算の合格とは区別した。採用値・測定方法・画像とメモリの制約・既存ComputeBuffer警告・後片付けは[最終計測とPR完了判断](./Map02DPerformanceReport.md#最終採用設定の補助確認とpr完了判断)を参照する。一時ハーネスと.meta、ビルド由来のResources性能テスト設定はAssetsに残さず、ビルド・CSV・画像・ソースの控えはGit対象外の一時出力先へ保持した。

MAP-02Eでは上記の継続回帰を通常PlayModeテストへ追加し、同じ区間で`Farmware.WorldGeneration.ChunkStreaming`、`Farmware.WorldGeneration.TilemapPresentation`、`Farmware.WorldGeneration.FloatingOriginRebase`を容量4,096の`ProfilerRecorder`へ記録する。単独実行の最大値は4.595／9.144／0.418ms、PlayMode全53件内では4.618／3.486／0.007msだった。実行順やEditor負荷で変動する比較記録であり、製品の時間閾値は未決定なので数値自体を通常テストの合否条件にしない。Markerが有効で0より大きい記録を持つことだけを検査する。Unity 6000.3.8f1で新規テスト単独1件、PlayMode全53件、EditMode全243件が成功し、最終Consoleはコンパイル失敗なし、エラー0・警告0だった。Development Player、製品最大Viewport、Windows／8GB・16GB機、長時間の正式性能評価とは区別する。

### PERF-GATE-Aの最小Player診断

2026-10-17のPERF-GATE-Aでは、FarmwareのランタイムSceneオブジェクトを含まない空SceneからmacOS Development Playerを1本作成した。同じバイナリを、通常起動と2D Animationの管理インスタンスを診断用に生成する起動引数の2条件で正常終了させた。通常起動だけで既存のComputeBuffer自動解放警告が1件再現し、管理インスタンスを生成した条件では警告が消えた。固定中の`com.unity.2d.animation` 13.0.4のソース上の生成・解放経路とも一致するため、Farmware所有リソースではなくパッケージ境界と判断した。条件とログは[PERF-GATE-A調査記録](./PerfGateAComputeBufferReport.md)を参照する。

この診断は非公開型への反射を原因確認だけに使用しており、製品コードと自動テストには残さない。外部パッケージの内部実装へ結合する回帰テストは保守上の誤った契約になるため追加していない。Unityまたは2D Animation更新時、Sprite Skin採用時、警告件数や実行中メモリが増えた時に、空SceneのControlを再実行して確認する。

## 6. 失敗時に記録する情報

- 失敗した完全なテスト名。
- EditModeまたはPlayModeの別。
- 使用したシード、GeneratorVersion、座標、変更した調整値。
- 最初の例外とStack Trace。
- Unityバージョンと、BurstまたはSafety Checkの有無。
- 再現が見た目に関係する場合は、Gameビューの解像度、アスペクト比、選択中の可視化モード。

## 7. このドキュメントの更新条件

テストファイル、テストAssembly、通常・`Explicit`の区分、実行方法、検証範囲、負荷方針を追加・削除・変更した場合は、同じcommitにこの文書の更新を含める。
