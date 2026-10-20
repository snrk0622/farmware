// このファイルは、TERRAIN-01C3-Aの水文セル座標、9点代表標高、海洋終端、D8流向、ECS用チャンク配列を検証します。
// 下流4,096辺、集水面積、流出、湖は後続枝番の責務とし、ここでは基礎グラフが決定論的で非循環になることを保証します。

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 固定8×8格子とD8辺をSceneやGameObjectなしで検証するEditModeテストです。
    /// 純粋GeneratorとChunkGenerationSystemが共有する入力・出力契約を対象にします。
    /// </summary>
    public sealed class DeterministicHydrologyGeneratorTests
    {
        /// <summary>
        /// 製品既定値を準備し、8タイル格子、3タイルサンプル、1チャンク64セルが確定仕様と一致することを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchTerrain01C3AContract()
        {
            WorldGenerationSettings settings = CreateSettings("hydrology-defaults");

            Assert.That(DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology), Is.True);
            Assert.That(settings.Hydrology.CellSizeInTiles, Is.EqualTo(8));
            Assert.That(settings.Hydrology.RepresentativeSampleOffsetInTiles, Is.EqualTo(3));
            Assert.That(DeterministicHydrologyGenerator.CellsPerChunkAxis, Is.EqualTo(8));
            Assert.That(DeterministicHydrologyGenerator.CellsPerChunk, Is.EqualTo(64));
        }

        /// <summary>
        /// ECS Bufferと生成作業枠の容量見積もりが、実際のUnityレイアウト40 bytesと一致することを保証します。
        /// 後続PRでフィールドを追加した場合に、性能文書と容量見積もりの更新漏れを検出します。
        /// </summary>
        [Test]
        public void GeneratedHydrologyCell_SizeMatchesCapacityDocumentation()
        {
            Assert.That(UnsafeUtility.SizeOf<GeneratedHydrologyCell>(), Is.EqualTo(40));
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
            FixedList512Bytes<GeneratedHydrologyCell> forward = default;
            forward.Add(CreateSyntheticCell(1, 1, 1000, 1, false));
            forward.Add(otherOcean);
            forward.Add(CreateSyntheticCell(0, 1, 2000, 2, false));
            forward.Add(expectedOcean);
            forward.Add(CreateSyntheticCell(-1, -1, 3000, 3, false));
            forward.Add(CreateSyntheticCell(0, -1, 4000, 4, false));
            forward.Add(CreateSyntheticCell(1, -1, 5000, 5, false));
            forward.Add(CreateSyntheticCell(-1, 1, 6000, 6, false));
            FixedList512Bytes<GeneratedHydrologyCell> reverse = default;

            // 同じ8候補を逆順にし、先に見つかった候補を採用する実装へ退行していないことを確認します。
            for (int index = forward.Length - 1; index >= 0; index--)
            {
                reverse.Add(forward[index]);
            }

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(
                    current,
                    forward,
                    out GeneratedHydrologyCell forwardSelected),
                Is.True);
            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(
                    current,
                    reverse,
                    out GeneratedHydrologyCell reverseSelected),
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
            FixedList512Bytes<GeneratedHydrologyCell> withLower = CreateNeighborList(
                lower,
                equalLowerRank,
                CreateSyntheticCell(-1, 0, 101, 1, false),
                CreateSyntheticCell(0, -1, 102, 2, false),
                CreateSyntheticCell(1, 1, 103, 3, false),
                CreateSyntheticCell(-1, 1, 104, 4, false),
                CreateSyntheticCell(-1, -1, 105, 5, false),
                CreateSyntheticCell(1, -1, 106, 6, false));

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(current, withLower, out GeneratedHydrologyCell selectedLower),
                Is.True);
            Assert.That(selectedLower.CellX, Is.EqualTo(lower.CellX));
            Assert.That(selectedLower.CellY, Is.EqualTo(lower.CellY));

            FixedList512Bytes<GeneratedHydrologyCell> equalPlateau = CreateNeighborList(
                equalLowerRank,
                CreateSyntheticCell(1, 0, 100, 600, false),
                CreateSyntheticCell(-1, 0, 101, 1, false),
                CreateSyntheticCell(0, -1, 102, 2, false),
                CreateSyntheticCell(1, 1, 103, 3, false),
                CreateSyntheticCell(-1, 1, 104, 4, false),
                CreateSyntheticCell(-1, -1, 105, 5, false),
                CreateSyntheticCell(1, -1, 106, 6, false));

            Assert.That(
                DeterministicHydrologyGenerator.TrySelectDownstream(current, equalPlateau, out GeneratedHydrologyCell selectedEqual),
                Is.True);
            Assert.That(selectedEqual.CellX, Is.EqualTo(equalLowerRank.CellX));
            Assert.That(selectedEqual.CellY, Is.EqualTo(equalLowerRank.CellY));

            FixedList512Bytes<GeneratedHydrologyCell> blocked = CreateNeighborList(
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
            int oceanCount = 0;
            int landEdgeCount = 0;

            for (int cellY = -24; cellY <= 24; cellY++)
            {
                for (int cellX = -24; cellX <= 24; cellX++)
                {
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            cellX,
                            cellY,
                            out GeneratedHydrologyCell cell),
                        Is.True);

                    if (cell.IsOceanOutlet)
                    {
                        oceanCount++;
                        Assert.That(cell.HasDownstream, Is.False);
                        Assert.That(cell.DownstreamCellX, Is.EqualTo(cell.CellX));
                        Assert.That(cell.DownstreamCellY, Is.EqualTo(cell.CellY));
                        continue;
                    }

                    if (!cell.HasDownstream)
                    {
                        continue;
                    }

                    landEdgeCount++;
                    Assert.That(System.Math.Abs(cell.DownstreamCellX - cell.CellX), Is.LessThanOrEqualTo(1));
                    Assert.That(System.Math.Abs(cell.DownstreamCellY - cell.CellY), Is.LessThanOrEqualTo(1));
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateCell(
                            settings,
                            cell.DownstreamCellX,
                            cell.DownstreamCellY,
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
                        DeterministicHydrologyGenerator.TryGenerateCell(
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
            const ulong initialHash = 0xCBF29CE484222325UL;

            ulong firstHash = DeterministicHydrologyGenerator.AppendChunkHash(first, coordinate, initialHash);
            ulong repeatedHash = DeterministicHydrologyGenerator.AppendChunkHash(first, coordinate, initialHash);
            ulong secondHash = DeterministicHydrologyGenerator.AppendChunkHash(second, coordinate, initialHash);

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
                    out _),
                Is.False);
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    validSettings,
                    int.MinValue,
                    0,
                    out _),
                Is.False);
        }

        /// <summary>テスト名から安定Seedを作り、製品既定値と同じGeneratorVersion 1の設定を返します。</summary>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seedText), 1);
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
        private static FixedList512Bytes<GeneratedHydrologyCell> CreateNeighborList(
            params GeneratedHydrologyCell[] neighbors)
        {
            Assert.That(neighbors, Has.Length.EqualTo(8));
            FixedList512Bytes<GeneratedHydrologyCell> result = default;

            // 製品APIは長さ8だけを受け付けるため、テスト入力の不足や過剰を上のAssertで先に明示します。
            for (int index = 0; index < neighbors.Length; index++)
            {
                result.Add(neighbors[index]);
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
            Assert.That(actual.HydrologyHeight, Is.EqualTo(expected.HydrologyHeight));
            Assert.That(actual.CenterElevation, Is.EqualTo(expected.CenterElevation));
            Assert.That(actual.PlateauRank, Is.EqualTo(expected.PlateauRank));
            Assert.That(actual.FlowDirection, Is.EqualTo(expected.FlowDirection));
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags));
        }
    }
}
