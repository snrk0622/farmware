# Farmware シミュレーション技術仕様 v0.1

- 作成日: 2026-10-14
- 最終更新日: 2026-10-16
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

`FarmwareCommandProcessingSystemGroup`は、同じ固定Tickに受付済みのCommandを1件ずつ選び、Validation、検証終端、Commit、ECB再生、結果確定までを完了してから次の受付番号へ進む。機能固有Validation Systemは対象Commandに対して1件だけ存在し、ゲーム状態を変更せず全条件を確認する。成功した場合だけ機能固有Commit SystemがComponent値更新を含む全変更を`SimulationCommandEntityCommandBufferSystem`へ記録し、成功状態まで完了したCommandのECBだけを再生する。Commandごとに直後の再生または破棄を確定するため、Validation／Commit Systemはメインスレッドで完結させ、Command専用ECBへ書くJobをスケジュールしない。担当Validation／Commit Systemの不足、複数Validation担当、不正状態、未完了Commitは共通失敗として拒否し、未完了Commitの記録済みECBは再生せず破棄する。前の成功CommandのECBを次のValidation前に再生するため、同一Tick内の競合Commandも直前の確定結果を参照できる。

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

## 9. エンドレス座標とFloating Origin

- Simulationの絶対位置は整数のワールドタイル座標で保持する。Transformのfloat座標を生成、占有、接続、セーブの判定に使用しない。
- タイル座標とチャンク座標は`int2`相当とし、減算、距離、範囲端などオーバーフローし得る中間計算は`long`相当で行う。
- Presentationはカメラ周辺の表示原点を絶対座標から差し引いた相対座標へ、Tilemap、Sprite、エフェクトを配置する。
- カメラが表示原点から一定距離を超えた場合は、固定TickのSimulationを変更せず、Presentationの表示原点と表示物だけを移動する。
- 画面入力は相対表示座標から整数の絶対タイル座標へ変換してCommandを作る。
- Floating Originの変更によってワールド生成、施設領域、物流、生産、セーブ結果を変化させない。

表示原点を移動する具体的な距離とTilemap再配置予算は、最大ズームアウトと高速パンの計測後に決定する。

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
