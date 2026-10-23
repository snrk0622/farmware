// このファイルは、旧7Biome用の分布監査がC5-C後の基礎生成を誤集計せず、明示停止することを検証します。
// 新18Biomeの候補率・全153ペア境界監査はロードマップどおりC5-Eで置換します。

using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// C5-E前に旧監査を実行してBiome=Noneのレポートを正常結果として扱う退行を検出します。
    /// </summary>
    public sealed class WorldGenerationDistributionAuditTests
    {
        /// <summary>
        /// 現行GeneratorVersionで旧監査を要求し、C5-Eへの置換前は明示的に拒否されることを保証します。
        /// </summary>
        [Test]
        public void Analyze_CurrentGeneratorVersion_IsDisabledUntilC5EReplacement()
        {
            WorldGenerationDistributionAuditSettings settings =
                new WorldGenerationDistributionAuditSettings(
                    new[] { "audit-test-a", "audit-test-b" },
                    4,
                    32,
                    WorldGenerationSettings.CurrentGeneratorVersion);

            Assert.That(
                () => WorldGenerationDistributionAnalyzer.Analyze(settings),
                Throws.TypeOf<System.NotSupportedException>());
        }
    }
}
