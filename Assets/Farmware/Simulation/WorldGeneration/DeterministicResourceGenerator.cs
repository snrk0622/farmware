// このファイルは、通常生成済みタイルの適性、用途別ノイズ、決定論的な群中心から基礎資源を配置します。
// 同種の資源群には設定された最低間隔を保証し、異種の群が重なるタイルは安定スコアで1種類へ解決します。
// 水はGeneratedResourceTileへ配置せず、GeneratedTileの淡水・海水属性そのものを採水源として扱います。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1タイルの資源生成判定を開発者表示とテストから確認するための純粋データです。
    /// 実際のセーブ対象ではなく、通常生成と同じ計算を必要な座標だけ再実行して取得します。
    /// </summary>
    public readonly struct ResourceGenerationDebugInfo
    {
        /// <summary>配置競合を解決した後、このタイルへ最終配置する資源IDです。</summary>
        public readonly GeneratedResourceType SelectedType;

        /// <summary>選択された資源群を所有する中心アンカーの水平ワールドタイル座標です。</summary>
        public readonly int AnchorTileX;

        /// <summary>選択された資源群を所有する中心アンカーの垂直ワールドタイル座標です。</summary>
        public readonly int AnchorTileY;

        /// <summary>選択された同種資源群の中心同士へ保証する最低距離です。単位はタイルです。</summary>
        public readonly int MinimumSpacingInTiles;

        /// <summary>選択された資源群が中心から広がれる最大半径です。単位はタイルです。</summary>
        public readonly int ClusterRadiusInTiles;

        /// <summary>選択された資源について、このタイルで得られた0〜65535の群生ノイズです。</summary>
        public readonly ushort TileNoise;

        /// <summary>選択された資源について、群中心で得られた0〜65535の群生ノイズです。</summary>
        public readonly ushort AnchorNoise;

        /// <summary>地形適性、ノイズ下限、群半径を満たしてこのタイルを要求した候補群の数です。</summary>
        public readonly int EligibleCandidateCount;

        /// <summary>
        /// 通常生成と同じ判定結果をまとめます。
        /// </summary>
        /// <param name="selectedType">競合解決後の資源IDです。</param>
        /// <param name="anchorTileX">選択された群中心の水平座標です。</param>
        /// <param name="anchorTileY">選択された群中心の垂直座標です。</param>
        /// <param name="minimumSpacingInTiles">同種群中心の最低間隔です。</param>
        /// <param name="clusterRadiusInTiles">群の最大半径です。</param>
        /// <param name="tileNoise">タイル位置の群生ノイズです。</param>
        /// <param name="anchorNoise">群中心位置の群生ノイズです。</param>
        /// <param name="eligibleCandidateCount">このタイルを要求した候補群数です。</param>
        public ResourceGenerationDebugInfo(
            GeneratedResourceType selectedType,
            int anchorTileX,
            int anchorTileY,
            int minimumSpacingInTiles,
            int clusterRadiusInTiles,
            ushort tileNoise,
            ushort anchorNoise,
            int eligibleCandidateCount)
        {
            SelectedType = selectedType;
            AnchorTileX = anchorTileX;
            AnchorTileY = anchorTileY;
            MinimumSpacingInTiles = minimumSpacingInTiles;
            ClusterRadiusInTiles = clusterRadiusInTiles;
            TileNoise = tileNoise;
            AnchorNoise = anchorNoise;
            EligibleCandidateCount = eligibleCandidateCount;
        }
    }

    /// <summary>
    /// 1タイルの地形適性を尊重しながら、種類別ノイズと最低間隔付き群アンカーから基礎資源を選びます。
    /// UnityEngine.Randomや生成済み隣接チャンクを使わないため、Burst Jobと初期位置探索から同じ結果を得られます。
    /// </summary>
    public static class DeterministicResourceGenerator
    {
        /// <summary>
        /// 指定ワールドタイルへ配置する基礎資源と表示差分を生成します。
        /// </summary>
        /// <param name="worldSettings">シードと生成器バージョンを保持するワールド設定です。</param>
        /// <param name="resourceSettings">群れサイズ、最低間隔、種類別密度を保持する資源設定です。</param>
        /// <param name="terrainTile">同じ座標ですでに生成された地形・資源適性です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>配置なしを含む1タイル分の資源データです。</returns>
        public static GeneratedResourceTile GenerateResource(
            in WorldGenerationSettings worldSettings,
            in ResourceGenerationSettings resourceSettings,
            in GeneratedTile terrainTile,
            int worldTileX,
            int worldTileY)
        {
            ResourceGenerationDebugInfo debugInfo = SelectResource(
                worldSettings,
                resourceSettings,
                terrainTile,
                worldTileX,
                worldTileY);
            if (debugInfo.SelectedType == GeneratedResourceType.None)
            {
                return default;
            }

            ulong variantHash = WorldSeedUtility.HashCoordinate(
                worldSettings.WorldSeed,
                worldSettings.GeneratorVersion,
                worldTileX,
                worldTileY,
                WorldGenerationPurposeSalts.ResourceVisualVariant,
                (uint)debugInfo.SelectedType);
            return new GeneratedResourceTile
            {
                Type = debugInfo.SelectedType,
                VisualVariant = (byte)(variantHash & 3UL)
            };
        }

        /// <summary>
        /// 通常生成と同じ資源判定を行い、選択された群中心と競合数を開発者向けに返します。
        /// </summary>
        /// <param name="worldSettings">シードと生成器バージョンを保持するワールド設定です。</param>
        /// <param name="resourceSettings">群れサイズ、最低間隔、種類別密度を保持する資源設定です。</param>
        /// <param name="terrainTile">同じ座標の生成済み地形です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>最終資源と、その判定に使った群中心情報です。</returns>
        public static ResourceGenerationDebugInfo GetDebugInfo(
            in WorldGenerationSettings worldSettings,
            in ResourceGenerationSettings resourceSettings,
            in GeneratedTile terrainTile,
            int worldTileX,
            int worldTileY)
        {
            return SelectResource(
                worldSettings,
                resourceSettings,
                terrainTile,
                worldTileX,
                worldTileY);
        }

        /// <summary>
        /// 木・石・砂の候補群を列挙し、同じタイルで競合した場合は正規化ノイズ強度と安定ハッシュで1種類へ決めます。
        /// </summary>
        /// <param name="worldSettings">ワールドシードと生成器バージョンです。</param>
        /// <param name="resourceSettings">資源生成の全調整値です。</param>
        /// <param name="terrainTile">資源適性を含む生成済み地形です。</param>
        /// <param name="worldTileX">水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">垂直ワールドタイル座標です。</param>
        /// <returns>競合解決後の資源生成診断値です。</returns>
        private static ResourceGenerationDebugInfo SelectResource(
            in WorldGenerationSettings worldSettings,
            in ResourceGenerationSettings resourceSettings,
            in GeneratedTile terrainTile,
            int worldTileX,
            int worldTileY)
        {
            GeneratedResourceType selectedType = GeneratedResourceType.None;
            int selectedScore = -1;
            ulong selectedTieBreaker = 0UL;
            int selectedAnchorX = 0;
            int selectedAnchorY = 0;
            int selectedMinimumSpacing = 0;
            int selectedClusterRadius = 0;
            ushort selectedTileNoise = 0;
            ushort selectedAnchorNoise = 0;
            int eligibleCandidateCount = 0;

            // 3種類は同じ予約レイヤーです。地形適性外は候補にせず、重なりだけを共通スコアで公平に解決します。
            TrySelectResource(
                ResourceSuitability.Tree,
                GeneratedResourceType.Tree,
                WorldGenerationPurposeSalts.TreeResource,
                resourceSettings.TreeThreshold,
                resourceSettings.TreeMinimumSpacingInTiles,
                resourceSettings.TreeClusterRadiusInTiles,
                terrainTile.ResourceSuitability,
                worldSettings,
                resourceSettings,
                worldTileX,
                worldTileY,
                ref selectedType,
                ref selectedScore,
                ref selectedTieBreaker,
                ref selectedAnchorX,
                ref selectedAnchorY,
                ref selectedMinimumSpacing,
                ref selectedClusterRadius,
                ref selectedTileNoise,
                ref selectedAnchorNoise,
                ref eligibleCandidateCount);
            TrySelectResource(
                ResourceSuitability.Stone,
                GeneratedResourceType.Stone,
                WorldGenerationPurposeSalts.StoneResource,
                resourceSettings.StoneThreshold,
                resourceSettings.StoneMinimumSpacingInTiles,
                resourceSettings.StoneClusterRadiusInTiles,
                terrainTile.ResourceSuitability,
                worldSettings,
                resourceSettings,
                worldTileX,
                worldTileY,
                ref selectedType,
                ref selectedScore,
                ref selectedTieBreaker,
                ref selectedAnchorX,
                ref selectedAnchorY,
                ref selectedMinimumSpacing,
                ref selectedClusterRadius,
                ref selectedTileNoise,
                ref selectedAnchorNoise,
                ref eligibleCandidateCount);
            TrySelectResource(
                ResourceSuitability.Sand,
                GeneratedResourceType.Sand,
                WorldGenerationPurposeSalts.SandResource,
                resourceSettings.SandThreshold,
                resourceSettings.SandMinimumSpacingInTiles,
                resourceSettings.SandClusterRadiusInTiles,
                terrainTile.ResourceSuitability,
                worldSettings,
                resourceSettings,
                worldTileX,
                worldTileY,
                ref selectedType,
                ref selectedScore,
                ref selectedTieBreaker,
                ref selectedAnchorX,
                ref selectedAnchorY,
                ref selectedMinimumSpacing,
                ref selectedClusterRadius,
                ref selectedTileNoise,
                ref selectedAnchorNoise,
                ref eligibleCandidateCount);

            return new ResourceGenerationDebugInfo(
                selectedType,
                selectedAnchorX,
                selectedAnchorY,
                selectedMinimumSpacing,
                selectedClusterRadius,
                selectedTileNoise,
                selectedAnchorNoise,
                eligibleCandidateCount);
        }

        /// <summary>
        /// 1種類の資源について周囲9セルの群中心を調べ、現在タイルを覆う有効候補を競合解決へ渡します。
        /// </summary>
        /// <param name="requiredSuitability">対象資源を置ける地形適性フラグです。</param>
        /// <param name="resourceType">条件を満たした場合に選ぶ安定資源IDです。</param>
        /// <param name="purposeSalt">他資源から独立したノイズと群中心を得る用途別ソルトです。</param>
        /// <param name="threshold">この値以上で配置候補になる0〜65535の下限です。</param>
        /// <param name="minimumSpacingInTiles">同種群中心へ保証する最低距離です。</param>
        /// <param name="clusterRadiusInTiles">群中心から配置できる最大半径です。</param>
        /// <param name="terrainSuitability">生成済み地形が持つ資源適性フラグです。</param>
        /// <param name="worldSettings">ワールドシードと生成器バージョンです。</param>
        /// <param name="resourceSettings">ノイズ格子サイズとオクターブ数です。</param>
        /// <param name="worldTileX">水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">垂直ワールドタイル座標です。</param>
        /// <param name="selectedType">現在選ばれている資源IDです。</param>
        /// <param name="selectedScore">現在候補の正規化済みノイズ強度です。</param>
        /// <param name="selectedTieBreaker">同点競合を生成順に依存せず解決する安定値です。</param>
        /// <param name="selectedAnchorX">現在候補の群中心水平座標です。</param>
        /// <param name="selectedAnchorY">現在候補の群中心垂直座標です。</param>
        /// <param name="selectedMinimumSpacing">現在候補の最低間隔です。</param>
        /// <param name="selectedClusterRadius">現在候補の群半径です。</param>
        /// <param name="selectedTileNoise">現在候補のタイル位置ノイズです。</param>
        /// <param name="selectedAnchorNoise">現在候補の群中心ノイズです。</param>
        /// <param name="eligibleCandidateCount">このタイルを要求した全候補群数です。</param>
        private static void TrySelectResource(
            ResourceSuitability requiredSuitability,
            GeneratedResourceType resourceType,
            ulong purposeSalt,
            ushort threshold,
            int minimumSpacingInTiles,
            int clusterRadiusInTiles,
            ResourceSuitability terrainSuitability,
            in WorldGenerationSettings worldSettings,
            in ResourceGenerationSettings resourceSettings,
            int worldTileX,
            int worldTileY,
            ref GeneratedResourceType selectedType,
            ref int selectedScore,
            ref ulong selectedTieBreaker,
            ref int selectedAnchorX,
            ref int selectedAnchorY,
            ref int selectedMinimumSpacing,
            ref int selectedClusterRadius,
            ref ushort selectedTileNoise,
            ref ushort selectedAnchorNoise,
            ref int eligibleCandidateCount)
        {
            if ((terrainSuitability & requiredSuitability) == 0)
            {
                return;
            }

            int safeNoiseCellSize = resourceSettings.ClusterCellSizeInTiles < 4
                ? 4
                : resourceSettings.ClusterCellSizeInTiles;
            int safeOctaveCount = resourceSettings.OctaveCount < 1
                ? 1
                : resourceSettings.OctaveCount;
            int safeMinimumSpacing = minimumSpacingInTiles < 2 ? 2 : minimumSpacingInTiles;
            int maximumRadius = safeMinimumSpacing / 2;
            int safeClusterRadius = clusterRadiusInTiles < 1 ? 1 : clusterRadiusInTiles;
            if (safeClusterRadius > maximumRadius)
            {
                safeClusterRadius = maximumRadius;
            }

            ushort tileNoise = DeterministicNoise.SampleFractalNoise(
                worldSettings.WorldSeed,
                worldSettings.GeneratorVersion,
                worldTileX,
                worldTileY,
                safeNoiseCellSize,
                safeOctaveCount,
                purposeSalt);
            if (tileNoise < threshold)
            {
                return;
            }

            int jitterAmplitude = safeMinimumSpacing / 4;
            if (jitterAmplitude < 1)
            {
                jitterAmplitude = 1;
            }

            // セル幅から両側の最大ジッターを引いても最低間隔が残るため、隣接セルの中心同士は必ず設定距離以上離れます。
            int anchorCellSize = safeMinimumSpacing + (jitterAmplitude * 2);
            int ownerCellX = WorldGrid.FloorDivide(worldTileX, anchorCellSize);
            int ownerCellY = WorldGrid.FloorDivide(worldTileY, anchorCellSize);
            int clusterRadiusSquared = safeClusterRadius * safeClusterRadius;

            // 半径は最低間隔の半分以下なので、現在セルと8近傍だけ見ればタイルを覆いうる全群中心を列挙できます。
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int anchorCellX = ownerCellX + offsetX;
                    int anchorCellY = ownerCellY + offsetY;
                    CalculateAnchorPosition(
                        worldSettings,
                        purposeSalt,
                        anchorCellX,
                        anchorCellY,
                        anchorCellSize,
                        jitterAmplitude,
                        out int anchorTileX,
                        out int anchorTileY);
                    int deltaX = worldTileX - anchorTileX;
                    int deltaY = worldTileY - anchorTileY;
                    int distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared > clusterRadiusSquared)
                    {
                        continue;
                    }

                    ushort anchorNoise = DeterministicNoise.SampleFractalNoise(
                        worldSettings.WorldSeed,
                        worldSettings.GeneratorVersion,
                        anchorTileX,
                        anchorTileY,
                        safeNoiseCellSize,
                        safeOctaveCount,
                        purposeSalt);
                    if (anchorNoise < threshold)
                    {
                        continue;
                    }

                    eligibleCandidateCount++;
                    int score = CalculateNormalizedExcess(tileNoise, threshold)
                        + CalculateNormalizedExcess(anchorNoise, threshold);
                    ulong tieBreaker = WorldSeedUtility.HashCoordinate(
                        worldSettings.WorldSeed,
                        worldSettings.GeneratorVersion,
                        anchorTileX,
                        anchorTileY,
                        purposeSalt,
                        (uint)resourceType);

                    // 全候補を見てから最大値を採用するため、木・石・砂の評価順を変えても同点以外の結果は変わりません。
                    if (score < selectedScore
                        || (score == selectedScore && tieBreaker <= selectedTieBreaker))
                    {
                        continue;
                    }

                    selectedType = resourceType;
                    selectedScore = score;
                    selectedTieBreaker = tieBreaker;
                    selectedAnchorX = anchorTileX;
                    selectedAnchorY = anchorTileY;
                    selectedMinimumSpacing = safeMinimumSpacing;
                    selectedClusterRadius = safeClusterRadius;
                    selectedTileNoise = tileNoise;
                    selectedAnchorNoise = anchorNoise;
                }
            }
        }

        /// <summary>
        /// 1つの最低間隔セルへ、中央から一定範囲だけ揺らした群中心を決定論的に配置します。
        /// </summary>
        /// <param name="worldSettings">アンカー位置を固定するシードと生成器バージョンです。</param>
        /// <param name="purposeSalt">資源種類ごとに独立した用途別ソルトです。</param>
        /// <param name="anchorCellX">符号付きアンカーセル水平座標です。</param>
        /// <param name="anchorCellY">符号付きアンカーセル垂直座標です。</param>
        /// <param name="anchorCellSize">最低間隔とジッター余白を含むセル幅です。</param>
        /// <param name="jitterAmplitude">セル中央から各軸へ動かせる最大タイル数です。</param>
        /// <param name="anchorTileX">計算された群中心の水平ワールドタイル座標です。</param>
        /// <param name="anchorTileY">計算された群中心の垂直ワールドタイル座標です。</param>
        private static void CalculateAnchorPosition(
            in WorldGenerationSettings worldSettings,
            ulong purposeSalt,
            int anchorCellX,
            int anchorCellY,
            int anchorCellSize,
            int jitterAmplitude,
            out int anchorTileX,
            out int anchorTileY)
        {
            ulong anchorHash = WorldSeedUtility.HashCoordinate(
                worldSettings.WorldSeed,
                worldSettings.GeneratorVersion,
                anchorCellX,
                anchorCellY,
                purposeSalt,
                0xA11CEu);
            int jitterRange = (jitterAmplitude * 2) + 1;
            int jitterX = (int)(anchorHash % (ulong)jitterRange) - jitterAmplitude;
            int jitterY = (int)((anchorHash >> 32) % (ulong)jitterRange) - jitterAmplitude;
            int cellCenterOffset = anchorCellSize / 2;
            anchorTileX = (anchorCellX * anchorCellSize) + cellCenterOffset + jitterX;
            anchorTileY = (anchorCellY * anchorCellSize) + cellCenterOffset + jitterY;
        }

        /// <summary>
        /// 種類ごとに異なる閾値超過量を0〜65535へ正規化し、競合比較で高閾値の資源が不利にならないようにします。
        /// </summary>
        /// <param name="sample">比較する0〜65535のノイズ値です。</param>
        /// <param name="threshold">その資源の配置下限です。</param>
        /// <returns>閾値ちょうどを0、最大ノイズを65535とする整数スコアです。</returns>
        private static int CalculateNormalizedExcess(ushort sample, ushort threshold)
        {
            int availableRange = ushort.MaxValue - threshold;
            if (availableRange <= 0)
            {
                return sample >= threshold ? ushort.MaxValue : 0;
            }

            // 65535×65535はint上限を超えるため、乗算だけ64bitへ広げてから0〜65535へ戻します。
            return (int)(((long)(sample - threshold) * ushort.MaxValue) / availableRange);
        }

        /// <summary>
        /// 1つの資源IDを初期位置探索用のビットフラグへ変換します。
        /// </summary>
        /// <param name="resourceType">変換する基礎資源の安定IDです。</param>
        /// <returns>対応する1bit、または配置なしの場合はNoneです。</returns>
        public static NaturalResourceTypeMask ToMask(GeneratedResourceType resourceType)
        {
            switch (resourceType)
            {
                case GeneratedResourceType.Tree:
                    return NaturalResourceTypeMask.Tree;
                case GeneratedResourceType.Stone:
                    return NaturalResourceTypeMask.Stone;
                case GeneratedResourceType.Sand:
                    return NaturalResourceTypeMask.Sand;
                default:
                    return NaturalResourceTypeMask.None;
            }
        }
    }
}
