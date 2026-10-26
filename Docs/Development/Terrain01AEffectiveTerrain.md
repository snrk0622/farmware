# TERRAIN-01A ブロック定義と有効セルの実装記録

- 作業日: 2026-10-19
- ブランチ: `feature/terrain-01a-effective-cells`
- 状態: 実装・AI検証・人間確認完了
- 依存: WORLD-01C、WORLD-01D
- 後続: TERRAIN-01C1〜C8、TERRAIN-01D、SAVE-01、UI-01、PLAYER-01、GATHER-01、BUILD-01

## 1. 目的

自然生成された基礎地形を破壊せず、プレイヤー操作等による疎な変更差分を適用して、移動・建築・保存が共通して参照できる現在の有効セルを得る。固定Biomeと、変更可能なGroundBlock、WallBlock、Liquid、TerrainSurfaceを分離し、同じX/YのSurface、Underwater、DeepSeaを別地点として扱う。

本PRでは採掘Command、液体伝播、本番Blockパレット、変更後Tilemap表示、初期スポーン変更は実装しない。

## 2. 定義境界

共通`DefinitionKind`へ次の固定保存値を末尾追加した。

| 値 | 分類 | 用途 |
| ---: | --- | --- |
| 6 | `GroundBlock` | 掘削・埋立できる地面 |
| 7 | `WallBlock` | 採掘・設置できる壁 |
| 8 | `Liquid` | 淡水、海水、マグマ等 |
| 9 | `TerrainSurface` | 床、舗装、耕地等 |

既存値1〜5は変更・再利用しない。`DefinitionCatalogAuthoring`のBakerは、既存Sceneの鶏定義へ次の組み込み定義を自動追加する。既存Scene YAMLへ手作業でGUIDを追加せず、全Worldで同じ境界を使用する。TERRAIN-01Aでは地形固有の硬度・回収物・通行属性をInspectorから入力する契約をまだ設けないため、`definitions`配列へ地形4分類を手動追加した場合は、地形Blobの件数不一致へ進む前に具体的なBakeエラーとして拒否する。追加BlockのAuthoringはTERRAIN-01C6で共通定義と地形固有値を一対一に入力できる形へ拡張する。

| 組み込み定義 | 分類 | TERRAIN-01Aで確定する属性 |
| --- | --- | --- |
| 通常地面 | GroundBlock | 通常設備・通常ベルトを許可 |
| 自然岩壁 | WallBlock | 移動遮断、水密、施設境界 |
| 淡水 | Liquid | 橋を許可 |
| 海水 | Liquid | 橋を許可 |
| マグマ | Liquid | 橋を許可し耐熱を要求 |

各定義は変更禁止GUID、共通RuntimeId、硬度、回収Item参照、ルール属性を持つ。硬度と回収ItemはGATHER-01、表示参照と追加パレットはTERRAIN-01C6まで未接続であり、現在の0／Invalidをゲームバランスへ流用しない。

## 3. 現行生成からの互換変換（C6-C1で廃止）

以下はTERRAIN-01A実装時点の履歴である。C6-C1では、完成Biome・地質・固定水文から`GeneratedTerrainCell`のGroundBlock、WallBlock、Liquidを直接選ぶ純粋確定処理へ置換した。`BaseTerrainCell`と`EffectiveTerrainResolver.TryCreateBaseCell`は削除され、有効地形差分は新しい生成セルを直接基準にする。C6-C2ではECSチャンクBuffer、ストリーミング寿命、生成署名、状態ハッシュへ接続した。旧`GeneratedTile`利用側の一括削除はC6-C3で完了させる。

TERRAIN-01Aでは既存Surfaceの地形生成結果と署名を変えない。`EffectiveTerrainResolver.TryCreateBaseCell`が、`GeneratedTile`を次のようにBlock区画へ分ける。

| 既存分類 | GroundBlock | WallBlock | Liquid |
| --- | --- | --- | --- |
| Land | 通常地面 | なし | なし |
| RockWall | 通常地面 | 自然岩壁 | なし |
| Water＋FreshWater | なし | なし | 淡水 |
| Water＋SaltWater | なし | なし | 海水 |
| Magma | なし | なし | マグマ |

RockWallを地面＋壁に分けることで、後続の採掘で壁だけを除去し、Biomeと歩行可能な地面を残せる。TERRAIN-01C6で生成器がBlockを直接選ぶまで、旧`GroundType`を解釈するのはこの互換境界だけとする。

## 4. 疎な変更差分

`TerrainChunkDeltaOwner`は`WorldLayer＋論理チャンクX/Y`を保持し、`TerrainCellDelta` Bufferを所有する。ストリーミングで破棄される生成チャンクEntityへ差分を置かないため、表示範囲外へ出ても変更を失わない。SAVE-01はこの所有者とBufferを保存対象にする。

Bufferは0〜4095の`LocalTileIndex`昇順・重複なしとする。変更Maskのbitがある区画はRuntimeIdがInvalidでも明示除去を意味し、bitがなければ自然生成値を維持する。`TerrainCellDeltaUtility.SetOrRemove`は希望地形の分類と区画構成を検証し、自然状態と一致した差分要素を削除する。

有効セルはGround、Wall、Liquid、TerrainSurfaceの順に差分を適用して再構築する。次を破損状態として拒否する。

- 未知の変更bit、範囲外LocalTileIndex。
- Ground区画からLiquid定義を参照する等の分類違い。
- 地面と液体の重複。
- 地面のないセルにTerrainSurfaceだけが存在する状態。
- 差分所有者と問い合わせ座標のLayerまたはチャンクが異なる状態。

Biomeは差分へ含めず、人工地形へ置換しても座標由来の地域情報を維持する。自然資源・植生と配置物は別状態のままとし、有効セルへ含めない。

## 5. 問い合わせと状態ハッシュ

`TerrainTraversalRules`は壁、穴、液体、耐熱能力を評価する。`TerrainPlacementRules`の有効セル版は通常設備、通常ベルト、橋の地形条件だけを評価する。例えば水域は泳げる主体なら移動可能だが、通常設備は置けず橋を置ける。自然資源、配置物、所持品、到達距離は後続Commandが別途検証する。

`EffectiveTerrainCellQuery.TryResolveFromChunk`は、チャンクEntityの行優先4096件`GeneratedTerrainCell` Bufferから絶対座標に対応する自然セルを読み、任意の永続差分所有者を適用する。チャンク座標、Layer、Buffer件数、差分Ownerの対応が不正なら成功値を返さない。

`TerrainStateHasher`は、地形定義属性をRuntimeId順、18Biomeと8地質の自然Blockパレットを安定ID・用途・候補順、差分所有者をLayer・チャンクX・チャンクY順、各BufferをLocalTileIndex順で追加する。Blobアドレス、Entity作成順、チャンク読込順、表示キャッシュは含めない。同じ差分集合はEntity作成順が異なっても一致し、同じX/YでもLayerが異なれば別の状態になる。パレットの行、連続Range、重み合計256、Ground／Wall分類を検証し、`TerrainChunkDeltaOwner`と`TerrainCellDelta` Bufferは独立したQueryで件数と対応Entityを確認する。片方だけが残った不完全な永続状態を「差分なし」として見逃さず拒否する。

チャンク生成署名は、旧`GeneratedTile`、固定水文、表流水、自然水文に加えて、ECS公開と同じ確定器で得た4096件の`GeneratedTerrainCell`を行優先で混合する。固定パレットまたは組み込みLiquid IDが異なる生成結果を、同じ署名として扱わない。

## 6. 表示・Editorツールへの影響

既存`GeneratedTile` BufferとTilemap参照はC6-C3まで維持する。C6-C2では`ChunkGenerationSystem`の再利用枠へ4096件の`GeneratedTerrainCell`を追加し、旧地形、Blockセル、資源、固定水文、表流水、自然水文の6 Bufferを同じ完成境界で一括公開する。`ChunkStreamingSystem`と初期スポーン探索が作るチャンクEntityも空の新Bufferを所有し、生成中アンロードや枠再利用で寿命を分離しない。Gameビュー、Map Viewer、分布監査、岩壁影、Haloの読み取り先はC6-C3・C6-Dで切り替える。

Editorメニューやキー割り当ては追加・変更しない。`Terrain Placement`は探索・建築に必要な通常設備、橋、耐熱橋の判定へ絞り、補助物流であるベルト固有のHUD項目と凡例は表示しない。設定SubSceneのBaker結果には、既存の共通定義・動物定義に加えて`TerrainBlockCatalog`が存在する。

## 7. 自動検証

AI確認結果:

- Unity 6000.3.8f1、Entities 1.5.0でスクリプトコンパイル成功。
- `EffectiveTerrainCellTests`: 13件成功。
- 組み込み定義、岩壁除去、淡水変換、矛盾拒否、疎な差分の整列・削除、Layer分離、移動・建築の独立、耐熱条件、状態ハッシュの作成順非依存・Layer差・組み込みID対応破損の拒否、差分Owner／Bufferの片方だけが残る不完全状態の拒否を確認。
- Inspector由来の地形4分類をTERRAIN-01C6まで拒否するBakerテスト: 4ケース成功。
- 全EditMode: 390件中388件成功、2件スキップ、失敗0件。
- 全PlayMode: 直前の全体確認で56件成功、失敗・スキップ0件。今回のレビュー修正はBakerと状態ハッシュのEditMode対象であり、修正後の全PlayModeは未実施。
- `git diff --check`: エラーなし。

スキップされた2件は通常実行から明示的に分離された既存の負荷テストであり、本変更による失敗ではない。Unity Pipelineによる接続中Editor操作は利用できなかったため、起動中Unity EditorのTest Runnerで全EditModeを実行し、コンパイル、Baker、状態ハッシュを検証した。

人間確認後の指摘により、`Terrain Placement`からベルト固有の色判定、中央HUD、凡例を除去した。起動中EditorのTundra再コンパイルとAssembly Reloadは成功し、今回変更したスクリプトのコンパイルエラーはない。修正後の表示も人間確認済み。Editorがプロジェクトを開いておりPipeline接続も到達不能だったため、この表示調整後の全PlayMode再実行はできていない。

## 8. 人間確認

人間確認完了。SampleSceneでSurface地形、資源、鶏、F3／F4、開発者可視化、Consoleに問題がないことを確認した。`Terrain Placement`は中央HUDに`Belt`がなく、凡例が`Equipment`、`Any Bridge`、`Heat Only`、`Blocked`の4分類であることも確認済み。変更差分の画面表示はTERRAIN-01C6まで未実装であり、今回の確認対象には含めていない。

## 9. 未実装・残る境界

- バイオーム構成と安定ID方針はTERRAIN-01Bで採用済み。生成への段階反映と総合調整はTERRAIN-01C1〜C8。
- Underwater／DeepSeaの自然Block生成と表示はTERRAIN-01C6。
- Biome別の複数Ground／Wallパレットと表示参照はTERRAIN-01C6。
- 新初期スポーン規則はTERRAIN-01D。
- 地形差分のファイル保存・復元はSAVE-01。
- プレイヤー移動への接続はPLAYER-01。
- 採掘、硬度、回収Item、道具耐久はGATHER-01。
- 地面・壁で共通の材質ItemとGround／Wall両形態を結ぶ対応、配置先による形態決定はTERRAIN-01C6とGATHER-01B。
- 地形Blockとは別の`GeologyDeposit`、Ground／Wall Host、採掘済み差分、大型鉱床はTERRAIN-01C8、GATHER-01C、AUTO-01D。
- 配置物占有、材料精算、橋はBUILD-01。
- 液体伝播と反応はTERRAIN-02。
