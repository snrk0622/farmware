// [UNITY-SKILL:SPRITEATLAS]
// [TYPE:PREBUILD]
// [VERSION:2.0.3]
// このファイルは、本番用バイオーム土地・岩壁と境界ハイライトのSpriteをSprite Atlas V2へまとめ、Tilemapの描画を安定させます。
// WorldGenerationSceneBootstrapからEditor確認用に呼ばれ、Playerビルド前にはIPreprocessBuildWithReportから同じ設定を再適用します。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Playerビルド開始前に土地・岩壁Atlasを再生成し、設定漏れや複数ページ化をビルドへ持ち込まないためのEditor専用処理です。
    /// </summary>
    public sealed class WorldGenerationSpriteAtlasPrebuildGenerator : IPreprocessBuildWithReport
    {
        /// <summary>
        /// 他の前処理より特別に先行・後続させる要件がないため、Unity標準の順序0を使用します。
        /// </summary>
        public int callbackOrder => 0;

        /// <summary>
        /// Playerビルド直前にAtlasを現在の元Spriteから再構築し、対象BuildTarget向けのパック結果を検証します。
        /// </summary>
        /// <param name="report">Unityが開始しようとしているビルドと対象Platformの情報です。</param>
        /// <remarks>AtlasアセットとSprite Packer設定を更新し、不正な場合は例外でビルドを中止します。</remarks>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            WorldGenerationSpriteAtlasBuilder.GenerateOrUpdateAtlases(report.summary.platform);
        }
    }

    /// <summary>
    /// 本番用の一体型バイオーム土地・岩壁と境界ハイライトのSprite Atlas V2を冪等に生成します。
    /// </summary>
    public static class WorldGenerationSpriteAtlasBuilder
    {
        /// <summary>生成するSprite AtlasをまとめるUnityプロジェクト相対フォルダです。</summary>
        public const string AtlasAssetFolder = "Assets/Farmware/Art/WorldGeneration/Atlases";

        /// <summary>7種類のバイオーム土地Spriteを同じTextureページへまとめるAtlasのアセットパスです。</summary>
        public const string GroundTerrainAtlasPath =
            AtlasAssetFolder + "/GroundTerrainAtlas.spriteatlasv2";

        /// <summary>16×28岩壁Spriteを土地とは独立して管理するAtlasのアセットパスです。</summary>
        public const string RockWallAtlasPath =
            AtlasAssetFolder + "/RockWallAtlas.spriteatlasv2";

        /// <summary>14基本形の境界ハイライトを独立管理するAtlasのアセットパスです。</summary>
        public const string BoundaryHighlightAtlasPath =
            AtlasAssetFolder + "/BoundaryHighlightAtlas.spriteatlasv2";

        /// <summary>7種類の本番用バイオーム土地Spriteを横一列に収録した元Textureパスです。</summary>
        public const string ProductionBiomeTerrainTexturePath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Base/terrain_biomes_base.png";

        /// <summary>上面16pxと側面12pxを一体化した本番用岩壁Textureパスです。</summary>
        public const string ProductionRockWallTexturePath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Base/terrain_rock_wall_01.png";

        /// <summary>14基本形を16pxずつ横一列に収録した境界ハイライトTextureパスです。</summary>
        public const string BoundaryHighlightTexturePath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Highlights/terrain_boundary_highlights.png";

        /// <summary>本番用土地シートに必ず存在するSprite数です。</summary>
        public const int ExpectedBiomeSpriteCount = 7;

        /// <summary>境界ハイライトシートに必ず存在するSprite数です。</summary>
        public const int ExpectedBoundaryHighlightSpriteCount = 14;

        /// <summary>Atlasの1ページへ許可する最大辺長です。単位はピクセルです。</summary>
        public const int AtlasMaxTextureSize = 512;

        /// <summary>隣接Spriteの色がにじまないように確保する余白です。単位はピクセルです。</summary>
        public const int AtlasPaddingInPixels = 4;

        /// <summary>プレイヤー向け表示に使う本番バイオーム土地Textureパスです。</summary>
        private static readonly string[] GroundTerrainSourceTexturePaths =
        {
            ProductionBiomeTerrainTexturePath
        };

        /// <summary>土地シートを左から右へSliceしたときの、Biome IDと対応する安定したSprite名です。</summary>
        private static readonly string[] ExpectedBiomeSpriteNames =
        {
            "terrain_grassland",
            "terrain_forest",
            "terrain_desert",
            "terrain_rockland",
            "terrain_wetland",
            "terrain_snowfield",
            "terrain_volcanic"
        };

        /// <summary>岩壁Atlasへ収録する元Textureパスです。</summary>
        private static readonly string[] RockWallSourceTexturePaths =
        {
            ProductionRockWallTexturePath
        };

        /// <summary>境界ハイライトAtlasへ収録する元Textureパスです。</summary>
        private static readonly string[] BoundaryHighlightSourceTexturePaths =
        {
            BoundaryHighlightTexturePath
        };

        /// <summary>境界ハイライトシートを左から右へSliceしたときの安定したSprite名です。</summary>
        private static readonly string[] ExpectedBoundaryHighlightSpriteNames =
        {
            "terrain_boundary_highlight_01",
            "terrain_boundary_highlight_02",
            "terrain_boundary_highlight_03",
            "terrain_boundary_highlight_04",
            "terrain_boundary_highlight_05",
            "terrain_boundary_highlight_06",
            "terrain_boundary_highlight_07",
            "terrain_boundary_highlight_08",
            "terrain_boundary_highlight_09",
            "terrain_boundary_highlight_10",
            "terrain_boundary_highlight_11",
            "terrain_boundary_highlight_12",
            "terrain_boundary_highlight_13",
            "terrain_boundary_highlight_14"
        };

        /// <summary>
        /// 土地Atlasへ収録する元Textureパスを、呼び出し元が書き換えられないコピーとして返します。
        /// </summary>
        /// <returns>Unityプロジェクト相対パスを格納した新しい配列です。</returns>
        public static string[] GetGroundTerrainSourceTexturePaths()
        {
            return (string[])GroundTerrainSourceTexturePaths.Clone();
        }

        /// <summary>
        /// 本番用土地シートへ必要なSprite名を、呼び出し元が書き換えられないコピーとして返します。
        /// </summary>
        /// <returns>Biome ID順の7個のSprite名です。</returns>
        public static string[] GetExpectedBiomeSpriteNames()
        {
            return (string[])ExpectedBiomeSpriteNames.Clone();
        }

        /// <summary>
        /// 岩壁Atlasへ収録する元Textureパスを、呼び出し元が書き換えられないコピーとして返します。
        /// </summary>
        /// <returns>Unityプロジェクト相対パスを格納した新しい配列です。</returns>
        public static string[] GetRockWallSourceTexturePaths()
        {
            return (string[])RockWallSourceTexturePaths.Clone();
        }

        /// <summary>
        /// 境界ハイライトAtlasへ収録する元Textureパスを、呼び出し元が書き換えられないコピーとして返します。
        /// </summary>
        /// <returns>Unityプロジェクト相対パスを格納した新しい配列です。</returns>
        public static string[] GetBoundaryHighlightSourceTexturePaths()
        {
            return (string[])BoundaryHighlightSourceTexturePaths.Clone();
        }

        /// <summary>
        /// 境界ハイライトシートへ必要なSprite名を、呼び出し元が書き換えられないコピーとして返します。
        /// </summary>
        /// <returns>画像の左から右への14個のSprite名です。</returns>
        public static string[] GetExpectedBoundaryHighlightSpriteNames()
        {
            return (string[])ExpectedBoundaryHighlightSpriteNames.Clone();
        }

        /// <summary>
        /// 現在選択中のBuildTarget向けに土地・岩壁Atlasを生成し、EditorのPlayMode確認へ即時反映します。
        /// </summary>
        /// <remarks>Atlasアセット、Importer設定、Sprite Packer設定を更新し、パック結果も再生成します。</remarks>
        public static void GenerateOrUpdateAtlases()
        {
            GenerateOrUpdateAtlases(EditorUserBuildSettings.activeBuildTarget);
        }

        /// <summary>
        /// 指定BuildTarget向けに土地・岩壁Atlasを生成し、全土地Spriteが1枚の共通Textureへパックされたことを検証します。
        /// </summary>
        /// <param name="buildTarget">Atlasをパックして検証するUnityのBuildTargetです。</param>
        /// <remarks>Atlasアセット、Importer設定、Sprite Packer設定を更新します。</remarks>
        public static void GenerateOrUpdateAtlases(BuildTarget buildTarget)
        {
            EnsureSpritePackerV2Enabled();
            EnsureFolderHierarchy(AtlasAssetFolder);
            ValidateProductionBiomeSpriteSheet();
            ValidateProductionRockWallSprite();
            ValidateBoundaryHighlightSpriteSheet();

            GenerateOrUpdateAtlas(GroundTerrainAtlasPath, GroundTerrainSourceTexturePaths);
            GenerateOrUpdateAtlas(RockWallAtlasPath, RockWallSourceTexturePaths);
            GenerateOrUpdateAtlas(
                BoundaryHighlightAtlasPath,
                BoundaryHighlightSourceTexturePaths);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Tilemapの行ソートは同じTextureページ内でなければ維持できません。
            // ビルド時とEditor確認時の両方で実際にパックし、土地が複数ページへ分割された時点で検出します。
            PackAndValidateAtlases(buildTarget);

            Debug.Log(
                $"Farmwareの地形Sprite Atlasを更新しました。"
                + $" Target={buildTarget}, Ground={GroundTerrainAtlasPath}, "
                + $"RockWall={RockWallAtlasPath}, Boundary={BoundaryHighlightAtlasPath}");
        }

        /// <summary>
        /// Sprite Atlas V2を有効化し、書き込み後の値も読み返して無効状態の見逃しを防ぎます。
        /// </summary>
        private static void EnsureSpritePackerV2Enabled()
        {
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            if (EditorSettings.spritePackerMode != SpritePackerMode.SpriteAtlasV2)
            {
                throw new InvalidOperationException(
                    "Sprite Packer ModeをSprite Atlas V2へ設定できませんでした。");
            }
        }

        /// <summary>
        /// 指定した元Textureだけを収録するAtlasを作成または更新し、Pixel Art向けImporter設定を適用します。
        /// </summary>
        /// <param name="atlasPath">生成または更新する`.spriteatlasv2`のUnityプロジェクト相対パスです。</param>
        /// <param name="sourceTexturePaths">Atlasへ収録するSprite Import済みTextureのパスです。</param>
        private static void GenerateOrUpdateAtlas(
            string atlasPath,
            IReadOnlyList<string> sourceTexturePaths)
        {
            UnityEngine.Object[] sourceTextures = LoadRequiredSourceTextures(sourceTexturePaths);
            SpriteAtlasAsset atlasAsset;

            if (AssetDatabase.LoadMainAssetAtPath(atlasPath) == null)
            {
                atlasAsset = new SpriteAtlasAsset();
            }
            else
            {
                atlasAsset = SpriteAtlasAsset.Load(atlasPath);
                if (atlasAsset == null)
                {
                    throw new InvalidOperationException($"Sprite Atlas V2を読み込めません: {atlasPath}");
                }

                // 削除済みTextureの参照はRuntime SpriteAtlas.GetPackablesでは取得できません。
                // SerializedPropertyから配列そのものを空にし、旧Soil派生などのMissing参照も確実に除去します。
                SerializedObject serializedAtlas = new SerializedObject(atlasAsset);
                SerializedProperty packablesProperty =
                    serializedAtlas.FindProperty("m_ImporterData.packables");
                if (packablesProperty == null || !packablesProperty.isArray)
                {
                    throw new InvalidOperationException(
                        $"Sprite Atlas V2のPackable配列を取得できません: {atlasPath}");
                }

                packablesProperty.ClearArray();
                serializedAtlas.ApplyModifiedPropertiesWithoutUndo();
            }

            // Texture自体をPackableにすると、横一列へSliceした7バイオームすべてを一つの参照で自動収録できます。
            atlasAsset.Add(sourceTextures);
            SpriteAtlasAsset.Save(atlasAsset, atlasPath);
            AssetDatabase.ImportAsset(
                atlasPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ConfigureAtlasImporter(atlasPath);
        }

        /// <summary>
        /// 本番用土地シートがBiome ID順の7枚へ正しくSliceされていることをAtlas更新前に確認します。
        /// </summary>
        /// <exception cref="InvalidOperationException">Sprite数または名前が期待と異なる場合に送出します。</exception>
        private static void ValidateProductionBiomeSpriteSheet()
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(ProductionBiomeTerrainTexturePath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.rect.x)
                .ToArray();

            if (sprites.Length != ExpectedBiomeSpriteCount)
            {
                throw new InvalidOperationException(
                    $"本番用バイオーム土地シートは{ExpectedBiomeSpriteCount}枚へSliceしてください: "
                    + $"{ProductionBiomeTerrainTexturePath}, actual={sprites.Length}");
            }

            for (int index = 0; index < ExpectedBiomeSpriteNames.Length; index++)
            {
                if (!string.Equals(
                        sprites[index].name,
                        ExpectedBiomeSpriteNames[index],
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"本番用バイオームSpriteの順序または名前が不正です: index={index}, "
                        + $"expected={ExpectedBiomeSpriteNames[index]}, actual={sprites[index].name}");
                }
            }
        }

        /// <summary>
        /// 本番用岩壁が16×28の単一SpriteとしてImportされ、Atlasへ安全に登録できることを確認します。
        /// </summary>
        /// <exception cref="InvalidOperationException">Sprite数、寸法、名前が期待と異なる場合に送出します。</exception>
        private static void ValidateProductionRockWallSprite()
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(ProductionRockWallTexturePath)
                .OfType<Sprite>()
                .ToArray();
            if (sprites.Length != 1)
            {
                throw new InvalidOperationException(
                    $"本番用岩壁画像は単一SpriteとしてImportしてください: "
                    + $"{ProductionRockWallTexturePath}, actual={sprites.Length}");
            }

            Sprite sprite = sprites[0];
            Rect rect = sprite.rect;
            bool geometryIsValid = Mathf.Approximately(rect.x, 0f)
                && Mathf.Approximately(rect.y, 0f)
                && Mathf.Approximately(rect.width, 16f)
                && Mathf.Approximately(rect.height, 28f);
            if (!geometryIsValid
                || !string.Equals(
                    sprite.name,
                    "terrain_rock_wall_01",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"本番用岩壁Spriteの名前または16×28区画が不正です: "
                    + $"expected=terrain_rock_wall_01, actual={sprite.name}, rect={rect}");
            }
        }

        /// <summary>
        /// 境界ハイライトシートが左から右へ16×16の14枚として、安定名でSliceされていることを確認します。
        /// </summary>
        /// <exception cref="InvalidOperationException">Sprite数、寸法、位置、名前が期待と異なる場合に送出します。</exception>
        private static void ValidateBoundaryHighlightSpriteSheet()
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(BoundaryHighlightTexturePath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.rect.x)
                .ToArray();

            if (sprites.Length != ExpectedBoundaryHighlightSpriteCount)
            {
                throw new InvalidOperationException(
                    $"境界ハイライトシートは{ExpectedBoundaryHighlightSpriteCount}枚へSliceしてください: "
                    + $"{BoundaryHighlightTexturePath}, actual={sprites.Length}");
            }

            for (int index = 0; index < ExpectedBoundaryHighlightSpriteNames.Length; index++)
            {
                Sprite sprite = sprites[index];
                Rect rect = sprite.rect;
                bool geometryIsValid = Mathf.Approximately(rect.x, index * 16f)
                    && Mathf.Approximately(rect.y, 0f)
                    && Mathf.Approximately(rect.width, 16f)
                    && Mathf.Approximately(rect.height, 16f);
                if (!geometryIsValid
                    || !string.Equals(
                        sprite.name,
                        ExpectedBoundaryHighlightSpriteNames[index],
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"境界ハイライトSpriteの順序、名前、または16px区画が不正です: "
                        + $"index={index}, expected={ExpectedBoundaryHighlightSpriteNames[index]}, "
                        + $"actual={sprite.name}, rect={rect}");
                }
            }
        }

        /// <summary>
        /// 元Textureを順番どおり読み込み、Sprite Importされていない画像や欠落ファイルを早期に報告します。
        /// </summary>
        /// <param name="sourceTexturePaths">確認・読み込み対象のUnityプロジェクト相対パスです。</param>
        /// <returns>Sprite AtlasへPackableとして追加できるTexture配列です。</returns>
        private static UnityEngine.Object[] LoadRequiredSourceTextures(
            IReadOnlyList<string> sourceTexturePaths)
        {
            var sourceTextures = new List<UnityEngine.Object>(sourceTexturePaths.Count);

            for (int index = 0; index < sourceTexturePaths.Count; index++)
            {
                string sourcePath = sourceTexturePaths[index];
                if (!sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Atlasの元画像はAssetsフォルダ内に置いてください: {sourcePath}");
                }

                Texture2D sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
                if (sourceTexture == null)
                {
                    throw new InvalidOperationException($"Atlasの元Textureが見つかりません: {sourcePath}");
                }

                bool containsSprite = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Sprite>().Any();
                if (!containsSprite)
                {
                    throw new InvalidOperationException(
                        $"Atlasの元TextureがSpriteとしてImportされていません: {sourcePath}");
                }

                sourceTextures.Add(sourceTexture);
            }

            return sourceTextures.ToArray();
        }

        /// <summary>
        /// AtlasへPoint・MipMapなし・無圧縮・回転なし・矩形Packingを設定し、Built-inデータとしてビルドへ含めます。
        /// </summary>
        /// <param name="atlasPath">設定対象となる`.spriteatlasv2`のUnityプロジェクト相対パスです。</param>
        private static void ConfigureAtlasImporter(string atlasPath)
        {
            SpriteAtlasImporter importer = AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"SpriteAtlasImporterを取得できません: {atlasPath}");
            }

            importer.includeInBuild = true;

            SpriteAtlasTextureSettings textureSettings = importer.textureSettings;
            textureSettings.filterMode = FilterMode.Point;
            textureSettings.generateMipMaps = false;
            textureSettings.readable = false;
            textureSettings.anisoLevel = 0;
            importer.textureSettings = textureSettings;

            SpriteAtlasPackingSettings packingSettings = importer.packingSettings;
            packingSettings.padding = AtlasPaddingInPixels;
            packingSettings.enableRotation = false;
            packingSettings.enableTightPacking = false;
            packingSettings.enableAlphaDilation = false;
            importer.packingSettings = packingSettings;

            TextureImporterPlatformSettings defaultPlatformSettings =
                importer.GetPlatformSettings("DefaultTexturePlatform");
            defaultPlatformSettings.name = "DefaultTexturePlatform";
            defaultPlatformSettings.maxTextureSize = AtlasMaxTextureSize;
            defaultPlatformSettings.format = TextureImporterFormat.RGBA32;
            defaultPlatformSettings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformSettings(defaultPlatformSettings);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Atlasを指定BuildTarget向けにパックし、土地Spriteがすべて同じパック済みTextureを参照することを確認します。
        /// </summary>
        /// <param name="buildTarget">パックキャッシュとTexture形式を決めるUnityのBuildTargetです。</param>
        private static void PackAndValidateAtlases(BuildTarget buildTarget)
        {
            SpriteAtlas groundAtlas = LoadRequiredRuntimeAtlas(GroundTerrainAtlasPath);
            SpriteAtlas rockWallAtlas = LoadRequiredRuntimeAtlas(RockWallAtlasPath);
            SpriteAtlas boundaryHighlightAtlas =
                LoadRequiredRuntimeAtlas(BoundaryHighlightAtlasPath);
            SpriteAtlasUtility.PackAtlases(
                new[] { groundAtlas, rockWallAtlas, boundaryHighlightAtlas },
                buildTarget,
                canCancel: false);

            ValidateSpritesSharePackedTexture(
                GroundTerrainAtlasPath,
                GroundTerrainSourceTexturePaths);
            ValidateSpritesSharePackedTexture(
                RockWallAtlasPath,
                RockWallSourceTexturePaths);
            ValidateSpritesSharePackedTexture(
                BoundaryHighlightAtlasPath,
                BoundaryHighlightSourceTexturePaths);
        }

        /// <summary>
        /// 指定パスからRuntime参照用SpriteAtlasを読み込み、パック処理へ渡せることを確認します。
        /// </summary>
        /// <param name="atlasPath">読み込む`.spriteatlasv2`のUnityプロジェクト相対パスです。</param>
        /// <returns>Editor内でパック結果を問い合わせるためのRuntime SpriteAtlasです。</returns>
        private static SpriteAtlas LoadRequiredRuntimeAtlas(string atlasPath)
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                throw new InvalidOperationException($"Runtime SpriteAtlasを読み込めません: {atlasPath}");
            }

            return atlas;
        }

        /// <summary>
        /// 全Spriteのパック後Textureが同一であることを確認し、複数ページ化による描画順の再発を防ぎます。
        /// </summary>
        /// <param name="atlasPath">エラーメッセージへ表示するAtlasパスです。</param>
        /// <param name="sourceTexturePaths">Atlasへ収録した元Textureパスです。</param>
        private static void ValidateSpritesSharePackedTexture(
            string atlasPath,
            IReadOnlyList<string> sourceTexturePaths)
        {
            Texture2D sharedPackedTexture = null;
            int spriteCount = 0;

            for (int textureIndex = 0; textureIndex < sourceTexturePaths.Count; textureIndex++)
            {
                string sourcePath = sourceTexturePaths[textureIndex];
                Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(sourcePath)
                    .OfType<Sprite>()
                    .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                    .ToArray();

                for (int spriteIndex = 0; spriteIndex < sprites.Length; spriteIndex++)
                {
                    Sprite sprite = sprites[spriteIndex];
                    Texture2D packedTexture = SpriteUtility.GetSpriteTexture(sprite, getAtlasData: true);
                    if (packedTexture == null || !sprite.packed)
                    {
                        throw new InvalidOperationException(
                            $"SpriteがAtlasへパックされていません: {atlasPath}, {sourcePath}, {sprite.name}");
                    }

                    if (sharedPackedTexture == null)
                    {
                        sharedPackedTexture = packedTexture;
                    }
                    else if (sharedPackedTexture != packedTexture)
                    {
                        throw new InvalidOperationException(
                            $"Atlasが複数Textureページへ分割されました: {atlasPath}. "
                            + $"Max Texture Sizeを見直し、全地形を1ページに保ってください。");
                    }

                    spriteCount++;
                }
            }

            if (spriteCount == 0)
            {
                throw new InvalidOperationException($"Atlasへ検証可能なSpriteがありません: {atlasPath}");
            }
        }

        /// <summary>
        /// Unityプロジェクト相対パスの各フォルダを、AssetDatabaseへ認識される形で不足分だけ作成します。
        /// </summary>
        /// <param name="assetFolderPath">Assetsから始まる作成対象フォルダです。</param>
        private static void EnsureFolderHierarchy(string assetFolderPath)
        {
            string[] pathParts = assetFolderPath.Split('/');
            string currentPath = pathParts[0];

            for (int index = 1; index < pathParts.Length; index++)
            {
                string nextPath = currentPath + "/" + pathParts[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, pathParts[index]);
                }

                currentPath = nextPath;
            }
        }
    }
}
