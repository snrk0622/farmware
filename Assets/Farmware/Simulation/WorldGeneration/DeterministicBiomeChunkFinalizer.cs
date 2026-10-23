// このファイルは、完成した固定水文と基礎地形を合成し、新18Biomeと最終的な地面属性を確定します。
// ChunkGenerationSystemの最終Jobと純粋計算テストが同じ入口を使い、旧7Biomeや旧淡水判定へ戻らない境界です。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// C1〜C4で生成済みの環境・地質・固定水文から、1タイルの新Biomeと暫定地形属性を確定します。
    /// C6でBlock表示へ移行するまではGeneratedTileの既存地面欄も更新しますが、分類の正は新18Biomeと固定水文です。
    /// </summary>
    public static class DeterministicBiomeChunkFinalizer
    {
        /// <summary>
        /// 1タイルの環境を再現し、特殊優先を含むBiome分類後に水面・マグマ・岩壁・陸地の優先順を適用します。
        /// </summary>
        /// <param name="worldSettings">Seed、GeneratorVersion、地形とマグマの固定設定です。</param>
        /// <param name="biomeSettings">検証済みの新18Biome Definitionです。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <param name="baseTile">先行Jobが生成した環境・地質・基礎岩壁情報です。</param>
        /// <param name="hydrologyTile">同じ座標で完成した自然水文と湿原規模です。</param>
        /// <param name="surfaceWaterTile">同じ座標で完成した固定表流水です。</param>
        /// <param name="finalizedTile">成功時に新Biomeと最終地面属性を反映したタイルを返します。</param>
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
                // 海域はBiomeId.Noneと海水Groundを明示し、後続の水域生息地分類が地上Biomeを偽装せず扱えるようにします。
                finalizedTile = baseTile;
                finalizedTile.Biome = BiomeId.None;
                finalizedTile.Ground = GroundType.Water;
                finalizedTile.Soil = SoilType.None;
                finalizedTile.WaterSalinity = WaterSalinity.SaltWater;
                finalizedTile.WaterBodyType = WaterBodyType.Sea;
                finalizedTile.GameplayFlags =
                    DeterministicChunkGenerator.ClassifyGameplayFlags(GroundType.Water);
                finalizedTile.ResourceSuitability = ResourceSuitability.None;
                finalizedTile.MovementCost = 2;
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

            BiomeId biome = (BiomeId)selection.StableId;
            GroundType ground = ClassifyFinalGround(
                worldSettings,
                environment,
                biome,
                baseTile.Ground,
                surfaceWaterTile,
                worldTileX,
                worldTileY,
                out WaterSalinity waterSalinity,
                out WaterBodyType waterBodyType);
            SoilType soil = ground == GroundType.Land
                ? ClassifyInterimSoil(environment, biome, worldSettings)
                : SoilType.None;

            finalizedTile = baseTile;
            finalizedTile.Biome = biome;
            finalizedTile.Ground = ground;
            finalizedTile.Soil = soil;
            finalizedTile.WaterSalinity = waterSalinity;
            finalizedTile.WaterBodyType = waterBodyType;
            finalizedTile.GameplayFlags = DeterministicChunkGenerator.ClassifyGameplayFlags(ground);
            finalizedTile.ResourceSuitability = ClassifyInterimResourceSuitability(ground, soil, biome);
            finalizedTile.MovementCost = ClassifyMovementCost(ground);
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

        /// <summary>
        /// 海、活動的火山のマグマ、固定表流水、基礎岩壁、陸地の順で既存GroundTypeへ一時反映します。
        /// C6でBlockとLiquidへ置換するまでの表示・通行用境界で、Biome分類や固定水文そのものは変更しません。
        /// </summary>
        /// <param name="settings">海面とマグマ形状の固定設定です。</param>
        /// <param name="environment">同じ座標の連続環境値です。</param>
        /// <param name="biome">新分類器が確定した18種のいずれかです。</param>
        /// <param name="baseGround">先行Jobが海と岩壁候補から作った基礎地面です。</param>
        /// <param name="surfaceWater">C4で完成した河道・湖・泉です。</param>
        /// <param name="worldTileX">マグマノイズへ使う絶対水平座標です。</param>
        /// <param name="worldTileY">マグマノイズへ使う絶対垂直座標です。</param>
        /// <param name="salinity">確定した水域の塩分です。</param>
        /// <param name="waterBodyType">既存利用側へ渡す海・河道・湖の暫定分類です。</param>
        /// <returns>既存の建築・通行判定へ渡す最終GroundTypeです。</returns>
        private static GroundType ClassifyFinalGround(
            in WorldGenerationSettings settings,
            in GeneratedEnvironmentData environment,
            BiomeId biome,
            GroundType baseGround,
            in GeneratedSurfaceWaterTile surfaceWater,
            int worldTileX,
            int worldTileY,
            out WaterSalinity salinity,
            out WaterBodyType waterBodyType)
        {
            salinity = WaterSalinity.None;
            waterBodyType = WaterBodyType.None;
            if (environment.Elevation < settings.SeaLevelThreshold)
            {
                salinity = WaterSalinity.SaltWater;
                waterBodyType = WaterBodyType.Sea;
                return GroundType.Water;
            }

            // 段階4の火山周縁へマグマを漏らさないよう、表示と共有する活動中核・形状判定を使います。
            if (IsMagmaCandidate(
                    settings,
                    biome,
                    environment.VolcanicActivityPotential,
                    worldTileX,
                    worldTileY))
            {
                return GroundType.Magma;
            }

            if ((surfaceWater.Flags & SurfaceWaterTileFlags.NaturalSurfaceWater) != 0)
            {
                bool saltwaterLake = surfaceWater.Type == SurfaceWaterTileType.PermanentSaltwaterLake;
                salinity = saltwaterLake ? WaterSalinity.SaltWater : WaterSalinity.FreshWater;
                waterBodyType = surfaceWater.Type == SurfaceWaterTileType.Stream
                    || surfaceWater.Type == SurfaceWaterTileType.River
                    || surfaceWater.Type == SurfaceWaterTileType.SpringStream
                        ? WaterBodyType.River
                        : WaterBodyType.Pond;
                return GroundType.Water;
            }

            return baseGround == GroundType.RockWall ? GroundType.RockWall : GroundType.Land;
        }

        /// <summary>
        /// C6のBlockパレットへ移るまで、連続環境値と新Biomeから既存SoilTypeを決めます。
        /// 旧7Biomeの境界幅や橋Biomeを再利用せず、乾燥・湿潤という現在も有効な意味だけを残します。
        /// </summary>
        /// <param name="environment">基礎湿度と地形気温を持つ固定環境です。</param>
        /// <param name="biome">新18種の確定Biomeです。</param>
        /// <param name="settings">既存SoilTypeの暫定境界に使う共有気候閾値です。</param>
        /// <returns>陸地の暫定土壌分類です。</returns>
        private static SoilType ClassifyInterimSoil(
            in GeneratedEnvironmentData environment,
            BiomeId biome,
            in WorldGenerationSettings settings)
        {
            if (biome == BiomeId.CactusDesertDryPlateau
                || biome == BiomeId.HotDesert
                || environment.BaseMoisture < settings.DryMoistureThreshold)
            {
                return SoilType.Sandy;
            }

            if (biome == BiomeId.FreshwaterMarshFloodplain
                || biome == BiomeId.SwampForest
                || environment.BaseMoisture >= settings.WetMoistureThreshold)
            {
                return SoilType.Fertile;
            }

            return SoilType.Normal;
        }

        /// <summary>
        /// C7の28資源適性へ置換するまで、旧3資源が最低限動作できる候補だけを新Biomeから導出します。
        /// </summary>
        /// <param name="ground">最終地面種別です。</param>
        /// <param name="soil">暫定土壌分類です。</param>
        /// <param name="biome">新18種の確定Biomeです。</param>
        /// <returns>木・石・砂の暫定候補bitです。</returns>
        private static ResourceSuitability ClassifyInterimResourceSuitability(
            GroundType ground,
            SoilType soil,
            BiomeId biome)
        {
            if (ground == GroundType.Water || ground == GroundType.Magma)
            {
                return ResourceSuitability.None;
            }

            if (ground == GroundType.RockWall
                || biome == BiomeId.AlpineRockland
                || biome == BiomeId.VolcanicGeothermal)
            {
                return ResourceSuitability.Stone;
            }

            if (soil == SoilType.Sandy)
            {
                return ResourceSuitability.Sand | ResourceSuitability.Stone;
            }

            return ResourceSuitability.Tree | ResourceSuitability.Stone;
        }

        /// <summary>最終地面から既存経路探索へ渡す暫定移動コストを返します。</summary>
        /// <param name="ground">水文とマグマまで反映した地面です。</param>
        /// <returns>陸地1、水域2、通常通行不可0です。</returns>
        private static byte ClassifyMovementCost(GroundType ground)
        {
            if (ground == GroundType.Land)
            {
                return 1;
            }

            return ground == GroundType.Water ? (byte)2 : (byte)0;
        }
    }
}
