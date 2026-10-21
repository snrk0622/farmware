// このファイルは、TERRAIN-01C3-D2のGameビューとMap Viewerが同じ固定水文結果を同じ分類色へ変換することを検証します。
// SceneやECS Worldを作らず、色写像、低解像度上限、決定論的サンプリングを純粋EditModeテストで確認します。

using System.Threading;
using System.Threading.Tasks;
using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>流向・終端・集水・河川・湖の診断色とMap Viewer接続を保証します。</summary>
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
                        DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            cellX,
                            cellY,
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
