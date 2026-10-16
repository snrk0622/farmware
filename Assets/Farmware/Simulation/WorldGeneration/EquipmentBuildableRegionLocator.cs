// このファイルは、通常のワールド生成結果から設備を連続配置できる矩形領域を決定論的に探します。
// 開始地点専用の地形上書きは行わず、中央コアを含む設備が同じ建築可否ルールを共有するための検索境界です。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 設備建築可能領域を探す大きさ、基準座標、探索上限をまとめた設定です。
    /// 新規ワールドの初期位置選択だけでなく、将来の自動配置や移転候補検索からも同じ規則を利用できます。
    /// </summary>
    public struct EquipmentBuildableRegionSearchSettings
    {
        /// <summary>必要な連続領域の横幅です。単位はタイルで、1〜64を指定します。</summary>
        public int RegionWidthInTiles;

        /// <summary>必要な連続領域の縦幅です。単位はタイルで、1〜64を指定します。</summary>
        public int RegionHeightInTiles;

        /// <summary>探索を開始する水平ワールドタイル座標です。初期位置探索では原点を指定します。</summary>
        public int OriginTileX;

        /// <summary>探索を開始する垂直ワールドタイル座標です。初期位置探索では原点を指定します。</summary>
        public int OriginTileY;

        /// <summary>基準チャンクから外側へ調べる最大距離です。単位は64×64論理チャンクです。</summary>
        public int MaximumSearchRadiusInChunks;

        /// <summary>
        /// 初期検証用の検索設定を作ります。
        /// 12×8はコアと初期設備を複数箇所へ置ける暫定値で、半径2は最低5×5チャンクを検査します。
        /// </summary>
        /// <param name="originTileX">探索基準となる水平ワールドタイル座標です。</param>
        /// <param name="originTileY">探索基準となる垂直ワールドタイル座標です。</param>
        /// <returns>原点付近から12×8の連続領域を5×5チャンク内で探す設定です。</returns>
        public static EquipmentBuildableRegionSearchSettings CreateDefault(
            int originTileX = 0,
            int originTileY = 0)
        {
            return new EquipmentBuildableRegionSearchSettings
            {
                RegionWidthInTiles = 12,
                RegionHeightInTiles = 8,
                OriginTileX = originTileX,
                OriginTileY = originTileY,
                MaximumSearchRadiusInChunks = 2
            };
        }

        /// <summary>
        /// 通常資源も条件に含める初期位置選択用の検索設定を作ります。
        /// まず原点側から調べる順序は維持しつつ、水域が遠いシードでも地形や資源を上書きせず候補を探せるよう範囲を広げます。
        /// </summary>
        /// <param name="originTileX">探索基準となる水平ワールドタイル座標です。</param>
        /// <param name="originTileY">探索基準となる垂直ワールドタイル座標です。</param>
        /// <returns>原点付近から12×8の連続領域を最大17×17チャンク内で探す設定です。</returns>
        public static EquipmentBuildableRegionSearchSettings CreateInitialLocationDefault(
            int originTileX = 0,
            int originTileY = 0)
        {
            EquipmentBuildableRegionSearchSettings settings = CreateDefault(originTileX, originTileY);
            settings.MaximumSearchRadiusInChunks = 8;
            return settings;
        }
    }

    /// <summary>
    /// 建設可能領域の周囲に自然配置されている必要がある基礎資源を定義します。
    /// 開始地点専用に資源を追加せず、通常の資源生成結果を候補選択条件として読むために使用します。
    /// </summary>
    public struct EquipmentBuildableRegionResourceRequirements
    {
        /// <summary>候補周辺で少なくとも1タイルずつ必要な生成資源または水域地形です。</summary>
        public NaturalResourceTypeMask RequiredTypes;

        /// <summary>候補矩形の各辺から外側へ資源を探す距離です。単位はタイルです。</summary>
        public int SearchMarginInTiles;

        /// <summary>
        /// 初期位置検証用の資源条件を作ります。
        /// 木、石、水は初期自動化の基礎として求め、砂は砂漠探索の価値を残すため必須にしません。
        /// </summary>
        /// <returns>候補から48タイル以内に木、石、水を必要とする設定です。</returns>
        public static EquipmentBuildableRegionResourceRequirements CreateDefault()
        {
            return new EquipmentBuildableRegionResourceRequirements
            {
                RequiredTypes = NaturalResourceTypeMask.Tree
                    | NaturalResourceTypeMask.Stone
                    | NaturalResourceTypeMask.Water,
                SearchMarginInTiles = 48
            };
        }
    }

    /// <summary>
    /// 通常生成された地形内で、全タイルへ通常設備を建築できる矩形領域を表します。
    /// 地形そのものは保持せず、ワールドタイル座標と寸法だけを後続の配置UIやセーブ処理へ渡します。
    /// </summary>
    public readonly struct EquipmentBuildableRegion
    {
        /// <summary>矩形左下の水平ワールドタイル座標です。</summary>
        public readonly int MinimumTileX;

        /// <summary>矩形左下の垂直ワールドタイル座標です。</summary>
        public readonly int MinimumTileY;

        /// <summary>矩形の横幅です。単位はタイルです。</summary>
        public readonly int WidthInTiles;

        /// <summary>矩形の縦幅です。単位はタイルです。</summary>
        public readonly int HeightInTiles;

        /// <summary>
        /// 発見済み領域の座標と寸法をまとめます。
        /// </summary>
        /// <param name="minimumTileX">矩形左下の水平ワールドタイル座標です。</param>
        /// <param name="minimumTileY">矩形左下の垂直ワールドタイル座標です。</param>
        /// <param name="widthInTiles">矩形の横幅です。単位はタイルです。</param>
        /// <param name="heightInTiles">矩形の縦幅です。単位はタイルです。</param>
        public EquipmentBuildableRegion(
            int minimumTileX,
            int minimumTileY,
            int widthInTiles,
            int heightInTiles)
        {
            MinimumTileX = minimumTileX;
            MinimumTileY = minimumTileY;
            WidthInTiles = widthInTiles;
            HeightInTiles = heightInTiles;
        }
    }

    /// <summary>
    /// 座標から直接生成した通常タイルだけを読み、連続した設備建築可能領域を探します。
    /// 検索中にEntity、Tilemap、生成設定を変更しないため、検索順によってマップ内容が変わりません。
    /// </summary>
    public static class EquipmentBuildableRegionLocator
    {
        // 誤設定で無制限にチャンクを走査しないための安全上限です。通常の初期値は半径2です。
        private const int MaximumSupportedSearchRadiusInChunks = 64;

        // 資源条件の誤設定で巨大な矩形を同期走査しないための安全上限です。通常の初期値は48タイルです。
        private const int MaximumSupportedResourceSearchMarginInTiles = 256;

        /// <summary>
        /// 資源種類ごとの二次元累積数をまとめ、任意矩形内に資源があるか定数時間で調べる一時データです。
        /// 1チャンク内の多数の建築候補が同じ周辺資源を繰り返し走査しないために使用します。
        /// </summary>
        private readonly struct ResourcePresencePrefixMap
        {
            /// <summary>樹木数の二次元累積表です。検索処理だけが所有し、処理終了時に破棄します。</summary>
            public readonly NativeArray<int> TreeCounts;

            /// <summary>石数の二次元累積表です。検索処理だけが所有し、処理終了時に破棄します。</summary>
            public readonly NativeArray<int> StoneCounts;

            /// <summary>採水可能な水域タイル数の二次元累積表です。検索処理だけが所有し、処理終了時に破棄します。</summary>
            public readonly NativeArray<int> WaterCounts;

            /// <summary>砂資源数の二次元累積表です。検索処理だけが所有し、処理終了時に破棄します。</summary>
            public readonly NativeArray<int> SandCounts;

            /// <summary>累積表が表す左下の水平ワールドタイル座標です。</summary>
            public readonly int MinimumWorldTileX;

            /// <summary>累積表が表す左下の垂直ワールドタイル座標です。</summary>
            public readonly int MinimumWorldTileY;

            /// <summary>左端と上端の0行を含む累積表の横要素数です。</summary>
            public readonly int PrefixWidth;

            /// <summary>
            /// 4種類の累積表と、その表をワールド座標へ対応付ける情報をまとめます。
            /// </summary>
            /// <param name="treeCounts">樹木数の累積表です。</param>
            /// <param name="stoneCounts">石数の累積表です。</param>
            /// <param name="waterCounts">水資源数の累積表です。</param>
            /// <param name="sandCounts">砂資源数の累積表です。</param>
            /// <param name="minimumWorldTileX">表の左下に対応する水平座標です。</param>
            /// <param name="minimumWorldTileY">表の左下に対応する垂直座標です。</param>
            /// <param name="prefixWidth">0列を含む累積表の横要素数です。</param>
            public ResourcePresencePrefixMap(
                NativeArray<int> treeCounts,
                NativeArray<int> stoneCounts,
                NativeArray<int> waterCounts,
                NativeArray<int> sandCounts,
                int minimumWorldTileX,
                int minimumWorldTileY,
                int prefixWidth)
            {
                TreeCounts = treeCounts;
                StoneCounts = stoneCounts;
                WaterCounts = waterCounts;
                SandCounts = sandCounts;
                MinimumWorldTileX = minimumWorldTileX;
                MinimumWorldTileY = minimumWorldTileY;
                PrefixWidth = prefixWidth;
            }
        }

        /// <summary>
        /// 指定タイルへ通常設備を配置できるか判定します。
        /// コアも特別扱いせず、この共通判定を利用します。
        /// </summary>
        /// <param name="tile">決定論的生成器または生成済みチャンクから得た基礎タイルです。</param>
        /// <returns>通常設備を配置できる場合はtrueです。</returns>
        public static bool CanPlaceEquipment(in GeneratedTile tile)
        {
            // 地形種別を呼び出し側ごとに再解釈しないよう、設備配置はMAP-01Aで導入した共通属性だけを参照します。
            return (tile.GameplayFlags & TileGameplayFlags.AllowsNormalEquipment) != 0;
        }

        /// <summary>
        /// 基準チャンクから正方形のリングを外側へ広げ、最初に見つかる最寄りの連続建設可能領域を返します。
        /// 同じシード、生成バージョン、設定なら、チャンクのロード順に関係なく同じ座標を返します。
        /// </summary>
        /// <param name="generationSettings">通常の地形を再生成するためのワールド設定です。</param>
        /// <param name="searchSettings">必要領域、基準座標、探索上限です。</param>
        /// <param name="region">成功時に見つかった矩形領域です。失敗時は既定値です。</param>
        /// <returns>探索上限内に条件を満たす領域が見つかった場合はtrueです。</returns>
        public static bool TryFindNearest(
            in WorldGenerationSettings generationSettings,
            in EquipmentBuildableRegionSearchSettings searchSettings,
            out EquipmentBuildableRegion region)
        {
            ResourceGenerationSettings unusedResourceSettings = default;
            EquipmentBuildableRegionResourceRequirements unusedRequirements = default;
            return TryFindNearestInternal(
                generationSettings,
                unusedResourceSettings,
                searchSettings,
                unusedRequirements,
                requireResources: false,
                out region);
        }

        /// <summary>
        /// 通常地形の連続建設可能領域に加え、周囲へ通常生成された必須資源がそろう最寄り候補を返します。
        /// 資源や地形を追加・変更せず、同じ生成器を座標から読み直すだけです。
        /// </summary>
        /// <param name="generationSettings">通常の地形を再生成するためのワールド設定です。</param>
        /// <param name="resourceSettings">通常の資源を再生成するための密度設定です。</param>
        /// <param name="searchSettings">必要領域、基準座標、探索上限です。</param>
        /// <param name="resourceRequirements">候補周囲に必要な資源種類と探索距離です。</param>
        /// <param name="region">成功時に見つかった矩形領域です。失敗時は既定値です。</param>
        /// <returns>地形と資源の両条件を満たす候補が探索上限内に見つかった場合はtrueです。</returns>
        public static bool TryFindNearestWithResources(
            in WorldGenerationSettings generationSettings,
            in ResourceGenerationSettings resourceSettings,
            in EquipmentBuildableRegionSearchSettings searchSettings,
            in EquipmentBuildableRegionResourceRequirements resourceRequirements,
            out EquipmentBuildableRegion region)
        {
            return TryFindNearestInternal(
                generationSettings,
                resourceSettings,
                searchSettings,
                resourceRequirements,
                requireResources: true,
                out region);
        }

        /// <summary>
        /// 地形だけ、または地形と資源の両方を条件に、共通の決定論的リング探索を実行します。
        /// </summary>
        /// <param name="generationSettings">通常地形の生成設定です。</param>
        /// <param name="resourceSettings">資源条件を使う場合の通常資源生成設定です。</param>
        /// <param name="searchSettings">領域寸法と探索範囲です。</param>
        /// <param name="resourceRequirements">資源条件を使う場合の種類と距離です。</param>
        /// <param name="requireResources">地形候補へ資源条件も適用する場合はtrueです。</param>
        /// <param name="region">成功時に選ばれた領域です。</param>
        /// <returns>条件を満たす領域が見つかった場合はtrueです。</returns>
        private static bool TryFindNearestInternal(
            in WorldGenerationSettings generationSettings,
            in ResourceGenerationSettings resourceSettings,
            in EquipmentBuildableRegionSearchSettings searchSettings,
            in EquipmentBuildableRegionResourceRequirements resourceRequirements,
            bool requireResources,
            out EquipmentBuildableRegion region)
        {
            region = default;
            if (!IsValid(searchSettings)
                || (requireResources && !IsValid(resourceRequirements)))
            {
                return false;
            }

            ChunkCoordinate originChunk = WorldGrid.ToChunkCoordinate(
                searchSettings.OriginTileX,
                searchSettings.OriginTileY);

            // 半径0から順に調べることで、遠方に条件の良い領域があっても近傍リングを先に選びます。
            for (int radius = 0; radius <= searchSettings.MaximumSearchRadiusInChunks; radius++)
            {
                bool foundInRing = false;
                long bestSquaredDistance = long.MaxValue;
                EquipmentBuildableRegion bestRegion = default;

                // 同じリング内は実距離に近い中心距離で比較するため、周上の全チャンクを確認します。
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (MaximumAbsolute(offsetX, offsetY) != radius)
                        {
                            continue;
                        }

                        long chunkX = (long)originChunk.X + offsetX;
                        long chunkY = (long)originChunk.Y + offsetY;
                        if (chunkX < int.MinValue || chunkX > int.MaxValue
                            || chunkY < int.MinValue || chunkY > int.MaxValue)
                        {
                            continue;
                        }

                        if (!TryFindBestInChunk(
                                generationSettings,
                                searchSettings,
                                (int)chunkX,
                                (int)chunkY,
                                default,
                                NaturalResourceTypeMask.None,
                                resourceSearchMarginInTiles: 0,
                                filterByResourcePresence: false,
                                out EquipmentBuildableRegion candidate,
                                out long squaredDistance))
                        {
                            continue;
                        }

                        // 最寄りの地形候補が資源条件を外す場合だけ、同じチャンク内の次善候補を詳しく調べます。
                        // 通常ケースで全候補の周辺資源を走査しないため、1,000シード検証の費用を抑えられます。
                        if (requireResources
                            && !HasRequiredResources(
                                generationSettings,
                                resourceSettings,
                                candidate,
                                resourceRequirements))
                        {
                            if (!TryFindBestInChunkWithResources(
                                    generationSettings,
                                    resourceSettings,
                                    searchSettings,
                                    resourceRequirements,
                                    (int)chunkX,
                                    (int)chunkY,
                                    out candidate,
                                    out squaredDistance))
                            {
                                continue;
                            }
                        }

                        if (!foundInRing
                            || IsPreferredCandidate(
                                candidate,
                                squaredDistance,
                                bestRegion,
                                bestSquaredDistance))
                        {
                            foundInRing = true;
                            bestRegion = candidate;
                            bestSquaredDistance = squaredDistance;
                        }
                    }
                }

                if (foundInRing)
                {
                    region = bestRegion;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 検索設定が有限時間で安全に処理できる範囲か確認します。
        /// </summary>
        /// <param name="settings">利用前に検証する検索設定です。</param>
        /// <returns>領域寸法と探索半径が対応範囲内ならtrueです。</returns>
        private static bool IsValid(in EquipmentBuildableRegionSearchSettings settings)
        {
            return settings.RegionWidthInTiles >= 1
                && settings.RegionWidthInTiles <= WorldGridConstants.LogicalChunkSize
                && settings.RegionHeightInTiles >= 1
                && settings.RegionHeightInTiles <= WorldGridConstants.LogicalChunkSize
                && settings.MaximumSearchRadiusInChunks >= 0
                && settings.MaximumSearchRadiusInChunks <= MaximumSupportedSearchRadiusInChunks;
        }

        /// <summary>
        /// 資源条件が有限の矩形走査として扱える範囲か確認します。
        /// </summary>
        /// <param name="requirements">検証する必須資源と探索距離です。</param>
        /// <returns>探索距離が対応範囲内ならtrueです。</returns>
        private static bool IsValid(in EquipmentBuildableRegionResourceRequirements requirements)
        {
            return requirements.SearchMarginInTiles >= 0
                && requirements.SearchMarginInTiles <= MaximumSupportedResourceSearchMarginInTiles;
        }

        /// <summary>
        /// 候補矩形とその周辺を通常生成器で走査し、指定されたすべての基礎資源が存在するか確認します。
        /// </summary>
        /// <param name="generationSettings">地形を再生成するワールド設定です。</param>
        /// <param name="resourceSettings">資源を再生成する密度設定です。</param>
        /// <param name="region">建設可能と確認済みの候補矩形です。</param>
        /// <param name="requirements">必要資源と外側へ調べる距離です。</param>
        /// <returns>必要な全資源が少なくとも1タイルずつ見つかった場合はtrueです。</returns>
        private static bool HasRequiredResources(
            in WorldGenerationSettings generationSettings,
            in ResourceGenerationSettings resourceSettings,
            in EquipmentBuildableRegion region,
            in EquipmentBuildableRegionResourceRequirements requirements)
        {
            if (requirements.RequiredTypes == NaturalResourceTypeMask.None)
            {
                return true;
            }

            long minimumX = (long)region.MinimumTileX - requirements.SearchMarginInTiles;
            long minimumY = (long)region.MinimumTileY - requirements.SearchMarginInTiles;
            long maximumX = (long)region.MinimumTileX
                + region.WidthInTiles
                - 1L
                + requirements.SearchMarginInTiles;
            long maximumY = (long)region.MinimumTileY
                + region.HeightInTiles
                - 1L
                + requirements.SearchMarginInTiles;
            // inclusiveループの最終値がint端点だと、その次の++で反対側へ折り返すため端点自体も除外します。
            if (minimumX <= int.MinValue || maximumX >= int.MaxValue
                || minimumY <= int.MinValue || maximumY >= int.MaxValue)
            {
                return false;
            }

            NaturalResourceTypeMask foundTypes = NaturalResourceTypeMask.None;
            // 1種類見つかるたびにビットへ記録し、全種類がそろった時点で残り座標を生成せず終了します。
            for (int worldY = (int)minimumY; worldY <= (int)maximumY; worldY++)
            {
                for (int worldX = (int)minimumX; worldX <= (int)maximumX; worldX++)
                {
                    GeneratedTile terrainTile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        worldX,
                        worldY);
                    GeneratedResourceTile resourceTile =
                        DeterministicResourceGenerator.GenerateResource(
                            generationSettings,
                            resourceSettings,
                            terrainTile,
                            worldX,
                            worldY);
                    foundTypes |= DeterministicResourceGenerator.ToMask(resourceTile.Type);
                    if (terrainTile.WaterSalinity != WaterSalinity.None)
                    {
                        foundTypes |= NaturalResourceTypeMask.Water;
                    }

                    if ((foundTypes & requirements.RequiredTypes) == requirements.RequiredTypes)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 1チャンク内の次善建築候補を調べる前に、必要範囲の資源を1回だけ生成して累積表へ変換します。
        /// 候補ごとに同じ周辺地形と資源を再生成しないため、資源が遠いシードでも探索時間の増大を抑えます。
        /// </summary>
        /// <param name="generationSettings">通常地形を生成するワールド設定です。</param>
        /// <param name="resourceSettings">通常資源を生成する密度設定です。</param>
        /// <param name="searchSettings">必要な矩形寸法と距離比較の基準座標です。</param>
        /// <param name="resourceRequirements">必要資源と候補からの探索距離です。</param>
        /// <param name="chunkX">候補の左下を走査する水平論理チャンク座標です。</param>
        /// <param name="chunkY">候補の左下を走査する垂直論理チャンク座標です。</param>
        /// <param name="region">資源条件も満たす、このチャンク内の最良候補です。</param>
        /// <param name="squaredDistance">候補中心と基準座標の距離の二乗です。</param>
        /// <returns>地形と資源の両条件を満たす候補が見つかった場合はtrueです。</returns>
        private static bool TryFindBestInChunkWithResources(
            in WorldGenerationSettings generationSettings,
            in ResourceGenerationSettings resourceSettings,
            in EquipmentBuildableRegionSearchSettings searchSettings,
            in EquipmentBuildableRegionResourceRequirements resourceRequirements,
            int chunkX,
            int chunkY,
            out EquipmentBuildableRegion region,
            out long squaredDistance)
        {
            region = default;
            squaredDistance = long.MaxValue;

            long chunkMinimumX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long chunkMinimumY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long resourceMinimumX = chunkMinimumX - resourceRequirements.SearchMarginInTiles;
            long resourceMinimumY = chunkMinimumY - resourceRequirements.SearchMarginInTiles;
            long resourceMaximumX = chunkMinimumX
                + WorldGridConstants.LogicalChunkSize
                + searchSettings.RegionWidthInTiles
                - 2L
                + resourceRequirements.SearchMarginInTiles;
            long resourceMaximumY = chunkMinimumY
                + WorldGridConstants.LogicalChunkSize
                + searchSettings.RegionHeightInTiles
                - 2L
                + resourceRequirements.SearchMarginInTiles;
            if (resourceMinimumX <= int.MinValue || resourceMaximumX >= int.MaxValue
                || resourceMinimumY <= int.MinValue || resourceMaximumY >= int.MaxValue)
            {
                return false;
            }

            int resourceWidth = (int)(resourceMaximumX - resourceMinimumX + 1L);
            int resourceHeight = (int)(resourceMaximumY - resourceMinimumY + 1L);
            int prefixWidth = resourceWidth + 1;
            int prefixLength = prefixWidth * (resourceHeight + 1);

            // 先頭行・先頭列を0として確保すると、矩形の端でも分岐せず4点の差だけで個数を求められます。
            // Allocator.Tempの所有権はこのメソッドにあり、usingにより成功・失敗のどちらでも必ず破棄します。
            using NativeArray<int> treeCounts = new NativeArray<int>(
                prefixLength,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using NativeArray<int> stoneCounts = new NativeArray<int>(
                prefixLength,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using NativeArray<int> waterCounts = new NativeArray<int>(
                prefixLength,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using NativeArray<int> sandCounts = new NativeArray<int>(
                prefixLength,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            NaturalResourceTypeMask foundTypes = NaturalResourceTypeMask.None;

            // このチャンクに左下を持つ全候補が参照し得る外接矩形を一度だけ生成し、種類別の累積表へ変換します。
            for (int localY = 1; localY <= resourceHeight; localY++)
            {
                int worldY = (int)resourceMinimumY + localY - 1;
                for (int localX = 1; localX <= resourceWidth; localX++)
                {
                    int worldX = (int)resourceMinimumX + localX - 1;
                    GeneratedTile terrainTile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        worldX,
                        worldY);
                    GeneratedResourceTile resourceTile =
                        DeterministicResourceGenerator.GenerateResource(
                            generationSettings,
                            resourceSettings,
                            terrainTile,
                            worldX,
                            worldY);
                    NaturalResourceTypeMask typeMask = DeterministicResourceGenerator.ToMask(
                        resourceTile.Type);
                    if (terrainTile.WaterSalinity != WaterSalinity.None)
                    {
                        typeMask |= NaturalResourceTypeMask.Water;
                    }

                    foundTypes |= typeMask;

                    // 各表には「左 + 下 - 左下 + 現在セル」を書き、左下から現在までの総数を保持します。
                    int prefixIndex = (localY * prefixWidth) + localX;
                    WritePrefixValue(
                        treeCounts,
                        prefixIndex,
                        prefixWidth,
                        resourceTile.Type == GeneratedResourceType.Tree);
                    WritePrefixValue(
                        stoneCounts,
                        prefixIndex,
                        prefixWidth,
                        resourceTile.Type == GeneratedResourceType.Stone);
                    WritePrefixValue(
                        waterCounts,
                        prefixIndex,
                        prefixWidth,
                        terrainTile.WaterSalinity != WaterSalinity.None);
                    WritePrefixValue(
                        sandCounts,
                        prefixIndex,
                        prefixWidth,
                        resourceTile.Type == GeneratedResourceType.Sand);
                }
            }

            if ((foundTypes & resourceRequirements.RequiredTypes) != resourceRequirements.RequiredTypes)
            {
                return false;
            }

            ResourcePresencePrefixMap resourcePresence = new ResourcePresencePrefixMap(
                treeCounts,
                stoneCounts,
                waterCounts,
                sandCounts,
                (int)resourceMinimumX,
                (int)resourceMinimumY,
                prefixWidth);
            return TryFindBestInChunk(
                generationSettings,
                searchSettings,
                chunkX,
                chunkY,
                resourcePresence,
                resourceRequirements.RequiredTypes,
                resourceRequirements.SearchMarginInTiles,
                filterByResourcePresence: true,
                out region,
                out squaredDistance);
        }

        /// <summary>
        /// 1チャンクに左下を持つ全矩形候補を調べ、基準座標に最も近い候補を返します。
        /// 矩形が右または上の隣接チャンクへまたがる場合も、同じワールド座標生成で検査します。
        /// </summary>
        /// <param name="generationSettings">通常タイルを生成するワールド設定です。</param>
        /// <param name="searchSettings">必要な矩形寸法と距離比較の基準座標です。</param>
        /// <param name="chunkX">候補の左下を走査する水平論理チャンク座標です。</param>
        /// <param name="chunkY">候補の左下を走査する垂直論理チャンク座標です。</param>
        /// <param name="resourcePresence">次善候補の絞り込みに再利用する種類別資源数の累積表です。</param>
        /// <param name="requiredTypes">候補周辺に必要な資源種類です。</param>
        /// <param name="resourceSearchMarginInTiles">候補の各辺から外へ資源を探す距離です。</param>
        /// <param name="filterByResourcePresence">事前生成した資源累積表で候補を絞る場合はtrueです。</param>
        /// <param name="region">このチャンクで見つかった最良候補です。</param>
        /// <param name="squaredDistance">候補中心と基準座標の距離の二乗です。</param>
        /// <returns>このチャンクに左下を持つ有効な候補が存在する場合はtrueです。</returns>
        private static bool TryFindBestInChunk(
            in WorldGenerationSettings generationSettings,
            in EquipmentBuildableRegionSearchSettings searchSettings,
            int chunkX,
            int chunkY,
            ResourcePresencePrefixMap resourcePresence,
            NaturalResourceTypeMask requiredTypes,
            int resourceSearchMarginInTiles,
            bool filterByResourcePresence,
            out EquipmentBuildableRegion region,
            out long squaredDistance)
        {
            region = default;
            squaredDistance = long.MaxValue;

            long chunkMinimumX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long chunkMinimumY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long generatedMaximumX = chunkMinimumX
                + WorldGridConstants.LogicalChunkSize
                + searchSettings.RegionWidthInTiles
                - 2L;
            long generatedMaximumY = chunkMinimumY
                + WorldGridConstants.LogicalChunkSize
                + searchSettings.RegionHeightInTiles
                - 2L;

            // int座標の端で加算が折り返すと別座標を誤検査するため、そのチャンクは候補外にします。
            if (chunkMinimumX <= int.MinValue || generatedMaximumX >= int.MaxValue
                || chunkMinimumY <= int.MinValue || generatedMaximumY >= int.MaxValue)
            {
                return false;
            }

            FixedList512Bytes<int> consecutiveBuildableRows = default;
            for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
            {
                // 候補左端ごとの「横幅を満たした連続行数」を保持し、矩形全体の再走査を避けます。
                consecutiveBuildableRows.Add(0);
            }

            bool found = false;
            int finalWorldX = (int)generatedMaximumX;
            int finalWorldY = (int)generatedMaximumY;

            // 各タイルを一度ずつ生成し、横方向の連続数と縦方向の連続行数から固定寸法矩形を判定します。
            for (int worldY = (int)chunkMinimumY; worldY <= finalWorldY; worldY++)
            {
                int horizontalBuildableCount = 0;
                for (int worldX = (int)chunkMinimumX; worldX <= finalWorldX; worldX++)
                {
                    GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        worldX,
                        worldY);
                    horizontalBuildableCount = CanPlaceEquipment(tile)
                        ? horizontalBuildableCount + 1
                        : 0;

                    int candidateMinimumX = worldX - searchSettings.RegionWidthInTiles + 1;
                    int candidateLocalX = candidateMinimumX - (int)chunkMinimumX;
                    if (candidateLocalX < 0
                        || candidateLocalX >= WorldGridConstants.LogicalChunkSize)
                    {
                        continue;
                    }

                    int updatedRowCount = horizontalBuildableCount >= searchSettings.RegionWidthInTiles
                        ? consecutiveBuildableRows[candidateLocalX] + 1
                        : 0;
                    consecutiveBuildableRows[candidateLocalX] = updatedRowCount;
                    if (updatedRowCount < searchSettings.RegionHeightInTiles)
                    {
                        continue;
                    }

                    int candidateMinimumY = worldY - searchSettings.RegionHeightInTiles + 1;
                    EquipmentBuildableRegion candidate = new EquipmentBuildableRegion(
                        candidateMinimumX,
                        candidateMinimumY,
                        searchSettings.RegionWidthInTiles,
                        searchSettings.RegionHeightInTiles);
                    if (filterByResourcePresence
                        && !HasRequiredResources(
                            resourcePresence,
                            candidate,
                            requiredTypes,
                            resourceSearchMarginInTiles))
                    {
                        continue;
                    }

                    long candidateSquaredDistance = CalculateSquaredCenterDistance(
                        candidate,
                        searchSettings.OriginTileX,
                        searchSettings.OriginTileY);

                    if (!found
                        || IsPreferredCandidate(
                            candidate,
                            candidateSquaredDistance,
                            region,
                            squaredDistance))
                    {
                        found = true;
                        region = candidate;
                        squaredDistance = candidateSquaredDistance;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 種類別の二次元累積表を使い、候補矩形の指定距離内に必要種類がすべてあるか判定します。
        /// </summary>
        /// <param name="resourcePresence">対象チャンク周辺の種類別資源数を保持する累積表です。</param>
        /// <param name="region">建設可能と確認済みの候補矩形です。</param>
        /// <param name="requiredTypes">候補周辺に必要な資源種類です。</param>
        /// <param name="searchMarginInTiles">候補の各辺から外へ調べる距離です。</param>
        /// <returns>必要な全種類が候補周辺に少なくとも1件ずつ存在する場合はtrueです。</returns>
        private static bool HasRequiredResources(
            in ResourcePresencePrefixMap resourcePresence,
            in EquipmentBuildableRegion region,
            NaturalResourceTypeMask requiredTypes,
            int searchMarginInTiles)
        {
            int minimumX = region.MinimumTileX
                - searchMarginInTiles
                - resourcePresence.MinimumWorldTileX;
            int minimumY = region.MinimumTileY
                - searchMarginInTiles
                - resourcePresence.MinimumWorldTileY;
            int maximumX = region.MinimumTileX
                + region.WidthInTiles
                - 1
                + searchMarginInTiles
                - resourcePresence.MinimumWorldTileX;
            int maximumY = region.MinimumTileY
                + region.HeightInTiles
                - 1
                + searchMarginInTiles
                - resourcePresence.MinimumWorldTileY;

            // 必須フラグに含まれる種類だけを4点参照で確認し、候補数に比例した資源リスト走査をなくします。
            return (!RequiresResource(requiredTypes, NaturalResourceTypeMask.Tree)
                    || HasAnyInRectangle(
                        resourcePresence.TreeCounts,
                        resourcePresence.PrefixWidth,
                        minimumX,
                        minimumY,
                        maximumX,
                        maximumY))
                && (!RequiresResource(requiredTypes, NaturalResourceTypeMask.Stone)
                    || HasAnyInRectangle(
                        resourcePresence.StoneCounts,
                        resourcePresence.PrefixWidth,
                        minimumX,
                        minimumY,
                        maximumX,
                        maximumY))
                && (!RequiresResource(requiredTypes, NaturalResourceTypeMask.Water)
                    || HasAnyInRectangle(
                        resourcePresence.WaterCounts,
                        resourcePresence.PrefixWidth,
                        minimumX,
                        minimumY,
                        maximumX,
                        maximumY))
                && (!RequiresResource(requiredTypes, NaturalResourceTypeMask.Sand)
                    || HasAnyInRectangle(
                        resourcePresence.SandCounts,
                        resourcePresence.PrefixWidth,
                        minimumX,
                        minimumY,
                        maximumX,
                        maximumY));
        }

        /// <summary>
        /// 現在セルまでの二次元累積数を1種類の表へ書き込みます。
        /// </summary>
        /// <param name="prefixCounts">更新対象の種類別累積表です。</param>
        /// <param name="prefixIndex">現在セルに対応する累積表内の一次元Indexです。</param>
        /// <param name="prefixWidth">累積表の横要素数です。</param>
        /// <param name="containsResource">現在セルがこの表の資源種類ならtrueです。</param>
        private static void WritePrefixValue(
            NativeArray<int> prefixCounts,
            int prefixIndex,
            int prefixWidth,
            bool containsResource)
        {
            int leftIndex = prefixIndex - 1;
            int belowIndex = prefixIndex - prefixWidth;
            int belowLeftIndex = belowIndex - 1;
            prefixCounts[prefixIndex] = prefixCounts[leftIndex]
                + prefixCounts[belowIndex]
                - prefixCounts[belowLeftIndex]
                + (containsResource ? 1 : 0);
        }

        /// <summary>
        /// 必須資源フラグに指定種類が含まれるか確認します。
        /// </summary>
        /// <param name="requiredTypes">複数種類をまとめた必須資源フラグです。</param>
        /// <param name="type">確認する1種類の資源フラグです。</param>
        /// <returns>指定種類が必要ならtrueです。</returns>
        private static bool RequiresResource(
            NaturalResourceTypeMask requiredTypes,
            NaturalResourceTypeMask type)
        {
            return (requiredTypes & type) != 0;
        }

        /// <summary>
        /// 二次元累積表の4点を読み、指定した両端を含む矩形内に1件以上あるか判定します。
        /// </summary>
        /// <param name="prefixCounts">1種類の資源数を保持する累積表です。</param>
        /// <param name="prefixWidth">累積表の横要素数です。</param>
        /// <param name="minimumX">表の左下を0とする矩形左端です。</param>
        /// <param name="minimumY">表の左下を0とする矩形下端です。</param>
        /// <param name="maximumX">表の左下を0とする矩形右端です。</param>
        /// <param name="maximumY">表の左下を0とする矩形上端です。</param>
        /// <returns>矩形内に対象資源が1件以上あればtrueです。</returns>
        private static bool HasAnyInRectangle(
            NativeArray<int> prefixCounts,
            int prefixWidth,
            int minimumX,
            int minimumY,
            int maximumX,
            int maximumY)
        {
            // 累積表には先頭の0行・0列があるため、右上だけ1を加えて半開区間へ変換します。
            int right = maximumX + 1;
            int top = maximumY + 1;
            int count = prefixCounts[(top * prefixWidth) + right]
                - prefixCounts[(minimumY * prefixWidth) + right]
                - prefixCounts[(top * prefixWidth) + minimumX]
                + prefixCounts[(minimumY * prefixWidth) + minimumX];
            return count > 0;
        }

        /// <summary>
        /// 2軸の絶対値の大きい方を返し、正方形リング上のチャンクだけを選別します。
        /// </summary>
        /// <param name="x">基準チャンクからの水平差です。</param>
        /// <param name="y">基準チャンクからの垂直差です。</param>
        /// <returns>チェビシェフ距離として使う非負の整数です。</returns>
        private static int MaximumAbsolute(int x, int y)
        {
            int absoluteX = x < 0 ? -x : x;
            int absoluteY = y < 0 ? -y : y;
            return absoluteX > absoluteY ? absoluteX : absoluteY;
        }

        /// <summary>
        /// 浮動小数点を使わず、矩形中心と基準タイル中心の距離の二乗を求めます。
        /// 中心座標を2倍した整数で比較し、偶数寸法の0.5タイルを失わないようにします。
        /// </summary>
        /// <param name="region">距離を測る候補矩形です。</param>
        /// <param name="originTileX">基準となる水平タイル座標です。</param>
        /// <param name="originTileY">基準となる垂直タイル座標です。</param>
        /// <returns>2倍座標系での中心距離の二乗です。</returns>
        private static long CalculateSquaredCenterDistance(
            in EquipmentBuildableRegion region,
            int originTileX,
            int originTileY)
        {
            long centerXTimesTwo = ((long)region.MinimumTileX * 2L) + region.WidthInTiles - 1L;
            long centerYTimesTwo = ((long)region.MinimumTileY * 2L) + region.HeightInTiles - 1L;
            long differenceX = centerXTimesTwo - ((long)originTileX * 2L);
            long differenceY = centerYTimesTwo - ((long)originTileY * 2L);
            return (differenceX * differenceX) + (differenceY * differenceY);
        }

        /// <summary>
        /// 距離が近い候補を優先し、同距離ではY、Xの昇順に固定して結果を完全に決定します。
        /// </summary>
        /// <param name="candidate">新しく比較する候補です。</param>
        /// <param name="candidateSquaredDistance">新候補の中心距離の二乗です。</param>
        /// <param name="current">現在選ばれている候補です。</param>
        /// <param name="currentSquaredDistance">現在候補の中心距離の二乗です。</param>
        /// <returns>新候補へ置き換えるべき場合はtrueです。</returns>
        private static bool IsPreferredCandidate(
            in EquipmentBuildableRegion candidate,
            long candidateSquaredDistance,
            in EquipmentBuildableRegion current,
            long currentSquaredDistance)
        {
            if (candidateSquaredDistance != currentSquaredDistance)
            {
                return candidateSquaredDistance < currentSquaredDistance;
            }

            if (candidate.MinimumTileY != current.MinimumTileY)
            {
                return candidate.MinimumTileY < current.MinimumTileY;
            }

            return candidate.MinimumTileX < current.MinimumTileX;
        }
    }
}
