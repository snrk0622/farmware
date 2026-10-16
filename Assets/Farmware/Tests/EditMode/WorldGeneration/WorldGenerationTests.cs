// このファイルは、負座標変換、地形・資源の決定性、初期スポーン探索と記録、ECS生成SystemをEditModeで検証します。
// 失敗時にどの仕様が崩れたか分かるよう、純粋計算とEntityデータフローを分けてテストします。

using System;
using System.Collections.Generic;
using System.Reflection;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// Phase 1〜3の決定論的チャンク基盤が、境界値とECS実行の両方で同じ結果を作ることを保証します。
    /// Unity Test RunnerのEditModeテストから実行されます。
    /// </summary>
    public sealed class WorldGenerationTests
    {
        /// <summary>
        /// ワールド生成SystemのUpdateInGroup属性を読み、Unity標準Groupへ直接ぶら下がらずFarmware共通Groupを使うことを保証します。
        /// 初期探索とフレーム駆動のチャンク処理を、後続の固定Tickゲームルールと混同しないための境界テストです。
        /// </summary>
        [Test]
        public void WorldGenerationSystems_UseFarmwareCommonGroups()
        {
            AssertSystemUpdatesInGroup(
                typeof(InitialSpawnLocationSystem),
                typeof(FarmwareInitializationSystemGroup));
            AssertSystemUpdatesInGroup(
                typeof(ChunkStreamingSystem),
                typeof(FarmwareSimulationSystemGroup));
            AssertSystemUpdatesInGroup(
                typeof(ChunkGenerationSystem),
                typeof(FarmwareSimulationSystemGroup));
        }

        /// <summary>
        /// 正負の境界タイルを準備し、床方向除算で期待するチャンク座標とローカル座標になることを検証します。
        /// </summary>
        /// <param name="tile">XとYの両方に使うワールドタイル座標です。</param>
        /// <param name="expectedChunk">期待するXとY共通の論理チャンク座標です。</param>
        /// <param name="expectedLocal">期待するXとY共通のチャンク内座標です。</param>
        [TestCase(0, 0, 0)]
        [TestCase(63, 0, 63)]
        [TestCase(64, 1, 0)]
        [TestCase(-1, -1, 63)]
        [TestCase(-64, -1, 0)]
        [TestCase(-65, -2, 63)]
        public void CoordinateConversion_UsesFloorDivisionForNegativeTiles(
            int tile,
            int expectedChunk,
            int expectedLocal)
        {
            ChunkCoordinate chunk = WorldGrid.ToChunkCoordinate(tile, tile);
            ChunkLocalTileCoordinate local = WorldGrid.ToLocalTileCoordinate(tile, tile);

            Assert.That(chunk.X, Is.EqualTo(expectedChunk));
            Assert.That(chunk.Y, Is.EqualTo(expectedChunk));
            Assert.That(local.X, Is.EqualTo(expectedLocal));
            Assert.That(local.Y, Is.EqualTo(expectedLocal));
        }

        /// <summary>
        /// intタイル座標域の正負端チャンクを準備し、通常の0〜63ローカル座標を端の絶対座標へ安全に変換できることを保証します。
        /// </summary>
        /// <param name="chunkCoordinate">正または負の端にある論理チャンク座標です。</param>
        /// <param name="localCoordinate">端の絶対タイルへ対応するチャンク内座標です。</param>
        /// <param name="expectedWorldTileCoordinate">期待するint最小または最大の絶対タイル座標です。</param>
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, 0, int.MinValue)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, 63, int.MaxValue)]
        public void TryToWorldTileCoordinate_WorldEdge_ConvertsValidLocalTile(
            int chunkCoordinate,
            int localCoordinate,
            int expectedWorldTileCoordinate)
        {
            bool converted = WorldGrid.TryToWorldTileCoordinate(
                chunkCoordinate,
                chunkCoordinate,
                localCoordinate,
                localCoordinate,
                out int worldTileX,
                out int worldTileY);

            Assert.That(converted, Is.True);
            Assert.That(worldTileX, Is.EqualTo(expectedWorldTileCoordinate));
            Assert.That(worldTileY, Is.EqualTo(expectedWorldTileCoordinate));
        }

        /// <summary>
        /// ワールド端チャンクから外側の境界・岩壁影サンプルを準備し、反対側のint端へ周回せず変換を拒否することを保証します。
        /// </summary>
        /// <param name="chunkCoordinate">正または負の端にある論理チャンク座標です。</param>
        /// <param name="localX">境界ハイライトまたは岩壁影が参照する水平ローカル座標です。</param>
        /// <param name="localY">境界ハイライトまたは岩壁影が参照する垂直ローカル座標です。</param>
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, -8, 0)]
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, 0, -8)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, 71, 0)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, 0, 71)]
        public void TryToWorldTileCoordinate_WorldEdge_RejectsOutsideNeighborSample(
            int chunkCoordinate,
            int localX,
            int localY)
        {
            bool converted = WorldGrid.TryToWorldTileCoordinate(
                chunkCoordinate,
                chunkCoordinate,
                localX,
                localY,
                out int worldTileX,
                out int worldTileY);

            Assert.That(converted, Is.False);
            Assert.That(worldTileX, Is.Zero);
            Assert.That(worldTileY, Is.Zero);
        }

        /// <summary>
        /// ワールド外となる表示用隣接セルを準備し、境界ハイライトでは非接続値、岩壁影では空地として扱うことを保証します。
        /// privateメソッドを直接呼ぶことで、範囲検査APIを追加してもPresenterが未検査変換へ戻る退行を検出します。
        /// </summary>
        /// <param name="chunkCoordinate">正または負の端にある論理チャンク座標です。</param>
        /// <param name="outsideLocalCoordinate">表示処理が参照するワールド外ローカル座標です。</param>
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, -8)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, 71)]
        public void ChunkTilemapPresenter_WorldEdge_TreatsOutsideNeighborAsDisconnected(
            int chunkCoordinate,
            int outsideLocalCoordinate)
        {
            using World world = new World("WorldEdgePresentationNeighborTests");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<GeneratedTile> tiles = world.EntityManager.AddBuffer<GeneratedTile>(entity);
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("world-edge-presentation-neighbor-test"),
                1);
            MethodInfo getGeneratedTile = typeof(ChunkTilemapPresenter).GetMethod(
                "GetGeneratedTile",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo isRockWallNeighbor = typeof(ChunkTilemapPresenter).GetMethod(
                "IsRockWallNeighbor",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(getGeneratedTile, Is.Not.Null);
            Assert.That(isRockWallNeighbor, Is.Not.Null);
            object[] arguments =
            {
                new ChunkCoordinate(chunkCoordinate, chunkCoordinate),
                outsideLocalCoordinate,
                0,
                tiles,
                settings
            };

            GeneratedTile presentationNeighbor = (GeneratedTile)getGeneratedTile.Invoke(null, arguments);
            bool isRockWall = (bool)isRockWallNeighbor.Invoke(null, arguments);

            Assert.That(
                presentationNeighbor.Ground,
                Is.EqualTo(GroundType.Water),
                "存在しない隣接セルは、土地・岩壁のどちらにも接続しない表示専用値にする必要があります。");
            Assert.That(isRockWall, Is.False);
        }

        /// <summary>
        /// F4の可視境界をint座標域の正負端へ置き、最外周を含むグリッド線数を有限な64bit値で求めることを保証します。
        /// </summary>
        /// <param name="minimumTileBoundary">検証する最小タイル境界です。</param>
        /// <param name="maximumTileBoundary">検証する最大タイル境界です。int最大タイルの外周も含みます。</param>
        /// <param name="intervalInTiles">タイル、サブチャンク、チャンクの境界間隔です。</param>
        /// <param name="expectedLineCount">期待する有限な境界線数です。</param>
        [TestCase((long)int.MaxValue - 1L, (long)int.MaxValue + 1L, 1, 3L)]
        [TestCase((long)int.MaxValue - 1L, (long)int.MaxValue + 1L, 16, 2L)]
        [TestCase((long)int.MinValue, (long)int.MinValue + 1L, 1, 2L)]
        [TestCase((long)int.MinValue + 1L, (long)int.MinValue + 65L, 64, 2L)]
        public void CalculateGridBoundaryCount_IntEdges_ReturnsFiniteCount(
            long minimumTileBoundary,
            long maximumTileBoundary,
            int intervalInTiles,
            long expectedLineCount)
        {
            long actual = WorldGridDebugOverlay.CalculateGridBoundaryCount(
                minimumTileBoundary,
                maximumTileBoundary,
                intervalInTiles);

            Assert.That(actual, Is.EqualTo(expectedLineCount));
        }

        /// <summary>
        /// 指定SystemのUpdateInGroup属性を取得し、期待するFarmware共通Groupへ1回だけ所属していることを確認します。
        /// </summary>
        /// <param name="systemType">所属先を確認するワールド生成System型です。</param>
        /// <param name="expectedGroupType">期待するFarmware共通System Group型です。</param>
        private static void AssertSystemUpdatesInGroup(Type systemType, Type expectedGroupType)
        {
            object[] attributes = systemType.GetCustomAttributes(
                typeof(UpdateInGroupAttribute),
                false);

            Assert.That(
                attributes,
                Has.Length.EqualTo(1),
                $"{systemType.Name}には共通親Groupを1件だけ指定してください。");
            UpdateInGroupAttribute updateInGroup = (UpdateInGroupAttribute)attributes[0];
            Assert.That(updateInGroup.GroupType, Is.EqualTo(expectedGroupType));
        }

        /// <summary>
        /// 既知の文字列を入力し、FNV-1aの固定値と一致することで.NETの可変HashCodeへ戻っていないことを保証します。
        /// </summary>
        [Test]
        public void SeedConversion_KnownTextProducesStableValue()
        {
            ulong actual = WorldSeedUtility.FromString("Farmware");

            Assert.That(actual, Is.EqualTo(0x3EE49B3100677454UL));
        }

        /// <summary>
        /// 用途別Saltの全public定数を準備し、異なる生成用途が同じ乱数系列を共有していないことを保証します。
        /// Reflectionで定数を列挙することで、将来Saltが追加されたときもテスト一覧へ手作業で追記せず重複を検出します。
        /// </summary>
        [Test]
        public void PurposeSalts_AllPublicConstantsAreUnique()
        {
            FieldInfo[] saltFields = typeof(WorldGenerationPurposeSalts).GetFields(
                BindingFlags.Public | BindingFlags.Static);
            HashSet<ulong> usedSalts = new HashSet<ulong>();

            // 同じ値が2用途へ割り当てられるとノイズ形状が意図せず相関するため、名前ではなく実際の64bit値を比較します。
            foreach (FieldInfo saltField in saltFields)
            {
                object constantValue = saltField.GetRawConstantValue();
                Assert.That(
                    constantValue,
                    Is.TypeOf<ulong>(),
                    $"{saltField.Name}は64bit符号なし整数の定数で定義してください。");
                Assert.That(
                    usedSalts.Add((ulong)constantValue),
                    Is.True,
                    $"{saltField.Name}のPurposeSaltが別用途と重複しています。");
            }
        }

        /// <summary>
        /// 格子境界の直前と直後を準備し、SmoothStep補間が正負どちらの境界でも急な段差を作らないことを検証します。
        /// </summary>
        [Test]
        public void NoiseSampling_AcrossGridBoundaryRemainsContinuous()
        {
            const ulong seed = 0x123456789ABCDEF0UL;
            const int cellSize = 64;
            const int maximumOneTileDifference = 128;

            ushort positiveBefore = DeterministicNoise.SampleValueNoise(
                seed, 1, 63, 17, cellSize, WorldGenerationPurposeSalts.Elevation);
            ushort positiveAfter = DeterministicNoise.SampleValueNoise(
                seed, 1, 64, 17, cellSize, WorldGenerationPurposeSalts.Elevation);
            ushort negativeBefore = DeterministicNoise.SampleValueNoise(
                seed, 1, -1, 17, cellSize, WorldGenerationPurposeSalts.Elevation);
            ushort negativeAfter = DeterministicNoise.SampleValueNoise(
                seed, 1, 0, 17, cellSize, WorldGenerationPurposeSalts.Elevation);

            // 128/65535は約0.2%です。整数丸めは許容しつつ、視認できる格子境界の段差は検出します。
            Assert.That(Math.Abs(positiveAfter - positiveBefore), Is.LessThanOrEqualTo(maximumOneTileDifference));
            Assert.That(Math.Abs(negativeAfter - negativeBefore), Is.LessThanOrEqualTo(maximumOneTileDifference));
        }

        /// <summary>
        /// 同じ設定で複数チャンクを異なる順序に計算し、各座標のハッシュが呼び出し順に依存しないことを保証します。
        /// </summary>
        [Test]
        public void ChunkHash_IsIndependentFromGenerationOrder()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("order-test"),
                1);
            ChunkCoordinate firstCoordinate = new ChunkCoordinate(-2, 3);
            ChunkCoordinate secondCoordinate = new ChunkCoordinate(4, -1);

            ulong firstThenSecondFirst = DeterministicChunkGenerator.ComputeChunkHash(settings, firstCoordinate);
            ulong firstThenSecondSecond = DeterministicChunkGenerator.ComputeChunkHash(settings, secondCoordinate);
            ulong secondThenFirstSecond = DeterministicChunkGenerator.ComputeChunkHash(settings, secondCoordinate);
            ulong secondThenFirstFirst = DeterministicChunkGenerator.ComputeChunkHash(settings, firstCoordinate);

            Assert.That(firstThenSecondFirst, Is.EqualTo(secondThenFirstFirst));
            Assert.That(firstThenSecondSecond, Is.EqualTo(secondThenFirstSecond));
            Assert.That(firstThenSecondFirst, Is.Not.EqualTo(firstThenSecondSecond));
        }

        /// <summary>
        /// 隣接する正負チャンクの端を準備し、チャンク生成値が同じワールド座標の直接生成値と一致することを保証します。
        /// </summary>
        [Test]
        public void TileGeneration_ChunkEdgesUseWorldCoordinates()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("edge-test"),
                1);

            AssertTileEqual(
                DeterministicChunkGenerator.GenerateTile(settings, 63, -1),
                GenerateTileThroughChunkCoordinates(settings, 0, -1, 63, 63));
            AssertTileEqual(
                DeterministicChunkGenerator.GenerateTile(settings, 64, 0),
                GenerateTileThroughChunkCoordinates(settings, 1, 0, 0, 0));
            AssertTileEqual(
                DeterministicChunkGenerator.GenerateTile(settings, -65, -64),
                GenerateTileThroughChunkCoordinates(settings, -2, -1, 63, 0));
        }

        /// <summary>
        /// 正負座標と64×64チャンク境界を準備し、軽量な岩壁判定が完全な地形生成結果と一致することを保証します。
        /// Presentationが境界外セルだけを再判定しても、SimulationのGroundTypeと食い違わないための回帰テストです。
        /// </summary>
        [Test]
        public void RockWallProbe_MatchesGeneratedGroundAcrossChunkEdges()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("rock-wall-probe-test"),
                1);
            int[] tileCoordinates = { -65, -64, -63, -1, 0, 1, 63, 64, 65 };

            // XとYへ同じ境界候補を組み合わせ、正負4象限とチャンク端の判定を少数の81セルで確認します。
            for (int yIndex = 0; yIndex < tileCoordinates.Length; yIndex++)
            {
                for (int xIndex = 0; xIndex < tileCoordinates.Length; xIndex++)
                {
                    int tileX = tileCoordinates[xIndex];
                    int tileY = tileCoordinates[yIndex];
                    bool expected = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        tileX,
                        tileY).Ground == GroundType.RockWall;
                    bool actual = DeterministicChunkGenerator.IsRockWall(settings, tileX, tileY);

                    Assert.That(
                        actual,
                        Is.EqualTo(expected),
                        $"岩壁の軽量判定が完全生成と不一致です: ({tileX}, {tileY})");
                }
            }
        }

        /// <summary>
        /// 128×128タイル分の最終岩壁候補を直接数え、既定値がリンク経路へ適度な迂回を作る占有率に収まることを保証します。
        /// 地形全体を生成せず岩壁純粋計算だけを使い、日常実行するEditModeテストを現実的な時間へ抑えます。
        /// </summary>
        [Test]
        public void RockWallNoise_DefaultSettingsCreateModerateCoverage()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("rock-wall-coverage-test"),
                1);
            const int sampleSize = 128;
            int rockWallCandidateCount = 0;

            // 複数のノイズ格子と正負座標を含む固定範囲を走査し、局所的な密度差だけへ結果が偏らないようにします。
            for (int tileY = -(sampleSize / 2); tileY < sampleSize / 2; tileY++)
            {
                for (int tileX = -(sampleSize / 2); tileX < sampleSize / 2; tileX++)
                {
                    if (DeterministicRockWallGenerator.IsRockWallCandidate(settings, tileX, tileY))
                    {
                        rockWallCandidateCount++;
                    }
                }
            }

            float coverage = rockWallCandidateCount / (float)(sampleSize * sampleSize);
            Assert.That(
                coverage,
                Is.InRange(0.08f, 0.25f),
                $"既定岩壁候補率が想定範囲外です。Coverage={coverage:P2}");
        }

        /// <summary>
        /// 岩壁候補と水域が重なる座標を探し、海・川・池の判定が優先されて岩壁データを重ねないことを保証します。
        /// </summary>
        [Test]
        public void RockWallNoise_WaterAlwaysSuppressesRockWallGround()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("rock-wall-water-priority-test"),
                1);
            settings.SeaLevelThreshold = ushort.MaxValue;
            int overlappingWaterCandidateCount = 0;

            // 64×64だけを調べれば複数のノイズ形状を含み、重い広域生成をせず優先順位を確認できます。
            for (int tileY = -32; tileY < 32; tileY++)
            {
                for (int tileX = -32; tileX < 32; tileX++)
                {
                    if (!DeterministicRockWallGenerator.IsRockWallCandidate(settings, tileX, tileY))
                    {
                        continue;
                    }

                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(settings, tileX, tileY);
                    if (tile.WaterBodyType == WaterBodyType.None)
                    {
                        continue;
                    }

                    overlappingWaterCandidateCount++;
                    Assert.That(tile.Ground, Is.EqualTo(GroundType.Water));
                    Assert.That(DeterministicChunkGenerator.IsRockWall(settings, tileX, tileY), Is.False);
                }
            }

            Assert.That(
                overlappingWaterCandidateCount,
                Is.GreaterThan(0),
                "試験範囲で水域と岩壁候補の重なりを確認できませんでした。");
        }

        /// <summary>
        /// 生の密度場、平滑化後の最終形状、通路削りを同じ範囲で調べ、固定図形ではない複雑な輪郭と空路が生じることを保証します。
        /// 連結成分を広域探索せず3×3近傍パターンだけを数えるため、形状退行を軽量に検出できます。
        /// </summary>
        [Test]
        public void RockWallNoise_CreatesVariedEdgesAndCarvedCorridors()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("rock-wall-variety-test"),
                1);
            const int sampleRadius = 32;
            const int sampleDiameter = sampleRadius * 2;
            const int paddedDiameter = sampleDiameter + 2;
            bool[] observedPatterns = new bool[512];
            bool[] finalCandidates = new bool[paddedDiameter * paddedDiameter];
            int distinctPatternCount = 0;
            int carvedRawCandidateCount = 0;

            // 1タイル外側まで一度だけ生成し、3×3パターンごとに同じ重いノイズ計算を繰り返さないようにします。
            for (int paddedY = 0; paddedY < paddedDiameter; paddedY++)
            {
                for (int paddedX = 0; paddedX < paddedDiameter; paddedX++)
                {
                    int tileX = paddedX - sampleRadius - 1;
                    int tileY = paddedY - sampleRadius - 1;
                    RockWallGenerationDebugInfo centerInfo =
                        DeterministicRockWallGenerator.GetDebugInfo(settings, tileX, tileY);
                    finalCandidates[(paddedY * paddedDiameter) + paddedX] =
                        centerInfo.FinalCandidate;
                    if (centerInfo.RawCandidate && centerInfo.Corridor)
                    {
                        carvedRawCandidateCount++;
                    }
                }
            }

            // 各中心の3×3最終候補を9bitへ詰め、楕円や長方形だけでは現れにくい多様な局所輪郭を数えます。
            for (int centerY = 1; centerY <= sampleDiameter; centerY++)
            {
                for (int centerX = 1; centerX <= sampleDiameter; centerX++)
                {
                    int pattern = 0;
                    int bit = 0;
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            int candidateIndex =
                                ((centerY + offsetY) * paddedDiameter) + centerX + offsetX;
                            if (finalCandidates[candidateIndex])
                            {
                                pattern |= 1 << bit;
                            }

                            bit++;
                        }
                    }

                    if (!observedPatterns[pattern])
                    {
                        observedPatterns[pattern] = true;
                        distinctPatternCount++;
                    }
                }
            }

            // 通路は長い低周波帯なので、局所パターン範囲に交差しない場合だけ広い範囲を軽い生密度判定で探します。
            for (int tileY = -128; tileY < 128 && carvedRawCandidateCount == 0; tileY++)
            {
                for (int tileX = -128; tileX < 128; tileX++)
                {
                    bool isRawCandidate =
                        DeterministicRockWallGenerator.SampleDensity(settings, tileX, tileY)
                        >= settings.RockWallNoiseThreshold;
                    if (isRawCandidate
                        && DeterministicRockWallGenerator.IsCorridor(settings, tileX, tileY))
                    {
                        carvedRawCandidateCount++;
                        break;
                    }
                }
            }

            Assert.That(distinctPatternCount, Is.GreaterThanOrEqualTo(20));
            Assert.That(carvedRawCandidateCount, Is.GreaterThan(0));
        }

        /// <summary>
        /// 現在の試作既定値を作り、64×64チャンクとは独立したバイオーム規模と移行帯幅が設定されることを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_UseTunedBiomeScaleAndTransitionWidth()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("default-settings-test"),
                2);

            Assert.That(settings.TemperatureCellSizeInTiles, Is.EqualTo(96));
            Assert.That(settings.MoistureCellSizeInTiles, Is.EqualTo(80));
            Assert.That(settings.WeirdnessCellSizeInTiles, Is.EqualTo(112));
            Assert.That(settings.BiomeWarpCellSizeInTiles, Is.EqualTo(192));
            Assert.That(settings.BiomeWarpAmplitudeInTiles, Is.EqualTo(12));
            Assert.That(settings.BiomeClassificationTransitionWidthInTiles, Is.EqualTo(12));
            Assert.That(settings.BiomeTransitionWidthInTiles, Is.EqualTo(6));
            Assert.That(settings.ColdTemperatureThreshold, Is.EqualTo(23000));
            Assert.That(settings.WetlandMoistureThreshold, Is.EqualTo(50000));
            Assert.That(settings.WetlandMaximumElevation, Is.EqualTo(36000));
            Assert.That(settings.HighlandElevationThreshold, Is.EqualTo(43000));
            Assert.That(settings.VolcanicWeirdnessThreshold, Is.EqualTo(47000));
            Assert.That(settings.BiomeAdjacencyTransitionScoreMargin, Is.EqualTo(18000));
            Assert.That(settings.RiverCellSizeInTiles, Is.EqualTo(96));
            Assert.That(settings.PondCellSizeInTiles, Is.EqualTo(48));
            Assert.That(settings.FreshWaterOctaveCount, Is.EqualTo(2));
            Assert.That(settings.WetClimatePondThresholdReduction, Is.EqualTo(8500));
            Assert.That(settings.WetlandPondThresholdReduction, Is.EqualTo(8500));
            Assert.That(settings.DesertPondThresholdIncrease, Is.EqualTo(5000));
            Assert.That(settings.HighlandPondThresholdIncrease, Is.EqualTo(3500));
            Assert.That(settings.VolcanicPondThresholdIncrease, Is.EqualTo(16000));
            Assert.That(settings.WetlandRiverHalfWidthIncrease, Is.EqualTo(500));
            Assert.That(settings.VolcanicRiverHalfWidthReduction, Is.EqualTo(450));
            Assert.That(settings.MagmaCellSizeInTiles, Is.EqualTo(24));
            Assert.That(settings.MagmaDetailCellSizeInTiles, Is.EqualTo(8));
            Assert.That(settings.MagmaWarpAmplitudeInTiles, Is.EqualTo(6));
            Assert.That(settings.MagmaOctaveCount, Is.EqualTo(3));
            Assert.That(settings.MagmaNoiseThreshold, Is.EqualTo(34000));
            Assert.That(settings.MagmaInfluenceNoiseThreshold, Is.EqualTo(30000));
            Assert.That(settings.MagmaMinimumVolcanicInfluence, Is.EqualTo(0));
            Assert.That(settings.MagmaInfluenceMinimumVolcanicInfluence, Is.EqualTo(0));
            Assert.That(settings.RocklandMagmaCellSizeInTiles, Is.EqualTo(12));
            Assert.That(settings.RocklandMagmaNoiseThreshold, Is.EqualTo(47000));
            Assert.That(settings.RocklandMagmaInfluenceNoiseThreshold, Is.EqualTo(44000));
            Assert.That(settings.RocklandMagmaMinimumVolcanicInfluence, Is.EqualTo(100));
            Assert.That(settings.RocklandMagmaInfluenceMinimumVolcanicInfluence, Is.EqualTo(50));
            Assert.That(settings.RockWallMassCellSizeInTiles, Is.EqualTo(24));
            Assert.That(settings.RockWallDetailCellSizeInTiles, Is.EqualTo(8));
            Assert.That(settings.RockWallWarpAmplitudeInTiles, Is.EqualTo(6));
            Assert.That(settings.RockWallNoiseThreshold, Is.EqualTo(41000));
            Assert.That(settings.RockWallCorridorHalfWidth, Is.EqualTo(1100));
        }

        /// <summary>
        /// 同じ水域形状ノイズ座標へ代表的な気候値だけを渡し、湿潤域では池と川が増え、乾燥・高地・火山では池が減る連続補正を検証します。
        /// 広域マップを走査せず7回の純粋計算だけで、バイオーム別の水域方針と火山中心部の池禁止を日常的に確認できます。
        /// </summary>
        [Test]
        public void WaterClimateModifier_AdjustsPondsAndRiversWithoutDiscreteBiomeChecks()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("water-climate-modifier-test"),
                1);
            const int sampleTileX = 17;
            const int sampleTileY = -23;

            WaterGenerationDebugInfo grassland = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                35000, 32000, 34000, 30000, settings, sampleTileX, sampleTileY);
            WaterGenerationDebugInfo forest = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                35000, 32000, 45000, 30000, settings, sampleTileX, sampleTileY);
            WaterGenerationDebugInfo wetland = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                28000, 32000, 60000, 30000, settings, sampleTileX, sampleTileY);
            WaterGenerationDebugInfo desert = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                35000, 50000, 15000, 30000, settings, sampleTileX, sampleTileY);
            WaterGenerationDebugInfo rockland = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                46000, 32000, 25000, 30000, settings, sampleTileX, sampleTileY);
            WaterGenerationDebugInfo snowfield = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                40000, 10000, 34000, 30000, settings, sampleTileX, sampleTileY);
            WaterGenerationDebugInfo volcanic = DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                46000, 50000, 25000, 60000, settings, sampleTileX, sampleTileY);

            // 池下限が低いほど池になりやすいため、森林より湿地を強く優遇し、砂漠と岩地では基準草原より抑制されることを比較します。
            Assert.That(forest.EffectivePondThreshold, Is.LessThan(grassland.EffectivePondThreshold));
            Assert.That(wetland.EffectivePondThreshold, Is.LessThan(forest.EffectivePondThreshold));
            Assert.That(desert.EffectivePondThreshold, Is.GreaterThan(grassland.EffectivePondThreshold));
            Assert.That(rockland.EffectivePondThreshold, Is.GreaterThan(grassland.EffectivePondThreshold));

            // 雪原は初期仕様で通常の淡水を保つため、気温だけを下げても草原と同じ池下限・川幅になります。
            Assert.That(snowfield.EffectivePondThreshold, Is.EqualTo(grassland.EffectivePondThreshold));
            Assert.That(snowfield.EffectiveRiverHalfWidth, Is.EqualTo(grassland.EffectiveRiverHalfWidth));

            // 湿地では川を広げ、火山では細くしつつ、連続する川をバイオーム境界で完全には切らない最小幅を残します。
            Assert.That(wetland.EffectiveRiverHalfWidth, Is.GreaterThan(settings.RiverCenterLineHalfWidth));
            Assert.That(volcanic.EffectiveRiverHalfWidth, Is.LessThan(settings.RiverCenterLineHalfWidth));
            Assert.That(volcanic.EffectiveRiverHalfWidth, Is.GreaterThan(0));

            // 火山中心部では補正後下限を無効値65535へ飽和させ、池ノイズが最大でも池を作らない規則を表します。
            Assert.That(volcanic.VolcanicInfluence, Is.EqualTo(ushort.MaxValue));
            Assert.That(volcanic.EffectivePondThreshold, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 7種類を代表する気候値を純粋な分類関数へ渡し、各バイオームが意図した標高・気温・湿度・Weirdness条件で選ばれることを保証します。
        /// 広域ワールド探索を行わないため、日常的な修正後にも短時間で実行できる回帰テストです。
        /// </summary>
        [Test]
        public void BiomeClassifier_SelectsAllSevenBiomesFromRepresentativeClimateProfiles()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("seven-biome-classification-test"),
                1);

            // 中標高・温暖・中湿度は、極端な気候条件を持たない基準の草原になります。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(35000, 32000, 34000, 30000, settings),
                Is.EqualTo(BiomeId.Grassland));

            // 温暖かつ湿潤でも、湿地閾値には届かない地域は森林になります。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(35000, 32000, 45000, 30000, settings),
                Is.EqualTo(BiomeId.Forest));

            // 高温かつ乾燥した低〜中標高域は砂漠になります。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(35000, 50000, 15000, 30000, settings),
                Is.EqualTo(BiomeId.Desert));

            // 高標高かつ乾燥した地域は、通常地面を持つ岩地になります。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(52000, 32000, 25000, 30000, settings),
                Is.EqualTo(BiomeId.Rockland));

            // 低標高かつ非常に湿潤な地域は森林より湿地を優先します。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(28000, 32000, 60000, 30000, settings),
                Is.EqualTo(BiomeId.Wetland));

            // 気温が十分低い地域は、標高が高地閾値未満でも雪原になります。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(40000, 10000, 34000, 30000, settings),
                Is.EqualTo(BiomeId.Snowfield));

            // 高温・高標高・高Weirdnessが重なる地域は希少な火山になります。
            Assert.That(
                DeterministicChunkGenerator.ClassifyBiome(52000, 50000, 25000, 60000, settings),
                Is.EqualTo(BiomeId.Volcanic));
        }

        /// <summary>
        /// 1つの固定シードについて広い範囲を32タイル間隔で軽量サンプリングし、実際のノイズ生成経路から7バイオームすべてへ到達できることを保証します。
        /// 全タイルや多数シードを総当たりせず4,225地点だけを調べるため、通常の修正サイクルでも短時間で実行できます。
        /// </summary>
        [Test]
        public void GeneratedWorld_ContainsAllSevenBiomesInLightweightSample()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("seven-biome-world-sample"),
                1);
            HashSet<BiomeId> discoveredBiomes = new HashSet<BiomeId>();

            // 生成範囲の全セルではなく一定間隔だけを読むことで、分布の到達性を確認しつつテスト時間を抑えます。
            for (int tileY = -1024; tileY <= 1024; tileY += 32)
            {
                for (int tileX = -1024; tileX <= 1024; tileX += 32)
                {
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        tileX,
                        tileY);
                    discoveredBiomes.Add(tile.Biome);
                }
            }

            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Grassland));
            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Forest));
            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Desert));
            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Rockland));
            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Wetland));
            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Snowfield));
            Assert.That(discoveredBiomes, Does.Contain(BiomeId.Volcanic));
        }

        /// <summary>
        /// 固定シードの16,641地点だけを等間隔で調べ、淡水生成可能な森林・湿地に池が視認できる割合で存在することを検証します。
        /// 全タイル走査や多数シードを避けながら、気候補正量が池ノイズの実分布に対して弱すぎる退行を検出します。
        /// </summary>
        [Test]
        public void PondDistribution_ForestAndWetlandRemainVisiblyPopulated()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("water-biome-distribution-test"),
                1);
            const int scanRadiusInTiles = 2048;
            const int sampleIntervalInTiles = 32;
            int forestEligibleTileCount = 0;
            int forestPondTileCount = 0;
            int wetlandEligibleTileCount = 0;
            int wetlandPondTileCount = 0;

            // 池ノイズ格子48タイルに対して32タイル間隔で読み、同じ池を過剰に数えず広い気候分布を短時間で比較します。
            for (int tileY = -scanRadiusInTiles; tileY <= scanRadiusInTiles; tileY += sampleIntervalInTiles)
            {
                for (int tileX = -scanRadiusInTiles; tileX <= scanRadiusInTiles; tileX += sampleIntervalInTiles)
                {
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        tileX,
                        tileY);
                    bool canGenerateFreshWater = tile.Elevation >= settings.SeaLevelThreshold
                        && tile.Elevation <= settings.FreshWaterMaximumElevation;
                    if (!canGenerateFreshWater)
                    {
                        continue;
                    }

                    if (tile.Biome == BiomeId.Forest)
                    {
                        forestEligibleTileCount++;
                        if (tile.WaterBodyType == WaterBodyType.Pond)
                        {
                            forestPondTileCount++;
                        }
                    }
                    else if (tile.Biome == BiomeId.Wetland)
                    {
                        wetlandEligibleTileCount++;
                        if (tile.WaterBodyType == WaterBodyType.Pond)
                        {
                            wetlandPondTileCount++;
                        }
                    }
                }
            }

            float forestPondRatio = forestPondTileCount / (float)forestEligibleTileCount;
            float wetlandPondRatio = wetlandPondTileCount / (float)wetlandEligibleTileCount;
            TestContext.WriteLine(
                $"森林の池率={forestPondRatio:P2} ({forestPondTileCount}/{forestEligibleTileCount}), "
                + $"湿地の池率={wetlandPondRatio:P2} ({wetlandPondTileCount}/{wetlandEligibleTileCount})");

            // 森林は池が明確に見つかる5%以上、湿地は仕様の初期目標10〜25%を少し余裕を持つ30%以下で保証します。
            Assert.That(forestEligibleTileCount, Is.GreaterThanOrEqualTo(100));
            Assert.That(wetlandEligibleTileCount, Is.GreaterThanOrEqualTo(80));
            Assert.That(forestPondRatio, Is.InRange(0.05f, 0.15f));
            Assert.That(wetlandPondRatio, Is.InRange(0.10f, 0.30f));
            Assert.That(wetlandPondRatio, Is.GreaterThan(forestPondRatio));
        }

        /// <summary>
        /// 資源既定値を作り、木・石・砂が種類別の最低間隔と群半径を持ち、半径が間隔の半分を超えないことを保証します。
        /// </summary>
        [Test]
        public void DefaultResourceSettings_UsePerTypeSpacingAndClusterRadius()
        {
            ResourceGenerationSettings settings = ResourceGenerationSettings.CreateDefault();

            Assert.That(settings.TreeMinimumSpacingInTiles, Is.EqualTo(14));
            Assert.That(settings.StoneMinimumSpacingInTiles, Is.EqualTo(20));
            Assert.That(settings.SandMinimumSpacingInTiles, Is.EqualTo(18));
            Assert.That(settings.TreeClusterRadiusInTiles, Is.EqualTo(5));
            Assert.That(settings.StoneClusterRadiusInTiles, Is.EqualTo(4));
            Assert.That(settings.SandClusterRadiusInTiles, Is.EqualTo(6));
            Assert.That(
                settings.TreeClusterRadiusInTiles * 2,
                Is.LessThan(settings.TreeMinimumSpacingInTiles));
            Assert.That(
                settings.StoneClusterRadiusInTiles * 2,
                Is.LessThan(settings.StoneMinimumSpacingInTiles));
            Assert.That(
                settings.SandClusterRadiusInTiles * 2,
                Is.LessThan(settings.SandMinimumSpacingInTiles));
        }

        /// <summary>
        /// 4種類の基礎地形を直接分類し、通常設備・通常ベルト・橋・耐熱要件が仕様の組み合わせになることを保証します。
        /// 生成ノイズを介さず地形契約そのものを検証するため、後続の建築処理が地形種別を再解釈する必要はありません。
        /// </summary>
        [Test]
        public void GameplayFlags_ExposeExplicitPlacementAttributesForEveryGroundType()
        {
            TileGameplayFlags normalGround =
                TileGameplayFlags.AllowsNormalEquipment
                | TileGameplayFlags.AllowsNormalBelt;
            TileGameplayFlags normalBridge = TileGameplayFlags.AllowsBridge;
            TileGameplayFlags heatResistantBridge =
                TileGameplayFlags.AllowsBridge
                | TileGameplayFlags.RequiresHeatResistance;

            Assert.That(
                DeterministicChunkGenerator.ClassifyGameplayFlags(GroundType.Land),
                Is.EqualTo(normalGround),
                "通常地面では通常設備と通常ベルトだけを許可する必要があります。");
            Assert.That(
                DeterministicChunkGenerator.ClassifyGameplayFlags(GroundType.Water),
                Is.EqualTo(normalBridge),
                "水域では耐熱性の有無にかかわらず橋を許可する必要があります。");
            Assert.That(
                DeterministicChunkGenerator.ClassifyGameplayFlags(GroundType.Magma),
                Is.EqualTo(heatResistantBridge),
                "マグマでは耐熱性を持つ橋だけを許可できる属性が必要です。");
            Assert.That(
                DeterministicChunkGenerator.ClassifyGameplayFlags(GroundType.RockWall),
                Is.EqualTo(TileGameplayFlags.None),
                "岩壁では通常設備、通常ベルト、橋のすべてを拒否する必要があります。");
            Assert.That(
                DeterministicChunkGenerator.ClassifyGameplayFlags((GroundType)byte.MaxValue),
                Is.EqualTo(TileGameplayFlags.None),
                "未知の地形IDは配置可能として扱わず、安全側へ倒す必要があります。");

            GeneratedTile equipmentTile = new GeneratedTile
            {
                GameplayFlags = TileGameplayFlags.AllowsNormalEquipment
            };
            GeneratedTile bridgeOnlyTile = new GeneratedTile
            {
                GameplayFlags = TileGameplayFlags.AllowsBridge
            };

            // 設備探索APIが橋用属性を誤って許可しないよう、通常設備専用の正方向属性だけを参照することを固定します。
            Assert.That(EquipmentBuildableRegionLocator.CanPlaceEquipment(equipmentTile), Is.True);
            Assert.That(EquipmentBuildableRegionLocator.CanPlaceEquipment(bridgeOnlyTile), Is.False);
        }

        /// <summary>
        /// 固定シードの広域を粗く走査し、火山のおよそ半分がマグマになり、岩地では稀にだけ生成されることを確認します。
        /// 各候補では水属性・配置属性・資源適性も調べ、海以外の水域、岩壁、通常資源と同じセルを共有しないことを保証します。
        /// </summary>
        [Test]
        public void MagmaGeneration_CreatesVolcanicBasinsAndSuppressesFreshWater()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("magma-generation-test"),
                1);
            int volcanicLandTileCount = 0;
            int volcanicMagmaTileCount = 0;
            int rocklandLandTileCount = 0;
            int rocklandMagmaTileCount = 0;
            int forbiddenBiomeMagmaTileCount = 0;
            int overlappingRockWallCandidateCount = 0;

            // 257×257地点を32タイル間隔で調べ、毎回の修正時にも現実的な時間で広域傾向を確認します。
            for (int worldY = -4096; worldY <= 4096; worldY += 32)
            {
                for (int worldX = -4096; worldX <= 4096; worldX += 32)
                {
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        worldX,
                        worldY);
                    MagmaGenerationDebugInfo magmaInfo =
                        DeterministicChunkGenerator.GetMagmaGenerationDebugInfo(
                            tile.Elevation,
                            tile.Temperature,
                            tile.Weirdness,
                            tile.Biome,
                            settings,
                            worldX,
                            worldY);

                    // 海は最優先なので例外ですが、陸上の火山影響域では川と池が生成されてはいけません。
                    if (magmaInfo.IsInfluenceArea && tile.WaterBodyType != WaterBodyType.Sea)
                    {
                        Assert.That(tile.WaterBodyType, Is.EqualTo(WaterBodyType.None));
                    }

                    if (tile.Biome == BiomeId.Volcanic
                        && tile.WaterBodyType != WaterBodyType.Sea)
                    {
                        volcanicLandTileCount++;
                    }
                    else if (tile.Biome == BiomeId.Rockland
                        && tile.WaterBodyType != WaterBodyType.Sea)
                    {
                        rocklandLandTileCount++;
                    }

                    if (tile.Ground != GroundType.Magma)
                    {
                        continue;
                    }

                    Assert.That(tile.WaterSalinity, Is.EqualTo(WaterSalinity.None));
                    Assert.That(tile.WaterBodyType, Is.EqualTo(WaterBodyType.None));
                    Assert.That(tile.ResourceSuitability, Is.EqualTo(ResourceSuitability.None));
                    Assert.That(tile.MovementCost, Is.EqualTo(0));
                    Assert.That(
                        tile.GameplayFlags,
                        Is.EqualTo(
                            TileGameplayFlags.AllowsBridge
                            | TileGameplayFlags.RequiresHeatResistance));
                    if (overlappingRockWallCandidateCount == 0
                        && DeterministicRockWallGenerator.IsRockWallCandidate(
                            settings,
                            worldX,
                            worldY))
                    {
                        // 重複候補を1件だけ最終判定まで再確認し、全マグマで重い岩壁ノイズを二重生成しないようにします。
                        overlappingRockWallCandidateCount++;
                        Assert.That(
                            DeterministicChunkGenerator.IsRockWall(settings, worldX, worldY),
                            Is.False,
                            "マグマと同じタイルへ最終岩壁判定を重ねてはいけません。");
                    }

                    if (tile.Biome == BiomeId.Volcanic)
                    {
                        Assert.That(
                            magmaInfo.UsesRocklandProfile,
                            Is.False,
                            "火山マグマは広域の火山用プロファイルで生成する必要があります。");
                        volcanicMagmaTileCount++;
                    }
                    else if (tile.Biome == BiomeId.Rockland)
                    {
                        Assert.That(
                            magmaInfo.UsesRocklandProfile,
                            Is.True,
                            "岩地マグマは小規模・低頻度の岩地用プロファイルで生成する必要があります。");
                        rocklandMagmaTileCount++;
                    }
                    else
                    {
                        forbiddenBiomeMagmaTileCount++;
                    }
                }
            }

            float volcanicMagmaRatio = volcanicMagmaTileCount / (float)volcanicLandTileCount;
            float rocklandMagmaRatio = rocklandMagmaTileCount / (float)rocklandLandTileCount;
            Assert.That(volcanicLandTileCount, Is.GreaterThanOrEqualTo(100));
            Assert.That(volcanicMagmaRatio, Is.InRange(0.40f, 0.60f));
            Assert.That(rocklandLandTileCount, Is.GreaterThanOrEqualTo(100));
            Assert.That(rocklandMagmaRatio, Is.InRange(0.001f, 0.03f));
            Assert.That(rocklandMagmaTileCount, Is.GreaterThan(0));
            Assert.That(forbiddenBiomeMagmaTileCount, Is.EqualTo(0));
            Assert.That(overlappingRockWallCandidateCount, Is.GreaterThan(0));
        }

        /// <summary>
        /// 実際に全域がマグマに見えた固定座標の周辺を調べ、1つの局所的な火山領域にもマグマと陸地の両方が残ることを確認します。
        /// 広域の平均比率だけでは小さな火山が同じ低周波ノイズ値へ覆われる不具合を検出できないため、報告座標を回帰条件として固定します。
        /// </summary>
        [Test]
        public void MagmaGeneration_KeepsLandInReportedVolcanicRegion()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("Farmware"),
                1);
            const int reportedTileX = -3698;
            const int reportedTileY = 2422;
            const int sampleRadiusInTiles = 64;
            int volcanicTileCount = 0;
            int volcanicMagmaTileCount = 0;

            // 報告地点を中心とする129×129タイルだけを走査し、修正ごとに実行しても短時間で局所分布を検証できるようにします。
            for (int worldY = reportedTileY - sampleRadiusInTiles;
                 worldY <= reportedTileY + sampleRadiusInTiles;
                 worldY++)
            {
                for (int worldX = reportedTileX - sampleRadiusInTiles;
                     worldX <= reportedTileX + sampleRadiusInTiles;
                     worldX++)
                {
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        worldX,
                        worldY);
                    if (tile.Biome != BiomeId.Volcanic
                        || tile.WaterBodyType == WaterBodyType.Sea)
                    {
                        continue;
                    }

                    volcanicTileCount++;
                    if (tile.Ground == GroundType.Magma)
                    {
                        volcanicMagmaTileCount++;
                    }
                }
            }

            Assert.That(
                volcanicTileCount,
                Is.GreaterThanOrEqualTo(500),
                "報告地点周辺に十分な火山タイルが存在し、局所比率を評価できる必要があります。");
            float localMagmaRatio = volcanicMagmaTileCount / (float)volcanicTileCount;
            Assert.That(
                localMagmaRatio,
                Is.InRange(0.20f, 0.80f),
                "局所的な火山領域にも、マグマと建築可能な火山性陸地の両方を残す必要があります。");
        }

        /// <summary>
        /// 火山マグマの合成密度を固定範囲で比較し、細部ノイズ、座標ワープ、境界平滑化が実際の形状へ反映されることを確認します。
        /// 単一の低周波ノイズへ退行すると丸く単純な塊へ戻るため、各段階を個別に無効化した密度との差を回帰条件にします。
        /// </summary>
        [Test]
        public void MagmaGeneration_IrregularShapeUsesDetailWarpAndBoundarySmoothing()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("Farmware"),
                1);
            WorldGenerationSettings noWarpSettings = settings;
            noWarpSettings.MagmaWarpAmplitudeInTiles = 0;
            WorldGenerationSettings coarseDetailSettings = settings;
            coarseDetailSettings.MagmaDetailCellSizeInTiles = 16;
            const int centerTileX = -3698;
            const int centerTileY = 2422;
            const int sampleRadiusInTiles = 64;
            int sampleCount = 0;
            int warpDifferenceCount = 0;
            int detailDifferenceCount = 0;
            int smoothingDifferenceCount = 0;

            // 2タイル間隔の4,225地点だけを調べ、輪郭生成の各段階を確認しながら毎回実行できる負荷へ抑えます。
            for (int worldY = centerTileY - sampleRadiusInTiles;
                 worldY <= centerTileY + sampleRadiusInTiles;
                 worldY += 2)
            {
                for (int worldX = centerTileX - sampleRadiusInTiles;
                     worldX <= centerTileX + sampleRadiusInTiles;
                     worldX += 2)
                {
                    ushort density = DeterministicMagmaGenerator.SampleDensity(
                        settings,
                        worldX,
                        worldY);
                    ushort noWarpDensity = DeterministicMagmaGenerator.SampleDensity(
                        noWarpSettings,
                        worldX,
                        worldY);
                    ushort coarseDetailDensity = DeterministicMagmaGenerator.SampleDensity(
                        coarseDetailSettings,
                        worldX,
                        worldY);
                    bool rawCandidate = density >= settings.MagmaNoiseThreshold;
                    bool smoothedCandidate = DeterministicMagmaGenerator.IsSmoothedCandidate(
                        settings,
                        worldX,
                        worldY,
                        settings.MagmaNoiseThreshold,
                        density);

                    sampleCount++;
                    warpDifferenceCount += density != noWarpDensity ? 1 : 0;
                    detailDifferenceCount += density != coarseDetailDensity ? 1 : 0;
                    smoothingDifferenceCount += rawCandidate != smoothedCandidate ? 1 : 0;
                }
            }

            Assert.That(
                warpDifferenceCount,
                Is.GreaterThan(sampleCount / 2),
                "座標ワープは十分な範囲の密度を変化させ、規則的な輪郭を崩す必要があります。");
            Assert.That(
                detailDifferenceCount,
                Is.GreaterThan(sampleCount * 3 / 4),
                "細部ノイズは大半の地点へ影響し、入り江や半島を作る必要があります。");
            Assert.That(
                smoothingDifferenceCount,
                Is.GreaterThan(0),
                "境界多数決は少なくとも一部の孤立タイルまたは1タイル穴を補正する必要があります。");
        }

        /// <summary>
        /// 同じシードを移行帯あり・なしで生成し、論理Biome、基礎地形、気候値を維持したまま境界付近の土壌だけが混ざることを保証します。
        /// </summary>
        [Test]
        public void BiomeTransition_ChangesSoilWithoutChangingLogicalBiomeOrGround()
        {
            WorldGenerationSettings transitionSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("biome-transition-test"),
                2);
            WorldGenerationSettings discreteSettings = transitionSettings;
            discreteSettings.BiomeTransitionWidthInTiles = 0;
            int changedSoilTileCount = 0;
            HashSet<SoilType> discoveredLandSoils = new HashSet<SoilType>();

            // 正負座標と複数チャンク境界を含む範囲を調べ、移行判定が特定チャンク内だけに閉じていないことも確認します。
            for (int tileY = -96; tileY < 96; tileY++)
            {
                for (int tileX = -96; tileX < 96; tileX++)
                {
                    GeneratedTile transitioned = DeterministicChunkGenerator.GenerateTile(
                        transitionSettings,
                        tileX,
                        tileY);
                    GeneratedTile discrete = DeterministicChunkGenerator.GenerateTile(
                        discreteSettings,
                        tileX,
                        tileY);

                    Assert.That(transitioned.Biome, Is.EqualTo(discrete.Biome));
                    Assert.That(transitioned.Ground, Is.EqualTo(discrete.Ground));
                    Assert.That(transitioned.Elevation, Is.EqualTo(discrete.Elevation));
                    Assert.That(transitioned.Temperature, Is.EqualTo(discrete.Temperature));
                    Assert.That(transitioned.Moisture, Is.EqualTo(discrete.Moisture));
                    Assert.That(transitioned.Weirdness, Is.EqualTo(discrete.Weirdness));

                    if (transitioned.Ground == GroundType.Land)
                    {
                        // 陸地には必ず資源候補と表示差分に使える土壌があり、物理地形とは独立して保持される必要があります。
                        Assert.That(transitioned.Soil, Is.Not.EqualTo(SoilType.None));
                        Assert.That(
                            transitioned.GameplayFlags & TileGameplayFlags.AllowsNormalEquipment,
                            Is.EqualTo(TileGameplayFlags.AllowsNormalEquipment),
                            "土壌の種類だけで通常設備の建築可否を変えてはいけません。");
                        Assert.That(transitioned.MovementCost, Is.EqualTo(1));
                        discoveredLandSoils.Add(transitioned.Soil);
                    }
                    else
                    {
                        Assert.That(
                            transitioned.Soil,
                            Is.EqualTo(SoilType.None),
                            "水域・岩壁・マグマへ陸地用の土壌を設定してはいけません。");
                    }

                    if (transitioned.Soil != discrete.Soil)
                    {
                        // 水域・マグマ・岩壁は土壌を持たず、移行帯が物理的な地形ルールを変えてはならないため陸地だけに限定します。
                        Assert.That(discrete.Ground, Is.EqualTo(GroundType.Land));
                        changedSoilTileCount++;
                    }
                }
            }

            Assert.That(
                changedSoilTileCount,
                Is.GreaterThan(0),
                "試験範囲にバイオーム移行土壌が1件も生成されませんでした。");
            Assert.That(discoveredLandSoils, Does.Contain(SoilType.Normal));
            Assert.That(discoveredLandSoils, Does.Contain(SoilType.Fertile));
            Assert.That(discoveredLandSoils, Does.Contain(SoilType.Sandy));
        }

        /// <summary>
        /// 通常生成された地形だけを探索し、発見領域の全タイルが中央コアを含む共通設備建築条件を満たすことを保証します。
        /// </summary>
        [Test]
        public void EquipmentBuildableRegionLocator_FindsOnlyNormallyBuildableTiles()
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("equipment-buildable-region-test"),
                2);
            EquipmentBuildableRegionSearchSettings searchSettings =
                EquipmentBuildableRegionSearchSettings.CreateDefault();

            bool found = EquipmentBuildableRegionLocator.TryFindNearest(
                generationSettings,
                searchSettings,
                out EquipmentBuildableRegion region);

            Assert.That(found, Is.True);
            Assert.That(region.WidthInTiles, Is.EqualTo(searchSettings.RegionWidthInTiles));
            Assert.That(region.HeightInTiles, Is.EqualTo(searchSettings.RegionHeightInTiles));

            // 領域の一部だけが建築可能でも合格しないよう、12×8の全タイルを通常生成器から再確認します。
            for (int localY = 0; localY < region.HeightInTiles; localY++)
            {
                for (int localX = 0; localX < region.WidthInTiles; localX++)
                {
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        region.MinimumTileX + localX,
                        region.MinimumTileY + localY);
                    Assert.That(
                        EquipmentBuildableRegionLocator.CanPlaceEquipment(tile),
                        Is.True,
                        $"建築不可タイル: ({region.MinimumTileX + localX}, {region.MinimumTileY + localY})");
                }
            }
        }

        /// <summary>
        /// 初期位置用の探索設定を準備し、3×3中央コアを収められない領域寸法を探索開始前に拒否することを保証します。
        /// 汎用探索の1×1以上という契約と、初期位置だけに必要なコア寸法の契約を混同しないためのテストです。
        /// </summary>
        /// <param name="regionWidthInTiles">検証する探索領域の横幅です。単位はタイルです。</param>
        /// <param name="regionHeightInTiles">検証する探索領域の縦幅です。単位はタイルです。</param>
        [TestCase(2, 8)]
        [TestCase(12, 2)]
        public void EquipmentBuildableRegionLocator_InitialLocationRejectsRegionSmallerThanCore(
            int regionWidthInTiles,
            int regionHeightInTiles)
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("initial-core-footprint-validation-test"),
                2);
            EquipmentBuildableRegionSearchSettings searchSettings =
                EquipmentBuildableRegionSearchSettings.CreateInitialLocationDefault();
            searchSettings.RegionWidthInTiles = regionWidthInTiles;
            searchSettings.RegionHeightInTiles = regionHeightInTiles;

            bool found = EquipmentBuildableRegionLocator.TryFindNearest(
                generationSettings,
                searchSettings,
                out _);

            Assert.That(searchSettings.RequireCenteredCoreFootprint, Is.True);
            Assert.That(
                found,
                Is.False,
                "初期位置探索は、中央コアの3×3占有を収められない設定を受け付けてはいけません。");
        }

        /// <summary>
        /// 同じ設定で探索を繰り返し、結果座標が一致し、探索前後で通常地形のチャンクハッシュが変わらないことを保証します。
        /// </summary>
        [Test]
        public void EquipmentBuildableRegionLocator_IsDeterministicAndDoesNotMutateTerrain()
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("equipment-region-determinism-test"),
                2);
            EquipmentBuildableRegionSearchSettings searchSettings =
                EquipmentBuildableRegionSearchSettings.CreateDefault(-17, 93);
            ChunkCoordinate observedChunk = new ChunkCoordinate(-1, 1);
            ulong beforeSearch = DeterministicChunkGenerator.ComputeChunkHash(
                generationSettings,
                observedChunk);

            bool foundFirst = EquipmentBuildableRegionLocator.TryFindNearest(
                generationSettings,
                searchSettings,
                out EquipmentBuildableRegion first);
            bool foundSecond = EquipmentBuildableRegionLocator.TryFindNearest(
                generationSettings,
                searchSettings,
                out EquipmentBuildableRegion second);
            ulong afterSearch = DeterministicChunkGenerator.ComputeChunkHash(
                generationSettings,
                observedChunk);

            Assert.That(foundFirst, Is.True);
            Assert.That(foundSecond, Is.True);
            Assert.That(second.MinimumTileX, Is.EqualTo(first.MinimumTileX));
            Assert.That(second.MinimumTileY, Is.EqualTo(first.MinimumTileY));
            Assert.That(second.WidthInTiles, Is.EqualTo(first.WidthInTiles));
            Assert.That(second.HeightInTiles, Is.EqualTo(first.HeightInTiles));
            Assert.That(afterSearch, Is.EqualTo(beforeSearch));
        }

        /// <summary>
        /// 1,000個の異なるワールドシードを準備し、地形の上書きなしで5×5チャンク以内に既定領域が見つかることを検証します。
        /// </summary>
        [Test]
        public void EquipmentBuildableRegionLocator_OneThousandSeedsFindDefaultRegion()
        {
            EquipmentBuildableRegionSearchSettings searchSettings =
                EquipmentBuildableRegionSearchSettings.CreateDefault();

            // 文字列生成を乱数代わりにせず、連番を安定64bit混合して広いシード分布を再現可能に調べます。
            for (int seedIndex = 0; seedIndex < 1000; seedIndex++)
            {
                ulong worldSeed = WorldSeedUtility.Mix64((ulong)seedIndex);
                WorldGenerationSettings generationSettings =
                    WorldGenerationSettings.CreateDefault(worldSeed, 2);
                bool found = EquipmentBuildableRegionLocator.TryFindNearest(
                    generationSettings,
                    searchSettings,
                    out EquipmentBuildableRegion region);

                Assert.That(
                    found,
                    Is.True,
                    $"5×5チャンク内に12×8建築可能領域がないシードです。seedIndex={seedIndex}, worldSeed={worldSeed}");
                Assert.That(region.WidthInTiles, Is.EqualTo(12));
                Assert.That(region.HeightInTiles, Is.EqualTo(8));
            }
        }

        /// <summary>
        /// 偶数寸法の建設可能矩形を準備し、初期スポーンが3×3中央コアを収められる中央寄りタイルへ決まり、元矩形も記録されることを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnLocation_FromRegionRecordsCenterTileAndSourceRegion()
        {
            EquipmentBuildableRegion region = new EquipmentBuildableRegion(-12, 7, 12, 8);

            InitialSpawnLocation location = InitialSpawnLocation.FromRegion(region);

            Assert.That(location.TileX, Is.EqualTo(-6));
            Assert.That(location.TileY, Is.EqualTo(11));
            Assert.That(location.RegionMinimumTileX, Is.EqualTo(region.MinimumTileX));
            Assert.That(location.RegionMinimumTileY, Is.EqualTo(region.MinimumTileY));
            Assert.That(location.RegionWidthInTiles, Is.EqualTo(region.WidthInTiles));
            Assert.That(location.RegionHeightInTiles, Is.EqualTo(region.HeightInTiles));
            int footprintRadius = InitialSpawnLocation.CentralCoreSizeInTiles / 2;
            Assert.That(
                location.TileX - footprintRadius,
                Is.GreaterThanOrEqualTo(region.MinimumTileX),
                "中央コア左端は選択元領域の内側に必要です。");
            Assert.That(
                location.TileX + footprintRadius,
                Is.LessThan(region.MinimumTileX + region.WidthInTiles),
                "中央コア右端は選択元領域の内側に必要です。");
            Assert.That(
                location.TileY - footprintRadius,
                Is.GreaterThanOrEqualTo(region.MinimumTileY),
                "中央コア下端は選択元領域の内側に必要です。");
            Assert.That(
                location.TileY + footprintRadius,
                Is.LessThan(region.MinimumTileY + region.HeightInTiles),
                "中央コア上端は選択元領域の内側に必要です。");
        }

        /// <summary>
        /// 広い正負座標範囲を2回生成し、水域分類と資源配置が決定論的で、資源が地形適性から外れないことを保証します。
        /// 川・池・海、河口の淡水海水接続、木・石・砂を同じ走査で確認し、日常テストの重複生成を避けます。
        /// </summary>
        [Test]
        public void ResourceGeneration_IsDeterministicAndRespectsTerrainSuitability()
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("resource-generation-test"),
                2);
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            NaturalResourceTypeMask foundTypes = NaturalResourceTypeMask.None;
            const int scanRadiusInTiles = 192;
            const int scanDiameterInTiles = scanRadiusInTiles * 2;
            WaterSalinity[] previousRowSalinities = new WaterSalinity[scanDiameterInTiles];
            WaterSalinity[] currentRowSalinities = new WaterSalinity[scanDiameterInTiles];
            int riverTileCount = 0;
            int pondTileCount = 0;
            int seaTileCount = 0;
            int freshAndSaltBoundaryCount = 0;

            // 6×6チャンク相当だけを走査し、日常実行時間を抑えながら複数バイオーム、水域、正負境界を確認します。
            for (int worldY = -scanRadiusInTiles; worldY < scanRadiusInTiles; worldY++)
            {
                for (int worldX = -scanRadiusInTiles; worldX < scanRadiusInTiles; worldX++)
                {
                    GeneratedTile terrainTile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        worldX,
                        worldY);
                    GeneratedResourceTile first = DeterministicResourceGenerator.GenerateResource(
                        generationSettings,
                        resourceSettings,
                        terrainTile,
                        worldX,
                        worldY);
                    GeneratedResourceTile second = DeterministicResourceGenerator.GenerateResource(
                        generationSettings,
                        resourceSettings,
                        terrainTile,
                        worldX,
                        worldY);

                    // 数十万回のNUnit Assert呼び出しは検証本体より重いため、不一致時だけ詳細メッセージ付きで失敗させます。
                    if (first.Type != second.Type || first.VisualVariant != second.VisualVariant)
                    {
                        Assert.Fail(
                            $"同じ入力の資源結果が一致しません。座標=({worldX}, {worldY}), "
                            + $"first=({first.Type}, {first.VisualVariant}), "
                            + $"second=({second.Type}, {second.VisualVariant})");
                    }

                    bool isWaterGround = terrainTile.Ground == GroundType.Water;
                    bool hasWaterAttributes = terrainTile.WaterSalinity != WaterSalinity.None
                        && terrainTile.WaterBodyType != WaterBodyType.None;
                    if (isWaterGround != hasWaterAttributes)
                    {
                        Assert.Fail(
                            $"水域地形と水属性が一致しません。座標=({worldX}, {worldY}), "
                            + $"Ground={terrainTile.Ground}, Salinity={terrainTile.WaterSalinity}, "
                            + $"Body={terrainTile.WaterBodyType}");
                    }

                    // 水域であることだけでなく、生成由来と塩分の対応も固定し、海を淡水として扱う退行を防ぎます。
                    bool hasCorrectSalinity = terrainTile.WaterBodyType switch
                    {
                        WaterBodyType.River => terrainTile.WaterSalinity == WaterSalinity.FreshWater,
                        WaterBodyType.Pond => terrainTile.WaterSalinity == WaterSalinity.FreshWater,
                        WaterBodyType.Sea => terrainTile.WaterSalinity == WaterSalinity.SaltWater,
                        _ => terrainTile.WaterSalinity == WaterSalinity.None
                    };
                    if (!hasCorrectSalinity)
                    {
                        Assert.Fail(
                            $"水域の生成由来と塩分が一致しません。座標=({worldX}, {worldY}), "
                            + $"Salinity={terrainTile.WaterSalinity}, Body={terrainTile.WaterBodyType}");
                    }

                    if (terrainTile.ResourceSuitability != ResourceSuitability.None && isWaterGround)
                    {
                        Assert.Fail($"水域へ別配置資源の適性が残っています。座標=({worldX}, {worldY})");
                    }

                    if (terrainTile.WaterBodyType == WaterBodyType.River)
                    {
                        riverTileCount++;
                    }
                    else if (terrainTile.WaterBodyType == WaterBodyType.Pond)
                    {
                        pondTileCount++;
                    }
                    else if (terrainTile.WaterBodyType == WaterBodyType.Sea)
                    {
                        seaTileCount++;
                    }

                    int rowIndex = worldX + scanRadiusInTiles;
                    currentRowSalinities[rowIndex] = terrainTile.WaterSalinity;
                    if (rowIndex > 0
                        && IsFreshSaltBoundary(
                            terrainTile.WaterSalinity,
                            currentRowSalinities[rowIndex - 1]))
                    {
                        freshAndSaltBoundaryCount++;
                    }

                    if (worldY > -scanRadiusInTiles
                        && IsFreshSaltBoundary(
                            terrainTile.WaterSalinity,
                            previousRowSalinities[rowIndex]))
                    {
                        freshAndSaltBoundaryCount++;
                    }

                    if (first.Type == GeneratedResourceType.None)
                    {
                        continue;
                    }

                    foundTypes |= DeterministicResourceGenerator.ToMask(first.Type);
                    ResourceSuitability requiredSuitability = ToSuitability(first.Type);
                    if ((terrainTile.ResourceSuitability & requiredSuitability) == 0)
                    {
                        Assert.Fail(
                            $"地形適性外へ資源が配置されました。座標=({worldX}, {worldY}), 資源={first.Type}");
                    }
                }

                // 次の行では直前行との上下接続を調べるため、配列参照だけを交換して再利用します。
                WaterSalinity[] swap = previousRowSalinities;
                previousRowSalinities = currentRowSalinities;
                currentRowSalinities = swap;
            }

            NaturalResourceTypeMask allTypes = NaturalResourceTypeMask.Tree
                | NaturalResourceTypeMask.Stone
                | NaturalResourceTypeMask.Sand;
            Assert.That(foundTypes, Is.EqualTo(allTypes), "試験範囲内に木・石・砂がすべて必要です。");
            Assert.That(riverTileCount, Is.GreaterThan(0), "試験範囲内に淡水の川が必要です。");
            Assert.That(pondTileCount, Is.GreaterThan(0), "試験範囲内に淡水の池が必要です。");
            Assert.That(seaTileCount, Is.GreaterThan(0), "試験範囲内に海水の海が必要です。");
            Assert.That(
                freshAndSaltBoundaryCount,
                Is.GreaterThan(0),
                "川または池の淡水が海水へ隙間なく接する河口境界が必要です。");
        }

        /// <summary>
        /// 地形生成を省いた全適性タイルへ1種類ずつ資源を生成し、負座標をまたいでも群中心同士が設定距離より近づかないことを保証します。
        /// 日常テストを軽く保つため96×96だけを走査し、同じ群に属する複数タイルは中心座標で重複除外します。
        /// </summary>
        [Test]
        public void ResourceGeneration_ClusterAnchorsRespectPerTypeMinimumSpacing()
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("resource-spacing-test"),
                1);
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();

            // 閾値を0にすると候補セルの有無ではなく、アンカー配置そのものの最低間隔だけを確実に検証できます。
            resourceSettings.TreeThreshold = 0;
            resourceSettings.StoneThreshold = 0;
            resourceSettings.SandThreshold = 0;
            AssertResourceClusterSpacing(
                generationSettings,
                resourceSettings,
                GeneratedResourceType.Tree,
                ResourceSuitability.Tree,
                resourceSettings.TreeMinimumSpacingInTiles);
            AssertResourceClusterSpacing(
                generationSettings,
                resourceSettings,
                GeneratedResourceType.Stone,
                ResourceSuitability.Stone,
                resourceSettings.StoneMinimumSpacingInTiles);
            AssertResourceClusterSpacing(
                generationSettings,
                resourceSettings,
                GeneratedResourceType.Sand,
                ResourceSuitability.Sand,
                resourceSettings.SandMinimumSpacingInTiles);
        }

        /// <summary>
        /// 木・石・砂の群が重なりやすい小間隔設定を準備し、同じタイルに複数候補があっても最終Buffer相当の結果が1種類へ決まることを保証します。
        /// </summary>
        [Test]
        public void ResourceGeneration_OverlappingCandidatesResolveToOneStableType()
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("resource-conflict-test"),
                1);
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            resourceSettings.TreeThreshold = 0;
            resourceSettings.StoneThreshold = 0;
            resourceSettings.SandThreshold = 0;
            resourceSettings.TreeMinimumSpacingInTiles = 8;
            resourceSettings.StoneMinimumSpacingInTiles = 8;
            resourceSettings.SandMinimumSpacingInTiles = 8;
            resourceSettings.TreeClusterRadiusInTiles = 4;
            resourceSettings.StoneClusterRadiusInTiles = 4;
            resourceSettings.SandClusterRadiusInTiles = 4;
            GeneratedTile allSuitableTerrain = new GeneratedTile
            {
                ResourceSuitability = ResourceSuitability.Tree
                    | ResourceSuitability.Stone
                    | ResourceSuitability.Sand
            };
            int resolvedConflictCount = 0;

            // 64×64内の競合セルだけを検証し、広域地形生成を伴う重いテストにはしません。
            for (int worldY = -32; worldY < 32; worldY++)
            {
                for (int worldX = -32; worldX < 32; worldX++)
                {
                    ResourceGenerationDebugInfo debugInfo =
                        DeterministicResourceGenerator.GetDebugInfo(
                            generationSettings,
                            resourceSettings,
                            allSuitableTerrain,
                            worldX,
                            worldY);
                    if (debugInfo.EligibleCandidateCount < 2)
                    {
                        continue;
                    }

                    GeneratedResourceTile generated = DeterministicResourceGenerator.GenerateResource(
                        generationSettings,
                        resourceSettings,
                        allSuitableTerrain,
                        worldX,
                        worldY);
                    Assert.That(generated.Type, Is.EqualTo(debugInfo.SelectedType));
                    Assert.That(generated.Type, Is.Not.EqualTo(GeneratedResourceType.None));
                    resolvedConflictCount++;
                }
            }

            Assert.That(
                resolvedConflictCount,
                Is.GreaterThan(0),
                "試験範囲に複数資源群が競合するタイルが必要です。");
        }

        /// <summary>
        /// 分布の異なる代表シードを1つずつ準備し、通常生成された建築可能領域の周囲に木・石・水が自然に存在する候補を選べることを保証します。
        /// 日常的な一括テストを数秒で終えるため3ケースに限定し、広いシード範囲は明示実行の負荷テストへ分離します。
        /// </summary>
        /// <param name="seedIndex">決定論的なワールドシードへ変換する代表番号です。</param>
        [TestCase(1)]
        [TestCase(37)]
        [TestCase(99)]
        public void EquipmentBuildableRegionLocator_RepresentativeSeedFindsNaturalRequiredResources(
            int seedIndex)
        {
            AssertNaturalRequiredResourcesForSeed(seedIndex);
        }

        /// <summary>
        /// 100個のシードを連続検査する長時間負荷テストです。
        /// 通常のRun Allでは実行せず、生成パラメーターや探索範囲を変更した節目でTest Runnerから明示的に選択します。
        /// </summary>
        [Test]
        [Explicit("最大17×17チャンクの資源探索を100シード行うため、通常の変更確認から分離した負荷テストです。")]
        public void EquipmentBuildableRegionLocator_OneHundredSeedSoakTest()
        {
            // 失敗時に再現用seedIndexがAssertionへ残るよう、同じ1シード検証を順番に呼び出します。
            for (int seedIndex = 0; seedIndex < 100; seedIndex++)
            {
                AssertNaturalRequiredResourcesForSeed(seedIndex);
            }
        }

        /// <summary>
        /// テスト用Worldへ生成設定を準備し、非同期探索Jobの完了後に建設可能な初期スポーンSingletonが1件だけ作られることを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnLocationSystem_RecordsOneBuildableLocationAfterJobCompletion()
        {
            using World world = new World("InitialSpawnLocationSystemTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.Mix64(1UL),
                1);
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, generationSettings);
            entityManager.AddComponentData(settingsEntity, resourceSettings);

            SystemHandle system = world.GetOrCreateSystem<InitialSpawnLocationSystem>();

            // 1回目はWorker Threadへ探索を渡し、明示的な完了後の2回目で結果Entityを作る二段階を再現します。
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            system.Update(world.Unmanaged);

            using EntityQuery locationQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnLocation>());
            using EntityQuery searchStatusQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnSearchStatus>());
            Assert.That(locationQuery.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(searchStatusQuery.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(
                searchStatusQuery.GetSingleton<InitialSpawnSearchStatus>().Phase,
                Is.EqualTo(InitialSpawnSearchPhase.Succeeded),
                "初期スポーンの記録後は、Presentationが待機を終えられる成功状態が必要です。");
            InitialSpawnLocation location = locationQuery.GetSingleton<InitialSpawnLocation>();
            AssertCenteredCoreFootprintIsBuildable(generationSettings, location);

            // 完了後に再更新してもSingletonを増やさず、ワールド内の開始地点が一意であることを保証します。
            system.Update(world.Unmanaged);
            Assert.That(locationQuery.CalculateEntityCount(), Is.EqualTo(1));
        }

        /// <summary>
        /// 非同期の初期スポーン探索を開始した直後に外部から既存地点を追加し、探索Jobの完了後に一時結果を安全に破棄できることを保証します。
        /// Jobが書き込み中のNativeReferenceを先に破棄するSafety Check例外と、探索結果によるSingletonの二重作成を防ぎます。
        /// </summary>
        [Test]
        public void InitialSpawnLocationSystem_ExistingLocationDuringSearchCompletesJobBeforeDisposal()
        {
            using World world = new World("InitialSpawnLocationSystem Existing Location Test");
            EntityManager entityManager = world.EntityManager;
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                settingsEntity,
                WorldGenerationSettings.CreateDefault(WorldSeedUtility.Mix64(2UL), 1));
            entityManager.AddComponentData(
                settingsEntity,
                ResourceGenerationSettings.CreateDefault());

            SystemHandle system = world.GetOrCreateSystem<InitialSpawnLocationSystem>();

            // 1回目の更新で探索Jobだけを開始し、完了待ちを行わないまま外部由来の地点を追加します。
            system.Update(world.Unmanaged);
            Entity existingLocationEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                existingLocationEntity,
                new InitialSpawnLocation
                {
                    TileX = 10,
                    TileY = -20,
                    RegionMinimumTileX = 4,
                    RegionMinimumTileY = -24,
                    RegionWidthInTiles = 12,
                    RegionHeightInTiles = 8
                });

            // 2回目の更新は探索Jobを完了してから結果領域を破棄し、既存地点をそのまま成功結果として採用します。
            system.Update(world.Unmanaged);

            using EntityQuery locationQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnLocation>());
            using EntityQuery searchStatusQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnSearchStatus>());
            Assert.That(locationQuery.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(
                searchStatusQuery.GetSingleton<InitialSpawnSearchStatus>().Phase,
                Is.EqualTo(InitialSpawnSearchPhase.Succeeded));
        }

        /// <summary>
        /// テスト用Worldへ設定Entityと要求チャンクを作り、System更新後に4096タイルと完了状態が得られることを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_ProducesBufferAndDisablesRequest()
        {
            using World world = new World("WorldGenerationTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-system-test"),
                1);

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            entityManager.AddComponentData(settingsEntity, resourceSettings);

            Entity chunkEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(chunkEntity, new ChunkCoordinateComponent { X = 1, Y = -1 });
            entityManager.AddComponentData(chunkEntity, new ChunkGenerationStatus
            {
                Phase = ChunkGenerationPhase.Queued,
                GeneratedWithVersion = 0
            });
            entityManager.AddComponent<ChunkGenerationRequest>(chunkEntity);
            entityManager.AddBuffer<GeneratedTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedResourceTile>(chunkEntity);

            // ISystemを明示的に1回更新し、テスト終了前にJobを完了してからEntityデータを読み取ります。
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            ChunkGenerationStatus status = entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
            DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(chunkEntity);
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity);

            Assert.That(status.Phase, Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
            Assert.That(status.GeneratedWithVersion, Is.EqualTo(settings.GeneratorVersion));
            Assert.That(tiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(resources.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(chunkEntity), Is.False);

            GeneratedTile expectedFirstTile = DeterministicChunkGenerator.GenerateTile(settings, 64, -64);
            AssertTileEqual(expectedFirstTile, tiles[0]);
            GeneratedResourceTile expectedFirstResource =
                DeterministicResourceGenerator.GenerateResource(
                    settings,
                    resourceSettings,
                    expectedFirstTile,
                    64,
                    -64);
            AssertResourceEqual(expectedFirstResource, resources[0], 64, -64);
        }

        /// <summary>
        /// 原点だけが見えるObserverと同時生成上限3を準備し、中心優先かつ上限以内で要求が作られることを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_QueuesNearChunksWithinConcurrentLimit()
        {
            using World world = new World("ChunkStreamingQueueTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 3,
                    MaximumSubChunkApplicationsPerFrame = 1
                });

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                ComponentType.ReadOnly<ChunkGenerationStatus>());
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(3));
            Assert.That(ContainsChunkCoordinate(chunkQuery, 0, 0), Is.True);

            // 生成Jobを進めない2回目の更新では3要求が上限を満たしたままなので、重複Entityを追加しません。
            streamingSystem.Update(world.Unmanaged);
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(3));
            chunkQuery.Dispose();
        }

        /// <summary>
        /// intタイル座標域の正負端にObserverを置き、先読み余白が有効域の外へチャンクを要求しないことを保証します。
        /// 範囲外チャンクを64倍した結果が反対側のint端へ周回し、無関係な地形を生成する退行を防ぐテストです。
        /// </summary>
        /// <param name="edgeChunkCoordinate">検証する正または負の論理チャンク境界です。</param>
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate)]
        public void ChunkStreamingSystem_WorldEdge_ClampsPreloadToConvertibleChunkDomain(
            int edgeChunkCoordinate)
        {
            using World world = new World("ChunkStreamingWorldEdgeTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = edgeChunkCoordinate,
                    MaximumVisibleChunkX = edgeChunkCoordinate,
                    MinimumVisibleChunkY = edgeChunkCoordinate,
                    MaximumVisibleChunkY = edgeChunkCoordinate
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 4,
                    MaximumSubChunkApplicationsPerFrame = 1
                });

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>());
            int inwardChunkCoordinate = edgeChunkCoordinate
                == WorldGridConstants.MinimumLogicalChunkCoordinate
                    ? edgeChunkCoordinate + 1
                    : edgeChunkCoordinate - 1;

            // 端の外側は切り落とされるため、境界セルと内側隣接セルを組み合わせた2×2だけが要求されます。
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(4));
            Assert.That(ContainsChunkCoordinate(chunkQuery, edgeChunkCoordinate, edgeChunkCoordinate), Is.True);
            Assert.That(ContainsChunkCoordinate(chunkQuery, inwardChunkCoordinate, edgeChunkCoordinate), Is.True);
            Assert.That(ContainsChunkCoordinate(chunkQuery, edgeChunkCoordinate, inwardChunkCoordinate), Is.True);
            Assert.That(ContainsChunkCoordinate(chunkQuery, inwardChunkCoordinate, inwardChunkCoordinate), Is.True);

            using Unity.Collections.NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Unity.Collections.Allocator.Temp);
            for (int index = 0; index < coordinates.Length; index++)
            {
                ChunkCoordinateComponent coordinate = coordinates[index];
                AssertChunkFitsInWorldTileDomain(coordinate.X);
                AssertChunkFitsInWorldTileDomain(coordinate.Y);
            }

            chunkQuery.Dispose();
        }

        /// <summary>
        /// プリロード外かつ保持範囲内の未開始要求を準備し、要求だけが取り消されEntityは再利用可能に残ることを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_CancelsQueuedChunkOutsidePreloadRange()
        {
            using World world = new World("ChunkStreamingCancellationTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity queuedEntity = CreateChunkEntity(
                entityManager,
                2,
                0,
                ChunkGenerationPhase.Queued,
                requestEnabled: true);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(queuedEntity), Is.True);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(queuedEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Unrequested));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(queuedEntity), Is.False);
        }

        /// <summary>
        /// 保持範囲外の古いチャンクと離れた新しいObserverを準備し、古いEntityを破棄して新中心を要求することを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_UnloadsOutsideRetentionAndRequestsNewCenter()
        {
            using World world = new World("ChunkStreamingUnloadTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 10,
                    MaximumVisibleChunkX = 10,
                    MinimumVisibleChunkY = -5,
                    MaximumVisibleChunkY = -5
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 0,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity oldEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Active,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(oldEntity), Is.False);
            EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>());
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(ContainsChunkCoordinate(chunkQuery, 10, -5), Is.True);
            chunkQuery.Dispose();
        }

        /// <summary>
        /// 表示済みチャンクを可視範囲外へ移動させ、保持データを破棄せずInactiveへ遷移することを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_MarksRetainedVisibleDataInactive()
        {
            using World world = new World("ChunkStreamingInactiveTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 2,
                    MaximumVisibleChunkX = 2,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity retainedEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Active,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(retainedEntity), Is.True);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(retainedEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Inactive));
        }

        /// <summary>
        /// 複数のキャンセル済みEntityを可視範囲内へ戻し、再有効化も同時生成上限を超えないことを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_ReactivationRespectsConcurrentLimit()
        {
            using World world = new World("ChunkStreamingReactivationTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity centerEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Unrequested,
                requestEnabled: false);
            Entity neighboringEntity = CreateChunkEntity(
                entityManager,
                1,
                0,
                ChunkGenerationPhase.Unrequested,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(centerEntity), Is.True);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(centerEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Queued));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(neighboringEntity), Is.False);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(neighboringEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Unrequested));
        }

        /// <summary>
        /// テスト用Worldへ生成設定、ストリーミング設定、Observerの3つのSingleton Entityを準備します。
        /// </summary>
        /// <param name="entityManager">テスト用WorldのEntityManagerです。</param>
        /// <param name="observer">カメラ表示範囲として入力する論理チャンク矩形です。</param>
        /// <param name="streamingSettings">生成上限と保持余白を含むテスト条件です。</param>
        private static void CreateStreamingSingletons(
            EntityManager entityManager,
            ChunkStreamingObserver observer,
            ChunkStreamingSettings streamingSettings)
        {
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                settingsEntity,
                WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString("streaming-test"), 1));
            entityManager.AddComponentData(settingsEntity, ResourceGenerationSettings.CreateDefault());
            entityManager.AddComponentData(settingsEntity, streamingSettings);

            Entity observerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(observerEntity, observer);
        }

        /// <summary>
        /// ストリーミングテスト用のチャンクEntityを指定状態で作り、空の地形・資源Bufferも追加します。
        /// </summary>
        /// <param name="entityManager">テスト用WorldのEntityManagerです。</param>
        /// <param name="x">論理チャンクの水平座標です。</param>
        /// <param name="y">論理チャンクの垂直座標です。</param>
        /// <param name="phase">準備する生成段階です。</param>
        /// <param name="requestEnabled">ChunkGenerationRequestを有効にする場合はtrueです。</param>
        /// <returns>作成したテスト用チャンクEntityです。</returns>
        private static Entity CreateChunkEntity(
            EntityManager entityManager,
            int x,
            int y,
            ChunkGenerationPhase phase,
            bool requestEnabled)
        {
            Entity entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new ChunkCoordinateComponent { X = x, Y = y });
            entityManager.AddComponentData(entity, new ChunkGenerationStatus
            {
                Phase = phase,
                GeneratedWithVersion = phase == ChunkGenerationPhase.Queued ? 0 : 1
            });
            entityManager.AddComponent<ChunkGenerationRequest>(entity);
            entityManager.SetComponentEnabled<ChunkGenerationRequest>(entity, requestEnabled);
            entityManager.AddBuffer<GeneratedTile>(entity);
            entityManager.AddBuffer<GeneratedResourceTile>(entity);
            return entity;
        }

        /// <summary>
        /// 1つのシードについて、地形や資源を上書きせずに必須資源付き建築候補を発見できることを検証します。
        /// 代表シードの通常テストと100シード負荷テストで同じAssertionを共有するための補助メソッドです。
        /// </summary>
        /// <param name="seedIndex">決定論的なワールドシードへ変換する番号です。</param>
        private static void AssertNaturalRequiredResourcesForSeed(int seedIndex)
        {
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            EquipmentBuildableRegionSearchSettings searchSettings =
                EquipmentBuildableRegionSearchSettings.CreateInitialLocationDefault();
            EquipmentBuildableRegionResourceRequirements requirements =
                EquipmentBuildableRegionResourceRequirements.CreateDefault();
            ulong worldSeed = WorldSeedUtility.Mix64((ulong)seedIndex);
            WorldGenerationSettings generationSettings =
                WorldGenerationSettings.CreateDefault(worldSeed, 2);

            // 地形・資源の追加生成や上書きを行わず、同じ通常生成関数だけで初期候補を選別します。
            bool found = EquipmentBuildableRegionLocator.TryFindNearestWithResources(
                generationSettings,
                resourceSettings,
                searchSettings,
                requirements,
                out EquipmentBuildableRegion region);

            Assert.That(
                found,
                Is.True,
                $"通常生成だけで木・石・水がそろう建築領域を発見できません。seedIndex={seedIndex}, worldSeed={worldSeed}");
            NaturalResourceTypeMask foundTypes = CollectResourceTypesAroundRegion(
                generationSettings,
                resourceSettings,
                region,
                requirements.SearchMarginInTiles);
            Assert.That(
                foundTypes & requirements.RequiredTypes,
                Is.EqualTo(requirements.RequiredTypes),
                $"返された候補の周囲に必須資源が不足しています。seedIndex={seedIndex}");
            AssertCenteredCoreFootprintIsBuildable(
                generationSettings,
                InitialSpawnLocation.FromRegion(region));
        }

        /// <summary>
        /// 初期スポーンを中心とする3×3を通常生成器から読み直し、中央コアの全占有セルが設備建築可能であることを保証します。
        /// 初期位置Systemテストと複数シードの資源付き探索テストで同じ9セル検証を共有します。
        /// </summary>
        /// <param name="generationSettings">検証対象ワールドと同じ地形生成設定です。</param>
        /// <param name="location">中央コアの中心として記録された初期スポーン地点です。</param>
        private static void AssertCenteredCoreFootprintIsBuildable(
            in WorldGenerationSettings generationSettings,
            in InitialSpawnLocation location)
        {
            int footprintRadius = InitialSpawnLocation.CentralCoreSizeInTiles / 2;

            // 中心だけでなく周囲8セルも確認し、水・マグマ・岩壁へコアがはみ出す候補を見逃さないようにします。
            for (int offsetY = -footprintRadius; offsetY <= footprintRadius; offsetY++)
            {
                for (int offsetX = -footprintRadius; offsetX <= footprintRadius; offsetX++)
                {
                    int worldTileX = location.TileX + offsetX;
                    int worldTileY = location.TileY + offsetY;
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        worldTileX,
                        worldTileY);
                    Assert.That(
                        EquipmentBuildableRegionLocator.CanPlaceEquipment(tile),
                        Is.True,
                        $"中央コア占有セルは通常設備を建築可能である必要があります。tile=({worldTileX}, {worldTileY})");
                }
            }
        }

        /// <summary>
        /// 建築候補の周囲を通常生成器で読み直し、存在する生成資源と採水可能な水域をビット集合へまとめます。
        /// </summary>
        /// <param name="generationSettings">通常地形を再生成する設定です。</param>
        /// <param name="resourceSettings">通常資源を再生成する設定です。</param>
        /// <param name="region">中央となる建築候補矩形です。</param>
        /// <param name="marginInTiles">矩形の各辺から外へ調べる距離です。単位はタイルです。</param>
        /// <returns>走査範囲で見つかった資源種類のビット集合です。</returns>
        private static NaturalResourceTypeMask CollectResourceTypesAroundRegion(
            in WorldGenerationSettings generationSettings,
            in ResourceGenerationSettings resourceSettings,
            in EquipmentBuildableRegion region,
            int marginInTiles)
        {
            NaturalResourceTypeMask foundTypes = NaturalResourceTypeMask.None;
            int minimumX = region.MinimumTileX - marginInTiles;
            int minimumY = region.MinimumTileY - marginInTiles;
            int maximumX = region.MinimumTileX + region.WidthInTiles - 1 + marginInTiles;
            int maximumY = region.MinimumTileY + region.HeightInTiles - 1 + marginInTiles;

            for (int worldY = minimumY; worldY <= maximumY; worldY++)
            {
                for (int worldX = minimumX; worldX <= maximumX; worldX++)
                {
                    GeneratedTile terrainTile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        worldX,
                        worldY);
                    GeneratedResourceTile resourceTile =
                        DeterministicResourceGenerator.GenerateResource(
                            generationSettings,
                            resourceSettings,
                            terrainTile,
                            worldX,
                            worldY);
                    foundTypes |= DeterministicResourceGenerator.ToMask(resourceTile.Type);
                    if (terrainTile.WaterSalinity != WaterSalinity.None)
                    {
                        foundTypes |= NaturalResourceTypeMask.Water;
                    }
                }
            }

            return foundTypes;
        }

        /// <summary>
        /// 資源IDから、同じ資源を配置可能にする地形適性フラグへ変換します。
        /// </summary>
        /// <param name="resourceType">配置済み基礎資源IDです。</param>
        /// <returns>対応する地形適性です。配置なしはNoneです。</returns>
        private static ResourceSuitability ToSuitability(GeneratedResourceType resourceType)
        {
            switch (resourceType)
            {
                case GeneratedResourceType.Tree:
                    return ResourceSuitability.Tree;
                case GeneratedResourceType.Stone:
                    return ResourceSuitability.Stone;
                case GeneratedResourceType.Sand:
                    return ResourceSuitability.Sand;
                default:
                    return ResourceSuitability.None;
            }
        }

        /// <summary>
        /// 1種類だけ配置可能な平坦テスト地形を走査し、発見した一意な群中心を総当たりで比較します。
        /// </summary>
        /// <param name="generationSettings">群中心を固定するシードと生成器バージョンです。</param>
        /// <param name="resourceSettings">最低間隔、群半径、ノイズ条件を含む資源設定です。</param>
        /// <param name="resourceType">今回検証する木・石・砂のいずれかです。</param>
        /// <param name="suitability">テスト地形へ付ける対象資源だけの適性です。</param>
        /// <param name="minimumSpacingInTiles">中心間で保証する期待距離です。単位はタイルです。</param>
        private static void AssertResourceClusterSpacing(
            in WorldGenerationSettings generationSettings,
            in ResourceGenerationSettings resourceSettings,
            GeneratedResourceType resourceType,
            ResourceSuitability suitability,
            int minimumSpacingInTiles)
        {
            GeneratedTile suitableTerrain = new GeneratedTile { ResourceSuitability = suitability };
            Dictionary<long, ResourceGenerationDebugInfo> uniqueAnchors =
                new Dictionary<long, ResourceGenerationDebugInfo>();

            // 正負座標と複数の最低間隔セルを含む固定範囲だけを走査し、チャンク生成やPresentationは起動しません。
            for (int worldY = -48; worldY < 48; worldY++)
            {
                for (int worldX = -48; worldX < 48; worldX++)
                {
                    ResourceGenerationDebugInfo debugInfo =
                        DeterministicResourceGenerator.GetDebugInfo(
                            generationSettings,
                            resourceSettings,
                            suitableTerrain,
                            worldX,
                            worldY);
                    if (debugInfo.SelectedType == GeneratedResourceType.None)
                    {
                        continue;
                    }

                    Assert.That(debugInfo.SelectedType, Is.EqualTo(resourceType));
                    long anchorKey = ((long)debugInfo.AnchorTileX << 32)
                        | (uint)debugInfo.AnchorTileY;
                    uniqueAnchors[anchorKey] = debugInfo;
                }
            }

            Assert.That(uniqueAnchors.Count, Is.GreaterThan(1), $"{resourceType}の群中心が不足しています。");
            ResourceGenerationDebugInfo[] anchors = new ResourceGenerationDebugInfo[uniqueAnchors.Count];
            uniqueAnchors.Values.CopyTo(anchors, 0);
            int minimumDistanceSquared = minimumSpacingInTiles * minimumSpacingInTiles;

            // 群内の多数の資源タイルではなく一意な中心だけを比較し、テスト時間を中心数の二乗へ抑えます。
            for (int firstIndex = 0; firstIndex < anchors.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < anchors.Length; secondIndex++)
                {
                    int deltaX = anchors[firstIndex].AnchorTileX - anchors[secondIndex].AnchorTileX;
                    int deltaY = anchors[firstIndex].AnchorTileY - anchors[secondIndex].AnchorTileY;
                    int distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    Assert.That(
                        distanceSquared,
                        Is.GreaterThanOrEqualTo(minimumDistanceSquared),
                        $"{resourceType}群中心の最低間隔違反です。"
                        + $"first=({anchors[firstIndex].AnchorTileX}, {anchors[firstIndex].AnchorTileY}), "
                        + $"second=({anchors[secondIndex].AnchorTileX}, {anchors[secondIndex].AnchorTileY})");
                }
            }
        }

        /// <summary>
        /// Query結果を走査し、指定した論理チャンク座標がちょうど1つ以上存在するか確認します。
        /// </summary>
        /// <param name="chunkQuery">ChunkCoordinateComponentを含むテスト用Queryです。</param>
        /// <param name="x">検索する水平チャンク座標です。</param>
        /// <param name="y">検索する垂直チャンク座標です。</param>
        /// <returns>一致する座標が見つかった場合はtrueです。</returns>
        private static bool ContainsChunkCoordinate(EntityQuery chunkQuery, int x, int y)
        {
            using Unity.Collections.NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Unity.Collections.Allocator.Temp);
            for (int index = 0; index < coordinates.Length; index++)
            {
                if (coordinates[index].X == x && coordinates[index].Y == y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 論理チャンクの先頭と末尾を64bitで計算し、intの絶対タイル座標域へ収まることを保証します。
        /// テスト自体の計算をintで周回させず、ストリーミングが生成器へ渡してよい座標かを確認します。
        /// </summary>
        /// <param name="chunkCoordinate">検証する1軸分の論理チャンク座標です。</param>
        private static void AssertChunkFitsInWorldTileDomain(int chunkCoordinate)
        {
            long firstTile = (long)chunkCoordinate * WorldGridConstants.LogicalChunkSize;
            long lastTile = firstTile + WorldGridConstants.LogicalChunkSize - 1L;

            Assert.That(firstTile, Is.InRange((long)int.MinValue, (long)int.MaxValue));
            Assert.That(lastTile, Is.InRange((long)int.MinValue, (long)int.MaxValue));
        }

        /// <summary>
        /// チャンク座標とローカル座標をワールド座標へ戻して1タイルを生成します。
        /// </summary>
        /// <param name="settings">テスト対象の生成設定です。</param>
        /// <param name="chunkX">論理チャンクの水平座標です。</param>
        /// <param name="chunkY">論理チャンクの垂直座標です。</param>
        /// <param name="localX">チャンク内の水平座標です。</param>
        /// <param name="localY">チャンク内の垂直座標です。</param>
        /// <returns>復元したワールド座標から生成したタイルです。</returns>
        private static GeneratedTile GenerateTileThroughChunkCoordinates(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            int localX,
            int localY)
        {
            int worldTileX = WorldGrid.ToWorldTileX(chunkX, localX);
            int worldTileY = WorldGrid.ToWorldTileY(chunkY, localY);
            return DeterministicChunkGenerator.GenerateTile(settings, worldTileX, worldTileY);
        }

        /// <summary>
        /// GeneratedTileの全フィールドを個別に比較し、失敗した分類または気候値をTest Runnerへ明示します。
        /// </summary>
        /// <param name="expected">期待するタイル値です。</param>
        /// <param name="actual">実際に生成されたタイル値です。</param>
        private static void AssertTileEqual(GeneratedTile expected, GeneratedTile actual)
        {
            Assert.That(actual.Biome, Is.EqualTo(expected.Biome));
            Assert.That(actual.Ground, Is.EqualTo(expected.Ground));
            Assert.That(actual.Soil, Is.EqualTo(expected.Soil));
            Assert.That(actual.WaterSalinity, Is.EqualTo(expected.WaterSalinity));
            Assert.That(actual.WaterBodyType, Is.EqualTo(expected.WaterBodyType));
            Assert.That(actual.ElevationClass, Is.EqualTo(expected.ElevationClass));
            Assert.That(actual.GameplayFlags, Is.EqualTo(expected.GameplayFlags));
            Assert.That(actual.ResourceSuitability, Is.EqualTo(expected.ResourceSuitability));
            Assert.That(actual.MovementCost, Is.EqualTo(expected.MovementCost));
            Assert.That(actual.Elevation, Is.EqualTo(expected.Elevation));
            Assert.That(actual.Temperature, Is.EqualTo(expected.Temperature));
            Assert.That(actual.Moisture, Is.EqualTo(expected.Moisture));
            Assert.That(actual.Weirdness, Is.EqualTo(expected.Weirdness));
        }

        /// <summary>
        /// 隣接する2タイルが淡水と海水の組み合わせかを判定します。
        /// 河口検証のループ内でAssertionを増やさず、境界数だけを集計するために使用します。
        /// </summary>
        /// <param name="first">現在タイルまたは上側タイルの水種別です。</param>
        /// <param name="second">左または下に隣接するタイルの水種別です。</param>
        /// <returns>一方が淡水でもう一方が海水ならtrueです。</returns>
        private static bool IsFreshSaltBoundary(WaterSalinity first, WaterSalinity second)
        {
            return (first == WaterSalinity.FreshWater && second == WaterSalinity.SaltWater)
                || (first == WaterSalinity.SaltWater && second == WaterSalinity.FreshWater);
        }

        /// <summary>
        /// GeneratedResourceTileの資源IDと表示差分を比較し、失敗座標をTest Runnerへ明示します。
        /// </summary>
        /// <param name="expected">期待する資源データです。</param>
        /// <param name="actual">実際に生成された資源データです。</param>
        /// <param name="worldTileX">失敗メッセージ用の水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">失敗メッセージ用の垂直ワールドタイル座標です。</param>
        private static void AssertResourceEqual(
            GeneratedResourceTile expected,
            GeneratedResourceTile actual,
            int worldTileX,
            int worldTileY)
        {
            Assert.That(
                actual.Type,
                Is.EqualTo(expected.Type),
                $"資源IDが一致しません。座標=({worldTileX}, {worldTileY})");
            Assert.That(
                actual.VisualVariant,
                Is.EqualTo(expected.VisualVariant),
                $"資源表示差分が一致しません。座標=({worldTileX}, {worldTileY})");
        }
    }
}
