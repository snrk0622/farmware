// このファイルは、負座標変換、地形・資源の決定性、初期スポーン探索と記録、ECS生成SystemをEditModeで検証します。
// 失敗時にどの仕様が崩れたか分かるよう、純粋計算とEntityデータフローを分けてテストします。

using System;
using System.Collections.Generic;
using System.Reflection;
using Farmware.Simulation;
using Farmware.WorldGeneration.Editor;
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

        /// <summary>ワールド外の表示用外周を生成し、土地・岩壁へ接続しない水域値になることを保証します。</summary>
        /// <param name="chunkCoordinate">正または負の端にある論理チャンク座標です。</param>
        /// <param name="outsideLocalCoordinate">外周1セルのローカル座標です。</param>
        [TestCase(WorldGridConstants.MinimumLogicalChunkCoordinate, -1)]
        [TestCase(WorldGridConstants.MaximumLogicalChunkCoordinate, 64)]
        public void TerrainNeighborHalo_WorldEdge_TreatsOutsideNeighborAsDisconnected(
            int chunkCoordinate, int outsideLocalCoordinate)
        {
            using World world = new World("WorldEdgePresentationNeighborTests");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<GeneratedTile> tiles = world.EntityManager.AddBuffer<GeneratedTile>(entity);
            using var halo = new Unity.Collections.NativeArray<GeneratedTile>(TerrainNeighborHaloUtility.TileCount,
                Unity.Collections.Allocator.TempJob);
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("world-edge-presentation-neighbor-test"), WorldGenerationSettings.CurrentGeneratorVersion);
            TerrainNeighborHaloUtility.Fill(settings, new ChunkCoordinate(chunkCoordinate, chunkCoordinate),
                0, WorldGridConstants.SubChunksPerChunk, halo);
            GeneratedTile neighbor = TerrainNeighborHaloUtility.GetTile(outsideLocalCoordinate, 0, tiles, halo);
            Assert.That(neighbor.Ground, Is.EqualTo(GroundType.Water),
                "int端の外側は土地・岩壁のどちらにも接続せず、資源を壁の背後へ隠さない値にします。");
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
                WorldGenerationSettings.CurrentGeneratorVersion);
            ChunkCoordinate firstCoordinate = new ChunkCoordinate(-2, 3);
            ChunkCoordinate secondCoordinate = new ChunkCoordinate(4, -1);
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();

            ulong firstThenSecondFirst = DeterministicChunkGenerator.ComputeChunkHash(settings, biomeSettings, firstCoordinate);
            ulong firstThenSecondSecond = DeterministicChunkGenerator.ComputeChunkHash(settings, biomeSettings, secondCoordinate);
            ulong secondThenFirstSecond = DeterministicChunkGenerator.ComputeChunkHash(settings, biomeSettings, secondCoordinate);
            ulong secondThenFirstFirst = DeterministicChunkGenerator.ComputeChunkHash(settings, biomeSettings, firstCoordinate);

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
                WorldGenerationSettings.CurrentGeneratorVersion);

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
                WorldGenerationSettings.CurrentGeneratorVersion);
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
                WorldGenerationSettings.CurrentGeneratorVersion);
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
                Is.InRange(0.06f, 0.25f),
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
                WorldGenerationSettings.CurrentGeneratorVersion);
            // 新しい標高帯契約では海面が低地上限を越える設定を無効とするため、
            // 有効範囲内で海を最大化し、試験範囲では標高格子を細かくして水域との重なりを十分に確保します。
            settings.SeaLevelThreshold = settings.WetlandMaximumElevation;
            settings.ElevationCellSizeInTiles = 16;
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
                WorldGenerationSettings.CurrentGeneratorVersion);
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
        /// 現在の試作既定値を作り、新18Biomeが使う環境軸と地形ノイズ設定を保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_UseCurrentBiomeAndTerrainSettings()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("default-settings-test"),
                2);

            Assert.That(settings.TemperatureCellSizeInTiles, Is.EqualTo(96));
            Assert.That(settings.MoistureCellSizeInTiles, Is.EqualTo(80));
            Assert.That(settings.BiomeWarpCellSizeInTiles, Is.EqualTo(192));
            Assert.That(settings.BiomeWarpAmplitudeInTiles, Is.EqualTo(12));
            Assert.That(settings.GeneratorVersion, Is.EqualTo(WorldGenerationSettings.CurrentGeneratorVersion));
            Assert.That(settings.ColdTemperatureThreshold, Is.EqualTo(23000));
            Assert.That(settings.WetlandMoistureThreshold, Is.EqualTo(50000));
            Assert.That(settings.WetlandMaximumElevation, Is.EqualTo(36000));
            Assert.That(settings.HighlandElevationThreshold, Is.EqualTo(43000));
            Assert.That(settings.MagmaCellSizeInTiles, Is.EqualTo(24));
            Assert.That(settings.MagmaDetailCellSizeInTiles, Is.EqualTo(8));
            Assert.That(settings.MagmaWarpAmplitudeInTiles, Is.EqualTo(6));
            Assert.That(settings.MagmaOctaveCount, Is.EqualTo(3));
            Assert.That(settings.MagmaNoiseThreshold, Is.EqualTo(34000));
            Assert.That(settings.MagmaMinimumVolcanicInfluence, Is.EqualTo(0));
            Assert.That(settings.RockWallMassCellSizeInTiles, Is.EqualTo(24));
            Assert.That(settings.RockWallDetailCellSizeInTiles, Is.EqualTo(8));
            Assert.That(settings.RockWallWarpAmplitudeInTiles, Is.EqualTo(6));
            Assert.That(settings.RockWallNoiseThreshold, Is.EqualTo(41000));
            Assert.That(settings.RockWallCorridorHalfWidth, Is.EqualTo(1100));
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

            // 共通配置APIが橋用属性を誤って許可しないよう、通常設備専用の正方向属性だけを参照することを固定します。
            Assert.That(
                TerrainPlacementRules.Evaluate(equipmentTile, TerrainPlacementKind.NormalEquipment),
                Is.EqualTo(TerrainPlacementResult.Allowed));
            Assert.That(
                TerrainPlacementRules.Evaluate(bridgeOnlyTile, TerrainPlacementKind.NormalEquipment),
                Is.Not.EqualTo(TerrainPlacementResult.Allowed));
        }

        /// <summary>
        /// 完成済みの乾いたタイル座標を準備し、初期スポーンが余分な旧建設領域を持たず同じ座標を記録することを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnLocation_FromTileRecordsSurfaceCoordinate()
        {
            InitialSpawnLocation location = InitialSpawnLocation.FromTile(-12, 7);

            Assert.That(location.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(location.TileX, Is.EqualTo(-12));
            Assert.That(location.TileY, Is.EqualTo(7));
        }

        /// <summary>
        /// C5-Cまで完成した1チャンクを準備し、資源配置が決定論的で最終地形適性から外れないことを保証します。
        /// 分類前の基礎タイルを旧7Biome相当として再利用せず、製品と同じC3・C4・C5-C順序を通します。
        /// </summary>
        [Test]
        public void ResourceGeneration_IsDeterministicAndRespectsTerrainSuitability()
        {
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("resource-generation-test"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            BiomeClassificationSettings biomeSettings =
                BiomeDefinitionCatalog.CreateDefault();
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            // C5-Cでは資源密度の再調整を行わないため、この統合テストでは閾値だけ0にして有効経路を確実に通します。
            // 種別ごとの通常密度と最低間隔は下の専用テストおよびTERRAIN-01C7で別に検証します。
            resourceSettings.TreeThreshold = 0;
            resourceSettings.StoneThreshold = 0;
            resourceSettings.SandThreshold = 0;
            ChunkCoordinate coordinate = new ChunkCoordinate(WorldLayer.Surface, 0, 0);
            Assert.That(
                HydrologyDiagnosticChunk.TryGenerate(
                    generationSettings,
                    biomeSettings,
                    coordinate,
                    out HydrologyDiagnosticChunk chunk,
                    out HydrologyDiagnosticGenerationStage stage),
                Is.True,
                $"完成地形チャンクを生成できませんでした。Stage={stage}");

            using (chunk)
            {
                int generatedResourceCount = 0;
                for (int tileIndex = 0;
                    tileIndex < WorldGridConstants.TilesPerChunk;
                    tileIndex++)
                {
                    int localX = tileIndex % WorldGridConstants.LogicalChunkSize;
                    int localY = tileIndex / WorldGridConstants.LogicalChunkSize;
                    int worldX = WorldGrid.ToWorldTileX(coordinate.X, localX);
                    int worldY = WorldGrid.ToWorldTileY(coordinate.Y, localY);
                    GeneratedTile terrainTile = chunk.TerrainTiles[tileIndex];
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

                    if (terrainTile.ResourceSuitability != ResourceSuitability.None
                        && (terrainTile.Ground == GroundType.Water
                            || terrainTile.Ground == GroundType.Magma))
                    {
                        Assert.Fail(
                            $"水域またはマグマへ別配置資源の適性が残っています。座標=({worldX}, {worldY})");
                    }

                    if (first.Type == GeneratedResourceType.None)
                    {
                        continue;
                    }

                    generatedResourceCount++;
                    ResourceSuitability requiredSuitability = ToSuitability(first.Type);
                    if ((terrainTile.ResourceSuitability & requiredSuitability) == 0)
                    {
                        Assert.Fail(
                            $"地形適性外へ資源が配置されました。座標=({worldX}, {worldY}), 資源={first.Type}");
                    }
                }

                Assert.That(
                    generatedResourceCount,
                    Is.GreaterThan(0),
                    "完成チャンク内で資源生成の有効経路を少なくとも1件検証します。");
            }
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
                WorldGenerationSettings.CurrentGeneratorVersion);
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
                WorldGenerationSettings.CurrentGeneratorVersion);
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
        /// テスト用Worldへ完成済み原点チャンクを準備し、許可Biomeの乾いた非占有セルから初期スポーンを1件だけ記録することを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnLocationSystem_RecordsCandidateFromCompletedChunk()
        {
            using World world = new World("InitialSpawnLocationSystemTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.Mix64(1UL),
                WorldGenerationSettings.CurrentGeneratorVersion);
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, generationSettings);
            entityManager.AddComponentData(settingsEntity, resourceSettings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());

            Entity chunkEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.ReadyToApply,
                false);
            InitializeCompletedChunkBuffers(entityManager, chunkEntity);
            SetInitialSpawnCandidate(entityManager, chunkEntity, 0, 0);

            SystemHandle system = world.GetOrCreateSystem<InitialSpawnLocationSystem>();

            // 1回目は原点リングを要求済みとして探索状態へ進み、2回目に完成Bufferだけを読み取ります。
            system.Update(world.Unmanaged);
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
            Assert.That(location.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(location.TileX, Is.Zero);
            Assert.That(location.TileY, Is.Zero);

            // 完了後に再更新してもSingletonを増やさず、ワールド内の開始地点が一意であることを保証します。
            system.Update(world.Unmanaged);
            Assert.That(locationQuery.CalculateEntityCount(), Is.EqualTo(1));
        }

        /// <summary>
        /// 原点チャンクの候補と負側隣接チャンクの候補を準備し、最初の候補で探索を打ち切らず、
        /// タイル原点から近い候補、または同距離で座標順が先の負側候補を選ぶことを保証します。
        /// </summary>
        /// <param name="originLocalX">原点チャンク候補の水平ローカル座標です。</param>
        /// <param name="originLocalY">原点チャンク候補の垂直ローカル座標です。</param>
        [TestCase(63, 63)]
        [TestCase(1, 0)]
        public void InitialSpawnLocationSystem_ContinuesToNegativeRingForCloserOrEarlierCandidate(
            int originLocalX,
            int originLocalY)
        {
            using World world = new World("InitialSpawnLocationSystem Negative Ring Test");
            EntityManager entityManager = world.EntityManager;
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                settingsEntity,
                WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.Mix64(3UL),
                    WorldGenerationSettings.CurrentGeneratorVersion));
            entityManager.AddComponentData(
                settingsEntity,
                ResourceGenerationSettings.CreateDefault());
            entityManager.AddComponentData(
                settingsEntity,
                BiomeDefinitionCatalog.CreateDefault());

            Entity originChunk = Entity.Null;
            Entity negativeXChunk = Entity.Null;
            for (int chunkY = -1; chunkY <= 1; chunkY++)
            {
                for (int chunkX = -1; chunkX <= 1; chunkX++)
                {
                    Entity chunkEntity = CreateChunkEntity(
                        entityManager,
                        chunkX,
                        chunkY,
                        ChunkGenerationPhase.ReadyToApply,
                        false);
                    InitializeCompletedChunkBuffers(entityManager, chunkEntity);

                    if (chunkX == 0 && chunkY == 0)
                    {
                        originChunk = chunkEntity;
                    }
                    else if (chunkX == -1 && chunkY == 0)
                    {
                        negativeXChunk = chunkEntity;
                    }
                }
            }

            // 負側チャンクの候補は常に(-1, 0)です。原点チャンクを(63, 63)にしたケースでは距離で、
            // (1, 0)にしたケースでは同距離時のY・X昇順で、どちらも負側候補が先になります。
            SetInitialSpawnCandidate(entityManager, originChunk, originLocalX, originLocalY);
            SetInitialSpawnCandidate(entityManager, negativeXChunk, 63, 0);

            SystemHandle system = world.GetOrCreateSystem<InitialSpawnLocationSystem>();

            // 1回目は半径0を要求し、2回目は遠い候補を保留して半径1へ拡張し、
            // 3回目に9チャンク全体から真の最近傍を確定します。
            system.Update(world.Unmanaged);
            system.Update(world.Unmanaged);
            system.Update(world.Unmanaged);

            using EntityQuery locationQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnLocation>());
            Assert.That(locationQuery.CalculateEntityCount(), Is.EqualTo(1));
            InitialSpawnLocation location = locationQuery.GetSingleton<InitialSpawnLocation>();
            Assert.That(location.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(location.TileX, Is.EqualTo(-1));
            Assert.That(location.TileY, Is.Zero);
        }

        /// <summary>
        /// 初期探索チャンクを要求した直後に外部から既存地点を追加し、探索結果によるSingletonの二重作成を防ぐことを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnLocationSystem_ExistingLocationStopsWithoutDuplicate()
        {
            using World world = new World("InitialSpawnLocationSystem Existing Location Test");
            EntityManager entityManager = world.EntityManager;
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                settingsEntity,
                WorldGenerationSettings.CreateDefault(WorldSeedUtility.Mix64(2UL), WorldGenerationSettings.CurrentGeneratorVersion));
            entityManager.AddComponentData(
                settingsEntity,
                ResourceGenerationSettings.CreateDefault());
            entityManager.AddComponentData(
                settingsEntity,
                BiomeDefinitionCatalog.CreateDefault());

            SystemHandle system = world.GetOrCreateSystem<InitialSpawnLocationSystem>();

            // 1回目の更新で原点チャンク要求だけを作り、完成待ち中に外部由来の地点を追加します。
            system.Update(world.Unmanaged);
            Entity existingLocationEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                existingLocationEntity,
                new InitialSpawnLocation
                {
                    Layer = WorldLayer.Surface,
                    TileX = 10,
                    TileY = -20
                });

            // 2回目の更新は既存地点をそのまま成功結果として採用し、要求中チャンクから地点を追加しません。
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
                WorldGenerationSettings.CurrentGeneratorVersion);

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            entityManager.AddComponentData(settingsEntity, resourceSettings);

            Entity chunkEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkCoordinateComponent
                {
                    Layer = WorldLayer.Surface,
                    X = 1,
                    Y = -1
                });
            entityManager.AddComponentData(chunkEntity, new ChunkGenerationStatus
            {
                Phase = ChunkGenerationPhase.Queued,
                GeneratedWithVersion = 0
            });
            entityManager.AddComponent<ChunkGenerationRequest>(chunkEntity);
            entityManager.AddBuffer<GeneratedTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedResourceTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedHydrologyCell>(chunkEntity);
            entityManager.AddBuffer<GeneratedSurfaceWaterTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedHydrologyTile>(chunkEntity);

            // 初回更新は投入だけです。Workerが早く完成しても、同じ更新で未公開結果を読ませないことを保証します。
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            Assert.That(entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.GeneratingData));
            Assert.That(entityManager.GetBuffer<GeneratedTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyCell>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedSurfaceWaterTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(chunkEntity), Is.False);
            WaitForChunkGeneration(world, system, chunkEntity);

            ChunkGenerationStatus status = entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
            DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(chunkEntity);
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity);
            DynamicBuffer<GeneratedHydrologyCell> hydrology =
                entityManager.GetBuffer<GeneratedHydrologyCell>(chunkEntity);
            DynamicBuffer<GeneratedSurfaceWaterTile> surfaceWater =
                entityManager.GetBuffer<GeneratedSurfaceWaterTile>(chunkEntity);
            DynamicBuffer<GeneratedHydrologyTile> hydrologyTiles =
                entityManager.GetBuffer<GeneratedHydrologyTile>(chunkEntity);

            Assert.That(status.Phase, Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
            Assert.That(status.GeneratedWithVersion, Is.EqualTo(settings.GeneratorVersion));
            Assert.That(tiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(resources.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(hydrology.Length, Is.EqualTo(DeterministicHydrologyGenerator.CellsPerChunk));
            Assert.That(surfaceWater.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(hydrologyTiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(chunkEntity), Is.False);

            GeneratedTile baseFirstTile = DeterministicChunkGenerator.GenerateTile(settings, 64, -64);
            Assert.That(
                DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                    settings,
                    BiomeDefinitionCatalog.CreateDefault(),
                    64,
                    -64,
                    baseFirstTile,
                    hydrologyTiles[0],
                    surfaceWater[0],
                    out GeneratedTile expectedFirstTile),
                Is.True,
                "製品Jobと同じ完成水文から新18Biomeを確定できる必要があります。");
            AssertTileEqual(expectedFirstTile, tiles[0]);
            GeneratedResourceTile expectedFirstResource =
                DeterministicResourceGenerator.GenerateResource(
                    settings,
                    resourceSettings,
                    expectedFirstTile,
                    64,
                    -64);
            AssertResourceEqual(expectedFirstResource, resources[0], 64, -64);
            Assert.That(hydrology[0].CellX, Is.EqualTo(8));
            Assert.That(hydrology[0].CellY, Is.EqualTo(-8));
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    8,
                    -8,
                    out GeneratedHydrologyCell expectedFirstHydrology),
                Is.True);
            Assert.That(hydrology[0].HydrologyHeight, Is.EqualTo(expectedFirstHydrology.HydrologyHeight));
            Assert.That(hydrology[0].HydrologyMoisture, Is.EqualTo(expectedFirstHydrology.HydrologyMoisture));
            Assert.That(hydrology[0].CatchmentAreaCells, Is.EqualTo(expectedFirstHydrology.CatchmentAreaCells));
            Assert.That(hydrology[0].RunoffMoistureSum, Is.EqualTo(expectedFirstHydrology.RunoffMoistureSum));
            Assert.That(hydrology[0].RunoffEquivalentCells, Is.EqualTo(expectedFirstHydrology.RunoffEquivalentCells));
            Assert.That(
                hydrology[0].MaximumContributingDistance,
                Is.EqualTo(expectedFirstHydrology.MaximumContributingDistance));
            Assert.That(hydrology[0].PlateauRank, Is.EqualTo(expectedFirstHydrology.PlateauRank));
            Assert.That(hydrology[0].FlowDirection, Is.EqualTo(expectedFirstHydrology.FlowDirection));
            Assert.That(
                hydrology[0].DownstreamReachCellX,
                Is.EqualTo(expectedFirstHydrology.DownstreamReachCellX));
            Assert.That(
                hydrology[0].DownstreamReachCellY,
                Is.EqualTo(expectedFirstHydrology.DownstreamReachCellY));
            Assert.That(
                hydrology[0].DownstreamEdgeCount,
                Is.EqualTo(expectedFirstHydrology.DownstreamEdgeCount));
            Assert.That(
                hydrology[0].ConnectionFlags & ~HydrologyConnectionFlags.Groundwater,
                Is.EqualTo(expectedFirstHydrology.ConnectionFlags));
            Assert.That(
                (hydrology[0].ConnectionFlags & HydrologyConnectionFlags.Groundwater) != 0,
                Is.EqualTo(
                    hydrology[0].GroundwaterPotential
                        >= settings.Groundwater.GroundwaterConnectionThreshold));
            Assert.That(hydrology[0].CatchmentFlags, Is.EqualTo(expectedFirstHydrology.CatchmentFlags));

            Assert.That(
                DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                    settings,
                    64,
                    -64,
                    expectedFirstTile.GeologyPermeability,
                    expectedFirstTile.GeologyGroundwaterStorage,
                    out AdjustedGroundwaterGeology expectedFirstAdjustedGeology),
                Is.True);
            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateNaturalSoilSaturation(
                    settings.Groundwater,
                    hydrology[0].HydrologyMoisture,
                    hydrology[0].GroundwaterPotential,
                    expectedFirstAdjustedGeology.Permeability,
                    expectedFirstTile.TerrainRuggedness,
                    out ushort expectedFirstSaturation),
                Is.True);
            Assert.That(
                hydrologyTiles[0].NaturalSoilSaturationPotential,
                Is.EqualTo(expectedFirstSaturation));

            int positiveFloodingTiles = 0;
            int freshwaterConnectedTiles = 0;
            int wetlandEligibleTiles = 0;
            int largeWetlandExtentTiles = 0;
            for (int index = 0; index < hydrologyTiles.Length; index++)
            {
                GeneratedHydrologyTile hydrologyTile = hydrologyTiles[index];
                GeneratedTile terrainTile = tiles[index];
                bool freshwaterConnected =
                    (hydrologyTile.Flags & GeneratedHydrologyTileFlags.FreshwaterConnected) != 0;
                bool wetlandEligible =
                    (hydrologyTile.Flags & GeneratedHydrologyTileFlags.WetlandTileEligible) != 0;
                bool expectedWetlandEligible =
                    DeterministicWetlandExtentGenerator.IsWetlandTileEligible(
                        settings.NaturalFlooding,
                        terrainTile.Elevation,
                        freshwaterConnected,
                        hydrologyTile.NaturalSoilSaturationPotential,
                        hydrologyTile.NaturalFloodingPotential);

                if (hydrologyTile.NaturalFloodingPotential > 0)
                {
                    positiveFloodingTiles++;
                }
                if (freshwaterConnected)
                {
                    freshwaterConnectedTiles++;
                }
                if (wetlandEligible)
                {
                    wetlandEligibleTiles++;
                }
                if ((hydrologyTile.Flags & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0)
                {
                    largeWetlandExtentTiles++;
                }

                Assert.That(
                    wetlandEligible,
                    Is.EqualTo(expectedWetlandEligible),
                    $"C4-D2の湿原資格bitが純粋判定と一致しません。tileIndex={index}");
            }
            TestContext.WriteLine(
                $"C4-D2 sample counts: flooded={positiveFloodingTiles}, "
                + $"freshwater={freshwaterConnectedTiles}, eligible={wetlandEligibleTiles}, "
                + $"largeExtent={largeWetlandExtentTiles}");
            Assert.That(positiveFloodingTiles, Is.GreaterThan(0));
            Assert.That(freshwaterConnectedTiles, Is.GreaterThan(0));
            Assert.That(largeWetlandExtentTiles, Is.GreaterThan(0));

            // 地形・資源は先頭だけでなく全4096タイルを、基礎地形＋同じ完成水文から再計算します。
            // C5-C以降の単一タイル生成はBiome未確定なので、その値を完成Bufferと直接比較してはいけません。
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            for (int index = 0; index < WorldGridConstants.TilesPerChunk; index++)
            {
                int x = 64 + index % WorldGridConstants.LogicalChunkSize;
                int y = -64 + index / WorldGridConstants.LogicalChunkSize;
                GeneratedTile baseTile = DeterministicChunkGenerator.GenerateTile(settings, x, y);
                Assert.That(
                    DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                        settings,
                        biomeSettings,
                        x,
                        y,
                        baseTile,
                        hydrologyTiles[index],
                        surfaceWater[index],
                        out GeneratedTile expected),
                    Is.True,
                    $"完成水文からC5-C地形を再計算できません。tileIndex={index}");
                AssertTileEqual(expected, tiles[index]);
                AssertResourceEqual(DeterministicResourceGenerator.GenerateResource(
                    settings, resourceSettings, expected, x, y), resources[index], x, y);
            }

            int temporaryLakeTiles = 0;
            int saltLakeTiles = 0;
            int freshwaterLakeTiles = 0;
            int springStreamTiles = 0;
            for (int index = 0; index < surfaceWater.Length; index++)
            {
                // C4-C固有Typeを数え、固定Seedの統合試料として利用できる区分が含まれるか診断します。
                switch (surfaceWater[index].Type)
                {
                    case SurfaceWaterTileType.LakeCandidate:
                        temporaryLakeTiles++;
                        break;
                    case SurfaceWaterTileType.PermanentSaltwaterLake:
                        saltLakeTiles++;
                        break;
                    case SurfaceWaterTileType.PermanentFreshwaterLake:
                        freshwaterLakeTiles++;
                        break;
                    case SurfaceWaterTileType.SpringStream:
                        springStreamTiles++;
                        break;
                }
            }
            TestContext.WriteLine(
                $"C4-C sample counts: temporary={temporaryLakeTiles}, salt={saltLakeTiles}, "
                + $"fresh={freshwaterLakeTiles}, spring={springStreamTiles}");
            Assert.That(
                freshwaterLakeTiles,
                Is.GreaterThan(0),
                $"C4-C sample counts: temporary={temporaryLakeTiles}, salt={saltLakeTiles}, "
                    + $"fresh={freshwaterLakeTiles}, spring={springStreamTiles}");
        }

        /// <summary>
        /// 最終湖面を含む固定Seedのチャンクを製品Jobで生成し、公開水文BufferへC4-Cの区分が保存されることを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_PublishesFinalLakeClassification()
        {
            using World world = new World("WorldGenerationLakeClassificationTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-system-test"),
                WorldGenerationSettings.CurrentGeneratorVersion);

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
            entityManager.AddComponentData(settingsEntity, ResourceGenerationSettings.CreateDefault());

            Entity chunkEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkCoordinateComponent
                {
                    Layer = WorldLayer.Surface,
                    X = 0,
                    Y = -3
                });
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkGenerationStatus
                {
                    Phase = ChunkGenerationPhase.Queued,
                    GeneratedWithVersion = 0
                });
            entityManager.AddComponent<ChunkGenerationRequest>(chunkEntity);
            entityManager.AddBuffer<GeneratedTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedResourceTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedHydrologyCell>(chunkEntity);
            entityManager.AddBuffer<GeneratedSurfaceWaterTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedHydrologyTile>(chunkEntity);

            // 製品と同じ二段階公開を通し、Job完了後にだけ固定水文Bufferを読み取ります。
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            WaitForChunkGeneration(world, system, chunkEntity);

            DynamicBuffer<GeneratedHydrologyCell> hydrology =
                entityManager.GetBuffer<GeneratedHydrologyCell>(chunkEntity);
            int lakeIndex = -1;
            for (int index = 0; index < hydrology.Length; index++)
            {
                // GeneratorVersion更新で固定ノイズ座標が変わっても、完成湖面を公開する契約そのものを検証します。
                if ((hydrology[index].LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0
                    && hydrology[index].LakeClassification
                        != LakeWaterBalanceClassification.None
                    && (hydrology[index].ConnectionFlags & HydrologyConnectionFlags.Lake) != 0)
                {
                    lakeIndex = index;
                    break;
                }
            }

            Assert.That(lakeIndex, Is.GreaterThanOrEqualTo(0),
                "固定Seedの検証チャンクには、最終区分とLake接続を公開する恒久湖面セルが必要です。");
            GeneratedHydrologyCell lakeCell = hydrology[lakeIndex];
            Assert.That(
                (lakeCell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0,
                Is.True,
                "固定Seedの検証セルは、最終区分を公開する湖面セルである必要があります。");
            Assert.That(
                lakeCell.LakeClassification,
                Is.Not.EqualTo(LakeWaterBalanceClassification.None),
                "湖面セルは候補状態ではなく、C4-Cで確定した水収支区分を公開する必要があります。");
            Assert.That(
                (lakeCell.ConnectionFlags & HydrologyConnectionFlags.Lake) != 0,
                Is.True,
                "越流淡水湖は最終区分とLake接続を同時に公開する必要があります。");
        }

        /// <summary>
        /// C4-B2だけが拒否する不正比重を準備し、先行Jobが作業配列を生成しても5種類のECS Bufferを部分公開しないことを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_GroundwaterFailureDoesNotPublishAnyBuffer()
        {
            using World world = new World("ChunkGenerationGroundwaterFailureTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-groundwater-failure-test"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            // 合計6という確定契約だけを崩し、地形・水文・表流水までは有効な入力として生成できる状態にします。
            settings.Groundwater.SaturationWeightTotal = 5;

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
            entityManager.AddComponentData(settingsEntity, ResourceGenerationSettings.CreateDefault());
            Entity chunkEntity = CreateChunkEntity(
                entityManager,
                1,
                -1,
                ChunkGenerationPhase.Queued,
                true);
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();

            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.GeneratingData));

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase
                == ChunkGenerationPhase.GeneratingData)
            {
                Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(30), "地下水失敗結果が有限時間で確定しませんでした。");
                system.Update(world.Unmanaged);
                // 実製品の描画フレームと同様にWorkerへ実行時間を渡し、待機側のCPU占有を避けます。
                System.Threading.Thread.Yield();
            }

            ChunkGenerationStatus status =
                entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
            Assert.That(status.Phase, Is.EqualTo(ChunkGenerationPhase.Failed));
            Assert.That(status.GeneratedWithVersion, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyCell>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedSurfaceWaterTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyTile>(chunkEntity).Length, Is.Zero);
        }

        /// <summary>
        /// C4-Cだけが拒否する不正な湖水収支比重を準備し、地下水まで完成しても5種類のECS Bufferを部分公開しないことを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_LakeSpringFailureDoesNotPublishAnyBuffer()
        {
            using World world = new World("ChunkGenerationLakeSpringFailureTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-lake-spring-failure-test"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            // 湖水収支の比重合計4だけを崩し、C4-B2までの全入力は有効な状態を維持します。
            settings.LakeSpring.LakeWeightTotal = 3;

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
            entityManager.AddComponentData(settingsEntity, ResourceGenerationSettings.CreateDefault());
            Entity chunkEntity = CreateChunkEntity(
                entityManager,
                1,
                -1,
                ChunkGenerationPhase.Queued,
                true);
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();

            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.GeneratingData));

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase
                == ChunkGenerationPhase.GeneratingData)
            {
                Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(30), "湖・泉失敗結果が有限時間で確定しませんでした。");
                system.Update(world.Unmanaged);
                // 製品の描画フレームと同様にWorkerへ実行時間を渡し、待機側のCPU占有を避けます。
                System.Threading.Thread.Yield();
            }

            ChunkGenerationStatus status =
                entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
            Assert.That(status.Phase, Is.EqualTo(ChunkGenerationPhase.Failed));
            Assert.That(status.GeneratedWithVersion, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyCell>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedSurfaceWaterTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyTile>(chunkEntity).Length, Is.Zero);
        }

        /// <summary>
        /// C4-D2だけが拒否する不正な冠水比重を準備し、湖・泉まで完成しても5種類のECS Bufferを部分公開しないことを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_NaturalFloodingFailureDoesNotPublishAnyBuffer()
        {
            using World world = new World("ChunkGenerationNaturalFloodingFailureTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-natural-flooding-failure-test"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            // 合計6というC4-D1/D2契約だけを崩し、C4-Cまでの全入力は有効な状態を維持します。
            settings.NaturalFlooding.WeightTotal = 5;

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
            entityManager.AddComponentData(settingsEntity, ResourceGenerationSettings.CreateDefault());
            Entity chunkEntity = CreateChunkEntity(
                entityManager,
                1,
                -1,
                ChunkGenerationPhase.Queued,
                true);
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();

            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.GeneratingData));

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity).Phase
                == ChunkGenerationPhase.GeneratingData)
            {
                Assert.That(
                    timer.Elapsed.TotalSeconds,
                    Is.LessThan(30),
                    "自然冠水失敗結果が有限時間で確定しませんでした。");
                system.Update(world.Unmanaged);
                // 実製品の描画フレームと同様にWorkerへ実行時間を渡し、待機側のCPU占有を避けます。
                System.Threading.Thread.Yield();
            }

            ChunkGenerationStatus status =
                entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
            Assert.That(status.Phase, Is.EqualTo(ChunkGenerationPhase.Failed));
            Assert.That(status.GeneratedWithVersion, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyCell>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedSurfaceWaterTile>(chunkEntity).Length, Is.Zero);
            Assert.That(entityManager.GetBuffer<GeneratedHydrologyTile>(chunkEntity).Length, Is.Zero);
        }

        /// <summary>
        /// 左右に隣接する2チャンクを同じバッチで生成し、境界をまたぐ同一水文セルの広域湿原判定が一致することを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_WetlandExtentMatchesAcrossChunkBoundary()
        {
            using World world = new World("ChunkGenerationWetlandBoundaryTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-system-test"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
            entityManager.AddComponentData(settingsEntity, ResourceGenerationSettings.CreateDefault());
            Entity left = CreateChunkEntity(
                entityManager,
                0,
                -1,
                ChunkGenerationPhase.Queued,
                true);
            Entity right = CreateChunkEntity(
                entityManager,
                1,
                -1,
                ChunkGenerationPhase.Queued,
                true);
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();

            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (entityManager.GetComponentData<ChunkGenerationStatus>(left).Phase
                    == ChunkGenerationPhase.GeneratingData
                || entityManager.GetComponentData<ChunkGenerationStatus>(right).Phase
                    == ChunkGenerationPhase.GeneratingData)
            {
                Assert.That(
                    timer.Elapsed.TotalSeconds,
                    Is.LessThan(30),
                    "湿原境界を含む2チャンク生成が有限時間で確定しませんでした。");
                system.Update(world.Unmanaged);
                System.Threading.Thread.Yield();
            }

            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(left).Phase,
                Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(right).Phase,
                Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
            DynamicBuffer<GeneratedHydrologyTile> leftTiles =
                entityManager.GetBuffer<GeneratedHydrologyTile>(left);
            DynamicBuffer<GeneratedHydrologyTile> rightTiles =
                entityManager.GetBuffer<GeneratedHydrologyTile>(right);
            int matchingLargeExtentRows = 0;
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                bool leftLarge = (leftTiles[WorldGrid.ToTileIndex(63, localY)].Flags
                    & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0;
                bool rightLarge = (rightTiles[WorldGrid.ToTileIndex(0, localY)].Flags
                    & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0;
                Assert.That(
                    rightLarge,
                    Is.EqualTo(leftLarge),
                    $"同じ境界水文セルの広域湿原bitが不一致です。localY={localY}");
                if (leftLarge)
                {
                    matchingLargeExtentRows++;
                }
            }

            Assert.That(
                matchingLargeExtentRows,
                Is.GreaterThan(0),
                "固定Seedが境界上の広域湿原判定を実際に通る試料である必要があります。");
        }

        /// <summary>上限1と2件の要求を準備し、Job開始と後続公開を経ても同時に2件を投入しないことを保証します。</summary>
        [Test]
        public void ChunkGenerationSystem_RespectsConcurrentLimitAndReusesSlot()
        {
            using World world = new World("ChunkGenerationSlotLimitTests");
            EntityManager manager = world.EntityManager;
            ChunkStreamingSettings streaming = ChunkStreamingSettings.CreateDefault();
            streaming.MaximumConcurrentGenerations = 1;
            CreateStreamingSingletons(manager, default, streaming);
            Entity first = CreateChunkEntity(manager, -2, 3, ChunkGenerationPhase.Queued, true);
            Entity second = CreateChunkEntity(manager, 4, -5, ChunkGenerationPhase.Queued, true);
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
            system.Update(world.Unmanaged);
            ChunkGenerationPhase firstPhase = manager.GetComponentData<ChunkGenerationStatus>(first).Phase;
            ChunkGenerationPhase secondPhase = manager.GetComponentData<ChunkGenerationStatus>(second).Phase;
            Assert.That((firstPhase == ChunkGenerationPhase.GeneratingData ? 1 : 0)
                + (secondPhase == ChunkGenerationPhase.GeneratingData ? 1 : 0), Is.EqualTo(1));
            Assert.That((firstPhase == ChunkGenerationPhase.Queued ? 1 : 0)
                + (secondPhase == ChunkGenerationPhase.Queued ? 1 : 0), Is.EqualTo(1));

            // EntityのQuery順には依存せず、どちらも同じ1枠を順に使って完成することを確認します。
            WaitForChunkGeneration(world, system, first);
            WaitForChunkGeneration(world, system, second);
            Assert.That(manager.GetBuffer<GeneratedTile>(first).Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(manager.GetBuffer<GeneratedTile>(second).Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            using EntityQuery settingsQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            WorldGenerationSettings settings = settingsQuery.GetSingleton<WorldGenerationSettings>();
            AssertFinalTileRetainsBaseEnvironment(
                DeterministicChunkGenerator.GenerateTile(settings, 256, -320),
                manager.GetBuffer<GeneratedTile>(second)[0]);
        }

        /// <summary>投入先Entityを破棄して別Entityを作り、古い結果を誤公開せず次の要求を処理できることを保証します。</summary>
        [Test]
        public void ChunkGenerationSystem_DestroyedTargetDoesNotPublishToReplacement()
        {
            using World world = new World("ChunkGenerationDiscardTests");
            EntityManager manager = world.EntityManager;
            ChunkStreamingSettings streaming = ChunkStreamingSettings.CreateDefault();
            streaming.MaximumConcurrentGenerations = 1;
            CreateStreamingSingletons(manager, default, streaming);
            Entity old = CreateChunkEntity(manager, -2, -3, ChunkGenerationPhase.Queued, true);
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
            system.Update(world.Unmanaged);
            manager.DestroyEntity(old);
            Entity replacement = CreateChunkEntity(manager, 4, 5, ChunkGenerationPhase.Queued, true);
            WaitForChunkGeneration(world, system, replacement);
            Assert.That(manager.Exists(old), Is.False);
            using EntityQuery settingsQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            WorldGenerationSettings settings = settingsQuery.GetSingleton<WorldGenerationSettings>();
            AssertFinalTileRetainsBaseEnvironment(
                DeterministicChunkGenerator.GenerateTile(settings, 256, 320),
                manager.GetBuffer<GeneratedTile>(replacement)[0]);
        }

        /// <summary>Job投入直後にWorldを破棄し、未完成Jobの完了とNative配列の解放が安全に行えることを保証します。</summary>
        [Test]
        public void ChunkGenerationSystem_WorldDisposeCompletesOwnedJobs()
        {
            World world = new World("ChunkGenerationDisposeTests");
            try
            {
                CreateStreamingSingletons(world.EntityManager, default, ChunkStreamingSettings.CreateDefault());
                CreateChunkEntity(world.EntityManager, -1, 0, ChunkGenerationPhase.Queued, true);
                SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
                system.Update(world.Unmanaged);
                Assert.DoesNotThrow(() => world.Dispose());
                Assert.That(world.IsCreated, Is.False);
            }
            finally
            {
                // Assert途中で失敗しても、テスト自身がWorldや所有Native配列を残さないようにします。
                if (world.IsCreated) world.Dispose();
            }
        }

        /// <summary>保持外へ出た生成中Entityを準備し、完了までは枠を予約し、完了後にアンロードすることを保証します。</summary>
        [Test]
        public void ChunkStreamingSystem_RetainsRunningGenerationUntilCompletion()
        {
            using World world = new World("ChunkStreamingRunningRetentionTests");
            EntityManager manager = world.EntityManager;
            ChunkStreamingSettings settings = ChunkStreamingSettings.CreateDefault();
            settings.MaximumConcurrentGenerations = 1;
            CreateStreamingSingletons(manager, default, settings);
            Entity outside = CreateChunkEntity(manager, 100, -100, ChunkGenerationPhase.GeneratingData, false);
            SystemHandle streaming = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streaming.Update(world.Unmanaged);
            using EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<ChunkGenerationStatus>());
            Assert.That(manager.Exists(outside), Is.True);
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1), "未完成の枠を予約し、新しい要求を増やさない必要があります。");

            // 生成Systemによる完成公開を模擬し、次回の寿命更新では旧Entityが消え、現在地の要求へ枠を渡します。
            manager.SetComponentData(outside, new ChunkGenerationStatus { Phase = ChunkGenerationPhase.ReadyToApply });
            streaming.Update(world.Unmanaged);
            Assert.That(manager.Exists(outside), Is.False);
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(query.GetSingleton<ChunkGenerationStatus>().Phase, Is.EqualTo(ChunkGenerationPhase.Queued));
        }

        /// <summary>テストだけで後続更新を進め、Workerの完成結果がECSへ公開されるまで有限時間待ちます。</summary>
        /// <param name="world">対象のテスト用Worldです。</param>
        /// <param name="system">公開と投入を担当する生成Systemです。</param>
        /// <param name="entity">ReadyToApplyになることを期待するEntityです。</param>
        private static void WaitForChunkGeneration(World world, SystemHandle system, Entity entity)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (world.EntityManager.GetComponentData<ChunkGenerationStatus>(entity).Phase != ChunkGenerationPhase.ReadyToApply)
            {
                Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(30), "生成結果が後続更新で公開されませんでした。");
                system.Update(world.Unmanaged);
                // 実製品では描画フレームがWorkerへ時間を渡します。同期テストではCPU占有を避けて明示的に譲ります。
                System.Threading.Thread.Yield();
            }
        }

        /// <summary>
        /// 原点だけが見えるObserverと同時生成上限3を準備し、中心の次に左右隣接を角より優先することを保証します。
        /// C4-D2の生成時間中に通常移動が境界へ達しても、左下・左上角を先に待って水平隣接が空白になる回帰を防ぎます。
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
                    Layer = WorldLayer.Surface,
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
            Assert.That(ContainsChunkCoordinate(chunkQuery, -1, 0), Is.True);
            Assert.That(ContainsChunkCoordinate(chunkQuery, 1, 0), Is.True);
            Assert.That(ContainsChunkCoordinate(chunkQuery, -1, -1), Is.False);
            Assert.That(ContainsChunkCoordinate(chunkQuery, -1, 1), Is.False);

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
                    Layer = WorldLayer.Surface,
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
                    Layer = WorldLayer.Surface,
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
                    Layer = WorldLayer.Surface,
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
                    Layer = WorldLayer.Surface,
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
                    Layer = WorldLayer.Surface,
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
        /// 同じX/Yに海中チャンクが存在する状態で地上Observerを更新し、海中を地上の既存要求と誤認せず地上要求へ置き換えることを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_SameXyDifferentLayer_DoesNotReuseWrongLayer()
        {
            using World world = new World("Layer-aware chunk streaming test");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    Layer = WorldLayer.Surface,
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 0,
                    RetentionMarginInChunks = 0,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity underwater = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Active,
                requestEnabled: false,
                layer: WorldLayer.Underwater);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(underwater), Is.False);
            using EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>());
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(1));
            ChunkCoordinateComponent actual = chunkQuery.GetSingleton<ChunkCoordinateComponent>();
            Assert.That(actual.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(actual.X, Is.Zero);
            Assert.That(actual.Y, Is.Zero);
        }

        /// <summary>
        /// WORLD-01Cより前と同じ未設定Layerと逆順の矩形を準備し、Streaming更新後にSurfaceの正規化結果が
        /// Singletonへ保存されることを保証します。Presenterが未補正のNoneを読んで地上表示を除外する退行を防ぎます。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_DefaultObserver_PersistsNormalizedSurfaceBounds()
        {
            using World world = new World("Default observer normalization test");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    Layer = WorldLayer.None,
                    MinimumVisibleChunkX = 3,
                    MaximumVisibleChunkX = -2,
                    MinimumVisibleChunkY = 5,
                    MaximumVisibleChunkY = -4
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 0,
                    RetentionMarginInChunks = 0,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            using EntityQuery observerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkStreamingObserver>());
            ChunkStreamingObserver actual = observerQuery.GetSingleton<ChunkStreamingObserver>();
            Assert.That(actual.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(actual.MinimumVisibleChunkX, Is.EqualTo(-2));
            Assert.That(actual.MaximumVisibleChunkX, Is.EqualTo(3));
            Assert.That(actual.MinimumVisibleChunkY, Is.EqualTo(-4));
            Assert.That(actual.MaximumVisibleChunkY, Is.EqualTo(5));
        }

        /// <summary>
        /// Surfaceから未生成のUnderwaterへ表示を切り替え、完了済みSurfaceチャンクを直ちに解放し、
        /// 実行中扱いのチャンクも完了後の次回更新で解放することを保証します。
        /// Underwater用の地上Generator要求を誤って作らないことも同時に検証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_UnsupportedLayer_ReleasesOldChunksWithoutQueuingReplacement()
        {
            using World world = new World("Unsupported layer cleanup test");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    Layer = WorldLayer.Underwater,
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 0,
                    RetentionMarginInChunks = 0,
                    MaximumConcurrentGenerations = 2,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity completedSurface = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Active,
                requestEnabled: false);
            Entity runningSurface = CreateChunkEntity(
                entityManager,
                1,
                0,
                ChunkGenerationPhase.GeneratingData,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(completedSurface), Is.False);
            Assert.That(entityManager.Exists(runningSurface), Is.True,
                "生成Jobの所有配列が完了するまでは対象Entityを残す必要があります。");
            using EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>());
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(1),
                "未生成のUnderwaterへ新しいチャンク要求を作ってはいけません。");

            // 実製品ではChunkGenerationSystemが結果公開時にReadyToApplyへ進めます。
            // テストではその境界だけを再現し、次回の寿命更新で残存Surfaceが解放されることを確認します。
            entityManager.SetComponentData(
                runningSurface,
                new ChunkGenerationStatus { Phase = ChunkGenerationPhase.ReadyToApply });
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(runningSurface), Is.False);
            Assert.That(chunkQuery.CalculateEntityCount(), Is.Zero);
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
                WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString("streaming-test"), WorldGenerationSettings.CurrentGeneratorVersion));
            entityManager.AddComponentData(settingsEntity, BiomeDefinitionCatalog.CreateDefault());
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
        /// <param name="layer">チャンクが属するワールド層です。省略時は既存地形と同じSurfaceです。</param>
        /// <returns>作成したテスト用チャンクEntityです。</returns>
        private static Entity CreateChunkEntity(
            EntityManager entityManager,
            int x,
            int y,
            ChunkGenerationPhase phase,
            bool requestEnabled,
            WorldLayer layer = WorldLayer.Surface)
        {
            Entity entity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                entity,
                new ChunkCoordinateComponent
                {
                    Layer = layer,
                    X = x,
                    Y = y
                });
            entityManager.AddComponentData(entity, new ChunkGenerationStatus
            {
                Phase = phase,
                GeneratedWithVersion = phase == ChunkGenerationPhase.Queued
                    ? 0
                    : WorldGenerationSettings.CurrentGeneratorVersion
            });
            entityManager.AddComponent<ChunkGenerationRequest>(entity);
            entityManager.SetComponentEnabled<ChunkGenerationRequest>(entity, requestEnabled);
            entityManager.AddBuffer<GeneratedTile>(entity);
            entityManager.AddBuffer<GeneratedResourceTile>(entity);
            entityManager.AddBuffer<GeneratedHydrologyCell>(entity);
            entityManager.AddBuffer<GeneratedSurfaceWaterTile>(entity);
            entityManager.AddBuffer<GeneratedHydrologyTile>(entity);
            return entity;
        }

        /// <summary>
        /// 初期スポーンSystemが完成済みチャンクとして走査できるよう、地形・資源Bufferを4096件の空値で初期化します。
        /// </summary>
        /// <param name="entityManager">対象チャンクのBufferを書き込むテストWorldのManagerです。</param>
        /// <param name="chunkEntity">完成状態として作成済みのテスト用チャンクEntityです。</param>
        private static void InitializeCompletedChunkBuffers(
            EntityManager entityManager,
            Entity chunkEntity)
        {
            DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(chunkEntity);
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity);
            tiles.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            resources.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            for (int index = 0; index < WorldGridConstants.TilesPerChunk; index++)
            {
                // 既定値は移動不能かつ資源なしなので、明示した候補以外が選ばれるのを防ぎます。
                tiles[index] = default;
                resources[index] = default;
            }
        }

        /// <summary>
        /// 完成済みテストチャンクの1セルを、資源なし・乾いた許可Biomeの初期スポーン候補へ変更します。
        /// </summary>
        /// <param name="entityManager">対象地形Bufferを書き込むテストWorldのManagerです。</param>
        /// <param name="chunkEntity">候補を持たせる完成済みチャンクEntityです。</param>
        /// <param name="localX">チャンク内の水平座標です。範囲は0〜63です。</param>
        /// <param name="localY">チャンク内の垂直座標です。範囲は0〜63です。</param>
        private static void SetInitialSpawnCandidate(
            EntityManager entityManager,
            Entity chunkEntity,
            int localX,
            int localY)
        {
            int index = (localY * WorldGridConstants.LogicalChunkSize) + localX;
            DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(chunkEntity);
            tiles[index] = new GeneratedTile
            {
                Biome = BiomeId.TemperateGrassland,
                Ground = GroundType.Land,
                WaterSalinity = WaterSalinity.None,
                MovementCost = 1
            };
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
            Assert.That(actual.VolcanicActivityPotential, Is.EqualTo(expected.VolcanicActivityPotential));
            Assert.That(actual.TerrainTemperature, Is.EqualTo(expected.TerrainTemperature));
            Assert.That(actual.WoodyVegetationPotential, Is.EqualTo(expected.WoodyVegetationPotential));
            Assert.That(actual.TerrainRuggedness, Is.EqualTo(expected.TerrainRuggedness));
            Assert.That(actual.CoastalInfluence, Is.EqualTo(expected.CoastalInfluence));
            Assert.That(actual.VolcanicActivityPotential, Is.EqualTo(expected.VolcanicActivityPotential));
            Assert.That(actual.GeologyClass, Is.EqualTo(expected.GeologyClass));
            Assert.That(actual.GeologyFlags, Is.EqualTo(expected.GeologyFlags));
            Assert.That(actual.GeologyPermeability, Is.EqualTo(expected.GeologyPermeability));
            Assert.That(actual.GeologyGroundwaterStorage, Is.EqualTo(expected.GeologyGroundwaterStorage));
            Assert.That(actual.GeologyErosionResistance, Is.EqualTo(expected.GeologyErosionResistance));
        }

        /// <summary>
        /// Biome分類前の基礎タイルとECS公開後の最終タイルを比べ、C5-Cが環境・地質を変更せずBiomeと地面だけを完成させることを保証します。
        /// </summary>
        /// <param name="baseTile">水文とBiomeの確定前に生成した基礎タイルです。</param>
        /// <param name="finalTile">ChunkGenerationSystemが水文完成後に公開した最終タイルです。</param>
        private static void AssertFinalTileRetainsBaseEnvironment(
            GeneratedTile baseTile,
            GeneratedTile finalTile)
        {
            Assert.That(finalTile.Elevation, Is.EqualTo(baseTile.Elevation));
            Assert.That(finalTile.Temperature, Is.EqualTo(baseTile.Temperature));
            Assert.That(finalTile.Moisture, Is.EqualTo(baseTile.Moisture));
            Assert.That(finalTile.TerrainTemperature, Is.EqualTo(baseTile.TerrainTemperature));
            Assert.That(
                finalTile.WoodyVegetationPotential,
                Is.EqualTo(baseTile.WoodyVegetationPotential));
            Assert.That(finalTile.TerrainRuggedness, Is.EqualTo(baseTile.TerrainRuggedness));
            Assert.That(finalTile.CoastalInfluence, Is.EqualTo(baseTile.CoastalInfluence));
            Assert.That(
                finalTile.VolcanicActivityPotential,
                Is.EqualTo(baseTile.VolcanicActivityPotential));
            Assert.That(finalTile.GeologyClass, Is.EqualTo(baseTile.GeologyClass));
            Assert.That(finalTile.GeologyFlags, Is.EqualTo(baseTile.GeologyFlags));
            Assert.That(finalTile.GeologyPermeability, Is.EqualTo(baseTile.GeologyPermeability));
            Assert.That(
                finalTile.GeologyGroundwaterStorage,
                Is.EqualTo(baseTile.GeologyGroundwaterStorage));
            Assert.That(
                finalTile.GeologyErosionResistance,
                Is.EqualTo(baseTile.GeologyErosionResistance));

            if (finalTile.WaterBodyType == WaterBodyType.Sea)
            {
                Assert.That(finalTile.Biome, Is.EqualTo(BiomeId.None));
            }
            else
            {
                Assert.That((int)finalTile.Biome, Is.InRange(BiomeStableIds.First, BiomeStableIds.Last));
            }
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
