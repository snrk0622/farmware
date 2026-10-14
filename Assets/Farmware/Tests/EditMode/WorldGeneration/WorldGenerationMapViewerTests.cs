// このファイルは、広域マップビューアが実ゲームと同じ座標・設定・Generator結果を表示することをEditModeで検証します。
// EditorWindowそのものを開かず、バックグラウンド化できる純粋サンプラーとAuthoring設定変換を対象にします。

using System.Reflection;
using System.Threading;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// マップビューア用の代表座標、色変換、Authoring共有設定が実際の地形生成と一致することを保証します。
    /// </summary>
    public sealed class WorldGenerationMapViewerTests
    {
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
        /// 左上原点のPointer座標を負座標を含むワールド座標へ変換し、Canvas中央が指定中心へ一致することを検証します。
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
                200f,
                150f,
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
            Assert.That(topLeftY, Is.EqualTo(800));
        }

        /// <summary>
        /// カーソル中心ズーム後も、そのPointer位置が同じワールドタイル付近を指すことを検証します。
        /// 整数タイルへ丸めるため、許容差は新しい1ピクセル幅以内とします。
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

            Assert.That(afterX, Is.EqualTo(beforeX).Within(4));
            Assert.That(afterY, Is.EqualTo(beforeY).Within(4));
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
            Assert.That(worldTileY, Is.EqualTo(376));
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
        /// 拡大表示されたTexture上のカーソル地点を準備し、ズーム前後で同じワールドタイルを指すことを検証します。
        /// 整数タイルへ丸めるため、許容差は新しい1サンプル幅以内とします。
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

            Assert.That(afterX, Is.EqualTo(beforeX).Within(4));
            Assert.That(afterY, Is.EqualTo(beforeY).Within(4));
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
    }
}
