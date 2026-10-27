// このファイルは、液体3種のSurface、静止水際の基準色、共有Shader Materialを製品カタログへ公開します。
// 水際形状はRuntimeの有効地形から決めるため、方向別4フレーム入力やAnimatedTileは生成しません。

using System;
using System.IO;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 淡水・海水・マグマの製品用Surface Tile、水際色、深度色を公開します。
    /// 製品の単色Surfaceは再生成し、明暗の調整値は共有MaterialのInspectorで変更できます。
    /// </summary>
    public static class TerrainLiquidProductPublisher
    {
        /// <summary>製品用Surface SpriteとTileを保存するフォルダーです。</summary>
        public const string PublishedSurfaceFolder =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Published/Surfaces";

        /// <summary>淡水・海水・マグマの完成参照をまとめる製品カタログの保存先です。</summary>
        public const string CatalogPath =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Published/TerrainLiquidPresentationCatalog.asset";

        /// <summary>淡水Surfaceの製品PNGパスです。</summary>
        public const string FreshWaterSurfaceTexturePath =
            PublishedSurfaceFolder + "/FreshWaterSurface.png";

        /// <summary>海水Surfaceの製品PNGパスです。</summary>
        public const string SaltWaterSurfaceTexturePath =
            PublishedSurfaceFolder + "/SaltWaterSurface.png";

        /// <summary>マグマSurfaceの製品PNGパスです。</summary>
        public const string MagmaSurfaceTexturePath =
            PublishedSurfaceFolder + "/MagmaSurface.png";

        /// <summary>淡水Surfaceの製品Tileパスです。</summary>
        public const string FreshWaterSurfaceTilePath =
            PublishedSurfaceFolder + "/FreshWaterSurface.asset";

        /// <summary>海水Surfaceの製品Tileパスです。</summary>
        public const string SaltWaterSurfaceTilePath =
            PublishedSurfaceFolder + "/SaltWaterSurface.asset";

        /// <summary>マグマSurfaceの製品Tileパスです。</summary>
        public const string MagmaSurfaceTilePath =
            PublishedSurfaceFolder + "/MagmaSurface.asset";

        /// <summary>淡水の初期水際線へ使う明るい色です。</summary>
        private static readonly Color32 FreshWaterEdgeLight = new Color32(171, 229, 238, 255);

        /// <summary>海水の初期水際線へ使う明るい色です。</summary>
        private static readonly Color32 SaltWaterEdgeLight = new Color32(157, 201, 237, 255);

        /// <summary>淡水の深化Fadeが96px地点で収束する、明るいシアンではない暗い青緑です。</summary>
        private static readonly Color32 FreshWaterDepthColor = new Color32(24, 72, 96, 255);

        /// <summary>海水の深化Fadeが96px地点で収束する、淡水より暗い深い青です。</summary>
        private static readonly Color32 SaltWaterDepthColor = new Color32(16, 42, 72, 255);

        /// <summary>マグマの初期接触発光へ使う明るい色です。</summary>
        private static readonly Color32 MagmaEdgeLight = new Color32(255, 211, 64, 255);

        /// <summary>固定した岸形状の上で明暗だけを動かす製品Materialの保存先です。</summary>
        public const string ShorelineMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidShoreline.mat";

        /// <summary>メニューから液体3種の製品Assetを公開します。</summary>
        [MenuItem("Farmware/World Generation/Generation/Publish Liquid Presentation Assets")]
        public static void PublishFromMenu()
        {
            try
            {
                TerrainLiquidPresentationCatalog catalog = PublishAll();
                EditorUtility.DisplayDialog(
                    "液体表示を製品公開しました",
                    $"淡水・海水・マグマのSurface、水際色、共有水際Material、製品カタログを更新しました。\n{AssetDatabase.GetAssetPath(catalog)}",
                    "閉じる");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "液体表示を製品公開できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>
        /// 単色Surfaceと共有水際Materialを公開し、現行形式の製品カタログを保存します。
        /// </summary>
        /// <returns>保存後に再読込した検証済み製品液体カタログです。</returns>
        public static TerrainLiquidPresentationCatalog PublishAll()
        {
            EnsureFolder(PublishedSurfaceFolder);
            EnsureShorelineMaterial();

            // 水は全面Surfaceで断面を隠さず、専用Depth Overlayだけで0%から連続的に濃くします。
            // マグマだけは最初の段から不透明にし、背後のFaceを完全に遮蔽します。
            Tile freshSurface = EnsureSurfaceTile(
                FreshWaterSurfaceTexturePath,
                FreshWaterSurfaceTilePath,
                new Color32(76, 157, 211, 0));
            Tile saltSurface = EnsureSurfaceTile(
                SaltWaterSurfaceTexturePath,
                SaltWaterSurfaceTilePath,
                new Color32(42, 101, 176, 0));
            Tile magmaSurface = EnsureSurfaceTile(
                MagmaSurfaceTexturePath,
                MagmaSurfaceTilePath,
                new Color32(238, 82, 24, 255));

            TerrainLiquidPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TerrainLiquidPresentationCatalog>();
                catalog.name = "TerrainLiquidPresentationCatalog";
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetDefinitions(new[]
            {
                CreateDefinition(
                    TerrainLiquidVisualKind.FreshWater,
                    freshSurface,
                    FreshWaterEdgeLight,
                    FreshWaterDepthColor),
                CreateDefinition(
                    TerrainLiquidVisualKind.SaltWater,
                    saltSurface,
                    SaltWaterEdgeLight,
                    SaltWaterDepthColor),
                CreateDefinition(
                    TerrainLiquidVisualKind.Magma,
                    magmaSurface,
                    MagmaEdgeLight,
                    new Color32(238, 82, 24, 255))
            });
            if (!catalog.TryInitialize(out string catalogError))
            {
                throw new InvalidOperationException(
                    $"製品液体カタログを検証できません: {catalogError}");
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            TerrainLiquidPresentationCatalog reloaded =
                AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(CatalogPath);
            if (reloaded == null || !reloaded.TryInitialize(out catalogError))
            {
                throw new InvalidOperationException(
                    $"保存後の製品液体カタログを再読込できません: {catalogError}");
            }

            return reloaded;
        }

        /// <summary>16×16 pxの単色製品Surface PNGとColliderなしTileを作成または更新します。</summary>
        /// <param name="texturePath">製品SpriteのPNGパスです。</param>
        /// <param name="tilePath">製品TileのAssetパスです。</param>
        /// <param name="color">水はAlpha 0、マグマはAlpha 255の基礎色です。</param>
        /// <returns>製品カタログへ登録する静止Tileです。</returns>
        private static Tile EnsureSurfaceTile(
            string texturePath,
            string tilePath,
            Color32 color)
        {
            const int size = TerrainLiquidPresentationUtility.PixelsPerCell;
            var pixels = new Color32[size * size];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = color;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureSurfaceTexture(texturePath);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    $"製品液体Surface Spriteを読み込めません: {texturePath}");
            }

            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = Path.GetFileNameWithoutExtension(tilePath);
                AssetDatabase.CreateAsset(tile, tilePath);
            }

            tile.sprite = sprite;
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.LockColor | TileFlags.LockTransform;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        /// <summary>製品SurfaceへPoint、16 PPU、Full Rect、非圧縮設定を適用します。</summary>
        /// <param name="texturePath">設定する製品PNGパスです。</param>
        private static void ConfigureSurfaceTexture(string texturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"製品液体SurfaceのImporterを取得できません: {texturePath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TerrainLiquidPresentationUtility.PixelsPerCell;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        /// <summary>1液体分のSurface、水際色、深度色を現行定義へまとめます。</summary>
        /// <param name="kind">淡水、海水、マグマの種別です。</param>
        /// <param name="surfaceTile">製品用の静止Surfaceです。</param>
        /// <param name="shorelineColor">明暗Shaderへ渡す基準RGB色です。</param>
        /// <param name="depthColor">深度Overlayへ使うRGB色です。</param>
        /// <returns>現行形式のカタログへ保存する1行です。</returns>
        private static TerrainLiquidPresentationDefinition CreateDefinition(
            TerrainLiquidVisualKind kind, Tile surfaceTile, Color shorelineColor, Color depthColor)
        {
            return new TerrainLiquidPresentationDefinition(kind, surfaceTile, shorelineColor, depthColor);
        }

        /// <summary>共有Shader Materialを初回だけ作ります。既存の明暗調整値は再公開でも保持します。</summary>
        private static void EnsureShorelineMaterial()
        {
            Shader shader = Shader.Find("Farmware/Terrain/Liquid Shoreline");
            if (shader == null)
            {
                throw new InvalidOperationException("製品用のLiquid Shoreline Shaderを読み込めません。");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(ShorelineMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "TerrainLiquidShoreline" };
                AssetDatabase.CreateAsset(material, ShorelineMaterialPath);
            }
            else if (material.shader != shader)
            {
                // 固定パスの製品Materialが別Shaderを使っている場合だけ、正しい参照へ戻します。
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>Assets配下のフォルダー階層を不足分だけ作成します。</summary>
        /// <param name="folderPath">作成するAssets相対フォルダーです。</param>
        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }
    }
}
