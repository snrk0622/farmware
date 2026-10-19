// このファイルは、動物の開発者表示が可視位置だけを保持し、画面外の不変Chunkを繰り返し走査しないことを検証します。
// Sceneを使わず一時Worldを作り、位置の変更版・構造変更・負座標と1,000〜100,000個体の読取件数を確認します。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>可視位置キャッシュの正確性と、画面外個体数に対する走査範囲を検証するEditModeテストです。</summary>
    public sealed class AnimalPositionViewportCacheTests
    {
        /// <summary>画面外個体が増えても、初回以外は範囲更新0件・可視Chunk内4件だけを読み、境界と負座標を含む3タイルを保持します。</summary>
        /// <param name="farCount">画面外に用意する個体数です。件数比較であり時間の合格値は設けません。</param>
        [TestCase(1000)]
        [TestCase(10000)]
        [TestCase(100000)]
        public void VisibleCache_SkipsUnchangedOffscreenChunks(int farCount)
        {
            using World world = new World("Animal visible position cache test");
            PrepareAnimals(world.EntityManager, farCount);
            using EntityQuery query = CreateQuery(world.EntityManager);
            var cache = new AnimalPositionViewportCache();
            cache.Update(world.EntityManager, query, -1, -1, 1, 1);
            Assert.That(cache.BoundsPositionReadCount, Is.EqualTo(farCount + 4));
            cache.Update(world.EntityManager, query, -1, -1, 1, 1);
            Assert.That(cache.BoundsPositionReadCount, Is.Zero);
            Assert.That(cache.VisiblePositionReadCount, Is.EqualTo(4));
            Assert.That(cache.Count, Is.EqualTo(3));
            Assert.That(cache.Contains(-1, -1), Is.True);
            Assert.That(cache.Contains(1, 1), Is.True);
            Assert.That(cache.Contains(2, 2), Is.False);

            // Cameraを画面外個体の場所へ動かしても、不変Chunkの範囲を再構築せず正しい位置だけへ切り替わります。
            cache.Update(world.EntityManager, query, 10000, 10000, 10000, 10000);
            Assert.That(cache.BoundsPositionReadCount, Is.Zero);
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.Contains(10000, 10000), Is.True);
            Assert.That(cache.Contains(0, 0), Is.False);
        }

        /// <summary>Simulation Systemの位置書込で画面外から入る個体と、Entity破棄による退出が次の表示へ反映されることを保証します。</summary>
        [Test]
        public void VisibleCache_RefreshesChangedPositionsAndRemovesDestroyedChunks()
        {
            using World world = new World("Animal position cache mutation test");
            EntityManager manager = world.EntityManager;
            Entity entity = manager.CreateEntity(typeof(WildAnimal), typeof(AnimalTilePosition));
            manager.SetComponentData(
                entity,
                new AnimalTilePosition
                {
                    Layer = WorldLayer.Surface,
                    TileX = 10000,
                    TileY = 10000
                });
            using EntityQuery query = CreateQuery(manager);
            var cache = new AnimalPositionViewportCache();
            cache.Update(manager, query, -1, -1, 1, 1);
            Assert.That(cache.Count, Is.Zero);
            var writer = world.GetOrCreateSystemManaged<PositionWriter>();
            writer.Target = entity;
            writer.Position = new AnimalTilePosition
            {
                Layer = WorldLayer.Surface,
                TileX = -1,
                TileY = 1
            };
            writer.Update();
            cache.Update(manager, query, -1, -1, 1, 1);
            Assert.That(cache.BoundsPositionReadCount, Is.EqualTo(1));
            Assert.That(cache.Contains(-1, 1), Is.True);

            manager.DestroyEntity(entity);
            cache.Update(manager, query, -1, -1, 1, 1);
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.TrackedChunkCount, Is.Zero);
            cache.Clear();
            Assert.That(cache.VisiblePositionReadCount, Is.Zero);
        }

        /// <summary>
        /// 同じX/Yへ地上と海中の個体を準備し、選択中レイヤーの個体だけを可視キャッシュへ含めることを保証します。
        /// </summary>
        [Test]
        public void VisibleCache_SameXyDifferentLayers_SelectsRequestedLayerOnly()
        {
            using World world = new World("Animal layer-aware position cache test");
            EntityManager manager = world.EntityManager;
            Entity surfaceAnimal = manager.CreateEntity(typeof(WildAnimal), typeof(AnimalTilePosition));
            manager.SetComponentData(
                surfaceAnimal,
                new AnimalTilePosition
                {
                    Layer = WorldLayer.Surface,
                    TileX = 4,
                    TileY = -2
                });
            Entity underwaterAnimal = manager.CreateEntity(typeof(WildAnimal), typeof(AnimalTilePosition));
            manager.SetComponentData(
                underwaterAnimal,
                new AnimalTilePosition
                {
                    Layer = WorldLayer.Underwater,
                    TileX = 4,
                    TileY = -2
                });

            using EntityQuery query = CreateQuery(manager);
            var cache = new AnimalPositionViewportCache();
            cache.Update(manager, query, WorldLayer.Surface, 4, -2, 4, -2);
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.Contains(WorldLayer.Surface, 4, -2), Is.True);
            Assert.That(cache.Contains(WorldLayer.Underwater, 4, -2), Is.False);

            cache.Update(manager, query, WorldLayer.Underwater, 4, -2, 4, -2);
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.Contains(WorldLayer.Surface, 4, -2), Is.False);
            Assert.That(cache.Contains(WorldLayer.Underwater, 4, -2), Is.True);
        }

        /// <summary>明示実行する局所比較です。同じWorldで旧全件配列化と新キャッシュを3回暖機後24回測り、CPU・managed allocationを記録します。</summary>
        [Test, Explicit("開発者表示の変更時だけ実行する1k/10k/100k局所比較です。製品性能の合格判定には使いません。")]
        public void VisibleCache_ReportsScalingComparison()
        {
            foreach (int farCount in new[] { 1000, 10000, 100000 })
            {
                using World world = new World("Animal viewport cache benchmark");
                EntityManager manager = world.EntityManager;
                PrepareAnimals(manager, farCount);
                using EntityQuery query = CreateQuery(manager);
                var cache = new AnimalPositionViewportCache();
                var old = new HashSet<long>();
                // 同じ個体データ・同じ矩形を使い、World準備・コンテナ拡張・Console出力は測定外にします。
                for (int warmup = 0; warmup < 3; warmup++)
                {
                    ReadAllPositions(query, old);
                    cache.Update(manager, query, -1, -1, 1, 1);
                }
                double oldMs = 0, newMs = 0;
                long oldAllocated = 0, newAllocated = 0;
                var stopwatch = new Stopwatch();
                for (int sample = 0; sample < 24; sample++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    stopwatch.Restart(); ReadAllPositions(query, old); stopwatch.Stop();
                    oldMs += stopwatch.Elapsed.TotalMilliseconds;
                    oldAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
                    before = GC.GetAllocatedBytesForCurrentThread();
                    stopwatch.Restart(); cache.Update(manager, query, -1, -1, 1, 1); stopwatch.Stop();
                    newMs += stopwatch.Elapsed.TotalMilliseconds;
                    newAllocated += GC.GetAllocatedBytesForCurrentThread() - before;
                }
                // CLIからも取得できるようConsoleへ記録します。文字列化とログ出力は全サンプルの測定後だけに行います。
                UnityEngine.Debug.Log($"ANIMAL_VIEWPORT far={farCount}, chunks={cache.TrackedChunkCount}, oldMeanMs={oldMs / 24:F6}, newMeanMs={newMs / 24:F6}, oldManagedBytes={oldAllocated / 24}, newManagedBytes={newAllocated / 24}, boundsReads={cache.BoundsPositionReadCount}, visibleReads={cache.VisiblePositionReadCount}, cachedTiles={cache.Count}");
                Assert.That(cache.Count, Is.EqualTo(3));
            }
        }

        /// <summary>旧Repaintと同じ全件コピー・全件HashSet投入を比較用に行います。Native配列はこの呼出しで破棄します。</summary>
        /// <param name="query">全野生個体のQueryです。</param>
        /// <param name="positions">旧処理と同じく容量を再利用する位置集合です。</param>
        private static void ReadAllPositions(EntityQuery query, HashSet<long> positions)
        {
            positions.Clear();
            using NativeArray<AnimalTilePosition> all = query.ToComponentDataArray<AnimalTilePosition>(Allocator.Temp);
            foreach (AnimalTilePosition position in all) positions.Add(((long)position.TileX << 32) | (uint)position.TileY);
        }

        /// <summary>画面外のまとまったChunkと、同じChunk内に可視・不可視が混在する4個体を準備します。</summary>
        /// <param name="manager">テストWorldです。</param>
        /// <param name="farCount">遠方個体の件数です。作成・全初期値設定は計測区間外で行います。</param>
        private static void PrepareAnimals(EntityManager manager, int farCount)
        {
            EntityArchetype farType = manager.CreateArchetype(typeof(WildAnimal), typeof(AnimalTilePosition), typeof(FarGroup));
            using NativeArray<Entity> far = new NativeArray<Entity>(farCount, Allocator.Temp);
            manager.CreateEntity(farType, far);
            for (int i = 0; i < far.Length; i++) manager.SetComponentData(far[i],
                new AnimalTilePosition
                {
                    Layer = WorldLayer.Surface,
                    TileX = 10000 + i % 256,
                    TileY = 10000 + i / 256
                });
            EntityArchetype nearType = manager.CreateArchetype(typeof(WildAnimal), typeof(AnimalTilePosition));
            using NativeArray<Entity> near = new NativeArray<Entity>(4, Allocator.Temp);
            manager.CreateEntity(nearType, near);
            for (int i = 0; i < near.Length; i++) manager.SetComponentData(near[i],
                new AnimalTilePosition
                {
                    Layer = WorldLayer.Surface,
                    TileX = i - 1,
                    TileY = i - 1
                });
        }

        /// <summary>テストのOverlayと同じ、Enableable条件を含まない読取専用Queryを作ります。</summary>
        /// <param name="manager">Queryの所有Worldです。</param>
        /// <returns>呼出し側がDisposeするQueryです。</returns>
        private static EntityQuery CreateQuery(EntityManager manager) => manager.CreateEntityQuery(
            ComponentType.ReadOnly<WildAnimal>(), ComponentType.ReadOnly<AnimalTilePosition>());

        /// <summary>遠方個体を別ECS Chunkへ配置するためのテスト専用タグです。ゲームのComponentへは追加しません。</summary>
        private struct FarGroup : IComponentData { }

        /// <summary>実物のSystem更新で変更版を進め、将来の固定Tick移動と同じ位置書込を再現するテスト専用Systemです。</summary>
        [DisableAutoCreation]
        public partial class PositionWriter : SystemBase
        {
            /// <summary>次のUpdateで移動するテスト個体です。テストが設定します。</summary>
            public Entity Target;
            /// <summary>次のUpdateで書き込む絶対タイル位置です。テストが設定します。</summary>
            public AnimalTilePosition Position;
            /// <summary>テストが明示Updateした時だけ位置を書き、ECSの位置変更版を更新します。</summary>
            protected override void OnUpdate() => EntityManager.SetComponentData(Target, Position);
        }
    }
}
