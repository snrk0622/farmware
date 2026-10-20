// このファイルは、Biome分類より前に必要な連続環境軸を、Seed・生成設定・絶対座標だけから求めます。
// DeterministicChunkGenerator、開発者用可視化、EditModeテストが同じ純粋計算を共有し、ロード順や表示状態による差を防ぎます。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 地形気温を、デバッグ表示と設定確認に使う5段階へ分類した値です。
    /// 連続値を置き換える保存データではなく、表示と監査でのみ使用します。
    /// </summary>
    public enum TerrainTemperatureBand : byte
    {
        /// <summary>地形気温0〜11,999の極寒域です。</summary>
        ExtremeCold = 1,

        /// <summary>地形気温12,000〜22,999の寒冷域です。</summary>
        Cold = 2,

        /// <summary>地形気温23,000〜32,767の冷涼域です。</summary>
        Cool = 3,

        /// <summary>地形気温32,768〜39,999の温暖域です。</summary>
        Temperate = 4,

        /// <summary>地形気温40,000〜65,535の高温域です。</summary>
        Hot = 5
    }

    /// <summary>
    /// 基礎湿度を、デバッグ表示と設定確認に使う5段階へ分類した値です。
    /// 現在の土壌水分や水面の有無は表さず、固定した地域気候だけを分類します。
    /// </summary>
    public enum BaseMoistureBand : byte
    {
        /// <summary>基礎湿度0〜15,999の極乾燥域です。</summary>
        ExtremelyDry = 1,

        /// <summary>基礎湿度16,000〜28,499の乾燥域です。</summary>
        Dry = 2,

        /// <summary>基礎湿度28,500〜39,499の中湿域です。</summary>
        Moderate = 3,

        /// <summary>基礎湿度39,500〜49,999の湿潤域です。</summary>
        Wet = 4,

        /// <summary>基礎湿度50,000〜65,535の過湿域です。</summary>
        ExtremelyWet = 5
    }

    /// <summary>
    /// 木本植生、起伏、海洋影響、火山活動度で共用する表示・監査用の5段階です。
    /// 各軸の意味は異なるため、連続値や生成Saltは共有せず、段階境界だけを共有します。
    /// </summary>
    public enum ContinuousEnvironmentBand : byte
    {
        /// <summary>連続値0〜13,106の第1段階です。</summary>
        Level1 = 1,

        /// <summary>連続値13,107〜26,213の第2段階です。</summary>
        Level2 = 2,

        /// <summary>連続値26,214〜39,320の第3段階です。</summary>
        Level3 = 3,

        /// <summary>連続値39,321〜52,427の第4段階です。</summary>
        Level4 = 4,

        /// <summary>連続値52,428〜65,535の第5段階です。</summary>
        Level5 = 5
    }

    /// <summary>
    /// TERRAIN-01C1の連続環境軸を再計算するため、ワールド寿命中に固定する設定です。
    /// WorldGenerationSettingsへ値として含め、将来のセーブではGeneratorVersionと一緒に保存します。
    /// </summary>
    public struct ContinuousEnvironmentSettings
    {
        /// <summary>最高標高で基礎気温から引く最大の寒冷補正量です。</summary>
        public ushort MaximumElevationCooling;

        /// <summary>極寒と寒冷を分ける地形気温の下限です。</summary>
        public ushort ColdTemperatureThreshold;

        /// <summary>冷涼と温暖を分ける地形気温の下限です。</summary>
        public ushort TemperateTemperatureThreshold;

        /// <summary>極乾燥と乾燥を分ける基礎湿度の下限です。</summary>
        public ushort DryMoistureThreshold;

        /// <summary>平地・台地と山地を分ける標高の下限です。</summary>
        public ushort MountainElevationThreshold;

        /// <summary>山地と高山を分ける標高の下限です。</summary>
        public ushort AlpineElevationThreshold;

        /// <summary>木本植生ポテンシャルの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int WoodyVegetationCellSizeInTiles;

        /// <summary>木本植生ポテンシャルへ重ねるノイズ解像度数です。</summary>
        public int WoodyVegetationOctaveCount;

        /// <summary>地形起伏を調べる上下左右の標高サンプル距離です。単位はタイルです。</summary>
        public int RuggednessSampleRadiusInTiles;

        /// <summary>地形起伏を調べる斜め標高サンプルのX・Yオフセットです。単位はタイルです。</summary>
        public int RuggednessDiagonalOffsetInTiles;

        /// <summary>地形起伏が最大値65,535へ到達する局所標高差です。</summary>
        public ushort RuggednessMaximumRelief;

        /// <summary>海洋影響で最も近い第1サンプル距離です。単位はタイルです。</summary>
        public int CoastalNearDistanceInTiles;

        /// <summary>海洋影響の第2サンプル距離です。単位はタイルです。</summary>
        public int CoastalMidDistanceInTiles;

        /// <summary>海洋影響の第3サンプル距離です。単位はタイルです。</summary>
        public int CoastalFarDistanceInTiles;

        /// <summary>海洋影響で最も遠い第4サンプル距離です。単位はタイルです。</summary>
        public int CoastalMaximumDistanceInTiles;

        /// <summary>第1距離で斜め方向へ加えるX・Yオフセットです。単位はタイルです。</summary>
        public int CoastalNearDiagonalOffsetInTiles;

        /// <summary>第2距離で斜め方向へ加えるX・Yオフセットです。単位はタイルです。</summary>
        public int CoastalMidDiagonalOffsetInTiles;

        /// <summary>第3距離で斜め方向へ加えるX・Yオフセットです。単位はタイルです。</summary>
        public int CoastalFarDiagonalOffsetInTiles;

        /// <summary>第4距離で斜め方向へ加えるX・Yオフセットです。単位はタイルです。</summary>
        public int CoastalMaximumDiagonalOffsetInTiles;

        /// <summary>第1距離に海がある場合の近接度です。</summary>
        public ushort CoastalNearProximity;

        /// <summary>第2距離が最短の場合の近接度です。</summary>
        public ushort CoastalMidProximity;

        /// <summary>第3距離が最短の場合の近接度です。</summary>
        public ushort CoastalFarProximity;

        /// <summary>第4距離が最短の場合の近接度です。</summary>
        public ushort CoastalMaximumProximity;

        /// <summary>近接度1に対して海方向露出へ掛ける整数比重です。</summary>
        public int CoastalDirectionalExposureWeight;

        /// <summary>火山活動度の最も低周波な格子サイズです。単位はタイルです。</summary>
        public int VolcanicActivityCellSizeInTiles;

        /// <summary>火山活動度へ重ねるノイズ解像度数です。</summary>
        public int VolcanicActivityOctaveCount;

        /// <summary>
        /// TERRAIN-01Bで合意した連続環境軸の既定設定を作ります。
        /// </summary>
        /// <returns>仕様v0.52と一致する固定設定です。</returns>
        public static ContinuousEnvironmentSettings CreateDefault()
        {
            return new ContinuousEnvironmentSettings
            {
                MaximumElevationCooling = 32768,
                ColdTemperatureThreshold = 12000,
                TemperateTemperatureThreshold = 32768,
                DryMoistureThreshold = 16000,
                MountainElevationThreshold = 43000,
                AlpineElevationThreshold = 54000,
                WoodyVegetationCellSizeInTiles = 192,
                WoodyVegetationOctaveCount = 3,
                RuggednessSampleRadiusInTiles = 16,
                RuggednessDiagonalOffsetInTiles = 11,
                RuggednessMaximumRelief = 16384,
                CoastalNearDistanceInTiles = 16,
                CoastalMidDistanceInTiles = 32,
                CoastalFarDistanceInTiles = 64,
                CoastalMaximumDistanceInTiles = 128,
                CoastalNearDiagonalOffsetInTiles = 11,
                CoastalMidDiagonalOffsetInTiles = 23,
                CoastalFarDiagonalOffsetInTiles = 45,
                CoastalMaximumDiagonalOffsetInTiles = 91,
                CoastalNearProximity = 65535,
                CoastalMidProximity = 49151,
                CoastalFarProximity = 32767,
                CoastalMaximumProximity = 16383,
                CoastalDirectionalExposureWeight = 3,
                VolcanicActivityCellSizeInTiles = 256,
                VolcanicActivityOctaveCount = 3
            };
        }
    }

    /// <summary>
    /// 1タイルについて生成した、Biome分類前の固定環境値です。
    /// 実際の植物、現在の天候、土壌水分、ロード済みチャンクは含みません。
    /// </summary>
    public struct GeneratedEnvironmentData
    {
        /// <summary>地面や水面へ変換する前の0〜65,535の連続標高です。</summary>
        public ushort Elevation;

        /// <summary>標高補正前の0〜65,535の基礎気温指数です。</summary>
        public ushort BaseTemperature;

        /// <summary>海面より上の標高寒冷補正を一度だけ適用した地形気温指数です。</summary>
        public ushort TerrainTemperature;

        /// <summary>現在の土壌水分や水面と分離した0〜65,535の基礎湿度指数です。</summary>
        public ushort BaseMoisture;

        /// <summary>配置済み樹木数とは独立した0〜65,535の木本植生ポテンシャルです。</summary>
        public ushort WoodyVegetationPotential;

        /// <summary>固定9標高サンプルの局所差から求めた0〜65,535の地形起伏です。</summary>
        public ushort TerrainRuggedness;

        /// <summary>固定32標高サンプルの海への近さと開放方向数から求めた0〜65,535の海洋影響です。</summary>
        public ushort CoastalInfluence;

        /// <summary>地質とは独立した0〜65,535の火山活動度です。</summary>
        public ushort VolcanicActivityPotential;

        /// <summary>気候と低周波環境ノイズを読む、Biome Warp適用後の水平座標です。</summary>
        public int WarpedSampleX;

        /// <summary>気候と低周波環境ノイズを読む、Biome Warp適用後の垂直座標です。</summary>
        public int WarpedSampleY;
    }

    /// <summary>
    /// 連続環境軸を整数演算だけで生成・分類する純粋計算です。
    /// Burst Jobから呼べるよう、例外、管理参照、UnityEngine APIを使用しません。
    /// </summary>
    public static class DeterministicEnvironmentGenerator
    {
        /// <summary>共通5段階の第2段階が始まる値です。</summary>
        public const ushort EnvironmentBand2Threshold = 13107;

        /// <summary>共通5段階の第3段階が始まる値です。</summary>
        public const ushort EnvironmentBand3Threshold = 26214;

        /// <summary>共通5段階の第4段階が始まる値です。</summary>
        public const ushort EnvironmentBand4Threshold = 39321;

        /// <summary>共通5段階の第5段階が始まる値です。</summary>
        public const ushort EnvironmentBand5Threshold = 52428;

        /// <summary>
        /// 1タイルの連続環境軸を生成します。
        /// 固定半径の標高参照がint座標域を越える場合は周回させずfalseを返します。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">水平の絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直の絶対タイル座標です。</param>
        /// <param name="environment">成功時に生成した全連続環境軸を返します。</param>
        /// <returns>設定と全サンプル座標が有効ならtrue、それ以外はfalseです。</returns>
        public static bool TryGenerate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out GeneratedEnvironmentData environment)
        {
            environment = default;
            int maximumSampleOffset = settings.EnvironmentAxes.CoastalMaximumDistanceInTiles;
            if (!CanOffsetCoordinate(worldTileX, maximumSampleOffset)
                || !CanOffsetCoordinate(worldTileY, maximumSampleOffset)
                || !TryGenerateBaseClimate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out environment))
            {
                return false;
            }

            ushort woodyVegetation = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                environment.WarpedSampleX,
                environment.WarpedSampleY,
                settings.EnvironmentAxes.WoodyVegetationCellSizeInTiles,
                settings.EnvironmentAxes.WoodyVegetationOctaveCount,
                WorldGenerationPurposeSalts.WoodyVegetationPotential);
            ushort volcanicActivity = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                environment.WarpedSampleX,
                environment.WarpedSampleY,
                settings.EnvironmentAxes.VolcanicActivityCellSizeInTiles,
                settings.EnvironmentAxes.VolcanicActivityOctaveCount,
                WorldGenerationPurposeSalts.VolcanicActivityPotential);

            if (!TryCalculateTerrainRuggedness(settings, worldTileX, worldTileY, out ushort ruggedness)
                || !TryCalculateCoastalInfluence(
                    settings,
                    worldTileX,
                    worldTileY,
                    environment.Elevation,
                    out ushort coastalInfluence))
            {
                return false;
            }

            environment.WoodyVegetationPotential = woodyVegetation;
            environment.TerrainRuggedness = ruggedness;
            environment.CoastalInfluence = coastalInfluence;
            environment.VolcanicActivityPotential = volcanicActivity;
            return true;
        }

        /// <summary>
        /// 標高、基礎気温、地形気温、基礎湿度とBiome Warp後座標だけを生成します。
        /// 岩壁外周判定のように起伏・海洋影響が不要な経路で、41点の追加標高サンプルを避けるための入口です。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">水平の絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直の絶対タイル座標です。</param>
        /// <param name="environment">成功時に基礎4値とWarp後座標を返し、他の連続軸は0のままです。</param>
        /// <returns>設定とWarp後座標が有効ならtrue、それ以外はfalseです。</returns>
        public static bool TryGenerateBaseClimate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out GeneratedEnvironmentData environment)
        {
            environment = default;
            if (!ValidateSettings(settings)
                || !TryCalculateWarpedCoordinate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out int warpedSampleX,
                    out int warpedSampleY))
            {
                return false;
            }

            ushort elevation = SampleElevation(settings, worldTileX, worldTileY);
            ushort baseTemperature = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                warpedSampleX,
                warpedSampleY,
                settings.TemperatureCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Temperature);
            environment = new GeneratedEnvironmentData
            {
                Elevation = elevation,
                BaseTemperature = baseTemperature,
                TerrainTemperature = CalculateTerrainTemperature(
                    baseTemperature,
                    elevation,
                    settings.SeaLevelThreshold,
                    settings.EnvironmentAxes.MaximumElevationCooling),
                BaseMoisture = DeterministicNoise.SampleFractalNoise(
                    settings.WorldSeed,
                    settings.GeneratorVersion,
                    warpedSampleX,
                    warpedSampleY,
                    settings.MoistureCellSizeInTiles,
                    settings.OctaveCount,
                    WorldGenerationPurposeSalts.Moisture),
                WarpedSampleX = warpedSampleX,
                WarpedSampleY = warpedSampleY
            };
            return true;
        }

        /// <summary>
        /// 連続環境軸の設定が、除算不能や逆転した境界を含まないことを検証します。
        /// </summary>
        /// <param name="settings">検証するワールド生成設定です。</param>
        /// <returns>すべての値が確定仕様の順序と正の寸法を満たす場合はtrueです。</returns>
        public static bool ValidateSettings(in WorldGenerationSettings settings)
        {
            ContinuousEnvironmentSettings axes = settings.EnvironmentAxes;
            return settings.SeaLevelThreshold < ushort.MaxValue
                && settings.ElevationCellSizeInTiles > 0
                && settings.TemperatureCellSizeInTiles > 0
                && settings.MoistureCellSizeInTiles > 0
                && settings.BiomeWarpCellSizeInTiles > 0
                && settings.OctaveCount > 0
                && settings.BiomeWarpAmplitudeInTiles >= 0
                && axes.MaximumElevationCooling > 0
                && axes.ColdTemperatureThreshold < settings.ColdTemperatureThreshold
                && settings.ColdTemperatureThreshold < axes.TemperateTemperatureThreshold
                && axes.TemperateTemperatureThreshold < settings.HotTemperatureThreshold
                && axes.DryMoistureThreshold < settings.DryMoistureThreshold
                && settings.DryMoistureThreshold < settings.WetMoistureThreshold
                && settings.WetMoistureThreshold < settings.WetlandMoistureThreshold
                && settings.SeaLevelThreshold <= settings.WetlandMaximumElevation
                && settings.WetlandMaximumElevation < axes.MountainElevationThreshold
                && settings.HighlandElevationThreshold == axes.MountainElevationThreshold
                && axes.MountainElevationThreshold < axes.AlpineElevationThreshold
                && axes.WoodyVegetationCellSizeInTiles > 0
                && axes.WoodyVegetationOctaveCount > 0
                && axes.RuggednessSampleRadiusInTiles > 0
                && axes.RuggednessDiagonalOffsetInTiles > 0
                && axes.RuggednessMaximumRelief > 0
                && axes.CoastalNearDistanceInTiles > 0
                && axes.CoastalNearDistanceInTiles < axes.CoastalMidDistanceInTiles
                && axes.CoastalMidDistanceInTiles < axes.CoastalFarDistanceInTiles
                && axes.CoastalFarDistanceInTiles < axes.CoastalMaximumDistanceInTiles
                && axes.CoastalNearDiagonalOffsetInTiles > 0
                && axes.CoastalMidDiagonalOffsetInTiles > axes.CoastalNearDiagonalOffsetInTiles
                && axes.CoastalFarDiagonalOffsetInTiles > axes.CoastalMidDiagonalOffsetInTiles
                && axes.CoastalMaximumDiagonalOffsetInTiles > axes.CoastalFarDiagonalOffsetInTiles
                && axes.CoastalNearProximity > axes.CoastalMidProximity
                && axes.CoastalMidProximity > axes.CoastalFarProximity
                && axes.CoastalFarProximity > axes.CoastalMaximumProximity
                && axes.CoastalDirectionalExposureWeight > 0
                && axes.VolcanicActivityCellSizeInTiles > 0
                && axes.VolcanicActivityOctaveCount > 0;
        }

        /// <summary>
        /// 海面より高い標高だけへ寒冷補正を一度適用し、0未満を0へ飽和させます。
        /// </summary>
        /// <param name="baseTemperature">標高補正前の基礎気温です。</param>
        /// <param name="elevation">連続標高です。</param>
        /// <param name="seaLevelThreshold">これ未満を海面下とする標高です。</param>
        /// <param name="maximumElevationCooling">最高標高で差し引く最大値です。</param>
        /// <returns>0〜65,535の地形気温です。</returns>
        public static ushort CalculateTerrainTemperature(
            ushort baseTemperature,
            ushort elevation,
            ushort seaLevelThreshold,
            ushort maximumElevationCooling)
        {
            if (elevation <= seaLevelThreshold || seaLevelThreshold == ushort.MaxValue)
            {
                return baseTemperature;
            }

            long elevationAboveSea = elevation - seaLevelThreshold;
            long elevationRange = ushort.MaxValue - seaLevelThreshold;
            long cooling = (elevationAboveSea * maximumElevationCooling) / elevationRange;
            long terrainTemperature = (long)baseTemperature - cooling;
            return terrainTemperature > 0 ? (ushort)terrainTemperature : (ushort)0;
        }

        /// <summary>
        /// 最大・最小標高差を、固定した飽和差まで0〜65,535へ線形変換します。
        /// </summary>
        /// <param name="minimumElevation">9点中の最小標高です。</param>
        /// <param name="maximumElevation">9点中の最大標高です。</param>
        /// <param name="maximumRelief">出力が65,535へ到達する標高差です。</param>
        /// <returns>0〜65,535の地形起伏です。maximumReliefが0なら0を返します。</returns>
        public static ushort CalculateTerrainRuggedness(
            ushort minimumElevation,
            ushort maximumElevation,
            ushort maximumRelief)
        {
            if (maximumRelief == 0 || maximumElevation <= minimumElevation)
            {
                return 0;
            }

            long localRelief = maximumElevation - minimumElevation;
            long clampedRelief = localRelief < maximumRelief ? localRelief : maximumRelief;
            return (ushort)((clampedRelief * ushort.MaxValue) / maximumRelief);
        }

        /// <summary>
        /// 最短海距離の近接値と8方向中の海方向数を、1対指定比重で合成します。
        /// </summary>
        /// <param name="nearestSeaProximity">最も近い海サンプルに対応する0〜65,535の値です。</param>
        /// <param name="seaDirectionCount">海サンプルを1件以上持つ方向数です。0〜8です。</param>
        /// <param name="directionalExposureWeight">近接度1に対する方向露出の整数比重です。</param>
        /// <returns>0〜65,535の海洋影響です。</returns>
        public static ushort CalculateCoastalInfluence(
            ushort nearestSeaProximity,
            byte seaDirectionCount,
            int directionalExposureWeight)
        {
            int safeDirectionCount = seaDirectionCount <= 8 ? seaDirectionCount : 8;
            int safeWeight = directionalExposureWeight > 0 ? directionalExposureWeight : 1;
            long directionalExposure = ((long)safeDirectionCount * ushort.MaxValue) / 8;
            long weightedTotal = nearestSeaProximity + (safeWeight * directionalExposure);
            return (ushort)(weightedTotal / (safeWeight + 1));
        }

        /// <summary>
        /// 地形気温を仕様の5段階へ分類します。
        /// </summary>
        /// <param name="value">0〜65,535の地形気温です。</param>
        /// <param name="settings">4境界を保持する生成設定です。</param>
        /// <returns>極寒から高温までのいずれか1段階です。</returns>
        public static TerrainTemperatureBand ClassifyTerrainTemperature(
            ushort value,
            in WorldGenerationSettings settings)
        {
            if (value < settings.EnvironmentAxes.ColdTemperatureThreshold)
            {
                return TerrainTemperatureBand.ExtremeCold;
            }

            if (value < settings.ColdTemperatureThreshold)
            {
                return TerrainTemperatureBand.Cold;
            }

            if (value < settings.EnvironmentAxes.TemperateTemperatureThreshold)
            {
                return TerrainTemperatureBand.Cool;
            }

            return value < settings.HotTemperatureThreshold
                ? TerrainTemperatureBand.Temperate
                : TerrainTemperatureBand.Hot;
        }

        /// <summary>
        /// 基礎湿度を仕様の5段階へ分類します。
        /// </summary>
        /// <param name="value">0〜65,535の基礎湿度です。</param>
        /// <param name="settings">4境界を保持する生成設定です。</param>
        /// <returns>極乾燥から過湿までのいずれか1段階です。</returns>
        public static BaseMoistureBand ClassifyBaseMoisture(
            ushort value,
            in WorldGenerationSettings settings)
        {
            if (value < settings.EnvironmentAxes.DryMoistureThreshold)
            {
                return BaseMoistureBand.ExtremelyDry;
            }

            if (value < settings.DryMoistureThreshold)
            {
                return BaseMoistureBand.Dry;
            }

            if (value < settings.WetMoistureThreshold)
            {
                return BaseMoistureBand.Moderate;
            }

            return value < settings.WetlandMoistureThreshold
                ? BaseMoistureBand.Wet
                : BaseMoistureBand.ExtremelyWet;
        }

        /// <summary>
        /// 木本植生、起伏、海洋影響、火山活動度の連続値を共通5段階へ分類します。
        /// </summary>
        /// <param name="value">0〜65,535の連続軸値です。</param>
        /// <returns>第1から第5までのいずれか1段階です。</returns>
        public static ContinuousEnvironmentBand ClassifyContinuousEnvironment(ushort value)
        {
            if (value < EnvironmentBand2Threshold)
            {
                return ContinuousEnvironmentBand.Level1;
            }

            if (value < EnvironmentBand3Threshold)
            {
                return ContinuousEnvironmentBand.Level2;
            }

            if (value < EnvironmentBand4Threshold)
            {
                return ContinuousEnvironmentBand.Level3;
            }

            return value < EnvironmentBand5Threshold
                ? ContinuousEnvironmentBand.Level4
                : ContinuousEnvironmentBand.Level5;
        }

        /// <summary>
        /// 地面や水域へ変換する前の連続標高を、固定した5区分へ分類します。
        /// </summary>
        /// <param name="elevation">0〜65,535の連続標高です。</param>
        /// <param name="settings">海面、低地上限、山地・高山境界を保持する設定です。</param>
        /// <returns>水域や岩壁で上書きされない標高区分です。</returns>
        public static ElevationClass ClassifyElevation(
            ushort elevation,
            in WorldGenerationSettings settings)
        {
            if (elevation < settings.SeaLevelThreshold)
            {
                return ElevationClass.BelowSeaLevel;
            }

            if (elevation <= settings.WetlandMaximumElevation)
            {
                return ElevationClass.Lowland;
            }

            if (elevation < settings.EnvironmentAxes.MountainElevationThreshold)
            {
                return ElevationClass.PlainOrPlateau;
            }

            return elevation < settings.EnvironmentAxes.AlpineElevationThreshold
                ? ElevationClass.Mountain
                : ElevationClass.Alpine;
        }

        /// <summary>
        /// Biome Warpを整数タイル座標へ適用し、int範囲を越える場合は明示的に失敗します。
        /// </summary>
        private static bool TryCalculateWarpedCoordinate(
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

            long horizontalOffset = ((long)(horizontalWarp - 32768) * settings.BiomeWarpAmplitudeInTiles) / 32768;
            long verticalOffset = ((long)(verticalWarp - 32768) * settings.BiomeWarpAmplitudeInTiles) / 32768;
            long warpedX = (long)worldTileX + horizontalOffset;
            long warpedY = (long)worldTileY + verticalOffset;
            if (warpedX < int.MinValue || warpedX > int.MaxValue
                || warpedY < int.MinValue || warpedY > int.MaxValue)
            {
                sampleX = 0;
                sampleY = 0;
                return false;
            }

            sampleX = (int)warpedX;
            sampleY = (int)warpedY;
            return true;
        }

        /// <summary>固定9地点の標高最小・最大から地形起伏を求めます。</summary>
        private static bool TryCalculateTerrainRuggedness(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out ushort ruggedness)
        {
            int radius = settings.EnvironmentAxes.RuggednessSampleRadiusInTiles;
            int diagonal = settings.EnvironmentAxes.RuggednessDiagonalOffsetInTiles;
            ushort minimum = ushort.MaxValue;
            ushort maximum = 0;

            // 中心、上下左右、斜めの順序は結果へ影響しませんが、仕様図と同じ9地点を漏れなく列挙します。
            if (!TryAccumulateElevation(settings, worldTileX, worldTileY, 0, 0, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, 0, radius, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, radius, 0, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, 0, -radius, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, -radius, 0, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, diagonal, diagonal, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, diagonal, -diagonal, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, -diagonal, -diagonal, ref minimum, ref maximum)
                || !TryAccumulateElevation(settings, worldTileX, worldTileY, -diagonal, diagonal, ref minimum, ref maximum))
            {
                ruggedness = 0;
                return false;
            }

            ruggedness = CalculateTerrainRuggedness(
                minimum,
                maximum,
                settings.EnvironmentAxes.RuggednessMaximumRelief);
            return true;
        }

        /// <summary>8方向×4距離の標高を調べ、最短海距離と海方向数から海洋影響を求めます。</summary>
        private static bool TryCalculateCoastalInfluence(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            ushort centerElevation,
            out ushort coastalInfluence)
        {
            if (centerElevation < settings.SeaLevelThreshold)
            {
                coastalInfluence = ushort.MaxValue;
                return true;
            }

            ushort nearestSeaProximity = 0;
            byte seaDirectionCount = 0;
            for (int direction = 0; direction < 8; direction++)
            {
                bool directionContainsSea = false;
                for (int distanceIndex = 0; distanceIndex < 4; distanceIndex++)
                {
                    GetCoastalOffset(
                        settings.EnvironmentAxes,
                        direction,
                        distanceIndex,
                        out int offsetX,
                        out int offsetY,
                        out ushort proximity);
                    if (!TryAddOffset(worldTileX, offsetX, out int sampleX)
                        || !TryAddOffset(worldTileY, offsetY, out int sampleY))
                    {
                        coastalInfluence = 0;
                        return false;
                    }

                    if (SampleElevation(settings, sampleX, sampleY) < settings.SeaLevelThreshold)
                    {
                        directionContainsSea = true;
                        if (proximity > nearestSeaProximity)
                        {
                            nearestSeaProximity = proximity;
                        }
                    }
                }

                if (directionContainsSea)
                {
                    seaDirectionCount++;
                }
            }

            coastalInfluence = CalculateCoastalInfluence(
                nearestSeaProximity,
                seaDirectionCount,
                settings.EnvironmentAxes.CoastalDirectionalExposureWeight);
            return true;
        }

        /// <summary>1地点の標高を最小・最大へ反映し、座標がint範囲外ならfalseを返します。</summary>
        private static bool TryAccumulateElevation(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            int offsetX,
            int offsetY,
            ref ushort minimum,
            ref ushort maximum)
        {
            if (!TryAddOffset(worldTileX, offsetX, out int sampleX)
                || !TryAddOffset(worldTileY, offsetY, out int sampleY))
            {
                return false;
            }

            ushort sample = SampleElevation(settings, sampleX, sampleY);
            if (sample < minimum)
            {
                minimum = sample;
            }

            if (sample > maximum)
            {
                maximum = sample;
            }

            return true;
        }

        /// <summary>海洋影響の方向番号と距離番号を、軸別の固定オフセットへ変換します。</summary>
        private static void GetCoastalOffset(
            in ContinuousEnvironmentSettings settings,
            int direction,
            int distanceIndex,
            out int offsetX,
            out int offsetY,
            out ushort proximity)
        {
            int straightDistance;
            int diagonalDistance;
            switch (distanceIndex)
            {
                case 0:
                    straightDistance = settings.CoastalNearDistanceInTiles;
                    diagonalDistance = settings.CoastalNearDiagonalOffsetInTiles;
                    proximity = settings.CoastalNearProximity;
                    break;
                case 1:
                    straightDistance = settings.CoastalMidDistanceInTiles;
                    diagonalDistance = settings.CoastalMidDiagonalOffsetInTiles;
                    proximity = settings.CoastalMidProximity;
                    break;
                case 2:
                    straightDistance = settings.CoastalFarDistanceInTiles;
                    diagonalDistance = settings.CoastalFarDiagonalOffsetInTiles;
                    proximity = settings.CoastalFarProximity;
                    break;
                default:
                    straightDistance = settings.CoastalMaximumDistanceInTiles;
                    diagonalDistance = settings.CoastalMaximumDiagonalOffsetInTiles;
                    proximity = settings.CoastalMaximumProximity;
                    break;
            }

            // 東、北東、北、西北、西、南西、南、南東の固定順です。方向数だけを数えるため列挙順は結果を変えません。
            switch (direction)
            {
                case 0: offsetX = straightDistance; offsetY = 0; return;
                case 1: offsetX = diagonalDistance; offsetY = diagonalDistance; return;
                case 2: offsetX = 0; offsetY = straightDistance; return;
                case 3: offsetX = -diagonalDistance; offsetY = diagonalDistance; return;
                case 4: offsetX = -straightDistance; offsetY = 0; return;
                case 5: offsetX = -diagonalDistance; offsetY = -diagonalDistance; return;
                case 6: offsetX = 0; offsetY = -straightDistance; return;
                default: offsetX = diagonalDistance; offsetY = -diagonalDistance; return;
            }
        }

        /// <summary>
        /// 同じ標高純粋関数を、中心、起伏、海洋影響、地質サイトの全サンプルで共有します。
        /// </summary>
        /// <param name="settings">標高格子、Octave数、Seed、GeneratorVersionを持つ生成設定です。</param>
        /// <param name="worldTileX">標高を読む水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">標高を読む垂直ワールドタイル座標です。</param>
        /// <returns>0〜65,535へ正規化した連続標高です。</returns>
        public static ushort SampleElevation(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            return DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.ElevationCellSizeInTiles,
                settings.OctaveCount,
                WorldGenerationPurposeSalts.Elevation);
        }

        /// <summary>正負どちらのオフセットもlongで加算し、int範囲内の場合だけ変換します。</summary>
        private static bool TryAddOffset(int coordinate, int offset, out int result)
        {
            long value = (long)coordinate + offset;
            if (value < int.MinValue || value > int.MaxValue)
            {
                result = 0;
                return false;
            }

            result = (int)value;
            return true;
        }

        /// <summary>正負両側へ最大オフセットを足せる座標かを、処理前にまとめて確認します。</summary>
        private static bool CanOffsetCoordinate(int coordinate, int maximumOffset)
        {
            long minimum = (long)coordinate - maximumOffset;
            long maximum = (long)coordinate + maximumOffset;
            return minimum >= int.MinValue && maximum <= int.MaxValue;
        }
    }
}
