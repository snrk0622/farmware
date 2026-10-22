// このファイルは、TERRAIN-01C5-Bの新18Biome Definitionから通常候補を評価し、安定順位で上位2件を選びます。
// C5-CでECS生成へ接続する前に、候補ゲート、整数Score、BiomeVariation、列挙順に依存しない順位を純粋計算として固定します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1タイルの通常Biome候補評価へ渡す固定環境と地質です。
    /// 地形生成結果や現在の天候を参照せず、絶対座標から再計算できる値だけを保持します。
    /// </summary>
    public struct BiomeClassificationInput
    {
        /// <summary>地形気温、基礎湿度、標高、植生傾向、起伏、海洋影響、火山活動度、Warp後座標です。</summary>
        public GeneratedEnvironmentData Environment;

        /// <summary>BiomeやBlockから独立して先に確定した1〜8の固定地質分類です。</summary>
        public GeologyClass GeologyClass;

        /// <summary>固定地質Definitionが持つ活動的火山適性などの必須条件です。</summary>
        public GeologyDefinitionFlags GeologyFlags;

        /// <summary>これ未満の標高を地上Biome候補から除外する、ワールドへ保存済みの海面閾値です。</summary>
        public ushort SeaLevelThreshold;
    }

    /// <summary>
    /// 1つの通常Biome候補について、候補資格を通過した各軸の整数適性と順位情報を保持します。
    /// C5-Eの診断は同じ純粋評価を再利用でき、表示側で分類式を作り直しません。
    /// </summary>
    public struct BiomeCandidateEvaluation
    {
        /// <summary>評価した新Biome Definitionの変更禁止IDです。</summary>
        public byte StableId;

        /// <summary>地形気温の台形型適性です。候補範囲内で0〜65,535になります。</summary>
        public ushort TerrainTemperatureFit;

        /// <summary>基礎湿度の台形型適性です。候補範囲内で0〜65,535になります。</summary>
        public ushort BaseMoistureFit;

        /// <summary>標高の台形型適性です。候補範囲内で0〜65,535になります。</summary>
        public ushort ElevationFit;

        /// <summary>木本植生ポテンシャルの台形型適性です。候補範囲内で0〜65,535になります。</summary>
        public ushort WoodyVegetationFit;

        /// <summary>地形起伏の台形型適性です。候補範囲内で0〜65,535になります。</summary>
        public ushort TerrainRuggednessFit;

        /// <summary>Definitionと固定地質分類の組合せから取得した適合度です。</summary>
        public ushort GeologyFit;

        /// <summary>火山活動度の台形型適性です。未使用軸でも診断用に全範囲適性を保持します。</summary>
        public ushort VolcanicActivityFit;

        /// <summary>海洋影響の台形型適性です。未使用軸でも診断用に全範囲適性を保持します。</summary>
        public ushort CoastalInfluenceFit;

        /// <summary>8軸の適性と重みを64bitで積和した0〜16,776,960の通常適性スコアです。</summary>
        public long Score;

        /// <summary>Scoreが完全同点の候補間だけを並べるDefinition固有Salt由来の地域値です。</summary>
        public ushort CandidateVariation;
    }

    /// <summary>
    /// 1タイルで資格を得た通常Biome候補の首位と診断用次点を保持します。
    /// 最終Biome選択はC5-B3の特殊優先を適用してから行うため、この値だけではまだ確定しません。
    /// </summary>
    public struct NormalBiomeCandidateRanking
    {
        /// <summary>通常候補の安定順位で首位になった評価です。分類成功時は必ず有効です。</summary>
        public BiomeCandidateEvaluation First;

        /// <summary>通常候補の安定順位で2位になった診断用評価です。HasSecondがfalseなら未設定です。</summary>
        public BiomeCandidateEvaluation Second;

        /// <summary>全Definitionのうち、通常候補ゲートを通過した件数です。</summary>
        public byte CandidateCount;

        /// <summary>Secondへ2位候補が格納されているかを示します。</summary>
        public bool HasSecond;
    }

    /// <summary>
    /// 新18Biomeの通常候補を、管理確保や浮動小数点を使わず決定論的に評価・順位付けします。
    /// 安定IDまで含む全順序を使うため、Definition登録順やJob実行順が変わっても同じ上位2候補を得られます。
    /// </summary>
    public static class DeterministicBiomeClassifier
    {
        /// <summary>通常Biomeの理論上の最大スコアです。65,535と重み合計256の積です。</summary>
        public const long MaximumBiomeScore = (long)ushort.MaxValue
            * BiomeDefinitionCatalog.RequiredNormalWeightTotal;

        /// <summary>
        /// 2つの通常候補をScore、Variation、安定IDの順で比較し、左候補が上位かを返します。
        /// </summary>
        /// <param name="candidate">現在の順位へ挿入できるか調べる候補です。</param>
        /// <param name="current">現在その順位にいる比較対象です。</param>
        /// <returns>Score降順、同点Variation降順、最終同点ID昇順でcandidateが上位ならtrueです。</returns>
        public static bool IsPreferredNormalCandidate(
            in BiomeCandidateEvaluation candidate,
            in BiomeCandidateEvaluation current)
        {
            if (candidate.Score != current.Score)
            {
                // VariationはScoreの完全同点だけに使い、1点でも高いScoreを地域ノイズで逆転させません。
                return candidate.Score > current.Score;
            }

            if (candidate.CandidateVariation != current.CandidateVariation)
            {
                return candidate.CandidateVariation > current.CandidateVariation;
            }

            // ScoreとVariationが同じ場合も、変更禁止の安定IDで一意な全順序へ収束させます。
            return candidate.StableId < current.StableId;
        }

        /// <summary>
        /// 検証済み18Biome設定の全Definitionを評価し、登録順に依存しない通常候補の上位2件を返します。
        /// </summary>
        /// <param name="settings">事前にBiomeDefinitionCatalog.ValidateSettingsを通した固定設定です。</param>
        /// <param name="worldSeed">ワールド寿命中に固定する64bit Seedです。</param>
        /// <param name="generatorVersion">生成規則の互換境界を表す固定Versionです。</param>
        /// <param name="input">対象タイルの固定環境と地質です。</param>
        /// <param name="ranking">成功時に首位、存在すれば次点、候補総数を返します。</param>
        /// <returns>通常候補が1件以上あればtrue、候補なしまたは設定の外形が不正ならfalseです。</returns>
        public static bool TryRankNormalCandidates(
            in BiomeClassificationSettings settings,
            ulong worldSeed,
            int generatorVersion,
            in BiomeClassificationInput input,
            out NormalBiomeCandidateRanking ranking)
        {
            ranking = default;
            if (settings.Definitions.Length != BiomeDefinitionCatalog.RequiredDefinitionCount
                || settings.VariationCellSizeInTiles
                    != BiomeDefinitionCatalog.RequiredVariationCellSizeInTiles
                || settings.VariationOctaveCount
                    != BiomeDefinitionCatalog.RequiredVariationOctaveCount)
            {
                return false;
            }

            for (int definitionIndex = 0;
                definitionIndex < settings.Definitions.Length;
                definitionIndex++)
            {
                BiomeDefinition definition = settings.Definitions[definitionIndex];
                if (!TryEvaluateNormalCandidate(
                        settings,
                        worldSeed,
                        generatorVersion,
                        definition,
                        input,
                        out BiomeCandidateEvaluation candidate))
                {
                    continue;
                }

                InsertNormalCandidate(ref ranking, candidate);
            }

            if (ranking.CandidateCount == 0)
            {
                // 候補なしでは途中値を公開せず、呼び出し側が明示的な分類失敗として扱えるようにします。
                return false;
            }

            return true;
        }

        /// <summary>
        /// 評価済み通常候補を現在の上位2件へ挿入し、3件目以降も候補総数だけは保持します。
        /// </summary>
        /// <param name="ranking">これまでに処理した候補の首位、次点、総数です。</param>
        /// <param name="candidate">候補ゲートと軸評価を通過した、今回追加する1件です。</param>
        internal static void InsertNormalCandidate(
            ref NormalBiomeCandidateRanking ranking,
            in BiomeCandidateEvaluation candidate)
        {
            // 固定18件中の通常候補は最大16件なので、byteで件数を保持しても周回しません。
            ranking.CandidateCount++;
            if (ranking.CandidateCount == 1)
            {
                ranking.First = candidate;
                return;
            }

            if (IsPreferredNormalCandidate(candidate, ranking.First))
            {
                // 新しい首位が来た場合、以前の首位は必ず次点なので、既存の次点と再比較する必要はありません。
                ranking.Second = ranking.First;
                ranking.First = candidate;
                ranking.HasSecond = true;
                return;
            }

            if (!ranking.HasSecond || IsPreferredNormalCandidate(candidate, ranking.Second))
            {
                ranking.Second = candidate;
                ranking.HasSecond = true;
            }
        }

        /// <summary>
        /// 連続軸の値を、候補許容範囲と代表範囲から0〜65,535の台形型適性へ変換します。
        /// </summary>
        /// <param name="value">評価する0〜65,535の固定環境値です。</param>
        /// <param name="range">候補許容範囲と最高適性の代表範囲です。</param>
        /// <param name="axisFit">成功時に整数除算で求めた台形型適性を返します。</param>
        /// <returns>範囲が正しく、値が候補許容範囲内ならtrueです。</returns>
        public static bool TryCalculateAxisFit(
            ushort value,
            in BiomeAxisRange range,
            out ushort axisFit)
        {
            axisFit = 0;
            if (!range.IsOrdered() || value < range.AllowedMin || value > range.AllowedMax)
            {
                return false;
            }

            if (value >= range.PreferredMin && value <= range.PreferredMax)
            {
                axisFit = ushort.MaxValue;
                return true;
            }

            if (value < range.PreferredMin)
            {
                // AllowedMinとPreferredMinが同じ場合、下側傾斜へ入る値は存在しないため0除算は起きません。
                long numerator = (long)(value - range.AllowedMin) * ushort.MaxValue;
                int denominator = range.PreferredMin - range.AllowedMin;
                axisFit = (ushort)(numerator / denominator);
                return true;
            }

            // PreferredMaxとAllowedMaxが同じ場合、上側傾斜へ入る値は存在しないため0除算は起きません。
            long upperNumerator = (long)(range.AllowedMax - value) * ushort.MaxValue;
            int upperDenominator = range.AllowedMax - range.PreferredMax;
            axisFit = (ushort)(upperNumerator / upperDenominator);
            return true;
        }

        /// <summary>
        /// 1つの通常Biome Definitionについて必須ゲートと全軸を評価し、順位比較に必要な値を返します。
        /// </summary>
        /// <param name="settings">BiomeVariationの固定格子設定を含む新18Biome設定です。</param>
        /// <param name="worldSeed">ワールド寿命中に固定する64bit Seedです。</param>
        /// <param name="generatorVersion">生成規則の互換境界を表す固定Versionです。</param>
        /// <param name="definition">評価する通常Biome Definitionです。</param>
        /// <param name="input">対象タイルの固定環境と地質です。</param>
        /// <param name="evaluation">候補になった場合だけ、軸適性、Score、Variationを返します。</param>
        /// <returns>通常候補の役割、必須ゲート、全許容範囲を満たす場合はtrueです。</returns>
        public static bool TryEvaluateNormalCandidate(
            in BiomeClassificationSettings settings,
            ulong worldSeed,
            int generatorVersion,
            in BiomeDefinition definition,
            in BiomeClassificationInput input,
            out BiomeCandidateEvaluation evaluation)
        {
            evaluation = default;
            bool usesNormalScore = (definition.Flags & BiomeDefinitionFlags.UsesNormalScore) != 0;
            bool isHydrologySpecial = (definition.Flags & BiomeDefinitionFlags.HydrologySpecial) != 0;
            if (!usesNormalScore
                || isHydrologySpecial
                || definition.StableId < BiomeStableIds.First
                || definition.StableId > BiomeStableIds.Last
                || settings.VariationCellSizeInTiles
                    != BiomeDefinitionCatalog.RequiredVariationCellSizeInTiles
                || settings.VariationOctaveCount
                    != BiomeDefinitionCatalog.RequiredVariationOctaveCount
                || input.GeologyClass < GeologyClass.UnconsolidatedSediment
                || input.GeologyClass > GeologyClass.UltramaficRock
                || input.Environment.Elevation < input.SeaLevelThreshold)
            {
                return false;
            }

            GeneratedEnvironmentData environment = input.Environment;
            if (!TryCalculateAxisFit(
                    environment.TerrainTemperature,
                    definition.TerrainTemperature,
                    out ushort terrainTemperatureFit)
                || !TryCalculateAxisFit(
                    environment.BaseMoisture,
                    definition.BaseMoisture,
                    out ushort baseMoistureFit)
                || !TryCalculateAxisFit(
                    environment.Elevation,
                    definition.Elevation,
                    out ushort elevationFit)
                || !TryCalculateAxisFit(
                    environment.WoodyVegetationPotential,
                    definition.WoodyVegetation,
                    out ushort woodyVegetationFit)
                || !TryCalculateAxisFit(
                    environment.TerrainRuggedness,
                    definition.TerrainRuggedness,
                    out ushort terrainRuggednessFit)
                || !TryCalculateAxisFit(
                    environment.VolcanicActivityPotential,
                    definition.VolcanicActivity,
                    out ushort volcanicActivityFit)
                || !TryCalculateAxisFit(
                    environment.CoastalInfluence,
                    definition.CoastalInfluence,
                    out ushort coastalInfluenceFit))
            {
                return false;
            }

            ushort geologyFit = definition.GetGeologyFit(input.GeologyClass);
            bool requiresStrongCoast = (definition.Flags
                & BiomeDefinitionFlags.RequiresStrongCoastalInfluence) != 0;
            bool requiresActiveVolcanism = (definition.Flags
                & BiomeDefinitionFlags.RequiresActiveVolcanism) != 0;
            if ((requiresStrongCoast
                    && environment.CoastalInfluence
                        < DeterministicEnvironmentGenerator.EnvironmentBand5Threshold)
                || (requiresActiveVolcanism
                    && (geologyFit == 0
                        || (input.GeologyFlags
                            & GeologyDefinitionFlags.SupportsActiveVolcanism) == 0)))
            {
                return false;
            }

            BiomeAxisWeights weights = definition.AxisWeights;
            // 各積と合計をlongで行い、重み合計256の最大値16,776,960を型と式の両方で保証します。
            long score = ((long)terrainTemperatureFit * weights.TerrainTemperature)
                + ((long)baseMoistureFit * weights.BaseMoisture)
                + ((long)elevationFit * weights.Elevation)
                + ((long)woodyVegetationFit * weights.WoodyVegetation)
                + ((long)terrainRuggednessFit * weights.TerrainRuggedness)
                + ((long)geologyFit * weights.Geology)
                + ((long)volcanicActivityFit * weights.VolcanicActivity)
                + ((long)coastalInfluenceFit * weights.CoastalInfluence);
            if (score < 0 || score > MaximumBiomeScore)
            {
                // Definition検証を迂回した不正な重みでも、失敗時に途中の評価値を呼び出し元へ公開しません。
                return false;
            }

            evaluation = new BiomeCandidateEvaluation
            {
                StableId = definition.StableId,
                TerrainTemperatureFit = terrainTemperatureFit,
                BaseMoistureFit = baseMoistureFit,
                ElevationFit = elevationFit,
                WoodyVegetationFit = woodyVegetationFit,
                TerrainRuggednessFit = terrainRuggednessFit,
                GeologyFit = geologyFit,
                VolcanicActivityFit = volcanicActivityFit,
                CoastalInfluenceFit = coastalInfluenceFit,
                Score = score,
                CandidateVariation = DeterministicNoise.SampleFractalNoise(
                    worldSeed,
                    generatorVersion,
                    environment.WarpedSampleX,
                    environment.WarpedSampleY,
                    settings.VariationCellSizeInTiles,
                    settings.VariationOctaveCount,
                    definition.VariationSalt)
            };
            return true;
        }
    }
}
