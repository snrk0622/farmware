// このファイルは、検証専用のScene・Shader・Material・生成設定が製品Build Sceneへ混入しないことを検査します。
// また、立体地形表示へ置換する工程で旧境界ハイライト一式を取り残さないための明示的な完了ゲートを提供します。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 製品Build Sceneと検証専用アセットの境界、および旧境界ハイライト削除の完了条件を保証します。
    /// 通常のEditMode実行ではBuild Scene依存関係を検査し、C6-D完了時だけExplicitゲートを明示実行します。
    /// </summary>
    public sealed class TerrainProductIsolationTests
    {
        // ここに置くアセットは検証・制作入力専用です。製品Sceneから直接・間接に参照するとPlayerへ同梱され得るため拒否します。
        private static readonly string[] ProhibitedBuildDependencyPathsOrPrefixes =
        {
            "Assets/Scenes/Development/",
            "Assets/Farmware/Editor/",
            "Assets/Farmware/Tests/",
            "Assets/Farmware/Shaders/Development/",
            "Assets/Farmware/Art/WorldGeneration/Generated/Inspection/",
            "Assets/Farmware/Art/WorldGeneration/Source/",
            "Assets/Farmware/Art/WorldGeneration/Terrain/Settings/TerrainTileArtGenerationSettings.asset",
        };

        // C6-Dの製品表示置換で削除する、旧14形状境界ハイライト固有のアセットと実装です。
        private static readonly string[] LegacyBoundaryHighlightAssetPaths =
        {
            "Assets/Farmware/Art/WorldGeneration/Terrain/Highlights",
            "Assets/Farmware/Art/WorldGeneration/Terrain/Highlights/terrain_boundary_highlights.png",
            "Assets/Farmware/Art/WorldGeneration/Atlases/BoundaryHighlightAtlas.spriteatlasv2",
            "Assets/Scenes/Development/TerrainBoundaryHighlightInspection.unity",
            "Assets/Farmware/Presentation/WorldGeneration/TerrainBoundaryHighlightUtility.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainBoundaryHighlightArtGenerator.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainBoundaryHighlightInspectionMapBuilder.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainBoundaryHighlightArtGeneratorTests.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainBoundaryHighlightUtilityTests.cs",
        };

        // 型名とSerializedProperty名を検出し、ファイル名だけ削除して利用側を残す不完全な置換を防ぎます。
        private static readonly string[] LegacyBoundaryHighlightTextTokens =
        {
            "TerrainBoundaryHighlight",
            "boundaryHighlight",
            "2ae884cf4c0924fabbec0498ef250e81",
            "2ed07e917aa5f446b94162eae27291b4",
        };

        // UnityのText Serialization対象とC#・Shaderだけを読み、PNGなどのバイナリを文字列として誤解析しないようにします。
        private static readonly HashSet<string> ScannedTextExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".asset",
                ".controller",
                ".cs",
                ".mat",
                ".meta",
                ".overrideController",
                ".prefab",
                ".shader",
                ".spriteatlas",
                ".spriteatlasv2",
                ".unity",
            };

        // このテスト自身には検出語と旧GUIDを記録する必要があるため、全文検索から除外します。
        private const string ThisTestAssetPath =
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainProductIsolationTests.cs";

        /// <summary>
        /// 有効な全Build Sceneの再帰依存を調べ、検証Scene・Shader・Material・制作入力が製品へ混入していないことを保証します。
        /// </summary>
        [Test]
        public void EnabledBuildScenes_DoNotDependOnDevelopmentOnlyAssets()
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            List<string> prohibitedDependencies = new List<string>();
            int enabledSceneCount = 0;

            for (int sceneIndex = 0; sceneIndex < buildScenes.Length; sceneIndex++)
            {
                EditorBuildSettingsScene buildScene = buildScenes[sceneIndex];
                if (!buildScene.enabled)
                {
                    continue;
                }

                enabledSceneCount++;
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(buildScene.path),
                    Is.Not.Null,
                    $"Build Settingsの有効Sceneを読み込めません: {buildScene.path}");

                // 再帰依存を使うことで、SceneからPrefabやMaterialを経由した間接的な混入も同じ検査で捕捉します。
                string[] dependencies = AssetDatabase.GetDependencies(
                    buildScene.path,
                    recursive: true);
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.Length;
                     dependencyIndex++)
                {
                    string dependencyPath = dependencies[dependencyIndex]
                        .Replace('\\', '/');
                    if (IsProhibitedBuildDependency(dependencyPath))
                    {
                        prohibitedDependencies.Add(
                            $"{buildScene.path} -> {dependencyPath}");
                    }
                }
            }

            Assert.That(
                enabledSceneCount,
                Is.GreaterThan(0),
                "製品Build Sceneが1件も有効ではないため、依存関係を検査できません。");
            Assert.That(
                prohibitedDependencies,
                Is.Empty,
                BuildFailureMessage(
                    "製品Build Sceneが検証・制作専用アセットを参照しています。",
                    prohibitedDependencies));
        }

        /// <summary>
        /// C6-D完了時に旧14形状境界ハイライトの素材、生成コード、選択処理、Scene参照、専用テストがすべて消えたことを検査します。
        /// 現在は旧製品表示が残るためExplicitとし、C6-Dの置換後にこのテストを明示実行して成功させます。
        /// </summary>
        [Test]
        [Explicit("C6-Dで新しい立体地形表示へ置換した後に実行する削除完了ゲートです。")]
        public void C6DCompletionGate_RemovesLegacyBoundaryHighlightImplementation()
        {
            List<string> remainingItems = new List<string>();

            for (int pathIndex = 0;
                 pathIndex < LegacyBoundaryHighlightAssetPaths.Length;
                 pathIndex++)
            {
                string assetPath = LegacyBoundaryHighlightAssetPaths[pathIndex];
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    remainingItems.Add($"旧アセットまたはコード: {assetPath}");
                }
            }

            CollectLegacyTextReferences(remainingItems);

            Assert.That(
                remainingItems,
                Is.Empty,
                BuildFailureMessage(
                    "旧14形状境界ハイライトが残っています。C6-Dを完了扱いにできません。",
                    remainingItems));
        }

        /// <summary>
        /// 指定パスが、Build Sceneからの参照を禁止した完全一致パスまたはフォルダー配下かを返します。
        /// </summary>
        /// <param name="dependencyPath">AssetDatabaseが返したAssets始まりの依存パスです。</param>
        /// <returns>検証・制作専用パスならtrue、それ以外ならfalseです。</returns>
        private static bool IsProhibitedBuildDependency(string dependencyPath)
        {
            for (int ruleIndex = 0;
                 ruleIndex < ProhibitedBuildDependencyPathsOrPrefixes.Length;
                 ruleIndex++)
            {
                string rule = ProhibitedBuildDependencyPathsOrPrefixes[ruleIndex];
                bool isFolderRule = rule.EndsWith("/", StringComparison.Ordinal);
                if (isFolderRule
                    && dependencyPath.StartsWith(rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!isFolderRule
                    && string.Equals(
                        dependencyPath,
                        rule,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Assets配下の文字列化可能なファイルを走査し、旧型名、SerializedProperty名、旧素材GUIDへの参照を収集します。
        /// </summary>
        /// <param name="remainingItems">見つかった残存箇所を追記する一覧です。</param>
        private static void CollectLegacyTextReferences(List<string> remainingItems)
        {
            string[] fullPaths = Directory.GetFiles(
                Application.dataPath,
                "*",
                SearchOption.AllDirectories);
            for (int fileIndex = 0; fileIndex < fullPaths.Length; fileIndex++)
            {
                string fullPath = fullPaths[fileIndex];
                string extension = Path.GetExtension(fullPath);
                if (!ScannedTextExtensions.Contains(extension))
                {
                    continue;
                }

                string assetPath = ConvertToAssetPath(fullPath);
                if (string.Equals(
                        assetPath,
                        ThisTestAssetPath,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        assetPath,
                        ThisTestAssetPath + ".meta",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string contents;
                try
                {
                    contents = File.ReadAllText(fullPath);
                }
                catch (Exception exception)
                {
                    // 読み飛ばすと旧参照を見逃すため、読取失敗自体を完了ゲートの失敗理由として記録します。
                    remainingItems.Add(
                        $"全文検索に失敗: {assetPath} ({exception.GetType().Name})");
                    continue;
                }

                for (int tokenIndex = 0;
                     tokenIndex < LegacyBoundaryHighlightTextTokens.Length;
                     tokenIndex++)
                {
                    string token = LegacyBoundaryHighlightTextTokens[tokenIndex];
                    if (contents.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    remainingItems.Add($"旧参照: {assetPath} ({token})");
                    break;
                }
            }
        }

        /// <summary>
        /// OS上のAssets絶対パスを、AssetDatabaseで扱えるAssets始まりの相対パスへ変換します。
        /// </summary>
        /// <param name="fullPath">Application.dataPath配下にあるファイルの絶対パスです。</param>
        /// <returns>区切り文字をスラッシュへ統一したUnity Assetパスです。</returns>
        private static string ConvertToAssetPath(string fullPath)
        {
            string normalizedFullPath = fullPath.Replace('\\', '/');
            string normalizedAssetsPath = Application.dataPath.Replace('\\', '/');
            return "Assets" + normalizedFullPath.Substring(normalizedAssetsPath.Length);
        }

        /// <summary>
        /// 失敗理由と全残存項目を、Test Runnerで一度に確認できる複数行メッセージへ整形します。
        /// </summary>
        /// <param name="heading">一覧の先頭へ表示する失敗理由です。</param>
        /// <param name="items">Scene依存または旧実装の残存箇所一覧です。</param>
        /// <returns>見出しと箇条書きを連結した文字列です。</returns>
        private static string BuildFailureMessage(
            string heading,
            IReadOnlyList<string> items)
        {
            StringBuilder builder = new StringBuilder(heading);
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                builder.AppendLine();
                builder.Append("- ");
                builder.Append(items[itemIndex]);
            }

            return builder.ToString();
        }
    }
}
