// このファイルは、Map Viewer専用Worldの製品Job生成が同期診断と同じ全5配列を返し、ゲームWorldへ影響しないことを検証します。
// 非同期処理をUnityTestのフレーム待ちで観測し、取消後のWorldとNative配列の所有者も確認します。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Farmware.Simulation;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>Map Viewerの未訪問チャンク生成、取消、事前完成契約を保証します。</summary>
    public sealed class WorldGenerationMapChunkGeneratorTests
    {
        /// <summary>専用Worldで生成した全5配列が、既存の同期診断結果と一致し、Default Worldを置き換えないことを保証します。</summary>
        [UnityTest]
        public IEnumerator PrepareAsync_MatchesAllSynchronousDiagnosticBuffers()
        {
            var settings = WorldGenerationSettings.CreateDefault(0x3EE49B3100677454UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var coordinate = new ChunkCoordinate(WorldLayer.Surface, 0, 0);
            World previousDefault = World.DefaultGameObjectInjectionWorld;
            int previousWorldCount = World.All.Count;
            Assert.That(HydrologyDiagnosticTerrainCatalogs.TryCreate(out var catalogs), Is.True);
            using (catalogs)
            {
                var cache = new WorldGenerationMapChunkCache();
                var task = WorldGenerationMapChunkGenerator.PrepareAsync(settings, catalogs,
                    new[] { coordinate }, cache, CancellationToken.None);
                while (!task.IsCompleted) yield return null;
                task.GetAwaiter().GetResult();
                Assert.That(World.DefaultGameObjectInjectionWorld, Is.SameAs(previousDefault));
                Assert.That(World.All.Count, Is.EqualTo(previousWorldCount));
                Assert.That(cache.TryRestore(settings, coordinate, out var actual), Is.True);
                Assert.That(HydrologyDiagnosticChunk.TryGenerate(settings, BiomeDefinitionCatalog.CreateDefault(), coordinate,
                    catalogs, out var expected, out var stage), Is.True, stage.ToString());
                using (actual)
                using (expected)
                {
                    // 一部の代表色だけでなく、全4096セルと64水文セルを比較して生成順序と分類の同一性を保証します。
                    CollectionAssert.AreEqual(expected.TerrainTiles.ToArray(), actual.TerrainTiles.ToArray());
                    CollectionAssert.AreEqual(expected.TerrainCells.ToArray(), actual.TerrainCells.ToArray());
                    CollectionAssert.AreEqual(expected.HydrologyCells.ToArray(), actual.HydrologyCells.ToArray());
                    CollectionAssert.AreEqual(expected.SurfaceWaterTiles.ToArray(), actual.SurfaceWaterTiles.ToArray());
                    CollectionAssert.AreEqual(expected.HydrologyTiles.ToArray(), actual.HydrologyTiles.ToArray());
                }
                WorldGenerationMapSampleResult sampled = WorldGenerationMapSampler.Sample(settings, catalogs,
                    WorldLayer.Surface, 32, 32, 4, 4, 1, WorldGenerationMapViewMode.Terrain,
                    CancellationToken.None, completedChunkReuseCache: cache, requirePreparedCompletedChunks: true);
                Assert.That(sampled.HasGeneratedData, Is.True);
                Assert.That(sampled.DiagnosticChunkCount, Is.EqualTo(1));
            }
        }

        /// <summary>生成開始直後の取消で次のチャンクを投入せず、実行中のJobと専用Worldを破棄してから終了することを保証します。</summary>
        [UnityTest]
        public IEnumerator PrepareAsync_CancellationReleasesWorldWithoutPublishingCanceledResults()
        {
            var settings = WorldGenerationSettings.CreateDefault(0x3EE49B3100677454UL, WorldGenerationSettings.CurrentGeneratorVersion);
            int previousWorldCount = World.All.Count;
            Assert.That(HydrologyDiagnosticTerrainCatalogs.TryCreate(out var catalogs), Is.True);
            using (catalogs)
            using (var cancellation = new CancellationTokenSource())
            {
                var cache = new WorldGenerationMapChunkCache();
                var task = WorldGenerationMapChunkGenerator.PrepareAsync(settings, catalogs,
                    new[] { new ChunkCoordinate(0, 0), new ChunkCoordinate(1, 0) }, cache, cancellation.Token);
                cancellation.Cancel();
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(cache.Count, Is.Zero);
                Assert.That(World.All.Count, Is.EqualTo(previousWorldCount));
            }
        }

        /// <summary>キャッシュへ保持できない巨大な要求を、Worldや作業領域の生成前に拒否することを保証します。</summary>
        [Test]
        public void PrepareAsync_RejectsRequestBeyondCacheCapacity()
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            var coordinates = new ChunkCoordinate[WorldGenerationMapChunkCache.MaximumCachedChunks + 1];
            int before = World.All.Count;
            var task = WorldGenerationMapChunkGenerator.PrepareAsync(settings, null, coordinates,
                new WorldGenerationMapChunkCache(), CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => task.GetAwaiter().GetResult());
            Assert.That(World.All.Count, Is.EqualTo(before));
        }

        /// <summary>製品Window経路で事前生成が欠けた場合、旧同期生成へ黙って戻らず明示的に失敗することを保証します。</summary>
        [Test]
        public void Sample_PreparedModeRejectsMissingChunk()
        {
            var settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);
            Assert.That(HydrologyDiagnosticTerrainCatalogs.TryCreate(out var catalogs), Is.True);
            using (catalogs)
            {
                Assert.Throws<InvalidOperationException>(() => WorldGenerationMapSampler.Sample(settings, catalogs,
                    WorldLayer.Surface, 0, 0, 1, 1, 1, WorldGenerationMapViewMode.Terrain,
                    CancellationToken.None, completedChunkReuseCache: new WorldGenerationMapChunkCache(),
                    requirePreparedCompletedChunks: true));
            }
        }
    }
}
