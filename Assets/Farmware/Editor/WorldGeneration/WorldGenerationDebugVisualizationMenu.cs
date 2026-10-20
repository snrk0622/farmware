// このファイルは、PlayMode中の地形・資源・動物個体群の開発者用可視化をUnity Editorメニューから切り替えます。
// Editor Assemblyだけに属し、プレイヤー用入力や製品版ビルドへ開発者専用の操作UIを追加しません。

using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Farmwareメニューの各項目を、実行中のWorldGenerationDebugVisualizationOverlayへ中継します。
    /// MenuItemはPlayModeかつ対象Componentが存在するときだけ有効になり、現在のモードへチェックを付けます。
    /// </summary>
    public static class WorldGenerationDebugVisualizationMenu
    {
        /// <summary>可視化を無効にするメニューパスです。</summary>
        private const string OffMenuPath = "Farmware/World Generation/Debug Visualization/Off";

        /// <summary>論理バイオーム表示へ切り替えるメニューパスです。</summary>
        private const string BiomeMenuPath = "Farmware/World Generation/Debug Visualization/Biome";

        /// <summary>標準・肥沃・砂質の土壌表示へ切り替えるメニューパスです。</summary>
        private const string SoilMenuPath = "Farmware/World Generation/Debug Visualization/Soil";

        /// <summary>連続標高表示へ切り替えるメニューパスです。</summary>
        private const string ElevationMenuPath = "Farmware/World Generation/Debug Visualization/Elevation";

        /// <summary>標高補正前の基礎気温表示へ切り替えるメニューパスです。</summary>
        private const string BaseTemperatureMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Base Temperature";

        /// <summary>標高寒冷補正後の地形気温表示へ切り替えるメニューパスです。</summary>
        private const string TerrainTemperatureMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Terrain Temperature";

        /// <summary>現在土壌水分から独立した基礎湿度表示へ切り替えるメニューパスです。</summary>
        private const string BaseMoistureMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Base Moisture";

        /// <summary>木本植生ポテンシャル表示へ切り替えるメニューパスです。</summary>
        private const string WoodyVegetationMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Woody Vegetation Potential";

        /// <summary>地形起伏表示へ切り替えるメニューパスです。</summary>
        private const string TerrainRuggednessMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Terrain Ruggedness";

        /// <summary>海洋影響表示へ切り替えるメニューパスです。</summary>
        private const string CoastalInfluenceMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Coastal Influence";

        /// <summary>旧Weirdnessと独立した火山活動度表示へ切り替えるメニューパスです。</summary>
        private const string VolcanicActivityMenuPath =
            "Farmware/World Generation/Debug Visualization/Environment/Volcanic Activity Potential";

        /// <summary>卓越基盤地質の8分類表示へ切り替えるメニューパスです。</summary>
        private const string GeologyClassMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Class";

        /// <summary>地質Definitionの基礎透水性表示へ切り替えるメニューパスです。</summary>
        private const string GeologyPermeabilityMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Permeability";

        /// <summary>地質Definitionの地下水保持力表示へ切り替えるメニューパスです。</summary>
        private const string GeologyGroundwaterStorageMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Groundwater Storage";

        /// <summary>地質Definitionの侵食抵抗表示へ切り替えるメニューパスです。</summary>
        private const string GeologyErosionResistanceMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Erosion Resistance";

        /// <summary>カルスト・活動的火山の必須条件フラグ表示へ切り替えるメニューパスです。</summary>
        private const string GeologyFlagsMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Flags";

        /// <summary>支配地質サイトごとの疑似色表示へ切り替えるメニューパスです。</summary>
        private const string GeologyDominantSiteMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Dominant Site";

        /// <summary>支配地質サイトの1タイル境界表示へ切り替えるメニューパスです。</summary>
        private const string GeologySiteBoundaryMenuPath =
            "Farmware/World Generation/Debug Visualization/Geology/Site Boundary";

        /// <summary>淡水・海水表示へ切り替えるメニューパスです。</summary>
        private const string WaterSalinityMenuPath =
            "Farmware/World Generation/Debug Visualization/Water Salinity";

        /// <summary>川・池・海表示へ切り替えるメニューパスです。</summary>
        private const string WaterBodyMenuPath =
            "Farmware/World Generation/Debug Visualization/Water Body";

        /// <summary>岩壁の最終形状と内部影濃度表示へ切り替えるメニューパスです。</summary>
        private const string RockWallMenuPath =
            "Farmware/World Generation/Debug Visualization/Rock Wall";

        /// <summary>マグマ本体と淡水抑制用の火山影響域表示へ切り替えるメニューパスです。</summary>
        private const string MagmaMenuPath =
            "Farmware/World Generation/Debug Visualization/Magma";

        /// <summary>最低間隔と競合解決後の基礎資源表示へ切り替えるメニューパスです。</summary>
        private const string ResourcesMenuPath =
            "Farmware/World Generation/Debug Visualization/Resources";

        /// <summary>通常設備・橋と耐熱要件の地形属性表示へ切り替えるメニューパスです。</summary>
        private const string TerrainPlacementMenuPath =
            "Farmware/World Generation/Debug Visualization/Terrain Placement";

        /// <summary>動物の配置候補と確定個体位置の表示へ切り替えるメニューパスです。</summary>
        private const string AnimalPopulationMenuPath =
            "Farmware/World Generation/Debug Visualization/Animal Population";

        /// <summary>地上レイヤーを診断対象にするメニューパスです。</summary>
        private const string SurfaceLayerMenuPath =
            "Farmware/World Generation/Debug Visualization/Layer/Surface";

        /// <summary>海中レイヤーを診断対象にするメニューパスです。</summary>
        private const string UnderwaterLayerMenuPath =
            "Farmware/World Generation/Debug Visualization/Layer/Underwater";

        /// <summary>深海レイヤーを診断対象にするメニューパスです。</summary>
        private const string DeepSeaLayerMenuPath =
            "Farmware/World Generation/Debug Visualization/Layer/Deep Sea";

        /// <summary>PlayMode中の開発者用可視化を通常表示へ戻します。</summary>
        [MenuItem(OffMenuPath, false, 200)]
        private static void SelectOff()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.None);
        }

        /// <summary>PlayMode中の論理Biome IDを7色で表示します。</summary>
        [MenuItem(BiomeMenuPath, false, 201)]
        private static void SelectBiome()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Biome);
        }

        /// <summary>PlayMode中のSoilTypeを標準・肥沃・砂質の固定色で表示します。</summary>
        [MenuItem(SoilMenuPath, false, 202)]
        private static void SelectSoil()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Soil);
        }

        /// <summary>PlayMode中の連続標高値を黒から白の濃淡で表示します。</summary>
        [MenuItem(ElevationMenuPath, false, 203)]
        private static void SelectElevation()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Elevation);
        }

        /// <summary>PlayMode中の標高補正前の基礎気温を青から赤のグラデーションで表示します。</summary>
        [MenuItem(BaseTemperatureMenuPath, false, 204)]
        private static void SelectBaseTemperature()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.BaseTemperature);
        }

        /// <summary>PlayMode中の標高寒冷補正後の地形気温を青から赤のグラデーションで表示します。</summary>
        [MenuItem(TerrainTemperatureMenuPath, false, 205)]
        private static void SelectTerrainTemperature()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.TerrainTemperature);
        }

        /// <summary>PlayMode中の基礎湿度を茶から水色のグラデーションで表示します。</summary>
        [MenuItem(BaseMoistureMenuPath, false, 206)]
        private static void SelectBaseMoisture()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.BaseMoisture);
        }

        /// <summary>PlayMode中の木本植生ポテンシャルを茶から緑のグラデーションで表示します。</summary>
        [MenuItem(WoodyVegetationMenuPath, false, 207)]
        private static void SelectWoodyVegetation()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.WoodyVegetationPotential);
        }

        /// <summary>PlayMode中の地形起伏を平坦側の明色から急峻側の暗色で表示します。</summary>
        [MenuItem(TerrainRuggednessMenuPath, false, 208)]
        private static void SelectTerrainRuggedness()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.TerrainRuggedness);
        }

        /// <summary>PlayMode中の海洋影響を内陸色から沿岸色のグラデーションで表示します。</summary>
        [MenuItem(CoastalInfluenceMenuPath, false, 209)]
        private static void SelectCoastalInfluence()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.CoastalInfluence);
        }

        /// <summary>PlayMode中の火山活動度を非活動色から活動色のグラデーションで表示します。</summary>
        [MenuItem(VolcanicActivityMenuPath, false, 210)]
        private static void SelectVolcanicActivity()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.VolcanicActivityPotential);
        }

        /// <summary>PlayMode中の卓越基盤地質を8種類の固定色で表示します。</summary>
        [MenuItem(GeologyClassMenuPath, false, 211)]
        private static void SelectGeologyClass()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologyClass);
        }

        /// <summary>PlayMode中の基礎透水性を低値から高値のグラデーションで表示します。</summary>
        [MenuItem(GeologyPermeabilityMenuPath, false, 212)]
        private static void SelectGeologyPermeability()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologyPermeability);
        }

        /// <summary>PlayMode中の地下水保持力を低値から高値のグラデーションで表示します。</summary>
        [MenuItem(GeologyGroundwaterStorageMenuPath, false, 213)]
        private static void SelectGeologyGroundwaterStorage()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage);
        }

        /// <summary>PlayMode中の侵食抵抗を低値から高値のグラデーションで表示します。</summary>
        [MenuItem(GeologyErosionResistanceMenuPath, false, 214)]
        private static void SelectGeologyErosionResistance()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologyErosionResistance);
        }

        /// <summary>PlayMode中の地質必須条件フラグを固定色で表示します。</summary>
        [MenuItem(GeologyFlagsMenuPath, false, 215)]
        private static void SelectGeologyFlags()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologyFlags);
        }

        /// <summary>PlayMode中の支配地質サイトをサイトごとの安定疑似色で表示します。</summary>
        [MenuItem(GeologyDominantSiteMenuPath, false, 216)]
        private static void SelectGeologyDominantSite()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologyDominantSite);
        }

        /// <summary>PlayMode中の支配地質サイト境界を1タイル幅の明色で表示します。</summary>
        [MenuItem(GeologySiteBoundaryMenuPath, false, 217)]
        private static void SelectGeologySiteBoundary()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.GeologySiteBoundary);
        }

        /// <summary>PlayMode中の水域を陸地・淡水・海水の固定色で表示します。</summary>
        [MenuItem(WaterSalinityMenuPath, false, 220)]
        private static void SelectWaterSalinity()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.WaterSalinity);
        }

        /// <summary>PlayMode中の水域を陸地・川・池・海の固定色で表示します。</summary>
        [MenuItem(WaterBodyMenuPath, false, 221)]
        private static void SelectWaterBody()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.WaterBody);
        }

        /// <summary>PlayMode中の岩壁を非岩壁・露出面・内部影の濃淡で表示します。</summary>
        [MenuItem(RockWallMenuPath, false, 222)]
        private static void SelectRockWall()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.RockWall);
        }

        /// <summary>PlayMode中のマグマ本体と周囲の淡水抑制域を固定色で表示します。</summary>
        [MenuItem(MagmaMenuPath, false, 223)]
        private static void SelectMagma()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Magma);
        }

        /// <summary>PlayMode中の最終資源配置を木・石・砂の固定色で表示します。</summary>
        [MenuItem(ResourcesMenuPath, false, 224)]
        private static void SelectResources()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Resources);
        }

        /// <summary>PlayMode中の地形配置属性を、通常地面・水上橋・耐熱橋・配置不可の固定色で表示します。</summary>
        [MenuItem(TerrainPlacementMenuPath, false, 225)]
        private static void SelectTerrainPlacement()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.TerrainPlacement);
        }

        /// <summary>PlayMode中の動物配置候補と確定個体位置を別色で表示します。</summary>
        [MenuItem(AnimalPopulationMenuPath, false, 226)]
        private static void SelectAnimalPopulation()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.AnimalPopulation);
        }

        /// <summary>PlayMode中の診断検索を地上レイヤーへ切り替えます。</summary>
        [MenuItem(SurfaceLayerMenuPath, false, 240)]
        private static void SelectSurfaceLayer()
        {
            SelectLayer(WorldLayer.Surface);
        }

        /// <summary>PlayMode中の診断検索を海中レイヤーへ切り替えます。未生成なら空表示になります。</summary>
        [MenuItem(UnderwaterLayerMenuPath, false, 241)]
        private static void SelectUnderwaterLayer()
        {
            SelectLayer(WorldLayer.Underwater);
        }

        /// <summary>PlayMode中の診断検索を深海レイヤーへ切り替えます。未生成なら空表示になります。</summary>
        [MenuItem(DeepSeaLayerMenuPath, false, 242)]
        private static void SelectDeepSeaLayer()
        {
            SelectLayer(WorldLayer.DeepSea);
        }

        /// <summary>Off項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(OffMenuPath, true)]
        private static bool ValidateOff()
        {
            return ValidateMode(OffMenuPath, WorldGenerationDebugVisualizationMode.None);
        }

        /// <summary>Biome項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(BiomeMenuPath, true)]
        private static bool ValidateBiome()
        {
            return ValidateMode(BiomeMenuPath, WorldGenerationDebugVisualizationMode.Biome);
        }

        /// <summary>Soil項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(SoilMenuPath, true)]
        private static bool ValidateSoil()
        {
            return ValidateMode(SoilMenuPath, WorldGenerationDebugVisualizationMode.Soil);
        }

        /// <summary>Elevation項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(ElevationMenuPath, true)]
        private static bool ValidateElevation()
        {
            return ValidateMode(ElevationMenuPath, WorldGenerationDebugVisualizationMode.Elevation);
        }

        /// <summary>Base Temperature項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(BaseTemperatureMenuPath, true)]
        private static bool ValidateBaseTemperature()
        {
            return ValidateMode(
                BaseTemperatureMenuPath,
                WorldGenerationDebugVisualizationMode.BaseTemperature);
        }

        /// <summary>Terrain Temperature項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(TerrainTemperatureMenuPath, true)]
        private static bool ValidateTerrainTemperature()
        {
            return ValidateMode(
                TerrainTemperatureMenuPath,
                WorldGenerationDebugVisualizationMode.TerrainTemperature);
        }

        /// <summary>Base Moisture項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(BaseMoistureMenuPath, true)]
        private static bool ValidateBaseMoisture()
        {
            return ValidateMode(
                BaseMoistureMenuPath,
                WorldGenerationDebugVisualizationMode.BaseMoisture);
        }

        /// <summary>Woody Vegetation Potential項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(WoodyVegetationMenuPath, true)]
        private static bool ValidateWoodyVegetation()
        {
            return ValidateMode(
                WoodyVegetationMenuPath,
                WorldGenerationDebugVisualizationMode.WoodyVegetationPotential);
        }

        /// <summary>Terrain Ruggedness項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(TerrainRuggednessMenuPath, true)]
        private static bool ValidateTerrainRuggedness()
        {
            return ValidateMode(
                TerrainRuggednessMenuPath,
                WorldGenerationDebugVisualizationMode.TerrainRuggedness);
        }

        /// <summary>Coastal Influence項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(CoastalInfluenceMenuPath, true)]
        private static bool ValidateCoastalInfluence()
        {
            return ValidateMode(
                CoastalInfluenceMenuPath,
                WorldGenerationDebugVisualizationMode.CoastalInfluence);
        }

        /// <summary>Volcanic Activity Potential項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(VolcanicActivityMenuPath, true)]
        private static bool ValidateVolcanicActivity()
        {
            return ValidateMode(
                VolcanicActivityMenuPath,
                WorldGenerationDebugVisualizationMode.VolcanicActivityPotential);
        }

        /// <summary>Geology Class項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologyClassMenuPath, true)]
        private static bool ValidateGeologyClass()
        {
            return ValidateMode(GeologyClassMenuPath, WorldGenerationDebugVisualizationMode.GeologyClass);
        }

        /// <summary>Geology Permeability項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologyPermeabilityMenuPath, true)]
        private static bool ValidateGeologyPermeability()
        {
            return ValidateMode(
                GeologyPermeabilityMenuPath,
                WorldGenerationDebugVisualizationMode.GeologyPermeability);
        }

        /// <summary>Geology Groundwater Storage項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologyGroundwaterStorageMenuPath, true)]
        private static bool ValidateGeologyGroundwaterStorage()
        {
            return ValidateMode(
                GeologyGroundwaterStorageMenuPath,
                WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage);
        }

        /// <summary>Geology Erosion Resistance項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologyErosionResistanceMenuPath, true)]
        private static bool ValidateGeologyErosionResistance()
        {
            return ValidateMode(
                GeologyErosionResistanceMenuPath,
                WorldGenerationDebugVisualizationMode.GeologyErosionResistance);
        }

        /// <summary>Geology Flags項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologyFlagsMenuPath, true)]
        private static bool ValidateGeologyFlags()
        {
            return ValidateMode(GeologyFlagsMenuPath, WorldGenerationDebugVisualizationMode.GeologyFlags);
        }

        /// <summary>Geology Dominant Site項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologyDominantSiteMenuPath, true)]
        private static bool ValidateGeologyDominantSite()
        {
            return ValidateMode(
                GeologyDominantSiteMenuPath,
                WorldGenerationDebugVisualizationMode.GeologyDominantSite);
        }

        /// <summary>Geology Site Boundary項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(GeologySiteBoundaryMenuPath, true)]
        private static bool ValidateGeologySiteBoundary()
        {
            return ValidateMode(
                GeologySiteBoundaryMenuPath,
                WorldGenerationDebugVisualizationMode.GeologySiteBoundary);
        }

        /// <summary>Water Salinity項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(WaterSalinityMenuPath, true)]
        private static bool ValidateWaterSalinity()
        {
            return ValidateMode(
                WaterSalinityMenuPath,
                WorldGenerationDebugVisualizationMode.WaterSalinity);
        }

        /// <summary>Water Body項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(WaterBodyMenuPath, true)]
        private static bool ValidateWaterBody()
        {
            return ValidateMode(WaterBodyMenuPath, WorldGenerationDebugVisualizationMode.WaterBody);
        }

        /// <summary>Rock Wall項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(RockWallMenuPath, true)]
        private static bool ValidateRockWall()
        {
            return ValidateMode(RockWallMenuPath, WorldGenerationDebugVisualizationMode.RockWall);
        }

        /// <summary>Magma項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(MagmaMenuPath, true)]
        private static bool ValidateMagma()
        {
            return ValidateMode(MagmaMenuPath, WorldGenerationDebugVisualizationMode.Magma);
        }

        /// <summary>Resources項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(ResourcesMenuPath, true)]
        private static bool ValidateResources()
        {
            return ValidateMode(ResourcesMenuPath, WorldGenerationDebugVisualizationMode.Resources);
        }

        /// <summary>Terrain Placement項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(TerrainPlacementMenuPath, true)]
        private static bool ValidateTerrainPlacement()
        {
            return ValidateMode(
                TerrainPlacementMenuPath,
                WorldGenerationDebugVisualizationMode.TerrainPlacement);
        }

        /// <summary>Animal Population項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(AnimalPopulationMenuPath, true)]
        private static bool ValidateAnimalPopulation()
        {
            return ValidateMode(
                AnimalPopulationMenuPath,
                WorldGenerationDebugVisualizationMode.AnimalPopulation);
        }

        /// <summary>Surfaceレイヤー項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(SurfaceLayerMenuPath, true)]
        private static bool ValidateSurfaceLayer()
        {
            return ValidateLayer(SurfaceLayerMenuPath, WorldLayer.Surface);
        }

        /// <summary>Underwaterレイヤー項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(UnderwaterLayerMenuPath, true)]
        private static bool ValidateUnderwaterLayer()
        {
            return ValidateLayer(UnderwaterLayerMenuPath, WorldLayer.Underwater);
        }

        /// <summary>DeepSeaレイヤー項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(DeepSeaLayerMenuPath, true)]
        private static bool ValidateDeepSeaLayer()
        {
            return ValidateLayer(DeepSeaLayerMenuPath, WorldLayer.DeepSea);
        }

        /// <summary>
        /// 実行中のOverlayを検索し、選択されたモードを設定してGameビューの再描画を要求します。
        /// </summary>
        /// <param name="mode">Editorメニューで選択された可視化モードです。</param>
        private static void SelectMode(WorldGenerationDebugVisualizationMode mode)
        {
            WorldGenerationDebugVisualizationOverlay overlay = FindOverlay();
            if (!EditorApplication.isPlaying || overlay == null)
            {
                Debug.LogWarning("ワールド生成デバッグ可視化はSampleSceneのPlayMode中だけ切り替えられます。");
                return;
            }

            overlay.SetMode(mode);
            EditorApplication.QueuePlayerLoopUpdate();

            // Gameビューが非フォーカスでも前モードのIMGUIを保持しないよう、全Editorビューへ即時Repaintを要求します。
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// 実行中のOverlayを検索し、診断対象の固定ワールドレイヤーを設定してGameビューの再描画を要求します。
        /// </summary>
        /// <param name="layer">Editorメニューで選択された固定ワールドレイヤーです。</param>
        private static void SelectLayer(WorldLayer layer)
        {
            WorldGenerationDebugVisualizationOverlay overlay = FindOverlay();
            if (!EditorApplication.isPlaying || overlay == null)
            {
                Debug.LogWarning("ワールド生成デバッグレイヤーはSampleSceneのPlayMode中だけ切り替えられます。");
                return;
            }

            overlay.SetLayer(layer);
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// メニューを開いたときに現在モードへチェックを付け、PlayMode外の項目を無効化します。
        /// </summary>
        /// <param name="menuPath">チェック状態を更新する完全なメニューパスです。</param>
        /// <param name="mode">この項目が表す可視化モードです。</param>
        /// <returns>PlayMode中にOverlayが見つかり、選択可能な場合はtrueです。</returns>
        private static bool ValidateMode(string menuPath, WorldGenerationDebugVisualizationMode mode)
        {
            WorldGenerationDebugVisualizationOverlay overlay = FindOverlay();
            bool isAvailable = EditorApplication.isPlaying && overlay != null;
            Menu.SetChecked(menuPath, isAvailable && overlay.CurrentMode == mode);
            return isAvailable;
        }

        /// <summary>
        /// メニューを開いたときに現在の診断対象レイヤーへチェックを付け、PlayMode外の項目を無効化します。
        /// </summary>
        /// <param name="menuPath">チェック状態を更新する完全なメニューパスです。</param>
        /// <param name="layer">この項目が表す固定ワールドレイヤーです。</param>
        /// <returns>PlayMode中にOverlayが見つかり、選択可能な場合はtrueです。</returns>
        private static bool ValidateLayer(string menuPath, WorldLayer layer)
        {
            WorldGenerationDebugVisualizationOverlay overlay = FindOverlay();
            bool isAvailable = EditorApplication.isPlaying && overlay != null;
            Menu.SetChecked(menuPath, isAvailable && overlay.CurrentLayer == layer);
            return isAvailable;
        }

        /// <summary>
        /// 現在読み込まれているSceneから、有効な開発者用可視化Overlayを1つ取得します。
        /// </summary>
        /// <returns>見つかったOverlayです。Scene未ロード時またはPlayMode外ではnullの場合があります。</returns>
        private static WorldGenerationDebugVisualizationOverlay FindOverlay()
        {
            return Object.FindFirstObjectByType<WorldGenerationDebugVisualizationOverlay>();
        }
    }
}
