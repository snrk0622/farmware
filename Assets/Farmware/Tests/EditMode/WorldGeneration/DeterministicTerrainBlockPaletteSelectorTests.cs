// このファイルは、Biome表層、地質材質差分、露出基盤、泥炭条件の純粋選択を検証します。
// 本番Bakerと同じパレットBlobを使い、負座標、固定境界、登録順、Seed再現性をEditModeで保証します。

using System;
using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// B3の用途別連続ノイズとBlockパレット選択が、確定した整数契約どおり動くことを保証するテストです。
    /// </summary>
    public sealed class DeterministicTerrainBlockPaletteSelectorTests
    {
        /// <summary>各テストで作成し、終了時に破棄する共通Definition Blobです。</summary>
        private BlobAssetReference<DefinitionCatalogBlob> definitionCatalog;

        /// <summary>各テストで作成し、終了時に破棄する地形Definition Blobです。</summary>
        private BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog;

        /// <summary>各テストで作成し、終了時に破棄する41材質対応Blobです。</summary>
        private BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog;

        /// <summary>各テストの選択対象として構築し、終了時に破棄するBlockパレットBlobです。</summary>
        private BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog;

        /// <summary>
        /// 各テストの前に、本番Bakerと同じ共通Definition・地形・材質・パレットカタログを構築します。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            DefinitionBuildEntry[] commonDefinitions =
                BuiltInTerrainBlockDefinitions.CreateCommonDefinitions(
                    0,
                    out DefinitionBuildReference[] commonReferences);
            Assert.That(
                DefinitionCatalogBuilder.TryBuild(
                    commonDefinitions,
                    commonReferences,
                    out definitionCatalog,
                    out DefinitionCatalogValidationError definitionError),
                Is.True,
                $"共通Definitionを構築できませんでした: {definitionError.Code}");
            Assert.That(
                BuiltInTerrainBlockDefinitions.TryCreateTerrainDefinitions(
                    definitionCatalog,
                    out TerrainBlockDefinitionBuildEntry[] terrainDefinitions),
                Is.True);
            Assert.That(
                TerrainBlockCatalogBuilder.TryBuild(
                    definitionCatalog,
                    terrainDefinitions,
                    out terrainCatalog),
                Is.True);
            Assert.That(
                TerrainBlockMaterialCatalogBuilder.TryBuild(
                    definitionCatalog,
                    TerrainBlockMaterialDefinitions.CreateDefault(),
                    out materialCatalog),
                Is.True);
            Assert.That(
                TerrainBlockPaletteCatalogBuilder.TryBuild(
                    terrainCatalog,
                    materialCatalog,
                    TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults(),
                    TerrainBlockPaletteDefinitions.CreateGeologyDefaults(),
                    out paletteCatalog),
                Is.True);
        }

        /// <summary>
        /// 各テストで所有するPersistent Blobを逆依存順に解放し、後続テストへメモリを残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (paletteCatalog.IsCreated)
            {
                paletteCatalog.Dispose();
            }

            if (materialCatalog.IsCreated)
            {
                materialCatalog.Dispose();
            }

            if (terrainCatalog.IsCreated)
            {
                terrainCatalog.Dispose();
            }

            if (definitionCatalog.IsCreated)
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 共有既定設定が表層64・32・16の4：2：1、地質・露出32・16の2：1、両境界39,321を保持することを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchSpecifiedScalesWeightsAndThresholds()
        {
            TerrainBlockPaletteSelectionSettings settings =
                TerrainBlockPaletteSelectionSettings.CreateDefault();

            Assert.That(
                DeterministicTerrainBlockPaletteSelector.ValidateSettings(settings),
                Is.True);
            Assert.That(settings.SurfaceLargeCellSizeInTiles, Is.EqualTo(64));
            Assert.That(settings.SurfaceMediumCellSizeInTiles, Is.EqualTo(32));
            Assert.That(settings.SurfaceSmallCellSizeInTiles, Is.EqualTo(16));
            Assert.That(settings.SurfaceLargeWeight, Is.EqualTo(4));
            Assert.That(settings.SurfaceMediumWeight, Is.EqualTo(2));
            Assert.That(settings.SurfaceSmallWeight, Is.EqualTo(1));
            Assert.That(settings.GeologyLargeCellSizeInTiles, Is.EqualTo(32));
            Assert.That(settings.GeologySmallCellSizeInTiles, Is.EqualTo(16));
            Assert.That(settings.GeologyLargeWeight, Is.EqualTo(2));
            Assert.That(settings.GeologySmallWeight, Is.EqualTo(1));
            Assert.That(settings.ExposureLargeCellSizeInTiles, Is.EqualTo(32));
            Assert.That(settings.ExposureSmallCellSizeInTiles, Is.EqualTo(16));
            Assert.That(settings.ExposureLargeWeight, Is.EqualTo(2));
            Assert.That(settings.ExposureSmallWeight, Is.EqualTo(1));
            Assert.That(settings.BedrockExposureThreshold, Is.EqualTo(39321));
            Assert.That(settings.PeatSoilSaturationThreshold, Is.EqualTo(39321));
        }

        /// <summary>
        /// 0〜65,535の上位8bitが、温帯草原の160・64・32累積重み区間の両端へ正しく入ることを保証します。
        /// </summary>
        [Test]
        public void WeightedSelection_UsesAllCumulativeIntervalBoundaries()
        {
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                    paletteCatalog,
                    BiomeId.TemperateGrassland,
                    out TerrainBlockPaletteRange range),
                Is.True);

            AssertSelectedMaterial(range, 0, TerrainBlockMaterialId.GrassLoam);
            AssertSelectedMaterial(range, 159, TerrainBlockMaterialId.GrassLoam);
            AssertSelectedMaterial(range, 160, TerrainBlockMaterialId.MeadowSoil);
            AssertSelectedMaterial(range, 223, TerrainBlockMaterialId.MeadowSoil);
            AssertSelectedMaterial(range, 224, TerrainBlockMaterialId.ClaySoil);
            AssertSelectedMaterial(range, 255, TerrainBlockMaterialId.ClaySoil);
        }

        /// <summary>
        /// 露出式が乾燥度と2：1：1比重を使い、境界39,321と最大値を64bit整数演算で正しく写像することを保証します。
        /// </summary>
        [Test]
        public void ExposureCalculation_PreservesThresholdAndEndpointContracts()
        {
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.CalculateBedrockExposurePotential(
                    12345,
                    45678,
                    23456),
                Is.EqualTo((ushort)(((2UL * 45678UL) + 23456UL + (65535UL - 12345UL)) / 4UL)));
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.CalculateBedrockExposureRate(39320, 39321),
                Is.EqualTo(0));
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.CalculateBedrockExposureRate(39321, 39321),
                Is.EqualTo(0));
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.CalculateBedrockExposureRate(39322, 39321),
                Is.EqualTo(2));
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.CalculateBedrockExposureRate(
                    ushort.MaxValue,
                    39321),
                Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 露出率0は常に表層、65,535は常に基盤となり、中間値だけノイズ値との厳密な未満比較を使うことを保証します。
        /// </summary>
        [Test]
        public void ExposureDecision_HandlesEndpointsBeforeStrictLessThanComparison()
        {
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.ShouldUseExposedGround(0, 0),
                Is.False);
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.ShouldUseExposedGround(
                    0,
                    ushort.MaxValue),
                Is.False);
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.ShouldUseExposedGround(
                    ushort.MaxValue,
                    ushort.MaxValue),
                Is.True);
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.ShouldUseExposedGround(100, 99),
                Is.True);
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.ShouldUseExposedGround(100, 100),
                Is.False);
        }

        /// <summary>
        /// 負座標で3用途の専用Salt・格子寸法・整数比重が仕様どおり合成され、同じ入力を再実行して同じ全結果を返すことを保証します。
        /// </summary>
        [Test]
        public void Selection_NegativeCoordinatesMatchExplicitWeightedNoiseAndRepeat()
        {
            WorldGenerationSettings settings = CreateSettings("block-palette-negative");
            const int worldX = -137;
            const int worldY = -65;
            const int warpedX = -149;
            const int warpedY = -54;

            Assert.That(
                TrySelect(
                    settings,
                    paletteCatalog,
                    worldX,
                    worldY,
                    warpedX,
                    warpedY,
                    BiomeId.TemperateGrassland,
                    GeologyClass.MetamorphicRock,
                    41000,
                    42000,
                    50000,
                    45000,
                    out TerrainBlockPaletteSelectionResult first),
                Is.True);
            Assert.That(
                TrySelect(
                    settings,
                    paletteCatalog,
                    worldX,
                    worldY,
                    warpedX,
                    warpedY,
                    BiomeId.TemperateGrassland,
                    GeologyClass.MetamorphicRock,
                    41000,
                    42000,
                    50000,
                    45000,
                    out TerrainBlockPaletteSelectionResult second),
                Is.True);

            Assert.That(first.SurfacePatchNoise, Is.EqualTo(SampleThreeScaleExpected(
                settings,
                warpedX,
                warpedY,
                64,
                32,
                16,
                4,
                2,
                1,
                WorldGenerationPurposeSalts.SurfaceBlockPatch)));
            Assert.That(first.GeologyVariantNoise, Is.EqualTo(SampleTwoScaleExpected(
                settings,
                worldX,
                worldY,
                32,
                16,
                2,
                1,
                WorldGenerationPurposeSalts.GeologyBlockVariant)));
            Assert.That(first.BedrockExposurePatchNoise, Is.EqualTo(SampleTwoScaleExpected(
                settings,
                worldX,
                worldY,
                32,
                16,
                2,
                1,
                WorldGenerationPurposeSalts.BedrockExposurePatch)));
            AssertEquivalent(first, second);
        }

        /// <summary>
        /// SwampForestで選ばれたPeatSoilが飽和39,321未満では最初の非泥炭MarshMudへ置換され、境界以上では維持されることを保証します。
        /// </summary>
        [Test]
        public void PeatCondition_UsesFirstNonPeatEntryBelowSaturationBoundary()
        {
            WorldGenerationSettings settings = CreateSettings("block-palette-peat");
            Assert.That(
                TryFindSwampPeatCoordinate(
                    settings,
                    out int worldX,
                    out int worldY,
                    out TerrainBlockPaletteSelectionResult dry),
                Is.True,
                "固定探索範囲内でSwampForestのPeatSoil候補を取得できませんでした。");
            Assert.That(dry.SurfaceGroundCandidate.MaterialId, Is.EqualTo(TerrainBlockMaterialId.PeatSoil));
            Assert.That(dry.SurfaceGround.MaterialId, Is.EqualTo(TerrainBlockMaterialId.MarshMud));
            Assert.That(dry.ReplacedUnsaturatedPeat, Is.True);

            Assert.That(
                TrySelect(
                    settings,
                    paletteCatalog,
                    worldX,
                    worldY,
                    worldX,
                    worldY,
                    BiomeId.SwampForest,
                    GeologyClass.UnconsolidatedSediment,
                    ushort.MaxValue,
                    0,
                    0,
                    39321,
                    out TerrainBlockPaletteSelectionResult saturated),
                Is.True);
            Assert.That(saturated.SurfaceGroundCandidate.MaterialId, Is.EqualTo(TerrainBlockMaterialId.PeatSoil));
            Assert.That(saturated.SurfaceGround.MaterialId, Is.EqualTo(TerrainBlockMaterialId.PeatSoil));
            Assert.That(saturated.ReplacedUnsaturatedPeat, Is.False);
        }

        /// <summary>
        /// Biome・地質Definition行の登録順を逆転しても、同じ安定IDと座標から同じ材質・ノイズ・露出結果を選ぶことを保証します。
        /// </summary>
        [Test]
        public void Selection_IsIndependentFromDefinitionRowRegistrationOrder()
        {
            BiomeSurfaceBlockPaletteDefinition[] biomes =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            GeologyBlockPaletteDefinition[] geologies =
                TerrainBlockPaletteDefinitions.CreateGeologyDefaults();
            Array.Reverse(biomes);
            Array.Reverse(geologies);
            Assert.That(
                TerrainBlockPaletteCatalogBuilder.TryBuild(
                    terrainCatalog,
                    materialCatalog,
                    biomes,
                    geologies,
                    out BlobAssetReference<TerrainBlockPaletteCatalogBlob> reversedCatalog),
                Is.True);

            try
            {
                WorldGenerationSettings settings = CreateSettings("block-palette-order");
                Assert.That(
                    TrySelect(
                        settings,
                        paletteCatalog,
                        -91,
                        73,
                        -103,
                        79,
                        BiomeId.VolcanicGeothermal,
                        GeologyClass.VolcanicRock,
                        25000,
                        60000,
                        50000,
                        45000,
                        out TerrainBlockPaletteSelectionResult normal),
                    Is.True);
                Assert.That(
                    TrySelect(
                        settings,
                        reversedCatalog,
                        -91,
                        73,
                        -103,
                        79,
                        BiomeId.VolcanicGeothermal,
                        GeologyClass.VolcanicRock,
                        25000,
                        60000,
                        50000,
                        45000,
                        out TerrainBlockPaletteSelectionResult reversed),
                    Is.True);
                AssertEquivalent(normal, reversed);
            }
            finally
            {
                reversedCatalog.Dispose();
            }
        }

        /// <summary>0〜255の指定区間値を16bitノイズへ戻し、期待材質が選ばれることを確認します。</summary>
        /// <param name="range">温帯草原の表層パレット範囲です。</param>
        /// <param name="selector">確認する0〜255の累積重み区間値です。</param>
        /// <param name="expectedMaterial">選ばれるべき材質IDです。</param>
        private void AssertSelectedMaterial(
            TerrainBlockPaletteRange range,
            int selector,
            TerrainBlockMaterialId expectedMaterial)
        {
            ushort patchNoise = (ushort)(selector << 8);
            Assert.That(
                DeterministicTerrainBlockPaletteSelector.TrySelectWeightedEntry(
                    paletteCatalog,
                    range,
                    patchNoise,
                    out TerrainBlockPaletteEntryBlob entry),
                Is.True);
            Assert.That(entry.MaterialId, Is.EqualTo(expectedMaterial));
        }

        /// <summary>Seed文字列からBlockパレット既定値を含む現行ワールド生成設定を作ります。</summary>
        /// <param name="seedText">再現対象のSeed文字列です。</param>
        /// <returns>GeneratorVersion 2の共有既定設定です。</returns>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString(seedText),
                WorldGenerationSettings.CurrentGeneratorVersion);
        }

        /// <summary>引数の並びを固定し、テストから本番の全選択入口を呼びます。</summary>
        /// <param name="settings">ワールド生成設定です。</param>
        /// <param name="catalog">選択対象のパレットBlobです。</param>
        /// <param name="worldX">水平絶対タイル座標です。</param>
        /// <param name="worldY">垂直絶対タイル座標です。</param>
        /// <param name="warpedX">Biome Warp後の水平座標です。</param>
        /// <param name="warpedY">Biome Warp後の垂直座標です。</param>
        /// <param name="biomeId">表層Biome IDです。</param>
        /// <param name="geologyClass">卓越地質IDです。</param>
        /// <param name="baseMoisture">基礎湿度です。</param>
        /// <param name="ruggedness">地形起伏です。</param>
        /// <param name="erosionResistance">侵食抵抗です。</param>
        /// <param name="saturation">自然土壌飽和ポテンシャルです。</param>
        /// <param name="result">成功時の全選択結果です。</param>
        /// <returns>本番選択が成功した場合はtrueです。</returns>
        private static bool TrySelect(
            in WorldGenerationSettings settings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            int worldX,
            int worldY,
            int warpedX,
            int warpedY,
            BiomeId biomeId,
            GeologyClass geologyClass,
            ushort baseMoisture,
            ushort ruggedness,
            ushort erosionResistance,
            ushort saturation,
            out TerrainBlockPaletteSelectionResult result)
        {
            return DeterministicTerrainBlockPaletteSelector.TrySelect(
                settings,
                catalog,
                worldX,
                worldY,
                warpedX,
                warpedY,
                biomeId,
                geologyClass,
                baseMoisture,
                ruggedness,
                erosionResistance,
                saturation,
                out result);
        }

        /// <summary>固定範囲を走査し、SwampForestでPeatSoilが選ばれる負座標を決定論的に探します。</summary>
        /// <param name="settings">探索に使用する固定Seed設定です。</param>
        /// <param name="worldX">成功時の水平絶対タイル座標です。</param>
        /// <param name="worldY">成功時の垂直絶対タイル座標です。</param>
        /// <param name="result">飽和境界直前39,320での選択結果です。</param>
        /// <returns>固定範囲内でPeatSoil候補を見つけた場合はtrueです。</returns>
        private bool TryFindSwampPeatCoordinate(
            in WorldGenerationSettings settings,
            out int worldX,
            out int worldY,
            out TerrainBlockPaletteSelectionResult result)
        {
            // 連続パッチの相関を避けるため16タイル刻みで負座標の複数格子を調べます。
            for (int y = -256; y <= -16; y += 16)
            {
                for (int x = -256; x <= -16; x += 16)
                {
                    if (TrySelect(
                            settings,
                            paletteCatalog,
                            x,
                            y,
                            x,
                            y,
                            BiomeId.SwampForest,
                            GeologyClass.UnconsolidatedSediment,
                            ushort.MaxValue,
                            0,
                            0,
                            39320,
                            out TerrainBlockPaletteSelectionResult candidate)
                        && candidate.SurfaceGroundCandidate.MaterialId
                            == TerrainBlockMaterialId.PeatSoil)
                    {
                        worldX = x;
                        worldY = y;
                        result = candidate;
                        return true;
                    }
                }
            }

            worldX = 0;
            worldY = 0;
            result = default;
            return false;
        }

        /// <summary>3解像度の値ノイズをテスト側で直接合成し、本番の固定比重を照合します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つ設定です。</param>
        /// <param name="x">サンプリングする水平整数座標です。</param>
        /// <param name="y">サンプリングする垂直整数座標です。</param>
        /// <param name="largeCellSize">大域格子サイズです。</param>
        /// <param name="mediumCellSize">中域格子サイズです。</param>
        /// <param name="smallCellSize">細部格子サイズです。</param>
        /// <param name="largeWeight">大域比重です。</param>
        /// <param name="mediumWeight">中域比重です。</param>
        /// <param name="smallWeight">細部比重です。</param>
        /// <param name="purposeSalt">用途専用Saltです。</param>
        /// <returns>明示的に合成した0〜65,535ノイズです。</returns>
        private static ushort SampleThreeScaleExpected(
            in WorldGenerationSettings settings,
            int x,
            int y,
            int largeCellSize,
            int mediumCellSize,
            int smallCellSize,
            byte largeWeight,
            byte mediumWeight,
            byte smallWeight,
            ulong purposeSalt)
        {
            ushort large = SampleExpectedScale(settings, x, y, largeCellSize, purposeSalt, 0);
            ushort medium = SampleExpectedScale(settings, x, y, mediumCellSize, purposeSalt, 1);
            ushort small = SampleExpectedScale(settings, x, y, smallCellSize, purposeSalt, 2);
            ulong weighted = ((ulong)large * largeWeight)
                + ((ulong)medium * mediumWeight)
                + ((ulong)small * smallWeight);
            return (ushort)(weighted / (uint)(largeWeight + mediumWeight + smallWeight));
        }

        /// <summary>2解像度の値ノイズをテスト側で直接合成し、本番の固定比重を照合します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つ設定です。</param>
        /// <param name="x">サンプリングする水平整数座標です。</param>
        /// <param name="y">サンプリングする垂直整数座標です。</param>
        /// <param name="largeCellSize">大域格子サイズです。</param>
        /// <param name="smallCellSize">細部格子サイズです。</param>
        /// <param name="largeWeight">大域比重です。</param>
        /// <param name="smallWeight">細部比重です。</param>
        /// <param name="purposeSalt">用途専用Saltです。</param>
        /// <returns>明示的に合成した0〜65,535ノイズです。</returns>
        private static ushort SampleTwoScaleExpected(
            in WorldGenerationSettings settings,
            int x,
            int y,
            int largeCellSize,
            int smallCellSize,
            byte largeWeight,
            byte smallWeight,
            ulong purposeSalt)
        {
            ushort large = SampleExpectedScale(settings, x, y, largeCellSize, purposeSalt, 0);
            ushort small = SampleExpectedScale(settings, x, y, smallCellSize, purposeSalt, 1);
            ulong weighted = ((ulong)large * largeWeight) + ((ulong)small * smallWeight);
            return (ushort)(weighted / (uint)(largeWeight + smallWeight));
        }

        /// <summary>本番と同じ解像度番号Saltを作り、1解像度の期待ノイズを取得します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つ設定です。</param>
        /// <param name="x">サンプリングする水平整数座標です。</param>
        /// <param name="y">サンプリングする垂直整数座標です。</param>
        /// <param name="cellSize">格子サイズです。単位はタイルです。</param>
        /// <param name="purposeSalt">用途専用Saltです。</param>
        /// <param name="scaleIndex">解像度を分離する0始まり番号です。</param>
        /// <returns>0〜65,535の値ノイズです。</returns>
        private static ushort SampleExpectedScale(
            in WorldGenerationSettings settings,
            int x,
            int y,
            int cellSize,
            ulong purposeSalt,
            uint scaleIndex)
        {
            return DeterministicNoise.SampleValueNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                x,
                y,
                cellSize,
                WorldSeedUtility.Mix64(purposeSalt ^ scaleIndex));
        }

        /// <summary>2回または2カタログの選択結果について、全ての監査対象値が一致することを確認します。</summary>
        /// <param name="expected">基準となる選択結果です。</param>
        /// <param name="actual">再実行または逆登録順カタログの選択結果です。</param>
        private static void AssertEquivalent(
            TerrainBlockPaletteSelectionResult expected,
            TerrainBlockPaletteSelectionResult actual)
        {
            Assert.That(actual.SurfaceGroundCandidate.MaterialId, Is.EqualTo(expected.SurfaceGroundCandidate.MaterialId));
            Assert.That(actual.SurfaceGround.MaterialId, Is.EqualTo(expected.SurfaceGround.MaterialId));
            Assert.That(actual.ExposedGround.MaterialId, Is.EqualTo(expected.ExposedGround.MaterialId));
            Assert.That(actual.NaturalWall.MaterialId, Is.EqualTo(expected.NaturalWall.MaterialId));
            Assert.That(actual.FinalGround.MaterialId, Is.EqualTo(expected.FinalGround.MaterialId));
            Assert.That(actual.SurfacePatchNoise, Is.EqualTo(expected.SurfacePatchNoise));
            Assert.That(actual.GeologyVariantNoise, Is.EqualTo(expected.GeologyVariantNoise));
            Assert.That(actual.BedrockExposurePatchNoise, Is.EqualTo(expected.BedrockExposurePatchNoise));
            Assert.That(actual.BedrockExposurePotential, Is.EqualTo(expected.BedrockExposurePotential));
            Assert.That(actual.BedrockExposureRate, Is.EqualTo(expected.BedrockExposureRate));
            Assert.That(actual.ReplacedUnsaturatedPeat, Is.EqualTo(expected.ReplacedUnsaturatedPeat));
            Assert.That(actual.UsesExposedGround, Is.EqualTo(expected.UsesExposedGround));
        }
    }
}
