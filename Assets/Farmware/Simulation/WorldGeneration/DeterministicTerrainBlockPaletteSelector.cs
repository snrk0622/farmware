// このファイルは、Biome表層、地質由来Ground・自然Wall、露出基盤を決定論的に選びます。
// B2の固定パレットBlobと既存の環境・地質・水文値を読み、後続のチャンク生成へBlock参照だけを返す純粋計算です。

using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// Blockパレット選択に使う連続ノイズ寸法、整数比重、固定境界を保持します。
    /// WorldGenerationSettingsへ値として含め、GeneratorVersionと一緒に保存・再生成の対象とします。
    /// </summary>
    public struct TerrainBlockPaletteSelectionSettings
    {
        /// <summary>表層パッチの大域ノイズ格子サイズです。単位はタイルです。</summary>
        public int SurfaceLargeCellSizeInTiles;

        /// <summary>表層パッチの中域ノイズ格子サイズです。単位はタイルです。</summary>
        public int SurfaceMediumCellSizeInTiles;

        /// <summary>表層パッチの細部ノイズ格子サイズです。単位はタイルです。</summary>
        public int SurfaceSmallCellSizeInTiles;

        /// <summary>表層パッチの64タイルノイズへ掛ける整数比重です。</summary>
        public byte SurfaceLargeWeight;

        /// <summary>表層パッチの32タイルノイズへ掛ける整数比重です。</summary>
        public byte SurfaceMediumWeight;

        /// <summary>表層パッチの16タイルノイズへ掛ける整数比重です。</summary>
        public byte SurfaceSmallWeight;

        /// <summary>地質材質差分の大域ノイズ格子サイズです。単位はタイルです。</summary>
        public int GeologyLargeCellSizeInTiles;

        /// <summary>地質材質差分の細部ノイズ格子サイズです。単位はタイルです。</summary>
        public int GeologySmallCellSizeInTiles;

        /// <summary>地質材質差分の32タイルノイズへ掛ける整数比重です。</summary>
        public byte GeologyLargeWeight;

        /// <summary>地質材質差分の16タイルノイズへ掛ける整数比重です。</summary>
        public byte GeologySmallWeight;

        /// <summary>露出基盤パッチの大域ノイズ格子サイズです。単位はタイルです。</summary>
        public int ExposureLargeCellSizeInTiles;

        /// <summary>露出基盤パッチの細部ノイズ格子サイズです。単位はタイルです。</summary>
        public int ExposureSmallCellSizeInTiles;

        /// <summary>露出基盤パッチの32タイルノイズへ掛ける整数比重です。</summary>
        public byte ExposureLargeWeight;

        /// <summary>露出基盤パッチの16タイルノイズへ掛ける整数比重です。</summary>
        public byte ExposureSmallWeight;

        /// <summary>露出率が0から増え始める0〜65,535の露出ポテンシャル境界です。</summary>
        public ushort BedrockExposureThreshold;

        /// <summary>PeatSoilを維持できる0〜65,535の自然土壌飽和ポテンシャル下限です。</summary>
        public ushort PeatSoilSaturationThreshold;

        /// <summary>
        /// 確定仕様のパッチ寸法、整数比重、露出・泥炭境界を作ります。
        /// </summary>
        /// <returns>GeneratorVersion 2で使用するBlockパレット固定設定です。</returns>
        public static TerrainBlockPaletteSelectionSettings CreateDefault()
        {
            return new TerrainBlockPaletteSelectionSettings
            {
                SurfaceLargeCellSizeInTiles = 64,
                SurfaceMediumCellSizeInTiles = 32,
                SurfaceSmallCellSizeInTiles = 16,
                SurfaceLargeWeight = 4,
                SurfaceMediumWeight = 2,
                SurfaceSmallWeight = 1,
                GeologyLargeCellSizeInTiles = 32,
                GeologySmallCellSizeInTiles = 16,
                GeologyLargeWeight = 2,
                GeologySmallWeight = 1,
                ExposureLargeCellSizeInTiles = 32,
                ExposureSmallCellSizeInTiles = 16,
                ExposureLargeWeight = 2,
                ExposureSmallWeight = 1,
                BedrockExposureThreshold = 39321,
                PeatSoilSaturationThreshold = 39321
            };
        }
    }

    /// <summary>
    /// 1タイルのBlockパレット選択途中値と最終結果を保持します。
    /// C工程のECS書込みと開発者用診断表示が同じ純粋結果を読み、別々に式を再実装しないために使用します。
    /// </summary>
    public struct TerrainBlockPaletteSelectionResult
    {
        /// <summary>泥炭条件を適用する前に表層パレットから選ばれた候補です。</summary>
        public TerrainBlockPaletteEntryBlob SurfaceGroundCandidate;

        /// <summary>泥炭条件を適用した後に表層として使用できるGround候補です。</summary>
        public TerrainBlockPaletteEntryBlob SurfaceGround;

        /// <summary>地質パレットから選ばれた露出基盤Ground候補です。</summary>
        public TerrainBlockPaletteEntryBlob ExposedGround;

        /// <summary>地質パレットから常に選ばれる自然Wall候補です。</summary>
        public TerrainBlockPaletteEntryBlob NaturalWall;

        /// <summary>露出判定後に地形セルへ設定する最終Ground候補です。</summary>
        public TerrainBlockPaletteEntryBlob FinalGround;

        /// <summary>表層用64・32・16タイルノイズを4：2：1で合成した0〜65,535の値です。</summary>
        public ushort SurfacePatchNoise;

        /// <summary>地質用32・16タイルノイズを2：1で合成した0〜65,535の値です。</summary>
        public ushort GeologyVariantNoise;

        /// <summary>露出用32・16タイルノイズを2：1で合成した0〜65,535の値です。</summary>
        public ushort BedrockExposurePatchNoise;

        /// <summary>起伏、侵食抵抗、乾燥度を2：1：1で合成した0〜65,535の値です。</summary>
        public ushort BedrockExposurePotential;

        /// <summary>露出境界以上のポテンシャルを0〜65,535へ広げた露出率です。</summary>
        public ushort BedrockExposureRate;

        /// <summary>泥炭候補を同じBiome行の最初の非泥炭材質へ置換した場合にtrueです。</summary>
        public bool ReplacedUnsaturatedPeat;

        /// <summary>最終Groundとして表層ではなく露出基盤を選んだ場合にtrueです。</summary>
        public bool UsesExposedGround;
    }

    /// <summary>
    /// Seed、GeneratorVersion、固定設定、絶対座標、既存生成値だけからBlockパレットを選ぶ純粋計算です。
    /// Burst Jobから呼べるよう、管理参照、例外、UnityEngine API、可変乱数状態を使用しません。
    /// </summary>
    public static class DeterministicTerrainBlockPaletteSelector
    {
        /// <summary>重み合計256のパレットへ割り当てる、0〜255の整数区間数です。</summary>
        private const int PaletteWeightTotal = TerrainBlockPaletteDefinitions.RequiredWeightTotal;

        /// <summary>露出境界から最大ポテンシャルまでの幅です。</summary>
        private const uint BedrockExposurePotentialRange = 26214;

        /// <summary>
        /// Blockパレット設定が確定仕様の寸法、比重、境界と一致するか検証します。
        /// 暗黙の補正をせず、設定の欠落や異なるGeneratorVersion用値の混入を生成前に拒否します。
        /// </summary>
        /// <param name="settings">検証するBlockパレット固定設定です。</param>
        /// <returns>確定したB3契約をすべて満たす場合はtrueです。</returns>
        public static bool ValidateSettings(in TerrainBlockPaletteSelectionSettings settings)
        {
            return settings.SurfaceLargeCellSizeInTiles == 64
                && settings.SurfaceMediumCellSizeInTiles == 32
                && settings.SurfaceSmallCellSizeInTiles == 16
                && settings.SurfaceLargeWeight == 4
                && settings.SurfaceMediumWeight == 2
                && settings.SurfaceSmallWeight == 1
                && settings.GeologyLargeCellSizeInTiles == 32
                && settings.GeologySmallCellSizeInTiles == 16
                && settings.GeologyLargeWeight == 2
                && settings.GeologySmallWeight == 1
                && settings.ExposureLargeCellSizeInTiles == 32
                && settings.ExposureSmallCellSizeInTiles == 16
                && settings.ExposureLargeWeight == 2
                && settings.ExposureSmallWeight == 1
                && settings.BedrockExposureThreshold == 39321
                && settings.PeatSoilSaturationThreshold == 39321;
        }

        /// <summary>
        /// 1タイルの表層、露出基盤、自然Wall、最終Groundをまとめて選びます。
        /// </summary>
        /// <param name="settings">Seed、版、Blockパレット固定設定を持つワールド生成設定です。</param>
        /// <param name="catalog">B2で構築済みの18Biome・8地質パレットBlobです。</param>
        /// <param name="worldTileX">地質差分と露出パッチを読む水平絶対タイル座標です。</param>
        /// <param name="worldTileY">地質差分と露出パッチを読む垂直絶対タイル座標です。</param>
        /// <param name="warpedSampleX">表層パッチを読むBiome Warp後の水平座標です。</param>
        /// <param name="warpedSampleY">表層パッチを読むBiome Warp後の垂直座標です。</param>
        /// <param name="biomeId">表層パレット行を選ぶ1〜18の安定Biome IDです。</param>
        /// <param name="geologyClass">露出Ground・自然Wall行を選ぶ1〜8の卓越地質IDです。</param>
        /// <param name="baseMoisture">現在の水面と分離した0〜65,535の基礎湿度です。</param>
        /// <param name="terrainRuggedness">固定標高サンプルから求めた0〜65,535の地形起伏です。</param>
        /// <param name="erosionResistance">卓越地質Definitionの0〜65,535の侵食抵抗です。</param>
        /// <param name="naturalSoilSaturationPotential">自然土壌飽和の0〜65,535ポテンシャルです。</param>
        /// <param name="result">成功時に途中ノイズ、候補、最終Block選択を受け取ります。</param>
        /// <returns>設定、ID、カタログ範囲、全候補が有効で選択を完了した場合はtrueです。</returns>
        public static bool TrySelect(
            in WorldGenerationSettings settings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            int worldTileX,
            int worldTileY,
            int warpedSampleX,
            int warpedSampleY,
            BiomeId biomeId,
            GeologyClass geologyClass,
            ushort baseMoisture,
            ushort terrainRuggedness,
            ushort erosionResistance,
            ushort naturalSoilSaturationPotential,
            out TerrainBlockPaletteSelectionResult result)
        {
            result = default;
            TerrainBlockPaletteSelectionSettings selectionSettings =
                settings.TerrainBlockPaletteSelection;
            if (settings.GeneratorVersion != WorldGenerationSettings.CurrentGeneratorVersion
                || !ValidateSettings(selectionSettings)
                || !TerrainBlockPaletteCatalogLookup.TryGetBiomeSurfaceRange(
                    catalog,
                    biomeId,
                    out TerrainBlockPaletteRange surfaceRange)
                || !TerrainBlockPaletteCatalogLookup.TryGetGeologyRange(
                    catalog,
                    geologyClass,
                    false,
                    out TerrainBlockPaletteRange exposedGroundRange)
                || !TerrainBlockPaletteCatalogLookup.TryGetGeologyRange(
                    catalog,
                    geologyClass,
                    true,
                    out TerrainBlockPaletteRange naturalWallRange))
            {
                return false;
            }

            ushort surfaceNoise = SampleSurfacePatch(
                settings,
                selectionSettings,
                warpedSampleX,
                warpedSampleY);
            ushort geologyNoise = SampleGeologyVariant(
                settings,
                selectionSettings,
                worldTileX,
                worldTileY);
            ushort exposureNoise = SampleExposurePatch(
                settings,
                selectionSettings,
                worldTileX,
                worldTileY);

            if (!TrySelectWeightedEntry(catalog, surfaceRange, surfaceNoise, out TerrainBlockPaletteEntryBlob surfaceCandidate)
                || !TrySelectWeightedEntry(catalog, exposedGroundRange, geologyNoise, out TerrainBlockPaletteEntryBlob exposedGround)
                || !TrySelectWeightedEntry(catalog, naturalWallRange, geologyNoise, out TerrainBlockPaletteEntryBlob naturalWall))
            {
                return false;
            }

            TerrainBlockPaletteEntryBlob surfaceGround = surfaceCandidate;
            bool replacedUnsaturatedPeat = false;
            if (surfaceCandidate.MaterialId == TerrainBlockMaterialId.PeatSoil
                && naturalSoilSaturationPotential < selectionSettings.PeatSoilSaturationThreshold)
            {
                // SwampForestは先頭候補もPeatSoilです。乾燥泥炭を残さないため、行内を表の順に走査し、
                // 最初の非泥炭材質へ置換します。候補の登録順ではなく、B2で固定したパレット表順を使います。
                if (!TryGetFirstNonPeatEntry(catalog, surfaceRange, out surfaceGround))
                {
                    return false;
                }

                replacedUnsaturatedPeat = true;
            }

            ushort exposurePotential = CalculateBedrockExposurePotential(
                baseMoisture,
                terrainRuggedness,
                erosionResistance);
            ushort exposureRate = CalculateBedrockExposureRate(
                exposurePotential,
                selectionSettings.BedrockExposureThreshold);
            bool usesExposedGround = ShouldUseExposedGround(exposureRate, exposureNoise);

            result = new TerrainBlockPaletteSelectionResult
            {
                SurfaceGroundCandidate = surfaceCandidate,
                SurfaceGround = surfaceGround,
                ExposedGround = exposedGround,
                NaturalWall = naturalWall,
                FinalGround = usesExposedGround ? exposedGround : surfaceGround,
                SurfacePatchNoise = surfaceNoise,
                GeologyVariantNoise = geologyNoise,
                BedrockExposurePatchNoise = exposureNoise,
                BedrockExposurePotential = exposurePotential,
                BedrockExposureRate = exposureRate,
                ReplacedUnsaturatedPeat = replacedUnsaturatedPeat,
                UsesExposedGround = usesExposedGround
            };
            return true;
        }

        /// <summary>
        /// 基礎湿度、起伏、侵食抵抗から露出基盤ポテンシャルを2：1：1で求めます。
        /// </summary>
        /// <param name="baseMoisture">0〜65,535の基礎湿度です。</param>
        /// <param name="terrainRuggedness">0〜65,535の地形起伏です。</param>
        /// <param name="erosionResistance">0〜65,535の侵食抵抗です。</param>
        /// <returns>切り捨て除算後の0〜65,535の露出ポテンシャルです。</returns>
        public static ushort CalculateBedrockExposurePotential(
            ushort baseMoisture,
            ushort terrainRuggedness,
            ushort erosionResistance)
        {
            uint dryness = (uint)ushort.MaxValue - baseMoisture;
            // 入力を64bitへ上げてから合成し、将来型幅を変更しても乗算途中の周回を起こしません。
            ulong weightedTotal = ((ulong)terrainRuggedness * 2UL)
                + erosionResistance
                + dryness;
            return (ushort)(weightedTotal / 4UL);
        }

        /// <summary>
        /// 露出ポテンシャルを、固定境界以上だけ0〜65,535の露出率へ写像します。
        /// </summary>
        /// <param name="exposurePotential">0〜65,535の露出基盤ポテンシャルです。</param>
        /// <param name="exposureThreshold">露出率0に対応する固定境界です。</param>
        /// <returns>境界未満と境界ちょうどでは0、最大ポテンシャルでは65,535です。</returns>
        public static ushort CalculateBedrockExposureRate(
            ushort exposurePotential,
            ushort exposureThreshold)
        {
            if (exposurePotential <= exposureThreshold)
            {
                return 0;
            }

            ulong distanceAboveThreshold = (uint)exposurePotential - exposureThreshold;
            ulong scaledRate = distanceAboveThreshold * ushort.MaxValue;
            return (ushort)(scaledRate / BedrockExposurePotentialRange);
        }

        /// <summary>
        /// 露出率の端点を明示処理し、中間値だけ専用ノイズとの未満比較を行います。
        /// </summary>
        /// <param name="exposureRate">0〜65,535の露出率です。</param>
        /// <param name="exposurePatchNoise">0〜65,535の露出パッチノイズです。</param>
        /// <returns>このタイルで表層を露出基盤へ置換する場合はtrueです。</returns>
        public static bool ShouldUseExposedGround(
            ushort exposureRate,
            ushort exposurePatchNoise)
        {
            if (exposureRate == 0)
            {
                return false;
            }

            if (exposureRate == ushort.MaxValue)
            {
                return true;
            }

            return exposurePatchNoise < exposureRate;
        }

        /// <summary>
        /// 0〜65,535の連続ノイズを0〜255へ変換し、重み合計256の累積区間から候補を選びます。
        /// </summary>
        /// <param name="catalog">B2で検証済みのBlockパレットBlobです。</param>
        /// <param name="range">選択対象のBiomeまたは地質パレット範囲です。</param>
        /// <param name="patchNoise">用途別の0〜65,535連続パッチノイズです。</param>
        /// <param name="entry">成功時に累積重み区間へ対応する候補を受け取ります。</param>
        /// <returns>Range内が正の重み合計256で、0〜255の区間を選択できた場合はtrueです。</returns>
        public static bool TrySelectWeightedEntry(
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            TerrainBlockPaletteRange range,
            ushort patchNoise,
            out TerrainBlockPaletteEntryBlob entry)
        {
            entry = default;
            int selector = patchNoise >> 8;
            int cumulativeWeight = 0;
            bool selected = false;

            // 候補数は最大4件です。全件を最後まで確認し、壊れた重み合計を偶然選択成功として通しません。
            for (int relativeIndex = 0; relativeIndex < range.Count; relativeIndex++)
            {
                if (!TerrainBlockPaletteCatalogLookup.TryGetEntry(
                        catalog,
                        range,
                        relativeIndex,
                        out TerrainBlockPaletteEntryBlob candidate)
                    || candidate.Weight == 0)
                {
                    return false;
                }

                cumulativeWeight += candidate.Weight;
                if (!selected && selector < cumulativeWeight)
                {
                    entry = candidate;
                    selected = true;
                }
            }

            return selected && cumulativeWeight == PaletteWeightTotal;
        }

        /// <summary>表層専用の64・32・16タイルノイズを4：2：1で合成します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つワールド生成設定です。</param>
        /// <param name="selectionSettings">表層ノイズの寸法と比重です。</param>
        /// <param name="warpedSampleX">Biome Warp後の水平座標です。</param>
        /// <param name="warpedSampleY">Biome Warp後の垂直座標です。</param>
        /// <returns>0〜65,535の合成済み表層パッチノイズです。</returns>
        private static ushort SampleSurfacePatch(
            in WorldGenerationSettings settings,
            in TerrainBlockPaletteSelectionSettings selectionSettings,
            int warpedSampleX,
            int warpedSampleY)
        {
            return SampleThreeScaleNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                warpedSampleX,
                warpedSampleY,
                selectionSettings.SurfaceLargeCellSizeInTiles,
                selectionSettings.SurfaceMediumCellSizeInTiles,
                selectionSettings.SurfaceSmallCellSizeInTiles,
                selectionSettings.SurfaceLargeWeight,
                selectionSettings.SurfaceMediumWeight,
                selectionSettings.SurfaceSmallWeight,
                WorldGenerationPurposeSalts.SurfaceBlockPatch);
        }

        /// <summary>地質専用の32・16タイルノイズを2：1で合成します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つワールド生成設定です。</param>
        /// <param name="selectionSettings">地質ノイズの寸法と比重です。</param>
        /// <param name="worldTileX">水平絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直絶対タイル座標です。</param>
        /// <returns>0〜65,535の合成済み地質差分ノイズです。</returns>
        private static ushort SampleGeologyVariant(
            in WorldGenerationSettings settings,
            in TerrainBlockPaletteSelectionSettings selectionSettings,
            int worldTileX,
            int worldTileY)
        {
            return SampleTwoScaleNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                selectionSettings.GeologyLargeCellSizeInTiles,
                selectionSettings.GeologySmallCellSizeInTiles,
                selectionSettings.GeologyLargeWeight,
                selectionSettings.GeologySmallWeight,
                WorldGenerationPurposeSalts.GeologyBlockVariant);
        }

        /// <summary>露出専用の32・16タイルノイズを2：1で合成します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つワールド生成設定です。</param>
        /// <param name="selectionSettings">露出ノイズの寸法と比重です。</param>
        /// <param name="worldTileX">水平絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直絶対タイル座標です。</param>
        /// <returns>0〜65,535の合成済み露出パッチノイズです。</returns>
        private static ushort SampleExposurePatch(
            in WorldGenerationSettings settings,
            in TerrainBlockPaletteSelectionSettings selectionSettings,
            int worldTileX,
            int worldTileY)
        {
            return SampleTwoScaleNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                selectionSettings.ExposureLargeCellSizeInTiles,
                selectionSettings.ExposureSmallCellSizeInTiles,
                selectionSettings.ExposureLargeWeight,
                selectionSettings.ExposureSmallWeight,
                WorldGenerationPurposeSalts.BedrockExposurePatch);
        }

        /// <summary>3解像度の独立した値ノイズを指定比重で整数平均します。</summary>
        /// <param name="worldSeed">ワールドの安定64bitシードです。</param>
        /// <param name="generatorVersion">生成器バージョンです。</param>
        /// <param name="worldTileX">サンプリングする水平整数座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直整数座標です。</param>
        /// <param name="largeCellSize">大域格子サイズです。単位はタイルです。</param>
        /// <param name="mediumCellSize">中域格子サイズです。単位はタイルです。</param>
        /// <param name="smallCellSize">細部格子サイズです。単位はタイルです。</param>
        /// <param name="largeWeight">大域ノイズの整数比重です。</param>
        /// <param name="mediumWeight">中域ノイズの整数比重です。</param>
        /// <param name="smallWeight">細部ノイズの整数比重です。</param>
        /// <param name="purposeSalt">用途を他の生成処理から分離する固定ソルトです。</param>
        /// <returns>切り捨て平均後の0〜65,535の値です。</returns>
        private static ushort SampleThreeScaleNoise(
            ulong worldSeed,
            int generatorVersion,
            int worldTileX,
            int worldTileY,
            int largeCellSize,
            int mediumCellSize,
            int smallCellSize,
            byte largeWeight,
            byte mediumWeight,
            byte smallWeight,
            ulong purposeSalt)
        {
            ushort large = SampleScale(
                worldSeed,
                generatorVersion,
                worldTileX,
                worldTileY,
                largeCellSize,
                purposeSalt,
                0);
            ushort medium = SampleScale(
                worldSeed,
                generatorVersion,
                worldTileX,
                worldTileY,
                mediumCellSize,
                purposeSalt,
                1);
            ushort small = SampleScale(
                worldSeed,
                generatorVersion,
                worldTileX,
                worldTileY,
                smallCellSize,
                purposeSalt,
                2);
            ulong total = ((ulong)large * largeWeight)
                + ((ulong)medium * mediumWeight)
                + ((ulong)small * smallWeight);
            uint weightTotal = (uint)largeWeight + mediumWeight + smallWeight;
            return (ushort)(total / weightTotal);
        }

        /// <summary>2解像度の独立した値ノイズを指定比重で整数平均します。</summary>
        /// <param name="worldSeed">ワールドの安定64bitシードです。</param>
        /// <param name="generatorVersion">生成器バージョンです。</param>
        /// <param name="worldTileX">サンプリングする水平整数座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直整数座標です。</param>
        /// <param name="largeCellSize">大域格子サイズです。単位はタイルです。</param>
        /// <param name="smallCellSize">細部格子サイズです。単位はタイルです。</param>
        /// <param name="largeWeight">大域ノイズの整数比重です。</param>
        /// <param name="smallWeight">細部ノイズの整数比重です。</param>
        /// <param name="purposeSalt">用途を他の生成処理から分離する固定ソルトです。</param>
        /// <returns>切り捨て平均後の0〜65,535の値です。</returns>
        private static ushort SampleTwoScaleNoise(
            ulong worldSeed,
            int generatorVersion,
            int worldTileX,
            int worldTileY,
            int largeCellSize,
            int smallCellSize,
            byte largeWeight,
            byte smallWeight,
            ulong purposeSalt)
        {
            ushort large = SampleScale(
                worldSeed,
                generatorVersion,
                worldTileX,
                worldTileY,
                largeCellSize,
                purposeSalt,
                0);
            ushort small = SampleScale(
                worldSeed,
                generatorVersion,
                worldTileX,
                worldTileY,
                smallCellSize,
                purposeSalt,
                1);
            ulong total = ((ulong)large * largeWeight) + ((ulong)small * smallWeight);
            uint weightTotal = (uint)largeWeight + smallWeight;
            return (ushort)(total / weightTotal);
        }

        /// <summary>1解像度を用途Saltと固定番号で分離し、連続値ノイズを読みます。</summary>
        /// <param name="worldSeed">ワールドの安定64bitシードです。</param>
        /// <param name="generatorVersion">生成器バージョンです。</param>
        /// <param name="worldTileX">サンプリングする水平整数座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直整数座標です。</param>
        /// <param name="cellSizeInTiles">格子1辺のタイル数です。</param>
        /// <param name="purposeSalt">用途専用の固定ソルトです。</param>
        /// <param name="scaleIndex">同じ用途内で解像度を分離する0始まり番号です。</param>
        /// <returns>0〜65,535の連続値ノイズです。</returns>
        private static ushort SampleScale(
            ulong worldSeed,
            int generatorVersion,
            int worldTileX,
            int worldTileY,
            int cellSizeInTiles,
            ulong purposeSalt,
            uint scaleIndex)
        {
            ulong scaleSalt = WorldSeedUtility.Mix64(purposeSalt ^ scaleIndex);
            return DeterministicNoise.SampleValueNoise(
                worldSeed,
                generatorVersion,
                worldTileX,
                worldTileY,
                cellSizeInTiles,
                scaleSalt);
        }

        /// <summary>Biome行を固定表順に走査し、最初の非PeatSoil Ground候補を取得します。</summary>
        /// <param name="catalog">B2で検証済みのBlockパレットBlobです。</param>
        /// <param name="range">対象Biomeの表層候補範囲です。</param>
        /// <param name="entry">成功時に乾燥時の代替Ground候補を受け取ります。</param>
        /// <returns>Rangeが有効で非泥炭候補を1件以上含む場合はtrueです。</returns>
        private static bool TryGetFirstNonPeatEntry(
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> catalog,
            TerrainBlockPaletteRange range,
            out TerrainBlockPaletteEntryBlob entry)
        {
            entry = default;
            // B2の固定表順はゲーム仕様の優先順です。行の登録順やRuntime ID順へ並べ替えません。
            for (int relativeIndex = 0; relativeIndex < range.Count; relativeIndex++)
            {
                if (!TerrainBlockPaletteCatalogLookup.TryGetEntry(
                    catalog,
                    range,
                    relativeIndex,
                    out TerrainBlockPaletteEntryBlob candidate))
                {
                    return false;
                }

                if (candidate.MaterialId != TerrainBlockMaterialId.PeatSoil)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
