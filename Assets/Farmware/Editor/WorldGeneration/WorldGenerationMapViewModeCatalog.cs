// このファイルは、Map Viewerの全表示モードについて、表示名、生成経路、負荷上限、進捗方式、Hoverデータ契約を一か所で定義します。
// EditorWindowと純粋サンプラーは同じ定義を参照し、モード追加時に片方だけが古い上限や取得経路へ残ることを防ぎます。

using System;
using System.Collections.Generic;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 1ピクセルの色を作るためにMap Viewerが使用する生成経路です。
    /// 同じ経路では解像度上限、進捗、Hover用コピーの寿命管理も共通になります。
    /// </summary>
    public enum WorldGenerationMapSamplingPath : byte
    {
        /// <summary>C1〜C2の単一タイル純粋生成だけを使う軽量経路です。</summary>
        DirectTile = 0,

        /// <summary>C3の8×8基礎水文セルを必要な座標ごとに生成する経路です。</summary>
        BaseHydrologyCell = 1,

        /// <summary>C3〜C5-Cの完成診断チャンクから最終TerrainとBiomeを読む経路です。</summary>
        CompletedTerrainChunk = 2,

        /// <summary>C3〜C5-Cの完成診断チャンクからC4派生水文を読む経路です。</summary>
        CompletedHydrologyChunk = 3
    }

    /// <summary>
    /// 表示モードの最大サンプル数を決める負荷区分です。
    /// 実際の上限値は縮尺も必要なため、Catalogの計算メソッドがこの区分を解釈します。
    /// </summary>
    public enum WorldGenerationMapResolutionPolicy : byte
    {
        /// <summary>軽量な単一タイル生成へ通常の768サンプル上限を適用します。</summary>
        Standard = 0,

        /// <summary>上流探索を含む基礎水文へ64サンプル上限を適用します。</summary>
        BaseHydrology = 1,

        /// <summary>完成診断チャンク数が各軸3件以内になる動的上限を適用します。</summary>
        CompletedChunks = 2
    }

    /// <summary>
    /// Map Viewerが利用者へ報告する進捗の計算方式です。
    /// 重い完成チャンク生成と軽い画像行処理を同じ割合として扱わないために分離します。
    /// </summary>
    public enum WorldGenerationMapProgressPolicy : byte
    {
        /// <summary>画像行の完了数だけで0〜100%を報告します。</summary>
        PixelRows = 0,

        /// <summary>完成チャンクへ95%、画像行へ残り5%を割り当てます。</summary>
        CompletedChunksThenPixelRows = 1
    }

    /// <summary>
    /// 画像生成後にPointer下の中央情報へ返せる値コピーの種類です。
    /// Hover側が表示モード名から取得方法を推測せず、画像生成時と同じ結果を読むために使います。
    /// </summary>
    public enum WorldGenerationMapHoverDataContract : byte
    {
        /// <summary>単一タイルGeneratorから再計算できる基礎地形だけを使います。</summary>
        GeneratedTile = 0,

        /// <summary>完成地形と、同じ固定入力から得たBiome選択理由・上位候補を使います。</summary>
        FinalizedTerrainAndBiomeSelection = 1,

        /// <summary>C3の基礎水文セルを同じWindow内キャッシュから使います。</summary>
        BaseHydrologyCell = 2,

        /// <summary>完成診断チャンクから画像と同時にコピーしたC4派生水文を使います。</summary>
        DerivedHydrologyTile = 3
    }

    /// <summary>
    /// 1つのMap Viewer表示モードが必要とするUI名と生成契約です。
    /// 値はCatalog構築後に変更せず、バックグラウンド生成中も同じ契約を参照します。
    /// </summary>
    public readonly struct WorldGenerationMapViewModeDefinition
    {
        /// <summary>保存値とDropdown選択を結ぶ安定した表示モードです。</summary>
        public readonly WorldGenerationMapViewMode Mode;

        /// <summary>Map ViewerのDropdownへ表示する開発者向け英語名です。</summary>
        public readonly string DisplayName;

        /// <summary>画像1ピクセルを作る固定生成経路です。</summary>
        public readonly WorldGenerationMapSamplingPath SamplingPath;

        /// <summary>Viewportと縮尺から画像解像度上限を求める負荷区分です。</summary>
        public readonly WorldGenerationMapResolutionPolicy ResolutionPolicy;

        /// <summary>生成中の0〜100%を計算する方式です。</summary>
        public readonly WorldGenerationMapProgressPolicy ProgressPolicy;

        /// <summary>画像適用後にPointer下へ表示できる値コピーの契約です。</summary>
        public readonly WorldGenerationMapHoverDataContract HoverDataContract;

        /// <summary>変更不可の1モード定義を構築します。</summary>
        /// <param name="mode">保存と処理分岐に使う表示モードです。</param>
        /// <param name="displayName">Dropdownへ表示する英語名です。</param>
        /// <param name="samplingPath">固定生成の実行経路です。</param>
        /// <param name="resolutionPolicy">画像解像度の負荷区分です。</param>
        /// <param name="progressPolicy">進捗割合の計算方式です。</param>
        /// <param name="hoverDataContract">Hoverへ保持する値コピーの種類です。</param>
        public WorldGenerationMapViewModeDefinition(
            WorldGenerationMapViewMode mode,
            string displayName,
            WorldGenerationMapSamplingPath samplingPath,
            WorldGenerationMapResolutionPolicy resolutionPolicy,
            WorldGenerationMapProgressPolicy progressPolicy,
            WorldGenerationMapHoverDataContract hoverDataContract)
        {
            Mode = mode;
            DisplayName = displayName;
            SamplingPath = samplingPath;
            ResolutionPolicy = resolutionPolicy;
            ProgressPolicy = progressPolicy;
            HoverDataContract = hoverDataContract;
        }
    }

    /// <summary>
    /// 全Map Viewerモードをenum順で保持し、UI、サンプラー、テストへ同じ契約を公開します。
    /// 配列添字とenum値の一致を静的構築時に検証し、不完全な追加をEditor起動時に明示します。
    /// </summary>
    public static class WorldGenerationMapViewModeCatalog
    {
        /// <summary>現在定義済みの表示モード総数です。</summary>
        public const int ModeCount = 27;

        /// <summary>全モードの変更不可契約をenum値と同じ順で保持します。</summary>
        private static readonly WorldGenerationMapViewModeDefinition[] Definitions =
        {
            CompletedTerrain(WorldGenerationMapViewMode.Terrain, "Terrain"),
            CompletedTerrain(WorldGenerationMapViewMode.Biome, "Biome"),
            Direct(WorldGenerationMapViewMode.Elevation, "Elevation"),
            Direct(WorldGenerationMapViewMode.BaseTemperature, "Base Temperature"),
            Direct(WorldGenerationMapViewMode.TerrainTemperature, "Terrain Temperature"),
            Direct(WorldGenerationMapViewMode.BaseMoisture, "Base Moisture"),
            Direct(WorldGenerationMapViewMode.WoodyVegetationPotential, "Woody Vegetation Potential"),
            Direct(WorldGenerationMapViewMode.TerrainRuggedness, "Terrain Ruggedness"),
            Direct(WorldGenerationMapViewMode.CoastalInfluence, "Coastal Influence"),
            Direct(WorldGenerationMapViewMode.VolcanicActivityPotential, "Volcanic Activity Potential"),
            Direct(WorldGenerationMapViewMode.GeologyClass, "Geology Class"),
            Direct(WorldGenerationMapViewMode.GeologyPermeability, "Geology Permeability"),
            Direct(WorldGenerationMapViewMode.GeologyGroundwaterStorage, "Geology Groundwater Storage"),
            Direct(WorldGenerationMapViewMode.GeologyErosionResistance, "Geology Erosion Resistance"),
            Direct(WorldGenerationMapViewMode.GeologyFlags, "Geology Flags"),
            Direct(WorldGenerationMapViewMode.GeologyDominantSite, "Geology Dominant Site"),
            Direct(WorldGenerationMapViewMode.GeologySiteBoundary, "Geology Site Boundary"),
            BaseHydrology(WorldGenerationMapViewMode.HydrologyFlowDirection, "Hydrology Flow Direction"),
            BaseHydrology(WorldGenerationMapViewMode.HydrologyTerminal, "Hydrology Terminal"),
            BaseHydrology(WorldGenerationMapViewMode.HydrologyCatchment, "Hydrology Catchment"),
            BaseHydrology(WorldGenerationMapViewMode.HydrologySurfaceWater, "Hydrology Surface Water"),
            BaseHydrology(WorldGenerationMapViewMode.HydrologyLake, "Hydrology Lake"),
            CompletedHydrology(WorldGenerationMapViewMode.HydrologyGroundwater, "Hydrology Groundwater"),
            CompletedHydrology(
                WorldGenerationMapViewMode.HydrologyNaturalSoilSaturation,
                "Hydrology Natural Soil Saturation"),
            CompletedHydrology(WorldGenerationMapViewMode.HydrologySpring, "Hydrology Spring"),
            CompletedHydrology(
                WorldGenerationMapViewMode.HydrologyNaturalFlooding,
                "Hydrology Natural Flooding"),
            CompletedHydrology(
                WorldGenerationMapViewMode.HydrologyWetlandCandidate,
                "Hydrology Wetland Candidate")
        };

        /// <summary>Dropdown用表示名をenum順で保持し、Window生成のたびに文字列を再構築しません。</summary>
        private static readonly string[] DisplayNames = CreateAndValidateDisplayNames();

        /// <summary>全表示名をenum順の読み取り専用一覧として返します。</summary>
        public static IReadOnlyList<string> OrderedDisplayNames => DisplayNames;

        /// <summary>指定モードの全生成契約を返します。</summary>
        /// <param name="mode">取得する定義済み表示モードです。</param>
        /// <returns>enum値と一対一の固定モード定義です。</returns>
        /// <exception cref="ArgumentOutOfRangeException">現在のCatalogに存在しない値を指定した場合に発生します。</exception>
        public static WorldGenerationMapViewModeDefinition Get(
            WorldGenerationMapViewMode mode)
        {
            int index = (int)mode;
            if (index < 0 || index >= Definitions.Length || Definitions[index].Mode != mode)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return Definitions[index];
        }

        /// <summary>Dropdownの0始まりIndexを定義済みモードへ変換します。</summary>
        /// <param name="selectedIndex">Dropdownが返す選択Indexです。未選択時は負値です。</param>
        /// <returns>範囲内なら対応モード、範囲外なら安全なTerrainです。</returns>
        public static WorldGenerationMapViewMode ResolveChoiceIndex(int selectedIndex)
        {
            return selectedIndex >= 0 && selectedIndex < Definitions.Length
                ? Definitions[selectedIndex].Mode
                : WorldGenerationMapViewMode.Terrain;
        }

        /// <summary>表示モードと縮尺から、画像1辺へ許可する最大サンプル数を返します。</summary>
        /// <param name="mode">負荷契約を参照する表示モードです。</param>
        /// <param name="tilesPerPixel">画像1ピクセルが代表する1以上のタイル数です。</param>
        /// <returns>通常768、基礎水文64、または完成チャンク数から求めた1〜64です。</returns>
        public static int GetMaximumSampleResolution(
            WorldGenerationMapViewMode mode,
            int tilesPerPixel)
        {
            if (tilesPerPixel <= 0)
            {
                // 直接生成と基礎水文でも無効な縮尺を黙って受理せず、全モードで同じ公開契約にします。
                throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
            }

            WorldGenerationMapViewModeDefinition definition = Get(mode);
            if (definition.ResolutionPolicy == WorldGenerationMapResolutionPolicy.CompletedChunks)
            {
                return WorldGenerationMapSampler.CalculateMaximumCompletedChunkResolution(
                    tilesPerPixel);
            }

            return definition.ResolutionPolicy == WorldGenerationMapResolutionPolicy.BaseHydrology
                ? WorldGenerationMapSampler.MaximumHydrologyResolution
                : WorldGenerationMapSampler.MaximumResolution;
        }

        /// <summary>軽量な単一タイル生成を使うモード定義を作ります。</summary>
        /// <param name="mode">定義する表示モードです。</param>
        /// <param name="displayName">Dropdownへ表示する英語名です。</param>
        /// <returns>通常解像度・行進捗・基礎地形Hoverの定義です。</returns>
        private static WorldGenerationMapViewModeDefinition Direct(
            WorldGenerationMapViewMode mode,
            string displayName)
        {
            return new WorldGenerationMapViewModeDefinition(
                mode,
                displayName,
                WorldGenerationMapSamplingPath.DirectTile,
                WorldGenerationMapResolutionPolicy.Standard,
                WorldGenerationMapProgressPolicy.PixelRows,
                WorldGenerationMapHoverDataContract.GeneratedTile);
        }

        /// <summary>C3基礎水文セルを使うモード定義を作ります。</summary>
        /// <param name="mode">定義する水文表示モードです。</param>
        /// <param name="displayName">Dropdownへ表示する英語名です。</param>
        /// <returns>水文解像度・行進捗・基礎水文Hoverの定義です。</returns>
        private static WorldGenerationMapViewModeDefinition BaseHydrology(
            WorldGenerationMapViewMode mode,
            string displayName)
        {
            return new WorldGenerationMapViewModeDefinition(
                mode,
                displayName,
                WorldGenerationMapSamplingPath.BaseHydrologyCell,
                WorldGenerationMapResolutionPolicy.BaseHydrology,
                WorldGenerationMapProgressPolicy.PixelRows,
                WorldGenerationMapHoverDataContract.BaseHydrologyCell);
        }

        /// <summary>C5-C完成地形とBiome診断を使うモード定義を作ります。</summary>
        /// <param name="mode">TerrainまたはBiomeです。</param>
        /// <param name="displayName">Dropdownへ表示する英語名です。</param>
        /// <returns>完成チャンク上限・二段階進捗・Biome診断Hoverの定義です。</returns>
        private static WorldGenerationMapViewModeDefinition CompletedTerrain(
            WorldGenerationMapViewMode mode,
            string displayName)
        {
            return new WorldGenerationMapViewModeDefinition(
                mode,
                displayName,
                WorldGenerationMapSamplingPath.CompletedTerrainChunk,
                WorldGenerationMapResolutionPolicy.CompletedChunks,
                WorldGenerationMapProgressPolicy.CompletedChunksThenPixelRows,
                WorldGenerationMapHoverDataContract.FinalizedTerrainAndBiomeSelection);
        }

        /// <summary>C4派生水文を完成チャンクから読むモード定義を作ります。</summary>
        /// <param name="mode">定義する派生水文表示モードです。</param>
        /// <param name="displayName">Dropdownへ表示する英語名です。</param>
        /// <returns>完成チャンク上限・二段階進捗・派生水文Hoverの定義です。</returns>
        private static WorldGenerationMapViewModeDefinition CompletedHydrology(
            WorldGenerationMapViewMode mode,
            string displayName)
        {
            return new WorldGenerationMapViewModeDefinition(
                mode,
                displayName,
                WorldGenerationMapSamplingPath.CompletedHydrologyChunk,
                WorldGenerationMapResolutionPolicy.CompletedChunks,
                WorldGenerationMapProgressPolicy.CompletedChunksThenPixelRows,
                WorldGenerationMapHoverDataContract.DerivedHydrologyTile);
        }

        /// <summary>全enum値が欠落なく重複なく並ぶことを検証し、Dropdown用表示名を作ります。</summary>
        /// <returns>Definitionsと同じ順序・件数の表示名配列です。</returns>
        private static string[] CreateAndValidateDisplayNames()
        {
            Array enumValues = Enum.GetValues(typeof(WorldGenerationMapViewMode));
            if (Definitions.Length != ModeCount || enumValues.Length != ModeCount)
            {
                throw new InvalidOperationException(
                    $"Map Viewerモード定義数が一致しません: Definitions={Definitions.Length}, Enum={enumValues.Length}, Expected={ModeCount}");
            }

            string[] displayNames = new string[Definitions.Length];
            for (int index = 0; index < Definitions.Length; index++)
            {
                WorldGenerationMapViewMode expectedMode =
                    (WorldGenerationMapViewMode)(byte)index;
                WorldGenerationMapViewModeDefinition definition = Definitions[index];
                if (definition.Mode != expectedMode || string.IsNullOrWhiteSpace(definition.DisplayName))
                {
                    throw new InvalidOperationException(
                        $"Map Viewerモード定義がenum順ではないか表示名が空です: Index={index}, Mode={definition.Mode}");
                }

                // 完成チャンクを生成するモードだけが二段階進捗を使う必要があります。
                // bool同士を名前付き変数へ分け、何の一致を検証しているかを読み取りやすくします。
                bool usesCompletedChunkResolution = definition.ResolutionPolicy
                    == WorldGenerationMapResolutionPolicy.CompletedChunks;
                bool usesCompletedChunkProgress = definition.ProgressPolicy
                    == WorldGenerationMapProgressPolicy.CompletedChunksThenPixelRows;
                if (usesCompletedChunkResolution != usesCompletedChunkProgress)
                {
                    throw new InvalidOperationException(
                        $"完成チャンク上限と進捗方式が一致しません: Mode={definition.Mode}");
                }

                displayNames[index] = definition.DisplayName;
            }

            return displayNames;
        }
    }
}
