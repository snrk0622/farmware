// このファイルは、生成済みBlockセル、疎な差分、有効セル、移動・建築判定を検証します。
// テストごとに最小のBlobとECS Bufferを準備し、表示やSceneに依存せず地形データ境界を保証します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 元生成を破壊しない差分合成、レイヤー分離、移動と建築の異なる結果を保証するEditModeテストです。
    /// </summary>
    public sealed class EffectiveTerrainCellTests
    {
        /// <summary>各テストで所有し、終了時に破棄する共通定義Blobです。</summary>
        private BlobAssetReference<DefinitionCatalogBlob> definitionCatalog;

        /// <summary>各テストで所有し、終了時に破棄する地形定義Blobです。</summary>
        private BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog;

        /// <summary>既存GeneratedTileをBlockへ変換する解決済みIDです。</summary>
        private BuiltInTerrainBlockRuntimeIds runtimeIds;

        /// <summary>
        /// 暫定5地形と41材質を含む共通カタログ・地形カタログを構築します。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            DefinitionBuildEntry[] definitions =
                BuiltInTerrainBlockDefinitions.CreateCommonDefinitions(
                    0,
                    out DefinitionBuildReference[] references);
            Assert.That(
                DefinitionCatalogBuilder.TryBuild(
                    definitions,
                    references,
                    out definitionCatalog,
                    out DefinitionCatalogValidationError definitionError),
                Is.True,
                $"共通定義を構築できませんでした: {definitionError.Code}");
            Assert.That(
                BuiltInTerrainBlockDefinitions.TryCreateTerrainDefinitions(
                    definitionCatalog,
                    out TerrainBlockDefinitionBuildEntry[] terrainDefinitions),
                Is.True,
                "41材質を含む地形入力を解決できませんでした。");
            Assert.That(
                TerrainBlockCatalogBuilder.TryBuild(
                    definitionCatalog,
                    terrainDefinitions,
                    out terrainCatalog),
                Is.True,
                "地形定義を構築できませんでした。");
            Assert.That(
                BuiltInTerrainBlockDefinitions.TryResolveRuntimeIds(
                    definitionCatalog,
                    out runtimeIds),
                Is.True,
                "組み込み地形IDを解決できませんでした。");
        }

        /// <summary>
        /// テストが作成したPersistent Blobを必ず解放し、後続テストへメモリを残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (terrainCatalog.IsCreated)
            {
                terrainCatalog.Dispose();
            }

            if (definitionCatalog.IsCreated)
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 組み込み定義が共通カタログの分類と地形固有属性を正しく結び付けることを保証します。
        /// </summary>
        [Test]
        public void TerrainCatalog_BuiltInDefinitions_ResolveExpectedKindsAndFlags()
        {
            Assert.That(
                TerrainBlockCatalogLookup.TryGetDefinition(
                    terrainCatalog,
                    runtimeIds.LandGround,
                    out TerrainBlockDefinitionBlob land),
                Is.True);
            Assert.That(land.Kind, Is.EqualTo(DefinitionKind.GroundBlock));
            Assert.That(
                land.Flags & TerrainBlockDefinitionFlags.AllowsNormalEquipment,
                Is.Not.EqualTo(TerrainBlockDefinitionFlags.None));

            Assert.That(
                TerrainBlockCatalogLookup.TryGetDefinition(
                    terrainCatalog,
                    runtimeIds.Magma,
                    out TerrainBlockDefinitionBlob magma),
                Is.True);
            Assert.That(magma.Kind, Is.EqualTo(DefinitionKind.Liquid));
            Assert.That(
                magma.Flags & TerrainBlockDefinitionFlags.RequiresHeatResistance,
                Is.Not.EqualTo(TerrainBlockDefinitionFlags.None));
        }

        /// <summary>
        /// 自然Wallセルから壁だけを除去し、生成済みGroundとBiomeが残ることを保証します。
        /// </summary>
        [Test]
        public void Resolve_RockWallRemoval_PreservesGroundAndBiome()
        {
            GeneratedTerrainCell baseCell = new GeneratedTerrainCell
            {
                Biome = BiomeId.AlpineRockland,
                GroundBlockRuntimeId = runtimeIds.LandGround,
                WallBlockRuntimeId = runtimeIds.NaturalRockWall
            };

            TerrainCellDelta delta = new TerrainCellDelta
            {
                LocalTileIndex = 15,
                ChangeMask = TerrainCellChangeMask.WallBlock,
                WallBlockRuntimeId = default
            };
            Assert.That(
                EffectiveTerrainResolver.TryResolve(
                    baseCell,
                    true,
                    delta,
                    terrainCatalog,
                    out EffectiveTerrainCell effectiveCell),
                Is.EqualTo(EffectiveTerrainResolveResult.Success));

            Assert.That(effectiveCell.Biome, Is.EqualTo(BiomeId.AlpineRockland));
            Assert.That(effectiveCell.GroundBlockRuntimeId, Is.EqualTo(runtimeIds.LandGround));
            Assert.That(effectiveCell.WallBlockRuntimeId.IsValid, Is.False);
        }

        /// <summary>
        /// 海域のBiome.Noneを有効な生成セルとして扱い、海水Liquidだけを有効セルへ引き継ぐことを保証します。
        /// </summary>
        [Test]
        public void Resolve_SeaWithoutLandBiome_UsesLiquidWithoutGround()
        {
            GeneratedTerrainCell baseCell = new GeneratedTerrainCell
            {
                Biome = BiomeId.None,
                LiquidRuntimeId = runtimeIds.SaltWater
            };

            Assert.That(
                EffectiveTerrainResolver.TryResolve(
                    baseCell,
                    false,
                    default,
                    terrainCatalog,
                    out EffectiveTerrainCell effectiveCell),
                Is.EqualTo(EffectiveTerrainResolveResult.Success));
            Assert.That(effectiveCell.Biome, Is.EqualTo(BiomeId.None));
            Assert.That(effectiveCell.GroundBlockRuntimeId.IsValid, Is.False);
            Assert.That(effectiveCell.LiquidRuntimeId, Is.EqualTo(runtimeIds.SaltWater));
        }

        /// <summary>
        /// 地面を残したまま液体を追加する矛盾差分を拒否し、壊れた有効セルを公開しないことを保証します。
        /// </summary>
        [Test]
        public void Resolve_GroundAndLiquidOverlap_IsRejected()
        {
            GeneratedTerrainCell baseCell = CreateLandBaseCell(BiomeId.TemperateGrassland);
            TerrainCellDelta delta = new TerrainCellDelta
            {
                LocalTileIndex = 0,
                ChangeMask = TerrainCellChangeMask.Liquid,
                LiquidRuntimeId = runtimeIds.FreshWater
            };

            Assert.That(
                EffectiveTerrainResolver.TryResolve(
                    baseCell,
                    true,
                    delta,
                    terrainCatalog,
                    out _),
                Is.EqualTo(EffectiveTerrainResolveResult.InvalidCombination));
        }

        /// <summary>
        /// 差分をセル番号順に挿入し、希望状態を自然状態へ戻すと要素自体を削除することを保証します。
        /// </summary>
        [Test]
        public void DeltaBuffer_SetOrRemove_KeepsSortedSparseState()
        {
            using World world = new World("Terrain Delta Test");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<TerrainCellDelta> deltas =
                world.EntityManager.AddBuffer<TerrainCellDelta>(entity);
            GeneratedTerrainCell baseCell = CreateLandBaseCell(BiomeId.TemperateGrassland);
            EffectiveTerrainCell changed = CreateEffectiveCell(baseCell);
            changed.GroundBlockRuntimeId = default;

            Assert.That(
                TerrainCellDeltaUtility.SetOrRemove(
                    deltas,
                    100,
                    baseCell,
                    changed,
                    terrainCatalog),
                Is.True);
            Assert.That(
                TerrainCellDeltaUtility.SetOrRemove(
                    deltas,
                    5,
                    baseCell,
                    changed,
                    terrainCatalog),
                Is.True);
            Assert.That(deltas.Length, Is.EqualTo(2));
            Assert.That(deltas[0].LocalTileIndex, Is.EqualTo(5));
            Assert.That(deltas[1].LocalTileIndex, Is.EqualTo(100));

            EffectiveTerrainCell restored = CreateEffectiveCell(baseCell);
            Assert.That(
                TerrainCellDeltaUtility.SetOrRemove(
                    deltas,
                    5,
                    baseCell,
                    restored,
                    terrainCatalog),
                Is.True);
            Assert.That(deltas.Length, Is.EqualTo(1));
            Assert.That(deltas[0].LocalTileIndex, Is.EqualTo(100));
        }

        /// <summary>
        /// 同じX/Yでも別レイヤーの差分所有者を問い合わせた場合は拒否し、地上編集を水中へ適用しないことを保証します。
        /// </summary>
        [Test]
        public void CellQuery_SameXYDifferentLayer_DoesNotReuseDeltaOwner()
        {
            using World world = new World("Terrain Layer Test");
            Entity entity = world.EntityManager.CreateEntity();
            DynamicBuffer<TerrainCellDelta> deltas =
                world.EntityManager.AddBuffer<TerrainCellDelta>(entity);
            TerrainChunkDeltaOwner surfaceOwner = new TerrainChunkDeltaOwner
            {
                Layer = WorldLayer.Surface,
                ChunkX = -1,
                ChunkY = 0
            };
            WorldTileCoordinate underwaterCoordinate = new WorldTileCoordinate(
                WorldLayer.Underwater,
                -1,
                2);

            Assert.That(
                EffectiveTerrainCellQuery.TryResolve(
                    underwaterCoordinate,
                    surfaceOwner,
                    deltas,
                    CreateLandBaseCell(BiomeId.TemperateGrassland),
                    terrainCatalog,
                    out _),
                Is.EqualTo(EffectiveTerrainResolveResult.InvalidDelta));
        }

        /// <summary>
        /// 水は泳げれば移動可能でも通常設備は置けず、移動判定と建築判定を混同しないことを保証します。
        /// </summary>
        [Test]
        public void WaterCell_SwimmerCanMove_ButNormalEquipmentCannotBePlaced()
        {
            EffectiveTerrainCell waterCell = new EffectiveTerrainCell
            {
                Biome = BiomeId.FreshwaterMarshFloodplain,
                LiquidRuntimeId = runtimeIds.FreshWater
            };
            TerrainTraversalCapabilities swimmer = new TerrainTraversalCapabilities
            {
                CanEnterLiquid = 1
            };

            Assert.That(
                TerrainTraversalRules.Evaluate(
                    waterCell,
                    terrainCatalog,
                    swimmer),
                Is.EqualTo(TerrainTraversalResult.Allowed));
            Assert.That(
                TerrainPlacementRules.Evaluate(
                    waterCell,
                    terrainCatalog,
                    TerrainPlacementKind.NormalEquipment),
                Is.EqualTo(TerrainPlacementResult.TerrainDoesNotAllowPlacement));
            Assert.That(
                TerrainPlacementRules.Evaluate(
                    waterCell,
                    terrainCatalog,
                    TerrainPlacementKind.Bridge),
                Is.EqualTo(TerrainPlacementResult.Allowed));
        }

        /// <summary>
        /// マグマ上の橋と進入がどちらも耐熱能力を要求し、通常水域と区別されることを保証します。
        /// </summary>
        [Test]
        public void MagmaCell_RequiresHeatResistanceForTraversalAndBridge()
        {
            EffectiveTerrainCell magmaCell = new EffectiveTerrainCell
            {
                Biome = BiomeId.VolcanicGeothermal,
                LiquidRuntimeId = runtimeIds.Magma
            };
            TerrainTraversalCapabilities swimmerWithoutHeatResistance =
                new TerrainTraversalCapabilities
                {
                    CanEnterLiquid = 1
                };

            Assert.That(
                TerrainTraversalRules.Evaluate(
                    magmaCell,
                    terrainCatalog,
                    swimmerWithoutHeatResistance),
                Is.EqualTo(TerrainTraversalResult.HeatResistanceRequired));
            Assert.That(
                TerrainPlacementRules.Evaluate(
                    magmaCell,
                    terrainCatalog,
                    TerrainPlacementKind.Bridge),
                Is.EqualTo(TerrainPlacementResult.HeatResistanceRequired));
            Assert.That(
                TerrainPlacementRules.Evaluate(
                    magmaCell,
                    terrainCatalog,
                    TerrainPlacementKind.Bridge,
                    hasHeatResistance: true),
                Is.EqualTo(TerrainPlacementResult.Allowed));
        }

        /// <summary>
        /// 同じ差分集合を異なるEntity作成順で用意しても同じ状態ハッシュになり、ロード順を論理状態へ持ち込まないことを保証します。
        /// </summary>
        [Test]
        public void TerrainStateHash_DifferentEntityCreationOrder_IsEqual()
        {
            using World firstWorld = new World("Terrain Hash First");
            using World secondWorld = new World("Terrain Hash Second");
            AddCatalogComponents(firstWorld.EntityManager);
            AddCatalogComponents(secondWorld.EntityManager);

            AddDeltaOwner(firstWorld.EntityManager, WorldLayer.Surface, -2, 1, 100);
            AddDeltaOwner(firstWorld.EntityManager, WorldLayer.Underwater, 3, -4, 7);
            AddDeltaOwner(secondWorld.EntityManager, WorldLayer.Underwater, 3, -4, 7);
            AddDeltaOwner(secondWorld.EntityManager, WorldLayer.Surface, -2, 1, 100);

            SimulationStateHash firstHash = ComputeTerrainHash(firstWorld.EntityManager);
            SimulationStateHash secondHash = ComputeTerrainHash(secondWorld.EntityManager);
            Assert.That(secondHash, Is.EqualTo(firstHash));
        }

        /// <summary>
        /// 同じチャンクX/Yと差分でも所属レイヤーが異なれば状態ハッシュが変わり、保存比較で別地点として扱うことを保証します。
        /// </summary>
        [Test]
        public void TerrainStateHash_SameXYDifferentLayer_IsDifferent()
        {
            using World surfaceWorld = new World("Terrain Hash Surface");
            using World underwaterWorld = new World("Terrain Hash Underwater");
            AddCatalogComponents(surfaceWorld.EntityManager);
            AddCatalogComponents(underwaterWorld.EntityManager);
            AddDeltaOwner(surfaceWorld.EntityManager, WorldLayer.Surface, 1, 2, 5);
            AddDeltaOwner(underwaterWorld.EntityManager, WorldLayer.Underwater, 1, 2, 5);

            Assert.That(
                ComputeTerrainHash(underwaterWorld.EntityManager),
                Is.Not.EqualTo(ComputeTerrainHash(surfaceWorld.EntityManager)));
        }

        /// <summary>
        /// 淡水と海水の互換ID対応が入れ替わった破損状態を拒否し、同じ定義一覧でも生成結果の意味が変わる状態を見逃さないことを保証します。
        /// </summary>
        [Test]
        public void TerrainStateHash_SwappedBuiltInRuntimeIds_IsRejected()
        {
            using World world = new World("Terrain Hash Invalid Built-in IDs");
            AddCatalogComponents(world.EntityManager);
            // EntityQueryはNative資源を所有するため、テスト終了を待たずusingで確実に解放します。
            using EntityQuery catalogQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TerrainBlockCatalog>());
            Entity catalogEntity = catalogQuery.GetSingletonEntity();
            TerrainBlockCatalog catalog =
                world.EntityManager.GetComponentData<TerrainBlockCatalog>(catalogEntity);
            DefinitionRuntimeId originalFreshWater =
                catalog.BuiltInRuntimeIds.FreshWater;
            catalog.BuiltInRuntimeIds.FreshWater = catalog.BuiltInRuntimeIds.SaltWater;
            catalog.BuiltInRuntimeIds.SaltWater = originalFreshWater;
            world.EntityManager.SetComponentData(catalogEntity, catalog);

            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                SimulationStateHashSchema.CurrentVersion);
            Assert.That(
                TerrainStateHasher.TryAppendTo(
                    world.EntityManager,
                    ref builder,
                    out TerrainStateHashError error),
                Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(TerrainStateHashErrorCode.TerrainCatalogDataIsInvalid));
        }

        /// <summary>
        /// 差分所有者だけが残った不完全Entityを拒否し、Buffer欠落を「差分なし」と同じハッシュへ隠さないことを保証します。
        /// </summary>
        [Test]
        public void TerrainStateHash_OwnerWithoutBuffer_IsRejected()
        {
            using World world = new World("Terrain Hash Owner Without Buffer");
            AddCatalogComponents(world.EntityManager);
            Entity incompleteEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                incompleteEntity,
                new TerrainChunkDeltaOwner
                {
                    Layer = WorldLayer.Surface,
                    ChunkX = 0,
                    ChunkY = 0
                });

            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                SimulationStateHashSchema.CurrentVersion);
            Assert.That(
                TerrainStateHasher.TryAppendTo(
                    world.EntityManager,
                    ref builder,
                    out TerrainStateHashError error),
                Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(TerrainStateHashErrorCode.TerrainDeltaDataIsInvalid));
        }

        /// <summary>
        /// 差分Bufferだけが残った不完全Entityを拒否し、所有座標を失った永続状態を監査で検出できることを保証します。
        /// </summary>
        [Test]
        public void TerrainStateHash_BufferWithoutOwner_IsRejected()
        {
            using World world = new World("Terrain Hash Buffer Without Owner");
            AddCatalogComponents(world.EntityManager);
            Entity incompleteEntity = world.EntityManager.CreateEntity();
            DynamicBuffer<TerrainCellDelta> deltas =
                world.EntityManager.AddBuffer<TerrainCellDelta>(incompleteEntity);
            deltas.Add(new TerrainCellDelta
            {
                LocalTileIndex = 0,
                ChangeMask = TerrainCellChangeMask.GroundBlock,
                GroundBlockRuntimeId = default
            });

            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                SimulationStateHashSchema.CurrentVersion);
            Assert.That(
                TerrainStateHasher.TryAppendTo(
                    world.EntityManager,
                    ref builder,
                    out TerrainStateHashError error),
                Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(TerrainStateHashErrorCode.TerrainDeltaDataIsInvalid));
        }

        /// <summary>指定Biomeに通常地面だけがある自然セルを作ります。</summary>
        /// <param name="biome">セルへ固定する基礎バイオームです。</param>
        /// <returns>組み込み通常地面だけを持つ自然セルです。</returns>
        private GeneratedTerrainCell CreateLandBaseCell(BiomeId biome)
        {
            return new GeneratedTerrainCell
            {
                Biome = biome,
                GroundBlockRuntimeId = runtimeIds.LandGround
            };
        }

        /// <summary>自然セルと同じ区画を持つ変更後セルを作ります。</summary>
        /// <param name="baseCell">全区画をコピーする変更不可の自然セルです。</param>
        /// <returns>自然状態と完全に一致する有効セルです。</returns>
        private static EffectiveTerrainCell CreateEffectiveCell(in GeneratedTerrainCell baseCell)
        {
            return new EffectiveTerrainCell
            {
                Biome = baseCell.Biome,
                GroundBlockRuntimeId = baseCell.GroundBlockRuntimeId,
                WallBlockRuntimeId = baseCell.WallBlockRuntimeId,
                LiquidRuntimeId = baseCell.LiquidRuntimeId,
                TerrainSurfaceRuntimeId = baseCell.TerrainSurfaceRuntimeId
            };
        }

        /// <summary>テストWorldへ同じ共通・地形定義Blobを参照するSingleton候補を追加します。</summary>
        /// <param name="entityManager">定義Componentを追加するテストWorldです。</param>
        private void AddCatalogComponents(EntityManager entityManager)
        {
            Entity catalogEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(catalogEntity, new DefinitionCatalog
            {
                Value = definitionCatalog
            });
            entityManager.AddComponentData(catalogEntity, new TerrainBlockCatalog
            {
                Value = terrainCatalog,
                BuiltInRuntimeIds = runtimeIds
            });
        }

        /// <summary>指定レイヤー付きチャンクへ、地面を明示除去する1件の疎な差分を追加します。</summary>
        /// <param name="entityManager">差分所有者Entityを追加するテストWorldです。</param>
        /// <param name="layer">差分が属する固定ワールドレイヤーです。</param>
        /// <param name="chunkX">差分所有者の水平論理チャンク座標です。</param>
        /// <param name="chunkY">差分所有者の垂直論理チャンク座標です。</param>
        /// <param name="localTileIndex">地面を明示除去する0〜4095のセル番号です。</param>
        private static void AddDeltaOwner(
            EntityManager entityManager,
            WorldLayer layer,
            int chunkX,
            int chunkY,
            ushort localTileIndex)
        {
            Entity ownerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(ownerEntity, new TerrainChunkDeltaOwner
            {
                Layer = layer,
                ChunkX = chunkX,
                ChunkY = chunkY
            });
            DynamicBuffer<TerrainCellDelta> deltas =
                entityManager.AddBuffer<TerrainCellDelta>(ownerEntity);
            deltas.Add(new TerrainCellDelta
            {
                LocalTileIndex = localTileIndex,
                ChangeMask = TerrainCellChangeMask.GroundBlock,
                GroundBlockRuntimeId = default
            });
        }

        /// <summary>地形セクションだけを追加した完成状態ハッシュを計算し、検証失敗ならテストを停止します。</summary>
        /// <param name="entityManager">共通定義、地形定義、任意差分を保持するテストWorldです。</param>
        /// <returns>地形定義と差分から完成した128bit状態ハッシュです。</returns>
        private static SimulationStateHash ComputeTerrainHash(EntityManager entityManager)
        {
            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                SimulationStateHashSchema.CurrentVersion);
            Assert.That(
                TerrainStateHasher.TryAppendTo(
                    entityManager,
                    ref builder,
                    out TerrainStateHashError error),
                Is.True,
                $"地形状態ハッシュを構築できませんでした: {error.Code}");
            return builder.Complete();
        }
    }
}
