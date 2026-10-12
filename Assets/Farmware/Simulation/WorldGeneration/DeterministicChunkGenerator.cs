// このファイルは、ワールド設定と整数タイル座標だけからバイオーム・地面・ルール用データを生成します。
// ECS Job、テスト、将来のセーブ再生成が同じ純粋関数を共有し、チャンク生成順に依存しない結果を保証します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 7種類の陸上バイオーム、淡水・海水、水域の生成由来、岩壁を決定論的に分類します。
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
        /// <returns>水域の優先判定後もGroundType.RockWallになる場合はtrueです。</returns>
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

            ushort elevation = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.ElevationCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Elevation);

            SampleBiomeClimate(
                settings,
                worldTileX,
                worldTileY,
                out ushort temperature,
                out ushort moisture,
                out ushort weirdness);

            // 地形生成では水域を岩壁より先に分類します。同じ優先順位を使い、表示だけがSimulation結果と食い違うことを防ぎます。
            WaterBodyType waterBodyType = ClassifyWaterBody(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings,
                worldTileX,
                worldTileY);
            return waterBodyType == WaterBodyType.None;
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
            ushort elevation = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.ElevationCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Elevation);

            SampleBiomeClimate(
                settings,
                worldTileX,
                worldTileY,
                out ushort temperature,
                out ushort moisture,
                out ushort weirdness);

            BiomeId biome = ClassifyBiome(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings);
            WaterBodyType waterBodyType = ClassifyWaterBody(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings,
                worldTileX,
                worldTileY);
            GroundType ground = ClassifyGround(
                temperature,
                moisture,
                biome,
                waterBodyType,
                settings,
                worldTileX,
                worldTileY);

            return new GeneratedTile
            {
                Biome = biome,
                Ground = ground,
                WaterSalinity = ClassifyWaterSalinity(waterBodyType),
                WaterBodyType = waterBodyType,
                ElevationClass = ClassifyElevation(elevation, ground, settings),
                GameplayFlags = ClassifyGameplayFlags(ground),
                ResourceSuitability = ClassifyResourceSuitability(ground, biome),
                MovementCost = ClassifyMovementCost(ground),
                Elevation = elevation,
                Temperature = temperature,
                Moisture = moisture,
                Weirdness = weirdness
            };
        }

        /// <summary>
        /// 歪ませた共通座標から気温・湿度・Weirdnessをまとめて取得します。
        /// 通常生成と外周岩壁判定が同じ気候値を使い、水域優先順位の結果を一致させるために共有します。
        /// </summary>
        /// <param name="settings">気候ノイズと座標歪みの設定です。</param>
        /// <param name="worldTileX">元の水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">元の垂直ワールドタイル座標です。</param>
        /// <param name="temperature">0〜65535の気温を返します。</param>
        /// <param name="moisture">0〜65535の湿度を返します。</param>
        /// <param name="weirdness">0〜65535の特殊地形度を返します。</param>
        private static void SampleBiomeClimate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out ushort temperature,
            out ushort moisture,
            out ushort weirdness)
        {
            CalculateWarpedBiomeCoordinate(
                settings,
                worldTileX,
                worldTileY,
                out int biomeSampleX,
                out int biomeSampleY);

            temperature = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                biomeSampleX,
                biomeSampleY,
                settings.TemperatureCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Temperature);
            moisture = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                biomeSampleX,
                biomeSampleY,
                settings.MoistureCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Moisture);
            weirdness = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                biomeSampleX,
                biomeSampleY,
                settings.WeirdnessCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Weirdness);
        }

        /// <summary>
        /// 気候ノイズを読む座標へ低周波の歪みを加え、バイオーム境界が直線的・帯状になりすぎることを防ぎます。
        /// 標高と水域の形状ノイズは元座標で生成し、水域の気候補正だけがこの歪ませた気候値を参照します。
        /// </summary>
        /// <param name="settings">歪みノイズの格子サイズと最大変位量を保持する生成設定です。</param>
        /// <param name="worldTileX">歪ませる前の水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">歪ませる前の垂直ワールドタイル座標です。</param>
        /// <param name="sampleX">気温・湿度・Weirdnessを読む水平座標を返します。</param>
        /// <param name="sampleY">気温・湿度・Weirdnessを読む垂直座標を返します。</param>
        private static void CalculateWarpedBiomeCoordinate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out int sampleX,
            out int sampleY)
        {
            ushort horizontalWarp = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.BiomeWarpCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.BiomeWarpX);
            ushort verticalWarp = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.BiomeWarpCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.BiomeWarpY);

            // 0〜65535をおよそ-1〜+1へ中央寄せし、整数タイル単位の最大変位量へ変換します。
            int horizontalOffset = ((horizontalWarp - 32768) * settings.BiomeWarpAmplitudeInTiles) / 32768;
            int verticalOffset = ((verticalWarp - 32768) * settings.BiomeWarpAmplitudeInTiles) / 32768;
            sampleX = worldTileX + horizontalOffset;
            sampleY = worldTileY + verticalOffset;
        }

        /// <summary>
        /// 標高から海を先に決め、残る陸上へ気候補正した連続ノイズで池と川を生成します。
        /// 補正はBiomeIdではなく連続気候値から求めるため、バイオーム境界で川や池が不自然に切れません。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="weirdness">0〜65535の特殊地形度です。</param>
        /// <param name="settings">海面と淡水形状の設定です。</param>
        /// <param name="worldTileX">淡水ノイズをサンプリングする水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">淡水ノイズをサンプリングする垂直ワールドタイル座標です。</param>
        /// <returns>川、池、海のいずれか、または水域でなければNoneです。</returns>
        private static WaterBodyType ClassifyWaterBody(
            ushort elevation,
            ushort temperature,
            ushort moisture,
            ushort weirdness,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            WaterGenerationDebugInfo debugInfo = GetWaterGenerationDebugInfo(
                elevation,
                temperature,
                moisture,
                weirdness,
                settings,
                worldTileX,
                worldTileY);
            return debugInfo.WaterBodyType;
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
        /// 指定チャンクの4096タイルを生成した場合の安定64bitハッシュを求めます。
        /// 配列を確保せずに計算できるため、決定性テストと開発用比較表示に使用します。
        /// </summary>
        /// <param name="settings">ワールド生成設定です。</param>
        /// <param name="chunkCoordinate">生成結果を比較する論理チャンク座標です。</param>
        /// <returns>タイル順序と全基礎値を反映した64bitハッシュです。</returns>
        public static ulong ComputeChunkHash(
            in WorldGenerationSettings settings,
            ChunkCoordinate chunkCoordinate)
        {
            ulong hash = 0xCBF29CE484222325UL;

            // 行優先の順序を固定し、同じ4096タイルでも並びが変われば別ハッシュになるようにします。
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int worldTileX = WorldGrid.ToWorldTileX(chunkCoordinate.X, localX);
                    int worldTileY = WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY);
                    GeneratedTile tile = GenerateTile(settings, worldTileX, worldTileY);
                    hash = HashTile(hash, tile);
                }
            }

            return hash;
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
            if (IsStronglySuppressedPair(bestBiome, secondBiome)
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
        private static bool IsStronglySuppressedPair(BiomeId first, BiomeId second)
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
        /// 水域と独立岩壁群を優先し、その後に気候値と座標ハッシュから陸上の地面種別を決めます。
        /// 論理Biome IDは閾値で一意に保ち、境界付近の地面だけを段階的に混ぜて見た目とゲームルールをなじませます。
        /// </summary>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="biome">気候から決めた陸上バイオームです。</param>
        /// <param name="waterBodyType">先に分類済みの川・池・海です。</param>
        /// <param name="settings">水面、岩壁群、バイオーム移行の設定です。</param>
        /// <param name="worldTileX">決定論的な移行タイル抽選に使う水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">決定論的な移行タイル抽選に使う垂直ワールドタイル座標です。</param>
        /// <returns>ゲームルール用の地面種別です。</returns>
        private static GroundType ClassifyGround(
            ushort temperature,
            ushort moisture,
            BiomeId biome,
            WaterBodyType waterBodyType,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (waterBodyType != WaterBodyType.None)
            {
                return GroundType.Water;
            }

            // 岩壁候補と水域が重なる場合は、直前の水域判定が先にreturnするため海・川・池が必ず優先されます。
            if (DeterministicRockWallGenerator.IsRockWallCandidate(
                settings,
                worldTileX,
                worldTileY))
            {
                return GroundType.RockWall;
            }

            // 幅0は比較用テストや旧生成器を再現する入口として、従来の離散的な地面判定をそのまま返します。
            if (settings.BiomeTransitionWidthInTiles <= 0)
            {
                return ClassifyDiscreteBiomeGround(biome);
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
                return GroundType.Sand;
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
                return GroundType.Vegetation;
            }

            return GroundType.Normal;
        }

        /// <summary>
        /// 移行帯を無効にした場合の、論理バイオームから地面への直接対応を返します。
        /// </summary>
        /// <param name="biome">気候閾値から決定済みの論理バイオームです。</param>
        /// <returns>森林と湿地は植生地面、砂漠は砂地、それ以外は通常地面です。</returns>
        private static GroundType ClassifyDiscreteBiomeGround(BiomeId biome)
        {
            if (biome == BiomeId.Desert)
            {
                return GroundType.Sand;
            }

            return biome == BiomeId.Forest || biome == BiomeId.Wetland
                ? GroundType.Vegetation
                : GroundType.Normal;
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
        /// 連続標高を水域・低地・平地・高地へ分け、独立生成された岩壁だけを岩壁分類へ上書きします。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="ground">淡水を含めて分類済みの地面です。</param>
        /// <param name="settings">海面を保持する生成設定です。</param>
        /// <returns>表示や配置条件で使う標高クラスです。</returns>
        private static ElevationClass ClassifyElevation(
            ushort elevation,
            GroundType ground,
            in WorldGenerationSettings settings)
        {
            // 川と池は元の標高が海面より高くても、水域として同じ分類を返します。
            if (ground == GroundType.Water)
            {
                return ElevationClass.Water;
            }

            if (ground == GroundType.RockWall)
            {
                return ElevationClass.RockWall;
            }

            int landRange = ushort.MaxValue - settings.SeaLevelThreshold;
            int lowlandEnd = settings.SeaLevelThreshold + (landRange / 3);
            int plainEnd = settings.SeaLevelThreshold + ((landRange * 2) / 3);

            if (elevation < lowlandEnd)
            {
                return ElevationClass.Lowland;
            }

            return elevation < plainEnd ? ElevationClass.Plain : ElevationClass.Highland;
        }

        /// <summary>
        /// 地面種別から建築可否と通常リンク遮断を決めます。
        /// 水は建築不可でもリンクを通し、岩壁だけがリンクを遮る仕様をここで明示します。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <returns>建築・リンク判定のビットフラグです。</returns>
        private static TileGameplayFlags ClassifyGameplayFlags(GroundType ground)
        {
            switch (ground)
            {
                case GroundType.Water:
                    return TileGameplayFlags.None;
                case GroundType.RockWall:
                    return TileGameplayFlags.BlocksNormalLink;
                default:
                    return TileGameplayFlags.IsBuildable;
            }
        }

        /// <summary>
        /// 地面とバイオームから、Phase 3で利用する資源候補フラグを決めます。
        /// ここでは候補だけを記録し、密度・群れ・最低間隔は後続の資源配置Systemで判定します。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <param name="biome">分類済みのバイオームです。</param>
        /// <returns>配置可能な資源種別の組み合わせです。</returns>
        private static ResourceSuitability ClassifyResourceSuitability(GroundType ground, BiomeId biome)
        {
            if (ground == GroundType.Water)
            {
                // 水は全水域タイルから直接採取できる地形属性なので、別の配置資源候補にはしません。
                return ResourceSuitability.None;
            }

            if (ground == GroundType.RockWall)
            {
                return ResourceSuitability.Stone;
            }

            if (ground == GroundType.Sand || biome == BiomeId.Desert)
            {
                return ResourceSuitability.Sand | ResourceSuitability.Stone;
            }

            if (ground == GroundType.Vegetation || biome == BiomeId.Forest)
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
        /// 岩壁は通行不可の0、水域は陸地より重い2、通常陸地は1とします。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <returns>0〜2の移動コストです。</returns>
        private static byte ClassifyMovementCost(GroundType ground)
        {
            if (ground == GroundType.RockWall)
            {
                return 0;
            }

            return ground == GroundType.Water ? (byte)2 : (byte)1;
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

                ulong classificationsHash = WorldSeedUtility.Mix64(currentHash ^ packedClassifications);
                return WorldSeedUtility.Mix64(classificationsHash ^ packedClimate);
            }
        }
    }
}
