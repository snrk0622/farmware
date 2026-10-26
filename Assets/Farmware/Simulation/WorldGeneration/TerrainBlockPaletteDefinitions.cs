// このファイルは、Biome表層Groundと地質由来の露出Ground・自然Wallに使う固定材質パレットを定義します。
// DefinitionCatalogAuthoringが材質カタログと地形カタログへ接続し、後続の地形生成が安定材質IDと解決済みBlock IDを同じ表から参照します。

using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1つの材質と整数抽選重みを、Authoring時の固定パレット入力として保持します。
    /// 重みは行ごとに合計256とし、実行時の決定論的な整数抽選へ渡します。
    /// </summary>
    public struct TerrainBlockPaletteWeight
    {
        /// <summary>保存やDefinition登録順から独立した1〜41の安定材質IDです。</summary>
        public TerrainBlockMaterialId MaterialId;

        /// <summary>同じ行の中で使う正の整数抽選重みです。行全体では256に固定します。</summary>
        public ushort Weight;

        /// <summary>
        /// 固定表へ追加する材質IDと抽選重みをまとめます。
        /// </summary>
        /// <param name="materialId">パレットへ含める安定材質IDです。</param>
        /// <param name="weight">行内の正の整数抽選重みです。</param>
        public TerrainBlockPaletteWeight(TerrainBlockMaterialId materialId, ushort weight)
        {
            MaterialId = materialId;
            Weight = weight;
        }
    }

    /// <summary>
    /// 1つのBiomeに対応する表層Ground材質パレットの構築入力です。
    /// 地表被覆が成立するセルで使用し、露出基盤や自然Wallの地質パレットとは分離します。
    /// </summary>
    public sealed class BiomeSurfaceBlockPaletteDefinition
    {
        /// <summary>この行を適用する1〜18のBiome安定IDです。</summary>
        public BiomeId BiomeId;

        /// <summary>表層Ground候補と、合計256になる整数重みです。</summary>
        public TerrainBlockPaletteWeight[] Entries;
    }

    /// <summary>
    /// 1つの地質に対応する露出基盤Groundと自然Wallの材質パレット構築入力です。
    /// GroundとWallは同じ材質候補でも別のBlock形態へ解決するため、配列を明示的に分けます。
    /// </summary>
    public sealed class GeologyBlockPaletteDefinition
    {
        /// <summary>この行を適用する1〜8の卓越基盤地質IDです。</summary>
        public GeologyClass GeologyClass;

        /// <summary>表土が露出したセルで使うGround材質候補と整数重みです。</summary>
        public TerrainBlockPaletteWeight[] GroundEntries;

        /// <summary>自然Wallセルで使うWall材質候補と整数重みです。</summary>
        public TerrainBlockPaletteWeight[] WallEntries;
    }

    /// <summary>
    /// 平坦化した実行時Entry配列の開始位置と件数を保持します。
    /// 各Biome・地質行が自分の候補だけを範囲走査できるように使用します。
    /// </summary>
    public struct TerrainBlockPaletteRange
    {
        /// <summary>カタログ全体のEntries配列内で、このパレットが始まる0始まりの位置です。</summary>
        public int StartIndex;

        /// <summary>このパレットに含まれる材質候補数です。</summary>
        public int Count;
    }

    /// <summary>
    /// 検証済みの材質候補を実行時抽選用に保持します。
    /// 安定IDは保存・監査に、Runtime IDは生成セルへ直接設定するBlock参照に使用します。
    /// </summary>
    public struct TerrainBlockPaletteEntryBlob
    {
        /// <summary>Definition登録順から独立した材質の安定IDです。</summary>
        public TerrainBlockMaterialId MaterialId;

        /// <summary>GroundまたはWall形態へ解決済みの共通Definition実行時IDです。</summary>
        public DefinitionRuntimeId BlockRuntimeId;

        /// <summary>行内の決定論的な整数抽選に使う正の重みです。</summary>
        public ushort Weight;
    }

    /// <summary>
    /// 1つのBiome安定IDと、その表層Groundパレット範囲を結び付けます。
    /// Blob配列はBiome ID昇順へ正規化されるため、入力配列の登録順には依存しません。
    /// </summary>
    public struct BiomeSurfaceBlockPaletteBlob
    {
        /// <summary>この行が表すBiome安定IDです。</summary>
        public BiomeId BiomeId;

        /// <summary>平坦化Entry配列内の表層Ground候補範囲です。</summary>
        public TerrainBlockPaletteRange SurfaceGround;
    }

    /// <summary>
    /// 1つの地質安定IDと、露出Ground・自然Wallの2パレット範囲を結び付けます。
    /// 両範囲は同じ材質IDでも、それぞれGroundBlockとWallBlockのRuntime IDを保持します。
    /// </summary>
    public struct GeologyBlockPaletteBlob
    {
        /// <summary>この行が表す卓越基盤地質の安定IDです。</summary>
        public GeologyClass GeologyClass;

        /// <summary>平坦化Entry配列内の露出基盤Ground候補範囲です。</summary>
        public TerrainBlockPaletteRange ExposedGround;

        /// <summary>平坦化Entry配列内の自然Wall候補範囲です。</summary>
        public TerrainBlockPaletteRange NaturalWall;
    }

    /// <summary>
    /// 18Biomeと8地質の固定パレットを安定ID順に保持する読み取り専用Blob Assetです。
    /// 全候補は1本の配列へ平坦化し、行側のRangeから参照することで重複した配列ヘッダーを避けます。
    /// </summary>
    public struct TerrainBlockPaletteCatalogBlob
    {
        /// <summary>Biome ID 1〜18に対応する表層Groundパレット行です。</summary>
        public BlobArray<BiomeSurfaceBlockPaletteBlob> BiomeSurfacePalettes;

        /// <summary>地質ID 1〜8に対応する露出Ground・自然Wallパレット行です。</summary>
        public BlobArray<GeologyBlockPaletteBlob> GeologyPalettes;

        /// <summary>すべての行からRangeで参照する、解決済み材質候補の平坦化配列です。</summary>
        public BlobArray<TerrainBlockPaletteEntryBlob> Entries;
    }

    /// <summary>
    /// Bakerが構築した固定BlockパレットをECS Worldへ公開するSingleton Componentです。
    /// 静的生成設定なので、固定Tickや地形編集では書き換えません。
    /// </summary>
    public struct TerrainBlockPaletteCatalog : IComponentData
    {
        /// <summary>26行の検証済みBlockパレットを保持する読み取り専用Blob Assetです。</summary>
        public BlobAssetReference<TerrainBlockPaletteCatalogBlob> Value;
    }

    /// <summary>
    /// ゲーム仕様で確定した18Biomeと8地質の固定材質パレット入力を作ります。
    /// 配列は構築時だけ使用し、実行時はTerrainBlockPaletteCatalogBlobへ変換した値を参照します。
    /// </summary>
    public static class TerrainBlockPaletteDefinitions
    {
        /// <summary>各パレット行が必ず満たす整数抽選重みの合計です。</summary>
        public const int RequiredWeightTotal = 256;

        /// <summary>固定表に必要なBiome表層パレット行数です。</summary>
        public const int BiomePaletteCount = BiomeStableIds.Count;

        /// <summary>固定表に必要な地質パレット行数です。</summary>
        public const int GeologyPaletteCount = 8;

        /// <summary>
        /// 18Biomeの表層Ground材質と重みを、安定Biome ID順で作ります。
        /// </summary>
        /// <returns>ゲーム仕様の表層パレット18行を持つ新しい配列です。</returns>
        public static BiomeSurfaceBlockPaletteDefinition[] CreateBiomeSurfaceDefaults()
        {
            return new[]
            {
                Biome(BiomeId.TemperateGrassland,
                    Weight(TerrainBlockMaterialId.GrassLoam, 160),
                    Weight(TerrainBlockMaterialId.MeadowSoil, 64),
                    Weight(TerrainBlockMaterialId.ClaySoil, 32)),
                Biome(BiomeId.TropicalSavannaWoodland,
                    Weight(TerrainBlockMaterialId.DryGrassSoil, 144),
                    Weight(TerrainBlockMaterialId.TropicalRedSoil, 64),
                    Weight(TerrainBlockMaterialId.SandySoil, 48)),
                Biome(BiomeId.TemperateDeciduousForest,
                    Weight(TerrainBlockMaterialId.ForestLoam, 160),
                    Weight(TerrainBlockMaterialId.HumusSoil, 64),
                    Weight(TerrainBlockMaterialId.ClaySoil, 32)),
                Biome(BiomeId.WarmTemperateEvergreenForest,
                    Weight(TerrainBlockMaterialId.ForestLoam, 144),
                    Weight(TerrainBlockMaterialId.HumusSoil, 80),
                    Weight(TerrainBlockMaterialId.ClaySoil, 32)),
                Biome(BiomeId.TemperateMontaneBambooForest,
                    Weight(TerrainBlockMaterialId.MountainLoam, 128),
                    Weight(TerrainBlockMaterialId.HumusSoil, 64),
                    Weight(TerrainBlockMaterialId.ClaySoil, 32),
                    Weight(TerrainBlockMaterialId.AlpineScree, 32)),
                Biome(BiomeId.TropicalRainforest,
                    Weight(TerrainBlockMaterialId.TropicalRedSoil, 112),
                    Weight(TerrainBlockMaterialId.HumusSoil, 80),
                    Weight(TerrainBlockMaterialId.ClaySoil, 64)),
                Biome(BiomeId.TropicalSeasonalForest,
                    Weight(TerrainBlockMaterialId.TropicalRedSoil, 96),
                    Weight(TerrainBlockMaterialId.ForestLoam, 80),
                    Weight(TerrainBlockMaterialId.DryGrassSoil, 48),
                    Weight(TerrainBlockMaterialId.ClaySoil, 32)),
                Biome(BiomeId.SubtropicalOceanicForest,
                    Weight(TerrainBlockMaterialId.IslandLoam, 112),
                    Weight(TerrainBlockMaterialId.TropicalRedSoil, 64),
                    Weight(TerrainBlockMaterialId.SandySoil, 48),
                    Weight(TerrainBlockMaterialId.HumusSoil, 32)),
                Biome(BiomeId.BorealConiferForest,
                    Weight(TerrainBlockMaterialId.PodzolSoil, 144),
                    Weight(TerrainBlockMaterialId.ForestLoam, 64),
                    Weight(TerrainBlockMaterialId.PeatSoil, 48)),
                Biome(BiomeId.Steppe,
                    Weight(TerrainBlockMaterialId.SteppeSoil, 160),
                    Weight(TerrainBlockMaterialId.DryGrassSoil, 64),
                    Weight(TerrainBlockMaterialId.SandySoil, 32)),
                Biome(BiomeId.DryShrublandWoodland,
                    Weight(TerrainBlockMaterialId.DryScrubSoil, 144),
                    Weight(TerrainBlockMaterialId.SandySoil, 64),
                    Weight(TerrainBlockMaterialId.Gravel, 48)),
                Biome(BiomeId.CactusDesertDryPlateau,
                    Weight(TerrainBlockMaterialId.DesertGravel, 112),
                    Weight(TerrainBlockMaterialId.SandySoil, 80),
                    Weight(TerrainBlockMaterialId.DesertSand, 64)),
                Biome(BiomeId.HotDesert,
                    Weight(TerrainBlockMaterialId.DesertSand, 160),
                    Weight(TerrainBlockMaterialId.DesertGravel, 64),
                    Weight(TerrainBlockMaterialId.SandySoil, 32)),
                Biome(BiomeId.FreshwaterMarshFloodplain,
                    Weight(TerrainBlockMaterialId.MarshMud, 112),
                    Weight(TerrainBlockMaterialId.AlluvialSoil, 96),
                    Weight(TerrainBlockMaterialId.PeatSoil, 48)),
                Biome(BiomeId.SwampForest,
                    Weight(TerrainBlockMaterialId.PeatSoil, 96),
                    Weight(TerrainBlockMaterialId.MarshMud, 80),
                    Weight(TerrainBlockMaterialId.HumusSoil, 80)),
                Biome(BiomeId.TundraPolarBarrens,
                    Weight(TerrainBlockMaterialId.PermafrostSoil, 144),
                    Weight(TerrainBlockMaterialId.TundraSoil, 64),
                    Weight(TerrainBlockMaterialId.Gravel, 48)),
                Biome(BiomeId.AlpineRockland,
                    Weight(TerrainBlockMaterialId.AlpineScree, 160),
                    Weight(TerrainBlockMaterialId.Gravel, 64),
                    Weight(TerrainBlockMaterialId.MountainLoam, 32)),
                Biome(BiomeId.VolcanicGeothermal,
                    Weight(TerrainBlockMaterialId.VolcanicAshSoil, 112),
                    Weight(TerrainBlockMaterialId.Scoria, 80),
                    Weight(TerrainBlockMaterialId.Basalt, 64))
            };
        }

        /// <summary>
        /// 8地質の露出基盤Groundと自然Wall材質・重みを、安定地質ID順で作ります。
        /// </summary>
        /// <returns>ゲーム仕様の地質パレット8行を持つ新しい配列です。</returns>
        public static GeologyBlockPaletteDefinition[] CreateGeologyDefaults()
        {
            return new[]
            {
                Geology(GeologyClass.UnconsolidatedSediment,
                    Palette(
                        Weight(TerrainBlockMaterialId.Gravel, 160),
                        Weight(TerrainBlockMaterialId.CompactedSediment, 96)),
                    Palette(
                        Weight(TerrainBlockMaterialId.CompactedSediment, 192),
                        Weight(TerrainBlockMaterialId.Conglomerate, 64))),
                Geology(GeologyClass.Sandstone,
                    Palette(
                        Weight(TerrainBlockMaterialId.Sandstone, 224),
                        Weight(TerrainBlockMaterialId.Conglomerate, 32)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Sandstone, 224),
                        Weight(TerrainBlockMaterialId.Conglomerate, 32))),
                Geology(GeologyClass.Limestone,
                    Palette(
                        Weight(TerrainBlockMaterialId.Limestone, 224),
                        Weight(TerrainBlockMaterialId.Dolomite, 32)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Limestone, 224),
                        Weight(TerrainBlockMaterialId.Dolomite, 32))),
                Geology(GeologyClass.MudstoneSlate,
                    Palette(
                        Weight(TerrainBlockMaterialId.Mudstone, 144),
                        Weight(TerrainBlockMaterialId.Slate, 112)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Mudstone, 144),
                        Weight(TerrainBlockMaterialId.Slate, 112))),
                Geology(GeologyClass.GraniticRock,
                    Palette(
                        Weight(TerrainBlockMaterialId.Granite, 192),
                        Weight(TerrainBlockMaterialId.Granodiorite, 64)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Granite, 192),
                        Weight(TerrainBlockMaterialId.Granodiorite, 64))),
                Geology(GeologyClass.MetamorphicRock,
                    Palette(
                        Weight(TerrainBlockMaterialId.Schist, 96),
                        Weight(TerrainBlockMaterialId.Gneiss, 96),
                        Weight(TerrainBlockMaterialId.Quartzite, 64)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Schist, 96),
                        Weight(TerrainBlockMaterialId.Gneiss, 96),
                        Weight(TerrainBlockMaterialId.Quartzite, 64))),
                Geology(GeologyClass.VolcanicRock,
                    Palette(
                        Weight(TerrainBlockMaterialId.Basalt, 128),
                        Weight(TerrainBlockMaterialId.Andesite, 64),
                        Weight(TerrainBlockMaterialId.Tuff, 64)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Basalt, 128),
                        Weight(TerrainBlockMaterialId.Andesite, 64),
                        Weight(TerrainBlockMaterialId.Tuff, 64))),
                Geology(GeologyClass.UltramaficRock,
                    Palette(
                        Weight(TerrainBlockMaterialId.Serpentinite, 160),
                        Weight(TerrainBlockMaterialId.Peridotite, 96)),
                    Palette(
                        Weight(TerrainBlockMaterialId.Serpentinite, 160),
                        Weight(TerrainBlockMaterialId.Peridotite, 96)))
            };
        }

        /// <summary>1つのBiome表層パレット行を作ります。</summary>
        /// <param name="biomeId">行へ割り当てるBiome安定IDです。</param>
        /// <param name="entries">表層Ground材質候補です。</param>
        /// <returns>カタログ構築へ渡せる入力行です。</returns>
        private static BiomeSurfaceBlockPaletteDefinition Biome(
            BiomeId biomeId,
            params TerrainBlockPaletteWeight[] entries)
        {
            return new BiomeSurfaceBlockPaletteDefinition
            {
                BiomeId = biomeId,
                Entries = entries
            };
        }

        /// <summary>1つの地質パレット行を作ります。</summary>
        /// <param name="geologyClass">行へ割り当てる地質安定IDです。</param>
        /// <param name="groundEntries">露出基盤Ground候補です。</param>
        /// <param name="wallEntries">自然Wall候補です。</param>
        /// <returns>カタログ構築へ渡せる入力行です。</returns>
        private static GeologyBlockPaletteDefinition Geology(
            GeologyClass geologyClass,
            TerrainBlockPaletteWeight[] groundEntries,
            TerrainBlockPaletteWeight[] wallEntries)
        {
            return new GeologyBlockPaletteDefinition
            {
                GeologyClass = geologyClass,
                GroundEntries = groundEntries,
                WallEntries = wallEntries
            };
        }

        /// <summary>可読性を保ったまま材質候補配列を地質行へ渡します。</summary>
        /// <param name="entries">同じ用途へ属する材質候補です。</param>
        /// <returns>呼び出し側が指定した候補配列です。</returns>
        private static TerrainBlockPaletteWeight[] Palette(
            params TerrainBlockPaletteWeight[] entries)
        {
            return entries;
        }

        /// <summary>固定表へ記述する1つの材質候補を作ります。</summary>
        /// <param name="materialId">安定材質IDです。</param>
        /// <param name="weight">行内の整数抽選重みです。</param>
        /// <returns>材質IDと重みを持つ入力値です。</returns>
        private static TerrainBlockPaletteWeight Weight(
            TerrainBlockMaterialId materialId,
            ushort weight)
        {
            return new TerrainBlockPaletteWeight(materialId, weight);
        }
    }

    /// <summary>
    /// 固定パレット入力を材質・地形カタログへ接続し、実行時用Blob Assetを構築します。
    /// 不完全な行、重複材質、重み合計違反、Block分類違反をBake時に拒否します。
    /// </summary>
    public static class TerrainBlockPaletteCatalogBuilder
    {
        /// <summary>
        /// 18Biomeと8地質の全パレットを検証し、安定ID順の実行時カタログを作ります。
        /// </summary>
        /// <param name="terrainCatalog">Block分類と自然Wall属性を確認する地形カタログです。</param>
        /// <param name="materialCatalog">安定材質IDをGround・Wall Runtime IDへ解決する材質カタログです。</param>
        /// <param name="biomeDefinitions">順不同で受け取る18Biome表層パレットです。</param>
        /// <param name="geologyDefinitions">順不同で受け取る8地質パレットです。</param>
        /// <param name="catalog">成功時にPersistent寿命で作成したBlob Assetを受け取ります。</param>
        /// <returns>全26行と全候補が固定契約を満たす場合はtrueです。</returns>
        public static bool TryBuild(
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog,
            BiomeSurfaceBlockPaletteDefinition[] biomeDefinitions,
            GeologyBlockPaletteDefinition[] geologyDefinitions,
            out BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog)
        {
            catalog = default;
            if (!terrainCatalog.IsCreated
                || !materialCatalog.IsCreated
                || biomeDefinitions == null
                || biomeDefinitions.Length != TerrainBlockPaletteDefinitions.BiomePaletteCount
                || geologyDefinitions == null
                || geologyDefinitions.Length != TerrainBlockPaletteDefinitions.GeologyPaletteCount)
            {
                return false;
            }

            // 入力順を実行結果へ影響させないため、安定IDから導いた添字へ各行を正規化します。
            BiomeSurfaceBlockPaletteDefinition[] normalizedBiomes =
                new BiomeSurfaceBlockPaletteDefinition[TerrainBlockPaletteDefinitions.BiomePaletteCount];
            GeologyBlockPaletteDefinition[] normalizedGeologies =
                new GeologyBlockPaletteDefinition[TerrainBlockPaletteDefinitions.GeologyPaletteCount];
            int entryCount = 0;
            for (int definitionIndex = 0;
                 definitionIndex < biomeDefinitions.Length;
                 definitionIndex++)
            {
                BiomeSurfaceBlockPaletteDefinition definition = biomeDefinitions[definitionIndex];
                int stableIndex = definition == null
                    ? -1
                    : (int)definition.BiomeId - BiomeStableIds.First;
                if (stableIndex < 0
                    || stableIndex >= normalizedBiomes.Length
                    || normalizedBiomes[stableIndex] != null
                    || !ValidatePalette(
                        terrainCatalog,
                        materialCatalog,
                        definition.Entries,
                        DefinitionKind.GroundBlock,
                        false))
                {
                    return false;
                }

                normalizedBiomes[stableIndex] = definition;
                entryCount += definition.Entries.Length;
            }

            for (int definitionIndex = 0;
                 definitionIndex < geologyDefinitions.Length;
                 definitionIndex++)
            {
                GeologyBlockPaletteDefinition definition = geologyDefinitions[definitionIndex];
                int stableIndex = definition == null
                    ? -1
                    : (int)definition.GeologyClass - (int)GeologyClass.UnconsolidatedSediment;
                if (stableIndex < 0
                    || stableIndex >= normalizedGeologies.Length
                    || normalizedGeologies[stableIndex] != null
                    || !ValidatePalette(
                        terrainCatalog,
                        materialCatalog,
                        definition.GroundEntries,
                        DefinitionKind.GroundBlock,
                        false)
                    || !ValidatePalette(
                        terrainCatalog,
                        materialCatalog,
                        definition.WallEntries,
                        DefinitionKind.WallBlock,
                        true))
                {
                    return false;
                }

                normalizedGeologies[stableIndex] = definition;
                entryCount += definition.GroundEntries.Length;
                entryCount += definition.WallEntries.Length;
            }

            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref TerrainBlockPaletteCatalogBlob root =
                    ref builder.ConstructRoot<TerrainBlockPaletteCatalogBlob>();
                BlobBuilderArray<BiomeSurfaceBlockPaletteBlob> biomeRows =
                    builder.Allocate(
                        ref root.BiomeSurfacePalettes,
                        normalizedBiomes.Length);
                BlobBuilderArray<GeologyBlockPaletteBlob> geologyRows =
                    builder.Allocate(
                        ref root.GeologyPalettes,
                        normalizedGeologies.Length);
                BlobBuilderArray<TerrainBlockPaletteEntryBlob> entries =
                    builder.Allocate(ref root.Entries, entryCount);

                int destinationIndex = 0;
                for (int biomeIndex = 0; biomeIndex < normalizedBiomes.Length; biomeIndex++)
                {
                    BiomeSurfaceBlockPaletteDefinition definition = normalizedBiomes[biomeIndex];
                    TerrainBlockPaletteRange range = WritePalette(
                        materialCatalog,
                        definition.Entries,
                        false,
                        ref entries,
                        ref destinationIndex);
                    biomeRows[biomeIndex] = new BiomeSurfaceBlockPaletteBlob
                    {
                        BiomeId = definition.BiomeId,
                        SurfaceGround = range
                    };
                }

                for (int geologyIndex = 0;
                     geologyIndex < normalizedGeologies.Length;
                     geologyIndex++)
                {
                    GeologyBlockPaletteDefinition definition =
                        normalizedGeologies[geologyIndex];
                    TerrainBlockPaletteRange groundRange = WritePalette(
                        materialCatalog,
                        definition.GroundEntries,
                        false,
                        ref entries,
                        ref destinationIndex);
                    TerrainBlockPaletteRange wallRange = WritePalette(
                        materialCatalog,
                        definition.WallEntries,
                        true,
                        ref entries,
                        ref destinationIndex);
                    geologyRows[geologyIndex] = new GeologyBlockPaletteBlob
                    {
                        GeologyClass = definition.GeologyClass,
                        ExposedGround = groundRange,
                        NaturalWall = wallRange
                    };
                }

                catalog = builder.CreateBlobAssetReference<TerrainBlockPaletteCatalogBlob>(
                    Allocator.Persistent);
            }

            return true;
        }

        /// <summary>
        /// 1パレットの材質重複、重み合計、解決先Block分類とWall必須属性を確認します。
        /// </summary>
        /// <param name="terrainCatalog">Block分類と属性を検索する地形カタログです。</param>
        /// <param name="materialCatalog">材質IDをBlock IDへ変換する材質カタログです。</param>
        /// <param name="entries">同じ抽選行に属する候補です。</param>
        /// <param name="expectedKind">GroundBlockまたはWallBlockの期待分類です。</param>
        /// <param name="requiresNaturalWallFlags">移動遮断と水密属性を必須にする場合はtrueです。</param>
        /// <returns>候補が1件以上あり、固定契約をすべて満たす場合はtrueです。</returns>
        private static bool ValidatePalette(
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog,
            TerrainBlockPaletteWeight[] entries,
            DefinitionKind expectedKind,
            bool requiresNaturalWallFlags)
        {
            if (entries == null || entries.Length == 0)
            {
                return false;
            }

            // 41材質の連続安定IDを添字にし、行内だけで重複を検出します。
            bool[] seenMaterials = new bool[TerrainBlockMaterialDefinitions.Count];
            int weightTotal = 0;
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                TerrainBlockPaletteWeight entry = entries[entryIndex];
                int materialIndex = (int)entry.MaterialId
                    - (int)TerrainBlockMaterialDefinitions.First;
                if (materialIndex < 0
                    || materialIndex >= seenMaterials.Length
                    || seenMaterials[materialIndex]
                    || entry.Weight == 0
                    || !TryResolveBlock(
                        terrainCatalog,
                        materialCatalog,
                        entry.MaterialId,
                        expectedKind,
                        requiresNaturalWallFlags,
                        out _))
                {
                    return false;
                }

                seenMaterials[materialIndex] = true;
                weightTotal += entry.Weight;
            }

            return weightTotal == TerrainBlockPaletteDefinitions.RequiredWeightTotal;
        }

        /// <summary>
        /// 検証済み入力を平坦化Entry配列へ書き、行が参照する範囲を返します。
        /// </summary>
        /// <param name="materialCatalog">材質IDをBlock IDへ解決するカタログです。</param>
        /// <param name="sourceEntries">事前検証済みの同一行候補です。</param>
        /// <param name="useWallRuntimeId">Wall形態を使う場合はtrue、Ground形態ならfalseです。</param>
        /// <param name="destinationEntries">Blob内の全候補を保持する配列です。</param>
        /// <param name="destinationIndex">次に書き込む位置で、書き込み件数分だけ進めます。</param>
        /// <returns>今回書き込んだ候補の開始位置と件数です。</returns>
        private static TerrainBlockPaletteRange WritePalette(
            BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog,
            TerrainBlockPaletteWeight[] sourceEntries,
            bool useWallRuntimeId,
            ref BlobBuilderArray<TerrainBlockPaletteEntryBlob> destinationEntries,
            ref int destinationIndex)
        {
            int startIndex = destinationIndex;
            for (int sourceIndex = 0; sourceIndex < sourceEntries.Length; sourceIndex++)
            {
                TerrainBlockPaletteWeight source = sourceEntries[sourceIndex];
                // TryBuildの検証を通過した同じ不変カタログを読むため、ここで失敗する経路はありません。
                TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                    materialCatalog,
                    source.MaterialId,
                    out TerrainBlockMaterialDefinitionBlob material);
                destinationEntries[destinationIndex++] = new TerrainBlockPaletteEntryBlob
                {
                    MaterialId = source.MaterialId,
                    BlockRuntimeId = useWallRuntimeId
                        ? material.WallRuntimeId
                        : material.GroundRuntimeId,
                    Weight = source.Weight
                };
            }

            return new TerrainBlockPaletteRange
            {
                StartIndex = startIndex,
                Count = sourceEntries.Length
            };
        }

        /// <summary>
        /// 材質IDを期待するGroundまたはWallへ解決し、自然Wallなら必須属性も確認します。
        /// </summary>
        /// <param name="terrainCatalog">Block分類と属性を検索する地形カタログです。</param>
        /// <param name="materialCatalog">材質IDをGround・Wallへ解決するカタログです。</param>
        /// <param name="materialId">解決する安定材質IDです。</param>
        /// <param name="expectedKind">解決先に要求するDefinition分類です。</param>
        /// <param name="requiresNaturalWallFlags">自然Wallの移動遮断・水密属性を要求する場合はtrueです。</param>
        /// <param name="runtimeId">成功時にGroundまたはWallの解決済み実行時IDを受け取ります。</param>
        /// <returns>材質・地形定義・分類・必須属性がすべて有効ならtrueです。</returns>
        private static bool TryResolveBlock(
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog,
            TerrainBlockMaterialId materialId,
            DefinitionKind expectedKind,
            bool requiresNaturalWallFlags,
            out DefinitionRuntimeId runtimeId)
        {
            runtimeId = default;
            if (!TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                    materialCatalog,
                    materialId,
                    out TerrainBlockMaterialDefinitionBlob material))
            {
                return false;
            }

            DefinitionRuntimeId candidateRuntimeId = expectedKind == DefinitionKind.WallBlock
                ? material.WallRuntimeId
                : material.GroundRuntimeId;
            if (!TerrainBlockCatalogLookup.TryGetDefinition(
                    terrainCatalog,
                    candidateRuntimeId,
                    out TerrainBlockDefinitionBlob block)
                || block.Kind != expectedKind)
            {
                return false;
            }

            if (requiresNaturalWallFlags)
            {
                TerrainBlockDefinitionFlags requiredFlags =
                    TerrainBlockDefinitionFlags.BlocksMovement
                    | TerrainBlockDefinitionFlags.Watertight;
                if ((block.Flags & requiredFlags) != requiredFlags)
                {
                    return false;
                }
            }

            runtimeId = candidateRuntimeId;
            return true;
        }
    }

    /// <summary>
    /// 安定Biome・地質IDから検証済みパレット範囲と候補を取得します。
    /// 範囲外IDや壊れたRangeを拒否し、生成処理がBlob配列を不正に参照しないようにします。
    /// </summary>
    public static class TerrainBlockPaletteCatalogLookup
    {
        /// <summary>
        /// 指定Biomeの表層Groundパレット範囲を取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した固定パレットカタログです。</param>
        /// <param name="biomeId">検索する1〜18のBiome安定IDです。</param>
        /// <param name="range">成功時に平坦化Entry配列内の候補範囲を受け取ります。</param>
        /// <returns>IDと対応行が有効な場合はtrueです。</returns>
        public static bool TryGetBiomeSurfaceRange(
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            BiomeId biomeId,
            out TerrainBlockPaletteRange range)
        {
            range = default;
            if (!catalog.IsCreated)
            {
                return false;
            }

            int stableIndex = (int)biomeId - BiomeStableIds.First;
            ref TerrainBlockPaletteCatalogBlob root = ref catalog.Value;
            if (stableIndex < 0 || stableIndex >= root.BiomeSurfacePalettes.Length)
            {
                return false;
            }

            BiomeSurfaceBlockPaletteBlob row = root.BiomeSurfacePalettes[stableIndex];
            if (row.BiomeId != biomeId || !IsValidRange(root.Entries.Length, row.SurfaceGround))
            {
                return false;
            }

            range = row.SurfaceGround;
            return true;
        }

        /// <summary>
        /// 指定地質の露出基盤Groundまたは自然Wallパレット範囲を取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した固定パレットカタログです。</param>
        /// <param name="geologyClass">検索する1〜8の地質安定IDです。</param>
        /// <param name="wall">自然Wallならtrue、露出基盤Groundならfalseです。</param>
        /// <param name="range">成功時に平坦化Entry配列内の候補範囲を受け取ります。</param>
        /// <returns>ID、用途、対応行が有効な場合はtrueです。</returns>
        public static bool TryGetGeologyRange(
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            GeologyClass geologyClass,
            bool wall,
            out TerrainBlockPaletteRange range)
        {
            range = default;
            if (!catalog.IsCreated)
            {
                return false;
            }

            int stableIndex = (int)geologyClass
                - (int)GeologyClass.UnconsolidatedSediment;
            ref TerrainBlockPaletteCatalogBlob root = ref catalog.Value;
            if (stableIndex < 0 || stableIndex >= root.GeologyPalettes.Length)
            {
                return false;
            }

            GeologyBlockPaletteBlob row = root.GeologyPalettes[stableIndex];
            TerrainBlockPaletteRange candidate = wall ? row.NaturalWall : row.ExposedGround;
            if (row.GeologyClass != geologyClass
                || !IsValidRange(root.Entries.Length, candidate))
            {
                return false;
            }

            range = candidate;
            return true;
        }

        /// <summary>
        /// 取得済みRange内の0始まり位置から、1つの材質候補を安全に取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した固定パレットカタログです。</param>
        /// <param name="range">Biomeまたは地質行から取得した候補範囲です。</param>
        /// <param name="relativeIndex">Range内の0始まり候補位置です。</param>
        /// <param name="entry">成功時に安定材質ID、Block ID、重みを受け取ります。</param>
        /// <returns>カタログ、Range、相対位置がすべて有効な場合はtrueです。</returns>
        public static bool TryGetEntry(
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            TerrainBlockPaletteRange range,
            int relativeIndex,
            out TerrainBlockPaletteEntryBlob entry)
        {
            entry = default;
            if (!catalog.IsCreated || relativeIndex < 0 || relativeIndex >= range.Count)
            {
                return false;
            }

            ref TerrainBlockPaletteCatalogBlob root = ref catalog.Value;
            if (!IsValidRange(root.Entries.Length, range))
            {
                return false;
            }

            entry = root.Entries[range.StartIndex + relativeIndex];
            return true;
        }

        /// <summary>Rangeが平坦化配列の内側にあり、1件以上を指すか確認します。</summary>
        /// <param name="entryCount">平坦化配列の全候補数です。</param>
        /// <param name="range">確認する開始位置と件数です。</param>
        /// <returns>開始位置から件数分が配列内に収まる場合はtrueです。</returns>
        private static bool IsValidRange(int entryCount, TerrainBlockPaletteRange range)
        {
            return range.StartIndex >= 0
                && range.Count > 0
                && range.StartIndex <= entryCount - range.Count;
        }
    }
}
