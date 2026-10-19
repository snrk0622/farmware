# WORLD-01C レイヤー付き座標の実装記録

- 対象PR: WORLD-01C
- 状態: 実装済み
- 前提: [WORLD-01B 多層水域の製品契約](./World01BMultiLayerWaterContract.md)
- 後続: WORLD-01D、TERRAIN-01A/B、SAVE-01B

## 1. 到達点

固定保存IDを持つ`WorldLayer`を、既存のセル・チャンク・生成要求・動物地域・個体位置・表示索引・診断へ接続した。同じX/YでもLayerが異なれば別の論理位置として比較、ハッシュ、検索する。

既存の自然地形生成は`Surface`だけで従来結果を維持する。ここで`Surface`は陸地だけではなく地上と水面の最上層を意味するため、現在の海・川・池もSurfaceの水域セルとして正しい。同じX/Yの水面下は別の`Underwater`セルであり、`Underwater`と`DeepSea`へ地上Generatorを流用せず、Map Viewerと開発者可視化では選択・座標表示だけを可能にし、自然生成未接続または空表示とする。3層の自然地形、Block、Liquidの生成はTERRAIN-01A/Bで実装する。

## 2. 座標とECSデータ

- `WorldTileCoordinate`は`(WorldLayer, TileX, TileY)`を保持し、等価比較と実行時ハッシュへLayerを含める。
- `ChunkCoordinate`は`(WorldLayer, ChunkX, ChunkY)`を保持する。既存の2引数コンストラクターと`WorldGrid.ToChunkCoordinate(x, y)`は互換入口として`Surface`を返す。
- `ChunkStreamingObserver`、`ChunkCoordinateComponent`、`InitialSpawnLocation`へLayerを追加した。未設定の旧Observerは共有処理で`Surface`へ正規化し、ECS Singletonへ書き戻してSimulationとPresentationの検索キーを一致させる。
- Camera Bridgeは所有中Observerの現在Layerを可視矩形の再計算前に取り込み、カメラ移動やFloating Origin更新でSurfaceへ戻さない。
- チャンク要求、生成結果の公開、Tilemap Presenterの可視判定はLayerを含む。異なるLayerの同X/Y Entityを既存要求として再利用しない。
- `Underwater`または`DeepSea`へ切り替えた場合も旧Surfaceチャンクの寿命処理を続ける。実行中Jobは完了まで所有Entityを保持し、公開後の次回更新で解放する。自然生成が未接続のレイヤーへ新規要求は作らない。
- Tilemap PresenterはObserverのLayer変更を検出したフレームで旧レイヤーの表示、途中Queue、岩壁影を即時消去する。通常のサブチャンク予算へ残像の消去を委ねず、異なるレイヤーを同時表示しない。
- `AnimalPopulationRegionState`、`AnimalSpawnCandidate`、`AnimalTilePosition`へLayerを追加した。地域索引は`(Layer, RegionX, RegionY)`、位置キャッシュは選択LayerとX/Yで絞り込む。
- 永続IDの安定順キーへ`ScopeOrder`を追加し、動物候補ではLayerを最優先キーにする。既存利用側の既定値0は従来順を維持する。

## 3. 決定論と互換性

Surfaceの地域乱数初期化はWORLD-01C以前と同じ入力列を維持する。別LayerだけがLayer値を混ぜた独立系列を使うため、既存地上個体群の初期配置を変更しない。

動物個体群の状態ハッシュは地域と個体位置へLayerを追加した。現在は初回リリース前で永続化した比較基準がないため、`SimulationStateHashSchema.CurrentVersion`は1のまま定義を更新する。初回リリース後、または保存済みハッシュを異なるビルド間で比較する運用を始めた後は、入力形式を変更するたびにVersionを増やし、異なるVersionの結果を比較しない。

## 4. 診断とEditorツール

- F3は現在の`ChunkStreamingObserver.Layer`を表示し、同じLayerの生成済みBiomeだけを読む。Observerがない旧Scene・テストではSurfaceへ戻す。
- F4は現在Layerと異なる初期スポーンマーカーを描かない。
- `Farmware > World Generation > Debug Visualization > Layer`からSurface、Underwater、Deep Seaを選べる。別層に生成済みBufferがなければ空表示になる。
- Map ViewerへLayer選択を追加した。Surfaceは従来Generator結果を表示し、Underwater／Deep Seaは暗色の未接続表示と明示メッセージを返す。

## 5. 検証契約

- 純粋計算: 同じX/Yの`WorldTileCoordinate`と`ChunkCoordinate`がLayerごとに別値となり、負座標の床方向除算を維持する。
- ECS: 同じチャンクX/Yの別Layerを要求済みと誤認しない。地域索引、永続ID順、状態ハッシュへLayerを含める。
- Presentation: 動物位置キャッシュ、Tilemap、F3/F4、開発者可視化、Map Viewerが選択Layer以外を混ぜない。
- 回帰: Surfaceの地形生成、初期スポーン、Floating Origin、既存動物個体群の決定性を維持する。

## 6. 今回含めないもの

- Underwater／DeepSeaの自然地形・資源・動物生成。
- GroundBlock、WallBlock、Liquid、変更差分を適用した有効セル。
- プレイヤーの潜水、上下移動、酸素・水圧、下層プレビュー。
- WorldId／PlayerIdとCommand操作主体。
- Layer付きセーブファイルの書き込み・復元。

これらはロードマップのTERRAIN-01、DIVE-01、WORLD-01D、SAVE-01で追加する。
