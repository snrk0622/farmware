// このファイルは、動物個体群の複数Seed・複数地域監査が再現可能で、群れと位置の契約違反を検出しないことを保証します。
// 開発者メニューより小さい範囲を使い、通常のEditModeテスト時間で実際の地形生成と候補生成を縦に確認します。

using NUnit.Framework;
using Farmware.WorldGeneration.Editor;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>AnimalPopulationDistributionAnalyzerの集計と決定論を確認するEditModeテストです。</summary>
    public sealed class AnimalPopulationDistributionAuditTests
    {
        /// <summary>
        /// 負座標を含む2組のSeed・18地域を2回監査し、署名、件数、群れ、位置重複が安定することを保証します。
        /// </summary>
        [Test]
        public void Analyze_IsRepeatableAndKeepsPopulationContracts()
        {
            AnimalPopulationDistributionAuditSettings settings =
                new AnimalPopulationDistributionAuditSettings(
                    new[] { "animal-audit-test-a", "animal-audit-test-b" },
                    new[] { 0x1029384756ABCDEFUL, 0xFEDCBA6547382910UL },
                    regionRadius: 1,
                    generatorVersion: 1);

            AnimalPopulationDistributionAuditResult first =
                AnimalPopulationDistributionAnalyzer.Analyze(settings);
            AnimalPopulationDistributionAuditResult second =
                AnimalPopulationDistributionAnalyzer.Analyze(settings);

            Assert.That(first.SeedPairCount, Is.EqualTo(2));
            Assert.That(first.RegionCount, Is.EqualTo(18));
            Assert.That(first.EligibleTileCount, Is.GreaterThan(0));
            Assert.That(first.TargetPopulation, Is.GreaterThan(0));
            Assert.That(first.SpawnedPopulation, Is.GreaterThan(0));
            Assert.That(first.InvalidHerdCount, Is.Zero);
            Assert.That(first.DuplicatePositionCount, Is.Zero);
            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.EligibleTileCount, Is.EqualTo(first.EligibleTileCount));
            Assert.That(second.TargetPopulation, Is.EqualTo(first.TargetPopulation));
            Assert.That(second.SpawnedPopulation, Is.EqualTo(first.SpawnedPopulation));
        }
    }
}
