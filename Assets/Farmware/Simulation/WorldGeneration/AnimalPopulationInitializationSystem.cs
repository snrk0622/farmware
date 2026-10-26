// このファイルは、完成した64×64地形チャンクを固定Tick境界で一度だけ動物地域として初期化します。
// 候補作成、共通永続IDの一括発行、地域状態と個体Entityの作成を全件成功後にまとめて確定します。

using System;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Farmware.WorldGeneration.Animals
{
    /// <summary>
    /// 生成完了チャンクの未初期化地域を座標順に補充し、野生動物を独立Entityとして作成する固定Tick Systemです。
    /// チャンクの表示・アンロード状態を個体の正にせず、地域状態と個体はストリーミング用チャンクEntityから分離します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareFixedTickGameplaySystemGroup))]
    public partial struct AnimalPopulationInitializationSystem : ISystem
    {
        /// <summary>保存対象の地域状態を検索し、同一地域の再初期化を防ぐQueryです。</summary>
        private EntityQuery regionStateQuery;

        /// <summary>
        /// 地域座標から状態Entityを引く再利用索引です。保存対象ではなく、Query件数が変わったときに再構築できます。
        /// </summary>
        private NativeParallelHashMap<int3, Entity> regionEntityByCoordinate;

        /// <summary>
        /// 必須Singletonと地域Queryを準備し、地域索引用Persistentメモリの所有権を取得します。
        /// </summary>
        /// <param name="state">更新条件とEntityQueryを設定するSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            regionStateQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationRegionState>());
            regionEntityByCoordinate = new NativeParallelHashMap<int3, Entity>(16, Allocator.Persistent);

            state.RequireForUpdate<AnimalPopulationSeed>();
            state.RequireForUpdate<AnimalSpeciesCatalog>();
            state.RequireForUpdate<TerrainBlockCatalog>();
            state.RequireForUpdate<PersistentIdAllocatorState>();
            state.RequireForUpdate<SimulationTickState>();
        }

        /// <summary>
        /// World破棄時に呼ばれ、このSystemだけが所有する地域索引のNativeメモリを解放します。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。EntityはWorldが破棄するため変更しません。</param>
        public void OnDestroy(ref SystemState state)
        {
            if (regionEntityByCoordinate.IsCreated)
            {
                regionEntityByCoordinate.Dispose();
            }
        }

        /// <summary>
        /// 今回までに完成した未確認チャンクを地域座標順へ整列し、初期候補とIDを一括確定します。
        /// </summary>
        /// <param name="state">地形Bufferの読取、Singleton更新、ECB再生に使用するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            using NativeList<PendingRegion> pendingRegions =
                new NativeList<PendingRegion>(8, Allocator.Temp);

            foreach ((RefRO<ChunkCoordinateComponent> coordinateReference,
                      RefRO<ChunkGenerationStatus> statusReference,
                      Entity chunkEntity) in
                     SystemAPI.Query<
                             RefRO<ChunkCoordinateComponent>,
                             RefRO<ChunkGenerationStatus>>()
                         .WithAll<GeneratedTerrainCell, GeneratedResourceTile>()
                         .WithNone<AnimalPopulationRegionProcessed>()
                         .WithEntityAccess())
            {
                ChunkGenerationPhase phase = statusReference.ValueRO.Phase;
                if (phase != ChunkGenerationPhase.ReadyToApply
                    && phase != ChunkGenerationPhase.Active
                    && phase != ChunkGenerationPhase.Inactive)
                {
                    continue;
                }

                // 一時停止中は固定Tickが止まっていてもPresentationが動き、完成チャンクを
                // ReadyToApplyからActive、さらに画面外ならInactiveへ進める場合があります。
                // 3状態はいずれも地形と資源の確定Bufferを保持するため、表示状態にかかわらず
                // 未処理地域を初期化し、再開後も動物が欠落しないようにします。

                pendingRegions.Add(new PendingRegion
                {
                    Coordinate = new int3(
                        (int)coordinateReference.ValueRO.Layer,
                        coordinateReference.ValueRO.X,
                        coordinateReference.ValueRO.Y),
                    ChunkEntity = chunkEntity
                });
            }

            if (pendingRegions.Length == 0)
            {
                return;
            }

            pendingRegions.AsArray().Sort();
            RebuildRegionIndexWhenNeeded(ref state);

            AnimalPopulationSeed populationSeed =
                SystemAPI.GetSingleton<AnimalPopulationSeed>();
            AnimalSpeciesCatalog catalog =
                SystemAPI.GetSingleton<AnimalSpeciesCatalog>();
            BuiltInTerrainBlockRuntimeIds terrainRuntimeIds =
                SystemAPI.GetSingleton<TerrainBlockCatalog>().BuiltInRuntimeIds;
            ulong currentTick = SystemAPI.GetSingleton<SimulationTickState>().CurrentTick;

            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(32, Allocator.Temp);
            using NativeList<NewRegion> newRegions =
                new NativeList<NewRegion>(pendingRegions.Length, Allocator.Temp);
            using NativeList<Entity> alreadyInitializedChunks =
                new NativeList<Entity>(pendingRegions.Length, Allocator.Temp);

            for (int regionIndex = 0; regionIndex < pendingRegions.Length; regionIndex++)
            {
                PendingRegion pending = pendingRegions[regionIndex];
                if (regionEntityByCoordinate.ContainsKey(pending.Coordinate))
                {
                    alreadyInitializedChunks.Add(pending.ChunkEntity);
                    continue;
                }

                DynamicBuffer<GeneratedTerrainCell> terrainCells =
                    state.EntityManager.GetBuffer<GeneratedTerrainCell>(pending.ChunkEntity, true);
                DynamicBuffer<GeneratedResourceTile> resources =
                    state.EntityManager.GetBuffer<GeneratedResourceTile>(pending.ChunkEntity, true);
                AnimalPopulationRegionState regionState =
                    AnimalPopulationGenerator.CreateInitialRegionState(
                        populationSeed,
                        (WorldLayer)pending.Coordinate.x,
                        pending.Coordinate.y,
                        pending.Coordinate.z);
                int firstCandidateIndex = candidates.Length;
                if (!populationSeed.IsValid
                    || !AnimalPopulationGenerator.TryAppendInitialPopulation(
                        catalog.Value,
                        terrainCells.AsNativeArray(),
                        resources.AsNativeArray(),
                        terrainRuntimeIds,
                        ref regionState,
                        candidates))
                {
                    // 不完全な地形・資源や不正Seedでは地域を初期化済みにせず、入力修正後の固定Tickで再試行します。
                    return;
                }

                regionState.InitializedAtTick = currentTick;
                newRegions.Add(new NewRegion
                {
                    ChunkEntity = pending.ChunkEntity,
                    State = regionState,
                    FirstCandidateIndex = firstCandidateIndex,
                    CandidateCount = candidates.Length - firstCandidateIndex
                });
            }

            RefRW<PersistentIdAllocatorState> allocatorReference =
                SystemAPI.GetSingletonRW<PersistentIdAllocatorState>();
            PersistentIdAllocatorState stagedAllocator = allocatorReference.ValueRO;
            using NativeArray<PersistentIdAssignment> assignments =
                AllocatePersistentIds(
                    ref stagedAllocator,
                    candidates,
                    out bool allocationSucceeded);
            if (!allocationSucceeded)
            {
                // 一括発行は失敗時にアロケーターを変更しないため、地域と個体も何も確定しません。
                return;
            }

            allocatorReference.ValueRW = stagedAllocator;

            using EntityCommandBuffer commandBuffer =
                new EntityCommandBuffer(Allocator.Temp);
            for (int chunkIndex = 0;
                 chunkIndex < alreadyInitializedChunks.Length;
                 chunkIndex++)
            {
                commandBuffer.AddComponent<AnimalPopulationRegionProcessed>(
                    alreadyInitializedChunks[chunkIndex]);
            }

            for (int regionIndex = 0; regionIndex < newRegions.Length; regionIndex++)
            {
                NewRegion newRegion = newRegions[regionIndex];
                Entity regionEntity = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(regionEntity, newRegion.State);
                commandBuffer.AddComponent<AnimalPopulationRegionProcessed>(
                    newRegion.ChunkEntity);

                int candidateEnd = newRegion.FirstCandidateIndex + newRegion.CandidateCount;
                for (int candidateIndex = newRegion.FirstCandidateIndex;
                     candidateIndex < candidateEnd;
                     candidateIndex++)
                {
                    AnimalSpawnCandidate candidate = candidates[candidateIndex];
                    PersistentObjectId individualId = assignments[candidateIndex].Id;
                    PersistentObjectId herdLeaderId =
                        assignments[candidate.HerdLeaderCandidateIndex].Id;

                    Entity animalEntity = commandBuffer.CreateEntity();
                    commandBuffer.AddComponent(animalEntity, individualId);
                    commandBuffer.AddComponent<WildAnimal>(animalEntity);
                    commandBuffer.AddComponent(
                        animalEntity,
                        new AnimalSpecies
                        {
                            RuntimeId = candidate.SpeciesRuntimeId
                        });
                    commandBuffer.AddComponent(
                        animalEntity,
                        new AnimalTilePosition
                        {
                            Layer = candidate.Layer,
                            TileX = candidate.TileX,
                            TileY = candidate.TileY
                        });
                    commandBuffer.AddComponent(
                        animalEntity,
                        new AnimalHerdMembership
                        {
                            HerdId = herdLeaderId.Value
                        });
                    commandBuffer.AddComponent(
                        animalEntity,
                        new AnimalAppearanceVariant
                        {
                            Value = candidate.AppearanceVariant
                        });
                }
            }

            // すべての候補、ID、地域状態がそろってから一度だけ再生し、部分的な個体群を公開しません。
            commandBuffer.Playback(state.EntityManager);
        }

        /// <summary>
        /// 地域状態Entity数が変わった場合だけ、保存状態から再構築可能な座標索引を作り直します。
        /// </summary>
        /// <param name="state">地域Componentを読み取るEntityManagerを持つSystem状態です。</param>
        private void RebuildRegionIndexWhenNeeded(ref SystemState state)
        {
            int regionCount = regionStateQuery.CalculateEntityCount();
            if (regionEntityByCoordinate.Count() == regionCount)
            {
                return;
            }

            regionEntityByCoordinate.Clear();
            if (regionEntityByCoordinate.Capacity < regionCount)
            {
                regionEntityByCoordinate.Capacity = regionCount;
            }

            using NativeArray<Entity> entities =
                regionStateQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<AnimalPopulationRegionState> states =
                regionStateQuery.ToComponentDataArray<AnimalPopulationRegionState>(Allocator.Temp);
            for (int index = 0; index < states.Length; index++)
            {
                AnimalPopulationRegionState regionState = states[index];
                regionEntityByCoordinate.TryAdd(
                    new int3(
                        (int)regionState.Layer,
                        regionState.RegionX,
                        regionState.RegionY),
                    entities[index]);
            }
        }

        /// <summary>
        /// 全候補の要求を地域X、地域Y、地域内受付順で共通アロケーターへ渡します。
        /// </summary>
        /// <param name="allocatorState">全件成功時だけ呼び出し側がSingletonへ書き戻す一時アロケーター状態です。</param>
        /// <param name="candidates">座標順へ作成済みの全地域候補です。</param>
        /// <param name="succeeded">IDを消費せず失敗した場合はfalse、全件発行できた場合はtrueです。</param>
        /// <returns>候補と同じ件数で、安定キー順に並んだ一時割り当て配列です。</returns>
        private static NativeArray<PersistentIdAssignment> AllocatePersistentIds(
            ref PersistentIdAllocatorState allocatorState,
            NativeList<AnimalSpawnCandidate> candidates,
            out bool succeeded)
        {
            NativeArray<PersistentIdAssignment> assignments =
                new NativeArray<PersistentIdAssignment>(candidates.Length, Allocator.Temp);
            NativeArray<PersistentIdAllocationRequest> requests =
                new NativeArray<PersistentIdAllocationRequest>(candidates.Length, Allocator.Temp);
            try
            {
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    AnimalSpawnCandidate candidate = candidates[candidateIndex];
                    requests[candidateIndex] = new PersistentIdAllocationRequest
                    {
                        ScopeOrder = (long)candidate.Layer,
                        MajorOrder = candidate.RegionX,
                        MinorOrder = candidate.RegionY,
                        Sequence = candidate.SpawnSequence
                    };
                }

                succeeded = PersistentIdAllocator.TryAllocateBatch(
                    ref allocatorState,
                    requests,
                    assignments);
                return assignments;
            }
            finally
            {
                requests.Dispose();
            }
        }

        /// <summary>処理待ちチャンクを地域X、地域Yの安定順へ並べる一時レコードです。</summary>
        private struct PendingRegion : IComparable<PendingRegion>
        {
            /// <summary>地域を識別する論理チャンク座標です。</summary>
            public int3 Coordinate;

            /// <summary>生成済み地形Bufferと処理済みタグを所有するストリーミング用Entityです。</summary>
            public Entity ChunkEntity;

            /// <summary>地形Entity生成順を使わず、Layer、地域X、地域Yの順に比較します。</summary>
            /// <param name="other">比較する別の処理待ち地域です。</param>
            /// <returns>この地域が先なら負、同じなら0、後なら正です。</returns>
            public readonly int CompareTo(PendingRegion other)
            {
                int layerComparison = Coordinate.x.CompareTo(other.Coordinate.x);
                if (layerComparison != 0)
                {
                    return layerComparison;
                }

                int xComparison = Coordinate.y.CompareTo(other.Coordinate.y);
                return xComparison != 0
                    ? xComparison
                    : Coordinate.z.CompareTo(other.Coordinate.z);
            }
        }

        /// <summary>候補生成に成功し、ECB再生待ちになった1地域の状態と候補範囲です。</summary>
        private struct NewRegion
        {
            /// <summary>処理済みタグを付ける生成済みチャンクEntityです。</summary>
            public Entity ChunkEntity;

            /// <summary>初期補充後に保存対象となる地域別抽選状態です。</summary>
            public AnimalPopulationRegionState State;

            /// <summary>全候補List内でこの地域の先頭となる添字です。</summary>
            public int FirstCandidateIndex;

            /// <summary>この地域で作成する個体候補数です。</summary>
            public int CandidateCount;
        }
    }
}
