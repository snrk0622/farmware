// このファイルは、液体3種の初期製品素材、水際アニメーション、製品表示カタログを一括公開します。
// TerrainLiquidEdgeWaveGeneratorへ方向別入力を渡し、ChunkTilemapPresenterが制作入力やFallbackを直接参照しない境界を作ります。

using System;
using System.IO;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 淡水・海水・マグマの製品用Surface Tile、7水際Tile、深度色を公開します。
    /// 入力PNGがまだない場合だけ採用済み検品値から初期画像を作り、手描き画像へ上書きした後は内容を保持します。
    /// </summary>
    public static class TerrainLiquidProductPublisher
    {
        /// <summary>製品用Surface SpriteとTileを保存するフォルダーです。</summary>
        public const string PublishedSurfaceFolder =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Published/Surfaces";

        /// <summary>淡水・海水・マグマの完成参照をまとめる製品カタログの保存先です。</summary>
        public const string CatalogPath =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Published/TerrainLiquidPresentationCatalog.asset";

        /// <summary>淡水の方向別水際入力を保存する固定パスです。</summary>
        public const string FreshWaterEdgeSourcePath =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Source/FreshWaterEdgeWave.png";

        /// <summary>海水の方向別水際入力を保存する固定パスです。</summary>
        public const string SaltWaterEdgeSourcePath =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Source/SaltWaterEdgeWave.png";

        /// <summary>マグマの方向別発光入力を保存する固定パスです。</summary>
        public const string MagmaEdgeSourcePath =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Source/MagmaEdgeGlow.png";

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

        /// <summary>淡水の初期水際線へ使う暗い色です。</summary>
        private static readonly Color32 FreshWaterEdgeDark = new Color32(86, 180, 215, 255);

        /// <summary>淡水の初期水際線へ使う明るい色です。</summary>
        private static readonly Color32 FreshWaterEdgeLight = new Color32(171, 229, 238, 255);

        /// <summary>海水の初期水際線へ使う暗い色です。</summary>
        private static readonly Color32 SaltWaterEdgeDark = new Color32(78, 135, 205, 255);

        /// <summary>海水の初期水際線へ使う明るい色です。</summary>
        private static readonly Color32 SaltWaterEdgeLight = new Color32(157, 201, 237, 255);

        /// <summary>淡水の深化Fadeが96px地点で収束する、明るいシアンではない暗い青緑です。</summary>
        private static readonly Color32 FreshWaterDepthColor = new Color32(24, 72, 96, 255);

        /// <summary>海水の深化Fadeが96px地点で収束する、淡水より暗い深い青です。</summary>
        private static readonly Color32 SaltWaterDepthColor = new Color32(16, 42, 72, 255);

        /// <summary>マグマの初期接触発光へ使う暗い色です。</summary>
        private static readonly Color32 MagmaEdgeDark = new Color32(238, 82, 24, 255);

        /// <summary>マグマの初期接触発光へ使う明るい色です。</summary>
        private static readonly Color32 MagmaEdgeLight = new Color32(255, 211, 64, 255);

        /// <summary>メニューから液体3種の製品Assetを公開します。</summary>
        [MenuItem("Farmware/World Generation/Generation/Publish Liquid Presentation Assets")]
        public static void PublishFromMenu()
        {
            try
            {
                TerrainLiquidPresentationCatalog catalog = PublishAll();
                EditorUtility.DisplayDialog(
                    "液体表示を製品公開しました",
                    $"淡水・海水・マグマのSurface、水際7形状、製品カタログを更新しました。\n{AssetDatabase.GetAssetPath(catalog)}",
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
        /// 不足する初期入力を作成し、方向別入力から水際Tileを再生成して製品カタログを保存します。
        /// </summary>
        /// <returns>保存後に再読込した検証済み製品液体カタログです。</returns>
        public static TerrainLiquidPresentationCatalog PublishAll()
        {
            EnsureFolder(TerrainLiquidEdgeWaveGenerator.SourceFolder);
            EnsureFolder(PublishedSurfaceFolder);

            // 初回だけ検品で採用した寸法と仮配色を画像化します。同じ固定パスに手描き画像があれば上書きしません。
            EnsureInitialEdgeSource(
                FreshWaterEdgeSourcePath,
                FreshWaterEdgeDark,
                FreshWaterEdgeLight);
            EnsureInitialEdgeSource(
                SaltWaterEdgeSourcePath,
                SaltWaterEdgeDark,
                SaltWaterEdgeLight);
            EnsureInitialEdgeSource(
                MagmaEdgeSourcePath,
                MagmaEdgeDark,
                MagmaEdgeLight);

            GenerateEdgeAssets(FreshWaterEdgeSourcePath);
            GenerateEdgeAssets(SaltWaterEdgeSourcePath);
            GenerateEdgeAssets(MagmaEdgeSourcePath);

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
                    "FreshWaterEdgeWave",
                    FreshWaterDepthColor),
                CreateDefinition(
                    TerrainLiquidVisualKind.SaltWater,
                    saltSurface,
                    "SaltWaterEdgeWave",
                    SaltWaterDepthColor),
                CreateDefinition(
                    TerrainLiquidVisualKind.Magma,
                    magmaSurface,
                    "MagmaEdgeGlow",
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

        /// <summary>
        /// 64×48 pxの初期方向別入力がない場合だけ、奥・左・右の1px接触線を4位相で作成します。
        /// </summary>
        /// <param name="assetPath">固定ファイル名を含むSource PNGパスです。</param>
        /// <param name="darkColor">2px帯の暗い色です。</param>
        /// <param name="lightColor">2px帯の明るい色です。</param>
        private static void EnsureInitialEdgeSource(
            string assetPath,
            Color32 darkColor,
            Color32 lightColor)
        {
            if (File.Exists(assetPath))
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                return;
            }

            int width = TerrainLiquidEdgeWaveGenerator.SourceWidthInPixels;
            int height = TerrainLiquidEdgeWaveGenerator.SourceHeightInPixels;
            var pixels = new Color32[width * height];
            for (int frame = 0; frame < TerrainLiquidEdgeWaveGenerator.FrameCount; frame++)
            {
                // Texture2Dは下端原点なので、下段=Right、中段=Left、上段=Backへ直接描きます。
                for (int coordinate = 0;
                     coordinate < TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
                     coordinate++)
                {
                    Color32 color = ((coordinate + frame) / 2) % 2 == 0
                        ? darkColor
                        : lightColor;
                    int frameStartX = frame * TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
                    SetPixel(pixels, width, frameStartX + coordinate, 44, color);
                    // 岸側の液面は上端3pxを隠すため、左右線も表示上端の12px目で止めて水面より上へ突き出させません。
                    if (coordinate <= TerrainLiquidEdgeWaveGenerator.TileSizeInPixels
                        - TerrainLiquidPresentationUtility.SurfaceClipPixels
                        - 1)
                    {
                        SetPixel(
                            pixels,
                            width,
                            frameStartX,
                            TerrainLiquidEdgeWaveGenerator.TileSizeInPixels + coordinate,
                            color);
                        SetPixel(
                            pixels,
                            width,
                            frameStartX + TerrainLiquidEdgeWaveGenerator.TileSizeInPixels - 1,
                            coordinate,
                            color);
                    }
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>指定画素へ色を書き、方向別入力の座標式を1か所へ集約します。</summary>
        /// <param name="pixels">下端原点で並ぶ出力画素です。</param>
        /// <param name="width">画像の横幅です。</param>
        /// <param name="x">書き込むX座標です。</param>
        /// <param name="y">書き込むY座標です。</param>
        /// <param name="color">Alpha 255の2色いずれかです。</param>
        private static void SetPixel(
            Color32[] pixels,
            int width,
            int x,
            int y,
            Color32 color)
        {
            pixels[(y * width) + x] = color;
        }

        /// <summary>Import済み方向別入力から28 Spriteと7 AnimatedTileを更新します。</summary>
        /// <param name="sourcePath">固定名の64×48 px入力PNGです。</param>
        private static void GenerateEdgeAssets(string sourcePath)
        {
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"液体水際入力を読み込めません: {sourcePath}");
            }

            TerrainLiquidEdgeWaveGenerator.Generate(source);
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

        /// <summary>1液体分のSurface、7水際Tile、深度色を完成定義へまとめます。</summary>
        /// <param name="kind">淡水、海水、マグマのいずれかです。</param>
        /// <param name="surfaceTile">製品用静止Surface Tileです。</param>
        /// <param name="edgeBaseName">生成済みAnimatedTileの共通接頭辞です。</param>
        /// <param name="depthColor">深度Overlayへ使うRGB色です。</param>
        /// <returns>製品カタログへ保存する1行です。</returns>
        private static TerrainLiquidPresentationDefinition CreateDefinition(
            TerrainLiquidVisualKind kind,
            Tile surfaceTile,
            string edgeBaseName,
            Color depthColor)
        {
            var edgeTiles = new TileBase[TerrainLiquidEdgeWaveGenerator.MaskCount];
            for (int maskIndex = 0; maskIndex < edgeTiles.Length; maskIndex++)
            {
                string assetPath =
                    $"{TerrainLiquidEdgeWaveGenerator.OutputFolder}/{edgeBaseName}Mask{maskIndex + 1}.asset";
                edgeTiles[maskIndex] = AssetDatabase.LoadAssetAtPath<TileBase>(assetPath);
                if (edgeTiles[maskIndex] == null)
                {
                    throw new InvalidOperationException(
                        $"生成済み水際Tileを読み込めません: {assetPath}");
                }
            }

            return new TerrainLiquidPresentationDefinition(
                kind,
                surfaceTile,
                edgeTiles,
                depthColor);
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
