// このファイルは、Map Viewerの未訪問チャンクを製品のChunkGenerationSystemと同じBurst Jobで完成させます。
// Main Threadで専用WorldへJobを投入し、未完了中はEditorへ制御を返します。Default World、固定Tick、動物初期化には接続しません。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>Window要求の寿命だけ独立Worldを所有し、完成した自然生成値を上限付きキャッシュへ渡します。</summary>
    public static class WorldGenerationMapChunkGenerator
    {
        /// <summary>製品Jobの完了確認を行う間隔です。単位はミリ秒で、Unity Main Threadを待機させません。</summary>
        private const int CompletionCheckDelayMilliseconds = 1;

        /// <summary>必要チャンクのキャッシュ不足分を直列生成します。必ずUnity Main Threadから呼び出します。</summary>
        /// <param name="settings">Seed・GeneratorVersionを含む全固定生成設定です。</param>
        /// <param name="catalogs">呼び出し側がMain Threadで構築し、Task終了まで保持するカタログです。</param>
        /// <param name="coordinates">Samplerと同じ規則で列挙したSurfaceチャンクです。</param>
        /// <param name="cache">今回のWindowが所有するManagedキャッシュです。</param>
        /// <param name="cancellationToken">取消後は新しい生成を投入しません。実行中Jobは非同期に完了確認してから破棄します。</param>
        /// <param name="progress">完成チャンク数に応じて0〜0.95を通知します。</param>
        /// <returns>全必要チャンクのコピー完了、取消、または生成失敗までのTaskです。</returns>
        public static async Task PrepareAsync(WorldGenerationSettings settings,
            HydrologyDiagnosticTerrainCatalogs catalogs, IReadOnlyList<ChunkCoordinate> coordinates,
            WorldGenerationMapChunkCache cache, CancellationToken cancellationToken, IProgress<float> progress = null)
        {
            if (SynchronizationContext.Current == null)
                throw new InvalidOperationException("地図のJob生成はUnity Main Threadから開始してください。");
            // 事前生成がキャッシュ容量を超えると先頭の完成結果を失うため、生成前に有限範囲を検証します。
            if (coordinates.Count > WorldGenerationMapChunkCache.MaximumCachedChunks)
                throw new ArgumentOutOfRangeException(nameof(coordinates), "地図の事前生成チャンク数が保持上限を超えています。");
            // 既存の同期診断と同じGateを使い、取消済み要求と最新要求が大容量作業領域を並列確保しないようにします。
            await WorldGenerationMapSampler.CompletedChunkGenerationGate.WaitAsync(cancellationToken);
            World world = null;
            try
            {
                // 既存の要求対象を先に保護し、生成途中の容量超過で事前完成値を失うことを防ぎます。
                cache.PrioritizeRequired(settings, coordinates);
                progress?.Report(0.01f);
                SystemHandle system = default;
                for (int index = 0; index < coordinates.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ChunkCoordinate coordinate = coordinates[index];
                    if (coordinate.Layer != WorldLayer.Surface)
                        throw new InvalidOperationException("地図の完成地形生成に未対応のWorldLayerが指定されました。");
                    if (!cache.Contains(settings, coordinate))
                    {
                        if (world == null)
                        {
                            // このWorldはPlayer Loopへ登録せず、生成Systemだけを明示的に使用します。
                            world = new World("Terrain Map Generation", WorldFlags.Editor);
                            system = world.GetOrCreateSystem<ChunkGenerationSystem>();
                        }
                        Entity target = CreateRequest(world.EntityManager, coordinate);
                        Schedule(world, system, target, settings, catalogs);
                        // 取消をDelayへ渡すとfinallyのWorld.Disposeが実行中Jobを同期完了させてしまいます。
                        // 現在の1件が自然に完成するまでUIへ制御を返し、取消時は結果を破棄して次の投入を止めます。
                        while (world.IsCreated && !TryPublish(world, system))
                            await Task.Delay(CompletionCheckDelayMilliseconds);
                        if (!world.IsCreated) throw new OperationCanceledException("地図生成Worldが終了しました。");
                        cancellationToken.ThrowIfCancellationRequested();
                        EntityManager manager = world.EntityManager;
                        if (manager.GetComponentData<ChunkGenerationStatus>(target).Phase != ChunkGenerationPhase.ReadyToApply)
                            throw new InvalidOperationException($"地図の地形生成に失敗しました: Chunk=({coordinate.X}, {coordinate.Y})");
                        cache.Remember(settings, coordinate, catalogs.RuntimeIds,
                            manager.GetBuffer<GeneratedTile>(target, true).AsNativeArray(),
                            manager.GetBuffer<GeneratedTerrainCell>(target, true).AsNativeArray(),
                            manager.GetBuffer<GeneratedHydrologyCell>(target, true).AsNativeArray(),
                            manager.GetBuffer<GeneratedSurfaceWaterTile>(target, true).AsNativeArray(),
                            manager.GetBuffer<GeneratedHydrologyTile>(target, true).AsNativeArray());
                        manager.DestroyEntity(target);
                    }
                    progress?.Report((index + 1f) / coordinates.Count * 0.95f);
                }
            }
            finally
            {
                // 通常・取消・失敗のいずれもWorldを先に破棄し、借用Blobは呼び出し側がTask終了後に破棄します。
                if (world != null && world.IsCreated) world.Dispose();
                WorldGenerationMapSampler.CompletedChunkGenerationGate.Release();
            }
        }

        /// <summary>製品生成Systemが要求するComponentと6 Bufferを持つ空Entityを専用Worldへ作ります。</summary>
        /// <param name="manager">専用Worldの管理境界です。</param>
        /// <param name="coordinate">生成対象座標です。</param>
        /// <returns>生成完了後に値コピーして破棄するEntityです。</returns>
        private static Entity CreateRequest(EntityManager manager, ChunkCoordinate coordinate)
        {
            Entity entity = manager.CreateEntity(typeof(ChunkCoordinateComponent), typeof(ChunkGenerationStatus), typeof(ChunkGenerationRequest));
            manager.SetComponentData(entity, new ChunkCoordinateComponent { Layer = coordinate.Layer, X = coordinate.X, Y = coordinate.Y });
            manager.SetComponentData(entity, new ChunkGenerationStatus { Phase = ChunkGenerationPhase.Queued });
            manager.AddBuffer<GeneratedTile>(entity);
            manager.AddBuffer<GeneratedTerrainCell>(entity);
            manager.AddBuffer<GeneratedResourceTile>(entity);
            manager.AddBuffer<GeneratedHydrologyCell>(entity);
            manager.AddBuffer<GeneratedSurfaceWaterTile>(entity);
            manager.AddBuffer<GeneratedHydrologyTile>(entity);
            return entity;
        }

        /// <summary>asyncメソッドにrefローカルを保持せず、Main Threadで製品の有限Jobバッチを1件投入します。</summary>
        /// <param name="world">専用Worldです。</param>
        /// <param name="handle">作成済み生成Systemです。</param>
        /// <param name="target">要求Entityです。投入失敗の診断に使います。</param>
        /// <param name="settings">固定設定の値コピーです。</param>
        /// <param name="catalogs">Task終了まで有効な読み取り専用カタログです。</param>
        private static void Schedule(World world, SystemHandle handle, Entity target,
            WorldGenerationSettings settings, HydrologyDiagnosticTerrainCatalogs catalogs)
        {
            ref ChunkGenerationSystem system = ref world.Unmanaged.GetUnsafeSystemRef<ChunkGenerationSystem>(handle);
            if (!system.ScheduleBatch(world.EntityManager, settings, BiomeDefinitionCatalog.CreateDefault(),
                ResourceGenerationSettings.CreateDefault(), catalogs.PaletteCatalog, catalogs.RuntimeIds, 1))
                throw new InvalidOperationException($"地図の地形生成要求を投入できません: Entity={target.Index}");
            // このWorldはPlayer Loopへ入れないため、フレーム末の暗黙送出に頼らずWorkerへ渡します。完了待ちはしません。
            Unity.Jobs.JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>全Job完成を確認した場合だけ公開します。未完成JobのCompleteは呼びません。</summary>
        /// <param name="world">要求を所有する専用Worldです。</param>
        /// <param name="handle">生成Systemです。</param>
        /// <returns>バッチが完成・公開済みならtrueです。</returns>
        private static bool TryPublish(World world, SystemHandle handle)
        {
            ref ChunkGenerationSystem system = ref world.Unmanaged.GetUnsafeSystemRef<ChunkGenerationSystem>(handle);
            return system.TryCompleteAndPublishBatch(world.EntityManager);
        }
    }
}
