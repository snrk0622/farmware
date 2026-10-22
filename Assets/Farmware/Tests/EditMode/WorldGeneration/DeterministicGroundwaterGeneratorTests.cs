// このファイルは、TERRAIN-01C4-B1の局所地質ポテンシャル、確定補正式、端点、決定論を検証します。
// SceneやECSを作らず、後続の地下水Jobが利用する純粋計算だけをEditModeで保証します。

using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 亀裂、風化帯、粘土層、帯水層の独立生成と、地下水専用の透水性・保持力補正を検証します。
    /// C4-B2の固定9地点平均、ECS Buffer、自然土壌飽和は次の変更単位で別に検証します。
    /// </summary>
    public sealed class DeterministicGroundwaterGeneratorTests
    {
        /// <summary>
        /// 製品既定設定を準備し、4格子、3オクターブ、中央値、8個の補正除数が確定仕様どおりであることを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchTerrain01C4B1Contract()
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();

            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(settings), Is.True);
            Assert.That(settings.FractureCellSizeInTiles, Is.EqualTo(128));
            Assert.That(settings.WeatheringCellSizeInTiles, Is.EqualTo(96));
            Assert.That(settings.ClayCellSizeInTiles, Is.EqualTo(192));
            Assert.That(settings.AquiferCellSizeInTiles, Is.EqualTo(256));
            Assert.That(settings.LocalGeologyOctaveCount, Is.EqualTo(3));
            Assert.That(settings.SupportMidpoint, Is.EqualTo(32768));
            Assert.That(settings.FracturePermeabilityDivisor, Is.EqualTo(2));
            Assert.That(settings.WeatheringPermeabilityDivisor, Is.EqualTo(4));
            Assert.That(settings.ClayPermeabilityDivisor, Is.EqualTo(2));
            Assert.That(settings.AquiferPermeabilityDivisor, Is.EqualTo(8));
            Assert.That(settings.FractureStorageDivisor, Is.EqualTo(8));
            Assert.That(settings.WeatheringStorageDivisor, Is.EqualTo(4));
            Assert.That(settings.ClayStorageDivisor, Is.EqualTo(4));
            Assert.That(settings.AquiferStorageDivisor, Is.EqualTo(2));
            Assert.That(settings.RunoffRechargeMultiplier, Is.EqualTo(2048));
            Assert.That(settings.GroundwaterConnectionThreshold, Is.EqualTo(32768));
            Assert.That(settings.SaturationMoistureWeight, Is.EqualTo(2));
            Assert.That(settings.SaturationGroundwaterWeight, Is.EqualTo(2));
            Assert.That(settings.SaturationDrainageWeight, Is.EqualTo(1));
            Assert.That(settings.SaturationFlatnessWeight, Is.EqualTo(1));
            Assert.That(settings.SaturationWeightTotal, Is.EqualTo(6));
            Assert.That(settings.NaturalSoilSaturationThreshold, Is.EqualTo(39321));
        }

        /// <summary>
        /// 各固定値を一つずつ壊した設定を準備し、暗黙補正や別契約として受理されないことを保証します。
        /// </summary>
        [Test]
        public void Validation_RejectsChangedContractValues()
        {
            GroundwaterGenerationSettings wrongCellSize = GroundwaterGenerationSettings.CreateDefault();
            wrongCellSize.FractureCellSizeInTiles = 64;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(wrongCellSize), Is.False);

            GroundwaterGenerationSettings wrongOctaves = GroundwaterGenerationSettings.CreateDefault();
            wrongOctaves.LocalGeologyOctaveCount = 2;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(wrongOctaves), Is.False);

            GroundwaterGenerationSettings wrongMidpoint = GroundwaterGenerationSettings.CreateDefault();
            wrongMidpoint.SupportMidpoint = 32767;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(wrongMidpoint), Is.False);

            GroundwaterGenerationSettings zeroDivisor = GroundwaterGenerationSettings.CreateDefault();
            zeroDivisor.AquiferStorageDivisor = 0;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(zeroDivisor), Is.False);

            GroundwaterGenerationSettings wrongCoefficient = GroundwaterGenerationSettings.CreateDefault();
            wrongCoefficient.ClayPermeabilityDivisor = 3;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(wrongCoefficient), Is.False);

            GroundwaterGenerationSettings wrongRecharge = GroundwaterGenerationSettings.CreateDefault();
            wrongRecharge.RunoffRechargeMultiplier = 1024;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(wrongRecharge), Is.False);

            GroundwaterGenerationSettings wrongWeightTotal = GroundwaterGenerationSettings.CreateDefault();
            wrongWeightTotal.SaturationWeightTotal = 5;
            Assert.That(DeterministicGroundwaterGenerator.ValidateSettings(wrongWeightTotal), Is.False);
        }

        /// <summary>
        /// 負座標を含む同じ入力を二度生成し、4ポテンシャルと補正後2値が完全に再現されることを保証します。
        /// </summary>
        /// <param name="worldTileX">検証する水平絶対タイル座標です。</param>
        /// <param name="worldTileY">検証する垂直絶対タイル座標です。</param>
        [TestCase(-4097, -2049)]
        [TestCase(-1, -1)]
        [TestCase(0, 0)]
        [TestCase(4096, 8192)]
        public void Generation_SameInputIsDeterministic(
            int worldTileX,
            int worldTileY)
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-deterministic");

            Assert.That(
                DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                    settings,
                    worldTileX,
                    worldTileY,
                    45874,
                    58981,
                    out AdjustedGroundwaterGeology first),
                Is.True);
            Assert.That(
                DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                    settings,
                    worldTileX,
                    worldTileY,
                    45874,
                    58981,
                    out AdjustedGroundwaterGeology second),
                Is.True);

            AssertAdjustedGeologyEqual(first, second);
        }

        /// <summary>
        /// 一つの座標を準備し、各用途が専用Salt・専用格子・同じ3オクターブで直接求めた値と一致することを保証します。
        /// </summary>
        [Test]
        public void Sampling_UsesIndependentPurposeSaltsAndCellSizes()
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-purpose-salts");
            const int WorldTileX = -731;
            const int WorldTileY = 1189;

            Assert.That(
                DeterministicGroundwaterGenerator.TrySampleLocalGeology(
                    settings,
                    WorldTileX,
                    WorldTileY,
                    out LocalGeologyPotentials actual),
                Is.True);

            Assert.That(
                actual.Fracture,
                Is.EqualTo(SampleExpected(
                    settings,
                    WorldTileX,
                    WorldTileY,
                    settings.Groundwater.FractureCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterFracturePotential)));
            Assert.That(
                actual.Weathering,
                Is.EqualTo(SampleExpected(
                    settings,
                    WorldTileX,
                    WorldTileY,
                    settings.Groundwater.WeatheringCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterWeatheringPotential)));
            Assert.That(
                actual.Clay,
                Is.EqualTo(SampleExpected(
                    settings,
                    WorldTileX,
                    WorldTileY,
                    settings.Groundwater.ClayCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterClayPotential)));
            Assert.That(
                actual.Aquifer,
                Is.EqualTo(SampleExpected(
                    settings,
                    WorldTileX,
                    WorldTileY,
                    settings.Groundwater.AquiferCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterAquiferPotential)));

            // 既存値を再利用すると機能追加で乱数列が衝突するため、4つの定数自体も相互に異なることを固定します。
            Assert.That(
                WorldGenerationPurposeSalts.GroundwaterFracturePotential,
                Is.Not.EqualTo(WorldGenerationPurposeSalts.GroundwaterWeatheringPotential));
            Assert.That(
                WorldGenerationPurposeSalts.GroundwaterFracturePotential,
                Is.Not.EqualTo(WorldGenerationPurposeSalts.GroundwaterClayPotential));
            Assert.That(
                WorldGenerationPurposeSalts.GroundwaterFracturePotential,
                Is.Not.EqualTo(WorldGenerationPurposeSalts.GroundwaterAquiferPotential));
            Assert.That(
                WorldGenerationPurposeSalts.GroundwaterWeatheringPotential,
                Is.Not.EqualTo(WorldGenerationPurposeSalts.GroundwaterClayPotential));
            Assert.That(
                WorldGenerationPurposeSalts.GroundwaterWeatheringPotential,
                Is.Not.EqualTo(WorldGenerationPurposeSalts.GroundwaterAquiferPotential));
            Assert.That(
                WorldGenerationPurposeSalts.GroundwaterClayPotential,
                Is.Not.EqualTo(WorldGenerationPurposeSalts.GroundwaterAquiferPotential));
        }

        /// <summary>
        /// 中央値以下と最大値の人工ポテンシャルを準備し、Supportと8係数の整数切り捨てが仕様式どおりであることを保証します。
        /// </summary>
        [Test]
        public void Adjustment_AppliesSupportAndIntegerFormulaOnce()
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();
            LocalGeologyPotentials potentials = new LocalGeologyPotentials
            {
                Fracture = ushort.MaxValue,
                Weathering = 32768,
                Clay = ushort.MaxValue,
                Aquifer = ushort.MaxValue
            };

            Assert.That(
                DeterministicGroundwaterGenerator.TryApplyLocalGeologyAdjustment(
                    settings,
                    20000,
                    10000,
                    potentials,
                    out AdjustedGroundwaterGeology result),
                Is.True);

            const long MaximumSupport = ushort.MaxValue - 32768L;
            long expectedPermeability = 20000L
                + (MaximumSupport / 2L)
                - (MaximumSupport / 2L)
                + (MaximumSupport / 8L);
            long expectedStorage = 10000L
                + (MaximumSupport / 8L)
                + (MaximumSupport / 4L)
                + (MaximumSupport / 2L);
            Assert.That(result.Permeability, Is.EqualTo((ushort)expectedPermeability));
            Assert.That(result.GroundwaterStorage, Is.EqualTo((ushort)expectedStorage));
            AssertPotentialsEqual(result.Potentials, potentials);
        }

        /// <summary>
        /// 大きな粘土補正と大きな正補正を別々に準備し、負値の周回や途中ushort変換をせず両端へ飽和することを保証します。
        /// </summary>
        [Test]
        public void Adjustment_ClampsOnlyAfterAllTermsAreCombined()
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();
            LocalGeologyPotentials clayOnly = new LocalGeologyPotentials
            {
                Clay = ushort.MaxValue
            };
            Assert.That(
                DeterministicGroundwaterGenerator.TryApplyLocalGeologyAdjustment(
                    settings,
                    100,
                    0,
                    clayOnly,
                    out AdjustedGroundwaterGeology lowResult),
                Is.True);
            Assert.That(lowResult.Permeability, Is.Zero);
            Assert.That(lowResult.GroundwaterStorage, Is.EqualTo(8191));

            LocalGeologyPotentials positive = new LocalGeologyPotentials
            {
                Fracture = ushort.MaxValue,
                Weathering = ushort.MaxValue,
                Aquifer = ushort.MaxValue
            };
            Assert.That(
                DeterministicGroundwaterGenerator.TryApplyLocalGeologyAdjustment(
                    settings,
                    65000,
                    65000,
                    positive,
                    out AdjustedGroundwaterGeology highResult),
                Is.True);
            Assert.That(highResult.Permeability, Is.EqualTo(ushort.MaxValue));
            Assert.That(highResult.GroundwaterStorage, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 不正設定と事前値を準備し、生成失敗時に部分的なポテンシャルや補正値を公開しないことを保証します。
        /// </summary>
        [Test]
        public void Generation_InvalidSettingsReturnFalseAndDefaultResult()
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-invalid");
            GroundwaterGenerationSettings invalid = settings.Groundwater;
            invalid.LocalGeologyOctaveCount = 0;
            settings.Groundwater = invalid;

            Assert.That(
                DeterministicGroundwaterGenerator.TrySampleLocalGeology(
                    settings,
                    10,
                    -20,
                    out LocalGeologyPotentials potentials),
                Is.False);
            AssertPotentialsEqual(potentials, default);

            Assert.That(
                DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                    settings,
                    10,
                    -20,
                    45874,
                    58981,
                    out AdjustedGroundwaterGeology result),
                Is.False);
            AssertAdjustedGeologyEqual(result, default);
        }

        /// <summary>テスト名から安定Seedを作り、製品既定値と同じGeneratorVersion 1の設定を返します。</summary>
        /// <param name="seedText">テストケース同士の乱数列を分離する固定文字列です。</param>
        /// <returns>局所地質を含む製品既定のワールド生成設定です。</returns>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString(seedText),
                1);
        }

        /// <summary>製品コードと同じ整数ノイズへ明示した格子とSaltを渡し、期待ポテンシャルを返します。</summary>
        /// <param name="settings">Seed、版、オクターブ数を持つ固定ワールド生成設定です。</param>
        /// <param name="worldTileX">サンプリングする水平絶対タイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直絶対タイル座標です。</param>
        /// <param name="cellSizeInTiles">期待する用途の最も低周波な格子サイズです。</param>
        /// <param name="purposeSalt">期待する用途へ割り当てた固定Saltです。</param>
        /// <returns>指定した用途の0〜65,535ポテンシャルです。</returns>
        private static ushort SampleExpected(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            int cellSizeInTiles,
            ulong purposeSalt)
        {
            return DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                cellSizeInTiles,
                settings.Groundwater.LocalGeologyOctaveCount,
                purposeSalt);
        }

        /// <summary>2つの局所地質ポテンシャルが全4用途で一致することを検証します。</summary>
        /// <param name="actual">製品コードから得た実際の4ポテンシャルです。</param>
        /// <param name="expected">テストが要求する4ポテンシャルです。</param>
        private static void AssertPotentialsEqual(
            in LocalGeologyPotentials actual,
            in LocalGeologyPotentials expected)
        {
            Assert.That(actual.Fracture, Is.EqualTo(expected.Fracture));
            Assert.That(actual.Weathering, Is.EqualTo(expected.Weathering));
            Assert.That(actual.Clay, Is.EqualTo(expected.Clay));
            Assert.That(actual.Aquifer, Is.EqualTo(expected.Aquifer));
        }

        /// <summary>2つの補正結果が入力ポテンシャルと導出2値のすべてで一致することを検証します。</summary>
        /// <param name="actual">製品コードから得た実際の補正結果です。</param>
        /// <param name="expected">テストが要求する補正結果です。</param>
        private static void AssertAdjustedGeologyEqual(
            in AdjustedGroundwaterGeology actual,
            in AdjustedGroundwaterGeology expected)
        {
            AssertPotentialsEqual(actual.Potentials, expected.Potentials);
            Assert.That(actual.Permeability, Is.EqualTo(expected.Permeability));
            Assert.That(actual.GroundwaterStorage, Is.EqualTo(expected.GroundwaterStorage));
        }
    }
}
