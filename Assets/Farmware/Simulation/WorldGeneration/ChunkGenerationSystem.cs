// このファイルは、地形・資源・固定水文をECSから独立した作業領域で生成し、完成結果だけをECSへ公開します。
// ChunkStreamingSystemが要求と寿命、Presentationが確定済みBufferの表示、固定Tick公開Systemが個体群の決定論境界を担当します。

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Farmware.Simulation;
using Farmware.Simulation.Animals;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 有効なChunkGenerationRequestを持つチャンクだけを生成し、同じ座標の地形・資源・固定水文を対応付けるECS Systemです。
    /// 開始時に要求を無効化します。動物個体群があるWorldでは固定Tick境界で一括公開し、Job完了順を論理状態へ持ち込みません。
    /// 個体群Worldでは通常フレームで先行投入し、未完了中は固定Tickを延期して、完成バッチだけをTick入口で公開します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareSimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkStreamingSystem))]
    public partial struct ChunkGenerationSystem : ISystem
    {
        /// <summary>基礎水文を並列生成する行数です。1論理チャンクの8行と一致します。</summary>
        private const int HydrologyRowWorkItemCount = DeterministicHydrologyGenerator.CellsPerChunkAxis;

        // 完了確認後の公開時間です。新規投入やWorker上の生成時間とは分けて測ります。
        private static readonly ProfilerMarker PublishProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationPublish");

        // 作業枠の確保とJob投入のCPU時間です。未完成Jobを待っていないか確認します。
        private static readonly ProfilerMarker ScheduleProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationSchedule");

        /// <summary>完成確認済みHandleを固定Tick境界で確定する時間です。未完成Jobの同期待ちは含めません。</summary>
        private static readonly ProfilerMarker BoundaryWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationBoundaryWait");

        // 完成確認済みの基礎水文Handleを安全に確定する時間です。未完了中はRate ManagerがTick自体を延期します。
        private static readonly ProfilerMarker HydrologyBaseWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationHydrologyBaseWait");

        // 完成確認済みの閉鎖流域・湖面Handleを安全に確定する時間です。
        private static readonly ProfilerMarker LakeWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationLakeWait");

        // 完成確認済みの地形・資源Handleを安全に確定する時間です。
        private static readonly ProfilerMarker TerrainWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationTerrainWait");

        // 有効なEnableable要求だけを調べる、System寿命中のQueryです。
        private EntityQuery requestQuery;

        // 固定Tick Rate ManagerへChunk生成中かを通知する任意Singleton Queryです。
        // 古い単体テストWorldに存在しない場合は書き込まず、通常のWorld初期化が作った1件だけを更新します。
        private EntityQuery fixedTickReadinessQuery;

        // 上限まで増やして再利用する作業枠です。Listも各Persistent配列もSystemが破棄します。
        // List自体はJobへ渡さず、選んだ枠の配列だけを依存関係で結んだ生成Job群へ貸します。
        private NativeList<GenerationSlot> slots;

        /// <summary>
        /// System作成時に要求Queryと再利用枠の一覧を確保し、生成設定がそろうまで更新を待つ条件を登録します。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            requestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkCoordinateComponent, ChunkGenerationStatus, ChunkGenerationRequest>()
                .WithAll<GeneratedTile, GeneratedResourceTile, GeneratedHydrologyCell>()
                .Build(ref state);
            fixedTickReadinessQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<FixedTickReadinessState>()
                .Build(ref state);
            slots = new NativeList<GenerationSlot>(
                ChunkStreamingSettings.CreateDefault().MaximumConcurrentGenerations, Allocator.Persistent);
            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
        }

        /// <summary>
        /// 描画フレームで次の有限バッチを先行投入し、Workerが固定Tickまで計算できる時間を確保します。
        /// 個体群Worldの公開は固定Tickへ限定し、停止中だけ前フレームの完成結果をTickを進めず公開します。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            bool fixedTickPublication = SystemAPI.HasSingleton<AnimalSpeciesCatalog>()
                && SystemAPI.HasSingleton<SimulationTickState>();
            bool isPaused = SystemAPI.TryGetSingleton(out SimulationPauseState pauseState)
                && pauseState.IsPaused;
            if (fixedTickPublication)
            {
                if (isPaused)
                {
                    // 停止中は固定Tickが来ないため、完成済みバッチだけをTickを進めず表示へ渡します。
                    // 未完成なら次の描画フレームへ延期し、Pause中のCamera操作でもMain Threadを止めません。
                    TryCompleteAndPublishBatch(state.EntityManager);
                }
                // 通常進行中はここで公開せず、完成後の固定Tick入口へ揃えます。
            }
            else
            {
                // 個体群を持たない地形単独Worldでは、既存の非同期表示・Editorテスト用経路を維持します。
                PublishCompletedResults(state.EntityManager);
            }

            int limit = SystemAPI.TryGetSingleton(out ChunkStreamingSettings streamingSettings)
                ? streamingSettings.MaximumConcurrentGenerations
                : ChunkStreamingSettings.CreateDefault().MaximumConcurrentGenerations;
            bool scheduled = ScheduleBatch(state.EntityManager, SystemAPI.GetSingleton<WorldGenerationSettings>(),
                SystemAPI.GetSingleton<ResourceGenerationSettings>(), limit);
            RefreshFixedTickReadiness(state.EntityManager, fixedTickPublication && !isPaused, scheduled);
        }

        /// <summary>
        /// Tick境界または描画フレームで呼び、座標順の要求から上限までJobを投入します。完成確認と公開は後続更新へ分離します。
        /// </summary>
        /// <param name="manager">このSystemと同じWorldのEntityManagerです。</param>
        /// <param name="settings">投入時点で固定する地形生成設定です。</param>
        /// <param name="resourceSettings">投入時点で固定する資源生成設定です。</param>
        /// <param name="maximumConcurrentGenerations">同時実行上限です。1未満は1へ正規化します。</param>
        /// <returns>この呼出しで1件以上の新しい生成Jobを投入した場合はtrueです。</returns>
        public bool ScheduleBatch(EntityManager manager, WorldGenerationSettings settings,
            ResourceGenerationSettings resourceSettings, int maximumConcurrentGenerations)
        {
            using (ScheduleProfilerMarker.Auto())
            {
                return ScheduleRequests(
                    manager,
                    settings,
                    resourceSettings,
                    math.max(1, maximumConcurrentGenerations));
            }
        }

        /// <summary>
        /// 固定Tick入口と停止中フレームから呼び、現在の有限バッチが全件完成済みの場合だけまとめて公開します。
        /// 一部でも未完了なら待たずにfalseを返し、描画フレームを止めず次回へ延期します。
        /// </summary>
        /// <param name="manager">このSystemと同じWorldの公開先です。完成時だけBufferと生成状態を更新します。</param>
        /// <returns>実行中バッチが無い、または全件を完成・公開できた場合はtrueです。</returns>
        public bool TryCompleteAndPublishBatch(EntityManager manager)
        {
            if (HasIncompleteRunningBatch())
            {
                return false;
            }

            using (BoundaryWaitProfilerMarker.Auto())
            {
                // IsCompletedを全枠で確認した後だけCompleteし、Native Containerの安全性Handleを確定します。
                // ここへ未完成Jobを入れないため、各Markerは同期停止ではなく完成済みHandleの解放費用を表します。
                using (HydrologyBaseWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.HydrologyBase);
                }
                using (LakeWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.Lake);
                }
                using (TerrainWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.Terrain);
                }
            }

            PublishCompletedResults(manager);
            return true;
        }

        /// <summary>
        /// 固定Tick Rate Managerへ、投入直後または未完成の生成バッチがあるかを通知します。
        /// </summary>
        /// <param name="manager">準備状態Singletonを所有する同じWorldのEntityManagerです。</param>
        /// <param name="barrierEnabled">個体群を持つ通常進行中のWorldだけtrueにします。</param>
        /// <param name="scheduledThisUpdate">同じ描画フレームでJobを投入し、最低1フレームの実行猶予が必要ならtrueです。</param>
        public void RefreshFixedTickReadiness(
            EntityManager manager,
            bool barrierEnabled,
            bool scheduledThisUpdate)
        {
            if (fixedTickReadinessQuery.CalculateEntityCount() != 1)
            {
                return;
            }

            FixedTickReadinessState readiness =
                fixedTickReadinessQuery.GetSingleton<FixedTickReadinessState>();
            readiness.SetChunkGenerationPending(
                barrierEnabled && (scheduledThisUpdate || HasIncompleteRunningBatch()));
            manager.SetComponentData(fixedTickReadinessQuery.GetSingletonEntity(), readiness);
        }

        /// <summary>実行中枠のうち一つでも最終Handleが未完了ならtrueを返します。</summary>
        /// <returns>固定Tickを延期し、Main ThreadでCompleteしてはいけない状態ならtrueです。</returns>
        private bool HasIncompleteRunningBatch()
        {
            for (int index = 0; index < slots.Length; index++)
            {
                GenerationSlot slot = slots[index];
                if (slot.Running && !slot.Handle.IsCompleted)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 実行中の全枠から指定系列の完成済みHandleだけを結合し、Native Containerの安全性状態を確定します。
        /// </summary>
        /// <param name="stage">基礎水文、湖、地形・資源のうち確定費用を分離する系列です。</param>
        private void CompleteRunningStage(GenerationStage stage)
        {
            JobHandle combined = default;
            for (int index = 0; index < slots.Length; index++)
            {
                GenerationSlot slot = slots[index];
                if (!slot.Running)
                {
                    continue;
                }

                JobHandle stageHandle;
                switch (stage)
                {
                    case GenerationStage.HydrologyBase:
                        stageHandle = slot.HydrologyBaseHandle;
                        break;
                    case GenerationStage.Lake:
                        stageHandle = slot.LakeHandle;
                        break;
                    default:
                        stageHandle = slot.TerrainHandle;
                        break;
                }
                combined = JobHandle.CombineDependencies(combined, stageHandle);
            }

            // 呼出し前に最終Handle全件のIsCompletedを確認済みなので、ここでは未完成Jobを待ちません。
            combined.Complete();
        }

        /// <summary>
        /// WorldまたはSystemの破棄時に残ったJobを待ち、使用中配列を先に解放しないようにします。
        /// この寿命境界ではECSへ結果を公開せず、通常更新とは別に全所有メモリを解放します。
        /// </summary>
        /// <param name="state">破棄されるSystemの状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                GenerationSlot slot = slots[index];
                slot.Handle.Complete();
                slot.Tiles.Dispose();
                slot.Resources.Dispose();
                slot.HydrologyCells.Dispose();
                slot.HydrologyCatchmentFrontier.Dispose();
                slot.HydrologyBasinMembers.Dispose();
                slot.HydrologyBasinLookup.Dispose();
                slot.HydrologyLakeWorkItems.Dispose();
                slot.HydrologyLakeWorkItemCount.Dispose();
                slot.HydrologyGenerationFailed.Dispose();
                slot.TerrainGenerationFailed.Dispose();
            }

            // Systemだけが先に破棄されるテストWorldでも、存在しない生成Jobを理由に固定Tickを止め続けません。
            RefreshFixedTickReadiness(state.EntityManager, false, false);
            slots.Dispose();
        }

        /// <summary>完成した作業枠だけを読み、存続する同じ要求Entityへ地形・資源・固定水文をまとめて公開します。</summary>
        /// <param name="manager">同じWorldの公開先です。所有Jobの専用配列から完成結果をコピーします。</param>
        private void PublishCompletedResults(EntityManager manager)
        {
            using var publicationScope = PublishProfilerMarker.Auto();
            for (int index = 0; index < slots.Length; index++)
            {
                GenerationSlot slot = slots[index];
                if (!slot.Running || !slot.Handle.IsCompleted)
                {
                    continue;
                }

                // 完了確認後のCompleteで安全性ハンドルを確定します。未完成のJobをここで待ちません。
                slot.Handle.Complete();
                if (manager.Exists(slot.Entity)
                    && manager.HasComponent<ChunkCoordinateComponent>(slot.Entity)
                    && manager.HasComponent<ChunkGenerationStatus>(slot.Entity)
                    && manager.HasBuffer<GeneratedTile>(slot.Entity)
                    && manager.HasBuffer<GeneratedResourceTile>(slot.Entity)
                    && manager.HasBuffer<GeneratedHydrologyCell>(slot.Entity))
                {
                    ChunkCoordinateComponent coordinate = manager.GetComponentData<ChunkCoordinateComponent>(slot.Entity);
                    ChunkGenerationStatus status = manager.GetComponentData<ChunkGenerationStatus>(slot.Entity);
                    if (coordinate.Layer == slot.Coordinate.Layer
                        && coordinate.X == slot.Coordinate.X
                        && coordinate.Y == slot.Coordinate.Y
                        && status.Phase == ChunkGenerationPhase.GeneratingData)
                    {
                        bool generationFailed = false;
                        for (int workItemIndex = 0;
                            workItemIndex < slot.HydrologyGenerationFailed.Length;
                            workItemIndex++)
                        {
                            if (slot.HydrologyGenerationFailed[workItemIndex] != 0)
                            {
                                generationFailed = true;
                                break;
                            }
                        }
                        generationFailed |= slot.TerrainGenerationFailed[0] != 0;

                        if (generationFailed)
                        {
                            // 固定半径サンプルがワールド座標域を越えた場合は、不完全なBufferを公開せず明示的な失敗へ進めます。
                            status.GeneratedWithVersion = 0;
                            status.Phase = ChunkGenerationPhase.Failed;
                        }
                        else
                        {
                            // 3つのBufferを確定してからReadyへ進め、半分だけ完成した結果を表示側へ渡さないようにします。
                            manager.GetBuffer<GeneratedTile>(slot.Entity).CopyFrom(slot.Tiles);
                            manager.GetBuffer<GeneratedResourceTile>(slot.Entity).CopyFrom(slot.Resources);
                            manager.GetBuffer<GeneratedHydrologyCell>(slot.Entity).CopyFrom(slot.HydrologyCells);
                            status.GeneratedWithVersion = slot.GeneratorVersion;
                            status.Phase = ChunkGenerationPhase.ReadyToApply;
                        }
                        manager.SetComponentData(slot.Entity, status);
                    }
                }

                // 破棄済みEntityや変更済み要求の結果は捨て、配列の容量だけを再利用します。
                slot.Running = false;
                slot.Handle = default;
                slots[index] = slot;
            }
        }

        /// <summary>実行中枠数を上限内に保ち、Queued要求だけを専用配列のJobへ渡します。</summary>
        /// <param name="manager">要求と生成状態を所有する同じWorldです。</param>
        /// <param name="settings">値コピーしてJobへ渡す地形設定です。</param>
        /// <param name="resourceSettings">値コピーしてJobへ渡す資源設定です。</param>
        /// <param name="limit">1以上へ正規化済みの同時生成上限です。</param>
        /// <returns>1件以上のQueued要求をJobへ投入した場合はtrueです。</returns>
        private bool ScheduleRequests(EntityManager manager, WorldGenerationSettings settings,
            ResourceGenerationSettings resourceSettings, int limit)
        {
            int running = 0;
            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index].Running)
                {
                    running++;
                }
            }

            if (running >= limit || requestQuery.IsEmpty)
            {
                return false;
            }
            using NativeArray<Entity> requests = requestQuery.ToEntityArray(Allocator.Temp);
            using NativeList<OrderedRequest> ordered = new NativeList<OrderedRequest>(requests.Length, Allocator.Temp);
            // Entity配列の順番は構造変更・作成順で変わるため、バッチを区切る前に論理座標で整列します。
            foreach (Entity entity in requests)
            {
                ChunkCoordinateComponent coordinate = manager.GetComponentData<ChunkCoordinateComponent>(entity);
                if (manager.GetComponentData<ChunkGenerationStatus>(entity).Phase == ChunkGenerationPhase.Queued
                    && coordinate.Layer == WorldLayer.Surface)
                {
                    // WORLD-01Cでは既存地上結果だけを接続します。別層の自然生成はTERRAIN-01Cが
                    // 専用規則を追加するまでQueuedのまま残し、地上データの複製を公開しません。
                    ordered.Add(new OrderedRequest { Entity = entity, Coordinate = coordinate });
                }
            }
            ordered.AsArray().Sort();
            bool scheduled = false;
            for (int index = 0; index < ordered.Length && running < limit; index++)
            {
                Entity entity = ordered[index].Entity;
                ChunkGenerationStatus status = manager.GetComponentData<ChunkGenerationStatus>(entity);
                if (status.Phase != ChunkGenerationPhase.Queued)
                {
                    continue;
                }
                int slotIndex = GetAvailableSlot();
                GenerationSlot slot = slots[slotIndex];
                slot.Entity = entity;
                slot.Coordinate = manager.GetComponentData<ChunkCoordinateComponent>(entity);
                slot.GeneratorVersion = settings.GeneratorVersion;
                slot.Running = true;
                // 8水文行と地形Jobが別配列へ書くため、並行実行前に両方の失敗欄を初期化します。
                for (int workItemIndex = 0;
                    workItemIndex < slot.HydrologyGenerationFailed.Length;
                    workItemIndex++)
                {
                    slot.HydrologyGenerationFailed[workItemIndex] = 0;
                }
                slot.TerrainGenerationFailed[0] = 0;
                slot.HydrologyLakeWorkItemCount[0] = 0;
                GenerateHydrologyRowsJob hydrologyJob = new GenerateHydrologyRowsJob
                {
                    Settings = settings,
                    Coordinate = slot.Coordinate,
                    HydrologyCells = slot.HydrologyCells,
                    HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                    GenerationFailed = slot.HydrologyGenerationFailed
                };
                GenerateTerrainJob terrainJob = new GenerateTerrainJob
                {
                    Settings = settings,
                    ResourceSettings = resourceSettings,
                    Coordinate = slot.Coordinate,
                    Tiles = slot.Tiles,
                    Resources = slot.Resources,
                    GenerationFailed = slot.TerrainGenerationFailed
                };
                ResolveChunkLakesJob lakeJob = new ResolveChunkLakesJob
                {
                    Settings = settings,
                    HydrologyCells = slot.HydrologyCells,
                    HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                    HydrologyBasinMembers = slot.HydrologyBasinMembers,
                    HydrologyBasinLookup = slot.HydrologyBasinLookup,
                    HydrologyLakeWorkItems = slot.HydrologyLakeWorkItems,
                    HydrologyLakeWorkItemCount = slot.HydrologyLakeWorkItemCount,
                    GenerationFailed = slot.HydrologyGenerationFailed
                };
                PrepareChunkLakesJob prepareLakeJob = new PrepareChunkLakesJob
                {
                    HydrologyCells = slot.HydrologyCells,
                    HydrologyLakeWorkItems = slot.HydrologyLakeWorkItems,
                    HydrologyLakeWorkItemCount = slot.HydrologyLakeWorkItemCount,
                    GenerationFailed = slot.HydrologyGenerationFailed
                };

                // 基礎水文の完了後に閉鎖末端を一度分類し、異なる流域を最大8担当へ分けます。
                // 湖系列全体を、独立した地形・資源Jobとは並行させます。
                // 最終Handleを結合してから公開するため、3 Bufferの一括公開境界と決定論は変わりません。
                JobHandle hydrologyHandle = hydrologyJob.Schedule(HydrologyRowWorkItemCount, 1);
                JobHandle prepareLakeHandle = prepareLakeJob.Schedule(hydrologyHandle);
                JobHandle lakeHandle = lakeJob.Schedule(HydrologyRowWorkItemCount, 1, prepareLakeHandle);
                JobHandle terrainHandle = terrainJob.Schedule();
                slot.HydrologyBaseHandle = hydrologyHandle;
                slot.LakeHandle = lakeHandle;
                slot.TerrainHandle = terrainHandle;
                slot.Handle = JobHandle.CombineDependencies(lakeHandle, terrainHandle);
                slots[slotIndex] = slot;
                status.Phase = ChunkGenerationPhase.GeneratingData;
                manager.SetComponentData(entity, status);
                manager.SetComponentEnabled<ChunkGenerationRequest>(entity, false);
                running++;
                scheduled = true;
            }

            // 独立したJobもWorkerへ送信し、次回の完了確認まで投入待ちのまま残らないようにします。
            if (scheduled)
            {
                JobHandle.ScheduleBatchedJobs();
            }

            return scheduled;
        }

        /// <summary>投入バッチの境界をEntity配置から切り離す、同一フレーム／Tick内だけの要求レコードです。</summary>
        private struct OrderedRequest : IComparable<OrderedRequest>
        {
            /// <summary>要求の所有Entityです。比較キーには使用しません。</summary>
            public Entity Entity;
            /// <summary>受付順を決める絶対チャンク座標です。Streamingが同座標の重複要求を防ぎます。</summary>
            public ChunkCoordinateComponent Coordinate;
            /// <summary>Layer、X、Yの辞書順で比較し、同じ要求集合から同じ先頭バッチを選びます。</summary>
            /// <param name="other">比較対象の要求です。</param>
            /// <returns>この要求が先なら負、同座標なら0、後なら正です。</returns>
            public int CompareTo(OrderedRequest other)
            {
                int layer = Coordinate.Layer.CompareTo(other.Coordinate.Layer);
                if (layer != 0)
                {
                    return layer;
                }

                int x = Coordinate.X.CompareTo(other.Coordinate.X);
                return x != 0 ? x : Coordinate.Y.CompareTo(other.Coordinate.Y);
            }
        }

        /// <summary>空き枠を返し、不足時だけ地形・資源各4096件と固定水文64件のPersistent配列を追加確保します。</summary>
        /// <returns>投入するJobの所有情報を書き込む枠の添字です。</returns>
        private int GetAvailableSlot()
        {
            for (int index = 0; index < slots.Length; index++)
            {
                if (!slots[index].Running)
                {
                    return index;
                }
            }

            slots.Add(new GenerationSlot
            {
                Tiles = new NativeArray<GeneratedTile>(WorldGridConstants.TilesPerChunk, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                Resources = new NativeArray<GeneratedResourceTile>(WorldGridConstants.TilesPerChunk, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                HydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                    DeterministicHydrologyGenerator.CellsPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                HydrologyCatchmentFrontier = new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyGenerator.ParallelChunkFrontierCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                HydrologyBasinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyGenerator.ParallelChunkBasinMemberCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                HydrologyBasinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                    DeterministicHydrologyGenerator.ParallelChunkBasinLookupCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                HydrologyLakeWorkItems = new NativeArray<HydrologyLakeChunkWorkItem>(
                    DeterministicHydrologyGenerator.CellsPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                HydrologyLakeWorkItemCount = new NativeArray<int>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                HydrologyGenerationFailed = new NativeArray<byte>(
                    HydrologyRowWorkItemCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                TerrainGenerationFailed = new NativeArray<byte>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory)
            });
            return slots.Length - 1;
        }

        /// <summary>SystemがWorld寿命中に再利用し、1件の生成Jobへ排他的に貸す作業枠です。</summary>
        private struct GenerationSlot
        {
            /// <summary>公開先Entityです。Version込みで保持し、同じIndexの別Entityへ結果を渡しません。</summary>
            public Entity Entity;
            /// <summary>投入時の絶対論理チャンク座標です。公開先が同じ座標か検査するため保持します。</summary>
            public ChunkCoordinateComponent Coordinate;
            /// <summary>生成に使用した版です。完成結果の公開時にECSへ記録します。</summary>
            public int GeneratorVersion;
            /// <summary>trueの間はJobが配列を所有し、Systemは完了確認だけを行います。</summary>
            public bool Running;
            /// <summary>完了確認とWorld終了時の同期をSystemだけが行う専用Handleです。</summary>
            public JobHandle Handle;
            /// <summary>基礎水文8行の完了待ちを湖処理から分離して計測するHandleです。</summary>
            public JobHandle HydrologyBaseHandle;
            /// <summary>準備処理を含む閉鎖流域・湖面系列の完了待ちを計測するHandleです。</summary>
            public JobHandle LakeHandle;
            /// <summary>水文と並行する地形・資源系列の完了待ちを計測するHandleです。</summary>
            public JobHandle TerrainHandle;
            /// <summary>地形4096件の作業配列です。枠の再利用中は保持し、System終了時にDisposeします。</summary>
            public NativeArray<GeneratedTile> Tiles;
            /// <summary>資源4096件の作業配列です。地形配列と同じ所有・解放規則です。</summary>
            public NativeArray<GeneratedResourceTile> Resources;
            /// <summary>中心がチャンク内に属する固定水文64件の作業配列です。枠の再利用中は保持します。</summary>
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;
            /// <summary>
            /// 8水文行へ1領域ずつ貸す上流候補Heapです。行同士は領域を共有せず、System終了時に破棄します。
            /// </summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;
            /// <summary>8湖担当へ1領域ずつ貸し、各流域の最大4,096セルを保持する作業配列です。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;
            /// <summary>8湖担当へ1領域ずつ貸し、各座標の流域所属をO(1)平均で引く固定Hash表です。</summary>
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;
            /// <summary>閉鎖末端ごとの対象セルMaskを保持し、湖担当間の書込重複を防ぐ作業一覧です。</summary>
            public NativeArray<HydrologyLakeChunkWorkItem> HydrologyLakeWorkItems;
            /// <summary>作業一覧内の有効な閉鎖末端数です。準備Jobだけが書き、湖Jobは読み取ります。</summary>
            public NativeArray<int> HydrologyLakeWorkItemCount;
            /// <summary>8水文行の各作業が失敗時に自分の添字へ1を書く、湖Jobと公開処理への通知領域です。</summary>
            public NativeArray<byte> HydrologyGenerationFailed;
            /// <summary>地形・資源Jobが失敗時に先頭へ1を書く、公開処理への独立通知領域です。</summary>
            public NativeArray<byte> TerrainGenerationFailed;
        }

        /// <summary>固定Tick境界で残り時間を個別計測する生成Job系列です。</summary>
        private enum GenerationStage : byte
        {
            /// <summary>64水文セルの下流・集水結果を作る8行の並列系列です。</summary>
            HydrologyBase = 0,

            /// <summary>閉鎖末端をまとめ、流域境界と湖面を確定する系列です。</summary>
            Lake = 1,

            /// <summary>4,096タイルの地形と資源を作る独立系列です。</summary>
            Terrain = 2
        }

        /// <summary>値コピーの設定と絶対座標だけから、8行の基礎水文を並列生成するBurst Jobです。</summary>
        [BurstCompile]
        private struct GenerateHydrologyRowsJob : IJobParallelFor
        {
            /// <summary>WorldのSingletonから値コピーした、Job実行中は読み取り専用の生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成する絶対チャンク座標です。Jobの実行中は変化しません。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>8つの水文行担当が互いに異なる8要素へ書く、行優先の固定水文配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;

            /// <summary>
            /// 各水文行へ固定容量ずつ割り当てたHeapです。行担当は自分の領域だけを8セルで再利用します。
            /// </summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;

            /// <summary>各水文行が座標範囲外や不正設定を検出した場合、自分と同じ添字へ1を書きます。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>Worker上で指定添字の水文1行を生成し、失敗時だけ通知欄を更新します。</summary>
            /// <param name="rowIndex">0〜7のチャンク内水文行です。</param>
            public void Execute(int rowIndex)
            {
                if (!DeterministicHydrologyGenerator.TryGenerateChunkCellRowWithoutLake(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        rowIndex,
                        HydrologyCells,
                        HydrologyCatchmentFrontier))
                {
                    // 1行でも失敗したチャンクは、他行が成功していても部分的な水文を公開しません。
                    GenerationFailed[rowIndex] = 1;
                }
            }
        }

        /// <summary>水文Jobと独立して、地形・資源4096タイルを生成するBurst Jobです。</summary>
        [BurstCompile]
        private struct GenerateTerrainJob : IJob
        {
            /// <summary>WorldのSingletonから値コピーした、Job実行中は読み取り専用の生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>WorldのSingletonから値コピーした、資源群の大きさと密度設定です。</summary>
            public ResourceGenerationSettings ResourceSettings;

            /// <summary>生成する絶対チャンク座標です。Jobの実行中は変化しません。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>このJobだけが全4096件を上書きし、最終Handle完了後にSystemへ返す地形配列です。</summary>
            public NativeArray<GeneratedTile> Tiles;

            /// <summary>このJobだけが書く、地形と同じ位置に対応した資源配列です。</summary>
            public NativeArray<GeneratedResourceTile> Resources;

            /// <summary>生成失敗時に先頭へ1を書き、3 Bufferすべての公開を止める地形専用通知領域です。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>地質キャッシュを一度構築し、全タイルの地形と対応資源を順に生成します。</summary>
            public void Execute()
            {

                bool hasGeologyCache = DeterministicGeologyGenerator.TryCreateChunkCache(
                    Settings,
                    Coordinate.X,
                    Coordinate.Y,
                    out GeologyChunkCache geologyCache);

                // 各タイルはワールド整数座標だけから求めるため、隣接チャンクの生成済み状態を参照しません。
                for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
                {
                    for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                    {
                        int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                        int worldTileX = WorldGrid.ToWorldTileX(Coordinate.X, localX);
                        int worldTileY = WorldGrid.ToWorldTileY(Coordinate.Y, localY);
                        GeneratedTile terrainTile = default;
                        bool generated = hasGeologyCache
                            && DeterministicChunkGenerator.TryGenerateTile(
                                Settings,
                                geologyCache,
                                worldTileX,
                                worldTileY,
                                out terrainTile);
                        if (!generated)
                        {
                            // このJobだけが地形欄へ書くため原子的操作は不要です。未定義配列も残しません。
                            GenerationFailed[0] = 1;
                            Tiles[tileIndex] = default;
                            Resources[tileIndex] = default;
                            continue;
                        }
                        Tiles[tileIndex] = terrainTile;
                        Resources[tileIndex] = DeterministicResourceGenerator.GenerateResource(
                            Settings,
                            ResourceSettings,
                            terrainTile,
                            worldTileX,
                            worldTileY);
                    }
                }
            }
        }

        /// <summary>基礎水文64件の完了後に閉鎖末端を一意化し、重複しない対象セルMaskを作るBurst Jobです。</summary>
        [BurstCompile]
        private struct PrepareChunkLakesJob : IJob
        {
            /// <summary>8行Jobが生成済みの基礎水文64件です。この準備Jobは読み取りだけを行います。</summary>
            [ReadOnly]
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;

            /// <summary>閉鎖末端座標と対象セルMaskを先頭から書く、最大64件の作業一覧です。</summary>
            public NativeArray<HydrologyLakeChunkWorkItem> HydrologyLakeWorkItems;

            /// <summary>成功時の有効作業数を先頭へ書き、後続の湖Jobへ渡す1要素配列です。</summary>
            public NativeArray<int> HydrologyLakeWorkItemCount;

            /// <summary>基礎8行の失敗を読み、作業構築失敗時は先頭へ1を書く通知領域です。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>基礎生成が全件成功した場合だけ、一意な閉鎖末端と対象セルMaskを構築します。</summary>
            public void Execute()
            {
                HydrologyLakeWorkItemCount[0] = 0;
                // 基礎結果が一部でも欠けていれば湖面を推測せず、既存の失敗状態を公開処理へ渡します。
                for (int workItemIndex = 0; workItemIndex < GenerationFailed.Length; workItemIndex++)
                {
                    if (GenerationFailed[workItemIndex] != 0)
                    {
                        return;
                    }
                }

                if (!DeterministicHydrologyGenerator.TryBuildChunkLakeWorkItems(
                        HydrologyCells,
                        HydrologyLakeWorkItems,
                        out int workItemCount))
                {
                    // 最終公開では基礎生成と作業構築の原因を区別せず、部分結果を拒否します。
                    GenerationFailed[0] = 1;
                    return;
                }

                HydrologyLakeWorkItemCount[0] = workItemCount;
            }
        }

        /// <summary>異なる閉鎖末端を最大8担当で並列解決し、互いに重ならないセルへ湖面を適用するBurst Jobです。</summary>
        [BurstCompile]
        private struct ResolveChunkLakesJob : IJobParallelFor
        {
            /// <summary>湖候補閾値、流域上限、標高生成値を含む固定ワールド生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>担当Maskが互いに重ならないため、各担当が別セルだけへ湖面結果を書きます。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;

            /// <summary>8担当へ固定Heap領域を一つずつ割り当てる上流候補配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;

            /// <summary>8担当へ4,096件領域を一つずつ割り当てる流域セル配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;

            /// <summary>8担当へ8,192スロット領域を一つずつ割り当てる流域所属Hash表です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;

            /// <summary>準備Jobが作成した、閉鎖末端ごとの重複しない対象セルMaskです。</summary>
            [ReadOnly]
            public NativeArray<HydrologyLakeChunkWorkItem> HydrologyLakeWorkItems;

            /// <summary>作業一覧の有効要素数を持つ、準備Job完了後は読み取り専用の1要素配列です。</summary>
            [ReadOnly]
            public NativeArray<int> HydrologyLakeWorkItemCount;

            /// <summary>湖担当が失敗時に自分の添字へ1を書き、部分結果の公開を止める通知領域です。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>指定担当に割り当てられた閉鎖流域を順に解決し、失敗時だけ自分の通知欄を更新します。</summary>
            /// <param name="laneIndex">0〜7の湖担当番号で、専用ワーク領域の番号でもあります。</param>
            public void Execute(int laneIndex)
            {
                if (!DeterministicHydrologyGenerator.TryApplyChunkLakeWorkItemsLane(
                        Settings,
                        HydrologyCells,
                        HydrologyLakeWorkItems,
                        HydrologyLakeWorkItemCount[0],
                        laneIndex,
                        HydrologyRowWorkItemCount,
                        HydrologyCatchmentFrontier,
                        HydrologyBasinMembers,
                        HydrologyBasinLookup))
                {
                    GenerationFailed[laneIndex] = 1;
                }
            }
        }
    }

    /// <summary>
    /// Tick加算後・Command処理前に完成済み地形を一括公開し、次の有限バッチを投入する個体群World専用境界です。
    /// 後続Gameplayの個体群初期化は、この境界で揃った地域全体へ同じTickでIDを発行します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareFixedTickSimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(SimulationTickAdvanceSystem))]
    [UpdateBefore(typeof(FarmwareCommandProcessingSystemGroup))]
    public partial struct ChunkGenerationFixedTickPublicationSystem : ISystem
    {
        /// <summary>地形単独Worldでは固定Tickバリアを追加せず、個体群とTickがあるWorldだけで動作させます。</summary>
        /// <param name="state">更新条件を登録するSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AnimalSpeciesCatalog>();
            state.RequireForUpdate<SimulationTickState>();
            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
        }

        /// <summary>同じWorldの生成Systemを探し、完成確認済みバッチを公開して次のバッチを非同期投入します。</summary>
        /// <param name="state">既存Systemの参照と公開先EntityManagerを取得するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            SystemHandle generation = state.WorldUnmanaged.GetExistingUnmanagedSystem<ChunkGenerationSystem>();
            if (generation == SystemHandle.Null)
            {
                return;
            }

            // 専用Jobと配列の所有権は生成Systemに残します。メインスレッド上で既存Systemのメソッドだけを
            // 呼び、参照をフィールドへ保持しないことでSystem破棄後や別Worldへ持ち出すことを避けます。
            ref ChunkGenerationSystem generator =
                ref state.WorldUnmanaged.GetUnsafeSystemRef<ChunkGenerationSystem>(generation);
            int limit = SystemAPI.TryGetSingleton(out ChunkStreamingSettings streamingSettings)
                ? streamingSettings.MaximumConcurrentGenerations
                : ChunkStreamingSettings.CreateDefault().MaximumConcurrentGenerations;
            // Rate Managerが未完成中のTick開始を止めるため、通常経路ではここへ完成済みバッチだけが到達します。
            // 防御的な再確認で未完成なら公開も追加投入もせず、部分バッチをGameplayへ渡しません。
            if (!generator.TryCompleteAndPublishBatch(state.EntityManager))
            {
                generator.RefreshFixedTickReadiness(state.EntityManager, true, false);
                return;
            }

            // 公開した同じTickのGameplayは完成地域を座標順で初期化します。次バッチはWorkerへ渡すだけに留め、
            // Rate Managerが同一描画フレームの追いつきTickを止めるため、Main Threadでは完了を待ちません。
            bool scheduled = generator.ScheduleBatch(
                state.EntityManager,
                SystemAPI.GetSingleton<WorldGenerationSettings>(),
                SystemAPI.GetSingleton<ResourceGenerationSettings>(),
                limit);
            generator.RefreshFixedTickReadiness(state.EntityManager, true, scheduled);
        }
    }
}
