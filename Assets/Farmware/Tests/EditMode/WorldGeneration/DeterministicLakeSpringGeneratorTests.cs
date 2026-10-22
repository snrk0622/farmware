// このファイルは、TERRAIN-01C4-Cの湖水収支区分と泉候補の固定整数境界をEditModeで検証します。
// 純粋計算の端点と安定同点順を保証し、Chunk生成Jobの統合回帰はWorldGenerationTestsと状態ハッシュテストへ委ねます。

using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Farmware.Simulation;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 湖の淡水・塩水・一時区分、泉放出境界、最低隣接セル、抑制優先順を保証するテストです。
    /// </summary>
    public sealed class DeterministicLakeSpringGeneratorTests
    {
        /// <summary>
        /// 共有既定設定を準備し、仕様で固定した比重、境界、倍率、抑制半径が欠けずに検証されることを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchConfirmedContract()
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();

            Assert.That(DeterministicLakeSpringGenerator.ValidateSettings(settings), Is.True);
            Assert.That(settings.LakeFillWeight, Is.EqualTo(2));
            Assert.That(settings.LakeGroundwaterWeight, Is.EqualTo(1));
            Assert.That(settings.LakeClimateRetentionWeight, Is.EqualTo(1));
            Assert.That(settings.LakeWeightTotal, Is.EqualTo(4));
            Assert.That(settings.PermanentSaltwaterLakeThreshold, Is.EqualTo(26214));
            Assert.That(settings.PermanentFreshwaterLakeThreshold, Is.EqualTo(52428));
            Assert.That(settings.SpringDropMultiplier, Is.EqualTo(16));
            Assert.That(settings.SpringDischargeThreshold, Is.EqualTo(39321));
            Assert.That(settings.SpringSuppressionRadiusInCells, Is.EqualTo(4));
        }

        /// <summary>
        /// 充填率、地下水、気候保持の端点を準備し、2：1：1の64bit整数式が0と65,535を正確に返すことを保証します。
        /// </summary>
        [Test]
        public void LakeWaterBalance_HandlesNormalizedEndpoints()
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicLakeSpringGenerator.TryCalculateLakeWaterBalance(
                    settings,
                    0,
                    0,
                    ushort.MaxValue,
                    0,
                    out ushort minimum),
                Is.True);
            Assert.That(minimum, Is.Zero);

            Assert.That(
                DeterministicLakeSpringGenerator.TryCalculateLakeWaterBalance(
                    settings,
                    ushort.MaxValue,
                    ushort.MaxValue,
                    0,
                    ushort.MaxValue,
                    out ushort maximum),
                Is.True);
            Assert.That(maximum, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 水収支の両境界直前・直後を入力し、一時湖、恒久塩湖、閉鎖淡水湖へ隙間なく分類されることを保証します。
        /// </summary>
        /// <param name="waterBalance">分類する0〜65,535湖水収支です。</param>
        /// <param name="expected">期待する越流なし湖の最終区分です。</param>
        [TestCase(26213, LakeWaterBalanceClassification.Temporary)]
        [TestCase(26214, LakeWaterBalanceClassification.PermanentSaltwater)]
        [TestCase(52427, LakeWaterBalanceClassification.PermanentSaltwater)]
        [TestCase(52428, LakeWaterBalanceClassification.ClosedFreshwater)]
        public void LakeClassification_UsesConfirmedBoundaries(
            int waterBalance,
            LakeWaterBalanceClassification expected)
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();

            LakeWaterBalanceClassification actual = DeterministicLakeSpringGenerator.ClassifyLake(
                settings,
                false,
                (ushort)waterBalance);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 低い水収支と有効な越流辺を同時に準備し、水収支境界より越流淡水化が優先されることを保証します。
        /// </summary>
        [Test]
        public void LakeClassification_OverflowAlwaysBecomesFreshwater()
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();

            LakeWaterBalanceClassification actual = DeterministicLakeSpringGenerator.ClassifyLake(
                settings,
                true,
                0);

            Assert.That(actual, Is.EqualTo(LakeWaterBalanceClassification.OverflowFreshwater));
        }

        /// <summary>
        /// 同じ湖面候補へ3区分を適用し、一時湖は固定水面なし、塩湖は淡水接続なし、淡水湖は両方ありになることを保証します。
        /// </summary>
        /// <param name="fillFraction">水収支を各区分へ送る湖面充填率です。</param>
        /// <param name="expectedType">期待する安定表流水Typeです。</param>
        /// <param name="expectedNaturalSurface">固定自然水面フラグを期待する場合はtrueです。</param>
        /// <param name="expectedFreshwater">淡水接続フラグを期待する場合はtrueです。</param>
        /// <param name="maximumClimateRetention">蒸発圧0の最大気候保持力を入力する場合はtrueです。</param>
        [TestCase(0, SurfaceWaterTileType.LakeCandidate, false, false, false)]
        [TestCase(35000, SurfaceWaterTileType.PermanentSaltwaterLake, true, false, false)]
        [TestCase(ushort.MaxValue, SurfaceWaterTileType.PermanentFreshwaterLake, true, true, true)]
        public void FinalizeLakeCandidate_AppliesSurfaceAndFreshwaterFlags(
            int fillFraction,
            SurfaceWaterTileType expectedType,
            bool expectedNaturalSurface,
            bool expectedFreshwater,
            bool maximumClimateRetention)
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();
            GeneratedSurfaceWaterTile candidate = new GeneratedSurfaceWaterTile
            {
                Type = SurfaceWaterTileType.LakeCandidate,
                Flags = SurfaceWaterTileFlags.RechargeCandidate,
                SourceAmount = (ushort)fillFraction,
                SourceCellX = 10,
                SourceCellY = -20
            };

            Assert.That(
                DeterministicLakeSpringGenerator.TryFinalizeLakeCandidate(
                    settings,
                    candidate,
                    false,
                    (ushort)fillFraction,
                    maximumClimateRetention ? (ushort)0 : ushort.MaxValue,
                    maximumClimateRetention ? ushort.MaxValue : (ushort)0,
                    out GeneratedSurfaceWaterTile finalized,
                    out _,
                    out _),
                Is.True);
            Assert.That(finalized.Type, Is.EqualTo(expectedType));
            Assert.That(
                (finalized.Flags & SurfaceWaterTileFlags.RechargeCandidate) != 0,
                Is.True,
                "最終区分後も長期涵養候補だった履歴は維持します。");
            Assert.That(
                (finalized.Flags & SurfaceWaterTileFlags.NaturalSurfaceWater) != 0,
                Is.EqualTo(expectedNaturalSurface));
            Assert.That(
                (finalized.Flags & SurfaceWaterTileFlags.FreshwaterConnected) != 0,
                Is.EqualTo(expectedFreshwater));
        }

        /// <summary>
        /// 1段階の標高差と二つの地下水値を準備し、泉放出度39,320／39,321の採用境界を整数除算で再現することを保証します。
        /// </summary>
        [Test]
        public void SpringDischarge_PreservesThresholdBoundary()
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicLakeSpringGenerator.TryCalculateSpringDischarge(
                    settings,
                    58972,
                    100,
                    99,
                    out ushort below),
                Is.True);
            Assert.That(below, Is.EqualTo(39320));

            Assert.That(
                DeterministicLakeSpringGenerator.TryCalculateSpringDischarge(
                    settings,
                    58974,
                    100,
                    99,
                    out ushort atThreshold),
                Is.True);
            Assert.That(atThreshold, Is.EqualTo(39321));
        }

        /// <summary>
        /// 8近傍に低地、同標高低地、地下水接続低地を配置し、地下水を除外して最低標高とX・Y順で流出先を選ぶことを保証します。
        /// </summary>
        [Test]
        public void SelectLowestSpringOutflowNeighbor_ExcludesGroundwaterAndUsesCoordinateTieBreak()
        {
            GeneratedHydrologyCell current = new GeneratedHydrologyCell
            {
                CellX = 0,
                CellY = 0,
                HydrologyHeight = 30000
            };
            NativeArray<GeneratedHydrologyCell> neighbors = new NativeArray<GeneratedHydrologyCell>(
                8,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            try
            {
                for (int index = 0; index < neighbors.Length; index++)
                {
                    neighbors[index] = new GeneratedHydrologyCell
                    {
                        CellX = index - 4,
                        CellY = 1,
                        HydrologyHeight = 29000
                    };
                }
                neighbors[0] = new GeneratedHydrologyCell
                {
                    CellX = -1,
                    CellY = 0,
                    HydrologyHeight = 10000,
                    ConnectionFlags = HydrologyConnectionFlags.Groundwater
                };
                neighbors[1] = new GeneratedHydrologyCell
                {
                    CellX = 1,
                    CellY = 0,
                    HydrologyHeight = 20000
                };
                neighbors[2] = new GeneratedHydrologyCell
                {
                    CellX = 0,
                    CellY = -1,
                    HydrologyHeight = 20000
                };

                Assert.That(
                    DeterministicLakeSpringGenerator.TrySelectLowestSpringOutflowNeighbor(
                        current,
                        neighbors,
                        out GeneratedHydrologyCell selected),
                    Is.True);
                Assert.That(selected.CellX, Is.EqualTo(0));
                Assert.That(selected.CellY, Is.EqualTo(-1));
                Assert.That(selected.HydrologyHeight, Is.EqualTo(20000));
            }
            finally
            {
                // NativeArrayはテストが途中で失敗しても必ず解放し、後続テストへAllocator警告を残しません。
                neighbors.Dispose();
            }
        }

        /// <summary>
        /// 放出度が異なる候補と同値候補を準備し、最大放出度、次にX・Y昇順で一意な泉源になることを保証します。
        /// </summary>
        [Test]
        public void PreferredSpringCandidate_UsesDischargeThenCoordinateOrder()
        {
            SpringCandidate baseline = new SpringCandidate
            {
                IsCandidate = 1,
                CellX = 5,
                CellY = 5,
                DischargePotential = 50000
            };
            SpringCandidate stronger = new SpringCandidate
            {
                IsCandidate = 1,
                CellX = 9,
                CellY = 9,
                DischargePotential = 50001
            };
            SpringCandidate earlierTie = new SpringCandidate
            {
                IsCandidate = 1,
                CellX = 4,
                CellY = 99,
                DischargePotential = 50000
            };

            Assert.That(
                DeterministicLakeSpringGenerator.IsPreferredSpringCandidate(stronger, baseline),
                Is.True);
            Assert.That(
                DeterministicLakeSpringGenerator.IsPreferredSpringCandidate(earlierTie, baseline),
                Is.True);
            Assert.That(
                DeterministicLakeSpringGenerator.IsPreferredSpringCandidate(baseline, earlierTie),
                Is.False);
        }

        /// <summary>
        /// 所有チャンク外のセルへ隣接河川の幅だけが入る配置を探索し、泉Haloの再計算でもそのセルを涵養済み地下水として扱うことを保証します。
        /// </summary>
        [Test]
        public void RefreshSpringHydrologyRecharge_UsesFullRiverWidthForHaloCells()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("lake-spring-halo-river-width"),
                1);
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace =
                new NativeArray<SurfaceWaterPathSearchNode>(
                    DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            NativeArray<SurfaceWaterPathTile> pathTiles = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<GeneratedHydrologyCell> springHydrologyCells =
                new NativeArray<GeneratedHydrologyCell>(
                    DeterministicLakeSpringGenerator.SpringHydrologyCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            NativeArray<SurfaceRechargeCandidate> springRechargeCandidates =
                new NativeArray<SurfaceRechargeCandidate>(
                DeterministicLakeSpringGenerator.SpringRechargeCandidateCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NativeArray<SurfaceRechargeCandidate> probeRechargeCandidates =
                new NativeArray<SurfaceRechargeCandidate>(
                DeterministicLakeSpringGenerator.SpringRechargeCandidateCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater =
                new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);

            try
            {
                const int HydrologyOrigin = -6;
                // 21×21泉水文は地下水更新結果の比較対象として、全座標を同じ初期値で準備します。
                for (int cacheY = 0;
                    cacheY < DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis;
                    cacheY++)
                {
                    for (int cacheX = 0;
                        cacheX < DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis;
                        cacheX++)
                    {
                        springHydrologyCells[
                            (cacheY * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis) + cacheX] =
                            new GeneratedHydrologyCell
                            {
                                CellX = HydrologyOrigin + cacheX,
                                CellY = HydrologyOrigin + cacheY,
                                HydrologyHeight = 30000,
                                CenterElevation = 30000,
                                CatchmentAreaCells = 1
                            };
                    }
                }

                GeneratedHydrologyCell selectedSource = default;
                int selectedTargetIndex = -1;
                // 中央9×9の河川からその外側Haloへ幅だけが届く実経路を固定範囲で探します。
                // 経路生成器そのものを使うため、Anchor位置をテスト側で推測して境界条件を弱めません。
                for (int sourceY = 0; sourceY <= 8 && selectedTargetIndex < 0; sourceY++)
                {
                    for (int sourceX = 0; sourceX <= 8 && selectedTargetIndex < 0; sourceX++)
                    {
                        for (int directionIndex = 1; directionIndex <= 8 && selectedTargetIndex < 0;
                            directionIndex++)
                        {
                            GetDirectionOffsetForTest(
                                (HydrologyFlowDirection)directionIndex,
                                out int offsetX,
                                out int offsetY);
                            GeneratedHydrologyCell source = new GeneratedHydrologyCell
                            {
                                CellX = sourceX,
                                CellY = sourceY,
                                DownstreamCellX = sourceX + offsetX,
                                DownstreamCellY = sourceY + offsetY,
                                HydrologyHeight = 30000,
                                CenterElevation = 30000,
                                CatchmentAreaCells = 1,
                                RunoffEquivalentCells = 128,
                                FlowDirection = (HydrologyFlowDirection)directionIndex,
                                ConnectionFlags = HydrologyConnectionFlags.River
                            };

                            for (int index = 0; index < probeRechargeCandidates.Length; index++)
                            {
                                probeRechargeCandidates[index] = default;
                            }
                            Assert.That(
                                DeterministicSurfaceWaterGenerator.TryRasterizeChannelWithinBounds(
                                    settings,
                                    source,
                                    0,
                                    0,
                                    WorldGridConstants.LogicalChunkSize - 1,
                                    WorldGridConstants.LogicalChunkSize - 1,
                                    pathWorkspace,
                                    pathTiles,
                                    default,
                                    default,
                                    default,
                                    HydrologyOrigin,
                                    HydrologyOrigin,
                                    probeRechargeCandidates,
                                    surfaceWater),
                                Is.True);

                            for (int targetIndex = 0;
                                targetIndex < probeRechargeCandidates.Length;
                                targetIndex++)
                            {
                                if (probeRechargeCandidates[targetIndex].HasRecharge == 0)
                                {
                                    continue;
                                }

                                int targetX = HydrologyOrigin
                                    + (targetIndex % DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis);
                                int targetY = HydrologyOrigin
                                    + (targetIndex / DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis);
                                bool isCentralIntersectingCell = targetX >= 0 && targetX <= 8
                                    && targetY >= 0 && targetY <= 8;
                                bool isChannelEndpoint = (targetX == source.CellX && targetY == source.CellY)
                                    || (targetX == source.DownstreamCellX
                                        && targetY == source.DownstreamCellY);
                                if (isCentralIntersectingCell || isChannelEndpoint
                                    || !DeterministicGroundwaterHydrologyGenerator.TrySampleAdjustedGeologyAverage(
                                        settings,
                                        targetX,
                                        targetY,
                                        out ushort permeability,
                                        out ushort storage)
                                    || (permeability < storage ? permeability : storage)
                                        < settings.Groundwater.GroundwaterConnectionThreshold)
                                {
                                    continue;
                                }

                                selectedSource = source;
                                selectedTargetIndex = targetIndex;
                                break;
                            }
                        }
                    }
                }

                Assert.That(
                    selectedTargetIndex,
                    Is.GreaterThanOrEqualTo(0),
                    "中央河川の幅だけが高帯水係数Haloセルへ入る固定試料が見つかりませんでした。");
                int selectedSourceIndex =
                    ((selectedSource.CellY - HydrologyOrigin)
                        * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis)
                    + selectedSource.CellX - HydrologyOrigin;
                springHydrologyCells[selectedSourceIndex] = selectedSource;

                GeneratedHydrologyCell before = springHydrologyCells[selectedTargetIndex];
                Assert.That(before.ConnectionFlags, Is.EqualTo(HydrologyConnectionFlags.None));
                Assert.That(
                    DeterministicLakeSpringGenerator.TryRefreshSpringHydrologyRecharge(
                        settings,
                        0,
                        0,
                        probeRechargeCandidates,
                        springHydrologyCells,
                        springRechargeCandidates),
                    Is.True);

                GeneratedHydrologyCell after = springHydrologyCells[selectedTargetIndex];
                Assert.That(
                    springRechargeCandidates[selectedTargetIndex].HasRecharge,
                    Is.EqualTo(1));
                Assert.That(
                    (after.ConnectionFlags & HydrologyConnectionFlags.Groundwater) != 0,
                    Is.True,
                    "所有セルにStream/Riverがなくても、隣接河川幅からの涵養で地下水接続になる必要があります。");
            }
            finally
            {
                // 途中の探索失敗でも全NativeArrayを解放し、Allocator警告を後続テストへ残しません。
                pathWorkspace.Dispose();
                pathTiles.Dispose();
                springHydrologyCells.Dispose();
                springRechargeCandidates.Dispose();
                probeRechargeCandidates.Dispose();
                surfaceWater.Dispose();
            }
        }

        /// <summary>
        /// 海面未満の1タイルだけを泉経路として渡し、SpringStreamと淡水接続を一切書き込まないことを保証します。
        /// </summary>
        [Test]
        public void ApplySpringPath_DoesNotOverwriteTilesBelowSeaLevel()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("lake-spring-below-sea"),
                1);
            using NativeArray<SurfaceWaterPathTile> pathTiles = new NativeArray<SurfaceWaterPathTile>(
                1,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using NativeArray<GeneratedSurfaceWaterTile> surfaceWater =
                new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);

            int selectedWorldTileX = default;
            int selectedWorldTileY = default;
            bool foundBelowSeaTile = false;
            for (int worldTileY = -512; worldTileY <= 512 && !foundBelowSeaTile; worldTileY += 8)
            {
                for (int worldTileX = -512; worldTileX <= 512; worldTileX += 8)
                {
                    Assert.That(
                        DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            worldTileX,
                            worldTileY,
                            out GeneratedEnvironmentData environment),
                        Is.True);
                    if (environment.Elevation < settings.SeaLevelThreshold)
                    {
                        foundBelowSeaTile = true;
                        selectedWorldTileX = worldTileX;
                        selectedWorldTileY = worldTileY;
                        SetPathTile(
                            pathTiles,
                            new SurfaceWaterPathTile
                            {
                                WorldTileX = worldTileX,
                                WorldTileY = worldTileY
                            });
                        break;
                    }
                }
            }

            Assert.That(foundBelowSeaTile, Is.True);
            ChunkCoordinate owner = WorldGrid.ToChunkCoordinate(selectedWorldTileX, selectedWorldTileY);
            int minimumTileX = owner.X * WorldGridConstants.LogicalChunkSize;
            int minimumTileY = owner.Y * WorldGridConstants.LogicalChunkSize;
            int localX = selectedWorldTileX - minimumTileX;
            int localY = selectedWorldTileY - minimumTileY;
            int selectedTileIndex = WorldGrid.ToTileIndex(localX, localY);
            SpringCandidate source = new SpringCandidate
            {
                IsCandidate = 1,
                CellX = 0,
                CellY = 0,
                DischargePotential = ushort.MaxValue
            };
            Assert.That(
                DeterministicLakeSpringGenerator.TryApplySpringPath(
                    settings,
                    source,
                    minimumTileX,
                    minimumTileY,
                    pathTiles,
                    1,
                    surfaceWater),
                Is.True);
            Assert.That(surfaceWater[selectedTileIndex].Type, Is.EqualTo(SurfaceWaterTileType.None));
            Assert.That(surfaceWater[selectedTileIndex].Flags, Is.EqualTo(SurfaceWaterTileFlags.None));
        }

        /// <summary>
        /// 準備済み21×21水文へ一つだけ泉候補を置き、固定小川の表流水とStream接続を追加しても
        /// 基礎D8、集水面積、流出寄与を変更しないことを保証します。
        /// </summary>
        [Test]
        public void FinalizePreparedChunk_AddsSpringWithoutChangingBaseHydrology()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("lake-spring-prepared-integration"),
                1);
            NativeArray<GeneratedHydrologyCell> hydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NativeArray<GeneratedHydrologyCell> intersectingCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NativeArray<HydrologyUpstreamCandidate> frontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<HydrologyUpstreamCandidate> basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<HydrologyBasinLookupSlot> basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<SurfaceWaterPathTile> pathTiles = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            NativeArray<GeneratedHydrologyCell> springHydrologyCells =
                new NativeArray<GeneratedHydrologyCell>(
                    DeterministicLakeSpringGenerator.SpringHydrologyCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            NativeArray<SpringCandidate> springCandidates = new NativeArray<SpringCandidate>(
                DeterministicLakeSpringGenerator.SpringCandidateCellCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            try
            {
                // チャンク(0,0)の泉キャッシュ原点は水文セル(-6,-6)です。全セルの座標と同一標高を先に設定します。
                for (int cacheY = 0;
                    cacheY < DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis;
                    cacheY++)
                {
                    for (int cacheX = 0;
                        cacheX < DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis;
                        cacheX++)
                    {
                        springHydrologyCells[
                            (cacheY * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis) + cacheX] =
                            new GeneratedHydrologyCell
                            {
                                CellX = cacheX - 6,
                                CellY = cacheY - 6,
                                HydrologyHeight = 30000
                            };
                    }
                }

                const int SourceCellX = 4;
                const int SourceCellY = 4;
                const int SourceCacheX = SourceCellX + 6;
                const int SourceCacheY = SourceCellY + 6;
                int sourceCacheIndex =
                    (SourceCacheY * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis)
                    + SourceCacheX;
                GeneratedHydrologyCell source = new GeneratedHydrologyCell
                {
                    CellX = SourceCellX,
                    CellY = SourceCellY,
                    HydrologyHeight = 30000,
                    GroundwaterPotential = ushort.MaxValue,
                    ConnectionFlags = HydrologyConnectionFlags.Groundwater,
                    FlowDirection = HydrologyFlowDirection.East,
                    DownstreamCellX = 5,
                    DownstreamCellY = 4,
                    CatchmentAreaCells = 123,
                    RunoffEquivalentCells = 456
                };
                springHydrologyCells[sourceCacheIndex] = source;

                // 南西セルだけを低くし、泉の合成流出先とタイル経路を一意にします。
                int outflowCacheIndex =
                    ((SourceCacheY - 1) * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis)
                    + SourceCacheX - 1;
                GeneratedHydrologyCell outflow = springHydrologyCells[outflowCacheIndex];
                outflow.HydrologyHeight = 29900;
                springHydrologyCells[outflowCacheIndex] = outflow;

                int intersectingIndex =
                    (SourceCellY * DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
                    + SourceCellX;
                int localIndex =
                    (SourceCellY * DeterministicHydrologyGenerator.CellsPerChunkAxis) + SourceCellX;
                intersectingCells[intersectingIndex] = source;
                hydrologyCells[localIndex] = source;

                Assert.That(
                    DeterministicLakeSpringGenerator.TryFinalizePreparedChunk(
                        settings,
                        0,
                        0,
                        hydrologyCells,
                        intersectingCells,
                        surfaceWater,
                        frontier,
                        basinMembers,
                        basinLookup,
                        pathWorkspace,
                        pathTiles,
                        springHydrologyCells,
                        springCandidates),
                    Is.True);

                GeneratedHydrologyCell finalizedSource = hydrologyCells[localIndex];
                Assert.That(
                    (finalizedSource.ConnectionFlags & HydrologyConnectionFlags.Stream) != 0,
                    Is.True);
                Assert.That(finalizedSource.FlowDirection, Is.EqualTo(HydrologyFlowDirection.East));
                Assert.That(finalizedSource.DownstreamCellX, Is.EqualTo(5));
                Assert.That(finalizedSource.DownstreamCellY, Is.EqualTo(4));
                Assert.That(finalizedSource.CatchmentAreaCells, Is.EqualTo(123));
                Assert.That(finalizedSource.RunoffEquivalentCells, Is.EqualTo(456));

                int springStreamCount = 0;
                for (int index = 0; index < surfaceWater.Length; index++)
                {
                    if (surfaceWater[index].Type == SurfaceWaterTileType.SpringStream)
                    {
                        springStreamCount++;
                    }
                }
                Assert.That(springStreamCount, Is.GreaterThan(0));
            }
            finally
            {
                // 全NativeArrayをテスト所有者が必ず解放し、失敗時もAllocator警告を後続テストへ残しません。
                hydrologyCells.Dispose();
                intersectingCells.Dispose();
                surfaceWater.Dispose();
                frontier.Dispose();
                basinMembers.Dispose();
                basinLookup.Dispose();
                pathWorkspace.Dispose();
                pathTiles.Dispose();
                springHydrologyCells.Dispose();
                springCandidates.Dispose();
            }
        }

        /// <summary>
        /// 泉候補の実レイアウトを確認し、Persistent作業枠のメモリ計算がランタイム構造体と一致することを保証します。
        /// </summary>
        [Test]
        public void SpringCandidate_SizeMatchesRecordedWorkMemory()
        {
            Assert.That(UnsafeUtility.SizeOf<SpringCandidate>(), Is.EqualTo(24));
            Assert.That(
                DeterministicLakeSpringGenerator.SpringRechargeCandidateCount,
                Is.EqualTo(21 * 21));
        }

        /// <summary>テスト用のD8列挙値を水平・垂直オフセットへ変換し、本番の全8方向と同じ隣接関係を作ります。</summary>
        /// <param name="direction">変換するD8流向です。</param>
        /// <param name="offsetX">成功時の水平差分です。</param>
        /// <param name="offsetY">成功時の垂直差分です。</param>
        private static void GetDirectionOffsetForTest(
            HydrologyFlowDirection direction,
            out int offsetX,
            out int offsetY)
        {
            offsetX = direction == HydrologyFlowDirection.East
                || direction == HydrologyFlowDirection.NorthEast
                || direction == HydrologyFlowDirection.SouthEast
                ? 1
                : direction == HydrologyFlowDirection.West
                    || direction == HydrologyFlowDirection.NorthWest
                    || direction == HydrologyFlowDirection.SouthWest
                    ? -1
                    : 0;
            offsetY = direction == HydrologyFlowDirection.North
                || direction == HydrologyFlowDirection.NorthEast
                || direction == HydrologyFlowDirection.NorthWest
                ? 1
                : direction == HydrologyFlowDirection.South
                    || direction == HydrologyFlowDirection.SouthEast
                    || direction == HydrologyFlowDirection.SouthWest
                    ? -1
                    : 0;
        }

        /// <summary>using変数のNativeArrayハンドルを値コピーし、所有権を移さず先頭経路タイルだけを書き込みます。</summary>
        /// <param name="pathTiles">呼出し元が所有し、このメソッドでは破棄しない経路配列です。</param>
        /// <param name="pathTile">先頭要素へ設定する絶対タイル座標です。</param>
        private static void SetPathTile(
            NativeArray<SurfaceWaterPathTile> pathTiles,
            in SurfaceWaterPathTile pathTile)
        {
            pathTiles[0] = pathTile;
        }

        /// <summary>
        /// 製品と同じ決定論ハッシュ生成を1／4／16チャンクで実行し、C4-Cを含む全生成のEditor時間を比較記録します。
        /// 通常回帰テストを重くしないため明示実行とし、環境未確定の絶対合否値は設けません。
        /// </summary>
        /// <param name="chunkCount">同じ4×4座標集合の先頭から生成するチャンク数です。</param>
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(16)]
        [Explicit("C4-Cの1／4／16チャンク性能記録を更新するときだけ実行します。")]
        [Timeout(300000)]
        public void LakeSpringGeneration_ReportsScaling(int chunkCount)
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("lake-spring-performance"),
                1);
            ChunkCoordinate warmupCoordinate = new ChunkCoordinate(WorldLayer.Surface, 8, 8);
            ulong warmup = DeterministicHydrologyGenerator.AppendChunkHash(
                settings,
                warmupCoordinate,
                0xC4C0000000000001UL);
            Assert.That(warmup, Is.Not.Zero);

            ulong aggregate = 0xC4C0000000000001UL;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                // 全TestCaseが同じ4×4座標集合の接頭辞を使い、件数以外の地形差を増やしません。
                int chunkX = (chunkIndex % 4) - 2;
                int chunkY = (chunkIndex / 4) - 2;
                aggregate = DeterministicHydrologyGenerator.AppendChunkHash(
                    settings,
                    new ChunkCoordinate(WorldLayer.Surface, chunkX, chunkY),
                    aggregate);
            }
            stopwatch.Stop();

            TestContext.WriteLine(
                $"C4-C full deterministic generation: {chunkCount} chunks, "
                + $"{stopwatch.Elapsed.TotalMilliseconds:F3} ms, hash={aggregate:X16}");
            Assert.That(aggregate, Is.Not.Zero);
        }

        /// <summary>
        /// 固定設定の比重合計だけを壊し、未確定設定から部分的な湖・泉結果を計算しないことを保証します。
        /// </summary>
        [Test]
        public void InvalidSettings_AreRejectedWithoutPartialResult()
        {
            LakeSpringGenerationSettings settings = LakeSpringGenerationSettings.CreateDefault();
            settings.LakeWeightTotal = 3;

            Assert.That(DeterministicLakeSpringGenerator.ValidateSettings(settings), Is.False);
            Assert.That(
                DeterministicLakeSpringGenerator.TryCalculateLakeWaterBalance(
                    settings,
                    ushort.MaxValue,
                    ushort.MaxValue,
                    0,
                    ushort.MaxValue,
                    out ushort result),
                Is.False);
            Assert.That(result, Is.Zero);
        }
    }
}
