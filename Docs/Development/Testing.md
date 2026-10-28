# テスト

- 最終更新日: 2026-10-28
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

Sceneを使わない最小ECS Worldへ描画フレーム相当のDeltaTimeを直接与え、固定Tick用Singletonが各1件作られること、10 Tick/秒でTick未満の端数を保持すること、1フレーム最大4 Tickを超えた整数Tick分の遅延を破棄すること、一時停止中の時間を再開後へ持ち越さないことを検証する。`FixedTickReadinessState`がチャンク生成中を示す間はTickを進めず、準備完了後に延期した最大1 Tickだけを再開し、それを超える待機時間を追いつかないことも保証する。合計時間が同じで追いつき上限内なら、細かい描画フレームと粗い描画フレームで`SimulationTickState.CurrentTick`が一致すること、Tick境界の許容誤差で先行した不足時間を負の端数として保持すること、0の設定値が安全な既定値へ正規化されることも保証する。

#### `PersistentIdTests`

ワールド共通永続IDが1から単調増加し、削除済みIDを再利用しないこと、`ulong.MaxValue`を最後の有効IDとして1回発行した後に停止することを検証する。同一Tickの複数要求は`ScopeOrder`、`MajorOrder`、`MinorOrder`、`Sequence`の安定キー順で割り当て、入力順が変わっても結果が一致すること、同じ地域X/Yと受付順でも異なるLayer相当Scopeなら衝突しないこと、重複キーやID不足では一部だけIDを消費しないこと、次回発行IDと上限到達状態をSnapshotへ保存・復元できることも保証する。

#### `DefinitionCatalogTests`

32桁とハイフン付きのGUID文字列が同じ128bit値へ正規化され、全0と不正文字列を拒否することを検証する。定義入力順に依存せずGUID昇順で1始まりの実行時IDを割り当て、定義間のGUID参照を整数IDへ解決すること、重複GUID、参照切れ、参照先分類不一致、連結されていない参照範囲ではBlob Assetを作成しないことも保証する。複数定義の参照を平坦化した後も、検証エラーの参照番号はInspector上の定義内添字へ戻す。テスト内で作成したPersistent Blob Assetは各テストが所有し、終了時にDisposeする。

#### `WorldSessionBoundaryTests`

WORLD-01Dの128bit WorldId、ワールド内PlayerId、開発用単独プレイヤー初期化、ローカルInput Context、共通主体検証、停止・再開窓口をSceneなしで検証する。GUID表記の正規化と無効値拒否、外部から一部だけ供給されたセッション状態を推測で補修しないこと、別WorldId、未登録PlayerId、重複PlayerIdを状態変更前に拒否すること、正規プレイヤーが停止中にも再開できること、不正主体の停止要求がPause状態を変更しないことを保証する。

#### `SimulationCommandQueueTests`

Sceneを使わない最小ECS Worldで、受付番号が1から単調増加し、`ulong.MaxValue`を1回使用した後は0へ周回しないことを検証する。別WorldId、未登録PlayerId、一時停止中はCommand Entityも受付番号も消費せず、正常な次Commandが未使用番号を取得する。受付後から固定Tick開始前にWorldIdが変わったCommandは処理開始直前の主体再検証で拒否し、新しいWorldへ変更を適用しない。同一固定Tickへ成功、競合失敗、成功の3件を順に投入し、各CommandがValidation、Commit、ECB再生、結果確定まで完了してから次へ進むこと、失敗Commandが状態を部分変更しないこと、Gameplay Groupが同Tick内の最終確定状態だけを読むことを保証する。Validation／Commit担当不在と複数Validation担当の拒否、未完了Commitが記録したECBの非再生、未完了Commitが連続した後も成功Commandだけを反映できること、完了結果を現在の描画フレーム中保持して次フレーム先頭で破棄する寿命も確認する。

#### `SimulationStateHashTests`

`xxHash3-128`へ渡す整数の固定バイト表現、セクションIDの昇順・重複禁止、Complete後の再利用禁止を検証する。共通状態では必須Singletonの欠落・重複、正規化前の固定Tick設定、未処理Command、無効な永続IDアロケーター、定義カタログ、WorldId、PlayerId一覧を検査し、不完全な値を比較へ使わないことを保証する。Player Entityの作成順はハッシュへ影響せず、WorldId／PlayerId変更は検出し、無効または重複PlayerIdを拒否する。同じ定義を異なる入力順から構築した場合もGUID順に同じハッシュとなることを確認する。さらに、同じCommand列とTick数を、Entity IndexとChunk配置をずらした2つのWorldで`Run`と`ScheduleParallel`へ分けて実行し、永続ID順へ正規化した機能状態ハッシュが一致すること、論理値の変更と永続ID重複を検出することを保証する。初回リリース前で永続化した比較基準がない現在は、WORLD-01CのLayer追加とWORLD-01DのWorld／Player追加もVersion 1の定義更新として扱う。リリース後、または保存済みハッシュを異なるビルド間で比較する運用を始めた後は、入力形式の変更時にSchema Versionを増やす。

#### `AnimalPopulationStateHashTests`

動物個体群Seed、Layer付き地域別乱数状態・次回受付順・初期化Tick、個体の永続ID・種類・Layer付き整数位置・群れID・外見差分を機能固有セクション1025へ追加する。地域Entityと個体Entityの作成順を逆転しても同じハッシュになること、同じ地域X/Yの別Layerを重複扱いしないこと、Layerだけの変更と将来の抽選結果を変える地域乱数状態の変更を検出すること、存在しない群れ先頭ID、循環する所属、別群れのメンバーへの所属、別種・別Layerの群れ先頭への所属、必須Componentが欠けた野生動物、動物種カタログに存在しない種ID、種定義の上限外となる外見差分番号を正式な状態として受理しないことを検証する。群れ先頭自身が自分の永続IDを群れIDに持つ正常な自己参照は受理する。

### 3.2 EditMode: `Farmware.WorldGeneration.Tests`

#### `TerrainProductIsolationTests`

通常実行の`EnabledBuildScenes_DoNotDependOnDevelopmentOnlyAssets`は、Build Settingsで有効な全Sceneの再帰依存を`AssetDatabase.GetDependencies`で検査する。`Assets/Scenes/Development/`、`Assets/Farmware/Editor/`、`Assets/Farmware/Tests/`、`Assets/Farmware/Shaders/Development/`、`Assets/Farmware/Art/WorldGeneration/Generated/Inspection/`、手描き制作入力、`TerrainTileArtGenerationSettings.asset`のいずれかを製品Sceneが直接・間接に参照した場合は失敗する。検証SceneをBuild Settingsへ誤登録した場合も、そのScene自身のパスから検出する。地形タイル生成設定の型が`Farmware.WorldGeneration.Editor`へ所属することは`TerrainTileArtGeneratorTests`でも保証し、Player対応Authoring Assemblyへ戻さない。

`ComparisonInspectionRemovalGate_RemovesAllInfrastructure`は通常のEditMode実行へ含める。旧14形状境界ハイライトのPNG、Atlas、Runtime選択Utilityだけでなく、採用方式を確認した比較・検品専用のScene、Builder、Editorメニュー、Development Material／Shader、生成Asset、設定および専用テストが存在せず、Assets配下のC#・Scene・Prefab・Material・Shader・Atlasなどに旧型名、SerializedProperty名、旧PNG／Atlas GUIDや検品専用参照が残っていないことを一括確認する。

#### `WorldLayerWaterContractTests`

WORLD-01Bの製品用`WorldLayer`と`WorldLayerWaterContract`を、Sceneを使わない純粋計算で検証する。固定保存ID、固定3層の隣接関係、川・池を通常水域に固定して海の沖合だけを深水域にする分類、陸地・通常水域・沖合深水域の各層媒体、未知値拒否、地形優先の上下境界合成を確認する。地上水の直下を1階層掘ると同じ固定Tickで自動浸水し、更新後にUnderwaterへ遷移できること、固体は排水や外水接続だけでは掘削されないことも保証する。

構造的に妥当な未ロード先だけを`NeedsPreparation`として再判定へ分離する。未ロードでも固定レイヤーID、座標、隣接関係を先に検証し、不正レイヤー、地上から深海への直接移動、異なるX/Y、水平の遠隔座標について不要な生成・読込を要求しない。同X/Yの隣接水域だけが上下移動でき、固体、乾燥室、地形・水密境界を拒否する。同一レイヤーの水平境界は共有辺だけを候補とし、int端で差分を周回させない。水と乾燥室の水平移動は排水稼働中だけ直接許可し、停止時は媒体更新を先に要求する。PLAYER-01の連続8方向移動、DIVE-01の装備・占有・保留Commandは後続テストで追加する。

#### `WorldGenerationTests`

ワールド生成の純粋計算とECS Systemを、Sceneを開かずに検証する主要テスト群である。

| 分類 | 用途 |
| --- | --- |
| 座標・シード・ハッシュ | 負座標の床除算、文字列シード、生成順に依存しないハッシュ、全用途のPurposeSaltが一意であることを保証する。 |
| ノイズ・チャンク境界 | ノイズの連続性と、隣接チャンクが同じワールド座標規則を使うことを保証する。 |
| 18Biome・最終地形 | 現行GeneratorVersionと環境軸・地形設定の既定値、基礎生成ではBiomeと派生地表を未確定に保つこと、C3／C4水文後の最終化で地上を18Biomeの有効IDへ分類し、海だけを`BiomeId.None`と海水にすることを保証する。不正なDefinitionとC5前のGeneratorVersionはフォールバックせず拒否する。 |
| 水文統合 | 完成した湖分類の公開、地下水・泉・自然冠水の失敗時に部分Bufferを公開しないこと、湿原規模がチャンク境界をまたいでも一致することを保証する。 |
| 岩壁 | 水域上の抑制、適度な被覆率、チャンク境界、凹凸、通路を保証する。 |
| 固定水文 | 8×8セル、固定9地点の標高・基礎湿度、中心タイルの海洋流出口、専用Saltの同点順、D8流向、負座標、循環拒否、最大4,096辺の下流終端、最大4,096セルの集水・自然流出、Level 0〜6の空間集約Summary、外部入口、分解条件、キャッシュ同値性、閉鎖流域境界、越流辺、基礎湖面、64件のECS Bufferと状態ハッシュに加え、複数Seed監査と診断表示の色写像を保証する。 |
| 地形配置属性 | 陸地は通常設備＋通常ベルト、水は橋、マグマは橋＋耐熱必須、岩壁と未知IDは配置不可になることを純粋計算で保証する。 |
| 有効地形 | 組み込みBlock定義、TERRAIN-01C前のInspector追加地形定義の明示拒否、完成Biome・地質・固定水文からの直接Block確定、4096件の生成Buffer、明示除去を含む疎な差分、自然状態への復帰時削除、レイヤー分離、移動・建築の独立判定、地形定義・Blockパレット・差分状態ハッシュの作成順非依存、Owner／Buffer片側欠落の拒否に加え、Gameビュー・開発者可視化・Map Viewerが同じ変更後セルを読むことを保証する。 |
| 資源 | 地形適性、最低間隔、群生半径、競合解決、決定性を保証する。 |
| 初期スポーン | 完成ECSチャンクの初期スポーン許可Biome、乾いた通行可能地面、資源なしを満たす1セルを、原点から有限のチャンクリングで決定論的に選ぶ。原点チャンクの候補だけで打ち切らず、より近い負側候補と同距離時のY・X昇順を保証する。探索中に既存地点が現れた場合もSingletonを二重作成しない。旧12×8建築可能領域、3×3中央コア、近傍資源は要求しない。 |
| ECS生成・ストリーミング | 生成要求、同時生成上限、範囲外キャンセル、保持、非表示化、再有効化を保証する。同じチャンクX/Yの別Layerを既存要求として再利用せず、Surface要求へ別層Entityを混ぜないことも確認する。先読み順は可視矩形の中心を最優先とし、同じリングの上下左右を角より先に要求する。旧Observerの未設定Layerと逆順矩形をSurfaceの正規化結果としてSingletonへ反映し、未生成レイヤーへの切替では実行中Jobの完了を待って旧Surfaceチャンクを解放し、新規要求を作らないことを検証する。Camera Bridgeは外部から選択されたObserver Layerをカメラ移動後も保持し、PresenterはLayer変更フレーム内に旧Tilemapと途中Queueを即時消去する。32bit絶対タイル座標域の正負端では、先読み・保持余白を安全に変換できる論理チャンク範囲へ収め、反対側へ整数周回したチャンクを要求しない。境界ハイライトの1セル近傍と岩壁影の8セル余白は範囲検査付き座標変換を使い、ワールド外を反対端の地形として参照せず非接続にする。初期スポーン、チャンク要求、チャンク生成がUnity標準Groupへ直接所属せず、用途に対応するFarmware共通Groupを使うことも確認する。 |

`InitialSpawnLocationSystem_RecordsCandidateFromCompletedChunk`は、完成Bufferの許可Biome・乾燥・通行可・資源なしセルを選び、Surfaceの初期地点を一意に記録することを確認する。
`InitialSpawnLocationSystem_ContinuesToNegativeRingForCloserOrEarlierCandidate`は、原点チャンクで候補を見つけても探索を早期終了せず、負側を含む未探索リングから距離とY・X昇順で正しい候補を選ぶことを確認する。
`InitialSpawnLocationSystem_ExistingLocationStopsWithoutDuplicate`は、探索要求後に既存地点が追加された場合も、その地点を成功結果として維持しSingletonを二重作成しないことを確認する。

#### `DeterministicEnvironmentGeneratorTests`

TERRAIN-01C1の基礎気温、地形気温、基礎湿度、標高区分、木本植生ポテンシャル、地形起伏、海洋影響、火山活動度をSceneなしの純粋計算で検証する。既定設定、64bit寒冷補正式と0飽和、気温・湿度・標高・共通5段階の全境界両側、起伏差0／16,383／16,384／65,535、海岸3方向と6方向の確定合成値、正負座標とチャンク境界、同入力の再現を対象にする。

基礎湿度の格子設定が独立4軸を変えないこと、旧`Weirdness`と火山活動度が別Saltであること、不正設定・`int`座標端を暗黙に周回させず失敗すること、新軸設定の変更をチャンク状態ハッシュが検出することも保証する。TERRAIN-01C1では地質、水文、18Biome分類のテストをこの群へ先行追加しない。

#### `DeterministicGeologyGeneratorTests`

TERRAIN-01C2-Aの8固定地質、基礎特性、フラグ、合計10,000の基礎重み、標高別倍率、512タイルのマクロセル、256／128タイルの境界ワープ、`-192〜+191`タイルのサイトジッター、5×5候補探索をSceneなしの純粋計算で検証する。Definition配列を逆順にしても安定ID順の抽選結果が変わらないこと、正負座標と512タイル境界、平方距離同値時の座標順、サイト絶対座標の標高再評価、固定設定違反・ID欠落・重複・重み・必須フラグ・`int`座標端の明示失敗を対象にする。

粗い固定格子で全8分類が生成可能なこと、地質基礎特性が`GeneratedTile`とチャンク状態ハッシュへ含まれることも保証する。TERRAIN-01C2-Bでは、複数Seedの面積比、連結領域径・面積、境界長、孤立領域、標高・Biome・火山活動度とのクロス集計、およびGameビュー・Map Viewerの色写像を別の監査・表示テストで追加した。

Authoring回帰では、Sceneへ保存済みの既存Inspector値から作る設定Snapshotにも共有の固定地質既定値が入り、Definition検証を通過することを保証する。Chunk生成Systemの4096セルをキャッシュなしの単一タイル生成と比較し、Jobローカルのサイトキャッシュが分類・基礎特性を変えないことも確認する。

#### `DeterministicHydrologyGeneratorTests`

TERRAIN-01C3-A〜D1の8×8固定水文セル、中心から`-3`・`0`・`+3`タイルの固定9標高・基礎湿度平均、中心タイルによる海洋流出口、専用Saltの`PlateauRank`、D8流向、最大4,096辺の固定下流終端、最大4,096セルの集水・自然流出、Level 0〜6の空間集約Summary、閉鎖流域境界、基礎湖面をSceneなしの純粋計算で検証する。セルの担当範囲`-4〜+3`、負座標の床方向除算、候補列挙順の不変、海洋流出口の標高非依存な優先、同標高・64bitハッシュ衝突時の座標順、陸上辺で安定全順序が必ず小さくなることを対象にする。

実生成した49×49水文セルでは、海洋セルが`Marine`、陸上末端が`ClosedBasin`となり、その他も`LongDrainage`を含む3終端の一つだけを持つこと、全下流がD8隣接であること、陸上辺が上記全順序を減少させることを確認する。Level 0同士の合成と直接Level 1の一致、最大Levelの終端・打切り、不完全・不連続Downstream Summaryの拒否も対象にする。C3-C1では対象セルを含む有限集水、同入力の再現、9地点基礎湿度、湿度合計と65,535除算、`Stream`境界15／16、`River`境界127／128、RiverがStreamを伴うことを追加した。C3-C2ではLevel 0〜6の整列正方形が保持するセル数・湿度合計・唯一の出口・最大内部距離・外部入口を直接集計と比較し、終端・複数出口・下流再進入・不完全経路を親Summaryの分解理由にする。人工4,095／4,096／4,097セルで上限判定を固定し、C3-C1基準経路、空キャッシュ、命中済みキャッシュ、逆順構築キャッシュの全結果を比較する。C3-D1では流域最低数4、流出寄与7／8・16・31／32、充填率の飽和、打切り流域の固定湖除外、正の水深、湖面セルと`Lake`接続の一致、最大充填時の越流辺を追加する。複数の開始セルから有限に追跡して再訪を拒否し、1チャンク64件の行優先順と単一セルAPIの一致、Seed差を含む水文状態ハッシュ、設定不正と`int`座標端の明示失敗も保証する。`UnsafeUtility.SizeOf<GeneratedHydrologyCell>() = 96 bytes`、`HydrologyUpstreamCandidate = 24 bytes`、`HydrologyBasinLookupSlot = 16 bytes`を固定し、性能文書の容量更新漏れを検出する。

既存`WorldGenerationTests`のChunk生成System回帰は、開始直後に地形・資源・水文・表流水・自然水文の全Bufferが未公開であり、完成境界で4096地形、4096資源、64水文、4096表流水、4096自然水文をまとめて公開することを確認する。先頭水文セルは終端座標、辺数、集水セル数、基礎湿度、湿度合計、流出相当値、地下水ポテンシャル、接続・打切りフラグ、閉鎖流域・湖面結果を含め、先頭タイルの自然土壌飽和も同じ純粋Generator結果と一致する。C3-C2の集約Resolverは比較用のMain Thread経路であり、既定のBurst `ChunkGenerationSystem`へ接続しない。

#### `DeterministicSurfaceWaterPathGeneratorTests`／`DeterministicSurfaceWaterGeneratorTests`

C4-A1では水文セル担当範囲の最低標高Anchor、専用Saltと絶対座標による同点解決、D8辺の両端、最大16×16・256ノードの8近傍経路、経路比較順、負座標、`int`座標端、不正入力時の部分結果非公開を検証する。

C4-A2では渓流が中心線だけ、河川が中心線と上下左右1タイルだけを候補にすることを固定する。別供給元の河川幅と渓流中心線を両方の適用順で重ね、河川の代表値を維持しながら`ChannelCenterLine`を候補間の論理和として残す。実生成チャンクについて同入力の全4096件一致、海面未満除外、湖面標高未満かつ同じ流域内を末端Anchorから8近傍で辿る実Flood Fill、海面下の末端Anchorを候補なしとして正常終了すること、隣接チャンクが共有する絶対タイルの一致を確認する。境界回帰では64×64タイルの実担当水文セルが0〜8の9×9、そこへ流入できる河道供給元が-1〜9の11×11になることを固定し、正側末尾4タイルの河道・湖を64件Buffer外として欠落させない。`UnsafeUtility.SizeOf<GeneratedSurfaceWaterTile>() = 16 bytes`、`SurfaceWaterPathTile = 8 bytes`、`SurfaceWaterPathNode = 48 bytes`も固定し、性能文書の容量更新漏れを検出する。

性能比較`SurfaceWaterGeneration_ReportsHydrologyComparison`は`Explicit`として通常回帰から分離する。同じ1／4／16チャンクを水文のみと水文＋表流水で交互に生成し、失敗0件とチェックサム一致を確認しながらMain Thread CPU時間を記録する。Burst Job、ECS公開、描画を含む製品性能の合格判定には使用しない。

#### `DeterministicGroundwaterHydrologyGeneratorTests`

C4-B2では当初2 bytes、C4-D2後は自然冠水度と資格bitを含む6 bytesの自然水文Buffer要素、涵養最大、流出倍率2,048、帯水係数の最小、地下水接続境界32,767／32,768、自然土壌飽和の比重2：2：1：1、0／65,535端点、飽和境界39,320／39,321を検証する。負座標水文セルの固定9地点補正後地質平均を製品APIと直接比較し、実チャンクの9×9交差セル、64地下水値、4096飽和値、正側末尾タイルが参照する9個目の水文セル、飽和・冠水・flags各フィールドの状態ハッシュ差も確認する。涵養候補の境界回帰では、隣接セル内の河川中心線から上下左右1タイルへ広がる幅が2セル先の担当範囲へ入るため、地下水入力を生成する場合だけ河道供給元を2セルHaloの13×13へ広げることを固定する。表流水だけを生成するC4-A経路は1セルHaloの11×11を維持する。

性能比較`GroundwaterHydrologyGeneration_ReportsSurfaceWaterComparison`は`Explicit`として通常回帰から分離する。同じ1／4／16チャンクを地形・水文・表流水までと、地下水・自然土壌飽和を含む経路で生成し、失敗0件を確認しながらMain Thread CPU時間を記録する。Burst Job、ECS公開、描画を含む製品性能の合格判定には使用しない。

#### `DeterministicLakeSpringGeneratorTests`

C4-Cでは湖水収支の比重2：1：1と64bit合成、0／65,535端点、越流淡水化、水収支境界26,213／26,214・52,427／52,428、一時湖・塩湖・淡水湖の表流水Typeと固定水面・淡水接続フラグを検証する。泉は放出境界39,320／39,321、地下水を持たない最低8近傍、放出度優先とX・Y同点順、合成泉流を追加しても基礎D8・下流座標・集水面積・流出寄与が変わらないことを固定する。PR #41レビュー回帰では、一時湖の最終Lake接続と最終区分を交差9×9の正側境界から21×21泉キャッシュへ同期し、先行する通常河道涵養で更新した地下水値を保持すること、セル自身に河道接続がなく隣接河川幅だけが入るセルを泉候補から除外することを確認する。診断回帰では一時湖、恒久塩湖、閉鎖淡水湖、越流淡水湖の4区分が別色になり、Map Viewerの全Hydrology Lakeサンプルが製品Generatorと同じ最終区分になることを確認する。製品Job統合では固定Seedの恒久淡水湖、公開水文セルの最終区分、失敗通知時にC6-C2以降の6 Bufferを公開しないこと、PlayModeストリーミングが300フレーム内で完了することを確認する。

性能記録`LakeSpringGeneration_ReportsScaling`は`Explicit`として通常回帰から分離し、地形からC4-Cまでの同期決定論ハッシュを1／4／16チャンクで生成する。製品側は21×21周辺水文を8担当へ行分割し、帯水係数の最大地下水上限が接続境界未満のセルでは集水探索を省略する。同期性能値をBurst並列Jobや対象機器の合格判定には使用しない。

#### `DeterministicNaturalFloodingGeneratorTests`

C4-D1では共有固定設定とAuthoring Snapshot、非負整数平方根、半径32の内外、水源なし、距離0／32、水面との比高0／4,096超、河道流出倍率512と65,535飽和、距離・低さ・平坦さ・規模の2：2：1：1、冠水境界39,320／39,321を検証する。水源選択は同じ整数距離で規模、X、Yの順に一意化し、負座標と`int`両端の非交差矩形も確認する。泉流、湖候補、塩湖、意味フラグ不足、不正設定、不足Bufferは成功結果へ混ぜない。C4-D1ではECS Bufferと表示を変更しないため、チャンク境界の水源再構築、状態ハッシュ、湿原規模、性能記録はC4-D2で追加する。

#### `DeterministicWetlandExtentGeneratorTests`

C4-D2では低地20,000／36,000の両端、淡水接続必須、自然飽和または自然冠水39,320／39,321、セル内15／16資格タイル、上下左右3／4水文セル、斜め非連結を人工入力で固定する。水文セルの淡水接続はStream・River・Groundwaterを直接認め、Lake bitは越流淡水湖または閉鎖淡水湖との組合せだけを認める。恒久塩湖、一時湖、未分類湖、およびLake bitのない湖区分を除外し、別のStream接続がある場合は湖区分だけで失わないことを境界10件で確認する。製品Job統合は4096件の湿原資格bitを同じ純粋判定と比較し、固定Seedで自然冠水・淡水接続・広域規模が実在すること、左右2チャンクが共有する境界水文セルの広域規模bitが一致すること、不正な自然冠水設定でC6-C2以降の6 Bufferを部分公開しないことを確認する。状態ハッシュは同一Seedの再現と別Seed差に加え、自然飽和、自然冠水、flagsの各差を検出する。

性能記録`NaturalFloodingWetlandGeneration_ReportsScaling`は`Explicit`として通常回帰から分離し、地形からC4-D2と状態ハッシュまでを1／4／16チャンクで同期生成する。周囲8チャンクの河道・湖、15×15水文の涵養・地下水・最終湖区分、14,400タイルの資格、有限連結探索を含むが、Burst製品Job、同時生成2枠、ECS公開、描画を含む製品性能の合格判定には使用しない。2026-10-22のPR #42レビュー修正後は、明示3件を含む対象25件、World Generation EditMode 542件、PlayMode 56件が全件成功した。

#### `HydrologyDistributionAuditTests`

C3-D2の複数Seed分布監査について、最小の1 Seed・1 ChunkをMain Threadと実メニュー同様のThreadPoolで集計する。総数64セル、流向・終端件数の合計、河川が渓流以下、湖面が固定湖候補以下、固定湖流域が解決済み流域以下、集水面積の範囲を確認し、比較署名と日本語レポートが再実行で一致することを保証する。監査はSceneやECS状態を書き換えず、製品と同じ純粋Generatorを使い、Main Thread限定の`Allocator.Temp`へ依存しない。

#### `HydrologyDiagnosticChunkTests`

C4-E1aの完成チャンク診断境界を対象にする。製品と同じC3、C4-A、C4-B、C4-C、C4-Dの順序でSurface原点チャンクを再構築し、地形4096件、水文セル64件、表流水4096件、自然水文4096件を部分結果なしで公開することを確認する。所有するPersistent `NativeArray`は破棄後に未確保状態へ戻り、`Dispose`を再度呼んでも二重破棄しない。自然生成へ未接続のUnderwaterは配列確保前に明示拒否し、Surface結果を流用しない。複数Seedの集計値、比較署名、Consoleメニューは`DerivedHydrologyDistributionAuditTests`とC4-E1bの実メニュー確認で扱う。

2026-10-22のC4-E1a AI確認では、Unity 6000.3.8f1 Editorの厳格再コンパイルが警告0件・エラー0件で成功した。最終対象EditMode 2件は8.70秒、World Generation EditMode Assembly 544件は283.63秒ですべて成功し、失敗、スキップ、未確定はいずれも0件だった。Unity CLI 1.0.0-beta.14と既存Pipeline 0.8.0-exp.1間の旧コマンド名照会エラー4件だけを内容確認後にConsoleから消去し、最終実状態はコンパイル失敗なし、エラー0件、警告0件である。

#### `DerivedHydrologyDistributionAuditTests`

C4-E1bの派生水文分布監査について、最小の1 Seed・1 ChunkをMain Threadと実メニュー同様のThreadPoolで完成生成する。総数1チャンク・64水文セル・4096タイル、各件数が対応する母数以内であること、最終湖4区分が相互排他的であることを確認する。比較署名と、0件分類も省略しない日本語レポートが再実行で一致することを保証する。固定の発生件数を合格条件にせず、各生成式の閾値や分類可能性はC4-B〜Dの境界テストを正とする。

2026-10-22のC4-E1b AI確認では、Unity 6000.3.8f1 Editorの厳格再コンパイルが警告0件・エラー0件で成功し、最終対象EditMode 1件は9.88秒、World Generation EditMode Assembly 545件は293.98秒ですべて成功した。既定3 Seed・各原点1チャンクの実メニュー監査は15.91秒で完走し、192水文セル、12,288タイル、比較署名`28C93E4ACD468FBD`を記録した。既定試料で一時湖、恒久塩湖、閉鎖淡水湖、泉流は0件だったが、監査は0件を省略せず観測値として表示し、固定の合否閾値には使用しない。Unity CLIと既存Pipeline間の旧コマンド名照会エラーだけを内容確認後に消去し、最終Console実状態はコンパイル失敗なし、エラー0件、警告0件である。同日に人間が実メニューを実行し、開始・完了ログ、集計母数、比較署名の一致を確認した。

#### `HydrologyVisualizationTests`

C3-D2のGameビューとMap Viewerが共有する基礎水文色写像と、C4-E2／E3のGameビュー・Map Viewer派生水文色写像を検証する。打切り流域、固定湖候補、実湖面、越流が相互に異なる色であり、流域共通の`Overflow`だけを持つ非湖面セルは越流色にせず固定湖候補色を保つこと、5基礎水文モードと5派生水文モードのMap Viewer RGBがOverlayと一致して不透明になることを保証する。派生水文では地下水接続境界32,767／32,768、自然土壌飽和・自然冠水の0／65,535端点、泉の有無、湿原条件外・局所資格・広域規模が別色となり、5モードだけが固定水文Buffer走査対象へ含まれることを確認する。小さい3×2画像の各代表タイルを直接生成した固定水文セルと比較し、実Window同様のThreadPoolでも画像を完成できること、最大64サンプル制限でも2:1のViewport縦横比を維持することを確認する。C4-E3では、タイル所有チャンクと中心基準8×8セルの所有チャンクが正側境界で分かれる代表座標をThreadPoolから生成し、2完成チャンクの値コピー、担当セル座標、Hover検索、表示色が一致することも固定する。

2026-10-21のC3-D2 AI確認はUnity 6000.3.8f1の接続済みEditorで行い、コンパイルエラー0、`HydrologyDistributionAuditTests` 1件、`HydrologyVisualizationTests` 5件、`WorldGenerationMapViewerTests` 51件が成功した。World Generation EditMode Assemblyは130.74秒で411件すべて成功した。全Assembly中の警告6件は変更前からあるテスト用WorldのSystem Group順序属性2種類が各3回出たもので、D2のAllocator・Job安全性警告ではない。既定3 Seed・各5×5チャンクの監査も4,800セルを完走し、実Map ViewerとSampleSceneのGameビューでHydrology Lakeの凡例・色面・中央情報を表示後、Consoleエラー0・警告0を確認した。初回実UI確認で検出したバックグラウンド処理のTemp Allocator違反は、所有者が`Allocator.Persistent`作業領域を`using`で破棄する方式へ修正し、ThreadPoolテストと実UIで再発しないことを確認した。2026-10-21に人間が水文表示とMap Viewerを確認し、問題なしと判断した。Pipeline 0.8.0-exp.1はPlayMode 56件を一覧取得できるが`run_tests`では0件となるため、全PlayMode Assemblyの自動再実行は未検証事項として残す。

PR #38のレビュー対応では、Map Viewerの描画・リサイズ・カーソル中心ズームでモード別最大解像度を共有する回帰テストと、`Overflow`を持つ非湖面セルを越流色にしない回帰テストを追加した。Unity 6000.3.8f1で再コンパイルエラー・警告0を確認し、通常EditorのTest Runnerで`HydrologyVisualizationTests` 6件、`WorldGenerationMapViewerTests` 52件がすべて成功した。Play Mode中の最初のEditMode実行はTest Runnerが`EditorSceneManager.NewScene`を使用できず失敗したため、Play Modeを停止してから同じ対象を再実行した結果を採用する。

2026-10-22のC4-E2 AI確認では、Unity 6000.3.8f1 Editorの厳格再コンパイルが警告0件・エラー0件、`HydrologyVisualizationTests` 9件が0.49秒、World Generation EditMode Assembly 547件が294.90秒で全件成功した。PlayMode Assemblyは非同期実行で56件中55件が成功し、残る1件は実行中にCLIの`unity status`が旧コマンド名`editor status`のエラーをConsoleへ挿入したためログ未許可で失敗した。同じ1件をCLI状態照会なしで分離再実行し18.68秒で成功したため、製品回帰56件の判定自体はすべて成功している。SampleSceneではフレーム進行を確認後、地下水、自然土壌飽和、泉、自然冠水、湿原候補の5メニューを実行し、選択モード、色面、凡例、中央固定値をGameビュー画像で確認した。最終Console実状態はコンパイル失敗なし、エラー0件、警告0件である。同日の人間確認では実際の高速移動で秒単位の一時的な表示遅延を確認した。AIの同一入力による診断ON／OFF比較では診断表示による追加の入力遅延は確認されず、既定の1チャンク先読みと同時生成2枠に対して移動が先行したと判断した。停止後に表示が収束し、例外や永続的な欠落がないことを条件として、開発者専用診断では許容範囲とし、人間確認とcommit許可を完了した。

2026-10-22のC4-E3 AI確認では、Unity 6000.3.8f1 Editorの厳格再コンパイルが警告0件・エラー0件、`HydrologyVisualizationTests` 11件が21.03秒、`WorldGenerationMapViewerTests` 52件が6.01秒で全件成功した。正側境界でタイル所有チャンクと中心基準8×8水文セルの所有チャンクが分かれるThreadPool統合テストは、2完成チャンクを13.876秒で生成し、コピー値、担当セル座標、Hover検索、表示色が一致した。実Map Viewerの`Hydrology Wetland Candidate`は495サンプル・6完成チャンクを51.31秒で一度だけ生成し、分類色、3項目の凡例、8行の中央／Hover固定値を確認した。初回実UIでは完成後にHover欄が広がって33×16から33×15へ二度生成する問題を検出したため、USSで初回から8行分を確保した。修正後は完了表示を3秒以上維持し、自動再生成しないことを確認した。全World Generation EditMode一括実行はUnityが`RunFinished`を通知してSceneを復元した後もPipelineの分離ジョブが`running`のまま結果を返さず、結果収集を確定できなかったため未検証として残す。対象テストと実UIではP0・P1ブロッカーを確認していないが、全一括結果と最終Consoleの既存テスト収集ログ分離は人間確認前の未検証事項である。

Groundwaterの人間確認で、生成中に旧画像を維持する既存仕様に対し、Hover判定だけが新Dropdownモードを参照して未完成の派生サンプルを検索する回帰を検出した。`WorldGenerationMapSampleResult`へ生成時モードを保持し、Hoverと基礎水文詳細生成を適用済み画像モードへ統一した。修正後のUnity厳格再コンパイルは成功し、表示モード保持を追加した`HydrologyVisualizationTests` 11件が17.33秒、`WorldGenerationMapViewerTests` 52件が0.46秒で全件成功した。実WindowでもGroundwater生成開始直後の旧Terrain画像にTerrain情報を表示し、「完成派生水文サンプルを取得できません」が出ないことを確認した。

派生水文5モードが高さ15サンプルで生成中0%のままに見える報告に対し、完成チャンクを先に列挙してGate待機開始1%、各完成チャンク終了時に95%まで、画像行処理で100%まで通知する回帰を追加した。正側境界の2完成チャンク統合テストは、通知が`1%`、1件目、2件目、`100%`の厳密な昇順となることも保証する。修正後のUnity厳格再コンパイルは警告0件・エラー0件、`HydrologyVisualizationTests` 11件が17.18秒、通常モードの16行間隔通知を含む`WorldGenerationMapViewerTests` 52件が0.61秒で全件成功した。

2026-10-22に人間がMap Viewerの派生水文について、段階的に進む生成割合、生成完了、適用済み画像と一致するHover情報を再確認し、C4-E3のcommitを許可した。

2026-10-21のUnity 6000.3.8f1によるC3-A AI確認は、`DeterministicHydrologyGeneratorTests` 21件、`ChunkGenerationSystem_`回帰 4件、`AnimalPopulationGenerationTests` 22件、Salt一意性とチャンクハッシュ順序不変の回帰 2件が全成功した。PlayMode Assemblyは56件中55件が成功し、残る1件は`-batchmode -nographics`で既存の`ChunkStreaming` Profiler Markerが0nsとなる計測環境差である。生成、表示、座標回復のAssertはそれより前に通過した。通常EditorのSampleSceneではカメラ移動と移動先チャンク表示を確認し、Play終了後のConsoleは情報・警告・エラー0件だった。Profiler Markerの自動テスト自体は、通常Editorでの人間確認に残す。

2026-10-21に人間が通常EditorでSampleScene、チャンク越境、負座標側の表示、対象PlayModeテスト、Consoleを確認し、C3-Aの動作確認を完了した。

2026-10-21のC3-B AI確認はUnity 6000.3.8f1の接続済みEditorで行い、`DeterministicHydrologyGeneratorTests` 24件、`ChunkGenerationSystem_`回帰 4件、World Generation EditMode Assembly 371件、PlayMode Assembly 56件が全成功した。PlayModeにはSampleSceneの生成・再生成、チャンク越境、負座標横断、Profiler Marker回帰を含む。完了後のConsole現時点値はエラー0件・警告0件だった。同日に人間の通常Editor動作確認も完了し、PR #35としてdevelopへマージされた。

2026-10-21のC3-C1最終AI確認では、Unity 6000.3.8f1で`RunoffMoistureSum`を符号付き64bitへ変更した最終差分を再コンパイルし、エラー・警告0件を確認した。`DeterministicHydrologyGeneratorTests` 31件、`ChunkGenerationSystem_`回帰4件、World Generation EditMode Assembly 378件、PlayMode Assembly 56件はすべて成功した。全EditModeは121.78秒、PlayModeは4.49秒だった。PlayModeにはSampleScene生成・再生成、チャンク越境、負座標横断、Profiler Marker回帰を含む。終了後Consoleの現時点値はエラー0件・警告0件であり、取得バッファに残る6件は既存のSystem Group順序属性警告2種類がテスト用World構築で各3回出たものである。通常EditorのSampleScene目視は人間確認へ残す。

C3-C2の通常回帰は、各Levelの直接集計、Level 0外部入口、再構築・構築順、4,095／4,096／4,097件境界、基準・初回・命中・逆順キャッシュの同値性を対象にする。性能比較`AggregatedCatchment_ReportsScalingAndChunkGenerationComparison`は`Explicit`かつ10分上限で通常回帰から分離し、最大32,768件のHeapを`Allocator.Persistent`で再利用してUnityの一時Allocator上限を計測結果へ混ぜない。256セルを温めた後に1,000／10,000／100,000要求、続いて16 Chunk・1,024水文セルを測り、両経路のチェックサムと全セル固定値、現在Threadのmanaged allocation、Summary利用・分解・命中・基準フォールバック数を確認する。2026-10-21の結果と解釈は[性能計測](./Map02DPerformanceReport.md#terrain-01c3-c2の集約summary比較)へ記録した。

C3-D1の局所性能診断`ClosedBasinResolution_ReportsScalingAndLakeDistribution`は`Explicit`かつ15分上限とし、上流Heap、流域セル、Hash表を`Allocator.Persistent`で一度だけ確保する。同じ100×100セル作業集合へ1,000／10,000／100,000要求を渡し、CPU時間、現在Thread managed allocation、生成失敗、固定湖、湖面セル、打切り流域、全固定値チェックサムを記録する。通常回帰や製品性能の合格判定には含めず、最適化前後を同一条件で比較するときに明示実行する。

`ChunkLakeBatching_ReportsComparisonWithRowLocalResolution`も`Explicit`とし、修正前相当の行別流域解決と、製品Jobが使うチャンク単位の終端共有を同じ16 Chunk・1,024セルで交互に測る。CPU時間、閉鎖流域セル数、チャンクごとの一意な末端数を記録し、全セルの固定値一致を必須とする。2026-10-21の隔離Unity Editorでは行別8,404.443ms、チャンク一括3,084.233ms、比率0.367、閉鎖流域セル737件、一意な末端合計42件だった。この値はMain Thread局所比較であり、通常Editorの`ChunkGenerationBoundaryWait`合格判定には使わない。

`ParallelLakeWorkItems_PartitionCellsAndMatchSequentialChunkResolution`は、閉鎖末端ごとの64bit Maskが重ならず全閉鎖流域セルを一度ずつ覆うことを検証する。8担当へ専用Heap・流域セル・Hash表領域を渡して全担当を処理した後、逐次チャンク解決との全固定値一致を保証する。実際のJob依存とNative安全性は`ChunkGenerationSystem_ProducesBufferAndDisablesRequest`でも確認する。

2026-10-21のC3-C2最終AI確認は、開いているEditorへ影響しない一時複製プロジェクトをUnity 6000.3.8f1で実行した。Runtime AssemblyとTest AssemblyのC#再コンパイルはエラー・警告0件、全EditModeは484件中480件成功・失敗0件・`Explicit` 4件除外だった。この内訳は`Farmware.Tests.EditMode` 92件成功、`Farmware.WorldGeneration.Tests` 392件中388件成功・明示4件除外である。PlayMode Assemblyは56件すべて成功した。集約性能テスト1件は187.475秒で成功した。C3-C2で一時配列からPersistent再利用へ変更した既存下流性能テストも単独で553.613秒・1件成功し、1,000／10,000／100,000件すべてで現在Threadのmanaged allocation 0 bytes、生成失敗0件、最大実辺数23／29／30、`LongDrainage` 0件だった。実行環境差でCPU時間は過去の接続済みEditor記録より長いため、異なるセッション間の速度比較には使わない。

2026-10-21のC3-D1 AI確認は、Unity 6000.3.8f1の一時複製プロジェクトでRuntime Assemblyと水文Test Assemblyを警告・エラー0で再コンパイルした。全EditModeは501件中496件成功・失敗0件・`Explicit` 5件除外で、既知のSystem Group順序属性警告2種類が各3回、Unity ILPPの重複Hint Path警告3件が出た。C3-C2集約性能テストは湖探索を比較対象から分離した状態で174.472秒・1件成功し、100,000要求と16 Chunkのチェックサム一致、現在Thread managed allocation 0 bytesを維持した。C3-D1明示負荷テストも415.097秒・1件成功し、1,000／10,000／100,000要求の生成失敗0、現在Thread managed allocation 0 bytes、固定湖・湖面分布とチェックサムを記録した。全PlayModeは56件中55件成功し、残る1件は状態Assert通過後に`-batchmode -nographics`で既知の`ChunkStreaming` Profiler値0nsとなる箇所だけが失敗し、単独再実行でも同じだった。通常Editor上の表示、Console、境界待ち計測は人間確認へ残す。

人間確認でチャンク越境待ちが許容範囲外となった後、チャンク単位の流域共有、湖面Queue削除、地形・資源Jobと水文系列の並行実行、異なる閉鎖流域の最大8並列を含む修正をUnity 6000.3.8f1一時複製プロジェクトで再検証した。全EditModeは159.586秒で503件中497件成功・失敗0件・`Explicit` 6件除外、全PlayModeは6.315秒で56件中55件成功だった。PlayModeの残る1件は生成・表示・再訪・負座標横断の状態Assert後、既知の`-batchmode -nographics`で同じ2130行のProfiler値0nsだけが失敗した。閉鎖末端Mask分割、逐次結果一致、湖面整合、Chunk生成System実Job回帰、行別対チャンク一括の明示比較は各1件成功し、コンパイルエラー、新規C#警告、Job安全性エラーは0件だった。グラフィックス有効の横断テストは3回すべて成功し、`ChunkGenerationBoundaryWait`最大31.249／37.170／33.717msを記録した。接続済み通常Editorはコンパイル失敗なし、Consoleエラー0件・警告0件だったが、Pipeline 0.8.0-exp.1の`run_tests`は対象テストを0件として終了した。通常EditorのSampleScene越境と体感判定は修正後の人間再確認へ残す。

下流探索の局所性能確認には、通常回帰から除外した明示実行テスト`DownstreamResolution_ReportsScalingComparison`を使う。同じ入力についてLevel 0の一辺Summaryと最大Levelの終端探索を交互に3回ずつ測り、1,000／10,000／100,000セルのCPU時間、現在Threadのmanaged allocation、平均・最大実辺数、終端内訳をConsoleへ記録する。入力は固定10 SeedとSeedごとに離した100×100セル領域であり、1,000件と10,000件は先頭Seed内、100,000件は10 Seed全体を使う。256セルのウォームアップ後に測定し、生成失敗、終端総数、実辺数上限もAssertする。この値は接続済みEditorのmanaged C#経路を比較する診断値であり、Burst JobやDevelopment Playerの製品性能合格値には使わない。

レビュー対応後の再実行では、明示実行テスト1件、通常の水文回帰24件、World Generation EditMode Assembly 371件がすべて成功した。全Assembly実行中に、`AnimalPopulationGenerationTests`のテスト用World構築から既存のSystem Group順序属性警告2種類が各3回、合計6件出た。同じ警告は変更前の全Assembly実行ログにもあり、今回変更した水文性能テストからの新規警告ではない。Consoleエラーとコンパイル失敗は0件だった。

#### `CompletedChunkTerrainNeighborhoodTests`

中心と周囲8チャンクの完成自然地形へ疎な`TerrainCellDelta`を適用し、表示と診断が同じ有効地形を直接参照する契約を検証する。Factoryはワールド内の3×3から1件でも欠ければ表示へ渡さず、自然生成へフォールバックしない。中心セルと負座標側のチャンク境界へWall差分を置き、どちらも同じRuntime IDへ解決することを確認する。intワールド左下端では外側Entityを要求せず、近傍取得を非接続として返す。`WorldGenerationTests.TerrainNeighborHalo_WorldEdge_TreatsOutsideNeighborAsDisconnected`も正負のintワールド端を独立して保証する。旧260要素外周配列の生成・再利用APIと、それだけを保証するテストはC6-D5で削除した。見た目と停止感は別途人間が確認する。

#### `WorldCoordinateConversionTests`

MAP-02Aの純粋な座標変換について、表示原点が相対セル`(0, 0)`へ対応すること、正負座標とint最小・最大の差を失わないこと、絶対タイルから相対セルを経由した往復で元の値を復元することを保証する。WORLD-01Cでは同じX/Yのセル・チャンクがLayerごとに別値となり、Layer付き変換でも負座標の床方向除算を維持することを追加確認する。連続した相対ワールド座標はセル境界の正負両側で床方向へ丸め、表示原点を変更しても逆変換後の絶対タイルが変わらない。NaN、無限大、表示原点加算後にint範囲外となる入力は誤った座標へ周回させず拒否する。

#### `TerrainPlacementRulesTests`

通常設備、通常ベルト、非耐熱橋、耐熱橋を通常Ground、淡水Liquid、マグマLiquid、自然Wallへ配置し、`EffectiveTerrainCell`が参照する定義カタログ属性と共通`TerrainPlacementRules`だけから判定する。水域では両方の橋、マグマでは耐熱橋だけを許可し、非耐熱橋は通常の地形不可と区別できる`HeatResistanceRequired`を返すことを保証する。無効Runtime ID、区画種別不一致、GroundとLiquidの重複も許可せず、入力した有効セルを変更しないことを確認する。

#### `EffectiveTerrainCellTests`

C6-C1以降は行優先の`GeneratedTerrainCell`を自然生成結果として使い、自然Wallから壁だけを明示除去してもBiomeとGroundを維持すること、海域の`BiomeId.None`と海水Liquidを有効な組み合わせとして解決すること、GroundとLiquidの重複を拒否することを確認する。旧`GeneratedTile`からBlockへ変換する互換テストは削除し、C6-Bの41材質Definitionを共通Definition GUIDと地形固有Blobへ構築してRuntimeIdから分類・属性を取得できる検証は維持する。C6-C2ではチャンクEntityの4096件生成Bufferと別Entityの疎な差分を絶対座標から合成できることを追加する。

ECS Bufferでは差分をLocalTileIndex昇順に挿入し、自然状態へ戻したセルの要素を削除する。同じX/YでもSurface所有者の差分をUnderwaterへ適用せず、水域は泳げれば移動可能でも通常設備は配置不可になること、マグマの移動・橋が耐熱能力を要求することを保証する。地形状態ハッシュはEntity作成順を変えても一致し、同じチャンクX/YでもLayerが変われば異なる値になり、固定3液体のGUIDと組み込みRuntimeIdの対応、18Biome・8地質パレットの行／Range／重み／Block分類、または`TerrainChunkDeltaOwner`と`TerrainCellDelta` Bufferの一対一対応が破損していれば拒否することを確認する。パレット内で合計256を保って重みを変えた場合もハッシュ差を検出する。自動登録入力は41材質のItem・Ground・Wallと固定3液体だけを含み、置換済みの共通Ground・Wall互換Definitionを受理しないことをEditModeで確認する。`DefinitionCatalogAuthoring`はTERRAIN-01CまでInspector由来の地形4分類を組み込み定義と併存させず、具体的なBake理由を返すこともEditModeで確認する。

C6-C3では`GeneratedTile`をBiome・環境・地質・暫定資源適性だけへ縮小し、生成、動物、初期スポーン、配置、自然水柱、診断、Map Viewer、暫定Tilemap表示の各テストを`GeneratedTerrainCell`と地形Definition属性へ移行する。旧型名、旧フィールド、旧変換メソッドはRuntime／Editor／TestsのC#検索で0件であることを確認する。2026-10-26時点でSimulation、World Generation Runtime、Authoring、Presentation、Editor、EditMode Test、PlayMode Testの生成済みプロジェクトを静的ビルドし、警告0件・エラー0件を確認した。レビュー修正後はUnity 6000.3.8f1で再コンパイル成功、互換Definition除去・有効地形・配置の対象EditMode 39件、デバッグ表示と代表SampleSceneのPlayMode 2件に成功した。後続確認で見つかった固定Tick公開境界1件はテスト用完成チャンクの`GeneratedTerrainCell`初期化漏れ、資源適性2件は期待値側のBlock由来適性確定漏れと旧契約から岩壁上資源を誤って除外したテスト条件が原因だった。製品の固定Tick公開順と岩壁上資源契約は変更せずテストを修正し、動物個体群23件、ワールド生成56件、World Generation EditMode全606件がUnity 6000.3.8f1で成功した。

#### `TerrainBlockMaterialDefinitionTests`

C6-B1の初回41材質について、安定材質ID 1〜41、共通Item・Ground形態・Wall形態の全123 GUIDが有効かつ一意であることを保証する。本番Bakerと同じ順序で共通Definition、87件の地形Definition、解決済み材質Blobを構築し、各材質のItem／Ground／Wall分類、GroundとWallが同じ回収Itemを指すこと、自然Wallが移動遮断・水密属性を持つことを確認する。材質入力の逆順でも安定ID検索結果が変わらず、ID重複、GUID重複、行欠落、予約ID・範囲外IDを拒否する。

#### `TerrainBlockPaletteDefinitionTests`

C6-B2の18Biome表層Groundパレットと8地質の露出基盤Ground・自然Wallパレットについて、確定表の材質順と重み、全26行の安定ID、行内材質の一意性、各パレットの重み合計256を保証する。本番Bakerと同じ共通Definition・地形・41材質カタログへ接続してRuntime Blobを構築し、全Ground候補が`GroundBlock`、全Wall候補が移動遮断・水密属性を持つ`WallBlock`へ解決されることを確認する。入力行を逆順にしても安定ID順の検索結果が変わらず、重み合計違反、行内材質重複、安定行ID重複、予約ID、Range外参照を拒否する。C6-B3以降は自然土壌飽和不足の泥炭を必ず置換できるよう、全Biome行に非`PeatSoil`候補が1件以上あることも構築時に保証する。

#### `DeterministicTerrainBlockPaletteSelectorTests`

C6-B3の純粋Blockパレット選択について、表層64／32／16タイル・4：2：1、地質32／16タイル・2：1、露出32／16タイル・2：1の固定設定と用途別Saltを、負座標の明示ノイズ合成と照合する。0〜255へ変換した累積重み区間の全境界、露出式の64bit合成、境界39,320／39,321／39,322、露出率0／65,535の端点、中間値だけの厳密な未満比較を保証する。`SwampForest`で選ばれた`PeatSoil`は自然土壌飽和39,320で最初の非泥炭候補`MarshMud`へ置換し、39,321では維持する。同一入力の再実行とBiome・地質行の逆登録順で、途中ノイズ、候補材質、最終Ground、自然Wallがすべて一致することも確認する。C6-C1では同じテストFixtureへ、陸上Ground・自然Wallの直接RuntimeId、河道の淡水Liquid、海域の`BiomeId.None`・海水Liquid、未確定湖候補の拒否を追加し、旧Ground／水域分類を経由せず自然生成セルを確定することを保証する。

2026-10-26のC6-B3 AI確認では、Unity 6000.3.8f1 Editorで`DeterministicTerrainBlockPaletteSelectorTests` 7件、B2・B3の`TerrainBlockPalette`関連14件、共有既定設定1件がすべて成功し、対象実行後のConsoleはエラー0件・警告0件だった。生成済みプロジェクト参照を使うRuntime・World Generation Testsの静的ビルドも警告0件・エラー0件で成功した。World Generation EditMode Assembly 622件の一括実行は約52分31秒で完走し、620件成功・2件失敗だった。失敗のうち`WorldGenerationMapViewerTests.Sample_ReportsDiagnosticChunksAndCompletedRowsWithoutChangingPixels`は一括実行時に180秒でTimeoutとなったが、単独再実行では90.55秒で成功した。もう1件は当時まだ残していた旧境界ハイライトの削除待ちゲートであり、C6-D6で削除対象を全比較・検品インフラへ広げた通常テスト`TerrainProductIsolationTests.ComparisonInspectionRemovalGate_RemovesAllInfrastructure`へ置き換えて成功済みである。全体実行後のConsoleはエラー0件で、警告6件は既存ECS Systemの無効な更新順属性2種類が各3回出力されたもので、C6-B3の変更箇所からの新規警告はなかった。

#### `WorldGenerationDistributionAuditTests`

Editorの分布監査と同じAnalyzerを小さな固定サンプルで2回実行し、バイオーム・基礎地形・土壌・水域の集計漏れがないことと、署名・レポートが決定論的に一致することを保証する。
広域のバランス確認はテストへ組み込まず、`Farmware > World Generation > Analysis > Analyze Distribution`から実行する。

#### `GeologyDistributionAuditTests`

Editorの地質分布監査と同じAnalyzerを、負座標を含む2シード・13×13代表格子で2回実行する。全サンプル数、東・北へ一度ずつ比較した全隣接辺数、8分類の件数合計、地質別境界参加数、標高区分・現行Biome・火山活動度5段階のクロス件数が一致することを保証する。4近傍領域数、1サンプル孤立領域、最大概算面積・直径が有効範囲にあり、署名と日本語レポートが決定論的に一致することも確認する。

既定3シード・129×129格子は通常テストへ組み込まず、`Farmware > World Generation > Analysis > Analyze Geology Distribution`から明示実行する。面積、直径、境界長は代表格子による比較用概算であり、自動合否閾値にはしない。

#### `AnimalPopulationGenerationTests`

WORLD-01Cでは、同じSeed・地域X/YでもLayerが異なれば独立した地域乱数状態となり、Surface候補がSurface位置を保持することを検証する。既存Surfaceの乱数系列は変更しない。

新規Worldへ非0の個体群Seedを1件だけ作り、復元済みSeedを上書きしないことを確認する。全タイルが有効な地域では密度8の目標数、3〜6羽の群れ、最大徘徊距離、位置重複なし、外見差分範囲を検証する。6羽を一群で置けない3タイルずつの分断地形でも、群れサイズを3羽まで縮めて2群を作り、目標6羽を補充できることを回帰確認する。さらに、4羽を先に確定すると1羽不足する5タイル＋3タイルの分断地形では、有限バックトラックにより5羽＋3羽へ戻して目標8羽を満たすことを確認する。さらにSeed 1と8つの適格座標を使い、先行メンバーが後続群れの必須タイルを塞ぐケースでも、メンバーの組合せを選び直して全8羽を配置すること、群れサイズ・徘徊距離・重複回避・受付順・外見範囲が有効で、再実行時の候補と地域乱数状態が一致することを保証する。完全配置できず部分補充へ戻る地形では、既存候補と既存占有を保持し、失敗分岐の候補や受付順が残らないことも検証する。同じ個体群Seedと地域状態では候補が一致し、地形が同じでも個体群Seedが異なれば配置が変わること、負座標の地域位置、BiomeとGroundの両条件、面積比例を確認する。ECS Systemでは逆順に作った地形チャンクへ地域座標順の永続IDを割り当て、初期化済み地域の再ロードで個体やIDを追加しないことを保証する。固定Tick停止中にPresentationが完成チャンクを`Active`または`Inactive`へ進めた場合も、再開後に地域状態、8羽、処理済みタグを作る回帰テストを含める。

木・石・砂で予約されたセルを避け、有効地形面積から求めた目標数は変えず、空いている8セルへ8羽を配置することをSystemテストで検証する。資源Bufferの欠落・4095件の未完成状態では地域状態・処理済みタグ・個体・IDを確定せず、4096件へ完成した後のTickで補充できることも保証する。

`GenerationBoundary_KeepsPopulationIndependentOfWorkerCompletion`は、実際のBurst地形Jobを2件ずつ2バッチ投入し、片方を先に完了・もう片方を先に完了・強制完了なしの3条件を、通常進行と一時停止で比較する。sleepによるタイミング推測はせず、反射は実物JobのHandle読取と全枠の明示完了に限り、本番の公開経路と固定Tick境界を実行する。通常フレームでは完成枠だけを公開せず、停止中も未完了なら待たず、全件完成を確認した描画更新でまとめて公開すること、IDはその時点で消費されないこと、Tick 12・13の各地域の初期化時刻と、全個体ID・群れID・抽選状態・次回IDを含むハッシュが一致することを保証する。`GenerationBoundary_RunsAtFixedTickEntry`は、公開Systemの所属とTick加算後・Command前の順序属性を確認する。地形単独Worldの既存非同期テストは引き続き通常回帰へ含める。

`GenerationSystem_PreSchedulesRunningWorldAndPublishesOnlyAtFixedTickBoundary`は、個体群Worldの通常フレームで要求が`GeneratingData`へ進み、要求が無効化されても地形・資源・水文・表流水Bufferは空のままであることを確認する。テスト側でWorker完了を確定した後に固定Tick公開Systemを実行し、地形4096件・資源4096件・水文64件・表流水4096件が同じ境界で`ReadyToApply`へ進むことを保証する。これにより、Job開始を早める性能変更、Main Threadで待たない完了確認、Simulation結果の公開時点を分離して回帰検出する。

再レビューのフレーム分割反例には`GenerationBoundary_MatchesAcrossFramePartitionsAndRequestCreationOrder`を追加した。最初から4つの同じ生成要求を用意し、親Simulation Groupと実物のRate Managerを通して0.2秒×1フレーム・0.1秒×2フレーム・0.05秒×4フレームを比較する。非同期延期で追いつきTickを破棄した条件は通常の0.1秒フレームを追加し、同じ論理Tick 2へそろえて比較する。最後の条件では要求Entity作成順も逆転する。各条件で2 Tick後に4地域が揃い、座標順の先頭2地域がTick 1・残り2地域がTick 2に初期化され、ID・群れID・抽選状態を含むハッシュが一致することを保証する。

C3-C1の水文生成は、8水文行と地形・資源を9つの`IJobParallelFor`作業へ分ける。各行は出力配列の異なる8要素と上流Heapの異なる32,768要素だけを書き、9作業の失敗通知をすべて確認してから一括公開する。水文純粋計算31件、個体群境界23件、`WorldGenerationTests` 68件、PlayMode 56件を関連回帰とし、長距離PlayModeでは`ChunkGenerationBoundaryWait`の変更前59.804ms、先行投入後42.762ms、行並列化後19.478msを同じEditorセッションで記録した。C3-D1のMain Thread待機除去後は、同じ通常Editorの長距離往復テストで`ChunkGenerationBoundaryWait`最大0.149ms、`ChunkGenerationHydrologyBaseWait`最大0.143ms、`ChunkGenerationLakeWait`最大0.001ms、`ChunkGenerationTerrainWait`最大0.001msだった。最終差分の全EditMode 498件と全PlayMode 56件もすべて成功した。各値は単一実行の診断値であり、製品のFPS合格値にはしない。

#### `AnimalPositionViewportCacheTests`

同じX/YにSurface／Underwater個体が存在しても、選択Layer側だけを可視キャッシュへ含めることを検証する。

1,000／10,000／100,000の画面外個体と4つの同Chunk内個体を用意し、可視矩形内の3タイルだけを保持すること、初回以外は不変Chunkの範囲更新が0件・可視Chunkの位置読取が4件となることを確認する。負座標・包含境界・Camera範囲変更・Simulation Systemによる位置変更・Entity破棄・索引消去も検証する。`VisibleCache_ReportsScalingComparison`は`Explicit`の局所計測で、旧全件配列化と新しいECS Chunk範囲索引を同じWorld・矩形で3回暖機後24回比較し、CPU平均・managed allocation・読取件数を記録する。通常回帰では実行せず、開発者表示の変更時に明示実行する。人工配置・Editorの結果であり、全個体移動・実際の探索順によるChunk混在・製品Playerの性能合格へ外挿しない。

#### 生成境界のPlayer検証履歴と再検証

この自動回帰とEditor局所計測だけでは、固定Tick公開境界のDevelopment Player性能を確認済みにしない。2026-10-19に修正commit`abb0e373`後の次作業として、修正前`b2713628`との同条件Player比較を10ケース・17,915前面フレームで実施した。通常／拡大Viewport、通常／高速移動、停止10秒・再開20秒で可視未完成は0件・0秒、停止中のTick・次回ID・個体群は不変、再開後の追加も確認した。生成境界待ち最大12.656msとフレーム間隔最大約32.0msを含む費用を記録し、性能改善・製品性能合格とは扱わない。製品コードは修正commitと完全一致へ戻したため、同commitで成功したEditMode 296件・PlayMode 53件は再実行せず、比較ビルドと復元後のコンパイル確認を追加検証とした。既知のビルド警告・終了時ComputeBuffer警告は両版で残る。続いて修正後Playerの5ケースを開発者向けに再生し、「問題ないように見えます」との確認とcommit・push許可を得た。今回PRの局所回帰と表示・停止感の受け入れは完了とし、製品最大Viewport・長時間・対象機器の性能合格とは分離する。条件・終了条件は[ロードマップのANIMAL-01B残検証](./ImplementationRoadmap_2026-10-19_Archive.md#animal-01b-残検証生成公開境界のdevelopment-player回帰)、実測値・成果物・制約は[性能計測の追補](./Map02DPerformanceReport.md#修正commit後のdevelopment-player比較条件)を参照する。

2026-10-19の投入境界・可視キャッシュ修正後は、World Generation EditMode 225件・Common EditMode 76件・PlayMode 53件と、明示局所計測1件が成功した。同日、開発者から「問題ありません」とcommit許可を得て、今回修正のEditorでの人間確認も完了した。上記`abb0e373`のPlayer比較・人間確認は同commitの履歴であり、今回変更のPlayer性能を確認済みとはしない。

続いて修正commit`b401ab41`後の最初の検証作業として、比較元`45401b9e`とのPlayer再比較を10ケース・17,879前面フレームで実施した。可視未完成・計測中エラー・警告は全ケース0で、停止中のTick・ID・地域・個体数不変と再開後の初期化を確認した。最大間隔61.368msと境界待ち最大12.690msを記録し、性能合格とはしない。初期地域・個体数と実時間の停止解除Tickには前後差があり、同一Tick入力の決定論試験と混同しない。製品コードとテストは修正commitと一致へ戻したため、上記の自動回帰は再実行せず、ビルド・Player計測・復元後コンパイル確認を追加検証とした。同日、開発者から「問題ないように見えます」と今回Playerの表示・停止感の確認とcommit・push・レビュー返信・スレッド解決の許可を得て、最大間隔・待機費用の制約を残したうえで今回範囲を受け入れた。PR #23マージ前の今回検証は完了とするが、後続ゲートの製品性能合格とは分離する。条件・費用・成果物・制約は[性能レポート](./Map02DPerformanceReport.md#固定tick投入修正後のdevelopment-player再比較)、完了条件は[ロードマップ](./ImplementationRoadmap_2026-10-19_Archive.md#animal-01b-再レビュー追補フレーム分割と可視位置キャッシュ)を参照する。

#### `AnimalPopulationDistributionAuditTests`

Runtimeと同じ資源予約を生成して候補から除外するEditorのAnalyzerを、負座標を含む2組のSeed・18地域で2回実行し、適格タイル・目標数・生成候補が存在すること、群れ契約違反と位置重複が0件であること、署名と集計値が一致することを保証する。約100万タイルの既定監査は通常テストへ含めず、`Farmware > World Generation > Analysis > Analyze Animal Population`から明示実行する。

#### `WorldGenerationMapChunkCacheTests`

Editor専用の上限付き完成自然地形キャッシュを7件で検証する。5種類の出力配列を元Native配列から値コピーし、元の破棄・復元先の編集・復元先の破棄が次の要求へ影響しないこと、Seed・GeneratorVersion・海面閾値それぞれの変更で失効すること、同じX/Yの別Layerを混ぜないこと、最大32チャンクを超えた古い値を除去し、今回必要な最古の値は不足分追加の前に保護することを保証する。生成器の速度やメモリの合格値をテストへ持ち込まず、規定長のテスト所有配列だけを使う。

`WorldGenerationMapViewerTests.Sample_TerrainDeltaSnapshotChangesFinalizedTerrainAndColor`は同じ自然地形キャッシュを3要求で共有し、自然地形、編集差分あり、差分なしへ戻した場合の色・Hover・差分件数を検証する。キャッシュへ編集結果を混ぜないことを統合経路で保証する。通常表示とMap Viewerの遅延計測・残件は[地形表示の遅延調査](./Terrain01C6D5PerformanceReport.md)を参照する。

2026-10-28、Unity 6000.3.8f1で新規キャッシュ6件が0.68秒、更新したMap Viewer関連65件が128.14秒ですべて成功した。再コンパイル成功、最終Console実状態のエラー0・警告0、Play停止・Scene未変更・背景実行falseを確認した。この時点はキャッシュだけの変更の確認である。続く部分表示と初回Job生成の検証は下記および遅延調査記録を参照する。実画面の人間確認とDevelopment Buildの性能評価は残す。

#### `WorldGenerationMapChunkGeneratorTests`

未訪問地形の専用Editor World生成を4件で検証する。製品Burst Jobの5出力配列が従来の同期診断と全要素一致し、Default Worldを置換せず終了後のWorld数も不変であることを確認する。投入直後の取消では実行中Jobの終了を待って破棄し、後続チャンクとキャッシュ公開を行わないこと、事前生成必須のSamplerが不足データを同期生成しないこと、32件超の要求をWorld確保前に拒否することを保証する。Unity 6000.3.8f1で4件が14.22秒で成功した。容量境界の回帰追加後、生成4件とキャッシュ7件の合計11件も14.36秒ですべて成功した。専用WorldのJob明示送出を追加した後にも生成4件が19.71秒で再成功した。

#### `WorldGenerationMapViewerTests`

WORLD-01Cでは、UnderwaterがLayer付きの未生成結果となり、Surface Generatorを複製せず固定の未接続色を返すことと、UXMLにLayer選択欄が存在することを検証する。

Map Viewerの全ピクセルが、同じ座標について完成診断チャンクへ保存した`GeneratedTerrainCell`と任意の差分Snapshotから求めた色と一致することを保証する。Terrain表示はLiquid、Wall、Groundの優先順を使い、Ground／Wall Runtime IDを41材質の安定IDへ解決した色を表示し、同じ材質のWallはGroundより暗くする。Hoverへ同じ安定材質IDを保存し、画像生成後にRuntime IDを再推測しない。差分Snapshotは負座標の床除算、チャンク正側境界、LocalTileIndex昇順を検証し、別セルや別レイヤーへ漏らさない。Biome表示は地表Blockの有無や変更可能Blockに左右されず同じ論理Biomeを保つ。
完成地形と派生水文のThreadPool統合テストは、Definition、Terrain、材質、Paletteの各BlobをMain Threadで先に完成させ、実Windowと同じ管理スレッドへ読み取り専用で渡して完成診断チャンクを生成する。ThreadPool内でBlobを再構築せず、Unity Entities内部のMain Thread限定`Allocator.Temp`へ到達しないことと、処理完了前に共有Blobを破棄しないことを保証する。
TERRAIN-01C1では、標高、基礎気温、地形気温、基礎湿度、木本植生ポテンシャル、地形起伏、海洋影響、火山活動度の値域両端がそれぞれ異なる不透明色になり、GameビューのDebug Visualizationと同じRGBを使うことを確認する。Dropdownの`int`選択Indexは`byte`基底の表示モードへ型を合わせてから検証し、定義値を保持しつつ未選択・範囲外値を`Terrain`へ戻すことも保証する。
TERRAIN-01C2-Bでは、地質3特性の値域両端、8分類、Definitionフラグ、異なる支配サイト、サイト内部・境界が不透明な別色になることを確認する。支配サイト診断は固定Seedの広域格子で同じ座標の`DeterministicGeologyGenerator`結果と一致し、上下左右のサイト差から少なくとも1つの境界を検出できることを保証する。Dropdownの有効上限も`Geology Site Boundary`まで拡張し、その次の値は`Terrain`へ戻す。
初回レイアウトの`NaN`、無限大、最小寸法未満では生成を開始せず、解像度計算も非有限値を拒否することを確認する。サンプラーの行進捗が100%に到達し、進捗通知の有無で画像が変わらないこと、初回生成案内のUXML要素と画像適用後の案内非表示も回帰対象とする。
あわせて、負座標とY反転を含むPointer座標変換、同じ表示ピクセル内のPointerが同じ代表タイルを返すこと、生成TextureとCanvasの表示解像度が異なるときの縦横比維持とリサイズ後の再生成判定、Pointer・パン・グリッド用座標変換とカーソル中心ズーム、描画・リサイズ・ズームが共有する通常768／基礎水文64／派生水文の各軸最大3所有チャンクとなる動的上限、派生水文5選択肢の有効範囲、ツールバーの＋・－Buttonによる中心座標不変のズーム、偶数寸法Textureを再生成した直後のHoverがSide Panelと同じ中心タイルを示すこと、奇数・偶数Textureでのグリッド・原点と地形サンプル境界の一致、Bakerと共有するAuthoring設定Snapshotのシード変換、新規・旧丸め値のどちらでもAuthoringと純粋計算の主要な既定気候閾値が一致することを検証する。湿度閾値をInspector上限へ設定した場合にも、乾燥・森林・湿地の境界が厳密な昇順を保つことを保証する。

TERRAIN-01C5-Dでは、全27モードの表示名、実行経路、解像度方針、進捗方式、Hover用データ契約が単一Catalogでenum順に一対一となり、完成チャンク経路だけが動的チャンク上限と二段階進捗を使うことを保証する。完成地形サンプルの全地上タイルでは、同じ固定入力から再取得したBiome選択診断の安定IDが最終`BiomeId`と一致することを確認する。最終理由、候補数、通常首位・次点、Score、Variation、両候補の8軸適性をMap ViewerとGameビューが共有する整形結果へ含め、5桁の適性値を各候補3行へ分割して診断全体を常に12行に保つことも固定する。2026-10-23のUnity 6000.3.8f1 Editorでは、`WorldGenerationMapViewerTests` 56件が117.19秒、World Generation EditMode 560件が275.26秒、PlayMode 56件が134.72秒で全件成功した。PR #47の表示幅レビュー対応では、最終IDと理由、候補名と順位値も意味単位で分割した。再コンパイルがエラー0件、`WorldGenerationMapViewerTests` 57件が118.57秒で全件成功した。Editorの12ptラベル実フォント計測では5桁値を持つ最長の適性行が371.06pxで、430pxの内容幅以内だった。続く縦見切れ修正では、960×540のGameビューを入力に、全モード共通の右上パネルが描画領域内へ収まり、超過したBiome内容高をスクロール可能量として保持すること、短い内容では縦バーを表示せず430pxの全幅を内容へ使うことを回帰テストで保証する。HUDは`ScrollWheel`イベントを処理せず、表示中のバーのドラッグまたはクリックだけで移動する。内容高は固定値ではなく実際の表示文字列の行数から求め、末尾の余白を6pxに限定する。再修正後の`WorldGenerationMapViewerTests` 59件は119.428秒で全件成功した。実Gameビューでは短い`Soil`モードでバーが表示されず最終行直下だけに余白が残ることと、Biomeでは超過時のバーが表示されることを確認した。Biome末尾まで移動した際の最終余白は人間の実表示再確認を残す。最終Consoleはエラー0件・警告0件である。

同日の追加レビューで、縦バー操作中に文字がHUD外へ描かれることを確認した。内容要素のY座標だけからスクロール量を差し引く初回修正では実表示が改善しなかったため、文字Styleの既定`Overflow`も無効化し、上下枠より2px内側の明示クリップ範囲を追加した。回帰テストではスクロール座標に加え、クリップ範囲のX座標と幅を維持しながら上下端だけが各2px内側にあることを保証する。対象3件は0.011秒、更新後の`WorldGenerationMapViewerTests` 60件は119.526秒で全件成功した。スクロール中に文字と凡例がHUD外へ出ないことは人間の実表示再確認を残す。

続くレビューでは、Map Viewerの最小Window寸法が720×700ポイントであることを固定し、地図最小240pxと17行Hover診断280pxに加えてヘッダー、操作欄、状態欄を同時表示できる高さを保証する。旧480pxへ戻り、`overflow: hidden`の親Panelで地図または診断が欠ける退行を防ぐ。追加した寸法契約テスト1件は0.04秒、更新後の`WorldGenerationMapViewerTests` 61件は118.3秒で全件成功した。

上記に残していたBiome末尾の余白、スクロール中の文字・凡例クリップ、最小Windowでの17行実データ表示は、2026-10-23の人間確認でいずれも問題なしだった。

続くP1レビューでは、Editor再コンパイルだけでは検出できない`UNITY_EDITOR`条件の不一致をStandaloneOSX Player Buildで確認する。Playerにも含まれる呼び出し元とBiome診断Formatterが同じコンパイル範囲にあり、`CS0103`を発生させないことをBuild Reportで保証する。修正後のPlayer Buildは71.094秒で成功し、エラー0件、Pipeline Runtime設定未作成を示す既知の警告1件だった。既存の文字列契約を検証する`WorldGenerationMapViewerTests` 61件も121.08秒で全件成功した。

#### `TerrainLayerPresentationUtilityTests`

16×24土地の上面16px・断面8px・Pivot Y 2/3、16×28岩壁の上面16px・側面12px・Pivot Y 2/7、液面を8px下へ移す`-0.5セル`をSceneなしの純粋計算として検証する。淡水・海水・マグマを別の液面アニメーションへ分類する規則も保証する。

旧一体型PNGだけを保証した`WorldGenerationFallbackArtTests`と旧土地・岩壁Atlasの`WorldGenerationSpriteAtlasTests`は削除済みである。製品41材質・680枚のAtlasは`TerrainMaterialProductPublisherTests`、旧素材・参照・APIの削除は`TerrainProductIsolationTests`で保証する。

`WorldGenerationMapViewerTests.CaptureTerrainDeltaSnapshot_RebindsWorldLocalIdsAndRetainsRemovals`は、地形よりGUID順が前の追加定義で全Runtime IDをずらしたWorldを作る。実WindowのSnapshot取得からサンプラーまで通し、負座標のGround／Wall材質・Liquidと明示除去が一致し、元の差分Bufferが変わらないことを保証する。

#### `TerrainTileArtGeneratorTests`

専用`WallTop`、材質別7外周部品、共通暗色内部、47枚のWall完成Spriteを対象とした旧試作テストは削除済みである。現行テストは、sRGB 8bit RGBの0.85暗色化と最近傍丸め、Alpha維持、`Face`を変えず不透明な`FaceBoundaryOverlay`だけを暗色化するGround／Wall合成、半透明Overlay拒否を検証する。

可変長配列については、手描き入力`G + F + B`、生成結果`F×B×(1 + D)`、Runtime候補数の計算を確認する。設定検証では16×16 px、Read/Write、Point、16 PPU、非圧縮、Mip Mapなし、sRGB、`Full Rect`、Alpha 0／255、材質ID、暗色化係数を対象とする。差分配列が個別Weightを持たず全候補を均等に扱うこと、暗色化ON・0.85、専用WallTop設定が存在しないことを専用テストとInspector確認で保証する。生成PNGの実Import、再生成時のGUID維持、Fingerprintによる要再生成、旧出力整理はGeneratorの実操作で確認する。境界なし材質は旧生成物が残らない限り初回から生成不要であり、GroundTopだけの変更は境界Fingerprintを変えない。境界ありから境界なしへ変えた場合は、以前の生成物を整理するまで更新必要と判定する。製品公開前検証では、境界生成物を持つ材質だけ現在Fingerprintと最終正常生成Fingerprintを比較し、画像や設定が変わったままなら同数の旧生成物を受理しない。境界なし材質は現在の入力Spriteを直接使うため、生成物がないことを理由に拒否しない。代表材質のInspector操作では、Ground／Wall境界の全`F×B`組合せが用途別に64×64の整数4倍で表示されることを確認する。

#### `TerrainMaterialSpriteImportUtilityTests`

材質専用Sourceフォルダー配下のPNGだけを対象とし、他用途の画像を変更しないことを保証する。新規64×48 px PNGへSprite Multiple、Point、16 PPU、Clamp、非圧縮、Mip Mapなし、sRGB、Read/Write有効、`Full Rect`を適用し、16×16 pxの4列×3行を上段左から安定した名前で分割する。初回Import中に透明領域基準の横長2区画が作られた状態を再現し、参照が作られる前だけ12区画へ置換できることも検証する。同じパスへ修正版PNGを書き戻して再Importした後は設定変更なしとなり、全12区画のSprite IDが維持されることを確認する。既存の境界なし材質は上2行の8区画も許可する。規約外寸法と初回Import完了後の8／12以外の既存分割はSerialized参照保護のため自動修復せず、区画とSprite IDが変化しないことを保証する。

#### `TerrainMaterialProductPresentationTests`

C6-D2の製品上面・断面経路を、41材質の完成Spriteを製品Sceneへ割り当てる前に純粋分類とRuntime Asset契約へ分けて検証する。Wall前面が手前2セルだけ、Ground崖面が連続する乾燥穴3セルだけへ投影され、液体やWallを越えないこと、北側チャンク境界を越えて同じ完成地形を参照することを確認する。穴上端0 px、段境界16／32 px、下端48 pxのFace Alphaが1、2/3、1/3、0となり、段ごとに濃さが戻らないことも保証する。

製品Resolverについては、GroundとWall上面が同じ`GroundTop` Spriteを選び、Wall上面だけへ0.85 Tintを掛けること、Ground／Wallの1段目が用途別境界合成済みSpriteを選ぶこと、通常`Face`を暗色化しないことを確認する。Runtime Tile Cacheは同じSprite・Tint組と深部黒Tileを再利用し、セル数に比例してUnity Objectを生成しない。乾燥崖Materialが検品用ではなく製品用`Farmware/Terrain/Dry Cliff Face` Shaderを参照することもAssetDatabaseから検証する。

41材質カタログ、液体3種カタログ、Ground／Wall断面、乾燥崖深部黒、液体専用レイヤー、AtlasはC6-D6でSampleSceneへ接続済みである。`TerrainProductSceneConnectionTests`は両製品カタログと採用レイヤーのSerialized参照、Material、Transform、Sorting Layerに加え、`TerrainWall`を照らすGlobal Light 2D、Wall背後資源と通常資源の前後関係、液体Surface→深度→反射→水際の順を確認する。2026-10-28に採用したマグマ網状模様は、専用の`magmaSurfacePatternTilemap`参照と`Magma Surface Pattern Tilemap`を使い、専用Material、Renderer有効状態、WaterSurface Order 2、反射より後ろであることを追加で保証する。網目6pxへの縮尺調整後、見た目はユーザー確認済みである。GPU描画テストは4fpsのコマ保持、明滅を止めても網目が変形すること、停止、負原点と4096px周期、16px原点移動、奥岸3pxクリップを検証する。SampleSceneのPlayMode回帰ではマグマ模様の共通時計、Material共有、水セルへの非描画も検証する。旧AnimatedTile分類専用テストを削除し、対象プラットフォームのGPU負荷計測は未実施として残す。マグマ岸の断面回帰では、全3液体・Wall有無の奥岸投影、実PresenterがWall前面を消さずGroundの縁を分類すること、模様の表示有無にかかわらずGPU出力の上端3pxだけに断面が残ることを追加検証する。

2026-10-28のマグマ正式採用時のAI確認では、Unity 6000.3.8f1で`TerrainLiquidRenderingTests` 38件（追加マグマ4ケースを含む）、`TerrainProductSceneConnectionTests` 4件、`TerrainLayerPresentationUtilityTests` 8件、`WorldGenerationSceneBootstrapTests` 10件がすべて成功した。SampleSceneのストリーミング・決定論回帰はチャンク別・共有Tilemapの各1件、合計2件が成功し、実行時の共通時計とMaterial共有も確認した。最終のUnity ConsoleとマグマShaderはエラー0件・警告0件だった。全プロジェクトの全テスト、製品ビルド、対象環境のGPU性能測定は今回の実行範囲に含めない。

2026-10-28のマグマ奥岸断面の修正では、共通岸判定を含む`TerrainLiquidPresentationUtilityTests` 58件、実Presenter分類を含む`TerrainMaterialProductPresentationTests` 9件、GPU読戻しの`TerrainLiquidRenderingTests` 40件が成功した。製品SampleSceneのPlayModeストリーミング回帰1件も成功した。マグマの奥にGroundだけがある場合とWallを持つ場合を検査し、水面より下を透視させない契約を維持する。奥岸の断面修正後、ユーザーが実際の見た目に問題ないことを確認済みである。

2026-10-26のC6-D2 AI確認では、Unity 6000.3.8f1 Editorの再コンパイルが警告0件・エラー0件、`TerrainMaterialProductPresentationTests` 7件、`TerrainMaterialPresentationCatalogTests` 9件、`TerrainNeighborHaloUtilityTests` 12件、`ChunkLocalTilemapPresentationTests` 21件がすべて成功した。PlayMode回帰の準備BufferとPresenter所有HaloをC6-C3以前の`GeneratedTile`から現行`GeneratedTerrainCell`へ更新し、Dirtyサブチャンク、チャンク単位カリング、Floating Origin、表示プールの既存契約を再確認した。Presentation、EditMode Test、PlayMode Test Assemblyの静的ビルドも警告0件・エラー0件で、最終Consoleはエラー0件・警告0件だった。C6-D6で41材質とAtlasの自動接続は完了し、製品Sceneの実Sprite、Sorting Order、48 px Shader描画だけを人間確認へ残す。

#### `TerrainMaterialProductPublisherTests`

C6-D6の素材公開について、今回の画像で確定した境界あり11材質が安定IDまで一致し、製品カタログが全41材質を境界あり11・なし30で初期化できることを保証する。各行の`GroundTop`、通常`Face`、Ground／Wall境界完成Spriteを走査し、製品カタログがEditor専用`Source`または生成設定を直接参照しないことを確認する。製品Atlasは41材質×通常8枚の328 Textureと、境界あり11材質×Face 4×境界4×Ground／Wall 2用途の352 Texture、合計680 TextureだけをPackableに持つことを検証する。Atlasの実パックと1ページ化は公開処理自体でも検証し、製品Sceneを接続する前に失敗させる。

#### `TerrainWallShadowUtilityTests`

C6-D4の製品Wall影を、41材質と製品Sceneを接続する前に純粋形状、Runtime表示再利用、製品Materialへ分けて検証する。完成済み中心チャンクと南隣Haloから「Wallかつ画面手前側の南隣が非Wall」の露出根元だけを抽出し、同一行で隣接する根元を1区間へ圧縮する。奥側Wallを除外すること、チャンク境界でも南隣Wallを参照すること、空きセルと行境界で区間を分割すること、入力へ材質IDを含めないため異材質Wallも連続区間になることを確認する。32 px折れ目影は1段目上端Alpha 35%から31 px目の0%へ単調減衰し、15／16 px境界で濃く戻らないことを保証する。固定接地影の左右1 px・手前3 px・各Alpha 22%、動的Casterの論理Wall根元に対する追加補正0タイルも共通定数として固定する。

Runtime表示テストは、同じDirtyサブチャンクを再反映してもCaster GameObjectを増やさず、既存Casterの位置と長さを更新し、論理Wallセルと最下段Face根元が同じ座標になることを確認する。PlayModeでは、プールから戻ったRootを先に有効化し、`ShadowCaster2D`を有効なHierarchy内で初期化してから非表示へ戻すことで、初回区間生成時に内部輪郭未初期化のNullReferenceを出さないこともSampleScene縦切りで確認する。Assetテストは製品用固定接地影と折れ目影Materialが製品Shaderを参照し、Shaderエラーがなく、開始行0／16 pxと採用Alphaを保持することを確認する。C6-D6では製品SceneへWall上面・前面・折れ目影の+2Y、固定影の+2Yから手前3 px、動的Casterの根元座標、Sorting Order、全41材質、Atlasを接続し、比較Sceneの仮`Light 2D`と検品用生成Assetを削除した。正式な環境光・局所光はTIME-01Bで接続する。

#### `TerrainSubChunkDependencyTests`

必要近傍Maskと実Presenterの部分反映を10件で検証する。中心だけなら内部4部分を表示できること、必要な辺だけを待つこと、正負ワールド端で域外を要求しないこと、不正な部分Indexを拒否することを確認する。実Tilemapへ内部だけが描かれ、近傍到着時に進捗を捨てず残りを反映すること、待機先頭が後続を止めないこと、使用済みEntity置換で進捗を失効させることを保証する。部分影は完成入力のある三角形だけを公開し、その頂点Alphaが全体完成時と一致すること、途中Meshを完成済みの原点変更再利用候補に残さないことも検証する。10件が0.32秒で成功し、関連Terrain全288件も48.09秒で成功した（包含関係のある実行であり件数は合算しない）。

生成Systemのパレット寿命回帰`WorldGenerationTests.ChunkGenerationSystem_KeepsPaletteAliveAfterSourceDisposal`は、Job投入直後に元カタログを解放してから完成を待つ。同じ値の再読込では所有Blobを再利用し、重みを変更した入力では新しい値を保持することを3回の投入で保証する。生成System関連10件が12.54秒で成功した。これは部分表示で完了が早まった後のScene連続読込から検出した寿命問題への回帰である。修正後のPlayMode Assembly全56件も128.98秒で成功し、連続Scene読込、正負の原点変更、アンロード再訪、共有／チャンク別表示を含む回帰を確認した。さらに可視辺を角より先に生成する回帰を東西南北4条件で追加し、`ChunkStreamingSystem`関連15件が5.62秒で成功した。優先順位変更後もPlayMode Assembly全56件が132.46秒で再成功した。`WorldGenerationTests`全61件もEditorログでは全成功したが、同期CLI応答は30秒で期限を超えて結果本文を受け取れなかったため、実行時間は記録しない。

#### C6-D5 有効地形・診断・境界回帰

Gameビューの通常表示、開発者可視化、岩壁影、Map Viewerが、完成自然地形と`TerrainCellDelta`を合成した同じ有効地形を読むことを保証する。中心と周囲8チャンクの自然地形Bufferに、同じ所有チャンクの疎な差分Bufferを重ね、負座標とチャンク境界でも同じ絶対セルへ解決する。表示Fingerprintは中心・周囲8チャンクと直下層の自然地形・差分を含み、差分変更、アンロード、Entity再利用、World切替後に古いサブチャンクを残さない。製品Presenterは旧260要素Haloと`TerrainNeighborHaloBuild` Markerを保持せず、必要な近傍が未完成なら自然生成へフォールバックせず、その入力を読む部分だけ反映を待つ。

Map Viewerの`Terrain`は同じ生成設定の実行中Worldがある場合だけ、メインスレッドで取得した不変の差分Snapshotをバックグラウンド生成へ渡す。表示色はLiquidを優先し、Ground／WallはRuntime IDから41材質の安定IDへ解決して、Wallだけ同色相を暗くする。Hover、凡例、取得元、実際に適用した差分サンプル数を同じ結果へ固定する。設定不一致またはEditModeでは自然生成であることを明示し、実行中の現在地形と誤表示しない。41材質凡例では長い名前を行内で折り返し、14pxの色見本を横方向にも縮小しないことを最小Window幅で確認する。

2026-10-27のC6-D5 AI確認では、Unity 6000.3.8f1 Editorの再コンパイルが警告0件・エラー0件、旧Halo削除前の有効地形近傍13件と、Map Viewerの負座標差分Snapshot、安定材質色、完成地形全画素一致、有効地形・色の統合回帰がすべて成功した。その後、製品から到達不能になった旧260要素Halo APIと旧挙動だけのテストを削除し、現行`CompletedChunkTerrainNeighborhoodTests` 3件と正負ワールド端2件もすべて再成功した。PlayModeでは、旧Presenter Haloを持たないこと、同一論理チャンクの自動原点変更で岩壁影を再利用すること、チャンク境界の連続影、自動原点変更の往復・負座標横断の4回帰が成功した。最後の回帰は、任意の差分配列を既定値のままUnity Jobへ渡せない安全性エラーを初回に検出したため、岩壁影マスクを完了済みBufferだけ読む同期ループへ修正して再成功した。自然地形Entity世代のFingerprint追加と近傍コンストラクタ整理後にも、`CompletedChunkTerrainNeighborhoodTests` 3件を0.020秒、往復・負座標横断PlayMode回帰1件を56.675秒で再実行し、どちらも成功した。Gameビュー、Map Viewer、F4、負座標、Floating Origin、アンロード・再利用の実表示一致と、ProfilerでのCPU時間・GC Alloc確認は人間確認へ残す。

#### 地形表示のDevelopment Player局所計測

2026-10-28、元プロジェクトを変更しない一時コピーで、現在実装と`b05e817f`の通常12／高速36タイル毎秒を比較した。各ケースは初期周辺16チャンク完成後に5秒静止・30秒移動・20秒静止し、macOS Universal／Mono Development、M3／24GiB、Metal、1920×1080、基準30×16.875タイル、先読み1・同時生成2・表示予算4を共通とした。専用引数だけで有効になる一時Componentを使い、ECS・Presenterの未反映Mask、ProfilerRecorderのCPU／GC／メモリ、重複timestampを除くFrameTimingManagerのGPU、OS RSSを記録した。行配列を事前確保し、観測ループ自身のmanaged allocationは全ケース0 byteだった。元Sceneや製品設定を変更せず、終了後は専用コピー内のC#・metaも削除した。

通常は比較元594/1,792フレームで未反映、現在版0/1,792、追加実行も0/1,794だった。高速の現在版は937/1,788フレームで未反映を検出し、停止後1.607秒で収束した（比較元7.235秒）。表示CPUとGC・表示メモリの増加、単発フレーム間隔66.269msと追加確認の35.573ms、既知の終了時ComputeBuffer警告も隠さず記録する。両ビルド成功、計5 Playerの区間中エラー・警告0、終了コード0を確認した。正式なFPS・全機器・最大Viewportの合格判定とは分離し、条件・全数値・制約・成果物は[遅延調査記録](./Terrain01C6D5PerformanceReport.md)を参照する。今回追加したのは計測と文書であり、製品C#・Scene・Assetは前回の確認時点から変更していないため、自動回帰全体の再実行は行っていない。

#### `TerrainLiquidPresentationUtilityTests`

C6-D3の製品Liquid表示を、製品Sceneへ全素材を割り当てる前に純粋分類、カタログ検証、Runtime Tile共有、製品Shader Assetへ分けて検証する。淡水・海水・マグマで共通する上端3 pxクリップは画面奥側がGroundまたはWallへ接する先頭セルだけを対象とし、連続液体の内部セルを切らないことを確認する。直下床ありのクリップ後45 px・最大Alpha 50%、床なしのクリップ後93 px・最大Alpha 100%も境界値で確認する。基底色を投影距離から独立させ、断面が各列の奥岸の真下だけへ描かれることを純粋計算とGPU描画で保証する。通常の奥・左・右接触辺に左右内角と左右の裏側終端クリップを加えた7bit形状を使い、画面手前辺や異種液体への接続を含まないことも保証する。カタログは現行形式の3液体と有効な水際RGB色を要求し、液体種別の重複・不正な色・旧カタログ形式を拒否する。基礎液面Tileは同じ入力とクリップ有無、深度Tileは深度データの組合せによらず全水域で1件、反射はTileではなく、Atlasの借用SpriteとDirtyなサブチャンクMeshで描画する。

通常Faceは淡水・海水が連続する最大6段だけ投影し、両者の境目は越えるが、マグマ・未知液体、GroundのないWall、空セルを越えない。交互に混在する全6段の断面・距離・元の液体種の維持、両向きの水種境界を挟むWall反射、マグマ・未知液体・空セルでの探索停止を検証する。Wallの下にGroundがある岸では、そのGroundを全6段の投影元に使い、Wallのある列だけ水中断面が欠けないことを確認する。マグマは液面より下のFaceと青深度を表示しない。液面より上のGroundの縁3pxとWall前面は残す。反射は淡水・海水から最大2段・32 px以内のWall Faceだけを対象とし、水平なGroundTop、WallTop、直下床、マグマを対象外にする。製品Materialが`Farmware/Terrain/Liquid Surface`、`Farmware/Terrain/Liquid Depth Overlay`、`Farmware/Terrain/Water Reflection Proxy`を参照し、水の基底色Materialに旧勾配設定が残らないことをAssetDatabaseから検証する。45／93pxと50／100%は断面Shaderの光学的被覆率としてGPU合成で検証し、反射の最大Alpha 0.22を維持し、更新頻度はMaterialの毎秒2コマを初期値とする。固定中心ごとの位相・周期を使う整数px変位、変位後に元Sprite矩形内だけをPoint Sampleすること、外周と透明部分の移動、水セル境界による固定した切り抜きを検証する。

直下床あり経路は、Presenterが同じX/Yの直下WorldLayerに完成済み3×3チャンク近傍を取得できる場合だけ使用する。現在のSurface専用ストリーミングでは直下層を同時常駐させないため、床あり45 pxと4段目以降の床プレビューのGameビュー確認は、Surface以外の自然生成・複数層ストリーミング接続後に行う。未準備時は床なし93 pxへ入り、現在層の材質を直下床へ読み替えないことを確認する。

#### `TerrainLiquidRenderingTests`

輪郭の揺れは、元位置のAlphaを固定マスクとして掛ける旧契約を廃止し、変位後の色とAlphaを一緒に読む。GPUテストでは振幅1px・濃度0.22と上限2pxを使い、外周が水面側へ広がる／後退すること、透明な穴が移動すること、付け根2pxが静止することを32コマ検査する。左右が陸地の1セル水路では外へ広がらず、上端3pxも覆わない。同じ接地点の内部Sprite境界に加え、接地点が1セル違う隣画像でも、受け手の揺れ深度を共用して穴・二重合成を作らないことを検証する。Atlasの隣画像混入、コマ保持、負原点変更の既存回帰も維持する。Shaderの初期値は一時Materialで検査し、ユーザーが調整する保存Materialには有効範囲だけを要求する。

2026-10-28の輪郭修正後は、液体関連80件（1.49秒）、水中描画純粋計算4件（0.02秒）、材質解決7件（0.03秒）、Scene接続4件（0.03秒）、Scene構築10件（0.03秒）、製品分離2件（0.91秒）のEditMode計107件と、SampleScene移動・再生成のPlayMode各1件（チャンク別46.60秒、共有46.83秒）が全件成功、Skipなしだった。添付動画の外周固定を確認したうえで、報告地点(-12.5,-30)の実シーンでも反射以外を静止させ、反射時計0秒／2秒の比較で外周が横へ変わることを確認した。8反射MeshがMaterial1件を共有し、当該表示での最大頂点数は332だった。これはProfilerの性能計測や最悪条件の実測値ではない。Editor内のユーザー調整（振幅2px・最大濃度1）と水面模様の調整値を保持し、水際Materialの保存内容は変更していない。再コンパイル・Shader・現在のConsoleにエラー／警告なし。確認用Playと一時停止を解除し、背景実行設定を復元して、比較画像は製品Assetから削除した。その後2026-10-28にユーザーが見た目を確認し、採用とcommitを承認した。水面模様Materialの既存調整は反射のcommitへ含めず、作業ツリーへ保持する。全体テスト、Development Build・Profiler計測は未実施とする。

反射投影の回帰では、接地点と高さに応じた到達／非到達、地面1セルを挟むWall、負座標、水面の上端3pxクリップ、24pxの連続減衰、実Wallの下段からの画像順を検証する。GPUは実際の`TerrainWaterReflectionMesh`を使い、Atlasの隣画像混入防止、外周と透明な穴の移動、内部タイル端の連続性、2fpsコマ保持、非同期変位、付け根の固定、原点変更の一致を読戻す。旧反射Tile・段数Alpha・行別4コマの期待値は廃止する。Scene回帰は製品Atlas参照、設定見本にTileがないこと、反射時計、Mesh間のMaterial共有、1サブチャンク最大6144頂点（256水セル×左右を含む3列×2画像×4頂点）を保証する。将来の木・設備・生物の描画元と、Development Build・性能計測は今回未実装／未検証である。

2026-10-28の反射改善では、液体関連73件（1.10秒）、水中描画純粋計算4件（0.02秒）、材質解決7件（0.02秒）、Scene接続4件（0.03秒）、Scene構築10件（0.03秒）、製品分離2件（0.89秒）のEditMode計100件が全件成功、Skipなしだった。SampleSceneの移動・再生成PlayModeはチャンク別46.72秒・共有46.69秒で各1件成功した。再コンパイルとConsoleのエラー・警告は0件。フレーム進行を確認したGameビューでは、報告地点(-12.5,-30)の断面と、Wallが水へ接する(-5,-53)付近を確認し、7反射Meshが実行時Material1件・Atlas Texture1件を共有していた。DontSaveの反射は全Object検索から除外されるため、Scene回帰では所有Gridの子からMeshを検査する。確認用Playは停止済みで、背景実行設定を元へ戻し、確認用画像を製品Assetから削除済みである。水際Materialは変更せず、水面模様のユーザー調整（密度0.3、出現5秒・休止4秒）は保持した。全ドキュメントの影響確認では、現行の描画契約・アート・Scene接続・テスト・ロードマップ・索引を更新し、生成・保存・生態・制作Prompt・過去の性能計測とArchiveは今回の描画変更で契約が変わらない。この時点では見た目の採用は人間確認待ちだったが、上記の輪郭修正後にユーザーが採用を承認した。全体テスト・Development Build・ProfilerによるCPU/GPU/GC計測は未実施とする。

水面模様の直線化では、形状分岐を削除し、厚さ1pxの連続水平線へ統一した。長さ3〜8pxの既存の位置・長さ抽選から作った期待画像とGPUの全画素を比較し、折れ・中央の欠けがなく、複数の長さが混在することを保証する。液体関連EditMode64件が2.74秒で全件成功し、Skipなし、Shaderのエラー・警告なしだった。コマ保持・非同期アニメーション・3pxクリップ・原点補正の既存回帰も成功した。両Materialの保存ファイルは変更していない。直線化後のGameビューの見た目は2026-10-27にユーザー確認済みで、commitも承認された。PlayMode・Build・性能計測は直線化後には再実行していない。

短い水面模様追加時のAI確認（2026-10-27）は、関連EditMode90件（液体63、純粋投影4、材質解決7、Scene接続4、Scene構築10、製品分離2）がすべて成功した。SampleSceneの移動・決定論的再生成はチャンク別方式48.64秒、共有方式49.29秒で各1件成功した。再コンパイルと新Shaderにエラー・警告はなく、Gameビューのフレーム進行、同地点(-12.5,-30)での短い線の変化、5 Rendererで1件の模様Material共有を確認した。水際Materialは変更せず、確認用画像は製品Assetから除いた。見た目はその後の直線化を経てユーザー確認済みとなった。全体テスト・Development Build・ProfilerによるCPU/GPU/GC計測は未実施とする。

短い水面模様は専用ShaderのGPU描画5件で、長さが不規則な厚さ1pxの連続水平線（折れ・中央の欠けなし）の全画素一致、疎な静止形状、16px境界を跨ぐ線、水域Tile消去後の非表示、4倍表示の素材1px内の均一性、大きな負原点への変更、実奥岸セルの上端3pxだけのクリップ、2fpsコマ保持、同一コマ内の成長と減衰の混在を検査する。Scene接続テストでは断面→水面模様→反射→水際の順を確認し、SampleSceneのチャンク別／共有方式PlayModeでは専用Tilemap、共通時計、全チャンクでのMaterial共有、セル情報の受渡しを確認する。ゲームルール・生成・セーブ形式は変更しない。

GPUを使えるEditorのEditModeで、未保存のPreview Sceneの製品TilemapをLinear浮動小数RenderTextureへ描いて読み戻す。水色単独の全7段・水種・床有無・奥岸クリップ、断面合成後の45px／93px減衰、同じTileの段数色だけの更新、岸Y差1・5・9と負座標での余計な断面の不在、上端3pxの地面の縁、4倍描画時の素材1px内4×4画素の濃度均一性を検査する。水際全127形状と明暗・原点変更の回帰は維持する。製品と同じApplyTilesでDirtyセルをRefreshしてから一括反映し、書込順の差をテストだけで隠さない。テスト用Scene・Texture・Tileは終了時に破棄し、GPUなしの環境では明示的にSkipして別のGPU付きEditorで実行する。

2026-10-27に修正前の実Tilemapで、穴上端から16 pxの期待Alpha約0.145に対し約0.021となる不具合を再現した。TilemapRendererが深度データを格納した頂点RGBへLinear色変換を掛けていたため、Shaderで元データ値へ復元した。また、水中Faceに乾燥崖の段内減衰が掛かっていたため、頂点Alpha 0を不透明な水中下地の識別値として復号するよう修正した。描画先にはURP Render Graphの要求に従って24bit深度バッファを確保し、深度なしRenderTextureによる警告を出さない。

#### 水際の静止形状・Shader回帰

`TerrainLiquidPresentationUtilityTests`は奥・左・右に左右内角と裏側終端を加えた7bit接触形状、異種液体をまたがない内角接続、連続する縦岸16px・奥岸および裏側終端の側線13px・内角の接点込み4px、左右独立の終端判定、淡水・海水・マグマ共通の終端、負側と正側の斜めHaloでの終端・継続判定、静止Tileの色×形状共有、旧カタログ形式の拒否、負座標とint両端での原点画素変換を検証する。

`TerrainLiquidRenderingTests`は専用水際Shaderと実Tilemapを使い、全127非空マスクの画素をCPUの形状契約と比較する。最低・最大濃度を1へ固定し、時間変化と色空間の影響を除いて形状の欠けを検出する。通常原点と負側の表示原点へ変更した描画を比較し、同じ絶対地点の明暗模様が維持されることも確認する。共有表示時間をMaterialへ明示入力し、完全透明を含む濃度4階調だけの出力（静止形状上のAlpha 0も計数）、毎秒4コマの保持と更新、同じコマで強まる・弱まる波の混在、旧全岸共通6秒周期への退行防止、4096px周期端での濃度連続性、4倍表示の素材1px内4×4画素の均一性をGPU描画で検査する。水中断面の回帰は、水色の均一性・実投影範囲・光学減衰を個別に検査し、結果の滑らかさだけを成功条件にしない。

`WorldGenerationScenePlayModeTests`のSampleSceneストリーミング回帰では、実際のPresenterが水際の共通表示時間を進め、チャンク別Rendererも同じ実行時Materialを共有することを確認する。Shaderだけが動き、製品Sceneでは時計未接続で静止する不具合を防ぐ。

2026-10-27の水際微調整では、濃度4階調・毎秒4コマ・その場での寄せ引きへ変更した。関連EditMode 37件が全件成功し、全EditModeでは744件中743件成功・1件失敗（325.02秒、Skipなし）だった。失敗は`SystemGroupBoundaryTests.CommonAssemblies_KeepOneWayLayerDependencies`で、変更前から`Farmware.Authoring.asmdef`が`Farmware.WorldGeneration.Runtime`も参照する一方、テストが`Farmware.Simulation`だけを期待している不一致である。該当asmdefとテストがHEADと同一であることを確認し、今回の水際変更とは分離した既存課題として扱う。水際のGPU検証では往復時の浮動小数丸めが階調を変える不具合を検出し、往路・復路の時間を同一範囲へ折り畳んで解消した。

同変更の全PlayModeは56件中48件成功・8件失敗（138.14秒、Skipなし）だった。水際時計・共有Materialを追加検証したSampleScene通常方式（45.27秒）、共有方式（35.01秒）、繰り返し原点変更（57.70秒）の3件は成功した。失敗8件は`ChunkLocalTilemapPresentationTests`で、1引数の旧`ChunkApplyProgress`コンストラクター呼出し3件と、地形カタログBlobを用意せず有効近傍を予約しようとするテスト5件である。該当テスト全体、`CollectPresentationWork`、`ChunkApplyProgress`が変更前のHEADと一致し、前者のコンストラクターは変更前から2引数、後者の有効近傍構築も変更前から地形カタログ必須であることを確認した。水際の修正とは分けた既存テスト整備課題として残す。

手動の実Scene確認では階段状岸と細い水路を撮影し、6 Rendererが実行時Material 1件を共有して表示時間を進め、元Materialの時刻と原点は0のままであることを確認した。検証後はPlayMode停止、背景実行false、Camera基準480×270、Scene未変更へ戻した。最終Console実状態はコンパイル失敗なし・エラー0・警告0だった。見た目の最終採否とDevelopment BuildでのCPU/GPU性能は人間確認・未計測として残す。

続く同日の濃淡調整では、水際の最低濃度を0.22から0、最大濃度を0.72から0.9へ変更し、Alpha 0／0.3／0.6／0.9の4階調とした。GPUテストは背景画素を濃度から推定せず、静止接続形状で選別して透明な水際も0階調として数える。関連EditMode 37件が1.32秒で全件成功（Skipなし）した。全EditModeの再実行は進行を確認できず、Editor設定参照のメインスレッド待機が5秒でタイムアウトしたため、今回開始した全体実行を`cancel_tests`で中断してから関連37件を実行した。全体結果と全PlayModeの再確認は未完了であり、上記の前回全体結果を今回の成功結果として扱わない。強制終了・再起動や接続パッケージの更新は行っていない。

濃淡調整後のSampleSceneはフレーム進行と初期位置の適用完了を確認し、階段状岸を表示時間0秒・1.5秒で比較した。強い接触線と透明な区間が入れ替わり、岸の接続形状・水深の描画が保たれることを確認した。撮影時刻は実行時Materialだけへ指定し、元Assetの時刻は0を維持する。見た目の採否は人間確認を残す。

同日のタイミング調整では、全岸共通の主・副2周期と、その全岸一斉往復だけを保証する旧描画テストを削除した。絶対画素座標の固定中心ごとに整数ハッシュで位相・周期を選び、基準3秒±25%の独立した強弱を周囲4中心の固定形状へ掛ける。関連EditMode 39件が1.51秒で全件成功（Skipなし）した。同じコマで強まる・弱まる画素が混在すること、旧6秒周期で全岸が同じ状態へ戻らないこと、4095／4096px境界で濃度が飛ばないことを追加検証し、4階調・コマ保持・原点変更・全31形状と深度の回帰も維持した。SampleSceneのストリーミング・再生成・共有時計・Material共有を通すPlayMode 1件は47.47秒で成功した。全体回帰は直前の停滞による未完了状態を引き継ぎ、今回も全件成功とは扱わない。Development BuildのCPU/GPU性能と見た目の採否は未確認として残す。

タイミング調整後も実Sceneのフレーム進行を確認し、階段状岸を表示時間0秒・0.75秒で比較した。部分ごとに明暗が異なって変化することを確認し、検証後はPlayMode・一時停止を解除、背景実行false、基準480×270、Scene未変更へ戻した。元Materialの時刻と原点は0で、最終Consoleの実状態はコンパイル失敗なし・エラー0・警告0だった。

同日の裏側終端修正では、接触形状を7bitへ拡張し、奥へ同種液体が続く一方で斜め奥に岸がない側だけ上端3pxを隠す。関連EditMode 48件が1.71秒で全件成功した。液体3種の左右独立判定、左右の斜めHaloをまたぐ終端と継続、終端側13pxと反対側16px、上位bitを保持する共有Tileを検証し、GPU形状回帰も全127非空マスクへ拡張した。既存の内角、深度、明暗、Floating Originの回帰を維持する。

この修正の全EditModeは755件中753件成功・2件失敗（432.99秒、Skipなし）だった。1件は上記の既知のAssembly参照不一致である。もう1件は、水文テスト中にAIが送ったEditor状態取得が30秒でタイムアウトし、そのPipelineエラーログを`GeneratedFlow_UsesOnlyOceanOrStrictlyDescendingD8Edges`が検出したものである。状態取得を挟まず同テストだけを再実行すると1件成功（11.35秒）した。全体実行そのものを全件成功とは扱わない。全PlayModeは56件中48件成功・8件失敗（141.21秒、Skipなし）で、失敗は上記と同じ`ChunkLocalTilemapPresentationTests`の既存不整合だった。該当するテストとAssembly定義がHEADと同一であることも確認した。

検証後はSampleScene停止中・Scene未変更、水際Shaderエラーなし、Console実状態のエラー0・警告0・コンパイル失敗なしを確認した。今回の実装では水際Materialの調整値を書き換える操作は行っていない。検証終了時の保存済みAssetには、毎秒2コマ・最低濃度0.03・最大濃度0.5・模様長さ12px・基準周期12秒・周期ばらつき25%の調整値が入っており、Shader初期値へ戻していない。形状・明暗の自動テストは独立した一時Materialを使用する。地面の裏側へ回り込む角で線の上端だけ3px隠れることと、連続する縦岸・内角が途切れないことの最終Gameビュー確認、およびDevelopment Buildでの確認は人間確認として残す。

大きな岸段差の追加修正では、深度TileのRGBを段開始Alphaではなく0〜6深度段（0〜255へ広げて符号化）へ置換した。Shaderは各列の現在画素の深度から勾配・飽和を評価した後に横補間し、同種液体かつ同じ床有無だけを混ぜる。小さい段数をbyteへそのまま入れる初期実装はLinear色変換で0へ丸められることをGPUで検出し、全範囲へ広げる符号化で解消した。旧開始Alpha計算APIとその契約だけを保証する期待値は残さない。

水中Faceの頂点Alphaは1〜6/255で0〜5段を表す。乾燥崖の48px減衰とは分離し、岸上端から80pxまでは不透明、80〜96pxだけを連続的に消す。隣の浅い列でOverlayが薄くなっても断面の描画終了線が出ないようにする。関連EditMode 60件は0.69秒で全件成功（Skipなし）した。CPUでは1・5・9タイルの段差と床有無、GPUでは5・9タイルの段差と床有無を走査し、濃度の縦方向の逆戻りがないことを検査した。実際に断面とOverlayを重ねた終端の読戻しも成功した。元の水際Materialの調整値は変更していない。

同修正の全EditModeは767件中765件成功・2件失敗（310.91秒、Skipなし）だった。1件は既知のAssembly参照不一致、もう1件は`ProductResolver_SharesTopAndSelectsPurposeSpecificBoundary`が旧Alpha 0を期待していたものである。後者の期待値を段数付きAlphaへ更新し、0〜5段のTintと6段以降の拒否を確認するようにした後、`TerrainMaterialProductPresentationTests`7件が0.04秒で全件成功した。全体実行と対象再確認の結果は分けて記録し、全体を全件成功とは扱わない。これらはEditor上のAI確認であり、報告画像と同じ地形での最終Gameビュー確認、Development Buildと性能計測は残す。

同修正の全PlayModeは56件中48件成功・8件失敗（148.99秒、Skipなし）だった。SampleSceneのストリーミング、決定論的再生成、共有Material、原点変更の連携は成功し、失敗は上記と同じ`ChunkLocalTilemapPresentationTests`の旧コンストラクター呼出し3件とカタログ未準備5件である。対象テストと既知のAssembly参照不一致の定義・テストはHEADと同一であり、今回の修正へ混入させない。検証後はSampleScene停止中・Scene未変更、深度OverlayとFaceのShaderエラーなし、Console実状態のエラー0・警告0・コンパイル失敗なしを確認した。水際Materialの保存済みファイルは修正開始時と同じである。

2026-10-27の再報告地点(-12.5,-30)では、左右が全行とも水である旧合成テストでは実際の陸地→水の切替を再現できていなかった。深度RGBは中央0〜6段・左右0〜6段または陸地7を値×255/7へ符号化し、陸地は行によらない濃度0へ固定した。水中Faceは頂点R/Bへ左右の断面状態（0=なし、0.5=終端、1=不透明）を渡し、断面なし・終端との境界を半セル8px幅で接続する。反射は3px低い実水面へ位置を合わせ、2段を圧縮した24px全体で連続減衰させる。反射の頂点Alphaは1/255・2/255の段数で、見た目の濃度ではない。実地形分類を通す5・9タイル岸段差、負座標、断面＋Overlayの両列合成、反射の開始位置・段間連続・終端をGPU読戻しで追加検証した。関連EditMode73件は4.32秒で全件成功、Skipなしだった。同地点のGameビューでも中央の縦境界の1画素差が緩和することを確認した。水際Materialのユーザー調整値は保存ファイルを変更していない。最終見た目の人間確認とDevelopment Buildでの確認は残す。

全体EditModeの再実行は、開始後18分以上経過しても完了結果を取得できず、未完了とする。UnityプロセスのCPU処理と管理コード実行は観測したが、進行中テスト名は取得できなかった。Test Runner正式APIによる通常キャンセル要求もメインスレッド待ちで未応答であり、Editorの強制終了・再起動は行っていない。全体の成功件数や既知失敗以外の回帰有無は判定しない。上記73件の対象実行と同地点の実Play確認は、この全体実行前に完了した独立した結果である。全PlayModeの再実行も今回は未実施である。

#### `TerrainWaterRenderDataTests`

今回のAI確認は、Unity 6000.3.8f1で液体分類・GPU描画90件（1.97秒）と本テスト7件（3.88秒）のEditMode計97件、SampleSceneのチャンク別・共有方式の移動／決定論的再生成PlayMode各1件（47.03秒・46.76秒）が全件成功し、Skipなしだった。フレーム進行を確認した同じ報告地点のGameビューでは、1列だけ断面が抜ける濃い溝がなくなったことをAI確認した。最終コンパイル失敗なし、現在のConsoleはエラー0・警告0。ユーザー調整のMaterial値は保持し、水際Material、Scene、生成設定に保存差分を残していない。確認用Playは停止し、背景実行設定はfalseへ復元した。確認用画像は製品Assetから削除し、比較用コピーだけをプロジェクト外の一時領域へ残した。差分レビューはブロッカーなし。全ドキュメントへの影響を確認し、現行のアート・ゲーム／マップ・多層水域・技術・Editor確認手順・テスト・ロードマップを更新した。索引は文書構成が変わらず既存要約とも矛盾しないため更新不要とし、素材制作Prompt・生態・生成／保存・過去の実装記録・Archiveは契約不変のため更新していない。全体テスト、Development Build・Profiler計測は未実施。2026-10-28にユーザーが溝の解消を確認し、採用とcommitを承認した。水面模様・反射Materialのユーザー調整差分は今回の修正commitへ含めず、作業ツリーへ保持する。

2026-10-28の海岸の溝報告に対し、Seed `0x3EE49B3100677454`・GeneratorVersion 2のChunk `(-5,3)`を製品Generatorで再生成する回帰を追加した。Surface `(-290,204)`から同じ列のGround Y=207まで、海水・海水・淡水が続き、断面が段2・1・0で投影されることを保証する。混在水域が完成済み北隣チャンクへ続く場合と、隣チャンクが未取得の場合も検証する。生成地形や水種を変える修正ではないため、GeneratorVersionは変更しない。

現在は、岸Y差1・5・9でも断面が各列自身の奥岸の真下だけへ投影され、元のSource X/Yと段数を保つこと、横・手前の陸地だけでは水中断面が生成されないことを純粋計算で検証する。岸からの画面上の投影距離は物理的な水深ではなく、別の岸と平均しない。「共有頂点の一致」「隣列の断面を描画範囲へ延長する」を成功条件とした旧テストは廃止した。

2026-10-27の赤枠再報告を受け、(-12.5,-30)で水中断面・水色を個別に表示したところ、どちらにも同じ縦帯があることを確認した。前回の「解消した」というAI判断は取り消す。一定の水色を先に描き、実際の岸からの距離で断面の可視Alphaを1−水の被覆率へ減衰させる方式へ置換した。GPU回帰は、水色だけなら全7段・床有無で完全に均一であること、実際の3pxクリップだけが残ること、断面合成後の45px／50%・93px／100%減衰、地面上端3px、岸Y差1・5・9と負座標で余計な断面が出ないことを別々に検証する。水色と断面の最終色を無条件に左右一致させるテストは使わない。実際の断面の端は正しい形状として残し、2026-10-27にユーザーが同地点の表示を確認して採用した。

この方式の最終対象EditModeは、水色・断面・水際・反射59件（2.44秒）、純粋投影4件（0.70秒）、材質解決7件（0.62秒）、Scene接続4件（0.63秒）、Scene構築10件（0.71秒）、製品分離2件（1.49秒）の計86件が成功し、Skipはなかった。4倍描画でも素材1pxの内部で濃度が変わらない回帰を含む。最終PlayModeはSampleSceneのストリーミング・決定論的再生成をチャンク別方式で47.27秒、共有方式で47.85秒、それぞれ1件成功した。同地点の修正後Gameビューでは水色単独の縦帯と左側への断面延長がなくなった一方、右側の実際の断面の端は直線として残ることを確認した。この直線を含む最終の見た目はユーザー確認済みで、2026-10-27にcommitを承認された。水際Materialの調整値は保持し、確認画像は製品Assetへ残さない。全体テストの再完走、下層自然生成接続後の実床プレビュー、Development Build・Profilerでの性能確認は未実施である。

以下の共通四隅深度場・追加断面・計測値は、廃止した方式の試行履歴である。成功件数は当時のテストの結果であり、赤枠の問題解消や現在方式の合格を示すものではない。

2026-10-27の責務分離後、共通深度・水際・反射56件、純粋な水中描画分類5件、材質解決7件、Scene接続4件のEditMode計72件が成功した。報告地点(-12.5,-30)のGameビューでは、手前の縦岸に出ていた明るい帯と中央の縦境界を同時に確認した。その後、距離0でも頂点Alphaを0にしない128のデータ印、水面の上端3pxへ地面断面が残る回帰、同じTileの色だけの更新を通常フレーム後に確認する回帰を追加した。後者はSetTilesだけでは保存値と描画結果が食い違うことを検出したため、製品とテストが同じApplyTilesを使い、Dirty範囲の非空セルだけRefreshTileしてから色付き一括書込する方式へ修正した。逆順では共有断面TileのLockColorがデータ色を白へ戻すため、GPUの断面上端3pxテストで書込順も保証する。最終対象EditModeは水際・共通深度・反射57件（2.41秒）、純粋分類5件（0.70秒）、材質解決7件（0.62秒）、Scene接続4件（0.64秒）、Scene構築10件（0.70秒）、製品分離2件（1.47秒）の計85件が全件成功、Skipなしだった。途中の再実行1回はPlay中のPreview Scene作成が拒否されて開始処理に失敗したため結果へ含めず、ユーザーの停止後に再実行した。

色付き書込の追加再描画は、同Editor／Monoの隔離Preview Scene、RendererなしTilemap、256セルの再利用配列2個を交互に100回反映（25,600セル、32回ウォームアップ）して比較した。SetTilesだけは7.637ms、DirtyセルRefresh＋SetTilesは39.536msで、256セル1レイヤー当たり約0.076→0.395ms、managed allocationはいずれも0 byteだった。全TilemapのRefreshAllTilesや毎フレーム更新は使わず、地形がDirtyの時だけ適用する。これはAPI呼出し時間の比較であり、Sprite・Renderer・GPUを含む実製品負荷、ProfilerやDevelopment Buildの性能合格を示すものではない。

最終のSampleScene PlayModeは、チャンク別Tilemap方式のストリーミング・決定論的再生成1件が47.72秒、共有方式の同回帰1件が47.40秒で成功した。同地点を最新の符号化・反映処理で再撮影し、手前縦岸の深水を薄くする帯がなく、中央の水色が連続することをAI確認した。実Playのフレーム進行も確認し、検証用Playを停止、背景実行設定を元のfalseへ戻した。水際Materialの調整値は変更せず、C#コンパイル失敗・関連Shaderエラーはない。見た目の最終採否、全体テストの再完走、Development Build・実製品負荷の計測は未確認として残す。

共通深度算出の単独計測はUnity 6000.3.8f1 Editor／Mono、Burstなし、中央64×64の完成地形、直線岸の0〜5段、各条件256回ウォームアップ後に行った。1,000セルは1.603ms、10,000セルは16.091ms、100,000セルは160.932msで、計測ループのmanaged allocationはいずれも0 byteだった。16×16セル換算で約0.412msだが、Tilemapへの反映、断面Source選択、描画、GPU時間を含まない。旧方式との同条件比較やDevelopment Buildでの性能合格を示す値ではない。四隅データの組合せごとにTileを生成せず、白い深度Tile1件とPresenter寿命の固定長再利用配列を使う。水中断面用Tilemapは表示チャンクごとに1レイヤー追加し、セルごとのGameObjectは作らない。

作業開始時点ではEditorは停止中・Scene未変更・Test Runner非実行で、以前のCLI実行中記録だけが残っていた。正式なcancelコマンドで古い記録を消し、Editorを再起動せず対象検証へ進んだ。以前の全体EditMode未完了を成功へ読み替えず、今回は対象回帰を優先する。

#### `TerrainLiquidProductPublisherTests`

製品カタログがVersion 2の3液体のSurface・水際色・深度色を初期化できること、再帰依存に旧制作SourceやFallbackが含まれないことを確認する。共有水際Materialが専用Shaderを使い、Shaderエラーがなく、液体製品フォルダーに旧AnimatedTileが残らないことを保証する。製品Surfaceの淡水・海水Alpha 0とマグマAlpha 255は維持する。旧方向別入力生成テストは生成実装・入力・Atlasとともに削除した。

#### 比較・検品専用テストの削除

2026-10-27の水面表現第1段階では、関連EditMode（水際・深度34件、製品接続・分離6件、Scene構築10件）計50件と、SampleSceneのストリーミング・決定論的再生成PlayMode 1件（47.41秒）が成功した。GPUの全31接触形状と原点変更に加え、実画面の細い水路と階段状岸を別時刻で比較し、輪郭を固定した明暗変化と、5 Rendererでの実行時Material 1件共有を確認した。元のMaterial Assetの原点値は変更されていない。CLIの`unity status`が古い接続パッケージへ`editor status`を送って発生する互換性エラーと、ゲームの例外・コンパイル失敗は分けて確認する。見た目の採否とDevelopment Buildでの性能計測は未実施であり、人間確認を残す。

地形タイル、Wall暗色化、Wall影、折れ目影、旧共通境界、水面反射の比較・検品専用テストは、採用方式を製品Sceneへ接続したC6-D6で対応するScene、Builder、Development Shader、生成Materialとともに削除した。現行回帰は`TerrainMaterialProductPublisherTests`、`TerrainLiquidProductPublisherTests`、`TerrainProductSceneConnectionTests`、`TerrainMaterialProductPresentationTests`、`TerrainWallShadowUtilityTests`、`TerrainLiquidPresentationUtilityTests`、`TerrainProductIsolationTests`で保証する。

2026-10-27のC6-D6材質接続AI確認では、Unity 6000.3.8f1 Editorの再コンパイルがエラー0件で完了し、製品公開、境界合成、Import、材質表示、Wall影、Atlas、Scene接続、Bootstrap、製品分離の関連EditMode 61件がすべて成功した。SampleSceneの製品材質Tile、資源の前後関係、ストリーミング、決定論的再生成を通すPlayMode 1件も46.67秒で成功し、最終Consoleはエラー0件・警告0件だった。PlayMode確認中に検出した`TerrainWall`のGlobal Light対象漏れ、通常資源がWall背後へ回るSorting Layer不整合、非アクティブHierarchy内での`ShadowCaster2D`初期化NullReference、旧一体型岩壁Tileを期待する回帰条件は同じ変更内で修正した。

続く液体接続では初期入力3枚、透明水Surface、不透明マグマSurface、全21水際Tile、液体製品カタログ、SampleSceneの専用5レイヤーを追加した。接続確認で階段状の岸に16 px単位の矩形帯が見つかったため、左右セルの段開始Alphaをセル中央間で補間する回帰を追加した。さらに深部で横帯が反復する再報告を受け、6段超を最終勾配の段5へ丸めていたことを原因と特定し、段6の飽和値へ固定する回帰を追加した。3pxクリップは奥側がGroundまたはWallの先頭水セルだけに限定し、斜め岸では列ごとの先頭セルが階段状に並ぶ一方、連続水域内部はクリップしないことを既存回帰で維持する。再コンパイルはエラー0件で、液体公開・分類・水際20件、Scene接続4件、製品分離2件、Scene再構築10件の関連EditMode計36件がすべて成功した。SampleSceneのストリーミング・決定論的再生成を通すPlayMode 1件は横補間修正後に50.84秒で成功し、段6飽和修正後の再実行でも成功した。初回PlayModeでは旧「Surface＋全面アニメーション」待機条件が新仕様と不一致で失敗したため、「Surface＋床有無いずれかの深度Overlay」と旧全面アニメーション不在を確認する条件へ置換し、再実行で成功した。段6飽和修正後のGameビューでは、岸から96 pxを超える広い水面が均一な最大濃度を維持し、16 px周期の横帯を繰り返さないことをAI確認した。続く報告画像から、0〜96 pxが深いほど明るいシアンへ近づく問題へは、まず製品カタログの収束色を調整した。断面差分の固定・奇数段上下反転による修正は効果がなく表示規則も変えるため撤回し、通常の段別差分選択へ戻した。淡水を`#184860`、海水を`#102A48`へ収束させる回帰を追加し、深いほど暗く見える方向へ修正した。液体公開4件、液体表示14件、材質表示7件、Scene接続4件の関連EditMode計29件と、SampleSceneのストリーミング・決定論的再生成を通すPlayMode 1件が成功し、Consoleは警告0件・エラー0件だった。その後に残った横帯は、深度データを詰めた頂点RGBのLinear色変換と、水中断面へ混入した乾燥崖の段内減衰をGPU描画で特定した。頂点RGBを元データ値へ復元し、水中断面へは乾燥減衰を掛けず、Wall下のGroundも投影するよう修正した。GPU描画4件を含む関連EditMode 35件と、SampleScene PlayMode 1件（55.115秒）が成功し、C#再コンパイルは警告0件・エラー0件だった。実地形の階段状の岸でも、Wall下Groundの水中断面が欠けず、0〜96pxの濃度がタイル境界で戻らないことをGame用Camera画像で確認した。最終Consoleは警告0件・エラー0件だった。報告画像と同じ位置での最終見た目は人間確認へ残す。

#### `WorldGenerationSceneBootstrapTests`

`Build Fallback Map Prototype`の対話メニューについて、保存確認をキャンセルした場合は構築を開始しないこと、構築成功後は退避したScene構成を復元すること、構築中に例外が発生しても復元後に例外を呼び出し元へ返すことを保証する。加えて、設定用GameObjectへ`DefinitionCatalogAuthoring`と鶏の共通定義・生成条件を設定する内部処理、および専用Inspectorが動物種GUIDから`0: Chicken`の見出しを解決し、参照切れでは短縮GUID付きの未解決表示を返すことをEditModeで検証する。Scene復元テストでは実際のSceneやアセットを変更せず、保存確認、構築、復元をDelegateで差し替えて制御フローだけを確認する。

### 3.3 PlayMode: `Farmware.WorldGeneration.PlayModeTests`

#### `ChunkLocalTilemapPresentationTests`

`PresentationPreload_`は通常・負座標・遠方の可視優先予約、外側1チャンクの準備、Inactive完成データの再利用、画面へ入った表示の維持、範囲外消去、生成中の非公開、明示移動での可視限定と予算4を確認する。旧共有方式と切替設定は削除し、製品チャンク別方式だけを検証する。SampleScene縦切りは設定SubScene・鶏・地形・液体・資源・診断表示も確認する。

有効地形近傍の回帰テストは、Presenterが旧260要素Haloフィールドを持たず、周囲8チャンクの完成自然地形と疎な差分を直接参照することを確認する。停止・再開、Presenter破棄、World切替では表示Fingerprintと借用参照だけを破棄し、表示専用の永続外周配列を所有しない。

`SparseWrite_`は、実際のPresenter書込経路で空→空だけを省くこと、末尾1セルを含む非空配列を書き込むこと、非空→空の実消去、レイヤーとサブチャンクごとの履歴分離、部分消去とプール再貸出、正負原点変更後の履歴維持を保証する。空判定時間も性能比較へ含め、見た目・GPUの合否はこれらのテストだけで決定しない。

製品チャンク別表示について、正方向・負方向と斜め・遠方の自動原点変更で同じTilemap・セル内容・完成済み集合・部分反映番号を保持することを検証する。消去途中ではプールへ返さず、全16サブチャンク消去後だけ空の表示単位を別チャンクへ再利用すること、プール上限0、設定変更、World切替で表示とプールを残さないことも保証する。上下チャンクの相対Z順は現行Orthographic描画の継ぎ目対策であり、XY位置とSorting Orderを変えないことを確認する。実際の描画ピクセル、GPU、メモリ帰属は別途Gameビューと性能計測で確認する。

2026-10-17に比較方式の7ケースを含むPlayMode全38件と、関連ワールド生成EditMode 163件がUnity 6000.3.8f1で成功した。原点変更前後と拡大・負座標・遠方の44画像確認、前面EditorのCPU比較、未検証事項は[性能計測レポート](./Map02DPerformanceReport.md)を参照する。


`WorldGenerationSceneBootstrapTests.ValidateProductSceneReferences_AcceptsPublishedProductScene`は、必要な場合だけ製品Sceneを追加読込し、構築ツール自身の読み取り専用検査を実行する。ユーザーやTest Runnerの未保存Sceneを閉じず、追加読込したSceneだけを終了時に閉じる。水中断面と直下床の採用済み`WaterSurface`を旧`Default`条件で拒否する回帰も防ぐ。

2026-10-28の製品切替整理では、製品カタログ未設定時に1回だけエラーを通知して描画を拒否すること、旧素材・Atlas・API・保存済み参照が残らないことを`TerrainProductIsolationTests`へ追加した。`WorldGenerationScenePlayModeTests.CalculateChunkBounds_ProductProjectionIncludesFullOverhang`は、正負のチャンク境界でWall 2段とGround崖面3段の張り出し全体が可視要求へ含まれることを検証する。旧表示の寸法・Biome代用・共有方式だけを保証したケースは削除した。

#### `WorldGenerationScenePlayModeTests`

`SampleScene_StreamsAndDeterministicallyRegeneratesTerrainTiles`は、保存された既定ONを確認し、見本Tilemapではなく実際のチャンク内Tilemapのセルを読む。初期表示、岩壁と下層土地、資源、正負の遠方、アンロード、帰還時の決定論、液面、F3/F4・開発者可視化の既存契約を維持する。さらに、実際のDefault Worldで非0の`AnimalPopulationSeed`、地域状態、共通永続ID・種類・整数位置・群れID・外見差分を持つ野生動物Entityが作られ、`Animal Population`可視化が候補・確定位置Queryを例外なく描画できることを確認する。C6-B1以降は同じSubScene Bakerが`TerrainBlockMaterialCatalog`を1件公開し、代表`GrassLoam`の共通Item・Ground・Wall実行時IDがすべて有効であることも確認する。C6-B2以降は`TerrainBlockPaletteCatalog`も1件公開し、温帯草原の先頭候補が重み160の`GrassLoam` Ground、火山岩地質の自然Wall先頭候補が重み128の`Basalt` Wallであり、後者が移動遮断・水密属性を持つことを確認する。実際の生成設定と個体座標を照合して資源予約との重複がないこと、候補表示が木のセルを除外し資源なしの草原陸地を受理することも確認する。`SampleScene_SharedModeStreamsAndRegeneratesTerrainTiles`も同じ検証経路をOFFで実行し、旧共有方式の回帰を残す。テスト内の反射は表示参照と候補判定の読取検証に限り、未割当表示をテストから生成しない。

| 分類 | 用途 |
| --- | --- |
| Windowbox | 480×270の整数倍表示と黒帯内の描画矩形を保証する。 |
| F3 | 現在座標、サブチャンク座標、ECSのBiome・GeneratorVersion・ワールドシード、記録済み初期スポーン座標を保証する。初期スポーン探索失敗時は探索中表示を残さず、Scene保存位置を使用中であることを示すHUD文字列も検証する。 |
| F4 | 初期スポーンの赤いTile・Chunk表示と、縦横グリッド線の矩形計算を保証する。32bit絶対タイル座標域の正負端では、最大タイルの外周を含む境界線数を64bitで有限に求め、整数周回による終了しない描画ループを起こさないことも確認する。 |
| Floating Origin | 初期スポーンと明示的な絶対座標移動では所属する64タイル境界を表示原点に選び、負座標では床方向の境界を選ぶことを保証する。Cameraの相対Viewportへ表示原点を加えたObserverが正しい絶対可視チャンク範囲を要求し、SampleSceneの遠方移動では新しい相対Tilemapセルへ地形が表示され、旧原点の資源セルが残らないことも確認する。正負の遠方表示原点では開発者用可視化の絶対Viewport範囲、中央絶対タイル、Tilemapセル、色面が一致する。通常移動では512タイル未満で原点を維持し、閾値到達時は正負とも所属チャンク境界へ自動更新して絶対Camera位置とObserver範囲を維持する。旧原点セルの分割消去が、新原点側の同じ論理チャンクを消さないことも単独テストで保証する。MAP-02Eでは複数回の自動原点変更、東西往復、0境界横断、保持範囲外からの再訪を一つの通常PlayModeテストで継続し、同一座標Entityの非重複、再生成した地形・資源の代表セル一致、生成設定・初期スポーンの不変性を確認する。同じ移動区間で`ChunkStreaming`、`TilemapPresentation`、`FloatingOriginRebase`、`ChunkGenerationBoundaryWait`と基礎水文・湖・地形の完成Handle確定時間を記録し、生成境界の費用を表示処理だけで見落とさない。最大ズームアウトと高速パン時の無停止性は自動テストだけで合否を決めず、ProfilerとGameビューで追加確認する。 |
| 初期スポーン失敗 | Scene保存位置を維持し、失敗状態の確定後にチャンクストリーミングを開始できることを保証する。 |
| Presentation寿命 | Camera GameObjectを維持したDefault World再作成時に新しい初期スポーンを再適用する。Camera Bridgeは新Worldの初期スポーン探索中でも旧WorldのObserverを直ちに破棄し、初期位置の確定前は新Observerを作らず、確定後に1件だけ作り直す。同一Worldでカメラ制御を再有効化した場合はプレイヤーの移動後位置を保つ。正負座標のGrid 64タイル境界へViewport右上端が一致しても、上限側の面積を持たない隣接チャンクを要求しない。Tilemap PresenterがWorld切替または無効化時に表示済み・反映途中・消去途中の旧表示を残さないことも保証する。 |
| 開発者用可視化 | バイオーム、Soil、水域、岩壁、マグマ、資源、TERRAIN-01C1の8連続環境表示、共通配置APIの判定結果を使う地形配置マトリクスの色写像がSimulationを変更しないことを保証する。正負の遠方表示原点でも、相対Viewportから得た絶対範囲と中央タイルを検索し、絶対タイルを正しい相対セルへ投影することを確認する。旧`Weirdness`と`Biome Transition`の色判定は削除済みであり、新しい回帰へ含めない。 |
| 旧一体型岩壁フォールバック | 製品カタログ未接続の開発Sceneでだけ、チャンク境界をまたぐ内部影が連続し、一体型岩壁の上面・内部影・露出資源が同じ12px高へ揃うことを保証する。 |
| SampleScene縦切り | SubSceneロード、Authoringと共通既定閾値、初期カメラ、通常12タイル/秒・Shift併用36タイル/秒のカメラ操作設定、512タイルの自動Floating Origin設定、チャンク生成、World Grid直下へScene保存した境界2層を含む9枚のテンプレートTilemap（実行時チャンク配下の複製は枚数から除外）、Ground・Rock WallのIndividual ModeおよびTop Left順、土地境界Order 1・岩壁境界Order 4と表示高、14 Sprite参照、境界の既定不透明度20%と実行時Tileへの反映、同じBiomeなら標準・肥沃・砂質のいずれも同じプレイヤー向けTileを選ぶこと、SampleSceneの7バイオーム参照が本番用`terrain_biomes_base.png`の同名Spriteへ対応すること、岩壁参照と全表示セルが本番用`terrain_rock_wall_01.png`の専用Tileへ対応すること、Editor専用Soil可視化が3種類を別色にすること、通常資源の岩壁背面用Order 2・前面用Order 10への振り分け、静止液面・アニメーションの-8pxオフセット、液体セルのGround Tilemapとの排他表示、岩壁セルの下層土地、岩壁上資源の12pxオフセット、土地・岩壁Spriteの上下はみ出しを含む可視チャンク範囲、液面4fps設定、アンロード、同一地形の再生成を保証する。EditModeでは、境界選択と資源背面分類の純粋計算を保証する。 |

`AutomaticRebase_PreviousPixelPerfectCameraMatrixDoesNotShiftObserver`は、URP Pixel Perfect Cameraが前回描画時の`worldToCameraMatrix`を保持する条件を明示代入で再現する。Camera Transformと表示原点を更新した後、Bridgeが現在のTransformに基づく行列へ戻してからViewportを変換することを検証する。実際にちらついた絶対X=448・960（チャンク7・15への進入）と負方向の3条件で、可視要求が8チャンク先へ飛ばず正しい範囲を示すことを保証する。描画イベントの発火時刻やGameビューの解像度へ依存せず、前フレームの行列が残っていることも前提条件として検査する。

長距離ストリーミングのPlayMode待機ループは、BatchModeで描画フレームだけが実プレイより速く進む場合に限り、各未完了フレームで10msだけWorkerへ譲る。これは60fpsの1フレーム約16.7msより短く、C4-D2の周囲チャンク再構築中に高速空フレームだけでWorkerを飢餓させないためのテスト専用値である。300フレーム上限は維持し、製品SystemのTickや生成予算は変更しない。

`SampleScene_RepeatedAutomaticRebasesPreserveStreamingAcrossRoundTripAndNegativeCrossing`は、採用済みのチャンク別Tilemap方式で絶対X=32.5、544.5、1056.5、479.5、-96.5、416.5、1056.5、-96.5、32.5の9地点を順に確認する。移動は明示テレポートではなく通常移動と同じ相対座標APIを使い、表示原点6種類、自動変更、東西往復、0境界横断、3チャンク再訪を一つのScene寿命で起こす。各地点のCamera、Observer、Activeチャンク、Tilemap表示、座標重複を検証し、再訪時には旧Entityのアンロードと地形・資源の代表3セルの決定論的一致を確認する。全4096セルの生成一致は既存EditModeテストへ任せ、通常回帰の実行時間を抑える。

SampleScene縦切りは、初期中心チャンクの表示完了時に左隣チャンクも`GeneratedTerrainCell`を含む6 Buffer公開済みのReadyToApply、Active、Inactiveのいずれかであることを確認する。C4-D2の生成中に左下・左上角を先読みし、通常の左境界越えで未生成空白が見える回帰をこの実Scene条件で防ぐ。

#### `TerrainMagmaShoreLightingTests`

製品の岸照明を通常EditModeで検証する13件。マグマ内部と未完成近傍で架空の岸を作らないこと、固定区画ごとの1灯配置とAdditive Blend Style、強度0.12、再利用、チャンク削除・Dirty更新による消灯、Floating Origin即時再配置、負座標と正負10億タイル付近、完成済み隣チャンクの岸判定、Wall前面・上面で隠れるマグマの除外と表示上の岸の抽出、チャンク境界を越えるWall投影、カメラ移動前後で共通に見える範囲の光源集合と強度の不変、岸の収集順を変えても固定区画の同じ光源を選ぶことを保証する。テスト所有の完成地形配列から収集し、Tilemapや製品Sceneへ書き込まない。実際のPlay中のComponent破棄、見た目、GPU負荷は別途確認する。

`TerrainProductSceneConnectionTests`へHDR・Post Processing・Volume Layer Mask、製品Bloom ProfileのIntensity 0.4・Threshold 1.15・Scatter 0.25・最大3反復、岸照明参照と再接続時の重複防止を追加する。`TerrainLiquidRenderingTests`は製品Materialの網目が1より大きいHDR輝度をGPU出力へ保持し、内部に暗部が残ることも検証する。Test AssemblyはURP Core・Universal・2D Runtimeを参照する。採用前の一時比較UI・復元専用テストは製品経路へ置き換えて削除する。

2026-10-28、Unity 6000.3.8f1／URP 17.3.0で岸照明7件（0.04秒）、製品Scene接続6件（0.04秒）、GPU描画41件（1.56秒）がすべて成功した。製品SampleSceneの`SampleScene_StreamsAndDeterministicallyRegeneratesTerrainTiles`も47.01秒で成功した。初回の照明テストで見つかった、PlayのAwakeを経ないEditModeでOnDestroy通知を期待する検査は、候補消去とプール再利用の検査へ整理した。URP Blend Style API名を修正した後にコンパイル要求が進まなくなり、Editorの再起動でUnityがLibrary破損を検出したため、UnityのRebuild Libraryを実行して復旧した。最新版Assemblyへの追加7件の読み込みを確認して上記テストを再実行し、最終テスト後のConsoleはエラー0・警告0だった。Library復旧は生成キャッシュの再構築であり、パッケージの追加・更新は行っていない。Development PlayerのCPU／GPU負荷・製品最大Viewport・動的遮蔽は未検証である。

同日の製品Play確認では、初期配置完了後に絶対タイル`(144, -476)`へCameraだけを移動し、合成済みGameビューで黄色い網目・Bloom・岸の橙色の光をAI確認した。全体照明は強度1のまま、岸の有効光源は12灯だった。`(16, 36)`へ離れると0灯、元へ戻ったロード完了後は同じInstance IDの12灯を再利用した。フレーム進行を複数時点で確認し、Play停止後は所有Light GameObjectが0、Scene未変更、検証用の背景実行も元のfalseへ戻った。これは静止画像と限られた往復の確認であり、ユーザーによる製品接続後の見た目確認・全岸の均等性・影・負荷測定の代替ではない。

続くユーザー報告では、絶対Camera位置`(177.813, -488.25)`でWall中央に点状の光が出る問題を再現した。光源の論理座標はマグマ上だったが、手前のWallが+2タイルの上面・前面として重なり、光源の液面を隠していた。隠れたマグマを候補から除き、可視の液面と投影Wallとの接触も岸に含める修正を行った。追加4ケースを含む照明EditMode 11件が0.04秒で成功し、同じ地点のGameビューで離れた光だまりが岸へ揃うことを確認した。有効16灯の中心を実際のWall Top／Wall Face Tilemapと照合し、壁に覆われる中心は0件だった。Play停止後は光源・一時マーカー残留0、Scene未変更、一時的な背景実行を復元した。今回の修正は照明の候補分類だけであり、前回のGPU41件とPlayMode1件は再実行していない。製品でのユーザー再確認と実機負荷の計測を残す。

さらに照り返しの強さとカメラ移動での位置変化の報告を受け、強度を0.35から0.12へ下げ、Camera距離で16灯へ絞る方式と最小間隔の再選択を廃止した。絶対座標の固定3×3タイル区画から1灯を決め、画面へ光が届く固定光源をすべて表示する。カメラ移動・ズームは更新間隔を待たず同じフレームに反映し、URP Light2DのLateUpdateより前に位置を更新する。追加2件を含む照明EditMode 13件が0.05秒で成功した。実際のPlayでもCameraを`(177.813, -488.25)`から右へ3タイル動かし、両方から見える共通矩形へ光が届く24灯の絶対座標・強度がすべて一致した。全有効灯数は25灯から30灯となったが、これは表示端に新たな固定光源が入ったためである。Bloom 0.4と全体照明強度1を維持し、合成済みGameビューで弱めた照り返しを確認した。停止後は光源残留0、Scene未変更、背景実行を元のfalseへ復元した。旧16灯上限がないため、固定区画密度とViewportに応じて表示灯数・プールの最大保持数が変わる。Development PlayerでのCPU／GPU時間・製品最大Viewportの負荷は未計測であり、この確認を性能合格とはしない。

2026-10-28、最終の岸照明修正後にユーザーが見た目を確認し、「いい感じです」と採用を承認した。今回のマグマ表現の人間確認は完了し、commitの許可も得た。上記の途中段階で残していた製品接続後の見た目再確認は、この確認で完了した。Development PlayerでのCPU／GPU時間・製品最大Viewport・動的遮蔽の未検証範囲は引き続き残す。

## 4. 変更内容ごとの実行範囲

第3章は現存するテストの説明である。以下は2026-10-19のゲーム仕様改訂に対応する実装時の検証方針で、未実装機能のテストが存在することを示さない。現在の地形・個体群・表示回帰を維持し、該当PRで新しいテスト名・実行区分を追記する。旧コア・ドローン・内容物全消失・固定施設性能のテスト計画は撤回する。

TERRAIN-01C1のAuthoring回帰では、既存の高地閾値を43,000以外へ変更した場合も、旧7Biomeの高地判定と新しい山地境界が同じ固定小数点値になり、環境設定検証を通過することを確認する。さらに海面・湿地上限・低温・高温・乾燥・湿潤閾値へInspectorの端値を入力し、5段階の固定境界と生成可能な順序へ補正されることを確認する。

| 変更内容／系列 | 必須の自動確認 | 人間・計測で確認する内容 |
| --- | --- | --- |
| 既存の共通基盤 | SystemGroupBoundaryTests、FixedTickSimulationTests、PersistentIdTests、DefinitionCatalogTests、WorldSessionBoundaryTests、SimulationCommandQueueTests、SimulationStateHashTestsから関連範囲。失敗時無変更、安定順、上限、停止・再開を維持 | Console、Assembly依存、必要時Player |
| WORLD-01 | Layer付きセル・チャンク・索引、同X/Yの分離、負座標・int端点、PlayerIdと操作主体。多層試作の遷移元・先・水密条件 | 人工海中、上下遮断、乾燥室への出入りの試作 |
| TERRAIN-01 | 同Seed・版・設定の`ushort`・`0〜65535`の基礎気温・地形気温・基礎湿度・標高とBiome分類、海面以下の補正0、既定海面・高地境界での補正16,551、最高標高での補正32,768、基礎気温未満への飽和、海面閾値19,999・65,535の拒否、64bit中間計算と一回適用、0・65,535の端点、気温境界11,999／12,000・22,999／23,000・32,767／32,768・39,999／40,000、湿度境界15,999／16,000・28,499／28,500・39,499／39,500・49,999／50,000の分類、標高境界19,999／20,000・36,000／36,001・42,999／43,000・53,999／54,000、新4軸の境界13,106／13,107・26,213／26,214・39,320／39,321・52,427／52,428、木本植生ポテンシャルの192・96・48タイルと4：2：1の整数加重平均・専用Salt・ワープ座標・負座標・チャンク境界、湿度を二重合成しないこと、湿地分岐39,320／39,321、地形起伏の中心・上下左右16・斜め11の9点、局所差0・16,383・16,384・65,535、海洋影響の8方向×16・32・64・128タイル、斜め11・23・45・91、最短距離値、海方向数0〜8、直線海岸3方向の`DirectionalExposure = 24,575`・`CoastalInfluence = 34,815`、半島・小島6方向の`49,151`・`53,247`、合成式、海面下表示65,535、淡水除外、亜熱帯海洋性林境界52,427／52,428、火山活動度の256・128・64タイルと4：2：1・専用Salt・活動段階、適合地質との中核・周縁・地熱分岐、露出マグマの中核包含、既存`Weirdness`とのGeneratorVersion分離、64bit座標加算・範囲外明示失敗・設定配列検証・キャッシュ前後一致、18バイオームの地形気温・基礎湿度・標高範囲内外、5段階の気温・湿度と4陸地区分の組合せ候補被覆、高山岩地が全気温・全湿度を受理すること、気候・標高・地質→陸海→水文→最終Biomeの順序、用途別Saltの分離、台形型適性の代表範囲・両傾斜・許容範囲端0・範囲外候補除外・片側傾斜なし、通常Biomeの重み合計256、分類値適合度、64bit整数加重和、全候補0点でも候補を維持すること、候補別Saltの低周波同点変動、安定ID最終同点解決、通常比較の先頭候補選択、候補なし・不正Definitionの監査失敗、生成順・Job順・候補列挙順・ロード範囲の不変、可変状態・配置結果を固定Biome入力へ戻さないこと、河川・池・岩壁でも元の標高区分を保持すること、基礎湿度と土壌水分・水域の分離、分類後の環境値保持、表示用変換値が生成へ戻らないこと、固定Biomeと可変Blockの分離、複数パレット、母岩適合、資源Definition別Salt、256×256タイルの監査窓で全地上バイオームの木材・石材・鉄鉱石候補最低クラスタ、差分適用、元生成不変、差分込み有効地形近傍・影境界 | 基礎気温・地形気温・基礎湿度・湿度5段階・連続標高・標高5区分・木本植生ポテンシャル・地形起伏・火山活動度・水文・海洋影響・Biome・Blockの個別可視化、各段階の実面積比、木本植生ポテンシャルの連結領域直径、地形起伏と標高区分のクロス集計・海岸内陸別分布・飽和比率、海洋影響と海岸実距離・海方向数・陸塊幅のクロス集計、火山活動度と適合地質の重複率・中核周縁の連結領域・孤立セル・露出マグマ包含、湿地条件内の草本・樹木湿地比、18バイオームの代表範囲と遷移側範囲を分けた分布監査、上位候補・軸値・軸別適性・重み・軸別積・総合スコア・同点解決段階・特殊判定・境界両側の環境差と接続理由、乾燥地域の局所水場と湿原Biomeの区別、水域・岩壁と標高区分の同時表示、資源密度・最大空白を含む分布監査、Map Viewer、Debug Visualization、ブロック・母岩識別 |
| TERRAIN-01C5／Biome ID置換 | `None = 0`、18種の安定ID 1〜18と仕様表の完全一致、ID重複・欠落・範囲外の拒否、Definition登録順・表示順・表示名・翻訳による不変、C5前の保存スキーマ・GeneratorVersion・生成済みチャンク・キャッシュの拒否、旧値を同じ数値の新Biomeへ暗黙変換しないこと、`AnimalBiomeMask : ulong`の独立bit 0〜17と全18種の明示変換、`None`・未定義IDの変換拒否、鶏Definitionの`TemperateGrassland`更新、状態ハッシュへの新ID・新Mask反映、旧7種のenum名・分類器・遷移処理・配色・`Weirdness`生成・状態・Mask・変換switch・互換フォールバック・旧実装専用テストが残っていないこと | Map Viewer、Gameビュー、凡例、中央情報がID 1〜18と環境名だけを表示し、旧7種モードやLegacy表示を持たないこと。新規ワールドの再生成手順と旧版拒否メッセージを確認する |
| TERRAIN-01C5-A／Biome Definition | `BiomeStableIds`の0と1〜18、独立した`BiomeClassificationSettings`共有既定値、18件の欠落なし検索、全18行の軸重み、全144件の地質適合度、通常16種の重み合計256、水文特殊2種の重み合計0、海洋性林・湿原分岐・高山・火山の境界値、初期スポーン可否、Definition固有Saltの非0・一意性・既存用途Saltとの分離、湿原Host 7件／4件、ID・Salt・範囲・重み・地質・Host破損の拒否、登録逆順での検証不変 | 製品生成・Scene・Prefab・表示は変更しない。Consoleの新規エラー・警告がなく、旧7Biome表示が意図せず変化していないことだけを人間確認する |
| TERRAIN-01C5-B1／通常候補の個別評価 | 海面直下の候補除外、海面閾値上の受理、通常・水文特殊Definition役割、7連続軸の許容範囲、台形型`AxisFit`の両端・傾斜・代表範囲・片側傾斜なし、地質適合、海洋影響段階5、活動的火山地質と火山活動段階4、火山中核段階5の通常候補除外、重み合計256の64bit最大16,776,960、全使用軸0点候補、256・128タイルを2：1で合成する候補別Variation、Warp後負座標、Definition固有Salt、Variation設定破損の拒否 | 製品生成・Scene・Prefab・表示は変更しない。Consoleの新規エラー・警告がなく、旧7Biome表示へ意図しない変化がないことを人間確認する。通常全候補の順位と候補なしはC5-B2、特殊優先はC5-B3で確認する |
| TERRAIN-01C5-B2／通常候補の安定順位 | Score降順、完全同点だけVariation降順、最終同点の安定ID昇順、Score差1の順位不変、最高Score 3候補の完全同点、異なる挿入順、実Definition 3候補の登録逆順、首位・診断用次点・候補総数、海面下の候補なしと部分結果非公開、管理確保と全件ソートなし | 製品生成・Scene・Prefab・表示は変更しない。Consoleの新規エラー・警告がなく、旧7Biome表示へ意図しない変化がないことを人間確認する。活動的火山中核、水文特殊昇格、最終選択はC5-B3で確認する |
| TERRAIN-01C5-B3／特殊優先と最終選択 | 火山Definitionの地質適合・`SupportsActiveVolcanism`・活動度段階5の3条件と水文特殊より高い優先度、淡水接続・湿原タイル資格・広域湿原規模の全成立、木本植生39,320／39,321境界、対象Definitionの地形気温・基礎湿度・標高・木本植生許容範囲、通常首位の専用Host許可・不許可、局所水場の通常Biome維持、特殊不成立時の通常首位、特殊選択後の通常順位保持、通常候補なしの明示失敗と既定結果、Definition登録逆順での最終ID・理由・通常順位不変 | 製品生成・Scene・Prefab・表示は変更しない。Consoleの新規エラー・警告がなく、旧7Biome表示へ意図しない変化がないことを人間確認する。ECS生成への接続と旧7Biome経路削除はC5-Cで確認する |
| TERRAIN-01C5-C／ECS生成切替 | 基礎環境・地質・陸海の後にC3／C4水文を完成し、最終Jobで新18Biomeと地面を4096件へ一括公開すること。海だけが`BiomeId.None`となり、地上は1〜18の有効IDであること。火山中核・水文特殊・通常候補の優先、露出マグマが活動度段階5・追加活動度下限・形状密度をすべて要求すること、マグマ＞固定表流水＞岩壁＞陸地の暫定Ground優先、候補なしのチャンク全体失敗とBuffer部分非公開、状態ハッシュ、チャンク順・Job完了順の不変、GeneratorVersion 2の受理とC5前の明示拒否、旧7Biome分類・橋遷移・`Weirdness`・旧淡水・旧岩地マグマ・12×8開始平地探索が実行可能でないこと。初期スポーンが完成ECSチャンクの許可Biome・乾燥・通行可・資源なしセルだけを有限探索し、原点チャンクの遠い候補より負側隣接チャンクの近い候補を選ぶこと。同距離の未探索候補があり得る間も座標順確定のため探索を継続すること。SampleScene統合は初期スポーン付近の資源や`TemperateGrassland`を保証せず、存在する資源・動物だけを検証すること | SampleSceneで18色が欠落・代替Tileなしで表示され、淡水・海水・マグマ・岩壁の優先が保たれること。`Magma`診断の候補色、中央HUD、凡例が製品と同じ段階5・追加下限・形状密度条件で一致すること。初期カメラが有効スポーンへ移動し、通常移動・チャンク境界往復で空白や旧色が残らないこと。Map Viewerの`Terrain`／`Biome`がGameビューと一致すること。C5-E前の旧分布監査が無効で、基礎`None`を成功値として集計しないこと |
| TERRAIN-01C5-E／分布・境界監査 | `BiomeDistributionAuditSettings`の空Seed・半径範囲、完成1チャンクの全4,096タイル保存則、陸地面積合計、通常首位が候補資格件数を超えないこと、自身隣接18件を含む陸地隣接辺保存則、全153異種ペア台帳、同条件2回の署名・レポート一致、候補なし・完成Buffer不一致・火山中核条件・水文Host不整合の明示失敗。旧単一タイル監査型、旧enum名、旧7bit Mask、旧分類・表示・橋遷移・実行可能な`Weirdness`・旧挙動専用テストの不存在 | `Farmware > World Generation > Analysis > Analyze Distribution`が既定3 Seed×3×3完成チャンクをバックグラウンドで完走し、全153ペアをゼロ件も含めて表示すること。火山中核／周縁、水文Host昇格、候補なし0件、実行時間、比較署名を記録する。景観合否の数値閾値はC6後のC7で合意する |
| TERRAIN-01C6／旧地形表示の置換 | 最終Block・Liquid・固定水文を正とする表示と状態ハッシュ、旧水域分類・旧River／Pond生成・旧Biome直結配色・互換変換・旧専用Wall上面生成に加え、補助表現比較で不採用となった折れ目影または旧14形状境界ハイライトの素材・Material／Shader・Atlas・Scene参照・選択処理・専用テストが残っていないこと、差分適用後も元生成を変更しないこと | GameビューとMap Viewerが新しい有効地形で一致し、材質ごとの16×16 `GroundTop`をGround／Wallで共有し、Wall表示だけが係数0.85で暗くなること。任意の16×16 `FaceBoundaryOverlay`、16×16 `Face`、淡水・海水で通常断面へ重なる半透明の`Liquid深度Overlay`、背後の断面を最初の段から隠す不透明なマグマを確認できること。Ground崖面とWall前面は同じ境界入力・断面素材を使うこと。すべての材質で断面1段目から`Face`を使い、境界ありではEditorがGround用の元色Overlay版とWall用の暗色化Overlay版を事前合成し、用途に対応する完成Spriteを1枚描画すること。`Face`自体は暗色化しないこと。表示高が論理的な水深、Liquid量、通行、採掘結果を変えず、液体専用断面を持たないこと。Wall接続では材質IDを比較せず、異材質Wall間にFace・固定接地影・動的影の切れ目を出さないこと。固定接地影を左右各1px・手前3px・各レイヤーAlpha 22%でサブチャンク単位に結合し、完成済みの差分込み有効地形近傍から求めた露出Wall前縁だけを前面段数分手前へ投影してDirtyサブチャンク単位で`Shadow Caster 2D`を更新すること。奥側WallのCasterでWall上面を自己遮蔽せず、動的影が最下段Faceの根元から伸びること。補助表現は専用比較で採用した1種類だけを使うこと。F4のチャンク境界診断を維持すること |
| TERRAIN-01C6-C2／ECS生成・公開 | `GeneratedTerrainCell`を全チャンクEntityと生成枠が所有し、完成Biome・地質・固定水文と同じ最終Jobで4096件を全上書きすること。Block確定失敗時は旧地形を含む6 Bufferを部分公開せず、成功時は全Bufferを同じ固定Tick境界で公開すること。絶対座標から生成Bufferと任意差分を合成できること。生成署名が全自然Blockセルを含み、永続状態ハッシュが18Biome・8地質パレットの行、Range、重み、Block分類を検証・反映すること | C6-C2は表示入力をまだ切り替えない。Unity Editorで対象EditMode／PlayMode回帰を実行し、ストリーミング完了後に6 Bufferが所定件数で揃うこと、通常移動・境界往復で生成失敗やConsoleの新規エラー／警告がないことを確認する。GameビューとMap Viewerの新Block表示確認はC6-C3・C6-Dで行う |
| TERRAIN-01C6-D1／製品表示カタログと決定論的差分選択 | 製品表示カタログが安定材質ID 1〜41を登録順に依存せず各1件公開し、欠落・重複・範囲外を拒否すること。`GroundTop`・`Face`の空、null、16×16 px以外、16 PPU以外、同一配列内の重複、0以下または1超の暗色化係数を拒否すること。境界なしでは用途別完成配列0件、境界ありではGround用`F×B`と暗色化時のWall用`F×B`を要求し、暗色化なしではGround用をWallへ再利用すること。41材質BlobのGround／Wall Runtime IDを別々の昇順検索表へ変換し、役割をまたぐフォールバック、未解決ID、安定ID・Runtime ID重複を拒否すること。表示選択Version 1が負座標を含むWorldLayer・絶対X/Y・断面段・安定材質ID・用途別Saltから可変長配列の範囲内を再現可能に選び、境界合成添字を`faceIndex×B + boundaryIndex`で求めること | C6-D1では製品Scene、暫定Presenter、Map Viewer、Atlas、既存の検品・生成ツールをまだ切り替えない。追加した対象EditModeテストとPresentation／Test Assemblyのコンパイル、Consoleの新規エラー・警告0件を確認する。残り36材質のSprite完成は不要で、実画面の新立体表示、全41材質アセット接続、旧表示削除はC6-D2〜D6で確認する |
| TERRAIN-01C6-D4／固定接地影・折れ目影・動的影 | 現在Wallかつ差分込み有効地形近傍の南隣セルが非Wallの露出根元だけから、16×16サブチャンクの横連続区間を作り、異材質Wallを分断しないこと。奥側Wallを除外してWall上面の自己遮蔽を作らないこと。固定接地影の左右1 px・手前3 px・各Alpha 22%、32 px折れ目影の上端Alpha 35%と0／16 px開始行、論理Wall根元に対するCasterの追加補正0タイルを固定すること。Dirty再反映でCasterを再利用し、表示外プール、アンロード、Floating OriginでSimulation状態を変更しないこと。製品Materialが専用Shaderを参照し、Shaderエラーがないこと。Groundを`Default`、Wallを`TerrainWall`へ分離すること | SampleSceneでWall上面・前面・折れ目影が+2Y、固定影が+2Yから手前3 px、Wall上資源が+2Y、動的Casterが論理根元に接続済みであることを自動確認する。Gameビューでは固定接地影と動的影、Wall上面の自己遮蔽、32 px連続減衰を確認する。正式な環境光・局所光はTIME-01Bで接続する |
| TERRAIN-01C6／地形の固定明暗と照明境界 | `GroundTop`・`Face`・透明な`FaceBoundaryOverlay`と、そのEditor合成結果だけで材質を識別できること。固定光が左右へ偏らない弱い上方光を基本とし、部位・差分間で方向と強さが揃うこと。Ground／Wallで共有する`GroundTop`は、Wall表示の0.85暗色化後も固定光の向きと模様を保つこと。左右へ偏った強いハイライト、硬い方向影、特定光源の色、隣接セルへ伸びる投影影、時間帯の明暗をBase Spriteへ含めないこと。Normal Mapが41材質の必須参照・必須Import要件になっていないこと | 動的照明なしでも立体構造を読め、`Light 2D`と`Shadow Caster 2D`による明るさ・色・投影影の変更が焼き込みと不自然に競合しないこと。採用した固定接地影は、2段Wall向けの手前3px・左右対称の接地感を保ちながら光源移動へ追従しない影として動的影と区別でき、明るい環境でも濃すぎないこと。Normal MapはTIME-01Bで代表材質を比較し、Pixel Perfect、Atlas、メモリ、GPU時間、Light 2D数を記録してから採否を決める |
| 地形タイル試作工程／代表5材質試作 | `GrassLoam`、`DesertSand`、`Sandstone`、`Granite`、`Basalt`について、16×16 `GroundTop`、材質ごとに任意の透明な16×16 `FaceBoundaryOverlay`、Ground崖面とWall前面で共用する16×16 `Face`をAsepriteまたはPixeloramaで手制作すること。専用`WallTop`は作らず、Wall上面でも同じ`GroundTop`を使う。断面は1段目から`Face`を使い、境界ありではEditorがGround用の元色Overlay版とWall用の暗色化Overlay版を事前合成し、Runtimeでは用途に対応する完成Spriteを1枚だけ描画すること。PixelLabは材質タイルの制作、ラフ生成、Prompt、配色決定に使用しないこと。各部位が少数色・低コントラスト・単純なブロック状模様で材質を読め、写真調の細密描写、強い明暗ノイズ、セル単位の枠を含まないこと。`GrassLoam`の`GroundTop`は草だけとし、Wall表示では同じSpriteを0.85で暗色化する。`FaceBoundaryOverlay`は暗めの緑の草層から暗めの茶色い土層へ切り替え、`Face`は上面より全体的に暗く、内部コントラストを抑えた茶色5色の土層と小石で構成する。根は使わないこと。完成した今回の画像では`GrassLoam`を境界あり、`DesertSand`・`Sandstone`・`Granite`・`Basalt`を境界なしの仕様とすること | 同一材質Ground上のWall上面・Face 2段・固定接地影の重なり、採用差分の全左右・上下組合せ、境界あり／なしの1段目、3段以上の縦反復、淡水・海水越しの断面、マグマ背後の断面が透けないことを固定検品マップで比較すること。`GrassLoam`では`GroundTop`、境界、断面の順に全体が暗くなり、`Face`の5色内の明暗差が小さいことを確認すること。上面、境界、断面で色相を統一せず、材質構造を読めることを確認する。良好なら、同じ明度関係を材質固有の色相と模様を保ったまま残り40材質へ適用すること。Wall暗色化係数0.85、採用した固定接地影と動的影、均等選択する可変長配列の内容、Ground崖面段数、Wall前面段数、41材質表の確定済み境界あり11材質・なし30材質、Ground崖面・Wall前面でのOverlay入力共有、Ground用元色合成結果・Wall用暗色化合成結果の使い分けが一致することを確認すること。`GroundTop`・`FaceBoundaryOverlay`・`Face`は地形タイル表現仕様第7章の採用パレット・使用比率・模様方針に一致させること。各部位の使用比率合計が100%であること、実Spriteが通常±5ポイント、透明な境界Overlayが±10ポイントの許容範囲内であることを確認すること |
| 地形タイル試作工程／Wall上面・固定接地影・動的影 | GroundとWallが材質ごとに同じ`GroundTop`候補を参照し、Wall表示だけがsRGB各成分へ`WallDarkenFactor`を乗算すること。既定値0.85、Alpha維持、0超1以下、決定論的な最近接整数丸めを保証すること。Wallの8近傍接続は材質IDを比較せず、隣接セルがWallなら接続扱いにすること。論理Wallセルを最下段Faceの根元とし、Wall上面・前面・折れ目影・Wall上資源を+2Yの同じ表示基準へ揃えること。固定接地影を左右各1px・手前3px・各レイヤーAlpha 22%で結合すること。動的影は差分込み有効地形近傍の南隣セルからFaceが露出するWall前縁だけを抽出し、論理Wall根元を基準にDirtyサブチャンク単位で更新すること。タイルごとのGameObjectを本番へ作らず、8px／16px／32px折れ目影と1px境界ハイライトの専用比較では補助表現以外の条件を揃えること | 通常Ground、孤立Wall、広いWall、異材質Wall同士、暗色`Basalt`、青Overlayが45 pxで50%へ達する直下床、93 pxで100%へ達する床なし穴を同じ画面へ配置する。Wall上面0.85暗色化、Face、固定接地影、動的影を重ね、専用比較で折れ目影3深度と境界ハイライトの識別性を判断する。32px影がFace 2段の境界で濃く戻らず連続して減衰すること、動的影が論理Wallセルと同じ最下段Faceの根元から伸びてWall上面から始まらないこと、奥側WallのCasterによる上面の明暗帯がないことを確認する。異材質Wall間にFace・固定接地影・動的影の切れ目がないこと。移動する`Light 2D`へ動的影だけが追従し、チャンク境界、Floating Origin、アンロード・再利用で欠け・重複・旧影が残らないこと |
| 地形タイル試作工程／材質差分配列と決定論 | `GroundTop`・`Face`が1件以上、`FaceBoundaryOverlay`が0件以上であること。各配列内の候補は個別Weightを持たず均等確率で選ぶこと。必須配列の空、16×16 px以外、半透明なOverlay、`WallDarkenFactor`の0以下または1超を拒否すること。`G`・`F`・`B`と暗色化有無`D`に対して、手描き入力数が`G + F + B`、生成数が`B > 0ならF×B×(1 + D)、B = 0なら0`になること。WorldLayer・絶対X/Y・断面段・安定材質ID・用途別Saltが同じなら、ロード、再描画、チャンク生成順、Job完了順にかかわらず同じ均等差分を選ぶこと。`UnityEngine.Random`へ依存しないこと | Inspectorで配列の追加・削除・並べ替え、Spriteプレビュー、暗色化設定と生成数内訳を確認する。Weight／Override項目が表示されないことを確認する。登録したすべての`GroundTop`と`Face`差分を必要な全組合せで並べ、破綻しないことを確認する。Ground／Wallが同じ`GroundTop`候補を参照し、Wall表示だけが暗色化されることを確認する |
| TERRAIN-01C7／バイオーム・地形生成の総合調整 | 調整した気候・Biome・水文・Block設定の端点と不正値拒否、同Seed・GeneratorVersion・設定での決定論、負座標・チャンク境界・生成順不変、状態ハッシュ、河川の流出量別段階幅と地下水涵養Halo・泉候補除外の一致、候補なし・水文特殊Host・火山必須ゲートの維持、C7-Aで固定したSeed・範囲の変更前後集計 | C6の最終Tileを使ったGameビューとMap Viewerで、18Biomeの面積率・領域径・孤立片・境界、水系幅、湖・湿原規模、Block構成、初期スポーン候補、生成時間を比較する。合格範囲は実測後に合意し、仮色や描画だけで生成上の問題を隠していないことを確認する |
| TERRAIN-01／BiomeVariation | 256・128タイル、2：1、既存Biome Warp後の座標、候補別64bit `VariationSalt`、Salt重複拒否、同じ`BiomeScore`内だけの変動値降順、変動値同点時の安定Definition ID昇順、スコア差1で順位不変、首位1候補＋同点次点2候補、最高スコア3候補以上の同点、最終選択前の一意化、先頭候補の最終選択・診断用上位2候補、表示名・登録順・候補列挙順・Job順・チャンク生成順による不変、GeneratorVersionと保存設定の分離 | 完全同点セル率、候補別勝率、連結領域サイズ、1セル孤立領域、同点候補・変動値・安定ID・上位2候補と最終選択理由の診断表示 |
| TERRAIN-01／HydrologyState | `CatchmentAreaCells`、`CatchmentTruncated`、複数`ConnectionFlags`、終端分類`Marine`／`ClosedBasin`／`LongDrainage`の相互排他、自然冠水・自然土壌飽和ポテンシャル、自然水面、低地境界、淡水接続の各フラグ、海水のみ・閉鎖流域のみ・長距離排水のみの除外、湿原共通ゲート、木本植生境界39,320／39,321、流向・現在冠水・現在土壌水分との分離、同Seed・版・設定・絶対座標での再現、ロード順・探索範囲による不変 | 固定水文と現在水文の個別可視化、集水域・上流打切り・下流長距離判定・接続種別・自然冠水・自然飽和・自然水面・湿原ゲート理由、小池・泉・オアシス周囲が無条件に湿原化しないこと |
| TERRAIN-01／水系基礎格子 | 8×8水文セル、負座標の床方向除算、担当範囲`-4〜+3`、固定9サンプル`-3／0／+3`、64bit合計と整数平均、D8全方向、海洋流出口の下流なし終端、隣接海洋流出口の代表標高に依存しない最優先選択、海洋候補複数時の`FlowKey`順、海洋候補なしでの低標高優先・同標高の専用Salt順位、ハッシュ衝突時の座標順、海洋以外の高標高セルを選ばないこと、流向循環なし、下流距離0／4,095／4,096／4,097辺、Level 0〜12・1〜4,096辺のDownstream Summary合成、終端を含むSummaryでの早期停止、キャッシュなしとの一致、`Marine`／`ClosedBasin`／`LongDrainage`相互排他、`LongDrainage`のロード後不変、論理チャンク最大262,144流向辺・2,097,152 D8候補判定、処理予算による中断再開・部分値非公開、チャンク境界・生成順・列挙順・Job順による不変、座標範囲外の明示失敗。中心標高19,999・9地点平均28,888の海洋セルと、代表標高21,000の隣接陸上セルを含む回帰ケースで海洋接続になり、閉鎖流域にならないこと | 代表標高・中心タイルの陸海判定・海洋優先の適用理由・流向・下流鎖・Downstream SummaryのLevel・適用数・展開数・終端までの辺数・長距離打切り・海洋接続・閉鎖流域の可視化、タイル細分化した河川が水文セル間の入口・出口と一致すること |
| TERRAIN-01C4-A1／河道中心線 | 水文セル中心`-4〜+3`の64タイルから最低標高Anchorを選ぶこと、同標高を専用Salt・X・Yで解決すること、負座標、座標範囲外の明示失敗、隣接D8辺だけを受理し流向と下流座標差が一致すること、最大16×16・256ノード、開始・終了Anchor、8近傍連結、最大標高・上り合計・辺数・標高合計・安定順位の比較順、後段高地で最大標高が同値化する反例、同一入力の再現性、表流水フラグなし・非隣接下流・不一致流向・不足作業領域で部分経路を公開しないこと | C4-A2でECS表流水Bufferへ接続した後、河道のセル境界連続性、渓流・河川幅、チャンク生成順不変、GameビューとMap Viewer、Profilerによる実生成費用を確認する。C4-A1単独では表示を変更しない |
| TERRAIN-01C4-A2／タイル表流水 | 渓流中心線、河川の上下左右1タイル幅、斜め角の非充填、河川・流出寄与・供給元座標の重なり順、異なる代表候補間の中心線フラグ論理和、海面未満除外、最大4,096流域セル・262,144タイル、湖面標高未満、末端Anchorからの8近傍Flood Fill、海面下Anchorの候補なし正常終了、同標高飛び地の除外、4096件の行優先Buffer、チャンク境界の絶対タイル一致、状態ハッシュ、失敗時の4 Buffer部分非公開、構造体サイズ | C4-EでGameビューとMap Viewerへ表流水モードを接続する。C4-A2では通常移動と境界往復、Console、`ChunkGenerationSurfaceWaterWait`をProfilerで確認し、表示自体は旧水域のまま維持する |
| TERRAIN-01C4-B2／地下水・自然土壌飽和 | 9×9交差セルと涵養フラグ、固定9地点の補正後地質平均、涵養最大、流出倍率2,048、帯水係数最小、64bit地下水式、接続境界32,767／32,768、2：2：1：1と合計6、0／65,535端点、飽和境界39,320／39,321、4096件の行優先自然水文Buffer、正側9個目の担当セル、状態ハッシュ、C6-C2以降は失敗時の6 Buffer部分非公開、固定Tick一括公開、構造体サイズ | C4-Eで地下水・自然土壌飽和の個別表示と分布監査を追加する。C4-B2では通常移動と境界往復、Console、`ChunkGenerationGroundwaterWait`をProfilerで確認し、表示自体は旧水域のまま維持する |
| TERRAIN-01C4-C／湖水収支・泉 | 比重2：1：1と合計4、64bit湖水収支、越流淡水化、境界26,213／26,214・52,427／52,428、一時湖・塩湖・淡水湖のType・固定水面・淡水接続、地下水のない最低8近傍、落差倍率16、放出境界39,320／39,321、半径4セル抑制、放出度・X・Y同点順、泉流追加時の基礎D8・下流・集水・流出寄与不変、通常河道と共有する21×21Halo涵養、隣接河川幅だけが入るセル、一時湖Lake接続の正側境界同期と地下水値保持、海面未満の泉流除外、21×21周辺水文の8担当生成、帯水係数上限による安全な候補除外、状態ハッシュ、C6-C2以降は失敗時の6 Buffer部分非公開、構造体サイズ | C4-Eで湖区分・泉候補・抑制理由・下流接続を表示する。C4-Cでは通常移動と境界往復、沿岸、Console、`ChunkGenerationLakeSpringWait`をProfilerで確認し、表示自体は旧水域のまま維持する |

2026-10-22にUnity 6000.3.8f1のEditModeで`Farmware.WorldGeneration.Tests.DeterministicSurfaceWaterPathGeneratorTests`を実行し、最大標高同値化の反例を含むC4-A1対象13件がすべて成功した。続けて修正後ソースで`Farmware.WorldGeneration.Tests`一式426件もすべて成功し、最終再コンパイルは警告0件・エラー0件だった。C4-A1は純粋計算のみで表示を変更しないため、Gameビュー、Map Viewer、Profilerの確認は表流水Bufferへ接続するC4-A2で行う。

C4-A2の最終AI確認では再コンパイルが警告0件・エラー0件で成功した。PRレビュー後に正側境界の担当範囲回帰、別供給元の河川幅・渓流中心線を両適用順で重ねる回帰、9地点平均では陸上でも最低Anchorだけが海面下になる流域の回帰を追加した。最新ソースで`DeterministicSurfaceWaterGeneratorTests` 9件、実Jobの4 Buffer同時公開1件、固定Tick境界公開1件、World Generation EditMode Assembly 435件、World Generation PlayMode Assembly 56件がすべて成功した。海面下Anchorの回帰では、海面以上の代替タイルを開始点にせず、その流域を淡水湖候補なしとしてチャンク生成を正常継続する。最終ソースのEditMode全体は169.39秒、PlayMode全体は6.74秒だった。中心線フラグ修正前に実行した明示的性能比較1件も15.50秒で成功し、1／4／16チャンクの水文のみが262.087600／774.949100／3,226.749000ms、水文＋表流水が573.823800／2,055.544000／7,713.830000msだった。今回の海面下Anchor修正は湖候補探索前の早期正常終了だけで作業配列・走査範囲・成功経路・公開容量を変えないため、性能比較は再実行していない。EditMode実行中は既知のテスト用System Group順序属性警告だけが発生し、新規警告はなかった。最終Console実状態はコンパイル失敗なし、エラー0件、警告0件だった。Presentationを変更していないため表流水表示テストは追加せず、通常移動・境界往復の体感とProfiler Markerは人間確認へ残す。

C4-B2のAI確認では再コンパイルが警告0件・エラー0件で成功した。PR #40レビュー修正後は、地下水入力時の河道供給元を2セルHaloへ広げる境界回帰を追加し、`DeterministicSurfaceWaterGeneratorTests` 10件、`DeterministicGroundwaterHydrologyGeneratorTests` 10件、World Generation EditMode Assembly 457件、PlayMode Assembly 56件がすべて成功した。EditMode全体は141.43秒、PlayMode全体は7.14秒だった。明示的性能比較1件も10.41秒で成功し、1／4／16チャンクの表流水までが280.581／908.071／3,660.614ms、地下水・自然土壌飽和を含む場合が284.813／931.007／3,766.692msだった。修正前の追加時間4.910／23.374／96.088msに対し、修正後は4.232／22.936／106.078msである。最終Console実状態はコンパイル失敗なし、エラー0件、警告0件で、テスト中の警告は既知のSystem Group順序属性だけだった。通常移動、チャンク境界往復、体感停止、Console、`ChunkGenerationGroundwaterWait`の人間確認は修正前の2026-10-22に完了しており、レビュー修正後の体感とProfilerは再確認対象として残す。

C4-Cの初回AI確認では再コンパイルが警告0件・エラー0件で成功し、`DeterministicLakeSpringGeneratorTests` 16件、製品Jobの通常公開1件と失敗時非公開1件、明示的性能テスト3件、World Generation EditMode Assembly 474件、PlayMode Assembly 56件が成功した。EditMode全体は192.11秒、PlayMode全体は9.84秒だった。初回PlayMode実行では21×21周辺水文の直列費用によりストリーミング3件が300フレーム上限を超えたが、8担当の行並列化と帯水係数上限による安全な集水省略後は56件すべて成功した。最適化後の1／4／16チャンク同期生成は560.460／1,298.688／7,240.875msで、最適化前の714.457／2,496.452／8,949.429msから短縮した。初回実装は人間による通常移動、境界往復、体感停止、`ChunkGenerationLakeSpringWait`の確認とcommit許可まで完了した。PR #41レビュー修正では、隣接河川幅だけが入るHaloセルの地下水更新、海面未満タイルを泉流で上書きしない回帰、一時湖の最終Lake接続を21×21泉キャッシュへ同期する正側境界回帰を追加した。追レビューでは河川幅セルの泉候補除外、製品Jobが公開する最終湖区分、4区分の診断色、Map Viewerと製品Generatorの一致を追加した。Unity 6000.3.8f1で厳格再コンパイルが警告0件・エラー0件、対象`DeterministicLakeSpringGeneratorTests` 20件、`HydrologyVisualizationTests` 7件、製品Job統合7件、World Generation EditMode Assembly 480件、PlayMode Assembly 56件、明示的性能テスト3件がすべて成功した。EditMode全体は163.78秒、PlayMode全体は10.16秒、性能3件は合計13.79秒だった。通常移動、境界往復、沿岸、体感停止、`ChunkGenerationLakeSpringWait`、GameビューとMap Viewerの湖4区分の人間確認を再確認対象として残す。
| TERRAIN-01／集水面積 | 対象セルを含むD8逆辺の集計、上流なし1、分岐・合流、1単位64タイル、距離・`FlowKey`・座標による正規化順、一本鎖と分岐それぞれの寄与4,095／4,096／4,097件、4,096件目の直接逆辺8件の検査、フロンティアが空でも上限セルに未選択上流がある場合の`CatchmentTruncated = true`、ちょうど4,096件で未選択上流がない場合の`false`、Level 0〜6・1〜64辺長の階層Summary、外部入口セルの保持、上限をまたぐ・複数出口・ノード再進入Summaryの分解、キャッシュ有無・構築順・破棄前後の一致、論理チャンク最大262,144セル選択・2,097,152逆辺判定、処理予算による中断再開、部分値非公開、湿度・降雨の非合成、Job順・チャンク生成順・ロード範囲による不変、座標範囲外の明示失敗 | 集水セル数、タイル換算面積、打切り、上限セルで見つけた未選択直接上流数、最遠寄与距離、Summary利用数・Level別展開数、上流グラフの可視化、Summary境界で値がリセットされないこと |
| TERRAIN-01／自然流出 | 固定9地点の基礎湿度平均、0・65,535の端点、第2.18節と同じ上流集合の再利用、64bit `RunoffMoistureSum`、最大268,431,360、65,535除算、流出相当15／16・127／128・4,095／4,096の境界、`River`が必ず`Stream`を含むこと、追加上流探索なし、上流湿潤・下流乾燥の外来河川、打切り時の遠方寄与除外、現在降雨の非合成、集計順・Job順・Summaryキャッシュによる不変、流路タイル以外を水面化しないこと | 流出相当値、渓流・河川・一時流路候補、外来河川、流路の連続性、打切り率・最遠寄与距離、流域面積別の水路密度・次数・長さ、自然水面範囲の可視化 |
| TERRAIN-01／閉鎖流域・湖 | 4,096辺以内で流向なし末端へ到達したセルだけの閉鎖流域処理、`LongDrainage`除外、同一末端への正規化済み流域集計、流域面積3／4、`CatchmentTruncated`時の固定湖・越流点除外、非打切り流域だけの境界辺の越流標高と安定同点順、正の比高、流出相当7／8・15／16・31／32、充填係数2,048、64bit湖面式、湖底からの連結、同標高飛び地の除外、タイル標高未満だけの水面化、越流辺がD8流向・集水面積を変えないこと、複数越流の循環検出、生成順・Job順による不変 | 流域面積、上流打切り、下流長距離判定、湖底・越流点・比高、充填率、湖面積、孤立水面、大規模閉鎖流域、乾燥窪地、小池・一時湖候補、越流接続の可視化 |
| TERRAIN-01／地質分類 | 8分類の安定ID、ID重複拒否、透水性・地下水保持・侵食抵抗の0／65,535端点と範囲外拒否、カルスト・活動的火山フラグ、必要な資源適合度の欠落拒否、Biome・Block・資源IDとの分離、採掘・埋立・表面Block変更後の固定地質不変、木材を地質資源表へ含めないこと | 地質分類、連続特性、カルスト・活動的火山適性、代表Block・地層・鉱床の個別可視化、Biomeとのクロス集計 |
| TERRAIN-01／地質基礎値 | 8分類それぞれの透水性・地下水保持・侵食抵抗の確定値、共通5段階中央値との一致、石灰岩だけの初回カルスト適性、火山岩だけの初回活動的火山適性、局所補正の0〜65,535飽和、補正後も基礎Definitionが不変、Biome適合度との重複加算禁止、GeneratorVersion間の固定 | 3連続値の個別表示、地質分類とのクロス集計、局所亀裂・粘土層・風化帯・帯水層の補正前後、カルスト・火山必須条件の成立理由 |
| TERRAIN-01／地質地域 | 512タイルの負座標床方向除算、サイト中心、ジッター端`-192／+191`、X・Y別ワープ、256・128タイル、2：1、最大変位64、5×5の25サイト、3×3外が最近傍になる境界例、64bit平方距離、完全同距離のサイト・マクロセル座標順、候補列挙順・Job順・チャンク生成順による不変、用途別Salt分離、座標範囲外の明示失敗 | 地質領域の面積・直径・境界長・孤立セル、最近傍サイトと距離、ワープ前後、標高・Biome・火山活動度とのクロス集計 |
| TERRAIN-01／地質出現重み | 8基礎重みと合計10,000、5標高区分×8分類の倍率、サイト座標での標高再評価、64bit補正重み・合計・累積区間、基礎重み合計不正・負倍率・合計0の拒否、偏りのない有界整数変換と再抽選、区間境界、サイト内再抽選禁止、候補列挙順・Job順による不変、Biome・水文・資源・火山活動度の非合成 | 複数Seedの分類別・標高別サイト率と実面積率、隣接同種サイトの結合領域、火山岩と火山活動度の独立性、想定6〜12チャンク規模 |
| TERRAIN-01／地下水 | 基礎湿度・流出寄与・自然表流水の涵養最大、流出係数2,048、局所補正後の透水性・地下水保持の最小、64bit乗算と65,535除算、0・65,535端点、接続境界32,767／32,768、人工水域・現在降雨・灌水・排水・採掘穴の除外、地下水だけで自然水面を作らないこと、河川・湖形状への非帰還 | 地下水ポテンシャル、接続フラグ、地質別発生率、河川・湖との重複、連結地下水域、1水文セル孤立域、局所地質補正前後の可視化 |
| TERRAIN-01／自然土壌飽和 | 基礎湿度・地下水の比重2、排水しにくさ・平坦さの比重1、合計6検証、64bit加算・除算、0・65,535端点、飽和境界39,320／39,321、タイル起伏の保持、低地・淡水接続とのAND条件、自然冠水とのOR条件、現在土壌水分・降雨・灌水・排水・耕作・人工水路・Block変更の除外 | 飽和度、地質・地下水・起伏・基礎湿度とのクロス集計、閾値前後の連結域・孤立セル、水文セル境界に四角い不連続が出ないこと |
| TERRAIN-01C4-D1／自然冠水の純粋計算 | 半径32の内外、非負整数平方根、距離0／32、水面との比高0／4,096、流出倍率512と65,535飽和、距離・低さ・平坦さ・規模の2：2：1：1、冠水境界39,320／39,321、水源なし0、同じ整数距離での規模・X・Y順、負座標、海水・塩湖・一時湖・泉流・意味フラグ不足の除外、不正設定・不足Bufferの明示失敗 | C4-D1単独では表示を変更しない。C4-D2とC4-Eで冠水源、距離、比高、4入力値、冠水度を確認する |
| TERRAIN-01C4-D2／自然冠水・湿原規模のECS接続 | チャンク外半径32の水源、4096件の固定Buffer、状態ハッシュ、失敗時の部分非公開、低地20,000／36,000、淡水接続、自然飽和または自然冠水、セル内適格15／16タイル、上下左右3／4セル、斜め非連結、生成順・ロード範囲による不変 | C4-Eで資格タイル数、連結成分、広域湿原と局所池辺・泉辺・オアシスの区別を表示する |
| TERRAIN-01／湖水収支・泉 | 湖充填率2・地下水1・気候保持1、64bit合成、越流淡水化、水収支26,213／26,214・52,427／52,428、一時湖の固定水面除去、塩湖の淡水接続除外、海岸水域との分離、地下水接続境界、8近傍最低標高、落差倍率16、泉放出39,320／39,321、河道中心線・隣接河川幅・既存恒久水面の泉候補除外、4水文セル内の最大候補と座標同点順、泉がD8・集水・流出量を変更しないこと、最終4湖区分の状態ハッシュと診断色 | 湖候補の昇格・除外理由、淡水湖・塩湖・一時湖、越流・地下水重複、泉候補・抑制・泉源・下流接続、GameビューとMap Viewerの区分一致、水温・化学Overlayとの分離 |
| TERRAIN-01／通常Biome適性表 | 15通常Biomeと火山周縁の全代表範囲、既存許容範囲への包含、木本・起伏の全範囲候補、亜熱帯海洋性林の海洋ゲート、火山段階4ゲートと段階5特殊優先、各軸重みと行合計256、水文特殊2Biomeの通常比較除外、未使用軸0、表欠落・範囲逆転・合計不正の拒否、許容端0点でも候補維持 | Biome別候補率・勝率・代表範囲内率・軸別寄与、上位2候補差、複数Seedの面積率、高山・火山の代表側と遷移側の分離表示 |
| TERRAIN-01／Biome地質適合度 | 18Biome×8地質の全144組合せ、安定地質キーによる参照、値域、欠落・重複拒否、通常Biomeの地質重み正数、水文特殊2Biomeの地質重み0とスコア非加算、火山・地熱地帯の火山岩65,535・他7分類0、`SupportsActiveVolcanism`必須、段階4・5の同一地質ゲート、地質特性値との重複加算禁止、GeneratorVersion間の固定 | Biome別・地質別の候補率・勝率・面積率・軸別寄与、低適合地質でも非火山Biomeが候補から消えないこと、火山地質と活動度の重複率、18Biomeのクロス集計・凡例・代表Block確認 |
| TERRAIN-01／環境境界と最終選択 | 海面下限20,000と低入力の設定・分類拒否、Inspector補正、最大スコア16,776,960、`BiomeScore`・同点時`CandidateVariation`・さらに同点時安定IDによる一意化、候補1件・候補2件・完全同点群での先頭選択、診断用次点の有無で結果が変わらないこと、候補なし・範囲不正・ID／Salt不正の明示失敗、第三候補がなくても有効な先頭を選べること、接続例の未記載・追記・並べ替えで結果不変、水文特殊2Biomeの専用Host集合の完全性・重複・通常以外のID拒否、Host許可時昇格・不許可時Overlay化、火山段階4の通常比較・段階5の特殊優先と必須地質、中核に周縁リングがなくても隣接セル自身の条件を満たせば直接接続、候補列挙順・Job順・チャンク生成順・ロード範囲による不変、GeneratorVersionによる旧橋方式との分離 | 全153ペアの境界長・接触率、両側の気温・湿度・標高と差、植生・地質・活動度・水文・海岸の根拠、1〜3タイル孤立片、領域サイズ、候補・選択理由の診断表示。自然な崖・河岸・火山境界と意図しない広域気候の急変を複数Seedで確認し、接触率0を一律合格条件にしない |
| TERRAIN-01／Blockパレット | 18表層行と8地質行、各Ground・Wallパレット合計256、Block安定ID・分類・重複、表層64・32・16タイルと4：2：1、地質32・16タイルと2：1、用途別Salt、乾燥度、露出式の64bit整数演算、境界39,320／39,321、露出率0で全表層・65,535で全セルが露出基盤となること、`PeatSoil`の飽和境界、自然壁の地質選択と水密・移動遮断、初回41材質の共通Item・Ground形態・Wall形態の一対一対応、固定Biome・地質・Block・Surfaceの分離、表示差分がSimulation結果を変更しないこと | 表層パッチ径、市松・孤立セル、Biome×地質の全組合せ、最終Ground・Wall・Surfaceの個別表示、Ground／Wallで共有する`GroundTop`、Wall表示の0.85暗色化、左右各1px・手前3px・各レイヤーAlpha 22%の固定接地影、任意の`FaceBoundaryOverlay`、Ground崖面／Wall前面共通`Face`、1段目からの`Face`使用、Ground用元色OverlayとWall用暗色化Overlayの事前合成、淡水・海水の半透明`Liquid深度Overlay`、背後を隠す不透明なマグマ、用途ごとに採用した表示段数、材質を問わないWall接続、設置面外周からの`Shadow Caster 2D`動的影、全差分組合せ、チャンク境界、Halo、Floating Origin、旧14形状境界ハイライトの不存在 |
| TERRAIN-01／資源適合・密度 | 28資源の3方式分類、地質依存24資源×8地質の全192値、値域・欠落・重複、0適合で非生成、鉄鉱石の全8地質正数、地質鉱床24資源の基準クラスタ数・ノード範囲、`LooseSurface`・`GroundEmbedded`・`WallEmbedded`・`MassiveDeposit`のHost・許可Mask・占有、通常クラスタと大型鉱床の別集計、Biome係数64／128／256／384／512、Biome・地質組ごとの適格セル数を使う窓全体65,536セルの面積按分、単一組で全セル適格かつ係数256・適合65,535なら基準数と一致、1セル断片の1セル分寄与、境界分割数による期待数不変、0適格セル、全セル不適格、符号付き64bit以上の検査付き密度式、余りの決定論的分数抽選、資源別Salt、組・資源の登録順・Job順による不変、表層資源の適格Block・水文条件、TerrainBlock Itemとクラフト資源Itemの分離 | 資源別・形態別・Biome別・地質別の平均・分散・ゼロ窓率、混在窓の適格セル数・係数別寄与・分子・分母・余り、クラスタ形状・最大空白距離、通常生成と保証追加と大型鉱床の別集計、特徴資源の探索価値、Ground／Wall用鉱床OverlayとHostの一致 |
| TERRAIN-01／資源配置競合 | 初回28資源の`ResourcePlacementPriority` 1〜28の一意性・欠落・0・重複拒否、候補列を資源配置前の固定入力だけから生成すること、優先度・安定資源ID・通常／最低保証・クラスタ・ノード・候補順位・WorldLayer・X・Yの全順序、同一セルへ2〜3資源が競合する場合の勝者と後続候補、`MassiveDeposit`の占有セル一式を原子的に予約し一部競合時に形状全体を次候補へ送ること、木材・石材・鉄鉱石の通常配置直後に同資源の最低保証を予約すること、通常要求の候補枯渇時は未配置記録、最低保証の候補枯渇時は監査エラー、Definition登録順・Job完了順・候補列挙順の不変、新規資源を最大優先度の後ろへ追加しても既存28資源の配置・署名が不変、既存優先度変更・途中挿入・削除値再利用の拒否またはGeneratorVersion分離 | 資源別の競合数・次候補使用数・候補枯渇数、通常／最低保証別の受理・拒否理由、配置優先度と安定資源ID、末尾追加前後の既存資源比較 |
| TERRAIN-01／基礎資源最低保証 | 256×256監査窓、Biome適格セル1,023／1,024、木材3〜6源・24以上、素手回収可能かつ`PrimitiveStoneMaterial`の`LooseSurface`石材8〜16源・32以上、石のつるはしで資源の必要硬度と形態固有条件を満たせる通常鉄鉱床3〜6ノード・24以上、`WallEmbedded`のHost Wall硬度判定、埋没石材・道具必須転石・高硬度Wall内鉄鉱床・`MassiveDeposit`の保証数除外、木16・石24以下の石道具2個予算、鉄鉱石2対精錬鉄1以上・鉄製道具8以下、通常抽選後の不足分だけ追加、母岩・液体・占有・排他条件、適格セル不足エラー、同Seed・版・窓・資源IDの再現、追加資源による既存結果不変 | 全18Biomeの保証成立率、初期道具で回収可能な総量、初期循環の実プレイ、保証追加率、適格セル不足理由、開始地点を資源量で補正していないこと |
| TERRAIN-01／隣接全ペア監査 | 全18Biomeの153ペアと自身隣接18件、代表接続例のID解決・重複・双方向性、通常比較16Biomeの120ペアと水文特殊2Biomeを含む33ペアの分類、[隣接監査](./BiomeAdjacencyAudit.md)に記録した旧橋不足17組での先頭候補選択、全8地質・海洋影響ゲート・火山段階4／5の分離、湿原Host集合7件／4件の維持、既定の標高寒冷補正で到達しない入力と分類Definition入力の区別、代表接続例を変えても分類・Host昇格結果が不変 | v0.48の環境に基づく直接接続方針で旧橋不足の失敗を解消する。実ワールドSeedでの自然さと発生頻度は別途分布監査し、隣接環境値・水域の整合をHalo付き純粋関数で確認する |
| 新スポーン | バイオーム許可、占有・通行、段階探索、全禁止・候補なしの明示失敗、禁止バイオームへ妥協しないこと。3×3コア・12×8・近傍資源条件を要求しない | 周辺難易度・希少度の不変、隣接危険地域も生成されること |
| PLAYER-01 | 連続8方向、斜め速度、壁・穴・液体・設備衝突、到達距離、壁越し作業、Layer・セル内位置保存 | 両入力、カメラ、操作応答、既定10 Tick/秒の試作 |
| ITEM-01 | 9＋27枠、装備別枠、スタック互換、数量・耐久・個体参照、満杯・競合、所有権の一意性 | 両入力で分割・結合・装備・収納、満杯ドロップ |
| GATHER-01 | 硬度・耐久消費、素手で得られる木材・`LooseSurface`小石、石道具以上で得られる鉄鉱石、木材3〜6源・24以上、石材8〜16源・32以上、鉄鉱床3〜6ノード・24以上、TerrainBlock Itemがクラフト材料タグを持たないこと、Ground／Wallから同じ材質Itemを1:1回収、資源なしWallのBlock Itemだけの回収、`WallEmbedded`のWall＋資源同時回収・両必要条件・失敗時無変更、`GroundEmbedded`採掘後のGround維持と未採掘時のGround掘削拒否、採掘済み鉱床の独立差分、穴・液体へのGround配置、既存Ground上へのWall配置、地面→壁と壁→地面、手動形態切替なし、壁採掘後の下地維持、GroundなしWall・Liquid重複の拒否、異なる下地上への壁設置、液体除去、Surfaceの原則維持と`BlocksWallPlacement`時の拒否、配置物・個体・依存物競合、埋立→再掘削、Block再配置・自然復元からの鉱床非生成 | 採掘・採集・地形ブロック配置の対象選択、全18バイオームの採取経路、Ground／Wall形態と鉱床Overlayのプレビュー、Block Item・資源Itemの識別、失敗理由、地形境界表示、保存往復 |
| BUILD-01／橋 | 占有・回転、所持品精算、全成功／全失敗、合意した内容物回収、足場と下層維持、マグマの耐熱性、上層依存 | 到達範囲、プレビュー、理由表示、素材名によらない橋の判定 |
| CRAFT-01 | 共通レシピ、材料・出力・予約・中断、出力満杯、木材・石材から石道具、石道具で採った鉄鉱石の精錬、鉄製道具までの初期循環、最低保証クラスタの確定収量、屋外作業台、固定Tick進捗、修理・最大耐久維持 | レシピ一覧、全18バイオームで初期循環が成立すること、屋内に置いただけで速度・数量が増えないこと |
| FAC-01／FAC-02 | 自然壁・設置壁・扉、共有壁、不定形、面積上限、屋内と施設と水密の分離、候補選択、Dirty範囲、ID・在庫の対応付け | 領域輪郭、不足条件、空室、排水・漏水・室内酸素、耐圧、破損 |
| ECO-01 | 既存AnimalSpeciesCatalogTests・AnimalPopulationGenerationTests・AnimalPopulationStateHashTests等に加え、移動、永続／匿名、捕獲・退避の会計、昇格、ゼロからの再定着、保存往復 | 鶏の通常Sprite、可視カリング、画面内の突然消滅防止、1,000／10,000／100,000件 |
| SURVIVAL-01 | 空腹ゼロで直接ダメージなし、攻撃・軽減・耐久、レベル補正なし、全所持品散乱、絶対ExpireTick、未ロード失効、独立死亡履歴、ベッド破損・安全位置 | 警告、回収期限、追跡時のみの大型種構造破壊、遠隔拠点無襲撃 |
| TIME-01 | WorldClockとSimulationTick、通常Tickと睡眠ジャンプの区別、次の起床帯、停止・オフライン非進行、就寝条件・中断。時刻から環境光を求める純粋計算、光源の生成・消滅、地形・配置物Dirtyによる遮蔽輪郭更新、Layer分離、チャンク境界・Floating Origin後の位置一致、照明表示によるSimulation状態・状態ハッシュの不変を確認する | 夜に環境光以外の視界制限なし。移動する光源と遮蔽物へ影の向き・形が追従し、隣接サブチャンクで欠け・重複・旧影残留・ピクセルのぼけや揺れがないこと。複数光源と最大ViewportでCPU・GPU時間、GC Alloc、Light／Shadow描画数を記録し、メニューと状態変更の関係も確認する |
| 睡眠横断回帰 | 同じTick数で成長・加工・個体群回復・液体・空腹・酸素・耐久・燃料・消失期限が飛ばした時刻分だけ進まないこと | 起床直後の天候・活動帯、退避・休息、捕獲継続 |
| JOURNAL-01 | 現在Layer・遮蔽・視認、行動観察、野生／人工証拠、正式取得、段階の一意キー、初回報酬・ランク・解禁条件の循環拒否 | 両入力図鑑、センサー・下層プレビューだけで登録しないこと、研究追跡 |
| PLANT-01 | 種子・根・成長・開花・結実、適地、根ごと採集、再定着、飼育目標、画面外・保存復元 | 栽培・植林・展示、定時一括復活なし |
| WEATHER-01 | 地域天候の再現・境界・予報、睡眠後の現在条件、寒冷・高温曝露、装備破損・料理・室内対策 | 季節なし、短い偵察可能、専用装備の警告と保護 |
| TERRAIN-02 | 水種優先度、固化反応、同一セル地面と液体の排他、伝播予算・安定順、未ロード保留・保存、無制限反応防止 | バケツ、自然海岸接触、未完成表示、キューの診断 |
| DIVE-01 | 同座標の潜水・浮上、Layer存在／未ロード区別、移動先衝突、酸素・耐圧・破損、Layer死亡・回収 | 地上→海中→深海の移動、直下プレビュー、酸素補給、入口不要 |
| VEHICLE-01 | 乗降・収納所有、Layer位置、燃料距離／維持消費、耐久、搭乗中死亡、車種別燃料切れ、安全な乗降先 | 実移動、道路幅、接岸・離着陸、脱出、速度別先読み |
| AUTO-01／LOG | レシピ・有限バッファ、個体情報、FIFO、合流・分配・選別・交差、停止理由、`MassiveDeposit`の複数セル占有・有限残量・採掘間隔・消耗品・出力満杯・枯渇、通常クラスタ／最低保証への非合算、個別／集計で順序・在庫・詰まり・進捗・到着Tick・鉱床残量の等価性 | 電力なし、鉱床採掘設備、遠隔稼働、1,000／10,000／100,000件、長時間・保存反復 |
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

C5-Eの`Analyze_CompletedChunk_IsCompleteAndDeterministic`は1 Seed×1チャンクを2回生成する通常回帰で、2026-10-24のUnity 6000.3.8f1では設定検証との計2件が13.56秒で成功した。既定の3 Seed×3×3チャンク、合計110,592タイルを調べる`Analyze_DefaultMultiSeedAudit_CompletesSpecialCoverage`は最終形で160.34秒かかるため`Explicit`へ分離する。C5-E完了確認では明示実行し、陸地108,069、海2,523、陸地隣接214,934、異種境界5,257、火山中核4、火山周縁1,092、淡水湿原昇格4,977、湿地林昇格0、署名`0x34A0C624BDEBBAFF`で成功した。Editorメニューの人間確認も259.1秒で全4 part・全153ペア・同じ署名を表示し、例外なく完了した。湿地林0件を失敗や合格へ自動変換せず、全153ペアのゼロ件と同様にC7の比較材料として保持する。

同変更でWorld Generation EditMode全体も実行したが、Pipelineの905秒上限で結果返却がタイムアウトし、全件数の合否は得られなかった。個別テスト失敗の通知はなく、Test Runner画面に実行中状態が残っていないことを確認してPipeline Serverを停止・再開した。C5-E対象3件の成功とは分け、全体回帰は未完了として扱う。

2026-10-17の`MAP-02D`一次性能計測は、通常回帰テストへ組み込まず、PlayMode中の一時ハーネスと`ProfilerRecorder`で実行した。[計測レポート](./Map02DPerformanceReport.md)に、通常／高速／試験用速度、拡大Viewport、負方向・遠方・斜め移動、60FPS目標、原点更新なしの対照条件を記録する。詳細再計測では、Tilemap書き込み、分類、旧セル消去、岩壁影のマスク・不透明度・形状・Mesh反映を個別Markerで取得した。`AutomaticRebase_ReusesRockWallShadowForSameLogicalChunk`は、原点変更前後で同じ岩壁影GameObjectとMeshを保持し、新相対位置へ移動して未使用影の掃除から除外できることを保証する。追加テスト単独1件とPlayMode全31件はUnity 6000.3.8f1で成功した。

岩壁影再利用後の比較もAssetへ計測コードを追加せず、Git対象外の一時ハーネスで実行した。通常12は3,000フレーム、高速36は1,920フレーム、縦横4倍・高速36は1,914フレームを60FPS目標で取得し、原点変更時の岩壁影再構築が0ms、位置更新が最大0.012292msであることを確認した。Editorが背面で約10FPSへ制限された予備試行は比較へ採用しない。完成済み集合に未登録の可視チャンク数は部分適用も含むため、そのまま画面の黒抜け件数として扱わない。製品最大ズームとDevelopment Buildでの測定、人間によるちらつき確認は別途必要であり、この計測によって`MAP-02D`完了状態は変更しない。

チャンク単位Tilemap比較実装の`d114156a`後は、Gameビューの実矩形も1920×1080へ固定し、通常2チャンク／拡大9チャンクの静止状態をOFF/ON各2回、5秒ウォームアップ＋10秒記録で比較した。合計4,804フレームの描画CounterとFrameTiming、所有Native Object、64チャンク移動時のプールを記録した。Frame Debuggerは時間計測と分離した。GPU値の遅延・変動と帰属、Native内部容量の保持があるため、合格判定やリークなしの証明には使用しない。条件、計測中のCLI確認1回の混入、未検証事項は[性能計測レポート](./Map02DPerformanceReport.md#commit後の描画gpu個別メモリ一次評価)を参照する。この追加作業は製品コードや回帰テストを変更せず、Development Buildと長時間比較を残す。

`7d65b5ac`後の追加計測は、同版UnityのmacOS Development Player（Mono／Universal出力をArm64で実行）で行った。静止4ケース、速度36で32秒の自動原点変更、128チャンクの往路・復路・再往路の合成試験を自動ハーネスで実行し、GPUタイムスタンプの重複を除いて集計した。Sceneを保存せず、一時C#と.metaはビルド後に削除した。最初のOFF静止は背景、ONは前面、OFFの再試行もfocusが混在したため、公平な静止CPU／GPUの対照比較は未完了である。これは回帰テストの追加や既存38件／163件の再実行ではなく、MAP-02Dの独立した性能診断である。ビルド警告1件、片付けAPIのEditorエラー2件と復元方法、実測値、残る検証は[Development Playerの一次比較](./Map02DPerformanceReport.md)を参照する。

`9f6311dd`後のOFF再計測では、ユーザーがPlayerを前面に保ち、静止597／600／600／598フレームと速度36の移動1,905フレーム、計4,300フレームすべてfocus=trueだった。全フレーム前面の先行ON記録と比較し、ViewportとGPUタイムスタンプの重複排除件数も確認した。今回は同一ビルドの再利用で、ONを再実行したり、複数プロセスで順序を入れ替えて反復したりはしていない。通常描画CPUとSetPassの増加、原点変更以外に残る停止も記録し、GPU中央値が近いだけで合格とはしない。製品コード・テスト・設定変更はなく、既存回帰テストも再実行していない。詳しい範囲と制約は[前面を維持した追加比較](./Map02DPerformanceReport.md#前面を維持した追加比較)を参照する。

`3ea6c19f`後の通常移動スパイク調査は、まずEditorの既存Markerを有効1,197フレーム記録し、続いて同一Development PlayerをONで再利用してバイナリProfiler記録を取得した。Player移動1,901フレームはすべて背景であり、前面静止比較には混ぜない。読み込めた最後の2,000フレームから親子サンプルとWorker時刻を照合し、表示対象収集時の生成Job待ちを特定した。Deep Profileや新規製品Marker、再ビルド、回帰テストの追加・再実行は行っていない。生成結果の非同期公開を実装する際には、決定論、未完了結果の非公開、アンロード・World破棄時のJobとNativeメモリ寿命、同時生成上限、待機先がStreaming側へ移っていないことを検証する。詳細は[原因特定レポート](./Map02DPerformanceReport.md#通常移動中に残る停止の原因特定)を参照する。

2026-10-17の非同期公開比較では、`ChunkGenerationSystem_ProducesBufferAndDisablesRequest`を、開始更新では生成中・両Bufferが空・要求無効、後続更新で完成結果を公開するテストへ更新した。全4096セルの地形・資源を直接計算と比較する。追加の`ChunkGenerationSystem_RespectsConcurrentLimitAndReusesSlot`、`ChunkGenerationSystem_DestroyedTargetDoesNotPublishToReplacement`、`ChunkGenerationSystem_WorldDisposeCompletesOwnedJobs`、`ChunkStreamingSystem_RetainsRunningGenerationUntilCompletion`は、枠上限と再利用、破棄済みEntityへの誤公開防止、World終了時のJob／Native配列解放、保持外の生成枠予約と完成後アンロードを保証する。公開待ちのテスト補助はWorkerへCPUを譲りつつ後続System更新を進め、30秒で失敗する有限待機とし、未完成Jobをテストから強制公開しない。最終ソースの関連EditMode 167件、PlayMode 38件が成功した。Development Playerでも速度36の32秒移動を2試行し、収集・公開・投入・Streamingを同時取得した。両試行とも全フレーム前面のため背景だった旧原因特定記録と改善率を比較せず、読み込めたTimeline範囲で長い生成Job待ちが再現しないことと、残る影・分類・書込の停止を分けて記録した。条件・後片付け・未検証事項は[非同期公開の計測](./Map02DPerformanceReport.md#生成結果の非同期公開比較実装)を参照する。人間の表示受け入れは別途必要である。

岩壁影マスクの同期Burst処理は`TerrainLayerPresentationUtilityTests.FillShadowMask_MatchesCompletedTerrainAcrossChunkBoundaries`で全6400セルを中心と周囲8チャンクの完成地形から判定する。東隣を最終Water、北隣を最終Magmaにした入力を含め、基礎岩壁候補を再利用して液体上へ影を延ばさないことを保証する。`FillShadowMask_InvalidArrayLength_RejectsBeforeRunning`の3ケースでは不正な配列長をJob実行前に拒否する。既存PlayMode回帰は影の原点変更時の再利用とPresenter停止・破棄に加え、表示収集が必要な完成近傍を待つことも確認する。性能比較と人間の表示受け入れは別途記録し、自動テストの成功だけで停止感の解消を保証しない。

2026-10-23のC5-C追加レビュー対応後は、Unity 6000.3.8f1の接続済みEditorで厳格再コンパイルがエラー0件・警告0件、World Generation EditMode 556件が343.99秒、PlayMode 56件が141.53秒で全件成功した。完成近傍12件、岩壁影9件、正負intワールド端2件を対象実行でも確認した。PlayModeのScene階層テストは、生成速度によって実行時チャンク9枚が先に作られても、World Grid直下へScene保存された9テンプレートだけを検証し、正常な実行時複製を重複Scene設定と誤判定しない。

同変更のPresentation asmdefにUnity.Burst参照を追加したため、`SystemGroupBoundaryTests` 4件も実行して全成功した。追加パッケージはなく、開発者可視化の式や凡例、Player向けのAssembly分離は維持する。

2026-10-17、ユーザーが影改善後の比較ONで通常移動と`(7,-1)`／`(15,-1)`付近の影の継ぎ目・濃さ・停止感を確認し、問題なさそうと回答した。今回の局所改善の動作確認済みとして扱い、製品の性能合格や比較方式の既定ON採用とは区別する。詳細は[性能計測の受け入れ記録](./Map02DPerformanceReport.md#岩壁影マスク構築の同期burst化)を参照する。

分類用外周の一括生成後はUnity 6000.3.8f1で関連EditMode 196件、PlayMode 39件が成功した。外周の追加16ケースと寿命の追加1ケースを含む。Development Playerでも新旧同一入力の前処理込み比較と前面移動2試行を実施し、開始地点の新旧PNG一致を確認した。これは移動中の全ピクセルや停止感を自動保証するものではない。2026-10-17にユーザーが比較ONの通常移動と`(7,-1)`／`(15,-1)`付近の影の継ぎ目・濃さ・停止感を確認し、問題なさそうと回答してcommitを許可した。今回の確認範囲は動作確認済みとし、最終Viewport・負方向・資源レイヤー等の網羅確認や既定ON採用とは区別する。条件と残る負荷は[外周生成の性能計測](./Map02DPerformanceReport.md#分類用外周地形のバッチ生成)を参照する。この段落はGeneratorVersion 1の外周生成方式に対する履歴であり、C6-D5以降の差分込み3×3有効地形近傍の現行回帰は上記`CompletedChunkTerrainNeighborhoodTests`と岩壁影マスク節を正とする。

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
