// このファイルは、WORLD-01Bで確定した多層水域の製品用純粋計算契約を検証します。
// 固定保存ID、自然水柱、人工造成、上下境界、排水、移動判定をSceneや表示に依存せず確認します。

using Farmware.Simulation;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// WorldLayerWaterContractとWorldLayerUtilityについて、仕様で合意した入力と結果の対応を保証するEditModeテストです。
    /// WORLD-01Aの試作内列挙値ではなく、後続Runtimeが参照する製品型を直接検証します。
    /// </summary>
    public sealed class WorldLayerWaterContractTests
    {
        /// <summary>
        /// 保存へ書き込む固定3レイヤーIDを確認し、列挙順の変更による既存データの誤読を防ぎます。
        /// </summary>
        [Test]
        public void WorldLayer_SaveIdsRemainStable()
        {
            Assert.That((byte)WorldLayer.None, Is.EqualTo(0));
            Assert.That((byte)WorldLayer.Surface, Is.EqualTo(1));
            Assert.That((byte)WorldLayer.Underwater, Is.EqualTo(2));
            Assert.That((byte)WorldLayer.DeepSea, Is.EqualTo(3));
        }

        /// <summary>
        /// 有効な3層だけを受理し、Noneと未知の保存値を通常レイヤーとして扱わないことを確認します。
        /// </summary>
        [Test]
        public void WorldLayerUtility_IsPlayable_RejectsReservedAndUnknownValues()
        {
            Assert.That(WorldLayerUtility.IsPlayable(WorldLayer.Surface), Is.True);
            Assert.That(WorldLayerUtility.IsPlayable(WorldLayer.Underwater), Is.True);
            Assert.That(WorldLayerUtility.IsPlayable(WorldLayer.DeepSea), Is.True);
            Assert.That(WorldLayerUtility.IsPlayable(WorldLayer.None), Is.False);
            Assert.That(WorldLayerUtility.IsPlayable((WorldLayer)byte.MaxValue), Is.False);
        }

        /// <summary>
        /// 地上と海中、海中と深海だけを上下隣接とし、地上から深海へ中間層を飛ばさないことを確認します。
        /// </summary>
        [Test]
        public void WorldLayerUtility_AreVerticallyAdjacent_AllowsOnlyNeighboringLayers()
        {
            Assert.That(
                WorldLayerUtility.AreVerticallyAdjacent(WorldLayer.Surface, WorldLayer.Underwater),
                Is.True);
            Assert.That(
                WorldLayerUtility.AreVerticallyAdjacent(WorldLayer.Underwater, WorldLayer.Surface),
                Is.True);
            Assert.That(
                WorldLayerUtility.AreVerticallyAdjacent(WorldLayer.Underwater, WorldLayer.DeepSea),
                Is.True);
            Assert.That(
                WorldLayerUtility.AreVerticallyAdjacent(WorldLayer.DeepSea, WorldLayer.Underwater),
                Is.True);
            Assert.That(
                WorldLayerUtility.AreVerticallyAdjacent(WorldLayer.Surface, WorldLayer.DeepSea),
                Is.False);
            Assert.That(
                WorldLayerUtility.AreVerticallyAdjacent(WorldLayer.Surface, WorldLayer.Surface),
                Is.False);
        }

        /// <summary>
        /// 地面、淡水、内陸塩水、海、マグマを準備し、DeepSeaが海の沖合だけに分類されることを確認します。
        /// </summary>
        [Test]
        public void TryResolveNaturalWaterColumnKind_RestrictsDeepSeaToOffshoreSea()
        {
            BuiltInTerrainBlockRuntimeIds runtimeIds = CreateRuntimeIds();
            AssertColumn(default, runtimeIds, true, NaturalWaterColumnKind.Land);
            AssertColumn(
                new GeneratedTerrainCell
                {
                    Biome = BiomeId.FreshwaterMarshFloodplain,
                    LiquidRuntimeId = runtimeIds.FreshWater
                },
                runtimeIds,
                true,
                NaturalWaterColumnKind.StandardWater);
            AssertColumn(
                new GeneratedTerrainCell
                {
                    Biome = BiomeId.HotDesert,
                    LiquidRuntimeId = runtimeIds.SaltWater
                },
                runtimeIds,
                true,
                NaturalWaterColumnKind.StandardWater);
            AssertColumn(
                new GeneratedTerrainCell
                {
                    Biome = BiomeId.None,
                    LiquidRuntimeId = runtimeIds.SaltWater
                },
                runtimeIds,
                false,
                NaturalWaterColumnKind.StandardWater);
            AssertColumn(
                new GeneratedTerrainCell
                {
                    Biome = BiomeId.None,
                    LiquidRuntimeId = runtimeIds.SaltWater
                },
                runtimeIds,
                true,
                NaturalWaterColumnKind.OffshoreDeepWater);
            AssertColumn(
                new GeneratedTerrainCell
                {
                    Biome = BiomeId.VolcanicGeothermal,
                    LiquidRuntimeId = runtimeIds.Magma
                },
                runtimeIds,
                true,
                NaturalWaterColumnKind.Land);
        }

        /// <summary>
        /// 未知のLiquid Runtime IDを準備し、陸地や通常水域へ暗黙に丸めないことを確認します。
        /// </summary>
        [Test]
        public void TryResolveNaturalWaterColumnKind_RejectsUnknownLiquidRuntimeId()
        {
            BuiltInTerrainBlockRuntimeIds runtimeIds = CreateRuntimeIds();
            bool resolved = WorldLayerWaterContract.TryResolveNaturalWaterColumnKind(
                new GeneratedTerrainCell
                {
                    LiquidRuntimeId = new DefinitionRuntimeId(999)
                },
                runtimeIds,
                true,
                out _);

            Assert.That(resolved, Is.False);
        }

        /// <summary>自然水柱テストで共有する5つの有効な仮Runtime IDを作ります。</summary>
        /// <returns>WorldLayerWaterContractが受理できる組み込みID一式です。</returns>
        private static BuiltInTerrainBlockRuntimeIds CreateRuntimeIds()
        {
            return new BuiltInTerrainBlockRuntimeIds
            {
                LandGround = new DefinitionRuntimeId(1),
                NaturalRockWall = new DefinitionRuntimeId(2),
                FreshWater = new DefinitionRuntimeId(3),
                SaltWater = new DefinitionRuntimeId(4),
                Magma = new DefinitionRuntimeId(5)
            };
        }

        /// <summary>1つの自然生成Blockセルを解決し、期待する水柱分類と一致することを確認します。</summary>
        /// <param name="cell">判定する自然生成Blockセルです。</param>
        /// <param name="runtimeIds">液体種を識別する組み込みIDです。</param>
        /// <param name="isOffshoreDeepCandidate">沖合深水候補ならtrueです。</param>
        /// <param name="expected">期待する自然水柱分類です。</param>
        private static void AssertColumn(
            in GeneratedTerrainCell cell,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            bool isOffshoreDeepCandidate,
            NaturalWaterColumnKind expected)
        {
            Assert.That(
                WorldLayerWaterContract.TryResolveNaturalWaterColumnKind(
                    cell,
                    runtimeIds,
                    isOffshoreDeepCandidate,
                    out NaturalWaterColumnKind actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 陸地、通常水域、沖合深水域を準備し、各レイヤーの自然媒体が合意した水深になることを確認します。
        /// </summary>
        /// <param name="columnKind">テストする自然水柱の分類です。</param>
        /// <param name="layer">媒体を問い合わせるレイヤーです。</param>
        /// <param name="expectedMedium">契約上期待する自然媒体です。</param>
        [TestCase(NaturalWaterColumnKind.Land, WorldLayer.Surface, WorldLayerCellMedium.DryAir)]
        [TestCase(NaturalWaterColumnKind.Land, WorldLayer.Underwater, WorldLayerCellMedium.Solid)]
        [TestCase(NaturalWaterColumnKind.Land, WorldLayer.DeepSea, WorldLayerCellMedium.Solid)]
        [TestCase(NaturalWaterColumnKind.StandardWater, WorldLayer.Surface, WorldLayerCellMedium.Water)]
        [TestCase(NaturalWaterColumnKind.StandardWater, WorldLayer.Underwater, WorldLayerCellMedium.Water)]
        [TestCase(NaturalWaterColumnKind.StandardWater, WorldLayer.DeepSea, WorldLayerCellMedium.Solid)]
        [TestCase(NaturalWaterColumnKind.OffshoreDeepWater, WorldLayer.Surface, WorldLayerCellMedium.Water)]
        [TestCase(NaturalWaterColumnKind.OffshoreDeepWater, WorldLayer.Underwater, WorldLayerCellMedium.Water)]
        [TestCase(NaturalWaterColumnKind.OffshoreDeepWater, WorldLayer.DeepSea, WorldLayerCellMedium.Water)]
        public void TryResolveNaturalMedium_MapsNaturalWaterColumnDepth(
            NaturalWaterColumnKind columnKind,
            WorldLayer layer,
            WorldLayerCellMedium expectedMedium)
        {
            bool resolved = WorldLayerWaterContract.TryResolveNaturalMedium(
                columnKind,
                layer,
                out WorldLayerCellMedium medium);

            Assert.That(resolved, Is.True);
            Assert.That(medium, Is.EqualTo(expectedMedium));
        }

        /// <summary>
        /// 未知の自然水柱またはレイヤーを与え、誤った既定地形へ丸めず明示的に拒否することを確認します。
        /// </summary>
        [Test]
        public void TryResolveNaturalMedium_RejectsUnknownContractValues()
        {
            bool unknownColumnResolved = WorldLayerWaterContract.TryResolveNaturalMedium(
                (NaturalWaterColumnKind)byte.MaxValue,
                WorldLayer.Surface,
                out _);
            bool unknownLayerResolved = WorldLayerWaterContract.TryResolveNaturalMedium(
                NaturalWaterColumnKind.Land,
                (WorldLayer)byte.MaxValue,
                out _);

            Assert.That(unknownColumnResolved, Is.False);
            Assert.That(unknownLayerResolved, Is.False);
        }

        /// <summary>
        /// 上層地面、下層天井、閉鎖水密配置物の組み合わせを準備し、地形を優先した境界合成を確認します。
        /// </summary>
        /// <param name="hasUpperGround">上層の有効地形に地面が残るかを表します。</param>
        /// <param name="hasLowerCeiling">下層の有効地形に天井が残るかを表します。</param>
        /// <param name="hasClosedPlacement">閉鎖水密配置物があるかを表します。</param>
        /// <param name="expected">契約上期待する境界です。</param>
        [TestCase(false, false, false, WorldLayerBoundaryKind.Open)]
        [TestCase(true, false, false, WorldLayerBoundaryKind.TerrainBlocked)]
        [TestCase(false, true, false, WorldLayerBoundaryKind.TerrainBlocked)]
        [TestCase(true, true, false, WorldLayerBoundaryKind.TerrainBlocked)]
        [TestCase(false, false, true, WorldLayerBoundaryKind.ClosedWatertightBarrier)]
        [TestCase(true, false, true, WorldLayerBoundaryKind.TerrainBlocked)]
        public void ResolveVerticalBoundary_ComposesEffectiveTerrainBeforePlacement(
            bool hasUpperGround,
            bool hasLowerCeiling,
            bool hasClosedPlacement,
            WorldLayerBoundaryKind expected)
        {
            WorldLayerBoundaryKind actual = WorldLayerWaterContract.ResolveVerticalBoundary(
                hasUpperGround,
                hasLowerCeiling,
                hasClosedPlacement);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 固体、乾燥室、水、外水接続、排水状態を準備し、移動判定前の固定Tick媒体更新を確認します。
        /// </summary>
        /// <param name="currentMedium">更新前のセル媒体です。</param>
        /// <param name="hasOpenWaterConnection">外水または上層水への開口があるかを表します。</param>
        /// <param name="drainageState">施設単位の排水成立状態です。</param>
        /// <param name="expectedMedium">同じ固定Tickで期待する更新後媒体です。</param>
        [TestCase(WorldLayerCellMedium.Solid, true, FacilityDrainageState.Unavailable, WorldLayerCellMedium.Solid)]
        [TestCase(WorldLayerCellMedium.DryAir, false, FacilityDrainageState.Unavailable, WorldLayerCellMedium.DryAir)]
        [TestCase(WorldLayerCellMedium.DryAir, true, FacilityDrainageState.Unavailable, WorldLayerCellMedium.Water)]
        [TestCase(WorldLayerCellMedium.Water, false, FacilityDrainageState.Unavailable, WorldLayerCellMedium.Water)]
        [TestCase(WorldLayerCellMedium.Water, false, FacilityDrainageState.Operational, WorldLayerCellMedium.DryAir)]
        [TestCase(WorldLayerCellMedium.DryAir, true, FacilityDrainageState.Operational, WorldLayerCellMedium.DryAir)]
        public void ResolveMediumAtFixedTick_UpdatesFloodingAndDrainageBeforeTraversal(
            WorldLayerCellMedium currentMedium,
            bool hasOpenWaterConnection,
            FacilityDrainageState drainageState,
            WorldLayerCellMedium expectedMedium)
        {
            WorldLayerCellMedium actual = WorldLayerWaterContract.ResolveMediumAtFixedTick(
                currentMedium,
                hasOpenWaterConnection,
                drainageState);

            Assert.That(actual, Is.EqualTo(expectedMedium));
        }

        /// <summary>
        /// 地上の水の直下を1階層掘った状態を準備し、同じ固定Tickで自動浸水してUnderwaterが成立することを確認します。
        /// </summary>
        [Test]
        public void ExcavatingOneLayerBelowWater_FloodsAndAllowsUnderwaterTransitionInSameTick()
        {
            // 掘削前のSolidは外水へ接していても水へ変わらず、地形変更が空間を作る責務を維持します。
            WorldLayerCellMedium beforeExcavation = WorldLayerWaterContract.ResolveMediumAtFixedTick(
                WorldLayerCellMedium.Solid,
                true,
                FacilityDrainageState.Unavailable);
            Assert.That(beforeExcavation, Is.EqualTo(WorldLayerCellMedium.Solid));

            // 掘削CommitがSolidをDryAirへ変えた後、同じ固定Tickの媒体更新が上層水を流入させます。
            WorldLayerCellMedium afterExcavation = WorldLayerWaterContract.ResolveMediumAtFixedTick(
                WorldLayerCellMedium.DryAir,
                true,
                FacilityDrainageState.Unavailable);
            Assert.That(afterExcavation, Is.EqualTo(WorldLayerCellMedium.Water));

            WorldLayerCellState surface = CreateReadyCell(4, -7, WorldLayer.Surface, WorldLayerCellMedium.Water);
            WorldLayerCellState underwater = CreateReadyCell(4, -7, WorldLayer.Underwater, afterExcavation);
            WorldLayerTransitionResult transition = WorldLayerWaterContract.EvaluateTransition(
                surface,
                underwater,
                WorldLayerTraversalKind.VerticalLayerChange,
                WorldLayerBoundaryKind.Open);

            Assert.That(transition, Is.EqualTo(WorldLayerTransitionResult.Allowed));
        }

        /// <summary>
        /// 未ロードの移動先を準備し、セル不存在や固体と誤認せず読み込み後の再判定を要求することを確認します。
        /// </summary>
        [Test]
        public void EvaluateTransition_UnloadedDestinationNeedsPreparation()
        {
            WorldLayerCellState source = CreateReadyCell(0, 0, WorldLayer.Surface, WorldLayerCellMedium.Water);
            WorldLayerCellState destination = new WorldLayerCellState(
                0,
                0,
                WorldLayer.Underwater,
                WorldLayerCellReadiness.Unloaded,
                WorldLayerCellMedium.Solid);

            WorldLayerTransitionResult actual = WorldLayerWaterContract.EvaluateTransition(
                source,
                destination,
                WorldLayerTraversalKind.VerticalLayerChange,
                WorldLayerBoundaryKind.Open);

            Assert.That(actual, Is.EqualTo(WorldLayerTransitionResult.NeedsPreparation));
        }

        /// <summary>
        /// 未ロードだがレイヤーまたは位置関係が不正な移動先を準備し、不要な生成・読込を要求する前に拒否することを確認します。
        /// </summary>
        [Test]
        public void EvaluateTransition_UnloadedInvalidDestinationIsRejectedBeforePreparation()
        {
            WorldLayerCellState surfaceWater = CreateReadyCell(
                0,
                0,
                WorldLayer.Surface,
                WorldLayerCellMedium.Water);

            // 不正レイヤーは、媒体をまだ読めない状態でも固定IDだけで拒否できます。
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateUnloadedCell(0, 0, WorldLayer.None),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByInvalidLayer));

            // 上下移動のX/Y不一致とレイヤー飛び越しも、チャンク生成を始める前に判定します。
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateUnloadedCell(1, 0, WorldLayer.Underwater),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByMismatchedLocation));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateUnloadedCell(0, 0, WorldLayer.DeepSea),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByNonAdjacentDestination));

            // 同一レイヤーでも共有辺ではない遠隔セルは、未ロードを理由に準備対象へしません。
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateUnloadedCell(2, 0, WorldLayer.Surface),
                    WorldLayerTraversalKind.Horizontal,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByNonAdjacentDestination));
        }

        /// <summary>
        /// 未知の準備状態と移動種別を準備し、通常の移動や未ロード待ちへ丸めず破損入力として拒否することを確認します。
        /// </summary>
        [Test]
        public void EvaluateTransition_UnknownContractValuesAreRejected()
        {
            WorldLayerCellState validSource = CreateReadyCell(
                0,
                0,
                WorldLayer.Surface,
                WorldLayerCellMedium.Water);
            WorldLayerCellState invalidReadinessDestination = new WorldLayerCellState(
                0,
                0,
                WorldLayer.Underwater,
                (WorldLayerCellReadiness)byte.MaxValue,
                WorldLayerCellMedium.Water);

            WorldLayerTransitionResult invalidReadiness = WorldLayerWaterContract.EvaluateTransition(
                validSource,
                invalidReadinessDestination,
                WorldLayerTraversalKind.VerticalLayerChange,
                WorldLayerBoundaryKind.Open);
            WorldLayerTransitionResult invalidTraversal = WorldLayerWaterContract.EvaluateTransition(
                validSource,
                CreateReadyCell(0, 0, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                (WorldLayerTraversalKind)byte.MaxValue,
                WorldLayerBoundaryKind.Open);

            Assert.That(
                invalidReadiness,
                Is.EqualTo(WorldLayerTransitionResult.BlockedByInvalidContractValue));
            Assert.That(
                invalidTraversal,
                Is.EqualTo(WorldLayerTransitionResult.BlockedByInvalidContractValue));
        }

        /// <summary>
        /// 同じX/Yの隣接する水域を準備し、地上・海中・深海の隣接層間だけを許可することを確認します。
        /// </summary>
        /// <param name="sourceLayer">遷移元レイヤーです。</param>
        /// <param name="destinationLayer">遷移先レイヤーです。</param>
        [TestCase(WorldLayer.Surface, WorldLayer.Underwater)]
        [TestCase(WorldLayer.Underwater, WorldLayer.Surface)]
        [TestCase(WorldLayer.Underwater, WorldLayer.DeepSea)]
        [TestCase(WorldLayer.DeepSea, WorldLayer.Underwater)]
        public void EvaluateTransition_VerticalWaterNeighborsAreAllowed(
            WorldLayer sourceLayer,
            WorldLayer destinationLayer)
        {
            WorldLayerTransitionResult actual = WorldLayerWaterContract.EvaluateTransition(
                CreateReadyCell(12, -34, sourceLayer, WorldLayerCellMedium.Water),
                CreateReadyCell(12, -34, destinationLayer, WorldLayerCellMedium.Water),
                WorldLayerTraversalKind.VerticalLayerChange,
                WorldLayerBoundaryKind.Open);

            Assert.That(actual, Is.EqualTo(WorldLayerTransitionResult.Allowed));
        }

        /// <summary>
        /// 異なるX/Y、中間層飛ばし、乾燥室、遮断境界を個別に準備し、上下移動の拒否理由を確認します。
        /// </summary>
        [Test]
        public void EvaluateTransition_VerticalInvalidCasesReturnSpecificReasons()
        {
            WorldLayerCellState surfaceWater = CreateReadyCell(1, 2, WorldLayer.Surface, WorldLayerCellMedium.Water);

            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateReadyCell(2, 2, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByMismatchedLocation));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateReadyCell(1, 2, WorldLayer.DeepSea, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByNonAdjacentDestination));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateReadyCell(1, 2, WorldLayer.Underwater, WorldLayerCellMedium.DryAir),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByNonWaterVerticalCell));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    surfaceWater,
                    CreateReadyCell(1, 2, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.VerticalLayerChange,
                    WorldLayerBoundaryKind.TerrainBlocked),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByBoundary));
        }

        /// <summary>
        /// 同一レイヤーの共有辺と非共有辺を準備し、水平境界契約が斜め・同一セル・整数周回を受理しないことを確認します。
        /// </summary>
        [Test]
        public void EvaluateTransition_HorizontalRequiresSameLayerSharedEdge()
        {
            WorldLayerCellState source = CreateReadyCell(0, 0, WorldLayer.Underwater, WorldLayerCellMedium.Water);

            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    source,
                    CreateReadyCell(1, 0, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.Horizontal,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.Allowed));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    source,
                    CreateReadyCell(1, 1, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.Horizontal,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByNonAdjacentDestination));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    source,
                    CreateReadyCell(0, 0, WorldLayer.DeepSea, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.Horizontal,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByMismatchedLocation));
            Assert.That(
                WorldLayerWaterContract.EvaluateTransition(
                    CreateReadyCell(int.MinValue, 0, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                    CreateReadyCell(int.MaxValue, 0, WorldLayer.Underwater, WorldLayerCellMedium.Water),
                    WorldLayerTraversalKind.Horizontal,
                    WorldLayerBoundaryKind.Open),
                Is.EqualTo(WorldLayerTransitionResult.BlockedByNonAdjacentDestination));
        }

        /// <summary>
        /// 外水と乾燥室を同一レイヤーで隣接させ、排水中だけ直接移動でき、停止時は先に浸水更新することを確認します。
        /// </summary>
        [Test]
        public void EvaluateTransition_HorizontalWaterToDryRoomDependsOnDrainage()
        {
            WorldLayerCellState exteriorWater = CreateReadyCell(
                8,
                9,
                WorldLayer.Underwater,
                WorldLayerCellMedium.Water);
            WorldLayerCellState dryRoom = CreateReadyCell(
                9,
                9,
                WorldLayer.Underwater,
                WorldLayerCellMedium.DryAir);

            WorldLayerTransitionResult operational = WorldLayerWaterContract.EvaluateTransition(
                exteriorWater,
                dryRoom,
                WorldLayerTraversalKind.Horizontal,
                WorldLayerBoundaryKind.Open,
                FacilityDrainageState.Operational);
            WorldLayerTransitionResult stopped = WorldLayerWaterContract.EvaluateTransition(
                exteriorWater,
                dryRoom,
                WorldLayerTraversalKind.Horizontal,
                WorldLayerBoundaryKind.Open,
                FacilityDrainageState.Unavailable);

            Assert.That(operational, Is.EqualTo(WorldLayerTransitionResult.Allowed));
            Assert.That(stopped, Is.EqualTo(WorldLayerTransitionResult.NeedsMediumUpdate));
        }

        /// <summary>
        /// テストで繰り返し使う判定可能セルを作成し、座標・レイヤー・媒体だけを各ケースから指定します。
        /// </summary>
        /// <param name="x">セルの絶対X座標です。</param>
        /// <param name="y">セルの絶対Y座標です。</param>
        /// <param name="layer">セルが属する固定ワールドレイヤーです。</param>
        /// <param name="medium">セル内部の固体・空気・水の状態です。</param>
        /// <returns>Ready状態で作成した製品契約用セルです。</returns>
        private static WorldLayerCellState CreateReadyCell(
            int x,
            int y,
            WorldLayer layer,
            WorldLayerCellMedium medium)
        {
            return new WorldLayerCellState(
                x,
                y,
                layer,
                WorldLayerCellReadiness.Ready,
                medium);
        }

        /// <summary>
        /// 未ロード先の構造検証で繰り返し使うセルを作成し、媒体を読めない状態を表します。
        /// </summary>
        /// <param name="x">セルの絶対X座標です。</param>
        /// <param name="y">セルの絶対Y座標です。</param>
        /// <param name="layer">生成・読込前でも要求から確定しているワールドレイヤーです。</param>
        /// <returns>Unloaded状態で、媒体を仮のSolidとして保持する契約用セルです。</returns>
        private static WorldLayerCellState CreateUnloadedCell(int x, int y, WorldLayer layer)
        {
            return new WorldLayerCellState(
                x,
                y,
                layer,
                WorldLayerCellReadiness.Unloaded,
                WorldLayerCellMedium.Solid);
        }
    }
}
