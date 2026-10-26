// このファイルは、新Biome分類より前に必要な連続環境・地質・陸海・岩壁候補を決定論的に生成します。
// ECS Job、C3/C4水文、テストが同じ基礎タイル生成を共有し、最終BiomeとBlockセルは水文完成後に確定します。

using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 最終Biomeの入力となる基礎GeneratedTileを生成し、地面から派生する共通ルールを提供します。
    /// 旧Biome分類、旧淡水、旧マグマは扱わず、UnityEngine.Objectやグローバル乱数にも依存しません。
    /// </summary>
    public static class DeterministicChunkGenerator
    {
        /// <summary>
        /// 1つのワールドタイル座標に対応する基礎地形データを生成します。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>Biomeと最終水面を未確定のまま保持する、連続環境・地質・陸海・岩壁候補の基礎タイルです。</returns>
        public static GeneratedTile GenerateTile(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            TryGenerateTile(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedTile tile);
            return tile;
        }

        /// <summary>
        /// 1つのワールドタイル座標に対応する基礎地形データを生成し、固定半径サンプルが座標域外なら失敗を返します。
        /// ChunkGenerationSystemはこの戻り値を使い、不完全なチャンクをReadyとして公開しません。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="tile">成功時に全基礎値を持つタイルを返します。失敗時はdefaultです。</param>
        /// <returns>設定と固定サンプル座標が有効ならtrue、それ以外はfalseです。</returns>
        public static bool TryGenerateTile(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out GeneratedTile tile)
        {
            tile = default;
            if (!DeterministicGeologyGenerator.TryGenerate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedGeologyData geology))
            {
                return false;
            }

            return TryGenerateTileWithGeology(
                settings,
                worldTileX,
                worldTileY,
                geology,
                out tile);
        }

        /// <summary>
        /// チャンク共有の地質サイトキャッシュを使って1タイルの基礎地形データを生成します。
        /// Chunk生成Jobとチャンク状態ハッシュだけが呼び、公開APIと同じ基礎タイル値を返します。
        /// </summary>
        /// <param name="settings">キャッシュ作成時と同じワールド生成設定です。</param>
        /// <param name="geologyCache">対象チャンクの候補サイトと分類を保持する一時キャッシュです。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="tile">成功時に全基礎値を持つタイルを返します。失敗時はdefaultです。</param>
        /// <returns>キャッシュ内の5×5候補と環境固定サンプルが有効ならtrueです。</returns>
        internal static bool TryGenerateTile(
            in WorldGenerationSettings settings,
            in GeologyChunkCache geologyCache,
            int worldTileX,
            int worldTileY,
            out GeneratedTile tile)
        {
            tile = default;
            if (!DeterministicGeologyGenerator.TryGenerateFromChunkCache(
                settings,
                geologyCache,
                worldTileX,
                worldTileY,
                out GeneratedGeologyData geology))
            {
                return false;
            }

            return TryGenerateTileWithGeology(
                settings,
                worldTileX,
                worldTileY,
                geology,
                out tile);
        }

        /// <summary>
        /// 確定済み地質と同じ座標の連続環境・陸海・岩壁候補を合成し、Biome分類前のGeneratedTileを作ります。
        /// 単一タイル生成とチャンクキャッシュ生成の結果を同じ代入処理へ集約するために呼びます。
        /// </summary>
        /// <param name="settings">連続環境、海面、岩壁候補を生成する固定設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="geology">同じ絶対座標で確定済みの固定地質結果です。</param>
        /// <param name="tile">成功時に環境と地質を含むタイルを返します。失敗時はdefaultです。</param>
        /// <returns>環境の固定半径サンプルが座標域内で生成できた場合はtrueです。</returns>
        private static bool TryGenerateTileWithGeology(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            in GeneratedGeologyData geology,
            out GeneratedTile tile)
        {
            tile = default;
            if (!DeterministicEnvironmentGenerator.TryGenerate(
                settings,
                worldTileX,
                worldTileY,
                out GeneratedEnvironmentData environment))
            {
                return false;
            }

            ushort elevation = environment.Elevation;

            tile = new GeneratedTile
            {
                // BiomeはC3/C4の固定水文が完成するまで確定できないため、中間BufferではNoneを明示します。
                Biome = BiomeId.None,
                ElevationClass = DeterministicEnvironmentGenerator.ClassifyElevation(elevation, settings),
                ResourceSuitability = ResourceSuitability.None,
                Elevation = elevation,
                Temperature = environment.BaseTemperature,
                Moisture = environment.BaseMoisture,
                TerrainTemperature = environment.TerrainTemperature,
                WoodyVegetationPotential = environment.WoodyVegetationPotential,
                TerrainRuggedness = environment.TerrainRuggedness,
                CoastalInfluence = environment.CoastalInfluence,
                VolcanicActivityPotential = environment.VolcanicActivityPotential,
                GeologyClass = geology.Class,
                GeologyFlags = geology.Flags,
                GeologyPermeability = geology.Permeability,
                GeologyGroundwaterStorage = geology.GroundwaterStorage,
                GeologyErosionResistance = geology.ErosionResistance
            };
            return true;
        }

        /// <summary>
        /// 指定チャンクの4096地形・Blockセル、地下水反映済み64水文セル、4096表流水、4096自然水文の安定64bitハッシュを求めます。
        /// 水文と表流水の有限探索用一時配列を確保し、決定性テストと開発用比較表示に使用します。
        /// </summary>
        /// <param name="settings">ワールド生成設定です。</param>
        /// <param name="biomeSettings">C5の新18Biome Definitionです。</param>
        /// <param name="paletteCatalog">自然Ground・Wallを選ぶ固定Blockパレットです。</param>
        /// <param name="terrainRuntimeIds">海水・淡水・マグマを直接設定する解決済みIDです。</param>
        /// <param name="chunkCoordinate">生成結果を比較する論理チャンク座標です。</param>
        /// <returns>地形、Blockセル、水文セル、表流水、自然土壌飽和の順序と全固定値を反映した64bitハッシュです。</returns>
        public static ulong ComputeChunkHash(
            in WorldGenerationSettings settings,
            in BiomeClassificationSettings biomeSettings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
            in BuiltInTerrainBlockRuntimeIds terrainRuntimeIds,
            ChunkCoordinate chunkCoordinate)
        {
            ulong hash = 0xCBF29CE484222325UL;
            // C4水文を完成させた後に新18Biomeを確定し、基礎地形だけの旧ハッシュ経路を残しません。
            return DeterministicHydrologyGenerator.AppendChunkHash(
                settings,
                biomeSettings,
                paletteCatalog,
                terrainRuntimeIds,
                chunkCoordinate,
                hash);
        }

        /// <summary>
        /// 1タイルの全基礎値を、それまでのチャンクハッシュへ順序付きで混合します。
        /// </summary>
        /// <param name="currentHash">直前までのタイルを反映したハッシュです。</param>
        /// <param name="tile">次に混合する生成済みタイルです。</param>
        /// <returns>tileを追加した64bitハッシュです。</returns>
        internal static ulong HashTile(ulong currentHash, GeneratedTile tile)
        {
            unchecked
            {
                ulong packedClassifications = (byte)tile.Biome;
                packedClassifications |= (ulong)(byte)tile.ElevationClass << 8;
                packedClassifications |= (ulong)(byte)tile.ResourceSuitability << 16;

                ulong packedClimate = tile.Elevation;
                packedClimate |= (ulong)tile.Temperature << 16;
                packedClimate |= (ulong)tile.Moisture << 32;
                // 旧WeirdnessはC5-Cで削除し、気候値の空いた上位16bitは0のまま固定します。

                ulong packedEnvironment = tile.TerrainTemperature;
                packedEnvironment |= (ulong)tile.WoodyVegetationPotential << 16;
                packedEnvironment |= (ulong)tile.TerrainRuggedness << 32;
                packedEnvironment |= (ulong)tile.CoastalInfluence << 48;

                ulong classificationsHash = WorldSeedUtility.Mix64(currentHash ^ packedClassifications);
                ulong climateHash = WorldSeedUtility.Mix64(classificationsHash ^ packedClimate);
                ulong environmentHash = WorldSeedUtility.Mix64(climateHash ^ packedEnvironment);
                ulong volcanicHash = WorldSeedUtility.Mix64(
                    environmentHash ^ tile.VolcanicActivityPotential);
                ulong packedGeology = (byte)tile.GeologyClass;
                packedGeology |= (ulong)(byte)tile.GeologyFlags << 8;
                packedGeology |= (ulong)tile.GeologyPermeability << 16;
                packedGeology |= (ulong)tile.GeologyGroundwaterStorage << 32;
                packedGeology |= (ulong)tile.GeologyErosionResistance << 48;
                return WorldSeedUtility.Mix64(volcanicHash ^ packedGeology);
            }
        }
    }
}
