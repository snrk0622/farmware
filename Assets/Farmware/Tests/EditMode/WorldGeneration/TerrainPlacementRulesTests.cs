// このファイルは、地形属性と通常設備・通常ベルト・橋定義を組み合わせる共通配置可否マトリクスをEditModeで検証します。
// 生成器、初期設備領域探索、後続の建築Command、開発者用可視化が同じTerrainPlacementRulesを安全に共有できることを保証します。

using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 陸地、水域、マグマ、岩壁に対する4種類の配置要求と、異常入力を純粋計算で保証します。
    /// Unity Test RunnerのEditModeテストから実行されます。
    /// </summary>
    public sealed class TerrainPlacementRulesTests
    {
        /// <summary>
        /// 4地形と通常設備・通常ベルト・非耐熱橋・耐熱橋の16組を準備し、仕様どおりの結果になることを保証します。
        /// 生成済み属性を入力に使うことで、GroundTypeを呼び出し側が再解釈せず共通APIだけで判定できることも確認します。
        /// </summary>
        /// <param name="groundType">配置属性を作る基礎地形です。</param>
        /// <param name="placementKind">配置を試す通常設備、通常ベルト、橋のいずれかです。</param>
        /// <param name="hasHeatResistance">橋定義が耐熱性を持つ場合はtrueです。</param>
        /// <param name="expectedResult">仕様で期待する地形配置判定結果です。</param>
        [TestCase(GroundType.Land, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.Allowed)]
        [TestCase(GroundType.Land, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.Allowed)]
        [TestCase(GroundType.Land, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.Land, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.Water, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.Water, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.Water, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.Allowed)]
        [TestCase(GroundType.Water, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.Allowed)]
        [TestCase(GroundType.Magma, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.Magma, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.Magma, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.HeatResistanceRequired)]
        [TestCase(GroundType.Magma, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.Allowed)]
        [TestCase(GroundType.RockWall, TerrainPlacementKind.NormalEquipment, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.RockWall, TerrainPlacementKind.NormalBelt, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.RockWall, TerrainPlacementKind.Bridge, false, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        [TestCase(GroundType.RockWall, TerrainPlacementKind.Bridge, true, TerrainPlacementResult.TerrainDoesNotAllowPlacement)]
        public void Evaluate_ReturnsSpecifiedPlacementMatrix(
            GroundType groundType,
            TerrainPlacementKind placementKind,
            bool hasHeatResistance,
            TerrainPlacementResult expectedResult)
        {
            TileGameplayFlags gameplayFlags =
                DeterministicChunkGenerator.ClassifyGameplayFlags(groundType);

            TerrainPlacementResult actualResult = TerrainPlacementRules.Evaluate(
                gameplayFlags,
                placementKind,
                hasHeatResistance);

            Assert.That(
                actualResult,
                Is.EqualTo(expectedResult),
                $"{groundType}への{placementKind}配置で耐熱性={hasHeatResistance}の結果が仕様と異なります。");
        }

        /// <summary>
        /// マグマ属性を持つGeneratedTileへ非耐熱橋と耐熱橋を順に評価し、入力タイルを変更せず不足理由だけを区別することを保証します。
        /// </summary>
        [Test]
        public void Evaluate_UsesGeneratedTileWithoutMutatingIt()
        {
            GeneratedTile magmaTile = new GeneratedTile
            {
                Ground = GroundType.Magma,
                GameplayFlags = TileGameplayFlags.AllowsBridge
                    | TileGameplayFlags.RequiresHeatResistance
            };
            GeneratedTile originalTile = magmaTile;

            TerrainPlacementResult normalBridgeResult = TerrainPlacementRules.Evaluate(
                magmaTile,
                TerrainPlacementKind.Bridge,
                hasHeatResistance: false);
            TerrainPlacementResult heatResistantBridgeResult = TerrainPlacementRules.Evaluate(
                magmaTile,
                TerrainPlacementKind.Bridge,
                hasHeatResistance: true);

            Assert.That(
                normalBridgeResult,
                Is.EqualTo(TerrainPlacementResult.HeatResistanceRequired));
            Assert.That(
                heatResistantBridgeResult,
                Is.EqualTo(TerrainPlacementResult.Allowed));
            Assert.That(magmaTile.Ground, Is.EqualTo(originalTile.Ground));
            Assert.That(magmaTile.GameplayFlags, Is.EqualTo(originalTile.GameplayFlags));
        }

        /// <summary>
        /// 未定義の配置対象と、橋を許可しない不整合な耐熱要件だけの属性を準備し、安全側の結果へ倒れることを保証します。
        /// </summary>
        [Test]
        public void Evaluate_RejectsUnsupportedOrIncompleteInputs()
        {
            TerrainPlacementResult unspecifiedKindResult = TerrainPlacementRules.Evaluate(
                TileGameplayFlags.AllowsNormalEquipment,
                TerrainPlacementKind.None);
            TerrainPlacementResult unknownKindResult = TerrainPlacementRules.Evaluate(
                TileGameplayFlags.AllowsNormalEquipment,
                (TerrainPlacementKind)byte.MaxValue);
            TerrainPlacementResult incompleteBridgeFlagsResult = TerrainPlacementRules.Evaluate(
                TileGameplayFlags.RequiresHeatResistance,
                TerrainPlacementKind.Bridge,
                hasHeatResistance: true);

            Assert.That(
                unspecifiedKindResult,
                Is.EqualTo(TerrainPlacementResult.UnsupportedPlacementKind));
            Assert.That(
                unknownKindResult,
                Is.EqualTo(TerrainPlacementResult.UnsupportedPlacementKind));
            Assert.That(
                incompleteBridgeFlagsResult,
                Is.EqualTo(TerrainPlacementResult.TerrainDoesNotAllowPlacement));
        }
    }
}
