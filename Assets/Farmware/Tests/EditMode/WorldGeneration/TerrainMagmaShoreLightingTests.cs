// このファイルは、ECS有効地形の岸候補から選ぶ製品Light2Dの上限・再利用・地形更新・原点変更を検証します。
// Tilemapや製品Sceneには書き込まず、テスト所有の完成地形配列とCameraを使用します。
using System.Linq;
using System.Reflection;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>固定区画の光源の再利用と、カメラ移動に依存しない岸の配置を確認する通常EditModeテストです。</summary>
    public sealed class TerrainMagmaShoreLightingTests
    {
        /// <summary>各テストで所有し、TearDownで破棄する完成地形です。</summary>
        private NativeArray<GeneratedTerrainCell> terrain;
        /// <summary>テスト所有のGround Presenterです。</summary>
        private GameObject root;
        /// <summary>テスト所有のCameraです。</summary>
        private GameObject cameraObject;
        /// <summary>検査対象の表示専用照明です。</summary>
        private TerrainMagmaShoreLighting lighting;
        /// <summary>光源へ共通表示原点を供給するテスト所有Controllerです。</summary>
        private CameraPanController controller;
        /// <summary>テスト内だけでマグマを表す任意の有効Runtime IDです。</summary>
        private readonly DefinitionRuntimeId magma = new DefinitionRuntimeId(103);
        /// <summary>テストで使うチャンクです。候補が近傍の中心配列へ対応します。</summary>
        private readonly ChunkCoordinate coordinate = new ChunkCoordinate(WorldLayer.Surface, 0, 0);

        /// <summary>各テスト前に、全域マグマのチャンクと無効Cameraを準備します。</summary>
        [SetUp]
        public void SetUp()
        {
            terrain = new NativeArray<GeneratedTerrainCell>(WorldGridConstants.TilesPerChunk, Allocator.Persistent);
            for (int i = 0; i < terrain.Length; i++) terrain[i] = new GeneratedTerrainCell { LiquidRuntimeId = magma };
            root = new GameObject("Magma Lighting Test", typeof(ChunkTilemapPresenter));
            lighting = root.AddComponent<TerrainMagmaShoreLighting>();
            cameraObject = new GameObject("Magma Lighting Test Camera", typeof(Camera), typeof(CameraPanController));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 32f;
            camera.aspect = 1f;
            cameraObject.transform.position = new Vector3(32f, 32f, -10f);
            controller = cameraObject.GetComponent<CameraPanController>();
            lighting.Configure(controller);
            lighting.BeginSubChunk(coordinate, 0);
        }

        /// <summary>成功・失敗にかかわらずテストObjectとNative配列を破棄します。</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(cameraObject);
            if (terrain.IsCreated) terrain.Dispose();
        }

        /// <summary>マグマ内部や未完成近傍の外側へ、架空の岸光源を作らないことを保証します。</summary>
        [Test]
        public void InteriorAndUnavailableNeighbors_DoNotCreateShoreLights()
        {
            Collect(10, 10);
            Collect(0, 0);
            Refresh();
            Assert.That(Lights().Length, Is.Zero);
        }

        /// <summary>固定区画の光源を灯数で脱落させず、消去・再生成でも同じプールを使い回すことを保証します。</summary>
        [Test]
        public void ShoreLights_AreBoundedReusedAndClearedWithTerrain()
        {
            for (int i = 0; i < terrain.Length; i++) terrain[i] = default;
            // 同じ3×3区画へ重ならない孤立マグマを64個用意し、旧16灯上限で脱落していた光源も確認します。
            for (int y = 2; y < 32; y += 4)
                for (int x = 2; x < 32; x += 4) AddShore(x, y);
            Refresh();
            Light2D[] first = Lights();
            Assert.That(first.Length, Is.EqualTo(64));
            Assert.That(first.All(light => light.blendStyleIndex == 1 && !light.shadowsEnabled), Is.True);
            Assert.That(first.All(light => light.intensity == 0.12f), Is.True);
            lighting.RemoveChunk(coordinate);
            Refresh();
            Assert.That(first.All(light => !light.enabled), Is.True);
            lighting.BeginSubChunk(coordinate, 0);
            AddShore(10, 10);
            Refresh();
            CollectionAssert.AreEquivalent(first, Lights());
            Assert.That(first.Count(light => light.enabled), Is.EqualTo(1));
            lighting.ClearCandidates();
            Assert.That(first.All(light => !light.enabled), Is.True);
            // EditModeではAwakeを経たPlay中のOnDestroy通知を再現しないため、実際の破棄は製品Play確認で別途検証します。
        }

        /// <summary>Dirty更新で同じ部分がマグマ内部へ変わったとき、古い岸の光を消すことを保証します。</summary>
        [Test]
        public void DirtySubChunk_ReplacesOldShoreCandidates()
        {
            AddShore(10, 10);
            Refresh();
            Assert.That(Lights().Count(light => light.enabled), Is.EqualTo(1));
            lighting.BeginSubChunk(coordinate, 0);
            terrain[WorldGrid.ToTileIndex(11, 10)] = new GeneratedTerrainCell { LiquidRuntimeId = magma };
            Collect(10, 10);
            Refresh();
            Assert.That(Lights().All(light => !light.enabled), Is.True);
        }

        /// <summary>原点だけを変更したフレームでも、選択周期を待たず絶対地点の光を相対化し直すことを保証します。</summary>
        [Test]
        public void FloatingOrigin_RepositionsExistingLightsImmediately()
        {
            AddShore(10, 10);
            Refresh();
            Light2D light = Lights().Single();
            Assert.That(light.transform.position.x, Is.EqualTo(10.5f));
            // 表示原点とCamera相対位置を逆方向へ変え、絶対Camera位置を保つ実際のFloating Originを再現します。
            typeof(CameraPanController).GetField("currentDisplayOrigin", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(controller, new DisplayOrigin(new int2(512, -512)));
            cameraObject.transform.position += new Vector3(-512f, 512f, 0f);
            Refresh();
            Assert.That(light.transform.position.x, Is.EqualTo(-501.5f));
            Assert.That(light.transform.position.y, Is.EqualTo(522.5f));
        }

        /// <summary>負座標・遠距離でも、絶対候補を表示原点へ戻した位置が近距離と同じになることを保証します。</summary>
        /// <param name="chunkX">検査する絶対チャンクXです。</param>
        /// <param name="chunkY">検査する絶対チャンクYです。</param>
        [TestCase(-1, -1)]
        [TestCase(15625000, -15625000)]
        public void DistantCoordinates_PreserveLocalLightPosition(int chunkX, int chunkY)
        {
            var target = new ChunkCoordinate(WorldLayer.Surface, chunkX, chunkY);
            lighting.BeginSubChunk(target, 0);
            // 絶対格子の位相が変わっても、この孤立セルだけが区画内の岸候補になるよう準備します。
            for (int i = 0; i < terrain.Length; i++) terrain[i] = default;
            terrain[WorldGrid.ToTileIndex(10, 10)] = new GeneratedTerrainCell { LiquidRuntimeId = magma };
            var neighborhood = new CompletedChunkTerrainNeighborhood(default, default, default, default, terrain,
                default, default, default, default);
            lighting.CollectMagmaCell(target, 0, 10, 10, neighborhood);
            typeof(CameraPanController).GetField("currentDisplayOrigin", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(controller, new DisplayOrigin(new int2(chunkX * 64, chunkY * 64)));
            Refresh();
            Light2D light = Lights().Single();
            Assert.That(light.transform.position.x, Is.EqualTo(10.5f));
            Assert.That(light.transform.position.y, Is.EqualTo(10.5f));
        }

        /// <summary>チャンク端の岸判定が完成済みの東隣チャンクも参照することを保証します。</summary>
        [Test]
        public void ChunkBoundary_UsesCompletedNeighborForShore()
        {
            using (var east = new NativeArray<GeneratedTerrainCell>(WorldGridConstants.TilesPerChunk, Allocator.Temp))
            {
                var neighborhood = new CompletedChunkTerrainNeighborhood(default, default, default, default, terrain,
                    east, default, default, default);
                lighting.CollectMagmaCell(coordinate, 0, 63, 10, neighborhood);
                Refresh();
                Assert.That(Lights().Single().transform.position.x, Is.EqualTo(63.5f));
            }
        }

        /// <summary>壁に隠れた液面を光源にせず、見える液面側にできた表示上の岸だけを照らすことを保証します。</summary>
        /// <param name="wallDistance">現在のマグマから手前のWall根元までの距離です。</param>
        /// <param name="visible">現在セルが壁に隠れず、表示上の岸になる場合はtrueです。</param>
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        public void ProjectedWall_SelectsOnlyVisibleMagmaShore(int wallDistance, bool visible)
        {
            // マグマが論理的には連続している入力で、壁の投影だけが新しい表示上の岸を作る状況を再現します。
            terrain[WorldGrid.ToTileIndex(10, 10 - wallDistance)] = new GeneratedTerrainCell
                { WallBlockRuntimeId = new DefinitionRuntimeId(201) };
            Collect(10, 10);
            Refresh();
            Assert.That(Lights().Count(light => light.enabled), Is.EqualTo(visible ? 1 : 0));
        }

        /// <summary>手前の隣チャンクのWall上面も被覆判定へ含め、チャンク境界だけ光が漏れることを防ぎます。</summary>
        [Test]
        public void ProjectedWallAcrossChunkBoundary_HidesMagmaLightSource()
        {
            var south = new NativeArray<GeneratedTerrainCell>(WorldGridConstants.TilesPerChunk, Allocator.Temp);
            try
            {
                south[WorldGrid.ToTileIndex(10, 62)] = new GeneratedTerrainCell
                    { WallBlockRuntimeId = new DefinitionRuntimeId(201) };
                var neighborhood = new CompletedChunkTerrainNeighborhood(default, south, default, default, terrain,
                    default, default, default, default);
                lighting.CollectMagmaCell(coordinate, 0, 10, 0, neighborhood);
                Refresh();
                Assert.That(Lights().Length, Is.Zero);
            }
            finally { south.Dispose(); }
        }

        /// <summary>Cameraを動かして候補更新周期を進めても、重なって見えている範囲の光源と強度を維持することを保証します。</summary>
        [Test]
        public void CameraMovement_PreservesLightsInOverlappingVisibleArea()
        {
            for (int i = 0; i < terrain.Length; i++) terrain[i] = default;
            for (int y = 2; y < 32; y += 4)
                for (int x = 2; x < 32; x += 4) AddShore(x, y);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographicSize = 16f;
            cameraObject.transform.position = new Vector3(20f, 20f, -10f);
            Refresh();
            Vector3[] commonBefore = Lights().Where(light => light.enabled && light.transform.position.x >= 6f)
                .Select(light => light.transform.position).ToArray();
            Assert.That(commonBefore.Length, Is.EqualTo(56));
            cameraObject.transform.position = new Vector3(25f, 20f, -10f);
            // 更新周期を待たず可視範囲を更新し、残って見える岸の光源と強度が入れ替わらないことを確認します。
            Refresh();
            Light2D[] after = Lights().Where(light => light.enabled).ToArray();
            CollectionAssert.AreEquivalent(commonBefore, after.Select(light => light.transform.position).ToArray());
            Assert.That(after.All(light => light.intensity == 0.12f), Is.True);
        }

        /// <summary>同じ固定区画に複数の岸があっても、収集順ではなく中心距離と絶対座標で同じ1灯を選ぶことを保証します。</summary>
        [Test]
        public void FixedWorldCell_PreservesAnchorAcrossCollectionOrders()
        {
            for (int i = 0; i < terrain.Length; i++) terrain[i] = default;
            for (int y = 9; y <= 11; y++)
                for (int x = 9; x <= 11; x++) terrain[WorldGrid.ToTileIndex(x, y)] = new GeneratedTerrainCell { LiquidRuntimeId = magma };
            for (int y = 9; y <= 11; y++)
                for (int x = 9; x <= 11; x++) Collect(x, y);
            Refresh();
            Vector3 first = Lights().Single(light => light.enabled).transform.position;
            Assert.That(first, Is.EqualTo(new Vector3(10.5f, 9.5f, 0f)));
            lighting.BeginSubChunk(coordinate, 0);
            for (int y = 11; y >= 9; y--)
                for (int x = 11; x >= 9; x--) Collect(x, y);
            Refresh();
            Assert.That(Lights().Single(light => light.enabled).transform.position, Is.EqualTo(first));
        }

        /// <summary>隣セルを空地形へ変え、現在セルを岸として収集します。</summary>
        /// <param name="x">現在セルのXです。</param>
        /// <param name="y">現在セルのYです。</param>
        private void AddShore(int x, int y)
        {
            terrain[WorldGrid.ToTileIndex(x, y)] = new GeneratedTerrainCell { LiquidRuntimeId = magma };
            terrain[WorldGrid.ToTileIndex(x + 1, y)] = default;
            Collect(x, y);
        }

        /// <summary>テスト所有配列を完成近傍として一時的に借用し、マグマセルを収集します。</summary>
        /// <param name="x">現在セルのXです。</param>
        /// <param name="y">現在セルのYです。</param>
        private void Collect(int x, int y)
        {
            var neighborhood = new CompletedChunkTerrainNeighborhood(default, default, default, default, terrain,
                default, default, default, default);
            lighting.CollectMagmaCell(coordinate, 0, x, y, neighborhood);
        }

        /// <summary>Playを開始せず、実際のLateUpdateによる光源選択と配置を1回実行します。</summary>
        private void Refresh()
        {
            typeof(TerrainMagmaShoreLighting).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(lighting, null);
        }

        /// <summary>テスト対象が所有する光源だけを、無効状態も含めて返します。</summary>
        /// <returns>破棄と再利用を確認するLight2D配列です。</returns>
        private Light2D[] Lights() => root.GetComponentsInChildren<Light2D>(true);
    }
}
