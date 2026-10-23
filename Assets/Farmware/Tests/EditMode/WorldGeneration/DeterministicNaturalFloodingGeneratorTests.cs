// このファイルは、TERRAIN-01C4-D1の恒久淡水源選択と自然冠水の固定整数式を検証します。
// 人工水域や小規模な泉を混ぜず、距離・比高・閾値・同点順が将来のECS接続でも変わらないことを保証します。

using Farmware.WorldGeneration.Authoring;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 自然冠水設定、水源探索、整数平方根、4入力合成の境界値を保証するEditModeテストです。
    /// C4-D2のChunk Jobはここで確定した純粋計算を呼び、別の近似式を持たないようにします。
    /// </summary>
    public sealed class DeterministicNaturalFloodingGeneratorTests
    {
        /// <summary>既定設定を準備し、仕様で確定した全定数と厳格検証が一致することを保証します。</summary>
        [Test]
        public void DefaultSettings_MatchConfirmedContract()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();

            Assert.That(DeterministicNaturalFloodingGenerator.ValidateSettings(settings), Is.True);
            Assert.That(settings.SearchRadiusInTiles, Is.EqualTo(32));
            Assert.That(settings.MaximumHeightAboveWater, Is.EqualTo(4096));
            Assert.That(settings.RunoffMagnitudeMultiplier, Is.EqualTo(512));
            Assert.That(settings.WaterProximityWeight, Is.EqualTo(2));
            Assert.That(settings.RelativeLowlandWeight, Is.EqualTo(2));
            Assert.That(settings.FlatnessWeight, Is.EqualTo(1));
            Assert.That(settings.SourceMagnitudeWeight, Is.EqualTo(1));
            Assert.That(settings.WeightTotal, Is.EqualTo(6));
            Assert.That(settings.NaturalFloodingThreshold, Is.EqualTo(39321));
            Assert.That(settings.WetlandMinimumElevation, Is.EqualTo(20000));
            Assert.That(settings.WetlandMaximumElevation, Is.EqualTo(36000));
            Assert.That(settings.MinimumEligibleTilesPerHydrologyCell, Is.EqualTo(16));
            Assert.That(settings.MinimumConnectedHydrologyCells, Is.EqualTo(4));
        }

        /// <summary>World既定設定を準備し、自然冠水設定がGeneratorVersion対象の値として初期化されることを保証します。</summary>
        [Test]
        public void WorldGenerationDefaults_ContainNaturalFloodingSettings()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(123UL, WorldGenerationSettings.CurrentGeneratorVersion);

            Assert.That(
                DeterministicNaturalFloodingGenerator.ValidateSettings(settings.NaturalFlooding),
                Is.True);
        }

        /// <summary>
        /// 新規AuthoringからBake用Snapshotを作り、実ゲーム側にも共有の自然冠水設定が渡ることを保証します。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_ContainsNaturalFloodingSettings()
        {
            GameObject authoringObject = new GameObject("Natural Flooding Authoring Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot();

                Assert.That(
                    DeterministicNaturalFloodingGenerator.ValidateSettings(
                        snapshot.NaturalFlooding),
                    Is.True);
            }
            finally
            {
                // EditModeでは通常のDestroyがフレーム終端まで遅延するため、テストが所有する一時GameObjectを即時破棄します。
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>各確定値を一つだけ変更し、暗黙補正せず不正設定として拒否することを保証します。</summary>
        /// <param name="caseIndex">変更する設定項目を表す0〜12の番号です。</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        public void ChangedContractValue_IsRejected(int caseIndex)
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            switch (caseIndex)
            {
                case 0: settings.SearchRadiusInTiles = 31; break;
                case 1: settings.MaximumHeightAboveWater = 4095; break;
                case 2: settings.RunoffMagnitudeMultiplier = 511; break;
                case 3: settings.WaterProximityWeight = 1; break;
                case 4: settings.RelativeLowlandWeight = 1; break;
                case 5: settings.FlatnessWeight = 2; break;
                case 6: settings.SourceMagnitudeWeight = 2; break;
                case 7: settings.WeightTotal = 5; break;
                case 8: settings.NaturalFloodingThreshold = 39320; break;
                case 9: settings.WetlandMinimumElevation = 19999; break;
                case 10: settings.WetlandMaximumElevation = 36001; break;
                case 11: settings.MinimumEligibleTilesPerHydrologyCell = 15; break;
                default: settings.MinimumConnectedHydrologyCells = 3; break;
            }

            Assert.That(DeterministicNaturalFloodingGenerator.ValidateSettings(settings), Is.False);
        }

        /// <summary>平方数とその直前直後を準備し、整数平方根が常に床方向へ丸められることを保証します。</summary>
        /// <param name="value">平方根を求める入力です。</param>
        /// <param name="expected">期待する床方向の整数平方根です。</param>
        [TestCase(0u, 0u)]
        [TestCase(1u, 1u)]
        [TestCase(2u, 1u)]
        [TestCase(3u, 1u)]
        [TestCase(4u, 2u)]
        [TestCase(1023u, 31u)]
        [TestCase(1024u, 32u)]
        [TestCase(1025u, 32u)]
        [TestCase(uint.MaxValue, 65535u)]
        public void IntegerSquareRoot_UsesFloorRounding(uint value, uint expected)
        {
            Assert.That(
                DeterministicNaturalFloodingGenerator.IntegerSquareRoot(value),
                Is.EqualTo(expected));
        }

        /// <summary>距離0・比高0・平坦・最大規模の水源を準備し、自然冠水度の最大端点を保証します。</summary>
        [Test]
        public void FloodingPotential_AllMaximumInputs_ReturnsMaximum()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            NaturalFloodingSource source = CreateSource(
                SurfaceWaterTileType.River,
                distance: 0,
                surfaceHeight: 12000,
                magnitude: ushort.MaxValue);

            bool calculated =
                DeterministicNaturalFloodingGenerator.TryCalculateNaturalFloodingPotential(
                    settings,
                    targetElevation: 12000,
                    terrainRuggedness: 0,
                    source,
                    out NaturalFloodingResult result);

            Assert.That(calculated, Is.True);
            Assert.That(result.HasSource, Is.EqualTo(1));
            Assert.That(result.WaterProximity, Is.EqualTo(ushort.MaxValue));
            Assert.That(result.RelativeLowland, Is.EqualTo(ushort.MaxValue));
            Assert.That(result.Flatness, Is.EqualTo(ushort.MaxValue));
            Assert.That(result.SourceMagnitude, Is.EqualTo(ushort.MaxValue));
            Assert.That(result.Potential, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>距離32・比高4,096・最大起伏・規模0を準備し、自然冠水度の最小端点を保証します。</summary>
        [Test]
        public void FloodingPotential_AllMinimumInputs_ReturnsZero()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            NaturalFloodingSource source = CreateSource(
                SurfaceWaterTileType.Stream,
                distance: 32,
                surfaceHeight: 10000,
                magnitude: 0);

            bool calculated =
                DeterministicNaturalFloodingGenerator.TryCalculateNaturalFloodingPotential(
                    settings,
                    targetElevation: 14096,
                    terrainRuggedness: ushort.MaxValue,
                    source,
                    out NaturalFloodingResult result);

            Assert.That(calculated, Is.True);
            Assert.That(result.WaterProximity, Is.Zero);
            Assert.That(result.RelativeLowland, Is.Zero);
            Assert.That(result.Flatness, Is.Zero);
            Assert.That(result.SourceMagnitude, Is.Zero);
            Assert.That(result.Potential, Is.Zero);
        }

        /// <summary>
        /// 閾値の直前と閾値そのものになる入力を準備し、整数除算後の自然冠水判定境界を保証します。
        /// </summary>
        /// <param name="terrainRuggedness">平坦度を境界値へ合わせる地形起伏です。</param>
        /// <param name="sourceMagnitude">合計値を6で割った結果へ合わせる水域規模です。</param>
        /// <param name="expectedPotential">整数除算後に期待する自然冠水度です。</param>
        [TestCase(13110, 52425, 39320)]
        [TestCase(13107, 52428, 39321)]
        public void FloodingPotential_ThresholdBoundary_UsesIntegerResult(
            int terrainRuggedness,
            int sourceMagnitude,
            int expectedPotential)
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            NaturalFloodingSource source = CreateSource(
                SurfaceWaterTileType.PermanentFreshwaterLake,
                distance: 32,
                surfaceHeight: 20000,
                magnitude: (ushort)sourceMagnitude);

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryCalculateNaturalFloodingPotential(
                    settings,
                    targetElevation: 20000,
                    terrainRuggedness: (ushort)terrainRuggedness,
                    source,
                    out NaturalFloodingResult result),
                Is.True);
            Assert.That(result.Potential, Is.EqualTo(expectedPotential));
            Assert.That(
                result.Potential >= settings.NaturalFloodingThreshold,
                Is.EqualTo(expectedPotential == settings.NaturalFloodingThreshold));
        }

        /// <summary>水面より低い対象と比高上限超過を準備し、相対低地度が両端へ飽和することを保証します。</summary>
        [Test]
        public void RelativeLowland_ClampsBelowWaterAndAboveMaximumHeight()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            NaturalFloodingSource source = CreateSource(
                SurfaceWaterTileType.PermanentFreshwaterLake,
                distance: 16,
                surfaceHeight: 20000,
                magnitude: 32768);

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryCalculateNaturalFloodingPotential(
                    settings,
                    targetElevation: 19000,
                    terrainRuggedness: 32768,
                    source,
                    out NaturalFloodingResult belowWater),
                Is.True);
            Assert.That(belowWater.RelativeLowland, Is.EqualTo(ushort.MaxValue));

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryCalculateNaturalFloodingPotential(
                    settings,
                    targetElevation: 25000,
                    terrainRuggedness: 32768,
                    source,
                    out NaturalFloodingResult aboveMaximum),
                Is.True);
            Assert.That(aboveMaximum.RelativeLowland, Is.Zero);
        }

        /// <summary>空の水域を準備し、水源なしが失敗ではなくPotential 0の正常結果になることを保証します。</summary>
        [Test]
        public void MissingSource_ReturnsExplicitZeroResult()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(1, Allocator.Temp);

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                    settings,
                    0,
                    0,
                    0,
                    0,
                    1,
                    1,
                    water,
                    out NaturalFloodingSource source),
                Is.True);
            Assert.That(source.IsValid, Is.Zero);
            Assert.That(
                DeterministicNaturalFloodingGenerator.TryCalculateNaturalFloodingPotential(
                    settings,
                    targetElevation: 30000,
                    terrainRuggedness: 0,
                    source,
                    out NaturalFloodingResult result),
                Is.True);
            Assert.That(result.HasSource, Is.Zero);
            Assert.That(result.Potential, Is.Zero);
        }

        /// <summary>半径32の内外へ水源を置き、円周上だけを候補に含めることを保証します。</summary>
        [Test]
        public void SourceSearch_IncludesRadius32AndExcludesRadius33()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            const int Width = 67;
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(Width, Allocator.Temp);
            SetWater(water, Width, localX: 65, localY: 0,
                CreateWaterTile(SurfaceWaterTileType.River, sourceAmount: 1));
            SetWater(water, Width, localX: 66, localY: 0,
                CreateWaterTile(SurfaceWaterTileType.River, sourceAmount: 127));

            bool found = DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                settings,
                targetWorldTileX: 0,
                targetWorldTileY: 0,
                waterAreaMinimumX: -33,
                waterAreaMinimumY: 0,
                waterAreaWidth: Width,
                waterAreaHeight: 1,
                water,
                out NaturalFloodingSource source);

            Assert.That(found, Is.True);
            Assert.That(source.IsValid, Is.EqualTo(1));
            Assert.That(source.WorldTileX, Is.EqualTo(32));
            Assert.That(source.DistanceInTiles, Is.EqualTo(32));
            Assert.That(source.Magnitude, Is.EqualTo(512));
        }

        /// <summary>同じ整数距離の複数水源を準備し、規模を座標より先に比較することを保証します。</summary>
        [Test]
        public void SourceSearch_SameFloorDistance_PrefersGreaterMagnitude()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            const int Width = 5;
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(Width * Width, Allocator.Temp);
            SetWater(water, Width, localX: 1, localY: 2,
                CreateWaterTile(SurfaceWaterTileType.Stream, sourceAmount: 1));
            SetWater(water, Width, localX: 3, localY: 3,
                CreateWaterTile(SurfaceWaterTileType.PermanentFreshwaterLake, sourceAmount: 60000));

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                    settings,
                    targetWorldTileX: 0,
                    targetWorldTileY: 0,
                    waterAreaMinimumX: -2,
                    waterAreaMinimumY: -2,
                    waterAreaWidth: Width,
                    waterAreaHeight: Width,
                    water,
                    out NaturalFloodingSource source),
                Is.True);
            Assert.That(source.DistanceInTiles, Is.EqualTo(1));
            Assert.That(source.Type, Is.EqualTo(SurfaceWaterTileType.PermanentFreshwaterLake));
            Assert.That(source.WorldTileX, Is.EqualTo(1));
            Assert.That(source.WorldTileY, Is.EqualTo(1));
        }

        /// <summary>距離と規模が同じ水源を負座標へ置き、X、Yの辞書順で一意化することを保証します。</summary>
        [Test]
        public void SourceSearch_FullTie_UsesCoordinateOrder()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            const int Width = 5;
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(Width * Width, Allocator.Temp);
            SetWater(water, Width, localX: 1, localY: 2,
                CreateWaterTile(SurfaceWaterTileType.River, sourceAmount: 4));
            SetWater(water, Width, localX: 2, localY: 1,
                CreateWaterTile(SurfaceWaterTileType.River, sourceAmount: 4));

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                    settings,
                    targetWorldTileX: -10,
                    targetWorldTileY: -10,
                    waterAreaMinimumX: -12,
                    waterAreaMinimumY: -12,
                    waterAreaWidth: Width,
                    waterAreaHeight: Width,
                    water,
                    out NaturalFloodingSource source),
                Is.True);
            Assert.That(source.WorldTileX, Is.EqualTo(-11));
            Assert.That(source.WorldTileY, Is.EqualTo(-10));
        }

        /// <summary>塩湖、一時湖、泉流、フラグ不足を混在させ、恒久淡水3種だけを水源にすることを保証します。</summary>
        [Test]
        public void SourceSearch_ExcludesNonContractWaterTypesAndFlags()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(6, Allocator.Temp);
            SetWater(water, 6, 0, 0,
                CreateWaterTile(SurfaceWaterTileType.PermanentSaltwaterLake, 65535));
            SetWater(water, 6, 1, 0,
                CreateWaterTile(SurfaceWaterTileType.LakeCandidate, 65535));
            SetWater(water, 6, 2, 0,
                CreateWaterTile(SurfaceWaterTileType.SpringStream, 65535));
            SetWater(water, 6, 3, 0, new GeneratedSurfaceWaterTile
                {
                    Type = SurfaceWaterTileType.River,
                    Flags = SurfaceWaterTileFlags.NaturalSurfaceWater,
                    SourceAmount = 65535
                });
            SetWater(water, 6, 4, 0,
                CreateWaterTile(SurfaceWaterTileType.Stream, 2));
            SetWater(water, 6, 5, 0,
                CreateWaterTile(SurfaceWaterTileType.PermanentFreshwaterLake, 1));

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                    settings,
                    targetWorldTileX: 0,
                    targetWorldTileY: 0,
                    waterAreaMinimumX: 0,
                    waterAreaMinimumY: 0,
                    waterAreaWidth: 6,
                    waterAreaHeight: 1,
                    water,
                    out NaturalFloodingSource source),
                Is.True);
            Assert.That(source.WorldTileX, Is.EqualTo(4));
            Assert.That(source.Type, Is.EqualTo(SurfaceWaterTileType.Stream));
        }

        /// <summary>河道流出量の倍率境界と湖面充填率を準備し、種別ごとの水域規模変換を保証します。</summary>
        [Test]
        public void SourceMagnitude_UsesRunoffMultiplierAndLakeFillFraction()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            GeneratedSurfaceWaterTile stream = CreateWaterTile(SurfaceWaterTileType.Stream, 127);
            GeneratedSurfaceWaterTile saturatedRiver = CreateWaterTile(SurfaceWaterTileType.River, 128);
            GeneratedSurfaceWaterTile lake =
                CreateWaterTile(SurfaceWaterTileType.PermanentFreshwaterLake, 50000);

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryGetSourceMagnitude(
                    settings, stream, out ushort streamMagnitude),
                Is.True);
            Assert.That(streamMagnitude, Is.EqualTo(65024));
            Assert.That(
                DeterministicNaturalFloodingGenerator.TryGetSourceMagnitude(
                    settings, saturatedRiver, out ushort riverMagnitude),
                Is.True);
            Assert.That(riverMagnitude, Is.EqualTo(ushort.MaxValue));
            Assert.That(
                DeterministicNaturalFloodingGenerator.TryGetSourceMagnitude(
                    settings, lake, out ushort lakeMagnitude),
                Is.True);
            Assert.That(lakeMagnitude, Is.EqualTo(50000));
        }

        /// <summary>長さ不足の矩形入力を準備し、部分的な探索結果を成功として返さないことを保証します。</summary>
        [Test]
        public void SourceSearch_InvalidBufferLength_FailsWithoutSource()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(3, Allocator.Temp);
            SetWater(water, 3, 0, 0,
                CreateWaterTile(SurfaceWaterTileType.River, 10));

            bool found = DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                settings,
                0,
                0,
                0,
                0,
                waterAreaWidth: 2,
                waterAreaHeight: 2,
                water,
                out NaturalFloodingSource source);

            Assert.That(found, Is.False);
            Assert.That(source.IsValid, Is.Zero);
        }

        /// <summary>
        /// int座標の両端へ探索対象と入力矩形を分離して置き、差分のint変換を行わず候補なしで終了することを保証します。
        /// </summary>
        [Test]
        public void SourceSearch_ExtremeSeparatedCoordinates_ReturnsNoSourceWithoutOverflow()
        {
            NaturalFloodingGenerationSettings settings =
                NaturalFloodingGenerationSettings.CreateDefault();
            using var water = new NativeArray<GeneratedSurfaceWaterTile>(1, Allocator.Temp);
            SetWater(water, 1, 0, 0,
                CreateWaterTile(SurfaceWaterTileType.River, sourceAmount: 1));

            Assert.That(
                DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                    settings,
                    targetWorldTileX: int.MaxValue,
                    targetWorldTileY: int.MaxValue,
                    waterAreaMinimumX: int.MinValue,
                    waterAreaMinimumY: int.MinValue,
                    waterAreaWidth: 1,
                    waterAreaHeight: 1,
                    water,
                    out NaturalFloodingSource source),
                Is.True);
            Assert.That(source.IsValid, Is.Zero);
        }

        /// <summary>テストで使用する有効な水源結果を作り、各計算テストの準備を読みやすくします。</summary>
        /// <param name="type">渓流、河川、恒久淡水湖のいずれかです。</param>
        /// <param name="distance">対象から水源までの整数距離です。</param>
        /// <param name="surfaceHeight">固定水面高です。</param>
        /// <param name="magnitude">計算済み水域規模です。</param>
        /// <returns>TryCalculateNaturalFloodingPotentialへ渡せる有効な水源です。</returns>
        private static NaturalFloodingSource CreateSource(
            SurfaceWaterTileType type,
            byte distance,
            ushort surfaceHeight,
            ushort magnitude)
        {
            return new NaturalFloodingSource
            {
                IsValid = 1,
                Type = type,
                DistanceInTiles = distance,
                SurfaceHeight = surfaceHeight,
                Magnitude = magnitude
            };
        }

        /// <summary>必要な意味フラグを持つ固定表流水を作り、水源種別の差だけをテストへ渡します。</summary>
        /// <param name="type">作成する固定表流水種別です。</param>
        /// <param name="sourceAmount">河道流出寄与または湖面充填率です。</param>
        /// <returns>自然水面かつ淡水接続を持つ表流水です。</returns>
        private static GeneratedSurfaceWaterTile CreateWaterTile(
            SurfaceWaterTileType type,
            ushort sourceAmount)
        {
            return new GeneratedSurfaceWaterTile
            {
                Type = type,
                Flags = SurfaceWaterTileFlags.NaturalSurfaceWater
                    | SurfaceWaterTileFlags.FreshwaterConnected,
                SurfaceHeight = 20000,
                SourceAmount = sourceAmount
            };
        }

        /// <summary>行優先のテスト矩形へ表流水を一件設定し、添字計算を各テストへ散らしません。</summary>
        /// <param name="water">書き込むNativeArrayです。テストメソッドが所有して破棄します。</param>
        /// <param name="width">行優先矩形の横幅です。</param>
        /// <param name="localX">矩形内の水平座標です。</param>
        /// <param name="localY">矩形内の垂直座標です。</param>
        /// <param name="tile">設定する固定表流水です。</param>
        private static void SetWater(
            NativeArray<GeneratedSurfaceWaterTile> water,
            int width,
            int localX,
            int localY,
            GeneratedSurfaceWaterTile tile)
        {
            water[(localY * width) + localX] = tile;
        }
    }
}
