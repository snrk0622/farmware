// このファイルは、地下水と自然土壌飽和だけが参照する局所地質補正を決定論的に生成します。
// 基礎地質DefinitionやGeneratedTileを書き換えず、後続のC4-B2が固定9地点平均と水文結果へ接続します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 亀裂、風化帯、粘土層、帯水層の生成寸法と補正式を固定する設定です。
    /// WorldGenerationSettingsへ値として含め、GeneratorVersionと一緒に保存対象とします。
    /// </summary>
    public struct GroundwaterGenerationSettings
    {
        /// <summary>亀裂・破砕帯ポテンシャルの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int FractureCellSizeInTiles;

        /// <summary>風化帯ポテンシャルの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int WeatheringCellSizeInTiles;

        /// <summary>粘土層ポテンシャルの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int ClayCellSizeInTiles;

        /// <summary>帯水層ポテンシャルの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int AquiferCellSizeInTiles;

        /// <summary>4ポテンシャルへそれぞれ重ねるノイズ解像度数です。</summary>
        public int LocalGeologyOctaveCount;

        /// <summary>ポテンシャルが補正へ寄与し始める境界です。この値以下は補正量0になります。</summary>
        public ushort SupportMidpoint;

        /// <summary>亀裂Supportを透水性へ加える前に割る正の整数です。</summary>
        public byte FracturePermeabilityDivisor;

        /// <summary>風化帯Supportを透水性へ加える前に割る正の整数です。</summary>
        public byte WeatheringPermeabilityDivisor;

        /// <summary>粘土層Supportを透水性から引く前に割る正の整数です。</summary>
        public byte ClayPermeabilityDivisor;

        /// <summary>帯水層Supportを透水性へ加える前に割る正の整数です。</summary>
        public byte AquiferPermeabilityDivisor;

        /// <summary>亀裂Supportを地下水保持力へ加える前に割る正の整数です。</summary>
        public byte FractureStorageDivisor;

        /// <summary>風化帯Supportを地下水保持力へ加える前に割る正の整数です。</summary>
        public byte WeatheringStorageDivisor;

        /// <summary>粘土層Supportを地下水保持力へ加える前に割る正の整数です。</summary>
        public byte ClayStorageDivisor;

        /// <summary>帯水層Supportを地下水保持力へ加える前に割る正の整数です。</summary>
        public byte AquiferStorageDivisor;

        /// <summary>
        /// TERRAIN-01C4-B1で確定した局所地質補正設定を作ります。
        /// </summary>
        /// <returns>4種類の独立した3オクターブノイズと確定補正式を持つ設定です。</returns>
        public static GroundwaterGenerationSettings CreateDefault()
        {
            return new GroundwaterGenerationSettings
            {
                FractureCellSizeInTiles = 128,
                WeatheringCellSizeInTiles = 96,
                ClayCellSizeInTiles = 192,
                AquiferCellSizeInTiles = 256,
                LocalGeologyOctaveCount = 3,
                SupportMidpoint = 32768,
                FracturePermeabilityDivisor = 2,
                WeatheringPermeabilityDivisor = 4,
                ClayPermeabilityDivisor = 2,
                AquiferPermeabilityDivisor = 8,
                FractureStorageDivisor = 8,
                WeatheringStorageDivisor = 4,
                ClayStorageDivisor = 4,
                AquiferStorageDivisor = 2
            };
        }
    }

    /// <summary>
    /// 一つの絶対タイル座標から生成した4種類の局所地質ポテンシャルです。
    /// すべて0〜65,535の固定値で、基礎地質DefinitionやBiomeへは書き戻しません。
    /// </summary>
    public struct LocalGeologyPotentials
    {
        /// <summary>亀裂・破砕帯が成立しやすい0〜65,535の固定ポテンシャルです。</summary>
        public ushort Fracture;

        /// <summary>風化帯が成立しやすい0〜65,535の固定ポテンシャルです。</summary>
        public ushort Weathering;

        /// <summary>粘土層が成立しやすい0〜65,535の固定ポテンシャルです。</summary>
        public ushort Clay;

        /// <summary>帯水層が成立しやすい0〜65,535の固定ポテンシャルです。</summary>
        public ushort Aquifer;
    }

    /// <summary>
    /// 局所地質ポテンシャルを基礎地質へ適用した地下水専用の導出値です。
    /// C4-B2の地下水・土壌飽和計算とC4-Eの診断だけが使用します。
    /// </summary>
    public struct AdjustedGroundwaterGeology
    {
        /// <summary>計算に使用した4種類の局所地質ポテンシャルです。</summary>
        public LocalGeologyPotentials Potentials;

        /// <summary>全補正を64bitで合成してから0〜65,535へ一度だけ飽和した透水性です。</summary>
        public ushort Permeability;

        /// <summary>全補正を64bitで合成してから0〜65,535へ一度だけ飽和した地下水保持力です。</summary>
        public ushort GroundwaterStorage;
    }

    /// <summary>
    /// Seed、GeneratorVersion、固定設定、絶対タイル座標だけから局所地質補正を生成する純粋計算です。
    /// Burst Jobから呼べるよう、管理参照、例外、UnityEngine API、生成済みチャンク参照を使用しません。
    /// </summary>
    public static class DeterministicGroundwaterGenerator
    {
        /// <summary>
        /// 設定が確定した4格子、3オクターブ、中央値、補正係数と一致するか検証します。
        /// </summary>
        /// <param name="settings">検証する地下水固定生成設定です。</param>
        /// <returns>暗黙補正せずC4-B1の純粋生成へ使用できる場合はtrueです。</returns>
        public static bool ValidateSettings(in GroundwaterGenerationSettings settings)
        {
            return settings.FractureCellSizeInTiles == 128
                && settings.WeatheringCellSizeInTiles == 96
                && settings.ClayCellSizeInTiles == 192
                && settings.AquiferCellSizeInTiles == 256
                && settings.LocalGeologyOctaveCount == 3
                && settings.SupportMidpoint == 32768
                && settings.FracturePermeabilityDivisor == 2
                && settings.WeatheringPermeabilityDivisor == 4
                && settings.ClayPermeabilityDivisor == 2
                && settings.AquiferPermeabilityDivisor == 8
                && settings.FractureStorageDivisor == 8
                && settings.WeatheringStorageDivisor == 4
                && settings.ClayStorageDivisor == 4
                && settings.AquiferStorageDivisor == 2;
        }

        /// <summary>
        /// 一つの絶対タイル座標で4種類の独立した局所地質ポテンシャルを生成します。
        /// </summary>
        /// <param name="settings">Seed、版、局所地質設定を持つ固定ワールド生成設定です。</param>
        /// <param name="worldTileX">サンプリングする水平絶対タイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直絶対タイル座標です。</param>
        /// <param name="potentials">成功時の4種類の0〜65,535ポテンシャルです。</param>
        /// <returns>設定が確定契約と一致し、全ポテンシャルを生成できた場合はtrueです。</returns>
        public static bool TrySampleLocalGeology(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out LocalGeologyPotentials potentials)
        {
            potentials = default;
            GroundwaterGenerationSettings groundwater = settings.Groundwater;
            if (!ValidateSettings(groundwater))
            {
                return false;
            }

            // 4用途は別Saltを使います。追加順や他の生成器の乱数消費から結果を分離するためです。
            potentials = new LocalGeologyPotentials
            {
                Fracture = SamplePotential(
                    settings,
                    worldTileX,
                    worldTileY,
                    groundwater.FractureCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterFracturePotential),
                Weathering = SamplePotential(
                    settings,
                    worldTileX,
                    worldTileY,
                    groundwater.WeatheringCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterWeatheringPotential),
                Clay = SamplePotential(
                    settings,
                    worldTileX,
                    worldTileY,
                    groundwater.ClayCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterClayPotential),
                Aquifer = SamplePotential(
                    settings,
                    worldTileX,
                    worldTileY,
                    groundwater.AquiferCellSizeInTiles,
                    WorldGenerationPurposeSalts.GroundwaterAquiferPotential)
            };
            return true;
        }

        /// <summary>
        /// 絶対座標の局所地質を生成し、基礎透水性と地下水保持力へ確定補正式を適用します。
        /// </summary>
        /// <param name="settings">Seed、版、局所地質設定を持つ固定ワールド生成設定です。</param>
        /// <param name="worldTileX">サンプリングする水平絶対タイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直絶対タイル座標です。</param>
        /// <param name="basePermeability">地質Definition由来の0〜65,535基礎透水性です。</param>
        /// <param name="baseGroundwaterStorage">地質Definition由来の0〜65,535基礎地下水保持力です。</param>
        /// <param name="result">成功時のポテンシャルと地下水専用補正後2値です。</param>
        /// <returns>設定と純粋生成が有効で、補正後値を完全に計算できた場合はtrueです。</returns>
        public static bool TryGenerateAdjustedGeology(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            ushort basePermeability,
            ushort baseGroundwaterStorage,
            out AdjustedGroundwaterGeology result)
        {
            result = default;
            if (!TrySampleLocalGeology(settings, worldTileX, worldTileY, out LocalGeologyPotentials potentials))
            {
                return false;
            }

            return TryApplyLocalGeologyAdjustment(
                settings.Groundwater,
                basePermeability,
                baseGroundwaterStorage,
                potentials,
                out result);
        }

        /// <summary>
        /// 呼出し側が用意した4ポテンシャルへ確定整数式を適用し、端点と飽和を求めます。
        /// </summary>
        /// <param name="settings">中央値と8個の補正除数を持つ固定設定です。</param>
        /// <param name="basePermeability">地質Definition由来の基礎透水性です。</param>
        /// <param name="baseGroundwaterStorage">地質Definition由来の基礎地下水保持力です。</param>
        /// <param name="potentials">適用する4種類の局所地質ポテンシャルです。</param>
        /// <param name="result">成功時の入力ポテンシャルと0〜65,535へ飽和した2値です。</param>
        /// <returns>設定が有効で補正式を適用できた場合はtrueです。</returns>
        public static bool TryApplyLocalGeologyAdjustment(
            in GroundwaterGenerationSettings settings,
            ushort basePermeability,
            ushort baseGroundwaterStorage,
            in LocalGeologyPotentials potentials,
            out AdjustedGroundwaterGeology result)
        {
            result = default;
            if (!ValidateSettings(settings))
            {
                return false;
            }

            long fractureSupport = GetSupport(potentials.Fracture, settings.SupportMidpoint);
            long weatheringSupport = GetSupport(potentials.Weathering, settings.SupportMidpoint);
            long claySupport = GetSupport(potentials.Clay, settings.SupportMidpoint);
            long aquiferSupport = GetSupport(potentials.Aquifer, settings.SupportMidpoint);

            // 中間値をushortへ戻すと減算前に周回するため、全項を符号付き64bitで合成して最後だけ飽和します。
            long adjustedPermeability = basePermeability
                + (fractureSupport / settings.FracturePermeabilityDivisor)
                + (weatheringSupport / settings.WeatheringPermeabilityDivisor)
                - (claySupport / settings.ClayPermeabilityDivisor)
                + (aquiferSupport / settings.AquiferPermeabilityDivisor);
            long adjustedStorage = baseGroundwaterStorage
                + (fractureSupport / settings.FractureStorageDivisor)
                + (weatheringSupport / settings.WeatheringStorageDivisor)
                + (claySupport / settings.ClayStorageDivisor)
                + (aquiferSupport / settings.AquiferStorageDivisor);

            result = new AdjustedGroundwaterGeology
            {
                Potentials = potentials,
                Permeability = ClampToUShort(adjustedPermeability),
                GroundwaterStorage = ClampToUShort(adjustedStorage)
            };
            return true;
        }

        /// <summary>一用途の3オクターブ連続ノイズを、他用途と共有しないSaltで取得します。</summary>
        /// <param name="settings">Seed、版、オクターブ数を持つ固定ワールド生成設定です。</param>
        /// <param name="worldTileX">サンプリングする水平絶対タイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直絶対タイル座標です。</param>
        /// <param name="cellSizeInTiles">この用途で最も低周波な格子の1辺です。単位はタイルです。</param>
        /// <param name="purposeSalt">他用途の乱数列と分離する固定64bit Saltです。</param>
        /// <returns>重み付き平均後の0〜65,535ポテンシャルです。</returns>
        private static ushort SamplePotential(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            int cellSizeInTiles,
            ulong purposeSalt)
        {
            return DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                cellSizeInTiles,
                settings.Groundwater.LocalGeologyOctaveCount,
                purposeSalt);
        }

        /// <summary>中央値を超えた部分だけを非負の補正Supportへ変換します。</summary>
        /// <param name="potential">0〜65,535の局所地質ポテンシャルです。</param>
        /// <param name="midpoint">補正が始まる境界で、境界値自体のSupportは0です。</param>
        /// <returns>0以上32,767以下の補正Supportです。</returns>
        private static long GetSupport(ushort potential, ushort midpoint)
        {
            return potential > midpoint ? (long)potential - midpoint : 0L;
        }

        /// <summary>符号付き64bitの補正結果を0〜65,535へ一度だけ飽和させます。</summary>
        /// <param name="value">全ての加減算を終えた補正結果です。</param>
        /// <returns>0〜65,535に収めた固定小数値です。</returns>
        private static ushort ClampToUShort(long value)
        {
            if (value <= 0L)
            {
                return 0;
            }

            return value >= ushort.MaxValue ? ushort.MaxValue : (ushort)value;
        }
    }
}
