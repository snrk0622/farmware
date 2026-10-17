// このファイルは、分類用外周の一括生成が従来のセル単位生成と一致し、再利用配列へ古い値を残さないことを検証します。
// ECS内部の確定地形を優先することと、反映予算が変わっても必要な隣接セルがすべて準備されることを保証します。

using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>EditModeの最小Worldと一時Native配列で、Presenterの外周参照契約を検証します。</summary>
    public sealed class TerrainNeighborHaloUtilityTests
    {
        /// <summary>全136通りの連続反映範囲を準備し、必要な外周の全フィールドが従来生成と一致することを保証します。</summary>
        /// <param name="chunkX">負座標・通常・遠方・int端を含む水平チャンク座標です。</param>
        /// <param name="chunkY">負座標・通常・遠方・int端を含む垂直チャンク座標です。</param>
        [TestCase(8, -1)]
        [TestCase(-3, -2)]
        [TestCase(15625000, -15625000)]
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, WorldGridConstants.MinimumLogicalChunkCoordinate)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, WorldGridConstants.MaximumLogicalChunkCoordinate)]
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, WorldGridConstants.MaximumLogicalChunkCoordinate)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, WorldGridConstants.MinimumLogicalChunkCoordinate)]
        public void Fill_EveryContiguousBatch_MatchesDirectGeneration(int chunkX, int chunkY)
        {
            using World world = new World("TerrainHaloBatchTests");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<GeneratedTile> tiles = world.EntityManager.AddBuffer<GeneratedTile>(entity);
            using var storage = new NativeArray<GeneratedTile>(Presentation.TerrainNeighborHaloUtility.TileCount,
                Allocator.TempJob);
            // using変数はreadonlyなので、書き込み用の借用コピーを作ります。破棄はstorageだけが担当します。
            NativeArray<GeneratedTile> halo = storage;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString("Farmware"), 1);
            ChunkCoordinate coordinate = new ChunkCoordinate(chunkX, chunkY);
            GeneratedTile[] expected = BuildExpectedHalo(settings, coordinate);

            for (int first = 0; first < WorldGridConstants.SubChunksPerChunk; first++)
            {
                for (int count = 1; count <= WorldGridConstants.SubChunksPerChunk - first; count++)
                {
                    // 毎回あり得ないBiome値で汚し、必要なセルが未更新のまま前バッチの値を読む退行を検出します。
                    for (int i = 0; i < halo.Length; i++) halo[i] = new GeneratedTile { Biome = (BiomeId)255 };
                    Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, first, count, halo);
                    for (int index = first; index < first + count; index++)
                    {
                        int startX = index % WorldGridConstants.SubChunksPerAxis * WorldGridConstants.SubChunkSize;
                        int startY = index / WorldGridConstants.SubChunksPerAxis * WorldGridConstants.SubChunkSize;
                        // 対象16×16を囲む18×18のうちチャンク外だけが必要です。斜め接続と資源の直下参照も含みます。
                        for (int y = startY - 1; y <= startY + WorldGridConstants.SubChunkSize; y++)
                        {
                            for (int x = startX - 1; x <= startX + WorldGridConstants.SubChunkSize; x++)
                            {
                                if (x >= 0 && x < 64 && y >= 0 && y < 64) continue;
                                GeneratedTile actual = Presentation.TerrainNeighborHaloUtility.GetTile(x, y, tiles, halo);
                                Assert.That(actual, Is.EqualTo(expected[ExpectedIndex(x, y)]),
                                    "境界・岩壁露出面・資源の前後判定は従来と同じ隣接地形を読む必要があります。");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>同じ配列を別シード・別チャンクへ貸し直し、外周を更新しても内部のECS確定値を優先することを保証します。</summary>
        [Test]
        public void Fill_ReusedStorage_UsesCurrentSettingsAndAuthoritativeCenter()
        {
            using World world = new World("TerrainHaloReuseTests");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<GeneratedTile> tiles = world.EntityManager.AddBuffer<GeneratedTile>(entity);
            tiles.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            using var halo = new NativeArray<GeneratedTile>(Presentation.TerrainNeighborHaloUtility.TileCount, Allocator.TempJob);
            for (int trial = 0; trial < 2; trial++)
            {
                WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault((ulong)(trial + 1), 1);
                ChunkCoordinate coordinate = new ChunkCoordinate(trial * 4, -trial);
                Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, 0, 16, halo);
                GeneratedTile[] expected = BuildExpectedHalo(settings, coordinate);
                for (int y = -1; y <= 64; y++)
                {
                    for (int x = -1; x <= 64; x++)
                    {
                        if (x >= 0 && x < 64 && y >= 0 && y < 64)
                        {
                            // 通常生成とは異なる値を置き、中心を再生成・古い配列コピーへ置き換えないことを検証します。
                            GeneratedTile authoritative = new GeneratedTile { Ground = GroundType.Magma, Biome = (BiomeId)trial };
                            tiles[WorldGrid.ToTileIndex(x, y)] = authoritative;
                            Assert.That(Presentation.TerrainNeighborHaloUtility.GetTile(x, y, tiles, halo), Is.EqualTo(authoritative));
                        }
                        else
                        {
                            Assert.That(Presentation.TerrainNeighborHaloUtility.GetTile(x, y, tiles, halo),
                                Is.EqualTo(expected[ExpectedIndex(x, y)]));
                        }
                    }
                }
            }
        }

        /// <summary>外周配列を不正値で埋め、必要な矩形だけを更新して全260セルの無条件生成へ戻らないことを保証します。</summary>
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
            using var storage = new NativeArray<GeneratedTile>(Presentation.TerrainNeighborHaloUtility.TileCount, Allocator.TempJob);
            // 書き込み用の借用コピーです。storageだけがDisposeします。
            NativeArray<GeneratedTile> halo = storage;
            for (int i = 0; i < halo.Length; i++) halo[i] = new GeneratedTile { Biome = (BiomeId)255 };
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(1, 1);
            Presentation.TerrainNeighborHaloUtility.Fill(settings, new ChunkCoordinate(0, 0), first, count, halo);
            int changed = 0;
            for (int i = 0; i < halo.Length; i++)
            {
                if (halo[i].Biome != (BiomeId)255) changed++;
            }
            Assert.That(changed, Is.EqualTo(expectedCount));
        }

        /// <summary>誤った配列長と反映範囲を入力し、Burst実行前に拒否することを保証します。</summary>
        [Test]
        public void Fill_InvalidArguments_RejectsBeforeJobExecution()
        {
            using var halo = new NativeArray<GeneratedTile>(Presentation.TerrainNeighborHaloUtility.TileCount, Allocator.TempJob);
            using var shortHalo = new NativeArray<GeneratedTile>(1, Allocator.TempJob);
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(1, 1);
            ChunkCoordinate coordinate = new ChunkCoordinate(0, 0);
            Assert.Throws<ArgumentException>(() => Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, 0, 1, shortHalo));
            Assert.Throws<ArgumentOutOfRangeException>(() => Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, -1, 1, halo));
            Assert.Throws<ArgumentOutOfRangeException>(() => Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, 0, 0, halo));
            Assert.Throws<ArgumentOutOfRangeException>(() => Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, 15, 2, halo));
            Assert.Throws<ArgumentOutOfRangeException>(() => Presentation.TerrainNeighborHaloUtility.Fill(settings, coordinate, 16, 1, halo));
        }

        /// <summary>旧経路と同じ1セル生成を行い、int域外だけは表示専用の水域値へ変換して比較用配列を返します。</summary>
        /// <param name="settings">比較する生成設定です。</param>
        /// <param name="coordinate">外周の中心チャンクです。</param>
        /// <returns>上下・左右の順で格納した260セルです。テストだけで確保します。</returns>
        private static GeneratedTile[] BuildExpectedHalo(WorldGenerationSettings settings, ChunkCoordinate coordinate)
        {
            GeneratedTile[] result = new GeneratedTile[Presentation.TerrainNeighborHaloUtility.TileCount];
            for (int y = -1; y <= 64; y++)
            {
                for (int x = -1; x <= 64; x++)
                {
                    if (x >= 0 && x < 64 && y >= 0 && y < 64) continue;
                    result[ExpectedIndex(x, y)] = WorldGrid.TryToWorldTileCoordinate(coordinate.X, coordinate.Y,
                        x, y, out int worldX, out int worldY)
                        ? DeterministicChunkGenerator.GenerateTile(settings, worldX, worldY)
                        : new GeneratedTile { Ground = GroundType.Water };
                }
            }

            return result;
        }

        /// <summary>テストの比較用配列で、上下の行と左右の列を重複なしの位置へ対応させます。</summary>
        /// <param name="x">外周セルの水平ローカル座標です。</param>
        /// <param name="y">外周セルの垂直ローカル座標です。</param>
        /// <returns>260要素のうち対応する位置です。</returns>
        private static int ExpectedIndex(int x, int y)
        {
            if (y == -1) return x + 1;
            if (y == 64) return 66 + x + 1;
            return 132 + (x == -1 ? 0 : 64) + y;
        }
    }
}
