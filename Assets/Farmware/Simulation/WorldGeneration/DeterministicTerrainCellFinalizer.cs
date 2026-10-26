// このファイルは、完成したBiome・地質・固定水文からGroundBlock、WallBlock、Liquidを直接確定します。
// Blockパレット選択と有効地形差分の間を結び、旧GroundTypeやWaterBodyTypeを経由しない自然生成境界を提供します。

using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1タイルの完成済み固定生成値を、変更差分の基準となるGeneratedTerrainCellへ変換します。
    /// Chunk生成Jobと純粋計算テストが同じ入口を使用し、表示用分類からBlockを逆算しないようにします。
    /// </summary>
    public static class DeterministicTerrainCellFinalizer
    {
        /// <summary>
        /// Surfaceレイヤーの海、マグマ、固定表流水、自然Wall、通常Groundを優先順に評価します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、海面、マグマ、Blockパレット設定を持つ固定設定です。</param>
        /// <param name="paletteCatalog">18Biomeと8地質の検証済みBlockパレットです。</param>
        /// <param name="runtimeIds">共通Definitionから解決済みの淡水・海水・マグマIDを含む組み込みIDです。</param>
        /// <param name="worldTileX">生成対象の符号付き水平絶対タイル座標です。</param>
        /// <param name="worldTileY">生成対象の符号付き垂直絶対タイル座標です。</param>
        /// <param name="finalizedTile">同じ座標でBiome確定済みの環境・地質タイルです。旧地形分類は読みません。</param>
        /// <param name="hydrologyTile">同じ座標の自然土壌飽和を持つ固定水文タイルです。</param>
        /// <param name="surfaceWaterTile">同じ座標の河道・恒久湖・泉流を持つ固定表流水タイルです。</param>
        /// <param name="terrainCell">成功時にBlockとLiquidを直接参照する変更不可の自然生成セルを返します。</param>
        /// <returns>入力が同じ座標の現行固定生成値で、全Definitionを確定できた場合はtrueです。</returns>
        public static bool TryFinalizeSurfaceCell(
            in WorldGenerationSettings settings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            int worldTileX,
            int worldTileY,
            in GeneratedTile finalizedTile,
            in GeneratedHydrologyTile hydrologyTile,
            in GeneratedSurfaceWaterTile surfaceWaterTile,
            out GeneratedTerrainCell terrainCell)
        {
            terrainCell = default;
            if (!runtimeIds.IsValid
                || !paletteCatalog.IsCreated
                || !DeterministicEnvironmentGenerator.TryGenerate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out GeneratedEnvironmentData environment)
                || !MatchesFixedEnvironment(finalizedTile, environment))
            {
                return false;
            }

            // 海は地上18Biomeの外側です。Biome.Noneを別Biomeへ読み替えず、海水Liquidだけを設定します。
            if (environment.Elevation < settings.SeaLevelThreshold)
            {
                if (finalizedTile.Biome != BiomeId.None)
                {
                    return false;
                }

                terrainCell = new GeneratedTerrainCell
                {
                    Biome = BiomeId.None,
                    LiquidRuntimeId = runtimeIds.SaltWater
                };
                return true;
            }

            if (!IsLandBiome(finalizedTile.Biome))
            {
                return false;
            }

            // 海面より上では、活動中核のマグマを表流水より先に確定して既存の優先契約を維持します。
            if (DeterministicBiomeChunkFinalizer.IsMagmaCandidate(
                    settings,
                    finalizedTile.Biome,
                    environment.VolcanicActivityPotential,
                    worldTileX,
                    worldTileY))
            {
                terrainCell = new GeneratedTerrainCell
                {
                    Biome = finalizedTile.Biome,
                    LiquidRuntimeId = runtimeIds.Magma
                };
                return true;
            }

            if ((surfaceWaterTile.Flags & SurfaceWaterTileFlags.NaturalSurfaceWater) != 0)
            {
                if (!TryResolveSurfaceWaterLiquid(
                        surfaceWaterTile.Type,
                        runtimeIds,
                        out DefinitionRuntimeId liquidRuntimeId))
                {
                    return false;
                }

                terrainCell = new GeneratedTerrainCell
                {
                    Biome = finalizedTile.Biome,
                    LiquidRuntimeId = liquidRuntimeId
                };
                return true;
            }

            if (!DeterministicTerrainBlockPaletteSelector.TrySelect(
                    settings,
                    paletteCatalog,
                    worldTileX,
                    worldTileY,
                    environment.WarpedSampleX,
                    environment.WarpedSampleY,
                    finalizedTile.Biome,
                    finalizedTile.GeologyClass,
                    environment.BaseMoisture,
                    environment.TerrainRuggedness,
                    finalizedTile.GeologyErosionResistance,
                    hydrologyTile.NaturalSoilSaturationPotential,
                    out TerrainBlockPaletteSelectionResult selection)
                || !selection.FinalGround.BlockRuntimeId.IsValid)
            {
                return false;
            }

            bool hasNaturalWall = DeterministicRockWallGenerator.IsRockWallCandidate(
                settings,
                worldTileX,
                worldTileY);
            if (hasNaturalWall && !selection.NaturalWall.BlockRuntimeId.IsValid)
            {
                return false;
            }

            // 自然Wallの下にもGroundを残し、壁だけを採掘した後に同じ地面を公開できるようにします。
            terrainCell = new GeneratedTerrainCell
            {
                Biome = finalizedTile.Biome,
                GroundBlockRuntimeId = selection.FinalGround.BlockRuntimeId,
                WallBlockRuntimeId = hasNaturalWall
                    ? selection.NaturalWall.BlockRuntimeId
                    : default
            };
            return true;
        }

        /// <summary>入力タイルが同じ座標から生成された固定環境値を保持するか照合します。</summary>
        /// <param name="tile">Biome確定Jobまで受け渡された環境・地質タイルです。</param>
        /// <param name="environment">絶対座標から再生成した固定環境値です。</param>
        /// <returns>Block選択と液体優先判定に使う全環境値が一致する場合はtrueです。</returns>
        private static bool MatchesFixedEnvironment(
            in GeneratedTile tile,
            in GeneratedEnvironmentData environment)
        {
            return tile.Elevation == environment.Elevation
                && tile.Moisture == environment.BaseMoisture
                && tile.TerrainRuggedness == environment.TerrainRuggedness
                && tile.VolcanicActivityPotential == environment.VolcanicActivityPotential;
        }

        /// <summary>海域用Noneを除く、現行18Biomeの安定ID範囲か確認します。</summary>
        /// <param name="biome">完成済み地上タイルが保持するBiome IDです。</param>
        /// <returns>1〜18の現行安定IDならtrueです。</returns>
        private static bool IsLandBiome(BiomeId biome)
        {
            int stableId = (int)biome;
            return stableId >= BiomeStableIds.First && stableId <= BiomeStableIds.Last;
        }

        /// <summary>確定自然水面の種類を淡水または海水Definitionへ変換します。</summary>
        /// <param name="type">NaturalSurfaceWaterフラグを持つ固定表流水種別です。</param>
        /// <param name="runtimeIds">Bakerが解決した液体Definition IDです。</param>
        /// <param name="liquidRuntimeId">成功時にセルへ設定する淡水または海水IDを返します。</param>
        /// <returns>河道、恒久湖、泉流の既知種別ならtrueです。</returns>
        private static bool TryResolveSurfaceWaterLiquid(
            SurfaceWaterTileType type,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            out DefinitionRuntimeId liquidRuntimeId)
        {
            switch (type)
            {
                case SurfaceWaterTileType.Stream:
                case SurfaceWaterTileType.River:
                case SurfaceWaterTileType.PermanentFreshwaterLake:
                case SurfaceWaterTileType.SpringStream:
                    liquidRuntimeId = runtimeIds.FreshWater;
                    return true;
                case SurfaceWaterTileType.PermanentSaltwaterLake:
                    liquidRuntimeId = runtimeIds.SaltWater;
                    return true;
                default:
                    // Noneと未確定LakeCandidateを水面へ読み替えず、生成入力の矛盾として失敗させます。
                    liquidRuntimeId = default;
                    return false;
            }
        }

        /// <summary>
        /// 1つの自然生成Blockセルを、チャンク生成署名へ固定順で混合します。
        /// Blobのメモリ表現やpaddingは使わず、論理フィールドだけを明示的に混ぜます。
        /// </summary>
        /// <param name="currentHash">直前までの生成結果を反映した64bitハッシュです。</param>
        /// <param name="terrainCell">次に追加する完成済み自然Blockセルです。</param>
        /// <returns>Biomeと4区画のRuntime IDを追加した64bitハッシュです。</returns>
        internal static ulong HashCell(
            ulong currentHash,
            in GeneratedTerrainCell terrainCell)
        {
            unchecked
            {
                ulong biomeHash = WorldSeedUtility.Mix64(
                    currentHash ^ (byte)terrainCell.Biome);
                ulong groundHash = WorldSeedUtility.Mix64(
                    biomeHash ^ (uint)terrainCell.GroundBlockRuntimeId.Value);
                ulong wallHash = WorldSeedUtility.Mix64(
                    groundHash ^ (uint)terrainCell.WallBlockRuntimeId.Value);
                ulong liquidHash = WorldSeedUtility.Mix64(
                    wallHash ^ (uint)terrainCell.LiquidRuntimeId.Value);
                return WorldSeedUtility.Mix64(
                    liquidHash ^ (uint)terrainCell.TerrainSurfaceRuntimeId.Value);
            }
        }
    }
}
