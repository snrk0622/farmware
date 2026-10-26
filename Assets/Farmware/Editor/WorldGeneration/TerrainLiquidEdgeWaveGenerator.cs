// このファイルは、液体種ごとの64×48 px水際入力から、奥・左・右の7組合せと4フレームAnimatedTileを生成します。
// 人間は方向別の12セルだけを手描きし、Runtimeが使う28 Spriteと7 Assetの機械的な組合せを手作業しません。

using System;
using System.Collections.Generic;
using System.IO;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 選択した淡水・海水・マグマの方向別アニメーションSheetを、製品水際Assetへ変換します。
    /// 入力PNGは透明背景、1px線、Alpha 0/255だけを許可し、半透明の制作ミスを生成前に拒否します。
    /// </summary>
    public static class TerrainLiquidEdgeWaveGenerator
    {
        /// <summary>手描きの方向別入力PNGだけを置く専用フォルダーです。</summary>
        public const string SourceFolder =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Source";

        /// <summary>7マスクAtlasとAnimatedTileを出力する製品Assetフォルダーです。</summary>
        public const string OutputFolder =
            "Assets/Farmware/Art/WorldGeneration/Liquids/Generated";

        /// <summary>1つのSpriteセルの幅と高さです。単位は画素です。</summary>
        public const int TileSizeInPixels = 16;

        /// <summary>横方向へ並べるアニメーションフレーム数です。</summary>
        public const int FrameCount = 4;

        /// <summary>上から奥、左、右を並べる方向入力数です。</summary>
        public const int DirectionCount = 3;

        /// <summary>3bitの非空組合せとして生成する完成マスク数です。</summary>
        public const int MaskCount = 7;

        /// <summary>入力PNGに必要な幅です。4フレーム×16pxです。</summary>
        public const int SourceWidthInPixels = FrameCount * TileSizeInPixels;

        /// <summary>入力PNGに必要な高さです。3方向×16pxです。</summary>
        public const int SourceHeightInPixels = DirectionCount * TileSizeInPixels;

        /// <summary>完成Atlasの幅です。7マスク×16pxです。</summary>
        public const int OutputWidthInPixels = MaskCount * TileSizeInPixels;

        /// <summary>完成Atlasの高さです。4フレーム×16pxです。</summary>
        public const int OutputHeightInPixels = FrameCount * TileSizeInPixels;

        /// <summary>生成するAnimatedTileの再生速度です。4フレーム/秒です。</summary>
        public const float FramesPerSecond = 4f;

        /// <summary>選択中の64×48 PNGから7形状×4フレームとAnimatedTileを生成します。</summary>
        [MenuItem("Farmware/World Generation/Generation/Generate Selected Liquid Edge Waves")]
        public static void GenerateSelectedFromMenu()
        {
            try
            {
                Texture2D source = Selection.activeObject as Texture2D;
                if (source == null)
                {
                    throw new InvalidOperationException(
                        "ProjectビューでFreshWaterEdgeWave.png、SaltWaterEdgeWave.png、MagmaEdgeGlow.pngのいずれかを選択してください。");
                }

                Generate(source);
                EditorUtility.DisplayDialog(
                    "水際アニメーションを生成しました",
                    $"{source.name} から7形状×{FrameCount}フレームと7 AnimatedTileを更新しました。",
                    "閉じる");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "水際アニメーションを生成できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>入力PNGを検証し、完成Atlasと7 AnimatedTileを再生成または更新します。</summary>
        /// <param name="source">専用SourceフォルダーからImport済みの64×48 Textureです。</param>
        public static void Generate(Texture2D source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!TryGetLiquidKind(sourcePath, out TerrainLiquidVisualKind kind))
            {
                throw new InvalidOperationException(
                    $"対応する液体入力名またはフォルダーではありません: {sourcePath}");
            }

            if (source.width != SourceWidthInPixels || source.height != SourceHeightInPixels)
            {
                throw new InvalidOperationException(
                    $"水際入力PNGは{SourceWidthInPixels}×{SourceHeightInPixels} pxで作成してください。"
                    + $" 現在は{source.width}×{source.height} pxです。");
            }

            if (!source.isReadable)
            {
                throw new InvalidOperationException(
                    "水際入力PNGのRead/Writeが無効です。再Import後に生成を実行してください。");
            }

            Color32[] sourcePixels = source.GetPixels32();
            ValidateSourcePixels(sourcePixels, sourcePath);
            Color32[] outputPixels = ComposeMasks(sourcePixels);
            EnsureFolder(OutputFolder);

            string baseName = GetBaseName(kind);
            string atlasPath = $"{OutputFolder}/{baseName}Masks.png";
            WriteAtlasPng(atlasPath, outputPixels);
            ConfigureOutputAtlas(atlasPath, baseName);
            Sprite[] sprites = LoadGeneratedSprites(atlasPath, baseName);
            CreateOrUpdateAnimatedTiles(baseName, sprites);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        /// <summary>入力パスと固定ファイル名から液体種別を求めます。</summary>
        /// <param name="assetPath">Assetsから始まるPNGパスです。</param>
        /// <param name="kind">成功時に淡水、海水、マグマのいずれかを受け取ります。</param>
        /// <returns>専用フォルダー直下の固定3ファイル名ならtrueです。</returns>
        public static bool TryGetLiquidKind(
            string assetPath,
            out TerrainLiquidVisualKind kind)
        {
            kind = TerrainLiquidVisualKind.None;
            string normalizedPath = (assetPath ?? string.Empty).Replace('\\', '/');
            if (!normalizedPath.StartsWith(SourceFolder + "/", StringComparison.Ordinal))
            {
                return false;
            }

            string fileName = Path.GetFileName(normalizedPath);
            if (string.Equals(fileName, "FreshWaterEdgeWave.png", StringComparison.OrdinalIgnoreCase))
            {
                kind = TerrainLiquidVisualKind.FreshWater;
                return true;
            }

            if (string.Equals(fileName, "SaltWaterEdgeWave.png", StringComparison.OrdinalIgnoreCase))
            {
                kind = TerrainLiquidVisualKind.SaltWater;
                return true;
            }

            if (string.Equals(fileName, "MagmaEdgeGlow.png", StringComparison.OrdinalIgnoreCase))
            {
                kind = TerrainLiquidVisualKind.Magma;
                return true;
            }

            return false;
        }

        /// <summary>12入力セルから、各方向をAlpha合成した7マスク×4フレームのAtlas画素を作ります。</summary>
        /// <param name="sourcePixels">Unity下端原点で並ぶ64×48のRGBA画素です。</param>
        /// <returns>Unity下端原点で並ぶ112×64の完成Atlas画素です。</returns>
        public static Color32[] ComposeMasks(Color32[] sourcePixels)
        {
            if (sourcePixels == null
                || sourcePixels.Length != SourceWidthInPixels * SourceHeightInPixels)
            {
                throw new ArgumentException("水際入力画素数が64×48と一致しません。", nameof(sourcePixels));
            }

            var output = new Color32[OutputWidthInPixels * OutputHeightInPixels];
            for (int frame = 0; frame < FrameCount; frame++)
            {
                for (int maskValue = 1; maskValue <= MaskCount; maskValue++)
                {
                    for (int pixelY = 0; pixelY < TileSizeInPixels; pixelY++)
                    {
                        for (int pixelX = 0; pixelX < TileSizeInPixels; pixelX++)
                        {
                            Color32 composed = default;
                            // 仕様書は上からBack、Left、Rightですが、Texture2D画素配列は下端原点なので行を反転して読みます。
                            if ((maskValue & (int)TerrainLiquidEdgeMask.Back) != 0)
                            {
                                composed = ReadSourcePixel(sourcePixels, frame, 2, pixelX, pixelY);
                            }

                            if ((maskValue & (int)TerrainLiquidEdgeMask.Left) != 0)
                            {
                                composed = OverwriteIfVisible(
                                    composed,
                                    ReadSourcePixel(sourcePixels, frame, 1, pixelX, pixelY));
                            }

                            if ((maskValue & (int)TerrainLiquidEdgeMask.Right) != 0)
                            {
                                composed = OverwriteIfVisible(
                                    composed,
                                    ReadSourcePixel(sourcePixels, frame, 0, pixelX, pixelY));
                            }

                            int destinationX = ((maskValue - 1) * TileSizeInPixels) + pixelX;
                            int destinationY = ((FrameCount - 1 - frame) * TileSizeInPixels) + pixelY;
                            output[(destinationY * OutputWidthInPixels) + destinationX] = composed;
                        }
                    }
                }
            }

            return output;
        }

        /// <summary>入力PNGの全画素が透明または完全不透明で、半透明補間を含まないか確認します。</summary>
        /// <param name="pixels">64×48入力の全画素です。</param>
        /// <param name="sourcePath">エラーで修正対象を示すAssetパスです。</param>
        private static void ValidateSourcePixels(Color32[] pixels, string sourcePath)
        {
            for (int index = 0; index < pixels.Length; index++)
            {
                byte alpha = pixels[index].a;
                if (alpha != 0 && alpha != 255)
                {
                    int x = index % SourceWidthInPixels;
                    int y = index / SourceWidthInPixels;
                    throw new InvalidOperationException(
                        $"水際入力はAlpha 0または255だけにしてください: {sourcePath} ({x}, {y}) Alpha={alpha}");
                }
            }
        }

        /// <summary>指定フレーム・方向セルの1画素を入力配列から読みます。</summary>
        /// <param name="pixels">64×48入力画素です。</param>
        /// <param name="frame">左から0〜3のフレームです。</param>
        /// <param name="sourceRowFromBottom">下から0=Right、1=Left、2=Backです。</param>
        /// <param name="pixelX">セル内Xです。</param>
        /// <param name="pixelY">セル内Yです。</param>
        /// <returns>指定方向セルのRGBA画素です。</returns>
        private static Color32 ReadSourcePixel(
            Color32[] pixels,
            int frame,
            int sourceRowFromBottom,
            int pixelX,
            int pixelY)
        {
            int sourceX = (frame * TileSizeInPixels) + pixelX;
            int sourceY = (sourceRowFromBottom * TileSizeInPixels) + pixelY;
            return pixels[(sourceY * SourceWidthInPixels) + sourceX];
        }

        /// <summary>透明なら既存色を保ち、不透明なら後の方向色で交点を上書きします。</summary>
        /// <param name="current">これまでに合成した方向画素です。</param>
        /// <param name="candidate">次に重ねる方向画素です。</param>
        /// <returns>Alpha 255の候補を優先した完成画素です。</returns>
        private static Color32 OverwriteIfVisible(Color32 current, Color32 candidate)
        {
            return candidate.a == 0 ? current : candidate;
        }

        /// <summary>完成画素を一時Textureへ設定し、PNGとして製品出力先へ保存します。</summary>
        /// <param name="assetPath">Assets配下の出力PNGパスです。</param>
        /// <param name="pixels">112×64完成画素です。</param>
        private static void WriteAtlasPng(string assetPath, Color32[] pixels)
        {
            var texture = new Texture2D(
                OutputWidthInPixels,
                OutputHeightInPixels,
                TextureFormat.RGBA32,
                false);
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

        /// <summary>完成AtlasへPoint、16 PPU、Multiple 28区画を設定します。</summary>
        /// <param name="atlasPath">生成直後の112×64 PNGパスです。</param>
        /// <param name="baseName">Sprite名へ使う液体種別名です。</param>
        private static void ConfigureOutputAtlas(string atlasPath, string baseName)
        {
            TextureImporter importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"完成AtlasのImporterを取得できません: {atlasPath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = TileSizeInPixels;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var rects = new SpriteRect[MaskCount * FrameCount];
            int index = 0;
            for (int maskValue = 1; maskValue <= MaskCount; maskValue++)
            {
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    rects[index++] = new SpriteRect
                    {
                        name = GetSpriteName(baseName, maskValue, frame),
                        rect = new Rect(
                            (maskValue - 1) * TileSizeInPixels,
                            (FrameCount - 1 - frame) * TileSizeInPixels,
                            TileSizeInPixels,
                            TileSizeInPixels),
                        pivot = new Vector2(0.5f, 0.5f),
                        alignment = SpriteAlignment.Center,
                        spriteID = GUID.Generate()
                    };
                }
            }

            provider.SetSpriteRects(rects);
            provider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>完成Atlasから名前で28 Spriteを読み、mask優先・frame順の配列へ並べます。</summary>
        /// <param name="atlasPath">区画設定済みの完成Atlasです。</param>
        /// <param name="baseName">期待するSprite名の液体接頭辞です。</param>
        /// <returns>長さ28の完成Sprite配列です。</returns>
        private static Sprite[] LoadGeneratedSprites(string atlasPath, string baseName)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(atlasPath);
            var byName = new Dictionary<string, Sprite>();
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Sprite sprite)
                {
                    byName[sprite.name] = sprite;
                }
            }

            var sprites = new Sprite[MaskCount * FrameCount];
            int destination = 0;
            for (int maskValue = 1; maskValue <= MaskCount; maskValue++)
            {
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    string name = GetSpriteName(baseName, maskValue, frame);
                    if (!byName.TryGetValue(name, out Sprite sprite))
                    {
                        throw new InvalidOperationException($"生成Spriteを再読込できません: {atlasPath} / {name}");
                    }

                    sprites[destination++] = sprite;
                }
            }

            return sprites;
        }

        /// <summary>各マスクの4 Spriteを参照する7 AnimatedTileを作成または更新します。</summary>
        /// <param name="baseName">出力Asset名に使う液体種別名です。</param>
        /// <param name="sprites">mask優先・frame順の28 Spriteです。</param>
        private static void CreateOrUpdateAnimatedTiles(string baseName, Sprite[] sprites)
        {
            for (int maskValue = 1; maskValue <= MaskCount; maskValue++)
            {
                string assetPath = $"{OutputFolder}/{baseName}Mask{maskValue}.asset";
                AnimatedTile tile = AssetDatabase.LoadAssetAtPath<AnimatedTile>(assetPath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<AnimatedTile>();
                    tile.name = $"{baseName}Mask{maskValue}";
                    AssetDatabase.CreateAsset(tile, assetPath);
                }

                var frames = new Sprite[FrameCount];
                int sourceStart = (maskValue - 1) * FrameCount;
                Array.Copy(sprites, sourceStart, frames, 0, FrameCount);
                tile.m_AnimatedSprites = frames;
                tile.m_MinSpeed = FramesPerSecond;
                tile.m_MaxSpeed = FramesPerSecond;
                tile.m_AnimationStartTime = 0f;
                tile.m_AnimationStartFrame = 0;
                tile.m_TileColliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
            }
        }

        /// <summary>液体種別から出力Assetへ使う固定英語名を返します。</summary>
        /// <param name="kind">淡水、海水、マグマのいずれかです。</param>
        /// <returns>EdgeWaveまたはEdgeGlowを含む出力接頭辞です。</returns>
        private static string GetBaseName(TerrainLiquidVisualKind kind)
        {
            switch (kind)
            {
                case TerrainLiquidVisualKind.FreshWater:
                    return "FreshWaterEdgeWave";
                case TerrainLiquidVisualKind.SaltWater:
                    return "SaltWaterEdgeWave";
                case TerrainLiquidVisualKind.Magma:
                    return "MagmaEdgeGlow";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>マスク値とフレームを一意に識別するSprite名を返します。</summary>
        /// <param name="baseName">液体種別の出力接頭辞です。</param>
        /// <param name="maskValue">1〜7の3bit値です。</param>
        /// <param name="frame">0〜3のフレームです。</param>
        /// <returns>Atlas内で重複しない英語名です。</returns>
        private static string GetSpriteName(string baseName, int maskValue, int frame)
        {
            return $"{baseName}_Mask{maskValue}_Frame{frame}";
        }

        /// <summary>Assets配下のフォルダー階層を不足分だけ作ります。</summary>
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

    /// <summary>
    /// 水際入力専用フォルダーの固定3 PNGへ、Point・非圧縮・Read/Write設定をImport前に自動適用します。
    /// 生成済みAtlasや地形材質PNGは対象外にし、用途ごとの分割規則を混在させません。
    /// </summary>
    internal sealed class TerrainLiquidEdgeWaveSourcePostprocessor : AssetPostprocessor
    {
        /// <summary>UnityがTextureをImportする直前に呼び、水際入力だけへ共通設定を適用します。</summary>
        private void OnPreprocessTexture()
        {
            if (!TerrainLiquidEdgeWaveGenerator.TryGetLiquidKind(assetPath, out _))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = true;
        }
    }
}
