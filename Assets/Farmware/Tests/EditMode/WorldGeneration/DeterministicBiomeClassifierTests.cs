// このファイルは、TERRAIN-01C5-B1/B2の通常Biome候補評価、整数Score、候補別Variation、安定順位を検証します。
// ECS生成と旧7Biomeはまだ切り替えず、新18Biome分類器の純粋計算だけをSceneなしのEditModeテストで固定します。

using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 通常Biomeの候補ゲート、軸別適性、スコア、BiomeVariation、上位2候補の安定順位を保証します。
    /// </summary>
    public sealed class DeterministicBiomeClassifierTests
    {
        /// <summary>
        /// 台形型適性の許容端、傾斜、代表範囲、範囲外を両側で実行し、整数切り捨て式を保証します。
        /// </summary>
        [Test]
        public void AxisFit_UsesInclusiveTrapezoidAndRejectsOutsideValues()
        {
            BiomeAxisRange range = new BiomeAxisRange
            {
                AllowedMin = 100,
                PreferredMin = 200,
                PreferredMax = 300,
                AllowedMax = 400
            };

            AssertAxisFit(range, 100, true, 0);
            AssertAxisFit(range, 150, true, 32767);
            AssertAxisFit(range, 200, true, ushort.MaxValue);
            AssertAxisFit(range, 250, true, ushort.MaxValue);
            AssertAxisFit(range, 300, true, ushort.MaxValue);
            AssertAxisFit(range, 350, true, 32767);
            AssertAxisFit(range, 400, true, 0);
            AssertAxisFit(range, 99, false, 0);
            AssertAxisFit(range, 401, false, 0);
        }

        /// <summary>
        /// 許容端と代表端が一致する片側傾斜なしの範囲でも0除算せず、代表値と範囲外を分けることを保証します。
        /// </summary>
        [Test]
        public void AxisFit_DegenerateSlopeEndsDoNotDivideByZero()
        {
            BiomeAxisRange lowerFlat = new BiomeAxisRange
            {
                AllowedMin = 100,
                PreferredMin = 100,
                PreferredMax = 200,
                AllowedMax = 300
            };
            BiomeAxisRange upperFlat = new BiomeAxisRange
            {
                AllowedMin = 100,
                PreferredMin = 200,
                PreferredMax = 300,
                AllowedMax = 300
            };

            AssertAxisFit(lowerFlat, 100, true, ushort.MaxValue);
            AssertAxisFit(lowerFlat, 99, false, 0);
            AssertAxisFit(upperFlat, 300, true, ushort.MaxValue);
            AssertAxisFit(upperFlat, 301, false, 0);
        }

        /// <summary>
        /// 全重み付き軸を最高適性にした候補で、64bit加重和が理論最大16,776,960へ正確に到達することを保証します。
        /// </summary>
        [Test]
        public void CandidateEvaluation_AllPreferredAxesReachMaximumScore()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.TemperateGrassland,
                out BiomeDefinition definition), Is.True);

            // 地質軸も最高適性に揃え、重み合計256の理論上限を直接検証します。
            definition.GeologyFits[0] = ushort.MaxValue;
            BiomeClassificationInput input = CreateInput(
                terrainTemperature: 30000,
                baseMoisture: 38000,
                elevation: 30000,
                woodyVegetation: 20000,
                ruggedness: 10000,
                coastalInfluence: 0,
                volcanicActivity: 0,
                geologyClass: GeologyClass.UnconsolidatedSediment);

            bool succeeded = DeterministicBiomeClassifier.TryEvaluateNormalCandidate(
                settings,
                123UL,
                1,
                definition,
                input,
                out BiomeCandidateEvaluation evaluation);

            Assert.That(succeeded, Is.True);
            Assert.That(evaluation.Score, Is.EqualTo(DeterministicBiomeClassifier.MaximumBiomeScore));
            Assert.That(evaluation.TerrainTemperatureFit, Is.EqualTo(ushort.MaxValue));
            Assert.That(evaluation.BaseMoistureFit, Is.EqualTo(ushort.MaxValue));
            Assert.That(evaluation.ElevationFit, Is.EqualTo(ushort.MaxValue));
            Assert.That(evaluation.WoodyVegetationFit, Is.EqualTo(ushort.MaxValue));
            Assert.That(evaluation.TerrainRuggednessFit, Is.EqualTo(ushort.MaxValue));
            Assert.That(evaluation.GeologyFit, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 許容範囲端で全使用軸が0点でも候補資格を維持し、候補なしへ誤変換しないことを保証します。
        /// </summary>
        [Test]
        public void CandidateEvaluation_ZeroScoreAtAllowedBoundsRemainsEligible()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.TemperateGrassland,
                out BiomeDefinition definition), Is.True);

            definition.GeologyFits[0] = 0;
            // 草原の起伏軸は許容下限から最高適性なので、その40点分の重みを0適性になる気温軸へ移して合計256を保ちます。
            definition.AxisWeights.TerrainTemperature += definition.AxisWeights.TerrainRuggedness;
            definition.AxisWeights.TerrainRuggedness = 0;
            BiomeClassificationInput input = CreateInput(
                definition.TerrainTemperature.AllowedMin,
                definition.BaseMoisture.AllowedMin,
                definition.Elevation.AllowedMin,
                definition.WoodyVegetation.AllowedMin,
                definition.TerrainRuggedness.AllowedMin,
                definition.CoastalInfluence.AllowedMin,
                definition.VolcanicActivity.AllowedMin,
                GeologyClass.UnconsolidatedSediment);

            bool succeeded = DeterministicBiomeClassifier.TryEvaluateNormalCandidate(
                settings,
                456UL,
                2,
                definition,
                input,
                out BiomeCandidateEvaluation evaluation);

            Assert.That(succeeded, Is.True);
            Assert.That(evaluation.Score, Is.Zero);
        }

        /// <summary>
        /// 候補のVariationがWarp後座標、Seed、Version、Definition固有Saltから同じノイズ関数で生成されることを保証します。
        /// </summary>
        [Test]
        public void CandidateVariation_UsesDefinitionSaltAndWarpedCoordinates()
        {
            const ulong worldSeed = 0xFEDCBA9876543210UL;
            const int generatorVersion = 7;
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.TemperateGrassland,
                out BiomeDefinition definition), Is.True);
            BiomeClassificationInput input = CreateInput(
                30000,
                38000,
                30000,
                20000,
                10000,
                0,
                0,
                GeologyClass.UnconsolidatedSediment,
                warpedSampleX: -513,
                warpedSampleY: 1025);

            Assert.That(DeterministicBiomeClassifier.TryEvaluateNormalCandidate(
                settings,
                worldSeed,
                generatorVersion,
                definition,
                input,
                out BiomeCandidateEvaluation evaluation), Is.True);
            ushort expected = DeterministicNoise.SampleFractalNoise(
                worldSeed,
                generatorVersion,
                -513,
                1025,
                settings.VariationCellSizeInTiles,
                settings.VariationOctaveCount,
                definition.VariationSalt);

            Assert.That(evaluation.CandidateVariation, Is.EqualTo(expected));
        }

        /// <summary>
        /// 海洋性林は海洋影響段階5、火山周縁は活動的火山地質と段階4だけを通常候補として受理することを保証します。
        /// </summary>
        [Test]
        public void CandidateEvaluation_EnforcesCoastalAndVolcanicGates()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.SubtropicalOceanicForest,
                out BiomeDefinition oceanic), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.VolcanicGeothermal,
                out BiomeDefinition volcanic), Is.True);

            BiomeClassificationInput weakCoast = CreateInput(
                40000, 50000, 30000, 50000, 20000, 52427, 0, GeologyClass.Limestone);
            BiomeClassificationInput strongCoast = weakCoast;
            strongCoast.Environment.CoastalInfluence = 52428;
            Assert.That(TryEvaluate(settings, oceanic, weakCoast), Is.False);
            Assert.That(TryEvaluate(settings, oceanic, strongCoast), Is.True);

            BiomeClassificationInput unsupportedVolcanism = CreateInput(
                30000, 30000, 30000, 20000, 50000, 0, 45000, GeologyClass.VolcanicRock);
            BiomeClassificationInput supportedVolcanism = unsupportedVolcanism;
            supportedVolcanism.GeologyFlags = GeologyDefinitionFlags.SupportsActiveVolcanism;
            BiomeClassificationInput activeCore = supportedVolcanism;
            activeCore.Environment.VolcanicActivityPotential = 52428;
            Assert.That(TryEvaluate(settings, volcanic, unsupportedVolcanism), Is.False);
            Assert.That(TryEvaluate(settings, volcanic, supportedVolcanism), Is.True);
            Assert.That(TryEvaluate(settings, volcanic, activeCore), Is.False);
        }

        /// <summary>
        /// 海面閾値の直下だけを地上Biome候補から除外し、閾値上の陸地は同じDefinitionで評価できることを保証します。
        /// </summary>
        [Test]
        public void CandidateEvaluation_RejectsOnlyCellsBelowSeaLevel()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.TemperateGrassland,
                out BiomeDefinition definition), Is.True);
            BiomeClassificationInput belowSeaLevel = CreateInput(
                30000,
                38000,
                30000,
                20000,
                10000,
                0,
                0,
                GeologyClass.UnconsolidatedSediment,
                seaLevelThreshold: 30001);
            BiomeClassificationInput atSeaLevel = belowSeaLevel;
            atSeaLevel.Environment.Elevation = 30001;

            Assert.That(TryEvaluate(settings, definition, belowSeaLevel), Is.False);
            Assert.That(TryEvaluate(settings, definition, atSeaLevel), Is.True);
        }

        /// <summary>
        /// 水文特殊Definition、不正なVariation設定、不明な地質IDを拒否し、失敗時に部分評価を公開しないことを保証します。
        /// </summary>
        [Test]
        public void CandidateEvaluation_RejectsInvalidRolesSettingsAndGeologyWithoutPartialResult()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.TemperateGrassland,
                out BiomeDefinition grassland), Is.True);
            Assert.That(BiomeDefinitionCatalog.TryGetDefinition(
                settings,
                BiomeStableIds.FreshwaterMarshFloodplain,
                out BiomeDefinition hydrologySpecial), Is.True);
            BiomeClassificationInput input = CreateInput(
                30000, 38000, 30000, 20000, 10000, 0, 0, GeologyClass.UnconsolidatedSediment);

            Assert.That(TryEvaluate(settings, hydrologySpecial, input), Is.False);

            BiomeClassificationSettings invalidVariation = settings;
            invalidVariation.VariationOctaveCount = 3;
            Assert.That(DeterministicBiomeClassifier.TryEvaluateNormalCandidate(
                invalidVariation,
                999UL,
                3,
                grassland,
                input,
                out BiomeCandidateEvaluation failedEvaluation), Is.False);
            Assert.That(failedEvaluation.StableId, Is.Zero);
            Assert.That(failedEvaluation.Score, Is.Zero);

            input.GeologyClass = (GeologyClass)byte.MaxValue;
            Assert.That(TryEvaluate(settings, grassland, input), Is.False);
        }

        /// <summary>
        /// Score差が1でもVariationで逆転せず、Score同点だけでVariation、さらに安定IDの順へ進むことを保証します。
        /// </summary>
        [Test]
        public void NormalRanking_UsesScoreVariationAndStableIdInThatOrder()
        {
            BiomeCandidateEvaluation higherScore = CreateEvaluation(
                stableId: 18,
                score: 1001,
                variation: 0);
            BiomeCandidateEvaluation lowerScoreWithMaximumVariation = CreateEvaluation(
                stableId: 1,
                score: 1000,
                variation: ushort.MaxValue);
            BiomeCandidateEvaluation higherVariation = CreateEvaluation(
                stableId: 18,
                score: 1000,
                variation: 50001);
            BiomeCandidateEvaluation lowerStableId = CreateEvaluation(
                stableId: 2,
                score: 1000,
                variation: 50000);
            BiomeCandidateEvaluation higherStableId = CreateEvaluation(
                stableId: 3,
                score: 1000,
                variation: 50000);

            Assert.That(DeterministicBiomeClassifier.IsPreferredNormalCandidate(
                higherScore,
                lowerScoreWithMaximumVariation), Is.True);
            Assert.That(DeterministicBiomeClassifier.IsPreferredNormalCandidate(
                lowerScoreWithMaximumVariation,
                higherScore), Is.False);
            Assert.That(DeterministicBiomeClassifier.IsPreferredNormalCandidate(
                higherVariation,
                lowerStableId), Is.True);
            Assert.That(DeterministicBiomeClassifier.IsPreferredNormalCandidate(
                lowerStableId,
                higherStableId), Is.True);
            Assert.That(DeterministicBiomeClassifier.IsPreferredNormalCandidate(
                higherStableId,
                lowerStableId), Is.False);
        }

        /// <summary>
        /// 最高Score・Variationが3候補で同点でも、挿入順に関係なくID昇順の先頭2件を保持することを保証します。
        /// </summary>
        [Test]
        public void NormalRanking_ThreeWayTieKeepsStableTopTwoForAnyInsertionOrder()
        {
            BiomeCandidateEvaluation idThree = CreateEvaluation(3, 1000, 50000);
            BiomeCandidateEvaluation idOne = CreateEvaluation(1, 1000, 50000);
            BiomeCandidateEvaluation idTwo = CreateEvaluation(2, 1000, 50000);
            NormalBiomeCandidateRanking forward = default;
            NormalBiomeCandidateRanking reverse = default;

            DeterministicBiomeClassifier.InsertNormalCandidate(ref forward, idThree);
            DeterministicBiomeClassifier.InsertNormalCandidate(ref forward, idOne);
            DeterministicBiomeClassifier.InsertNormalCandidate(ref forward, idTwo);
            DeterministicBiomeClassifier.InsertNormalCandidate(ref reverse, idTwo);
            DeterministicBiomeClassifier.InsertNormalCandidate(ref reverse, idOne);
            DeterministicBiomeClassifier.InsertNormalCandidate(ref reverse, idThree);

            Assert.That(forward.CandidateCount, Is.EqualTo(3));
            Assert.That(forward.HasSecond, Is.True);
            Assert.That(forward.First.StableId, Is.EqualTo(1));
            Assert.That(forward.Second.StableId, Is.EqualTo(2));
            AssertEvaluationEquals(forward.First, reverse.First);
            AssertEvaluationEquals(forward.Second, reverse.Second);
            Assert.That(reverse.CandidateCount, Is.EqualTo(forward.CandidateCount));
        }

        /// <summary>
        /// 3件の通常候補から上位2件を保持し、Definition登録順を逆転しても全順位情報が一致することを保証します。
        /// </summary>
        [Test]
        public void NormalRanking_ReturnsTopTwoIndependentOfDefinitionOrder()
        {
            const ulong worldSeed = 0x123456789ABCDEF0UL;
            const int generatorVersion = 9;
            BiomeClassificationSettings forwardSettings = BiomeDefinitionCatalog.CreateDefault();
            BiomeClassificationSettings reverseSettings = forwardSettings;
            ReverseDefinitions(ref reverseSettings);
            BiomeClassificationInput input = CreateInput(
                terrainTemperature: 35000,
                baseMoisture: 39500,
                elevation: 30000,
                woodyVegetation: 50000,
                ruggedness: 20000,
                coastalInfluence: 0,
                volcanicActivity: 0,
                geologyClass: GeologyClass.UnconsolidatedSediment,
                warpedSampleX: -257,
                warpedSampleY: 511);

            Assert.That(DeterministicBiomeClassifier.TryRankNormalCandidates(
                forwardSettings,
                worldSeed,
                generatorVersion,
                input,
                out NormalBiomeCandidateRanking forward), Is.True);
            Assert.That(DeterministicBiomeClassifier.TryRankNormalCandidates(
                reverseSettings,
                worldSeed,
                generatorVersion,
                input,
                out NormalBiomeCandidateRanking reverse), Is.True);

            Assert.That(forward.CandidateCount, Is.EqualTo(3));
            Assert.That(forward.HasSecond, Is.True);
            Assert.That(forward.First.StableId, Is.Not.EqualTo(forward.Second.StableId));
            AssertEvaluationEquals(forward.First, reverse.First);
            AssertEvaluationEquals(forward.Second, reverse.Second);
            Assert.That(reverse.CandidateCount, Is.EqualTo(forward.CandidateCount));
            Assert.That(reverse.HasSecond, Is.EqualTo(forward.HasSecond));
        }

        /// <summary>
        /// 海面下で通常候補が0件の場合は明示的に失敗し、首位や途中件数を公開しないことを保証します。
        /// </summary>
        [Test]
        public void NormalRanking_NoEligibleCandidateReturnsFalseAndDefaultResult()
        {
            BiomeClassificationSettings settings = BiomeDefinitionCatalog.CreateDefault();
            BiomeClassificationInput input = CreateInput(
                terrainTemperature: 30000,
                baseMoisture: 38000,
                elevation: 19999,
                woodyVegetation: 20000,
                ruggedness: 10000,
                coastalInfluence: 0,
                volcanicActivity: 0,
                geologyClass: GeologyClass.UnconsolidatedSediment);

            bool succeeded = DeterministicBiomeClassifier.TryRankNormalCandidates(
                settings,
                123UL,
                1,
                input,
                out NormalBiomeCandidateRanking ranking);

            Assert.That(succeeded, Is.False);
            Assert.That(ranking.CandidateCount, Is.Zero);
            Assert.That(ranking.HasSecond, Is.False);
            Assert.That(ranking.First.StableId, Is.EqualTo(BiomeStableIds.None));
            Assert.That(ranking.Second.StableId, Is.EqualTo(BiomeStableIds.None));
        }

        /// <summary>
        /// 台形型適性の成否と値を1行で比較し、境界ケースの期待値を読みやすく保ちます。
        /// </summary>
        /// <param name="range">評価する許容・代表範囲です。</param>
        /// <param name="value">評価する連続軸値です。</param>
        /// <param name="expectedSuccess">候補範囲内として期待する成否です。</param>
        /// <param name="expectedFit">成功時に期待する0〜65,535の適性です。</param>
        private static void AssertAxisFit(
            BiomeAxisRange range,
            ushort value,
            bool expectedSuccess,
            ushort expectedFit)
        {
            bool succeeded = DeterministicBiomeClassifier.TryCalculateAxisFit(value, range, out ushort fit);
            Assert.That(succeeded, Is.EqualTo(expectedSuccess));
            Assert.That(fit, Is.EqualTo(expectedFit));
        }

        /// <summary>
        /// 1候補の成否だけを検査するテストで、共通SeedとVersionを供給します。
        /// </summary>
        /// <param name="settings">BiomeVariation設定です。</param>
        /// <param name="definition">評価する通常Biome Definitionです。</param>
        /// <param name="input">固定環境と地質です。</param>
        /// <returns>候補ゲートと許容範囲をすべて満たした場合はtrueです。</returns>
        private static bool TryEvaluate(
            in BiomeClassificationSettings settings,
            in BiomeDefinition definition,
            in BiomeClassificationInput input)
        {
            return DeterministicBiomeClassifier.TryEvaluateNormalCandidate(
                settings,
                999UL,
                3,
                definition,
                input,
                out _);
        }

        /// <summary>
        /// 順位規則だけを境界値で検査するため、比較に必要な3値を持つ候補評価を作ります。
        /// </summary>
        /// <param name="stableId">最終同点解決へ使う安定IDです。</param>
        /// <param name="score">最初に比較する整数Scoreです。</param>
        /// <param name="variation">Score同点時だけ比較する候補別Variationです。</param>
        /// <returns>順位比較へ直接渡せる最小限の候補評価です。</returns>
        private static BiomeCandidateEvaluation CreateEvaluation(
            byte stableId,
            long score,
            ushort variation)
        {
            return new BiomeCandidateEvaluation
            {
                StableId = stableId,
                Score = score,
                CandidateVariation = variation
            };
        }

        /// <summary>
        /// FixedListの前後を交換し、同じDefinition集合を逆登録順にします。
        /// </summary>
        /// <param name="settings">Definitionsだけを逆順へ変更するテスト用設定です。</param>
        private static void ReverseDefinitions(ref BiomeClassificationSettings settings)
        {
            int lastIndex = settings.Definitions.Length - 1;
            for (int leftIndex = 0; leftIndex < settings.Definitions.Length / 2; leftIndex++)
            {
                int rightIndex = lastIndex - leftIndex;
                BiomeDefinition left = settings.Definitions[leftIndex];
                settings.Definitions[leftIndex] = settings.Definitions[rightIndex];
                settings.Definitions[rightIndex] = left;
            }
        }

        /// <summary>
        /// 順位結果に含む全フィールドを比較し、登録順が診断情報にも影響しないことを保証します。
        /// </summary>
        /// <param name="expected">正順設定から得た候補評価です。</param>
        /// <param name="actual">逆順設定から得た候補評価です。</param>
        private static void AssertEvaluationEquals(
            in BiomeCandidateEvaluation expected,
            in BiomeCandidateEvaluation actual)
        {
            Assert.That(actual.StableId, Is.EqualTo(expected.StableId));
            Assert.That(actual.TerrainTemperatureFit, Is.EqualTo(expected.TerrainTemperatureFit));
            Assert.That(actual.BaseMoistureFit, Is.EqualTo(expected.BaseMoistureFit));
            Assert.That(actual.ElevationFit, Is.EqualTo(expected.ElevationFit));
            Assert.That(actual.WoodyVegetationFit, Is.EqualTo(expected.WoodyVegetationFit));
            Assert.That(actual.TerrainRuggednessFit, Is.EqualTo(expected.TerrainRuggednessFit));
            Assert.That(actual.GeologyFit, Is.EqualTo(expected.GeologyFit));
            Assert.That(actual.VolcanicActivityFit, Is.EqualTo(expected.VolcanicActivityFit));
            Assert.That(actual.CoastalInfluenceFit, Is.EqualTo(expected.CoastalInfluenceFit));
            Assert.That(actual.Score, Is.EqualTo(expected.Score));
            Assert.That(actual.CandidateVariation, Is.EqualTo(expected.CandidateVariation));
        }

        /// <summary>
        /// 連続環境値と地質を純粋分類入力へまとめ、各テストが注目する値だけを明示できるようにします。
        /// </summary>
        /// <param name="terrainTemperature">標高寒冷補正後の地形気温です。</param>
        /// <param name="baseMoisture">現在水分と分離した基礎湿度です。</param>
        /// <param name="elevation">連続標高です。</param>
        /// <param name="woodyVegetation">木本植生ポテンシャルです。</param>
        /// <param name="ruggedness">地形起伏です。</param>
        /// <param name="coastalInfluence">海洋影響です。</param>
        /// <param name="volcanicActivity">火山活動度です。</param>
        /// <param name="geologyClass">固定地質分類です。</param>
        /// <param name="geologyFlags">固定地質Definitionの明示特性です。</param>
        /// <param name="seaLevelThreshold">これ未満を地上Biome候補外にする保存済み海面閾値です。</param>
        /// <param name="warpedSampleX">Biome Warp後の水平座標です。</param>
        /// <param name="warpedSampleY">Biome Warp後の垂直座標です。</param>
        /// <returns>分類器へ直接渡せるunmanaged入力です。</returns>
        private static BiomeClassificationInput CreateInput(
            ushort terrainTemperature,
            ushort baseMoisture,
            ushort elevation,
            ushort woodyVegetation,
            ushort ruggedness,
            ushort coastalInfluence,
            ushort volcanicActivity,
            GeologyClass geologyClass,
            GeologyDefinitionFlags geologyFlags = GeologyDefinitionFlags.None,
            ushort seaLevelThreshold = WorldGenerationSettings.DefaultSeaLevelThreshold,
            int warpedSampleX = 0,
            int warpedSampleY = 0)
        {
            return new BiomeClassificationInput
            {
                Environment = new GeneratedEnvironmentData
                {
                    TerrainTemperature = terrainTemperature,
                    BaseMoisture = baseMoisture,
                    Elevation = elevation,
                    WoodyVegetationPotential = woodyVegetation,
                    TerrainRuggedness = ruggedness,
                    CoastalInfluence = coastalInfluence,
                    VolcanicActivityPotential = volcanicActivity,
                    WarpedSampleX = warpedSampleX,
                    WarpedSampleY = warpedSampleY
                },
                GeologyClass = geologyClass,
                GeologyFlags = geologyFlags,
                SeaLevelThreshold = seaLevelThreshold
            };
        }
    }
}
