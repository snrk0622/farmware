// このファイルは、PlayMode中のノイズ・バイオーム・土壌・水域・マグマ・岩壁・資源可視化をUnity Editorメニューから切り替えます。
// Editor Assemblyだけに属し、プレイヤー用入力や製品版ビルドへ開発者専用の操作UIを追加しません。

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

        /// <summary>連続気温表示へ切り替えるメニューパスです。</summary>
        private const string TemperatureMenuPath = "Farmware/World Generation/Debug Visualization/Temperature";

        /// <summary>連続湿度表示へ切り替えるメニューパスです。</summary>
        private const string MoistureMenuPath = "Farmware/World Generation/Debug Visualization/Moisture";

        /// <summary>連続Weirdness表示へ切り替えるメニューパスです。</summary>
        private const string WeirdnessMenuPath = "Farmware/World Generation/Debug Visualization/Weirdness";

        /// <summary>淡水・海水表示へ切り替えるメニューパスです。</summary>
        private const string WaterSalinityMenuPath =
            "Farmware/World Generation/Debug Visualization/Water Salinity";

        /// <summary>川・池・海表示へ切り替えるメニューパスです。</summary>
        private const string WaterBodyMenuPath =
            "Farmware/World Generation/Debug Visualization/Water Body";

        /// <summary>バイオーム移行タイル表示へ切り替えるメニューパスです。</summary>
        private const string TransitionMenuPath =
            "Farmware/World Generation/Debug Visualization/Biome Transition";

        /// <summary>岩壁の最終形状と内部影濃度表示へ切り替えるメニューパスです。</summary>
        private const string RockWallMenuPath =
            "Farmware/World Generation/Debug Visualization/Rock Wall";

        /// <summary>マグマ本体と淡水抑制用の火山影響域表示へ切り替えるメニューパスです。</summary>
        private const string MagmaMenuPath =
            "Farmware/World Generation/Debug Visualization/Magma";

        /// <summary>最低間隔と競合解決後の基礎資源表示へ切り替えるメニューパスです。</summary>
        private const string ResourcesMenuPath =
            "Farmware/World Generation/Debug Visualization/Resources";

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

        /// <summary>PlayMode中の連続気温値を青から赤のグラデーションで表示します。</summary>
        [MenuItem(TemperatureMenuPath, false, 204)]
        private static void SelectTemperature()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Temperature);
        }

        /// <summary>PlayMode中の連続湿度値を茶から水色のグラデーションで表示します。</summary>
        [MenuItem(MoistureMenuPath, false, 205)]
        private static void SelectMoisture()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Moisture);
        }

        /// <summary>PlayMode中の連続Weirdness値を紫から黄のグラデーションで表示します。</summary>
        [MenuItem(WeirdnessMenuPath, false, 206)]
        private static void SelectWeirdness()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Weirdness);
        }

        /// <summary>PlayMode中の水域を陸地・淡水・海水の固定色で表示します。</summary>
        [MenuItem(WaterSalinityMenuPath, false, 207)]
        private static void SelectWaterSalinity()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.WaterSalinity);
        }

        /// <summary>PlayMode中の水域を陸地・川・池・海の固定色で表示します。</summary>
        [MenuItem(WaterBodyMenuPath, false, 208)]
        private static void SelectWaterBody()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.WaterBody);
        }

        /// <summary>PlayMode中に論理Biomeと異なる陸上Groundだけを強調表示します。</summary>
        [MenuItem(TransitionMenuPath, false, 209)]
        private static void SelectTransition()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.BiomeTransition);
        }

        /// <summary>PlayMode中の岩壁を非岩壁・露出面・内部影の濃淡で表示します。</summary>
        [MenuItem(RockWallMenuPath, false, 210)]
        private static void SelectRockWall()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.RockWall);
        }

        /// <summary>PlayMode中のマグマ本体と周囲の淡水抑制域を固定色で表示します。</summary>
        [MenuItem(MagmaMenuPath, false, 211)]
        private static void SelectMagma()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Magma);
        }

        /// <summary>PlayMode中の最終資源配置を木・石・砂の固定色で表示します。</summary>
        [MenuItem(ResourcesMenuPath, false, 212)]
        private static void SelectResources()
        {
            SelectMode(WorldGenerationDebugVisualizationMode.Resources);
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

        /// <summary>Temperature項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(TemperatureMenuPath, true)]
        private static bool ValidateTemperature()
        {
            return ValidateMode(TemperatureMenuPath, WorldGenerationDebugVisualizationMode.Temperature);
        }

        /// <summary>Moisture項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(MoistureMenuPath, true)]
        private static bool ValidateMoisture()
        {
            return ValidateMode(MoistureMenuPath, WorldGenerationDebugVisualizationMode.Moisture);
        }

        /// <summary>Weirdness項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(WeirdnessMenuPath, true)]
        private static bool ValidateWeirdness()
        {
            return ValidateMode(WeirdnessMenuPath, WorldGenerationDebugVisualizationMode.Weirdness);
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

        /// <summary>Biome Transition項目の有効状態とチェック表示を更新します。</summary>
        /// <returns>PlayMode中に対象Overlayが存在する場合はtrueです。</returns>
        [MenuItem(TransitionMenuPath, true)]
        private static bool ValidateTransition()
        {
            return ValidateMode(
                TransitionMenuPath,
                WorldGenerationDebugVisualizationMode.BiomeTransition);
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
        /// 現在読み込まれているSceneから、有効な開発者用可視化Overlayを1つ取得します。
        /// </summary>
        /// <returns>見つかったOverlayです。Scene未ロード時またはPlayMode外ではnullの場合があります。</returns>
        private static WorldGenerationDebugVisualizationOverlay FindOverlay()
        {
            return Object.FindFirstObjectByType<WorldGenerationDebugVisualizationOverlay>();
        }
    }
}
