// このファイルは、完成済みの地形・固定水文・表流水から地下水と自然土壌飽和を決定論的に導出します。
// 水文セルの地下水はGeneratedHydrologyCellへ、タイル別飽和度は専用ECS Bufferへ出力し、現在水文とは分離します。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1タイルの固定水文が満たす淡水接続、湿原資格、広域規模を重複可能なbitで保持します。
    /// C4-D2の生成Jobだけが設定し、公開後はワールド生成結果として変更しません。
    /// </summary>
    [Flags]
    public enum GeneratedHydrologyTileFlags : ushort
    {
        /// <summary>固定水文上の追加条件を満たさない状態です。</summary>
        None = 0,

        /// <summary>河川、淡水湖、地下水、または半径32内の恒久淡水源へ接続するタイルです。</summary>
        FreshwaterConnected = 1 << 0,

        /// <summary>低地、淡水接続、自然飽和または自然冠水の3条件を満たすタイルです。</summary>
        WetlandTileEligible = 1 << 1,

        /// <summary>資格タイル16以上の水文セルが上下左右に4セル以上連結する成分へ属するタイルです。</summary>
        LargeWetlandExtent = 1 << 2
    }

    /// <summary>
    /// 1タイルの自然状態における固定水文導出値です。
    /// 1論理チャンクEntityへ行優先4096件を保持し、降雨、灌水、排水、耕作などの現在状態から書き換えません。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GeneratedHydrologyTile : IBufferElementData
    {
        /// <summary>
        /// 基礎湿度、地下水、局所透水性、地形起伏から求めた0〜65,535の自然土壌飽和ポテンシャルです。
        /// 地下水Jobが設定し、公開後は固定生成結果として変更しません。
        /// </summary>
        public ushort NaturalSoilSaturationPotential;

        /// <summary>
        /// 恒久淡水源への距離、水面との比高、平坦さ、水域規模から求めた0〜65,535の自然冠水度です。
        /// 自然冠水Jobが設定し、公開後は固定生成結果として変更しません。
        /// </summary>
        public ushort NaturalFloodingPotential;

        /// <summary>
        /// 淡水接続、タイル資格、広域湿原規模を示す固定bitです。
        /// 自然冠水Jobが設定し、公開後は固定生成結果として変更しません。
        /// </summary>
        public GeneratedHydrologyTileFlags Flags;
    }

    /// <summary>
    /// 地形4096件、チャンクと交差する完成水文9×9件、表流水由来の涵養有無から固定地下水と自然土壌飽和を生成します。
    /// Burst Jobから利用するため、管理参照や実行時のNative Collection確保を行いません。
    /// </summary>
    public static class DeterministicGroundwaterHydrologyGenerator
    {
        /// <summary>64×64タイルと交差する水文セルの各軸数です。中心が属する8セルに正側1セルを加えます。</summary>
        public const int IntersectingCellsPerAxis =
            DeterministicHydrologyGenerator.CellsPerChunkAxis + 1;

        /// <summary>1論理チャンクと交差する9×9水文セルの総数です。</summary>
        public const int IntersectingCellCount = IntersectingCellsPerAxis * IntersectingCellsPerAxis;

        /// <summary>
        /// 完成済み入力を検証し、9×9セルの地下水、64セルのECS公開値、4096タイルの自然土壌飽和を一括生成します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、地質、水文、地下水の固定設定です。</param>
        /// <param name="chunkX">生成対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">生成対象の垂直論理チャンク座標です。</param>
        /// <param name="tiles">完成済みの行優先4096地形タイルです。基礎地質値と起伏を読みます。</param>
        /// <param name="hydrologyCells">成功時に地下水を反映する、中心がチャンク内に属する行優先64水文セルです。</param>
        /// <param name="intersectingCells">表流水段階が完成させた、チャンクと交差する行優先9×9水文セルです。</param>
        /// <param name="surfaceRechargeCandidates">9×9各セル内に自然表流水の涵養候補が1タイル以上あれば1となる配列です。</param>
        /// <param name="hydrologyTiles">成功時に4096件すべてを上書きする自然土壌飽和の出力配列です。</param>
        /// <returns>全セルと全タイルを部分結果なしで生成できた場合はtrueです。</returns>
        public static bool TryGenerateChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedTile> tiles,
            NativeArray<GeneratedHydrologyCell> hydrologyCells,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<SurfaceRechargeCandidate> surfaceRechargeCandidates,
            NativeArray<GeneratedHydrologyTile> hydrologyTiles)
        {
            if (!DeterministicGroundwaterGenerator.ValidateSettings(settings.Groundwater)
                || !DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || !tiles.IsCreated || tiles.Length < WorldGridConstants.TilesPerChunk
                || !hydrologyCells.IsCreated
                || hydrologyCells.Length < DeterministicHydrologyGenerator.CellsPerChunk
                || !intersectingCells.IsCreated || intersectingCells.Length < IntersectingCellCount
                || !surfaceRechargeCandidates.IsCreated
                || surfaceRechargeCandidates.Length < IntersectingCellCount
                || !hydrologyTiles.IsCreated
                || hydrologyTiles.Length < WorldGridConstants.TilesPerChunk
                || !TryGetChunkOrigins(
                    chunkX,
                    chunkY,
                    out int minimumTileX,
                    out int minimumTileY,
                    out int firstCellX,
                    out int firstCellY))
            {
                return false;
            }

            // 正側4タイルは9個目の水文セルを参照するため、先に交差9×9セルをすべて完成させます。
            for (int cellOffsetY = 0; cellOffsetY < IntersectingCellsPerAxis; cellOffsetY++)
            {
                for (int cellOffsetX = 0; cellOffsetX < IntersectingCellsPerAxis; cellOffsetX++)
                {
                    int cellIndex = (cellOffsetY * IntersectingCellsPerAxis) + cellOffsetX;
                    GeneratedHydrologyCell cell = intersectingCells[cellIndex];
                    int expectedCellX = firstCellX + cellOffsetX;
                    int expectedCellY = firstCellY + cellOffsetY;
                    if (cell.CellX != expectedCellX
                        || cell.CellY != expectedCellY
                        || !TryGenerateCellGroundwater(
                            settings,
                            cell,
                            surfaceRechargeCandidates[cellIndex].HasRecharge != 0,
                            out ushort groundwaterPotential))
                    {
                        return false;
                    }

                    cell.GroundwaterPotential = groundwaterPotential;
                    // 再利用配列に以前のGroundwater bitが残っても、今回の閾値判定だけを正にします。
                    cell.ConnectionFlags &= ~HydrologyConnectionFlags.Groundwater;
                    if (groundwaterPotential >= settings.Groundwater.GroundwaterConnectionThreshold)
                    {
                        cell.ConnectionFlags |= HydrologyConnectionFlags.Groundwater;
                    }
                    intersectingCells[cellIndex] = cell;

                    if (cellOffsetX < DeterministicHydrologyGenerator.CellsPerChunkAxis
                        && cellOffsetY < DeterministicHydrologyGenerator.CellsPerChunkAxis)
                    {
                        int localCellIndex =
                            (cellOffsetY * DeterministicHydrologyGenerator.CellsPerChunkAxis)
                            + cellOffsetX;
                        hydrologyCells[localCellIndex] = cell;
                    }
                }
            }

            // 飽和度はセル値を共有しても、透水性と起伏を対象タイル自身から読むため境界を四角く固定しません。
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                    int worldTileX = minimumTileX + localX;
                    int worldTileY = minimumTileY + localY;
                    GeneratedTile tile = tiles[tileIndex];
                    if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                            settings.Hydrology,
                            worldTileX,
                            worldTileY,
                            out int cellX,
                            out int cellY))
                    {
                        return false;
                    }

                    int cellOffsetX = cellX - firstCellX;
                    int cellOffsetY = cellY - firstCellY;
                    if (cellOffsetX < 0 || cellOffsetX >= IntersectingCellsPerAxis
                        || cellOffsetY < 0 || cellOffsetY >= IntersectingCellsPerAxis
                        || !DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                            settings,
                            worldTileX,
                            worldTileY,
                            tile.GeologyPermeability,
                            tile.GeologyGroundwaterStorage,
                            out AdjustedGroundwaterGeology adjusted)
                        || !TryCalculateNaturalSoilSaturation(
                            settings.Groundwater,
                            intersectingCells[(cellOffsetY * IntersectingCellsPerAxis) + cellOffsetX]
                                .HydrologyMoisture,
                            intersectingCells[(cellOffsetY * IntersectingCellsPerAxis) + cellOffsetX]
                                .GroundwaterPotential,
                            adjusted.Permeability,
                            tile.TerrainRuggedness,
                            out ushort saturation))
                    {
                        return false;
                    }

                    hydrologyTiles[tileIndex] = new GeneratedHydrologyTile
                    {
                        NaturalSoilSaturationPotential = saturation,
                        // 後続C4-D2 Jobが全4096件を上書きします。枠再利用時の前回値を先に消します。
                        NaturalFloodingPotential = 0,
                        Flags = GeneratedHydrologyTileFlags.None
                    };
                }
            }

            return true;
        }

        /// <summary>
        /// 一つの水文セルについて固定9地点の補正後地質を平均し、涵養候補から地下水ポテンシャルを求めます。
        /// </summary>
        /// <param name="settings">地質と地下水の固定生成設定です。</param>
        /// <param name="cell">基礎湿度と流出寄与を含む完成済み固定水文セルです。</param>
        /// <param name="hasRechargeSurfaceWaterCandidate">セル内に自然表流水の涵養候補がある場合はtrueです。</param>
        /// <param name="groundwaterPotential">成功時の0〜65,535固定地下水ポテンシャルです。</param>
        /// <returns>固定9地点と地下水式をすべて計算できた場合はtrueです。</returns>
        public static bool TryGenerateCellGroundwater(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell cell,
            bool hasRechargeSurfaceWaterCandidate,
            out ushort groundwaterPotential)
        {
            groundwaterPotential = default;
            if (!TrySampleAdjustedGeologyAverage(
                    settings,
                    cell.CellX,
                    cell.CellY,
                    out ushort adjustedPermeability,
                    out ushort adjustedGroundwaterStorage))
            {
                return false;
            }

            return TryCalculateGroundwaterPotential(
                settings.Groundwater,
                cell.HydrologyMoisture,
                cell.RunoffEquivalentCells,
                hasRechargeSurfaceWaterCandidate,
                adjustedPermeability,
                adjustedGroundwaterStorage,
                out groundwaterPotential);
        }

        /// <summary>
        /// 確定済みの5入力から涵養最大、帯水係数、地下水ポテンシャルを64bit整数式で求めます。
        /// </summary>
        /// <param name="settings">涵養倍率と接続閾値を含む固定地下水設定です。</param>
        /// <param name="hydrologyMoisture">固定9地点の基礎湿度平均です。</param>
        /// <param name="runoffEquivalentCells">最大4,096の流出寄与セル相当値です。</param>
        /// <param name="hasRechargeSurfaceWaterCandidate">自然河川・渓流・湖面候補がセル内にある場合はtrueです。</param>
        /// <param name="adjustedPermeability">固定9地点で平均した補正後透水性です。</param>
        /// <param name="adjustedGroundwaterStorage">固定9地点で平均した補正後地下水保持力です。</param>
        /// <param name="groundwaterPotential">成功時の0〜65,535固定地下水ポテンシャルです。</param>
        /// <returns>設定が確定契約と一致して計算できた場合はtrueです。</returns>
        public static bool TryCalculateGroundwaterPotential(
            in GroundwaterGenerationSettings settings,
            ushort hydrologyMoisture,
            ushort runoffEquivalentCells,
            bool hasRechargeSurfaceWaterCandidate,
            ushort adjustedPermeability,
            ushort adjustedGroundwaterStorage,
            out ushort groundwaterPotential)
        {
            groundwaterPotential = default;
            if (!DeterministicGroundwaterGenerator.ValidateSettings(settings))
            {
                return false;
            }

            long runoffRecharge = (long)runoffEquivalentCells * settings.RunoffRechargeMultiplier;
            if (runoffRecharge > ushort.MaxValue)
            {
                runoffRecharge = ushort.MaxValue;
            }

            long surfaceRecharge = hydrologyMoisture;
            if (runoffRecharge > surfaceRecharge)
            {
                surfaceRecharge = runoffRecharge;
            }
            if (hasRechargeSurfaceWaterCandidate)
            {
                surfaceRecharge = ushort.MaxValue;
            }

            long aquiferFactor = adjustedPermeability < adjustedGroundwaterStorage
                ? adjustedPermeability
                : adjustedGroundwaterStorage;
            groundwaterPotential = (ushort)((surfaceRecharge * aquiferFactor) / ushort.MaxValue);
            return true;
        }

        /// <summary>
        /// 基礎湿度2、地下水2、排水しにくさ1、平坦さ1を64bitで合成し、タイル別自然土壌飽和を求めます。
        /// </summary>
        /// <param name="settings">比重2：2：1：1と合計6を持つ固定地下水設定です。</param>
        /// <param name="hydrologyMoisture">対象タイルを担当する水文セルの基礎湿度です。</param>
        /// <param name="groundwaterPotential">対象タイルを担当する水文セルの固定地下水ポテンシャルです。</param>
        /// <param name="adjustedPermeability">対象タイル自身の局所補正後透水性です。</param>
        /// <param name="terrainRuggedness">対象タイル自身の固定地形起伏です。</param>
        /// <param name="saturationPotential">成功時の0〜65,535自然土壌飽和ポテンシャルです。</param>
        /// <returns>設定が確定契約と一致して計算できた場合はtrueです。</returns>
        public static bool TryCalculateNaturalSoilSaturation(
            in GroundwaterGenerationSettings settings,
            ushort hydrologyMoisture,
            ushort groundwaterPotential,
            ushort adjustedPermeability,
            ushort terrainRuggedness,
            out ushort saturationPotential)
        {
            saturationPotential = default;
            if (!DeterministicGroundwaterGenerator.ValidateSettings(settings))
            {
                return false;
            }

            long drainageImpedance = ushort.MaxValue - (long)adjustedPermeability;
            long flatness = ushort.MaxValue - (long)terrainRuggedness;
            long weightedTotal = ((long)hydrologyMoisture * settings.SaturationMoistureWeight)
                + ((long)groundwaterPotential * settings.SaturationGroundwaterWeight)
                + (drainageImpedance * settings.SaturationDrainageWeight)
                + (flatness * settings.SaturationFlatnessWeight);
            saturationPotential = (ushort)(weightedTotal / settings.SaturationWeightTotal);
            return true;
        }

        /// <summary>状態ハッシュへ1タイルの自然水文導出値を固定幅で追加します。</summary>
        /// <param name="currentHash">直前までの地形・水文・表流水を反映したハッシュです。</param>
        /// <param name="tile">追加するタイル別固定自然水文です。</param>
        /// <returns>自然土壌飽和、自然冠水、湿原条件bitを固定順で混合した64bitハッシュです。</returns>
        internal static ulong HashTile(ulong currentHash, in GeneratedHydrologyTile tile)
        {
            currentHash = WorldSeedUtility.Mix64(
                currentHash ^ tile.NaturalSoilSaturationPotential);
            currentHash = WorldSeedUtility.Mix64(
                currentHash ^ tile.NaturalFloodingPotential);
            return WorldSeedUtility.Mix64(currentHash ^ (ushort)tile.Flags);
        }

        /// <summary>水文セル中心の固定9地点で局所補正後透水性・保持力を64bit合計し、整数平均します。</summary>
        /// <param name="settings">地質と局所補正を再現する固定生成設定です。</param>
        /// <param name="cellX">水平絶対水文セル座標です。</param>
        /// <param name="cellY">垂直絶対水文セル座標です。</param>
        /// <param name="adjustedPermeability">成功時の固定9地点補正後透水性平均です。</param>
        /// <param name="adjustedGroundwaterStorage">成功時の固定9地点補正後保持力平均です。</param>
        /// <returns>全サンプル座標と地質を生成できた場合はtrueです。</returns>
        internal static bool TrySampleAdjustedGeologyAverage(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out ushort adjustedPermeability,
            out ushort adjustedGroundwaterStorage)
        {
            adjustedPermeability = default;
            adjustedGroundwaterStorage = default;
            int cellSize = settings.Hydrology.CellSizeInTiles;
            int sampleOffset = settings.Hydrology.RepresentativeSampleOffsetInTiles;
            long centerX = (long)cellX * cellSize;
            long centerY = (long)cellY * cellSize;
            long permeabilityTotal = 0L;
            long storageTotal = 0L;
            for (int sampleYIndex = -1; sampleYIndex <= 1; sampleYIndex++)
            {
                for (int sampleXIndex = -1; sampleXIndex <= 1; sampleXIndex++)
                {
                    long sampleX = centerX + ((long)sampleXIndex * sampleOffset);
                    long sampleY = centerY + ((long)sampleYIndex * sampleOffset);
                    if (sampleX < int.MinValue || sampleX > int.MaxValue
                        || sampleY < int.MinValue || sampleY > int.MaxValue
                        || !DeterministicGeologyGenerator.TryGenerate(
                            settings,
                            (int)sampleX,
                            (int)sampleY,
                            out GeneratedGeologyData geology)
                        || !DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                            settings,
                            (int)sampleX,
                            (int)sampleY,
                            geology.Permeability,
                            geology.GroundwaterStorage,
                            out AdjustedGroundwaterGeology adjusted))
                    {
                        return false;
                    }

                    permeabilityTotal += adjusted.Permeability;
                    storageTotal += adjusted.GroundwaterStorage;
                }
            }

            adjustedPermeability = (ushort)(permeabilityTotal / 9L);
            adjustedGroundwaterStorage = (ushort)(storageTotal / 9L);
            return true;
        }

        /// <summary>チャンク左下タイルと、水文Bufferの先頭セル座標を64bit中間計算から求めます。</summary>
        /// <param name="chunkX">水平論理チャンク座標です。</param>
        /// <param name="chunkY">垂直論理チャンク座標です。</param>
        /// <param name="minimumTileX">成功時のチャンク左端絶対タイルXです。</param>
        /// <param name="minimumTileY">成功時のチャンク下端絶対タイルYです。</param>
        /// <param name="firstCellX">成功時の先頭水平水文セル座標です。</param>
        /// <param name="firstCellY">成功時の先頭垂直水文セル座標です。</param>
        /// <returns>64タイル幅と9セル幅をint座標域で表せる場合はtrueです。</returns>
        private static bool TryGetChunkOrigins(
            int chunkX,
            int chunkY,
            out int minimumTileX,
            out int minimumTileY,
            out int firstCellX,
            out int firstCellY)
        {
            long tileX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long tileY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long cellX = (long)chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long cellY = (long)chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            if (tileX < int.MinValue
                || tileX + WorldGridConstants.LogicalChunkSize - 1L > int.MaxValue
                || tileY < int.MinValue
                || tileY + WorldGridConstants.LogicalChunkSize - 1L > int.MaxValue
                || cellX < int.MinValue
                || cellX + IntersectingCellsPerAxis - 1L > int.MaxValue
                || cellY < int.MinValue
                || cellY + IntersectingCellsPerAxis - 1L > int.MaxValue)
            {
                minimumTileX = default;
                minimumTileY = default;
                firstCellX = default;
                firstCellY = default;
                return false;
            }

            minimumTileX = (int)tileX;
            minimumTileY = (int)tileY;
            firstCellX = (int)cellX;
            firstCellY = (int)cellY;
            return true;
        }
    }
}
