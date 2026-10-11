// このファイルは、ECSで生成済みのチャンクデータを16×16サブチャンク単位でTilemapへ反映・消去します。
// Tilemapは表示専用キャッシュであり、建築可否やバイオームなどのゲーム状態はGeneratedTileを正として参照します。

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// カメラに見える生成済みチャンクだけを、共有されたフレーム予算内でTilemapへ反映します。
    /// 画面外またはアンロード済みチャンクも同じ単位で消去し、4096タイル一括更新による停止を避けます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChunkTilemapPresenter : MonoBehaviour
    {
        // ProfilerでTilemapの収集・反映・消去に使ったCPU時間をまとめて確認する計測区間です。
        private static readonly ProfilerMarker PresentationProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.TilemapPresentation");

        // 地形だけを描画する専用Tilemapです。このComponentは担当範囲のセルを書き換えます。
        [Tooltip("生成地形を描画する専用Tilemapです。このComponentが表示・消去するため、手描きタイルとは分けてください。")]
        [SerializeField] private Tilemap targetTilemap;

        // 草原の通常地面へ使用する表示専用Tileです。ゲームルール判定には使いません。
        [Tooltip("草原バイオームの通常地面に表示するTileです。未指定の場合、そのセルは空表示になります。")]
        [SerializeField] private TileBase grasslandTile;

        // 森林の植生または通常地面へ使用する表示専用Tileです。
        [Tooltip("森林バイオームの地面に表示するTileです。未指定の場合、そのセルは空表示になります。")]
        [SerializeField] private TileBase forestTile;

        // 砂漠の砂地または通常地面へ使用する表示専用Tileです。
        [Tooltip("砂漠バイオームの地面に表示するTileです。未指定の場合、そのセルは空表示になります。")]
        [SerializeField] private TileBase desertTile;

        // GroundType.Waterへ使用する表示専用Tileです。
        [Tooltip("水域に表示するTileです。水域のリンク通過可否はECSデータで判定し、この参照には依存しません。")]
        [SerializeField] private TileBase waterTile;

        // GroundType.RockWallへ使用する表示専用Tileです。
        [Tooltip("岩壁に表示するTileです。岩壁の建築・リンク遮断判定はECSデータで行います。")]
        [SerializeField] private TileBase rockWallTile;

        // World再作成を検知し、EntityQueryを作り直すために保持する現在のDefault Worldです。
        private World presentationWorld;

        // 座標、状態、タイルBufferを持つチャンクだけを取得する、World寿命中再利用するQueryです。
        private EntityQuery chunkQuery;

        // カメラが要求する可視矩形を取得するSingleton Queryです。
        private EntityQuery observerQuery;

        // AuthoringからBakeされたTilemap適用予算を取得するSingleton Queryです。
        private EntityQuery settingsQuery;

        // Queryを現在のWorld向けに作成済みかを示し、未初期化のEntityQueryを使用しないために保持します。
        private bool queriesAreCreated;

        // 反映途中チャンクのEntityと次のサブチャンク番号を座標別に保持するPresentation専用キャッシュです。
        private readonly Dictionary<ChunkCoordinate, ChunkApplyProgress> applyProgressByCoordinate =
            new Dictionary<ChunkCoordinate, ChunkApplyProgress>();

        // 反映を開始した順番を保ち、1チャンクだけを毎フレーム最初から探し直さないための待ち行列です。
        private readonly Queue<ChunkCoordinate> applyQueue = new Queue<ChunkCoordinate>();

        // 16サブチャンクすべてをTilemapへ反映済みの座標です。Simulationの正データではありません。
        private readonly HashSet<ChunkCoordinate> displayedCoordinates = new HashSet<ChunkCoordinate>();

        // 画面外またはEntity消失により消去が必要な座標を、重複なく保持します。
        private readonly HashSet<ChunkCoordinate> pendingClearCoordinates = new HashSet<ChunkCoordinate>();

        // 現フレームのQueryに存在した座標を再利用し、アンロード差分検出で毎フレームHashSetを確保しないようにします。
        private readonly HashSet<ChunkCoordinate> seenCoordinates = new HashSet<ChunkCoordinate>();

        // 消去を要求した順番と途中位置を保持し、フレーム予算を超えないよう分割処理します。
        private readonly Queue<ChunkClearProgress> clearQueue = new Queue<ChunkClearProgress>();

        // DictionaryとHashSetを走査中に変更しないため、削除対象を一時退避する再利用Listです。
        private readonly List<ChunkCoordinate> coordinateScratch = new List<ChunkCoordinate>();

        // SetTilesBlockへ渡す256要素の再利用配列です。サブチャンクごとのmanaged allocationを防ぎます。
        private TileBase[] subChunkTileBuffer;

        // 消去用のnullだけを持つ256要素配列です。各フレームで作り直しません。
        private TileBase[] emptySubChunkTileBuffer;

        /// <summary>
        /// Editor構築ツールまたはテストから、専用Tilemapと地形分類ごとの表示Tileをまとめて設定します。
        /// ゲームルールは変更せず、GeneratedTileをどのTileBaseで見せるかだけを更新します。
        /// </summary>
        /// <param name="tilemap">生成地形だけを描画する専用Tilemapです。</param>
        /// <param name="grassland">草原バイオームの通常地面に表示するTileです。</param>
        /// <param name="forest">森林バイオームの地面に表示するTileです。</param>
        /// <param name="desert">砂漠バイオームの地面に表示するTileです。</param>
        /// <param name="water">GroundType.Waterに表示するTileです。</param>
        /// <param name="rockWall">GroundType.RockWallに表示するTileです。</param>
        public void Configure(
            Tilemap tilemap,
            TileBase grassland,
            TileBase forest,
            TileBase desert,
            TileBase water,
            TileBase rockWall)
        {
            targetTilemap = tilemap;
            grasslandTile = grassland;
            forestTile = forest;
            desertTile = desert;
            waterTile = water;
            rockWallTile = rockWall;
        }

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、16×16タイルの再利用配列を1回だけ確保します。
        /// </summary>
        private void Awake()
        {
            int tileCount = WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize;
            subChunkTileBuffer = new TileBase[tileCount];
            emptySubChunkTileBuffer = new TileBase[tileCount];
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、表示対象の収集と予算分割された反映・消去を実行します。
        /// </summary>
        private void Update()
        {
            using (PresentationProfilerMarker.Auto())
            {
                if (targetTilemap == null || !TryEnsureQueries())
                {
                    return;
                }

                if (observerQuery.CalculateEntityCount() != 1)
                {
                    return;
                }

                ChunkStreamingObserver observer = observerQuery.GetSingleton<ChunkStreamingObserver>();
                CollectPresentationWork(observer);

                int remainingBudget = GetSubChunkBudget();
                remainingBudget = ProcessClearQueue(remainingBudget);
                ProcessApplyQueue(remainingBudget);
            }
        }

        /// <summary>
        /// Componentが無効化されたときにUnityから呼ばれ、このComponentが表示したタイルだけを即時消去します。
        /// 再有効化時にはActiveなECSデータから再構築できるため、Tilemapを状態保存には使用しません。
        /// </summary>
        private void OnDisable()
        {
            if (targetTilemap != null)
            {
                coordinateScratch.Clear();
                foreach (ChunkCoordinate coordinate in displayedCoordinates)
                {
                    coordinateScratch.Add(coordinate);
                }

                foreach (ChunkCoordinate coordinate in applyProgressByCoordinate.Keys)
                {
                    if (!coordinateScratch.Contains(coordinate))
                    {
                        coordinateScratch.Add(coordinate);
                    }
                }

                for (int index = 0; index < coordinateScratch.Count; index++)
                {
                    ClearWholeChunkImmediately(coordinateScratch[index]);
                }
            }

            ResetPresentationCaches();
        }

        /// <summary>
        /// Component破棄時にUnityから呼ばれ、明示作成したEntityQueryを解放します。
        /// </summary>
        private void OnDestroy()
        {
            DisposeQueries();
        }

        /// <summary>
        /// Default World向けQueryを用意し、Play Mode終了やWorld再作成時には古いQueryを破棄します。
        /// </summary>
        /// <returns>有効なWorldとQueryを利用できる場合はtrueです。</returns>
        private bool TryEnsureQueries()
        {
            World currentWorld = World.DefaultGameObjectInjectionWorld;
            if (currentWorld == null || !currentWorld.IsCreated)
            {
                return false;
            }

            if (queriesAreCreated && presentationWorld == currentWorld)
            {
                return true;
            }

            DisposeQueries();
            presentationWorld = currentWorld;
            EntityManager entityManager = currentWorld.EntityManager;
            chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                ComponentType.ReadOnly<ChunkGenerationStatus>(),
                ComponentType.ReadOnly<GeneratedTile>());
            observerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingObserver>());
            settingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingSettings>());
            queriesAreCreated = true;
            return true;
        }

        /// <summary>
        /// 現在のECSチャンクを調べ、可視チャンクの反映と非表示・消失チャンクの消去を予約します。
        /// </summary>
        /// <param name="observer">カメラが要求する可視チャンク包含矩形です。</param>
        private void CollectPresentationWork(ChunkStreamingObserver observer)
        {
            EntityManager entityManager = presentationWorld.EntityManager;
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);
            seenCoordinates.Clear();

            // 保持対象チャンクはカメラ周辺だけに制限されています。ここでは表示状態の変化時だけQueueを更新します。
            for (int index = 0; index < entities.Length; index++)
            {
                ChunkCoordinate coordinate = new ChunkCoordinate(coordinates[index].X, coordinates[index].Y);
                ChunkGenerationStatus status = statuses[index];
                seenCoordinates.Add(coordinate);

                bool isVisible = Contains(observer, coordinate);
                bool hasCompleteData = status.Phase == ChunkGenerationPhase.ReadyToApply
                    || status.Phase == ChunkGenerationPhase.Active;

                if (isVisible && hasCompleteData)
                {
                    ScheduleApply(coordinate, entities[index]);
                }
                else
                {
                    CancelApplyAndScheduleClearIfNeeded(coordinate);
                }
            }

            // Entityがアンロードで消えた座標はQuery結果に現れないため、表示キャッシュとの差分から消去を予約します。
            coordinateScratch.Clear();
            foreach (ChunkCoordinate coordinate in displayedCoordinates)
            {
                if (!seenCoordinates.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            foreach (ChunkCoordinate coordinate in applyProgressByCoordinate.Keys)
            {
                if (!seenCoordinates.Contains(coordinate) && !coordinateScratch.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            for (int index = 0; index < coordinateScratch.Count; index++)
            {
                CancelApplyAndScheduleClearIfNeeded(coordinateScratch[index]);
            }
        }

        /// <summary>
        /// 未表示の可視チャンクを反映待ちへ追加し、すでに待機中なら最新Entity参照へ更新します。
        /// </summary>
        /// <param name="coordinate">反映する論理チャンク座標です。</param>
        /// <param name="entity">GeneratedTile Bufferを所有する現在のEntityです。</param>
        private void ScheduleApply(ChunkCoordinate coordinate, Entity entity)
        {
            if (displayedCoordinates.Contains(coordinate) || pendingClearCoordinates.Contains(coordinate))
            {
                return;
            }

            if (applyProgressByCoordinate.TryGetValue(coordinate, out ChunkApplyProgress progress))
            {
                progress.Entity = entity;
                return;
            }

            applyProgressByCoordinate.Add(coordinate, new ChunkApplyProgress(entity));
            applyQueue.Enqueue(coordinate);
        }

        /// <summary>
        /// 途中反映を中止し、1セルでも書き込んだ可能性がある座標を消去待ちへ追加します。
        /// </summary>
        /// <param name="coordinate">画面外またはアンロードになった論理チャンク座標です。</param>
        private void CancelApplyAndScheduleClearIfNeeded(ChunkCoordinate coordinate)
        {
            bool hadApplyProgress = applyProgressByCoordinate.Remove(coordinate);
            bool wasDisplayed = displayedCoordinates.Contains(coordinate);
            if (!hadApplyProgress && !wasDisplayed)
            {
                return;
            }

            if (pendingClearCoordinates.Add(coordinate))
            {
                clearQueue.Enqueue(new ChunkClearProgress(coordinate));
            }
        }

        /// <summary>
        /// 消去待ちを16×16単位で処理し、使用しなかった残り予算を返します。
        /// </summary>
        /// <param name="remainingBudget">このフレームで処理できる残りサブチャンク数です。</param>
        /// <returns>消去後に反映処理へ渡せる残り予算です。</returns>
        private int ProcessClearQueue(int remainingBudget)
        {
            while (remainingBudget > 0 && clearQueue.Count > 0)
            {
                ChunkClearProgress progress = clearQueue.Peek();
                ClearSubChunk(progress.Coordinate, progress.NextSubChunkIndex);
                progress.NextSubChunkIndex++;
                remainingBudget--;

                if (progress.NextSubChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                {
                    clearQueue.Dequeue();
                    pendingClearCoordinates.Remove(progress.Coordinate);
                    displayedCoordinates.Remove(progress.Coordinate);
                }
            }

            return remainingBudget;
        }

        /// <summary>
        /// 反映待ちを16×16単位で処理し、1チャンク完了時だけECS状態をActiveへ進めます。
        /// </summary>
        /// <param name="remainingBudget">このフレームで処理できる残りサブチャンク数です。</param>
        private void ProcessApplyQueue(int remainingBudget)
        {
            EntityManager entityManager = presentationWorld.EntityManager;
            while (remainingBudget > 0 && applyQueue.Count > 0)
            {
                ChunkCoordinate coordinate = applyQueue.Peek();
                if (!applyProgressByCoordinate.TryGetValue(coordinate, out ChunkApplyProgress progress))
                {
                    // Queueには残っていても画面外移動でDictionaryから除かれた項目は、安全に読み飛ばします。
                    applyQueue.Dequeue();
                    continue;
                }

                if (!entityManager.Exists(progress.Entity)
                    || !entityManager.HasBuffer<GeneratedTile>(progress.Entity))
                {
                    CancelApplyAndScheduleClearIfNeeded(coordinate);
                    applyQueue.Dequeue();
                    continue;
                }

                DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(progress.Entity, true);
                if (tiles.Length != WorldGridConstants.TilesPerChunk)
                {
                    // Job完了前または不正バッファは描画せず、次フレームに再確認します。
                    return;
                }

                ApplySubChunk(coordinate, progress.NextSubChunkIndex, tiles);
                progress.NextSubChunkIndex++;
                remainingBudget--;

                if (progress.NextSubChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                {
                    applyProgressByCoordinate.Remove(coordinate);
                    applyQueue.Dequeue();
                    displayedCoordinates.Add(coordinate);

                    ChunkGenerationStatus status = entityManager.GetComponentData<ChunkGenerationStatus>(progress.Entity);
                    if (status.Phase == ChunkGenerationPhase.ReadyToApply)
                    {
                        status.Phase = ChunkGenerationPhase.Active;
                        entityManager.SetComponentData(progress.Entity, status);
                    }
                }
            }
        }

        /// <summary>
        /// 1つの16×16サブチャンクをDynamic Bufferから再利用配列へコピーし、Tilemapへ一括反映します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="tiles">4096件の生成済みタイルBufferです。</param>
        private void ApplySubChunk(
            ChunkCoordinate coordinate,
            int subChunkIndex,
            DynamicBuffer<GeneratedTile> tiles)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int localStartX = subChunkX * WorldGridConstants.SubChunkSize;
            int localStartY = subChunkY * WorldGridConstants.SubChunkSize;

            for (int localY = 0; localY < WorldGridConstants.SubChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.SubChunkSize; localX++)
                {
                    int sourceIndex = WorldGrid.ToTileIndex(localStartX + localX, localStartY + localY);
                    int destinationIndex = (localY * WorldGridConstants.SubChunkSize) + localX;
                    subChunkTileBuffer[destinationIndex] = SelectTile(tiles[sourceIndex]);
                }
            }

            BoundsInt bounds = CreateSubChunkBounds(coordinate, subChunkIndex);
            targetTilemap.SetTilesBlock(bounds, subChunkTileBuffer);
        }

        /// <summary>
        /// 1つの16×16サブチャンクをnull配列で上書きし、表示オブジェクトだけを消去します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        private void ClearSubChunk(ChunkCoordinate coordinate, int subChunkIndex)
        {
            targetTilemap.SetTilesBlock(
                CreateSubChunkBounds(coordinate, subChunkIndex),
                emptySubChunkTileBuffer);
        }

        /// <summary>
        /// Component無効化時に1チャンク分を即時消去し、次回有効化時の古い表示を残しません。
        /// </summary>
        /// <param name="coordinate">消去する論理チャンク座標です。</param>
        private void ClearWholeChunkImmediately(ChunkCoordinate coordinate)
        {
            for (int subChunkIndex = 0;
                 subChunkIndex < WorldGridConstants.SubChunksPerChunk;
                 subChunkIndex++)
            {
                ClearSubChunk(coordinate, subChunkIndex);
            }
        }

        /// <summary>
        /// 論理チャンク座標とサブチャンク番号から、Tilemapセル用の16×16 BoundsIntを作ります。
        /// </summary>
        /// <param name="coordinate">親となる論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <returns>Z=0平面にある16×16×1セルの範囲です。</returns>
        private static BoundsInt CreateSubChunkBounds(ChunkCoordinate coordinate, int subChunkIndex)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int worldTileX = WorldGrid.ToWorldTileX(
                coordinate.X,
                subChunkX * WorldGridConstants.SubChunkSize);
            int worldTileY = WorldGrid.ToWorldTileY(
                coordinate.Y,
                subChunkY * WorldGridConstants.SubChunkSize);

            return new BoundsInt(
                worldTileX,
                worldTileY,
                0,
                WorldGridConstants.SubChunkSize,
                WorldGridConstants.SubChunkSize,
                1);
        }

        /// <summary>
        /// ゲームルール用GeneratedTileを、Inspectorで割り当てた表示専用Tileへ変換します。
        /// </summary>
        /// <param name="tile">ECSを正とする1タイル分の基礎地形データです。</param>
        /// <returns>水域・岩壁を優先し、それ以外はバイオームに対応するTileです。</returns>
        private TileBase SelectTile(GeneratedTile tile)
        {
            if (tile.Ground == GroundType.Water)
            {
                return waterTile;
            }

            if (tile.Ground == GroundType.RockWall)
            {
                return rockWallTile;
            }

            if (tile.Biome == BiomeId.Forest)
            {
                return forestTile;
            }

            if (tile.Biome == BiomeId.Desert)
            {
                return desertTile;
            }

            return grasslandTile;
        }

        /// <summary>
        /// Authoring設定から1フレームのサブチャンク処理数を取得し、不正値は最低1へ補正します。
        /// </summary>
        /// <returns>反映と消去で共有する正のフレーム予算です。</returns>
        private int GetSubChunkBudget()
        {
            if (settingsQuery.CalculateEntityCount() != 1)
            {
                return 1;
            }

            ChunkStreamingSettings settings = settingsQuery.GetSingleton<ChunkStreamingSettings>();
            return Mathf.Max(1, settings.MaximumSubChunkApplicationsPerFrame);
        }

        /// <summary>
        /// 正規化前のObserverにも対応し、指定チャンクがカメラ表示矩形に含まれるか判定します。
        /// </summary>
        /// <param name="observer">Presentation Bridgeが送った可視範囲です。</param>
        /// <param name="coordinate">検査する論理チャンク座標です。</param>
        /// <returns>4辺を含む矩形内ならtrueです。</returns>
        private static bool Contains(ChunkStreamingObserver observer, ChunkCoordinate coordinate)
        {
            int minimumX = Mathf.Min(observer.MinimumVisibleChunkX, observer.MaximumVisibleChunkX);
            int maximumX = Mathf.Max(observer.MinimumVisibleChunkX, observer.MaximumVisibleChunkX);
            int minimumY = Mathf.Min(observer.MinimumVisibleChunkY, observer.MaximumVisibleChunkY);
            int maximumY = Mathf.Max(observer.MinimumVisibleChunkY, observer.MaximumVisibleChunkY);
            return coordinate.X >= minimumX
                && coordinate.X <= maximumX
                && coordinate.Y >= minimumY
                && coordinate.Y <= maximumY;
        }

        /// <summary>
        /// World切り替えまたはComponent破棄時に、明示作成したQueryを安全に解放します。
        /// </summary>
        private void DisposeQueries()
        {
            if (!queriesAreCreated)
            {
                return;
            }

            // Play Mode終了ではWorldが先に破棄される場合があります。その場合QueryもWorldと一緒に解放済みです。
            if (presentationWorld != null && presentationWorld.IsCreated)
            {
                chunkQuery.Dispose();
                observerQuery.Dispose();
                settingsQuery.Dispose();
            }

            queriesAreCreated = false;
            presentationWorld = null;
        }

        /// <summary>
        /// 表示・反映・消去の管理キャッシュを空にし、再有効化時にECSから再構築できる状態へ戻します。
        /// </summary>
        private void ResetPresentationCaches()
        {
            applyProgressByCoordinate.Clear();
            applyQueue.Clear();
            displayedCoordinates.Clear();
            pendingClearCoordinates.Clear();
            seenCoordinates.Clear();
            clearQueue.Clear();
            coordinateScratch.Clear();
        }

        /// <summary>
        /// 1チャンクのTilemap反映途中位置を保持するPresentation専用の参照型です。
        /// Dictionaryから取得した後に値を書き戻さず進捗を更新するため、短寿命の生成ごとに1回だけ確保します。
        /// </summary>
        private sealed class ChunkApplyProgress
        {
            /// <summary>GeneratedTile Bufferを読み取る現在のチャンクEntityです。</summary>
            public Entity Entity;

            /// <summary>次に反映する行優先のサブチャンク番号です。範囲は0〜16です。</summary>
            public int NextSubChunkIndex;

            /// <summary>
            /// 新しい反映作業を先頭サブチャンクから開始します。
            /// </summary>
            /// <param name="entity">生成済みタイルを所有するEntityです。</param>
            public ChunkApplyProgress(Entity entity)
            {
                Entity = entity;
                NextSubChunkIndex = 0;
            }
        }

        /// <summary>
        /// 1チャンクのTilemap消去途中位置を保持するPresentation専用の参照型です。
        /// Queueの先頭を取り出したまま次番号を更新できるため、毎フレームの再登録を避けます。
        /// </summary>
        private sealed class ChunkClearProgress
        {
            /// <summary>消去対象の論理チャンク座標です。</summary>
            public readonly ChunkCoordinate Coordinate;

            /// <summary>次に消去する行優先のサブチャンク番号です。範囲は0〜16です。</summary>
            public int NextSubChunkIndex;

            /// <summary>
            /// 新しい消去作業を先頭サブチャンクから開始します。
            /// </summary>
            /// <param name="coordinate">消去対象の論理チャンク座標です。</param>
            public ChunkClearProgress(ChunkCoordinate coordinate)
            {
                Coordinate = coordinate;
                NextSubChunkIndex = 0;
            }
        }
    }
}
