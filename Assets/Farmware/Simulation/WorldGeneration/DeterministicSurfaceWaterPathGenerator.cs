// このファイルは、8×8水文セルのD8辺をタイル単位の河道中心線へ細分化します。
// DeterministicHydrologyGeneratorの流向・集水結果を正として読み、後続の表流水Buffer生成とテストが同じ有限経路探索を共有します。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 河道中心線に含まれる1タイルの絶対整数座標です。
    /// C4-A1の純粋経路結果として使い、ECS Bufferへ格納する最終表流水属性とは分離します。
    /// </summary>
    public struct SurfaceWaterPathTile
    {
        /// <summary>河道中心線上の水平ワールドタイル座標です。</summary>
        public int WorldTileX;

        /// <summary>河道中心線上の垂直ワールドタイル座標です。</summary>
        public int WorldTileY;
    }

    /// <summary>
    /// 有限な河道経路探索で1タイル分の費用、直前位置、探索状態を保持します。
    /// 呼出し側が固定長NativeArrayを所有し、経路ごとに再利用することでHot Pathのmanaged allocationを避けます。
    /// </summary>
    internal struct SurfaceWaterPathSearchNode
    {
        /// <summary>開始点からこのタイルまでに通った最大標高です。低い経路を最優先するために使います。</summary>
        public ulong MaximumElevation;

        /// <summary>開始点からの正の上り標高差だけを合計した値です。</summary>
        public ulong TotalUphill;

        /// <summary>開始点から進んだ8近傍辺の数です。</summary>
        public ulong EdgeCount;

        /// <summary>経路上の標高合計です。最大標高と上り量が同じ経路の低地側を優先します。</summary>
        public ulong TotalElevation;

        /// <summary>同じ標高費用を安定して解決する、各タイルの専用Hash下位16bitの合計です。</summary>
        public ulong StableRankSum;

        /// <summary>最小費用を更新した直前ノードの配列添字です。開始点と未到達では-1です。</summary>
        public int PreviousIndex;

        /// <summary>このタイル自身の固定連続標高です。</summary>
        public ushort Elevation;

        /// <summary>0は未到達、1は探索候補、2は最小費用確定済みを表します。</summary>
        public byte State;
    }

    /// <summary>
    /// 水文セル内の安定Anchorと、隣接するD8セル間の河道中心線を決定論的に生成します。
    /// 最大16×16タイルだけを探索し、ロード済みチャンク、生成順、UnityEngine.Randomを参照しません。
    /// </summary>
    public static class DeterministicSurfaceWaterPathGenerator
    {
        /// <summary>隣接する2つの8×8担当範囲を含む、経路探索ノード数の固定上限です。</summary>
        public const int MaximumSearchNodeCount = 16 * 16;

        /// <summary>水文セル中心から担当範囲の最小タイルまでの固定オフセットです。</summary>
        private const int ResponsibilityMinimumOffset = -4;

        /// <summary>水文セル中心から担当範囲の最大タイルまでの固定オフセットです。</summary>
        private const int ResponsibilityMaximumOffset = 3;

        /// <summary>
        /// 水文セルが担当する8×8タイルから最低標高の河道Anchorを一つ選びます。
        /// 同じ最低標高では専用Salt、絶対X、Yの順で決め、候補列挙順による差を残しません。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、標高、水文格子を固定した生成設定です。</param>
        /// <param name="cellX">河道Anchorを選ぶ水平水文セル座標です。</param>
        /// <param name="cellY">河道Anchorを選ぶ垂直水文セル座標です。</param>
        /// <param name="anchor">成功時に最低標高の絶対タイル座標を受け取ります。</param>
        /// <param name="anchorElevation">成功時にAnchorの連続標高を受け取ります。</param>
        /// <returns>設定と64タイルの固定サンプル座標が有効ならtrueです。</returns>
        public static bool TryFindChannelAnchor(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out SurfaceWaterPathTile anchor,
            out ushort anchorElevation)
        {
            anchor = default;
            anchorElevation = default;
            if (!DeterministicEnvironmentGenerator.ValidateSettings(settings)
                || !DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || !TryGetResponsibilityBounds(cellX, cellY, out int minimumX, out int minimumY,
                    out int maximumX, out int maximumY))
            {
                return false;
            }

            bool found = false;
            ulong bestRank = ulong.MaxValue;
            for (int worldTileY = minimumY; worldTileY <= maximumY; worldTileY++)
            {
                for (int worldTileX = minimumX; worldTileX <= maximumX; worldTileX++)
                {
                    if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            worldTileX,
                            worldTileY,
                            out GeneratedEnvironmentData environment))
                    {
                        // 64件の一部だけでAnchorを推測すると境界側チャンクと結果が食い違うため、全体を失敗させます。
                        anchor = default;
                        anchorElevation = default;
                        return false;
                    }

                    ulong rank = WorldSeedUtility.HashCoordinate(
                        settings.WorldSeed,
                        settings.GeneratorVersion,
                        worldTileX,
                        worldTileY,
                        WorldGenerationPurposeSalts.HydrologyChannelAnchor);
                    if (!found
                        || environment.Elevation < anchorElevation
                        || (environment.Elevation == anchorElevation
                            && IsAnchorTieEarlier(worldTileX, worldTileY, rank, anchor, bestRank)))
                    {
                        found = true;
                        anchor = new SurfaceWaterPathTile
                        {
                            WorldTileX = worldTileX,
                            WorldTileY = worldTileY
                        };
                        anchorElevation = environment.Elevation;
                        bestRank = rank;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// StreamまたはRiverを持つ1本のD8辺について、開始Anchorから下流Anchorまでの中心線を生成します。
        /// 経路費用は最大標高、上り合計、辺数、標高合計、安定順位の順で比較します。
        /// </summary>
        /// <param name="settings">Anchorと全経路タイルの標高を再生成する固定設定です。</param>
        /// <param name="sourceCell">StreamまたはRiverと隣接下流セルを持つ生成済み水文セルです。</param>
        /// <param name="workspace">最大256件を保持する、呼出し側所有の探索作業配列です。</param>
        /// <param name="pathTiles">開始点から終了点までの順で結果を書き込む256件以上の配列です。</param>
        /// <param name="pathLength">成功時に有効な経路タイル数を受け取ります。</param>
        /// <returns>隣接D8辺と探索範囲が有効で、下流Anchorまで到達できた場合はtrueです。</returns>
        internal static bool TryBuildChannelCenterLine(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell sourceCell,
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            out int pathLength)
        {
            pathLength = 0;
            HydrologyConnectionFlags surfaceWater = sourceCell.ConnectionFlags
                & (HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River);
            long deltaCellX = (long)sourceCell.DownstreamCellX - sourceCell.CellX;
            long deltaCellY = (long)sourceCell.DownstreamCellY - sourceCell.CellY;
            // 座標だけが隣接していてもFlowDirectionと食い違えば壊れたD8辺なので、部分的な河道を作る前に拒否します。
            DeterministicHydrologyCatchmentResolver.GetDirectionOffset(
                sourceCell.FlowDirection,
                out int expectedDeltaX,
                out int expectedDeltaY);
            if (surfaceWater == HydrologyConnectionFlags.None
                || !sourceCell.HasDownstream
                || deltaCellX < -1 || deltaCellX > 1
                || deltaCellY < -1 || deltaCellY > 1
                || (deltaCellX == 0 && deltaCellY == 0)
                || deltaCellX != expectedDeltaX || deltaCellY != expectedDeltaY
                || !workspace.IsCreated || workspace.Length < MaximumSearchNodeCount
                || !pathTiles.IsCreated || pathTiles.Length < MaximumSearchNodeCount
                || !TryFindChannelAnchor(settings, sourceCell.CellX, sourceCell.CellY,
                    out SurfaceWaterPathTile start, out ushort startElevation)
                || !TryFindChannelAnchor(settings, sourceCell.DownstreamCellX, sourceCell.DownstreamCellY,
                    out SurfaceWaterPathTile destination, out ushort destinationElevation))
            {
                return false;
            }

            // Anchor位置から単純に矩形を広げると最大23幅になり得るため、実際の2セル担当範囲へ探索矩形を固定します。
            if (!TryGetCombinedResponsibilityBounds(
                    sourceCell.CellX,
                    sourceCell.CellY,
                    sourceCell.DownstreamCellX,
                    sourceCell.DownstreamCellY,
                    out int minimumX,
                    out int minimumY,
                    out int maximumX,
                    out int maximumY))
            {
                return false;
            }

            int width = maximumX - minimumX + 1;
            int height = maximumY - minimumY + 1;
            int nodeCount = width * height;
            if (width <= 0 || height <= 0 || width > 16 || height > 16
                || nodeCount > MaximumSearchNodeCount)
            {
                return false;
            }

            // 全ノードの標高を先に固定し、探索途中の失敗で部分的な経路を返さないようにします。
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                int worldTileX = minimumX + (nodeIndex % width);
                int worldTileY = minimumY + (nodeIndex / width);
                if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                        settings,
                        worldTileX,
                        worldTileY,
                        out GeneratedEnvironmentData environment))
                {
                    return false;
                }

                workspace[nodeIndex] = new SurfaceWaterPathSearchNode
                {
                    MaximumElevation = ulong.MaxValue,
                    TotalUphill = ulong.MaxValue,
                    EdgeCount = ulong.MaxValue,
                    TotalElevation = ulong.MaxValue,
                    StableRankSum = ulong.MaxValue,
                    PreviousIndex = -1,
                    Elevation = environment.Elevation,
                    State = 0
                };
            }

            int startIndex = ((start.WorldTileY - minimumY) * width) + start.WorldTileX - minimumX;
            int destinationIndex = ((destination.WorldTileY - minimumY) * width)
                + destination.WorldTileX - minimumX;
            if (workspace[startIndex].Elevation != startElevation
                || workspace[destinationIndex].Elevation != destinationElevation)
            {
                return false;
            }

            return TryBuildPreparedCenterLine(
                settings,
                minimumX,
                minimumY,
                width,
                height,
                startIndex,
                destinationIndex,
                workspace,
                pathTiles,
                out pathLength);
        }

        /// <summary>
        /// 標高を格納済みの有限矩形から、仕様どおりの辞書順費用で中心線を求めます。
        /// 最大標高だけを先に確定し、その標高以下へ制限した第2探索で残りの加算費用を比較します。
        /// </summary>
        /// <param name="settings">経路同点用のSeedとGeneratorVersionを持つ固定設定です。</param>
        /// <param name="minimumX">探索矩形の左端に対応する絶対タイルXです。</param>
        /// <param name="minimumY">探索矩形の下端に対応する絶対タイルYです。</param>
        /// <param name="width">探索矩形の幅です。1〜16だけを受理します。</param>
        /// <param name="height">探索矩形の高さです。1〜16だけを受理します。</param>
        /// <param name="startIndex">行優先配列内の開始ノード添字です。</param>
        /// <param name="destinationIndex">行優先配列内の終了ノード添字です。</param>
        /// <param name="workspace">各要素のElevationを設定済みの、呼出し側所有作業配列です。</param>
        /// <param name="pathTiles">開始から終了までの絶対座標を書き込む結果配列です。</param>
        /// <param name="pathLength">成功時の有効な結果数です。失敗時は0です。</param>
        /// <returns>有限矩形内で開始から終了へ到達できた場合はtrueです。</returns>
        internal static bool TryBuildPreparedCenterLine(
            in WorldGenerationSettings settings,
            int minimumX,
            int minimumY,
            int width,
            int height,
            int startIndex,
            int destinationIndex,
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            out int pathLength)
        {
            pathLength = 0;
            if (!DeterministicEnvironmentGenerator.ValidateSettings(settings)
                || width <= 0 || height <= 0 || width > 16 || height > 16)
            {
                return false;
            }

            int nodeCount = width * height;
            if (nodeCount > MaximumSearchNodeCount
                || startIndex < 0 || startIndex >= nodeCount
                || destinationIndex < 0 || destinationIndex >= nodeCount
                || !workspace.IsCreated || workspace.Length < nodeCount
                || !pathTiles.IsCreated || pathTiles.Length < nodeCount)
            {
                return false;
            }

            PrepareSearchNodes(workspace, nodeCount);
            SurfaceWaterPathSearchNode startNode = workspace[startIndex];
            startNode.MaximumElevation = startNode.Elevation;
            startNode.State = 1;
            workspace[startIndex] = startNode;

            // 最大標高と残りの費用を一度に確定すると、後段の高地で最大値が同じになった別経路を早く捨てる場合があります。
            // そのため第1探索は最小の最大標高だけを求め、第2探索が通ってよい標高上限として固定します。
            for (int iteration = 0; iteration < nodeCount; iteration++)
            {
                int currentIndex = FindLowestOpenNodeByMaximumElevation(workspace, nodeCount);
                if (currentIndex < 0)
                {
                    return false;
                }

                SurfaceWaterPathSearchNode current = workspace[currentIndex];
                current.State = 2;
                workspace[currentIndex] = current;
                if (currentIndex == destinationIndex)
                {
                    break;
                }

                int currentLocalX = currentIndex % width;
                int currentLocalY = currentIndex / width;
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if ((offsetX == 0 && offsetY == 0)
                            || currentLocalX + offsetX < 0 || currentLocalX + offsetX >= width
                            || currentLocalY + offsetY < 0 || currentLocalY + offsetY >= height)
                        {
                            continue;
                        }

                        int neighborIndex = ((currentLocalY + offsetY) * width)
                            + currentLocalX + offsetX;
                        SurfaceWaterPathSearchNode neighbor = workspace[neighborIndex];
                        if (neighbor.State == 2)
                        {
                            continue;
                        }

                        ulong candidateMaximum = current.MaximumElevation > neighbor.Elevation
                            ? current.MaximumElevation
                            : neighbor.Elevation;
                        if (neighbor.State == 0
                            || candidateMaximum < neighbor.MaximumElevation
                            || (candidateMaximum == neighbor.MaximumElevation
                                && currentIndex < neighbor.PreviousIndex))
                        {
                            neighbor.MaximumElevation = candidateMaximum;
                            neighbor.PreviousIndex = currentIndex;
                            neighbor.State = 1;
                            workspace[neighborIndex] = neighbor;
                        }
                    }
                }
            }

            if (workspace[destinationIndex].State != 2)
            {
                return false;
            }

            ulong minimumMaximumElevation = workspace[destinationIndex].MaximumElevation;
            PrepareSearchNodes(workspace, nodeCount);
            startNode = workspace[startIndex];
            startNode.MaximumElevation = minimumMaximumElevation;
            startNode.TotalUphill = 0;
            startNode.EdgeCount = 0;
            startNode.TotalElevation = startNode.Elevation;
            startNode.StableRankSum = GetStablePathRank(
                settings,
                minimumX + (startIndex % width),
                minimumY + (startIndex / width));
            startNode.State = 1;
            workspace[startIndex] = startNode;

            for (int iteration = 0; iteration < nodeCount; iteration++)
            {
                int currentIndex = FindLowestOpenNode(workspace, nodeCount);
                if (currentIndex < 0)
                {
                    return false;
                }

                SurfaceWaterPathSearchNode current = workspace[currentIndex];
                current.State = 2;
                workspace[currentIndex] = current;
                if (currentIndex == destinationIndex)
                {
                    break;
                }

                int currentLocalX = currentIndex % width;
                int currentLocalY = currentIndex / width;
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if ((offsetX == 0 && offsetY == 0)
                            || currentLocalX + offsetX < 0 || currentLocalX + offsetX >= width
                            || currentLocalY + offsetY < 0 || currentLocalY + offsetY >= height)
                        {
                            continue;
                        }

                        int neighborIndex = ((currentLocalY + offsetY) * width) + currentLocalX + offsetX;
                        SurfaceWaterPathSearchNode neighbor = workspace[neighborIndex];
                        if (neighbor.State == 2 || neighbor.Elevation > minimumMaximumElevation)
                        {
                            continue;
                        }

                        ulong elevation = neighbor.Elevation;
                        ulong uphill = neighbor.Elevation > current.Elevation
                            ? (ulong)(neighbor.Elevation - current.Elevation)
                            : 0UL;
                        var candidate = new SurfaceWaterPathSearchNode
                        {
                            MaximumElevation = minimumMaximumElevation,
                            TotalUphill = current.TotalUphill + uphill,
                            EdgeCount = current.EdgeCount + 1UL,
                            TotalElevation = current.TotalElevation + elevation,
                            StableRankSum = current.StableRankSum + GetStablePathRank(
                                settings,
                                minimumX + currentLocalX + offsetX,
                                minimumY + currentLocalY + offsetY),
                            PreviousIndex = currentIndex,
                            Elevation = neighbor.Elevation,
                            State = 1
                        };
                        if (neighbor.State == 0 || CompareCost(candidate, neighbor) < 0
                            || (CompareCost(candidate, neighbor) == 0
                                && currentIndex < neighbor.PreviousIndex))
                        {
                            workspace[neighborIndex] = candidate;
                        }
                    }
                }
            }

            if (workspace[destinationIndex].State != 2)
            {
                return false;
            }

            // 直前Indexは終点から始点へ向くため、一度逆順で格納してから呼出し側が使う順へ反転します。
            int cursor = destinationIndex;
            while (cursor >= 0 && pathLength < nodeCount)
            {
                pathTiles[pathLength] = new SurfaceWaterPathTile
                {
                    WorldTileX = minimumX + (cursor % width),
                    WorldTileY = minimumY + (cursor / width)
                };
                pathLength++;
                if (cursor == startIndex)
                {
                    break;
                }

                cursor = workspace[cursor].PreviousIndex;
            }

            if (pathLength == 0 || cursor != startIndex)
            {
                pathLength = 0;
                return false;
            }

            for (int left = 0, right = pathLength - 1; left < right; left++, right--)
            {
                SurfaceWaterPathTile swap = pathTiles[left];
                pathTiles[left] = pathTiles[right];
                pathTiles[right] = swap;
            }

            return true;
        }

        /// <summary>探索前に費用と状態だけを初期化し、呼出し側が用意した標高を維持します。</summary>
        /// <param name="workspace">標高を保持したまま再利用する作業配列です。</param>
        /// <param name="nodeCount">探索矩形内で初期化する先頭要素数です。</param>
        private static void PrepareSearchNodes(
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            int nodeCount)
        {
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                SurfaceWaterPathSearchNode node = workspace[nodeIndex];
                node.MaximumElevation = ulong.MaxValue;
                node.TotalUphill = ulong.MaxValue;
                node.EdgeCount = ulong.MaxValue;
                node.TotalElevation = ulong.MaxValue;
                node.StableRankSum = ulong.MaxValue;
                node.PreviousIndex = -1;
                node.State = 0;
                workspace[nodeIndex] = node;
            }
        }

        /// <summary>第1探索で、未確定候補から最大標高が最小のノードを返します。</summary>
        /// <param name="workspace">最大標高費用と探索状態を保持する作業配列です。</param>
        /// <param name="nodeCount">探索矩形内で使用中の先頭要素数です。</param>
        /// <returns>最小候補の添字です。候補がなければ-1です。</returns>
        private static int FindLowestOpenNodeByMaximumElevation(
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            int nodeCount)
        {
            int bestIndex = -1;
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                SurfaceWaterPathSearchNode node = workspace[nodeIndex];
                if (node.State != 1)
                {
                    continue;
                }

                if (bestIndex < 0
                    || node.MaximumElevation < workspace[bestIndex].MaximumElevation
                    || (node.MaximumElevation == workspace[bestIndex].MaximumElevation
                        && nodeIndex < bestIndex))
                {
                    bestIndex = nodeIndex;
                }
            }

            return bestIndex;
        }

        /// <summary>第2探索で、費用が確定していない候補から辞書順で最小のノードを返します。</summary>
        /// <param name="workspace">現在の探索費用と状態を保持する作業配列です。</param>
        /// <param name="nodeCount">探索矩形内で使用中の先頭要素数です。</param>
        /// <returns>最小候補の添字です。候補がなければ-1です。</returns>
        private static int FindLowestOpenNode(
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            int nodeCount)
        {
            int bestIndex = -1;
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                SurfaceWaterPathSearchNode node = workspace[nodeIndex];
                if (node.State != 1)
                {
                    continue;
                }

                if (bestIndex < 0 || CompareCost(node, workspace[bestIndex]) < 0
                    || (CompareCost(node, workspace[bestIndex]) == 0 && nodeIndex < bestIndex))
                {
                    bestIndex = nodeIndex;
                }
            }

            return bestIndex;
        }

        /// <summary>2経路の固定費用を、仕様で定めた優先順に比較します。</summary>
        /// <param name="left">比較する第1経路の終端ノードです。</param>
        /// <param name="right">比較する第2経路の終端ノードです。</param>
        /// <returns>leftが先なら負、同値なら0、rightが先なら正です。</returns>
        private static int CompareCost(
            in SurfaceWaterPathSearchNode left,
            in SurfaceWaterPathSearchNode right)
        {
            if (left.MaximumElevation != right.MaximumElevation)
            {
                return left.MaximumElevation < right.MaximumElevation ? -1 : 1;
            }
            if (left.TotalUphill != right.TotalUphill)
            {
                return left.TotalUphill < right.TotalUphill ? -1 : 1;
            }
            if (left.EdgeCount != right.EdgeCount)
            {
                return left.EdgeCount < right.EdgeCount ? -1 : 1;
            }
            if (left.TotalElevation != right.TotalElevation)
            {
                return left.TotalElevation < right.TotalElevation ? -1 : 1;
            }
            if (left.StableRankSum != right.StableRankSum)
            {
                return left.StableRankSum < right.StableRankSum ? -1 : 1;
            }
            return 0;
        }

        /// <summary>同じ最低標高のAnchor候補を専用Hash、X、Yの全順序で比較します。</summary>
        /// <param name="candidateX">新しい候補の水平絶対座標です。</param>
        /// <param name="candidateY">新しい候補の垂直絶対座標です。</param>
        /// <param name="candidateRank">新しい候補の専用Hash順位です。</param>
        /// <param name="current">現在選択中のAnchor座標です。</param>
        /// <param name="currentRank">現在選択中の専用Hash順位です。</param>
        /// <returns>新しい候補が全順序で先ならtrueです。</returns>
        private static bool IsAnchorTieEarlier(
            int candidateX,
            int candidateY,
            ulong candidateRank,
            in SurfaceWaterPathTile current,
            ulong currentRank)
        {
            if (candidateRank != currentRank)
            {
                return candidateRank < currentRank;
            }
            if (candidateX != current.WorldTileX)
            {
                return candidateX < current.WorldTileX;
            }
            return candidateY < current.WorldTileY;
        }

        /// <summary>経路同点用Hashの下位16bitを非負の加算費用として返します。</summary>
        /// <param name="settings">SeedとGeneratorVersionを持つ固定設定です。</param>
        /// <param name="worldTileX">対象タイルの水平絶対座標です。</param>
        /// <param name="worldTileY">対象タイルの垂直絶対座標です。</param>
        /// <returns>0〜65,535の安定した経路同点費用です。</returns>
        private static ulong GetStablePathRank(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            return WorldSeedUtility.HashCoordinate(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                WorldGenerationPurposeSalts.HydrologyChannelPath) & ushort.MaxValue;
        }

        /// <summary>一つの水文セルが担当する中心-4〜+3の絶対タイル境界を安全に求めます。</summary>
        /// <param name="cellX">水平水文セル座標です。</param>
        /// <param name="cellY">垂直水文セル座標です。</param>
        /// <param name="minimumX">成功時の最小水平タイル座標です。</param>
        /// <param name="minimumY">成功時の最小垂直タイル座標です。</param>
        /// <param name="maximumX">成功時の最大水平タイル座標です。</param>
        /// <param name="maximumY">成功時の最大垂直タイル座標です。</param>
        /// <returns>中心乗算と両端がint範囲内ならtrueです。</returns>
        private static bool TryGetResponsibilityBounds(
            int cellX,
            int cellY,
            out int minimumX,
            out int minimumY,
            out int maximumX,
            out int maximumY)
        {
            long centerX = (long)cellX * 8L;
            long centerY = (long)cellY * 8L;
            long minimumLongX = centerX + ResponsibilityMinimumOffset;
            long minimumLongY = centerY + ResponsibilityMinimumOffset;
            long maximumLongX = centerX + ResponsibilityMaximumOffset;
            long maximumLongY = centerY + ResponsibilityMaximumOffset;
            if (minimumLongX < int.MinValue || minimumLongY < int.MinValue
                || maximumLongX > int.MaxValue || maximumLongY > int.MaxValue)
            {
                minimumX = default;
                minimumY = default;
                maximumX = default;
                maximumY = default;
                return false;
            }

            minimumX = (int)minimumLongX;
            minimumY = (int)minimumLongY;
            maximumX = (int)maximumLongX;
            maximumY = (int)maximumLongY;
            return true;
        }

        /// <summary>隣接する2水文セルの担当範囲を含む最大16×16の探索矩形を求めます。</summary>
        /// <param name="sourceCellX">上流側の水平水文セル座標です。</param>
        /// <param name="sourceCellY">上流側の垂直水文セル座標です。</param>
        /// <param name="destinationCellX">下流側の水平水文セル座標です。</param>
        /// <param name="destinationCellY">下流側の垂直水文セル座標です。</param>
        /// <param name="minimumX">成功時の探索最小水平タイル座標です。</param>
        /// <param name="minimumY">成功時の探索最小垂直タイル座標です。</param>
        /// <param name="maximumX">成功時の探索最大水平タイル座標です。</param>
        /// <param name="maximumY">成功時の探索最大垂直タイル座標です。</param>
        /// <returns>両セルの担当範囲がint内で求められた場合はtrueです。</returns>
        private static bool TryGetCombinedResponsibilityBounds(
            int sourceCellX,
            int sourceCellY,
            int destinationCellX,
            int destinationCellY,
            out int minimumX,
            out int minimumY,
            out int maximumX,
            out int maximumY)
        {
            if (!TryGetResponsibilityBounds(sourceCellX, sourceCellY,
                    out int sourceMinimumX, out int sourceMinimumY,
                    out int sourceMaximumX, out int sourceMaximumY)
                || !TryGetResponsibilityBounds(destinationCellX, destinationCellY,
                    out int destinationMinimumX, out int destinationMinimumY,
                    out int destinationMaximumX, out int destinationMaximumY))
            {
                minimumX = default;
                minimumY = default;
                maximumX = default;
                maximumY = default;
                return false;
            }

            minimumX = sourceMinimumX < destinationMinimumX ? sourceMinimumX : destinationMinimumX;
            minimumY = sourceMinimumY < destinationMinimumY ? sourceMinimumY : destinationMinimumY;
            maximumX = sourceMaximumX > destinationMaximumX ? sourceMaximumX : destinationMaximumX;
            maximumY = sourceMaximumY > destinationMaximumY ? sourceMaximumY : destinationMaximumY;
            return true;
        }
    }
}
