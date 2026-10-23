// このファイルは、新Biome分類より前に必要な連続環境・地質・陸海・岩壁候補を決定論的に生成します。
// ECS Job、C3/C4水文、テストが同じ基礎タイル生成を共有し、最終BiomeはDeterministicBiomeChunkFinalizerが水文完成後に確定します。

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
            GroundType ground = elevation < settings.SeaLevelThreshold
                ? GroundType.Water
                : DeterministicRockWallGenerator.IsRockWallCandidate(
                    settings,
                    worldTileX,
                    worldTileY)
                    ? GroundType.RockWall
                    : GroundType.Land;
            WaterBodyType waterBodyType = ground == GroundType.Water
                ? WaterBodyType.Sea
                : WaterBodyType.None;

            tile = new GeneratedTile
            {
                // BiomeはC3/C4の固定水文が完成するまで確定できないため、中間BufferではNoneを明示します。
                Biome = BiomeId.None,
                Ground = ground,
                Soil = SoilType.None,
                WaterSalinity = waterBodyType == WaterBodyType.Sea
                    ? WaterSalinity.SaltWater
                    : WaterSalinity.None,
                WaterBodyType = waterBodyType,
                ElevationClass = DeterministicEnvironmentGenerator.ClassifyElevation(elevation, settings),
                GameplayFlags = ClassifyGameplayFlags(ground),
                ResourceSuitability = ResourceSuitability.None,
                MovementCost = ground == GroundType.Land ? (byte)1 : (byte)0,
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
        /// 指定チャンクの4096地形、地下水反映済み64水文セル、4096表流水、4096自然水文の安定64bitハッシュを求めます。
        /// 水文と表流水の有限探索用一時配列を確保し、決定性テストと開発用比較表示に使用します。
        /// </summary>
        /// <param name="settings">ワールド生成設定です。</param>
        /// <param name="biomeSettings">C5の新18Biome Definitionです。</param>
        /// <param name="chunkCoordinate">生成結果を比較する論理チャンク座標です。</param>
        /// <returns>地形、水文セル、表流水、自然土壌飽和の順序と全固定値を反映した64bitハッシュです。</returns>
        public static ulong ComputeChunkHash(
            in WorldGenerationSettings settings,
            in BiomeClassificationSettings biomeSettings,
            ChunkCoordinate chunkCoordinate)
        {
            ulong hash = 0xCBF29CE484222325UL;
            // C4水文を完成させた後に新18Biomeを確定し、基礎地形だけの旧ハッシュ経路を残しません。
            return DeterministicHydrologyGenerator.AppendChunkHash(
                settings,
                biomeSettings,
                chunkCoordinate,
                hash);
        }

        /// <summary>
        /// 地面種別から通常設備、通常ベルト、橋の配置可否と、橋の耐熱要件を決めます。
        /// 後続の建築処理がGroundTypeを独自解釈せず、生成済み属性だけを参照できるようにします。
        /// </summary>
        /// <param name="ground">分類済みの地面種別です。</param>
        /// <returns>各配置可否と橋の耐熱要件を表すビットフラグです。</returns>
        public static TileGameplayFlags ClassifyGameplayFlags(GroundType ground)
        {
            switch (ground)
            {
                case GroundType.Land:
                    return TileGameplayFlags.AllowsNormalEquipment
                        | TileGameplayFlags.AllowsNormalBelt;
                case GroundType.Water:
                    // 水上では橋の素材を耐熱性で制限しないため、橋可だけを設定します。
                    return TileGameplayFlags.AllowsBridge;
                case GroundType.Magma:
                    // マグマ上の橋は耐熱性を持つ定義だけに限定できるよう、要件を独立したbitで記録します。
                    return TileGameplayFlags.AllowsBridge
                        | TileGameplayFlags.RequiresHeatResistance;
                case GroundType.RockWall:
                    return TileGameplayFlags.None;
                default:
                    // 予約済みIDや破損データを配置可能地として扱わず、すべて不許可の安全側へ倒します。
                    return TileGameplayFlags.None;
            }
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
                packedClassifications |= (ulong)(byte)tile.Ground << 8;
                packedClassifications |= (ulong)(byte)tile.ElevationClass << 16;
                packedClassifications |= (ulong)(byte)tile.GameplayFlags << 24;
                packedClassifications |= (ulong)(byte)tile.ResourceSuitability << 32;
                packedClassifications |= (ulong)tile.MovementCost << 40;
                packedClassifications |= (ulong)(byte)tile.WaterSalinity << 48;
                packedClassifications |= (ulong)(byte)tile.WaterBodyType << 56;

                ulong packedClimate = tile.Elevation;
                packedClimate |= (ulong)tile.Temperature << 16;
                packedClimate |= (ulong)tile.Moisture << 32;
                // 旧WeirdnessはC5-Cで削除し、気候値の空いた上位16bitは0のまま固定します。

                ulong packedEnvironment = tile.TerrainTemperature;
                packedEnvironment |= (ulong)tile.WoodyVegetationPotential << 16;
                packedEnvironment |= (ulong)tile.TerrainRuggedness << 32;
                packedEnvironment |= (ulong)tile.CoastalInfluence << 48;

                ulong classificationsHash = WorldSeedUtility.Mix64(currentHash ^ packedClassifications);
                // 既存8分類で64bitを使い切っているため、土壌は独立した混合段階へ加えて配置差分も検出します。
                ulong soilHash = WorldSeedUtility.Mix64(classificationsHash ^ (byte)tile.Soil);
                ulong climateHash = WorldSeedUtility.Mix64(soilHash ^ packedClimate);
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
