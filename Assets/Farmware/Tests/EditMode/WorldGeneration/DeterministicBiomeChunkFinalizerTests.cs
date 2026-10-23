// このファイルは、C3/C4水文完成後にだけ新18Biomeと最終地面が確定されるC5-Cの統合境界を検証します。
// 旧7Biome、旧淡水、旧岩地マグマへ戻らず、基礎タイルと完成水文から一意に結果を得ることをEditModeで保証します。

using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// Biome分類前の基礎生成、海域、地上分類、無効Definitionの明示失敗を検証するテスト群です。
    /// </summary>
    public sealed class DeterministicBiomeChunkFinalizerTests
    {
        /// <summary>
        /// 地上の基礎タイルを生成し、水文完成前にBiome・土壌・資源適性を先回り決定しないことを保証します。
        /// </summary>
        [Test]
        public void BaseTileGeneration_LeavesBiomeAndDerivedSurfaceUnclassified()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-base-tile"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            GeneratedTile baseTile = FindBaseLandTile(settings, out _, out _);

            Assert.That(baseTile.Biome, Is.EqualTo(BiomeId.None));
            Assert.That(baseTile.Soil, Is.EqualTo(SoilType.None));
            Assert.That(baseTile.ResourceSuitability, Is.EqualTo(ResourceSuitability.None));
            Assert.That(baseTile.WaterBodyType, Is.EqualTo(WaterBodyType.None));
            Assert.That(baseTile.Ground, Is.EqualTo(GroundType.Land).Or.EqualTo(GroundType.RockWall));
        }

        /// <summary>
        /// 地上の基礎タイルへ空の固定水文を渡し、新Definitionの1〜18だけが最終Biomeとして公開されることを保証します。
        /// </summary>
        [Test]
        public void FinalizeLandTile_SelectsNewBiomeAndRetainsFixedEnvironment()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-final-biome"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            GeneratedTile baseTile = FindBaseLandTile(settings, out int tileX, out int tileY);

            bool finalized = DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                settings,
                biomeSettings,
                tileX,
                tileY,
                baseTile,
                default,
                default,
                out GeneratedTile finalTile);

            Assert.That(finalized, Is.True);
            Assert.That((int)finalTile.Biome, Is.InRange(BiomeStableIds.First, BiomeStableIds.Last));
            Assert.That(finalTile.Elevation, Is.EqualTo(baseTile.Elevation));
            Assert.That(finalTile.TerrainTemperature, Is.EqualTo(baseTile.TerrainTemperature));
            Assert.That(finalTile.GeologyClass, Is.EqualTo(baseTile.GeologyClass));
            if (finalTile.Ground == GroundType.Land)
            {
                Assert.That(finalTile.Soil, Is.Not.EqualTo(SoilType.None));
            }
            else
            {
                Assert.That(finalTile.Soil, Is.EqualTo(SoilType.None));
            }
        }

        /// <summary>
        /// 海面下のタイルを準備し、地上Biomeを偽装せずNone・海水・水域として正常完了することを保証します。
        /// </summary>
        [Test]
        public void FinalizeSeaTile_KeepsBiomeNoneAndPublishesSaltWater()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-sea"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            GeneratedTile baseTile = FindBaseSeaTile(settings, out int tileX, out int tileY);

            bool finalized = DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                settings,
                BiomeDefinitionCatalog.CreateDefault(),
                tileX,
                tileY,
                baseTile,
                default,
                default,
                out GeneratedTile finalTile);

            Assert.That(finalized, Is.True);
            Assert.That(finalTile.Biome, Is.EqualTo(BiomeId.None));
            Assert.That(finalTile.Ground, Is.EqualTo(GroundType.Water));
            Assert.That(finalTile.WaterSalinity, Is.EqualTo(WaterSalinity.SaltWater));
            Assert.That(finalTile.WaterBodyType, Is.EqualTo(WaterBodyType.Sea));
            Assert.That(finalTile.ResourceSuitability, Is.EqualTo(ResourceSuitability.None));
        }

        /// <summary>
        /// 地上タイルと欠落したBiome Definitionを準備し、Noneや温帯草原へのフォールバックを行わず失敗を返すことを保証します。
        /// </summary>
        [Test]
        public void FinalizeLandTile_InvalidBiomeCatalogFailsWithoutFallback()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-invalid-catalog"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            GeneratedTile baseTile = FindBaseLandTile(settings, out int tileX, out int tileY);

            bool finalized = DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                settings,
                default,
                tileX,
                tileY,
                baseTile,
                default,
                default,
                out GeneratedTile finalTile);

            Assert.That(finalized, Is.False);
            Assert.That(finalTile, Is.EqualTo(default(GeneratedTile)));
        }

        /// <summary>
        /// 旧7BiomeのGeneratorVersion 1を入力し、C5の新IDへ暗黙変換せず基礎生成の時点で拒否することを保証します。
        /// </summary>
        [Test]
        public void BaseTileGeneration_PreC5GeneratorVersionIsRejected()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-old-version"),
                1);

            bool generated = DeterministicChunkGenerator.TryGenerateTile(
                settings,
                0,
                0,
                out GeneratedTile tile);

            Assert.That(generated, Is.False);
            Assert.That(tile, Is.EqualTo(default(GeneratedTile)));
        }

        /// <summary>
        /// 有効な地上タイルを小さな有限範囲から探し、テストが特定の旧Biome座標に依存しないようにします。
        /// </summary>
        /// <param name="settings">固定Seedと海面を持つ生成設定です。</param>
        /// <param name="tileX">見つけたタイルの水平座標を返します。</param>
        /// <param name="tileY">見つけたタイルの垂直座標を返します。</param>
        /// <returns>海面上の陸地または岩壁候補である基礎タイルです。</returns>
        private static GeneratedTile FindBaseLandTile(
            in WorldGenerationSettings settings,
            out int tileX,
            out int tileY)
        {
            for (int y = -64; y <= 64; y += 4)
            {
                for (int x = -64; x <= 64; x += 4)
                {
                    GeneratedTile candidate = DeterministicChunkGenerator.GenerateTile(settings, x, y);
                    if (candidate.Elevation >= settings.SeaLevelThreshold)
                    {
                        tileX = x;
                        tileY = y;
                        return candidate;
                    }
                }
            }

            Assert.Fail("テスト範囲に地上タイルが見つかりません。");
            tileX = 0;
            tileY = 0;
            return default;
        }

        /// <summary>
        /// 海面下の基礎タイルを小さな有限範囲から探し、海域専用分岐の入力を準備します。
        /// </summary>
        /// <param name="settings">固定Seedと海面を持つ生成設定です。</param>
        /// <param name="tileX">見つけた海タイルの水平座標を返します。</param>
        /// <param name="tileY">見つけた海タイルの垂直座標を返します。</param>
        /// <returns>海面下でWaterBodyType.Seaを持つ基礎タイルです。</returns>
        private static GeneratedTile FindBaseSeaTile(
            in WorldGenerationSettings settings,
            out int tileX,
            out int tileY)
        {
            for (int y = -256; y <= 256; y += 8)
            {
                for (int x = -256; x <= 256; x += 8)
                {
                    GeneratedTile candidate = DeterministicChunkGenerator.GenerateTile(settings, x, y);
                    if (candidate.Elevation < settings.SeaLevelThreshold)
                    {
                        tileX = x;
                        tileY = y;
                        return candidate;
                    }
                }
            }

            Assert.Fail("テスト範囲に海面下タイルが見つかりません。");
            tileX = 0;
            tileY = 0;
            return default;
        }
    }
}
