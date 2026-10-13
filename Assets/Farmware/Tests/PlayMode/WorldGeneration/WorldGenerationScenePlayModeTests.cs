// このファイルは、SampleSceneを実際に再生し、初期カメラ位置、ECS地形・資源生成、Tilemap表示までの縦切りを検証します。
// F3/F4と開発者用表示を含め、純粋計算では確認できないGameObject・SubScene・Presentation間の参照切れを検出します。

using System.Collections;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.PlayModeTests
{
    /// <summary>
    /// SampleSceneの起動後にデバッグ表示、生成設定Singleton、チャンクEntity、地形・資源Tileが正しく連携することを保証します。
    /// Unity Test RunnerのPlayModeテストから実行されます。
    /// </summary>
    public sealed class WorldGenerationScenePlayModeTests
    {
        /// <summary>SubSceneの非同期Bake・ロードと初回チャンク生成を待つ最大描画フレーム数です。</summary>
        private const int MaximumWaitFrames = 300;

        /// <summary>初期スポーンチャンクが保持範囲外へ確実に出る、テスト用カメラ水平移動量です。</summary>
        private const int FarCameraOffsetInTiles = WorldGridConstants.LogicalChunkSize * 8;


        /// <summary>
        /// Windowboxの基準解像度を整数倍して中央配置し、余ったGameビュー領域が黒帯として除外されることを検証します。
        /// F4グリッドとHUDが参照する純粋な矩形計算なので、Sceneロード前に複数アスペクト比をまとめて確認します。
        /// </summary>
        /// <param name="screenWidth">テストするGameビュー全体の幅です。単位は画面ピクセルです。</param>
        /// <param name="screenHeight">テストするGameビュー全体の高さです。単位は画面ピクセルです。</param>
        /// <param name="expectedX">期待する地形描画矩形の左端です。単位は画面ピクセルです。</param>
        /// <param name="expectedY">期待する地形描画矩形の上端です。単位は画面ピクセルです。</param>
        /// <param name="expectedWidth">期待する地形描画矩形の幅です。単位は画面ピクセルです。</param>
        /// <param name="expectedHeight">期待する地形描画矩形の高さです。単位は画面ピクセルです。</param>
        [TestCase(1920, 1080, 0, 0, 1920, 1080)]
        [TestCase(1760, 916, 160, 53, 1440, 810)]
        [TestCase(1761, 917, 160, 54, 1440, 810)]
        [TestCase(1200, 900, 120, 180, 960, 540)]
        public void CalculateWindowboxGuiRect_CentersIntegerScaledViewport(
            int screenWidth,
            int screenHeight,
            int expectedX,
            int expectedY,
            int expectedWidth,
            int expectedHeight)
        {
            Rect actual = WorldGridDebugOverlay.CalculateWindowboxGuiRect(
                screenWidth,
                screenHeight,
                480,
                270);

            Assert.That(actual.x, Is.EqualTo(expectedX));
            Assert.That(actual.y, Is.EqualTo(expectedY));
            Assert.That(actual.width, Is.EqualTo(expectedWidth));
            Assert.That(actual.height, Is.EqualTo(expectedHeight));
        }

        /// <summary>
        /// 現在の負座標と記録済み初期スポーンを準備し、F3文字列が両者を混同せずタイル・チャンク座標として表示することを保証します。
        /// </summary>
        [Test]
        public void BuildCoordinateHudText_IncludesRecordedInitialSpawnLocation()
        {
            InitialSpawnLocation spawnLocation = new InitialSpawnLocation
            {
                TileX = -65,
                TileY = 64
            };

            string text = WorldGridDebugOverlay.BuildCoordinateHudText(
                new Vector3(5.5f, -2.5f, -10f),
                new Vector3Int(5, -3, 0),
                true,
                spawnLocation);

            Assert.That(text, Does.Contain("Tile  : (5, -3)"));
            Assert.That(text, Does.Contain("Spawn: Tile (-65, 64) / Chunk (-2, 1)"));
        }

        /// <summary>
        /// 負座標の初期スポーンを準備し、F4の赤い所属チャンク枠と専用ラベルがF3と同じ床方向チャンクを指すことを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnF4Marker_UsesContainingChunkBoundsAndDistinctLabel()
        {
            InitialSpawnLocation spawnLocation = new InitialSpawnLocation
            {
                TileX = -65,
                TileY = 64
            };

            RectInt chunkBounds = WorldGridDebugOverlay.CalculateInitialSpawnChunkTileBounds(
                spawnLocation);
            string spawnLabel = WorldGridDebugOverlay.BuildChunkCoordinateLabelText(-2, 1, true);
            string normalLabel = WorldGridDebugOverlay.BuildChunkCoordinateLabelText(-1, 1, false);

            Assert.That(chunkBounds, Is.EqualTo(new RectInt(-128, 64, 64, 64)));
            Assert.That(spawnLabel, Is.EqualTo("Spawn Chunk (-2, 1)"));
            Assert.That(normalLabel, Is.EqualTo("Chunk (-1, 1)"));
        }

        /// <summary>
        /// F4グリッドの縦線と横線が、Windowbox内でGUI.matrixを回転せず描画できる正の矩形へ変換されることを検証します。
        /// 開始・終了の向きを逆にしたケースも含め、縦線だけ消える不具合の再発を防ぎます。
        /// </summary>
        /// <param name="startX">線の開始X座標です。単位は画面ピクセルです。</param>
        /// <param name="startY">線の開始Y座標です。単位は画面ピクセルです。</param>
        /// <param name="endX">線の終了X座標です。単位は画面ピクセルです。</param>
        /// <param name="endY">線の終了Y座標です。単位は画面ピクセルです。</param>
        /// <param name="expectedX">期待する描画矩形の左端です。単位は画面ピクセルです。</param>
        /// <param name="expectedY">期待する描画矩形の上端です。単位は画面ピクセルです。</param>
        /// <param name="expectedWidth">期待する描画矩形の幅です。単位は画面ピクセルです。</param>
        /// <param name="expectedHeight">期待する描画矩形の高さです。単位は画面ピクセルです。</param>
        [TestCase(25f, 10f, 25f, 90f, 24f, 10f, 2f, 80f)]
        [TestCase(25f, 90f, 25f, 10f, 24f, 10f, 2f, 80f)]
        [TestCase(10f, 25f, 90f, 25f, 10f, 24f, 80f, 2f)]
        [TestCase(90f, 25f, 10f, 25f, 10f, 24f, 80f, 2f)]
        public void TryCalculateAxisAlignedLineRect_ReturnsVisibleRectangle(
            float startX,
            float startY,
            float endX,
            float endY,
            float expectedX,
            float expectedY,
            float expectedWidth,
            float expectedHeight)
        {
            bool succeeded = WorldGridDebugOverlay.TryCalculateAxisAlignedLineRect(
                new Vector2(startX, startY),
                new Vector2(endX, endY),
                2f,
                out Rect actual);

            Assert.That(succeeded, Is.True);
            Assert.That(actual.x, Is.EqualTo(expectedX));
            Assert.That(actual.y, Is.EqualTo(expectedY));
            Assert.That(actual.width, Is.EqualTo(expectedWidth));
            Assert.That(actual.height, Is.EqualTo(expectedHeight));
        }

        /// <summary>
        /// 開発者用可視化が7種Biome、連続ノイズ、水域、移行帯、資源を異なる色へ写像し、論理BiomeとGroundの差を正しく判定することを検証します。
        /// SceneやECS Worldを作らない純粋計算として、表示色の両端、水域分類、水域除外、最終資源分類をまとめて保証します。
        /// </summary>
        [Test]
        public void DebugVisualization_MapsGeneratedTileValuesWithoutChangingSimulationData()
        {
            GeneratedTile forestTransitionTile = new GeneratedTile
            {
                Biome = BiomeId.Forest,
                Ground = GroundType.Normal,
                Elevation = ushort.MaxValue,
                Temperature = 0,
                Moisture = ushort.MaxValue,
                Weirdness = ushort.MaxValue
            };

            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsBiomeTransitionTile(forestTransitionTile),
                Is.True,
                "森林Biomeで通常地面が選ばれた陸上タイルは移行帯として強調される必要があります。");

            Color coldColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Temperature,
                forestTransitionTile);
            Assert.That(coldColor.b, Is.GreaterThan(coldColor.r), "最低気温は赤より青が強い必要があります。");

            Color highElevationColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Elevation,
                forestTransitionTile);
            Assert.That(
                highElevationColor.r,
                Is.GreaterThan(0.9f),
                "最高標高は凡例どおり白に近い必要があります。");

            Color highWeirdnessColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Weirdness,
                forestTransitionTile);
            Assert.That(
                highWeirdnessColor.r,
                Is.GreaterThan(highWeirdnessColor.b),
                "最高Weirdnessは凡例どおり紫より黄に近い必要があります。");

            Color wetlandColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Biome,
                new GeneratedTile { Biome = BiomeId.Wetland });
            Color volcanicColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Biome,
                new GeneratedTile { Biome = BiomeId.Volcanic });
            Assert.That(
                wetlandColor,
                Is.Not.EqualTo(volcanicColor),
                "7バイオーム表示では湿地と火山を別色で判別できる必要があります。");

            Color transitionColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.BiomeTransition,
                forestTransitionTile);
            Assert.That(
                transitionColor.r,
                Is.GreaterThan(transitionColor.g),
                "移行タイルは暗色ではなくマゼンタ系で強調される必要があります。");

            GeneratedTile waterTile = forestTransitionTile;
            waterTile.Ground = GroundType.Water;
            waterTile.WaterSalinity = WaterSalinity.FreshWater;
            waterTile.WaterBodyType = WaterBodyType.River;
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsBiomeTransitionTile(waterTile),
                Is.False,
                "標高分類で生じる水域はバイオーム移行帯へ数えません。");

            Color freshWaterColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterSalinity,
                waterTile);
            GeneratedTile saltWaterTile = waterTile;
            saltWaterTile.WaterSalinity = WaterSalinity.SaltWater;
            saltWaterTile.WaterBodyType = WaterBodyType.Sea;
            Color saltWaterColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterSalinity,
                saltWaterTile);
            Assert.That(
                freshWaterColor,
                Is.Not.EqualTo(saltWaterColor),
                "淡水と海水はWater Salinity表示で別の色になる必要があります。");

            Color riverColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterBody,
                waterTile);
            GeneratedTile pondTile = waterTile;
            pondTile.WaterBodyType = WaterBodyType.Pond;
            Color pondColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterBody,
                pondTile);
            Color seaColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterBody,
                saltWaterTile);
            Assert.That(riverColor, Is.Not.EqualTo(pondColor), "川と池は別の色で判別できる必要があります。");
            Assert.That(pondColor, Is.Not.EqualTo(seaColor), "池と海は別の色で判別できる必要があります。");
            Assert.That(riverColor, Is.Not.EqualTo(seaColor), "川と海は別の色で判別できる必要があります。");

            GeneratedTile magmaTile = forestTransitionTile;
            magmaTile.Biome = BiomeId.Volcanic;
            magmaTile.Ground = GroundType.Magma;
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsBiomeTransitionTile(magmaTile),
                Is.False,
                "独立生成されるマグマはバイオーム移行帯へ数えません。");
            Color magmaColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Magma,
                magmaTile,
                0f,
                GeneratedResourceType.None,
                true);
            Color magmaInfluenceColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.Magma,
                    forestTransitionTile,
                    0f,
                    GeneratedResourceType.None,
                    true);
            Color magmaOutsideColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.Magma,
                    forestTransitionTile);
            Assert.That(magmaColor, Is.Not.EqualTo(magmaInfluenceColor));
            Assert.That(magmaInfluenceColor, Is.Not.EqualTo(magmaOutsideColor));

            Color treeResourceColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Resources,
                forestTransitionTile,
                0f,
                GeneratedResourceType.Tree);
            Color stoneResourceColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Resources,
                forestTransitionTile,
                0f,
                GeneratedResourceType.Stone);
            Color emptyResourceColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Resources,
                forestTransitionTile);
            Assert.That(
                treeResourceColor,
                Is.Not.EqualTo(stoneResourceColor),
                "Resources表示では木と石を別色で判別できる必要があります。");
            Assert.That(
                emptyResourceColor,
                Is.Not.EqualTo(treeResourceColor),
                "Resources表示では配置なしと樹木を別色で判別できる必要があります。");

            Color hiddenColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.None,
                forestTransitionTile);
            Assert.That(hiddenColor.a, Is.EqualTo(0f), "Offモードは地形を一切着色してはいけません。");
        }

        /// <summary>
        /// 岩壁の露出面から内部へ連続増加する影不透明度と、共有頂点へ平均する補間値を確認します。
        /// 64×64チャンク境界の両側もワールド座標から同じ規則で分類され、内部だけ資源表示を隠すことを保証します。
        /// </summary>
        [Test]
        public void RockWallAppearance_UsesContinuousGradientAcrossChunkBoundary()
        {
            float exposedOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(1);
            float shallowOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(4);
            float middleOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(16);
            float deepOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(
                    int.MaxValue);
            Assert.That(exposedOpacity, Is.EqualTo(0f));
            Assert.That(shallowOpacity, Is.GreaterThan(exposedOpacity));
            Assert.That(
                shallowOpacity,
                Is.GreaterThan(0.5f),
                "露出面から1タイル内側の影が薄すぎる状態へ戻ってはいけません。");
            Assert.That(middleOpacity, Is.GreaterThan(shallowOpacity));
            Assert.That(
                deepOpacity,
                Is.EqualTo(RockWallAppearanceUtility.MaximumShadowOpacity));

            // 共有頂点は周囲4タイルの中間値になり、1タイル全面を同じ暗色にする旧方式へ戻っていないことを保証します。
            float interpolatedVertexOpacity = RockWallAppearanceUtility.CalculateVertexOpacity(
                0f,
                shallowOpacity,
                middleOpacity,
                deepOpacity);
            Assert.That(interpolatedVertexOpacity, Is.GreaterThan(shallowOpacity));
            Assert.That(interpolatedVertexOpacity, Is.LessThan(deepOpacity));
            Assert.That(
                RockWallAppearanceUtility.ShouldDisplayResource(RockWallAppearance.Interior),
                Is.False,
                "岩壁内部の資源アイコンは影の上へ表示してはいけません。");
            Assert.That(
                RockWallAppearanceUtility.ShouldDisplayResource(RockWallAppearance.Exposed),
                Is.True,
                "空間へ露出した岩壁の資源表示は内部影の非表示規則へ巻き込んではいけません。");
            Assert.That(
                RockWallAppearanceUtility.ShouldDisplayResource(RockWallAppearance.None),
                Is.True,
                "通常地面の資源アイコンは岩壁用の非表示規則へ巻き込んではいけません。");

            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("rock-wall-chunk-edge-test"),
                1);
            settings.SeaLevelThreshold = 0;
            settings.FreshWaterMaximumElevation = 0;
            settings.RiverCenterLineHalfWidth = 0;
            settings.PondThreshold = ushort.MaxValue;
            bool foundShadowAcrossChunkBoundary = false;

            // X=63とX=64は別チャンクです。境界をまたぐ岩壁を探し、両側が座標再生成で影分類できることを確認します。
            for (int tileY = -256; tileY <= 256; tileY++)
            {
                RockWallAppearance left =
                    RockWallAppearanceUtility.ClassifyAt(settings, 63, tileY);
                RockWallAppearance right =
                    RockWallAppearanceUtility.ClassifyAt(settings, 64, tileY);
                if (left != RockWallAppearance.None && right != RockWallAppearance.None)
                {
                    foundShadowAcrossChunkBoundary = true;
                    break;
                }
            }

            Assert.That(
                foundShadowAcrossChunkBoundary,
                Is.True,
                "64×64チャンク境界をまたぐ岩壁を試験範囲で確認できませんでした。");

            // 境界上の座標版も0〜最大値へ収まり、未ロード隣接チャンクを含む距離探索で例外や範囲外値を出さないことを確認します。
            float boundaryOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityAt(settings, 63, 0);
            Assert.That(
                boundaryOpacity,
                Is.InRange(0f, RockWallAppearanceUtility.MaximumShadowOpacity));

            GeneratedTile rockWallTile = new GeneratedTile { Ground = GroundType.RockWall };
            Color exposedColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.RockWall,
                rockWallTile,
                0f);
            Color deepShadowColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.RockWall,
                rockWallTile,
                RockWallAppearanceUtility.MaximumShadowOpacity);
            Assert.That(
                exposedColor,
                Is.Not.EqualTo(deepShadowColor),
                "岩壁デバッグ表示では露出面と深い内部影を別色で確認できる必要があります。");
        }

        /// <summary>
        /// SampleSceneを読み込み、初回表示、遠方移動時のアンロード、原点へ戻った再生成の決定論を検証します。
        /// </summary>
        /// <returns>非同期Sceneロードと複数フレームの生成・表示完了を待つIEnumeratorです。</returns>
        [UnityTest]
        public IEnumerator SampleScene_StreamsAndDeterministicallyRegeneratesFallbackTiles()
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null, "Build SettingsからSampleSceneを読み込めませんでした。");

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            GameObject groundTilemapObject = GameObject.Find("World Grid/Ground Tilemap");
            Tilemap tilemap = groundTilemapObject == null
                ? null
                : groundTilemapObject.GetComponent<Tilemap>();
            Assert.That(tilemap, Is.Not.Null, "SampleSceneに地形表示用Ground Tilemapがありません。");
            GameObject resourceTilemapObject = GameObject.Find("World Grid/Resource Tilemap");
            Tilemap resourceTilemap = resourceTilemapObject == null
                ? null
                : resourceTilemapObject.GetComponent<Tilemap>();
            Assert.That(resourceTilemap, Is.Not.Null, "SampleSceneに資源表示用Resource Tilemapがありません。");
            GameObject resourceBehindRockWallTilemapObject =
                GameObject.Find("World Grid/Resource Behind Rock Wall Tilemap");
            Tilemap resourceBehindRockWallTilemap = resourceBehindRockWallTilemapObject == null
                ? null
                : resourceBehindRockWallTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                resourceBehindRockWallTilemap,
                Is.Not.Null,
                "SampleSceneに手前岩壁で隠す資源専用Tilemapがありません。");
            GameObject liquidBaseTilemapObject =
                GameObject.Find("World Grid/Liquid Base Tilemap");
            Tilemap liquidBaseTilemap = liquidBaseTilemapObject == null
                ? null
                : liquidBaseTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                liquidBaseTilemap,
                Is.Not.Null,
                "SampleSceneに8px下げた静止液面専用Tilemapがありません。");
            Assert.That(
                liquidBaseTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells)
                    .Within(0.0001f),
                "静止した水・マグマ面は論理セルを変えず、表示だけ8px下へ移動する必要があります。");
            GameObject liquidAnimationTilemapObject =
                GameObject.Find("World Grid/Liquid Surface Animation Tilemap");
            Tilemap liquidAnimationTilemap = liquidAnimationTilemapObject == null
                ? null
                : liquidAnimationTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                liquidAnimationTilemap,
                Is.Not.Null,
                "SampleSceneに液面アニメーション専用Tilemapがありません。");
            Assert.That(
                liquidAnimationTilemap.animationFrameRate,
                Is.EqualTo(4f),
                "液面アニメーションは仮素材の4fps設定を保持する必要があります。");
            Assert.That(
                liquidAnimationTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells)
                    .Within(0.0001f),
                "波・気泡は静止液面と同じ8px下へ移動し、上下へ分離してはいけません。");
            Assert.That(
                GameObject.Find("World Grid/Terrain Front Edge Tilemap"),
                Is.Null,
                "土地断面を一体化したため、旧Terrain Front Edge Tilemapを残してはいけません。");
            Assert.That(
                GameObject.Find("World Grid/Rock Wall Front Face Tilemap"),
                Is.Null,
                "岩壁側面を一体化したため、旧Rock Wall Front Face Tilemapを残してはいけません。");
            Assert.That(
                GameObject.Find("World Grid/Rock Wall Top Tilemap"),
                Is.Null,
                "一体型岩壁へ改名したため、旧Rock Wall Top Tilemapを残してはいけません。");
            GameObject rockWallTilemapObject =
                GameObject.Find("World Grid/Rock Wall Tilemap");
            Tilemap rockWallTilemap = rockWallTilemapObject == null
                ? null
                : rockWallTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                rockWallTilemap,
                Is.Not.Null,
                "SampleSceneに16×28の一体型岩壁専用Tilemapがありません。");
            Assert.That(
                rockWallTilemap.transform.localPosition.y,
                Is.EqualTo(0f).Within(0.0001f),
                "岩壁の高さはSprite Pivotで表すため、Tilemap Transformを移動してはいけません。");
            GameObject rockWallResourceTilemapObject =
                GameObject.Find("World Grid/Rock Wall Resource Tilemap");
            Tilemap rockWallResourceTilemap = rockWallResourceTilemapObject == null
                ? null
                : rockWallResourceTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                rockWallResourceTilemap,
                Is.Not.Null,
                "SampleSceneに岩壁上資源専用Tilemapがありません。");
            Assert.That(
                rockWallResourceTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.RockWallElevationInCells).Within(0.0001f),
                "岩壁上資源は岩壁上面と同じ12px上へ移動している必要があります。");

            // 液面の下半分を手前土地で隠す前後関係はTransform位置だけでは決まらないため、Rendererの描画順もScene実体から確認します。
            TilemapRenderer groundRenderer = tilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer rockWallRenderer = rockWallTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer resourceRenderer = resourceTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer resourceBehindRockWallRenderer =
                resourceBehindRockWallTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer liquidBaseRenderer = liquidBaseTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer liquidAnimationRenderer =
                liquidAnimationTilemap.GetComponent<TilemapRenderer>();
            Assert.That(groundRenderer, Is.Not.Null, "Ground TilemapにTilemapRendererがありません。");
            Assert.That(rockWallRenderer, Is.Not.Null, "Rock Wall TilemapにTilemapRendererがありません。");
            Assert.That(resourceRenderer, Is.Not.Null, "Resource TilemapにTilemapRendererがありません。");
            Assert.That(
                resourceBehindRockWallRenderer,
                Is.Not.Null,
                "Resource Behind Rock Wall TilemapにTilemapRendererがありません。");
            Assert.That(
                groundRenderer.mode,
                Is.EqualTo(TilemapRenderer.Mode.Individual),
                "16×24土地はセルごとの行ソートが必要なためIndividual Modeで描画してください。");
            Assert.That(
                rockWallRenderer.mode,
                Is.EqualTo(TilemapRenderer.Mode.Individual),
                "16×28岩壁はセルごとの行ソートが必要なためIndividual Modeで描画してください。");
            Assert.That(
                groundRenderer.sortOrder,
                Is.EqualTo(TilemapRenderer.SortOrder.TopLeft),
                "16×24土地は奥の高いYから先に描き、手前の低いYを後から重ねてください。");
            Assert.That(
                rockWallRenderer.sortOrder,
                Is.EqualTo(TilemapRenderer.SortOrder.TopLeft),
                "16×28岩壁は奥の高いYから先に描き、手前の低いYを後から重ねてください。");
            Assert.That(
                resourceBehindRockWallRenderer.sortingOrder,
                Is.LessThan(rockWallRenderer.sortingOrder),
                "直前方が岩壁の通常資源は、岩壁より先に描いて重なる下部を隠してください。");
            Assert.That(
                resourceRenderer.sortingOrder,
                Is.GreaterThan(rockWallRenderer.sortingOrder),
                "岩壁に隠れない通常資源は、岩壁より後から描いて前面へ表示してください。");
            Assert.That(
                liquidBaseRenderer,
                Is.Not.Null,
                "Liquid Base TilemapにTilemapRendererがありません。");
            Assert.That(
                liquidAnimationRenderer,
                Is.Not.Null,
                "Liquid Surface Animation TilemapにTilemapRendererがありません。");
            Assert.That(
                liquidBaseRenderer.sortingOrder,
                Is.LessThan(liquidAnimationRenderer.sortingOrder),
                "静止液面は透明な波アニメーションより奥へ描画する必要があります。");
            Assert.That(
                liquidAnimationRenderer.sortingOrder,
                Is.LessThan(groundRenderer.sortingOrder),
                "8px下げた液面の下辺は、手前にある土地上面より奥へ描画する必要があります。");
            CameraPanController cameraPanController = Object.FindFirstObjectByType<CameraPanController>();
            Assert.That(cameraPanController, Is.Not.Null, "Main CameraにCameraPanControllerがありません。");
            InitialCameraPositionController initialPositionController =
                Object.FindFirstObjectByType<InitialCameraPositionController>();
            Assert.That(
                initialPositionController,
                Is.Not.Null,
                "Main CameraにInitialCameraPositionControllerがありません。");
            WorldGridDebugOverlay debugOverlay = Object.FindFirstObjectByType<WorldGridDebugOverlay>();
            Assert.That(debugOverlay, Is.Not.Null, "Main CameraにWorldGridDebugOverlayがありません。");
            WorldGenerationDebugVisualizationOverlay developerVisualization =
                Object.FindFirstObjectByType<WorldGenerationDebugVisualizationOverlay>();
            Assert.That(
                developerVisualization,
                Is.Not.Null,
                "Main CameraにWorldGenerationDebugVisualizationOverlayがありません。");
            PixelPerfectCamera pixelPerfectCamera = Object.FindFirstObjectByType<PixelPerfectCamera>();
            Assert.That(pixelPerfectCamera, Is.Not.Null, "Main CameraにURP PixelPerfectCameraがありません。");

            // 制作開始前に決めた16 PPU・480×270・WindowboxがSceneへ保存され、表示範囲が意図せず戻らないことを保証します。
            Assert.That(pixelPerfectCamera.assetsPPU, Is.EqualTo(16));
            Assert.That(pixelPerfectCamera.refResolutionX, Is.EqualTo(480));
            Assert.That(pixelPerfectCamera.refResolutionY, Is.EqualTo(270));
            Assert.That(
                pixelPerfectCamera.gridSnapping,
                Is.EqualTo(PixelPerfectCamera.GridSnapping.PixelSnapping));
            Assert.That(
                pixelPerfectCamera.cropFrame,
                Is.EqualTo(PixelPerfectCamera.CropFrame.Windowbox));

            // F3・F4と同じ公開入口で表示状態を切り替え、両オーバーレイを独立して操作できることを確認します。
            debugOverlay.SetCoordinateHudVisible(true);
            Assert.That(debugOverlay.IsCoordinateHudVisible, Is.True);
            Assert.That(debugOverlay.IsChunkGridVisible, Is.False);
            debugOverlay.SetChunkGridVisible(true);
            Assert.That(debugOverlay.IsChunkGridVisible, Is.True);

            // 開発者用表示はキーではなくEditorメニューと同じ公開入口から切り替え、F3/F4の状態へ影響しないことを確認します。
            Assert.That(
                developerVisualization.CurrentMode,
                Is.EqualTo(WorldGenerationDebugVisualizationMode.None));
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.Biome);
            Assert.That(
                developerVisualization.CurrentMode,
                Is.EqualTo(WorldGenerationDebugVisualizationMode.Biome));
            Assert.That(debugOverlay.IsCoordinateHudVisible, Is.True);
            Assert.That(debugOverlay.IsChunkGridVisible, Is.True);

            // 1描画フレームだけ両方を有効にし、GUI Style作成、座標変換、タイル・サブチャンク・チャンク線とラベル描画まで例外なく通します。
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);
            debugOverlay.SetCoordinateHudVisible(false);
            debugOverlay.SetChunkGridVisible(false);

            World world = null;
            EntityQuery settingsQuery = default;
            EntityQuery locationQuery = default;
            EntityQuery chunkQuery = default;
            bool queriesCreated = false;
            bool initialLoadCompleted = false;
            Entity originalChunkEntity = Entity.Null;
            Vector3Int originalResourceCell = default;
            InitialSpawnLocation initialSpawnLocation = default;
            ChunkCoordinate initialSpawnChunk = default;
            Vector2 initialCameraWorldPosition = default;

            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated && !queriesCreated)
                {
                    settingsQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<WorldGenerationSettings>(),
                        ComponentType.ReadOnly<ResourceGenerationSettings>(),
                        ComponentType.ReadOnly<ChunkStreamingSettings>());
                    locationQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<InitialSpawnLocation>());
                    chunkQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                        ComponentType.ReadOnly<ChunkGenerationStatus>(),
                        ComponentType.ReadOnly<GeneratedTile>(),
                        ComponentType.ReadOnly<GeneratedResourceTile>());
                    queriesCreated = true;
                }

                // 設定、初期スポーン記録、カメラ移動、スポーン先チャンク、地形・資源Tileがそろうまで固定秒数に依存せず待ちます。
                if (queriesCreated
                    && settingsQuery.CalculateEntityCount() == 1
                    && locationQuery.CalculateEntityCount() == 1)
                {
                    initialSpawnLocation = locationQuery.GetSingleton<InitialSpawnLocation>();
                    initialSpawnChunk = WorldGrid.ToChunkCoordinate(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY);
                    initialCameraWorldPosition = new Vector2(
                        initialSpawnLocation.TileX + 0.5f,
                        initialSpawnLocation.TileY + 0.5f);
                    Vector3Int spawnCell = new Vector3Int(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY,
                        0);

                    if (initialPositionController.HasAppliedInitialPosition
                        && TryGetActiveCompleteChunk(
                            world.EntityManager,
                            chunkQuery,
                            initialSpawnChunk.X,
                            initialSpawnChunk.Y,
                            out originalChunkEntity)
                        && tilemap.GetTile(spawnCell) != null
                        && TryGetDisplayedResourceCell(
                            world.EntityManager,
                            originalChunkEntity,
                            initialSpawnChunk,
                            resourceTilemap,
                            resourceBehindRockWallTilemap,
                            out originalResourceCell))
                    {
                        initialLoadCompleted = true;
                        break;
                    }
                }

                yield return null;
            }

            Assert.That(
                initialLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に初回生成と表示が完了しませんでした。");

            // 一体型岩壁が存在するセルを実際の表示結果から探し、同じ論理セルに土地ブロックも残る二段構成を保証します。
            // TransformとPivotだけを検査すると、Presenterが岩壁を書き忘れてもテストが通るため、Tilemapの内容まで確認します。
            bool foundDisplayedRockWall = false;
            foreach (Vector3Int cellPosition in rockWallTilemap.cellBounds.allPositionsWithin)
            {
                if (!rockWallTilemap.HasTile(cellPosition))
                {
                    continue;
                }

                foundDisplayedRockWall = true;
                Assert.That(
                    tilemap.HasTile(cellPosition),
                    Is.True,
                    $"岩壁セル {cellPosition} の下に、Biome由来の一体型土地ブロックが残っていません。");
            }

            Assert.That(
                foundDisplayedRockWall,
                Is.True,
                "初回表示範囲に一体型岩壁が1枚もなく、二段構成を実表示で検証できませんでした。");
            Assert.That(
                cameraPanController.transform.position.x,
                Is.EqualTo(initialCameraWorldPosition.x).Within(0.00001f),
                "カメラX座標が記録済み初期スポーンタイル中央と一致しません。");
            Assert.That(
                cameraPanController.transform.position.y,
                Is.EqualTo(initialCameraWorldPosition.y).Within(0.00001f),
                "カメラY座標が記録済み初期スポーンタイル中央と一致しません。");

            // 初期移動後も通常カメラ操作は16 PPUへ丸まり、テスト後は記録済みスポーンへ正確に戻せることを確認します。
            cameraPanController.SetWorldPosition(
                initialCameraWorldPosition + new Vector2(0.1f, -0.1f));
            Assert.That(
                cameraPanController.transform.position.x,
                Is.EqualTo(initialCameraWorldPosition.x + 0.125f).Within(0.00001f));
            Assert.That(
                cameraPanController.transform.position.y,
                Is.EqualTo(initialCameraWorldPosition.y - 0.125f).Within(0.00001f));
            cameraPanController.SetWorldPosition(initialCameraWorldPosition);

            // 正式なセーブ互換運用前は生成調整のたびに番号を増やさず、Sceneの初期バージョンを1へ固定します。
            WorldGenerationSettings runtimeGenerationSettings =
                settingsQuery.GetSingleton<WorldGenerationSettings>();
            Assert.That(
                runtimeGenerationSettings.GeneratorVersion,
                Is.EqualTo(1),
                "初期開発中のGeneratorVersionは1へ固定してください。");

            // 生成済みタイルを使う移行帯表示を1フレーム描画し、ECS Buffer読取、Windowboxクリップ、情報HUDまで例外なく通します。
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.BiomeTransition);
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);

            DynamicBuffer<GeneratedTile> originalTiles =
                world.EntityManager.GetBuffer<GeneratedTile>(originalChunkEntity, true);
            DynamicBuffer<GeneratedResourceTile> originalResources =
                world.EntityManager.GetBuffer<GeneratedResourceTile>(originalChunkEntity, true);
            GeneratedTile originalFirstTile = originalTiles[0];
            GeneratedTile originalCenterTile = originalTiles[WorldGridConstants.TilesPerChunk / 2];
            GeneratedTile originalLastTile = originalTiles[WorldGridConstants.TilesPerChunk - 1];
            GeneratedResourceTile originalFirstResource = originalResources[0];
            GeneratedResourceTile originalCenterResource =
                originalResources[WorldGridConstants.TilesPerChunk / 2];
            GeneratedResourceTile originalLastResource =
                originalResources[WorldGridConstants.TilesPerChunk - 1];

            // 初期スポーンから8チャンク東へ即時移動し、開始チャンクがRetention範囲外になってデータと表示から消えるのを待ちます。
            Vector2 farCameraWorldPosition = initialCameraWorldPosition
                + new Vector2(FarCameraOffsetInTiles, 0f);
            ChunkCoordinate farCameraChunk = WorldGrid.ToChunkCoordinate(
                Mathf.FloorToInt(farCameraWorldPosition.x),
                Mathf.FloorToInt(farCameraWorldPosition.y));
            cameraPanController.SetWorldPosition(farCameraWorldPosition);
            bool farLoadCompleted = false;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                bool originalChunkWasUnloaded = !HasChunkAt(
                    chunkQuery,
                    initialSpawnChunk.X,
                    initialSpawnChunk.Y);
                bool originalTileWasCleared = tilemap.GetTile(
                    new Vector3Int(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY,
                        0)) == null;
                bool originalResourceWasCleared =
                    resourceTilemap.GetTile(originalResourceCell) == null
                    && resourceBehindRockWallTilemap.GetTile(originalResourceCell) == null;
                bool farChunkIsActive = TryGetActiveCompleteChunk(
                    world.EntityManager,
                    chunkQuery,
                    farCameraChunk.X,
                    farCameraChunk.Y,
                    out _);

                if (originalChunkWasUnloaded
                    && originalTileWasCleared
                    && originalResourceWasCleared
                    && farChunkIsActive)
                {
                    farLoadCompleted = true;
                    break;
                }

                yield return null;
            }

            Assert.That(
                farLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に遠方チャンクへの切替と原点のアンロードが完了しませんでした。");

            // 記録済みスポーンへ戻り、破棄済みEntityに依存せず同じシードから同一タイルデータを再生成できることを確認します。
            cameraPanController.SetWorldPosition(initialCameraWorldPosition);
            bool returnLoadCompleted = false;
            Entity regeneratedChunkEntity = Entity.Null;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                if (TryGetActiveCompleteChunk(
                        world.EntityManager,
                        chunkQuery,
                        initialSpawnChunk.X,
                        initialSpawnChunk.Y,
                        out regeneratedChunkEntity)
                    && tilemap.GetTile(
                        new Vector3Int(
                            initialSpawnLocation.TileX,
                            initialSpawnLocation.TileY,
                            0)) != null)
                {
                    returnLoadCompleted = true;
                    break;
                }

                yield return null;
            }

            Assert.That(
                returnLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に原点チャンクの再生成と再表示が完了しませんでした。");
            Assert.That(
                regeneratedChunkEntity,
                Is.Not.EqualTo(originalChunkEntity),
                "初期スポーンチャンクは遠方移動時に破棄され、別Entityとして再生成される必要があります。");

            DynamicBuffer<GeneratedTile> regeneratedTiles =
                world.EntityManager.GetBuffer<GeneratedTile>(regeneratedChunkEntity, true);
            DynamicBuffer<GeneratedResourceTile> regeneratedResources =
                world.EntityManager.GetBuffer<GeneratedResourceTile>(regeneratedChunkEntity, true);
            AssertTileEquals(originalFirstTile, regeneratedTiles[0], 0);
            AssertTileEquals(
                originalCenterTile,
                regeneratedTiles[WorldGridConstants.TilesPerChunk / 2],
                WorldGridConstants.TilesPerChunk / 2);
            AssertTileEquals(
                originalLastTile,
                regeneratedTiles[WorldGridConstants.TilesPerChunk - 1],
                WorldGridConstants.TilesPerChunk - 1);
            AssertResourceEquals(originalFirstResource, regeneratedResources[0], 0);
            AssertResourceEquals(
                originalCenterResource,
                regeneratedResources[WorldGridConstants.TilesPerChunk / 2],
                WorldGridConstants.TilesPerChunk / 2);
            AssertResourceEquals(
                originalLastResource,
                regeneratedResources[WorldGridConstants.TilesPerChunk - 1],
                WorldGridConstants.TilesPerChunk - 1);
            Assert.That(
                resourceTilemap.GetTile(originalResourceCell) != null
                    || resourceBehindRockWallTilemap.GetTile(originalResourceCell) != null,
                Is.True,
                "原点へ戻った後、決定論的に再生成された資源アイコンが再表示される必要があります。");

            // 初期スポーン周辺は安全な陸地として選ばれるため、水が偶然あることへ依存せず、生成規則から液体セルを探して表示先へ移動します。
            bool foundLiquidCell = TryFindLiquidCell(
                runtimeGenerationSettings,
                initialSpawnLocation.TileX,
                initialSpawnLocation.TileY,
                out Vector3Int liquidCell);
            Assert.That(
                foundLiquidCell,
                Is.True,
                "初期スポーンから2048タイル以内に、液面レイヤーを実表示検証できる水・マグマが見つかりませんでした。");

            cameraPanController.SetWorldPosition(
                new Vector2(liquidCell.x + 0.5f, liquidCell.y + 0.5f));
            bool liquidDisplayCompleted = false;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                // 静止面とアニメーションが同じ論理セルへそろって初めて、実際の液面表示が完成したと判断します。
                if (liquidBaseTilemap.HasTile(liquidCell)
                    && liquidAnimationTilemap.HasTile(liquidCell))
                {
                    liquidDisplayCompleted = true;
                    break;
                }

                yield return null;
            }

            Assert.That(
                liquidDisplayCompleted,
                Is.True,
                $"液体セル {liquidCell} の静止面とアニメーションが{MaximumWaitFrames}フレーム以内に表示されませんでした。");
            Assert.That(
                tilemap.HasTile(liquidCell),
                Is.False,
                $"液体セル {liquidCell} がGround Tilemapにも重複し、元の高さの液面が残っています。");

            if (queriesCreated && world != null && world.IsCreated)
            {
                settingsQuery.Dispose();
                locationQuery.Dispose();
                chunkQuery.Dispose();
            }
        }

        /// <summary>
        /// 指定座標のチャンクがQuery内に存在するかを、生成段階に関係なく確認します。
        /// </summary>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">検索する水平論理チャンク座標です。</param>
        /// <param name="chunkY">検索する垂直論理チャンク座標です。</param>
        /// <returns>指定座標を持つEntityが1つ以上あればtrueです。</returns>
        private static bool HasChunkAt(EntityQuery chunkQuery, int chunkX, int chunkY)
        {
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);

            for (int index = 0; index < coordinates.Length; index++)
            {
                if (coordinates[index].X == chunkX && coordinates[index].Y == chunkY)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 初期スポーンを中心とする一定範囲を粗く走査し、実表示テストに使う水またはマグマの論理セルを決定論的に探します。
        /// </summary>
        /// <param name="settings">実行中Worldが使用している地形生成設定です。</param>
        /// <param name="centerTileX">探索中心となる水平ワールドタイル座標です。</param>
        /// <param name="centerTileY">探索中心となる垂直ワールドタイル座標です。</param>
        /// <param name="liquidCell">成功時に、見つかった液体セルのワールド座標を返します。</param>
        /// <returns>探索範囲内に水またはマグマが見つかった場合はtrueです。</returns>
        private static bool TryFindLiquidCell(
            in WorldGenerationSettings settings,
            int centerTileX,
            int centerTileY,
            out Vector3Int liquidCell)
        {
            // 無限ワールド全体を走査せず、32タイル刻みで最大2048タイル四方を調べれば、テスト時間を抑えつつ低地を確実に探せます。
            const int searchRadiusInTiles = 2048;
            const int searchStrideInTiles = 32;
            for (int offsetY = -searchRadiusInTiles;
                 offsetY <= searchRadiusInTiles;
                 offsetY += searchStrideInTiles)
            {
                for (int offsetX = -searchRadiusInTiles;
                     offsetX <= searchRadiusInTiles;
                     offsetX += searchStrideInTiles)
                {
                    int tileX = centerTileX + offsetX;
                    int tileY = centerTileY + offsetY;
                    GeneratedTile generatedTile =
                        DeterministicChunkGenerator.GenerateTile(settings, tileX, tileY);
                    if (generatedTile.Ground != GroundType.Water
                        && generatedTile.Ground != GroundType.Magma)
                    {
                        continue;
                    }

                    liquidCell = new Vector3Int(tileX, tileY, 0);
                    return true;
                }
            }

            liquidCell = default;
            return false;
        }

        /// <summary>
        /// 指定座標のチャンクを調べ、Active状態かつ地形・資源を各4096件保持するEntityを取得します。
        /// </summary>
        /// <param name="entityManager">Default WorldのEntityManagerです。</param>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">検索する水平論理チャンク座標です。</param>
        /// <param name="chunkY">検索する垂直論理チャンク座標です。</param>
        /// <param name="chunkEntity">成功時に、完全生成されたチャンクEntityを返します。</param>
        /// <returns>完全生成され表示対象になった指定座標のチャンクが見つかればtrueです。</returns>
        private static bool TryGetActiveCompleteChunk(
            EntityManager entityManager,
            EntityQuery chunkQuery,
            int chunkX,
            int chunkY,
            out Entity chunkEntity)
        {
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);

            for (int index = 0; index < entities.Length; index++)
            {
                if (coordinates[index].X != chunkX
                    || coordinates[index].Y != chunkY
                    || statuses[index].Phase != ChunkGenerationPhase.Active)
                {
                    continue;
                }

                DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(entities[index], true);
                DynamicBuffer<GeneratedResourceTile> resources =
                    entityManager.GetBuffer<GeneratedResourceTile>(entities[index], true);
                if (tiles.Length == WorldGridConstants.TilesPerChunk
                    && resources.Length == WorldGridConstants.TilesPerChunk)
                {
                    chunkEntity = entities[index];
                    return true;
                }
            }

            chunkEntity = Entity.Null;
            return false;
        }

        /// <summary>
        /// 指定チャンクの資源Bufferから最初の配置済みセルを探し、前面または岩壁背面のResource Tilemapへ表示されたか確認します。
        /// </summary>
        /// <param name="entityManager">Default WorldのEntityManagerです。</param>
        /// <param name="chunkEntity">検査対象チャンクを表す完全生成済みEntityです。</param>
        /// <param name="chunkCoordinate">ローカルBuffer Indexをワールドセルへ戻す論理チャンク座標です。</param>
        /// <param name="resourceTilemap">岩壁に隠れない資源を重ね描画するTilemapです。</param>
        /// <param name="resourceBehindRockWallTilemap">直前方の岩壁に隠す資源を描画するTilemapです。</param>
        /// <param name="resourceCell">成功時に、表示済み資源のワールドセル座標を返します。</param>
        /// <returns>配置済み資源が1件以上あり、そのTileが表示済みならtrueです。</returns>
        private static bool TryGetDisplayedResourceCell(
            EntityManager entityManager,
            Entity chunkEntity,
            ChunkCoordinate chunkCoordinate,
            Tilemap resourceTilemap,
            Tilemap resourceBehindRockWallTilemap,
            out Vector3Int resourceCell)
        {
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity, true);

            // Bufferは行優先なので、Indexを64で割った商と余りから原点チャンク内座標へ戻します。
            for (int tileIndex = 0; tileIndex < resources.Length; tileIndex++)
            {
                if (resources[tileIndex].Type == GeneratedResourceType.None)
                {
                    continue;
                }

                int localX = tileIndex % WorldGridConstants.LogicalChunkSize;
                int localY = tileIndex / WorldGridConstants.LogicalChunkSize;
                resourceCell = new Vector3Int(
                    WorldGrid.ToWorldTileX(chunkCoordinate.X, localX),
                    WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY),
                    0);
                return resourceTilemap.GetTile(resourceCell) != null
                    || resourceBehindRockWallTilemap.GetTile(resourceCell) != null;
            }

            resourceCell = default;
            return false;
        }

        /// <summary>
        /// 再生成前後の1タイルについて、表示分類だけでなくゲームルールが参照する全フィールドの一致を保証します。
        /// </summary>
        /// <param name="expected">遠方移動前に原点チャンクから退避したタイル値です。</param>
        /// <param name="actual">原点へ戻った後に再生成されたタイル値です。</param>
        /// <param name="tileIndex">失敗メッセージへ表示するチャンク内の行優先Indexです。</param>
        private static void AssertTileEquals(GeneratedTile expected, GeneratedTile actual, int tileIndex)
        {
            Assert.That(actual.Biome, Is.EqualTo(expected.Biome), $"Biomeが一致しません。Index={tileIndex}");
            Assert.That(actual.Ground, Is.EqualTo(expected.Ground), $"Groundが一致しません。Index={tileIndex}");
            Assert.That(
                actual.WaterSalinity,
                Is.EqualTo(expected.WaterSalinity),
                $"WaterSalinityが一致しません。Index={tileIndex}");
            Assert.That(
                actual.WaterBodyType,
                Is.EqualTo(expected.WaterBodyType),
                $"WaterBodyTypeが一致しません。Index={tileIndex}");
            Assert.That(
                actual.ElevationClass,
                Is.EqualTo(expected.ElevationClass),
                $"ElevationClassが一致しません。Index={tileIndex}");
            Assert.That(
                actual.GameplayFlags,
                Is.EqualTo(expected.GameplayFlags),
                $"GameplayFlagsが一致しません。Index={tileIndex}");
            Assert.That(
                actual.ResourceSuitability,
                Is.EqualTo(expected.ResourceSuitability),
                $"ResourceSuitabilityが一致しません。Index={tileIndex}");
            Assert.That(
                actual.MovementCost,
                Is.EqualTo(expected.MovementCost),
                $"MovementCostが一致しません。Index={tileIndex}");
            Assert.That(actual.Elevation, Is.EqualTo(expected.Elevation), $"Elevationが一致しません。Index={tileIndex}");
            Assert.That(actual.Temperature, Is.EqualTo(expected.Temperature), $"Temperatureが一致しません。Index={tileIndex}");
            Assert.That(actual.Moisture, Is.EqualTo(expected.Moisture), $"Moistureが一致しません。Index={tileIndex}");
            Assert.That(actual.Weirdness, Is.EqualTo(expected.Weirdness), $"Weirdnessが一致しません。Index={tileIndex}");
        }

        /// <summary>
        /// 再生成前後の資源IDと表示差分を比較し、地形と同じ決定論が維持されていることを保証します。
        /// </summary>
        /// <param name="expected">遠方移動前に退避した資源データです。</param>
        /// <param name="actual">原点へ戻った後に再生成された資源データです。</param>
        /// <param name="tileIndex">失敗メッセージへ表示するチャンク内の行優先Indexです。</param>
        private static void AssertResourceEquals(
            GeneratedResourceTile expected,
            GeneratedResourceTile actual,
            int tileIndex)
        {
            Assert.That(actual.Type, Is.EqualTo(expected.Type), $"資源IDが一致しません。Index={tileIndex}");
            Assert.That(
                actual.VisualVariant,
                Is.EqualTo(expected.VisualVariant),
                $"資源表示差分が一致しません。Index={tileIndex}");
        }
    }
}
