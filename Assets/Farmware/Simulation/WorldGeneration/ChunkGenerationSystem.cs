// このファイルは、生成要求が有効なチャンクEntityを並列Jobで処理し、4096タイルのDynamic Bufferを作ります。
// データ生成だけを担当し、TilemapやGameObjectへの反映は後続のPresentation層へ委ねます。

using Unity.Burst;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 有効なChunkGenerationRequestを持つチャンクだけを生成するECS Systemです。
    /// 完了後は要求を無効化し、生成済みの大量チャンクを毎フレーム再走査しないようにします。
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChunkStreamingSystem))]
    public partial struct ChunkGenerationSystem : ISystem
    {
        /// <summary>
        /// System作成時に呼ばれ、ワールド生成設定が存在するフレームだけ更新する条件を登録します。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WorldGenerationSettings>();
        }

        /// <summary>
        /// 毎フレーム呼ばれ、待機中チャンクを並列生成するJobを現在の依存関係へ連結します。
        /// Job完了待ちはここで行わず、後続SystemへDependencyを渡してメインスレッドの同期点を避けます。
        /// </summary>
        /// <param name="state">SystemのQuery、依存関係、Worldへアクセスする状態です。</param>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            WorldGenerationSettings settings = SystemAPI.GetSingleton<WorldGenerationSettings>();
            GenerateQueuedChunksJob job = new GenerateQueuedChunksJob
            {
                Settings = settings
            };

            state.Dependency = job.ScheduleParallel(state.Dependency);
        }

        /// <summary>
        /// 1チャンクEntityずつ4096タイルを生成するBurst対応Jobです。
        /// Entity間に共有書き込みを持たないため、チャンク単位で安全に並列実行できます。
        /// </summary>
        [BurstCompile]
        private partial struct GenerateQueuedChunksJob : IJobEntity
        {
            /// <summary>WorldのSingletonから値コピーした、Job実行中は読み取り専用の生成設定です。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>
            /// 1つの要求チャンクを生成し、状態をReadyToApplyへ進めて要求を無効化します。
            /// </summary>
            /// <param name="coordinate">このEntityが表す論理チャンク座標です。</param>
            /// <param name="status">生成段階と使用バージョンを書き戻すComponentです。</param>
            /// <param name="tiles">4096件へ確保して生成結果を書き込むDynamic Bufferです。</param>
            /// <param name="requestEnabled">完了時にfalseへ切り替える生成要求の有効状態です。</param>
            private void Execute(
                in ChunkCoordinateComponent coordinate,
                ref ChunkGenerationStatus status,
                ref DynamicBuffer<GeneratedTile> tiles,
                EnabledRefRW<ChunkGenerationRequest> requestEnabled)
            {
                status.Phase = ChunkGenerationPhase.GeneratingData;
                tiles.ResizeUninitialized(WorldGridConstants.TilesPerChunk);

                // 各タイルはワールド整数座標だけから求めるため、隣接チャンクの生成済み状態を参照しません。
                for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
                {
                    for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                    {
                        int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                        int worldTileX = WorldGrid.ToWorldTileX(coordinate.X, localX);
                        int worldTileY = WorldGrid.ToWorldTileY(coordinate.Y, localY);
                        tiles[tileIndex] = DeterministicChunkGenerator.GenerateTile(
                            Settings,
                            worldTileX,
                            worldTileY);
                    }
                }

                status.GeneratedWithVersion = Settings.GeneratorVersion;
                status.Phase = ChunkGenerationPhase.ReadyToApply;

                // Enableable Componentを無効化すればArchetypeは変わらず、次フレームの生成Queryからだけ外れます。
                requestEnabled.ValueRW = false;
            }
        }
    }
}
