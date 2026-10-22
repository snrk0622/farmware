// このファイルは、TERRAIN-01C5-Aで確定した新18Biomeの安定ID、適性範囲、軸重み、地質適合度を定義します。
// 旧7Biomeの実行経路とは接続せず、C5後半の分類器が同じ固定Definitionを読むための保存可能な基盤だけを提供します。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 新18Biomeへ割り当てる変更禁止の数値IDです。
    /// C5完了時に置換するBiomeId列挙型とセーブ形式が同じ値を参照できるよう、数値契約を旧7Biomeから分離します。
    /// </summary>
    public static class BiomeStableIds
    {
        /// <summary>未設定または分類失敗を表す予約値です。</summary>
        public const byte None = 0;

        /// <summary>温帯草原の安定IDです。</summary>
        public const byte TemperateGrassland = 1;

        /// <summary>熱帯サバンナ・疎林の安定IDです。</summary>
        public const byte TropicalSavannaWoodland = 2;

        /// <summary>温帯落葉広葉樹林の安定IDです。</summary>
        public const byte TemperateDeciduousForest = 3;

        /// <summary>暖温帯照葉樹林の安定IDです。</summary>
        public const byte WarmTemperateEvergreenForest = 4;

        /// <summary>温帯山地竹林の安定IDです。</summary>
        public const byte TemperateMontaneBambooForest = 5;

        /// <summary>熱帯雨林の安定IDです。</summary>
        public const byte TropicalRainforest = 6;

        /// <summary>熱帯季節林・モンスーン林の安定IDです。</summary>
        public const byte TropicalSeasonalForest = 7;

        /// <summary>亜熱帯海洋性林の安定IDです。</summary>
        public const byte SubtropicalOceanicForest = 8;

        /// <summary>亜寒帯針葉樹林・タイガの安定IDです。</summary>
        public const byte BorealConiferForest = 9;

        /// <summary>ステップの安定IDです。</summary>
        public const byte Steppe = 10;

        /// <summary>乾燥低木地・疎林の安定IDです。</summary>
        public const byte DryShrublandWoodland = 11;

        /// <summary>サボテン砂漠・乾燥高原の安定IDです。</summary>
        public const byte CactusDesertDryPlateau = 12;

        /// <summary>高温砂漠の安定IDです。</summary>
        public const byte HotDesert = 13;

        /// <summary>淡水湿原・氾濫原の安定IDです。</summary>
        public const byte FreshwaterMarshFloodplain = 14;

        /// <summary>湿地林・沼沢林の安定IDです。</summary>
        public const byte SwampForest = 15;

        /// <summary>ツンドラ・極地荒原の安定IDです。</summary>
        public const byte TundraPolarBarrens = 16;

        /// <summary>高山岩地の安定IDです。</summary>
        public const byte AlpineRockland = 17;

        /// <summary>火山・地熱地帯の安定IDです。</summary>
        public const byte VolcanicGeothermal = 18;

        /// <summary>新Biomeとして有効な最小安定IDです。</summary>
        public const byte First = TemperateGrassland;

        /// <summary>新Biomeとして有効な最大安定IDです。</summary>
        public const byte Last = VolcanicGeothermal;
    }

    /// <summary>
    /// Definitionが通常スコア、水文特殊昇格、必須環境ゲート、初期スポーンのどれへ参加するかを表します。
    /// 分類器は表示名やID範囲から役割を推測せず、この明示フラグを参照します。
    /// </summary>
    [Flags]
    public enum BiomeDefinitionFlags : byte
    {
        /// <summary>分類上の役割を持たない不正な状態です。</summary>
        None = 0,

        /// <summary>合計256の軸重みによる通常適性比較へ参加します。</summary>
        UsesNormalScore = 1 << 0,

        /// <summary>通常比較後のHostと固定水文条件を満たした場合だけ昇格します。</summary>
        HydrologySpecial = 1 << 1,

        /// <summary>海洋影響の段階5を候補必須条件として要求します。</summary>
        RequiresStrongCoastalInfluence = 1 << 2,

        /// <summary>活動的火山に適合する地質を候補必須条件として要求します。</summary>
        RequiresActiveVolcanism = 1 << 3,

        /// <summary>初期スポーン地点の固定Biomeとして許可します。</summary>
        AllowsInitialSpawn = 1 << 4
    }

    /// <summary>
    /// 1つの連続環境軸について、候補になれる許容範囲と最高適性になる代表範囲を保持します。
    /// すべてushortの0〜65,535で保存し、分類時に浮動小数点へ変換しません。
    /// </summary>
    public struct BiomeAxisRange
    {
        /// <summary>候補資格を持つ最小値です。境界値自身を含みます。</summary>
        public ushort AllowedMin;

        /// <summary>最高適性へ到達する代表範囲の最小値です。</summary>
        public ushort PreferredMin;

        /// <summary>最高適性を維持する代表範囲の最大値です。</summary>
        public ushort PreferredMax;

        /// <summary>候補資格を持つ最大値です。境界値自身を含みます。</summary>
        public ushort AllowedMax;

        /// <summary>
        /// 許容範囲と代表範囲が台形型適性を安全に計算できる順序かを返します。
        /// </summary>
        /// <returns>AllowedMin以上AllowedMax以下へ代表範囲が収まる場合はtrueです。</returns>
        public bool IsOrdered()
        {
            return AllowedMin <= PreferredMin
                && PreferredMin <= PreferredMax
                && PreferredMax <= AllowedMax;
        }
    }

    /// <summary>
    /// 通常Biomeの8適性軸へ割り当てる非負整数重みです。
    /// 通常比較へ参加するDefinitionでは合計256、水文特殊Definitionでは合計0に固定します。
    /// </summary>
    public struct BiomeAxisWeights
    {
        /// <summary>地形気温適性へ掛ける重みです。</summary>
        public byte TerrainTemperature;

        /// <summary>基礎湿度適性へ掛ける重みです。</summary>
        public byte BaseMoisture;

        /// <summary>標高適性へ掛ける重みです。</summary>
        public byte Elevation;

        /// <summary>木本植生ポテンシャル適性へ掛ける重みです。</summary>
        public byte WoodyVegetation;

        /// <summary>地形起伏適性へ掛ける重みです。</summary>
        public byte TerrainRuggedness;

        /// <summary>地質分類適合度へ掛ける重みです。</summary>
        public byte Geology;

        /// <summary>火山活動度適性へ掛ける重みです。</summary>
        public byte VolcanicActivity;

        /// <summary>海洋影響適性へ掛ける重みです。</summary>
        public byte CoastalInfluence;

        /// <summary>
        /// 8軸をintへ拡張して合計し、byteの周回を避けて設定検証へ返します。
        /// </summary>
        /// <returns>0〜2,040の軸重み合計です。</returns>
        public int CalculateTotal()
        {
            return TerrainTemperature
                + BaseMoisture
                + Elevation
                + WoodyVegetation
                + TerrainRuggedness
                + Geology
                + VolcanicActivity
                + CoastalInfluence;
        }
    }

    /// <summary>
    /// 1つの新Biomeについて、候補ゲート、代表範囲、軸重み、地質適合度、同点用Saltを保持します。
    /// 独立したBiomeClassificationSettingsシングルトンへ値として保存し、ワールド作成後は変更しません。
    /// </summary>
    public struct BiomeDefinition
    {
        /// <summary>表示順や旧BiomeIdから独立した1〜18の安定IDです。</summary>
        public byte StableId;

        /// <summary>通常比較、水文特殊、必須ゲート、初期スポーンの役割です。</summary>
        public BiomeDefinitionFlags Flags;

        /// <summary>地形気温の候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange TerrainTemperature;

        /// <summary>基礎湿度の候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange BaseMoisture;

        /// <summary>連続標高の候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange Elevation;

        /// <summary>木本植生ポテンシャルの候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange WoodyVegetation;

        /// <summary>地形起伏の候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange TerrainRuggedness;

        /// <summary>火山活動度の候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange VolcanicActivity;

        /// <summary>海洋影響の候補許容範囲と代表範囲です。</summary>
        public BiomeAxisRange CoastalInfluence;

        /// <summary>通常スコアへ使う8軸の整数重みです。</summary>
        public BiomeAxisWeights AxisWeights;

        /// <summary>GeologyClass 1〜8の順で保持する固定適合度です。</summary>
        public FixedList64Bytes<ushort> GeologyFits;

        /// <summary>同じBiomeScoreだけを地域的に一意化するDefinition固有Saltです。</summary>
        public ulong VariationSalt;

        /// <summary>
        /// 指定した安定地質IDに対応する適合度を返します。
        /// </summary>
        /// <param name="geologyClass">1〜8の固定地質分類です。</param>
        /// <returns>地質表が完全なら0〜65,535の適合度、不正なIDまたは表なら0です。</returns>
        public ushort GetGeologyFit(GeologyClass geologyClass)
        {
            int index = (int)geologyClass - (int)GeologyClass.UnconsolidatedSediment;
            if (index < 0 || index >= GeologyFits.Length)
            {
                return 0;
            }

            return GeologyFits[index];
        }
    }

    /// <summary>
    /// 新18BiomeのDefinitionと、水文特殊Biomeごとの許可Host集合をまとめた固定設定です。
    /// 分類器だけが読むECSシングルトンとしてWorldGenerationSettingsから分離し、無関係な生成Jobへの約4KBの値コピーを避けます。
    /// C5-Aでは設定と検証だけを追加し、旧7Biomeの生成結果へはまだ接続しません。
    /// </summary>
    public struct BiomeClassificationSettings : IComponentData
    {
        /// <summary>登録順に依存せず安定IDで検索する18件のBiome Definitionです。</summary>
        public FixedList4096Bytes<BiomeDefinition> Definitions;

        /// <summary>淡水湿原・氾濫原へ昇格できる通常Biomeの安定ID集合です。</summary>
        public FixedList32Bytes<byte> FreshwaterMarshHostIds;

        /// <summary>湿地林・沼沢林へ昇格できる通常Biomeの安定ID集合です。</summary>
        public FixedList32Bytes<byte> SwampForestHostIds;
    }

    /// <summary>
    /// TERRAIN-01C5-Aの固定表を構築し、ID・範囲・重み・地質・Hostの完全性を検証します。
    /// 文字列、管理参照、配列確保を使わず、後続のBurst分類器から同じ値を読める形を維持します。
    /// </summary>
    public static class BiomeDefinitionCatalog
    {
        /// <summary>新Biome Definitionの必須件数です。</summary>
        public const int RequiredDefinitionCount = 18;

        /// <summary>各Biomeが持つ地質適合度の必須件数です。</summary>
        public const int RequiredGeologyFitCount = 8;

        /// <summary>通常Biomeの全軸で共有する重み合計です。</summary>
        public const int RequiredNormalWeightTotal = 256;

        /// <summary>全範囲を候補として扱う軸の最小値です。</summary>
        private const ushort AxisMinimum = 0;

        /// <summary>全範囲を候補として扱う軸の最大値です。</summary>
        private const ushort AxisMaximum = ushort.MaxValue;

        /// <summary>
        /// C5-Aの確定表から新18Biomeの既定設定を作ります。
        /// </summary>
        /// <returns>安定ID1〜18、全地質表、2種類の湿原Host集合を持つ固定設定です。</returns>
        public static BiomeClassificationSettings CreateDefault()
        {
            BiomeClassificationSettings settings = default;
            AddNormalDefinitions(ref settings.Definitions);
            AddHydrologyAndExtremeDefinitions(ref settings.Definitions);
            AddWetlandHosts(ref settings);
            return settings;
        }

        /// <summary>
        /// Definition設定が新18Biomeの保存・分類契約を満たすか検証します。
        /// </summary>
        /// <param name="settings">Authoringまたは既定値から作られた固定設定です。</param>
        /// <returns>欠落、重複、範囲逆転、重み不正、Salt重複、地質表不正、Host不正がなければtrueです。</returns>
        public static bool ValidateSettings(in BiomeClassificationSettings settings)
        {
            if (settings.Definitions.Length != RequiredDefinitionCount)
            {
                return false;
            }

            uint foundIdMask = 0;
            BiomeDefinitionFlags knownFlags = BiomeDefinitionFlags.UsesNormalScore
                | BiomeDefinitionFlags.HydrologySpecial
                | BiomeDefinitionFlags.RequiresStrongCoastalInfluence
                | BiomeDefinitionFlags.RequiresActiveVolcanism
                | BiomeDefinitionFlags.AllowsInitialSpawn;

            for (int index = 0; index < settings.Definitions.Length; index++)
            {
                BiomeDefinition definition = settings.Definitions[index];
                int stableIndex = definition.StableId - BiomeStableIds.First;
                if (stableIndex < 0 || stableIndex >= RequiredDefinitionCount)
                {
                    return false;
                }

                uint idBit = 1u << stableIndex;
                if ((foundIdMask & idBit) != 0
                    || definition.VariationSalt == 0
                    || (definition.Flags & ~knownFlags) != 0
                    || !ValidateRanges(definition)
                    || !MatchesCanonicalAllowedRanges(definition)
                    || !ValidateRoleAndWeights(definition)
                    || !ValidateGeologyFits(definition))
                {
                    return false;
                }

                // Saltは表示名や登録順から生成しません。同じ値が2件あると完全同点の地域差を共有するため拒否します。
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    if (settings.Definitions[previousIndex].VariationSalt == definition.VariationSalt)
                    {
                        return false;
                    }
                }

                foundIdMask |= idBit;
            }

            uint requiredIdMask = (1u << RequiredDefinitionCount) - 1u;
            return foundIdMask == requiredIdMask
                && ValidateHostList(
                    settings,
                    settings.FreshwaterMarshHostIds,
                    BuildFreshwaterMarshHostMask())
                && ValidateHostList(
                    settings,
                    settings.SwampForestHostIds,
                    BuildSwampForestHostMask());
        }

        /// <summary>
        /// 登録順に依存せず、指定した安定IDのDefinitionを検索します。
        /// </summary>
        /// <param name="settings">検索対象の18Biome設定です。</param>
        /// <param name="stableId">1〜18の安定IDです。</param>
        /// <param name="definition">成功時に該当するDefinitionを返します。</param>
        /// <returns>指定IDが1件だけ登録されていればtrueです。</returns>
        public static bool TryGetDefinition(
            in BiomeClassificationSettings settings,
            byte stableId,
            out BiomeDefinition definition)
        {
            for (int index = 0; index < settings.Definitions.Length; index++)
            {
                if (settings.Definitions[index].StableId == stableId)
                {
                    definition = settings.Definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        /// <summary>
        /// ID1〜13の通常気候Biomeを確定表どおりに追加します。
        /// </summary>
        /// <param name="definitions">初期化中の固定長Definition一覧です。</param>
        private static void AddNormalDefinitions(ref FixedList4096Bytes<BiomeDefinition> definitions)
        {
            definitions.Add(CreateDefinition(
                BiomeStableIds.TemperateGrassland,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(23000, 25000, 36000, 39999), Range(28500, 32000, 44000, 49999),
                Range(20000, 22000, 40000, 42999), Range(0, 13107, 26213, 65535),
                Range(0, 0, 19660, 65535), FullRange(), FullRange(),
                Weights(48, 48, 32, 72, 40, 16, 0, 0), 0xC5A0000000000001UL,
                58981, 32767, 45874, 45874, 19660, 19660, 45874, 32767));
            definitions.Add(CreateDefinition(
                BiomeStableIds.TropicalSavannaWoodland,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(40000, 45000, 60000, 65535), Range(16000, 18000, 34000, 39499),
                Range(20000, 22000, 43000, 53999), Range(0, 26214, 39320, 65535),
                Range(0, 0, 32767, 65535), FullRange(), FullRange(),
                Weights(64, 56, 24, 64, 24, 24, 0, 0), 0xC5A0000000000002UL,
                45874, 45874, 32767, 32767, 32767, 32767, 45874, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.TemperateDeciduousForest,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(23000, 25000, 36000, 39999), Range(39500, 45000, 58000, 65535),
                Range(20000, 28000, 48000, 53999), Range(0, 39321, 58981, 65535),
                Range(0, 13107, 39320, 65535), FullRange(), FullRange(),
                Weights(48, 56, 32, 72, 24, 24, 0, 0), 0xC5A0000000000003UL,
                58981, 32767, 45874, 45874, 32767, 32767, 45874, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.WarmTemperateEvergreenForest,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(32768, 34000, 39000, 39999), Range(39500, 45000, 60000, 65535),
                Range(20000, 22000, 45000, 53999), Range(0, 45874, 65535, 65535),
                Range(0, 13107, 39320, 65535), FullRange(), FullRange(),
                Weights(56, 56, 24, 72, 24, 24, 0, 0), 0xC5A0000000000004UL,
                45874, 32767, 32767, 45874, 45874, 45874, 58981, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.TemperateMontaneBambooForest,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(12000, 18000, 30000, 32767), Range(39500, 45000, 60000, 65535),
                Range(36001, 38000, 52000, 53999), Range(0, 39321, 65535, 65535),
                Range(0, 26214, 52427, 65535), FullRange(), FullRange(),
                Weights(48, 48, 56, 64, 24, 16, 0, 0), 0xC5A0000000000005UL,
                19660, 32767, 32767, 45874, 45874, 45874, 58981, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.TropicalRainforest,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(40000, 48000, 65535, 65535), Range(50000, 55000, 65535, 65535),
                Range(20000, 22000, 40000, 53999), Range(0, 52428, 65535, 65535),
                Range(0, 13107, 39320, 65535), FullRange(), FullRange(),
                Weights(64, 64, 16, 72, 16, 24, 0, 0), 0xC5A0000000000006UL,
                45874, 32767, 45874, 45874, 32767, 32767, 58981, 6553));
            definitions.Add(CreateDefinition(
                BiomeStableIds.TropicalSeasonalForest,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(40000, 44000, 60000, 65535), Range(28500, 32000, 45000, 49999),
                Range(20000, 22000, 43000, 53999), Range(0, 39321, 52427, 65535),
                Range(0, 13107, 39320, 65535), FullRange(), FullRange(),
                Weights(64, 64, 16, 64, 24, 24, 0, 0), 0xC5A0000000000007UL,
                45874, 45874, 32767, 45874, 32767, 32767, 58981, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.SubtropicalOceanicForest,
                BiomeDefinitionFlags.UsesNormalScore
                    | BiomeDefinitionFlags.RequiresStrongCoastalInfluence
                    | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(32768, 36000, 56000, 65535), Range(39500, 45000, 60000, 65535),
                Range(20000, 22000, 40000, 53999), Range(0, 45874, 65535, 65535),
                Range(0, 13107, 45874, 65535), FullRange(), Range(52428, 58981, 65535, 65535),
                Weights(48, 48, 16, 56, 16, 16, 0, 56), 0xC5A0000000000008UL,
                45874, 32767, 45874, 32767, 32767, 32767, 58981, 32767));
            definitions.Add(CreateDefinition(
                BiomeStableIds.BorealConiferForest,
                BiomeDefinitionFlags.UsesNormalScore,
                Range(12000, 14000, 28000, 32767), Range(28500, 35000, 55000, 65535),
                Range(20000, 30000, 50000, 53999), Range(0, 39321, 58981, 65535),
                Range(0, 13107, 45874, 65535), FullRange(), FullRange(),
                Weights(64, 48, 40, 56, 24, 24, 0, 0), 0xC5A0000000000009UL,
                32767, 32767, 19660, 32767, 58981, 58981, 45874, 32767));
            definitions.Add(CreateDefinition(
                BiomeStableIds.Steppe,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(12000, 16000, 34000, 39999), Range(0, 8000, 24000, 28499),
                Range(20000, 26000, 48000, 53999), Range(0, 0, 13106, 65535),
                Range(0, 0, 26213, 65535), FullRange(), FullRange(),
                Weights(48, 72, 32, 64, 24, 16, 0, 0), 0xC5A000000000000AUL,
                58981, 45874, 45874, 58981, 19660, 32767, 32767, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.DryShrublandWoodland,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(32768, 35000, 55000, 65535), Range(16000, 20000, 35000, 39499),
                Range(20000, 24000, 48000, 53999), Range(0, 26214, 45874, 65535),
                Range(0, 13107, 39320, 65535), FullRange(), FullRange(),
                Weights(56, 64, 24, 64, 24, 24, 0, 0), 0xC5A000000000000BUL,
                32767, 45874, 32767, 32767, 58981, 45874, 45874, 32767));
            definitions.Add(CreateDefinition(
                BiomeStableIds.CactusDesertDryPlateau,
                BiomeDefinitionFlags.UsesNormalScore,
                Range(32768, 36000, 56000, 65535), Range(0, 4000, 22000, 28499),
                Range(20000, 32000, 52000, 53999), Range(0, 0, 26213, 65535),
                Range(0, 26214, 52427, 65535), FullRange(), FullRange(),
                Weights(48, 72, 40, 32, 40, 24, 0, 0), 0xC5A000000000000CUL,
                19660, 58981, 45874, 32767, 58981, 45874, 58981, 32767));
            definitions.Add(CreateDefinition(
                BiomeStableIds.HotDesert,
                BiomeDefinitionFlags.UsesNormalScore,
                Range(40000, 48000, 65535, 65535), Range(0, 0, 10000, 15999),
                Range(20000, 22000, 45000, 53999), Range(0, 0, 13106, 65535),
                Range(0, 0, 39320, 65535), FullRange(), FullRange(),
                Weights(80, 80, 24, 24, 24, 24, 0, 0), 0xC5A000000000000DUL,
                58981, 58981, 45874, 32767, 45874, 32767, 45874, 32767));
        }

        /// <summary>
        /// 水文特殊2Biomeと寒冷・高山・火山のDefinitionを確定表どおりに追加します。
        /// </summary>
        /// <param name="definitions">ID1〜13が追加済みの固定長Definition一覧です。</param>
        private static void AddHydrologyAndExtremeDefinitions(
            ref FixedList4096Bytes<BiomeDefinition> definitions)
        {
            definitions.Add(CreateDefinition(
                BiomeStableIds.FreshwaterMarshFloodplain,
                BiomeDefinitionFlags.HydrologySpecial | BiomeDefinitionFlags.AllowsInitialSpawn,
                Range(12000, 12000, 65535, 65535), Range(16000, 16000, 65535, 65535),
                Range(20000, 20000, 36000, 36000), Range(0, 0, 39320, 39320),
                FullRange(), FullRange(), FullRange(), Weights(0, 0, 0, 0, 0, 0, 0, 0),
                0xC5A000000000000EUL,
                58981, 45874, 32767, 58981, 19660, 19660, 32767, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.SwampForest,
                BiomeDefinitionFlags.HydrologySpecial,
                Range(32768, 32768, 65535, 65535), Range(39500, 39500, 65535, 65535),
                Range(20000, 20000, 36000, 36000), Range(39321, 39321, 65535, 65535),
                FullRange(), FullRange(), FullRange(), Weights(0, 0, 0, 0, 0, 0, 0, 0),
                0xC5A000000000000FUL,
                58981, 32767, 32767, 58981, 19660, 19660, 45874, 19660));
            definitions.Add(CreateDefinition(
                BiomeStableIds.TundraPolarBarrens,
                BiomeDefinitionFlags.UsesNormalScore,
                Range(0, 0, 18000, 22999), Range(0, 16000, 45000, 65535),
                Range(20000, 30000, 60000, 65535), Range(0, 0, 13106, 65535),
                Range(0, 0, 39320, 65535), FullRange(), FullRange(),
                Weights(96, 16, 48, 48, 32, 16, 0, 0), 0xC5A0000000000010UL,
                45874, 32767, 32767, 45874, 58981, 58981, 45874, 32767));
            definitions.Add(CreateDefinition(
                BiomeStableIds.AlpineRockland,
                BiomeDefinitionFlags.UsesNormalScore,
                Range(0, 0, 28000, 65535), Range(0, 16000, 45000, 65535),
                Range(43000, 54000, 65535, 65535), Range(0, 0, 19660, 65535),
                Range(0, 45874, 65535, 65535), FullRange(), FullRange(),
                Weights(24, 8, 88, 16, 88, 32, 0, 0), 0xC5A0000000000011UL,
                19660, 32767, 45874, 32767, 58981, 58981, 58981, 58981));
            definitions.Add(CreateDefinition(
                BiomeStableIds.VolcanicGeothermal,
                BiomeDefinitionFlags.UsesNormalScore | BiomeDefinitionFlags.RequiresActiveVolcanism,
                FullRange(), FullRange(), Range(20000, 20000, 65535, 65535),
                Range(0, 0, 39320, 65535), Range(0, 32767, 65535, 65535),
                Range(39321, 49152, 52427, 52427), FullRange(),
                Weights(0, 0, 24, 16, 40, 72, 104, 0), 0xC5A0000000000012UL,
                0, 0, 0, 0, 0, 0, 65535, 0));
        }

        /// <summary>
        /// 水文特殊Biomeごとの通常Host集合を、分類表と同じ安定IDで追加します。
        /// </summary>
        /// <param name="settings">初期化中のBiome分類設定です。</param>
        private static void AddWetlandHosts(ref BiomeClassificationSettings settings)
        {
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.TemperateGrassland);
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.TropicalSavannaWoodland);
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.TemperateDeciduousForest);
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.WarmTemperateEvergreenForest);
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.TropicalSeasonalForest);
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.BorealConiferForest);
            settings.FreshwaterMarshHostIds.Add(BiomeStableIds.TundraPolarBarrens);

            settings.SwampForestHostIds.Add(BiomeStableIds.TemperateDeciduousForest);
            settings.SwampForestHostIds.Add(BiomeStableIds.WarmTemperateEvergreenForest);
            settings.SwampForestHostIds.Add(BiomeStableIds.TropicalRainforest);
            settings.SwampForestHostIds.Add(BiomeStableIds.SubtropicalOceanicForest);
        }

        /// <summary>
        /// 確定表の1行をunmanagedなDefinitionへ変換します。
        /// </summary>
        /// <param name="stableId">表示順から独立して保存する1〜18の安定IDです。</param>
        /// <param name="flags">通常比較、水文特殊、必須ゲート、初期スポーン可否をまとめた役割です。</param>
        /// <param name="terrainTemperature">地形気温の許容範囲と代表範囲です。</param>
        /// <param name="baseMoisture">基礎湿度の許容範囲と代表範囲です。</param>
        /// <param name="elevation">連続標高の許容範囲と代表範囲です。</param>
        /// <param name="woodyVegetation">木本植生ポテンシャルの許容範囲と代表範囲です。</param>
        /// <param name="terrainRuggedness">地形起伏の許容範囲と代表範囲です。</param>
        /// <param name="volcanicActivity">火山活動度の許容範囲と代表範囲です。</param>
        /// <param name="coastalInfluence">海洋影響の許容範囲と代表範囲です。</param>
        /// <param name="axisWeights">通常適性比較へ使う8軸の整数重みです。</param>
        /// <param name="variationSalt">同点候補の地域差だけに使う固有Saltです。</param>
        /// <param name="unconsolidatedSedimentFit">未固結堆積物への適合度です。</param>
        /// <param name="sandstoneFit">砂岩への適合度です。</param>
        /// <param name="limestoneFit">石灰岩への適合度です。</param>
        /// <param name="mudstoneSlateFit">泥岩・頁岩・粘板岩への適合度です。</param>
        /// <param name="graniticRockFit">花崗岩質岩への適合度です。</param>
        /// <param name="metamorphicRockFit">変成岩への適合度です。</param>
        /// <param name="volcanicRockFit">火山岩への適合度です。</param>
        /// <param name="ultramaficRockFit">超苦鉄質岩への適合度です。</param>
        /// <returns>8地質を安定順で格納した1件のBiome Definitionです。</returns>
        private static BiomeDefinition CreateDefinition(
            byte stableId,
            BiomeDefinitionFlags flags,
            BiomeAxisRange terrainTemperature,
            BiomeAxisRange baseMoisture,
            BiomeAxisRange elevation,
            BiomeAxisRange woodyVegetation,
            BiomeAxisRange terrainRuggedness,
            BiomeAxisRange volcanicActivity,
            BiomeAxisRange coastalInfluence,
            BiomeAxisWeights axisWeights,
            ulong variationSalt,
            ushort unconsolidatedSedimentFit,
            ushort sandstoneFit,
            ushort limestoneFit,
            ushort mudstoneSlateFit,
            ushort graniticRockFit,
            ushort metamorphicRockFit,
            ushort volcanicRockFit,
            ushort ultramaficRockFit)
        {
            BiomeDefinition definition = new BiomeDefinition
            {
                StableId = stableId,
                Flags = flags,
                TerrainTemperature = terrainTemperature,
                BaseMoisture = baseMoisture,
                Elevation = elevation,
                WoodyVegetation = woodyVegetation,
                TerrainRuggedness = terrainRuggedness,
                VolcanicActivity = volcanicActivity,
                CoastalInfluence = coastalInfluence,
                AxisWeights = axisWeights,
                VariationSalt = variationSalt
            };

            // GeologyClassの安定ID順で格納し、表示上の列順や登録順を分類の意味へ使いません。
            definition.GeologyFits.Add(unconsolidatedSedimentFit);
            definition.GeologyFits.Add(sandstoneFit);
            definition.GeologyFits.Add(limestoneFit);
            definition.GeologyFits.Add(mudstoneSlateFit);
            definition.GeologyFits.Add(graniticRockFit);
            definition.GeologyFits.Add(metamorphicRockFit);
            definition.GeologyFits.Add(volcanicRockFit);
            definition.GeologyFits.Add(ultramaficRockFit);
            return definition;
        }

        /// <summary>
        /// 台形型適性へ渡す許容範囲と代表範囲を作ります。
        /// </summary>
        /// <param name="allowedMin">候補に含める最小値です。</param>
        /// <param name="preferredMin">最高適性となる代表範囲の最小値です。</param>
        /// <param name="preferredMax">最高適性となる代表範囲の最大値です。</param>
        /// <param name="allowedMax">候補に含める最大値です。</param>
        /// <returns>指定した4境界を保持する固定小数点軸範囲です。</returns>
        private static BiomeAxisRange Range(
            ushort allowedMin,
            ushort preferredMin,
            ushort preferredMax,
            ushort allowedMax)
        {
            return new BiomeAxisRange
            {
                AllowedMin = allowedMin,
                PreferredMin = preferredMin,
                PreferredMax = preferredMax,
                AllowedMax = allowedMax
            };
        }

        /// <summary>
        /// 候補ゲートにもスコアにも使用しない軸の全範囲定義を返します。
        /// </summary>
        /// <returns>0〜65,535の全域を代表範囲にも含める軸範囲です。</returns>
        private static BiomeAxisRange FullRange()
        {
            return Range(AxisMinimum, AxisMinimum, AxisMaximum, AxisMaximum);
        }

        /// <summary>
        /// 表のT・M・E・W・R・G・V・C列から軸重みを作ります。
        /// </summary>
        /// <param name="terrainTemperature">地形気温の重みです。</param>
        /// <param name="baseMoisture">基礎湿度の重みです。</param>
        /// <param name="elevation">連続標高の重みです。</param>
        /// <param name="woodyVegetation">木本植生ポテンシャルの重みです。</param>
        /// <param name="terrainRuggedness">地形起伏の重みです。</param>
        /// <param name="geology">地質適合度の重みです。</param>
        /// <param name="volcanicActivity">火山活動度の重みです。</param>
        /// <param name="coastalInfluence">海洋影響の重みです。</param>
        /// <returns>指定値を8軸へ対応づけた重み一式です。</returns>
        private static BiomeAxisWeights Weights(
            byte terrainTemperature,
            byte baseMoisture,
            byte elevation,
            byte woodyVegetation,
            byte terrainRuggedness,
            byte geology,
            byte volcanicActivity,
            byte coastalInfluence)
        {
            return new BiomeAxisWeights
            {
                TerrainTemperature = terrainTemperature,
                BaseMoisture = baseMoisture,
                Elevation = elevation,
                WoodyVegetation = woodyVegetation,
                TerrainRuggedness = terrainRuggedness,
                Geology = geology,
                VolcanicActivity = volcanicActivity,
                CoastalInfluence = coastalInfluence
            };
        }

        /// <summary>
        /// 全連続軸が許容・代表範囲の順序を満たすか検証します。
        /// </summary>
        /// <param name="definition">7本の連続軸範囲を持つ検証対象です。</param>
        /// <returns>すべての軸で許容範囲内に代表範囲が収まる場合はtrueです。</returns>
        private static bool ValidateRanges(in BiomeDefinition definition)
        {
            return definition.TerrainTemperature.IsOrdered()
                && definition.BaseMoisture.IsOrdered()
                && definition.Elevation.IsOrdered()
                && definition.WoodyVegetation.IsOrdered()
                && definition.TerrainRuggedness.IsOrdered()
                && definition.VolcanicActivity.IsOrdered()
                && definition.CoastalInfluence.IsOrdered();
        }

        /// <summary>
        /// 気温・湿度・標高の許容範囲が第2.4〜2.6節の確定値と一致するか検証します。
        /// </summary>
        /// <param name="definition">安定IDと主要3軸を照合する検証対象です。</param>
        /// <returns>安定IDに対応する3軸の許容境界がすべて一致する場合はtrueです。</returns>
        private static bool MatchesCanonicalAllowedRanges(in BiomeDefinition definition)
        {
            switch (definition.StableId)
            {
                case BiomeStableIds.TemperateGrassland:
                    return MatchesAllowed(definition, 23000, 39999, 28500, 49999, 20000, 42999);
                case BiomeStableIds.TropicalSavannaWoodland:
                    return MatchesAllowed(definition, 40000, 65535, 16000, 39499, 20000, 53999);
                case BiomeStableIds.TemperateDeciduousForest:
                    return MatchesAllowed(definition, 23000, 39999, 39500, 65535, 20000, 53999);
                case BiomeStableIds.WarmTemperateEvergreenForest:
                    return MatchesAllowed(definition, 32768, 39999, 39500, 65535, 20000, 53999);
                case BiomeStableIds.TemperateMontaneBambooForest:
                    return MatchesAllowed(definition, 12000, 32767, 39500, 65535, 36001, 53999);
                case BiomeStableIds.TropicalRainforest:
                    return MatchesAllowed(definition, 40000, 65535, 50000, 65535, 20000, 53999);
                case BiomeStableIds.TropicalSeasonalForest:
                    return MatchesAllowed(definition, 40000, 65535, 28500, 49999, 20000, 53999);
                case BiomeStableIds.SubtropicalOceanicForest:
                    return MatchesAllowed(definition, 32768, 65535, 39500, 65535, 20000, 53999);
                case BiomeStableIds.BorealConiferForest:
                    return MatchesAllowed(definition, 12000, 32767, 28500, 65535, 20000, 53999);
                case BiomeStableIds.Steppe:
                    return MatchesAllowed(definition, 12000, 39999, 0, 28499, 20000, 53999);
                case BiomeStableIds.DryShrublandWoodland:
                    return MatchesAllowed(definition, 32768, 65535, 16000, 39499, 20000, 53999);
                case BiomeStableIds.CactusDesertDryPlateau:
                    return MatchesAllowed(definition, 32768, 65535, 0, 28499, 20000, 53999);
                case BiomeStableIds.HotDesert:
                    return MatchesAllowed(definition, 40000, 65535, 0, 15999, 20000, 53999);
                case BiomeStableIds.FreshwaterMarshFloodplain:
                    return MatchesAllowed(definition, 12000, 65535, 16000, 65535, 20000, 36000);
                case BiomeStableIds.SwampForest:
                    return MatchesAllowed(definition, 32768, 65535, 39500, 65535, 20000, 36000);
                case BiomeStableIds.TundraPolarBarrens:
                    return MatchesAllowed(definition, 0, 22999, 0, 65535, 20000, 65535);
                case BiomeStableIds.AlpineRockland:
                    return MatchesAllowed(definition, 0, 65535, 0, 65535, 43000, 65535);
                case BiomeStableIds.VolcanicGeothermal:
                    return MatchesAllowed(definition, 0, 65535, 0, 65535, 20000, 65535);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Definitionの3つの主要候補範囲を確定表の境界と比較します。
        /// </summary>
        /// <param name="definition">比較対象のBiome Definitionです。</param>
        /// <param name="temperatureMin">地形気温の確定最小値です。</param>
        /// <param name="temperatureMax">地形気温の確定最大値です。</param>
        /// <param name="moistureMin">基礎湿度の確定最小値です。</param>
        /// <param name="moistureMax">基礎湿度の確定最大値です。</param>
        /// <param name="elevationMin">連続標高の確定最小値です。</param>
        /// <param name="elevationMax">連続標高の確定最大値です。</param>
        /// <returns>3軸の許容境界がすべて指定値と一致する場合はtrueです。</returns>
        private static bool MatchesAllowed(
            in BiomeDefinition definition,
            ushort temperatureMin,
            ushort temperatureMax,
            ushort moistureMin,
            ushort moistureMax,
            ushort elevationMin,
            ushort elevationMax)
        {
            return definition.TerrainTemperature.AllowedMin == temperatureMin
                && definition.TerrainTemperature.AllowedMax == temperatureMax
                && definition.BaseMoisture.AllowedMin == moistureMin
                && definition.BaseMoisture.AllowedMax == moistureMax
                && definition.Elevation.AllowedMin == elevationMin
                && definition.Elevation.AllowedMax == elevationMax;
        }

        /// <summary>
        /// 通常比較と水文特殊が排他的で、各役割の重みと必須ゲートが確定契約に一致するか検証します。
        /// </summary>
        /// <param name="definition">役割フラグと軸重みを照合する検証対象です。</param>
        /// <returns>役割、重み合計、必須ゲート、初期スポーン可否が一致する場合はtrueです。</returns>
        private static bool ValidateRoleAndWeights(in BiomeDefinition definition)
        {
            bool usesNormalScore = (definition.Flags & BiomeDefinitionFlags.UsesNormalScore) != 0;
            bool isHydrologySpecial = (definition.Flags & BiomeDefinitionFlags.HydrologySpecial) != 0;
            if (usesNormalScore == isHydrologySpecial)
            {
                return false;
            }

            int weightTotal = definition.AxisWeights.CalculateTotal();
            if (usesNormalScore)
            {
                if (weightTotal != RequiredNormalWeightTotal
                    || definition.AxisWeights.Geology == 0
                    || (definition.AxisWeights.TerrainTemperature == 0
                        && definition.StableId != BiomeStableIds.VolcanicGeothermal)
                    || (definition.AxisWeights.BaseMoisture == 0
                        && definition.StableId != BiomeStableIds.VolcanicGeothermal))
                {
                    return false;
                }
            }
            else if (weightTotal != 0
                || definition.StableId < BiomeStableIds.FreshwaterMarshFloodplain
                || definition.StableId > BiomeStableIds.SwampForest)
            {
                return false;
            }

            bool requiresCoast = (definition.Flags
                & BiomeDefinitionFlags.RequiresStrongCoastalInfluence) != 0;
            bool requiresVolcanism = (definition.Flags
                & BiomeDefinitionFlags.RequiresActiveVolcanism) != 0;
            bool allowsInitialSpawn = (definition.Flags
                & BiomeDefinitionFlags.AllowsInitialSpawn) != 0;
            return requiresCoast == (definition.StableId == BiomeStableIds.SubtropicalOceanicForest)
                && requiresVolcanism == (definition.StableId == BiomeStableIds.VolcanicGeothermal)
                && allowsInitialSpawn == IsInitialSpawnAllowed(definition.StableId)
                && (definition.AxisWeights.CoastalInfluence > 0) == requiresCoast
                && (definition.AxisWeights.VolcanicActivity > 0) == requiresVolcanism;
        }

        /// <summary>
        /// 第3.2節で初期スポーンを許可したBiomeかを、表示順に依存しない安定IDで返します。
        /// </summary>
        /// <param name="stableId">判定する新Biomeの安定IDです。</param>
        /// <returns>第3.2節で初期スポーンが許可されている場合はtrueです。</returns>
        private static bool IsInitialSpawnAllowed(byte stableId)
        {
            switch (stableId)
            {
                case BiomeStableIds.TemperateGrassland:
                case BiomeStableIds.TropicalSavannaWoodland:
                case BiomeStableIds.TemperateDeciduousForest:
                case BiomeStableIds.WarmTemperateEvergreenForest:
                case BiomeStableIds.TemperateMontaneBambooForest:
                case BiomeStableIds.TropicalRainforest:
                case BiomeStableIds.TropicalSeasonalForest:
                case BiomeStableIds.SubtropicalOceanicForest:
                case BiomeStableIds.Steppe:
                case BiomeStableIds.DryShrublandWoodland:
                case BiomeStableIds.FreshwaterMarshFloodplain:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 8地質の欠落と、火山Biomeの排他的地質ゲートを検証します。
        /// </summary>
        /// <param name="definition">地質適合表を持つ検証対象です。</param>
        /// <returns>8件が揃い、火山Biomeなら火山岩だけが最高適合の場合はtrueです。</returns>
        private static bool ValidateGeologyFits(in BiomeDefinition definition)
        {
            if (definition.GeologyFits.Length != RequiredGeologyFitCount)
            {
                return false;
            }

            if (definition.StableId != BiomeStableIds.VolcanicGeothermal)
            {
                return true;
            }

            for (int index = 0; index < definition.GeologyFits.Length; index++)
            {
                ushort expected = index == (int)GeologyClass.VolcanicRock - 1
                    ? ushort.MaxValue
                    : (ushort)0;
                if (definition.GeologyFits[index] != expected)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Host集合のID、重複、通常Biome限定、確定メンバーをビット集合で検証します。
        /// </summary>
        /// <param name="settings">Host IDからDefinitionを検索する18Biome設定です。</param>
        /// <param name="hostIds">水文特殊Biomeへの昇格を許可する通常Biome ID集合です。</param>
        /// <param name="expectedMask">仕様で確定したHost集合の検証用ビット集合です。</param>
        /// <returns>存在する通常Biomeだけが重複なく確定集合と一致する場合はtrueです。</returns>
        private static bool ValidateHostList(
            in BiomeClassificationSettings settings,
            in FixedList32Bytes<byte> hostIds,
            uint expectedMask)
        {
            uint actualMask = 0;
            for (int index = 0; index < hostIds.Length; index++)
            {
                byte hostId = hostIds[index];
                int stableIndex = hostId - BiomeStableIds.First;
                if (stableIndex < 0
                    || stableIndex >= RequiredDefinitionCount
                    || !TryGetDefinition(settings, hostId, out BiomeDefinition definition)
                    || (definition.Flags & BiomeDefinitionFlags.UsesNormalScore) == 0)
                {
                    return false;
                }

                uint hostBit = 1u << stableIndex;
                if ((actualMask & hostBit) != 0)
                {
                    return false;
                }

                actualMask |= hostBit;
            }

            return actualMask == expectedMask;
        }

        /// <summary>
        /// 淡水湿原・氾濫原へ昇格できる7種類の通常Hostをビット集合で返します。
        /// </summary>
        /// <returns>ID1をbit 0とする確定Host 7種類のビット集合です。</returns>
        private static uint BuildFreshwaterMarshHostMask()
        {
            return IdBit(BiomeStableIds.TemperateGrassland)
                | IdBit(BiomeStableIds.TropicalSavannaWoodland)
                | IdBit(BiomeStableIds.TemperateDeciduousForest)
                | IdBit(BiomeStableIds.WarmTemperateEvergreenForest)
                | IdBit(BiomeStableIds.TropicalSeasonalForest)
                | IdBit(BiomeStableIds.BorealConiferForest)
                | IdBit(BiomeStableIds.TundraPolarBarrens);
        }

        /// <summary>
        /// 湿地林・沼沢林へ昇格できる4種類の通常Hostをビット集合で返します。
        /// </summary>
        /// <returns>ID1をbit 0とする確定Host 4種類のビット集合です。</returns>
        private static uint BuildSwampForestHostMask()
        {
            return IdBit(BiomeStableIds.TemperateDeciduousForest)
                | IdBit(BiomeStableIds.WarmTemperateEvergreenForest)
                | IdBit(BiomeStableIds.TropicalRainforest)
                | IdBit(BiomeStableIds.SubtropicalOceanicForest);
        }

        /// <summary>
        /// 1〜18の安定IDを、ID1がbit 0になる検証用ビットへ変換します。
        /// </summary>
        /// <param name="stableId">1〜18の範囲にある新Biome安定IDです。</param>
        /// <returns>指定IDだけが立った32bitの検証用ビット集合です。</returns>
        private static uint IdBit(byte stableId)
        {
            return 1u << (stableId - BiomeStableIds.First);
        }
    }
}
