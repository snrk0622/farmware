// このファイルは、広域マップビューアが実ゲームと同じ座標・設定・Generator結果を表示することをEditModeで検証します。
// EditorWindowそのものを開かず、バックグラウンド化できる純粋サンプラーとAuthoring設定変換を対象にします。

using System.Reflection;
using System.Threading;
using Farmware.Simulation;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// マップビューア用の代表座標、色変換、Authoring共有設定が実際の地形生成と一致することを保証します。
    /// </summary>
    public sealed class WorldGenerationMapViewerTests
    {
        /// <summary>
        /// 1つのRoot配下の兄弟GameObjectへAuthoringを2件配置し、設定取得が重複を見逃さないことを保証します。
        /// Bakerが複数の設定Entityを生成するSceneをMap Viewerだけが正常と誤認する退行を防ぎます。
        /// </summary>
        [Test]
        public void TryFindSingleAuthoring_SameRootDuplicatesAreRejected()
        {
            GameObject root = new GameObject("Duplicate Authoring Root");

            try
            {
                GameObject firstObject = new GameObject("First Authoring");
                firstObject.transform.SetParent(root.transform);
                firstObject.AddComponent<WorldGenerationAuthoring>();
                GameObject secondObject = new GameObject("Second Authoring");
                secondObject.transform.SetParent(root.transform);
                WorldGenerationAuthoring secondAuthoring =
                    secondObject.AddComponent<WorldGenerationAuthoring>();

                MethodInfo tryFindSingleAuthoring =
                    typeof(WorldGenerationEditorSettingsProvider).GetMethod(
                        "TryFindSingleAuthoring",
                        BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(tryFindSingleAuthoring, Is.Not.Null);

                object[] arguments =
                {
                    new[] { root },
                    null
                };
                bool succeeded = (bool)tryFindSingleAuthoring.Invoke(null, arguments);

                Assert.That(succeeded, Is.False);
                Assert.That(
                    arguments[1],
                    Is.SameAs(secondAuthoring),
                    "複数時は0件と区別できるよう、検出した2件目を呼び出し側へ返す必要があります。");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 小さな表示範囲を生成し、全ピクセルが同じ座標へ直接GenerateTileした結果の色と一致することを検証します。
        /// ビューア専用の簡易バイオーム計算を追加して実マップとずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void Sample_EveryPixelMatchesDirectDeterministicGeneratorResult()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("map-viewer-shared-generator-test"),
                1);
            WorldGenerationMapSampleResult result = WorldGenerationMapSampler.Sample(
                settings,
                -65,
                130,
                7,
                5,
                8,
                WorldGenerationMapViewMode.Terrain,
                CancellationToken.None);

            // 全ピクセルを直接Generator結果と比較し、座標変換・配列順・色変換をまとめて保証します。
            for (int pixelY = 0; pixelY < result.Height; pixelY++)
            {
                for (int pixelX = 0; pixelX < result.Width; pixelX++)
                {
                    result.GetSampledWorldTile(
                        pixelX,
                        pixelY,
                        out int worldTileX,
                        out int worldTileY);
                    GeneratedTile directTile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        worldTileX,
                        worldTileY);
                    Color32 expectedColor = WorldGenerationMapSampler.GetDisplayColor(
                        WorldGenerationMapViewMode.Terrain,
                        directTile);

                    Assert.That(
                        result.PixelColors[(pixelY * result.Width) + pixelX],
                        Is.EqualTo(expectedColor),
                        $"pixel=({pixelX}, {pixelY}), tile=({worldTileX}, {worldTileY})");
                }
            }
        }

        /// <summary>
        /// 未接続の海中レイヤーを要求し、地上Generatorを複製せず、レイヤー付きの未生成結果を返すことを保証します。
        /// </summary>
        [Test]
        public void Sample_Underwater_ReturnsLayeredUnavailableResult()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("map-viewer-underwater-placeholder-test"),
                1);
            WorldGenerationMapSampleResult result = WorldGenerationMapSampler.Sample(
                settings,
                WorldLayer.Underwater,
                12,
                -34,
                3,
                2,
                4,
                WorldGenerationMapViewMode.Terrain,
                CancellationToken.None);

            Assert.That(result.Layer, Is.EqualTo(WorldLayer.Underwater));
            Assert.That(result.HasGeneratedData, Is.False);
            Assert.That(result.PixelColors, Has.Length.EqualTo(6));
            Assert.That(result.PixelColors[0], Is.EqualTo(new Color32(24, 28, 36, 255)));
            Assert.That(result.PixelColors[5], Is.EqualTo(result.PixelColors[0]));
        }

        /// <summary>
        /// 保存予約値NoneをMap Viewerへ渡し、存在する層のような未接続画像を作らず明示的に拒否することを保証します。
        /// </summary>
        [Test]
        public void Sample_InvalidLayer_IsRejected()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("map-viewer-invalid-layer-test"),
                1);

            Assert.That(
                () => WorldGenerationMapSampler.Sample(
                    settings,
                    WorldLayer.None,
                    0,
                    0,
                    1,
                    1,
                    1,
                    WorldGenerationMapViewMode.Terrain,
                    CancellationToken.None),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Map ViewerのUXMLを読み、WORLD-01Cで追加したレイヤー選択欄が名前付き要素として取得できることを保証します。
        /// </summary>
        [Test]
        public void MapViewerUxml_ContainsLayerSelector()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/Farmware/Editor/WorldGeneration/WorldGenerationMapViewerWindow.uxml");
            Assert.That(tree, Is.Not.Null);
            VisualElement root = tree.CloneTree();
            DropdownField layerField = root.Q<DropdownField>("layerField");

            Assert.That(layerField, Is.Not.Null);
        }

        /// <summary>
        /// 同じ論理Biomeへ3種類のSoilTypeを設定しても、TerrainとBiomeの両表示が所属色だけを維持することを保証します。
        /// </summary>
        [Test]
        public void DisplayColor_PlayerTerrainIgnoresSoilAndKeepsBiomeIdentity()
        {
            GeneratedTile forestNormal = new GeneratedTile
            {
                Biome = BiomeId.Forest,
                Ground = GroundType.Land,
                Soil = SoilType.Normal
            };
            GeneratedTile forestFertile = forestNormal;
            forestFertile.Soil = SoilType.Fertile;
            GeneratedTile forestSandy = forestNormal;
            forestSandy.Soil = SoilType.Sandy;

            Color32 terrainNormal = WorldGenerationMapSampler.GetDisplayColor(
                WorldGenerationMapViewMode.Terrain,
                forestNormal);
            Color32 terrainFertile = WorldGenerationMapSampler.GetDisplayColor(
                WorldGenerationMapViewMode.Terrain,
                forestFertile);
            Color32 terrainSandy = WorldGenerationMapSampler.GetDisplayColor(
                WorldGenerationMapViewMode.Terrain,
                forestSandy);
            Color32 biomeNormal = WorldGenerationMapSampler.GetDisplayColor(
                WorldGenerationMapViewMode.Biome,
                forestNormal);
            Color32 biomeFertile = WorldGenerationMapSampler.GetDisplayColor(
                WorldGenerationMapViewMode.Biome,
                forestFertile);

            Assert.That(
                terrainNormal,
                Is.EqualTo(terrainFertile),
                "プレイヤー向けTerrain表示へ肥沃土の違いを反映してはいけません。");
            Assert.That(
                terrainSandy,
                Is.EqualTo(terrainNormal),
                "プレイヤー向けTerrain表示へ砂質土の違いを反映してはいけません。");
            Assert.That(
                biomeNormal,
                Is.EqualTo(biomeFertile),
                "Biome表示はSoilに左右されず、両方を同じ森林として表示する必要があります。");
        }

        /// <summary>
        /// 左上原点のPointer座標を実際のTextureピクセルへ変換し、負座標とY反転を含む代表タイルへ一致することを検証します。
        /// </summary>
        [Test]
        public void CalculateWorldTileAtPointer_CenterAndCornersUseExpectedAxes()
        {
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                -100,
                200,
                4,
                400,
                300,
                400f,
                300f,
                200.1f,
                149.9f,
                out int centerX,
                out int centerY);
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                -100,
                200,
                4,
                400,
                300,
                400f,
                300f,
                0f,
                0f,
                out int topLeftX,
                out int topLeftY);

            Assert.That(centerX, Is.EqualTo(-100));
            Assert.That(centerY, Is.EqualTo(200));
            Assert.That(topLeftX, Is.EqualTo(-900));
            Assert.That(topLeftY, Is.EqualTo(796));
        }

        /// <summary>
        /// 同じ表示ピクセルの前半と後半へPointerを置き、連続座標の小数部が変わっても同じ代表タイルを返すことを保証します。
        /// Point Filterの表示色とHover情報・ズーム固定点が、ピクセル途中で隣のタイルへずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void CalculateWorldTileAtPointer_PositionsWithinSameDisplayedPixelReturnSameTile()
        {
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                10,
                -20,
                1,
                400,
                300,
                400f,
                300f,
                200.1f,
                149.1f,
                out int firstX,
                out int firstY);
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                10,
                -20,
                1,
                400,
                300,
                400f,
                300f,
                200.9f,
                149.9f,
                out int secondX,
                out int secondY);

            Assert.That(firstX, Is.EqualTo(10));
            Assert.That(firstY, Is.EqualTo(-20));
            Assert.That(secondX, Is.EqualTo(firstX));
            Assert.That(secondY, Is.EqualTo(firstY));
        }

        /// <summary>
        /// 偶数寸法の生成結果をEditorWindowへ適用し、Pointerが動く前のHoverが要求した中心タイルを示すことを保証します。
        /// Canvas中央の境界を通常Pointer変換へ通し、Y座標が1サンプル下へずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void ApplyRenderedMap_EvenDimensionsInitialHoverUsesRequestedCenterTile()
        {
            const int centerTileX = 123;
            const int centerTileY = -456;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("map-viewer-initial-hover-test"),
                1);
            WorldGenerationMapSampleResult result = WorldGenerationMapSampler.Sample(
                settings,
                centerTileX,
                centerTileY,
                4,
                2,
                4,
                WorldGenerationMapViewMode.Terrain,
                CancellationToken.None);
            WorldGenerationMapViewerWindow window =
                ScriptableObject.CreateInstance<WorldGenerationMapViewerWindow>();

            try
            {
                Label hoverLabel = new Label();
                SetPrivateField(window, "mapCanvas", new WorldGenerationMapCanvas());
                SetPrivateField(window, "hoverLabel", hoverLabel);
                SetPrivateField(window, "zoomLabel", new Label());
                SetPrivateField(window, "sourceLabel", new Label());
                SetPrivateField(window, "settingsSourceDescription", "EditMode回帰テスト");

                MethodInfo applyRenderedMap = typeof(WorldGenerationMapViewerWindow).GetMethod(
                    "ApplyRenderedMap",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(applyRenderedMap, Is.Not.Null);

                // contentRectが未レイアウトの0でも、初期HoverはPointer変換へ依存せず中心を評価する必要があります。
                applyRenderedMap.Invoke(window, new object[] { result, settings });

                Assert.That(
                    hoverLabel.text,
                    Does.StartWith($"Surface / Tile ({centerTileX}, {centerTileY}) /"));
            }
            finally
            {
                // EditorWindowが所有する一時Textureとイベント購読をOnDisable経由で確実に破棄します。
                Object.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// カーソル中心ズーム後も、そのPointerに表示されるTextureピクセルが同じ代表タイルを指すことを検証します。
        /// </summary>
        [Test]
        public void CalculateZoomedCenter_PreservesWorldPositionUnderPointer()
        {
            const float pointerX = 310f;
            const float pointerY = 70f;
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                50,
                -75,
                8,
                400,
                300,
                400f,
                300f,
                pointerX,
                pointerY,
                out int beforeX,
                out int beforeY);

            WorldGenerationMapSampler.CalculateZoomedCenter(
                50,
                -75,
                8,
                4,
                400,
                300,
                400,
                300,
                400f,
                300f,
                pointerX,
                pointerY,
                out int newCenterX,
                out int newCenterY);
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                newCenterX,
                newCenterY,
                4,
                400,
                300,
                400f,
                300f,
                pointerX,
                pointerY,
                out int afterX,
                out int afterY);

            Assert.That(afterX, Is.EqualTo(beforeX));
            Assert.That(afterY, Is.EqualTo(beforeY));
        }

        /// <summary>
        /// ツールバーの＋・－Button用計算へ任意の中心座標を渡し、縮尺だけが変わって中心は維持されることを保証します。
        /// 偶数解像度のCanvas中央をPointerとして解釈し、上下左右の片側へ中心がずれる退行を防ぎます。
        /// </summary>
        /// <param name="zoomOut">広域側へ変更する場合はtrueです。</param>
        /// <param name="expectedTilesPerPixel">変更後に期待する1サンプルあたりタイル数です。</param>
        [TestCase(false, 2)]
        [TestCase(true, 8)]
        public void CalculateToolbarZoomState_PreservesCenterCoordinates(
            bool zoomOut,
            int expectedTilesPerPixel)
        {
            MethodInfo calculateToolbarZoomState = typeof(WorldGenerationMapViewerWindow).GetMethod(
                "CalculateToolbarZoomState",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(calculateToolbarZoomState, Is.Not.Null);

            // out引数もReflectionの引数配列へ結果が戻るため、呼び出し後に中心と縮尺を個別に検証します。
            object[] arguments = { 123, -456, 4, zoomOut, 0, 0, 0 };
            calculateToolbarZoomState.Invoke(null, arguments);

            Assert.That((int)arguments[4], Is.EqualTo(123));
            Assert.That((int)arguments[5], Is.EqualTo(-456));
            Assert.That((int)arguments[6], Is.EqualTo(expectedTilesPerPixel));
        }

        /// <summary>
        /// 768サンプルへ制限したTextureを大きなCanvasへ伸張し、Pointer座標が表示上の地形と一致することを検証します。
        /// 縦横で異なる倍率を使い、X側の上限だけが適用されるWindow形状でも誤変換を防ぎます。
        /// </summary>
        [Test]
        public void CalculateWorldTileAtPointer_StretchedTextureUsesSampleResolution()
        {
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                100,
                -200,
                4,
                768,
                576,
                1200f,
                900f,
                900f,
                225f,
                out int worldTileX,
                out int worldTileY);

            Assert.That(worldTileX, Is.EqualTo(868));
            Assert.That(worldTileY, Is.EqualTo(372));
        }

        /// <summary>
        /// 横長・縦長・上限以内のCanvasを準備し、上限768サンプル内で縦横比を保つことを検証します。
        /// 長軸だけを制限して正方形のタイルとグリッドが長方形へ歪む退行を防ぎます。
        /// </summary>
        /// <param name="viewportWidth">検証するCanvasの横幅です。</param>
        /// <param name="viewportHeight">検証するCanvasの縦幅です。</param>
        /// <param name="expectedWidth">期待するTextureの横サンプル数です。</param>
        /// <param name="expectedHeight">期待するTextureの縦サンプル数です。</param>
        [TestCase(1200f, 600f, 768, 384)]
        [TestCase(600f, 1200f, 384, 768)]
        [TestCase(640f, 480f, 640, 480)]
        public void CalculateSampleResolution_PreservesViewportAspectRatio(
            float viewportWidth,
            float viewportHeight,
            int expectedWidth,
            int expectedHeight)
        {
            WorldGenerationMapSampler.CalculateSampleResolution(
                viewportWidth,
                viewportHeight,
                out int sampleWidth,
                out int sampleHeight);

            Assert.That(sampleWidth, Is.EqualTo(expectedWidth));
            Assert.That(sampleHeight, Is.EqualTo(expectedHeight));
        }

        /// <summary>
        /// Windowリサイズ前後のTexture解像度を準備し、縦横比が合わない古いTextureだけを再生成対象と判定することを検証します。
        /// 同じ比率の大きさ変更で不要な生成を繰り返さず、横長・縦長の切り替えは見逃さないことを保証します。
        /// </summary>
        /// <param name="sampleWidth">表示中Textureの横サンプル数です。</param>
        /// <param name="sampleHeight">表示中Textureの縦サンプル数です。</param>
        /// <param name="viewportWidth">リサイズ後Canvasの横幅です。</param>
        /// <param name="viewportHeight">リサイズ後Canvasの縦幅です。</param>
        /// <param name="expected">表示中Textureをそのまま利用できる場合はtrueです。</param>
        [TestCase(768, 576, 1200f, 900f, true)]
        [TestCase(768, 600, 1200f, 900f, false)]
        [TestCase(768, 384, 600f, 1200f, false)]
        public void DoesSampleResolutionMatchViewport_DetectsResizeThatNeedsRegeneration(
            int sampleWidth,
            int sampleHeight,
            float viewportWidth,
            float viewportHeight,
            bool expected)
        {
            bool actual = WorldGenerationMapSampler.DoesSampleResolutionMatchViewport(
                sampleWidth,
                sampleHeight,
                viewportWidth,
                viewportHeight);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 拡大表示中のドラッグ量とグリッドの逆変換を準備し、同じ地形上の移動量を往復できることを検証します。
        /// CanvasポイントをTextureサンプルと誤認し、パン量や境界線がずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void ViewportAndTileOffsets_StretchedTextureRoundTrip()
        {
            long tileOffset = WorldGenerationMapSampler.CalculateTileOffsetFromViewportDelta(
                300f,
                1200f,
                768,
                4);
            float viewportOffset =
                WorldGenerationMapSampler.CalculateViewportOffsetFromTileOffset(
                    tileOffset,
                    4,
                    768,
                    1200f);

            Assert.That(tileOffset, Is.EqualTo(768));
            Assert.That(viewportOffset, Is.EqualTo(300f).Within(0.0001f));
        }

        /// <summary>
        /// 奇数・偶数のTextureと伸張Canvasを準備し、グリッドと原点が地形生成と同じ整数中央の境界へ揃うことを検証します。
        /// Y軸はTexture下端原点からCanvas上端原点へ反転するため、奇数解像度ではXと1サンプル異なる位置になります。
        /// </summary>
        /// <param name="centerTileCoordinate">Texture中央サンプルへ割り当てるワールドタイル座標です。</param>
        /// <param name="worldBoundaryCoordinate">Canvas位置を求めるワールドタイル境界座標です。</param>
        /// <param name="tilesPerPixel">Texture 1サンプルが表すタイル数です。</param>
        /// <param name="sampleLength">Textureの対象軸のサンプル数です。</param>
        /// <param name="viewportLength">Canvasの対象軸の長さです。</param>
        /// <param name="expectedX">左端原点で期待する境界位置です。</param>
        /// <param name="expectedY">上端原点へ反転した場合に期待する境界位置です。</param>
        [TestCase(0, 0, 1, 400, 400f, 200f, 200f)]
        [TestCase(0, 0, 1, 401, 401f, 200f, 201f)]
        [TestCase(16, 0, 4, 401, 802f, 392f, 410f)]
        public void CalculateViewportPositionFromWorldBoundary_UsesSampledIntegerCenter(
            int centerTileCoordinate,
            long worldBoundaryCoordinate,
            int tilesPerPixel,
            int sampleLength,
            float viewportLength,
            float expectedX,
            float expectedY)
        {
            float actualX =
                WorldGenerationMapSampler.CalculateViewportPositionFromWorldBoundary(
                    centerTileCoordinate,
                    worldBoundaryCoordinate,
                    tilesPerPixel,
                    sampleLength,
                    viewportLength,
                    invertAxis: false);
            float actualY =
                WorldGenerationMapSampler.CalculateViewportPositionFromWorldBoundary(
                    centerTileCoordinate,
                    worldBoundaryCoordinate,
                    tilesPerPixel,
                    sampleLength,
                    viewportLength,
                    invertAxis: true);

            Assert.That(actualX, Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(actualY, Is.EqualTo(expectedY).Within(0.0001f));
        }

        /// <summary>
        /// 拡大表示されたTexture上のカーソル地点を準備し、ズーム前後で同じ表示ピクセルの代表タイルを指すことを検証します。
        /// </summary>
        [Test]
        public void CalculateZoomedCenter_StretchedTexturePreservesPointerTile()
        {
            const float pointerX = 900f;
            const float pointerY = 225f;
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                50,
                -75,
                8,
                768,
                576,
                1200f,
                900f,
                pointerX,
                pointerY,
                out int beforeX,
                out int beforeY);

            WorldGenerationMapSampler.CalculateZoomedCenter(
                50,
                -75,
                8,
                4,
                768,
                576,
                768,
                576,
                1200f,
                900f,
                pointerX,
                pointerY,
                out int newCenterX,
                out int newCenterY);
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                newCenterX,
                newCenterY,
                4,
                768,
                576,
                1200f,
                900f,
                pointerX,
                pointerY,
                out int afterX,
                out int afterY);

            Assert.That(afterX, Is.EqualTo(beforeX));
            Assert.That(afterY, Is.EqualTo(beforeY));
        }

        /// <summary>
        /// Authoringへ別シードを渡した設定Snapshotが、その文字列を実ゲームと同じ安定64bit値へ変換することを検証します。
        /// Bakerも同じSnapshotメソッドを呼ぶため、EditorWindowだけ別の変換経路を持たないことを保証します。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_SeedOverrideUsesSharedStableConversion()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot("viewer-seed-override");

                Assert.That(
                    snapshot.WorldSeed,
                    Is.EqualTo(WorldSeedUtility.FromString("viewer-seed-override")));
                Assert.That(snapshot.GeneratorVersion, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>
        /// 新規Authoringの既定値と純粋計算・分布監査が使うCreateDefaultを準備し、主要な気候閾値が一致することを保証します。
        /// Inspectorの丸めた小数値だけが別の固定小数点へ変換され、実ゲームと監査結果がずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_DefaultClimateThresholdsMatchSharedDefaults()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Defaults Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot();
                WorldGenerationSettings expected = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(authoring.SeedText),
                    1);

                Assert.That(snapshot.SeaLevelThreshold, Is.EqualTo(expected.SeaLevelThreshold));
                Assert.That(
                    snapshot.HotTemperatureThreshold,
                    Is.EqualTo(expected.HotTemperatureThreshold));
                Assert.That(
                    snapshot.DryMoistureThreshold,
                    Is.EqualTo(expected.DryMoistureThreshold));
                Assert.That(
                    snapshot.WetMoistureThreshold,
                    Is.EqualTo(expected.WetMoistureThreshold));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>
        /// 既存Sceneに保存されている旧丸め小数を準備し、Snapshot変換時だけ現在の共通既定値へ移行されることを保証します。
        /// Sceneファイルを外部から書き換えず、開いているUnity Editorの未保存状態と競合しない移行経路を検証します。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_LegacyRoundedClimateDefaultsUseSharedDefaults()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Legacy Defaults Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                SerializedObject serializedAuthoring = new SerializedObject(authoring);
                serializedAuthoring.FindProperty("waterThreshold").floatValue = 0.305f;
                serializedAuthoring.FindProperty("hotTemperatureThreshold").floatValue = 0.61f;
                serializedAuthoring.FindProperty("dryMoistureThreshold").floatValue = 0.435f;
                serializedAuthoring.FindProperty("wetMoistureThreshold").floatValue = 0.603f;
                serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();

                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot();

                Assert.That(
                    snapshot.SeaLevelThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultSeaLevelThreshold));
                Assert.That(
                    snapshot.HotTemperatureThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultHotTemperatureThreshold));
                Assert.That(
                    snapshot.DryMoistureThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultDryMoistureThreshold));
                Assert.That(
                    snapshot.WetMoistureThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultWetMoistureThreshold));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>
        /// 乾燥・森林・湿地の湿度閾値へInspector上限を設定して検証処理を実行し、3つの領域が必ず昇順で残ることを保証します。
        /// 乾燥閾値と森林閾値が同じ0.999へ補正され、中湿度の森林候補帯が消える退行を防ぎます。
        /// </summary>
        [Test]
        public void AuthoringOnValidate_MaximumMoistureThresholdsKeepStrictOrdering()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Moisture Threshold Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                SerializedObject serializedAuthoring = new SerializedObject(authoring);

                // 3つともInspectorで選べる最大値を入力し、レビュー指摘の境界条件を再現します。
                serializedAuthoring.FindProperty("dryMoistureThreshold").floatValue = 1f;
                serializedAuthoring.FindProperty("wetMoistureThreshold").floatValue = 1f;
                serializedAuthoring.FindProperty("wetlandMoistureThreshold").floatValue = 1f;
                serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();

                // OnValidateはUnityから呼ばれる非公開ライフサイクルメソッドなので、EditModeテストではReflection経由で明示的に実行します。
                MethodInfo onValidate = typeof(WorldGenerationAuthoring).GetMethod(
                    "OnValidate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onValidate, Is.Not.Null);
                onValidate.Invoke(authoring, null);
                serializedAuthoring.Update();

                float dry = serializedAuthoring.FindProperty("dryMoistureThreshold").floatValue;
                float wet = serializedAuthoring.FindProperty("wetMoistureThreshold").floatValue;
                float wetland = serializedAuthoring.FindProperty("wetlandMoistureThreshold").floatValue;

                Assert.That(dry, Is.LessThan(wet));
                Assert.That(wet, Is.LessThan(wetland));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>
        /// EditorWindowの非公開UI参照へテスト用要素を設定します。
        /// Window全体のUXMLを開かず、対象の適用処理だけをEditModeで検証するために使用します。
        /// </summary>
        /// <param name="window">テスト対象のMap Viewer Windowです。</param>
        /// <param name="fieldName">設定する非公開フィールド名です。</param>
        /// <param name="value">フィールドへ設定するUI要素または説明値です。</param>
        private static void SetPrivateField(
            WorldGenerationMapViewerWindow window,
            string fieldName,
            object value)
        {
            FieldInfo field = typeof(WorldGenerationMapViewerWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"非公開フィールド '{fieldName}' が見つかりません。");
            field.SetValue(window, value);
        }
    }
}
