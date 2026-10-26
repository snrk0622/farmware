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
        /// 地上の基礎タイルを生成し、水文完成前にBiome・資源適性を先回り決定しないことを保証します。
        /// </summary>
        [Test]
        public void BaseTileGeneration_LeavesBiomeAndDerivedSurfaceUnclassified()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-base-tile"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            GeneratedTile baseTile = FindBaseLandTile(settings, out _, out _);

            Assert.That(baseTile.Biome, Is.EqualTo(BiomeId.None));
            Assert.That(baseTile.ResourceSuitability, Is.EqualTo(ResourceSuitability.None));
        }

        /// <summary>
        /// 火山・地熱地帯の活動度段階4と5、追加下限を直接入力し、自然露出マグマを段階5の活動中核だけへ限定することを保証します。
        /// </summary>
        [Test]
        public void MagmaActivityEligibility_RequiresBandFiveAndAdditionalThreshold()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-magma-activity"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            settings.MagmaNoiseThreshold = 0;
            ushort bandFive = DeterministicEnvironmentGenerator.EnvironmentBand5Threshold;

            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaActivityEligible(
                    settings,
                    BiomeId.TemperateGrassland,
                    ushort.MaxValue),
                Is.False,
                "活動度が高くても火山・地熱地帯以外をマグマ候補にしてはいけません。");
            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaActivityEligible(
                    settings,
                    BiomeId.VolcanicGeothermal,
                    (ushort)(bandFive - 1)),
                Is.False,
                "通常候補として選ばれる段階4の火山周縁へ露出マグマを生成してはいけません。");
            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaActivityEligible(
                    settings,
                    BiomeId.VolcanicGeothermal,
                    bandFive),
                Is.True,
                "段階5の下端は活動中核として受理する必要があります。");

            settings.MagmaMinimumVolcanicInfluence = (ushort)(bandFive + 1);
            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaActivityEligible(
                    settings,
                    BiomeId.VolcanicGeothermal,
                    bandFive),
                Is.False,
                "Inspectorの追加下限は段階5内をさらに狭める条件として適用する必要があります。");
            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaActivityEligible(
                    settings,
                    BiomeId.VolcanicGeothermal,
                    (ushort)(bandFive + 1)),
                Is.True);
        }

        /// <summary>
        /// 段階5の火山中核と形状密度の境界を準備し、密度閾値未満を可視化候補にも製品マグマにも含めないことを保証します。
        /// </summary>
        [Test]
        public void MagmaCandidate_RequiresDensityThreshold()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("c5-c-magma-density"),
                WorldGenerationSettings.CurrentGeneratorVersion);
            FindNonMaximumMagmaDensity(settings, out int tileX, out int tileY, out ushort density);
            settings.MagmaMinimumVolcanicInfluence = 0;
            settings.MagmaNoiseThreshold = density;

            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaCandidate(
                    settings,
                    BiomeId.VolcanicGeothermal,
                    DeterministicEnvironmentGenerator.EnvironmentBand5Threshold,
                    tileX,
                    tileY),
                Is.True,
                "形状密度の閾値ちょうどはマグマ候補へ含めます。");

            settings.MagmaNoiseThreshold = (ushort)(density + 1);
            Assert.That(
                DeterministicBiomeChunkFinalizer.IsMagmaCandidate(
                    settings,
                    BiomeId.VolcanicGeothermal,
                    DeterministicEnvironmentGenerator.EnvironmentBand5Threshold,
                    tileX,
                    tileY),
                Is.False,
                "形状密度が閾値を1下回るタイルを候補色で塗ってはいけません。");
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
            Assert.That(finalTile.ResourceSuitability, Is.EqualTo(ResourceSuitability.None));
        }

        /// <summary>
        /// 海面下のタイルを準備し、地上Biomeを偽装せずNoneとして正常完了することを保証します。
        /// </summary>
        [Test]
        public void FinalizeSeaTile_KeepsBiomeNone()
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
        /// 閾値の両側を安全に作れる、最大値未満のマグマ形状密度を有限範囲から探します。
        /// </summary>
        /// <param name="settings">固定Seedとマグマ形状設定です。</param>
        /// <param name="tileX">見つかった水平ワールドタイル座標です。</param>
        /// <param name="tileY">見つかった垂直ワールドタイル座標です。</param>
        /// <param name="density">見つかった0〜65,534の形状密度です。</param>
        private static void FindNonMaximumMagmaDensity(
            in WorldGenerationSettings settings,
            out int tileX,
            out int tileY,
            out ushort density)
        {
            tileX = 0;
            tileY = 0;
            density = ushort.MaxValue;
            for (int y = -16; y <= 16; y++)
            {
                for (int x = -16; x <= 16; x++)
                {
                    ushort candidate = DeterministicMagmaGenerator.SampleDensity(settings, x, y);
                    if (candidate == ushort.MaxValue)
                    {
                        continue;
                    }

                    tileX = x;
                    tileY = y;
                    density = candidate;
                    return;
                }
            }

            Assert.Fail("有限テスト範囲に最大値未満のマグマ形状密度が必要です。");
        }

        /// <summary>
        /// 海面下の基礎タイルを小さな有限範囲から探し、海域専用分岐の入力を準備します。
        /// </summary>
        /// <param name="settings">固定Seedと海面を持つ生成設定です。</param>
        /// <param name="tileX">見つけた海タイルの水平座標を返します。</param>
        /// <param name="tileY">見つけた海タイルの垂直座標を返します。</param>
        /// <returns>海面下の連続環境値を持つ基礎タイルです。</returns>
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
