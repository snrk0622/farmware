// このファイルは、表示と診断が中心と周囲8チャンクの完成自然地形・永続差分を同じ規則で参照することを検証します。
// 水文・Biome確定前の単一タイル生成や旧外周キャッシュへ戻らず、負座標とワールド端でも有効地形を共有します。

using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>EditModeの最小Worldで、表示用の完成有効地形近傍とFactory契約を検証します。</summary>
    public sealed class CompletedChunkTerrainNeighborhoodTests
    {
        /// <summary>
        /// 完成チャンク索引の周囲1件を欠かし、Factoryが未完成近傍を表示へ渡さないことを保証します。
        /// </summary>
        [Test]
        public void NeighborhoodFactory_RequiresEveryInWorldCompletedChunk()
        {
            using World world = new World("CompletedTerrainNeighborhoodFactoryTests");
            EntityManager entityManager = world.EntityManager;
            var completedEntities = new System.Collections.Generic.Dictionary<ChunkCoordinate, Entity>();
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    Entity entity = entityManager.CreateEntity();
                    DynamicBuffer<GeneratedTerrainCell> buffer = entityManager.AddBuffer<GeneratedTerrainCell>(entity);
                    buffer.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
                    completedEntities[new ChunkCoordinate(offsetX, offsetY)] = entity;
                }
            }

            Assert.That(
                CompletedChunkTerrainNeighborhoodFactory.TryCreate(
                    entityManager,
                    completedEntities,
                    new ChunkCoordinate(0, 0),
                    out _),
                Is.True);

            completedEntities.Remove(new ChunkCoordinate(-1, 0));
            Assert.That(
                CompletedChunkTerrainNeighborhoodFactory.TryCreate(
                    entityManager,
                    completedEntities,
                    new ChunkCoordinate(0, 0),
                    out _),
                Is.False,
                "未完成の左隣を基礎生成で補完して表示を確定してはいけません。");
        }

        /// <summary>
        /// 中心と負側隣接チャンクへ疎なWall差分を置き、3×3近傍参照が同じ有効地形を返すことを保証します。
        /// </summary>
        [Test]
        public void EffectiveNeighborhood_AppliesCenterAndNegativeBoundaryDeltas()
        {
            using TerrainGenerationTestCatalogSet catalogs = TerrainGenerationTestCatalogSet.Create();
            using World world = new World("EffectiveTerrainNeighborhoodTests");
            EntityManager entityManager = world.EntityManager;
            var completedEntities = new System.Collections.Generic.Dictionary<ChunkCoordinate, Entity>();
            var deltaOwners = new System.Collections.Generic.Dictionary<ChunkCoordinate, Entity>();
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    var coordinate = new ChunkCoordinate(WorldLayer.Surface, offsetX, offsetY);
                    Entity terrainEntity = entityManager.CreateEntity();
                    DynamicBuffer<GeneratedTerrainCell> terrain =
                        entityManager.AddBuffer<GeneratedTerrainCell>(terrainEntity);
                    terrain.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
                    for (int tileIndex = 0; tileIndex < terrain.Length; tileIndex++)
                    {
                        terrain[tileIndex] = new GeneratedTerrainCell
                        {
                            Biome = BiomeId.TemperateGrassland,
                            GroundBlockRuntimeId = catalogs.GroundRuntimeId
                        };
                    }

                    completedEntities.Add(coordinate, terrainEntity);
                }
            }

            // 中心左下セルと、西隣チャンク右下セルへ同じWallを追加し、負座標をまたぐ境界を作ります。
            AddWallDeltaOwner(entityManager, deltaOwners, new ChunkCoordinate(WorldLayer.Surface, 0, 0), 0, catalogs.WallRuntimeId);
            AddWallDeltaOwner(entityManager, deltaOwners, new ChunkCoordinate(WorldLayer.Surface, -1, 0), 63, catalogs.WallRuntimeId);

            Assert.That(
                CompletedChunkTerrainNeighborhoodFactory.TryCreateEffective(
                    entityManager,
                    completedEntities,
                    deltaOwners,
                    new ChunkCoordinate(WorldLayer.Surface, 0, 0),
                    catalogs.TerrainCatalog,
                    out CompletedChunkTerrainNeighborhood neighborhood),
                Is.True);
            Assert.That(neighborhood.TryGetTile(0, 0, out GeneratedTerrainCell center), Is.True);
            Assert.That(neighborhood.TryGetTile(-1, 0, out GeneratedTerrainCell west), Is.True);
            Assert.That(center.WallBlockRuntimeId, Is.EqualTo(catalogs.WallRuntimeId));
            Assert.That(west.WallBlockRuntimeId, Is.EqualTo(catalogs.WallRuntimeId));

        }

        /// <summary>指定チャンクへWall追加差分を1件持つ永続所有者を作り、テスト索引へ登録します。</summary>
        private static void AddWallDeltaOwner(
            EntityManager entityManager,
            System.Collections.Generic.IDictionary<ChunkCoordinate, Entity> owners,
            ChunkCoordinate coordinate,
            ushort localTileIndex,
            DefinitionRuntimeId wallRuntimeId)
        {
            Entity ownerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(ownerEntity, new TerrainChunkDeltaOwner
            {
                Layer = coordinate.Layer,
                ChunkX = coordinate.X,
                ChunkY = coordinate.Y
            });
            DynamicBuffer<TerrainCellDelta> deltas =
                entityManager.AddBuffer<TerrainCellDelta>(ownerEntity);
            deltas.Add(new TerrainCellDelta
            {
                LocalTileIndex = localTileIndex,
                ChangeMask = TerrainCellChangeMask.WallBlock,
                WallBlockRuntimeId = wallRuntimeId
            });
            owners.Add(coordinate, ownerEntity);
        }

        /// <summary>
        /// intワールド左下端では、ワールド外3方向のEntityを要求せず、外側を非接続として返すことを保証します。
        /// </summary>
        [Test]
        public void NeighborhoodFactory_WorldMinimumCorner_DoesNotRequireOutsideChunks()
        {
            using World world = new World("CompletedTerrainNeighborhoodWorldEdgeTests");
            EntityManager entityManager = world.EntityManager;
            var completedEntities = new System.Collections.Generic.Dictionary<ChunkCoordinate, Entity>();
            int minimum = WorldGridConstants.MinimumLogicalChunkCoordinate;
            for (int offsetY = 0; offsetY <= 1; offsetY++)
            {
                for (int offsetX = 0; offsetX <= 1; offsetX++)
                {
                    Entity entity = entityManager.CreateEntity();
                    DynamicBuffer<GeneratedTerrainCell> buffer = entityManager.AddBuffer<GeneratedTerrainCell>(entity);
                    buffer.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
                    completedEntities[new ChunkCoordinate(minimum + offsetX, minimum + offsetY)] = entity;
                }
            }

            Assert.That(
                CompletedChunkTerrainNeighborhoodFactory.TryCreate(
                    entityManager,
                    completedEntities,
                    new ChunkCoordinate(minimum, minimum),
                    out CompletedChunkTerrainNeighborhood neighborhood),
                Is.True);
            Assert.That(neighborhood.TryGetTile(0, 0, out _), Is.True);
            Assert.That(
                neighborhood.TryGetTile(-1, 0, out _),
                Is.False,
                "intワールド外を隣接土地として返してはいけません。");
        }

    }
}
