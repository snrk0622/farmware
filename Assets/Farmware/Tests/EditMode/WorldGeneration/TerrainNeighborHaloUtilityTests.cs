// このファイルは、分類用外周が中心と周囲8チャンクの完成ECS地形を正しく参照することを検証します。
// 水文・Biome確定前の単一タイル生成へ戻らず、再利用配列へ古い値を残さない表示境界を保証します。

using System;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>EditModeの最小Worldと一時Native配列で、Presenterの完成外周参照契約を検証します。</summary>
    public sealed class TerrainNeighborHaloUtilityTests
    {
        /// <summary>全136通りの連続反映範囲を準備し、必要な外周が9つの完成Bufferと一致することを保証します。</summary>
        [Test]
        public void Fill_EveryContiguousBatch_MatchesCompletedNeighborhood()
        {
            using var neighborhoodStorage = new NeighborhoodStorage();
            using World world = new World("TerrainHaloBatchTests");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<GeneratedTerrainCell> tiles = world.EntityManager.AddBuffer<GeneratedTerrainCell>(entity);
            CopyToBuffer(neighborhoodStorage.Center, tiles);
            using var storage = new NativeArray<GeneratedTerrainCell>(
                TerrainNeighborHaloUtility.TileCount,
                Allocator.TempJob);
            // using変数はreadonlyなので、書き込み用の借用コピーを作ります。破棄はstorageだけが担当します。
            NativeArray<GeneratedTerrainCell> halo = storage;

            for (int first = 0; first < WorldGridConstants.SubChunksPerChunk; first++)
            {
                for (int count = 1; count <= WorldGridConstants.SubChunksPerChunk - first; count++)
                {
                    // 毎回あり得ないBiome値で汚し、必要なセルが未更新のまま前バッチの値を読む退行を検出します。
                    for (int index = 0; index < halo.Length; index++)
                    {
                        halo[index] = new GeneratedTerrainCell { Biome = (BiomeId)255 };
                    }

                    TerrainNeighborHaloUtility.Fill(
                        neighborhoodStorage.Neighborhood,
                        first,
                        count,
                        halo);
                    for (int subChunkIndex = first;
                         subChunkIndex < first + count;
                         subChunkIndex++)
                    {
                        int startX = subChunkIndex % WorldGridConstants.SubChunksPerAxis
                            * WorldGridConstants.SubChunkSize;
                        int startY = subChunkIndex / WorldGridConstants.SubChunksPerAxis
                            * WorldGridConstants.SubChunkSize;
                        // 対象16×16を囲む18×18のうちチャンク外だけが必要です。斜め接続と資源の直下参照も含みます。
                        for (int y = startY - 1;
                             y <= startY + WorldGridConstants.SubChunkSize;
                             y++)
                        {
                            for (int x = startX - 1;
                                 x <= startX + WorldGridConstants.SubChunkSize;
                                 x++)
                            {
                                if (x >= 0 && x < 64 && y >= 0 && y < 64)
                                {
                                    continue;
                                }

                                Assert.That(
                                    neighborhoodStorage.Neighborhood.TryGetTile(
                                        x,
                                        y,
                                        out GeneratedTerrainCell expected),
                                    Is.True);
                                GeneratedTerrainCell actual = TerrainNeighborHaloUtility.GetTile(
                                    x,
                                    y,
                                    tiles,
                                    halo);
                                Assert.That(
                                    actual,
                                    Is.EqualTo(expected),
                                    "外周は水文・Biome確定後の隣接チャンクBufferをそのまま読む必要があります。");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>同じ配列を別の完成近傍へ貸し直し、外周更新後も内部は現在のECS Bufferを優先することを保証します。</summary>
        [Test]
        public void Fill_ReusedStorage_UsesCurrentCompletedNeighborhoodAndAuthoritativeCenter()
        {
            using World world = new World("TerrainHaloReuseTests");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<GeneratedTerrainCell> tiles = world.EntityManager.AddBuffer<GeneratedTerrainCell>(entity);
            tiles.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            using var halo = new NativeArray<GeneratedTerrainCell>(
                TerrainNeighborHaloUtility.TileCount,
                Allocator.TempJob);

            for (int trial = 0; trial < 2; trial++)
            {
                using var neighborhoodStorage = new NeighborhoodStorage(trial * 32);
                TerrainNeighborHaloUtility.Fill(
                    neighborhoodStorage.Neighborhood,
                    0,
                    WorldGridConstants.SubChunksPerChunk,
                    halo);
                for (int y = -1; y <= 64; y++)
                {
                    for (int x = -1; x <= 64; x++)
                    {
                        if (x >= 0 && x < 64 && y >= 0 && y < 64)
                        {
                            // 近傍の中心配列と異なる値を置き、GetTileが現在のECS Bufferを正として維持することを検証します。
                            GeneratedTerrainCell authoritative = new GeneratedTerrainCell
                            {
                                LiquidRuntimeId = new DefinitionRuntimeId(5),
                                Biome = (BiomeId)(trial + 1)
                            };
                            tiles[WorldGrid.ToTileIndex(x, y)] = authoritative;
                            Assert.That(
                                TerrainNeighborHaloUtility.GetTile(x, y, tiles, halo),
                                Is.EqualTo(authoritative));
                        }
                        else
                        {
                            Assert.That(
                                neighborhoodStorage.Neighborhood.TryGetTile(
                                    x,
                                    y,
                                    out GeneratedTerrainCell expected),
                                Is.True);
                            Assert.That(
                                TerrainNeighborHaloUtility.GetTile(x, y, tiles, halo),
                                Is.EqualTo(expected));
                        }
                    }
                }
            }
        }

        /// <summary>外周配列を不正値で埋め、必要な矩形だけを更新して全260セルの無条件複写へ戻らないことを保証します。</summary>
        /// <param name="first">先頭サブチャンクです。</param>
        /// <param name="count">連続する反映対象数です。</param>
        /// <param name="expectedCount">重複なしで準備される外周セル数です。</param>
        [TestCase(0, 4, 100)]
        [TestCase(4, 4, 36)]
        [TestCase(8, 4, 36)]
        [TestCase(12, 4, 100)]
        [TestCase(0, 16, 260)]
        [TestCase(5, 1, 0)]
        [TestCase(0, 1, 35)]
        public void Fill_PreparesOnlyBatchRectangle(int first, int count, int expectedCount)
        {
            using var neighborhoodStorage = new NeighborhoodStorage();
            using var storage = new NativeArray<GeneratedTerrainCell>(
                TerrainNeighborHaloUtility.TileCount,
                Allocator.TempJob);
            // 書き込み用の借用コピーです。storageだけがDisposeします。
            NativeArray<GeneratedTerrainCell> halo = storage;
            for (int index = 0; index < halo.Length; index++)
            {
                halo[index] = new GeneratedTerrainCell { Biome = (BiomeId)255 };
            }

            TerrainNeighborHaloUtility.Fill(
                neighborhoodStorage.Neighborhood,
                first,
                count,
                halo);
            int changed = 0;
            for (int index = 0; index < halo.Length; index++)
            {
                if (halo[index].Biome != (BiomeId)255)
                {
                    changed++;
                }
            }

            Assert.That(changed, Is.EqualTo(expectedCount));
        }

        /// <summary>誤った配列長と反映範囲を入力し、Burst実行前に拒否することを保証します。</summary>
        [Test]
        public void Fill_InvalidArguments_RejectsBeforeJobExecution()
        {
            using var neighborhoodStorage = new NeighborhoodStorage();
            using var halo = new NativeArray<GeneratedTerrainCell>(
                TerrainNeighborHaloUtility.TileCount,
                Allocator.TempJob);
            using var shortHalo = new NativeArray<GeneratedTerrainCell>(1, Allocator.TempJob);
            Assert.Throws<ArgumentException>(() => TerrainNeighborHaloUtility.Fill(
                neighborhoodStorage.Neighborhood, 0, 1, shortHalo));
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainNeighborHaloUtility.Fill(
                neighborhoodStorage.Neighborhood, -1, 1, halo));
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainNeighborHaloUtility.Fill(
                neighborhoodStorage.Neighborhood, 0, 0, halo));
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainNeighborHaloUtility.Fill(
                neighborhoodStorage.Neighborhood, 15, 2, halo));
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainNeighborHaloUtility.Fill(
                neighborhoodStorage.Neighborhood, 16, 1, halo));
        }

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

        /// <summary>NativeArrayの完成中心地形をDynamicBufferへコピーし、GetTileの中心優先を検証できる状態にします。</summary>
        /// <param name="source">コピー元の4096完成セルです。</param>
        /// <param name="destination">テストWorldが所有するコピー先Bufferです。</param>
        private static void CopyToBuffer(
            NativeArray<GeneratedTerrainCell> source,
            DynamicBuffer<GeneratedTerrainCell> destination)
        {
            destination.ResizeUninitialized(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        /// <summary>9つの独立NativeArrayを所有し、完成地形近傍をテスト終了時に一括破棄します。</summary>
        private sealed class NeighborhoodStorage : IDisposable
        {
            // 左下チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> southWest;

            // 下チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> south;

            // 右下チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> southEast;

            // 左チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> west;

            // 中心チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> center;

            // 右チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> east;

            // 左上チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> northWest;

            // 上チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> north;

            // 右上チャンクの4096完成セルを保持し、このヘルパーが破棄します。
            private NativeArray<GeneratedTerrainCell> northEast;

            /// <summary>中心チャンクの完成配列です。DynamicBufferへの比較用コピーだけに使います。</summary>
            public NativeArray<GeneratedTerrainCell> Center => center;

            /// <summary>9配列を同期処理中だけ参照する完成地形近傍です。</summary>
            public CompletedChunkTerrainNeighborhood Neighborhood { get; }

            /// <summary>各チャンクを識別できる完成値で9配列を確保・初期化します。</summary>
            /// <param name="valueOffset">再利用試験で前回値と区別するBiome値への加算です。</param>
            public NeighborhoodStorage(int valueOffset = 0)
            {
                southWest = CreateChunk(0 + valueOffset);
                south = CreateChunk(1 + valueOffset);
                southEast = CreateChunk(2 + valueOffset);
                west = CreateChunk(3 + valueOffset);
                center = CreateChunk(4 + valueOffset);
                east = CreateChunk(5 + valueOffset);
                northWest = CreateChunk(6 + valueOffset);
                north = CreateChunk(7 + valueOffset);
                northEast = CreateChunk(8 + valueOffset);
                Neighborhood = new CompletedChunkTerrainNeighborhood(
                    southWest,
                    south,
                    southEast,
                    west,
                    center,
                    east,
                    northWest,
                    north,
                    northEast);
            }

            /// <summary>所有する9配列を、作成済みのものだけ確実に破棄します。</summary>
            public void Dispose()
            {
                if (southWest.IsCreated) southWest.Dispose();
                if (south.IsCreated) south.Dispose();
                if (southEast.IsCreated) southEast.Dispose();
                if (west.IsCreated) west.Dispose();
                if (center.IsCreated) center.Dispose();
                if (east.IsCreated) east.Dispose();
                if (northWest.IsCreated) northWest.Dispose();
                if (north.IsCreated) north.Dispose();
                if (northEast.IsCreated) northEast.Dispose();
            }

            /// <summary>完成後の水域・マグマを含む、チャンクごとに識別可能な4096セルを作ります。</summary>
            /// <param name="chunkValue">9チャンクと再利用試行を区別する固定値です。</param>
            /// <returns>呼び出し側が所有・破棄する完成地形配列です。</returns>
            private static NativeArray<GeneratedTerrainCell> CreateChunk(int chunkValue)
            {
                var tiles = new NativeArray<GeneratedTerrainCell>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.TempJob);
                for (int index = 0; index < tiles.Length; index++)
                {
                    // BiomeとRuntime IDも変え、区画の有無だけ一致する誤ったコピーではテストを通過できないようにします。
                    tiles[index] = new GeneratedTerrainCell
                    {
                        Biome = (BiomeId)(chunkValue + 1),
                        GroundBlockRuntimeId = chunkValue % 3 == 2
                            ? new DefinitionRuntimeId(100 + chunkValue + (index % 7))
                            : default,
                        WallBlockRuntimeId = chunkValue % 3 == 2
                            ? new DefinitionRuntimeId(200 + chunkValue + (index % 7))
                            : default,
                        LiquidRuntimeId = chunkValue % 3 != 2
                            ? new DefinitionRuntimeId(300 + chunkValue + (index % 7))
                            : default
                    };
                }

                return tiles;
            }
        }
    }
}
