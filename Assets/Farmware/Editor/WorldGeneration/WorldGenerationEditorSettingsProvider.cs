// このファイルは、広域マップビューアが実ゲームと同じWorldGenerationSettingsを取得するEditor専用入口です。
// PlayModeではECS Singleton、EditModeでは設定SubSceneのAuthoringを読み、独自の既定値へ静かにフォールバックしません。

using System;
using Farmware.WorldGeneration.Authoring;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// マップビューアが利用する生成設定、元のシード文字列、取得元説明をまとめます。
    /// SeedTextがnullの場合もSettings.WorldSeedにはPlayModeで実際に使用中の値が保持されています。
    /// </summary>
    public sealed class WorldGenerationEditorSettingsSnapshot
    {
        /// <summary>実ゲームのBaker入力またはPlayMode ECSから取得した地形生成設定です。</summary>
        public WorldGenerationSettings Settings { get; }

        /// <summary>Authoringの元文字列を特定できた場合のシードです。特定できないPlayModeではnullです。</summary>
        public string SeedText { get; }

        /// <summary>設定をどこから取得したか、EditorWindowへ表示する説明です。</summary>
        public string SourceDescription { get; }

        /// <summary>
        /// 取得済みの設定値を変更不能な参照情報としてまとめます。
        /// </summary>
        /// <param name="settings">実際の生成へ渡される値コピーです。</param>
        /// <param name="seedText">分かる場合の元シード文字列です。</param>
        /// <param name="sourceDescription">取得元を示す短い説明です。</param>
        internal WorldGenerationEditorSettingsSnapshot(
            WorldGenerationSettings settings,
            string seedText,
            string sourceDescription)
        {
            Settings = settings;
            SeedText = seedText;
            SourceDescription = sourceDescription;
        }
    }

    /// <summary>
    /// PlayMode ECSまたは設定SubSceneからWorldGenerationSettingsを取得します。
    /// 取得失敗時は生成を止め、別の設定で似た地図を表示して誤認させない方針です。
    /// </summary>
    public static class WorldGenerationEditorSettingsProvider
    {
        /// <summary>現在のSampleSceneが参照するワールド生成設定SubSceneのプロジェクト相対パスです。</summary>
        private const string SettingsSubScenePath =
            "Assets/Scenes/WorldGenerationSettingsSubScene.unity";

        /// <summary>
        /// 現在のゲームと一致する設定を取得します。
        /// PlayMode中はECS Singletonを優先し、それ以外はPreview SceneでAuthoringを安全に読みます。
        /// </summary>
        /// <param name="snapshot">成功時に、設定と取得元をまとめた値を返します。</param>
        /// <param name="errorMessage">失敗理由をEditorWindowへ表示できる日本語で返します。</param>
        /// <returns>実ゲームと共有できる設定を取得できた場合はtrueです。</returns>
        public static bool TryLoad(
            out WorldGenerationEditorSettingsSnapshot snapshot,
            out string errorMessage)
        {
            snapshot = null;
            errorMessage = null;

            bool authoringLoaded = TryLoadAuthoringSnapshot(
                out WorldGenerationSettings authoringSettings,
                out string authoringSeedText,
                out string authoringError);

            if (EditorApplication.isPlaying
                && TryLoadPlayModeSettings(out WorldGenerationSettings playModeSettings))
            {
                // セーブ由来などで実行中シードがAuthoringと異なる場合、文字列へ戻せないためhash値を正として保持します。
                string matchingSeedText = authoringLoaded
                    && WorldSeedUtility.FromString(authoringSeedText) == playModeSettings.WorldSeed
                        ? authoringSeedText
                        : null;
                snapshot = new WorldGenerationEditorSettingsSnapshot(
                    playModeSettings,
                    matchingSeedText,
                    "PlayMode ECS WorldGenerationSettings Singleton");
                return true;
            }

            if (authoringLoaded)
            {
                snapshot = new WorldGenerationEditorSettingsSnapshot(
                    authoringSettings,
                    authoringSeedText,
                    SettingsSubScenePath);
                return true;
            }

            errorMessage =
                "実ゲームと同じ生成設定を取得できませんでした。"
                + $" {authoringError}"
                + " Build Fallback Map Prototypeで設定SubSceneを再構築してください。";
            return false;
        }

        /// <summary>
        /// PlayModeのDefault Worldから、実際にChunkGenerationSystemが読むSingletonを取得します。
        /// </summary>
        /// <param name="settings">Singletonが1件だけ存在する場合の値コピーです。</param>
        /// <returns>有効なWorldとSingletonが存在する場合はtrueです。</returns>
        private static bool TryLoadPlayModeSettings(out WorldGenerationSettings settings)
        {
            settings = default;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return false;
            }

            using EntityQuery settingsQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldGenerationSettings>());
            if (settingsQuery.CalculateEntityCount() != 1)
            {
                return false;
            }

            settings = settingsQuery.GetSingleton<WorldGenerationSettings>();
            return true;
        }

        /// <summary>
        /// 設定SubSceneをHierarchyへ表示しないPreview Sceneとして開き、Bakerと共有する変換メソッドを呼びます。
        /// finallyで必ず閉じるため、開発者の現在Scene構成や未保存状態を変更しません。
        /// </summary>
        /// <param name="settings">Authoringから変換した地形生成設定です。</param>
        /// <param name="seedText">Authoringへ保存されている元シード文字列です。</param>
        /// <param name="errorMessage">読み込み失敗時の詳細です。</param>
        /// <returns>WorldGenerationAuthoringを1件取得できた場合はtrueです。</returns>
        private static bool TryLoadAuthoringSnapshot(
            out WorldGenerationSettings settings,
            out string seedText,
            out string errorMessage)
        {
            settings = default;
            seedText = null;
            errorMessage = null;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SettingsSubScenePath) == null)
            {
                errorMessage = $"設定Sceneが見つかりません: {SettingsSubScenePath}";
                return false;
            }

            Scene previewScene = default;
            try
            {
                previewScene = EditorSceneManager.OpenPreviewScene(SettingsSubScenePath);
                GameObject[] roots = previewScene.GetRootGameObjects();
                if (!TryFindSingleAuthoring(roots, out WorldGenerationAuthoring foundAuthoring))
                {
                    errorMessage =
                        foundAuthoring == null
                            ? $"{SettingsSubScenePath} にWorldGenerationAuthoringがありません。"
                            : $"{SettingsSubScenePath} にWorldGenerationAuthoringが複数あります。";
                    return false;
                }

                seedText = foundAuthoring.SeedText;
                settings = foundAuthoring.CreateWorldGenerationSettingsSnapshot();
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"設定Sceneの読み込み中に失敗しました: {exception.Message}";
                return false;
            }
            finally
            {
                if (previewScene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(previewScene);
                }
            }
        }

        /// <summary>
        /// Preview Sceneの全Rootと全子孫を列挙し、WorldGenerationAuthoringが正確に1件だけ存在するか確認します。
        /// 同じRoot配下に複数ある場合もBakerは複数Entityを生成するため、最初の1件だけを採用せず設定エラーにします。
        /// </summary>
        /// <param name="roots">Preview Sceneから取得したRoot GameObject一覧です。</param>
        /// <param name="foundAuthoring">1件ならそのComponent、0件ならnull、複数なら2件目を返します。</param>
        /// <returns>全階層を通してWorldGenerationAuthoringが正確に1件の場合はtrueです。</returns>
        private static bool TryFindSingleAuthoring(
            GameObject[] roots,
            out WorldGenerationAuthoring foundAuthoring)
        {
            foundAuthoring = null;

            // Rootごとに全子孫を列挙し、兄弟GameObjectに置かれた重複も見落とさないようにします。
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                WorldGenerationAuthoring[] candidates =
                    roots[rootIndex].GetComponentsInChildren<WorldGenerationAuthoring>(true);
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    if (foundAuthoring != null)
                    {
                        // 呼び出し側が「0件」と「複数」を区別できるよう、複数時は2件目を保持して返します。
                        foundAuthoring = candidates[candidateIndex];
                        return false;
                    }

                    foundAuthoring = candidates[candidateIndex];
                }
            }

            return foundAuthoring != null;
        }
    }
}
