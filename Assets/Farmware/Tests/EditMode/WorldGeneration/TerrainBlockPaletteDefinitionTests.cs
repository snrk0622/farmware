// このファイルは、Biome表層Groundと地質由来Ground・自然Wallの固定材質パレットを検証します。
// 本番Bakerと同じDefinitionカタログ群を一時構築し、確定表、重み、分類、属性、登録順非依存をEditModeで保証します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 18Biomeと8地質の固定パレットが仕様表どおりで、安全なRuntime IDだけを公開することを保証するテストです。
    /// </summary>
    public sealed class TerrainBlockPaletteDefinitionTests
    {
        /// <summary>各テストで作成し、終了時に破棄する共通Definition Blobです。</summary>
        private BlobAssetReference<DefinitionCatalogBlob> definitionCatalog;

        /// <summary>各テストで作成し、終了時に破棄する地形Definition Blobです。</summary>
        private BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog;

        /// <summary>各テストで作成し、終了時に破棄する41材質対応Blobです。</summary>
        private BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog;

        /// <summary>テスト対象として構築し、終了時に破棄する固定パレットBlobです。</summary>
        private BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog;

        /// <summary>
        /// 各テストの前に、本番Bakerと同じ組み込みDefinition・地形・材質カタログを構築します。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            DefinitionBuildEntry[] commonDefinitions =
                BuiltInTerrainBlockDefinitions.CreateCommonDefinitions(
                    0,
                    out DefinitionBuildReference[] commonReferences);
            Assert.That(
                DefinitionCatalogBuilder.TryBuild(
                    commonDefinitions,
                    commonReferences,
                    out definitionCatalog,
                    out DefinitionCatalogValidationError definitionError),
                Is.True,
                $"共通Definitionを構築できませんでした: {definitionError.Code}");
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
                "41材質を含む地形カタログを構築できませんでした。");
            Assert.That(
                TerrainBlockMaterialCatalogBuilder.TryBuild(
                    definitionCatalog,
                    TerrainBlockMaterialDefinitions.CreateDefault(),
                    out materialCatalog),
                Is.True,
                "41材質対応カタログを構築できませんでした。");
        }

        /// <summary>
        /// 各テストで作成したPersistent Blobをすべて解放し、後続テストへメモリを残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (paletteCatalog.IsCreated)
            {
                paletteCatalog.Dispose();
            }

            if (materialCatalog.IsCreated)
            {
                materialCatalog.Dispose();
            }

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
        /// 既定入力が18Biome行と8地質行を持ち、すべてのパレットで重み合計256と行内材質一意性を満たすことを保証します。
        /// </summary>
        [Test]
        public void DefaultDefinitions_ContainCompleteRowsWithNormalizedWeights()
        {
            BiomeSurfaceBlockPaletteDefinition[] biomes =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            GeologyBlockPaletteDefinition[] geologies =
                TerrainBlockPaletteDefinitions.CreateGeologyDefaults();

            Assert.That(biomes.Length, Is.EqualTo(18));
            Assert.That(geologies.Length, Is.EqualTo(8));
            for (int biomeIndex = 0; biomeIndex < biomes.Length; biomeIndex++)
            {
                Assert.That((int)biomes[biomeIndex].BiomeId, Is.EqualTo(biomeIndex + 1));
                AssertNormalizedUniquePalette(biomes[biomeIndex].Entries);
            }

            for (int geologyIndex = 0; geologyIndex < geologies.Length; geologyIndex++)
            {
                Assert.That((int)geologies[geologyIndex].GeologyClass, Is.EqualTo(geologyIndex + 1));
                AssertNormalizedUniquePalette(geologies[geologyIndex].GroundEntries);
                AssertNormalizedUniquePalette(geologies[geologyIndex].WallEntries);
            }
        }

        /// <summary>
        /// 仕様表の先頭・4候補行・湿地・火山・全地質行が、指定された材質順と重みを保持することを保証します。
        /// </summary>
        [Test]
        public void DefaultDefinitions_MatchSpecifiedMaterialTables()
        {
            BiomeSurfaceBlockPaletteDefinition[] biomes =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            GeologyBlockPaletteDefinition[] geologies =
                TerrainBlockPaletteDefinitions.CreateGeologyDefaults();

            AssertEntries(biomes[0].Entries, Pair(TerrainBlockMaterialId.GrassLoam, 160), Pair(TerrainBlockMaterialId.MeadowSoil, 64), Pair(TerrainBlockMaterialId.ClaySoil, 32));
            AssertEntries(biomes[1].Entries, Pair(TerrainBlockMaterialId.DryGrassSoil, 144), Pair(TerrainBlockMaterialId.TropicalRedSoil, 64), Pair(TerrainBlockMaterialId.SandySoil, 48));
            AssertEntries(biomes[2].Entries, Pair(TerrainBlockMaterialId.ForestLoam, 160), Pair(TerrainBlockMaterialId.HumusSoil, 64), Pair(TerrainBlockMaterialId.ClaySoil, 32));
            AssertEntries(biomes[3].Entries, Pair(TerrainBlockMaterialId.ForestLoam, 144), Pair(TerrainBlockMaterialId.HumusSoil, 80), Pair(TerrainBlockMaterialId.ClaySoil, 32));
            AssertEntries(biomes[4].Entries, Pair(TerrainBlockMaterialId.MountainLoam, 128), Pair(TerrainBlockMaterialId.HumusSoil, 64), Pair(TerrainBlockMaterialId.ClaySoil, 32), Pair(TerrainBlockMaterialId.AlpineScree, 32));
            AssertEntries(biomes[5].Entries, Pair(TerrainBlockMaterialId.TropicalRedSoil, 112), Pair(TerrainBlockMaterialId.HumusSoil, 80), Pair(TerrainBlockMaterialId.ClaySoil, 64));
            AssertEntries(biomes[6].Entries, Pair(TerrainBlockMaterialId.TropicalRedSoil, 96), Pair(TerrainBlockMaterialId.ForestLoam, 80), Pair(TerrainBlockMaterialId.DryGrassSoil, 48), Pair(TerrainBlockMaterialId.ClaySoil, 32));
            AssertEntries(biomes[7].Entries, Pair(TerrainBlockMaterialId.IslandLoam, 112), Pair(TerrainBlockMaterialId.TropicalRedSoil, 64), Pair(TerrainBlockMaterialId.SandySoil, 48), Pair(TerrainBlockMaterialId.HumusSoil, 32));
            AssertEntries(biomes[8].Entries, Pair(TerrainBlockMaterialId.PodzolSoil, 144), Pair(TerrainBlockMaterialId.ForestLoam, 64), Pair(TerrainBlockMaterialId.PeatSoil, 48));
            AssertEntries(biomes[9].Entries, Pair(TerrainBlockMaterialId.SteppeSoil, 160), Pair(TerrainBlockMaterialId.DryGrassSoil, 64), Pair(TerrainBlockMaterialId.SandySoil, 32));
            AssertEntries(biomes[10].Entries, Pair(TerrainBlockMaterialId.DryScrubSoil, 144), Pair(TerrainBlockMaterialId.SandySoil, 64), Pair(TerrainBlockMaterialId.Gravel, 48));
            AssertEntries(biomes[11].Entries, Pair(TerrainBlockMaterialId.DesertGravel, 112), Pair(TerrainBlockMaterialId.SandySoil, 80), Pair(TerrainBlockMaterialId.DesertSand, 64));
            AssertEntries(biomes[12].Entries, Pair(TerrainBlockMaterialId.DesertSand, 160), Pair(TerrainBlockMaterialId.DesertGravel, 64), Pair(TerrainBlockMaterialId.SandySoil, 32));
            AssertEntries(biomes[13].Entries, Pair(TerrainBlockMaterialId.MarshMud, 112), Pair(TerrainBlockMaterialId.AlluvialSoil, 96), Pair(TerrainBlockMaterialId.PeatSoil, 48));
            AssertEntries(biomes[14].Entries, Pair(TerrainBlockMaterialId.PeatSoil, 96), Pair(TerrainBlockMaterialId.MarshMud, 80), Pair(TerrainBlockMaterialId.HumusSoil, 80));
            AssertEntries(biomes[15].Entries, Pair(TerrainBlockMaterialId.PermafrostSoil, 144), Pair(TerrainBlockMaterialId.TundraSoil, 64), Pair(TerrainBlockMaterialId.Gravel, 48));
            AssertEntries(biomes[16].Entries, Pair(TerrainBlockMaterialId.AlpineScree, 160), Pair(TerrainBlockMaterialId.Gravel, 64), Pair(TerrainBlockMaterialId.MountainLoam, 32));
            AssertEntries(biomes[17].Entries, Pair(TerrainBlockMaterialId.VolcanicAshSoil, 112), Pair(TerrainBlockMaterialId.Scoria, 80), Pair(TerrainBlockMaterialId.Basalt, 64));

            AssertGeologyEntries(geologies[0], new[] { Pair(TerrainBlockMaterialId.Gravel, 160), Pair(TerrainBlockMaterialId.CompactedSediment, 96) }, new[] { Pair(TerrainBlockMaterialId.CompactedSediment, 192), Pair(TerrainBlockMaterialId.Conglomerate, 64) });
            AssertGeologyEntries(geologies[1], new[] { Pair(TerrainBlockMaterialId.Sandstone, 224), Pair(TerrainBlockMaterialId.Conglomerate, 32) }, new[] { Pair(TerrainBlockMaterialId.Sandstone, 224), Pair(TerrainBlockMaterialId.Conglomerate, 32) });
            AssertGeologyEntries(geologies[2], new[] { Pair(TerrainBlockMaterialId.Limestone, 224), Pair(TerrainBlockMaterialId.Dolomite, 32) }, new[] { Pair(TerrainBlockMaterialId.Limestone, 224), Pair(TerrainBlockMaterialId.Dolomite, 32) });
            AssertGeologyEntries(geologies[3], new[] { Pair(TerrainBlockMaterialId.Mudstone, 144), Pair(TerrainBlockMaterialId.Slate, 112) }, new[] { Pair(TerrainBlockMaterialId.Mudstone, 144), Pair(TerrainBlockMaterialId.Slate, 112) });
            AssertGeologyEntries(geologies[4], new[] { Pair(TerrainBlockMaterialId.Granite, 192), Pair(TerrainBlockMaterialId.Granodiorite, 64) }, new[] { Pair(TerrainBlockMaterialId.Granite, 192), Pair(TerrainBlockMaterialId.Granodiorite, 64) });
            AssertGeologyEntries(geologies[5], new[] { Pair(TerrainBlockMaterialId.Schist, 96), Pair(TerrainBlockMaterialId.Gneiss, 96), Pair(TerrainBlockMaterialId.Quartzite, 64) }, new[] { Pair(TerrainBlockMaterialId.Schist, 96), Pair(TerrainBlockMaterialId.Gneiss, 96), Pair(TerrainBlockMaterialId.Quartzite, 64) });
            AssertGeologyEntries(geologies[6], new[] { Pair(TerrainBlockMaterialId.Basalt, 128), Pair(TerrainBlockMaterialId.Andesite, 64), Pair(TerrainBlockMaterialId.Tuff, 64) }, new[] { Pair(TerrainBlockMaterialId.Basalt, 128), Pair(TerrainBlockMaterialId.Andesite, 64), Pair(TerrainBlockMaterialId.Tuff, 64) });
            AssertGeologyEntries(geologies[7], new[] { Pair(TerrainBlockMaterialId.Serpentinite, 160), Pair(TerrainBlockMaterialId.Peridotite, 96) }, new[] { Pair(TerrainBlockMaterialId.Serpentinite, 160), Pair(TerrainBlockMaterialId.Peridotite, 96) });
        }

        /// <summary>
        /// 全26行がBlobへ構築され、Ground候補はGroundBlock、Wall候補は移動遮断かつ水密なWallBlockへ解決されることを保証します。
        /// </summary>
        [Test]
        public void RuntimeCatalog_ResolvesEveryEntryToExpectedBlockKindAndFlags()
        {
            BuildDefaultPaletteCatalog();

            ref TerrainBlockPaletteCatalogBlob root = ref paletteCatalog.Value;
            Assert.That(root.BiomeSurfacePalettes.Length, Is.EqualTo(18));
            Assert.That(root.GeologyPalettes.Length, Is.EqualTo(8));
            for (byte biomeValue = BiomeStableIds.First;
                 biomeValue <= BiomeStableIds.Last;
                 biomeValue++)
            {
                Assert.That(
                    TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                        paletteCatalog,
                        (BiomeId)biomeValue,
                        out TerrainBlockPaletteRange range),
                    Is.True);
                AssertRuntimeRange(range, DefinitionKind.GroundBlock, false);
            }

            for (byte geologyValue = (byte)GeologyClass.UnconsolidatedSediment;
                 geologyValue <= (byte)GeologyClass.UltramaficRock;
                 geologyValue++)
            {
                GeologyClass geologyClass = (GeologyClass)geologyValue;
                Assert.That(
                    TerrainBlockPaletteCatalogLookup.TryGetGeologyRange(
                        paletteCatalog,
                        geologyClass,
                        false,
                        out TerrainBlockPaletteRange groundRange),
                    Is.True);
                Assert.That(
                    TerrainBlockPaletteCatalogLookup.TryGetGeologyRange(
                        paletteCatalog,
                        geologyClass,
                        true,
                        out TerrainBlockPaletteRange wallRange),
                    Is.True);
                AssertRuntimeRange(groundRange, DefinitionKind.GroundBlock, false);
                AssertRuntimeRange(wallRange, DefinitionKind.WallBlock, true);
            }
        }

        /// <summary>
        /// 入力行を逆順にしても、Blobの行順と候補内容が安定ID順へ正規化されることを保証します。
        /// </summary>
        [Test]
        public void RuntimeCatalog_IsIndependentFromDefinitionRegistrationOrder()
        {
            BiomeSurfaceBlockPaletteDefinition[] biomes =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            GeologyBlockPaletteDefinition[] geologies =
                TerrainBlockPaletteDefinitions.CreateGeologyDefaults();
            Array.Reverse(biomes);
            Array.Reverse(geologies);

            Assert.That(
                TerrainBlockPaletteCatalogBuilder.TryBuild(
                    terrainCatalog,
                    materialCatalog,
                    biomes,
                    geologies,
                    out paletteCatalog),
                Is.True);
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                    paletteCatalog,
                    BiomeId.TemperateGrassland,
                    out TerrainBlockPaletteRange grasslandRange),
                Is.True);
            AssertRuntimeEntry(
                grasslandRange,
                0,
                TerrainBlockMaterialId.GrassLoam,
                160,
                DefinitionKind.GroundBlock);
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetGeologyRange(
                    paletteCatalog,
                    GeologyClass.UltramaficRock,
                    true,
                    out TerrainBlockPaletteRange ultramaficWallRange),
                Is.True);
            AssertRuntimeEntry(
                ultramaficWallRange,
                1,
                TerrainBlockMaterialId.Peridotite,
                96,
                DefinitionKind.WallBlock);
        }

        /// <summary>
        /// 重み合計違反、行内材質重複、Biome行重複、非泥炭候補なしの入力を構築時に拒否することを保証します。
        /// </summary>
        [Test]
        public void Builder_RejectsInvalidWeightDuplicateMaterialAndDuplicateStableRow()
        {
            BiomeSurfaceBlockPaletteDefinition[] invalidWeightBiomes =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            invalidWeightBiomes[0].Entries[0].Weight = 159;
            AssertBuildRejected(invalidWeightBiomes, TerrainBlockPaletteDefinitions.CreateGeologyDefaults());

            GeologyBlockPaletteDefinition[] duplicateMaterialGeologies =
                TerrainBlockPaletteDefinitions.CreateGeologyDefaults();
            duplicateMaterialGeologies[0].WallEntries[1].MaterialId =
                duplicateMaterialGeologies[0].WallEntries[0].MaterialId;
            AssertBuildRejected(
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults(),
                duplicateMaterialGeologies);

            BiomeSurfaceBlockPaletteDefinition[] duplicateBiomeRows =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            duplicateBiomeRows[17].BiomeId = duplicateBiomeRows[0].BiomeId;
            AssertBuildRejected(duplicateBiomeRows, TerrainBlockPaletteDefinitions.CreateGeologyDefaults());

            BiomeSurfaceBlockPaletteDefinition[] peatOnlyBiomes =
                TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults();
            peatOnlyBiomes[0].Entries = new[]
            {
                new TerrainBlockPaletteWeight(TerrainBlockMaterialId.PeatSoil, 256)
            };
            AssertBuildRejected(peatOnlyBiomes, TerrainBlockPaletteDefinitions.CreateGeologyDefaults());
        }

        /// <summary>
        /// 自然Wall候補のBlockから移動遮断・水密属性を欠かせた場合、重みと分類が正しくても構築を拒否することを保証します。
        /// </summary>
        [Test]
        public void Builder_RejectsNaturalWallWithoutBlockingAndWatertightFlags()
        {
            Assert.That(
                BuiltInTerrainBlockDefinitions.TryCreateTerrainDefinitions(
                    definitionCatalog,
                    out TerrainBlockDefinitionBuildEntry[] terrainDefinitions),
                Is.True);
            Assert.That(
                TerrainBlockMaterialDefinitions.TryGetDefinition(
                    TerrainBlockMaterialDefinitions.CreateDefault(),
                    TerrainBlockMaterialId.Sandstone,
                    out TerrainBlockMaterialDefinition sandstone),
                Is.True);

            bool wallFound = false;
            // 対象材質のWall GUIDだけを変更し、別材質やGroundの検証結果を混在させません。
            for (int definitionIndex = 0;
                 definitionIndex < terrainDefinitions.Length;
                 definitionIndex++)
            {
                if (!terrainDefinitions[definitionIndex].Guid.Equals(sandstone.WallGuid))
                {
                    continue;
                }

                TerrainBlockDefinitionBuildEntry invalidWall = terrainDefinitions[definitionIndex];
                invalidWall.Flags = TerrainBlockDefinitionFlags.FacilityBoundary;
                terrainDefinitions[definitionIndex] = invalidWall;
                wallFound = true;
                break;
            }

            Assert.That(wallFound, Is.True, "Sandstone Wallの地形入力を見つけられませんでした。");
            Assert.That(
                TerrainBlockCatalogBuilder.TryBuild(
                    definitionCatalog,
                    terrainDefinitions,
                    out BlobAssetReference<TerrainBlockCatalogBlob> invalidTerrainCatalog),
                Is.True,
                "属性不足をパレット側で検出するための地形カタログを構築できませんでした。");
            try
            {
                Assert.That(
                    TerrainBlockPaletteCatalogBuilder.TryBuild(
                        invalidTerrainCatalog,
                        materialCatalog,
                        TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults(),
                        TerrainBlockPaletteDefinitions.CreateGeologyDefaults(),
                        out BlobAssetReference<TerrainBlockPaletteCatalogBlob> rejectedCatalog),
                    Is.False);
                Assert.That(rejectedCatalog.IsCreated, Is.False);
            }
            finally
            {
                // このテストだけが所有する代替Blobを、成功・失敗にかかわらず必ず解放します。
                invalidTerrainCatalog.Dispose();
            }
        }

        /// <summary>
        /// 予約ID、Range外相対位置、空カタログを安全に拒否し、別行の候補を誤取得しないことを保証します。
        /// </summary>
        [Test]
        public void Lookup_RejectsReservedIdsAndOutOfRangeEntry()
        {
            BuildDefaultPaletteCatalog();
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                    paletteCatalog,
                    BiomeId.None,
                    out _),
                Is.False);
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetGeologyRange(
                    paletteCatalog,
                    GeologyClass.None,
                    false,
                    out _),
                Is.False);
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                    paletteCatalog,
                    BiomeId.TemperateGrassland,
                    out TerrainBlockPaletteRange range),
                Is.True);
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetEntry(
                    paletteCatalog,
                    range,
                    range.Count,
                    out _),
                Is.False);
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                    default,
                    BiomeId.TemperateGrassland,
                    out _),
                Is.False);
        }

        /// <summary>本番固定入力からテスト対象のパレットBlobを構築します。</summary>
        private void BuildDefaultPaletteCatalog()
        {
            Assert.That(
                TerrainBlockPaletteCatalogBuilder.TryBuild(
                    terrainCatalog,
                    materialCatalog,
                    TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults(),
                    TerrainBlockPaletteDefinitions.CreateGeologyDefaults(),
                    out paletteCatalog),
                Is.True,
                "固定Blockパレットを構築できませんでした。");
        }

        /// <summary>1パレットが合計256かつ材質重複なしであることを確認します。</summary>
        /// <param name="entries">確認する固定入力の候補配列です。</param>
        private static void AssertNormalizedUniquePalette(TerrainBlockPaletteWeight[] entries)
        {
            Assert.That(entries, Is.Not.Null.And.Not.Empty);
            int total = 0;
            var materialIds = new HashSet<TerrainBlockMaterialId>();
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                TerrainBlockPaletteWeight entry = entries[entryIndex];
                Assert.That(entry.Weight, Is.GreaterThan(0));
                Assert.That(materialIds.Add(entry.MaterialId), Is.True);
                total += entry.Weight;
            }

            Assert.That(total, Is.EqualTo(TerrainBlockPaletteDefinitions.RequiredWeightTotal));
        }

        /// <summary>地質行のGroundとWall候補を仕様上の期待配列と比較します。</summary>
        /// <param name="definition">確認する地質入力行です。</param>
        /// <param name="expectedGround">期待する露出Ground候補です。</param>
        /// <param name="expectedWall">期待する自然Wall候補です。</param>
        private static void AssertGeologyEntries(
            GeologyBlockPaletteDefinition definition,
            TerrainBlockPaletteWeight[] expectedGround,
            TerrainBlockPaletteWeight[] expectedWall)
        {
            AssertEntries(definition.GroundEntries, expectedGround);
            AssertEntries(definition.WallEntries, expectedWall);
        }

        /// <summary>入力候補の材質順と重みを期待配列へ1件ずつ照合します。</summary>
        /// <param name="actual">固定表から作成された候補です。</param>
        /// <param name="expected">仕様表をテスト側へ記録した期待候補です。</param>
        private static void AssertEntries(
            TerrainBlockPaletteWeight[] actual,
            params TerrainBlockPaletteWeight[] expected)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int entryIndex = 0; entryIndex < expected.Length; entryIndex++)
            {
                Assert.That(actual[entryIndex].MaterialId, Is.EqualTo(expected[entryIndex].MaterialId));
                Assert.That(actual[entryIndex].Weight, Is.EqualTo(expected[entryIndex].Weight));
            }
        }

        /// <summary>テスト期待値として材質IDと重みの組を作ります。</summary>
        /// <param name="materialId">期待する安定材質IDです。</param>
        /// <param name="weight">期待する整数重みです。</param>
        /// <returns>比較へ使う材質候補値です。</returns>
        private static TerrainBlockPaletteWeight Pair(
            TerrainBlockMaterialId materialId,
            ushort weight)
        {
            return new TerrainBlockPaletteWeight(materialId, weight);
        }

        /// <summary>Runtime Rangeの全候補について重み合計、Block分類、Wall必須属性を確認します。</summary>
        /// <param name="range">カタログから取得した候補範囲です。</param>
        /// <param name="expectedKind">全候補に期待するGroundまたはWall分類です。</param>
        /// <param name="requiresNaturalWallFlags">移動遮断・水密属性を要求する場合はtrueです。</param>
        private void AssertRuntimeRange(
            TerrainBlockPaletteRange range,
            DefinitionKind expectedKind,
            bool requiresNaturalWallFlags)
        {
            int weightTotal = 0;
            var materialIds = new HashSet<TerrainBlockMaterialId>();
            for (int relativeIndex = 0; relativeIndex < range.Count; relativeIndex++)
            {
                Assert.That(
                    TerrainBlockPaletteCatalogLookup.TryGetEntry(
                        paletteCatalog,
                        range,
                        relativeIndex,
                        out TerrainBlockPaletteEntryBlob entry),
                    Is.True);
                Assert.That(materialIds.Add(entry.MaterialId), Is.True);
                Assert.That(
                    TerrainBlockCatalogLookup.TryGetDefinition(
                        terrainCatalog,
                        entry.BlockRuntimeId,
                        out TerrainBlockDefinitionBlob block),
                    Is.True);
                Assert.That(block.Kind, Is.EqualTo(expectedKind));
                if (requiresNaturalWallFlags)
                {
                    TerrainBlockDefinitionFlags requiredFlags =
                        TerrainBlockDefinitionFlags.BlocksMovement
                        | TerrainBlockDefinitionFlags.Watertight;
                    Assert.That(block.Flags & requiredFlags, Is.EqualTo(requiredFlags));
                }

                weightTotal += entry.Weight;
            }

            Assert.That(weightTotal, Is.EqualTo(TerrainBlockPaletteDefinitions.RequiredWeightTotal));
        }

        /// <summary>Runtime候補1件の材質ID、重み、Block分類を確認します。</summary>
        /// <param name="range">候補が属するカタログ範囲です。</param>
        /// <param name="relativeIndex">範囲内の0始まり位置です。</param>
        /// <param name="expectedMaterialId">期待する安定材質IDです。</param>
        /// <param name="expectedWeight">期待する整数重みです。</param>
        /// <param name="expectedKind">期待するGroundまたはWall分類です。</param>
        private void AssertRuntimeEntry(
            TerrainBlockPaletteRange range,
            int relativeIndex,
            TerrainBlockMaterialId expectedMaterialId,
            ushort expectedWeight,
            DefinitionKind expectedKind)
        {
            Assert.That(
                TerrainBlockPaletteCatalogLookup.TryGetEntry(
                    paletteCatalog,
                    range,
                    relativeIndex,
                    out TerrainBlockPaletteEntryBlob entry),
                Is.True);
            Assert.That(entry.MaterialId, Is.EqualTo(expectedMaterialId));
            Assert.That(entry.Weight, Is.EqualTo(expectedWeight));
            Assert.That(
                TerrainBlockCatalogLookup.TryGetDefinition(
                    terrainCatalog,
                    entry.BlockRuntimeId,
                    out TerrainBlockDefinitionBlob block),
                Is.True);
            Assert.That(block.Kind, Is.EqualTo(expectedKind));
        }

        /// <summary>不正入力がfalseを返し、部分的なPersistent Blobを残さないことを確認します。</summary>
        /// <param name="biomes">Builderへ渡すBiome入力です。</param>
        /// <param name="geologies">Builderへ渡す地質入力です。</param>
        private void AssertBuildRejected(
            BiomeSurfaceBlockPaletteDefinition[] biomes,
            GeologyBlockPaletteDefinition[] geologies)
        {
            Assert.That(
                TerrainBlockPaletteCatalogBuilder.TryBuild(
                    terrainCatalog,
                    materialCatalog,
                    biomes,
                    geologies,
                    out BlobAssetReference<TerrainBlockPaletteCatalogBlob> rejectedCatalog),
                Is.False);
            Assert.That(rejectedCatalog.IsCreated, Is.False);
        }
    }
}
