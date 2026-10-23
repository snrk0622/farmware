// このファイルは、動物個体群の複数Seed・複数地域監査が再現可能で、群れと位置の契約違反を検出しないことを保証します。
// 開発者メニューより小さい範囲を使い、通常のEditModeテスト時間で実際の地形生成と候補生成を縦に確認します。

using System;
using NUnit.Framework;
using Farmware.WorldGeneration.Editor;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>AnimalPopulationDistributionAnalyzerの集計と決定論を確認するEditModeテストです。</summary>
    public sealed class AnimalPopulationDistributionAuditTests
    {
        /// <summary>
        /// C5前の生成器版を監査設定へ渡し、旧7Biome結果を新18Biome分布として混在させる前に拒否することを保証します。
        /// </summary>
        [Test]
        public void Settings_PreC5GeneratorVersionIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AnimalPopulationDistributionAuditSettings(
                    new[] { "legacy-animal-audit" },
                    new[] { 0x1029384756ABCDEFUL },
                    regionRadius: 0,
                    generatorVersion: WorldGenerationSettings.CurrentGeneratorVersion - 1));
        }

        /// <summary>
        /// 固定1 Seed・1地域を2回監査し、署名、件数、群れ、位置重複が安定することを保証します。
        /// C5-C以降は1地域ごとにC4完成水文まで生成するため、広域の件数は手動監査へ分けます。
        /// </summary>
        [Test]
        [Timeout(120000)]
        public void Analyze_IsRepeatableAndKeepsPopulationContracts()
        {
            AnimalPopulationDistributionAuditSettings settings =
                new AnimalPopulationDistributionAuditSettings(
                    new[] { "animal-audit-grassland" },
                    new[] { 0x1029384756ABCDEFUL },
                    regionRadius: 0,
                    generatorVersion: WorldGenerationSettings.CurrentGeneratorVersion);

            AnimalPopulationDistributionAuditResult first =
                AnimalPopulationDistributionAnalyzer.Analyze(settings);
            AnimalPopulationDistributionAuditResult second =
                AnimalPopulationDistributionAnalyzer.Analyze(settings);

            Assert.That(first.SeedPairCount, Is.EqualTo(1));
            Assert.That(first.RegionCount, Is.EqualTo(1));
            Assert.That(first.EligibleTileCount, Is.GreaterThan(0));
            // 18Biome化後は原点1チャンク内の温帯草原が密度換算で1羽未満になる場合があります。
            // 小数を切り上げて分布を水増しせず、0を含む目標数と配置数の包含関係を監査します。
            Assert.That(first.TargetPopulation, Is.GreaterThanOrEqualTo(0));
            Assert.That(first.SpawnedPopulation, Is.InRange(0, first.TargetPopulation));
            Assert.That(first.InvalidHerdCount, Is.Zero);
            Assert.That(first.DuplicatePositionCount, Is.Zero);
            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.EligibleTileCount, Is.EqualTo(first.EligibleTileCount));
            Assert.That(second.TargetPopulation, Is.EqualTo(first.TargetPopulation));
            Assert.That(second.SpawnedPopulation, Is.EqualTo(first.SpawnedPopulation));
        }
    }
}
