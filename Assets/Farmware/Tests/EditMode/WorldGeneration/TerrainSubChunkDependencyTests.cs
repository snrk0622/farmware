// このファイルは、部分表示が未完成地形を代用せず、必要入力が揃ったサブチャンクだけを既存の反映経路へ渡すことを検証します。
// 最小ECS Worldと一時Tilemapを所有し、近傍到着、予算、待機中のQueue、部分影の完成版との一致を確認します。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>表示入力の依存Maskと、実Presenterの段階的な反映を検証するEditMode Fixtureです。</summary>
    public sealed class TerrainSubChunkDependencyTests
    {
        /// <summary>Fixtureが所有する最小Worldです。Presenterより後に破棄します。</summary>
        private World world;
        /// <summary>変更差分とBlock分類を解決するテスト所有カタログです。</summary>
        private TerrainGenerationTestCatalogSet catalogs;
        /// <summary>FixtureだけのGridです。子のTilemapとPresenterも所有します。</summary>
        private GameObject grid;
        /// <summary>確認対象の実Presenterです。Scene Assetは使用しません。</summary>
        private ChunkTilemapPresenter presenter;
        /// <summary>実Presenterが同期参照する完成Entityの索引です。</summary>
        private Dictionary<ChunkCoordinate, Entity> completed;

        /// <summary>各テスト前に所有World・カタログ・Tilemapを作り、実反映経路へ接続します。</summary>
        [SetUp]
        public void SetUp()
        {
            world = new World("Partial Terrain Presentation Test");
            catalogs = TerrainGenerationTestCatalogSet.Create();
            grid = new GameObject("Partial Terrain Grid", typeof(Grid));
            var layer = new GameObject("Partial Terrain", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(grid.transform, false);
            presenter = layer.AddComponent<ChunkTilemapPresenter>();
            Field("targetTilemap").SetValue(presenter, layer.GetComponent<Tilemap>());
            // EditModeではAwakeが自動実行されない場合だけ初期化し、Native配列を二重確保しません。
            if (Field("subChunkTileBuffer").GetValue(presenter) == null) Invoke("Awake");
            Field("presentationWorld").SetValue(presenter, world);
            Field("terrainCatalogBlob").SetValue(presenter, catalogs.TerrainCatalog);
            Field("terrainRuntimeIds").SetValue(presenter, catalogs.RuntimeIds);
            completed = (Dictionary<ChunkCoordinate, Entity>)Field("completedChunkEntities").GetValue(presenter);
            // 実製品と同じ41材質・液体カタログとRuntime ID検索表を使い、旧Biome代用表示を検証へ持ち込みません。
            var product = AssetDatabase.LoadAssetAtPath<TerrainMaterialPresentationCatalog>(
                "Assets/Farmware/Art/WorldGeneration/Terrain/Published/TerrainMaterialPresentationCatalog.asset");
            Assert.That(product, Is.Not.Null);
            Assert.That(product.TryInitialize(out string productError), Is.True, productError);
            Field("materialPresentationCatalog").SetValue(presenter, product);
            Assert.That(TerrainBlockMaterialCatalogBuilder.TryBuild(catalogs.DefinitionCatalog,
                TerrainBlockMaterialDefinitions.CreateDefault(), out var materials), Is.True);
            try
            {
                Assert.That(TerrainMaterialPresentationRuntimeLookup.TryCreate(materials, out var lookup, out string error), Is.True, error);
                Field("materialRuntimeLookup").SetValue(presenter, lookup);
            }
            finally { materials.Dispose(); }
            Field("materialTileCache").SetValue(presenter, Activator.CreateInstance(Field("materialTileCache").FieldType, true));
            Field("liquidTileCache").SetValue(presenter, new TerrainLiquidRuntimeTileCache());
            Field("liquidPresentationCatalog").SetValue(presenter, AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(
                "Assets/Farmware/Art/WorldGeneration/Liquids/Published/TerrainLiquidPresentationCatalog.asset"));

        }

        /// <summary>各テスト後にUnity Object、World、Blobを所有順に解放します。</summary>
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(grid);
            world.Dispose();
            catalogs.Dispose();
        }

        /// <summary>中心だけなら内側4サブチャンクだけを許可し、未完成外周を補完しないことを保証します。</summary>
        [Test]
        public void ReadyMask_CenterOnlyAllowsFourInteriorSubChunks()
        {
            Assert.That(TerrainSubChunkDependencyUtility.GetReadySubChunkMask(new ChunkCoordinate(-3, 7), 1 << 4), Is.EqualTo(0x0660));
            Assert.That(TerrainSubChunkDependencyUtility.GetReadySubChunkMask(new ChunkCoordinate(0, 0), 0x1ff), Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>東隣だけが追加された場合、東外周の中央2部分だけが追加で準備可能になることを保証します。</summary>
        [Test]
        public void ReadyMask_OnlyWaitsForReferencedSide()
        {
            ushort available = (1 << 4) | (1 << 5);
            ushort ready = TerrainSubChunkDependencyUtility.GetReadySubChunkMask(new ChunkCoordinate(0, 0), available);
            Assert.That(ready, Is.EqualTo(0x0ee0));
        }

        /// <summary>整数ワールド端では存在しない外側チャンクの生成を要求しないことを保証します。</summary>
        [Test]
        public void RequiredMask_WorldCornerExcludesOutsideDomain()
        {
            int minimum = WorldGridConstants.MinimumLogicalChunkCoordinate;
            Assert.That(TerrainSubChunkDependencyUtility.GetRequiredChunkMask(new ChunkCoordinate(minimum, minimum), 0), Is.EqualTo(1 << 4));
        }

        /// <summary>不正サブチャンク番号を別の位置へ丸めないことを保証します。</summary>
        /// <param name="index">範囲外の番号です。</param>
        [TestCase(-1)]
        [TestCase(16)]
        public void RequiredMask_InvalidIndexIsRejected(int index)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainSubChunkDependencyUtility.GetRequiredChunkMask(new ChunkCoordinate(0, 0), index));
        }

        /// <summary>部分Factoryは未完成位置を読取不可のまま返し、従来の全近傍Factoryは全件完成を要求し続けることを保証します。</summary>
        [Test]
        public void AvailableNeighborhood_NeverFabricatesMissingTerrain()
        {
            AddChunk(0, 0);
            var deltas = new Dictionary<ChunkCoordinate, Entity>();
            Assert.That(CompletedChunkTerrainNeighborhoodFactory.TryCreateEffectiveAvailable(world.EntityManager, completed,
                deltas, new ChunkCoordinate(0, 0), catalogs.TerrainCatalog, out var partial), Is.True);
            Assert.That(partial.AvailableChunkMask, Is.EqualTo(1 << 4));
            Assert.That(partial.TryGetTile(-1, 16, out _), Is.False);
            Assert.That(partial.TryGetTile(16, 16, out _), Is.True);
            Assert.That(CompletedChunkTerrainNeighborhoodFactory.TryCreateEffective(world.EntityManager, completed,
                deltas, new ChunkCoordinate(0, 0), catalogs.TerrainCatalog, out _), Is.False);
        }

        /// <summary>外周到着で内側の反映済み進捗を捨てず、残り12部分だけを既定予算で描くことを保証します。</summary>
        [Test]
        public void ApplyQueue_InteriorAppearsBeforeNeighborsAndIsRetainedOnArrival()
        {
            var center = new ChunkCoordinate(0, 0);
            Entity entity = AddChunk(0, 0);
            Schedule(center, entity);
            Assert.That(Invoke("ProcessApplyQueue", 4), Is.EqualTo(0));
            object progress = ((IDictionary)Field("applyProgressByCoordinate").GetValue(presenter))[center];
            Assert.That(progress.GetType().GetField("AppliedSubChunkMask").GetValue(progress), Is.EqualTo((ushort)0x0660));
            var shown = (HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter);
            Assert.That(shown.Contains(center), Is.False);
            bool interiorWasWritten = false;
            foreach (Tilemap map in grid.GetComponentsInChildren<Tilemap>())
            {
                interiorWasWritten |= map.HasTile(new Vector3Int(16, 16, 0));
                Assert.That(map.GetTile(Vector3Int.zero), Is.Null,
                    "未完成近傍が必要な外周へ仮のTileを書いてはいけません。");
            }
            Assert.That(interiorWasWritten, Is.True, "進捗Maskだけでなく、実際のTilemapへ内側を先行反映する必要があります。");
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) if (x != 0 || y != 0) AddChunk(x, y);
            Schedule(center, entity);
            Assert.That(((IDictionary)Field("applyProgressByCoordinate").GetValue(presenter))[center], Is.SameAs(progress));
            Assert.That(Invoke("ProcessApplyQueue", 16), Is.EqualTo(4));
            Assert.That(shown.Contains(center), Is.True);
        }

        /// <summary>入力待ちの先頭が予算を占有せず、後続の完成済み部分へ反映を進めることを保証します。</summary>
        [Test]
        public void ApplyQueue_WaitingHeadDoesNotBlockReadyCoordinate()
        {
            var first = new ChunkCoordinate(0, 0);
            Schedule(first, AddChunk(0, 0));
            Invoke("ProcessApplyQueue", 4);
            for (int y = -1; y <= 1; y++) for (int x = 4; x <= 6; x++) AddChunk(x, y);
            var next = new ChunkCoordinate(5, 0);
            Schedule(next, completed[next]);
            Assert.That(Invoke("ProcessApplyQueue", 4), Is.EqualTo(0));
            var progress = (IDictionary)Field("applyProgressByCoordinate").GetValue(presenter);
            Assert.That(progress[next].GetType().GetField("NextSubChunkIndex").GetValue(progress[next]), Is.EqualTo(4));
        }

        /// <summary>使用済み近傍が同じ座標の別Entityへ変わった場合、旧進捗を混在させず消去待ちへ移すことを保証します。</summary>
        [Test]
        public void ApplyQueue_UsedEntityReplacementInvalidatesPartialProgress()
        {
            var center = new ChunkCoordinate(0, 0);
            Entity original = AddChunk(0, 0);
            Schedule(center, original);
            Invoke("ProcessApplyQueue", 4);
            world.EntityManager.DestroyEntity(original);
            completed.Remove(center);
            Schedule(center, AddChunk(0, 0));
            Assert.That(((IDictionary)Field("applyProgressByCoordinate").GetValue(presenter)).Contains(center), Is.False);
            Assert.That(((HashSet<ChunkCoordinate>)Field("pendingClearCoordinates").GetValue(presenter)).Contains(center), Is.True);
        }

        /// <summary>部分影が公開する全頂点のAlphaが、周囲完成後の全体影と一致することを保証します。</summary>
        [Test]
        public void PartialShadow_OnlyPublishesReadyTrianglesWithFinalAlpha()
        {
            AddChunk(0, 0, true);
            var deltas = new Dictionary<ChunkCoordinate, Entity>();
            CompletedChunkTerrainNeighborhoodFactory.TryCreateEffectiveAvailable(world.EntityManager, completed,
                deltas, new ChunkCoordinate(0, 0), catalogs.TerrainCatalog, out var partial);
            Invoke("BuildRockWallShadowMeshForSubChunks", new ChunkCoordinate(0, 0), partial, (ushort)0x0660);
            var shadows = (IDictionary)Field("rockWallShadowByCoordinate").GetValue(presenter);
            object shadow = shadows[new ChunkCoordinate(0, 0)];
            Mesh mesh = (Mesh)shadow.GetType().GetField("Mesh").GetValue(shadow);
            int[] triangles = mesh.triangles;
            Color32[] colors = mesh.colors32;
            Assert.That(triangles.Length, Is.EqualTo(4 * 256 * 6));
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) if (x != 0 || y != 0) AddChunk(x, y, true);
            CompletedChunkTerrainNeighborhoodFactory.TryCreateEffective(world.EntityManager, completed,
                deltas, new ChunkCoordinate(0, 0), catalogs.TerrainCatalog, out var full);
            Invoke("BuildRockWallShadowMesh", new ChunkCoordinate(0, 0), full);
            Color32[] fullColors = mesh.colors32;
            foreach (int index in triangles) Assert.That(colors[index], Is.EqualTo(fullColors[index]));
        }

        /// <summary>指定座標へ、全4096セルと資源Bufferを完成させたEntityを作ります。</summary>
        /// <param name="x">絶対チャンクXです。</param>
        /// <param name="y">絶対チャンクYです。</param>
        /// <param name="wall">内部影の比較用に全面Wallとする場合はtrueです。</param>
        /// <returns>Fixture Worldが所有するEntityです。</returns>
        private Entity AddChunk(int x, int y, bool wall = false)
        {
            var manager = world.EntityManager;
            Entity entity = manager.CreateEntity(typeof(ChunkGenerationStatus));
            manager.SetComponentData(entity, new ChunkGenerationStatus { Phase = ChunkGenerationPhase.ReadyToApply });
            var terrain = manager.AddBuffer<GeneratedTerrainCell>(entity);
            terrain.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            for (int i = 0; i < terrain.Length; i++) terrain[i] = new GeneratedTerrainCell
            { Biome = BiomeId.TemperateGrassland, GroundBlockRuntimeId = catalogs.GroundRuntimeId,
                WallBlockRuntimeId = wall ? catalogs.WallRuntimeId : default };
            var resources = manager.AddBuffer<GeneratedResourceTile>(entity);
            resources.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            for (int i = 0; i < resources.Length; i++) resources[i] = default;
            completed.Add(new ChunkCoordinate(x, y), entity);
            return entity;
        }

        /// <summary>実Presenterと同じ部分入力Fingerprintを使って反映を予約します。</summary>
        /// <param name="coordinate">対象座標です。</param>
        /// <param name="entity">完成Buffer所有者です。</param>
        private void Schedule(ChunkCoordinate coordinate, Entity entity)
        {
            Invoke("ScheduleApply", coordinate, entity, (ulong)Invoke("CalculatePendingTerrainFingerprint", coordinate));
        }

        /// <summary>テスト専用に非公開フィールドを取得し、製品APIへ検査口を追加しません。</summary>
        /// <param name="name">検査するフィールドです。</param>
        /// <returns>対象フィールドのメタデータです。</returns>
        private static FieldInfo Field(string name) => typeof(ChunkTilemapPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        /// <summary>実際の非公開反映経路を同期実行します。</summary>
        /// <param name="name">メソッド名です。</param>
        /// <param name="args">メソッドの引数です。</param>
        /// <returns>元メソッドの戻り値です。</returns>
        private object Invoke(string name, params object[] args) => typeof(ChunkTilemapPresenter).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, args);
    }
}
