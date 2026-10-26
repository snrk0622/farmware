// このファイルは、TERRAIN-01C4-D2の湿原タイル資格と上下左右4水文セルの最低規模を検証します。
// 製品Jobの4096件統合とは分離し、閾値の境界と斜め非連結を小さい人工入力で読みやすく保証します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 低地、淡水接続、飽和または冠水の資格条件と、15×15範囲内の有限連結探索を検証します。
    /// 製品ECS Bufferへの一括公開と失敗時非公開はWorldGenerationTestsで別に確認します。
    /// </summary>
    public sealed class DeterministicWetlandExtentGeneratorTests
    {
        /// <summary>
        /// 一つの8×8セルに15件と16件の資格タイルを準備し、仕様の最低数を境界値どおり判定することを保証します。
        /// </summary>
        /// <param name="eligibleTileCount">セル内で資格を満たした人工タイル数です。</param>
        /// <param name="expected">広域湿原候補セルになる期待値です。</param>
        [TestCase(15, false)]
        [TestCase(16, true)]
        public void EligibleTileCount_RequiresSixteenTiles(int eligibleTileCount, bool expected)
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicWetlandExtentGenerator.HasMinimumEligibleTiles(
                    settings,
                    eligibleTileCount),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// 標高、淡水接続、飽和・冠水の境界値を一つずつ準備し、3条件をすべて満たす場合だけ資格を得ることを保証します。
        /// </summary>
        /// <param name="elevation">0〜65,535の固定標高です。</param>
        /// <param name="freshwaterConnected">河川、淡水湖、地下水、または半径32内の水源接続です。</param>
        /// <param name="saturation">0〜65,535の自然土壌飽和度です。</param>
        /// <param name="flooding">0〜65,535の自然冠水度です。</param>
        /// <param name="expected">すべての湿原タイル資格を満たす期待値です。</param>
        [TestCase(19999, true, 39321, 0, false)]
        [TestCase(20000, true, 39321, 0, true)]
        [TestCase(36000, true, 0, 39321, true)]
        [TestCase(36001, true, 39321, 65535, false)]
        [TestCase(24000, false, 65535, 65535, false)]
        [TestCase(24000, true, 39320, 39320, false)]
        [TestCase(24000, true, 39321, 39320, true)]
        [TestCase(24000, true, 39320, 39321, true)]
        public void WetlandTileEligibility_UsesInclusiveConfirmedBoundaries(
            int elevation,
            bool freshwaterConnected,
            int saturation,
            int flooding,
            bool expected)
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();

            bool actual = DeterministicWetlandExtentGenerator.IsWetlandTileEligible(
                settings,
                (ushort)elevation,
                freshwaterConnected,
                (ushort)saturation,
                (ushort)flooding);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 河川・地下水接続と各湖区分を個別に準備し、Lake bitだけでは塩湖や一時湖を淡水へ昇格させないことを保証します。
        /// </summary>
        /// <param name="connectionFlags">完成水文セルに設定する重複可能な接続フラグです。</param>
        /// <param name="lakeClassification">C4-Cで確定した湖の水収支区分です。</param>
        /// <param name="expected">湿原資格へ渡す淡水接続の期待値です。</param>
        [TestCase(HydrologyConnectionFlags.Stream, LakeWaterBalanceClassification.None, true)]
        [TestCase(HydrologyConnectionFlags.River, LakeWaterBalanceClassification.None, true)]
        [TestCase(HydrologyConnectionFlags.Groundwater, LakeWaterBalanceClassification.None, true)]
        [TestCase(HydrologyConnectionFlags.Lake, LakeWaterBalanceClassification.OverflowFreshwater, true)]
        [TestCase(HydrologyConnectionFlags.Lake, LakeWaterBalanceClassification.ClosedFreshwater, true)]
        [TestCase(HydrologyConnectionFlags.Lake, LakeWaterBalanceClassification.PermanentSaltwater, false)]
        [TestCase(HydrologyConnectionFlags.Lake, LakeWaterBalanceClassification.Temporary, false)]
        [TestCase(HydrologyConnectionFlags.Lake, LakeWaterBalanceClassification.None, false)]
        [TestCase(HydrologyConnectionFlags.None, LakeWaterBalanceClassification.ClosedFreshwater, false)]
        [TestCase(
            HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.Lake,
            LakeWaterBalanceClassification.PermanentSaltwater,
            true)]
        public void HydrologyCellFreshwaterConnection_RequiresFreshwaterLakeClassification(
            HydrologyConnectionFlags connectionFlags,
            LakeWaterBalanceClassification lakeClassification,
            bool expected)
        {
            var hydrologyCell = new GeneratedHydrologyCell
            {
                ConnectionFlags = connectionFlags,
                LakeClassification = lakeClassification
            };

            bool actual = DeterministicWetlandExtentGenerator
                .IsHydrologyCellFreshwaterConnected(hydrologyCell);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 中央9×9の左端から外側探索帯へ伸びる3セルと4セルを準備し、チャンク境界相当でも最低4セルだけを認めます。
        /// </summary>
        [Test]
        public void ConnectedExtent_CrossesTargetBoundaryAndRequiresFourCells()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            using var candidates = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var visited = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            const int StartX = DeterministicWetlandExtentGenerator.ExtentSearchRadiusInCells;
            const int StartY = 7;

            // 対象9×9の左端から外側へ3セルだけ連結した段階では、広域湿原に昇格しません。
            SetCandidate(candidates, StartX, StartY);
            SetCandidate(candidates, StartX - 1, StartY);
            SetCandidate(candidates, StartX - 2, StartY);
            Assert.That(
                DeterministicWetlandExtentGenerator.HasMinimumConnectedComponent(
                    settings,
                    StartX,
                    StartY,
                    candidates,
                    visited,
                    queue),
                Is.False);

            // 探索帯の端に4セル目を加え、ロード済み隣接Entityなしでも境界越し成分を認識します。
            SetCandidate(candidates, StartX - 3, StartY);
            Assert.That(
                DeterministicWetlandExtentGenerator.HasMinimumConnectedComponent(
                    settings,
                    StartX,
                    StartY,
                    candidates,
                    visited,
                    queue),
                Is.True);
        }

        /// <summary>
        /// 斜めに接触する4候補セルを準備し、上下左右だけを水文セルの連結として数えることを保証します。
        /// </summary>
        [Test]
        public void ConnectedExtent_DoesNotConnectDiagonalCells()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            using var candidates = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var visited = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            SetCandidate(candidates, 5, 5);
            SetCandidate(candidates, 6, 6);
            SetCandidate(candidates, 7, 7);
            SetCandidate(candidates, 8, 8);

            Assert.That(
                DeterministicWetlandExtentGenerator.HasMinimumConnectedComponent(
                    settings,
                    5,
                    5,
                    candidates,
                    visited,
                    queue),
                Is.False);
        }

        /// <summary>
        /// 製品と同じ状態ハッシュ経路を1／4／16チャンクで実行し、C4-D2追加後のEditor時間を比較記録します。
        /// 通常回帰を重くしない明示テストであり、未確定の目標FPSから絶対的な合格値を作りません。
        /// </summary>
        /// <param name="chunkCount">同じ4×4座標集合の先頭から生成するチャンク数です。</param>
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(16)]
        [Explicit("C4-D2の1／4／16チャンク性能記録を更新するときだけ実行します。")]
        [Timeout(300000)]
        public void NaturalFloodingWetlandGeneration_ReportsScaling(int chunkCount)
        {
            using TerrainGenerationTestCatalogSet catalogs =
                TerrainGenerationTestCatalogSet.Create();
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("natural-flooding-wetland-performance"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            ulong warmup = DeterministicHydrologyGenerator.AppendChunkHash(
                settings,
                biomeSettings,
                catalogs.PaletteCatalog,
                catalogs.RuntimeIds,
                new ChunkCoordinate(WorldLayer.Surface, 8, 8),
                0xC4D0000000000001UL);
            Assert.That(warmup, Is.Not.Zero);

            ulong aggregate = 0xC4D0000000000001UL;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                // 全TestCaseが同じ4×4座標集合の接頭辞を使い、件数以外の地形差を増やしません。
                int chunkX = (chunkIndex % 4) - 2;
                int chunkY = (chunkIndex / 4) - 2;
                aggregate = DeterministicHydrologyGenerator.AppendChunkHash(
                    settings,
                    biomeSettings,
                    catalogs.PaletteCatalog,
                    catalogs.RuntimeIds,
                    new ChunkCoordinate(WorldLayer.Surface, chunkX, chunkY),
                    aggregate);
            }
            stopwatch.Stop();

            TestContext.WriteLine(
                $"C4-D2 full deterministic generation: {chunkCount} chunks, "
                + $"{stopwatch.Elapsed.TotalMilliseconds:F3} ms, hash={aggregate:X16}");
            Assert.That(aggregate, Is.Not.Zero);
        }

        /// <summary>
        /// 人工候補配列の指定座標へ1を書き、行優先添字の計算を各テストで重複させません。
        /// </summary>
        /// <param name="candidates">テストが所有し、呼出し後も破棄する15×15配列です。</param>
        /// <param name="x">配列内の0〜14の水平座標です。</param>
        /// <param name="y">配列内の0〜14の垂直座標です。</param>
        private static void SetCandidate(NativeArray<byte> candidates, int x, int y)
        {
            candidates[(y * DeterministicWetlandExtentGenerator.EvaluationCellsPerAxis) + x] = 1;
        }
    }
}
