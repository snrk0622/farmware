// このファイルは、有効Blockセルと通常設備・通常ベルト・橋定義を組み合わせる共通配置可否マトリクスをEditModeで検証します。
// 建築Commandと開発者用可視化が、旧分類ではなく同じTerrainPlacementRulesとDefinition属性を共有することを保証します。

using Farmware.Simulation;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>地面、水域、マグマ、岩壁に対する配置要求と異常入力を純粋計算で保証します。</summary>
    public sealed class TerrainPlacementRulesTests
    {
        /// <summary>テスト入力で作るBlockセル構成を、製品の旧分類へ依存しない名前で区別します。</summary>
        public enum TerrainScenario : byte
        {
            /// <summary>標準GroundBlockだけを持つセルです。</summary>
            Land = 0,

            /// <summary>淡水Liquidだけを持つセルです。</summary>
            FreshWater = 1,

            /// <summary>マグマLiquidだけを持つセルです。</summary>
            Magma = 2,

            /// <summary>GroundBlockと自然WallBlockを持つセルです。</summary>
            RockWall = 3
        }

        /// <summary>4つのBlockセル構成と4配置要求の16組が、Definition属性どおりの結果になることを保証します。</summary>
        /// <param name="scenario">評価する地面、淡水、マグマ、岩壁のセル構成です。</param>
        /// <param name="placementKind">配置を試す通常設備、通常ベルト、橋のいずれかです。</param>
        /// <param name="hasHeatResistance">橋定義が耐熱性を持つ場合はtrueです。</param>
        /// <param name="expectedResult">仕様で期待する地形配置判定結果です。</param>
        [TestCase(TerrainScenario.Land, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.Allowed)]
        [TestCase(TerrainScenario.Land, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.Allowed)]
        [TestCase(TerrainScenario.Land, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.Land, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.FreshWater, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.FreshWater, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.FreshWater, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.Allowed)]
        [TestCase(TerrainScenario.FreshWater, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.Allowed)]
        [TestCase(TerrainScenario.Magma, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.Magma, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.Magma, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.HeatResistanceRequired)]
        [TestCase(TerrainScenario.Magma, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.Allowed)]
        [TestCase(TerrainScenario.RockWall, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.RockWall, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.RockWall, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(TerrainScenario.RockWall, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        public void Evaluate_ReturnsSpecifiedPlacementMatrix(
            TerrainScenario scenario,
            TerrainPlacementKind placementKind,
            bool hasHeatResistance,
            TerrainPlacementResult expectedResult)
        {
            using TerrainGenerationTestCatalogSet catalogs =
                TerrainGenerationTestCatalogSet.Create();
            EffectiveTerrainCell cell = CreateCell(scenario, catalogs.RuntimeIds);

            TerrainPlacementResult actualResult = TerrainPlacementRules.Evaluate(
                cell,
                catalogs.TerrainCatalog,
                placementKind,
                hasHeatResistance);

            Assert.That(actualResult, Is.EqualTo(expectedResult));
        }

        /// <summary>未知の配置対象を有効な地面へ入力し、許可へ倒さず未対応結果を返すことを保証します。</summary>
        [Test]
        public void Evaluate_RejectsUnsupportedPlacementKind()
        {
            using TerrainGenerationTestCatalogSet catalogs =
                TerrainGenerationTestCatalogSet.Create();
            EffectiveTerrainCell land = CreateCell(TerrainScenario.Land, catalogs.RuntimeIds);

            Assert.That(
                TerrainPlacementRules.Evaluate(
                    land,
                    catalogs.TerrainCatalog,
                    TerrainPlacementKind.None),
                Is.EqualTo(TerrainPlacementResult.UnsupportedPlacementKind));
            Assert.That(
                TerrainPlacementRules.Evaluate(
                    land,
                    catalogs.TerrainCatalog,
                    (TerrainPlacementKind)byte.MaxValue),
                Is.EqualTo(TerrainPlacementResult.UnsupportedPlacementKind));
        }

        /// <summary>テスト用の地形シナリオを、実カタログのRuntime IDを持つ有効セルへ変換します。</summary>
        /// <param name="scenario">作成する地面、淡水、マグマ、岩壁の区別です。</param>
        /// <param name="runtimeIds">実カタログから解決した組み込みIDです。</param>
        /// <returns>配置判定へ直接渡せる有効地形セルです。</returns>
        private static EffectiveTerrainCell CreateCell(
            TerrainScenario scenario,
            in BuiltInTerrainBlockRuntimeIds runtimeIds)
        {
            switch (scenario)
            {
                case TerrainScenario.Land:
                    return new EffectiveTerrainCell
                    {
                        Biome = BiomeId.TemperateGrassland,
                        GroundBlockRuntimeId = runtimeIds.LandGround
                    };
                case TerrainScenario.FreshWater:
                    return new EffectiveTerrainCell
                    {
                        Biome = BiomeId.TemperateGrassland,
                        LiquidRuntimeId = runtimeIds.FreshWater
                    };
                case TerrainScenario.Magma:
                    return new EffectiveTerrainCell
                    {
                        Biome = BiomeId.VolcanicGeothermal,
                        LiquidRuntimeId = runtimeIds.Magma
                    };
                case TerrainScenario.RockWall:
                    return new EffectiveTerrainCell
                    {
                        Biome = BiomeId.AlpineRockland,
                        GroundBlockRuntimeId = runtimeIds.LandGround,
                        WallBlockRuntimeId = runtimeIds.NaturalRockWall
                    };
                default:
                    return default;
            }
        }
    }
}
