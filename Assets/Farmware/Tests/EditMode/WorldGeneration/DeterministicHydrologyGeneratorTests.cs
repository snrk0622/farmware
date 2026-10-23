// このファイルは、TERRAIN-01C3-A〜D1の水文格子、D8流向、下流終端、集水、自然流出、閉鎖流域、基礎湖面を検証します。
// 診断表示は後続commitの責務とし、固定水文結果が決定論的かつ有限になることを保証します。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 固定8×8格子、D8辺、下流終端、集水・自然流出、閉鎖流域、湖面、Level 0〜6集約Summaryを検証するEditModeテストです。
    /// 純粋GeneratorとChunkGenerationSystemが共有する入力・出力契約を対象にします。
    /// </summary>
    public sealed class DeterministicHydrologyGeneratorTests
    {
        /// <summary>
        /// 製品既定値を準備し、8タイル格子、3タイルサンプル、1チャンク64セルが確定仕様と一致することを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchTerrain01C3DContract()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-defaults");

            Assert.That(DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology), Is.True);
            Assert.That(settings.Hydrology.CellSizeInTiles, Is.EqualTo(8));
            Assert.That(settings.Hydrology.RepresentativeSampleOffsetInTiles, Is.EqualTo(3));
            Assert.That(settings.Hydrology.MaximumDownstreamEdges, Is.EqualTo(4096));
            Assert.That(settings.Hydrology.MaximumDownstreamSummaryLevel, Is.EqualTo(12));
            Assert.That(settings.Hydrology.MaximumCatchmentContributingCells, Is.EqualTo(4096));
            Assert.That(settings.Hydrology.MaximumAggregationSummaryLevel, Is.EqualTo(6));
            Assert.That(settings.Hydrology.StreamRunoffEquivalentThreshold, Is.EqualTo(16));
            Assert.That(settings.Hydrology.RiverRunoffEquivalentThreshold, Is.EqualTo(128));
            Assert.That(settings.Hydrology.MinimumClosedBasinAreaCells, Is.EqualTo(4));
            Assert.That(settings.Hydrology.LakeRunoffEquivalentThreshold, Is.EqualTo(8));
            Assert.That(settings.Hydrology.LakeOverflowRunoffEquivalentThreshold, Is.EqualTo(32));
            Assert.That(settings.Hydrology.LakeFillFractionMultiplier, Is.EqualTo(2048));
            Assert.That(DeterministicHydrologyGenerator.CellsPerChunkAxis, Is.EqualTo(8));
            Assert.That(DeterministicHydrologyGenerator.CellsPerChunk, Is.EqualTo(64));
        }

        /// <summary>
        /// ECS Bufferと生成作業枠の容量見積もりが、集水・湖面結果を含むUnityレイアウト96 bytesと一致することを保証します。
        /// 後続PRでフィールドを追加した場合に、性能文書と容量見積もりの更新漏れを検出します。
        /// </summary>
        [Test]
        public void GeneratedHydrologyCell_SizeMatchesCapacityDocumentation()
        {
            Assert.That(UnsafeUtility.SizeOf<GeneratedHydrologyCell>(), Is.EqualTo(96));
            Assert.That(
                typeof(GeneratedHydrologyCell).GetField(nameof(GeneratedHydrologyCell.RunoffMoistureSum))?.FieldType,
                Is.EqualTo(typeof(long)));
            Assert.That(UnsafeUtility.SizeOf<HydrologyFlowCandidate>(), Is.EqualTo(24));
            Assert.That(UnsafeUtility.SizeOf<HydrologyUpstreamCandidate>(), Is.EqualTo(24));
            Assert.That(UnsafeUtility.SizeOf<HydrologyBasinLookupSlot>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<HydrologyLakeChunkWorkItem>(), Is.EqualTo(16));

            FixedList512Bytes<HydrologyFlowCandidate> candidates = default;
            Assert.That(candidates.Capacity, Is.GreaterThanOrEqualTo(8));
        }

        /// <summary>
        /// 渓流・河川境界の直前と直後を入力し、0〜15は未固定、16〜127はStream、128以上はStreamとRiverになることを保証します。
        /// </summary>
        [TestCase(15, HydrologyConnectionFlags.None)]
        [TestCase(16, HydrologyConnectionFlags.Stream)]
        [TestCase(127, HydrologyConnectionFlags.Stream)]
        [TestCase(128, HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River)]
        [TestCase(4096, HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River)]
        public void SurfaceWaterClassification_UsesFixedRunoffBoundaries(
            int runoffEquivalentCells,
            HydrologyConnectionFlags expected)
        {
            HydrologyGenerationSettings settings = HydrologyGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicHydrologyCatchmentResolver.ClassifySurfaceWaterConnection(
                    settings,
                    (ushort)runoffEquivalentCells),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// 湖面充填率の境界直前・直後を準備し、8で約25%、16で約50%、32以上で最大へ飽和することを保証します。
        /// </summary>
        [TestCase(7, 14336)]
        [TestCase(8, 16384)]
        [TestCase(16, 32768)]
        [TestCase(31, 63488)]
        [TestCase(32, 65535)]
        [TestCase(4096, 65535)]
        public void LakeFillFraction_UsesFixedIntegerMultiplierAndSaturation(
            int runoffEquivalentCells,
            int expectedFillFraction)
        {
            HydrologyGenerationSettings settings = HydrologyGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicHydrologyLakeResolver.CalculateFillFraction(
                    settings,
                    (ushort)runoffEquivalentCells),
                Is.EqualTo(expectedFillFraction));
        }

        /// <summary>
        /// 閉鎖流域の打切り、3／4セル、流出7／8・31／32、正の水深を入力し、除外理由と越流境界を保証します。
        /// </summary>
        [TestCase(4096u, true, 4096, 100, 200, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.BasinTruncated)]
        [TestCase(3u, false, 4096, 100, 200, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.BasinTooSmall)]
        [TestCase(4u, false, 7, 100, 200, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.InsufficientRunoff)]
        [TestCase(4u, false, 8, 100, 100, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.NoPositiveDepth)]
        [TestCase(4u, false, 8, 100, 200, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.FixedLake)]
        [TestCase(4u, false, 31, 100, 200, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.FixedLake)]
        [TestCase(4u, false, 32, 100, 200, HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.Overflow)]
        public void LakeClassification_UsesFiniteBasinAndRunoffBoundaries(
            uint basinAreaCells,
            bool basinTruncated,
            int runoffEquivalentCells,
            int lakeBottomHeight,
            int spillHeight,
            HydrologyLakeFlags expected)
        {
            HydrologyGenerationSettings settings = HydrologyGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicHydrologyLakeResolver.ClassifyBasin(
                    settings,
                    basinAreaCells,
                    basinTruncated,
                    (ushort)runoffEquivalentCells,
                    (ushort)lakeBottomHeight,
                    (ushort)spillHeight),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// 同じ越流標高と外側FlowKeyを持つ候補を準備し、内側X・Yを外側座標より先に比較する仕様順を保証します。
        /// </summary>
        [Test]
        public void SpillOrder_UsesOutsideFlowKeyThenInsideAndOutsideCoordinates()
        {
            var earlierInside = new DeterministicHydrologyLakeResolver.HydrologySpillCandidate
            {
                SpillHeight = 200,
                OutsideHydrologyHeight = 180,
                OutsidePlateauRank = 400,
                InsideCellX = -5,
                InsideCellY = 7,
                OutsideCellX = 100,
                OutsideCellY = 100
            };
            var earlierOutside = new DeterministicHydrologyLakeResolver.HydrologySpillCandidate
            {
                SpillHeight = 200,
                OutsideHydrologyHeight = 180,
                OutsidePlateauRank = 400,
                InsideCellX = -4,
                InsideCellY = 7,
                OutsideCellX = -100,
                OutsideCellY = -100
            };
            var sameInsideEarlierOutside = earlierInside;
            sameInsideEarlierOutside.OutsideCellX = 99;

            Assert.That(
                DeterministicHydrologyLakeResolver.CompareSpillCandidates(earlierInside, earlierOutside),
                Is.LessThan(0));
            Assert.That(
                DeterministicHydrologyLakeResolver.CompareSpillCandidates(sameInsideEarlierOutside, earlierInside),
                Is.LessThan(0));
        }

        /// <summary>
        /// 同じ対象セルの集水を2回生成し、対象自身を含む件数、湿度合計、整数流出値、打切り状態が再現することを保証します。
        /// </summary>
        [Test]
        public void CatchmentResolution_IsFiniteAndDeterministic()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-catchment-deterministic");

            Assert.That(
                DeterministicHydrologyCatchmentResolver.TryResolve(settings, -37, 52, out HydrologyCatchmentResult first),
                Is.True);
            Assert.That(
                DeterministicHydrologyCatchmentResolver.TryResolve(settings, -37, 52, out HydrologyCatchmentResult repeated),
                Is.True);

            Assert.That(first.CatchmentAreaCells, Is.InRange(1u, 4096u));
            Assert.That(first.RunoffMoistureSum, Is.GreaterThanOrEqualTo(first.TargetHydrologyMoisture));
            Assert.That(first.RunoffMoistureSum, Is.LessThanOrEqualTo(4096L * ushort.MaxValue));
            Assert.That(first.RunoffEquivalentCells, Is.EqualTo(first.RunoffMoistureSum / ushort.MaxValue));
            Assert.That(first.CatchmentAreaCells, Is.EqualTo(repeated.CatchmentAreaCells));
            Assert.That(first.RunoffMoistureSum, Is.EqualTo(repeated.RunoffMoistureSum));
            Assert.That(first.RunoffEquivalentCells, Is.EqualTo(repeated.RunoffEquivalentCells));
            Assert.That(first.MaximumContributingDistance, Is.EqualTo(repeated.MaximumContributingDistance));
            Assert.That(first.CatchmentFlags, Is.EqualTo(repeated.CatchmentFlags));
            Assert.That(first.SurfaceWaterConnectionFlags, Is.EqualTo(repeated.SurfaceWaterConnectionFlags));
        }

        /// <summary>
        /// Level 0〜6を同じ負座標から構築し、1〜4,096セルの階層寸法、行優先セル数、湿度合計が個別生成と一致することを保証します。
        /// 集約不能な親Levelも失敗へ丸めず、終端・複数出口・再進入などの分解理由を保持することを確認します。
        /// </summary>
        [Test]
        public void AggregationSummary_LevelsZeroThroughSixPreserveCellTotals()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-aggregation-levels");
            using var cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Temp);
            using var distances = new NativeArray<ushort>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Temp);
            using var entrances = new NativeArray<HydrologyCatchmentExternalEntrance>(
                DeterministicHydrologyAggregationSummary.MaximumExternalEntranceCapacity,
                Allocator.Temp);

            for (int level = 0; level <= settings.Hydrology.MaximumAggregationSummaryLevel; level++)
            {
                Assert.That(
                    DeterministicHydrologyAggregationSummary.TryBuildAligned(
                        settings,
                        -17,
                        23,
                        level,
                        cells,
                        distances,
                        entrances,
                        out HydrologyCatchmentAggregationSummary summary),
                    Is.True,
                    $"Level {level}のSummaryを生成できませんでした。");

                int sideLength = 1 << level;
                int expectedCellCount = sideLength * sideLength;
                long expectedMoistureSum = 0L;
                for (int index = 0; index < expectedCellCount; index++)
                {
                    expectedMoistureSum += cells[index].HydrologyMoisture;
                }

                Assert.That(summary.Level, Is.EqualTo(level));
                Assert.That(summary.CellCount, Is.EqualTo((uint)expectedCellCount));
                Assert.That(summary.RunoffMoistureSum, Is.EqualTo(expectedMoistureSum));
                Assert.That(summary.MinimumCellX % sideLength, Is.Zero);
                Assert.That(summary.MinimumCellY % sideLength, Is.Zero);
                if (summary.CanAggregate)
                {
                    Assert.That(summary.ExternalEntranceCount, Is.LessThanOrEqualTo((4 * sideLength) + 4));
                    Assert.That(summary.MaximumDistanceToOutlet, Is.LessThan(expectedCellCount));
                }
                else
                {
                    Assert.That(summary.Flags, Is.Not.EqualTo(HydrologyAggregationSummaryFlags.None));
                }
            }
        }

        /// <summary>
        /// 集約可能なLevel 0領域を準備し、外周8セルのうち中心へ流れるセルだけが外部入口として残ることを保証します。
        /// 各入口の距離、FlowKey、湿度も個別生成結果と比較し、Summary境界で上流情報が失われないことを確認します。
        /// </summary>
        [Test]
        public void AggregationSummary_LevelZeroRetainsEveryDirectExternalEntrance()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-aggregation-entrances");
            using var cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Temp);
            using var distances = new NativeArray<ushort>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Temp);
            using var entrances = new NativeArray<HydrologyCatchmentExternalEntrance>(
                DeterministicHydrologyAggregationSummary.MaximumExternalEntranceCapacity,
                Allocator.Temp);

            Assert.That(
                TryFindAggregatableSummary(
                    settings,
                    0,
                    cells,
                    distances,
                    entrances,
                    out HydrologyCatchmentAggregationSummary summary),
                Is.True);

            int expectedEntranceCount = 0;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    int candidateX = summary.MinimumCellX + offsetX;
                    int candidateY = summary.MinimumCellY + offsetY;
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            candidateX,
                            candidateY,
                            out GeneratedHydrologyCell candidate),
                        Is.True);
                    if (!candidate.HasDownstream
                        || candidate.DownstreamCellX != summary.MinimumCellX
                        || candidate.DownstreamCellY != summary.MinimumCellY)
                    {
                        continue;
                    }

                    HydrologyCatchmentExternalEntrance actual = entrances[expectedEntranceCount];
                    Assert.That(actual.CellX, Is.EqualTo(candidateX));
                    Assert.That(actual.CellY, Is.EqualTo(candidateY));
                    Assert.That(actual.InternalCellX, Is.EqualTo(summary.MinimumCellX));
                    Assert.That(actual.InternalCellY, Is.EqualTo(summary.MinimumCellY));
                    Assert.That(actual.DistanceToOutlet, Is.EqualTo(1));
                    Assert.That(actual.HydrologyHeight, Is.EqualTo(candidate.HydrologyHeight));
                    Assert.That(actual.PlateauRank, Is.EqualTo(candidate.PlateauRank));
                    expectedEntranceCount++;
                }
            }

            Assert.That(summary.ExternalEntranceCount, Is.EqualTo(expectedEntranceCount));
        }

        /// <summary>
        /// 同じ領域群を昇順と降順で再構築し、先に作った値をキャッシュ相当として保持してもSummaryと外部入口が一致することを保証します。
        /// これにより共有キャッシュを後から導入・破棄しても、構築順が固定生成結果へ入らないことを確認します。
        /// </summary>
        [Test]
        public void AggregationSummary_RebuildMatchesCachedValuesInDifferentConstructionOrder()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-aggregation-order");
            var requests = new (int CellX, int CellY, int Level)[]
            {
                (-33, 17, 0),
                (12, -29, 1),
                (47, 51, 2),
                (-65, -3, 3),
                (96, -80, 4)
            };
            var cachedSummaries = new HydrologyCatchmentAggregationSummary[requests.Length];
            var cachedEntrances = new List<HydrologyCatchmentExternalEntrance>[requests.Length];
            using var cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Temp);
            using var distances = new NativeArray<ushort>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Temp);
            using var entrances = new NativeArray<HydrologyCatchmentExternalEntrance>(
                DeterministicHydrologyAggregationSummary.MaximumExternalEntranceCapacity,
                Allocator.Temp);

            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                (int cellX, int cellY, int level) = requests[requestIndex];
                Assert.That(
                    DeterministicHydrologyAggregationSummary.TryBuildAligned(
                        settings,
                        cellX,
                        cellY,
                        level,
                        cells,
                        distances,
                        entrances,
                        out cachedSummaries[requestIndex]),
                    Is.True);
                cachedEntrances[requestIndex] = CopyExternalEntrances(cachedSummaries[requestIndex], entrances);
            }

            for (int requestIndex = requests.Length - 1; requestIndex >= 0; requestIndex--)
            {
                (int cellX, int cellY, int level) = requests[requestIndex];
                Assert.That(
                    DeterministicHydrologyAggregationSummary.TryBuildAligned(
                        settings,
                        cellX,
                        cellY,
                        level,
                        cells,
                        distances,
                        entrances,
                        out HydrologyCatchmentAggregationSummary rebuilt),
                    Is.True);
                AssertAggregationSummaryEqual(cachedSummaries[requestIndex], rebuilt);

                List<HydrologyCatchmentExternalEntrance> rebuiltEntrances = CopyExternalEntrances(rebuilt, entrances);
                Assert.That(rebuiltEntrances.Count, Is.EqualTo(cachedEntrances[requestIndex].Count));
                for (int entranceIndex = 0; entranceIndex < rebuiltEntrances.Count; entranceIndex++)
                {
                    AssertExternalEntranceEqual(
                        cachedEntrances[requestIndex][entranceIndex],
                        rebuiltEntrances[entranceIndex]);
                }
            }
        }

        /// <summary>
        /// 同じ16対象セルをC3-C1基準経路、空キャッシュ、温まったキャッシュ、逆順構築キャッシュで解決します。
        /// 件数、湿度、最遠距離、流路区分がすべて一致し、キャッシュ命中が固定結果を変えないことを保証します。
        /// </summary>
        [Test]
        public void AggregatedCatchment_CacheMissHitAndConstructionOrderMatchBaseline()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-aggregated-catchment-equivalence");
            const int targetCount = 16;
            var targetX = new int[targetCount];
            var targetY = new int[targetCount];
            var expected = new HydrologyCatchmentResult[targetCount];
            using var baselineFrontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var forwardCache = new HydrologyAggregationSummaryCache(settings, 256);
            using var reverseCache = new HydrologyAggregationSummaryCache(settings, 256);

            for (int index = 0; index < targetCount; index++)
            {
                targetX[index] = -12 + ((index * 7) % 25);
                targetY[index] = 18 - ((index * 11) % 31);
                Assert.That(
                    DeterministicHydrologyGenerator.TryGenerateFlowCell(
                        settings,
                        targetX[index],
                        targetY[index],
                        out GeneratedHydrologyCell startCell),
                    Is.True);
                Assert.That(
                    DeterministicHydrologyCatchmentResolver.TryResolveWithStartCell(
                        settings,
                        startCell,
                        baselineFrontier,
                        out expected[index]),
                    Is.True);
                Assert.That(
                    DeterministicHydrologyAggregatedCatchmentResolver.TryResolve(
                        forwardCache,
                        targetX[index],
                        targetY[index],
                        out HydrologyCatchmentResult coldResult,
                        out _),
                    Is.True);
                AssertCatchmentResultEqual(expected[index], coldResult);
            }

            int warmCacheHits = 0;
            for (int index = targetCount - 1; index >= 0; index--)
            {
                Assert.That(
                    DeterministicHydrologyAggregatedCatchmentResolver.TryResolve(
                        forwardCache,
                        targetX[index],
                        targetY[index],
                        out HydrologyCatchmentResult warmResult,
                        out HydrologyAggregatedCatchmentStatistics warmStatistics),
                    Is.True);
                AssertCatchmentResultEqual(expected[index], warmResult);
                warmCacheHits += warmStatistics.CacheHitCount;

                Assert.That(
                    DeterministicHydrologyAggregatedCatchmentResolver.TryResolve(
                        reverseCache,
                        targetX[index],
                        targetY[index],
                        out HydrologyCatchmentResult reverseResult,
                        out _),
                    Is.True);
                AssertCatchmentResultEqual(expected[index], reverseResult);
            }

            Assert.That(warmCacheHits, Is.GreaterThan(0));
            Assert.That(forwardCache.SummaryCount, Is.GreaterThan(0));
            Assert.That(reverseCache.SummaryCount, Is.GreaterThan(0));

            // 最初のキャッシュを明示破棄してから同じ設定で作り直し、Nativeコンテナの寿命が固定結果へ入らないことを確認します。
            forwardCache.Dispose();
            using var recreatedCache = new HydrologyAggregationSummaryCache(settings, 256);
            Assert.That(
                DeterministicHydrologyAggregatedCatchmentResolver.TryResolve(
                    recreatedCache,
                    targetX[0],
                    targetY[0],
                    out HydrologyCatchmentResult recreatedResult,
                    out _),
                Is.True);
            AssertCatchmentResultEqual(expected[0], recreatedResult);
        }

        /// <summary>
        /// 4,095／4,096／4,097件相当のSummaryを準備し、固定上限をまたぐ場合だけ一括利用を拒否することを保証します。
        /// 拒否されたSummaryは下位Levelまたは個別セルへ分解し、C3-C1の優先順を維持するための境界です。
        /// </summary>
        [TestCase(0u, 4095u, true)]
        [TestCase(0u, 4096u, true)]
        [TestCase(0u, 4097u, false)]
        [TestCase(1u, 4095u, true)]
        [TestCase(1u, 4096u, false)]
        public void AggregationSummary_CrossingCatchmentLimitRequiresDecomposition(
            uint selectedCellCount,
            uint summaryCellCount,
            bool expected)
        {
            var summary = new HydrologyCatchmentAggregationSummary
            {
                CellCount = summaryCellCount,
                Flags = HydrologyAggregationSummaryFlags.None
            };

            Assert.That(
                DeterministicHydrologyAggregationSummary.CanAccumulateWithoutCrossingLimit(
                    summary,
                    selectedCellCount,
                    HydrologyGenerationSettings.CreateDefault().MaximumCatchmentContributingCells),
                Is.EqualTo(expected));

            summary.Flags = HydrologyAggregationSummaryFlags.MultipleExits;
            Assert.That(
                DeterministicHydrologyAggregationSummary.CanAccumulateWithoutCrossingLimit(
                    summary,
                    selectedCellCount,
                    HydrologyGenerationSettings.CreateDefault().MaximumCatchmentContributingCells),
                Is.False);
        }

        /// <summary>
        /// 水文セル中心の固定9地点をテスト側でも基礎気候から平均し、製品HydrologyMoistureが同じ整数値を使うことを保証します。
        /// </summary>
        [Test]
        public void HydrologyMoisture_AveragesTheFixedNineBaseMoistureSamples()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-moisture-nine-samples");
            const int cellX = -9;
            const int cellY = 14;
            int centerX = cellX * settings.Hydrology.CellSizeInTiles;
            int centerY = cellY * settings.Hydrology.CellSizeInTiles;
            long expectedTotal = 0L;

            for (int sampleYIndex = -1; sampleYIndex <= 1; sampleYIndex++)
            {
                for (int sampleXIndex = -1; sampleXIndex <= 1; sampleXIndex++)
                {
                    Assert.That(
                        DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            centerX + (sampleXIndex * settings.Hydrology.RepresentativeSampleOffsetInTiles),
                            centerY + (sampleYIndex * settings.Hydrology.RepresentativeSampleOffsetInTiles),
                            out GeneratedEnvironmentData environment),
                        Is.True);
                    expectedTotal += environment.BaseMoisture;
                }
            }

            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(settings, cellX, cellY, out GeneratedHydrologyCell cell),
                Is.True);
            Assert.That(cell.HydrologyMoisture, Is.EqualTo((ushort)(expectedTotal / 9L)));
            Assert.That(cell.RunoffMoistureSum, Is.GreaterThanOrEqualTo(cell.HydrologyMoisture));
        }

        /// <summary>
        /// 連続するLevel 0要約を準備し、二つをLevel 1へ連結して辺数・到達座標・後半の終端を保持することを保証します。
        /// 不連続座標や前半終端を持つ要約は、経路を飛び越して合成しないことも確認します。
        /// </summary>
        [Test]
        public void DownstreamSummary_CombineRequiresContiguousCompleteHalves()
        {
            HydrologyDownstreamSummary first = CreateSyntheticSummary(
                10,
                -4,
                11,
                -4,
                1,
                0,
                HydrologyConnectionFlags.None);
            HydrologyDownstreamSummary second = CreateSyntheticSummary(
                11,
                -4,
                12,
                -5,
                1,
                0,
                HydrologyConnectionFlags.Marine);

            Assert.That(
                DeterministicHydrologyDownstreamResolver.TryCombine(first, second, out HydrologyDownstreamSummary combined),
                Is.True);
            Assert.That(combined.StartCellX, Is.EqualTo(10));
            Assert.That(combined.StartCellY, Is.EqualTo(-4));
            Assert.That(combined.ReachedCellX, Is.EqualTo(12));
            Assert.That(combined.ReachedCellY, Is.EqualTo(-5));
            Assert.That(combined.EdgeCount, Is.EqualTo(2));
            Assert.That(combined.Level, Is.EqualTo(1));
            Assert.That(combined.Terminal, Is.EqualTo(HydrologyConnectionFlags.Marine));

            HydrologyDownstreamSummary disconnected = second;
            disconnected.StartCellX = 99;
            Assert.That(
                DeterministicHydrologyDownstreamResolver.TryCombine(first, disconnected, out _),
                Is.False);

            first.Terminal = HydrologyConnectionFlags.ClosedBasin;
            Assert.That(
                DeterministicHydrologyDownstreamResolver.TryCombine(first, second, out _),
                Is.False);
        }

        /// <summary>
        /// 最大Levelの終端あり・終端なしSummaryを準備し、前者は到達済み分類、後者は固定LongDrainageになることを保証します。
        /// 最大辺数未満なのに終端がない不完全Summaryをロード不足として受理しないことも確認します。
        /// </summary>
        [Test]
        public void DownstreamSummary_MaximumLevelClassifiesTerminalOrLongDrainage()
        {
            HydrologyGenerationSettings settings = HydrologyGenerationSettings.CreateDefault();
            HydrologyDownstreamSummary marine = CreateSyntheticSummary(
                0,
                0,
                7,
                -3,
                19,
                12,
                HydrologyConnectionFlags.Marine);
            HydrologyDownstreamSummary longDrainage = CreateSyntheticSummary(
                0,
                0,
                4096,
                0,
                4096,
                12,
                HydrologyConnectionFlags.None);

            Assert.That(
                DeterministicHydrologyDownstreamResolver.TryClassifySummary(settings, marine, out HydrologyDownstreamResult marineResult),
                Is.True);
            Assert.That(marineResult.Terminal, Is.EqualTo(HydrologyConnectionFlags.Marine));
            Assert.That(marineResult.EdgeCount, Is.EqualTo(19));

            Assert.That(
                DeterministicHydrologyDownstreamResolver.TryClassifySummary(
                    settings,
                    longDrainage,
                    out HydrologyDownstreamResult longResult),
                Is.True);
            Assert.That(longResult.Terminal, Is.EqualTo(HydrologyConnectionFlags.LongDrainage));
            Assert.That(longResult.EdgeCount, Is.EqualTo(4096));

            longDrainage.EdgeCount = 4095;
            Assert.That(
                DeterministicHydrologyDownstreamResolver.TryClassifySummary(settings, longDrainage, out _),
                Is.False);
        }

        /// <summary>
        /// 実生成経路から連続するLevel 0要約を探し、直接作ったLevel 1要約と二分合成結果が完全一致することを保証します。
        /// これによりSummaryキャッシュを使う場合とLevel 0まで展開する場合で結果が変わらないことを確認します。
        /// </summary>
        [Test]
        public void DownstreamSummary_DirectBuildMatchesLevelZeroComposition()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-summary-composition");
            bool foundTwoEdgePath = false;

            for (int cellY = -24; cellY <= 24 && !foundTwoEdgePath; cellY++)
            {
                for (int cellX = -24; cellX <= 24; cellX++)
                {
                    Assert.That(
                        DeterministicHydrologyDownstreamResolver.TryBuildSummary(
                            settings,
                            cellX,
                            cellY,
                            1,
                            out HydrologyDownstreamSummary direct),
                        Is.True);
                    if (direct.EdgeCount != 2)
                    {
                        continue;
                    }

                    Assert.That(
                        DeterministicHydrologyDownstreamResolver.TryBuildSummary(
                            settings,
                            cellX,
                            cellY,
                            0,
                            out HydrologyDownstreamSummary first),
                        Is.True);
                    Assert.That(first.HasTerminal, Is.False);
                    Assert.That(
                        DeterministicHydrologyDownstreamResolver.TryBuildSummary(
                            settings,
                            first.ReachedCellX,
                            first.ReachedCellY,
                            0,
                            out HydrologyDownstreamSummary second),
                        Is.True);
                    Assert.That(
                        DeterministicHydrologyDownstreamResolver.TryCombine(first, second, out HydrologyDownstreamSummary combined),
                        Is.True);
                    AssertSummaryEqual(direct, combined);
                    foundTwoEdgePath = true;
                    break;
                }
            }

            Assert.That(foundTwoEdgePath, Is.True);
        }

        /// <summary>
        /// 同じSeed・座標を使って一辺Summaryと最大4,096辺の終端探索を比較し、
        /// 1,000／10,000／100,000セルでCPU時間、managed allocation、実経路長を記録します。
        /// 通常回帰を長時間化させず、キャッシュ導入の要否を実測から判断するため明示実行だけに分離します。
        /// </summary>
        [Test]
        [Explicit("TERRAIN-01C3-Bの1k/10k/100k下流探索を同じ入力で比較する局所性能計測です。製品性能の合格判定には使いません。")]
        [Timeout(600000)]
        public void DownstreamResolution_ReportsScalingComparison()
        {
            const int seedCount = 10;
            const int samplesPerScale = 3;
            var settingsBySeed = new WorldGenerationSettings[seedCount];
            using var catchmentFrontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            for (int seedIndex = 0; seedIndex < settingsBySeed.Length; seedIndex++)
            {
                // Seedごとに離れた座標領域を使い、原点周辺だけに偏った経路長を一般化しないようにします。
                settingsBySeed[seedIndex] = CreateSettings($"hydrology-downstream-benchmark-{seedIndex}");
            }

            // JITと初回型初期化を測定外へ出します。両方式へ同じ256セルを渡して条件を揃えます。
            for (int warmupIndex = 0; warmupIndex < 256; warmupIndex++)
            {
                GetBenchmarkInput(warmupIndex, settingsBySeed, out WorldGenerationSettings settings, out int cellX, out int cellY);
                Assert.That(
                    DeterministicHydrologyDownstreamResolver.TryBuildSummary(
                        settings,
                        cellX,
                        cellY,
                        0,
                        out _),
                    Is.True);
                Assert.That(
                    DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                        settings,
                        cellX,
                        cellY,
                        catchmentFrontier,
                        0,
                        out _),
                    Is.True);
            }

            foreach (int cellCount in new[] { 1000, 10000, 100000 })
            {
                double oneEdgeMilliseconds = 0d;
                double downstreamMilliseconds = 0d;
                long oneEdgeAllocatedBytes = 0L;
                long downstreamAllocatedBytes = 0L;
                long totalEdges = 0L;
                int maximumEdges = 0;
                int marineCount = 0;
                int closedBasinCount = 0;
                int longDrainageCount = 0;
                int oneEdgeFailures = 0;
                int downstreamFailures = 0;
                var stopwatch = new Stopwatch();

                for (int sampleIndex = 0; sampleIndex < samplesPerScale; sampleIndex++)
                {
                    // 一辺Summaryを先に測る回と後に測る回を交互にし、時間経過による片側だけの偏りを抑えます。
                    if ((sampleIndex & 1) == 0)
                    {
                        MeasureOneEdgeSummaries(
                            cellCount,
                            settingsBySeed,
                            stopwatch,
                            ref oneEdgeMilliseconds,
                            ref oneEdgeAllocatedBytes,
                            ref oneEdgeFailures);
                        MeasureDownstreamResolution(
                            cellCount,
                            settingsBySeed,
                            catchmentFrontier,
                            stopwatch,
                            ref downstreamMilliseconds,
                            ref downstreamAllocatedBytes,
                            ref downstreamFailures,
                            ref totalEdges,
                            ref maximumEdges,
                            ref marineCount,
                            ref closedBasinCount,
                            ref longDrainageCount);
                    }
                    else
                    {
                        MeasureDownstreamResolution(
                            cellCount,
                            settingsBySeed,
                            catchmentFrontier,
                            stopwatch,
                            ref downstreamMilliseconds,
                            ref downstreamAllocatedBytes,
                            ref downstreamFailures,
                            ref totalEdges,
                            ref maximumEdges,
                            ref marineCount,
                            ref closedBasinCount,
                            ref longDrainageCount);
                        MeasureOneEdgeSummaries(
                            cellCount,
                            settingsBySeed,
                            stopwatch,
                            ref oneEdgeMilliseconds,
                            ref oneEdgeAllocatedBytes,
                            ref oneEdgeFailures);
                    }
                }

                int measuredCellCount = cellCount * samplesPerScale;
                Assert.That(oneEdgeFailures, Is.Zero);
                Assert.That(downstreamFailures, Is.Zero);
                Assert.That(marineCount + closedBasinCount + longDrainageCount, Is.EqualTo(measuredCellCount));
                Assert.That(maximumEdges, Is.InRange(0, settingsBySeed[0].Hydrology.MaximumDownstreamEdges));

                // 文字列化とConsole出力は測定後だけに行い、計測対象のmanaged allocationへ混ぜません。
                UnityEngine.Debug.Log(
                    $"HYDROLOGY_DOWNSTREAM cells={cellCount}, samples={samplesPerScale}, "
                    + $"oneEdgeMeanMs={oneEdgeMilliseconds / samplesPerScale:F6}, "
                    + $"downstreamMeanMs={downstreamMilliseconds / samplesPerScale:F6}, "
                    + $"ratio={downstreamMilliseconds / oneEdgeMilliseconds:F3}, "
                    + $"oneEdgeManagedBytes={oneEdgeAllocatedBytes / samplesPerScale}, "
                    + $"downstreamManagedBytes={downstreamAllocatedBytes / samplesPerScale}, "
                    + $"meanEdges={(double)totalEdges / measuredCellCount:F3}, maximumEdges={maximumEdges}, "
                    + $"marine={marineCount / samplesPerScale}, closedBasin={closedBasinCount / samplesPerScale}, "
                    + $"longDrainage={longDrainageCount / samplesPerScale}");
            }
        }

        /// <summary>
        /// 256セルの固定作業集合を先に温め、基準経路とSummaryキャッシュ経路を1,000／10,000／100,000要求で比較します。
        /// 続いて16論理チャンク・1,024水文セルを両経路で生成し、CPU時間、managed allocation、キャッシュ統計、全固定値を記録します。
        /// </summary>
        [Test]
        [Explicit("TERRAIN-01C3-C2の集約Summaryと現行基準経路を同じ入力で比較する局所性能計測です。製品性能の合格判定には使いません。")]
        [Timeout(600000)]
        public void AggregatedCatchment_ReportsScalingAndChunkGenerationComparison()
        {
            const int workingSetCellCount = 256;
            const int measuredChunkCount = 16;
            WorldGenerationSettings settings = CreateSettings("hydrology-aggregation-benchmark");
            using var baselineFrontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var cache = new HydrologyAggregationSummaryCache(settings, 2048);
            using var baselineChunkCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            var stopwatch = new Stopwatch();

            // 両経路のJITと、集約側の256セル作業集合を測定前に準備します。以後の比較はキャッシュ命中時の再利用費用です。
            for (int index = 0; index < workingSetCellCount; index++)
            {
                GetAggregationBenchmarkInput(index, out int cellX, out int cellY);
                Assert.That(
                    DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                        settings,
                        cellX,
                        cellY,
                        baselineFrontier,
                        0,
                        out GeneratedHydrologyCell baseline),
                    Is.True);
                Assert.That(
                    DeterministicHydrologyAggregatedCatchmentResolver.TryGenerateCell(
                        cache,
                        cellX,
                        cellY,
                        out GeneratedHydrologyCell aggregated,
                        out _),
                    Is.True);
                AssertCellEqual(baseline, aggregated);
            }

            foreach (int cellCount in new[] { 1000, 10000, 100000 })
            {
                int baselineFailures = 0;
                int aggregatedFailures = 0;
                ulong baselineChecksum = 0UL;
                ulong aggregatedChecksum = 0UL;
                long baselineAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                stopwatch.Restart();
                for (int index = 0; index < cellCount; index++)
                {
                    GetAggregationBenchmarkInput(index, out int cellX, out int cellY);
                    if (!DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                        settings,
                        cellX,
                        cellY,
                        baselineFrontier,
                        0,
                        out GeneratedHydrologyCell cell))
                    {
                        baselineFailures++;
                        continue;
                    }

                    baselineChecksum = AccumulateHydrologyChecksum(baselineChecksum, cell);
                }
                stopwatch.Stop();
                double baselineMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                long baselineAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - baselineAllocatedBefore;

                long aggregatedSummaryCount = 0L;
                long decomposedSummaryCount = 0L;
                long cacheHitCount = 0L;
                long cacheMissCount = 0L;
                long baselineFallbackCount = 0L;
                long aggregatedAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                stopwatch.Restart();
                for (int index = 0; index < cellCount; index++)
                {
                    GetAggregationBenchmarkInput(index, out int cellX, out int cellY);
                    if (!DeterministicHydrologyAggregatedCatchmentResolver.TryGenerateCell(
                        cache,
                        cellX,
                        cellY,
                        out GeneratedHydrologyCell cell,
                        out HydrologyAggregatedCatchmentStatistics statistics))
                    {
                        aggregatedFailures++;
                        continue;
                    }

                    aggregatedChecksum = AccumulateHydrologyChecksum(aggregatedChecksum, cell);
                    aggregatedSummaryCount += statistics.AggregatedSummaryCount;
                    decomposedSummaryCount += statistics.DecomposedSummaryCount;
                    cacheHitCount += statistics.CacheHitCount;
                    cacheMissCount += statistics.CacheMissCount;
                    baselineFallbackCount += statistics.BaselineFallbackCount;
                }
                stopwatch.Stop();
                double aggregatedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                long aggregatedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - aggregatedAllocatedBefore;

                Assert.That(baselineFailures, Is.Zero);
                Assert.That(aggregatedFailures, Is.Zero);
                Assert.That(aggregatedChecksum, Is.EqualTo(baselineChecksum));
                UnityEngine.Debug.Log(
                    $"HYDROLOGY_AGGREGATION cells={cellCount}, workingSet={workingSetCellCount}, "
                    + $"baselineMs={baselineMilliseconds:F6}, aggregatedMs={aggregatedMilliseconds:F6}, "
                    + $"ratio={aggregatedMilliseconds / baselineMilliseconds:F3}, "
                    + $"baselineManagedBytes={baselineAllocatedBytes}, aggregatedManagedBytes={aggregatedAllocatedBytes}, "
                    + $"aggregatedSummaries={aggregatedSummaryCount}, decomposedSummaries={decomposedSummaryCount}, "
                    + $"cacheHits={cacheHitCount}, cacheMisses={cacheMissCount}, "
                    + $"baselineFallbacks={baselineFallbackCount}, cachedSummaryCount={cache.SummaryCount}");
            }

            long baselineChunkAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int baselineChunkFailures = 0;
            stopwatch.Restart();
            for (int chunkIndex = 0; chunkIndex < measuredChunkCount; chunkIndex++)
            {
                GetAggregationBenchmarkChunk(chunkIndex, out int chunkX, out int chunkY);
                if (!TryGenerateChunkCellsWithoutLake(
                        settings,
                        chunkX,
                        chunkY,
                        baselineChunkCells,
                        baselineFrontier))
                {
                    baselineChunkFailures++;
                }
            }
            stopwatch.Stop();
            double baselineChunkMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            long baselineChunkAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - baselineChunkAllocatedBefore;

            long aggregatedChunkAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int aggregatedChunkFailures = 0;
            stopwatch.Restart();
            for (int chunkIndex = 0; chunkIndex < measuredChunkCount; chunkIndex++)
            {
                GetAggregationBenchmarkChunk(chunkIndex, out int chunkX, out int chunkY);
                int firstCellX = chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
                int firstCellY = chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
                for (int localY = 0; localY < DeterministicHydrologyGenerator.CellsPerChunkAxis; localY++)
                {
                    for (int localX = 0; localX < DeterministicHydrologyGenerator.CellsPerChunkAxis; localX++)
                    {
                        if (!DeterministicHydrologyAggregatedCatchmentResolver.TryGenerateCell(
                                cache,
                                firstCellX + localX,
                                firstCellY + localY,
                                out _,
                                out _))
                        {
                            aggregatedChunkFailures++;
                        }
                    }
                }
            }
            stopwatch.Stop();
            double aggregatedChunkMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            long aggregatedChunkAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - aggregatedChunkAllocatedBefore;
            Assert.That(baselineChunkFailures, Is.Zero);
            Assert.That(aggregatedChunkFailures, Is.Zero);

            // 時間計測後に同じ16チャンクを再生成して全フィールドを比較し、Assert費用を上の時間へ混ぜません。
            for (int chunkIndex = 0; chunkIndex < measuredChunkCount; chunkIndex++)
            {
                GetAggregationBenchmarkChunk(chunkIndex, out int chunkX, out int chunkY);
                Assert.That(
                    TryGenerateChunkCellsWithoutLake(
                        settings,
                        chunkX,
                        chunkY,
                        baselineChunkCells,
                        baselineFrontier),
                    Is.True);
                int firstCellX = chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
                int firstCellY = chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
                for (int localY = 0; localY < DeterministicHydrologyGenerator.CellsPerChunkAxis; localY++)
                {
                    for (int localX = 0; localX < DeterministicHydrologyGenerator.CellsPerChunkAxis; localX++)
                    {
                        int index = (localY * DeterministicHydrologyGenerator.CellsPerChunkAxis) + localX;
                        Assert.That(
                            DeterministicHydrologyAggregatedCatchmentResolver.TryGenerateCell(
                                cache,
                                firstCellX + localX,
                                firstCellY + localY,
                                out GeneratedHydrologyCell aggregated,
                                out _),
                            Is.True);
                        AssertCellEqual(baselineChunkCells[index], aggregated);
                    }
                }
            }

            UnityEngine.Debug.Log(
                $"HYDROLOGY_AGGREGATION_CHUNKS chunks={measuredChunkCount}, "
                + $"hydrologyCells={measuredChunkCount * DeterministicHydrologyGenerator.CellsPerChunk}, "
                + $"baselineMs={baselineChunkMilliseconds:F6}, aggregatedMs={aggregatedChunkMilliseconds:F6}, "
                + $"ratio={aggregatedChunkMilliseconds / baselineChunkMilliseconds:F3}, "
                + $"baselineManagedBytes={baselineChunkAllocatedBytes}, "
                + $"aggregatedManagedBytes={aggregatedChunkAllocatedBytes}, cachedSummaryCount={cache.SummaryCount}");
        }

        /// <summary>
        /// C3-D1の製品セル経路へ1,000／10,000／100,000要求を渡し、固定ワーク領域でのCPU時間、
        /// managed allocation、固定湖・湖面・打切り流域の件数を記録します。
        /// 通常回帰を長時間化せず、将来の最適化を同じ入力で比較できるよう明示実行へ分離します。
        /// </summary>
        [Test]
        [Explicit("TERRAIN-01C3-D1の閉鎖流域・湖面を1k/10k/100kセルで測る局所性能診断です。製品性能の合格判定には使いません。")]
        [Timeout(900000)]
        public void ClosedBasinResolution_ReportsScalingAndLakeDistribution()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-lake-benchmark");
            using var frontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            HydrologyLakeBasinCache cache = default;

            // JITと最初の流域Hash表初期化を測定外へ出し、三つの桁で同じ10,000セル作業集合を使います。
            for (int index = 0; index < 256; index++)
            {
                GetLakeBenchmarkInput(index, out int cellX, out int cellY);
                Assert.That(
                    DeterministicHydrologyGenerator.TryGenerateCell(
                        settings,
                        cellX,
                        cellY,
                        frontier,
                        0,
                        basinMembers,
                        0,
                        basinLookup,
                        0,
                        ref cache,
                        out _),
                    Is.True);
            }

            foreach (int cellCount in new[] { 1000, 10000, 100000 })
            {
                int failureCount = 0;
                int fixedLakeCount = 0;
                int surfaceCellCount = 0;
                int truncatedBasinCount = 0;
                ulong checksum = 0UL;
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();

                for (int index = 0; index < cellCount; index++)
                {
                    GetLakeBenchmarkInput(index, out int cellX, out int cellY);
                    if (!DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            cellX,
                            cellY,
                            frontier,
                            0,
                            basinMembers,
                            0,
                            basinLookup,
                            0,
                            ref cache,
                            out GeneratedHydrologyCell cell))
                    {
                        failureCount++;
                        continue;
                    }

                    checksum = AccumulateHydrologyChecksum(checksum, cell);
                    if ((cell.LakeFlags & HydrologyLakeFlags.FixedLake) != 0) fixedLakeCount++;
                    if ((cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0) surfaceCellCount++;
                    if ((cell.LakeFlags & HydrologyLakeFlags.BasinTruncated) != 0) truncatedBasinCount++;
                }

                stopwatch.Stop();
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                Assert.That(failureCount, Is.Zero);
                Assert.That(fixedLakeCount, Is.GreaterThan(0));
                Assert.That(surfaceCellCount, Is.GreaterThan(0));
                Assert.That(checksum, Is.Not.Zero);

                // 文字列化とConsole出力は測定停止後に行い、計測対象のmanaged allocationへ混ぜません。
                UnityEngine.Debug.Log(
                    $"HYDROLOGY_LAKES cells={cellCount}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F6}, "
                    + $"managedBytes={allocatedBytes}, failures={failureCount}, fixedLakes={fixedLakeCount}, "
                    + $"surfaceCells={surfaceCellCount}, truncatedBasins={truncatedBasinCount}, checksum={checksum}");
            }
        }

        /// <summary>
        /// 修正前の行別流域再構築と、製品Jobが使うチャンク単位の終端共有を同じ16チャンクで比較します。
        /// 全固定値が一致することを保証しつつCPU時間と重複削減対象数を記録します。
        /// </summary>
        [Test]
        [Explicit("TERRAIN-01C3-D1のチャンク境界待ち修正を、修正前相当の行別解決と同じ1,024セルで比較します。製品性能の合格判定には使いません。")]
        [Timeout(300000)]
        public void ChunkLakeBatching_ReportsComparisonWithRowLocalResolution()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-lake-invariants");
            using var rowLocalCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var batchedCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var frontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            var stopwatch = new Stopwatch();
            double rowLocalMilliseconds = 0d;
            double batchedMilliseconds = 0d;
            int closedCellCount = 0;
            int uniqueSinkCount = 0;

            // 最初のJITとHash表初期化を測定外へ出し、比較する両経路に同じ作業領域を順番に貸します。
            Assert.That(
                TryGenerateChunkCellsWithRowLocalLakeCache(
                    settings,
                    0,
                    0,
                    rowLocalCells,
                    frontier,
                    basinMembers,
                    basinLookup),
                Is.True);
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateChunkCells(
                    settings,
                    0,
                    0,
                    batchedCells,
                    frontier,
                    basinMembers,
                    basinLookup),
                Is.True);

            for (int chunkY = -2; chunkY <= 1; chunkY++)
            {
                for (int chunkX = -2; chunkX <= 1; chunkX++)
                {
                    stopwatch.Restart();
                    Assert.That(
                        TryGenerateChunkCellsWithRowLocalLakeCache(
                            settings,
                            chunkX,
                            chunkY,
                            rowLocalCells,
                            frontier,
                            basinMembers,
                            basinLookup),
                        Is.True);
                    stopwatch.Stop();
                    rowLocalMilliseconds += stopwatch.Elapsed.TotalMilliseconds;

                    stopwatch.Restart();
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateChunkCells(
                            settings,
                            chunkX,
                            chunkY,
                            batchedCells,
                            frontier,
                            basinMembers,
                            basinLookup),
                        Is.True);
                    stopwatch.Stop();
                    batchedMilliseconds += stopwatch.Elapsed.TotalMilliseconds;

                    var uniqueSinks = new HashSet<long>();
                    for (int index = 0; index < DeterministicHydrologyGenerator.CellsPerChunk; index++)
                    {
                        AssertCellEqual(rowLocalCells[index], batchedCells[index]);
                        GeneratedHydrologyCell cell = batchedCells[index];
                        if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin)
                        {
                            continue;
                        }

                        closedCellCount++;
                        long sinkKey = ((long)cell.DownstreamReachCellX << 32)
                            ^ (uint)cell.DownstreamReachCellY;
                        uniqueSinks.Add(sinkKey);
                    }

                    uniqueSinkCount += uniqueSinks.Count;
                }
            }

            Assert.That(closedCellCount, Is.GreaterThan(0));
            Assert.That(uniqueSinkCount, Is.GreaterThan(0));
            UnityEngine.Debug.Log(
                $"HYDROLOGY_LAKE_CHUNK_BATCHING chunks=16, cells=1024, "
                + $"rowLocalMs={rowLocalMilliseconds:F6}, batchedMs={batchedMilliseconds:F6}, "
                + $"ratio={batchedMilliseconds / rowLocalMilliseconds:F3}, "
                + $"closedCells={closedCellCount}, uniqueChunkSinks={uniqueSinkCount}");
        }

        /// <summary>
        /// 修正前の製品経路と同じく、水文行ごとにキャッシュを破棄して同じ閉鎖末端を別行で再構築します。
        /// 性能比較専用であり、ゲーム側から呼び出しません。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkX">比較対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">比較対象の垂直論理チャンク座標です。</param>
        /// <param name="cells">修正前相当の64セル結果を受け取る再利用配列です。</param>
        /// <param name="frontier">上流探索で再利用する固定Heapです。</param>
        /// <param name="basinMembers">閉鎖流域セルを保持する固定作業配列です。</param>
        /// <param name="basinLookup">座標が閉鎖流域に属するかを保持する固定Hash表です。</param>
        /// <returns>修正前相当の行別処理で全64セルを生成できた場合はtrueです。</returns>
        private static bool TryGenerateChunkCellsWithRowLocalLakeCache(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup)
        {
            long firstCellX = (long)chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long firstCellY = (long)chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long lastCellX = firstCellX + DeterministicHydrologyGenerator.CellsPerChunkAxis - 1L;
            long lastCellY = firstCellY + DeterministicHydrologyGenerator.CellsPerChunkAxis - 1L;
            if (!cells.IsCreated || cells.Length < DeterministicHydrologyGenerator.CellsPerChunk
                || firstCellX < int.MinValue || firstCellY < int.MinValue
                || lastCellX > int.MaxValue || lastCellY > int.MaxValue)
            {
                return false;
            }

            for (int localY = 0; localY < DeterministicHydrologyGenerator.CellsPerChunkAxis; localY++)
            {
                // 旧Jobでは8行が別の作業領域を所有したため、行境界でキャッシュを引き継ぎませんでした。
                HydrologyLakeBasinCache cache = default;
                for (int localX = 0; localX < DeterministicHydrologyGenerator.CellsPerChunkAxis; localX++)
                {
                    int index = (localY * DeterministicHydrologyGenerator.CellsPerChunkAxis) + localX;
                    if (!DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            (int)firstCellX + localX,
                            (int)firstCellY + localY,
                            frontier,
                            0,
                            basinMembers,
                            0,
                            basinLookup,
                            0,
                            ref cache,
                            out GeneratedHydrologyCell cell))
                    {
                        return false;
                    }

                    cells[index] = cell;
                }
            }

            return true;
        }

        /// <summary>
        /// C3-C2性能比較用に、湖判定を含めず64個の基礎水文セルを行優先で生成します。
        /// C3-D1の閉鎖流域探索費用を既存の集水Summary比較へ混ぜず、計測対象を維持するためのテスト補助です。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkX">比較対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">比較対象の垂直論理チャンク座標です。</param>
        /// <param name="cells">生成した64セルを受け取る、呼出元所有の再利用配列です。</param>
        /// <param name="frontier">各セルの集水探索で順次再利用する固定Heap配列です。</param>
        /// <returns>座標範囲が有効で、全64セルを生成できた場合はtrueです。</returns>
        private static bool TryGenerateChunkCellsWithoutLake(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier)
        {
            if (!cells.IsCreated || cells.Length < DeterministicHydrologyGenerator.CellsPerChunk)
            {
                return false;
            }

            long firstCellX = (long)chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long firstCellY = (long)chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long lastCellX = firstCellX + DeterministicHydrologyGenerator.CellsPerChunkAxis - 1L;
            long lastCellY = firstCellY + DeterministicHydrologyGenerator.CellsPerChunkAxis - 1L;
            if (firstCellX < int.MinValue || firstCellY < int.MinValue
                || lastCellX > int.MaxValue || lastCellY > int.MaxValue)
            {
                return false;
            }

            // 製品チャンクと同じ行優先順を保ち、差分比較時に配列Indexの意味が変わらないようにします。
            for (int localY = 0; localY < DeterministicHydrologyGenerator.CellsPerChunkAxis; localY++)
            {
                for (int localX = 0; localX < DeterministicHydrologyGenerator.CellsPerChunkAxis; localX++)
                {
                    int index = (localY * DeterministicHydrologyGenerator.CellsPerChunkAxis) + localX;
                    if (!DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                            settings,
                            (int)firstCellX + localX,
                            (int)firstCellY + localY,
                            frontier,
                            0,
                            out GeneratedHydrologyCell cell))
                    {
                        return false;
                    }

                    cells[index] = cell;
                }
            }

            return true;
        }

        /// <summary>
        /// 中心から-4〜+3の担当範囲境界を正負座標で変換し、C#の0方向丸めへ依存しないことを保証します。
        /// </summary>
        /// <param name="worldTile">変換する水平・垂直タイル座標です。</param>
        /// <param name="expectedCell">両軸で期待する水文セル座標です。</param>
        [TestCase(-13, -2)]
        [TestCase(-12, -1)]
        [TestCase(-5, -1)]
        [TestCase(-4, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(4, 1)]
        [TestCase(11, 1)]
        [TestCase(12, 2)]
        public void CellCoordinate_UsesFloorDivisionAndMinus4ToPlus3Ownership(
            int worldTile,
            int expectedCell)
        {
            HydrologyGenerationSettings settings = HydrologyGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    settings,
                    worldTile,
                    worldTile,
                    out int cellX,
                    out int cellY),
                Is.True);
            Assert.That(cellX, Is.EqualTo(expectedCell));
            Assert.That(cellY, Is.EqualTo(expectedCell));
        }

        /// <summary>
        /// 固定セルの中心、中心から-3・0・+3の9地点を準備し、64bit合計の整数平均と中心標高を保証します。
        /// </summary>
        [Test]
        public void RepresentativeHeight_AveragesFixedNineElevationSamples()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-nine-samples");
            const int cellX = 17;
            const int cellY = -23;
            int centerX = cellX * settings.Hydrology.CellSizeInTiles;
            int centerY = cellY * settings.Hydrology.CellSizeInTiles;
            long expectedTotal = 0L;

            // 製品Generatorと同じ9座標だけを直接標高関数へ渡し、Biomeや既存水域を入力にしていないことも確認します。
            for (int sampleY = -3; sampleY <= 3; sampleY += 3)
            {
                for (int sampleX = -3; sampleX <= 3; sampleX += 3)
                {
                    expectedTotal += DeterministicEnvironmentGenerator.SampleElevation(
                        settings,
                        centerX + sampleX,
                        centerY + sampleY);
                }
            }

            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    cellX,
                    cellY,
                    out GeneratedHydrologyCell generated),
                Is.True);
            Assert.That(generated.HydrologyHeight, Is.EqualTo((ushort)(expectedTotal / 9L)));
            Assert.That(
                generated.CenterElevation,
                Is.EqualTo(DeterministicEnvironmentGenerator.SampleElevation(settings, centerX, centerY)));
            Assert.That(
                generated.PlateauRank,
                Is.EqualTo(WorldSeedUtility.HashCoordinate(
                    settings.WorldSeed,
                    settings.GeneratorVersion,
                    cellX,
                    cellY,
                    WorldGenerationPurposeSalts.HydrologyFlowTie)));
        }

        /// <summary>
        /// 高い代表標高の海洋候補と低い陸上候補を混在させ、海洋を最優先し、候補順を逆転しても同じセルを選ぶことを保証します。
        /// </summary>
        [Test]
        public void DownstreamSelection_PrioritizesOceanAndIgnoresEnumerationOrder()
        {
            GeneratedHydrologyCell current = CreateSyntheticCell(0, 0, 10000, 500, false);
            GeneratedHydrologyCell expectedOcean = CreateSyntheticCell(-1, 0, 50000, 100, true);
            GeneratedHydrologyCell otherOcean = CreateSyntheticCell(1, 0, 60000, 200, true);
            FixedList512Bytes<HydrologyFlowCandidate> forward = CreateNeighborList(
                CreateSyntheticCell(1, 1, 1000, 1, false),
                otherOcean,
                CreateSyntheticCell(0, 1, 2000, 2, false),
                expectedOcean,
                CreateSyntheticCell(-1, -1, 3000, 3, false),
                CreateSyntheticCell(0, -1, 4000, 4, false),
                CreateSyntheticCell(1, -1, 5000, 5, false),
                CreateSyntheticCell(-1, 1, 6000, 6, false));
            FixedList512Bytes<HydrologyFlowCandidate> reverse = default;

            // 同じ8候補を逆順にし、先に見つかった候補を採用する実装へ退行していないことを確認します。
            for (int index = forward.Length - 1; index >= 0; index--)
            {
                reverse.Add(forward[index]);
            }

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(
                    current,
                    forward,
                    out HydrologyFlowCandidate forwardSelected),
                Is.True);
            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(
                    current,
                    reverse,
                    out HydrologyFlowCandidate reverseSelected),
                Is.True);
            Assert.That(forwardSelected.CellX, Is.EqualTo(expectedOcean.CellX));
            Assert.That(forwardSelected.CellY, Is.EqualTo(expectedOcean.CellY));
            Assert.That(reverseSelected.CellX, Is.EqualTo(expectedOcean.CellX));
            Assert.That(reverseSelected.CellY, Is.EqualTo(expectedOcean.CellY));
        }

        /// <summary>
        /// 海洋がない8近傍を準備し、低い代表標高、同標高の小さい順位、流向なしの順に規則が適用されることを保証します。
        /// より低いセルがある場合は、順位が小さい同標高セルや高い陸上セルへ流れないことも確認します。
        /// </summary>
        [Test]
        public void DownstreamSelection_UsesLowerHeightThenPlateauRankAndRejectsHigherLand()
        {
            GeneratedHydrologyCell current = CreateSyntheticCell(0, 0, 100, 500, false);
            GeneratedHydrologyCell lower = CreateSyntheticCell(1, 0, 90, 900, false);
            GeneratedHydrologyCell equalLowerRank = CreateSyntheticCell(0, 1, 100, 10, false);
            FixedList512Bytes<HydrologyFlowCandidate> withLower = CreateNeighborList(
                lower,
                equalLowerRank,
                CreateSyntheticCell(-1, 0, 101, 1, false),
                CreateSyntheticCell(0, -1, 102, 2, false),
                CreateSyntheticCell(1, 1, 103, 3, false),
                CreateSyntheticCell(-1, 1, 104, 4, false),
                CreateSyntheticCell(-1, -1, 105, 5, false),
                CreateSyntheticCell(1, -1, 106, 6, false));

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(current, withLower, out HydrologyFlowCandidate selectedLower),
                Is.True);
            Assert.That(selectedLower.CellX, Is.EqualTo(lower.CellX));
            Assert.That(selectedLower.CellY, Is.EqualTo(lower.CellY));

            FixedList512Bytes<HydrologyFlowCandidate> equalPlateau = CreateNeighborList(
                equalLowerRank,
                CreateSyntheticCell(1, 0, 100, 600, false),
                CreateSyntheticCell(-1, 0, 101, 1, false),
                CreateSyntheticCell(0, -1, 102, 2, false),
                CreateSyntheticCell(1, 1, 103, 3, false),
                CreateSyntheticCell(-1, 1, 104, 4, false),
                CreateSyntheticCell(-1, -1, 105, 5, false),
                CreateSyntheticCell(1, -1, 106, 6, false));

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(current, equalPlateau, out HydrologyFlowCandidate selectedEqual),
                Is.True);
            Assert.That(selectedEqual.CellX, Is.EqualTo(equalLowerRank.CellX));
            Assert.That(selectedEqual.CellY, Is.EqualTo(equalLowerRank.CellY));

            FixedList512Bytes<HydrologyFlowCandidate> blocked = CreateNeighborList(
                CreateSyntheticCell(1, 0, 100, 600, false),
                CreateSyntheticCell(0, 1, 100, 700, false),
                CreateSyntheticCell(-1, 0, 101, 1, false),
                CreateSyntheticCell(0, -1, 102, 2, false),
                CreateSyntheticCell(1, 1, 103, 3, false),
                CreateSyntheticCell(-1, 1, 104, 4, false),
                CreateSyntheticCell(-1, -1, 105, 5, false),
                CreateSyntheticCell(1, -1, 106, 6, false));

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(current, blocked, out _),
                Is.False);
        }

        /// <summary>
        /// 代表標高と64bit順位が完全同値の候補を準備し、X、次にYの昇順が最終比較になることを保証します。
        /// </summary>
        [Test]
        public void FlowOrder_UsesCoordinatesAfterHashCollision()
        {
            GeneratedHydrologyCell first = CreateSyntheticCell(-2, 4, 32000, 99, false);
            GeneratedHydrologyCell second = CreateSyntheticCell(-2, 5, 32000, 99, false);
            GeneratedHydrologyCell third = CreateSyntheticCell(-1, -100, 32000, 99, false);

            Assert.That(DeterministicHydrologyGenerator.CompareFlowOrder(first, second), Is.LessThan(0));
            Assert.That(DeterministicHydrologyGenerator.CompareFlowOrder(second, first), Is.GreaterThan(0));
            Assert.That(DeterministicHydrologyGenerator.CompareFlowOrder(second, third), Is.LessThan(0));
        }

        /// <summary>
        /// 正負座標を含む広域格子を生成し、海洋は終端、陸上の下流はD8隣接かつ安定Flow順で降下することを保証します。
        /// </summary>
        [Test]
        public void GeneratedFlow_UsesOnlyOceanOrStrictlyDescendingD8Edges()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-d8-invariants");
            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            int oceanCount = 0;
            int landEdgeCount = 0;
            int closedBasinCount = 0;

            for (int cellY = -24; cellY <= 24; cellY++)
            {
                for (int cellX = -24; cellX <= 24; cellX++)
                {
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                            settings,
                            cellX,
                            cellY,
                            frontier,
                            0,
                            out GeneratedHydrologyCell cell),
                        Is.True);

                    if (cell.IsOceanOutlet)
                    {
                        oceanCount++;
                        Assert.That(cell.HasDownstream, Is.False);
                        Assert.That(cell.DownstreamCellX, Is.EqualTo(cell.CellX));
                        Assert.That(cell.DownstreamCellY, Is.EqualTo(cell.CellY));
                        Assert.That(cell.DownstreamTerminal, Is.EqualTo(HydrologyConnectionFlags.Marine));
                        Assert.That(cell.DownstreamEdgeCount, Is.Zero);
                        Assert.That(cell.DownstreamReachCellX, Is.EqualTo(cell.CellX));
                        Assert.That(cell.DownstreamReachCellY, Is.EqualTo(cell.CellY));
                        continue;
                    }

                    if (!cell.HasDownstream)
                    {
                        closedBasinCount++;
                        Assert.That(cell.DownstreamTerminal, Is.EqualTo(HydrologyConnectionFlags.ClosedBasin));
                        Assert.That(cell.DownstreamEdgeCount, Is.Zero);
                        continue;
                    }

                    landEdgeCount++;
                    Assert.That(
                        cell.DownstreamTerminal,
                        Is.EqualTo(HydrologyConnectionFlags.Marine)
                            .Or.EqualTo(HydrologyConnectionFlags.ClosedBasin)
                            .Or.EqualTo(HydrologyConnectionFlags.LongDrainage));
                    Assert.That(cell.DownstreamEdgeCount, Is.GreaterThan(0));
                    Assert.That(cell.DownstreamEdgeCount, Is.LessThanOrEqualTo(4096));
                    if (cell.DownstreamTerminal == HydrologyConnectionFlags.LongDrainage)
                    {
                        Assert.That(cell.DownstreamEdgeCount, Is.EqualTo(4096));
                    }
                    Assert.That(System.Math.Abs(cell.DownstreamCellX - cell.CellX), Is.LessThanOrEqualTo(1));
                    Assert.That(System.Math.Abs(cell.DownstreamCellY - cell.CellY), Is.LessThanOrEqualTo(1));
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                            settings,
                            cell.DownstreamCellX,
                            cell.DownstreamCellY,
                            frontier,
                            0,
                            out GeneratedHydrologyCell downstream),
                        Is.True);

                    if (!downstream.IsOceanOutlet)
                    {
                        Assert.That(
                            DeterministicHydrologyGenerator.CompareFlowOrder(downstream, cell),
                            Is.LessThan(0));
                    }
                }
            }

            Assert.That(oceanCount, Is.GreaterThan(0));
            Assert.That(landEdgeCount, Is.GreaterThan(0));
            Assert.That(closedBasinCount, Is.GreaterThan(0));
        }

        /// <summary>
        /// 複数チャンクを製品と同じ再利用ワーク領域で生成し、閉鎖流域の除外理由、固定湖面、越流状態が整合することを保証します。
        /// </summary>
        [Test]
        public void GeneratedLakes_UseResolvedBasinAndConnectedSurfaceState()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-lake-invariants");
            using NativeArray<GeneratedHydrologyCell> cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Temp);
            int closedBasinCount = 0;
            int fixedLakeBasinCellCount = 0;
            int surfaceCellCount = 0;

            // 原点周囲の4×4チャンクを有限に調べます。チャンクごとに固定ワーク領域を再利用し、
            // 単発APIの一時配列確保を分布走査へ持ち込まないための製品経路回帰でもあります。
            for (int chunkY = -2; chunkY <= 1; chunkY++)
            {
                for (int chunkX = -2; chunkX <= 1; chunkX++)
                {
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateChunkCells(
                            settings,
                            chunkX,
                            chunkY,
                            cells),
                        Is.True);

                    for (int index = 0; index < cells.Length; index++)
                    {
                        GeneratedHydrologyCell cell = cells[index];
                        if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin)
                        {
                            Assert.That(cell.LakeFlags, Is.EqualTo(HydrologyLakeFlags.None));
                            Assert.That(
                                cell.ConnectionFlags & HydrologyConnectionFlags.Lake,
                                Is.EqualTo(HydrologyConnectionFlags.None));
                            continue;
                        }

                        closedBasinCount++;
                        Assert.That(cell.LakeFlags & HydrologyLakeFlags.BasinResolved, Is.Not.Zero);
                        Assert.That(cell.BasinAreaCells, Is.GreaterThanOrEqualTo(1u));
                        AssertLakeStateIsConsistent(cell);
                        if ((cell.LakeFlags & HydrologyLakeFlags.FixedLake) != 0)
                        {
                            fixedLakeBasinCellCount++;
                        }
                        if ((cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0)
                        {
                            surfaceCellCount++;
                        }
                    }
                }
            }

            Assert.That(closedBasinCount, Is.GreaterThan(0));
            Assert.That(fixedLakeBasinCellCount, Is.GreaterThan(0));
            Assert.That(surfaceCellCount, Is.GreaterThan(0));
        }

        /// <summary>
        /// 製品Jobと同じ閉鎖末端作業一覧と8つの専用領域を準備し、各担当を実行した結果が逐次チャンク生成と一致することを保証します。
        /// </summary>
        [Test]
        public void ParallelLakeWorkItems_PartitionCellsAndMatchSequentialChunkResolution()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-parallel-lake-work-items");
            using var expectedCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var actualCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var frontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyGenerator.ParallelChunkFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyGenerator.ParallelChunkBasinMemberCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyGenerator.ParallelChunkBasinLookupCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            using var workItems = new NativeArray<HydrologyLakeChunkWorkItem>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            int workItemCount = 0;
            bool foundChunkWithClosedBasin = false;
            for (int chunkY = -2; chunkY <= 2 && !foundChunkWithClosedBasin; chunkY++)
            {
                for (int chunkX = -2; chunkX <= 2; chunkX++)
                {
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateChunkCells(
                            settings,
                            chunkX,
                            chunkY,
                            expectedCells),
                        Is.True);
                    Assert.That(
                        TryGenerateChunkCellsWithoutLake(
                            settings,
                            chunkX,
                            chunkY,
                            actualCells,
                            frontier),
                        Is.True);
                    Assert.That(
                        DeterministicHydrologyGenerator.TryBuildChunkLakeWorkItems(
                            actualCells,
                            workItems,
                            out workItemCount),
                        Is.True);

                    // GeneratorVersion変更で特定座標の閉鎖流域数は変わるため、固定Seedの有限範囲から最初の対象チャンクを選びます。
                    // 選択後の逐次生成と並列担当の一致がこのテストの責務で、旧版の座標分布そのものは保証しません。
                    if (workItemCount > 0)
                    {
                        foundChunkWithClosedBasin = true;
                        break;
                    }
                }
            }

            Assert.That(foundChunkWithClosedBasin, Is.True);

            ulong combinedMask = 0UL;
            for (int workItemIndex = 0; workItemIndex < workItemCount; workItemIndex++)
            {
                HydrologyLakeChunkWorkItem workItem = workItems[workItemIndex];
                // 同じセルを複数担当へ渡すと並列書込競合になるため、Maskの重複を結果比較とは別に検出します。
                Assert.That(combinedMask & workItem.CellMask, Is.Zero);
                combinedMask |= workItem.CellMask;
            }

            ulong expectedClosedBasinMask = 0UL;
            for (int cellIndex = 0; cellIndex < actualCells.Length; cellIndex++)
            {
                if (actualCells[cellIndex].DownstreamTerminal == HydrologyConnectionFlags.ClosedBasin)
                {
                    expectedClosedBasinMask |= 1UL << cellIndex;
                }
            }
            Assert.That(combinedMask, Is.EqualTo(expectedClosedBasinMask));

            for (int laneIndex = 0;
                laneIndex < DeterministicHydrologyGenerator.CellsPerChunkAxis;
                laneIndex++)
            {
                Assert.That(
                    DeterministicHydrologyGenerator.TryApplyChunkLakeWorkItemsLane(
                        settings,
                        actualCells,
                        workItems,
                        workItemCount,
                        laneIndex,
                        DeterministicHydrologyGenerator.CellsPerChunkAxis,
                        frontier,
                        basinMembers,
                        basinLookup),
                    Is.True);
            }

            for (int cellIndex = 0; cellIndex < actualCells.Length; cellIndex++)
            {
                AssertCellEqual(expectedCells[cellIndex], actualCells[cellIndex]);
            }
        }

        /// <summary>
        /// 複数の開始セルからD8辺をたどり、海洋終端または陸上末端へ着くまで同じ座標を再訪しないことを保証します。
        /// </summary>
        [Test]
        public void GeneratedFlow_DoesNotCreateCycles()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-no-cycles");

            for (int startIndex = 0; startIndex < 8; startIndex++)
            {
                int currentX = (startIndex * 17) - 190;
                int currentY = (startIndex * -23) + 250;
                var visited = new HashSet<long>();

                // 安定キーが各辺で必ず小さくなるため本来は循環しません。
                // 広域の全辺降下テストと重複しすぎないよう、8起点を各96辺まで追跡して向き間違いを検出します。
                for (int edge = 0; edge < 96; edge++)
                {
                    long coordinateKey = ((long)currentX << 32) ^ (uint)currentY;
                    Assert.That(visited.Add(coordinateKey), Is.True, $"循環座標: ({currentX}, {currentY})");
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateFlowCell(
                            settings,
                            currentX,
                            currentY,
                            out GeneratedHydrologyCell cell),
                        Is.True);

                    if (!cell.HasDownstream)
                    {
                        break;
                    }

                    currentX = cell.DownstreamCellX;
                    currentY = cell.DownstreamCellY;
                }
            }
        }

        /// <summary>
        /// 負チャンクを含む64件配列を生成し、中心所有セルが行優先で連続し、単一セルAPIと一致することを保証します。
        /// </summary>
        [Test]
        public void ChunkCells_ContainEightByEightOwnedCentersInRowMajorOrder()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-chunk-buffer");
            using NativeArray<GeneratedHydrologyCell> cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Temp);

            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateChunkCells(settings, -2, 3, cells),
                Is.True);
            for (int localY = 0; localY < DeterministicHydrologyGenerator.CellsPerChunkAxis; localY++)
            {
                for (int localX = 0; localX < DeterministicHydrologyGenerator.CellsPerChunkAxis; localX++)
                {
                    int index = (localY * DeterministicHydrologyGenerator.CellsPerChunkAxis) + localX;
                    int expectedX = (-2 * DeterministicHydrologyGenerator.CellsPerChunkAxis) + localX;
                    int expectedY = (3 * DeterministicHydrologyGenerator.CellsPerChunkAxis) + localY;
                    GeneratedHydrologyCell actual = cells[index];
                    Assert.That(actual.CellX, Is.EqualTo(expectedX));
                    Assert.That(actual.CellY, Is.EqualTo(expectedY));
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            expectedX,
                            expectedY,
                            out GeneratedHydrologyCell expected),
                        Is.True);
                    AssertCellEqual(expected, actual);
                }
            }
        }

        /// <summary>
        /// 同じチャンクを繰り返しハッシュし、結果が再現し、別Seedでは水文順位を含む比較値が変わることを保証します。
        /// </summary>
        [Test]
        public void ChunkHash_IncludesDeterministicHydrologyCells()
        {
            WorldGenerationSettings first = CreateSettings("hydrology-hash-first");
            WorldGenerationSettings second = CreateSettings("hydrology-hash-second");
            ChunkCoordinate coordinate = new ChunkCoordinate(-7, 11);
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            const ulong initialHash = 0xCBF29CE484222325UL;

            ulong firstHash = DeterministicHydrologyGenerator.AppendChunkHash(first, biomeSettings, coordinate, initialHash);
            ulong repeatedHash = DeterministicHydrologyGenerator.AppendChunkHash(first, biomeSettings, coordinate, initialHash);
            ulong secondHash = DeterministicHydrologyGenerator.AppendChunkHash(second, biomeSettings, coordinate, initialHash);

            Assert.That(repeatedHash, Is.EqualTo(firstHash));
            Assert.That(secondHash, Is.Not.EqualTo(firstHash));
        }

        /// <summary>
        /// 不正な格子寸法とint端で中心・9点・D8隣接を置けないセルを入力し、周回せず明示失敗することを保証します。
        /// </summary>
        [Test]
        public void Generation_InvalidSettingsAndCoordinateEdgesFailExplicitly()
        {
            WorldGenerationSettings invalidSettings = CreateSettings("hydrology-invalid-settings");
            invalidSettings.Hydrology.CellSizeInTiles = 0;
            WorldGenerationSettings validSettings = CreateSettings("hydrology-coordinate-edge");

            Assert.That(DeterministicHydrologyGenerator.ValidateSettings(invalidSettings.Hydrology), Is.False);
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    invalidSettings,
                    0,
                    0,
                    out _),
                Is.False);
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    validSettings,
                    int.MaxValue,
                    0,
                    out GeneratedHydrologyCell maximumCoordinateCell),
                Is.False);
            Assert.That(maximumCoordinateCell, Is.EqualTo(default(GeneratedHydrologyCell)));
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    validSettings,
                    int.MinValue,
                    0,
                    out GeneratedHydrologyCell minimumCoordinateCell),
                Is.False);
            Assert.That(minimumCoordinateCell, Is.EqualTo(default(GeneratedHydrologyCell)));
        }

        /// <summary>
        /// 下流判定だけが成功しても、集水Heap容量が不足した部分結果を成功として公開しないことを保証します。
        /// </summary>
        [Test]
        public void Generation_IncompleteCatchmentWorkspaceFailsWithoutPartialResult()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-incomplete-workspace");
            using var insufficientFrontier = new NativeArray<HydrologyUpstreamCandidate>(
                1,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    0,
                    0,
                    insufficientFrontier,
                    out GeneratedHydrologyCell cell),
                Is.False);
            Assert.That(cell, Is.EqualTo(default(GeneratedHydrologyCell)));
        }

        /// <summary>テスト名から安定Seedを作り、製品既定値と同じ現行GeneratorVersionの設定を返します。</summary>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seedText), WorldGenerationSettings.CurrentGeneratorVersion);
        }

        /// <summary>
        /// 集約性能の計測番号を16×16セルへ循環割当し、全件数で同じ256セルの温まったキャッシュを共有します。
        /// </summary>
        /// <param name="index">0から始まる計測要求番号です。</param>
        /// <param name="cellX">要求番号に対応する水平絶対水文座標です。</param>
        /// <param name="cellY">要求番号に対応する垂直絶対水文座標です。</param>
        private static void GetAggregationBenchmarkInput(int index, out int cellX, out int cellY)
        {
            const int axisLength = 16;
            int localIndex = index % (axisLength * axisLength);
            cellX = (localIndex % axisLength) - (axisLength / 2);
            cellY = (localIndex / axisLength) - (axisLength / 2);
        }

        /// <summary>
        /// 湖性能の要求番号を原点周囲100×100セルへ循環割当し、各桁で同じ固定作業集合を再利用します。
        /// </summary>
        /// <param name="index">0から始まるセル生成要求番号です。</param>
        /// <param name="cellX">要求番号に対応する水平絶対水文座標です。</param>
        /// <param name="cellY">要求番号に対応する垂直絶対水文座標です。</param>
        private static void GetLakeBenchmarkInput(int index, out int cellX, out int cellY)
        {
            const int axisLength = 100;
            int localIndex = index % (axisLength * axisLength);
            cellX = (localIndex % axisLength) - (axisLength / 2);
            cellY = (localIndex / axisLength) - (axisLength / 2);
        }

        /// <summary>
        /// 16チャンクの計測番号を原点周囲の4×4論理チャンクへ割り当てます。
        /// </summary>
        /// <param name="index">0〜15の論理チャンク計測番号です。</param>
        /// <param name="chunkX">番号に対応する水平論理チャンク座標です。</param>
        /// <param name="chunkY">番号に対応する垂直論理チャンク座標です。</param>
        private static void GetAggregationBenchmarkChunk(int index, out int chunkX, out int chunkY)
        {
            const int axisLength = 4;
            chunkX = (index % axisLength) - (axisLength / 2);
            chunkY = (index / axisLength) - (axisLength / 2);
        }

        /// <summary>
        /// 性能ループの生成結果が最適化で捨てられないよう、主要固定値を順序付き64bit検査値へ加えます。
        /// この値はセーブ互換性や製品状態ハッシュではなく、同じ計測入力同士の比較だけに使用します。
        /// </summary>
        /// <param name="current">一つ前のセルまでを反映した計測用検査値です。</param>
        /// <param name="cell">今回反映する固定水文セルです。</param>
        /// <returns>座標、集水数、湿度合計、接続、湖面を混合した次の検査値です。</returns>
        private static ulong AccumulateHydrologyChecksum(ulong current, in GeneratedHydrologyCell cell)
        {
            unchecked
            {
                ulong coordinate = (uint)cell.CellX | ((ulong)(uint)cell.CellY << 32);
                ulong catchment = cell.CatchmentAreaCells | ((ulong)(ushort)cell.ConnectionFlags << 32);
                ulong lake = cell.BasinAreaCells | ((ulong)(byte)cell.LakeFlags << 32);
                lake |= (ulong)cell.LakeSurfaceHeight << 40;
                current = WorldSeedUtility.Mix64(current ^ coordinate);
                current = WorldSeedUtility.Mix64(current ^ catchment);
                current = WorldSeedUtility.Mix64(current ^ lake);
                return WorldSeedUtility.Mix64(current ^ (ulong)cell.RunoffMoistureSum);
            }
        }

        /// <summary>
        /// 局所計測の通し番号を、10 Seed・各100×100セルの離れた絶対座標へ変換します。
        /// 件数を増やしても先頭入力が同じになるため、1k／10k／100kのスケーリングを比較できます。
        /// </summary>
        /// <param name="index">0から始まる計測セル番号です。</param>
        /// <param name="settingsBySeed">計測前に作成した10個の固定設定です。</param>
        /// <param name="settings">番号に対応する固定ワールド設定です。</param>
        /// <param name="cellX">番号に対応する水平水文セル座標です。</param>
        /// <param name="cellY">番号に対応する垂直水文セル座標です。</param>
        private static void GetBenchmarkInput(
            int index,
            WorldGenerationSettings[] settingsBySeed,
            out WorldGenerationSettings settings,
            out int cellX,
            out int cellY)
        {
            const int cellsPerSeed = 10000;
            const int axisLength = 100;
            int seedIndex = (index / cellsPerSeed) % settingsBySeed.Length;
            int localIndex = index % cellsPerSeed;
            int coordinateOffset = seedIndex * cellsPerSeed;
            settings = settingsBySeed[seedIndex];
            cellX = (localIndex % axisLength) - (axisLength / 2) + coordinateOffset;
            cellY = (localIndex / axisLength) - (axisLength / 2) - coordinateOffset;
        }

        /// <summary>
        /// 指定件数のLevel 0 Summaryを生成し、最大一辺を調べる比較側のCPU時間とmanaged allocationを加算します。
        /// </summary>
        /// <param name="cellCount">今回測る水文セル数です。</param>
        /// <param name="settingsBySeed">計測用の固定ワールド設定です。</param>
        /// <param name="stopwatch">呼出元で再利用する計時器です。</param>
        /// <param name="totalMilliseconds">全サンプルの経過時間合計です。</param>
        /// <param name="totalAllocatedBytes">全サンプルのmanaged allocation合計です。</param>
        /// <param name="failureCount">生成失敗件数の合計です。</param>
        private static void MeasureOneEdgeSummaries(
            int cellCount,
            WorldGenerationSettings[] settingsBySeed,
            Stopwatch stopwatch,
            ref double totalMilliseconds,
            ref long totalAllocatedBytes,
            ref int failureCount)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Restart();
            for (int index = 0; index < cellCount; index++)
            {
                GetBenchmarkInput(index, settingsBySeed, out WorldGenerationSettings settings, out int cellX, out int cellY);
                if (!DeterministicHydrologyDownstreamResolver.TryBuildSummary(
                        settings,
                        cellX,
                        cellY,
                        0,
                        out _))
                {
                    failureCount++;
                }
            }
            stopwatch.Stop();
            totalMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            totalAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        /// <summary>
        /// 指定件数の最大Level終端を生成し、CPU時間、managed allocation、実辺数、終端内訳を加算します。
        /// </summary>
        /// <param name="cellCount">今回測る水文セル数です。</param>
        /// <param name="settingsBySeed">計測用の固定ワールド設定です。</param>
        /// <param name="catchmentFrontier">全セルで再利用し、セルごとのNative確保を測定から除く上流Heap配列です。</param>
        /// <param name="stopwatch">呼出元で再利用する計時器です。</param>
        /// <param name="totalMilliseconds">全サンプルの経過時間合計です。</param>
        /// <param name="totalAllocatedBytes">全サンプルのmanaged allocation合計です。</param>
        /// <param name="failureCount">生成失敗件数の合計です。</param>
        /// <param name="totalEdges">成功した全セルの実辺数合計です。</param>
        /// <param name="maximumEdges">成功したセルの最大実辺数です。</param>
        /// <param name="marineCount">海洋終端件数です。</param>
        /// <param name="closedBasinCount">閉鎖流域終端件数です。</param>
        /// <param name="longDrainageCount">4,096辺打切り件数です。</param>
        private static void MeasureDownstreamResolution(
            int cellCount,
            WorldGenerationSettings[] settingsBySeed,
            NativeArray<HydrologyUpstreamCandidate> catchmentFrontier,
            Stopwatch stopwatch,
            ref double totalMilliseconds,
            ref long totalAllocatedBytes,
            ref int failureCount,
            ref long totalEdges,
            ref int maximumEdges,
            ref int marineCount,
            ref int closedBasinCount,
            ref int longDrainageCount)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Restart();
            for (int index = 0; index < cellCount; index++)
            {
                GetBenchmarkInput(index, settingsBySeed, out WorldGenerationSettings settings, out int cellX, out int cellY);
                if (!DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                        settings,
                        cellX,
                        cellY,
                        catchmentFrontier,
                        0,
                        out GeneratedHydrologyCell cell))
                {
                    failureCount++;
                    continue;
                }

                totalEdges += cell.DownstreamEdgeCount;
                maximumEdges = Math.Max(maximumEdges, cell.DownstreamEdgeCount);
                switch (cell.DownstreamTerminal)
                {
                    case HydrologyConnectionFlags.Marine:
                        marineCount++;
                        break;
                    case HydrologyConnectionFlags.ClosedBasin:
                        closedBasinCount++;
                        break;
                    case HydrologyConnectionFlags.LongDrainage:
                        longDrainageCount++;
                        break;
                }
            }
            stopwatch.Stop();
            totalMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            totalAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }

        /// <summary>
        /// 指定Levelで一括利用できる実生成Summaryを有限範囲から探します。
        /// テストを特定Seedの一座標へ過度に固定せず、見つからない場合も無限探索しないため±32セルへ限定します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="level">探す空間Summary Levelです。</param>
        /// <param name="cells">領域セル用の再利用配列です。</param>
        /// <param name="distances">内部距離用の再利用配列です。</param>
        /// <param name="entrances">外部入口用の再利用配列です。</param>
        /// <param name="summary">成功時に見つかった集約可能Summaryです。</param>
        /// <returns>有限探索範囲内に集約可能Summaryがあればtrueです。</returns>
        private static bool TryFindAggregatableSummary(
            in WorldGenerationSettings settings,
            int level,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<ushort> distances,
            NativeArray<HydrologyCatchmentExternalEntrance> entrances,
            out HydrologyCatchmentAggregationSummary summary)
        {
            summary = default;
            for (int cellY = -32; cellY <= 32; cellY++)
            {
                for (int cellX = -32; cellX <= 32; cellX++)
                {
                    if (!DeterministicHydrologyAggregationSummary.TryBuildAligned(
                            settings,
                            cellX,
                            cellY,
                            level,
                            cells,
                            distances,
                            entrances,
                            out summary))
                    {
                        return false;
                    }

                    if (summary.CanAggregate)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Summaryの有効な外部入口範囲だけをmanaged Listへ複製し、次の再構築後も比較できるようにします。</summary>
        private static List<HydrologyCatchmentExternalEntrance> CopyExternalEntrances(
            in HydrologyCatchmentAggregationSummary summary,
            NativeArray<HydrologyCatchmentExternalEntrance> entrances)
        {
            var result = new List<HydrologyCatchmentExternalEntrance>(summary.ExternalEntranceCount);
            for (int index = 0; index < summary.ExternalEntranceCount; index++)
            {
                result.Add(entrances[index]);
            }

            return result;
        }

        /// <summary>再構築した空間Summaryの全固定値を比較し、構築順による差を明確にします。</summary>
        private static void AssertAggregationSummaryEqual(
            in HydrologyCatchmentAggregationSummary expected,
            in HydrologyCatchmentAggregationSummary actual)
        {
            Assert.That(actual.MinimumCellX, Is.EqualTo(expected.MinimumCellX));
            Assert.That(actual.MinimumCellY, Is.EqualTo(expected.MinimumCellY));
            Assert.That(actual.OutletCellX, Is.EqualTo(expected.OutletCellX));
            Assert.That(actual.OutletCellY, Is.EqualTo(expected.OutletCellY));
            Assert.That(actual.DownstreamCellX, Is.EqualTo(expected.DownstreamCellX));
            Assert.That(actual.DownstreamCellY, Is.EqualTo(expected.DownstreamCellY));
            Assert.That(actual.CellCount, Is.EqualTo(expected.CellCount));
            Assert.That(actual.RunoffMoistureSum, Is.EqualTo(expected.RunoffMoistureSum));
            Assert.That(actual.MaximumDistanceToOutlet, Is.EqualTo(expected.MaximumDistanceToOutlet));
            Assert.That(actual.ExternalEntranceCount, Is.EqualTo(expected.ExternalEntranceCount));
            Assert.That(actual.Level, Is.EqualTo(expected.Level));
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags));
        }

        /// <summary>基準経路と集約経路の全固定集水値を比較し、キャッシュ由来の差を明確にします。</summary>
        private static void AssertCatchmentResultEqual(
            in HydrologyCatchmentResult expected,
            in HydrologyCatchmentResult actual)
        {
            Assert.That(actual.CatchmentAreaCells, Is.EqualTo(expected.CatchmentAreaCells));
            Assert.That(actual.RunoffMoistureSum, Is.EqualTo(expected.RunoffMoistureSum));
            Assert.That(actual.RunoffEquivalentCells, Is.EqualTo(expected.RunoffEquivalentCells));
            Assert.That(actual.TargetHydrologyMoisture, Is.EqualTo(expected.TargetHydrologyMoisture));
            Assert.That(actual.MaximumContributingDistance, Is.EqualTo(expected.MaximumContributingDistance));
            Assert.That(actual.CatchmentFlags, Is.EqualTo(expected.CatchmentFlags));
            Assert.That(actual.SurfaceWaterConnectionFlags, Is.EqualTo(expected.SurfaceWaterConnectionFlags));
        }

        /// <summary>再構築した外部入口の座標、距離、FlowKey、湿度を比較し、境界情報の欠落を明確にします。</summary>
        private static void AssertExternalEntranceEqual(
            in HydrologyCatchmentExternalEntrance expected,
            in HydrologyCatchmentExternalEntrance actual)
        {
            Assert.That(actual.CellX, Is.EqualTo(expected.CellX));
            Assert.That(actual.CellY, Is.EqualTo(expected.CellY));
            Assert.That(actual.InternalCellX, Is.EqualTo(expected.InternalCellX));
            Assert.That(actual.InternalCellY, Is.EqualTo(expected.InternalCellY));
            Assert.That(actual.DistanceToOutlet, Is.EqualTo(expected.DistanceToOutlet));
            Assert.That(actual.HydrologyHeight, Is.EqualTo(expected.HydrologyHeight));
            Assert.That(actual.HydrologyMoisture, Is.EqualTo(expected.HydrologyMoisture));
            Assert.That(actual.PlateauRank, Is.EqualTo(expected.PlateauRank));
        }

        /// <summary>下流選択の優先順位だけを検証するため、必要な固定値を持つ水文セルを作ります。</summary>
        private static GeneratedHydrologyCell CreateSyntheticCell(
            int cellX,
            int cellY,
            ushort height,
            ulong plateauRank,
            bool isOceanOutlet)
        {
            return new GeneratedHydrologyCell
            {
                CellX = cellX,
                CellY = cellY,
                DownstreamCellX = cellX,
                DownstreamCellY = cellY,
                HydrologyHeight = height,
                CenterElevation = isOceanOutlet ? (ushort)1000 : (ushort)40000,
                PlateauRank = plateauRank,
                FlowDirection = HydrologyFlowDirection.None,
                Flags = isOceanOutlet ? HydrologyCellFlags.OceanOutlet : HydrologyCellFlags.None
            };
        }

        /// <summary>
        /// 下流選択テストで指定した8候補を、製品APIが受け取る固定容量Listへ同じ順序で詰めます。
        /// </summary>
        /// <param name="neighbors">基準セルのD8位置として用意した8候補です。</param>
        /// <returns>候補順の不変性や優先順位を検証する値型Listです。</returns>
        private static FixedList512Bytes<HydrologyFlowCandidate> CreateNeighborList(
            params GeneratedHydrologyCell[] neighbors)
        {
            Assert.That(neighbors, Has.Length.EqualTo(8));
            FixedList512Bytes<HydrologyFlowCandidate> result = default;

            // 製品APIは長さ8だけを受け付けるため、テスト入力の不足や過剰を上のAssertで先に明示します。
            for (int index = 0; index < neighbors.Length; index++)
            {
                GeneratedHydrologyCell neighbor = neighbors[index];
                result.Add(new HydrologyFlowCandidate
                {
                    CellX = neighbor.CellX,
                    CellY = neighbor.CellY,
                    PlateauRank = neighbor.PlateauRank,
                    HydrologyHeight = neighbor.HydrologyHeight,
                    Flags = neighbor.Flags
                });
            }

            return result;
        }

        /// <summary>単一セルAPIとチャンク配列の全フィールドを比較し、相違した固定値を明確にします。</summary>
        private static void AssertCellEqual(
            in GeneratedHydrologyCell expected,
            in GeneratedHydrologyCell actual)
        {
            Assert.That(actual.CellX, Is.EqualTo(expected.CellX));
            Assert.That(actual.CellY, Is.EqualTo(expected.CellY));
            Assert.That(actual.DownstreamCellX, Is.EqualTo(expected.DownstreamCellX));
            Assert.That(actual.DownstreamCellY, Is.EqualTo(expected.DownstreamCellY));
            Assert.That(actual.DownstreamReachCellX, Is.EqualTo(expected.DownstreamReachCellX));
            Assert.That(actual.DownstreamReachCellY, Is.EqualTo(expected.DownstreamReachCellY));
            Assert.That(actual.HydrologyHeight, Is.EqualTo(expected.HydrologyHeight));
            Assert.That(actual.CenterElevation, Is.EqualTo(expected.CenterElevation));
            Assert.That(actual.HydrologyMoisture, Is.EqualTo(expected.HydrologyMoisture));
            Assert.That(actual.CatchmentAreaCells, Is.EqualTo(expected.CatchmentAreaCells));
            Assert.That(actual.RunoffMoistureSum, Is.EqualTo(expected.RunoffMoistureSum));
            Assert.That(actual.RunoffEquivalentCells, Is.EqualTo(expected.RunoffEquivalentCells));
            Assert.That(actual.MaximumContributingDistance, Is.EqualTo(expected.MaximumContributingDistance));
            Assert.That(actual.PlateauRank, Is.EqualTo(expected.PlateauRank));
            Assert.That(actual.DownstreamEdgeCount, Is.EqualTo(expected.DownstreamEdgeCount));
            Assert.That(actual.ConnectionFlags, Is.EqualTo(expected.ConnectionFlags));
            Assert.That(actual.BasinAreaCells, Is.EqualTo(expected.BasinAreaCells));
            Assert.That(actual.SpillInsideCellX, Is.EqualTo(expected.SpillInsideCellX));
            Assert.That(actual.SpillInsideCellY, Is.EqualTo(expected.SpillInsideCellY));
            Assert.That(actual.SpillOutsideCellX, Is.EqualTo(expected.SpillOutsideCellX));
            Assert.That(actual.SpillOutsideCellY, Is.EqualTo(expected.SpillOutsideCellY));
            Assert.That(actual.LakeBottomHeight, Is.EqualTo(expected.LakeBottomHeight));
            Assert.That(actual.SpillHeight, Is.EqualTo(expected.SpillHeight));
            Assert.That(actual.LakeFillFraction, Is.EqualTo(expected.LakeFillFraction));
            Assert.That(actual.LakeSurfaceHeight, Is.EqualTo(expected.LakeSurfaceHeight));
            Assert.That(actual.FlowDirection, Is.EqualTo(expected.FlowDirection));
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags));
            Assert.That(actual.CatchmentFlags, Is.EqualTo(expected.CatchmentFlags));
            Assert.That(actual.LakeFlags, Is.EqualTo(expected.LakeFlags));
        }

        /// <summary>
        /// 実生成セルの湖面フラグ、標高、接続を検査し、除外理由と固定湖面が同時に成立しないことを保証します。
        /// </summary>
        /// <param name="cell">閉鎖流域へ到達する生成済み水文セルです。</param>
        private static void AssertLakeStateIsConsistent(in GeneratedHydrologyCell cell)
        {
            bool fixedLake = (cell.LakeFlags & HydrologyLakeFlags.FixedLake) != 0;
            bool surfaceCell = (cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0;
            bool excluded = (cell.LakeFlags
                & (HydrologyLakeFlags.BasinTruncated
                    | HydrologyLakeFlags.BasinTooSmall
                    | HydrologyLakeFlags.InsufficientRunoff
                    | HydrologyLakeFlags.NoPositiveDepth)) != 0;

            Assert.That(fixedLake && excluded, Is.False);
            Assert.That(surfaceCell, Is.EqualTo((cell.ConnectionFlags & HydrologyConnectionFlags.Lake) != 0));
            Assert.That(surfaceCell && !fixedLake, Is.False);
            if (fixedLake)
            {
                Assert.That(cell.BasinAreaCells, Is.GreaterThanOrEqualTo(4u));
                Assert.That(cell.LakeFillFraction, Is.GreaterThanOrEqualTo(16384));
                Assert.That(cell.SpillHeight, Is.GreaterThan(cell.LakeBottomHeight));
                Assert.That(cell.LakeSurfaceHeight, Is.GreaterThan(cell.LakeBottomHeight));
                Assert.That(cell.LakeSurfaceHeight, Is.LessThanOrEqualTo(cell.SpillHeight));
            }

            if ((cell.LakeFlags & HydrologyLakeFlags.Overflow) != 0)
            {
                Assert.That(fixedLake, Is.True);
                Assert.That(cell.LakeFillFraction, Is.EqualTo(ushort.MaxValue));
                Assert.That(cell.LakeSurfaceHeight, Is.EqualTo(cell.SpillHeight));
            }
        }

        /// <summary>合成境界のテスト入力として、指定した全固定値を持つDownstream Summaryを作ります。</summary>
        private static HydrologyDownstreamSummary CreateSyntheticSummary(
            int startCellX,
            int startCellY,
            int reachedCellX,
            int reachedCellY,
            int edgeCount,
            int level,
            HydrologyConnectionFlags terminal)
        {
            return new HydrologyDownstreamSummary
            {
                StartCellX = startCellX,
                StartCellY = startCellY,
                ReachedCellX = reachedCellX,
                ReachedCellY = reachedCellY,
                EdgeCount = (ushort)edgeCount,
                Level = (byte)level,
                Terminal = terminal
            };
        }

        /// <summary>直接構築と二分合成のSummary全フィールドを比較し、キャッシュ経路の差を明確にします。</summary>
        private static void AssertSummaryEqual(
            in HydrologyDownstreamSummary expected,
            in HydrologyDownstreamSummary actual)
        {
            Assert.That(actual.StartCellX, Is.EqualTo(expected.StartCellX));
            Assert.That(actual.StartCellY, Is.EqualTo(expected.StartCellY));
            Assert.That(actual.ReachedCellX, Is.EqualTo(expected.ReachedCellX));
            Assert.That(actual.ReachedCellY, Is.EqualTo(expected.ReachedCellY));
            Assert.That(actual.EdgeCount, Is.EqualTo(expected.EdgeCount));
            Assert.That(actual.Level, Is.EqualTo(expected.Level));
            Assert.That(actual.Terminal, Is.EqualTo(expected.Terminal));
        }
    }
}
