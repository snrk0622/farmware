// このファイルは、Map Viewerの完成自然地形キャッシュが別要求・別設定・別Layerへ状態を混ぜないことを検証します。
// 重い地形生成を繰り返さず、テスト所有の完成配列を用意して値コピー、破棄責任、保持上限を確認します。
using System;
using System.Reflection;
using Farmware.Simulation;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>Window単位キャッシュの設定境界と、Sampleが独立して配列を破棄できる契約を保証します。</summary>
    public sealed class WorldGenerationMapChunkCacheTests
    {
        /// <summary>生成元を破棄し復元値を編集しても、キャッシュが保持する全5配列が変化しないことを保証します。</summary>
        [Test]
        public void Restore_CopiesAllBuffersAndDoesNotShareOwnership()
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var coordinate = new ChunkCoordinate(WorldLayer.Surface, -2, 3);
            var cache = new WorldGenerationMapChunkCache();
            using (HydrologyDiagnosticChunk original = CreateChunk(coordinate))
            {
                var tiles = original.TerrainTiles;
                tiles[0] = new GeneratedTile { Elevation = 123 };
                cache.Remember(settings, original);
            }
            Assert.That(cache.TryRestore(settings, coordinate, out HydrologyDiagnosticChunk first), Is.True);
            using (first)
            {
                Assert.That(first.TerrainTiles[0].Elevation, Is.EqualTo(123));
                Assert.That(first.TerrainCells.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(first.HydrologyCells.Length, Is.EqualTo(DeterministicHydrologyGenerator.CellsPerChunk));
                Assert.That(first.SurfaceWaterTiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(first.HydrologyTiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
                // 復元先だけを編集し、次の要求へ編集結果が漏れないことを確認します。
                var tiles = first.TerrainTiles;
                tiles[0] = new GeneratedTile { Elevation = 456 };
                var cells = first.TerrainCells;
                cells[0] = new GeneratedTerrainCell { Biome = BiomeId.TemperateGrassland };
            }
            Assert.That(cache.TryRestore(settings, coordinate, out HydrologyDiagnosticChunk second), Is.True);
            using (second)
            {
                Assert.That(second.TerrainTiles[0].Elevation, Is.EqualTo(123));
                Assert.That(second.TerrainCells[0].Biome, Is.EqualTo(BiomeId.None));
                Assert.That(second.HydrologyCells[0], Is.EqualTo(default(GeneratedHydrologyCell)));
                Assert.That(second.SurfaceWaterTiles[0], Is.EqualTo(default(GeneratedSurfaceWaterTile)));
                Assert.That(second.HydrologyTiles[0], Is.EqualTo(default(GeneratedHydrologyTile)));
            }
        }

        /// <summary>Seedだけでなく生成版・気候設定の変更でも古い自然地形を拒否することを保証します。</summary>
        /// <param name="changedField">変更する設定の種類です。</param>
        [TestCase("seed")]
        [TestCase("version")]
        [TestCase("seaLevel")]
        public void Restore_RejectsDifferentGenerationSettings(string changedField)
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var cache = new WorldGenerationMapChunkCache();
            var coordinate = new ChunkCoordinate(WorldLayer.Surface, 0, 0);
            using (var original = CreateChunk(coordinate)) cache.Remember(settings, original);
            var changed = settings;
            if (changedField == "seed") changed.WorldSeed++;
            else if (changedField == "version") changed.GeneratorVersion++;
            else changed.SeaLevelThreshold++;
            Assert.That(cache.TryRestore(changed, coordinate, out var restored), Is.False);
            Assert.That(restored, Is.Null);
            Assert.That(cache.Count, Is.Zero);
        }

        /// <summary>同じ水平座標の別レイヤーをSurfaceのコピーとして返さないことを保証します。</summary>
        [Test]
        public void Restore_DoesNotReadAnotherLayer()
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var cache = new WorldGenerationMapChunkCache();
            using (var original = CreateChunk(new ChunkCoordinate(WorldLayer.Surface, -1, 0))) cache.Remember(settings, original);
            Assert.That(cache.TryRestore(settings, new ChunkCoordinate(WorldLayer.Underwater, -1, 0), out var restored), Is.False);
            Assert.That(restored, Is.Null);
            Assert.That(cache.Count, Is.EqualTo(1));
        }

        /// <summary>長距離パンでも保持数が上限を超えず、最古の再生成可能な値だけを除去することを保証します。</summary>
        [Test]
        public void Remember_EvictsOldestChunkAtCapacity()
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var cache = new WorldGenerationMapChunkCache();
            for (int x = 0; x <= WorldGenerationMapChunkCache.MaximumCachedChunks; x++)
            {
                using (var original = CreateChunk(new ChunkCoordinate(WorldLayer.Surface, x, 0))) cache.Remember(settings, original);
            }
            Assert.That(cache.Count, Is.EqualTo(WorldGenerationMapChunkCache.MaximumCachedChunks));
            Assert.That(cache.TryRestore(settings, new ChunkCoordinate(WorldLayer.Surface, 0, 0), out var oldest), Is.False);
            Assert.That(oldest, Is.Null);
            Assert.That(cache.TryRestore(settings, new ChunkCoordinate(WorldLayer.Surface, 1, 0), out var retained), Is.True);
            retained.Dispose();
        }

        /// <summary>上限まで保持した後、今回必要な最古の値が新規生成の追加で失われないことを保証します。</summary>
        [Test]
        public void Preparation_PreservesRequiredOldestChunkWhenAddingMissingChunk()
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var cache = new WorldGenerationMapChunkCache();
            for (int x = 0; x < WorldGenerationMapChunkCache.MaximumCachedChunks; x++)
            {
                using (var original = CreateChunk(new ChunkCoordinate(x, 0))) cache.Remember(settings, original);
            }
            var oldest = new ChunkCoordinate(0, 0);
            var missing = new ChunkCoordinate(WorldGenerationMapChunkCache.MaximumCachedChunks, 0);
            // Windowの準備段階と同じ順序で、必要集合を確定してから未訪問の完成値を追加します。
            typeof(WorldGenerationMapChunkCache).GetMethod("PrioritizeRequired", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(cache, new object[] { settings, new[] { oldest, missing } });
            using (var original = CreateChunk(missing)) cache.Remember(settings, original);
            Assert.That(cache.Count, Is.EqualTo(WorldGenerationMapChunkCache.MaximumCachedChunks));
            Assert.That(cache.TryRestore(settings, oldest, out var retained), Is.True);
            retained.Dispose();
            Assert.That(cache.TryRestore(settings, missing, out var added), Is.True);
            added.Dispose();
            Assert.That(cache.TryRestore(settings, new ChunkCoordinate(1, 0), out var evicted), Is.False);
            Assert.That(evicted, Is.Null);
        }

        /// <summary>重い生成器を実行せず、テストだけが所有する規定長のゼロ初期化済み完成配列を準備します。</summary>
        /// <param name="coordinate">コピー元のレイヤー付き座標です。</param>
        /// <returns>テストがusingで破棄するNative配列所有者です。</returns>
        private static HydrologyDiagnosticChunk CreateChunk(ChunkCoordinate coordinate)
        {
            // 製品APIへ不完全な診断結果の作成口を追加せず、Fixtureだけで所有配列を用意します。
            var chunk = (HydrologyDiagnosticChunk)Activator.CreateInstance(typeof(HydrologyDiagnosticChunk),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { coordinate }, null);
            var tiles = chunk.TerrainTiles;
            var cells = chunk.TerrainCells;
            var hydrology = chunk.HydrologyCells;
            var water = chunk.SurfaceWaterTiles;
            var hydrologyTiles = chunk.HydrologyTiles;
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = default;
                cells[i] = default;
                water[i] = default;
                hydrologyTiles[i] = default;
            }
            for (int i = 0; i < hydrology.Length; i++) hydrology[i] = default;
            return chunk;
        }
    }
}
