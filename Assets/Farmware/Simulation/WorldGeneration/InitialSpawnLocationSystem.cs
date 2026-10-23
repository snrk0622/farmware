// このファイルは、C5-Cで完成したECSチャンクから新規ワールドの初期スポーン候補を段階的に探します。
// 分類前の単一タイルを別経路で再生成せず、ChunkGenerationSystemが公開した18Biome・水文・資源を正として読み取ります。

using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 原点チャンクから有限のリングを順に要求し、初期スポーン許可Biomeにある乾いた通行可能セルを一度だけ記録します。
    /// FarmwareInitializationSystemGroupで要求を作り、FarmwareSimulationSystemGroupのChunkGenerationSystemが完成させた後に結果を読みます。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareInitializationSystemGroup))]
    public partial struct InitialSpawnLocationSystem : ISystem
    {
        /// <summary>
        /// 初期実装で探索する最大チャンク半径です。0、1、2の三段階で最大5×5チャンクを調べます。
        /// 無制限探索で開始処理を終えられなくしないため固定上限を持たせ、将来の距離調整は専用設定へ分離します。
        /// </summary>
        private const int MaximumSearchRadiusInChunks = 2;

        /// <summary>Systemが作成した初期探索用チャンクを含め、完成状態とBufferを読むQueryです。</summary>
        private EntityQuery chunkQuery;

        /// <summary>探索中・成功・失敗をPresentationへ伝えるSingleton Entityです。</summary>
        private Entity searchStatusEntity;

        /// <summary>現在完成待ちの正方形リング半径です。単位は64×64論理チャンクです。</summary>
        private int currentSearchRadiusInChunks;

        /// <summary>0は未要求、1は現在リングの生成待ち、2は探索終了です。</summary>
        private byte searchState;

        /// <summary>
        /// System作成時に完成チャンクQueryと待機状態Singletonを準備し、生成に必要な全設定がそろうまで更新を待ちます。
        /// </summary>
        /// <param name="state">Systemの更新条件、依存関係、EntityManagerへアクセスする状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            chunkQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ChunkCoordinateComponent, ChunkGenerationStatus>()
                .WithAll<GeneratedTile, GeneratedResourceTile>()
                .Build(ref state);

            searchStatusEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(
                searchStatusEntity,
                new InitialSpawnSearchStatus
                {
                    Phase = InitialSpawnSearchPhase.WaitingForSettings
                });
            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<BiomeClassificationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
            currentSearchRadiusInChunks = 0;
            searchState = 0;
        }

        /// <summary>
        /// 未要求なら次のリングを作り、完成待ちなら待機し、完成後は最寄りの有効セルを選びます。
        /// 旧12×8平地・資源距離探索や分類前タイルの同期再生成は行いません。
        /// </summary>
        /// <param name="state">完成チャンクBufferの読取りと要求Entity作成に使うSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            // ロード処理などが既に確定地点を復元した場合は、再探索や二重Singleton作成を行いません。
            if (SystemAPI.TryGetSingleton<InitialSpawnLocation>(out _))
            {
                searchState = 2;
                SetSearchPhase(ref state, InitialSpawnSearchPhase.Succeeded);
                state.Enabled = false;
                return;
            }

            if (searchState == 0)
            {
                QueueSearchRing(state.EntityManager, currentSearchRadiusInChunks);
                searchState = 1;
                SetSearchPhase(ref state, InitialSpawnSearchPhase.Searching);
                return;
            }

            if (searchState != 1
                || !IsSearchRingComplete(state.EntityManager, currentSearchRadiusInChunks))
            {
                // ChunkGenerationSystemは別System Groupで非同期生成するため、未完成フレームでは待たずに制御を返します。
                return;
            }

            BiomeClassificationSettings biomeSettings =
                SystemAPI.GetSingleton<BiomeClassificationSettings>();
            if (TryFindNearestCandidate(
                    state.EntityManager,
                    biomeSettings,
                    currentSearchRadiusInChunks,
                    out int tileX,
                    out int tileY)
                && IsNearestCandidateProven(
                    currentSearchRadiusInChunks,
                    tileX,
                    tileY))
            {
                Entity locationEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(
                    locationEntity,
                    InitialSpawnLocation.FromTile(tileX, tileY));
                searchState = 2;
                SetSearchPhase(ref state, InitialSpawnSearchPhase.Succeeded);
                state.Enabled = false;
                return;
            }

            if (currentSearchRadiusInChunks < MaximumSearchRadiusInChunks)
            {
                // 近いリングで候補がなかった場合だけ次の8、16チャンクへ広げ、遠方を先に選ばない決定論を維持します。
                currentSearchRadiusInChunks++;
                QueueSearchRing(state.EntityManager, currentSearchRadiusInChunks);
                return;
            }

            searchState = 2;
            SetSearchPhase(ref state, InitialSpawnSearchPhase.Failed);
            Debug.LogError(
                "初期スポーン地点を5×5チャンク内で発見できませんでした。分類前地形へ戻さず、探索失敗として記録します。");
            state.Enabled = false;
        }

        /// <summary>
        /// 指定半径の外周チャンクだけを座標順に要求します。内側リングは前段階で既に要求済みです。
        /// </summary>
        /// <param name="manager">要求Entityと空Bufferを作成する同じECS WorldのManagerです。</param>
        /// <param name="radiusInChunks">0〜2の現在探索リング半径です。</param>
        private void QueueSearchRing(EntityManager manager, int radiusInChunks)
        {
            for (int chunkY = -radiusInChunks; chunkY <= radiusInChunks; chunkY++)
            {
                for (int chunkX = -radiusInChunks; chunkX <= radiusInChunks; chunkX++)
                {
                    if (MaximumAbsolute(chunkX, chunkY) != radiusInChunks
                        || TryFindChunkEntity(manager, chunkX, chunkY, out _))
                    {
                        continue;
                    }

                    Entity entity = manager.CreateEntity();
                    manager.AddComponentData(
                        entity,
                        new ChunkCoordinateComponent
                        {
                            Layer = WorldLayer.Surface,
                            X = chunkX,
                            Y = chunkY
                        });
                    manager.AddComponentData(
                        entity,
                        new ChunkGenerationStatus
                        {
                            Phase = ChunkGenerationPhase.Queued,
                            GeneratedWithVersion = 0
                        });
                    manager.AddComponent<ChunkGenerationRequest>(entity);
                    manager.AddBuffer<GeneratedTile>(entity);
                    manager.AddBuffer<GeneratedResourceTile>(entity);
                    manager.AddBuffer<GeneratedHydrologyCell>(entity);
                    manager.AddBuffer<GeneratedSurfaceWaterTile>(entity);
                    manager.AddBuffer<GeneratedHydrologyTile>(entity);
                }
            }
        }

        /// <summary>
        /// 現在リングの全チャンクが成功または失敗の終端状態へ到達したか確認します。
        /// </summary>
        /// <param name="manager">チャンク座標と生成状態を読むManagerです。</param>
        /// <param name="radiusInChunks">完成を待つリング半径です。</param>
        /// <returns>外周すべてが生成中ではなくなった場合はtrueです。</returns>
        private bool IsSearchRingComplete(EntityManager manager, int radiusInChunks)
        {
            for (int chunkY = -radiusInChunks; chunkY <= radiusInChunks; chunkY++)
            {
                for (int chunkX = -radiusInChunks; chunkX <= radiusInChunks; chunkX++)
                {
                    if (MaximumAbsolute(chunkX, chunkY) != radiusInChunks)
                    {
                        continue;
                    }

                    if (!TryFindChunkEntity(manager, chunkX, chunkY, out Entity entity))
                    {
                        return false;
                    }

                    ChunkGenerationPhase phase =
                        manager.GetComponentData<ChunkGenerationStatus>(entity).Phase;
                    if (phase == ChunkGenerationPhase.Queued
                        || phase == ChunkGenerationPhase.GeneratingData
                        || phase == ChunkGenerationPhase.Unrequested)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Systemが所有する状態Singletonを更新し、Presentationへ探索の開始または終了を通知します。
        /// </summary>
        /// <param name="state">状態Entityへ書き込むためのEntityManagerを保持するSystem状態です。</param>
        /// <param name="phase">新しく記録する探索段階です。</param>
        private void SetSearchPhase(ref SystemState state, InitialSpawnSearchPhase phase)
        {
            state.EntityManager.SetComponentData(
                searchStatusEntity,
                new InitialSpawnSearchStatus { Phase = phase });
        }

        /// <summary>
        /// 現在までに完成した正方形範囲を走査し、原点タイルへ最も近い許可セルを選びます。
        /// </summary>
        /// <param name="manager">完成地形・資源Bufferを読むManagerです。</param>
        /// <param name="biomeSettings">初期スポーン許可フラグを持つ新18Biome Definitionです。</param>
        /// <param name="radiusInChunks">走査済み範囲のチャンク半径です。</param>
        /// <param name="tileX">成功時に選んだ水平ワールドタイル座標です。</param>
        /// <param name="tileY">成功時に選んだ垂直ワールドタイル座標です。</param>
        /// <returns>乾いた通行可能・資源非占有・許可Biomeのセルが存在する場合はtrueです。</returns>
        private bool TryFindNearestCandidate(
            EntityManager manager,
            in BiomeClassificationSettings biomeSettings,
            int radiusInChunks,
            out int tileX,
            out int tileY)
        {
            bool found = false;
            long bestSquaredDistance = long.MaxValue;
            tileX = 0;
            tileY = 0;

            for (int chunkY = -radiusInChunks; chunkY <= radiusInChunks; chunkY++)
            {
                for (int chunkX = -radiusInChunks; chunkX <= radiusInChunks; chunkX++)
                {
                    if (!TryFindChunkEntity(manager, chunkX, chunkY, out Entity entity))
                    {
                        continue;
                    }

                    ChunkGenerationStatus status =
                        manager.GetComponentData<ChunkGenerationStatus>(entity);
                    if (status.Phase == ChunkGenerationPhase.Failed)
                    {
                        continue;
                    }

                    DynamicBuffer<GeneratedTile> tiles = manager.GetBuffer<GeneratedTile>(entity);
                    DynamicBuffer<GeneratedResourceTile> resources =
                        manager.GetBuffer<GeneratedResourceTile>(entity);
                    if (tiles.Length != WorldGridConstants.TilesPerChunk
                        || resources.Length != WorldGridConstants.TilesPerChunk)
                    {
                        continue;
                    }

                    for (int index = 0; index < WorldGridConstants.TilesPerChunk; index++)
                    {
                        GeneratedTile terrain = tiles[index];
                        if (!IsInitialSpawnCandidate(
                                biomeSettings,
                                terrain,
                                resources[index]))
                        {
                            continue;
                        }

                        int candidateX = WorldGrid.ToWorldTileX(
                            chunkX,
                            index % WorldGridConstants.LogicalChunkSize);
                        int candidateY = WorldGrid.ToWorldTileY(
                            chunkY,
                            index / WorldGridConstants.LogicalChunkSize);
                        long squaredDistance = ((long)candidateX * candidateX)
                            + ((long)candidateY * candidateY);
                        if (!found
                            || squaredDistance < bestSquaredDistance
                            || (squaredDistance == bestSquaredDistance
                                && IsCoordinateBefore(candidateX, candidateY, tileX, tileY)))
                        {
                            found = true;
                            bestSquaredDistance = squaredDistance;
                            tileX = candidateX;
                            tileY = candidateY;
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 現在候補より近い、または同距離で座標順が先の候補を、次の未探索リングが持ち得ないことを確認します。
        /// </summary>
        /// <param name="radiusInChunks">現在まで完成・走査した正方形のチャンク半径です。</param>
        /// <param name="candidateX">現在最良候補の水平ワールドタイル座標です。</param>
        /// <param name="candidateY">現在最良候補の垂直ワールドタイル座標です。</param>
        /// <returns>現在候補を原点最近傍として安全に確定できる場合はtrueです。</returns>
        private static bool IsNearestCandidateProven(
            int radiusInChunks,
            int candidateX,
            int candidateY)
        {
            if (radiusInChunks >= MaximumSearchRadiusInChunks)
            {
                // 有限探索上限まで走査済みなら、範囲内の最良候補を確定します。
                return true;
            }

            long candidateSquaredDistance = ((long)candidateX * candidateX)
                + ((long)candidateY * candidateY);

            // チャンク0はタイル0〜63を担当するため、原点に対して正負対称ではありません。
            // 次リングの負側チャンクは正側より原点へ近く、最も近いタイルの絶対座標は
            // 現在半径×64+1です。他方の軸を0にできるので、この二乗が未探索範囲の距離下限です。
            long nextRingMinimumAbsoluteTile =
                ((long)radiusInChunks * WorldGridConstants.LogicalChunkSize) + 1L;
            long nextRingMinimumSquaredDistance =
                nextRingMinimumAbsoluteTile * nextRingMinimumAbsoluteTile;

            // 同距離ではY、Xの昇順で負側候補が先になる可能性があるため、
            // 未探索距離が現在値と等しい場合も次リングを生成して比較します。
            return candidateSquaredDistance < nextRingMinimumSquaredDistance;
        }

        /// <summary>
        /// 1セルが現行仕様の初期スポーン候補かを、完成済み地形・資源とBiome Definitionだけから判定します。
        /// </summary>
        /// <param name="biomeSettings">安定IDに対応する許可フラグを検索する設定です。</param>
        /// <param name="terrain">ChunkGenerationSystemがC5-Cまで完成させた地形です。</param>
        /// <param name="resource">同じ座標の暫定自然資源です。</param>
        /// <returns>乾いた陸上を歩け、資源に埋まらず、Biomeが開始を許可する場合はtrueです。</returns>
        private static bool IsInitialSpawnCandidate(
            in BiomeClassificationSettings biomeSettings,
            in GeneratedTile terrain,
            in GeneratedResourceTile resource)
        {
            if (terrain.Ground != GroundType.Land
                || terrain.WaterSalinity != WaterSalinity.None
                || terrain.MovementCost == 0
                || resource.Type != GeneratedResourceType.None
                || !BiomeDefinitionCatalog.TryGetDefinition(
                    biomeSettings,
                    (byte)terrain.Biome,
                    out BiomeDefinition definition))
            {
                return false;
            }

            return (definition.Flags & BiomeDefinitionFlags.AllowsInitialSpawn) != 0;
        }

        /// <summary>
        /// Query内から指定Surfaceチャンクを検索します。探索範囲は最大25件なので管理Dictionaryを常駐させません。
        /// </summary>
        /// <param name="manager">座標Componentを読むManagerです。</param>
        /// <param name="chunkX">検索する水平論理チャンク座標です。</param>
        /// <param name="chunkY">検索する垂直論理チャンク座標です。</param>
        /// <param name="entity">成功時に一致したEntityを返します。</param>
        /// <returns>同じSurface座標のチャンクが1件見つかった場合はtrueです。</returns>
        private bool TryFindChunkEntity(
            EntityManager manager,
            int chunkX,
            int chunkY,
            out Entity entity)
        {
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            for (int index = 0; index < entities.Length; index++)
            {
                ChunkCoordinateComponent coordinate = coordinates[index];
                if (coordinate.Layer == WorldLayer.Surface
                    && coordinate.X == chunkX
                    && coordinate.Y == chunkY)
                {
                    entity = entities[index];
                    return true;
                }
            }

            entity = Entity.Null;
            return false;
        }

        /// <summary>同じ距離の候補を垂直座標、水平座標の昇順で一意に並べます。</summary>
        /// <param name="candidateX">比較する新候補の水平座標です。</param>
        /// <param name="candidateY">比較する新候補の垂直座標です。</param>
        /// <param name="currentX">現在の最良候補の水平座標です。</param>
        /// <param name="currentY">現在の最良候補の垂直座標です。</param>
        /// <returns>新候補を優先する場合はtrueです。</returns>
        private static bool IsCoordinateBefore(
            int candidateX,
            int candidateY,
            int currentX,
            int currentY)
        {
            return candidateY < currentY
                || (candidateY == currentY && candidateX < currentX);
        }

        /// <summary>2軸の絶対値の大きい方を返し、正方形リングの外周判定へ使います。</summary>
        /// <param name="x">水平オフセットです。</param>
        /// <param name="y">垂直オフセットです。</param>
        /// <returns>符号を除いた大きい方の値です。</returns>
        private static int MaximumAbsolute(int x, int y)
        {
            int absoluteX = x < 0 ? -x : x;
            int absoluteY = y < 0 ? -y : y;
            return absoluteX > absoluteY ? absoluteX : absoluteY;
        }
    }
}
