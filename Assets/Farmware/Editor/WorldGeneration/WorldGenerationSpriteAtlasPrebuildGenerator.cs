// [UNITY-SKILL:SPRITEATLAS]
// [TYPE:PREBUILD]
// このファイルは、一体型土地・岩壁SpriteをSprite Atlas V2へまとめ、異なる元Texture間でもTilemapの行ソートを維持します。
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
    /// 一体型土地と岩壁のSprite Atlas V2を、フォールバック素材または同じパスへ差し替えた製品用素材から冪等に生成します。
    /// </summary>
    public static class WorldGenerationSpriteAtlasBuilder
    {
        /// <summary>生成するSprite AtlasをまとめるUnityプロジェクト相対フォルダです。</summary>
        public const string AtlasAssetFolder = "Assets/Farmware/Art/WorldGeneration/Atlases";

        /// <summary>7種類の16×24土地Spriteを同じTextureページへまとめるAtlasのアセットパスです。</summary>
        public const string GroundTerrainAtlasPath =
            AtlasAssetFolder + "/GroundTerrainAtlas.spriteatlasv2";

        /// <summary>16×28岩壁Spriteを土地とは独立して管理するAtlasのアセットパスです。</summary>
        public const string RockWallAtlasPath =
            AtlasAssetFolder + "/RockWallAtlas.spriteatlasv2";

        /// <summary>Atlasの1ページへ許可する最大辺長です。単位はピクセルです。</summary>
        public const int AtlasMaxTextureSize = 512;

        /// <summary>隣接Spriteの色がにじまないように確保する余白です。単位はピクセルです。</summary>
        public const int AtlasPaddingInPixels = 4;

        /// <summary>製品用画像と同じパスへ差し替え可能なフォールバック素材フォルダです。</summary>
        private const string FallbackAssetFolder =
            "Assets/Farmware/Art/WorldGeneration/Fallback";

        /// <summary>土地Atlasへ収録する7種類の元Textureパスです。</summary>
        private static readonly string[] GroundTerrainSourceTexturePaths =
        {
            FallbackAssetFolder + "/Grassland.png",
            FallbackAssetFolder + "/Forest.png",
            FallbackAssetFolder + "/Desert.png",
            FallbackAssetFolder + "/Rockland.png",
            FallbackAssetFolder + "/Wetland.png",
            FallbackAssetFolder + "/Snowfield.png",
            FallbackAssetFolder + "/Volcanic.png"
        };

        /// <summary>岩壁Atlasへ収録する元Textureパスです。</summary>
        private static readonly string[] RockWallSourceTexturePaths =
        {
            FallbackAssetFolder + "/RockWall.png"
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
        /// 岩壁Atlasへ収録する元Textureパスを、呼び出し元が書き換えられないコピーとして返します。
        /// </summary>
        /// <returns>Unityプロジェクト相対パスを格納した新しい配列です。</returns>
        public static string[] GetRockWallSourceTexturePaths()
        {
            return (string[])RockWallSourceTexturePaths.Clone();
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

            GenerateOrUpdateAtlas(GroundTerrainAtlasPath, GroundTerrainSourceTexturePaths);
            GenerateOrUpdateAtlas(RockWallAtlasPath, RockWallSourceTexturePaths);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Tilemapの行ソートは同じTextureページ内でなければ維持できません。
            // ビルド時とEditor確認時の両方で実際にパックし、土地が複数ページへ分割された時点で検出します。
            PackAndValidateAtlases(buildTarget);

            Debug.Log(
                $"Farmwareの地形Sprite Atlasを更新しました。"
                + $" Target={buildTarget}, Ground={GroundTerrainAtlasPath}, RockWall={RockWallAtlasPath}");
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

                SpriteAtlas runtimeAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                if (runtimeAtlas == null)
                {
                    throw new InvalidOperationException($"Runtime SpriteAtlasを読み込めません: {atlasPath}");
                }

                UnityEngine.Object[] existingPackables = runtimeAtlas.GetPackables();
                if (existingPackables.Length > 0)
                {
                    atlasAsset.Remove(existingPackables);
                }
            }

            // Texture自体をPackableにすると、将来4×4へSliceしてSprite数が増えても同じ画像内の全差分が自動収録されます。
            atlasAsset.Add(sourceTextures);
            SpriteAtlasAsset.Save(atlasAsset, atlasPath);
            AssetDatabase.ImportAsset(
                atlasPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ConfigureAtlasImporter(atlasPath);
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
            SpriteAtlasUtility.PackAtlases(
                new[] { groundAtlas, rockWallAtlas },
                buildTarget,
                canCancel: false);

            ValidateSpritesSharePackedTexture(
                GroundTerrainAtlasPath,
                GroundTerrainSourceTexturePaths);
            ValidateSpritesSharePackedTexture(
                RockWallAtlasPath,
                RockWallSourceTexturePaths);
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
