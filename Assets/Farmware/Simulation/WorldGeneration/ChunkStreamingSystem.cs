// このファイルは、カメラが要求した可視範囲を基にチャンクEntityの生成要求、休止、キャンセル、アンロードを管理します。
// 純粋データ生成はChunkGenerationSystem、表示反映はPresentationへ分離し、このSystemは寿命と優先順位だけを担当します。

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Farmware.Simulation;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 可視矩形の近くに必要なチャンクEntityを作り、遠ざかった再生成可能データを破棄するECS Systemです。
    /// カメラ中心に近い座標から要求することで、生成予算が小さい場合も画面中央を優先します。
    /// FarmwareSimulationSystemGroupで描画フレームごとに動き、固定Tickのゲーム内時間は進めません。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareSimulationSystemGroup))]
    [UpdateBefore(typeof(ChunkGenerationSystem))]
    public partial struct ChunkStreamingSystem : ISystem
    {
        /// <summary>Profilerでチャンク寿命管理のCPU時間と同期点を見つけるための計測区間です。</summary>
        private static readonly ProfilerMarker StreamingProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkStreaming");

        /// <summary>現在存在するすべてのチャンクEntityを取得する、System寿命中再利用するQueryです。</summary>
        private EntityQuery chunkQuery;

        /// <summary>
        /// System作成時にQueryとSingleton依存条件を準備します。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            chunkQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkCoordinateComponent, ChunkGenerationStatus>()
                .Build(ref state);

            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<ChunkStreamingSettings>();
            state.RequireForUpdate<ChunkStreamingObserver>();
        }

        /// <summary>
        /// 毎Farmware Simulation更新で可視・先読み・保持の各矩形を評価し、必要最小限の構造変更をまとめて適用します。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            using (StreamingProfilerMarker.Auto())
            {
                ChunkStreamingSettings settings = SystemAPI.GetSingleton<ChunkStreamingSettings>();
                ChunkStreamingObserver storedObserver = SystemAPI.GetSingleton<ChunkStreamingObserver>();
                ChunkStreamingObserver observer = storedObserver.Normalized();
                int preloadMargin = math.max(1, settings.PreloadMarginInChunks);
                int retentionMargin = math.max(0, settings.RetentionMarginInChunks);
                int maximumConcurrentGenerations = math.max(1, settings.MaximumConcurrentGenerations);

                // ローカルコピーだけを補正するとPresenterが未設定Layerを読み続け、生成済みSurfaceを画面外扱いします。
                // 値が変わった場合だけ正規化結果を書き戻し、SimulationとPresentationの検索キーを一致させます。
                if (storedObserver.Layer != observer.Layer
                    || storedObserver.MinimumVisibleChunkX != observer.MinimumVisibleChunkX
                    || storedObserver.MaximumVisibleChunkX != observer.MaximumVisibleChunkX
                    || storedObserver.MinimumVisibleChunkY != observer.MinimumVisibleChunkY
                    || storedObserver.MaximumVisibleChunkY != observer.MaximumVisibleChunkY)
                {
                    SystemAPI.SetSingleton(observer);
                }

                // UnderwaterとDeepSeaの自然地形はTERRAIN-01Cで接続します。
                // falseの間も既存Surfaceチャンクの寿命処理は続け、レイヤー切替後に旧Bufferを残しません。
                bool canGenerateSelectedLayer = observer.Layer == WorldLayer.Surface;

                // Observerを直接書き込む別Presentationが将来増えても、生成Systemへ不正なチャンク座標を渡さないよう、
                // Camera側ではなくストリーミング境界で可視・先読み・保持の全範囲を有効なタイル座標域へ収めます。
                ChunkBounds visibleBounds = ChunkBounds
                    .FromObserver(observer)
                    .ClampedToWorldTileDomain();
                ChunkBounds preloadBounds = visibleBounds
                    .Expanded(preloadMargin)
                    .ClampedToWorldTileDomain();
                ChunkBounds retentionBounds = preloadBounds
                    .Expanded(retentionMargin)
                    .ClampedToWorldTileDomain();

                // 生成Jobは専用配列だけへ書くため、ここでは未完成生成を待たず確定済みECS状態を読みます。
                // 他のECS処理との通常の依存解決はQuery APIへ委ね、生成JobのHandleは生成Systemだけが管理します。
                NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
                NativeArray<ChunkCoordinateComponent> coordinates =
                    chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
                NativeArray<ChunkGenerationStatus> statuses =
                    chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);
                NativeParallelHashSet<ChunkCoordinate> knownCoordinates = new NativeParallelHashSet<ChunkCoordinate>(
                    math.max(1, entities.Length + maximumConcurrentGenerations),
                    Allocator.Temp);
                NativeParallelHashMap<ChunkCoordinate, Entity> reactivatableRequests =
                    new NativeParallelHashMap<ChunkCoordinate, Entity>(
                        math.max(1, entities.Length),
                        Allocator.Temp);
                EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
                int pendingGenerationCount = 0;

                // 現在のチャンクは少数のカメラ周辺集合に限定されます。毎フレーム全ワールドを走査しないよう、
                // 保持矩形の外はこの場で破棄し、以後の検索集合へ追加しません。
                for (int index = 0; index < entities.Length; index++)
                {
                    Entity entity = entities[index];
                    ChunkCoordinate coordinate = new ChunkCoordinate(
                        coordinates[index].Layer,
                        coordinates[index].X,
                        coordinates[index].Y);
                    ChunkGenerationStatus status = statuses[index];

                    // WORLD-01Cでは現在表示中かつ生成器が接続済みのレイヤーだけを保持します。
                    // 同じX/Yの別レイヤーを矩形だけで一致扱いすると要求を誤接続し、未生成レイヤーで既存Surfaceを
                    // 保持するとECS状態と現在表示が食い違うため、どちらも同じ寿命処理で解放します。
                    if (!canGenerateSelectedLayer || coordinate.Layer != observer.Layer)
                    {
                        if (status.Phase == ChunkGenerationPhase.GeneratingData)
                        {
                            pendingGenerationCount++;
                            continue;
                        }

                        commandBuffer.DestroyEntity(entity);
                        continue;
                    }

                    if (!retentionBounds.Contains(coordinate))
                    {
                        // 生成中の枠を解放扱いにすると、移動のたびに未完成Jobが上限を超えて積み上がります。
                        // 完成まではEntityと予約枠だけ保持し、完成後の次回更新で通常どおり破棄します。
                        // Jobは専用配列へ書くため、この判定で完了を待つ必要はありません。
                        if (status.Phase == ChunkGenerationPhase.GeneratingData)
                        {
                            pendingGenerationCount++;
                            continue;
                        }

                        commandBuffer.DestroyEntity(entity);
                        continue;
                    }

                    knownCoordinates.Add(coordinate);

                    bool isInsidePreload = preloadBounds.Contains(coordinate);
                    bool isVisible = visibleBounds.Contains(coordinate);

                    // まだJobが開始していない遠方要求だけを取り消します。生成済みデータは保持余白内なら残します。
                    if (!isInsidePreload && status.Phase == ChunkGenerationPhase.Queued)
                    {
                        status.Phase = ChunkGenerationPhase.Unrequested;
                        commandBuffer.SetComponent(entity, status);
                        commandBuffer.SetComponentEnabled<ChunkGenerationRequest>(entity, false);
                        continue;
                    }

                    if (status.Phase == ChunkGenerationPhase.Queued
                        || status.Phase == ChunkGenerationPhase.GeneratingData)
                    {
                        pendingGenerationCount++;
                    }

                    // 可視範囲を外れた完成済みデータはInactiveにします。表示側は状態名だけで消去せず、
                    // 外側1チャンクの先行準備範囲では確定Bufferと表示を再利用し、その外で予算内消去します。
                    if (!isVisible && status.Phase == ChunkGenerationPhase.Active)
                    {
                        status.Phase = ChunkGenerationPhase.Inactive;
                        commandBuffer.SetComponent(entity, status);
                    }
                    else if (isVisible && status.Phase == ChunkGenerationPhase.Inactive)
                    {
                        status.Phase = ChunkGenerationPhase.Active;
                        commandBuffer.SetComponent(entity, status);
                    }
                    else if (isInsidePreload && status.Phase == ChunkGenerationPhase.Unrequested)
                    {
                        // 再要求も新規要求と同じ同時生成上限へ含めるため、ここでは候補だけを記録します。
                        // 後段の中心優先リング走査で予算が残っている座標だけを再有効化します。
                        reactivatableRequests.TryAdd(coordinate, entity);
                    }
                }

                // 未生成レイヤーでは既存チャンクの解放だけを行い、Surface生成器を流用した新規要求は作りません。
                if (canGenerateSelectedLayer)
                {
                    int requestBudget = math.max(0, maximumConcurrentGenerations - pendingGenerationCount);
                    QueueMissingChunksNearFirst(
                        observer.Layer,
                        preloadBounds,
                        visibleBounds.CenterX,
                        visibleBounds.CenterY,
                        requestBudget,
                        ref knownCoordinates,
                        ref reactivatableRequests,
                        ref commandBuffer);
                }

                // 構造変更を1回へまとめ、Query走査中にEntity配置が変わることを防ぎます。
                commandBuffer.Playback(state.EntityManager);

                commandBuffer.Dispose();
                reactivatableRequests.Dispose();
                knownCoordinates.Dispose();
                statuses.Dispose();
                coordinates.Dispose();
                entities.Dispose();
            }
        }

        /// <summary>
        /// 指定矩形内の不足チャンクを中心から外側へ向かう四角いリング順で要求します。
        /// </summary>
        /// <param name="layer">今回作成・再利用するチャンクが属する固定ワールドレイヤーです。</param>
        /// <param name="bounds">生成対象となるプリロード込み矩形です。</param>
        /// <param name="centerX">優先順位の中心となる水平チャンク座標です。</param>
        /// <param name="centerY">優先順位の中心となる垂直チャンク座標です。</param>
        /// <param name="requestBudget">今回新規作成してよいチャンクEntity数です。</param>
        /// <param name="knownCoordinates">既存座標と今回追加した座標を保持する重複防止集合です。</param>
        /// <param name="reactivatableRequests">保持中のUnrequested Entityを座標から引く再利用候補です。</param>
        /// <param name="commandBuffer">新規Entity作成を遅延適用するCommand Bufferです。</param>
        private static void QueueMissingChunksNearFirst(
            WorldLayer layer,
            ChunkBounds bounds,
            int centerX,
            int centerY,
            int requestBudget,
            ref NativeParallelHashSet<ChunkCoordinate> knownCoordinates,
            ref NativeParallelHashMap<ChunkCoordinate, Entity> reactivatableRequests,
            ref EntityCommandBuffer commandBuffer)
        {
            if (requestBudget <= 0)
            {
                return;
            }

            int maximumRing = bounds.MaximumDistanceFrom(centerX, centerY);
            for (int ring = 0; ring <= maximumRing && requestBudget > 0; ring++)
            {
                if (ring == 0)
                {
                    TryQueueChunk(
                        layer,
                        centerX,
                        centerY,
                        bounds,
                        ref requestBudget,
                        ref knownCoordinates,
                        ref reactivatableRequests,
                        ref commandBuffer);
                    continue;
                }

                int left = centerX - ring;
                int right = centerX + ring;
                int bottom = centerY - ring;
                int top = centerY + ring;

                // 上下の辺を左から右へ、続いて左右の辺を下から上へ処理し、角の重複を避けます。
                for (int x = left; x <= right && requestBudget > 0; x++)
                {
                    TryQueueChunk(
                        layer,
                        x,
                        bottom,
                        bounds,
                        ref requestBudget,
                        ref knownCoordinates,
                        ref reactivatableRequests,
                        ref commandBuffer);
                    TryQueueChunk(
                        layer,
                        x,
                        top,
                        bounds,
                        ref requestBudget,
                        ref knownCoordinates,
                        ref reactivatableRequests,
                        ref commandBuffer);
                }

                for (int y = bottom + 1; y < top && requestBudget > 0; y++)
                {
                    TryQueueChunk(
                        layer,
                        left,
                        y,
                        bounds,
                        ref requestBudget,
                        ref knownCoordinates,
                        ref reactivatableRequests,
                        ref commandBuffer);
                    TryQueueChunk(
                        layer,
                        right,
                        y,
                        bounds,
                        ref requestBudget,
                        ref knownCoordinates,
                        ref reactivatableRequests,
                        ref commandBuffer);
                }
            }
        }

        /// <summary>
        /// 1座標が対象内かつ未要求なら、生成に必要なComponentとBufferを持つEntityを予約します。
        /// </summary>
        /// <param name="layer">要求候補が属する固定ワールドレイヤーです。</param>
        /// <param name="x">要求候補の水平論理チャンク座標です。</param>
        /// <param name="y">要求候補の垂直論理チャンク座標です。</param>
        /// <param name="bounds">候補を含めてよいプリロード矩形です。</param>
        /// <param name="requestBudget">成功時に1減らす今回の残り要求数です。</param>
        /// <param name="knownCoordinates">重複生成を防ぐ座標集合です。</param>
        /// <param name="reactivatableRequests">空Entityを再利用できる座標とEntityの対応表です。</param>
        /// <param name="commandBuffer">Entity作成を記録するCommand Bufferです。</param>
        private static void TryQueueChunk(
            WorldLayer layer,
            int x,
            int y,
            ChunkBounds bounds,
            ref int requestBudget,
            ref NativeParallelHashSet<ChunkCoordinate> knownCoordinates,
            ref NativeParallelHashMap<ChunkCoordinate, Entity> reactivatableRequests,
            ref EntityCommandBuffer commandBuffer)
        {
            if (requestBudget <= 0)
            {
                return;
            }

            ChunkCoordinate coordinate = new ChunkCoordinate(layer, x, y);
            if (!bounds.Contains(coordinate))
            {
                return;
            }

            // 一度キャンセルした空Entityが保持範囲に残っていれば、構造を作り直さず要求だけを再有効化します。
            if (reactivatableRequests.TryGetValue(coordinate, out Entity reactivatableEntity))
            {
                commandBuffer.SetComponent(reactivatableEntity, new ChunkGenerationStatus
                {
                    Phase = ChunkGenerationPhase.Queued,
                    GeneratedWithVersion = 0
                });
                commandBuffer.SetComponentEnabled<ChunkGenerationRequest>(reactivatableEntity, true);
                reactivatableRequests.Remove(coordinate);
                requestBudget--;
                return;
            }

            if (!knownCoordinates.Add(coordinate))
            {
                return;
            }

            Entity entity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(entity, new ChunkCoordinateComponent
            {
                Layer = layer,
                X = x,
                Y = y
            });
            commandBuffer.AddComponent(entity, new ChunkGenerationStatus
            {
                Phase = ChunkGenerationPhase.Queued,
                GeneratedWithVersion = 0
            });
            commandBuffer.AddComponent<ChunkGenerationRequest>(entity);
            commandBuffer.AddBuffer<GeneratedTile>(entity);
            commandBuffer.AddBuffer<GeneratedResourceTile>(entity);
            commandBuffer.AddBuffer<GeneratedHydrologyCell>(entity);
            commandBuffer.AddBuffer<GeneratedSurfaceWaterTile>(entity);
            commandBuffer.AddBuffer<GeneratedHydrologyTile>(entity);
            requestBudget--;
        }

        /// <summary>
        /// 可視・プリロード・保持範囲を共通の包含矩形として扱う内部値型です。
        /// System内だけで使い、セーブ対象や公開APIにはしません。
        /// </summary>
        private readonly struct ChunkBounds
        {
            /// <summary>包含する最小水平チャンク座標です。</summary>
            public readonly int MinimumX;

            /// <summary>包含する最大水平チャンク座標です。</summary>
            public readonly int MaximumX;

            /// <summary>包含する最小垂直チャンク座標です。</summary>
            public readonly int MinimumY;

            /// <summary>包含する最大垂直チャンク座標です。</summary>
            public readonly int MaximumY;

            /// <summary>矩形中央の水平チャンク座標です。偶数幅では小さい側へ丸めます。</summary>
            public int CenterX => (int)(((long)MinimumX + MaximumX) / 2L);

            /// <summary>矩形中央の垂直チャンク座標です。偶数幅では小さい側へ丸めます。</summary>
            public int CenterY => (int)(((long)MinimumY + MaximumY) / 2L);

            /// <summary>
            /// 4辺から包含矩形を作ります。
            /// </summary>
            /// <param name="minimumX">最小水平座標です。</param>
            /// <param name="maximumX">最大水平座標です。</param>
            /// <param name="minimumY">最小垂直座標です。</param>
            /// <param name="maximumY">最大垂直座標です。</param>
            private ChunkBounds(int minimumX, int maximumX, int minimumY, int maximumY)
            {
                MinimumX = minimumX;
                MaximumX = maximumX;
                MinimumY = minimumY;
                MaximumY = maximumY;
            }

            /// <summary>
            /// PresentationのObserver値から可視矩形を作ります。
            /// </summary>
            /// <param name="observer">正規化済みの可視範囲です。</param>
            /// <returns>同じ4辺を持つ包含矩形です。</returns>
            public static ChunkBounds FromObserver(ChunkStreamingObserver observer)
            {
                return new ChunkBounds(
                    observer.MinimumVisibleChunkX,
                    observer.MaximumVisibleChunkX,
                    observer.MinimumVisibleChunkY,
                    observer.MaximumVisibleChunkY);
            }

            /// <summary>
            /// 全方向へ指定チャンク数だけ広げた矩形を返します。
            /// </summary>
            /// <param name="margin">各辺へ追加する0以上のチャンク数です。</param>
            /// <returns>元の矩形を包含する拡張済み矩形です。</returns>
            public ChunkBounds Expanded(int margin)
            {
                return new ChunkBounds(
                    SaturatingSubtract(MinimumX, margin),
                    SaturatingAdd(MaximumX, margin),
                    SaturatingSubtract(MinimumY, margin),
                    SaturatingAdd(MaximumY, margin));
            }

            /// <summary>
            /// 各辺を、64×64の全タイルをint絶対座標へ安全に変換できる論理チャンク範囲へ収めます。
            /// 可視範囲の外へ先読み・保持余白を加えたときも、反対側のint端へ周回した地形を要求させないために使います。
            /// </summary>
            /// <returns>ワールドタイル座標へ安全に変換できる4辺を持つ包含矩形です。</returns>
            public ChunkBounds ClampedToWorldTileDomain()
            {
                return new ChunkBounds(
                    math.clamp(
                        MinimumX,
                        WorldGridConstants.MinimumLogicalChunkCoordinate,
                        WorldGridConstants.MaximumLogicalChunkCoordinate),
                    math.clamp(
                        MaximumX,
                        WorldGridConstants.MinimumLogicalChunkCoordinate,
                        WorldGridConstants.MaximumLogicalChunkCoordinate),
                    math.clamp(
                        MinimumY,
                        WorldGridConstants.MinimumLogicalChunkCoordinate,
                        WorldGridConstants.MaximumLogicalChunkCoordinate),
                    math.clamp(
                        MaximumY,
                        WorldGridConstants.MinimumLogicalChunkCoordinate,
                        WorldGridConstants.MaximumLogicalChunkCoordinate));
            }

            /// <summary>
            /// 指定座標が4辺を含む矩形内にあるか判定します。
            /// </summary>
            /// <param name="coordinate">検査する論理チャンク座標です。</param>
            /// <returns>矩形内または境界上ならtrueです。</returns>
            public bool Contains(ChunkCoordinate coordinate)
            {
                return coordinate.X >= MinimumX
                    && coordinate.X <= MaximumX
                    && coordinate.Y >= MinimumY
                    && coordinate.Y <= MaximumY;
            }

            /// <summary>
            /// 中心から最も遠い辺までのチェビシェフ距離を求めます。
            /// </summary>
            /// <param name="centerX">優先順位中心の水平座標です。</param>
            /// <param name="centerY">優先順位中心の垂直座標です。</param>
            /// <returns>矩形全体を四角いリングで覆うために必要な最大半径です。</returns>
            public int MaximumDistanceFrom(int centerX, int centerY)
            {
                long horizontal = math.max(
                    math.abs((long)MinimumX - centerX),
                    math.abs((long)MaximumX - centerX));
                long vertical = math.max(
                    math.abs((long)MinimumY - centerY),
                    math.abs((long)MaximumY - centerY));
                return (int)math.min(int.MaxValue, math.max(horizontal, vertical));
            }

            /// <summary>
            /// intの下限を越えないように余白を減算します。
            /// </summary>
            /// <param name="value">元の座標です。</param>
            /// <param name="amount">減らす0以上の値です。</param>
            /// <returns>int範囲へ飽和させた差です。</returns>
            private static int SaturatingSubtract(int value, int amount)
            {
                return (int)math.max(int.MinValue, (long)value - amount);
            }

            /// <summary>
            /// intの上限を越えないように余白を加算します。
            /// </summary>
            /// <param name="value">元の座標です。</param>
            /// <param name="amount">加える0以上の値です。</param>
            /// <returns>int範囲へ飽和させた和です。</returns>
            private static int SaturatingAdd(int value, int amount)
            {
                return (int)math.min(int.MaxValue, (long)value + amount);
            }
        }
    }
}
