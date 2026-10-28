// このファイルは、検証専用のScene・Shader・Material・生成設定が製品Build Sceneへ混入しないことを検査します。
// また、立体地形表示へ置換した後に比較・検品専用インフラを取り残さないための完了ゲートを提供します。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Farmware.WorldGeneration.Presentation;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 製品Build Sceneと制作入力の境界、および比較・検品専用インフラ削除の完了条件を保証します。
    /// どちらも通常のEditMode実行へ含め、後続変更で削除済み経路が復活した時点で検出します。
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
            "Assets/Farmware/Art/WorldGeneration/Liquids/Source/",
            "Assets/Farmware/Art/WorldGeneration/Terrain/Settings/TerrainTileArtGenerationSettings.asset",
        };

        // 製品表示の置換時に削除した、比較・検品専用のアセットと実装です。
        private static readonly string[] RemovedComparisonAssetPaths =
        {
            "Assets/Farmware/Art/WorldGeneration/Generated/Inspection",
            "Assets/Farmware/Shaders/Development",
            "Assets/Farmware/Art/WorldGeneration/Terrain/Highlights",
            "Assets/Farmware/Art/WorldGeneration/Fallback/BorealConiferForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/HotDesert.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TropicalSavannaWoodland.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/CactusDesertDryPlateau.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/VolcanicGeothermal.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/Steppe.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/DryShrublandWoodland.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/AlpineRockland.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/VolcanicGeothermal.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/HotDesert.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/BorealConiferForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TundraPolarBarrens.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/WarmTemperateEvergreenForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/AlpineRockland.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TundraPolarBarrens.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/SwampForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/SaltWater.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/Steppe.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/Magma.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/FreshWater.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TropicalSeasonalForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/RockWall.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/SwampForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/SubtropicalOceanicForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/FreshwaterMarshFloodplain.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/SubtropicalOceanicForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TemperateDeciduousForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TropicalSeasonalForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TemperateMontaneBambooForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/FreshWater.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/FreshwaterMarshFloodplain.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TropicalSavannaWoodland.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/SaltWater.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TemperateGrassland.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TemperateGrassland.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TropicalRainforest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/WarmTemperateEvergreenForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/CactusDesertDryPlateau.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TemperateMontaneBambooForest.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/Magma.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/RockWall.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/DryShrublandWoodland.png",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TemperateDeciduousForest.asset",
            "Assets/Farmware/Art/WorldGeneration/Fallback/TropicalRainforest.png",
            "Assets/Farmware/Art/WorldGeneration/Terrain/Base",
            "Assets/Farmware/Art/WorldGeneration/Atlases/GroundTerrainAtlas.spriteatlasv2",
            "Assets/Farmware/Art/WorldGeneration/Atlases/RockWallAtlas.spriteatlasv2",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/WorldGenerationSpriteAtlasTests.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/WorldGenerationFallbackArtTests.cs",
            "Assets/Farmware/Art/WorldGeneration/Terrain/Highlights/terrain_boundary_highlights.png",
            "Assets/Farmware/Art/WorldGeneration/Atlases/BoundaryHighlightAtlas.spriteatlasv2",
            "Assets/Scenes/Development/TerrainBoundaryHighlightInspection.unity",
            "Assets/Scenes/Development/TerrainTileArtInspection.unity",
            "Assets/Scenes/Development/TerrainWallEdgeCueComparison.unity",
            "Assets/Scenes/Development/TerrainWallShadowComparison.unity",
            "Assets/Scenes/Development/TerrainWallSurfaceDarkenComparison.unity",
            "Assets/Scenes/Development/TerrainWaterReflectionComparison.unity",
            "Assets/Farmware/Presentation/WorldGeneration/TerrainBoundaryHighlightUtility.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainBoundaryHighlightArtGenerator.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainBoundaryHighlightInspectionMapBuilder.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainTileArtInspectionMapBuilder.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainWallShadowComparisonMapBuilder.cs",
            "Assets/Farmware/Editor/WorldGeneration/TerrainWaterReflectionComparisonMapBuilder.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainBoundaryHighlightArtGeneratorTests.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainBoundaryHighlightUtilityTests.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainTileArtInspectionMapBuilderTests.cs",
            "Assets/Farmware/Tests/EditMode/WorldGeneration/TerrainWaterReflectionComparisonMapBuilderTests.cs",
        };

        // 型名、Scene名、SerializedProperty名を検出し、ファイル名だけ削除して利用側を残す不完全な置換を防ぎます。
        private static readonly string[] RemovedComparisonTextTokens =
        {
            "TerrainBoundaryHighlight",
            "boundaryHighlight",
            "WorldGenerationSpriteAtlasBuilder",
            "ConfigureChunkLocalPresentation",
            "useChunkLocalTilemaps",
            "staleOriginClearQueue",
            "generatedFreshWaterFallbackTile",
            "biomeTiles",
            "b15364dc321034b7e85a17e9c3839fa3",
            "aad0f800000a745aebad059cd3911e85",
            "45d52e52c3df945bf9c4536025bcb0b7",
            "fe67e7eb45bb5441ab0410d6c74d2acf",
            "43403acd19280403a976b80de685a755",
            "8b15c7b2841f347679dc4c2774c88ba0",
            "277e08e4d648142ef9b43eff5e00947c",
            "7d3c30d51d0554ab3bf04f945ffe61c5",
            "ea95048ee88fb4200904927a0b4d3a86",
            "2252b8c6d7f994734a63fd9a78b3ad28",
            "0c8767b14ec794733bf2093f1c6b8c2f",
            "4d1f6bce2bd2c4b99a6022bf3628e95c",
            "fa8ea60dadde444d59d796b4b1fbacfd",
            "82c7262229f1d46abb1e4b13eaf5fc15",
            "c1692df47c9d64525b5b52f8c73b22d4",
            "a3a62d82b462e4d5aa94b10c17674f63",
            "6a015ba4629a2470990ef874ec171e33",
            "9e98dc532e42c441b9ac8472987c54eb",
            "1efd85541c8714c359c7e09eea3ffb12",
            "b63c2cac675524c3bb542ae99067dea7",
            "06507ec0f78364046ab6c0ec235d8bcf",
            "c589ee80b2e47455a869df72715ead2b",
            "e7776a557f66e409c8e1334d6788249c",
            "a912e8c5bef5d4070b72994a31a16cdf",
            "1de8aa51176c3427d9f2a01e005da558",
            "31cf9616f88544d7fa8060e277923959",
            "5f2a80b90c1dc426fa075dc2db6aeb0a",
            "4747c795dbbf14759bfbe9a6a695bf3f",
            "2a1f5134346f84db3bc6867871add4dd",
            "c6c7e124e5c874c5fb6781e8abd79d6a",
            "e6ce56f9d5d41470791d9d3213269646",
            "330105c42272847018015869c7a171ec",
            "6c2514abed1db4e2c8adc18678ceff03",
            "eb7d22dae0b7d4258b6c7a6f45d1f41e",
            "0e59fa06334954d6fa30212741f81ac6",
            "c6004b040940040288f8c7a4bb3e2433",
            "c194a438082ee4b59921fcdc75b4f7c2",
            "12dd08f740959497ebadd41cba9e7e20",
            "6b6fed4f78d85456f837d4c24b578d5b",
            "78e3fc69c859f47888538e72c61a611b",
            "fb165b34e2239482087259ee502aeb0f",
            "bb92e2928116d46e2bd57efe0ed016a8",
            "568d58706b01d4b688920be8021c184c",
            "451ed00406dbf4a97b10f00515ad4024",
            "112de089b44174ccda2084a249b08cd8",
            "a798b005219cf41cc9f3d8a512151dae",
            "412ada73616542ef80975048eec27f51",
            "5f5e96297bc284ba3a03345f2f7fd030",
            "b642a3ae94175467a93817fae33bf345",
            "7ee287944b9ff4c5087fa64923bb4251",
            "8742ae7a60f7f434f894678fe1add07b",
            "cfaf69eb81d29435695f221a8ae3b907",
            "bc89c6c7cda5a41c1b5a8574ad6739d3",
            "eba7c174fb8d945d48649edc499e98d0",
            "6b140b9d48f8a4382ae682f665d183c7",
            "398db16b0f8724fd19c1fd41a4bf960a",
            "b5726550b27e04f128ee5901a191c465",
            "e214cd92f2e6543ed84db1a3e17336c6",

            "TerrainTileArtInspection",
            "TerrainWallEdgeCueComparison",
            "TerrainWallShadowComparison",
            "TerrainWallSurfaceDarkenComparison",
            "TerrainWaterReflectionComparison",
            "Generated/Inspection",
            "Shaders/Development",
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
        /// 立体地形表示への置換後に、比較・検品専用のScene、生成物、コード、Serialized参照、専用テストがすべて消えたことを検査します。
        /// </summary>
        [Test]
        public void ComparisonInspectionRemovalGate_RemovesAllInfrastructure()
        {
            List<string> remainingItems = new List<string>();

            for (int pathIndex = 0;
                 pathIndex < RemovedComparisonAssetPaths.Length;
                 pathIndex++)
            {
                string assetPath = RemovedComparisonAssetPaths[pathIndex];
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    remainingItems.Add($"比較・検品専用アセットまたはコード: {assetPath}");
                }
            }

            CollectRemovedTextReferences(remainingItems);

            Assert.That(
                remainingItems,
                Is.Empty,
                BuildFailureMessage(
                    "製品表示への置換後も、比較・検品専用インフラが残っています。",
                    remainingItems));
        }

        /// <summary>製品カタログ未設定のPresenterは旧素材を描かず、設定不足を1回だけ通知することを保証します。</summary>
        [Test]
        public void Presenter_MissingProductCatalogsRejectsRenderingWithoutFallback()
        {
            var root = new GameObject("Missing Terrain Catalog Test");
            try
            {
                var presenter = root.AddComponent<ChunkTilemapPresenter>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var update = typeof(ChunkTilemapPresenter).GetMethod("Update", flags);
                LogAssert.Expect(LogType.Error,
                    "地形材質の製品表示を開始できません。41材質と3液体の製品表示カタログを両方設定してください。");
                update.Invoke(presenter, null);
                // 毎フレーム同じエラーを増やさず、描画キャッシュを作らないことも合わせて確認します。
                update.Invoke(presenter, null);
                var displayed = (System.Collections.IDictionary)typeof(ChunkTilemapPresenter)
                    .GetField("chunkLocalPresentations", flags).GetValue(presenter);
                Assert.That(displayed.Count, Is.Zero);
                LogAssert.NoUnexpectedReceived();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
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
        private static void CollectRemovedTextReferences(List<string> remainingItems)
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
                     tokenIndex < RemovedComparisonTextTokens.Length;
                     tokenIndex++)
                {
                    string token = RemovedComparisonTextTokens[tokenIndex];
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
