// このファイルは、TERRAIN-01C2の固定地質Definition、Voronoiサイト、標高別抽選、負座標、状態ハッシュを検証します。
// SceneやGameObjectを作らず、チャンク生成Jobと同じ純粋計算が設定・生成順・配列順に依存しないことを保証します。

using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 8地質の基礎特性、512タイル地域、5×5サイト探索、決定論的な分類をEditModeで検証します。
    /// C2-Bの表示・分布監査とは分け、Simulationの固定生成契約だけを対象にします。
    /// </summary>
    public sealed class DeterministicGeologyGeneratorTests
    {
        /// <summary>
        /// 確定仕様から既定設定を作り、地域寸法、ワープ、ジッター、全8分類の特性と重みを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_MatchTerrain01C2Contract()
        {
            WorldGenerationSettings settings = CreateSettings("geology-defaults");
            GeologyGenerationSettings geology = settings.Geology;

            Assert.That(DeterministicGeologyGenerator.ValidateSettings(geology), Is.True);
            Assert.That(geology.MacroCellSizeInTiles, Is.EqualTo(512));
            Assert.That(geology.WarpCellSizeInTiles, Is.EqualTo(256));
            Assert.That(geology.WarpOctaveCount, Is.EqualTo(2));
            Assert.That(geology.WarpAmplitudeInTiles, Is.EqualTo(64));
            Assert.That(geology.SiteNegativeJitterInTiles, Is.EqualTo(192));
            Assert.That(geology.SitePositiveJitterInTiles, Is.EqualTo(191));
            Assert.That(geology.SiteSearchRadiusInCells, Is.EqualTo(2));
            Assert.That(geology.Definitions.Length, Is.EqualTo(8));

            AssertDefinition(
                geology,
                GeologyClass.UnconsolidatedSediment,
                45874,
                58981,
                6553,
                GeologyDefinitionFlags.None,
                1800,
                384,
                384,
                256,
                64,
                16);
            AssertDefinition(
                geology,
                GeologyClass.Sandstone,
                45874,
                45874,
                32767,
                GeologyDefinitionFlags.None,
                1500,
                256,
                256,
                256,
                192,
                128);
            AssertDefinition(
                geology,
                GeologyClass.Limestone,
                58981,
                45874,
                45874,
                GeologyDefinitionFlags.SupportsKarst,
                1200,
                192,
                256,
                256,
                192,
                128);
            AssertDefinition(
                geology,
                GeologyClass.MudstoneSlate,
                6553,
                19660,
                32767,
                GeologyDefinitionFlags.None,
                1400,
                384,
                320,
                256,
                192,
                128);
            AssertDefinition(
                geology,
                GeologyClass.GraniticRock,
                19660,
                19660,
                58981,
                GeologyDefinitionFlags.None,
                1500,
                128,
                160,
                256,
                384,
                384);
            AssertDefinition(
                geology,
                GeologyClass.MetamorphicRock,
                19660,
                19660,
                58981,
                GeologyDefinitionFlags.None,
                1100,
                128,
                128,
                192,
                384,
                512);
            AssertDefinition(
                geology,
                GeologyClass.VolcanicRock,
                45874,
                45874,
                45874,
                GeologyDefinitionFlags.SupportsActiveVolcanism,
                1100,
                256,
                256,
                256,
                320,
                320);
            AssertDefinition(
                geology,
                GeologyClass.UltramaficRock,
                32767,
                19660,
                58981,
                GeologyDefinitionFlags.None,
                400,
                128,
                128,
                192,
                320,
                384);
        }

        /// <summary>
        /// Definition順だけを逆転させた設定を準備し、安定ID順の抽選結果と基礎特性が変わらないことを保証します。
        /// </summary>
        [Test]
        public void Generation_DefinitionArrayOrderDoesNotChangeResult()
        {
            WorldGenerationSettings firstSettings = CreateSettings("geology-definition-order");
            WorldGenerationSettings secondSettings = firstSettings;
            GeologyGenerationSettings reorderedGeology = secondSettings.Geology;
            FixedList512Bytes<GeologyDefinition> reversedDefinitions = default;

            // 設定配列を逆順にしても、抽選累積順はGeologyClassの安定ID昇順を使う必要があります。
            for (int index = reorderedGeology.Definitions.Length - 1; index >= 0; index--)
            {
                reversedDefinitions.Add(reorderedGeology.Definitions[index]);
            }

            reorderedGeology.Definitions = reversedDefinitions;
            secondSettings.Geology = reorderedGeology;
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(reorderedGeology), Is.True);

            for (int sampleIndex = 0; sampleIndex < 24; sampleIndex++)
            {
                int worldTileX = (sampleIndex * 347) - 3900;
                int worldTileY = (sampleIndex * -509) + 4700;
                Assert.That(
                    DeterministicGeologyGenerator.TryGenerate(
                        firstSettings,
                        worldTileX,
                        worldTileY,
                        out GeneratedGeologyData first),
                    Is.True);
                Assert.That(
                    DeterministicGeologyGenerator.TryGenerate(
                        secondSettings,
                        worldTileX,
                        worldTileY,
                        out GeneratedGeologyData second),
                    Is.True);
                AssertGeologyEqual(first, second);
            }
        }

        /// <summary>
        /// 正負座標と512タイル境界の両側を複数回生成し、同じ入力が同じサイト・分類を返すことを保証します。
        /// </summary>
        [TestCase(-513, -513)]
        [TestCase(-512, -512)]
        [TestCase(-1, -1)]
        [TestCase(0, 0)]
        [TestCase(511, 511)]
        [TestCase(512, 512)]
        public void Generation_NegativeCoordinatesAndMacroBoundariesAreDeterministic(
            int worldTileX,
            int worldTileY)
        {
            WorldGenerationSettings settings = CreateSettings("geology-negative-boundaries");

            Assert.That(
                DeterministicGeologyGenerator.TryGenerate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out GeneratedGeologyData first),
                Is.True);
            Assert.That(
                DeterministicGeologyGenerator.TryGenerate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out GeneratedGeologyData second),
                Is.True);

            AssertGeologyEqual(first, second);
            Assert.That(first.Class, Is.Not.EqualTo(GeologyClass.None));
            Assert.That(first.Site.DistanceSquared, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// 距離が同じ2サイトを準備し、Site座標とMacro座標の辞書順で候補が一意に選ばれることを保証します。
        /// </summary>
        [Test]
        public void SiteTieBreak_UsesStableCoordinateOrder()
        {
            GeologySiteData current = new GeologySiteData
            {
                MacroCellX = 4,
                MacroCellY = 5,
                SiteX = 100,
                SiteY = 200,
                DistanceSquared = 900
            };
            GeologySiteData nearer = current;
            nearer.DistanceSquared = 899;
            GeologySiteData smallerSiteX = current;
            smallerSiteX.SiteX = 99;
            GeologySiteData smallerSiteY = current;
            smallerSiteY.SiteY = 199;
            GeologySiteData smallerMacroX = current;
            smallerMacroX.MacroCellX = 3;
            GeologySiteData smallerMacroY = current;
            smallerMacroY.MacroCellY = 4;

            Assert.That(DeterministicGeologyGenerator.IsCandidatePreferred(nearer, current), Is.True);
            Assert.That(DeterministicGeologyGenerator.IsCandidatePreferred(smallerSiteX, current), Is.True);
            Assert.That(DeterministicGeologyGenerator.IsCandidatePreferred(smallerSiteY, current), Is.True);
            Assert.That(DeterministicGeologyGenerator.IsCandidatePreferred(smallerMacroX, current), Is.True);
            Assert.That(DeterministicGeologyGenerator.IsCandidatePreferred(smallerMacroY, current), Is.True);
            Assert.That(DeterministicGeologyGenerator.IsCandidatePreferred(current, current), Is.False);
        }

        /// <summary>
        /// 支配サイトの絶対座標で標高を再評価し、対象タイルの標高ではなくサイト標高区分を抽選へ使うことを保証します。
        /// </summary>
        [Test]
        public void Generation_SiteElevationBandUsesAbsoluteSiteCoordinate()
        {
            WorldGenerationSettings settings = CreateSettings("geology-site-elevation");
            Assert.That(
                DeterministicGeologyGenerator.TryGenerate(
                    settings,
                    -1777,
                    2389,
                    out GeneratedGeologyData geology),
                Is.True);

            ushort siteElevation = DeterministicEnvironmentGenerator.SampleElevation(
                settings,
                geology.Site.SiteX,
                geology.Site.SiteY);
            ElevationClass expectedClass = DeterministicEnvironmentGenerator.ClassifyElevation(
                siteElevation,
                settings);

            Assert.That(geology.SiteElevationClass, Is.EqualTo(expectedClass));
        }

        /// <summary>
        /// 固定寸法違反、欠落、重複ID、基礎重み合計違反、全標高重み0、必須フラグ誤設定を個別に作り、読込時に拒否することを保証します。
        /// </summary>
        [Test]
        public void Validation_RejectsIncompleteOrContradictoryDefinitions()
        {
            GeologyGenerationSettings wrongFixedSize = GeologyGenerationSettings.CreateDefault();
            wrongFixedSize.MacroCellSizeInTiles = 256;
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(wrongFixedSize), Is.False);

            GeologyGenerationSettings missing = GeologyGenerationSettings.CreateDefault();
            missing.Definitions.RemoveAt(missing.Definitions.Length - 1);
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(missing), Is.False);

            GeologyGenerationSettings duplicate = GeologyGenerationSettings.CreateDefault();
            GeologyDefinition duplicatedDefinition = duplicate.Definitions[7];
            duplicatedDefinition.Class = GeologyClass.VolcanicRock;
            duplicate.Definitions[7] = duplicatedDefinition;
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(duplicate), Is.False);

            GeologyGenerationSettings wrongTotal = GeologyGenerationSettings.CreateDefault();
            GeologyDefinition wrongWeight = wrongTotal.Definitions[0];
            wrongWeight.BaseWeight++;
            wrongTotal.Definitions[0] = wrongWeight;
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(wrongTotal), Is.False);

            GeologyGenerationSettings zeroBand = GeologyGenerationSettings.CreateDefault();
            for (int index = 0; index < zeroBand.Definitions.Length; index++)
            {
                GeologyDefinition definition = zeroBand.Definitions[index];
                definition.AlpineMultiplier = 0;
                zeroBand.Definitions[index] = definition;
            }
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(zeroBand), Is.False);

            GeologyGenerationSettings wrongFlag = GeologyGenerationSettings.CreateDefault();
            GeologyDefinition limestone = wrongFlag.Definitions[2];
            limestone.Flags = GeologyDefinitionFlags.None;
            wrongFlag.Definitions[2] = limestone;
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(wrongFlag), Is.False);
        }

        /// <summary>
        /// int座標端と不正な探索半径を入力し、周回や3×3探索への暗黙縮小をせず生成失敗を返すことを保証します。
        /// </summary>
        [Test]
        public void Generation_InvalidSettingsOrWorldEdgeFailsExplicitly()
        {
            WorldGenerationSettings settings = CreateSettings("geology-invalid-input");

            Assert.That(
                DeterministicGeologyGenerator.TryGenerate(settings, int.MaxValue, 0, out _),
                Is.False);
            Assert.That(
                DeterministicGeologyGenerator.TryGenerate(settings, int.MinValue, 0, out _),
                Is.False);

            GeologyGenerationSettings invalidGeology = settings.Geology;
            invalidGeology.SiteSearchRadiusInCells = 1;
            settings.Geology = invalidGeology;
            Assert.That(DeterministicGeologyGenerator.ValidateSettings(settings.Geology), Is.False);
            Assert.That(DeterministicGeologyGenerator.TryGenerate(settings, 0, 0, out _), Is.False);
        }

        /// <summary>
        /// 広い固定格子を監査し、確定8分類がすべて生成可能で未設定IDが混ざらないことを保証します。
        /// 面積比の妥当性はC2-Bの複数Seed分布監査で別途確認します。
        /// </summary>
        [Test]
        public void Generation_CoarseAuditContainsAllEightClasses()
        {
            WorldGenerationSettings settings = CreateSettings("geology-eight-classes");
            bool[] foundClasses = new bool[DeterministicGeologyGenerator.RequiredDefinitionCount + 1];

            // サイト規模と同じ512タイル間隔で32×32地点を読み、同一地域の過剰な重複を避けます。
            for (int sampleY = -16; sampleY < 16; sampleY++)
            {
                for (int sampleX = -16; sampleX < 16; sampleX++)
                {
                    Assert.That(
                        DeterministicGeologyGenerator.TryGenerate(
                            settings,
                            sampleX * 512,
                            sampleY * 512,
                            out GeneratedGeologyData geology),
                        Is.True);
                    foundClasses[(int)geology.Class] = true;
                }
            }

            for (int classValue = (int)GeologyClass.UnconsolidatedSediment;
                 classValue <= (int)GeologyClass.UltramaficRock;
                 classValue++)
            {
                Assert.That(
                    foundClasses[classValue],
                    Is.True,
                    $"地質ID {(GeologyClass)classValue} が粗い固定監査範囲に出現しませんでした。");
            }
        }

        /// <summary>
        /// 全Definitionの基礎特性だけを変え、地質分類が同じでもタイル値とチャンク状態ハッシュが変わることを保証します。
        /// </summary>
        [Test]
        public void ChunkHash_ContainsGeneratedGeologyProperties()
        {
            using TerrainGenerationTestCatalogSet catalogs =
                TerrainGenerationTestCatalogSet.Create();
            WorldGenerationSettings firstSettings = CreateSettings("geology-hash");
            WorldGenerationSettings secondSettings = firstSettings;
            GeologyGenerationSettings modifiedGeology = secondSettings.Geology;

            for (int index = 0; index < modifiedGeology.Definitions.Length; index++)
            {
                GeologyDefinition definition = modifiedGeology.Definitions[index];
                definition.Permeability++;
                modifiedGeology.Definitions[index] = definition;
            }

            secondSettings.Geology = modifiedGeology;
            ChunkCoordinate coordinate = new ChunkCoordinate(-2, 3);
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            ulong firstHash = DeterministicChunkGenerator.ComputeChunkHash(
                firstSettings, biomeSettings, catalogs.PaletteCatalog, catalogs.RuntimeIds, coordinate);
            ulong secondHash = DeterministicChunkGenerator.ComputeChunkHash(
                secondSettings, biomeSettings, catalogs.PaletteCatalog, catalogs.RuntimeIds, coordinate);

            Assert.That(DeterministicGeologyGenerator.ValidateSettings(modifiedGeology), Is.True);
            Assert.That(secondHash, Is.Not.EqualTo(firstHash));
        }

        /// <summary>テスト名から安定Seedを作り、製品既定値と同じ現在GeneratorVersionの設定を返します。</summary>
        /// <param name="seedText">テストケース同士の乱数列を分離する固定文字列です。</param>
        /// <returns>地質を含む製品既定のワールド生成設定です。</returns>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString(seedText),
                WorldGenerationSettings.CurrentGeneratorVersion);
        }

        /// <summary>指定した安定IDのDefinitionを取得し、確定表の全列と一致することを検証します。</summary>
        /// <param name="settings">検証対象の8地質Definition設定です。</param>
        /// <param name="geologyClass">検索する地質の安定IDです。</param>
        /// <param name="permeability">期待する基礎透水性です。</param>
        /// <param name="groundwaterStorage">期待する基礎地下水保持力です。</param>
        /// <param name="erosionResistance">期待する基礎侵食抵抗です。</param>
        /// <param name="flags">期待する必須条件フラグです。</param>
        /// <param name="baseWeight">期待する標高補正前の重みです。</param>
        /// <param name="belowSeaLevelMultiplier">期待する海面下倍率です。</param>
        /// <param name="lowlandMultiplier">期待する低地倍率です。</param>
        /// <param name="plainOrPlateauMultiplier">期待する平地・台地倍率です。</param>
        /// <param name="mountainMultiplier">期待する山地倍率です。</param>
        /// <param name="alpineMultiplier">期待する高山倍率です。</param>
        private static void AssertDefinition(
            in GeologyGenerationSettings settings,
            GeologyClass geologyClass,
            ushort permeability,
            ushort groundwaterStorage,
            ushort erosionResistance,
            GeologyDefinitionFlags flags,
            ushort baseWeight,
            ushort belowSeaLevelMultiplier,
            ushort lowlandMultiplier,
            ushort plainOrPlateauMultiplier,
            ushort mountainMultiplier,
            ushort alpineMultiplier)
        {
            Assert.That(
                DeterministicGeologyGenerator.TryGetDefinition(
                    settings,
                    geologyClass,
                    out GeologyDefinition definition),
                Is.True);
            Assert.That(definition.Permeability, Is.EqualTo(permeability));
            Assert.That(definition.GroundwaterStorage, Is.EqualTo(groundwaterStorage));
            Assert.That(definition.ErosionResistance, Is.EqualTo(erosionResistance));
            Assert.That(definition.Flags, Is.EqualTo(flags));
            Assert.That(definition.BaseWeight, Is.EqualTo(baseWeight));
            Assert.That(definition.BelowSeaLevelMultiplier, Is.EqualTo(belowSeaLevelMultiplier));
            Assert.That(definition.LowlandMultiplier, Is.EqualTo(lowlandMultiplier));
            Assert.That(definition.PlainOrPlateauMultiplier, Is.EqualTo(plainOrPlateauMultiplier));
            Assert.That(definition.MountainMultiplier, Is.EqualTo(mountainMultiplier));
            Assert.That(definition.AlpineMultiplier, Is.EqualTo(alpineMultiplier));
        }

        /// <summary>2回の生成結果について、分類、基礎特性、支配サイトの全値を比較します。</summary>
        /// <param name="expected">先に生成した比較基準の地質結果です。</param>
        /// <param name="actual">同条件またはDefinition逆順で生成した実測結果です。</param>
        private static void AssertGeologyEqual(
            in GeneratedGeologyData expected,
            in GeneratedGeologyData actual)
        {
            Assert.That(actual.Class, Is.EqualTo(expected.Class));
            Assert.That(actual.SiteElevationClass, Is.EqualTo(expected.SiteElevationClass));
            Assert.That(actual.Permeability, Is.EqualTo(expected.Permeability));
            Assert.That(actual.GroundwaterStorage, Is.EqualTo(expected.GroundwaterStorage));
            Assert.That(actual.ErosionResistance, Is.EqualTo(expected.ErosionResistance));
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags));
            Assert.That(actual.Site.MacroCellX, Is.EqualTo(expected.Site.MacroCellX));
            Assert.That(actual.Site.MacroCellY, Is.EqualTo(expected.Site.MacroCellY));
            Assert.That(actual.Site.SiteX, Is.EqualTo(expected.Site.SiteX));
            Assert.That(actual.Site.SiteY, Is.EqualTo(expected.Site.SiteY));
            Assert.That(actual.Site.DistanceSquared, Is.EqualTo(expected.Site.DistanceSquared));
        }
    }
}
