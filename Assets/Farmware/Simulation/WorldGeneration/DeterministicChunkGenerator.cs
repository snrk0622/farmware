// このファイルは、ワールド設定と整数タイル座標だけからバイオーム・地面・ルール用データを生成します。
// ECS Job、テスト、将来のセーブ再生成が同じ純粋関数を共有し、チャンク生成順に依存しない結果を保証します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 7種類の陸上バイオーム、土壌、淡水・海水、水域の生成由来、マグマ、岩壁を決定論的に分類します。
    /// UnityEngine.Objectやグローバル乱数を参照しないため、Burst Jobから並列に呼び出せます。
    /// </summary>
    public static class DeterministicChunkGenerator
    {
        /// <summary>
        /// 指定ワールド座標が、通常の地形生成規則で岩壁になるかだけを判定します。
        /// Presentationがチャンク外周の隣接岩壁を調べる際に、気温・湿度など不要な値の生成を省くために使用します。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>水域とマグマの優先判定後もGroundType.RockWallになる場合はtrueです。</returns>
        public static bool IsRockWall(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            // 約9割の非候補タイルでは標高・淡水ノイズを計算せず、チャンク外周の影・露出面判定を軽量に保ちます。
            if (!DeterministicRockWallGenerator.IsRockWallCandidate(
                settings,
                worldTileX,
                worldTileY))
            {
                return false;
            }

            if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedEnvironmentData environment))
            {
                // ワールド端で固定半径サンプルがint範囲を越える場合は、反対端へ周回した偽の岩壁を作りません。
                return false;
            }

            ushort elevation = environment.Elevation;
            ushort temperature = environment.BaseTemperature;
            ushort moisture = environment.BaseMoisture;
            ushort weirdness = SampleLegacyWeirdness(settings, environment);

            BiomeId biome = ClassifyBiome(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings);
            MagmaGenerationDebugInfo magmaInfo = GetMagmaGenerationDebugInfo(
                elevation,
                temperature,
                weirdness,
                biome,
                settings,
                worldTileX,
                worldTileY);

            // 地形生成と同じ「海 > マグマ > 淡水 > 岩壁」の優先順位を使い、外周影だけが異なる結果にならないようにします。
            WaterGenerationDebugInfo waterInfo = GetWaterGenerationDebugInfo(
                elevation,
                temperature,
                moisture,
                weirdness,
                magmaInfo.IsInfluenceArea,
                settings,
                worldTileX,
                worldTileY);
            WaterBodyType waterBodyType = waterInfo.WaterBodyType;
            return waterBodyType == WaterBodyType.None && !magmaInfo.IsMagma;
        }

        /// <summary>
        /// 1つのワールドタイル座標に対応する基礎地形データを生成します。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>バイオーム、地面、建築可否、連続気候値を含むタイルデータです。</returns>
        public static GeneratedTile GenerateTile(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            TryGenerateTile(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedTile tile);
            return tile;
        }

        /// <summary>
        /// 1つのワールドタイル座標に対応する基礎地形データを生成し、固定半径サンプルが座標域外なら失敗を返します。
        /// ChunkGenerationSystemはこの戻り値を使い、不完全なチャンクをReadyとして公開しません。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="tile">成功時に全基礎値を持つタイルを返します。失敗時はdefaultです。</param>
        /// <returns>設定と固定サンプル座標が有効ならtrue、それ以外はfalseです。</returns>
        public static bool TryGenerateTile(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out GeneratedTile tile)
        {
            tile = default;
            if (!DeterministicGeologyGenerator.TryGenerate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedGeologyData geology))
            {
                return false;
            }

            return TryGenerateTileWithGeology(
                settings,
                worldTileX,
                worldTileY,
                geology,
                out tile);
        }

        /// <summary>
        /// チャンク共有の地質サイトキャッシュを使って1タイルの基礎地形データを生成します。
        /// Chunk生成Jobとチャンク状態ハッシュだけが呼び、公開APIと同じ最終タイル値を返します。
        /// </summary>
        /// <param name="settings">キャッシュ作成時と同じワールド生成設定です。</param>
        /// <param name="geologyCache">対象チャンクの候補サイトと分類を保持する一時キャッシュです。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="tile">成功時に全基礎値を持つタイルを返します。失敗時はdefaultです。</param>
        /// <returns>キャッシュ内の5×5候補と環境固定サンプルが有効ならtrueです。</returns>
        internal static bool TryGenerateTile(
            in WorldGenerationSettings settings,
            in GeologyChunkCache geologyCache,
            int worldTileX,
            int worldTileY,
            out GeneratedTile tile)
        {
            tile = default;
            if (!DeterministicGeologyGenerator.TryGenerateFromChunkCache(
                settings,
                geologyCache,
                worldTileX,
                worldTileY,
                out GeneratedGeologyData geology))
            {
                return false;
            }

            return TryGenerateTileWithGeology(
                settings,
                worldTileX,
                worldTileY,
                geology,
                out tile);
        }

        /// <summary>
        /// 確定済み地質と同じ座標の環境・旧地形分類を合成し、GeneratedTileを完成させます。
        /// 単一タイル生成とチャンクキャッシュ生成の結果を同じ代入処理へ集約するために呼びます。
        /// </summary>
        /// <param name="settings">環境、旧7Biome、地面、水域を生成する固定設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="geology">同じ絶対座標で確定済みの固定地質結果です。</param>
        /// <param name="tile">成功時に環境と地質を含むタイルを返します。失敗時はdefaultです。</param>
        /// <returns>環境の固定半径サンプルが座標域内で生成できた場合はtrueです。</returns>
        private static bool TryGenerateTileWithGeology(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            in GeneratedGeologyData geology,
            out GeneratedTile tile)
        {
            tile = default;
            if (!DeterministicEnvironmentGenerator.TryGenerate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedEnvironmentData environment))
            {
                return false;
            }

            ushort elevation = environment.Elevation;
            ushort temperature = environment.BaseTemperature;
            ushort moisture = environment.BaseMoisture;
            ushort weirdness = SampleLegacyWeirdness(settings, environment);

            BiomeId biome = ClassifyBiome(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings);
            MagmaGenerationDebugInfo magmaInfo = GetMagmaGenerationDebugInfo(
                elevation,
                temperature,
                weirdness,
                biome,
                settings,
                worldTileX,
                worldTileY);
            WaterGenerationDebugInfo waterInfo = GetWaterGenerationDebugInfo(
                elevation,
                temperature,
                moisture,
                weirdness,
                magmaInfo.IsInfluenceArea,
                settings,
                worldTileX,
                worldTileY);
            WaterBodyType waterBodyType = waterInfo.WaterBodyType;
            GroundType ground = ClassifyGround(
                waterBodyType,
                magmaInfo.IsMagma,
                settings,
                worldTileX,
                worldTileY);
            SoilType soil = ground == GroundType.Land
                ? ClassifySoil(
                    temperature,
                    moisture,
                    biome,
                    settings,
                    worldTileX,
                    worldTileY)
                : SoilType.None;

            tile = new GeneratedTile
            {
                Biome = biome,
                Ground = ground,
                Soil = soil,
                WaterSalinity = ClassifyWaterSalinity(waterBodyType),
                WaterBodyType = waterBodyType,
                ElevationClass = DeterministicEnvironmentGenerator.ClassifyElevation(elevation, settings),
                GameplayFlags = ClassifyGameplayFlags(ground),
                ResourceSuitability = ClassifyResourceSuitability(ground, soil, biome),
                MovementCost = ClassifyMovementCost(ground),
                Elevation = elevation,
                Temperature = temperature,
                Moisture = moisture,
                Weirdness = weirdness,
                TerrainTemperature = environment.TerrainTemperature,
                WoodyVegetationPotential = environment.WoodyVegetationPotential,
                TerrainRuggedness = environment.TerrainRuggedness,
                CoastalInfluence = environment.CoastalInfluence,
                VolcanicActivityPotential = environment.VolcanicActivityPotential,
                GeologyClass = geology.Class,
                GeologyFlags = geology.Flags,
                GeologyPermeability = geology.Permeability,
                GeologyGroundwaterStorage = geology.GroundwaterStorage,
                GeologyErosionResistance = geology.ErosionResistance
            };
            return true;
        }

        /// <summary>
        /// TERRAIN-01C5まで旧7Biome分類を維持するため、新方式と別SaltのWeirdnessだけを取得します。
        /// 新しい火山活動度へ暗黙に改名せず、移行中も両方の生成結果を状態ハッシュへ含めます。
        /// </summary>
        /// <param name="settings">旧Weirdnessの格子とSaltを保持する設定です。</param>
        /// <param name="environment">新方式と共有するBiome Warp後座標です。</param>
        /// <returns>旧7Biome分類専用の0〜65,535のWeirdnessです。</returns>
        private static ushort SampleLegacyWeirdness(
            in WorldGenerationSettings settings,
            in GeneratedEnvironmentData environment)
        {
            return DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                environment.WarpedSampleX,
                environment.WarpedSampleY,
                settings.WeirdnessCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Weirdness);
        }

        /// <summary>
        /// 火山適性と合成密度から、マグマ本体とそれを囲む淡水抑制域を判定します。
        /// 岩地では火山と異なり、従来どおり小さく希少な溜まり向けの専用ノイズを使用します。
        /// 火山では広く、岩地では別の細かな高閾値ノイズで稀かつ小さくなるよう、バイオーム別プロファイルを使います。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="weirdness">0〜65535の特殊地形度です。</param>
        /// <param name="biome">連続気候値から分類済みの陸上バイオームです。</param>
        /// <param name="settings">マグマ形状と火山適性の閾値です。</param>
        /// <param name="worldTileX">マグマノイズを読む水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">マグマノイズを読む垂直ワールドタイル座標です。</param>
        /// <returns>火山適性、専用ノイズ、影響域、マグマ本体判定をまとめた診断値です。</returns>
        public static MagmaGenerationDebugInfo GetMagmaGenerationDebugInfo(
            ushort elevation,
            ushort temperature,
            ushort weirdness,
            BiomeId biome,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            ushort volcanicInfluence = CalculateVolcanicInfluence(
                elevation,
                temperature,
                weirdness,
                settings);
            bool usesRocklandProfile = biome == BiomeId.Rockland;
            bool isEligibleBiome = biome == BiomeId.Volcanic || usesRocklandProfile;
            if (!isEligibleBiome)
            {
                // 対象外バイオームでは専用ノイズ自体を読まず、広域チャンク生成の不要な計算を避けます。
                return new MagmaGenerationDebugInfo
                {
                    VolcanicInfluence = volcanicInfluence,
                    MagmaSample = 0,
                    RawInfluenceArea = false,
                    RawMagmaCandidate = false,
                    IsEligibleBiome = false,
                    UsesRocklandProfile = false,
                    IsInfluenceArea = false,
                    IsMagma = false
                };
            }

            ushort magmaNoiseThreshold = usesRocklandProfile
                ? settings.RocklandMagmaNoiseThreshold
                : settings.MagmaNoiseThreshold;
            ushort influenceNoiseThreshold = usesRocklandProfile
                ? settings.RocklandMagmaInfluenceNoiseThreshold
                : settings.MagmaInfluenceNoiseThreshold;
            ushort minimumVolcanicInfluence = usesRocklandProfile
                ? settings.RocklandMagmaMinimumVolcanicInfluence
                : settings.MagmaMinimumVolcanicInfluence;
            ushort influenceMinimumVolcanicInfluence = usesRocklandProfile
                ? settings.RocklandMagmaInfluenceMinimumVolcanicInfluence
                : settings.MagmaInfluenceMinimumVolcanicInfluence;

            ushort magmaSample;
            bool rawInfluenceArea;
            bool rawMagmaCandidate;
            bool smoothedInfluenceArea;
            bool smoothedMagmaCandidate;
            if (usesRocklandProfile)
            {
                // 岩地は従来の小さな単一ノイズを維持し、火山の広い不規則形状が岩地へ流出することを防ぎます。
                magmaSample = DeterministicNoise.SampleFractalNoise(
                    settings.WorldSeed,
                    settings.GeneratorVersion,
                    worldTileX,
                    worldTileY,
                    settings.RocklandMagmaCellSizeInTiles,
                    settings.MagmaOctaveCount,
                    WorldGenerationPurposeSalts.RocklandMagma);
                rawInfluenceArea = magmaSample >= influenceNoiseThreshold;
                rawMagmaCandidate = magmaSample >= magmaNoiseThreshold;
                smoothedInfluenceArea = rawInfluenceArea;
                smoothedMagmaCandidate = rawMagmaCandidate;
            }
            else
            {
                // 火山では大域・細部・座標ワープを合成し、単純な丸い塊ではなく入り江や半島を持つ輪郭を作ります。
                magmaSample = DeterministicMagmaGenerator.SampleDensity(
                    settings,
                    worldTileX,
                    worldTileY);
                rawInfluenceArea = magmaSample >= influenceNoiseThreshold;
                rawMagmaCandidate = magmaSample >= magmaNoiseThreshold;
                smoothedInfluenceArea = DeterministicMagmaGenerator.IsSmoothedCandidate(
                    settings,
                    worldTileX,
                    worldTileY,
                    influenceNoiseThreshold,
                    magmaSample);
                smoothedMagmaCandidate = DeterministicMagmaGenerator.IsSmoothedCandidate(
                    settings,
                    worldTileX,
                    worldTileY,
                    magmaNoiseThreshold,
                    magmaSample);
            }

            // 本体より低い2つの閾値で広めの影響域を作り、川・池との間に火山性の陸地を残します。
            bool isInfluenceArea = volcanicInfluence >= influenceMinimumVolcanicInfluence
                && smoothedInfluenceArea;
            bool isMagma = volcanicInfluence >= minimumVolcanicInfluence
                && smoothedMagmaCandidate;

            return new MagmaGenerationDebugInfo
            {
                VolcanicInfluence = volcanicInfluence,
                MagmaSample = magmaSample,
                RawInfluenceArea = rawInfluenceArea,
                RawMagmaCandidate = rawMagmaCandidate,
                IsEligibleBiome = isEligibleBiome,
                UsesRocklandProfile = usesRocklandProfile,
                IsInfluenceArea = isInfluenceArea,
                IsMagma = isMagma
            };
        }

        /// <summary>
        /// 連続気候値から池の下限と川幅を補正し、最終水域種別と診断値を返します。
        /// Simulation、開発者HUD、EditModeテストがこの1つの計算を共有し、アルゴリズム変更時の表示ずれを防ぎます。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="weirdness">0〜65535の特殊地形度です。</param>
        /// <param name="settings">水域と気候境界の設定です。</param>
        /// <param name="worldTileX">水域ノイズを読む水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">水域ノイズを読む垂直ワールドタイル座標です。</param>
        /// <returns>気候影響、補正後閾値、ノイズ値、最終水域種別をまとめた診断値です。</returns>
        public static WaterGenerationDebugInfo GetWaterGenerationDebugInfo(
            ushort elevation,
            ushort temperature,
            ushort moisture,
            ushort weirdness,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            BiomeId biome = ClassifyBiome(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings);
            MagmaGenerationDebugInfo magmaInfo = GetMagmaGenerationDebugInfo(
                elevation,
                temperature,
                weirdness,
                biome,
                settings,
                worldTileX,
                worldTileY);
            return GetWaterGenerationDebugInfo(
                elevation,
                temperature,
                moisture,
                weirdness,
                magmaInfo.IsInfluenceArea,
                settings,
                worldTileX,
                worldTileY);
        }

        /// <summary>
        /// 呼び出し側で計算済みのマグマ影響域を使い、海と淡水の優先判定を重複ノイズ計算なしで行います。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="weirdness">0〜65535の特殊地形度です。</param>
        /// <param name="suppressFreshWater">マグマ周辺として川と池を生成しない場合はtrueです。</param>
        /// <param name="settings">水域と気候境界の設定です。</param>
        /// <param name="worldTileX">水域ノイズを読む水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">水域ノイズを読む垂直ワールドタイル座標です。</param>
        /// <returns>気候補正と優先判定を終えた水域診断値です。</returns>
        private static WaterGenerationDebugInfo GetWaterGenerationDebugInfo(
            ushort elevation,
            ushort temperature,
            ushort moisture,
            ushort weirdness,
            bool suppressFreshWater,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            ushort wetClimateInfluence = CalculateAboveThresholdWeight(
                moisture,
                settings.WetMoistureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles);
            ushort saturatedInfluence = CalculateAboveThresholdWeight(
                moisture,
                settings.WetlandMoistureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles);
            ushort lowlandInfluence = InvertWeight(CalculateAboveThresholdWeight(
                elevation,
                settings.WetlandMaximumElevation,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.ElevationCellSizeInTiles));
            ushort wetlandInfluence = MultiplyWeights(saturatedInfluence, lowlandInfluence);
            ushort hotInfluence = CalculateAboveThresholdWeight(
                temperature,
                settings.HotTemperatureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.TemperatureCellSizeInTiles);
            ushort dryInfluence = InvertWeight(CalculateAboveThresholdWeight(
                moisture,
                settings.DryMoistureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles));
            ushort desertInfluence = MultiplyWeights(hotInfluence, dryInfluence);
            ushort highlandInfluence = CalculateAboveThresholdWeight(
                elevation,
                settings.HighlandElevationThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.ElevationCellSizeInTiles);
            ushort unusualInfluence = CalculateAboveThresholdWeight(
                weirdness,
                settings.VolcanicWeirdnessThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.WeirdnessCellSizeInTiles);
            ushort volcanicInfluence = MultiplyWeights(
                MultiplyWeights(hotInfluence, highlandInfluence),
                unusualInfluence);

            // 池を増やす補正は下限から引き、減らす補正は足します。longで計算して途中の符号と範囲を安全に保ちます。
            long effectivePondThreshold = settings.PondThreshold;
            effectivePondThreshold -= ScaleAdjustment(
                settings.WetClimatePondThresholdReduction,
                wetClimateInfluence);
            effectivePondThreshold -= ScaleAdjustment(
                settings.WetlandPondThresholdReduction,
                wetlandInfluence);
            effectivePondThreshold += ScaleAdjustment(
                settings.DesertPondThresholdIncrease,
                desertInfluence);
            effectivePondThreshold += ScaleAdjustment(
                settings.HighlandPondThresholdIncrease,
                highlandInfluence);
            effectivePondThreshold += ScaleAdjustment(
                settings.VolcanicPondThresholdIncrease,
                volcanicInfluence);
            ushort clampedPondThreshold = ClampToUShort(effectivePondThreshold);

            // 川は湿地で太く、火山で細くします。基準幅が正なら最低1を残し、バイオーム境界で川が完全に分断されないようにします。
            long effectiveRiverHalfWidth = settings.RiverCenterLineHalfWidth;
            effectiveRiverHalfWidth += ScaleAdjustment(
                settings.WetlandRiverHalfWidthIncrease,
                wetlandInfluence);
            effectiveRiverHalfWidth -= ScaleAdjustment(
                settings.VolcanicRiverHalfWidthReduction,
                volcanicInfluence);
            long minimumRiverHalfWidth = settings.RiverCenterLineHalfWidth > 0 ? 1L : 0L;
            if (effectiveRiverHalfWidth < minimumRiverHalfWidth)
            {
                effectiveRiverHalfWidth = minimumRiverHalfWidth;
            }

            ushort clampedRiverHalfWidth = ClampToUShort(effectiveRiverHalfWidth);
            ushort pondSample = 0;
            ushort riverSample = 0;
            int riverDistance = 32768;

            WaterBodyType waterBodyType;
            if (elevation < settings.SeaLevelThreshold)
            {
                // 海は広域標高の最低部を占め、気候補正の影響を受けない海水地形として最優先します。
                waterBodyType = WaterBodyType.Sea;
            }
            else if (suppressFreshWater)
            {
                // 海はマグマへ直接接してよい一方、川と池は広めの火山影響域から排除して不自然な1タイル接触を減らします。
                waterBodyType = WaterBodyType.None;
            }
            else if (elevation > settings.FreshWaterMaximumElevation)
            {
                // 山頂まで淡水ノイズが貫通すると不自然なため、淡水を生成できる標高上限を明示します。
                waterBodyType = WaterBodyType.None;
            }
            else
            {
                // 海と高地では不要な淡水ノイズを読まず、広域チャンク生成時の計算量を抑えます。
                pondSample = DeterministicNoise.SampleFractalNoise(
                    settings.WorldSeed,
                    settings.GeneratorVersion,
                    worldTileX,
                    worldTileY,
                    settings.PondCellSizeInTiles,
                    settings.FreshWaterOctaveCount,
                    WorldGenerationPurposeSalts.Pond);
                if (clampedPondThreshold < ushort.MaxValue
                    && pondSample >= clampedPondThreshold)
                {
                    // 下限が最大値へ達した火山中心部は、ノイズが偶然最大になっても池を生成しません。
                    waterBodyType = WaterBodyType.Pond;
                }
                else
                {
                    riverSample = DeterministicNoise.SampleFractalNoise(
                        settings.WorldSeed,
                        settings.GeneratorVersion,
                        worldTileX,
                        worldTileY,
                        settings.RiverCellSizeInTiles,
                        settings.FreshWaterOctaveCount,
                        WorldGenerationPurposeSalts.River);
                    riverDistance = riverSample >= 32768
                        ? riverSample - 32768
                        : 32768 - riverSample;
                    waterBodyType = riverDistance <= clampedRiverHalfWidth
                        ? WaterBodyType.River
                        : WaterBodyType.None;
                }
            }

            return new WaterGenerationDebugInfo
            {
                PondSample = pondSample,
                EffectivePondThreshold = clampedPondThreshold,
                RiverSample = riverSample,
                RiverDistanceFromCenterLine = (ushort)riverDistance,
                EffectiveRiverHalfWidth = clampedRiverHalfWidth,
                WetClimateInfluence = wetClimateInfluence,
                WetlandInfluence = wetlandInfluence,
                DesertInfluence = desertInfluence,
                HighlandInfluence = highlandInfluence,
                VolcanicInfluence = volcanicInfluence,
                WaterBodyType = waterBodyType
            };
        }

        /// <summary>
        /// 0〜65535の連続重みに応じて、設定された最大補正量の一部を整数演算で求めます。
        /// </summary>
        /// <param name="maximumAdjustment">重みが最大のときに適用する補正量です。</param>
        /// <param name="influence">適用割合を表す0〜65535の連続重みです。</param>
        /// <returns>0から最大補正量までの整数値です。</returns>
        private static long ScaleAdjustment(ushort maximumAdjustment, ushort influence)
        {
            return ((long)maximumAdjustment * influence) / ushort.MaxValue;
        }

        /// <summary>
        /// 高温・高地・高Weirdnessの連続重みを掛け合わせ、火山中心への近さを0〜65535で求めます。
        /// 水域補正とマグマ生成が同じ定義を共有し、片方だけ火山境界がずれることを防ぎます。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="weirdness">0〜65535の特殊地形度です。</param>
        /// <param name="settings">気候閾値、移行幅、各ノイズ格子サイズです。</param>
        /// <returns>3条件すべてが強い場合だけ大きくなる0〜65535の連続火山適性です。</returns>
        private static ushort CalculateVolcanicInfluence(
            ushort elevation,
            ushort temperature,
            ushort weirdness,
            in WorldGenerationSettings settings)
        {
            ushort hotInfluence = CalculateAboveThresholdWeight(
                temperature,
                settings.HotTemperatureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.TemperatureCellSizeInTiles);
            ushort highlandInfluence = CalculateAboveThresholdWeight(
                elevation,
                settings.HighlandElevationThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.ElevationCellSizeInTiles);
            ushort unusualInfluence = CalculateAboveThresholdWeight(
                weirdness,
                settings.VolcanicWeirdnessThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.WeirdnessCellSizeInTiles);
            return MultiplyWeights(
                MultiplyWeights(hotInfluence, highlandInfluence),
                unusualInfluence);
        }

        /// <summary>
        /// 符号付きの補正結果を0〜65535へ収め、設定Componentへ格納できる固定小数点値へ変換します。
        /// </summary>
        /// <param name="value">加減算を終えた補正値です。</param>
        /// <returns>ushortの範囲へ収めた値です。</returns>
        private static ushort ClampToUShort(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return value >= ushort.MaxValue
                ? ushort.MaxValue
                : (ushort)value;
        }

        /// <summary>
        /// 水域の生成由来を、採水や生物判定で使う淡水・海水へ変換します。
        /// </summary>
        /// <param name="waterBodyType">川、池、海の生成由来です。</param>
        /// <returns>川と池は淡水、海は海水、陸地はNoneです。</returns>
        private static WaterSalinity ClassifyWaterSalinity(WaterBodyType waterBodyType)
        {
            if (waterBodyType == WaterBodyType.Sea)
            {
                return WaterSalinity.SaltWater;
            }

            return waterBodyType == WaterBodyType.River || waterBodyType == WaterBodyType.Pond
                ? WaterSalinity.FreshWater
                : WaterSalinity.None;
        }

        /// <summary>
        /// 指定チャンクの4096地形、地下水反映済み64水文セル、4096表流水、4096自然水文の安定64bitハッシュを求めます。
        /// 水文と表流水の有限探索用一時配列を確保し、決定性テストと開発用比較表示に使用します。
        /// </summary>
        /// <param name="settings">ワールド生成設定です。</param>
        /// <param name="chunkCoordinate">生成結果を比較する論理チャンク座標です。</param>
        /// <returns>地形、水文セル、表流水、自然土壌飽和の順序と全固定値を反映した64bitハッシュです。</returns>
        public static ulong ComputeChunkHash(
            in WorldGenerationSettings settings,
            ChunkCoordinate chunkCoordinate)
        {
            ulong hash = 0xCBF29CE484222325UL;
            bool hasGeologyCache = DeterministicGeologyGenerator.TryCreateChunkCache(
                settings,
                chunkCoordinate.X,
                chunkCoordinate.Y,
                out GeologyChunkCache geologyCache);

            // 行優先の順序を固定し、同じ4096タイルでも並びが変われば別ハッシュになるようにします。
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int worldTileX = WorldGrid.ToWorldTileX(chunkCoordinate.X, localX);
                    int worldTileY = WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY);
                    GeneratedTile tile;
                    if (!hasGeologyCache
                        || !TryGenerateTile(
                            settings,
                            geologyCache,
                            worldTileX,
                            worldTileY,
                            out tile))
                    {
                        // 座標端や不正設定では従来の単一タイルAPIへ戻し、公開済みハッシュ契約を維持します。
                        tile = GenerateTile(settings, worldTileX, worldTileY);
                    }
                    hash = HashTile(hash, tile);
                }
            }

            // 水文64件、表流水4096件、自然水文4096件を同じ一括生成契約の順序で追加します。
            return DeterministicHydrologyGenerator.AppendChunkHash(
                settings,
                chunkCoordinate,
                hash);
        }

        /// <summary>
        /// 標高、気温、湿度、Weirdnessから7種類の陸上バイオームを選びます。
        /// 各バイオームの適性スコアを比較し、強く隣接抑制する2候補が僅差の場合は草原または岩地を緩衝帯として挟みます。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="weirdness">0〜65535の特殊地形度です。高いほど火山候補が強くなります。</param>
        /// <param name="settings">判定閾値を保持する生成設定です。</param>
        /// <returns>草原、森林、砂漠、岩地、湿地、雪原、火山のいずれかです。</returns>
        public static BiomeId ClassifyBiome(
            ushort elevation,
            ushort temperature,
            ushort moisture,
            ushort weirdness,
            in WorldGenerationSettings settings)
        {
            ushort cold = InvertWeight(CalculateAboveThresholdWeight(
                temperature,
                settings.ColdTemperatureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.TemperatureCellSizeInTiles));
            ushort hot = CalculateAboveThresholdWeight(
                temperature,
                settings.HotTemperatureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.TemperatureCellSizeInTiles);
            ushort temperate = MinimumWeight(InvertWeight(cold), InvertWeight(hot));

            ushort dry = InvertWeight(CalculateAboveThresholdWeight(
                moisture,
                settings.DryMoistureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles));
            ushort wet = CalculateAboveThresholdWeight(
                moisture,
                settings.WetMoistureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles);
            ushort saturated = CalculateAboveThresholdWeight(
                moisture,
                settings.WetlandMoistureThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles);
            ushort moderateMoisture = MinimumWeight(InvertWeight(dry), InvertWeight(wet));

            ushort lowland = InvertWeight(CalculateAboveThresholdWeight(
                elevation,
                settings.WetlandMaximumElevation,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.ElevationCellSizeInTiles));
            ushort highland = CalculateAboveThresholdWeight(
                elevation,
                settings.HighlandElevationThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.ElevationCellSizeInTiles);
            ushort volcanic = CalculateAboveThresholdWeight(
                weirdness,
                settings.VolcanicWeirdnessThreshold,
                settings.BiomeClassificationTransitionWidthInTiles,
                settings.WeirdnessCellSizeInTiles);

            // 係数は「どの気候要素を優先するか」を表します。すべて整数なので、実行環境による丸め差が発生しません。
            long grasslandScore = (2L * temperate)
                + (2L * moderateMoisture)
                + InvertWeight(highland)
                + 32768L;
            long forestScore = (3L * wet)
                + (2L * temperate)
                + InvertWeight(highland);
            long desertScore = (3L * hot)
                + (3L * dry)
                + InvertWeight(highland);
            long rocklandScore = (4L * highland)
                + dry
                + 32768L;
            long wetlandScore = (4L * saturated)
                + (2L * lowland)
                + temperate;
            long snowfieldScore = (4L * cold)
                + highland
                + InvertWeight(hot);
            long volcanicScore = (2L * hot)
                + (2L * highland)
                + (3L * volcanic)
                + dry;

            BiomeId bestBiome = BiomeId.Grassland;
            long bestScore = grasslandScore;
            BiomeId secondBiome = BiomeId.Grassland;
            long secondScore = long.MinValue;
            TrackBiomeCandidate(BiomeId.Forest, forestScore, ref bestBiome, ref bestScore, ref secondBiome, ref secondScore);
            TrackBiomeCandidate(BiomeId.Desert, desertScore, ref bestBiome, ref bestScore, ref secondBiome, ref secondScore);
            TrackBiomeCandidate(BiomeId.Rockland, rocklandScore, ref bestBiome, ref bestScore, ref secondBiome, ref secondScore);
            TrackBiomeCandidate(BiomeId.Wetland, wetlandScore, ref bestBiome, ref bestScore, ref secondBiome, ref secondScore);
            TrackBiomeCandidate(BiomeId.Snowfield, snowfieldScore, ref bestBiome, ref bestScore, ref secondBiome, ref secondScore);
            TrackBiomeCandidate(BiomeId.Volcanic, volcanicScore, ref bestBiome, ref bestScore, ref secondBiome, ref secondScore);

            // 強い抑制関係の境界を直接接続せず、低地は草原、高地は岩地へ置き換えて自然な遷移を作ります。
            if (IsStronglySuppressedBiomePair(bestBiome, secondBiome)
                && bestScore - secondScore <= settings.BiomeAdjacencyTransitionScoreMargin)
            {
                return highland >= (ushort.MaxValue / 2)
                    ? BiomeId.Rockland
                    : BiomeId.Grassland;
            }

            return bestBiome;
        }

        /// <summary>
        /// バイオーム候補を上位2件へ挿入します。同点時は呼び出し順を優先し、決定論的な結果を保ちます。
        /// </summary>
        /// <param name="candidateBiome">評価するバイオームです。</param>
        /// <param name="candidateScore">気候条件から計算した適性スコアです。</param>
        /// <param name="bestBiome">現在1位のバイオームで、必要なら更新します。</param>
        /// <param name="bestScore">現在1位のスコアで、必要なら更新します。</param>
        /// <param name="secondBiome">現在2位のバイオームで、必要なら更新します。</param>
        /// <param name="secondScore">現在2位のスコアで、必要なら更新します。</param>
        private static void TrackBiomeCandidate(
            BiomeId candidateBiome,
            long candidateScore,
            ref BiomeId bestBiome,
            ref long bestScore,
            ref BiomeId secondBiome,
            ref long secondScore)
        {
            if (candidateScore > bestScore)
            {
                secondBiome = bestBiome;
                secondScore = bestScore;
                bestBiome = candidateBiome;
                bestScore = candidateScore;
                return;
            }

            if (candidateScore > secondScore)
            {
                secondBiome = candidateBiome;
                secondScore = candidateScore;
            }
        }

        /// <summary>
        /// 仕様で直接接続を強く抑制したバイオームの組み合わせかを、順序に依存せず判定します。
        /// </summary>
        /// <param name="first">比較する1つ目のバイオームです。</param>
        /// <param name="second">比較する2つ目のバイオームです。</param>
        /// <returns>間に緩衝バイオームを挟む対象ならtrueです。</returns>
        public static bool IsStronglySuppressedBiomePair(BiomeId first, BiomeId second)
        {
            return IsBiomePair(first, second, BiomeId.Forest, BiomeId.Desert)
                || IsBiomePair(first, second, BiomeId.Forest, BiomeId.Volcanic)
                || IsBiomePair(first, second, BiomeId.Desert, BiomeId.Wetland)
                || IsBiomePair(first, second, BiomeId.Desert, BiomeId.Snowfield)
                || IsBiomePair(first, second, BiomeId.Wetland, BiomeId.Volcanic)
                || IsBiomePair(first, second, BiomeId.Snowfield, BiomeId.Volcanic);
        }

        /// <summary>
        /// 2つのバイオームが、指定した組み合わせと順不同で一致するかを返します。
        /// </summary>
        /// <param name="first">実際の1つ目のバイオームです。</param>
        /// <param name="second">実際の2つ目のバイオームです。</param>
        /// <param name="expectedFirst">期待する組み合わせの1つ目です。</param>
        /// <param name="expectedSecond">期待する組み合わせの2つ目です。</param>
        /// <returns>順序を無視して同じ組み合わせならtrueです。</returns>
        private static bool IsBiomePair(
            BiomeId first,
            BiomeId second,
            BiomeId expectedFirst,
            BiomeId expectedSecond)
        {
            return (first == expectedFirst && second == expectedSecond)
                || (first == expectedSecond && second == expectedFirst);
        }

        /// <summary>
        /// 水域、マグマ、独立岩壁群を優先し、それ以外を建築可能な陸地として分類します。
        /// 資源候補と表示差分はSoilTypeへ分離し、地表の変化だけで建築・通行ルールが変わらないようにします。
        /// </summary>
        /// <param name="waterBodyType">先に分類済みの川・池・海です。</param>
        /// <param name="isMagma">専用ノイズと火山適性からマグマ本体に選ばれた場合はtrueです。</param>
        /// <param name="settings">水面、岩壁群、バイオーム移行の設定です。</param>
        /// <param name="worldTileX">決定論的な移行タイル抽選に使う水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">決定論的な移行タイル抽選に使う垂直ワールドタイル座標です。</param>
        /// <returns>建築、通行、リンク判定に使う基礎地形種別です。</returns>
        private static GroundType ClassifyGround(
            WaterBodyType waterBodyType,
            bool isMagma,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (waterBodyType != WaterBodyType.None)
            {
                return GroundType.Water;
            }

            if (isMagma)
            {
                // 海以外の水域を抑制した後、岩壁より先にマグマを確定して両者が同じセルを占有しないようにします。
                return GroundType.Magma;
            }

            // 岩壁候補と水域が重なる場合は、直前の水域判定が先にreturnするため海・川・池が必ず優先されます。
            if (DeterministicRockWallGenerator.IsRockWallCandidate(
                settings,
                worldTileX,
                worldTileY))
            {
                return GroundType.RockWall;
            }

            return GroundType.Land;
        }

        /// <summary>
        /// 陸地の気候値と論理バイオームから、資源候補と地表差分に使う土壌を決めます。
        /// 論理Biome IDは閾値で一意に保ち、境界付近の土壌だけを段階的に混ぜて資源と見た目へ緩急を付けます。
        /// </summary>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="biome">気候から決めた陸上バイオームです。</param>
        /// <param name="settings">バイオーム移行の設定です。</param>
        /// <param name="worldTileX">決定論的な土壌抽選に使う水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">決定論的な土壌抽選に使う垂直ワールドタイル座標です。</param>
        /// <returns>陸地の資源適性と表示差分に使う土壌種別です。</returns>
        private static SoilType ClassifySoil(
            ushort temperature,
            ushort moisture,
            BiomeId biome,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            // 幅0は比較用テストや旧生成器を再現する入口として、バイオームと土壌を直接対応させます。
            if (settings.BiomeTransitionWidthInTiles <= 0)
            {
                return ClassifyDiscreteBiomeSoil(biome);
            }

            ushort hotWeight = CalculateAboveThresholdWeight(
                temperature,
                settings.HotTemperatureThreshold,
                settings.BiomeTransitionWidthInTiles,
                settings.TemperatureCellSizeInTiles);
            ushort dryWeight = InvertWeight(CalculateAboveThresholdWeight(
                moisture,
                settings.DryMoistureThreshold,
                settings.BiomeTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles));
            ushort desertWeight = MultiplyWeights(hotWeight, dryWeight);

            // 砂漠は高温と乾燥の両方が必要なので、2つの重みを掛け合わせて角でも急に切り替わらないようにします。
            ushort desertSample = WorldSeedUtility.ToUnitUShort(WorldSeedUtility.HashCoordinate(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                WorldGenerationPurposeSalts.BiomeTransition,
                candidateIndex: 0));
            if (IsSelectedByWeight(desertWeight, desertSample))
            {
                return SoilType.Sandy;
            }

            ushort forestWeight = CalculateAboveThresholdWeight(
                moisture,
                settings.WetMoistureThreshold,
                settings.BiomeTransitionWidthInTiles,
                settings.MoistureCellSizeInTiles);
            ushort forestSample = WorldSeedUtility.ToUnitUShort(WorldSeedUtility.HashCoordinate(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                WorldGenerationPurposeSalts.BiomeTransition,
                candidateIndex: 1));
            if (IsSelectedByWeight(forestWeight, forestSample))
            {
                return SoilType.Fertile;
            }

            return SoilType.Normal;
        }

        /// <summary>
        /// 移行帯を無効にした場合の、論理バイオームから土壌への直接対応を返します。
        /// </summary>
        /// <param name="biome">気候閾値から決定済みの論理バイオームです。</param>
        /// <returns>森林と湿地は肥沃土、砂漠は砂質土、それ以外は標準土壌です。</returns>
        private static SoilType ClassifyDiscreteBiomeSoil(BiomeId biome)
        {
            if (biome == BiomeId.Desert)
            {
                return SoilType.Sandy;
            }

            return biome == BiomeId.Forest || biome == BiomeId.Wetland
                ? SoilType.Fertile
                : SoilType.Normal;
        }

        /// <summary>
        /// 閾値より下を0、上を65535とする滑らかな重みを整数演算だけで求めます。
        /// 気候ノイズの格子サイズに対するタイル幅を値域へ換算し、設定変更後も移行帯の見かけ幅を近づけます。
        /// </summary>
        /// <param name="value">判定対象となる0〜65535の気候値です。</param>
        /// <param name="threshold">重みが中央になる0〜65535の閾値です。</param>
        /// <param name="transitionWidthInTiles">移行帯全体のおおよその幅です。単位はタイルです。</param>
        /// <param name="noiseCellSizeInTiles">対象気候ノイズの基準格子サイズです。単位はタイルです。</param>
        /// <returns>閾値より十分下で0、十分上で65535となる重みです。</returns>
        private static ushort CalculateAboveThresholdWeight(
            ushort value,
            ushort threshold,
            int transitionWidthInTiles,
            int noiseCellSizeInTiles)
        {
            if (transitionWidthInTiles <= 0)
            {
                return value >= threshold ? ushort.MaxValue : (ushort)0;
            }

            int safeCellSize = noiseCellSizeInTiles > 0 ? noiseCellSizeInTiles : 1;
            long halfRangeLong = ((long)ushort.MaxValue * transitionWidthInTiles)
                / (safeCellSize * 2L);
            int halfRange = (int)(halfRangeLong > ushort.MaxValue
                ? ushort.MaxValue
                : halfRangeLong);
            if (halfRange < 1)
            {
                halfRange = 1;
            }

            int lowerBound = threshold - halfRange;
            if (lowerBound < 0)
            {
                lowerBound = 0;
            }

            int upperBound = threshold + halfRange;
            if (upperBound > ushort.MaxValue)
            {
                upperBound = ushort.MaxValue;
            }

            if (value <= lowerBound)
            {
                return 0;
            }

            if (value >= upperBound)
            {
                return ushort.MaxValue;
            }

            // SmoothStepを固定小数点で計算し、OSやCPUの浮動小数点差が生成結果へ入らないようにします。
            long normalized = ((long)value - lowerBound) * ushort.MaxValue
                / (upperBound - lowerBound);
            long normalizedSquared = normalized * normalized / ushort.MaxValue;
            long smoothWeight = normalizedSquared
                * ((3L * ushort.MaxValue) - (2L * normalized))
                / ushort.MaxValue;
            return (ushort)smoothWeight;
        }

        /// <summary>
        /// 0〜65535の重みを反転し、乾燥のように値が低いほど強い条件へ変換します。
        /// </summary>
        /// <param name="weight">反転前の重みです。</param>
        /// <returns>0と65535が入れ替わった重みです。</returns>
        private static ushort InvertWeight(ushort weight)
        {
            return (ushort)(ushort.MaxValue - weight);
        }

        /// <summary>
        /// 2つの0〜65535重みの小さい方を返し、両条件を同時に満たす度合いとして利用します。
        /// </summary>
        /// <param name="first">比較する1つ目の重みです。</param>
        /// <param name="second">比較する2つ目の重みです。</param>
        /// <returns>2つのうち小さい重みです。</returns>
        private static ushort MinimumWeight(ushort first, ushort second)
        {
            return first < second ? first : second;
        }

        /// <summary>
        /// 2つの0〜65535重みを固定小数点で掛け、両条件を満たす割合を求めます。
        /// </summary>
        /// <param name="first">1つ目の重みです。</param>
        /// <param name="second">2つ目の重みです。</param>
        /// <returns>両方が強い場合だけ大きくなる0〜65535の重みです。</returns>
        private static ushort MultiplyWeights(ushort first, ushort second)
        {
            return (ushort)(((long)first * second) / ushort.MaxValue);
        }

        /// <summary>
        /// 座標ごとの固定サンプルが指定重みに含まれるかを判定します。
        /// 端点を特別扱いし、重み0で誤選択せず、重み65535では必ず選択します。
        /// </summary>
        /// <param name="weight">選択確率に相当する0〜65535の重みです。</param>
        /// <param name="sample">座標ハッシュから得た0〜65535の固定サンプルです。</param>
        /// <returns>この座標で対象地面を選ぶ場合はtrueです。</returns>
        private static bool IsSelectedByWeight(ushort weight, ushort sample)
        {
            if (weight == 0)
            {
                return false;
            }

            return weight == ushort.MaxValue || sample < weight;
        }

        /// <summary>
        /// 地面種別から通常設備、通常ベルト、橋の配置可否と、橋の耐熱要件を決めます。
        /// 後続の建築処理がGroundTypeを独自解釈せず、生成済み属性だけを参照できるようにします。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <returns>各配置可否と橋の耐熱要件を表すビットフラグです。</returns>
        public static TileGameplayFlags ClassifyGameplayFlags(GroundType ground)
        {
            switch (ground)
            {
                case GroundType.Land:
                    return TileGameplayFlags.AllowsNormalEquipment
                        | TileGameplayFlags.AllowsNormalBelt;
                case GroundType.Water:
                    // 水上では橋の素材を耐熱性で制限しないため、橋可だけを設定します。
                    return TileGameplayFlags.AllowsBridge;
                case GroundType.Magma:
                    // マグマ上の橋は耐熱性を持つ定義だけに限定できるよう、要件を独立したbitで記録します。
                    return TileGameplayFlags.AllowsBridge
                        | TileGameplayFlags.RequiresHeatResistance;
                case GroundType.RockWall:
                    return TileGameplayFlags.None;
                default:
                    // 予約済みIDや破損データを配置可能地として扱わず、すべて不許可の安全側へ倒します。
                    return TileGameplayFlags.None;
            }
        }

        /// <summary>
        /// 基礎地形、土壌、バイオームから、Phase 3で利用する資源候補フラグを決めます。
        /// ここでは候補だけを記録し、密度・群れ・最低間隔は後続の資源配置Systemで判定します。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <param name="soil">陸地の局所的な土壌種別です。</param>
        /// <param name="biome">分類済みのバイオームです。</param>
        /// <returns>配置可能な資源種別の組み合わせです。</returns>
        private static ResourceSuitability ClassifyResourceSuitability(
            GroundType ground,
            SoilType soil,
            BiomeId biome)
        {
            if (ground == GroundType.Water || ground == GroundType.Magma)
            {
                // 水は地形自体から採取し、マグマは通常資源を置かないため、どちらも別の配置資源候補にはしません。
                return ResourceSuitability.None;
            }

            if (ground == GroundType.RockWall)
            {
                return ResourceSuitability.Stone;
            }

            if (ground != GroundType.Land)
            {
                // 予約済みIDや破損データへ通常資源を生成せず、既知の陸地だけを候補にします。
                return ResourceSuitability.None;
            }

            if (soil == SoilType.Sandy || biome == BiomeId.Desert)
            {
                return ResourceSuitability.Sand | ResourceSuitability.Stone;
            }

            if (soil == SoilType.Fertile || biome == BiomeId.Forest)
            {
                return ResourceSuitability.Tree | ResourceSuitability.Stone;
            }

            if (biome == BiomeId.Rockland || biome == BiomeId.Volcanic)
            {
                return ResourceSuitability.Stone;
            }

            return ResourceSuitability.Tree | ResourceSuitability.Stone;
        }

        /// <summary>
        /// 地面種別から初期の相対移動コストを決めます。
        /// 岩壁とマグマは通行不可の0、水域は陸地より重い2、通常陸地は1とします。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <returns>0〜2の移動コストです。</returns>
        private static byte ClassifyMovementCost(GroundType ground)
        {
            if (ground == GroundType.Land)
            {
                return 1;
            }

            if (ground == GroundType.Water)
            {
                return 2;
            }

            // 岩壁、マグマ、予約済みIDは通常移動不可として0を返します。
            return 0;
        }

        /// <summary>
        /// 1タイルの全基礎値を、それまでのチャンクハッシュへ順序付きで混合します。
        /// </summary>
        /// <param name="currentHash">直前までのタイルを反映したハッシュです。</param>
        /// <param name="tile">次に混合する生成済みタイルです。</param>
        /// <returns>tileを追加した64bitハッシュです。</returns>
        private static ulong HashTile(ulong currentHash, GeneratedTile tile)
        {
            unchecked
            {
                ulong packedClassifications = (byte)tile.Biome;
                packedClassifications |= (ulong)(byte)tile.Ground << 8;
                packedClassifications |= (ulong)(byte)tile.ElevationClass << 16;
                packedClassifications |= (ulong)(byte)tile.GameplayFlags << 24;
                packedClassifications |= (ulong)(byte)tile.ResourceSuitability << 32;
                packedClassifications |= (ulong)tile.MovementCost << 40;
                packedClassifications |= (ulong)(byte)tile.WaterSalinity << 48;
                packedClassifications |= (ulong)(byte)tile.WaterBodyType << 56;

                ulong packedClimate = tile.Elevation;
                packedClimate |= (ulong)tile.Temperature << 16;
                packedClimate |= (ulong)tile.Moisture << 32;
                packedClimate |= (ulong)tile.Weirdness << 48;

                ulong packedEnvironment = tile.TerrainTemperature;
                packedEnvironment |= (ulong)tile.WoodyVegetationPotential << 16;
                packedEnvironment |= (ulong)tile.TerrainRuggedness << 32;
                packedEnvironment |= (ulong)tile.CoastalInfluence << 48;

                ulong classificationsHash = WorldSeedUtility.Mix64(currentHash ^ packedClassifications);
                // 既存8分類で64bitを使い切っているため、土壌は独立した混合段階へ加えて配置差分も検出します。
                ulong soilHash = WorldSeedUtility.Mix64(classificationsHash ^ (byte)tile.Soil);
                ulong climateHash = WorldSeedUtility.Mix64(soilHash ^ packedClimate);
                ulong environmentHash = WorldSeedUtility.Mix64(climateHash ^ packedEnvironment);
                ulong volcanicHash = WorldSeedUtility.Mix64(
                    environmentHash ^ tile.VolcanicActivityPotential);
                ulong packedGeology = (byte)tile.GeologyClass;
                packedGeology |= (ulong)(byte)tile.GeologyFlags << 8;
                packedGeology |= (ulong)tile.GeologyPermeability << 16;
                packedGeology |= (ulong)tile.GeologyGroundwaterStorage << 32;
                packedGeology |= (ulong)tile.GeologyErosionResistance << 48;
                return WorldSeedUtility.Mix64(volcanicHash ^ packedGeology);
            }
        }
    }
}
