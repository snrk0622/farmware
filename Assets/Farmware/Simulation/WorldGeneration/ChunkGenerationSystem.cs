// このファイルは、地形・資源をECSから独立した作業領域で生成し、完成結果だけをECSへ公開します。
// ChunkStreamingSystemが要求と寿命、Presentationが確定済みBufferの表示を担当します。

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Farmware.Simulation;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 有効なChunkGenerationRequestを持つチャンクだけを生成し、同じ座標の地形と資源を対応付けるECS Systemです。
    /// 開始時に要求を無効化し、後続更新で完成した結果だけを公開して表示側のJob待ちを避けます。
    /// FarmwareSimulationSystemGroupでストリーミング要求の後に動き、固定Tickのゲームルールからは独立します。
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
        /// 毎Simulation更新で以前開始したJobの完成結果を公開してから、新規要求を空き枠へ投入します。
        /// 新規Jobは同じ呼び出しでは公開せず、未完成JobのCompleteも行いません。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            using (PublishProfilerMarker.Auto())
            {
                PublishCompletedResults(ref state);
            }

            using (ScheduleProfilerMarker.Auto())
            {
                ScheduleRequests(ref state);
            }
        }

        /// <summary>
        /// WorldまたはSystemの破棄時だけ残ったJobを待ち、使用中配列を先に解放しないようにします。
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
        /// <param name="state">公開先EntityManagerを持つ状態です。</param>
        private void PublishCompletedResults(ref SystemState state)
        {
            EntityManager manager = state.EntityManager;
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
}
