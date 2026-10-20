// このファイルは、TERRAIN-01C1で追加した連続環境軸の整数式、境界、決定論、失敗条件をEditModeで検証します。
// 地形生成Jobや開発者表示が同じ純粋計算を使えることを、少数の固定入力とチャンクハッシュ比較で保証します。

using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 基礎・地形気温、基礎湿度、標高区分、木本植生、起伏、海洋影響、火山活動度の契約を検証します。
    /// Unity Test RunnerのEditModeから実行し、SceneやGameObjectは作成しません。
    /// </summary>
    public sealed class DeterministicEnvironmentGeneratorTests
    {
        /// <summary>
        /// TERRAIN-01Bの確定値から既定設定を作り、全境界・距離・ノイズ規模が正しく保存されることを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchTerrain01BContract()
        {
            WorldGenerationSettings settings = CreateSettings("environment-defaults");
            ContinuousEnvironmentSettings axes = settings.EnvironmentAxes;

            Assert.That(DeterministicEnvironmentGenerator.ValidateSettings(settings), Is.True);
            Assert.That(axes.MaximumElevationCooling, Is.EqualTo(32768));
            Assert.That(axes.ColdTemperatureThreshold, Is.EqualTo(12000));
            Assert.That(axes.TemperateTemperatureThreshold, Is.EqualTo(32768));
            Assert.That(axes.DryMoistureThreshold, Is.EqualTo(16000));
            Assert.That(axes.MountainElevationThreshold, Is.EqualTo(43000));
            Assert.That(axes.AlpineElevationThreshold, Is.EqualTo(54000));
            Assert.That(axes.WoodyVegetationCellSizeInTiles, Is.EqualTo(192));
            Assert.That(axes.WoodyVegetationOctaveCount, Is.EqualTo(3));
            Assert.That(axes.RuggednessSampleRadiusInTiles, Is.EqualTo(16));
            Assert.That(axes.RuggednessDiagonalOffsetInTiles, Is.EqualTo(11));
            Assert.That(axes.RuggednessMaximumRelief, Is.EqualTo(16384));
            Assert.That(axes.CoastalNearDistanceInTiles, Is.EqualTo(16));
            Assert.That(axes.CoastalMidDistanceInTiles, Is.EqualTo(32));
            Assert.That(axes.CoastalFarDistanceInTiles, Is.EqualTo(64));
            Assert.That(axes.CoastalMaximumDistanceInTiles, Is.EqualTo(128));
            Assert.That(axes.CoastalNearDiagonalOffsetInTiles, Is.EqualTo(11));
            Assert.That(axes.CoastalMidDiagonalOffsetInTiles, Is.EqualTo(23));
            Assert.That(axes.CoastalFarDiagonalOffsetInTiles, Is.EqualTo(45));
            Assert.That(axes.CoastalMaximumDiagonalOffsetInTiles, Is.EqualTo(91));
            Assert.That(axes.VolcanicActivityCellSizeInTiles, Is.EqualTo(256));
            Assert.That(axes.VolcanicActivityOctaveCount, Is.EqualTo(3));
        }

        /// <summary>
        /// 海面以下、既定高地境界、最高標高、0飽和を準備し、標高寒冷補正を64bit整数式で一度だけ適用することを保証します。
        /// </summary>
        [Test]
        public void TerrainTemperature_AppliesElevationCoolingOnceAndSaturatesAtZero()
        {
            const ushort seaLevel = WorldGenerationSettings.DefaultSeaLevelThreshold;
            const ushort maximumCooling = 32768;

            Assert.That(
                DeterministicEnvironmentGenerator.CalculateTerrainTemperature(45000, 19999, seaLevel, maximumCooling),
                Is.EqualTo(45000));
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateTerrainTemperature(45000, seaLevel, seaLevel, maximumCooling),
                Is.EqualTo(45000));
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateTerrainTemperature(45000, 43000, seaLevel, maximumCooling),
                Is.EqualTo(28449));
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateTerrainTemperature(ushort.MaxValue, ushort.MaxValue, seaLevel, maximumCooling),
                Is.EqualTo(32767));
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateTerrainTemperature(10000, ushort.MaxValue, seaLevel, maximumCooling),
                Is.Zero);
        }

        /// <summary>
        /// 気温・湿度・標高・共通環境軸の全境界両側を入力し、隙間や重複なしで期待する段階へ入ることを保証します。
        /// </summary>
        [Test]
        public void EnvironmentBands_ClassifyAllBoundaryPairs()
        {
            WorldGenerationSettings settings = CreateSettings("environment-bands");

            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(11999, settings), Is.EqualTo(TerrainTemperatureBand.ExtremeCold));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(12000, settings), Is.EqualTo(TerrainTemperatureBand.Cold));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(22999, settings), Is.EqualTo(TerrainTemperatureBand.Cold));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(23000, settings), Is.EqualTo(TerrainTemperatureBand.Cool));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(32767, settings), Is.EqualTo(TerrainTemperatureBand.Cool));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(32768, settings), Is.EqualTo(TerrainTemperatureBand.Temperate));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(39999, settings), Is.EqualTo(TerrainTemperatureBand.Temperate));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(40000, settings), Is.EqualTo(TerrainTemperatureBand.Hot));

            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(15999, settings), Is.EqualTo(BaseMoistureBand.ExtremelyDry));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(16000, settings), Is.EqualTo(BaseMoistureBand.Dry));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(28499, settings), Is.EqualTo(BaseMoistureBand.Dry));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(28500, settings), Is.EqualTo(BaseMoistureBand.Moderate));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(39499, settings), Is.EqualTo(BaseMoistureBand.Moderate));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(39500, settings), Is.EqualTo(BaseMoistureBand.Wet));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(49999, settings), Is.EqualTo(BaseMoistureBand.Wet));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyBaseMoisture(50000, settings), Is.EqualTo(BaseMoistureBand.ExtremelyWet));

            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(13106), Is.EqualTo(ContinuousEnvironmentBand.Level1));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(13107), Is.EqualTo(ContinuousEnvironmentBand.Level2));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(26213), Is.EqualTo(ContinuousEnvironmentBand.Level2));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(26214), Is.EqualTo(ContinuousEnvironmentBand.Level3));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(39320), Is.EqualTo(ContinuousEnvironmentBand.Level3));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(39321), Is.EqualTo(ContinuousEnvironmentBand.Level4));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(52427), Is.EqualTo(ContinuousEnvironmentBand.Level4));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(52428), Is.EqualTo(ContinuousEnvironmentBand.Level5));

            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(19999, settings), Is.EqualTo(ElevationClass.BelowSeaLevel));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(20000, settings), Is.EqualTo(ElevationClass.Lowland));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(36000, settings), Is.EqualTo(ElevationClass.Lowland));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(36001, settings), Is.EqualTo(ElevationClass.PlainOrPlateau));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(42999, settings), Is.EqualTo(ElevationClass.PlainOrPlateau));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(43000, settings), Is.EqualTo(ElevationClass.Mountain));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(53999, settings), Is.EqualTo(ElevationClass.Mountain));
            Assert.That(DeterministicEnvironmentGenerator.ClassifyElevation(54000, settings), Is.EqualTo(ElevationClass.Alpine));
        }

        /// <summary>
        /// 局所標高差の端点を入力し、16,384以上だけが65,535へ飽和し、直前値は飽和しないことを保証します。
        /// </summary>
        [Test]
        public void TerrainRuggedness_UsesFixedReliefScaleAndSaturation()
        {
            Assert.That(DeterministicEnvironmentGenerator.CalculateTerrainRuggedness(10000, 10000, 16384), Is.Zero);
            Assert.That(DeterministicEnvironmentGenerator.CalculateTerrainRuggedness(10000, 26383, 16384), Is.EqualTo(65531));
            Assert.That(DeterministicEnvironmentGenerator.CalculateTerrainRuggedness(10000, 26384, 16384), Is.EqualTo(ushort.MaxValue));
            Assert.That(DeterministicEnvironmentGenerator.CalculateTerrainRuggedness(0, ushort.MaxValue, 16384), Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 直線海岸3方向と半島・小島6方向の確定入力を合成し、仕様に記載した海洋影響値になることを保証します。
        /// </summary>
        [Test]
        public void CoastalInfluence_CombinesNearestSeaAndDirectionalExposure()
        {
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateCoastalInfluence(ushort.MaxValue, 3, 3),
                Is.EqualTo(34815));
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateCoastalInfluence(ushort.MaxValue, 6, 3),
                Is.EqualTo(53247));
            Assert.That(
                DeterministicEnvironmentGenerator.CalculateCoastalInfluence(0, 0, 3),
                Is.Zero);
        }

        /// <summary>
        /// 同じ設定・正負座標を複数回生成し、全軸とWarp後座標が同一でチャンク生成順を状態に持たないことを保証します。
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(63, 64)]
        [TestCase(-1, -64)]
        [TestCase(-65, 127)]
        public void EnvironmentGeneration_SameInputProducesSameAxes(int worldTileX, int worldTileY)
        {
            WorldGenerationSettings settings = CreateSettings("environment-determinism");

            bool firstSucceeded = DeterministicEnvironmentGenerator.TryGenerate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedEnvironmentData first);
            bool secondSucceeded = DeterministicEnvironmentGenerator.TryGenerate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedEnvironmentData second);

            Assert.That(firstSucceeded, Is.True);
            Assert.That(secondSucceeded, Is.True);
            AssertEnvironmentEquals(first, second);
        }

        /// <summary>
        /// 基礎湿度の格子設定だけを変え、木本植生・起伏・海洋影響・火山活動度へ湿度を二重合成しないことを保証します。
        /// </summary>
        [Test]
        public void EnvironmentGeneration_MoistureSettingDoesNotChangeIndependentAxes()
        {
            WorldGenerationSettings firstSettings = CreateSettings("environment-independent-axes");
            WorldGenerationSettings secondSettings = firstSettings;
            secondSettings.MoistureCellSizeInTiles = 17;

            Assert.That(DeterministicEnvironmentGenerator.TryGenerate(firstSettings, 217, -149, out GeneratedEnvironmentData first), Is.True);
            Assert.That(DeterministicEnvironmentGenerator.TryGenerate(secondSettings, 217, -149, out GeneratedEnvironmentData second), Is.True);

            Assert.That(second.WoodyVegetationPotential, Is.EqualTo(first.WoodyVegetationPotential));
            Assert.That(second.TerrainRuggedness, Is.EqualTo(first.TerrainRuggedness));
            Assert.That(second.CoastalInfluence, Is.EqualTo(first.CoastalInfluence));
            Assert.That(second.VolcanicActivityPotential, Is.EqualTo(first.VolcanicActivityPotential));
        }

        /// <summary>
        /// int端から最大128タイルを参照する入力と除算不能設定を準備し、周回や暗黙補正をせず失敗することを保証します。
        /// </summary>
        [Test]
        public void EnvironmentGeneration_InvalidSettingsOrWorldEdgeFailsExplicitly()
        {
            WorldGenerationSettings settings = CreateSettings("environment-invalid-input");

            Assert.That(DeterministicEnvironmentGenerator.TryGenerate(settings, int.MaxValue, 0, out _), Is.False);
            Assert.That(DeterministicEnvironmentGenerator.TryGenerate(settings, int.MinValue, 0, out _), Is.False);

            settings.SeaLevelThreshold = ushort.MaxValue;
            Assert.That(DeterministicEnvironmentGenerator.ValidateSettings(settings), Is.False);
            Assert.That(DeterministicEnvironmentGenerator.TryGenerate(settings, 0, 0, out _), Is.False);

            settings = CreateSettings("environment-invalid-relief");
            settings.EnvironmentAxes.RuggednessMaximumRelief = 0;
            Assert.That(DeterministicEnvironmentGenerator.ValidateSettings(settings), Is.False);

            settings = CreateSettings("environment-inconsistent-mountain-threshold");
            ContinuousEnvironmentSettings inconsistentAxes = settings.EnvironmentAxes;
            inconsistentAxes.MountainElevationThreshold++;
            settings.EnvironmentAxes = inconsistentAxes;
            Assert.That(
                DeterministicEnvironmentGenerator.ValidateSettings(settings),
                Is.False,
                "旧7Biome移行期間は高地判定と新しい山地区分の境界を分離してはいけません。");
        }

        /// <summary>
        /// 旧Weirdnessと新しい火山活動度を複数座標で生成し、別Saltの独立軸として同じ乱数列を共有しないことを保証します。
        /// </summary>
        [Test]
        public void GeneratedTile_VolcanicActivityDoesNotAliasLegacyWeirdness()
        {
            WorldGenerationSettings settings = CreateSettings("volcanic-salt-separation");
            int differentValueCount = 0;

            for (int sampleIndex = 0; sampleIndex < 16; sampleIndex++)
            {
                GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                    settings,
                    (sampleIndex * 73) - 500,
                    (sampleIndex * -47) + 300);
                if (tile.Weirdness != tile.VolcanicActivityPotential)
                {
                    differentValueCount++;
                }
            }

            Assert.That(differentValueCount, Is.GreaterThanOrEqualTo(15));
        }

        /// <summary>
        /// 木本植生の格子サイズだけを変えた2設定で同じチャンクを生成し、新しい軸が状態ハッシュへ含まれることを保証します。
        /// </summary>
        [Test]
        public void ChunkHash_ChangesWhenContinuousEnvironmentSettingChanges()
        {
            WorldGenerationSettings firstSettings = CreateSettings("environment-hash");
            WorldGenerationSettings secondSettings = firstSettings;
            secondSettings.EnvironmentAxes.WoodyVegetationCellSizeInTiles = 96;
            ChunkCoordinate coordinate = new ChunkCoordinate(-2, 3);

            ulong firstHash = DeterministicChunkGenerator.ComputeChunkHash(firstSettings, coordinate);
            ulong secondHash = DeterministicChunkGenerator.ComputeChunkHash(secondSettings, coordinate);

            Assert.That(secondHash, Is.Not.EqualTo(firstHash));
        }

        /// <summary>テスト名ごとの安定Seedと現在の確定既定値から、検証対象設定を作ります。</summary>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seedText), 2);
        }

        /// <summary>2つの生成結果に含まれる全連続軸とWarp後座標が一致することを項目別に報告します。</summary>
        private static void AssertEnvironmentEquals(
            GeneratedEnvironmentData expected,
            GeneratedEnvironmentData actual)
        {
            Assert.That(actual.Elevation, Is.EqualTo(expected.Elevation));
            Assert.That(actual.BaseTemperature, Is.EqualTo(expected.BaseTemperature));
            Assert.That(actual.TerrainTemperature, Is.EqualTo(expected.TerrainTemperature));
            Assert.That(actual.BaseMoisture, Is.EqualTo(expected.BaseMoisture));
            Assert.That(actual.WoodyVegetationPotential, Is.EqualTo(expected.WoodyVegetationPotential));
            Assert.That(actual.TerrainRuggedness, Is.EqualTo(expected.TerrainRuggedness));
            Assert.That(actual.CoastalInfluence, Is.EqualTo(expected.CoastalInfluence));
            Assert.That(actual.VolcanicActivityPotential, Is.EqualTo(expected.VolcanicActivityPotential));
            Assert.That(actual.WarpedSampleX, Is.EqualTo(expected.WarpedSampleX));
            Assert.That(actual.WarpedSampleY, Is.EqualTo(expected.WarpedSampleY));
        }
    }
}
