// このファイルは、TERRAIN-01C5-Aで追加した新18Biome Definitionの安定ID、固定表、検証失敗条件を確認します。
// C5-Cの最終分類Jobが読む新18Biome設定契約をEditModeで固定します。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 新18BiomeのID1〜18、代表範囲、軸重み、地質適合度、湿原Host、登録順非依存を検証します。
    /// </summary>
    public sealed class BiomeDefinitionCatalogTests
    {
        /// <summary>
        /// 独立したECS設定の共有既定値を作り、完全な18件として検証を通ることを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_ContainValidEighteenBiomeCatalog()
        {
            BiomeClassificationSettings catalog = BiomeDefinitionCatalog.CreateDefault();

            Assert.That(BiomeDefinitionCatalog.ValidateSettings(catalog), Is.True);
            Assert.That(catalog.Definitions.Length, Is.EqualTo(18));
            Assert.That(catalog.VariationCellSizeInTiles, Is.EqualTo(256));
            Assert.That(catalog.VariationOctaveCount, Is.EqualTo(2));
            Assert.That(catalog.FreshwaterMarshHostIds.Length, Is.EqualTo(7));
            Assert.That(catalog.SwampForestHostIds.Length, Is.EqualTo(4));

            // 旧BiomeIdの列挙値や登録順を新IDの意味へ使わず、数値契約1〜18が欠落なく検索できることを確認します。
            for (byte stableId = BiomeStableIds.First; stableId <= BiomeStableIds.Last; stableId++)
            {
                Assert.That(
                    BiomeDefinitionCatalog.TryGetDefinition(catalog, stableId, out BiomeDefinition definition),
                    Is.True,
                    $"Stable ID {stableId} must exist.");
                Assert.That(definition.StableId, Is.EqualTo(stableId));
            }
        }

        /// <summary>
        /// 仕様表の英語識別子へID1〜18が順番どおり固定され、0が未設定予約値のままであることを保証します。
        /// </summary>
        [Test]
        public void StableIds_MatchTerrain01C5Contract()
        {
            Assert.That(BiomeStableIds.None, Is.EqualTo(0));
            Assert.That(BiomeStableIds.TemperateGrassland, Is.EqualTo(1));
            Assert.That(BiomeStableIds.TropicalSavannaWoodland, Is.EqualTo(2));
            Assert.That(BiomeStableIds.TemperateDeciduousForest, Is.EqualTo(3));
            Assert.That(BiomeStableIds.WarmTemperateEvergreenForest, Is.EqualTo(4));
            Assert.That(BiomeStableIds.TemperateMontaneBambooForest, Is.EqualTo(5));
            Assert.That(BiomeStableIds.TropicalRainforest, Is.EqualTo(6));
            Assert.That(BiomeStableIds.TropicalSeasonalForest, Is.EqualTo(7));
            Assert.That(BiomeStableIds.SubtropicalOceanicForest, Is.EqualTo(8));
            Assert.That(BiomeStableIds.BorealConiferForest, Is.EqualTo(9));
            Assert.That(BiomeStableIds.Steppe, Is.EqualTo(10));
            Assert.That(BiomeStableIds.DryShrublandWoodland, Is.EqualTo(11));
            Assert.That(BiomeStableIds.CactusDesertDryPlateau, Is.EqualTo(12));
            Assert.That(BiomeStableIds.HotDesert, Is.EqualTo(13));
            Assert.That(BiomeStableIds.FreshwaterMarshFloodplain, Is.EqualTo(14));
            Assert.That(BiomeStableIds.SwampForest, Is.EqualTo(15));
            Assert.That(BiomeStableIds.TundraPolarBarrens, Is.EqualTo(16));
            Assert.That(BiomeStableIds.AlpineRockland, Is.EqualTo(17));
            Assert.That(BiomeStableIds.VolcanicGeothermal, Is.EqualTo(18));
        }

        /// <summary>
        /// 18件のVariationSaltが互いに一意で、既存の地形・水文・資源用Saltを再利用していないことを保証します。
        /// </summary>
        [Test]
        public void VariationSalts_AreUniqueAndSeparatedFromOtherGenerationPurposes()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            var otherPurposeSalts = new HashSet<ulong>();
            var variationSalts = new HashSet<ulong>();

            // テスト内だけで定数フィールドを列挙し、用途Saltが将来追加された場合も意図しない共有を検出します。
            FieldInfo[] purposeFields = typeof(WorldGenerationPurposeSalts).GetFields(
                BindingFlags.Public | BindingFlags.Static);
            for (int index = 0; index < purposeFields.Length; index++)
            {
                FieldInfo field = purposeFields[index];
                if (field.IsLiteral && field.FieldType == typeof(ulong))
                {
                    otherPurposeSalts.Add((ulong)field.GetRawConstantValue());
                }
            }

            for (int index = 0; index < settings.Definitions.Length; index++)
            {
                ulong variationSalt = settings.Definitions[index].VariationSalt;
                Assert.That(variationSalt, Is.Not.Zero);
                Assert.That(variationSalts.Add(variationSalt), Is.True);
                Assert.That(otherPurposeSalts.Contains(variationSalt), Is.False);
            }
        }

        /// <summary>
        /// 全18行の軸重みを仕様表と照合し、通常16行が256、水文特殊2行が0であることを保証します。
        /// </summary>
        [Test]
        public void DefaultDefinitions_MatchAxisWeightTable()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            byte[,] expectedWeights =
            {
                { 48, 48, 32, 72, 40, 16, 0, 0 },
                { 64, 56, 24, 64, 24, 24, 0, 0 },
                { 48, 56, 32, 72, 24, 24, 0, 0 },
                { 56, 56, 24, 72, 24, 24, 0, 0 },
                { 48, 48, 56, 64, 24, 16, 0, 0 },
                { 64, 64, 16, 72, 16, 24, 0, 0 },
                { 64, 64, 16, 64, 24, 24, 0, 0 },
                { 48, 48, 16, 56, 16, 16, 0, 56 },
                { 64, 48, 40, 56, 24, 24, 0, 0 },
                { 48, 72, 32, 64, 24, 16, 0, 0 },
                { 56, 64, 24, 64, 24, 24, 0, 0 },
                { 48, 72, 40, 32, 40, 24, 0, 0 },
                { 80, 80, 24, 24, 24, 24, 0, 0 },
                { 0, 0, 0, 0, 0, 0, 0, 0 },
                { 0, 0, 0, 0, 0, 0, 0, 0 },
                { 96, 16, 48, 48, 32, 16, 0, 0 },
                { 24, 8, 88, 16, 88, 32, 0, 0 },
                { 0, 0, 24, 16, 40, 72, 104, 0 }
            };

            for (byte stableId = BiomeStableIds.First; stableId <= BiomeStableIds.Last; stableId++)
            {
                Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                    settings,
                    stableId,
                    out BiomeDefinition definition), Is.True);
                BiomeAxisWeights actual = definition.AxisWeights;
                int row = stableId - BiomeStableIds.First;
                Assert.That(actual.TerrainTemperature, Is.EqualTo(expectedWeights[row, 0]));
                Assert.That(actual.BaseMoisture, Is.EqualTo(expectedWeights[row, 1]));
                Assert.That(actual.Elevation, Is.EqualTo(expectedWeights[row, 2]));
                Assert.That(actual.WoodyVegetation, Is.EqualTo(expectedWeights[row, 3]));
                Assert.That(actual.TerrainRuggedness, Is.EqualTo(expectedWeights[row, 4]));
                Assert.That(actual.Geology, Is.EqualTo(expectedWeights[row, 5]));
                Assert.That(actual.VolcanicActivity, Is.EqualTo(expectedWeights[row, 6]));
                Assert.That(actual.CoastalInfluence, Is.EqualTo(expectedWeights[row, 7]));
            }
        }

        /// <summary>
        /// 全18行×8地質の適合度を仕様表と照合し、火山以外も列ずれなく保持されることを保証します。
        /// </summary>
        [Test]
        public void DefaultDefinitions_MatchGeologyFitTable()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            ushort[,] expectedFits =
            {
                { 58981, 32767, 45874, 45874, 19660, 19660, 45874, 32767 },
                { 45874, 45874, 32767, 32767, 32767, 32767, 45874, 19660 },
                { 58981, 32767, 45874, 45874, 32767, 32767, 45874, 19660 },
                { 45874, 32767, 32767, 45874, 45874, 45874, 58981, 19660 },
                { 19660, 32767, 32767, 45874, 45874, 45874, 58981, 19660 },
                { 45874, 32767, 45874, 45874, 32767, 32767, 58981, 6553 },
                { 45874, 45874, 32767, 45874, 32767, 32767, 58981, 19660 },
                { 45874, 32767, 45874, 32767, 32767, 32767, 58981, 32767 },
                { 32767, 32767, 19660, 32767, 58981, 58981, 45874, 32767 },
                { 58981, 45874, 45874, 58981, 19660, 32767, 32767, 19660 },
                { 32767, 45874, 32767, 32767, 58981, 45874, 45874, 32767 },
                { 19660, 58981, 45874, 32767, 58981, 45874, 58981, 32767 },
                { 58981, 58981, 45874, 32767, 45874, 32767, 45874, 32767 },
                { 58981, 45874, 32767, 58981, 19660, 19660, 32767, 19660 },
                { 58981, 32767, 32767, 58981, 19660, 19660, 45874, 19660 },
                { 45874, 32767, 32767, 45874, 58981, 58981, 45874, 32767 },
                { 19660, 32767, 45874, 32767, 58981, 58981, 58981, 58981 },
                { 0, 0, 0, 0, 0, 0, 65535, 0 }
            };

            for (byte stableId = BiomeStableIds.First; stableId <= BiomeStableIds.Last; stableId++)
            {
                Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                    settings,
                    stableId,
                    out BiomeDefinition definition), Is.True);
                int row = stableId - BiomeStableIds.First;
                for (int geologyIndex = 0; geologyIndex < BiomeDefinitionCatalog.RequiredGeologyFitCount; geologyIndex++)
                {
                    GeologyClass geologyClass = (GeologyClass)(geologyIndex + 1);
                    Assert.That(
                        definition.GetGeologyFit(geologyClass),
                        Is.EqualTo(expectedFits[row, geologyIndex]),
                        $"Biome {stableId}, geology {geologyClass}");
                }
            }
        }

        /// <summary>
        /// 代表値の転記で境界ミスが起きやすい海洋性林、湿原、火山、高山の範囲と役割を個別に保証します。
        /// </summary>
        [Test]
        public void DefaultDefinitions_MatchSpecialRangeAndRoleContracts()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.SubtropicalOceanicForest,
                out BiomeDefinition oceanicForest), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.FreshwaterMarshFloodplain,
                out BiomeDefinition marsh), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.SwampForest,
                out BiomeDefinition swampForest), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.AlpineRockland,
                out BiomeDefinition alpine), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.VolcanicGeothermal,
                out BiomeDefinition volcanic), Is.True);

            AssertRange(oceanicForest.CoastalInfluence, 52428, 58981, 65535, 65535);
            Assert.That(
                oceanicForest.Flags & BiomeDefinitionFlags.RequiresStrongCoastalInfluence,
                Is.Not.EqualTo(BiomeDefinitionFlags.None));
            AssertRange(marsh.WoodyVegetation, 0, 0, 39320, 39320);
            AssertRange(swampForest.WoodyVegetation, 39321, 39321, 65535, 65535);
            Assert.That(marsh.AxisWeights.CalculateTotal(), Is.Zero);
            Assert.That(swampForest.AxisWeights.CalculateTotal(), Is.Zero);
            AssertRange(alpine.Elevation, 43000, 54000, 65535, 65535);
            AssertRange(volcanic.VolcanicActivity, 39321, 49152, 52427, 52427);
            Assert.That(
                volcanic.Flags & BiomeDefinitionFlags.RequiresActiveVolcanism,
                Is.Not.EqualTo(BiomeDefinitionFlags.None));
        }

        /// <summary>
        /// Definitionの登録順だけを反転し、安定ID検索と完全性検証が配列順へ依存しないことを保証します。
        /// </summary>
        [Test]
        public void Validation_DefinitionOrderDoesNotChangeResult()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            FixedList4096Bytes<BiomeDefinition> reversed = default;

            // 分類候補の優先順位を登録順から得ないため、逆順でも同じ設定として受理される必要があります。
            for (int index = settings.Definitions.Length - 1; index >= 0; index--)
            {
                reversed.Add(settings.Definitions[index]);
            }

            settings.Definitions = reversed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(settings), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.TemperateGrassland,
                out BiomeDefinition firstId), Is.True);
            Assert.That(firstId.StableId, Is.EqualTo(BiomeStableIds.TemperateGrassland));
        }

        /// <summary>
        /// ID・Salt重複、範囲逆転、重み不足、地質欠落、Host混入、Variation設定破損をそれぞれ拒否します。
        /// </summary>
        [Test]
        public void Validation_RejectsIncompleteOrAmbiguousDefinitions()
        {
            BiomeClassificationSettings duplicateId = BiomeDefinitionCatalog.CreateDefault();
            BiomeDefinition changed = duplicateId.Definitions[0];
            changed.StableId = BiomeStableIds.TropicalSavannaWoodland;
            duplicateId.Definitions[0] = changed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(duplicateId), Is.False);

            BiomeClassificationSettings duplicateSalt = BiomeDefinitionCatalog.CreateDefault();
            changed = duplicateSalt.Definitions[1];
            changed.VariationSalt = duplicateSalt.Definitions[0].VariationSalt;
            duplicateSalt.Definitions[1] = changed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(duplicateSalt), Is.False);

            BiomeClassificationSettings reversedRange = BiomeDefinitionCatalog.CreateDefault();
            changed = reversedRange.Definitions[0];
            changed.TerrainTemperature.PreferredMin = 40000;
            changed.TerrainTemperature.PreferredMax = 30000;
            reversedRange.Definitions[0] = changed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(reversedRange), Is.False);

            BiomeClassificationSettings wrongWeight = BiomeDefinitionCatalog.CreateDefault();
            changed = wrongWeight.Definitions[0];
            changed.AxisWeights.TerrainTemperature--;
            wrongWeight.Definitions[0] = changed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(wrongWeight), Is.False);

            BiomeClassificationSettings missingGeology = BiomeDefinitionCatalog.CreateDefault();
            changed = missingGeology.Definitions[0];
            changed.GeologyFits.RemoveAt(changed.GeologyFits.Length - 1);
            missingGeology.Definitions[0] = changed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(missingGeology), Is.False);

            BiomeClassificationSettings invalidHost = BiomeDefinitionCatalog.CreateDefault();
            invalidHost.FreshwaterMarshHostIds[0] = BiomeStableIds.FreshwaterMarshFloodplain;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(invalidHost), Is.False);

            BiomeClassificationSettings invalidSpawn = BiomeDefinitionCatalog.CreateDefault();
            changed = invalidSpawn.Definitions[8];
            changed.Flags |= BiomeDefinitionFlags.AllowsInitialSpawn;
            invalidSpawn.Definitions[8] = changed;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(invalidSpawn), Is.False);

            BiomeClassificationSettings invalidVariation = BiomeDefinitionCatalog.CreateDefault();
            invalidVariation.VariationCellSizeInTiles = 128;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(invalidVariation), Is.False);

            invalidVariation = BiomeDefinitionCatalog.CreateDefault();
            invalidVariation.VariationOctaveCount = 3;
            Assert.That(BiomeDefinitionCatalog.ValidateSettings(invalidVariation), Is.False);
        }

        /// <summary>
        /// 軸範囲の4境界を個別に比較し、失敗時に転記箇所を特定しやすくします。
        /// </summary>
        /// <param name="actual">Definitionから取得した実際の軸範囲です。</param>
        /// <param name="allowedMin">期待する候補許容範囲の最小値です。</param>
        /// <param name="preferredMin">期待する代表範囲の最小値です。</param>
        /// <param name="preferredMax">期待する代表範囲の最大値です。</param>
        /// <param name="allowedMax">期待する候補許容範囲の最大値です。</param>
        private static void AssertRange(
            BiomeAxisRange actual,
            ushort allowedMin,
            ushort preferredMin,
            ushort preferredMax,
            ushort allowedMax)
        {
            Assert.That(actual.AllowedMin, Is.EqualTo(allowedMin));
            Assert.That(actual.PreferredMin, Is.EqualTo(preferredMin));
            Assert.That(actual.PreferredMax, Is.EqualTo(preferredMax));
            Assert.That(actual.AllowedMax, Is.EqualTo(allowedMax));
        }
    }
}
