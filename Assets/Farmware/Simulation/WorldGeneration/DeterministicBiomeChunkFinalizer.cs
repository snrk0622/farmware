// このファイルは、完成した固定水文と基礎地形を合成し、新18Biomeを確定します。
// Block、Liquid、Surfaceの確定はDeterministicTerrainCellFinalizerへ分離し、旧地形分類を再生成しません。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// C1〜C4で生成済みの環境・地質・固定水文から、1タイルの新Biomeだけを確定します。
    /// 物理地形は別BufferのGeneratedTerrainCellを正とし、この型は環境・地質値を維持します。
    /// </summary>
    public static class DeterministicBiomeChunkFinalizer
    {
        /// <summary>
        /// 完成済み地形と自然水文から、製品最終化と同じBiome選択理由・上位候補を診断用に再取得します。
        /// ECSへ診断専用Componentを常駐させず、Map ViewerとGameビュー中央1タイルだけが必要時に呼びます。
        /// </summary>
        /// <param name="worldSettings">Seed、GeneratorVersion、海面を含む製品生成設定です。</param>
        /// <param name="biomeSettings">製品Jobが使用する検証済み18Biome Definitionです。</param>
        /// <param name="worldTileX">診断する符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">診断する符号付きワールド垂直タイル座標です。</param>
        /// <param name="finalizedTile">C5-CでBiome確定済みの地形タイルです。</param>
        /// <param name="hydrologyTile">同じ座標のC4自然水文タイルです。</param>
        /// <param name="selection">成功時に最終理由、通常首位、次点、候補数、軸適性を返します。</param>
        /// <returns>地上タイルを同じ入力で再評価でき、保存済みBiomeと一致した場合はtrueです。</returns>
        public static bool TryGetBiomeSelectionDiagnostics(
            in WorldGenerationSettings worldSettings,
            in BiomeClassificationSettings biomeSettings,
            int worldTileX,
            int worldTileY,
            in GeneratedTile finalizedTile,
            in GeneratedHydrologyTile hydrologyTile,
            out FinalBiomeSelection selection)
        {
            selection = default;
            if (!DeterministicEnvironmentGenerator.TryGenerate(
                    worldSettings,
                    worldTileX,
                    worldTileY,
                    out GeneratedEnvironmentData environment)
                || environment.Elevation < worldSettings.SeaLevelThreshold)
            {
                // 地上18Biomeを持たない海域は診断対象外です。Noneを通常候補として偽装しません。
                return false;
            }

            BiomeClassificationInput input = new BiomeClassificationInput
            {
                Environment = environment,
                GeologyClass = finalizedTile.GeologyClass,
                GeologyFlags = finalizedTile.GeologyFlags,
                SeaLevelThreshold = worldSettings.SeaLevelThreshold,
                HydrologyFlags = hydrologyTile.Flags
            };
            if (!DeterministicBiomeClassifier.TrySelectFinalBiome(
                    biomeSettings,
                    worldSettings.WorldSeed,
                    worldSettings.GeneratorVersion,
                    input,
                    out selection))
            {
                return false;
            }

            // 診断の再計算が製品Bufferと食い違う場合、もっともらしい別理由を表示せず不一致として失敗させます。
            return selection.StableId == (byte)finalizedTile.Biome;
        }

        /// <summary>
        /// 1タイルの環境を再現し、特殊優先を含むBiome分類を適用します。
        /// </summary>
        /// <param name="worldSettings">Seed、GeneratorVersion、海面の固定設定です。</param>
        /// <param name="biomeSettings">検証済みの新18Biome Definitionです。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="baseTile">先行Jobが生成した環境・地質情報です。</param>
        /// <param name="hydrologyTile">同じ座標で完成した自然水文と湿原規模です。</param>
        /// <param name="surfaceWaterTile">同じ座標で完成した固定表流水です。呼び出し契約をそろえるため受け取り、Block確定側で使用します。</param>
        /// <param name="finalizedTile">成功時に新Biomeを反映し、環境・地質値を維持したタイルを返します。</param>
        /// <returns>環境再計算と新Biome分類を完全に終了できた場合はtrueです。失敗時はdefaultを返します。</returns>
        public static bool TryFinalizeTile(
            in WorldGenerationSettings worldSettings,
            in BiomeClassificationSettings biomeSettings,
            int worldTileX,
            int worldTileY,
            in GeneratedTile baseTile,
            in GeneratedHydrologyTile hydrologyTile,
            in GeneratedSurfaceWaterTile surfaceWaterTile,
            out GeneratedTile finalizedTile)
        {
            finalizedTile = default;
            if (!DeterministicEnvironmentGenerator.TryGenerate(
                    worldSettings,
                    worldTileX,
                    worldTileY,
                    out GeneratedEnvironmentData environment))
            {
                // BiomeVariationが使うWarp後座標も同じ純粋生成結果に含まれるため、部分的な入力で分類を推測しません。
                return false;
            }

            if (environment.Elevation < worldSettings.SeaLevelThreshold)
            {
                // 18種は地上Biomeであり、海中セルを候補なしの分類失敗にしません。
                // 海域はBiomeId.Noneを明示し、後続の水域生息地分類が地上Biomeを偽装せず扱えるようにします。
                finalizedTile = baseTile;
                finalizedTile.Biome = BiomeId.None;
                finalizedTile.ResourceSuitability = ResourceSuitability.None;
                return true;
            }

            BiomeClassificationInput input = new BiomeClassificationInput
            {
                Environment = environment,
                GeologyClass = baseTile.GeologyClass,
                GeologyFlags = baseTile.GeologyFlags,
                SeaLevelThreshold = worldSettings.SeaLevelThreshold,
                HydrologyFlags = hydrologyTile.Flags
            };
            if (!DeterministicBiomeClassifier.TrySelectFinalBiome(
                    biomeSettings,
                    worldSettings.WorldSeed,
                    worldSettings.GeneratorVersion,
                    input,
                    out FinalBiomeSelection selection)
                || selection.StableId < BiomeStableIds.First
                || selection.StableId > BiomeStableIds.Last)
            {
                // 候補なしや未定義IDをNoneまたは温帯草原へ読み替えず、チャンク全体の公開を失敗させます。
                return false;
            }

            finalizedTile = baseTile;
            finalizedTile.Biome = (BiomeId)selection.StableId;
            finalizedTile.ResourceSuitability = ResourceSuitability.None;
            return true;
        }

        /// <summary>
        /// 確定Biomeと火山活動度が、自然露出マグマを置ける活動中核かを判定します。
        /// </summary>
        /// <param name="settings">段階5より厳しく絞る場合の追加活動度下限を持つ生成設定です。</param>
        /// <param name="biome">新分類器が確定したBiomeです。</param>
        /// <param name="volcanicActivityPotential">対象タイルの0〜65,535の火山活動度です。</param>
        /// <returns>火山・地熱地帯で、段階5と追加下限の両方を満たす場合はtrueです。</returns>
        public static bool IsMagmaActivityEligible(
            in WorldGenerationSettings settings,
            BiomeId biome,
            ushort volcanicActivityPotential)
        {
            if (biome != BiomeId.VolcanicGeothermal
                || volcanicActivityPotential
                    < DeterministicEnvironmentGenerator.EnvironmentBand5Threshold)
            {
                // 段階4は通常比較で選ばれる火山周縁なので、同じBiome IDでも露出マグマを許可しません。
                return false;
            }

            // Inspectorの追加下限は活動中核をさらに狭める用途だけに使い、段階5の必須条件を緩和しません。
            return volcanicActivityPotential >= settings.MagmaMinimumVolcanicInfluence;
        }

        /// <summary>
        /// 活動中核の条件と専用形状ノイズを両方評価し、自然露出マグマ候補かを判定します。
        /// 製品生成、開発者用色面、中央HUDが同じ条件を共有するための純粋計算です。
        /// </summary>
        /// <param name="settings">活動度の追加下限とマグマ形状閾値を持つ生成設定です。</param>
        /// <param name="biome">新分類器が確定したBiomeです。</param>
        /// <param name="volcanicActivityPotential">対象タイルの0〜65,535の火山活動度です。</param>
        /// <param name="worldTileX">形状ノイズを読む符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">形状ノイズを読む符号付きワールド垂直タイル座標です。</param>
        /// <returns>活動中核かつ形状密度が閾値以上の場合はtrueです。</returns>
        public static bool IsMagmaCandidate(
            in WorldGenerationSettings settings,
            BiomeId biome,
            ushort volcanicActivityPotential,
            int worldTileX,
            int worldTileY)
        {
            return IsMagmaActivityEligible(settings, biome, volcanicActivityPotential)
                && DeterministicMagmaGenerator.SampleDensity(settings, worldTileX, worldTileY)
                    >= settings.MagmaNoiseThreshold;
        }

    }
}
