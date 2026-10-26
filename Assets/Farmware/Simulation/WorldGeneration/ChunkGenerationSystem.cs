// このファイルは、地形・Blockセル・資源・固定水文・表流水・地下水・湖水収支・泉・自然冠水をECS外の作業領域で生成し、完成結果だけをECSへ公開します。
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
    /// 有効なChunkGenerationRequestを持つチャンクだけを生成し、地形・資源・固定水文・表流水・地下水・湖水収支・泉・自然冠水を対応付けるECS Systemです。
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

        // 完成確認済みの河道幅・湖面候補細分化Handleを安全に確定する時間です。
        private static readonly ProfilerMarker SurfaceWaterWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationSurfaceWaterWait");

        // 完成済みの地下水・自然土壌飽和Handleを安全に確定する時間です。
        private static readonly ProfilerMarker GroundwaterWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationGroundwaterWait");

        // 完成済みの湖最終区分・泉Handleを安全に確定する時間です。
        private static readonly ProfilerMarker LakeSpringWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationLakeSpringWait");

        // 完成済みの自然冠水・湿地広がりHandleを安全に確定する時間です。
        private static readonly ProfilerMarker NaturalFloodingWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationNaturalFloodingWait");

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
                .WithAll<GeneratedTile, GeneratedTerrainCell, GeneratedResourceTile,
                    GeneratedHydrologyCell, GeneratedSurfaceWaterTile, GeneratedHydrologyTile>()
                .Build(ref state);
            fixedTickReadinessQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<FixedTickReadinessState>()
                .Build(ref state);
            slots = new NativeList<GenerationSlot>(
                ChunkStreamingSettings.CreateDefault().MaximumConcurrentGenerations, Allocator.Persistent);
            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<BiomeClassificationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
            state.RequireForUpdate<TerrainBlockCatalog>();
            state.RequireForUpdate<TerrainBlockPaletteCatalog>();
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
            bool scheduled = ScheduleBatch(
                state.EntityManager,
                SystemAPI.GetSingleton<WorldGenerationSettings>(),
                SystemAPI.GetSingleton<BiomeClassificationSettings>(),
                SystemAPI.GetSingleton<ResourceGenerationSettings>(),
                SystemAPI.GetSingleton<TerrainBlockPaletteCatalog>().Value,
                SystemAPI.GetSingleton<TerrainBlockCatalog>().BuiltInRuntimeIds,
                limit);
            RefreshFixedTickReadiness(state.EntityManager, fixedTickPublication && !isPaused, scheduled);
        }

        /// <summary>
        /// Tick境界または描画フレームで呼び、座標順の要求から上限までJobを投入します。完成確認と公開は後続更新へ分離します。
        /// </summary>
        /// <param name="manager">このSystemと同じWorldのEntityManagerです。</param>
        /// <param name="settings">投入時点で固定する地形生成設定です。</param>
        /// <param name="biomeSettings">投入時点で固定する新18Biome Definitionです。</param>
        /// <param name="resourceSettings">投入時点で固定する資源生成設定です。</param>
        /// <param name="paletteCatalog">Biome・地質別Block候補を保持する読み取り専用Blobです。</param>
        /// <param name="terrainRuntimeIds">海水・淡水・マグマなど固定用途へ解決済みのRuntime IDです。</param>
        /// <param name="maximumConcurrentGenerations">同時実行上限です。1未満は1へ正規化します。</param>
        /// <returns>この呼出しで1件以上の新しい生成Jobを投入した場合はtrueです。</returns>
        public bool ScheduleBatch(
            EntityManager manager,
            WorldGenerationSettings settings,
            BiomeClassificationSettings biomeSettings,
            ResourceGenerationSettings resourceSettings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
            BuiltInTerrainBlockRuntimeIds terrainRuntimeIds,
            int maximumConcurrentGenerations)
        {
            using (ScheduleProfilerMarker.Auto())
            {
                return ScheduleRequests(
                    manager,
                    settings,
                    biomeSettings,
                    resourceSettings,
                    paletteCatalog,
                    terrainRuntimeIds,
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
                using (SurfaceWaterWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.SurfaceWater);
                }
                using (TerrainWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.Terrain);
                }
                using (GroundwaterWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.Groundwater);
                }
                using (LakeSpringWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.LakeSpring);
                }
                using (NaturalFloodingWaitProfilerMarker.Auto())
                {
                    CompleteRunningStage(GenerationStage.NaturalFlooding);
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
        /// <param name="stage">基礎水文から自然冠水まで、確定費用を分離する生成系列です。</param>
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
                    case GenerationStage.SurfaceWater:
                        stageHandle = slot.SurfaceWaterHandle;
                        break;
                    case GenerationStage.Terrain:
                        stageHandle = slot.TerrainHandle;
                        break;
                    case GenerationStage.Groundwater:
                        stageHandle = slot.GroundwaterHandle;
                        break;
                    case GenerationStage.LakeSpring:
                        stageHandle = slot.LakeSpringHandle;
                        break;
                    default:
                        stageHandle = slot.NaturalFloodingHandle;
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
                slot.TerrainCells.Dispose();
                slot.Resources.Dispose();
                slot.HydrologyCells.Dispose();
                slot.HydrologyCatchmentFrontier.Dispose();
                slot.HydrologyBasinMembers.Dispose();
                slot.HydrologyBasinLookup.Dispose();
                slot.HydrologyLakeWorkItems.Dispose();
                slot.HydrologyLakeWorkItemCount.Dispose();
                slot.SurfaceWater.Dispose();
                slot.SurfaceWaterPathWorkspace.Dispose();
                slot.SurfaceWaterPathTiles.Dispose();
                slot.SurfaceWaterFloodVisited.Dispose();
                slot.SurfaceWaterFloodQueue.Dispose();
                slot.IntersectingHydrologyCells.Dispose();
                slot.SurfaceRechargeCandidates.Dispose();
                slot.HydrologyTiles.Dispose();
                slot.SpringSurfaceRechargeCandidates.Dispose();
                slot.SpringHydrologyCells.Dispose();
                slot.SpringCandidates.Dispose();
                slot.LakeSpringHydrologyGenerationFailed.Dispose();
                slot.HydrologyGenerationFailed.Dispose();
                slot.TerrainGenerationFailed.Dispose();
                slot.SurfaceWaterGenerationFailed.Dispose();
                slot.GroundwaterGenerationFailed.Dispose();
                slot.LakeSpringGenerationFailed.Dispose();
                slot.NaturalFloodingScratchHydrologyCells.Dispose();
                slot.NaturalFloodingScratchIntersectingHydrologyCells.Dispose();
                slot.NaturalFloodingScratchSurfaceRechargeCandidates.Dispose();
                slot.NaturalFloodingScratchSurfaceWater.Dispose();
                slot.NaturalFloodingSourceWater.Dispose();
                slot.WetlandEvaluationHydrologyCells.Dispose();
                slot.WetlandEvaluationHydrologyCellWritten.Dispose();
                slot.WetlandCandidateCells.Dispose();
                slot.LargeWetlandExtentCells.Dispose();
                slot.WetlandVisitedCells.Dispose();
                slot.WetlandCellQueue.Dispose();
                slot.NaturalFloodingGenerationFailed.Dispose();
            }

            // Systemだけが先に破棄されるテストWorldでも、存在しない生成Jobを理由に固定Tickを止め続けません。
            RefreshFixedTickReadiness(state.EntityManager, false, false);
            slots.Dispose();
        }

        /// <summary>完成した作業枠だけを読み、存続する要求Entityへ6種類の固定生成Bufferをまとめて公開します。</summary>
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
                    && manager.HasBuffer<GeneratedTerrainCell>(slot.Entity)
                    && manager.HasBuffer<GeneratedResourceTile>(slot.Entity)
                    && manager.HasBuffer<GeneratedHydrologyCell>(slot.Entity)
                    && manager.HasBuffer<GeneratedSurfaceWaterTile>(slot.Entity)
                    && manager.HasBuffer<GeneratedHydrologyTile>(slot.Entity))
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
                        generationFailed |= slot.SurfaceWaterGenerationFailed[0] != 0;
                        generationFailed |= slot.GroundwaterGenerationFailed[0] != 0;
                        generationFailed |= slot.LakeSpringGenerationFailed[0] != 0;
                        generationFailed |= slot.NaturalFloodingGenerationFailed[0] != 0;

                        if (generationFailed)
                        {
                            // 固定半径サンプルがワールド座標域を越えた場合は、不完全なBufferを公開せず明示的な失敗へ進めます。
                            status.GeneratedWithVersion = 0;
                            status.Phase = ChunkGenerationPhase.Failed;
                        }
                        else
                        {
                            // 6つのBufferを確定してからReadyへ進め、半分だけ完成した結果を後続Systemへ渡しません。
                            manager.GetBuffer<GeneratedTile>(slot.Entity).CopyFrom(slot.Tiles);
                            manager.GetBuffer<GeneratedTerrainCell>(slot.Entity).CopyFrom(slot.TerrainCells);
                            manager.GetBuffer<GeneratedResourceTile>(slot.Entity).CopyFrom(slot.Resources);
                            manager.GetBuffer<GeneratedHydrologyCell>(slot.Entity).CopyFrom(slot.HydrologyCells);
                            manager.GetBuffer<GeneratedSurfaceWaterTile>(slot.Entity).CopyFrom(slot.SurfaceWater);
                            manager.GetBuffer<GeneratedHydrologyTile>(slot.Entity).CopyFrom(slot.HydrologyTiles);
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
        /// <param name="biomeSettings">値コピーしてJobへ渡す18Biome分類設定です。</param>
        /// <param name="resourceSettings">値コピーしてJobへ渡す資源設定です。</param>
        /// <param name="paletteCatalog">Jobが自然Ground・Wallを選ぶ固定Blockパレットです。</param>
        /// <param name="terrainRuntimeIds">Jobが固定液体を直接設定する解決済みIDです。</param>
        /// <param name="limit">1以上へ正規化済みの同時生成上限です。</param>
        /// <returns>1件以上のQueued要求をJobへ投入した場合はtrueです。</returns>
        private bool ScheduleRequests(
            EntityManager manager,
            WorldGenerationSettings settings,
            BiomeClassificationSettings biomeSettings,
            ResourceGenerationSettings resourceSettings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
            BuiltInTerrainBlockRuntimeIds terrainRuntimeIds,
            int limit)
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
                slot.SurfaceWaterGenerationFailed[0] = 0;
                slot.GroundwaterGenerationFailed[0] = 0;
                slot.LakeSpringGenerationFailed[0] = 0;
                for (int sourceLaneIndex = 0;
                    sourceLaneIndex < slot.NaturalFloodingGenerationFailed.Length;
                    sourceLaneIndex++)
                {
                    slot.NaturalFloodingGenerationFailed[sourceLaneIndex] = 0;
                }
                for (int workItemIndex = 0;
                    workItemIndex < slot.LakeSpringHydrologyGenerationFailed.Length;
                    workItemIndex++)
                {
                    slot.LakeSpringHydrologyGenerationFailed[workItemIndex] = 0;
                }
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
                    Coordinate = slot.Coordinate,
                    Tiles = slot.Tiles,
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
                GenerateSurfaceWaterJob surfaceWaterJob = new GenerateSurfaceWaterJob
                {
                    Settings = settings,
                    Coordinate = slot.Coordinate,
                    HydrologyCells = slot.HydrologyCells,
                    HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                    HydrologyBasinMembers = slot.HydrologyBasinMembers,
                    HydrologyBasinLookup = slot.HydrologyBasinLookup,
                    PathWorkspace = slot.SurfaceWaterPathWorkspace,
                    PathTiles = slot.SurfaceWaterPathTiles,
                    FloodVisited = slot.SurfaceWaterFloodVisited,
                    FloodQueue = slot.SurfaceWaterFloodQueue,
                    IntersectingHydrologyCells = slot.IntersectingHydrologyCells,
                    SurfaceRechargeCandidates = slot.SurfaceRechargeCandidates,
                    SpringSurfaceRechargeCandidates = slot.SpringSurfaceRechargeCandidates,
                    SurfaceWater = slot.SurfaceWater,
                    GenerationFailed = slot.SurfaceWaterGenerationFailed
                };
                GenerateGroundwaterHydrologyJob groundwaterJob = new GenerateGroundwaterHydrologyJob
                {
                    Settings = settings,
                    Coordinate = slot.Coordinate,
                    Tiles = slot.Tiles,
                    HydrologyCells = slot.HydrologyCells,
                    IntersectingHydrologyCells = slot.IntersectingHydrologyCells,
                    SurfaceRechargeCandidates = slot.SurfaceRechargeCandidates,
                    HydrologyTiles = slot.HydrologyTiles,
                    SurfaceWaterGenerationFailed = slot.SurfaceWaterGenerationFailed,
                    TerrainGenerationFailed = slot.TerrainGenerationFailed,
                    GenerationFailed = slot.GroundwaterGenerationFailed
                };
                PrepareLakeSpringHydrologyRowsJob prepareLakeSpringHydrologyJob =
                    new PrepareLakeSpringHydrologyRowsJob
                    {
                        Settings = settings,
                        Coordinate = slot.Coordinate,
                        IntersectingHydrologyCells = slot.IntersectingHydrologyCells,
                        HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                        HydrologyBasinMembers = slot.HydrologyBasinMembers,
                        HydrologyBasinLookup = slot.HydrologyBasinLookup,
                        SpringHydrologyCells = slot.SpringHydrologyCells,
                        GroundwaterGenerationFailed = slot.GroundwaterGenerationFailed,
                        GenerationFailed = slot.LakeSpringHydrologyGenerationFailed
                    };
                FinalizeLakeSpringJob lakeSpringJob = new FinalizeLakeSpringJob
                {
                    Settings = settings,
                    Coordinate = slot.Coordinate,
                    HydrologyCells = slot.HydrologyCells,
                    IntersectingHydrologyCells = slot.IntersectingHydrologyCells,
                    SurfaceWater = slot.SurfaceWater,
                    HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                    HydrologyBasinMembers = slot.HydrologyBasinMembers,
                    HydrologyBasinLookup = slot.HydrologyBasinLookup,
                    PathWorkspace = slot.SurfaceWaterPathWorkspace,
                    PathTiles = slot.SurfaceWaterPathTiles,
                    SpringSurfaceRechargeCandidates = slot.SpringSurfaceRechargeCandidates,
                    SpringHydrologyCells = slot.SpringHydrologyCells,
                    SpringCandidates = slot.SpringCandidates,
                    GroundwaterGenerationFailed = slot.GroundwaterGenerationFailed,
                    SpringHydrologyGenerationFailed = slot.LakeSpringHydrologyGenerationFailed,
                    GenerationFailed = slot.LakeSpringGenerationFailed
                };
                GenerateNaturalFloodingSourceChunksJob naturalFloodingFirstWaveJob =
                    new GenerateNaturalFloodingSourceChunksJob
                    {
                        Settings = settings,
                        Coordinate = slot.Coordinate,
                        SourceLaneOffset = 0,
                        HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                        HydrologyBasinMembers = slot.HydrologyBasinMembers,
                        HydrologyBasinLookup = slot.HydrologyBasinLookup,
                        PathWorkspace = slot.SurfaceWaterPathWorkspace,
                        PathTiles = slot.SurfaceWaterPathTiles,
                        FloodVisited = slot.SurfaceWaterFloodVisited,
                        FloodQueue = slot.SurfaceWaterFloodQueue,
                        ScratchHydrologyCells = slot.NaturalFloodingScratchHydrologyCells,
                        ScratchIntersectingHydrologyCells =
                            slot.NaturalFloodingScratchIntersectingHydrologyCells,
                        ScratchSurfaceRechargeCandidates =
                            slot.NaturalFloodingScratchSurfaceRechargeCandidates,
                        ScratchSurfaceWater = slot.NaturalFloodingScratchSurfaceWater,
                        SourceWaterArea = slot.NaturalFloodingSourceWater,
                        LakeSpringGenerationFailed = slot.LakeSpringGenerationFailed,
                        GenerationFailed = slot.NaturalFloodingGenerationFailed
                    };
                GenerateNaturalFloodingSourceChunksJob naturalFloodingSecondWaveJob =
                    naturalFloodingFirstWaveJob;
                naturalFloodingSecondWaveJob.SourceLaneOffset =
                    DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount;
                FinalizeNaturalFloodingWetlandJob naturalFloodingJob =
                    new FinalizeNaturalFloodingWetlandJob
                    {
                        Settings = settings,
                        Coordinate = slot.Coordinate,
                        Tiles = slot.Tiles,
                        HydrologyTiles = slot.HydrologyTiles,
                        SurfaceWater = slot.SurfaceWater,
                        SpringHydrologyCells = slot.SpringHydrologyCells,
                        HydrologyCatchmentFrontier = slot.HydrologyCatchmentFrontier,
                        HydrologyBasinMembers = slot.HydrologyBasinMembers,
                        HydrologyBasinLookup = slot.HydrologyBasinLookup,
                        ScratchIntersectingHydrologyCells =
                            slot.NaturalFloodingScratchIntersectingHydrologyCells,
                        ScratchSurfaceRechargeCandidates =
                            slot.NaturalFloodingScratchSurfaceRechargeCandidates,
                        SourceWaterArea = slot.NaturalFloodingSourceWater,
                        EvaluationHydrologyCells = slot.WetlandEvaluationHydrologyCells,
                        EvaluationHydrologyCellWritten =
                            slot.WetlandEvaluationHydrologyCellWritten,
                        CandidateCells = slot.WetlandCandidateCells,
                        LargeExtentCells = slot.LargeWetlandExtentCells,
                        VisitedCells = slot.WetlandVisitedCells,
                        CellQueue = slot.WetlandCellQueue,
                        LakeSpringGenerationFailed = slot.LakeSpringGenerationFailed,
                        GenerationFailed = slot.NaturalFloodingGenerationFailed
                    };
                FinalizeBiomeTerrainJob biomeTerrainJob = new FinalizeBiomeTerrainJob
                {
                    WorldSettings = settings,
                    BiomeSettings = biomeSettings,
                    ResourceSettings = resourceSettings,
                    PaletteCatalog = paletteCatalog,
                    TerrainRuntimeIds = terrainRuntimeIds,
                    Coordinate = slot.Coordinate,
                    Tiles = slot.Tiles,
                    TerrainCells = slot.TerrainCells,
                    HydrologyTiles = slot.HydrologyTiles,
                    SurfaceWater = slot.SurfaceWater,
                    Resources = slot.Resources,
                    GenerationFailed = slot.NaturalFloodingGenerationFailed
                };

                // 基礎水文の完了後に閉鎖末端を一度分類し、異なる流域を最大8担当へ分けます。
                // 湖系列全体を、独立した地形・資源Jobとは並行させます。
                // 湖面確定後に表流水を細分化し、地形・資源Jobは引き続き独立して並行させます。
                // 地下水Jobは完成した地形と表流水の両方へ依存します。
                // 湖水収支・泉Job後は、周囲8チャンクを4担当ずつ2波で再構築してから最終評価します。
                // 4件分の巨大Flood Fill領域を再利用し、8件分を常駐させずにWorker並列性を確保します。
                // Blockセルを含む6 Bufferを同じ最終Jobと公開境界へ束ね、旧分類との部分公開を防ぎます。
                JobHandle hydrologyHandle = hydrologyJob.Schedule(HydrologyRowWorkItemCount, 1);
                JobHandle prepareLakeHandle = prepareLakeJob.Schedule(hydrologyHandle);
                JobHandle lakeHandle = lakeJob.Schedule(HydrologyRowWorkItemCount, 1, prepareLakeHandle);
                JobHandle surfaceWaterHandle = surfaceWaterJob.Schedule(lakeHandle);
                JobHandle terrainHandle = terrainJob.Schedule();
                JobHandle groundwaterInputHandle =
                    JobHandle.CombineDependencies(surfaceWaterHandle, terrainHandle);
                JobHandle groundwaterHandle = groundwaterJob.Schedule(groundwaterInputHandle);
                JobHandle lakeSpringHydrologyHandle = prepareLakeSpringHydrologyJob.Schedule(
                    HydrologyRowWorkItemCount,
                    1,
                    groundwaterHandle);
                JobHandle lakeSpringHandle = lakeSpringJob.Schedule(lakeSpringHydrologyHandle);
                JobHandle naturalFloodingFirstWaveHandle = naturalFloodingFirstWaveJob.Schedule(
                    DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    1,
                    lakeSpringHandle);
                JobHandle naturalFloodingSecondWaveHandle = naturalFloodingSecondWaveJob.Schedule(
                    DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    1,
                    naturalFloodingFirstWaveHandle);
                JobHandle naturalFloodingHandle = naturalFloodingJob.Schedule(
                    naturalFloodingSecondWaveHandle);
                JobHandle biomeTerrainHandle = biomeTerrainJob.Schedule(naturalFloodingHandle);
                slot.HydrologyBaseHandle = hydrologyHandle;
                slot.LakeHandle = lakeHandle;
                slot.SurfaceWaterHandle = surfaceWaterHandle;
                slot.TerrainHandle = terrainHandle;
                slot.GroundwaterHandle = groundwaterHandle;
                slot.LakeSpringHandle = lakeSpringHandle;
                slot.NaturalFloodingHandle = naturalFloodingHandle;
                slot.Handle = biomeTerrainHandle;
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

        /// <summary>空き枠を返し、不足時だけ6出力Bufferと固定上限の中間作業用Persistent配列を追加確保します。</summary>
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
                TerrainCells = new NativeArray<GeneratedTerrainCell>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Persistent,
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
                SurfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SurfaceWaterPathWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                    DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                        * DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SurfaceWaterPathTiles = new NativeArray<SurfaceWaterPathTile>(
                    DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                        * DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SurfaceWaterFloodVisited = new NativeArray<byte>(
                    DeterministicSurfaceWaterGenerator.MaximumFloodTileCount
                        * DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SurfaceWaterFloodQueue = new NativeArray<int>(
                    DeterministicSurfaceWaterGenerator.MaximumFloodTileCount
                        * DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                IntersectingHydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                    DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SurfaceRechargeCandidates = new NativeArray<SurfaceRechargeCandidate>(
                    DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                HydrologyTiles = new NativeArray<GeneratedHydrologyTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SpringHydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                    DeterministicLakeSpringGenerator.SpringHydrologyCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                SpringSurfaceRechargeCandidates = new NativeArray<SurfaceRechargeCandidate>(
                    DeterministicLakeSpringGenerator.SpringRechargeCandidateCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                SpringCandidates = new NativeArray<SpringCandidate>(
                    DeterministicLakeSpringGenerator.SpringCandidateCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                LakeSpringHydrologyGenerationFailed = new NativeArray<byte>(
                    HydrologyRowWorkItemCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                HydrologyGenerationFailed = new NativeArray<byte>(
                    HydrologyRowWorkItemCount,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                TerrainGenerationFailed = new NativeArray<byte>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                SurfaceWaterGenerationFailed = new NativeArray<byte>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                GroundwaterGenerationFailed = new NativeArray<byte>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                LakeSpringGenerationFailed = new NativeArray<byte>(
                    1,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory),
                NaturalFloodingScratchHydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                    DeterministicHydrologyGenerator.CellsPerChunk
                        * DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                NaturalFloodingScratchIntersectingHydrologyCells =
                    new NativeArray<GeneratedHydrologyCell>(
                        DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount
                            * DeterministicWetlandExtentGenerator.SourceChunkLaneCount,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                NaturalFloodingScratchSurfaceRechargeCandidates =
                    new NativeArray<SurfaceRechargeCandidate>(
                        DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount
                            * DeterministicWetlandExtentGenerator.SourceChunkLaneCount,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                NaturalFloodingScratchSurfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk
                        * DeterministicWetlandExtentGenerator.ParallelSourceWorkspaceLaneCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                WetlandEvaluationHydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                WetlandEvaluationHydrologyCellWritten = new NativeArray<byte>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                NaturalFloodingSourceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                    DeterministicWetlandExtentGenerator.SourceAreaTileCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                WetlandCandidateCells = new NativeArray<byte>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                LargeWetlandExtentCells = new NativeArray<byte>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                WetlandVisitedCells = new NativeArray<byte>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                WetlandCellQueue = new NativeArray<int>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory),
                NaturalFloodingGenerationFailed = new NativeArray<byte>(
                    DeterministicWetlandExtentGenerator.SourceChunkLaneCount,
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
            /// <summary>河道幅と連続湖面候補を4096タイルへ細分化する系列の完了待ちを計測するHandleです。</summary>
            public JobHandle SurfaceWaterHandle;
            /// <summary>水文と並行する地形・資源系列の完了待ちを計測するHandleです。</summary>
            public JobHandle TerrainHandle;
            /// <summary>地形と表流水の後、湖・泉より前に地下水・自然土壌飽和を生成するHandleです。</summary>
            public JobHandle GroundwaterHandle;
            /// <summary>地下水後に湖最終区分と泉を表流水・水文へ反映する最終系列のHandleです。</summary>
            public JobHandle LakeSpringHandle;
            /// <summary>湖・泉確定後に自然冠水と広域湿原を生成する最終系列のHandleです。</summary>
            public JobHandle NaturalFloodingHandle;
            /// <summary>地形4096件の作業配列です。枠の再利用中は保持し、System終了時にDisposeします。</summary>
            public NativeArray<GeneratedTile> Tiles;
            /// <summary>完成Blockセル4096件の作業配列です。最終Jobが全件上書きし、完成時だけECSへ公開します。</summary>
            public NativeArray<GeneratedTerrainCell> TerrainCells;
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
            /// <summary>河道、河川幅、湖面候補を行優先4096件で保持し、完成時にECSへコピーする配列です。</summary>
            public NativeArray<GeneratedSurfaceWaterTile> SurfaceWater;
            /// <summary>通常生成は先頭、自然冠水は4担当へ分割して使う河道探索ノード配列です。</summary>
            public NativeArray<SurfaceWaterPathSearchNode> SurfaceWaterPathWorkspace;
            /// <summary>通常生成は先頭、自然冠水は4担当へ分割して使う河道経路配列です。</summary>
            public NativeArray<SurfaceWaterPathTile> SurfaceWaterPathTiles;
            /// <summary>通常生成は先頭、自然冠水は4担当へ分割して使う湖Flood Fill訪問配列です。</summary>
            public NativeArray<byte> SurfaceWaterFloodVisited;
            /// <summary>通常生成は先頭、自然冠水は4担当へ分割して使う湖Flood Fill待ち行列です。</summary>
            public NativeArray<int> SurfaceWaterFloodQueue;
            /// <summary>64×64チャンクと交差する9×9完成水文を地下水Jobへ渡す中間配列です。</summary>
            public NativeArray<GeneratedHydrologyCell> IntersectingHydrologyCells;
            /// <summary>交差9×9水文セル内に自然表流水の涵養候補が存在するかを0／1で保持します。</summary>
            public NativeArray<SurfaceRechargeCandidate> SurfaceRechargeCandidates;
            /// <summary>4096タイルの自然土壌飽和を保持し、完成時に専用ECS Bufferへコピーする配列です。</summary>
            public NativeArray<GeneratedHydrologyTile> HydrologyTiles;
            /// <summary>泉抑制範囲と8近傍を含む21×21地下水・湖状態の再利用作業配列です。</summary>
            public NativeArray<GeneratedHydrologyCell> SpringHydrologyCells;
            /// <summary>通常表流水生成が同じ河道中心線から記録する泉判定用21×21涵養フラグです。</summary>
            public NativeArray<SurfaceRechargeCandidate> SpringSurfaceRechargeCandidates;
            /// <summary>19×19泉候補を一度だけ評価し、半径4セルの最大候補比較へ使う作業配列です。</summary>
            public NativeArray<SpringCandidate> SpringCandidates;
            /// <summary>21×21周辺水文の8行担当が失敗時に自分の添字へ1を書く通知領域です。</summary>
            public NativeArray<byte> LakeSpringHydrologyGenerationFailed;
            /// <summary>8水文行の各作業が失敗時に自分の添字へ1を書く、湖Jobと公開処理への通知領域です。</summary>
            public NativeArray<byte> HydrologyGenerationFailed;
            /// <summary>地形・資源Jobが失敗時に先頭へ1を書く、公開処理への独立通知領域です。</summary>
            public NativeArray<byte> TerrainGenerationFailed;
            /// <summary>表流水細分化Jobが失敗時に先頭へ1を書き、6 Bufferすべての公開を止める通知領域です。</summary>
            public NativeArray<byte> SurfaceWaterGenerationFailed;
            /// <summary>地下水・自然土壌飽和Jobが失敗時に先頭へ1を書き、6 Bufferの公開を止める通知領域です。</summary>
            public NativeArray<byte> GroundwaterGenerationFailed;
            /// <summary>湖水収支・泉Jobが失敗時に先頭へ1を書き、6 Bufferの公開を止める通知領域です。</summary>
            public NativeArray<byte> LakeSpringGenerationFailed;
            /// <summary>周辺4チャンクの64水文セルを同時再構築し、2波で再利用する自然冠水作業配列です。</summary>
            public NativeArray<GeneratedHydrologyCell> NaturalFloodingScratchHydrologyCells;
            /// <summary>周囲8チャンク分の交差9×9水文を、2波をまたいで保持する作業配列です。</summary>
            public NativeArray<GeneratedHydrologyCell>
                NaturalFloodingScratchIntersectingHydrologyCells;
            /// <summary>周囲8チャンク分の交差9×9涵養有無を、2波をまたいで保持する作業配列です。</summary>
            public NativeArray<SurfaceRechargeCandidate>
                NaturalFloodingScratchSurfaceRechargeCandidates;
            /// <summary>周辺4チャンクの4096表流水を同時再構築し、2波で再利用する作業配列です。</summary>
            public NativeArray<GeneratedSurfaceWaterTile> NaturalFloodingScratchSurfaceWater;
            /// <summary>中心を含む3×3チャンクの恒久淡水源を保持する192×192配列です。</summary>
            public NativeArray<GeneratedSurfaceWaterTile> NaturalFloodingSourceWater;
            /// <summary>湿原評価に必要な15×15の地下水・最終湖区分完成水文を保持します。</summary>
            public NativeArray<GeneratedHydrologyCell> WetlandEvaluationHydrologyCells;
            /// <summary>15×15水文が周囲再構築ですべて埋まったかを0／1で保持します。</summary>
            public NativeArray<byte> WetlandEvaluationHydrologyCellWritten;
            /// <summary>15×15水文セルが湿原候補かを0／1で保持する再利用配列です。</summary>
            public NativeArray<byte> WetlandCandidateCells;
            /// <summary>対象9×9セルが4セル以上の成分へ属するかを0／1で保持する配列です。</summary>
            public NativeArray<byte> LargeWetlandExtentCells;
            /// <summary>湿原成分の有限探索ごとに初期化して再利用する訪問配列です。</summary>
            public NativeArray<byte> WetlandVisitedCells;
            /// <summary>湿原成分を上下左右へ探索する最大225件のFIFO配列です。</summary>
            public NativeArray<int> WetlandCellQueue;
            /// <summary>周囲8担当または最終湿原評価の失敗時に1を書き、6 Bufferの公開を止めます。</summary>
            public NativeArray<byte> NaturalFloodingGenerationFailed;
        }

        /// <summary>固定Tick境界で残り時間を個別計測する生成Job系列です。</summary>
        private enum GenerationStage : byte
        {
            /// <summary>64水文セルの下流・集水結果を作る8行の並列系列です。</summary>
            HydrologyBase = 0,

            /// <summary>閉鎖末端をまとめ、流域境界と湖面を確定する系列です。</summary>
            Lake = 1,

            /// <summary>河道幅と湖面候補を4096タイルへ描き下ろす系列です。</summary>
            SurfaceWater = 2,

            /// <summary>4,096タイルの地形と資源を作る独立系列です。</summary>
            Terrain = 3,

            /// <summary>完成した地形・水文・表流水から地下水と自然土壌飽和を作る中間系列です。</summary>
            Groundwater = 4,

            /// <summary>地下水後の湖最終区分と抑制済み泉を生成する系列です。</summary>
            LakeSpring = 5,

            /// <summary>半径32の自然冠水と4セル以上の湿原広がりを生成する最終系列です。</summary>
            NaturalFlooding = 6
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

            /// <summary>
            /// 各水文行が座標範囲外や不正設定を検出した場合、自分と同じ添字へ1を書きます。
            /// IJobParallelForの安全性制限へ担当添字との対応を推論させず、Burst実行時も8担当の独立書き込みを許可します。
            /// </summary>
            [NativeDisableParallelForRestriction]
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

        /// <summary>水文Jobと独立して、分類前の基礎地形4096タイルを生成するBurst Jobです。</summary>
        [BurstCompile]
        private struct GenerateTerrainJob : IJob
        {
            /// <summary>WorldのSingletonから値コピーした、Job実行中は読み取り専用の生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成する絶対チャンク座標です。Jobの実行中は変化しません。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>このJobだけが全4096件を上書きし、最終Handle完了後にSystemへ返す地形配列です。</summary>
            public NativeArray<GeneratedTile> Tiles;

            /// <summary>生成失敗時に先頭へ1を書き、6 Bufferすべての公開を止める地形専用通知領域です。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>地質キャッシュを一度構築し、全タイルの分類前地形を順に生成します。</summary>
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
                            continue;
                        }
                        Tiles[tileIndex] = terrainTile;
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

            /// <summary>
            /// 湖担当が失敗時に自分の添字へ1を書き、部分結果の公開を止める通知領域です。
            /// 各担当はlaneIndexと同じ要素だけを書きますが、呼出し先をまたぐため並列書き込み制限を明示的に解除します。
            /// </summary>
            [NativeDisableParallelForRestriction]
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

        /// <summary>
        /// 湖面解決済みの固定水文を読み、河道幅と連続湖面候補を4096タイルへ細分化するBurst Jobです。
        /// 湖Jobの作業配列を依存関係後に再利用し、Job実行中の追加確保とECSへの部分公開を避けます。
        /// </summary>
        [BurstCompile]
        private struct GenerateSurfaceWaterJob : IJob
        {
            /// <summary>河道、湖面、海面、連続標高を同じSeedと版から再現する固定設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>湖面解決まで完了した、対象チャンクの行優先64水文セルです。</summary>
            [ReadOnly]
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;

            /// <summary>外周河道の集水と湖流域再構築で先頭領域を再利用する上流候補Heapです。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;

            /// <summary>湖Job完了後に先頭4,096件を一つの流域へ再利用する水文セル配列です。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;

            /// <summary>湖Job完了後に先頭8,192件を一つの流域へ再利用する固定Hash表です。</summary>
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;

            /// <summary>最大16×16の河道探索で費用と直前位置を保持する256件の作業配列です。</summary>
            public NativeArray<SurfaceWaterPathSearchNode> PathWorkspace;

            /// <summary>河道中心線を開始から終了まで一時保持する256件の作業配列です。</summary>
            public NativeArray<SurfaceWaterPathTile> PathTiles;

            /// <summary>最大262,144湖候補タイルの訪問状態を流域ごとに再利用する配列です。</summary>
            public NativeArray<byte> FloodVisited;

            /// <summary>最大262,144湖候補タイルを一度ずつ処理する固定長FIFO配列です。</summary>
            public NativeArray<int> FloodQueue;

            /// <summary>成功時にチャンクと交差する9×9完成水文を地下水Jobへ渡す中間配列です。</summary>
            public NativeArray<GeneratedHydrologyCell> IntersectingHydrologyCells;

            /// <summary>河道・湖候補が存在する交差9×9水文セルを0／1で地下水Jobへ渡します。</summary>
            public NativeArray<SurfaceRechargeCandidate> SurfaceRechargeCandidates;

            /// <summary>同じ河道中心線と河川幅が通る泉判定用21×21水文セルを0／1で保持します。</summary>
            public NativeArray<SurfaceRechargeCandidate> SpringSurfaceRechargeCandidates;

            /// <summary>成功時に4096件すべてを上書きし、最終Handle後にECSへ公開する表流水配列です。</summary>
            public NativeArray<GeneratedSurfaceWaterTile> SurfaceWater;

            /// <summary>生成失敗時に先頭へ1を書き、地形・Blockセル・資源・水文を含む6 Bufferの公開を止めます。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>固定長作業領域を純粋生成へ渡し、失敗時だけ公開停止フラグを設定します。</summary>
            public void Execute()
            {
                if (!DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        HydrologyCells,
                        HydrologyCatchmentFrontier,
                        HydrologyBasinMembers,
                        HydrologyBasinLookup,
                        PathWorkspace,
                        PathTiles,
                        FloodVisited,
                        FloodQueue,
                        IntersectingHydrologyCells,
                        SurfaceRechargeCandidates,
                        SpringSurfaceRechargeCandidates,
                        SurfaceWater))
                {
                    GenerationFailed[0] = 1;
                }
            }
        }

        /// <summary>
        /// 完成済み地形・水文・表流水の後で、交差9×9セルの地下水と4096タイルの自然土壌飽和を生成するBurst Jobです。
        /// </summary>
        [BurstCompile]
        private struct GenerateGroundwaterHydrologyJob : IJob
        {
            /// <summary>局所地質、涵養、地下水接続、自然土壌飽和の固定設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>局所地質の基礎値と起伏を読む、完成済み4096地形タイルです。</summary>
            [ReadOnly]
            public NativeArray<GeneratedTile> Tiles;

            /// <summary>地下水ポテンシャルと接続bitを反映し、完成時にECSへ公開する64水文セルです。</summary>
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;

            /// <summary>表流水段階が完成させ、地下水計算後の境界タイル参照にも使う交差9×9水文です。</summary>
            public NativeArray<GeneratedHydrologyCell> IntersectingHydrologyCells;

            /// <summary>交差9×9各セルに自然表流水の涵養候補があるかを保持する読み取り専用配列です。</summary>
            [ReadOnly]
            public NativeArray<SurfaceRechargeCandidate> SurfaceRechargeCandidates;

            /// <summary>成功時に4096件すべてを上書きし、専用ECS Bufferへ公開する自然水文タイルです。</summary>
            public NativeArray<GeneratedHydrologyTile> HydrologyTiles;

            /// <summary>表流水生成が失敗済みなら地下水の部分計算を開始しないために読む1要素通知です。</summary>
            [ReadOnly]
            public NativeArray<byte> SurfaceWaterGenerationFailed;

            /// <summary>地形生成が失敗済みなら基礎地質のdefault値を入力にしないために読む1要素通知です。</summary>
            [ReadOnly]
            public NativeArray<byte> TerrainGenerationFailed;

            /// <summary>地下水または自然土壌飽和に失敗した場合、先頭へ1を書いて6 Bufferの公開を止めます。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>先行失敗を確認し、完成入力だけを純粋生成へ渡して一括成功または一括失敗にします。</summary>
            public void Execute()
            {
                if (SurfaceWaterGenerationFailed[0] != 0
                    || TerrainGenerationFailed[0] != 0
                    || !DeterministicGroundwaterHydrologyGenerator.TryGenerateChunk(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        Tiles,
                        HydrologyCells,
                        IntersectingHydrologyCells,
                        SurfaceRechargeCandidates,
                        HydrologyTiles))
                {
                    GenerationFailed[0] = 1;
                }
            }
        }

        /// <summary>
        /// 泉判定用21×21周辺水文を8担当で並列生成するBurst Jobです。
        /// 各担当は別の行、集水Heap、流域配列、Hash領域だけを書き、共有作業領域の競合を避けます。
        /// </summary>
        [BurstCompile]
        private struct PrepareLakeSpringHydrologyRowsJob : IJobParallelFor
        {
            /// <summary>湖水収支と泉を含む固定ワールド生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>中央9×9で再生成を省略する、地下水反映済み完成水文です。</summary>
            [ReadOnly]
            public NativeArray<GeneratedHydrologyCell> IntersectingHydrologyCells;

            /// <summary>8担当へ独立領域を一つずつ割り当てる集水Heapです。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;

            /// <summary>8担当へ独立領域を一つずつ割り当てる閉鎖流域セル配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;

            /// <summary>8担当へ独立領域を一つずつ割り当てる流域所属Hash表です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;

            /// <summary>各担当が別行だけへ書き込む、泉判定用21×21完成水文です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedHydrologyCell> SpringHydrologyCells;

            /// <summary>先行地下水Jobが失敗済みなら周辺再生成を開始しないために読む通知です。</summary>
            [ReadOnly]
            public NativeArray<byte> GroundwaterGenerationFailed;

            /// <summary>
            /// 各担当が失敗時に自分の添字だけへ1を書く通知領域です。
            /// 8担当は重複しない要素だけを変更するため、ParallelForの単一要素制限を解除して安全な分割書き込みを許可します。
            /// </summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<byte> GenerationFailed;

            /// <summary>担当番号と同じ剰余を持つ21行を生成し、失敗時だけ担当別通知を更新します。</summary>
            /// <param name="laneIndex">0〜7の行担当番号で、専用作業領域の番号でもあります。</param>
            public void Execute(int laneIndex)
            {
                if (GroundwaterGenerationFailed[0] != 0
                    || !DeterministicLakeSpringGenerator.TryPrepareSpringHydrologyRowsLane(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        IntersectingHydrologyCells,
                        HydrologyCatchmentFrontier,
                        HydrologyBasinMembers,
                        HydrologyBasinLookup,
                        SpringHydrologyCells,
                        laneIndex,
                        HydrologyRowWorkItemCount))
                {
                    GenerationFailed[laneIndex] = 1;
                }
            }
        }

        /// <summary>
        /// 準備済み周辺水文を使って湖候補を淡水湖・塩湖・一時湖へ確定し、抑制済みの泉と固定淡水小川を追加するBurst Jobです。
        /// </summary>
        [BurstCompile]
        private struct FinalizeLakeSpringJob : IJob
        {
            /// <summary>湖水収支と泉を含む固定ワールド生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>湖接続と泉Streamを最終結果へ反映する、対象チャンクの64水文セルです。</summary>
            public NativeArray<GeneratedHydrologyCell> HydrologyCells;

            /// <summary>地下水反映済みの交差9×9水文セルです。外周泉候補の再利用元にもなります。</summary>
            public NativeArray<GeneratedHydrologyCell> IntersectingHydrologyCells;

            /// <summary>湖候補の最終区分と泉流を反映する4096固定表流水です。</summary>
            public NativeArray<GeneratedSurfaceWaterTile> SurfaceWater;

            /// <summary>外周21×21セルの集水・流出再生成へ先頭領域を再利用する上流候補Heapです。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;

            /// <summary>外周セルの閉鎖流域状態を再構築するときに再利用する最大4096件の作業領域です。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;

            /// <summary>外周セルの閉鎖流域所属を引く固定Hash表の再利用領域です。</summary>
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;

            /// <summary>泉源から隣接低地への最大16×16経路探索へ再利用する256ノード配列です。</summary>
            public NativeArray<SurfaceWaterPathSearchNode> PathWorkspace;

            /// <summary>一つの細分化済み泉経路を開始から終了まで保持する配列です。</summary>
            public NativeArray<SurfaceWaterPathTile> PathTiles;

            /// <summary>泉抑制範囲と8近傍を含む21×21完成水文の作業配列です。</summary>
            public NativeArray<GeneratedHydrologyCell> SpringHydrologyCells;

            /// <summary>通常表流水生成が同じ河道中心線と河川幅から記録した21×21涵養フラグです。</summary>
            [ReadOnly]
            public NativeArray<SurfaceRechargeCandidate> SpringSurfaceRechargeCandidates;

            /// <summary>半径4セル抑制で比較する19×19泉候補の作業配列です。</summary>
            public NativeArray<SpringCandidate> SpringCandidates;

            /// <summary>先行地下水Jobが失敗済みなら最終区分を開始しないために読む通知です。</summary>
            [ReadOnly]
            public NativeArray<byte> GroundwaterGenerationFailed;

            /// <summary>8担当の周辺水文生成が一つでも失敗したかを最終処理前に確認する通知です。</summary>
            [ReadOnly]
            public NativeArray<byte> SpringHydrologyGenerationFailed;

            /// <summary>湖区分または泉生成が失敗した場合に1を書き、6 Bufferの部分公開を止めます。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>先行失敗を確認し、湖・泉を一括成功または一括失敗として適用します。</summary>
            public void Execute()
            {
                bool preparationFailed = GroundwaterGenerationFailed[0] != 0;
                for (int laneIndex = 0;
                    laneIndex < SpringHydrologyGenerationFailed.Length;
                    laneIndex++)
                {
                    if (SpringHydrologyGenerationFailed[laneIndex] != 0)
                    {
                        preparationFailed = true;
                        break;
                    }
                }
                if (preparationFailed
                    || !DeterministicLakeSpringGenerator.TryFinalizePreparedChunkWithRechargeRefresh(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        HydrologyCells,
                        IntersectingHydrologyCells,
                        SurfaceWater,
                        HydrologyCatchmentFrontier,
                        HydrologyBasinMembers,
                        HydrologyBasinLookup,
                        PathWorkspace,
                        PathTiles,
                        SpringSurfaceRechargeCandidates,
                        SpringHydrologyCells,
                        SpringCandidates))
                {
                    GenerationFailed[0] = 1;
                }
            }
        }

        /// <summary>
        /// 周囲8チャンクを4担当ずつ2波で再構築するBurst並列Jobです。
        /// 各担当は独立した作業領域と共有水源内の重ならない64×64領域だけを書きます。
        /// </summary>
        [BurstCompile]
        private struct GenerateNaturalFloodingSourceChunksJob : IJobParallelFor
        {
            /// <summary>表流水、地下水、湖区分を再現する固定設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>第1波は0、第2波は4で、担当内0〜3を全体0〜7へ変換します。</summary>
            public int SourceLaneOffset;

            /// <summary>4担当へ一つずつ貸す集水Heap連結配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;

            /// <summary>4担当へ一つずつ貸す閉鎖流域セル連結配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;

            /// <summary>4担当へ一つずつ貸す流域Hash連結配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;

            /// <summary>4担当分の河道探索ノードを連結した作業配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<SurfaceWaterPathSearchNode> PathWorkspace;

            /// <summary>4担当分の河道経路を連結した作業配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<SurfaceWaterPathTile> PathTiles;

            /// <summary>4担当分の湖Flood Fill訪問状態を連結した作業配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<byte> FloodVisited;

            /// <summary>4担当分の湖Flood Fill待ち行列を連結した作業配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<int> FloodQueue;

            /// <summary>4担当分の64水文セルを連結した作業配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedHydrologyCell> ScratchHydrologyCells;

            /// <summary>周囲8担当分を保持し、2波の上書きを避ける交差9×9水文出力です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedHydrologyCell> ScratchIntersectingHydrologyCells;

            /// <summary>周囲8担当分を保持し、2波の上書きを避ける交差9×9涵養出力です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<SurfaceRechargeCandidate> ScratchSurfaceRechargeCandidates;

            /// <summary>4担当分の4096表流水を連結し、第2波で再利用する作業配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedSurfaceWaterTile> ScratchSurfaceWater;

            /// <summary>各担当が重ならない64×64位置へ書く、共有192×192水源配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<GeneratedSurfaceWaterTile> SourceWaterArea;

            /// <summary>先行する湖・泉生成が失敗済みなら、未完成値を読まないために確認する通知です。</summary>
            [ReadOnly]
            public NativeArray<byte> LakeSpringGenerationFailed;

            /// <summary>各周囲担当が失敗時に自分の全体担当番号へ1を書く通知配列です。</summary>
            [NativeDisableParallelForRestriction]
            public NativeArray<byte> GenerationFailed;

            /// <summary>担当内番号から全体担当と専用部分配列を求め、周囲1チャンクを再構築します。</summary>
            /// <param name="workspaceLaneIndex">現在の波で使用する0〜3の作業領域番号です。</param>
            public void Execute(int workspaceLaneIndex)
            {
                int sourceLaneIndex = SourceLaneOffset + workspaceLaneIndex;
                int frontierCapacity = DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity;
                int basinMemberCapacity = DeterministicHydrologyLakeResolver.MaximumBasinMembers;
                int basinLookupCapacity = DeterministicHydrologyLakeResolver.LookupCapacity;
                int pathCapacity = DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount;
                int floodCapacity = DeterministicSurfaceWaterGenerator.MaximumFloodTileCount;
                int hydrologyCapacity = DeterministicHydrologyGenerator.CellsPerChunk;
                int intersectingCapacity =
                    DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount;
                int surfaceWaterCapacity = WorldGridConstants.TilesPerChunk;

                if (LakeSpringGenerationFailed[0] != 0
                    || !DeterministicWetlandExtentGenerator.TryGenerateSourceChunkLane(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        sourceLaneIndex,
                        HydrologyCatchmentFrontier.GetSubArray(
                            workspaceLaneIndex * frontierCapacity,
                            frontierCapacity),
                        HydrologyBasinMembers.GetSubArray(
                            workspaceLaneIndex * basinMemberCapacity,
                            basinMemberCapacity),
                        HydrologyBasinLookup.GetSubArray(
                            workspaceLaneIndex * basinLookupCapacity,
                            basinLookupCapacity),
                        PathWorkspace.GetSubArray(workspaceLaneIndex * pathCapacity, pathCapacity),
                        PathTiles.GetSubArray(workspaceLaneIndex * pathCapacity, pathCapacity),
                        FloodVisited.GetSubArray(workspaceLaneIndex * floodCapacity, floodCapacity),
                        FloodQueue.GetSubArray(workspaceLaneIndex * floodCapacity, floodCapacity),
                        ScratchHydrologyCells.GetSubArray(
                            workspaceLaneIndex * hydrologyCapacity,
                            hydrologyCapacity),
                        ScratchIntersectingHydrologyCells.GetSubArray(
                            sourceLaneIndex * intersectingCapacity,
                            intersectingCapacity),
                        ScratchSurfaceRechargeCandidates.GetSubArray(
                            sourceLaneIndex * intersectingCapacity,
                            intersectingCapacity),
                        ScratchSurfaceWater.GetSubArray(
                            workspaceLaneIndex * surfaceWaterCapacity,
                            surfaceWaterCapacity),
                        SourceWaterArea))
                {
                    GenerationFailed[sourceLaneIndex] = 1;
                }
            }
        }

        /// <summary>
        /// 2波で完成した周囲表流水・交差水文を固定順で結合し、自然冠水と湿原規模を確定する最終Burst Jobです。
        /// </summary>
        [BurstCompile]
        private struct FinalizeNaturalFloodingWetlandJob : IJob
        {
            /// <summary>自然冠水、地形、水文、地下水、湖区分を再現する固定設定です。</summary>
            public WorldGenerationSettings Settings;
            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;
            /// <summary>低地判定へ使う完成済み4096地形です。</summary>
            [ReadOnly] public NativeArray<GeneratedTile> Tiles;
            /// <summary>自然飽和を読み、自然冠水と湿原bitを追加する4096自然水文タイルです。</summary>
            public NativeArray<GeneratedHydrologyTile> HydrologyTiles;
            /// <summary>中心チャンクの湖・泉確定済み4096表流水です。</summary>
            [ReadOnly] public NativeArray<GeneratedSurfaceWaterTile> SurfaceWater;
            /// <summary>中央9×9の完成地下水・湖区分を再利用するC4-Cキャッシュです。</summary>
            [ReadOnly] public NativeArray<GeneratedHydrologyCell> SpringHydrologyCells;
            /// <summary>外周水文の最終湖区分へ順次再利用する集水Heapです。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyCatchmentFrontier;
            /// <summary>外周水文の最終湖区分へ順次再利用する閉鎖流域配列です。</summary>
            public NativeArray<HydrologyUpstreamCandidate> HydrologyBasinMembers;
            /// <summary>外周水文の最終湖区分へ順次再利用する流域Hash表です。</summary>
            public NativeArray<HydrologyBasinLookupSlot> HydrologyBasinLookup;
            /// <summary>8担当分を保持する交差9×9水文です。</summary>
            public NativeArray<GeneratedHydrologyCell> ScratchIntersectingHydrologyCells;
            /// <summary>8担当分を保持する交差9×9涵養状態です。</summary>
            [ReadOnly] public NativeArray<SurfaceRechargeCandidate> ScratchSurfaceRechargeCandidates;
            /// <summary>周囲8担当が配置済みで、中心を追加する192×192表流水です。</summary>
            public NativeArray<GeneratedSurfaceWaterTile> SourceWaterArea;
            /// <summary>15×15湿原候補の完成水文を保持する作業配列です。</summary>
            public NativeArray<GeneratedHydrologyCell> EvaluationHydrologyCells;
            /// <summary>15×15各水文セルの書込完了を示す0／1配列です。</summary>
            public NativeArray<byte> EvaluationHydrologyCellWritten;
            /// <summary>15×15水文セルが16資格タイル以上を持つかを保持します。</summary>
            public NativeArray<byte> CandidateCells;
            /// <summary>中央9×9セルが4セル以上の成分へ属するかを保持します。</summary>
            public NativeArray<byte> LargeExtentCells;
            /// <summary>連結探索ごとに初期化して再利用する訪問状態です。</summary>
            public NativeArray<byte> VisitedCells;
            /// <summary>連結探索へ再利用する最大225件のFIFOです。</summary>
            public NativeArray<int> CellQueue;
            /// <summary>先行する湖・泉生成の失敗通知です。</summary>
            [ReadOnly] public NativeArray<byte> LakeSpringGenerationFailed;
            /// <summary>周囲8担当と最終評価の失敗を保持し、6 Buffer一括公開を止めます。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>先行失敗を集約し、成功時だけ固定順の結合と4096件の最終評価を実行します。</summary>
            public void Execute()
            {
                bool failed = LakeSpringGenerationFailed[0] != 0;
                for (int sourceLaneIndex = 0;
                    sourceLaneIndex < DeterministicWetlandExtentGenerator.SourceChunkLaneCount;
                    sourceLaneIndex++)
                {
                    failed |= GenerationFailed[sourceLaneIndex] != 0;
                }

                if (failed
                    || !DeterministicWetlandExtentGenerator.TryFinalizeParallelChunk(
                        Settings,
                        Coordinate.X,
                        Coordinate.Y,
                        Tiles,
                        HydrologyTiles,
                        SurfaceWater,
                        SpringHydrologyCells,
                        HydrologyCatchmentFrontier,
                        HydrologyBasinMembers,
                        HydrologyBasinLookup,
                        ScratchIntersectingHydrologyCells,
                        ScratchSurfaceRechargeCandidates,
                        SourceWaterArea,
                        EvaluationHydrologyCells,
                        EvaluationHydrologyCellWritten,
                        CandidateCells,
                        LargeExtentCells,
                        VisitedCells,
                        CellQueue))
                {
                    GenerationFailed[0] = 1;
                }
            }
        }

        /// <summary>
        /// 完成した水文の後で新18Biome、Blockセル、仮資源を一括確定する最終Burst Jobです。
        /// このJobが成功するまでBiomeIdとBlockセルを公開せず、旧分類から後変換する二重経路を作りません。
        /// </summary>
        [BurstCompile]
        private struct FinalizeBiomeTerrainJob : IJob
        {
            /// <summary>Seed、GeneratorVersion、環境値、暫定地形規則を持つ固定設定です。</summary>
            public WorldGenerationSettings WorldSettings;

            /// <summary>新18Biomeの検証済みDefinitionと水文特殊Host集合です。</summary>
            public BiomeClassificationSettings BiomeSettings;

            /// <summary>C7まで維持する木・石・砂の仮資源生成設定です。</summary>
            public ResourceGenerationSettings ResourceSettings;

            /// <summary>Biome表層と地質由来のGround・Wall候補を固定順で保持するBlobです。</summary>
            [ReadOnly]
            public BlobAssetReference<TerrainBlockPaletteCatalogBlob> PaletteCatalog;

            /// <summary>自然水域とマグマへ直接設定する解決済みLiquid IDです。</summary>
            public BuiltInTerrainBlockRuntimeIds TerrainRuntimeIds;

            /// <summary>生成対象となるSurface上の絶対論理チャンク座標です。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>基礎環境・地質を読み、新Biomeと最終地面属性を書き戻す4096タイルです。</summary>
            public NativeArray<GeneratedTile> Tiles;

            /// <summary>最終Biome・地質・水文から全件上書きする自然Blockセル4096件です。</summary>
            public NativeArray<GeneratedTerrainCell> TerrainCells;

            /// <summary>水文特殊Biomeの判定に使う、完成済み自然水文4096タイルです。</summary>
            [ReadOnly]
            public NativeArray<GeneratedHydrologyTile> HydrologyTiles;

            /// <summary>既存GroundTypeへ一時反映する、湖・泉まで確定済みの固定表流水です。</summary>
            [ReadOnly]
            public NativeArray<GeneratedSurfaceWaterTile> SurfaceWater;

            /// <summary>最終地面と新Biomeに対応してこのJobが全件上書きする仮資源配列です。</summary>
            public NativeArray<GeneratedResourceTile> Resources;

            /// <summary>先行失敗を読み、分類失敗時は先頭へ1を書いて全Bufferの公開を止めます。</summary>
            public NativeArray<byte> GenerationFailed;

            /// <summary>4096タイルを固定順で分類し、1件でも失敗した場合は部分結果を公開しません。</summary>
            public void Execute()
            {
                for (int failureIndex = 0; failureIndex < GenerationFailed.Length; failureIndex++)
                {
                    if (GenerationFailed[failureIndex] != 0)
                    {
                        return;
                    }
                }

                for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
                {
                    for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                    {
                        int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                        int worldTileX = WorldGrid.ToWorldTileX(Coordinate.X, localX);
                        int worldTileY = WorldGrid.ToWorldTileY(Coordinate.Y, localY);
                        if (!DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                                WorldSettings,
                                BiomeSettings,
                                worldTileX,
                                worldTileY,
                                Tiles[tileIndex],
                                HydrologyTiles[tileIndex],
                                SurfaceWater[tileIndex],
                                out GeneratedTile finalizedTile))
                        {
                            GenerationFailed[0] = 1;
                            Tiles[tileIndex] = default;
                            TerrainCells[tileIndex] = default;
                            Resources[tileIndex] = default;
                            continue;
                        }

                        if (!DeterministicTerrainCellFinalizer.TryFinalizeSurfaceCell(
                                WorldSettings,
                                PaletteCatalog,
                                TerrainRuntimeIds,
                                worldTileX,
                                worldTileY,
                                finalizedTile,
                                HydrologyTiles[tileIndex],
                                SurfaceWater[tileIndex],
                                out GeneratedTerrainCell terrainCell))
                        {
                            // 1セルでもBlock確定に失敗した場合は、旧GeneratedTileだけを公開できないよう全出力を失敗扱いにします。
                            GenerationFailed[0] = 1;
                            Tiles[tileIndex] = default;
                            TerrainCells[tileIndex] = default;
                            Resources[tileIndex] = default;
                            continue;
                        }

                        Tiles[tileIndex] = finalizedTile;
                        TerrainCells[tileIndex] = terrainCell;
                        Resources[tileIndex] = DeterministicResourceGenerator.GenerateResource(
                            WorldSettings,
                            ResourceSettings,
                            finalizedTile,
                            worldTileX,
                            worldTileY);
                    }
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
            state.RequireForUpdate<BiomeClassificationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
            state.RequireForUpdate<TerrainBlockCatalog>();
            state.RequireForUpdate<TerrainBlockPaletteCatalog>();
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
                SystemAPI.GetSingleton<BiomeClassificationSettings>(),
                SystemAPI.GetSingleton<ResourceGenerationSettings>(),
                SystemAPI.GetSingleton<TerrainBlockPaletteCatalog>().Value,
                SystemAPI.GetSingleton<TerrainBlockCatalog>().BuiltInRuntimeIds,
                limit);
            generator.RefreshFixedTickReadiness(state.EntityManager, true, scheduled);
        }
    }
}
