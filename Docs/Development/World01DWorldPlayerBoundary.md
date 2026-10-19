# WORLD-01D 操作主体とワールド境界の実装記録

- 対象PR: WORLD-01D
- 状態: 実装済み
- 前提: [WORLD-01C レイヤー付き座標](./World01CLayeredCoordinates.md)
- 後続: TERRAIN-01A、SAVE-01、PLAYER-01、TIME-01

## 1. 到達点

ワールド共有状態とPlayerId別状態を分ける共通境界を追加し、Inputが暗黙の全局プレイヤーではなく、`WorldId`と`PlayerId`の組をCommand操作主体として渡せるようにした。初回はローカル1人だけを作るが、検証処理はPlayer Entity一覧の無効値と重複を拒否し、任意の1件を選ばない。

`WorldId`は128bit、`PlayerId`はワールド内`ulong`とし、どちらも0を無効値として扱う。セーブ基盤が未実装の現在は、既存SampleScene用に固定の`WorldId.DevelopmentDefault`と`PlayerId = 1`を初期化する。SAVE-01では製品ワールド作成時に別のWorldIdを発行・保存し、開発用固定IDの移行方針を明示する。

## 2. 状態の責務

| 状態 | ECS表現 | 保存・変更主体 |
| --- | --- | --- |
| 現在の製品ワールド | `WorldSessionIdentity` Singleton | SAVE-01のワールドヘッダーへ保存し、World読込時に設定する。実行途中では変更しない |
| プレイヤー別進行の所有者 | `PlayerIdentity`を持つPlayer Entity | PlayerId別に保存する。所持品、研究、健康、位置等は後続PRで同じEntityへ追加する |
| この端末が操作するPlayer | `LocalPlayerContext` Singleton | 保存対象外のセッション状態。Inputが明示的なCommand主体を作るためだけに使用する |
| ワールド共有状態 | 固定Tick、地形、個体群、設備等のSingleton／Entity | Player Entityへ複製しない。必要な所有関係だけPlayerIdまたは永続IDで参照する |

`WorldSessionInitializationSystem`は3種類がすべて欠けた開発用Worldだけを初期化する。保存読込等が一部だけ作成した状態や重複状態を固定値で補修せず、共通検証と状態ハッシュが不整合として拒否する。

## 3. Command操作主体

`SimulationCommand`へ`WorldCommandActor`を追加した。`SimulationCommandQueue.TryEnqueue`はCommand Entityや受付番号を作る前に`WorldSessionAuthority`を呼び、次を検証する。

- WorldIdとPlayerIdが有効である。
- `WorldSessionIdentity`が1件だけ存在し、CommandのWorldIdと一致する。
- `PlayerIdentity`が1件以上存在し、全IDが有効かつ重複しない。
- CommandのPlayerIdが現在Worldへ登録されている。

別ワールドへ切り替えた直後に古いUI入力が残っても、WorldId不一致として状態変更前に拒否する。不正主体ではCommand Entityも受付番号も消費しない。到達距離、所持品、地形、占有等の機能固有条件は、後続のValidation Systemが同じ主体を起点に検証する。

受付後から固定Tick開始前にWorld読込やPlayer削除が行われる場合もあるため、QueueからCommandを選択してActiveにする直前に保存済み主体を再検証する。現在セッションと一致しなくなったCommandは機能固有Validationへ渡さず、`ActorIsNoLongerAuthorized`で失敗確定して新しいワールド状態を変更しない。

## 4. 停止・睡眠・関心範囲

停止は`SimulationPauseControl.TrySetPaused`へ集約した。固定Tick停止中はCommand Queue自体が進まないため、停止と再開だけはQueue外で同じWorld／Player主体を検証して即時反映する。通常のゲーム状態変更Commandは従来どおり停止中に受け付けず、受付番号も消費しない。

睡眠はTIME-01で`WorldClock`だけを変更するCommandとして実装し、今回追加した`WorldCommandActor`と`WorldSessionAuthority`を再利用する。WORLD-01DではWorldClock、睡眠条件、時刻ジャンプを実装しない。

Simulationの関心範囲はPLAYER-01以降でPlayerId、Layer、論理位置から供給し、表示用の`ChunkStreamingObserver`とは分離する。カメラ移動や開発者表示だけで生態、図鑑、遠隔設備のSimulation対象を変更しない。歩行速度、関心距離、車両先読み等は各試作前に固定しない。

## 5. 決定論と状態ハッシュ

共通状態ハッシュのセクション4へWorldIdとPlayerId一覧を追加した。Player Entityの作成順に依存しないようPlayerId昇順へ正規化し、0件、無効値、重複を計算失敗にする。`LocalPlayerContext`は端末固有で保存対象外のためハッシュへ含めない。

初回リリース前で永続化した比較基準がないため、`SimulationStateHashSchema.CurrentVersion`は1のまま定義を更新した。初回リリース後、または保存済みハッシュをビルド間比較する運用開始後は、入力形式変更時にVersionを増やす。

## 6. 検証契約

- GUID表記を同じWorldIdへ正規化し、不正文字列と全0を拒否する。
- 開発用WorldへWorldId、Player 1、Local Contextを各1件だけ作る。
- 外部準備された部分状態を推測で上書きしない。
- 別WorldId、未登録PlayerId、重複PlayerIdを状態変更前に拒否する。
- 正規主体は停止と再開を行え、停止中でも再開経路を失わない。
- 不正主体のCommandはEntityと受付番号を消費しない。
- 受付後にWorldIdまたはPlayer登録が変わったCommandは、処理開始直前の再検証で拒否する。
- Player Entity作成順を変えても状態ハッシュが一致し、WorldId／PlayerId変更を検出する。
- WORLD-01CまでのSurface生成、Floating Origin、既存個体群、表示回帰を維持する。

## 7. 今回含めないもの

- セーブファイルへのWorldId／PlayerId書込と復元、複数ワールドUI。
- プレイヤー位置、移動、衝突、到達距離、所持品、研究、健康。
- WorldClock、睡眠条件、睡眠時刻ジャンプ。
- Simulation関心距離、遠距離集計、車両の速度別先読み。
- Netcode、ロビー、通信同期、予測補正、ホスト移行。

これらはロードマップのSAVE-01、PLAYER-01、ITEM-01、TIME-01、ECO-01、VEHICLE-01、LOG-01で段階的に追加する。
