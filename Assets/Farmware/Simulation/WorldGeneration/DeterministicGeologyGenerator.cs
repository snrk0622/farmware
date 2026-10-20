// このファイルは、TERRAIN-01C2の固定地質Definitionと、座標ワープ付きジッターVoronoiによる地質地域生成を定義します。
// DeterministicChunkGenerator、後続の水文・Biome分類、開発者用監査が同じ整数計算を共有し、チャンク生成順による差を防ぎます。

using System;
using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// Biome、Block、資源IDから独立した卓越基盤地質の安定IDです。
    /// セーブ互換性のため既存値を変更・再利用せず、表示順にも使用しません。
    /// </summary>
    public enum GeologyClass : byte
    {
        /// <summary>未設定または生成失敗を表す予約値です。</summary>
        None = 0,

        /// <summary>砂、礫、粘土、シルト、沖積層を含む未固結堆積物です。</summary>
        UnconsolidatedSediment = 1,

        /// <summary>砂岩と礫岩を含む砂岩質の基盤です。</summary>
        Sandstone = 2,

        /// <summary>石灰岩とドロマイトを含むカルスト適合基盤です。</summary>
        Limestone = 3,

        /// <summary>泥岩、頁岩、粘板岩を含む細粒な堆積・弱変成基盤です。</summary>
        MudstoneSlate = 4,

        /// <summary>花崗岩と花崗閃緑岩を含む花崗岩質基盤です。</summary>
        GraniticRock = 5,

        /// <summary>片麻岩、結晶片岩、珪岩を含む変成岩基盤です。</summary>
        MetamorphicRock = 6,

        /// <summary>玄武岩、安山岩、流紋岩、凝灰岩を含む火山岩基盤です。</summary>
        VolcanicRock = 7,

        /// <summary>かんらん岩と蛇紋岩を含む超苦鉄質岩基盤です。</summary>
        UltramaficRock = 8
    }

    /// <summary>
    /// 地質分類が後続生成の必須条件として提供する性質です。
    /// 連続値や表示名から暗黙に推測せず、Definitionの明示フラグだけを参照します。
    /// </summary>
    [Flags]
    public enum GeologyDefinitionFlags : byte
    {
        /// <summary>カルストと活動的火山のどちらの必須条件も満たしません。</summary>
        None = 0,

        /// <summary>洞窟、陥没地、地下河川などのカルスト候補にできます。</summary>
        SupportsKarst = 1 << 0,

        /// <summary>火山活動度と組み合わせて活動的火山候補にできます。</summary>
        SupportsActiveVolcanism = 1 << 1
    }

    /// <summary>
    /// 1つの固定地質について、基礎特性とサイト抽選重みを保持する静的Definitionです。
    /// WorldGenerationSettingsへ値として保存し、採掘や地形編集では書き換えません。
    /// </summary>
    public struct GeologyDefinition
    {
        /// <summary>名称や配列位置から独立した変更禁止の地質IDです。</summary>
        public GeologyClass Class;

        /// <summary>地下へ水を通しやすい度合いを表す0〜65,535の基礎値です。</summary>
        public ushort Permeability;

        /// <summary>浸透水を地下水として保持しやすい度合いを表す0〜65,535の基礎値です。</summary>
        public ushort GroundwaterStorage;

        /// <summary>河川侵食や露頭形成に対する抵抗を表す0〜65,535の基礎値です。</summary>
        public ushort ErosionResistance;

        /// <summary>カルストと活動的火山の必須条件を明示するビットフラグです。</summary>
        public GeologyDefinitionFlags Flags;

        /// <summary>全8分類で合計10,000になる、標高補正前のサイト抽選重みです。</summary>
        public ushort BaseWeight;

        /// <summary>海面下サイトの重みに掛ける、256を1倍とする固定小数倍率です。</summary>
        public ushort BelowSeaLevelMultiplier;

        /// <summary>低地サイトの重みに掛ける、256を1倍とする固定小数倍率です。</summary>
        public ushort LowlandMultiplier;

        /// <summary>平地・台地サイトの重みに掛ける、256を1倍とする固定小数倍率です。</summary>
        public ushort PlainOrPlateauMultiplier;

        /// <summary>山地サイトの重みに掛ける、256を1倍とする固定小数倍率です。</summary>
        public ushort MountainMultiplier;

        /// <summary>高山サイトの重みに掛ける、256を1倍とする固定小数倍率です。</summary>
        public ushort AlpineMultiplier;

        /// <summary>
        /// サイト標高区分に対応する固定小数倍率を返します。
        /// </summary>
        /// <param name="elevationClass">サイト絶対座標で再評価した標高区分です。</param>
        /// <returns>256を1倍とする非負倍率です。</returns>
        public ushort GetElevationMultiplier(ElevationClass elevationClass)
        {
            switch (elevationClass)
            {
                case ElevationClass.BelowSeaLevel:
                    return BelowSeaLevelMultiplier;
                case ElevationClass.Lowland:
                    return LowlandMultiplier;
                case ElevationClass.PlainOrPlateau:
                    return PlainOrPlateauMultiplier;
                case ElevationClass.Mountain:
                    return MountainMultiplier;
                case ElevationClass.Alpine:
                    return AlpineMultiplier;
                default:
                    return 0;
            }
        }
    }

    /// <summary>
    /// 地質地域の形状、サイト探索、固定Definitionをまとめた生成設定です。
    /// GeneratorVersionと一緒にワールド寿命中は変更しません。
    /// </summary>
    public struct GeologyGenerationSettings
    {
        /// <summary>ジッターVoronoiサイトを1個ずつ置くマクロセルの1辺です。単位はタイルです。</summary>
        public int MacroCellSizeInTiles;

        /// <summary>地質境界座標ワープの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int WarpCellSizeInTiles;

        /// <summary>地質境界座標ワープへ重ねるノイズ解像度数です。</summary>
        public int WarpOctaveCount;

        /// <summary>X・Yそれぞれの地質境界座標ワープ最大幅です。単位はタイルです。</summary>
        public int WarpAmplitudeInTiles;

        /// <summary>サイトをマクロセル中心から負方向へずらせる最大幅です。単位はタイルです。</summary>
        public int SiteNegativeJitterInTiles;

        /// <summary>サイトをマクロセル中心から正方向へずらせる最大幅です。単位はタイルです。</summary>
        public int SitePositiveJitterInTiles;

        /// <summary>対象マクロセルから前後何セルのサイトまで探索するかを表します。既定2で5×5になります。</summary>
        public int SiteSearchRadiusInCells;

        /// <summary>8地質の基礎特性と標高別抽選重みです。配列順ではなくGeologyClassで参照します。</summary>
        public FixedList512Bytes<GeologyDefinition> Definitions;

        /// <summary>
        /// TERRAIN-01C2の確定値を持つ既定地質設定を作ります。
        /// </summary>
        /// <returns>8分類、512タイル地域、64タイル境界ワープを含む設定です。</returns>
        public static GeologyGenerationSettings CreateDefault()
        {
            GeologyGenerationSettings settings = new GeologyGenerationSettings
            {
                MacroCellSizeInTiles = 512,
                WarpCellSizeInTiles = 256,
                WarpOctaveCount = 2,
                WarpAmplitudeInTiles = 64,
                SiteNegativeJitterInTiles = 192,
                SitePositiveJitterInTiles = 191,
                SiteSearchRadiusInCells = 2
            };

            // 安定ID順で初期化しますが、生成時はID検索するため配列順を抽選順としては使用しません。
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.UnconsolidatedSediment, 45874, 58981, 6553,
                GeologyDefinitionFlags.None, 1800, 384, 384, 256, 64, 16));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.Sandstone, 45874, 45874, 32767,
                GeologyDefinitionFlags.None, 1500, 256, 256, 256, 192, 128));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.Limestone, 58981, 45874, 45874,
                GeologyDefinitionFlags.SupportsKarst, 1200, 192, 256, 256, 192, 128));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.MudstoneSlate, 6553, 19660, 32767,
                GeologyDefinitionFlags.None, 1400, 384, 320, 256, 192, 128));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.GraniticRock, 19660, 19660, 58981,
                GeologyDefinitionFlags.None, 1500, 128, 160, 256, 384, 384));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.MetamorphicRock, 19660, 19660, 58981,
                GeologyDefinitionFlags.None, 1100, 128, 128, 192, 384, 512));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.VolcanicRock, 45874, 45874, 45874,
                GeologyDefinitionFlags.SupportsActiveVolcanism, 1100, 256, 256, 256, 320, 320));
            settings.Definitions.Add(CreateDefinition(
                GeologyClass.UltramaficRock, 32767, 19660, 58981,
                GeologyDefinitionFlags.None, 400, 128, 128, 192, 320, 384));
            return settings;
        }

        /// <summary>
        /// 確定表の1行をDefinitionへ変換します。
        /// 引数順を表の列順へ揃え、既定値の転記ミスをテストで発見しやすくします。
        /// </summary>
        /// <param name="geologyClass">保存互換性に使用する地質の安定IDです。</param>
        /// <param name="permeability">0〜65,535の基礎透水性です。</param>
        /// <param name="groundwaterStorage">0〜65,535の基礎地下水保持力です。</param>
        /// <param name="erosionResistance">0〜65,535の基礎侵食抵抗です。</param>
        /// <param name="flags">カルスト・活動的火山の必須条件フラグです。</param>
        /// <param name="baseWeight">標高補正前の抽選重みです。</param>
        /// <param name="belowSeaLevelMultiplier">海面下で256を1倍とする倍率です。</param>
        /// <param name="lowlandMultiplier">低地で256を1倍とする倍率です。</param>
        /// <param name="plainOrPlateauMultiplier">平地・台地で256を1倍とする倍率です。</param>
        /// <param name="mountainMultiplier">山地で256を1倍とする倍率です。</param>
        /// <param name="alpineMultiplier">高山で256を1倍とする倍率です。</param>
        /// <returns>確定表の1行を値として保持するDefinitionです。</returns>
        private static GeologyDefinition CreateDefinition(
            GeologyClass geologyClass,
            ushort permeability,
            ushort groundwaterStorage,
            ushort erosionResistance,
            GeologyDefinitionFlags flags,
            ushort baseWeight,
            ushort belowSeaLevelMultiplier,
            ushort lowlandMultiplier,
            ushort plainOrPlateauMultiplier,
            ushort mountainMultiplier,
            ushort alpineMultiplier)
        {
            return new GeologyDefinition
            {
                Class = geologyClass,
                Permeability = permeability,
                GroundwaterStorage = groundwaterStorage,
                ErosionResistance = erosionResistance,
                Flags = flags,
                BaseWeight = baseWeight,
                BelowSeaLevelMultiplier = belowSeaLevelMultiplier,
                LowlandMultiplier = lowlandMultiplier,
                PlainOrPlateauMultiplier = plainOrPlateauMultiplier,
                MountainMultiplier = mountainMultiplier,
                AlpineMultiplier = alpineMultiplier
            };
        }
    }

    /// <summary>
    /// 最近傍比較に必要な1つの地質サイト情報です。
    /// 開発者診断と境界テストでも同じ同点順を確認できるよう、純粋データとして公開します。
    /// </summary>
    public struct GeologySiteData
    {
        /// <summary>サイトを所有する地質マクロセルの水平座標です。</summary>
        public int MacroCellX;

        /// <summary>サイトを所有する地質マクロセルの垂直座標です。</summary>
        public int MacroCellY;

        /// <summary>ジッター適用後の水平ワールドタイル座標です。</summary>
        public int SiteX;

        /// <summary>ジッター適用後の垂直ワールドタイル座標です。</summary>
        public int SiteY;

        /// <summary>ワープ後の評価座標からサイトまでの平方ユークリッド距離です。</summary>
        public long DistanceSquared;
    }

    /// <summary>
    /// 1タイルの固定地質生成結果と、監査に必要な支配サイト情報です。
    /// GeneratedTileへは高頻度参照する分類・基礎特性をコピーし、サイト情報は診断時だけ使用します。
    /// </summary>
    public struct GeneratedGeologyData
    {
        /// <summary>BiomeやBlockから独立した卓越基盤地質の安定IDです。</summary>
        public GeologyClass Class;

        /// <summary>支配サイト絶対座標で再評価した標高区分です。</summary>
        public ElevationClass SiteElevationClass;

        /// <summary>Definitionから取得した0〜65,535の基礎透水性です。</summary>
        public ushort Permeability;

        /// <summary>Definitionから取得した0〜65,535の基礎地下水保持力です。</summary>
        public ushort GroundwaterStorage;

        /// <summary>Definitionから取得した0〜65,535の基礎侵食抵抗です。</summary>
        public ushort ErosionResistance;

        /// <summary>Definitionから取得したカルスト・活動的火山の明示フラグです。</summary>
        public GeologyDefinitionFlags Flags;

        /// <summary>このタイルを支配したVoronoiサイトです。境界と分布の監査に使用します。</summary>
        public GeologySiteData Site;
    }

    /// <summary>
    /// 1つの論理チャンク周辺で再利用する地質サイトと、そのサイトに一度だけ割り当てたDefinitionです。
    /// Chunk生成Jobのローカル値としてだけ保持し、ECSやセーブデータには保存しません。
    /// </summary>
    internal struct CachedGeologySite
    {
        /// <summary>距離を除くサイト絶対座標と所有マクロセルです。</summary>
        public GeologySiteData Site;

        /// <summary>サイト絶対座標の標高区分から選んだ固定地質Definitionです。</summary>
        public GeologyDefinition Definition;

        /// <summary>Definition抽選に使用したサイト絶対座標の標高区分です。</summary>
        public ElevationClass SiteElevationClass;
    }

    /// <summary>
    /// 64×64論理チャンクで参照し得る地質サイトをまとめた、Burst対応の一時キャッシュです。
    /// 最大36サイトを値型FixedListへ保持し、タイルごとのサイトジッター・標高・分類抽選の再計算を避けます。
    /// </summary>
    internal struct GeologyChunkCache
    {
        /// <summary>チャンク内のどのタイルからも5×5候補になり得るサイト一覧です。</summary>
        public FixedList4096Bytes<CachedGeologySite> Sites;
    }

    /// <summary>
    /// 絶対座標と固定設定だけから地質サイト、分類、基礎特性を生成する純粋計算です。
    /// Burst Jobから呼べるよう、例外、管理参照、UnityEngine APIを使用しません。
    /// </summary>
    public static class DeterministicGeologyGenerator
    {
        /// <summary>初回仕様で必須とする地質Definition数です。</summary>
        public const int RequiredDefinitionCount = 8;

        /// <summary>全地質の基礎重みが一致すべき固定合計です。</summary>
        public const int RequiredBaseWeightTotal = 10000;

        /// <summary>
        /// 1タイルを支配する地質サイトと固定地質を生成します。
        /// int座標域の端でワープまたは5×5候補サイトを安全に表現できない場合は失敗します。
        /// </summary>
        /// <param name="settings">ワールド作成時に固定した生成設定です。</param>
        /// <param name="worldTileX">水平の絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直の絶対タイル座標です。</param>
        /// <param name="geology">成功時に地質分類、基礎特性、支配サイトを返します。</param>
        /// <returns>設定と候補座標が有効で8分類から選択できた場合はtrueです。</returns>
        public static bool TryGenerate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out GeneratedGeologyData geology)
        {
            geology = default;
            if (!ValidateSettings(settings.Geology)
                || !TryCalculateWarpedCoordinate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out int warpedX,
                    out int warpedY))
            {
                return false;
            }

            int centerMacroCellX = WorldGrid.FloorDivide(
                warpedX,
                settings.Geology.MacroCellSizeInTiles);
            int centerMacroCellY = WorldGrid.FloorDivide(
                warpedY,
                settings.Geology.MacroCellSizeInTiles);
            bool foundSite = false;
            GeologySiteData selectedSite = default;

            // ±192タイルのジッターでは3×3外のサイトも最近傍になり得るため、確定仕様どおり5×5を省略せず調べます。
            for (int offsetY = -settings.Geology.SiteSearchRadiusInCells;
                 offsetY <= settings.Geology.SiteSearchRadiusInCells;
                 offsetY++)
            {
                for (int offsetX = -settings.Geology.SiteSearchRadiusInCells;
                     offsetX <= settings.Geology.SiteSearchRadiusInCells;
                     offsetX++)
                {
                    if (!TryAdd(centerMacroCellX, offsetX, out int macroCellX)
                        || !TryAdd(centerMacroCellY, offsetY, out int macroCellY)
                        || !TryCreateSite(
                            settings,
                            macroCellX,
                            macroCellY,
                            warpedX,
                            warpedY,
                            out GeologySiteData candidateSite))
                    {
                        return false;
                    }

                    if (!foundSite || IsCandidatePreferred(candidateSite, selectedSite))
                    {
                        foundSite = true;
                        selectedSite = candidateSite;
                    }
                }
            }

            if (!foundSite
                || !TrySelectDefinitionForSite(
                    settings,
                    selectedSite,
                    out GeologyDefinition definition,
                    out ElevationClass siteElevationClass))
            {
                return false;
            }

            geology = new GeneratedGeologyData
            {
                Class = definition.Class,
                SiteElevationClass = siteElevationClass,
                Permeability = definition.Permeability,
                GroundwaterStorage = definition.GroundwaterStorage,
                ErosionResistance = definition.ErosionResistance,
                Flags = definition.Flags,
                Site = selectedSite
            };
            return true;
        }

        /// <summary>
        /// 1論理チャンクの全タイルが参照し得る地質サイトと分類を一度だけ生成します。
        /// Chunk生成Jobとチャンク状態ハッシュが4096回のサイト再生成を避けるため、タイル走査前に呼びます。
        /// </summary>
        /// <param name="settings">固定地質Definition、Seed、GeneratorVersionを持つ生成設定です。</param>
        /// <param name="chunkX">キャッシュ対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">キャッシュ対象の垂直論理チャンク座標です。</param>
        /// <param name="cache">成功時にチャンク周辺の候補サイトを返します。</param>
        /// <returns>設定とチャンク周辺座標が有効で、全候補サイトを作れた場合はtrueです。</returns>
        internal static bool TryCreateChunkCache(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            out GeologyChunkCache cache)
        {
            cache = default;
            if (!ValidateSettings(settings.Geology)
                || !TryBuildStableDefinitionList(
                    settings.Geology,
                    out FixedList512Bytes<GeologyDefinition> stableDefinitions))
            {
                return false;
            }

            long firstWorldTileX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long firstWorldTileY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long lastWorldTileX = firstWorldTileX + WorldGridConstants.LogicalChunkSize - 1L;
            long lastWorldTileY = firstWorldTileY + WorldGridConstants.LogicalChunkSize - 1L;
            long minimumWarpedX = firstWorldTileX - settings.Geology.WarpAmplitudeInTiles;
            long minimumWarpedY = firstWorldTileY - settings.Geology.WarpAmplitudeInTiles;
            long maximumWarpedX = lastWorldTileX + settings.Geology.WarpAmplitudeInTiles;
            long maximumWarpedY = lastWorldTileY + settings.Geology.WarpAmplitudeInTiles;
            if (minimumWarpedX < int.MinValue || minimumWarpedY < int.MinValue
                || maximumWarpedX > int.MaxValue || maximumWarpedY > int.MaxValue)
            {
                return false;
            }

            int minimumCenterMacroCellX = WorldGrid.FloorDivide(
                (int)minimumWarpedX,
                settings.Geology.MacroCellSizeInTiles);
            int minimumCenterMacroCellY = WorldGrid.FloorDivide(
                (int)minimumWarpedY,
                settings.Geology.MacroCellSizeInTiles);
            int maximumCenterMacroCellX = WorldGrid.FloorDivide(
                (int)maximumWarpedX,
                settings.Geology.MacroCellSizeInTiles);
            int maximumCenterMacroCellY = WorldGrid.FloorDivide(
                (int)maximumWarpedY,
                settings.Geology.MacroCellSizeInTiles);
            if (!TryAdd(
                    minimumCenterMacroCellX,
                    -settings.Geology.SiteSearchRadiusInCells,
                    out int minimumSiteMacroCellX)
                || !TryAdd(
                    minimumCenterMacroCellY,
                    -settings.Geology.SiteSearchRadiusInCells,
                    out int minimumSiteMacroCellY)
                || !TryAdd(
                    maximumCenterMacroCellX,
                    settings.Geology.SiteSearchRadiusInCells,
                    out int maximumSiteMacroCellX)
                || !TryAdd(
                    maximumCenterMacroCellY,
                    settings.Geology.SiteSearchRadiusInCells,
                    out int maximumSiteMacroCellY))
            {
                return false;
            }

            // 64タイル幅と±64タイルのワープでは中心セルが各軸最大2個となり、候補の和集合は最大6×6です。
            for (int macroCellY = minimumSiteMacroCellY;
                 macroCellY <= maximumSiteMacroCellY;
                 macroCellY++)
            {
                for (int macroCellX = minimumSiteMacroCellX;
                     macroCellX <= maximumSiteMacroCellX;
                     macroCellX++)
                {
                    if (cache.Sites.Length >= cache.Sites.Capacity
                        || !TryCreateSitePosition(
                            settings,
                            macroCellX,
                            macroCellY,
                            out GeologySiteData site)
                        || !TrySelectDefinitionForSite(
                            settings,
                            site,
                            stableDefinitions,
                            out GeologyDefinition definition,
                            out ElevationClass siteElevationClass))
                    {
                        return false;
                    }

                    cache.Sites.Add(new CachedGeologySite
                    {
                        Site = site,
                        Definition = definition,
                        SiteElevationClass = siteElevationClass
                    });
                }
            }

            return cache.Sites.Length > 0;
        }

        /// <summary>
        /// チャンクで共有したサイト一覧から、1タイルを支配する地質を選びます。
        /// Chunk生成Job内だけで使用し、各タイル固有の座標ワープと距離比較は省略しません。
        /// </summary>
        /// <param name="settings">キャッシュ作成時と同じ固定生成設定です。</param>
        /// <param name="cache">対象チャンク用に事前生成した候補サイト一覧です。</param>
        /// <param name="worldTileX">水平の絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直の絶対タイル座標です。</param>
        /// <param name="geology">成功時に分類、基礎特性、支配サイトを返します。</param>
        /// <returns>このタイル固有の5×5候補から最近傍サイトを選べた場合はtrueです。</returns>
        internal static bool TryGenerateFromChunkCache(
            in WorldGenerationSettings settings,
            in GeologyChunkCache cache,
            int worldTileX,
            int worldTileY,
            out GeneratedGeologyData geology)
        {
            geology = default;
            if (!TryCalculateWarpedCoordinate(
                settings,
                worldTileX,
                worldTileY,
                out int warpedX,
                out int warpedY))
            {
                return false;
            }

            int centerMacroCellX = WorldGrid.FloorDivide(
                warpedX,
                settings.Geology.MacroCellSizeInTiles);
            int centerMacroCellY = WorldGrid.FloorDivide(
                warpedY,
                settings.Geology.MacroCellSizeInTiles);
            bool foundSite = false;
            CachedGeologySite selectedCachedSite = default;
            GeologySiteData selectedSite = default;

            for (int index = 0; index < cache.Sites.Length; index++)
            {
                CachedGeologySite cachedSite = cache.Sites[index];
                long macroDifferenceX = (long)cachedSite.Site.MacroCellX - centerMacroCellX;
                long macroDifferenceY = (long)cachedSite.Site.MacroCellY - centerMacroCellY;
                if (macroDifferenceX < -settings.Geology.SiteSearchRadiusInCells
                    || macroDifferenceX > settings.Geology.SiteSearchRadiusInCells
                    || macroDifferenceY < -settings.Geology.SiteSearchRadiusInCells
                    || macroDifferenceY > settings.Geology.SiteSearchRadiusInCells)
                {
                    continue;
                }

                long differenceX = (long)warpedX - cachedSite.Site.SiteX;
                long differenceY = (long)warpedY - cachedSite.Site.SiteY;
                GeologySiteData candidateSite = cachedSite.Site;
                candidateSite.DistanceSquared = (differenceX * differenceX) + (differenceY * differenceY);
                if (!foundSite || IsCandidatePreferred(candidateSite, selectedSite))
                {
                    foundSite = true;
                    selectedCachedSite = cachedSite;
                    selectedSite = candidateSite;
                }
            }

            if (!foundSite)
            {
                return false;
            }

            geology = new GeneratedGeologyData
            {
                Class = selectedCachedSite.Definition.Class,
                SiteElevationClass = selectedCachedSite.SiteElevationClass,
                Permeability = selectedCachedSite.Definition.Permeability,
                GroundwaterStorage = selectedCachedSite.Definition.GroundwaterStorage,
                ErosionResistance = selectedCachedSite.Definition.ErosionResistance,
                Flags = selectedCachedSite.Definition.Flags,
                Site = selectedSite
            };
            return true;
        }

        /// <summary>
        /// 地質設定が、確定した8分類、寸法、基礎重み、標高別重み、必須フラグを満たすか検証します。
        /// </summary>
        /// <param name="settings">検証する固定地質設定です。</param>
        /// <returns>生成時に暗黙補正せず使用できる場合はtrueです。</returns>
        public static bool ValidateSettings(in GeologyGenerationSettings settings)
        {
            if (settings.MacroCellSizeInTiles != 512
                || settings.WarpCellSizeInTiles != 256
                || settings.WarpOctaveCount != 2
                || settings.WarpAmplitudeInTiles != 64
                || settings.SiteNegativeJitterInTiles != 192
                || settings.SitePositiveJitterInTiles != 191
                || settings.SiteSearchRadiusInCells != 2
                || settings.Definitions.Length != RequiredDefinitionCount)
            {
                return false;
            }

            int baseWeightTotal = 0;
            int foundClassMask = 0;
            long belowSeaLevelWeightTotal = 0;
            long lowlandWeightTotal = 0;
            long plainOrPlateauWeightTotal = 0;
            long mountainWeightTotal = 0;
            long alpineWeightTotal = 0;
            GeologyDefinitionFlags knownFlags = GeologyDefinitionFlags.SupportsKarst
                | GeologyDefinitionFlags.SupportsActiveVolcanism;

            // 1回の走査でID、フラグ、全標高帯の重みを検証し、4096タイル生成時の重複走査を抑えます。
            // ビット位置は安定IDから決めるため、Definition配列を並べ替えても検証結果は変わりません。
            for (int index = 0; index < settings.Definitions.Length; index++)
            {
                GeologyDefinition definition = settings.Definitions[index];
                int classValue = (int)definition.Class;
                if (classValue < (int)GeologyClass.UnconsolidatedSediment
                    || classValue > (int)GeologyClass.UltramaficRock)
                {
                    return false;
                }

                int classBit = 1 << (classValue - 1);
                if ((foundClassMask & classBit) != 0
                    || definition.BaseWeight == 0
                    || (definition.Flags & ~knownFlags) != 0)
                {
                    return false;
                }

                GeologyDefinitionFlags requiredFlags = GeologyDefinitionFlags.None;
                if (definition.Class == GeologyClass.Limestone)
                {
                    requiredFlags = GeologyDefinitionFlags.SupportsKarst;
                }
                else if (definition.Class == GeologyClass.VolcanicRock)
                {
                    requiredFlags = GeologyDefinitionFlags.SupportsActiveVolcanism;
                }

                if (definition.Flags != requiredFlags)
                {
                    return false;
                }

                foundClassMask |= classBit;
                baseWeightTotal += definition.BaseWeight;
                belowSeaLevelWeightTotal += (long)definition.BaseWeight
                    * definition.BelowSeaLevelMultiplier;
                lowlandWeightTotal += (long)definition.BaseWeight
                    * definition.LowlandMultiplier;
                plainOrPlateauWeightTotal += (long)definition.BaseWeight
                    * definition.PlainOrPlateauMultiplier;
                mountainWeightTotal += (long)definition.BaseWeight
                    * definition.MountainMultiplier;
                alpineWeightTotal += (long)definition.BaseWeight
                    * definition.AlpineMultiplier;
            }

            int requiredClassMask = (1 << RequiredDefinitionCount) - 1;
            return foundClassMask == requiredClassMask
                && baseWeightTotal == RequiredBaseWeightTotal
                && belowSeaLevelWeightTotal > 0
                && lowlandWeightTotal > 0
                && plainOrPlateauWeightTotal > 0
                && mountainWeightTotal > 0
                && alpineWeightTotal > 0;
        }

        /// <summary>
        /// 安定IDから地質Definitionを検索します。
        /// 配列位置を永続IDとして扱わず、Authoringで並びが変わっても同じ定義を返します。
        /// </summary>
        /// <param name="settings">検索対象の固定地質設定です。</param>
        /// <param name="geologyClass">取得する地質の安定IDです。</param>
        /// <param name="definition">成功時に一致したDefinitionを返します。</param>
        /// <returns>一致するDefinitionが1件見つかった場合はtrueです。</returns>
        public static bool TryGetDefinition(
            in GeologyGenerationSettings settings,
            GeologyClass geologyClass,
            out GeologyDefinition definition)
        {
            definition = default;
            bool found = false;
            for (int index = 0; index < settings.Definitions.Length; index++)
            {
                GeologyDefinition candidate = settings.Definitions[index];
                if (candidate.Class != geologyClass)
                {
                    continue;
                }

                if (found)
                {
                    definition = default;
                    return false;
                }

                found = true;
                definition = candidate;
            }

            return found;
        }

        /// <summary>
        /// 2サイトの距離と確定同点順を比較し、候補を現在値より優先するか返します。
        /// </summary>
        /// <param name="candidate">新しく比較する候補サイトです。</param>
        /// <param name="current">現在選択中のサイトです。</param>
        /// <returns>距離、SiteX、SiteY、MacroCellX、MacroCellYの順で候補が前ならtrueです。</returns>
        public static bool IsCandidatePreferred(
            in GeologySiteData candidate,
            in GeologySiteData current)
        {
            if (candidate.DistanceSquared != current.DistanceSquared)
            {
                return candidate.DistanceSquared < current.DistanceSquared;
            }

            if (candidate.SiteX != current.SiteX)
            {
                return candidate.SiteX < current.SiteX;
            }

            if (candidate.SiteY != current.SiteY)
            {
                return candidate.SiteY < current.SiteY;
            }

            if (candidate.MacroCellX != current.MacroCellX)
            {
                return candidate.MacroCellX < current.MacroCellX;
            }

            return candidate.MacroCellY < current.MacroCellY;
        }

        /// <summary>
        /// 地質境界専用ノイズを整数タイル変位へ変換します。
        /// サイト探索前に呼び、int座標域を周回させないため範囲外を明示的に失敗させます。
        /// </summary>
        /// <param name="settings">地質ワープ設定、Seed、GeneratorVersionを持つ生成設定です。</param>
        /// <param name="worldTileX">ワープ前の水平絶対タイル座標です。</param>
        /// <param name="worldTileY">ワープ前の垂直絶対タイル座標です。</param>
        /// <param name="warpedX">成功時に水平ワープ後の絶対タイル座標を返します。</param>
        /// <param name="warpedY">成功時に垂直ワープ後の絶対タイル座標を返します。</param>
        /// <returns>両座標をintで表現できた場合はtrueです。</returns>
        private static bool TryCalculateWarpedCoordinate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out int warpedX,
            out int warpedY)
        {
            ushort horizontalWarp = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.Geology.WarpCellSizeInTiles,
                settings.Geology.WarpOctaveCount,
                WorldGenerationPurposeSalts.GeologyWarpX);
            ushort verticalWarp = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                settings.Geology.WarpCellSizeInTiles,
                settings.Geology.WarpOctaveCount,
                WorldGenerationPurposeSalts.GeologyWarpY);

            long horizontalOffset = ((long)(horizontalWarp - 32768)
                * settings.Geology.WarpAmplitudeInTiles) / 32768;
            long verticalOffset = ((long)(verticalWarp - 32768)
                * settings.Geology.WarpAmplitudeInTiles) / 32768;
            long candidateX = (long)worldTileX + horizontalOffset;
            long candidateY = (long)worldTileY + verticalOffset;
            if (candidateX < int.MinValue || candidateX > int.MaxValue
                || candidateY < int.MinValue || candidateY > int.MaxValue)
            {
                warpedX = 0;
                warpedY = 0;
                return false;
            }

            warpedX = (int)candidateX;
            warpedY = (int)candidateY;
            return true;
        }

        /// <summary>
        /// マクロセル中心へ専用ジッターを加え、評価座標までの平方距離を求めます。
        /// 5×5候補の各セルで呼び、同じマクロセルが常に同じサイトを持つようにします。
        /// </summary>
        /// <param name="settings">サイト寸法、ジッター範囲、Seedを持つ生成設定です。</param>
        /// <param name="macroCellX">候補サイトを所有する水平マクロセル座標です。</param>
        /// <param name="macroCellY">候補サイトを所有する垂直マクロセル座標です。</param>
        /// <param name="warpedX">距離を測る水平ワープ後座標です。</param>
        /// <param name="warpedY">距離を測る垂直ワープ後座標です。</param>
        /// <param name="site">成功時にサイト座標、所有セル、平方距離を返します。</param>
        /// <returns>サイト絶対座標をintで表現できた場合はtrueです。</returns>
        private static bool TryCreateSite(
            in WorldGenerationSettings settings,
            int macroCellX,
            int macroCellY,
            int warpedX,
            int warpedY,
            out GeologySiteData site)
        {
            if (!TryCreateSitePosition(settings, macroCellX, macroCellY, out site))
            {
                return false;
            }

            // 候補は評価座標の前後2マクロセルに限定されるため、差の二乗はlong範囲へ十分収まります。
            long differenceX = (long)warpedX - site.SiteX;
            long differenceY = (long)warpedY - site.SiteY;
            site.DistanceSquared = (differenceX * differenceX) + (differenceY * differenceY);
            return true;
        }

        /// <summary>
        /// マクロセル中心へ専用ジッターを加え、距離計算前の安定したサイト座標を作ります。
        /// チャンクキャッシュ作成と単一タイル生成で共有し、ジッター式の差を防ぎます。
        /// </summary>
        /// <param name="settings">サイト寸法、ジッター範囲、Seedを持つ生成設定です。</param>
        /// <param name="macroCellX">サイトを所有する水平マクロセル座標です。</param>
        /// <param name="macroCellY">サイトを所有する垂直マクロセル座標です。</param>
        /// <param name="site">成功時に所有セルと絶対サイト座標を返します。距離は0です。</param>
        /// <returns>サイト絶対座標をintで表現できた場合はtrueです。</returns>
        private static bool TryCreateSitePosition(
            in WorldGenerationSettings settings,
            int macroCellX,
            int macroCellY,
            out GeologySiteData site)
        {
            site = default;
            ulong jitterRange = (ulong)settings.Geology.SiteNegativeJitterInTiles
                + (ulong)settings.Geology.SitePositiveJitterInTiles
                + 1UL;
            ulong horizontalJitterSample = SampleUnbiasedBounded(
                settings.WorldSeed,
                settings.GeneratorVersion,
                macroCellX,
                macroCellY,
                WorldGenerationPurposeSalts.GeologySiteJitterX,
                jitterRange);
            ulong verticalJitterSample = SampleUnbiasedBounded(
                settings.WorldSeed,
                settings.GeneratorVersion,
                macroCellX,
                macroCellY,
                WorldGenerationPurposeSalts.GeologySiteJitterY,
                jitterRange);
            long jitterX = (long)horizontalJitterSample - settings.Geology.SiteNegativeJitterInTiles;
            long jitterY = (long)verticalJitterSample - settings.Geology.SiteNegativeJitterInTiles;
            long halfCell = settings.Geology.MacroCellSizeInTiles / 2L;
            long siteX = ((long)macroCellX * settings.Geology.MacroCellSizeInTiles)
                + halfCell
                + jitterX;
            long siteY = ((long)macroCellY * settings.Geology.MacroCellSizeInTiles)
                + halfCell
                + jitterY;
            if (siteX < int.MinValue || siteX > int.MaxValue
                || siteY < int.MinValue || siteY > int.MaxValue)
            {
                return false;
            }

            site = new GeologySiteData
            {
                MacroCellX = macroCellX,
                MacroCellY = macroCellY,
                SiteX = (int)siteX,
                SiteY = (int)siteY,
                DistanceSquared = 0
            };
            return true;
        }

        /// <summary>
        /// サイト絶対座標の標高区分で重みを補正し、安定ID順の累積区間から1地質を選びます。
        /// 最近傍サイトの決定後に一度呼び、対象タイルの標高でサイト内分類が変わることを防ぎます。
        /// </summary>
        /// <param name="settings">標高生成、Definition、Seedを持つ生成設定です。</param>
        /// <param name="site">分類を一意に決める支配サイトです。</param>
        /// <param name="selectedDefinition">成功時に抽選された地質Definitionを返します。</param>
        /// <param name="siteElevationClass">成功時にサイト座標で再評価した標高区分を返します。</param>
        /// <returns>Definitionを安定ID順に並べ、正の総重みから選択できた場合はtrueです。</returns>
        private static bool TrySelectDefinitionForSite(
            in WorldGenerationSettings settings,
            in GeologySiteData site,
            out GeologyDefinition selectedDefinition,
            out ElevationClass siteElevationClass)
        {
            if (!TryBuildStableDefinitionList(
                settings.Geology,
                out FixedList512Bytes<GeologyDefinition> stableDefinitions))
            {
                selectedDefinition = default;
                siteElevationClass = default;
                return false;
            }

            return TrySelectDefinitionForSite(
                settings,
                site,
                stableDefinitions,
                out selectedDefinition,
                out siteElevationClass);
        }

        /// <summary>
        /// 既に安定ID順へ並べたDefinitionを使い、サイト標高と専用Saltから1地質を選びます。
        /// チャンクキャッシュでは同じ並べ替えをサイト数分繰り返さないために使用します。
        /// </summary>
        /// <param name="settings">標高生成と地質抽選Saltを持つ生成設定です。</param>
        /// <param name="site">分類を一意に決めるサイト絶対座標です。</param>
        /// <param name="stableDefinitions">GeologyClass 1〜8の順に正規化済みのDefinitionです。</param>
        /// <param name="selectedDefinition">成功時に抽選されたDefinitionを返します。</param>
        /// <param name="siteElevationClass">成功時にサイト絶対座標の標高区分を返します。</param>
        /// <returns>正の補正後総重みから1つ選択できた場合はtrueです。</returns>
        private static bool TrySelectDefinitionForSite(
            in WorldGenerationSettings settings,
            in GeologySiteData site,
            in FixedList512Bytes<GeologyDefinition> stableDefinitions,
            out GeologyDefinition selectedDefinition,
            out ElevationClass siteElevationClass)
        {
            ushort siteElevation = DeterministicEnvironmentGenerator.SampleElevation(
                settings,
                site.SiteX,
                site.SiteY);
            siteElevationClass = DeterministicEnvironmentGenerator.ClassifyElevation(
                siteElevation,
                settings);
            long totalAdjustedWeight = 0;

            // Definition配列の並びではなく安定ID順で総和と累積区間を作り、並べ替えによる生成差を防ぎます。
            for (int index = 0; index < stableDefinitions.Length; index++)
            {
                GeologyDefinition definition = stableDefinitions[index];
                totalAdjustedWeight += (long)definition.BaseWeight
                    * definition.GetElevationMultiplier(siteElevationClass);
            }

            if (totalAdjustedWeight <= 0)
            {
                selectedDefinition = default;
                return false;
            }

            ulong selectedWeight = SampleUnbiasedBounded(
                settings.WorldSeed,
                settings.GeneratorVersion,
                site.SiteX,
                site.SiteY,
                WorldGenerationPurposeSalts.GeologyClassSelection,
                (ulong)totalAdjustedWeight);
            ulong accumulatedWeight = 0;
            for (int index = 0; index < stableDefinitions.Length; index++)
            {
                GeologyDefinition definition = stableDefinitions[index];
                accumulatedWeight += (ulong)definition.BaseWeight
                    * definition.GetElevationMultiplier(siteElevationClass);
                if (selectedWeight < accumulatedWeight)
                {
                    selectedDefinition = definition;
                    return true;
                }
            }

            selectedDefinition = default;
            return false;
        }

        /// <summary>
        /// 配列順が任意のDefinitionを、安定ID順の一時FixedListへ正規化します。
        /// 管理ヒープを確保せず、同じサイト抽選区間をどのAuthoring順でも再現するために使用します。
        /// </summary>
        /// <param name="settings">順序を正規化する8件のDefinition設定です。</param>
        /// <param name="stableDefinitions">成功時にGeologyClass 1〜8の順でDefinitionを返します。</param>
        /// <returns>範囲内の安定IDが重複なく8件揃う場合はtrueです。</returns>
        private static bool TryBuildStableDefinitionList(
            in GeologyGenerationSettings settings,
            out FixedList512Bytes<GeologyDefinition> stableDefinitions)
        {
            stableDefinitions = default;
            for (int classIndex = 0; classIndex < RequiredDefinitionCount; classIndex++)
            {
                stableDefinitions.Add(default);
            }

            int foundClassMask = 0;
            for (int index = 0; index < settings.Definitions.Length; index++)
            {
                GeologyDefinition definition = settings.Definitions[index];
                int classIndex = (int)definition.Class - 1;
                if (classIndex < 0 || classIndex >= RequiredDefinitionCount)
                {
                    return false;
                }

                int classBit = 1 << classIndex;
                if ((foundClassMask & classBit) != 0)
                {
                    return false;
                }

                foundClassMask |= classBit;
                stableDefinitions[classIndex] = definition;
            }

            int requiredClassMask = (1 << RequiredDefinitionCount) - 1;
            return foundClassMask == requiredClassMask;
        }

        /// <summary>
        /// 64bitハッシュを剰余偏りなしで0以上上限未満へ変換します。
        /// 拒否時は同じ座標・Saltと試行番号から次の値を作り、共有乱数状態を使用しません。
        /// </summary>
        /// <param name="worldSeed">ワールド作成時に固定した64bit Seedです。</param>
        /// <param name="generatorVersion">生成規則の保存互換Versionです。</param>
        /// <param name="coordinateX">サイトまたはマクロセルの水平安定座標です。</param>
        /// <param name="coordinateY">サイトまたはマクロセルの垂直安定座標です。</param>
        /// <param name="purposeSalt">他用途の乱数列から分離するSaltです。</param>
        /// <param name="exclusiveUpperBound">0を含み、この値を含まない抽選上限です。</param>
        /// <returns>剰余偏りを除いた0以上上限未満の整数です。</returns>
        private static ulong SampleUnbiasedBounded(
            ulong worldSeed,
            int generatorVersion,
            int coordinateX,
            int coordinateY,
            ulong purposeSalt,
            ulong exclusiveUpperBound)
        {
            // 2^64を上限で割った余り未満を捨てると、受理範囲の各剰余が同じ件数になります。
            ulong rejectionThreshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;
            uint attempt = 0;
            while (true)
            {
                ulong hash = WorldSeedUtility.HashCoordinate(
                    worldSeed,
                    generatorVersion,
                    coordinateX,
                    coordinateY,
                    purposeSalt,
                    attempt);
                if (hash >= rejectionThreshold)
                {
                    return hash % exclusiveUpperBound;
                }

                attempt++;
            }
        }

        /// <summary>
        /// 2つのintをlongで加算し、候補マクロセルが座標域を周回しない場合だけintへ変換します。
        /// </summary>
        /// <param name="value">探索中心となるマクロセル座標です。</param>
        /// <param name="offset">探索半径内の相対セル数です。</param>
        /// <param name="result">成功時に加算後のマクロセル座標を返します。</param>
        /// <returns>加算結果がint範囲内の場合はtrueです。</returns>
        private static bool TryAdd(int value, int offset, out int result)
        {
            long candidate = (long)value + offset;
            if (candidate < int.MinValue || candidate > int.MaxValue)
            {
                result = 0;
                return false;
            }

            result = (int)candidate;
            return true;
        }
    }
}
