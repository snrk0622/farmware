// このファイルは、動物種ごとの生成条件と移動調整値をECS用Blob Assetへ保持するデータ境界です。
// DefinitionCatalogの実行時IDと結び付き、後続の個体生成・移動Systemは文字列やAuthoring値を直接参照しません。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation.Animals
{
    /// <summary>
    /// 動物が出現できる18種のバイオームをbit単位で表す安定した分類です。
    /// BiomeIdの数値を直接shiftせず、ANIMAL-01Bの生成判定で明示的に対応付けます。
    /// </summary>
    [Flags]
    public enum AnimalBiomeMask : ulong
    {
        /// <summary>出現可能なバイオームがない無効値です。</summary>
        None = 0,

        /// <summary>温帯草原を許可します。</summary>
        TemperateGrassland = 1UL << 0,
        /// <summary>熱帯サバンナ・疎林を許可します。</summary>
        TropicalSavannaWoodland = 1UL << 1,
        /// <summary>温帯落葉広葉樹林を許可します。</summary>
        TemperateDeciduousForest = 1UL << 2,
        /// <summary>暖温帯常緑樹林を許可します。</summary>
        WarmTemperateEvergreenForest = 1UL << 3,
        /// <summary>温帯山地竹林を許可します。</summary>
        TemperateMontaneBambooForest = 1UL << 4,
        /// <summary>熱帯雨林を許可します。</summary>
        TropicalRainforest = 1UL << 5,
        /// <summary>熱帯季節林を許可します。</summary>
        TropicalSeasonalForest = 1UL << 6,
        /// <summary>亜熱帯海洋性樹林を許可します。</summary>
        SubtropicalOceanicForest = 1UL << 7,
        /// <summary>亜寒帯針葉樹林を許可します。</summary>
        BorealConiferForest = 1UL << 8,
        /// <summary>ステップを許可します。</summary>
        Steppe = 1UL << 9,
        /// <summary>乾燥灌木地・疎林を許可します。</summary>
        DryShrublandWoodland = 1UL << 10,
        /// <summary>サボテン砂漠・乾燥高原を許可します。</summary>
        CactusDesertDryPlateau = 1UL << 11,
        /// <summary>熱砂漠を許可します。</summary>
        HotDesert = 1UL << 12,
        /// <summary>淡水湿地・氾濫原を許可します。</summary>
        FreshwaterMarshFloodplain = 1UL << 13,
        /// <summary>湿地林を許可します。</summary>
        SwampForest = 1UL << 14,
        /// <summary>ツンドラ・極地荒原を許可します。</summary>
        TundraPolarBarrens = 1UL << 15,
        /// <summary>高山岩地を許可します。</summary>
        AlpineRockland = 1UL << 16,
        /// <summary>火山・地熱地帯を許可します。</summary>
        VolcanicGeothermal = 1UL << 17,

        /// <summary>現在定義済みの18バイオームすべてを表します。</summary>
        All = (1UL << 18) - 1UL
    }

    /// <summary>
    /// 動物が滞在できるBlockセル構成をbit単位で表す安定した分類です。
    /// Runtime IDの数値を直接shiftせず、壁・液体・地面の意味から明示的に対応付けます。
    /// </summary>
    [Flags]
    public enum AnimalGroundMask : byte
    {
        /// <summary>滞在可能な地形がない無効値です。</summary>
        None = 0,

        /// <summary>GroundBlockを持つ通常の陸地を許可します。</summary>
        Land = 1 << 0,

        /// <summary>淡水または海水Liquidを持つ水域を許可します。</summary>
        Water = 1 << 1,

        /// <summary>WallBlockを持つ岩壁セルを許可します。</summary>
        RockWall = 1 << 2,

        /// <summary>マグマLiquidを持つセルを許可します。</summary>
        Magma = 1 << 3,

        /// <summary>現在定義済みの全基礎地形を表します。</summary>
        All = Land | Water | RockWall | Magma
    }

    /// <summary>
    /// 1種類の動物について、生成・移動・外見選択に必要な変更不可の設定を保持します。
    /// 値はBakerが検証済みであり、ANIMAL-01B以降のSystemがWorld存続中に書き換えることはありません。
    /// </summary>
    public struct AnimalSpeciesDefinitionBlob
    {
        /// <summary>共通DefinitionCatalog内で動物種を参照する1始まりの実行時IDです。</summary>
        public DefinitionRuntimeId SpeciesRuntimeId;

        /// <summary>この動物種が出現できるバイオームの組み合わせです。</summary>
        public AnimalBiomeMask AllowedBiomes;

        /// <summary>この動物種が出現・滞在できる基礎地形の組み合わせです。</summary>
        public AnimalGroundMask AllowedGrounds;

        /// <summary>1つの群れを作る最小個体数です。単位は個体です。</summary>
        public byte MinimumHerdSize;

        /// <summary>1つの群れを作る最大個体数です。単位は個体です。</summary>
        public byte MaximumHerdSize;

        /// <summary>有効な地形4096タイル当たりの目標個体数です。ANIMAL-01Bが地域面積へ比例配分します。</summary>
        public ushort TargetPopulationPer4096EligibleTiles;

        /// <summary>移動を終えてから次の行動候補を選ぶまでの最小待機時間です。単位は固定Tickです。</summary>
        public uint MinimumIdleTicks;

        /// <summary>移動を終えてから次の行動候補を選ぶまでの最大待機時間です。単位は固定Tickです。</summary>
        public uint MaximumIdleTicks;

        /// <summary>隣接タイルへ1歩進むのに必要な時間です。単位は固定Tickです。</summary>
        public uint MovementStepIntervalTicks;

        /// <summary>群れの基準位置から通常の徘徊で離れてよい最大距離です。単位はタイルです。</summary>
        public ushort MaximumWanderDistanceInTiles;

        /// <summary>生成時に選べる0始まりの外見差分数です。選んだ番号は個体状態として保存します。</summary>
        public byte AppearanceVariantCount;
    }

    /// <summary>
    /// 全動物種の検証済み設定を読み取り専用メモリへまとめたBlob Assetのルートです。
    /// 配列はSpeciesRuntimeId昇順で、定義入力順やBaker実行順には依存しません。
    /// </summary>
    public struct AnimalSpeciesCatalogBlob
    {
        /// <summary>SpeciesRuntimeId昇順に並んだ動物種設定です。</summary>
        public BlobArray<AnimalSpeciesDefinitionBlob> Species;
    }

    /// <summary>
    /// Bakerが作成した動物種カタログをECS Worldへ公開するSingleton Componentです。
    /// 個体Entityの状態ではなく、全個体が共有する静的設定だけを保持します。
    /// </summary>
    public struct AnimalSpeciesCatalog : IComponentData
    {
        /// <summary>全動物種の検証済み静的設定を保持するBlob Assetです。</summary>
        public BlobAssetReference<AnimalSpeciesCatalogBlob> Value;
    }

    /// <summary>
    /// 実行時IDから動物種設定を取得し、バイオーム・地形Maskの単一候補を判定します。
    /// 通常Tickでは解決済みIDを使い、Definition GUIDや文字列の検索を繰り返しません。
    /// </summary>
    public static class AnimalSpeciesCatalogLookup
    {
        /// <summary>
        /// 共通定義の実行時IDから、対応する動物種設定を二分探索で取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した動物種カタログです。</param>
        /// <param name="speciesRuntimeId">取得する動物種の共通実行時IDです。</param>
        /// <param name="definition">成功時に検証済みの動物種設定を受け取ります。</param>
        /// <returns>カタログが作成済みで、指定IDの動物種が存在する場合はtrueです。</returns>
        public static bool TryGetDefinition(
            BlobAssetReference<AnimalSpeciesCatalogBlob> catalog,
            DefinitionRuntimeId speciesRuntimeId,
            out AnimalSpeciesDefinitionBlob definition)
        {
            definition = default;
            if (!catalog.IsCreated || !speciesRuntimeId.IsValid)
            {
                return false;
            }

            ref AnimalSpeciesCatalogBlob root = ref catalog.Value;
            int lowerIndex = 0;
            int upperIndex = root.Species.Length - 1;

            // 動物種以外の共通定義が間に入るとIDが連続しないため、配列添字ではなく二分探索します。
            while (lowerIndex <= upperIndex)
            {
                int middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
                AnimalSpeciesDefinitionBlob candidate = root.Species[middleIndex];
                int comparison = candidate.SpeciesRuntimeId.Value.CompareTo(
                    speciesRuntimeId.Value);
                if (comparison == 0)
                {
                    definition = candidate;
                    return true;
                }

                if (comparison < 0)
                {
                    lowerIndex = middleIndex + 1;
                }
                else
                {
                    upperIndex = middleIndex - 1;
                }
            }

            return false;
        }

        /// <summary>
        /// 1種類だけを表すバイオームbitが、許可Maskに含まれるか確認します。
        /// </summary>
        /// <param name="allowedBiomes">動物種定義が許可するバイオームMaskです。</param>
        /// <param name="candidateBiome">判定する1bitだけのバイオームです。</param>
        /// <returns>候補が既知の単一bitで、許可Maskに含まれる場合はtrueです。</returns>
        public static bool IsBiomeAllowed(
            AnimalBiomeMask allowedBiomes,
            AnimalBiomeMask candidateBiome)
        {
            ulong candidateValue = (ulong)candidateBiome;
            bool isSingleBit = candidateValue != 0
                && (candidateValue & (candidateValue - 1)) == 0;
            bool isKnownBit = (candidateBiome & ~AnimalBiomeMask.All) == 0;
            return isSingleBit
                && isKnownBit
                && (allowedBiomes & candidateBiome) != 0;
        }

        /// <summary>
        /// 1種類だけを表す基礎地形bitが、許可Maskに含まれるか確認します。
        /// </summary>
        /// <param name="allowedGrounds">動物種定義が許可する基礎地形Maskです。</param>
        /// <param name="candidateGround">判定する1bitだけの基礎地形です。</param>
        /// <returns>候補が既知の単一bitで、許可Maskに含まれる場合はtrueです。</returns>
        public static bool IsGroundAllowed(
            AnimalGroundMask allowedGrounds,
            AnimalGroundMask candidateGround)
        {
            byte candidateValue = (byte)candidateGround;
            bool isSingleBit = candidateValue != 0
                && (candidateValue & (candidateValue - 1)) == 0;
            bool isKnownBit = (candidateGround & ~AnimalGroundMask.All) == 0;
            return isSingleBit
                && isKnownBit
                && (allowedGrounds & candidateGround) != 0;
        }
    }
}
