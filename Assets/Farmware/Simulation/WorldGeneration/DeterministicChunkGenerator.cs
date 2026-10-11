// このファイルは、ワールド設定と整数タイル座標だけからバイオーム・地面・ルール用データを生成します。
// ECS Job、テスト、将来のセーブ再生成が同じ純粋関数を共有し、チャンク生成順に依存しない結果を保証します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 3種類の初期バイオームと水域・岩壁を決定論的に分類します。
    /// UnityEngine.Objectやグローバル乱数を参照しないため、Burst Jobから並列に呼び出せます。
    /// </summary>
    public static class DeterministicChunkGenerator
    {
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

            ushort temperature = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.TemperatureCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Temperature);

            ushort moisture = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.MoistureCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Moisture);

            BiomeId biome = ClassifyBiome(temperature, moisture, settings);
            GroundType ground = ClassifyGround(
                elevation,
                temperature,
                moisture,
                biome,
                settings,
                worldTileX,
                worldTileY);

            return new GeneratedTile
            {
                Biome = biome,
                Ground = ground,
                ElevationClass = ClassifyElevation(elevation, settings),
                GameplayFlags = ClassifyGameplayFlags(ground),
                ResourceSuitability = ClassifyResourceSuitability(ground, biome),
                MovementCost = ClassifyMovementCost(ground),
                Elevation = elevation,
                Temperature = temperature,
                Moisture = moisture
            };
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
        /// 気温と湿度から初期3バイオームを選びます。
        /// 砂漠条件を先に評価し、乾燥高温地域が湿度だけで草原へ戻らないようにします。
        /// </summary>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="settings">判定閾値を保持する生成設定です。</param>
        /// <returns>草原、森林、砂漠のいずれかです。</returns>
        private static BiomeId ClassifyBiome(
            ushort temperature,
            ushort moisture,
            in WorldGenerationSettings settings)
        {
            if (temperature >= settings.HotTemperatureThreshold
                && moisture < settings.DryMoistureThreshold)
            {
                return BiomeId.Desert;
            }

            if (moisture >= settings.WetMoistureThreshold)
            {
                return BiomeId.Forest;
            }

            return BiomeId.Grassland;
        }

        /// <summary>
        /// 標高を最優先し、その後に気候値と座標ハッシュから陸上の地面種別を決めます。
        /// 論理Biome IDは閾値で一意に保ち、境界付近の地面だけを段階的に混ぜて見た目とゲームルールをなじませます。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="temperature">0〜65535の気温です。</param>
        /// <param name="moisture">0〜65535の湿度です。</param>
        /// <param name="biome">気候から決めた陸上バイオームです。</param>
        /// <param name="settings">水面と岩壁の閾値を保持する生成設定です。</param>
        /// <param name="worldTileX">決定論的な移行タイル抽選に使う水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">決定論的な移行タイル抽選に使う垂直ワールドタイル座標です。</param>
        /// <returns>ゲームルール用の地面種別です。</returns>
        private static GroundType ClassifyGround(
            ushort elevation,
            ushort temperature,
            ushort moisture,
            BiomeId biome,
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (elevation < settings.WaterThreshold)
            {
                return GroundType.Water;
            }

            if (elevation >= settings.RockWallThreshold)
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
        /// <returns>草原は通常地面、森林は植生地面、砂漠は砂地です。</returns>
        private static GroundType ClassifyDiscreteBiomeGround(BiomeId biome)
        {
            if (biome == BiomeId.Desert)
            {
                return GroundType.Sand;
            }

            return biome == BiomeId.Forest ? GroundType.Vegetation : GroundType.Normal;
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
        /// 連続標高を水域・低地・平地・高地・岩壁の5段階へ分類します。
        /// </summary>
        /// <param name="elevation">0〜65535の標高です。</param>
        /// <param name="settings">水面と岩壁の閾値を保持する設定です。</param>
        /// <returns>表示や配置条件で使う標高クラスです。</returns>
        private static ElevationClass ClassifyElevation(
            ushort elevation,
            in WorldGenerationSettings settings)
        {
            if (elevation < settings.WaterThreshold)
            {
                return ElevationClass.Water;
            }

            if (elevation >= settings.RockWallThreshold)
            {
                return ElevationClass.RockWall;
            }

            int landRange = settings.RockWallThreshold - settings.WaterThreshold;
            int lowlandEnd = settings.WaterThreshold + (landRange / 3);
            int plainEnd = settings.WaterThreshold + ((landRange * 2) / 3);

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
                return ResourceSuitability.Water;
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

                ulong packedClimate = tile.Elevation;
                packedClimate |= (ulong)tile.Temperature << 16;
                packedClimate |= (ulong)tile.Moisture << 32;

                ulong classificationsHash = WorldSeedUtility.Mix64(currentHash ^ packedClassifications);
                return WorldSeedUtility.Mix64(classificationsHash ^ packedClimate);
            }
        }
    }
}
