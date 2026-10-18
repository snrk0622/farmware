// このファイルは、地形・資源をECSから独立した作業領域で生成し、完成結果だけをECSへ公開します。
// ChunkStreamingSystemが要求と寿命、Presentationが確定済みBufferの表示、固定Tick公開Systemが個体群の決定論境界を担当します。

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Farmware.Simulation;
using Farmware.Simulation.Animals;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 有効なChunkGenerationRequestを持つチャンクだけを生成し、同じ座標の地形と資源を対応付けるECS Systemです。
    /// 開始時に要求を無効化します。動物個体群があるWorldでは固定Tick境界で一括公開し、Job完了順を論理状態へ持ち込みません。
    /// 投入はFarmwareSimulationSystemGroupで行い、個体群Worldの公開だけを固定Tick入口へ分離します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareSimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkStreamingSystem))]
    public partial struct ChunkGenerationSystem : ISystem
    {
        // 完了確認後の公開時間です。新規投入やWorker上の生成時間とは分けて測ります。
        private static readonly ProfilerMarker PublishProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationPublish");

        // 作業枠の確保とJob投入のCPU時間です。未完成Jobを待っていないか確認します。
        private static readonly ProfilerMarker ScheduleProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationSchedule");

        /// <summary>決定論のため固定Tick境界または停止中の公開境界で待つ、実行中バッチの同期時間です。</summary>
        private static readonly ProfilerMarker BoundaryWaitProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkGenerationBoundaryWait");

        // 有効なEnableable要求だけを調べる、System寿命中のQueryです。
        private EntityQuery requestQuery;

        // 上限まで増やして再利用する作業枠です。Listも各Persistent配列もSystemが破棄します。
        // List自体はJobへ渡さず、選んだ枠の配列だけを1つのJobへ排他的に貸します。
        private NativeList<GenerationSlot> slots;

        /// <summary>
        /// System作成時に要求Queryと再利用枠の一覧を確保し、生成設定がそろうまで更新を待つ条件を登録します。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            requestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkCoordinateComponent, ChunkGenerationStatus, ChunkGenerationRequest>()
                .WithAll<GeneratedTile, GeneratedResourceTile>()
                .Build(ref state);
            slots = new NativeList<GenerationSlot>(
                ChunkStreamingSettings.CreateDefault().MaximumConcurrentGenerations, Allocator.Persistent);
            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
        }

        /// <summary>
        /// 毎描画フレームで空き枠へ要求を投入します。個体群があるWorldの通常公開は固定Tick入口へ委ねます。
        /// 停止中は前回投入したバッチを全件公開し、Tickを進めずカメラ周辺の表示を更新します。
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
                // 完了した枠だけを先に解放すると、次の要求集合までWorker速度で変わります。
                // 稼働中は全枠をTick境界まで保持し、停止中も前フレームの全枠を待って一括解放します。
                if (isPaused)
                {
                    CompleteAndPublishBatch(state.EntityManager);
                }
            }
            else
            {
                // 個体群を持たない地形単独Worldでは、既存の非同期表示・Editorテスト用経路を維持します。
                PublishCompletedResults(state.EntityManager);
            }

            using (ScheduleProfilerMarker.Auto())
            {
                ScheduleRequests(ref state);
            }
        }

        /// <summary>
        /// 固定Tick入口と停止中フレームから呼び、現在実行中の有限バッチを全件待ってまとめて公開します。
        /// 完成済み枠だけを選ばないことで、IDの割当順と初期化TickがWorkerの完了順へ依存しなくなります。
        /// </summary>
        /// <param name="manager">このSystemと同じWorldの公開先です。バッチのBufferと生成状態だけを更新します。</param>
        public void CompleteAndPublishBatch(EntityManager manager)
        {
            using (BoundaryWaitProfilerMarker.Auto())
            {
                // 各Jobは投入時点から並列に動きます。Handleだけを結合し、既投入バッチの完了を1回の境界で待ちます。
                JobHandle batch = default;
                for (int index = 0; index < slots.Length; index++)
                {
                    if (slots[index].Running)
                    {
                        batch = JobHandle.CombineDependencies(batch, slots[index].Handle);
                    }
                }

                // 同時生成設定を上限とする既投入Jobだけを待ち、新規要求をここから無制限に投入しません。
                batch.Complete();
            }

            PublishCompletedResults(manager);
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
            }

            slots.Dispose();
        }

        /// <summary>完成した作業枠だけを読み、存続する同じ要求Entityへ地形と資源をまとめて公開します。</summary>
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
                    && manager.HasBuffer<GeneratedResourceTile>(slot.Entity))
                {
                    ChunkCoordinateComponent coordinate = manager.GetComponentData<ChunkCoordinateComponent>(slot.Entity);
                    ChunkGenerationStatus status = manager.GetComponentData<ChunkGenerationStatus>(slot.Entity);
                    if (coordinate.X == slot.Coordinate.X && coordinate.Y == slot.Coordinate.Y
                        && status.Phase == ChunkGenerationPhase.GeneratingData)
                    {
                        // 両Bufferを確定してからReadyへ進め、半分だけ完成した結果を表示側へ渡さないようにします。
                        manager.GetBuffer<GeneratedTile>(slot.Entity).CopyFrom(slot.Tiles);
                        manager.GetBuffer<GeneratedResourceTile>(slot.Entity).CopyFrom(slot.Resources);
                        status.GeneratedWithVersion = slot.GeneratorVersion;
                        status.Phase = ChunkGenerationPhase.ReadyToApply;
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
        /// <param name="state">設定と生成段階へアクセスする状態です。</param>
        private void ScheduleRequests(ref SystemState state)
        {
            int limit = SystemAPI.TryGetSingleton(out ChunkStreamingSettings streamingSettings)
                ? math.max(1, streamingSettings.MaximumConcurrentGenerations)
                : ChunkStreamingSettings.CreateDefault().MaximumConcurrentGenerations;
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
                return;
            }
            WorldGenerationSettings settings = SystemAPI.GetSingleton<WorldGenerationSettings>();
            ResourceGenerationSettings resourceSettings = SystemAPI.GetSingleton<ResourceGenerationSettings>();
            EntityManager manager = state.EntityManager;
            using NativeArray<Entity> requests = requestQuery.ToEntityArray(Allocator.Temp);
            bool scheduled = false;
            for (int index = 0; index < requests.Length && running < limit; index++)
            {
                Entity entity = requests[index];
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
                GenerateChunkJob job = new GenerateChunkJob
                {
                    Settings = settings,
                    ResourceSettings = resourceSettings,
                    Coordinate = slot.Coordinate,
                    Tiles = slot.Tiles,
                    Resources = slot.Resources
                };

                // JobはECSへ一切触れず入力も値コピーです。Dependencyへ連結すると表示側が再び待つため、
                // このSystemが専用Handleを保持して完了確認と破棄を管理します。
                slot.Handle = job.Schedule();
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
        }

        /// <summary>空き枠を返し、不足時だけ4096件のPersistent配列を追加確保します。</summary>
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
                    NativeArrayOptions.UninitializedMemory)
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
            /// <summary>地形4096件の作業配列です。枠の再利用中は保持し、System終了時にDisposeします。</summary>
            public NativeArray<GeneratedTile> Tiles;
            /// <summary>資源4096件の作業配列です。地形配列と同じ所有・解放規則です。</summary>
            public NativeArray<GeneratedResourceTile> Resources;
        }

        /// <summary>
        /// 値コピーの設定と絶対座標だけから、専用配列へ1チャンクを生成するBurst Jobです。
        /// </summary>
        [BurstCompile]
        private struct GenerateChunkJob : IJob
        {
            /// <summary>WorldのSingletonから値コピーした、Job実行中は読み取り専用の生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>WorldのSingletonから値コピーした、資源群の大きさと密度設定です。</summary>
            public ResourceGenerationSettings ResourceSettings;

            /// <summary>生成する絶対チャンク座標です。Jobの実行中は変化しません。</summary>
            public ChunkCoordinateComponent Coordinate;

            /// <summary>このJobだけが全件上書きし、完了後にSystemへ返す地形配列です。</summary>
            public NativeArray<GeneratedTile> Tiles;

            /// <summary>地形と同じ位置に対応し、完了後にSystemへ返す資源配列です。</summary>
            public NativeArray<GeneratedResourceTile> Resources;

            /// <summary>
            /// Worker上で4096セルを生成します。ECSの状態やBufferへはアクセスしません。
            /// </summary>
            public void Execute()
            {
                // 各タイルはワールド整数座標だけから求めるため、隣接チャンクの生成済み状態を参照しません。
                for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
                {
                    for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                    {
                        int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                        int worldTileX = WorldGrid.ToWorldTileX(Coordinate.X, localX);
                        int worldTileY = WorldGrid.ToWorldTileY(Coordinate.Y, localY);
                        GeneratedTile terrainTile = DeterministicChunkGenerator.GenerateTile(
                            Settings,
                            worldTileX,
                            worldTileY);
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
    }

    /// <summary>
    /// Tick加算後・Command処理前に地形の実行中バッチを一括公開する、個体群World専用の同期境界です。
    /// 後続Gameplayの個体群初期化は、この境界で揃った地域全体へ同じTickでIDを発行します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareFixedTickSimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(SimulationTickAdvanceSystem))]
    [UpdateBefore(typeof(FarmwareCommandProcessingSystemGroup))]
    public partial struct ChunkGenerationFixedTickPublicationSystem : ISystem
    {
        /// <summary>地形単独Worldでは待ち合わせを追加せず、個体群とTickがあるWorldだけで動作させます。</summary>
        /// <param name="state">更新条件を登録するSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AnimalSpeciesCatalog>();
            state.RequireForUpdate<SimulationTickState>();
        }

        /// <summary>同じWorldの生成Systemを探し、そのSystemだけが所有するJobバッチをTick境界で確定します。</summary>
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
            generator.CompleteAndPublishBatch(state.EntityManager);
        }
    }
}
