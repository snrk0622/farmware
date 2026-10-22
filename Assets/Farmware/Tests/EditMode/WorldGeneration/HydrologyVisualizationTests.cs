// このファイルは、TERRAIN-01C3-D2とC4-E2のGameビューが固定水文・派生水文を正しい分類色へ変換することを検証します。
// SceneやECS Worldを作らず、色写像、低解像度上限、決定論的サンプリングを純粋EditModeテストで確認します。

using System.Threading;
using System.Threading.Tasks;
using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>流向・終端・集水・河川・湖・派生水文の診断色とMap Viewer接続を保証します。</summary>
    public sealed class HydrologyVisualizationTests
    {
        /// <summary>
        /// 固定湖、湖面、越流、打切り流域が別色となり、打切りを固定湖として表示しないことを保証します。
        /// </summary>
        [Test]
        public void LakeColors_DistinguishFixedSurfaceOverflowAndTruncatedBasin()
        {
            GeneratedHydrologyCell fixedLake = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.FixedLake
            };
            GeneratedHydrologyCell surface = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell
            };
            GeneratedHydrologyCell overflow = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.FixedLake
                    | HydrologyLakeFlags.SurfaceCell
                    | HydrologyLakeFlags.Overflow
            };
            GeneratedHydrologyCell truncated = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.BasinResolved
                    | HydrologyLakeFlags.BasinTruncated
            };

            Color fixedColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    fixedLake);
            Color surfaceColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    surface);
            Color overflowColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    overflow);
            Color truncatedColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    truncated);

            Assert.That(surfaceColor, Is.Not.EqualTo(fixedColor));
            Assert.That(overflowColor, Is.Not.EqualTo(surfaceColor));
            Assert.That(truncatedColor, Is.Not.EqualTo(fixedColor));
            Assert.That(truncatedColor, Is.Not.EqualTo(surfaceColor));
            Assert.That(truncatedColor, Is.Not.EqualTo(overflowColor));
        }

        /// <summary>
        /// 越流する流域内の高所セルを準備し、湖面でないセルが越流色ではなく固定湖候補色になることを保証します。
        /// Overflowは流域全体へ共有されるため、SurfaceCellとの組み合わせを描画側で確認する退行テストです。
        /// </summary>
        [Test]
        public void LakeColor_OverflowWithoutSurfaceCellUsesFixedLakeColor()
        {
            GeneratedHydrologyCell fixedLake = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.FixedLake
            };
            GeneratedHydrologyCell dryOverflowBasinCell = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.Overflow
            };
            GeneratedHydrologyCell overflowingSurface = new GeneratedHydrologyCell
            {
                LakeFlags = HydrologyLakeFlags.FixedLake
                    | HydrologyLakeFlags.SurfaceCell
                    | HydrologyLakeFlags.Overflow
            };

            Color fixedColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    fixedLake);
            Color dryOverflowBasinColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    dryOverflowBasinCell);
            Color overflowingSurfaceColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    overflowingSurface);

            Assert.That(dryOverflowBasinColor, Is.EqualTo(fixedColor));
            Assert.That(dryOverflowBasinColor, Is.Not.EqualTo(overflowingSurfaceColor));
        }

        /// <summary>
        /// C4-Cの最終4区分を持つ湖面セルを準備し、一時湖、塩湖、閉鎖淡水湖、越流淡水湖が別色になることを保証します。
        /// </summary>
        [Test]
        public void LakeColors_DistinguishFinalWaterBalanceClassifications()
        {
            HydrologyLakeFlags surfaceFlags = HydrologyLakeFlags.FixedLake
                | HydrologyLakeFlags.SurfaceCell;
            GeneratedHydrologyCell temporary = new GeneratedHydrologyCell
            {
                LakeFlags = surfaceFlags,
                LakeClassification = LakeWaterBalanceClassification.Temporary
            };
            GeneratedHydrologyCell saltwater = new GeneratedHydrologyCell
            {
                LakeFlags = surfaceFlags,
                LakeClassification = LakeWaterBalanceClassification.PermanentSaltwater
            };
            GeneratedHydrologyCell freshwater = new GeneratedHydrologyCell
            {
                LakeFlags = surfaceFlags,
                LakeClassification = LakeWaterBalanceClassification.ClosedFreshwater
            };
            GeneratedHydrologyCell overflow = new GeneratedHydrologyCell
            {
                LakeFlags = surfaceFlags | HydrologyLakeFlags.Overflow,
                LakeClassification = LakeWaterBalanceClassification.OverflowFreshwater
            };

            Color temporaryColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    temporary);
            Color saltwaterColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    saltwater);
            Color freshwaterColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    freshwater);
            Color overflowColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyLake,
                    overflow);

            Assert.That(temporaryColor, Is.Not.EqualTo(saltwaterColor));
            Assert.That(temporaryColor, Is.Not.EqualTo(freshwaterColor));
            Assert.That(temporaryColor, Is.Not.EqualTo(overflowColor));
            Assert.That(saltwaterColor, Is.Not.EqualTo(freshwaterColor));
            Assert.That(saltwaterColor, Is.Not.EqualTo(overflowColor));
            Assert.That(freshwaterColor, Is.Not.EqualTo(overflowColor));
        }

        /// <summary>
        /// C4-E2で追加する5モードを固定水文診断として認識し、無関係な地形モードを水文Buffer走査へ含めないことを保証します。
        /// </summary>
        [Test]
        public void IsHydrologyVisualizationMode_IncludesAllDerivedModesOnly()
        {
            WorldGenerationDebugVisualizationMode[] derivedModes =
            {
                WorldGenerationDebugVisualizationMode.HydrologyGroundwater,
                WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation,
                WorldGenerationDebugVisualizationMode.HydrologySpring,
                WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding,
                WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate
            };

            foreach (WorldGenerationDebugVisualizationMode mode in derivedModes)
            {
                Assert.That(
                    WorldGenerationDebugVisualizationOverlay.IsHydrologyVisualizationMode(mode),
                    Is.True,
                    mode.ToString());
            }

            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsHydrologyVisualizationMode(
                    WorldGenerationDebugVisualizationMode.BaseMoisture),
                Is.False);
        }

        /// <summary>
        /// 地下水接続境界、自然飽和・自然冠水の端点、泉の有無、局所・広域湿原を準備し、
        /// 各固定生成状態が別色になって現在水文を入力せず判定できることを保証します。
        /// </summary>
        [Test]
        public void DerivedHydrologyColors_DistinguishFixedGeneratedStates()
        {
            GeneratedHydrologyCell disconnectedGroundwater = new GeneratedHydrologyCell
            {
                GroundwaterPotential = 32767
            };
            GeneratedHydrologyCell connectedGroundwater = new GeneratedHydrologyCell
            {
                GroundwaterPotential = 32768,
                ConnectionFlags = HydrologyConnectionFlags.Groundwater
            };
            GeneratedHydrologyTile dryTile = default;
            GeneratedHydrologyTile wetTile = new GeneratedHydrologyTile
            {
                NaturalSoilSaturationPotential = ushort.MaxValue,
                NaturalFloodingPotential = ushort.MaxValue
            };
            GeneratedHydrologyTile localWetland = new GeneratedHydrologyTile
            {
                Flags = GeneratedHydrologyTileFlags.WetlandTileEligible
            };
            GeneratedHydrologyTile largeWetland = new GeneratedHydrologyTile
            {
                Flags = GeneratedHydrologyTileFlags.WetlandTileEligible
                    | GeneratedHydrologyTileFlags.LargeWetlandExtent
            };
            GeneratedSurfaceWaterTile noSpring = default;
            GeneratedSurfaceWaterTile spring = new GeneratedSurfaceWaterTile
            {
                Type = SurfaceWaterTileType.SpringStream
            };

            Assert.That(
                WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyGroundwater,
                    disconnectedGroundwater,
                    dryTile,
                    noSpring),
                Is.Not.EqualTo(
                    WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                        WorldGenerationDebugVisualizationMode.HydrologyGroundwater,
                        connectedGroundwater,
                        dryTile,
                        noSpring)));
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation,
                    default,
                    dryTile,
                    noSpring),
                Is.Not.EqualTo(
                    WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                        WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation,
                        default,
                        wetTile,
                        noSpring)));
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologySpring,
                    default,
                    dryTile,
                    noSpring),
                Is.Not.EqualTo(
                    WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                        WorldGenerationDebugVisualizationMode.HydrologySpring,
                        default,
                        dryTile,
                        spring)));
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding,
                    default,
                    dryTile,
                    noSpring),
                Is.Not.EqualTo(
                    WorldGenerationDebugVisualizationOverlay.GetHydrologyVisualizationColor(
                        WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding,
                        default,
                        wetTile,
                        noSpring)));

            Color ineligibleColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate,
                    default,
                    dryTile,
                    noSpring);
            Color localColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate,
                    default,
                    localWetland,
                    noSpring);
            Color largeColor = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate,
                    default,
                    largeWetland,
                    noSpring);
            Assert.That(ineligibleColor, Is.Not.EqualTo(localColor));
            Assert.That(ineligibleColor, Is.Not.EqualTo(largeColor));
            Assert.That(localColor, Is.Not.EqualTo(largeColor));
        }

        /// <summary>
        /// Map Viewerの5水文モードがGameビューとRGBを共有し、背景Texture向けに不透明化されることを保証します。
        /// </summary>
        [Test]
        public void MapViewerHydrologyColors_MatchOverlayRgbAndAreOpaque()
        {
            GeneratedHydrologyCell cell = new GeneratedHydrologyCell
            {
                FlowDirection = HydrologyFlowDirection.NorthEast,
                ConnectionFlags = HydrologyConnectionFlags.ClosedBasin
                    | HydrologyConnectionFlags.Stream
                    | HydrologyConnectionFlags.River,
                CatchmentAreaCells = 4096,
                CatchmentFlags = HydrologyCatchmentFlags.Truncated,
                LakeFlags = HydrologyLakeFlags.BasinTruncated
            };
            WorldGenerationMapViewMode[] mapModes =
            {
                WorldGenerationMapViewMode.HydrologyFlowDirection,
                WorldGenerationMapViewMode.HydrologyTerminal,
                WorldGenerationMapViewMode.HydrologyCatchment,
                WorldGenerationMapViewMode.HydrologySurfaceWater,
                WorldGenerationMapViewMode.HydrologyLake
            };
            WorldGenerationDebugVisualizationMode[] overlayModes =
            {
                WorldGenerationDebugVisualizationMode.HydrologyFlowDirection,
                WorldGenerationDebugVisualizationMode.HydrologyTerminal,
                WorldGenerationDebugVisualizationMode.HydrologyCatchment,
                WorldGenerationDebugVisualizationMode.HydrologySurfaceWater,
                WorldGenerationDebugVisualizationMode.HydrologyLake
            };

            for (int index = 0; index < mapModes.Length; index++)
            {
                Color32 mapColor = WorldGenerationMapSampler.GetHydrologyDisplayColor(
                    mapModes[index],
                    cell);
                Color32 overlayColor = WorldGenerationDebugVisualizationOverlay
                    .GetHydrologyVisualizationColor(overlayModes[index], cell);
                Assert.That(mapColor.r, Is.EqualTo(overlayColor.r));
                Assert.That(mapColor.g, Is.EqualTo(overlayColor.g));
                Assert.That(mapColor.b, Is.EqualTo(overlayColor.b));
                Assert.That(mapColor.a, Is.EqualTo(byte.MaxValue));
            }
        }

        /// <summary>
        /// 小さな水文地図を生成し、各ピクセルが同じ座標へ直接生成した固定水文セルの色と一致することを保証します。
        /// </summary>
        [Test]
        public void Sample_HydrologyEveryPixelMatchesDirectGeneratorResult()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("hydrology-map-viewer-test"),
                1);
            WorldGenerationMapSampleResult result = WorldGenerationMapSampler.Sample(
                settings,
                0,
                0,
                3,
                2,
                8,
                WorldGenerationMapViewMode.HydrologyLake,
                CancellationToken.None);

            for (int pixelY = 0; pixelY < result.Height; pixelY++)
            {
                for (int pixelX = 0; pixelX < result.Width; pixelX++)
                {
                    result.GetSampledWorldTile(
                        pixelX,
                        pixelY,
                        out int tileX,
                        out int tileY);
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGetCellCoordinate(
                            settings.Hydrology,
                            tileX,
                            tileY,
                            out int cellX,
                            out int cellY),
                        Is.True);
                    Assert.That(
                        WorldGenerationMapSampler.TryGenerateHydrologyCellForDisplay(
                            settings,
                            cellX,
                            cellY,
                            true,
                            out GeneratedHydrologyCell cell),
                        Is.True);
                    Color32 expected = WorldGenerationMapSampler.GetHydrologyDisplayColor(
                        WorldGenerationMapViewMode.HydrologyLake,
                        cell);
                    Assert.That(
                        result.PixelColors[(pixelY * result.Width) + pixelX],
                        Is.EqualTo(expected));
                }
            }
        }

        /// <summary>
        /// 実際のMap Viewerと同じThreadPool上で水文画像を生成し、Main Thread限定Temp Allocatorへ依存しないことを保証します。
        /// </summary>
        [Test]
        public void Sample_HydrologyCanRunOnThreadPool()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("hydrology-map-thread-test"),
                1);

            // EditorWindowの非同期描画経路と同じ実行位置で、固定水文セルと画像の完成まで待ちます。
            WorldGenerationMapSampleResult result = Task.Run(
                    () => WorldGenerationMapSampler.Sample(
                        settings,
                        0,
                        0,
                        2,
                        2,
                        8,
                        WorldGenerationMapViewMode.HydrologyTerminal,
                        CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Assert.That(result.PixelColors, Has.Length.EqualTo(4));
            Assert.That(result.HasGeneratedData, Is.True);
        }

        /// <summary>水文用64サンプル上限でもCanvasの縦横比を保ち、通常768上限と分離されることを保証します。</summary>
        [Test]
        public void CalculateSampleResolution_HydrologyLimitKeepsAspectRatio()
        {
            WorldGenerationMapSampler.CalculateSampleResolution(
                800f,
                400f,
                WorldGenerationMapSampler.MaximumHydrologyResolution,
                out int width,
                out int height);

            Assert.That(width, Is.EqualTo(64));
            Assert.That(height, Is.EqualTo(32));
            Assert.That(
                WorldGenerationMapSampler.DoesSampleResolutionMatchViewport(
                    width,
                    height,
                    800f,
                    400f,
                    WorldGenerationMapSampler.MaximumHydrologyResolution),
                Is.True);
        }
    }
}
