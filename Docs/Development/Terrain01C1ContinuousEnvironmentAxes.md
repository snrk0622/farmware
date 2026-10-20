# TERRAIN-01C1 連続環境軸 実装記録

- 更新日: 2026-10-20
- 対象PR: TERRAIN-01C1
- 状態: 実装・AI確認・人間によるUnity Editor表示確認完了

## 1. この変更単位の範囲

TERRAIN-01Bで採用した正仕様から、最終18バイオーム分類より前に必要な連続環境軸だけを実装する。地質、水文、地下水、最終Biome、18Biome用Blockパレット、資源鉱床は後続のTERRAIN-01C2〜C7へ残す。移行期間中は現在の7Biome生成を維持し、新しい環境値を同じ`GeneratedTile`へ併記する。

変更量を小さく確認するため、同じPR内を次の2区分で扱う。

- C1-A: 純粋生成関数、固定設定、用途別Salt、ECS Buffer、状態ハッシュ、座標範囲外の失敗通知、EditModeテスト。
- C1-B: GameビューのDebug Visualization、Map Viewer、凡例、中央・Hover情報、表示テスト、旧診断モードの整理。

## 2. 生成する固定値

全値はSeed、GeneratorVersion、保存対象となる生成設定、絶対整数座標から決定論的に生成し、`ushort`の0〜65,535を正とする。

| 値 | 実装契約 |
| --- | --- |
| 基礎気温 | 既存のBiome Warp後座標とTemperature用Saltで生成する。旧7Biome互換の`GeneratedTile.Temperature`へ保持する |
| 地形気温 | 海面より上の標高だけ、最高標高で最大32,768になる寒冷補正を64bit整数式で一度だけ引き、0へ飽和する |
| 基礎湿度 | 既存のBiome Warp後座標とMoisture用Saltで生成し、水域・現在土壌水分を合成しない |
| 標高区分 | 海面下、低地、平地／台地、山地、高山の5区分。水域・マグマ・岩壁でも元区分を保持する |
| 木本植生ポテンシャル | Warp後座標、192／96／48タイル、4：2：1、専用Salt。配置済み植生と基礎湿度を入力へ戻さない |
| 地形起伏 | 中心、上下左右16タイル、斜め11タイルの9標高から最大差を求め、16,384以上を65,535へ飽和する |
| 海洋影響 | 8方向×16／32／64／128タイルの標高だけから海への近接度と開放方向数を1：3で合成する。海面下セルは65,535とする |
| 火山活動度 | Warp後座標、256／128／64タイル、4：2：1、専用Salt。旧`Weirdness`と乱数列を共有しない |

気温は12,000／23,000／32,768／40,000、湿度は16,000／28,500／39,500／50,000で5段階へ分類する。木本植生・起伏・海洋影響・火山活動度は13,107／26,214／39,321／52,428を共通の表示・監査境界にするが、生成式とSaltは共有しない。

## 3. 既存生成との移行境界

- 現在の7Biome、Soil、水域、マグマ、岩壁、資源配置はこのPRでは置き換えない。
- 旧7Biomeだけが参照する`Weirdness`はTERRAIN-01C5まで内部互換値として残し、新しい`VolcanicActivityPotential`とは別に状態ハッシュへ含める。
- 連続環境設定が不正、Warp後座標または固定半径サンプルが`int`範囲外の場合は、`TryGenerateTile`が`false`を返す。チャンクJobは一部だけ完成したBufferを公開せず`Failed`へ進める。
- 地形気温、標高区分、木本植生、起伏、海洋影響、火山活動度を`GeneratedTile`へ保持し、表示側はECS Bufferを読み取るだけにする。

## 4. 開発者用可視化の整理

Gameビューの`Debug Visualization > Environment`とMap Viewerへ、標高、基礎気温、地形気温、基礎湿度、木本植生ポテンシャル、地形起伏、海洋影響、火山活動度を追加した。中央タイルHUDとMap ViewerのHoverには全軸値と標高区分を表示する。

新方式で診断根拠にならない次の旧ツールは削除した。

- `Weirdness`: 旧7Biome互換値の専用色表示。新しい火山診断は`Volcanic Activity Potential`を使う。
- `Biome Transition`: 7Biomeの代表Soilとの差だけを移行帯とみなす旧判定。採用済み仕様は第三Biomeや代表土壌による強制移行を行わず、TERRAIN-01C5で環境差と選択理由を監査する。

旧`Weirdness`そのものは7Biome互換生成が使用中なので削除せず、中央HUDでは`LegacyWeirdness`と明示する。

## 5. 検証範囲

自動確認は、整数式の端点、全段階境界、正負座標・チャンク境界、同入力の再現、湿度と他軸の分離、用途別Salt、設定不正・座標端の明示失敗、状態ハッシュ、GameビューとMap Viewerの値域両端色を対象とする。既存の決定論的チャンク、PlayModeの生成・表示・遠方座標回帰も合わせて実行する。

AI確認結果:

- Unity 6000.3.8f1でTundraコンパイルとAssembly Reloadが成功し、今回変更したC#のコンパイルエラーはない。
- Map Viewer起動時、`byte`基底の表示モードを調べる`Enum.IsDefined`へDropdownの`int` Indexを直接渡し、`ArgumentException`になる問題を人間確認で検出した。Indexを範囲確認後に表示モード型へ変換する純粋関数へ修正し、定義値、未選択、`byte`範囲外を対象とする回帰テストを追加した。
- 続く空表示の報告をUnity画面で調べ、未確定の`NaN`寸法から作られた1×1画像の全面伸張と、広域画像の生成待ちを確認した。Canvasの有限値・最小寸法を生成開始条件にし、初回地図領域と下部に完了割合を表示する。Hover欄は3行分を初回から確保し、値表示の追加でCanvasが縮んで二度生成されることも防ぐ。
- 上記修正前の`Farmware.WorldGeneration.EditModeTests`: 310件成功、失敗・スキップ・未確定0件。所要119.31秒。今回のMap Viewer修正後は、Unity 6000.3.8f1のTest Runnerで`WorldGenerationMapViewerTests`を実行し、追加分を含む47件すべて成功、失敗・スキップ・未確定0件、所要0.064秒を結果XMLでも確認した。全Assemblyの再実行はしていない。
- Unity CLIのPipelineはサンドボックス外からも到達できなかったが、開いているEditorの画面操作で修正をImportし、初回の進捗表示から地図完成への切替を確認した。Consoleは警告0件・エラー0件だった。地図完成画面の表示値は、既定Seed `Farmware`、Surface、中心(0, 0)、Terrain、768×392サンプル、4 tiles/sampleで4.56秒だった。これは画面上の生成経過時間であり、ProfilerによるCPU・GC計測や性能合格判定ではない。全表示モードの手動切替と連続キャンセル操作は人間確認へ残す。
- `WorldGenerationScenePlayModeTests`の開発者可視化、SampleScene生成・決定論、繰り返しFloating Origin・負座標通過の3対象は各1件成功。
- `Farmware.WorldGeneration.PlayModeTests`全体実行中にUnity Pipeline接続が失われ、結果を回収できなかった。Unity 6000.3.8f1で同じプロジェクトを再起動したが、90秒以上待ってもPipelineが到達可能にならなかったため、全PlayModeは未検証事項として残す。接続消失前に上記3対象が成功したことは個別結果で確認済みである。
- `GeneratedTile`は18 bytesから28 bytesへ増えた。生成作業配列と分類用外周配列の現行容量は[性能計測](./Map02DPerformanceReport.md#terrain-01c1後の生成配列容量)へ追記した。
- `git diff --check`はエラーなし。リポジトリ内の全22文書を確認し、生成契約、Editorツール、テスト、ロードマップ、アーキテクチャ、性能容量、索引へ影響する文書を同時更新した。アート寸法・Import設定・制作手順、既存性能ゲート、WORLD-01実装記録、Codexスキルは変更していないため更新不要と判断した。

人間確認では、SampleSceneのPlayModeで8つの連続環境表示を順に切り替え、凡例、中央HUD、色面、正負遠方のTilemapとの一致、`Off`復帰を確認する。Map Viewerでは起動時に例外が再発しないこと、同じSeed・座標で各軸表示とHover値が一致すること、旧`Weirdness`と`Biome Transition`がメニューに残っていないことを確認する。

41点の標高再評価を含む新軸生成は既存より重い。TERRAIN-01C1では正しさと有限作業量を確定し、製品最大Viewport・対象機器・合格FPSが未決定のため独自の性能合格値は設定しない。後続でキャッシュやバッチ化を行う場合も、同じSeed・設定・座標の結果を変えない。
