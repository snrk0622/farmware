# Farmware シミュレーション技術仕様 v0.1

- 作成日: 2026-10-14
- 最終更新日: 2026-10-17
- 状態: 設備・物流・施設実装前の基盤仕様
- 対象環境: Unity 6000.3.8f1、Entities 1.5.0
- 関連仕様: [ゲームコンセプト](../GameSpecification/2026-10-11_casual_factory_concept_v0.1.md)
- 関連仕様: [ランダムマップ生成仕様](../GameSpecification/RandomMapGenerationSpecification.md)
- 実装計画: [実装ロードマップ](./ImplementationRoadmap.md)

## 1. 目的

本書は、設備、ベルト、搬送アイテム、施設、遠隔工場、建築編集、セーブを後から別方式へ全面移行しないために、実装前に固定する技術上の境界を定める。ゲームルールとプレイヤー体験はゲーム仕様を正とし、具体的なTickレート、速度、容量、Job分割数、読み込み距離などの調整値はプロトタイプと計測後に決定する。

SimulationのECS状態をゲーム状態の正とする。InputとUIはCommandを渡し、PresentationはSimulationの結果だけを読み取る。GameObject、Transform、Tilemap、表示用Spriteを生産、物流、施設、保存状態の正にしない。

### 1.1 Assembly境界と依存方向

共通コードは次のAssemblyへ分離し、機能別Assemblyから用途に対応する共通Assemblyを明示参照する。共通Assemblyは`autoReferenced: false`とし、参照を必要とするAssemblyだけが依存を宣言する。

| Assembly | 責務 | 参照できるFarmware共通Assembly |
| --- | --- | --- |
| `Farmware.Simulation` | ECS Component、System、更新順、ゲーム状態の正 | なし |
| `Farmware.Input` | 入力をSimulation Commandへ変換する境界 | `Farmware.Simulation` |
| `Farmware.Presentation` | Simulation結果の描画、補間、音声 | `Farmware.Simulation` |
| `Farmware.Authoring` | MonoBehaviour、Baker、設定アセット | `Farmware.Simulation` |
| `Farmware.Tests.EditMode` | 共通境界とSystem順序のEditorテスト | 検証対象となる上記4 Assembly |

`Input`、`Presentation`、`Authoring`は互いを直接参照せず、`Simulation`からこれらへ逆参照しない。機能別Assemblyでは、例えば`Farmware.WorldGeneration.Runtime`から`Farmware.Simulation`、`Farmware.WorldGeneration.Presentation`から`Farmware.Presentation`を参照する。既存のカメラ移動入力はゲーム状態を変更しない表示操作なのでWorldGeneration Presentationに残し、建築や設定変更などのゲームルール入力は今後`Farmware.Input`からCommandとして渡す。

ECSの共通ルートGroupは次のとおりとする。

| 共通Group | Unity標準の親Group | 用途 |
| --- | --- | --- |
| `FarmwareInitializationSystemGroup` | `InitializationSystemGroup` | Singleton準備、初期探索などSimulation更新前の初期化 |
| `FarmwareSimulationSystemGroup` | `SimulationSystemGroup` | フレーム単位のSimulation側準備、ストリーミング、結果確定 |
| `FarmwareFixedTickSimulationSystemGroup` | `FarmwareSimulationSystemGroup` | 固定Tickの時間境界と、Command・ゲームルール用子Groupの更新 |
| `FarmwareCommandProcessingSystemGroup` | `FarmwareFixedTickSimulationSystemGroup` | 固定Tick開始時に受付済みCommandを1件ずつ受付順で処理 |
| `FarmwareCommandValidationSystemGroup` | `FarmwareCommandProcessingSystemGroup` | 機能固有Commandの副作用を持たない事前検証 |
| `FarmwareCommandCommitSystemGroup` | `FarmwareCommandProcessingSystemGroup` | 検証成功Commandの状態変更とECB記録 |
| `FarmwareFixedTickGameplaySystemGroup` | `FarmwareFixedTickSimulationSystemGroup` | Command確定後の生産、物流、敵、動物などのゲームルール更新 |
| `FarmwarePresentationSystemGroup` | `PresentationSystemGroup` | Simulation結果の描画・音声への反映 |

`FarmwareSimulationSystemGroup`の更新回数はゲーム内時間の正ではない。固定Tickで進める機能は`FarmwareFixedTickSimulationSystemGroup`の子へ置き、機能固有CommandはValidation／Commit Group、生産・物流・動物などの通常ゲームルールは`FarmwareFixedTickGameplaySystemGroup`へ所属させる。カメラ可視範囲に応じたチャンク要求と地形生成は表示ストリーミング用のフレーム駆動処理であり、固定Tickのゲームルールへ含めない。

## 2. 固定Tickと決定論

- 生産、物流、施設、手動採取、敵、動物、コア進行は共通の固定Tickで更新する。
- 現在のSimulation Tickは`ulong`で保持する。一時停止中はTickを進めず、再開時に停止時間を追いつき処理しない。
- 生産進捗、搬送進捗、クールダウン、次回イベント時刻は整数で表現する。描画フレーム数、`Time.deltaTime`、浮動小数点の累積値をゲーム結果の正にしない。
- Tickレートは共通設定から供給する。同一セーブの実行途中では変更せず、将来の変更が既存セーブへ影響する段階では保存形式またはゲームルールの移行として扱う。
- 同一の初期状態、Command列、Tick数から、Job実行順、Entity生成順、チャンクの表示状態にかかわらず同じ結果を得る。

`SIM-01B`時点では、`FixedTickConfiguration`の開発用既定値を10 Tick/秒、1描画フレーム当たり最大4 Tickとする。描画遅延によって実行可能なTickが上限を超えた場合は、上限を超えた整数Tick分の経過時間を破棄し、Tick未満の端数だけを次フレームへ保持する。floatの描画時間がTick境界へわずかに届かない場合は小さな許容誤差でTickを開始できるが、その不足時間を0へ丸めず、負の端数として次フレームへ残す。同じ合計時間ならフレーム分割によってTick数が変わらないようにするためである。これにより、遅れを取り戻す処理自体が次の遅れを生む連鎖を防ぎ、負荷が高い場合はゲーム内時間が実時間より遅く進む。一時停止中の描画フレーム時間は最初から蓄積しない。

`SimulationTickState.CurrentTick`を固定Tick Groupが開始したTick数と現在実行中のTick番号の正とし、`SimulationTickAdvanceSystem`がGroupの先頭で1だけ加算する。同じTickの後続Systemは加算後の番号を共通して使用する。`SimulationPauseState`は現在Tickと分離し、将来のInputまたは全画面メニューが停止状態だけを変更できるようにする。`FixedTickConfiguration`、`SimulationTickState`、`SimulationPauseState`は`SimulationTimeInitializationSystem`がWorld開始時に1件ずつ作成する。固定Tick Groupは設定を初回更新時に読み取ってWorld寿命中固定し、毎描画フレーム変化し得る一時停止状態だけを読み直す。

10 Tick/秒と最大4 Tickは製品用の最終調整値ではない。設備・物流の性能試作では、同じ固定Tick境界と超過時の破棄方針を維持したまま、操作応答、CPU時間、GC Alloc、遠距離集計との等価性を計測して最終値を決定する。

### 2.1 状態ハッシュ

`SIM-01F`では、同じ初期状態、Command列、Tick数から同じ論理状態へ到達したかを比較するため、`SimulationStateHash`と`SimulationStateHashBuilder`を追加した。状態ハッシュはテストまたは開発者診断から必要な固定Tick完了境界だけで計算し、通常の全Tickでは計算しない。暗号学的な改ざん検出、セーブデータの完全性保証、状態の復元には使用しない。

状態ハッシュ形式の契約は次のとおりとする。

- アルゴリズムはUnity Collectionsの`xxHash3-128`とし、結果には`SimulationStateHashSchema.CurrentVersion`を併記する。ハッシュ対象、フィールド順、型、正規化規則を変更した場合はスキーマバージョンを更新する。
- 構造体の生メモリを渡さず、符号の有無とbit幅を固定した整数をリトルエンディアンで明示的に書き込む。構造体padding、CPUのバイト順、Blob Assetのアドレス、EntityのIndex／Versionを結果へ含めない。
- データ種別ごとに昇順かつ重複しないセクションIDを付ける。`1`から`1023`は共通Simulation用、`1024`以上は設備、物流、動物などの機能用とする。
- 複数EntityやBufferを含める機能は、永続IDや論理座標などの一意で安定したキー順へ並べ、要素数を先に書いてから各値を書く。無効キー、重複キー、Singletonの欠落・重複は任意の1件を選ばず計算失敗にする。
- Presentation、GameObject、Transform、Tilemap、カメラ、チャンク表示状態、再生成可能な空間索引やキャッシュは含めない。表示状態やJobの実行順を変えても、論理Simulation状態が同じなら同じ値を得るためである。

共通セクションには固定Tick設定、現在Tick、一時停止状態、次回永続IDと上限到達状態、任意の定義カタログを含める。定義カタログはGUID昇順、1始まりのRuntime ID、分類、参照順を検証して論理値だけを含める。未処理CommandがQueueに残るTick途中では計算を拒否し、受付番号状態、処理済みCommand Entity、結果通知は保存対象ではない一時データとして含めない。後続機能は共通セクションの後へ機能固有セクションを追加し、その機能で保存・復元すべき状態の正だけを列挙する。

## 3. 配置物と搬送アイテムのECS表現

### 3.1 配置物

- ベルトは1セルにつき1 Entityとし、論理座標、方向、種類、接続状態、永続IDをComponentとして保持する。
- 複数タイル設備は1設備につき1 Entityとし、アンカーセル、向き、占有形状、永続IDを保持する。占有セルごとに設備Entityを複製しない。
- 施設は1施設につき1 Entityとし、構成設備、領域、共有在庫、処理状態、設定、永続IDを保持する。
- セルから配置物を検索する空間索引はチャンクまたはサブチャンク単位の再構築可能なキャッシュとし、配置物そのものの正にしない。

### 3.2 搬送アイテム

- 搬送アイテム1個ごとのEntityは作成しない。
- 各ベルトセルEntityが`DynamicBuffer`で、そのセル上の搬送アイテムを保持する。
- 搬送要素は少なくとも品目の実行時ID、セル内の整数進行度、FIFO順序を持つunmanagedデータとする。
- 1搬送枠には1個だけを保持し、同種アイテムをベルト上でスタックしない。
- セル間移動はBuffer要素の転送として処理し、搬送のためにEntityを生成・破棄しない。
- 設備、施設、コアの在庫は品目IDと数量へ集約したBufferで保持し、ベルトへの出力時に1個単位の搬送要素へ変換する。
- 表示用Spriteは画面内の搬送要素から生成または再利用し、Collider2D、Rigidbody2D、個別GameObjectをSimulationに使用しない。

### 3.3 動物と牧場の境界

- 野生動物は1個体につき1 Entityとし、種類、永続個体ID、整数タイル座標、所属群れ、移動状態をunmanaged Componentとして保持する。表示用SpriteやGameObjectを個体状態の正にしない。
- 初期個体群の生成、地域密度が下がった後のリスポーン、基本移動、ストリーミング、保存は牧場施設から独立させ、施設が存在しない新規ワールドでも動物Simulationを成立させる。
- 初期配置とリスポーンは別の動物データやID体系を作らず、同じ個体生成パイプラインを使用する。初期配置は地域を初めて有効化したときに目標個体数まで補充する一度限りの地域初期化、リスポーンは初期化済み地域の個体数低下をクールダウン後に補う反復処理として、起動条件と補充量だけを分ける。
- 地形用ワールドシードから動物の初期位置を固定しない。新規ワールドごとに地形とは独立した`AnimalPopulationSeed`を一度だけ生成して保存し、地域別の抽選状態、初期化済み状態、次回リスポーンTickと組み合わせて、そのワールド内の個体群を再現する。同じ地形シードから作成した別ワールドでは、動物の初期配置が異なってよい。
- 牧場は施設Entityとして共有在庫と生産状態を保持し、所属する動物を永続個体IDまたはEntity参照で関連付ける。動物を施設内Bufferの値や1×1設備へ変換しない。
- 牧場が不成立になった場合は所属関係だけを解除し、元の動物Entity、個体ID、基本移動状態を維持する。
- 捕獲、解放、牧場所属の変更はCommandとして固定Tick境界で処理し、Presentation上の移動完了を待たない。

## 4. 近距離個別処理と遠距離集計処理

- 設備・物流の初回実装から、近距離の個別処理と遠距離の集計処理を併設する。集計処理を後付け機能にしない。
- 個別状態を復元可能な論理状態の正とし、遠距離用の接続グラフ、イベント予定、集計値は再構築可能な実行時キャッシュとする。
- 遠距離処理は平均生産量による概算ではなく、次に状態が変化するTickまでをイベント単位で進める。
- 個別処理と集計処理は同じレシピ、容量、FIFO、分岐、ラウンドロビン、詰まり規則を使用する。
- 同じ初期状態とCommand列に対し、品目別生産量・消費量、在庫、搬送順序、詰まり、生産進捗、コア到着Tickを一致させる。
- モード切替は固定Tick境界だけで行う。近距離へ戻す際は搬送品の順序と論理進行を個別状態へ復元する。
- 混載、循環、外部接続、敵の干渉などにより等価性を保証できない物流網は、遠距離でも個別処理を継続する。
- 表示チャンクのActive／Unloaded状態だけをSimulation方式の判定条件にしない。

集計可能条件、切替距離、イベントデータ構造は実装前の物流設計で具体化するが、結果等価性と安全な個別処理へのフォールバックは変更しない。

## 5. 永続IDと静的定義ID

### 5.1 ワールド内オブジェクト

- プレイヤー配置物、施設、中央コア、初期配置・リスポーンされた動物など、セーブと相互参照が必要な実行時オブジェクトへワールド内一意の`ulong`永続IDを付ける。個体として保存・参照する再生資源を後から追加する場合も同じ発行基盤を使用する。
- `0`は未割り当てを表す無効IDとし、ワールドは`1`から始まる次回発行IDを保持する。削除済みIDを再利用せず、`ulong`の上限では`0`へ周回させず発行失敗として扱う。
- 初期配置とリスポーンを含む動物IDは、地形シードや座標から導出せず、他の実行時オブジェクトと同じ共通アロケーターから発行する。同一Tickに複数の生成要求がある場合は、地域座標や受付順などの安定キーで要求を並べてから割り当て、Jobの完了順で結果を変えない。
- 共通アロケーターの一括発行要求は、機能固有データを持たず、呼び出し側が設定する`MajorOrder`、`MinorOrder`、`Sequence`の3段階キーだけを持つ。動物生成は地域座標と受付順、建築はCommand順などを割り当て、3値の組が重複する要求はIDを消費せず一括で拒否する。
- Entityの実行時Index、Version、生成順、GameObject名を永続IDに使用しない。
- 範囲移動では永続IDを維持する。コピー、設計図貼り付け、撤去後の再建築では新しいIDを発行する。
- 中央コアと動物を含む各オブジェクトのID、次回発行ID、相互参照先IDをセーブする。ロード済みオブジェクトへ新しいIDを再発行しない。

### 5.2 アイテム・設備・レシピ・動物種定義

- アイテム、設備、施設、レシピ、動物種などの編集可能な静的定義へ、名称や配列位置から独立した変更禁止の128bit定義GUIDを付ける。
- 実行中は定義GUIDを連続した整数の実行時IDへ変換し、Hot PathではGUIDや文字列を比較しない。
- セーブには定義GUIDを記録し、ロード時に現在の定義表へ解決する。
- GUIDの重複、未設定、参照切れはEditor検証またはビルド前検証でエラーにする。
- バイオームや地形のようにコードで閉じた安定分類は、既存値を変更・再利用しないappend-onlyな数値IDを使用できる。

`SIM-01D`では、全0を無効値とする上位・下位各64bitの`DefinitionGuid`と、1始まりの`DefinitionRuntimeId`を共通Simulation型として実装した。Authoringの定義配列はBakerでGUID昇順へ正規化し、入力順に依存しない連続ID、定義分類、解決済み参照を`DefinitionCatalogBlob`へ格納する。GUID文字列はInspectorとセーブ境界だけで扱い、実行時の定義参照は整数IDを使用する。BakerとEditModeテストは同じ`DefinitionCatalogBuilder`を使い、GUID未設定、重複、参照切れ、参照先分類不一致を破損したBlob Assetの生成前に拒否する。実際のアイテム、設備、施設、レシピ、動物種データとSceneへのカタログ配置は各機能PRで追加する。

`ANIMAL-01A`では、動物種固有の生成条件と移動調整値を`AnimalSpeciesCatalogBlob`へ分離し、共通`DefinitionCatalogBlob`で解決した`DefinitionRuntimeId`を参照する。対応バイオームと有効地形は値域の異なる別々のビットマスクで保持し、通常Tickで文字列、GUID、managed参照を使わない。BakerとEditModeテストが共有する`AnimalSpeciesCatalogBuilder`は、未登録・分類不一致・重複する動物種、未知ビット、群れ範囲、密度、固定Tick範囲、徘徊距離、外見差分数を検証してから、実行時ID順のBlob Assetを作る。最初の定義は草原の通常陸地に生息する鶏であり、個体Entityや動物生成Systemは後続PRで追加する。`AnimalSpeciesStateHasher`は機能固有セクションID 1024へ全静的値を固定順で追加し、同じGUIDでも生成条件や移動値が異なるWorldのハッシュを区別する。

## 6. チャンク境界と保存所有権

- 複数タイル設備はアンカーセルを含む論理チャンクを保存上の所有チャンクとし、設備全体を1回だけ保存する。
- 所有チャンク外の占有セルは永続IDへの空間参照として扱い、設備状態を重複保存しない。
- 施設も1つの施設Entityと永続IDを正とし、境界セルや構成チャンクへ施設在庫と進捗を複製しない。
- チャンクをまたぐベルト、設備、施設の接続は永続IDと論理座標から復元できるようにし、ロード順に依存させない。
- 表示をアンロードしても、建設済み工場のSimulation状態は維持する。
- 範囲移動でアンカーが別チャンクへ移る場合は、配置、空間参照、保存所有チャンクの変更を同じ建築トランザクションで確定する。
- チャンク間参照の実行時キャッシュは、ロード、移動、分割、結合後に再構築できるものとする。

## 7. セーブ形式と初期開発中の互換性

- ECSのメモリ、Entityチャンク、Blob Assetのメモリアドレスを直接保存せず、バージョン付きの保存専用データへ変換する。
- ワールドヘッダーと変更チャンクデータを分離し、未変更地形はシード、GeneratorVersion、座標から再生成する。
- 保存は固定Tick境界の一貫したSnapshotから行い、設備、在庫、搬送順序と論理進行、生産進捗、永続ID、プレイヤー地形差分を復元できるようにする。動物については`AnimalPopulationSeed`、地域の初期化済み状態、地域別抽選状態、次回リスポーンTick、現存個体と次回発行IDも保存する。
- 書き込みは一時ファイルを完成させてから本ファイルへ置き換え、中断による既存セーブの破損を避ける。
- `SaveFormatVersion`フィールドは最初から持つが、初期開発中は初期値のままインクリメントしない。
- 初期開発中は古い開発用セーブとの互換性と移行を保証しない。形式変更後に読み込めない開発用セーブは作り直す。
- 製品版またはセーブ互換性を保証する開発段階へ移行するときに、`SaveFormatVersion`の更新条件、移行経路、サポート範囲をあらためて決定する。
- 作成ゲームバージョンと最終プレイゲームバージョンは`SaveFormatVersion`と別に記録する。

## 8. 共通Command Queueと建築トランザクション

- InputとUIはワールドを直接変更せず、建築、撤去、移動、回転、置換、設定変更をCommandとしてSimulationへ渡す。
- Commandは単調増加する受付順番号を持ち、同一Tickではその順番に決定的に処理する。
- 各Commandは、対象セル、占有、地形、必要資材、失われる内容物、施設と物流への影響を変更前に検証する。
- 検証失敗時は、配置、資材、施設、物流、Undo履歴のいずれも変更しない。
- 成功時は、資材精算と配置変更を固定Tick開始時に一括適用する。その後、Dirty範囲の空間索引、物流接続、施設認識を再構築してから同Tickの生産・物流を実行する。
- CommitのComponent値更新と構造変更はCommand専用EntityCommandBufferへまとめ、途中状態を他Systemへ公開しない。
- Undo／RedoはWorld全体のSnapshotではなく、確定済みの建築差分と資材差分に対するCommandとして実行する。

`SIM-01E`では、機能固有のunmanaged Componentを一時Command Entityへ付け、共通の`SimulationCommand`、`SimulationCommandActive`、`SimulationCommandCompleted`と組み合わせる。受付済みEntityは`SimulationCommandQueueElement`のSingleton Dynamic Bufferへ追加し、受付番号はWorld実行中だけ1から単調増加する。0は無効値とし、`ulong.MaxValue`を最後に1回発行した後は周回せず受付を停止する。Commandと受付番号状態は未確定の操作要求なのでセーブせず、ロード後の新しいWorldでは1から開始する。失敗したCommandも番号を再利用しないが、一時停止中、Singleton不整合、番号上限による受付失敗ではEntityも番号も消費しない。

`FarmwareCommandProcessingSystemGroup`は、同じ固定Tickに受付済みのCommandを1件ずつ選び、Validation、検証終端、Commit、ECB再生、結果確定までを完了してから次の受付番号へ進む。機能固有Validation Systemは対象Commandに対して1件だけ存在し、ゲーム状態を変更せず全条件を確認する。成功した場合だけ機能固有Commit SystemがComponent値更新を含む全変更を`SimulationCommandEntityCommandBufferSystem`へ記録し、成功状態まで完了したCommandのECBだけを再生する。Commandごとに直後の再生または破棄を確定するため、Validation／Commit Systemはメインスレッドで完結させ、Command専用ECBへ書くJobをスケジュールしない。担当Validation／Commit Systemの不足、複数Validation担当、不正状態、未完了Commitは共通失敗として拒否する。未完了CommitのECBは`ShouldPlayback`を無効にして再生だけを抑止し、成功時と同じ`EntityCommandBufferSystem`標準処理へ渡してBufferの破棄と専用Allocatorの巻き戻しを必ず行う。前の成功CommandのECBを次のValidation前に再生するため、同一Tick内の競合Commandも直前の確定結果を参照できる。

完了したCommand Entityは成功または失敗結果と処理Tickを現在の描画フレーム中だけ保持し、次の`FarmwareSimulationSystemGroup`先頭で破棄する。同じ描画フレームに複数固定Tickが進んでもPresentationが結果を読み取る機会を失わない。`Farmware.Simulation`のunsafe許可は、Entities公式の`IECBSingleton`パターンでECB System所有の未再生Buffer一覧を登録する箇所だけに限定し、ゲーム側でポインターの寿命や解放を管理しない。

基本順序は次のとおりとする。

```text
Input / UI
    ↓ Command
Command検証
    ↓ 成功時だけCommit
空間索引・物流接続・施設認識のDirty再構築
    ↓
生産・物流・敵・動物の固定Tick
    ↓ Result / Event / Snapshot
Presentation
```

### チャンク生成の作業データと確定結果

`MAP-02D`の非同期公開比較では、`ChunkGenerationSystem`が上限付きの作業枠とJobHandleをWorld寿命中に所有する。各枠のBurst `IJob`は設定・絶対座標の値コピーと専用Native配列だけへアクセスし、ECSへ書き込まないため、ECS Component依存へHandleを登録しない。代わりにSystemが後続更新で`IsCompleted`を確認してから安全性を確定し、地形と資源の両Bufferを公開して`ReadyToApply`へ進める。未完成作業配列はSimulationの確定結果やPresentationの読み取り対象にしない。

Streamingは生成中の保持外Entityも完成まで予算に含め、完成後の次回更新でアンロードする。破棄済みEntityへ古い結果を公開せず、World／System破棄時には全Handleを完了してからPersistent配列を解放する。これはMonoBehaviourへチャンク状態を移す変更ではなく、要求と生成段階は引き続きECSを正とする。作業枠は通常フレームごとに再確保せず、使用した同時生成上限まで再利用する。計測条件と残るリスクは[性能計測](./Map02DPerformanceReport.md)を参照する。

### 岩壁影マスクの表示専用計算

地形Presentationの岩壁影マスクは、確定済み地形Bufferを読み取り専用のNativeArrayとして同期Burst Jobへ借用する。中心64×64はECSの確定値、外周8タイルは同じ整数座標生成規則を用い、Transformや近隣チャンクのロード順に依存しない。出力80×80のbool配列はPresenterがAwakeでPersistent確保し、表示チャンク間で再利用してOnDestroyでDisposeする。Runが呼び出し内で完了するため借用参照やJobHandleをフレーム間保持せず、Simulation状態へ書き戻さない。Presentation Assemblyは導入済みUnity.Burstを直接参照するが、Editor依存やパッケージ追加はない。

### 分類用の外周地形キャッシュ

地形分類はチャンク内部を現在のECS Bufferから読み、外周1セルだけを`TerrainNeighborHaloUtility`の同期Burst Jobでまとめて生成する。260要素のNative配列（現行GeneratedTileの要素本体で4,680 bytes）はPresenterがAwakeでPersistent確保し、OnDestroyでDisposeする。チャンク別・フレーム間キャッシュにはせず、`ProcessApplyQueue`の呼び出し内で同一チャンクの連続反映範囲を共有し、次フレームや別チャンクでは必要範囲を必ず上書きする。これによりWorld再作成、Entity再利用、生成設定変更の古い外周が混入しない。中心の再コピー、ECS書き戻し、未完了Jobは追加しない。外周のint域外は非接続の水域値を使い、境界・岩壁露出・資源の前後判定を維持する。

### チャンク別Tilemapの空領域履歴

`ChunkLocalTilemapPresentation`は9レイヤーごとに16bitの非空サブチャンク履歴を所有する。通常反映は`WriteSubChunk`を通し、今回の256セルが全nullで、かつ以前も空の領域だけ書込を省く。非空→空は実際のSetTilesBlock成功後に履歴を解除する。全消去は従来どおり全設定レイヤーへnullを書き、成功したサブチャンクの履歴を解除するため、16領域の消去後だけ空の状態でプールへ返せる。原点変更ではセルと履歴を一緒に維持し、World／方式切替では表示単位ごと破棄する。内部Tilemapの通常反映を別経路から直接行わないことが前提である。共有Tilemap方式は旧原点消去との寿命が異なるため、この履歴による省略を適用しない。履歴は表示専用であり、Simulationや保存状態の正にしない。

## 9. エンドレス座標とFloating Origin

- Simulationの絶対位置は整数のワールドタイル座標で保持する。Transformのfloat座標を生成、占有、接続、セーブの判定に使用しない。
- タイル座標とチャンク座標は`int2`相当とし、減算、距離、範囲端などオーバーフローし得る中間計算は`long`相当で行う。
- チャンクストリーミングの可視・先読み・保持範囲は、64×64の全ローカルタイルを`int`絶対タイル座標へ復元できる論理チャンク範囲へクランプする。座標域の端で余白を追加しても、反対側の端へ周回したチャンクを生成しない。境界ハイライトや岩壁影がチャンク外を標本化する場合は64bitで絶対座標を検証し、`int`ワールド外のセルを存在しない非接続セルとして扱う。
- Presentationはカメラ周辺の表示原点を絶対座標から差し引いた相対座標へ、Tilemap、Sprite、エフェクトを配置する。
- カメラが表示原点から一定距離を超えた場合は、固定TickのSimulationを変更せず、Presentationの表示原点と表示物だけを移動する。
- 画面入力は相対表示座標から整数の絶対タイル座標へ変換してCommandを作る。
- Floating Originの変更によってワールド生成、施設領域、物流、生産、セーブ結果を変化させない。

`MAP-02D`では、通常移動の相対Camera座標が現在の表示原点から8論理チャンク、512タイルへ達した軸がある場合、絶対Camera位置を保ったまま所属する64×64論理チャンク境界へ表示原点を更新する。現在の既定ONのチャンク別Tilemap方式は、既存表示・部分反映進捗・岩壁影を保って相対位置だけを更新する。診断用に残す共有方式OFFは、新原点側の可視チャンクを先に再構築し、旧原点側の画面外セルを通常予算で後から消去する。明示テレポートでは両方式とも旧表示を即時消去する。512タイル、生成プリロード1チャンク、保持余白1チャンク、表示先行1チャンク、同時生成2、反映・消去合計4サブチャンク/フレーム、空きプール8、速度12／36をMAP-02Dの採用値とする。最終回帰・人間確認・最終コードの限定Player確認からMAP-02Dの今回範囲は完了と判断した。続くMAP-02Eでは、通常移動と同じ相対座標APIで正負を9地点巡回し、複数回の自動原点変更、東西往復、0境界横断、保持範囲外からの3チャンク再訪を一つのPlayModeセッションで検証した。各地点で生成設定と初期スポーンSingletonが不変で、同じ絶対チャンクが重複せず、再生成された地形・資源の代表セルが一致することを確認した。これによりMAP-02A〜Eの今回範囲を完了するが、製品最大Viewport・対象機器での正式性能合格と長時間評価は別課題である。詳細は[最終採用設定の検証と制約](./Map02DPerformanceReport.md#最終採用設定の補助確認とpr完了判断)と[MAP-02Eの継続回帰記録](./Map02DPerformanceReport.md#map-02e-遠距離継続回帰)を参照する。

Presenterは可視範囲と四辺の外側1チャンクまで、生成済みの表示を同じ予算内で先行準備する。ECSの可視Observerと生成・保持要求は広げず、外側で`Inactive`になった確定Bufferも表示へ再利用する。新規予約は可視を先にし、先行範囲外・Entity消失時は従来の分割消去と上限8の空きプールへ戻す。共有方式の原点変更と明示移動の同期再構築に余白を含めず、先行準備は次の通常更新から再開する。通常移動で保持する完成表示は可視矩形の幅・高さをそれぞれ2チャンク増やした有限集合内であり、これに消去途中とプールの寿命を加える。今回の拡大120×67.5タイルでは割当表示最大20単位だった。画面外の分類・書込・影構築と保持メモリの費用、OFFの原点変更後に残る追従遅れは[先行表示の計測](./Map02DPerformanceReport.md#画面外の先行表示と既定on採用)へ記録する。以降の既定OFF・共有方式の再構築記述は比較時点の経緯として読む。

2026-10-17の[Editor一次計測](./Map02DPerformanceReport.md)では、原点変更時の全再構築へ通常表示で約30〜33msが集中した。内訳はTilemap書き込み約14〜16ms、分類約7〜9ms、岩壁影約7msである。縦横4倍では、原点変更時の表示済みチャンク4件で約42〜44ms、8件で約86〜91msとなり、同時表示数へのほぼ比例傾向を確認した。最初の比較実装では、自動原点変更前から存在する同一論理チャンクの岩壁影Meshを、新表示原点基準へGameObjectごと再配置して再利用する。新Viewportで採用されなかった影だけを同フレーム末に破棄し、明示テレポート、通常チャンク完成、World切り替えは従来の再構築・破棄経路を維持する。

同じ表示済みチャンク数での60FPS目標再計測では、通常12が30.791msから26.019ms、高速36が29.825／32.960msから22.083／26.739ms、縦横4倍・8チャンクが90.674／85.782msから68.720／66.571msへ低下した。全5回で影再構築は0ms、位置更新は最大0.012292msだったため、影の再計算除去は実測でも成立した。ただし残るスパイクは通常表示でも22〜27ms、縦横4倍で約67〜69msあり、Tilemap書き込みと分類が次の主対象である。通常フレームの低負荷だけで方式を確定せず、表示を切らさず両処理の再構築量を減らす方式と、製品の最大Viewportで再評価する。計測結果を理由に自動原点変更を無効化したり、製品速度や予算を変更したりはしていない。

続く比較実装では、`ChunkLocalTilemapPresentation`がチャンク内0〜63の9 Tilemapを持ち、自動原点変更で表示済み集合と部分構築番号を維持してXY位置だけを更新する。岩壁影も既存Meshを移動する。消去開始で非表示にし、16サブチャンクの消去完了後だけ上限8の空きプールへ返す。World切替・明示テレポート・方式切替・無効化では表示とプールを破棄する。World Gridは固定し、Simulationには変更を加えない。別Tilemap間の上下順を保つため、現行Orthographic描画では相対チャンク行×1/1024の小さい表示Zを付ける。これは絶対Yによるソートではなく、Camera方向やURP透明ソートを変える場合は再評価する。比較方式の原点変更5回では分類・書込・消去・影構築がすべて0、表示処理全体は0.024334〜0.087750msだった。一方、SetPass増加と原点変更以外の停止が残るため、比較切替は既定OFFであり採用は未確定である。詳細は[比較計測](./Map02DPerformanceReport.md#チャンク単位の地形表示再利用比較実装既定は無効)を参照する。

`MAP-02A`では、表示空間のローカルセル`(0, 0)`に対応する絶対タイル座標を`DisplayOrigin`として表す。`WorldCoordinateConversion`は、Simulationが保持する`int2`の絶対タイル座標と、表示原点からの2軸64bit`RelativeCellCoordinate`、1タイルを1ワールド単位とする`double2`相対ワールド座標を相互変換する。セル左下は整数座標、セル中心は各軸へ0.5を加えた位置とし、相対ワールド座標から所属セルへ戻す場合は正負とも床方向へ丸める。

絶対座標同士の差は`int`のまま減算せず、`long`へ広げてから計算する。逆変換では表示原点を加えた結果が`int`範囲内であることを先に検証し、NaN、無限大、範囲外を失敗として拒否する。このAPIはCamera、Transform、GridLayout、Tilemapへ依存しない純粋計算とし、表示原点の変更がSimulationの絶対座標、地形生成、初期スポーン、状態ハッシュを変更する経路を持たせない。

`MAP-02B`では`CameraPanController`がPresentation寿命中の`DisplayOrigin`を所有する。World GridのTransformは原点へ固定し、Camera、9枚のTilemap、岩壁内部影Meshは表示原点からの相対座標だけを使用する。初期スポーンと明示的な絶対座標移動は所属チャンク境界を表示原点へ選び、Camera Transformへ0〜64付近の値を渡す。Viewportを読む`CameraChunkStreamingBridge`は相対範囲へ現在の表示原点を加算し、ECSの`ChunkStreamingObserver`を絶対チャンク座標に保つ。明示的な絶対座標移動で表示原点が変わった場合、`ChunkTilemapPresenter`は旧相対セルを即時消去し、新しい表示原点で可視チャンクを再構築する。F4のタイル・サブチャンク・チャンク境界は、`int`最大タイルの外周となる最大値+1を含めて64bitで列挙し、座標周回や終了しない描画ループを起こさない。`MAP-02D`では通常移動にも上記の512タイル閾値を接続し、World Gridを動かさず、絶対Observer範囲とSimulation状態を維持したままCameraと全Presentationを新しい相対座標へ再配置する。自動変更の旧Tilemapセルは表示原点を添えて待ち行列へ移し、新原点側を同じフレームに反映した後、通常の4サブチャンク/フレーム予算で消去する。

`MAP-02C`では、F3／F4に加えて開発者用Gameビュー可視化も`CameraPanController`の`DisplayOrigin`へ接続する。CameraとGridから得た相対セルへ表示原点を加えた絶対タイル座標でECSを検索し、検索結果の絶対タイルから表示原点を引いた相対セルへ色面を投影する。正負の遠方表示原点でもViewport範囲、中央タイル、Tilemap、色面が同じ絶対セルを参照する。Map Viewerは独立したEditorWindowで絶対座標を直接扱い、変換対象に含めない。現時点のGameビューには選択・建築ポインター入力が存在しないため、将来の入力はTransformやGridの相対値をSimulationへ直接渡さず、同じ表示原点境界で絶対タイル座標へ戻してからCommandを作る。

Cameraの可視範囲計算は現在のTransformを基準とする。`CameraChunkStreamingBridge`は`ViewportToWorldPoint`の前に`ResetWorldToCameraMatrix()`を呼び、URP Pixel Perfect Cameraが保持する前フレームの描画行列を解除する。原点変更フレームでも、現在の相対Camera座標と同じ表示原点を組み合わせる。描画直前のピクセル整列行列の設定はURPへ委ねる。任意の独自ビュー行列を読み込み範囲の正として扱うCameraは、このBridgeの対象外とする。

## 10. 実装前後の検証要件

- 同一の初期状態、Command列、Tick数から同じ状態ハッシュを得る決定性テストを用意する。
- 同じ保存済み`AnimalPopulationSeed`、地域状態、Tick、入力から同じ初期補充・リスポーン結果を得る一方、同じ地形シードだけでは動物の初期配置を固定しないことを確認する。
- 近距離個別処理だけの結果と、途中で遠距離集計へ切り替えた結果を比較する。
- チャンク境界をまたぐ設備、施設、ベルトを異なるロード順で復元しても、永続ID、占有、接続、在庫が一致することを確認する。
- 建築Commandの失敗、キャンセル、競合時に部分変更が残らないことを確認する。
- セーブ中断を模した場合に、直前の正常なセーブを読み込めることを確認する。
- Floating Originの変更前後で、選択タイル、生成結果、接続、状態ハッシュが一致することを確認する。
- 搬送アイテムと設備を1,000、10,000、100,000件へ増やし、個別処理、集計処理、モード変換のCPU時間、GC Alloc、メモリを同一条件で記録する。

## 11. 後から決める項目

次の項目は本仕様の境界を変えずに調整できるため、現在は確定しない。

- 固定Tickの製品用周波数と1フレームの最大追いつき回数。SIM-01Bの開発用暫定値は10 Tick/秒、最大4 Tickとする。
- ベルト速度、セル容量、設備バッファ容量、生産時間。
- 個別処理と集計処理の切替距離、集計可能判定の最適化範囲。
- Jobの分割数、バッチサイズ、Native Collectionの初期容量。
- 表示原点を移動する距離、カリング範囲、Presentationのプール数。
- セーブファイルの具体的なバイナリ形式、圧縮方式、分割サイズ。
