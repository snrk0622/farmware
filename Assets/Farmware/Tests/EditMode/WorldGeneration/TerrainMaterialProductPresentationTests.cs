// このファイルは、製品地形表示のGround崖面3段、Wall前面2段、48px減衰、Sprite・Tint解決を検証します。
// 実Sceneへ41材質を接続する前でも、完成チャンク境界と用途別境界Spriteを含む表示契約をEditModeで固定します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 3×3完成チャンクを準備し、投影分類と製品カタログ解決が表示原点や登録順へ依存しないことを保証します。
    /// </summary>
    public sealed class TerrainMaterialProductPresentationTests
    {
        /// <summary>各テストが所有し、終了時に破棄する一時NativeArrayです。</summary>
        private readonly List<NativeArray<GeneratedTerrainCell>> terrainArrays =
            new List<NativeArray<GeneratedTerrainCell>>();

        /// <summary>各テストが作成した一時Textureです。Spriteより後に破棄します。</summary>
        private readonly List<Texture2D> textures = new List<Texture2D>();

        /// <summary>各テストが作成した16×16 Spriteです。</summary>
        private readonly List<Sprite> sprites = new List<Sprite>();

        /// <summary>各テストが作成した製品カタログです。</summary>
        private readonly List<TerrainMaterialPresentationCatalog> catalogs =
            new List<TerrainMaterialPresentationCatalog>();

        /// <summary>
        /// テスト後にNativeArrayとUnity Objectをすべて破棄し、別テストへ材質や地形を残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < terrainArrays.Count; index++)
            {
                NativeArray<GeneratedTerrainCell> array = terrainArrays[index];
                if (array.IsCreated)
                {
                    array.Dispose();
                }
            }

            for (int index = 0; index < catalogs.Count; index++)
            {
                UnityEngine.Object.DestroyImmediate(catalogs[index]);
            }

            for (int index = 0; index < sprites.Count; index++)
            {
                UnityEngine.Object.DestroyImmediate(sprites[index]);
            }

            for (int index = 0; index < textures.Count; index++)
            {
                UnityEngine.Object.DestroyImmediate(textures[index]);
            }

            terrainArrays.Clear();
            catalogs.Clear();
            sprites.Clear();
            textures.Clear();
        }

        /// <summary>
        /// +2Y表示補正前のレイヤ内でWallの直前と2セル手前へ0段目・1段目を分類し、
        /// 補正後は論理根元から上へ2段積めることを保証します。
        /// </summary>
        [Test]
        public void WallProjection_UsesExactlyTwoFrontCells()
        {
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles,
                Is.EqualTo(2),
                "Wall上面は論理根元からFace 2段分だけ画面奥へずらす必要があります。");
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center,
                out _);
            DefinitionRuntimeId wallRuntimeId = new DefinitionRuntimeId(101);
            SetCell(center, 10, 12, new GeneratedTerrainCell
            {
                WallBlockRuntimeId = wallRuntimeId
            });

            Assert.That(
                TerrainMaterialVerticalProjectionUtility.TryProjectWallFace(
                    neighborhood, 10, 11, out TerrainMaterialFaceProjection first),
                Is.True);
            Assert.That(first.FaceDepth, Is.EqualTo(0));
            Assert.That(first.SourceCell.WallBlockRuntimeId, Is.EqualTo(wallRuntimeId));

            Assert.That(
                TerrainMaterialVerticalProjectionUtility.TryProjectWallFace(
                    neighborhood, 10, 10, out TerrainMaterialFaceProjection second),
                Is.True);
            Assert.That(second.FaceDepth, Is.EqualTo(1));

            Assert.That(
                TerrainMaterialVerticalProjectionUtility.TryProjectWallFace(
                    neighborhood, 10, 9, out _),
                Is.False);
        }

        /// <summary>
        /// 乾燥穴だけへGround崖面を3段投影し、液体またはWallを越えて遠方Groundを誤表示しないことを保証します。
        /// </summary>
        [Test]
        public void GroundProjection_UsesThreeDryCellsAndStopsAtOtherTerrain()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center,
                out _);
            DefinitionRuntimeId groundRuntimeId = new DefinitionRuntimeId(201);
            SetCell(center, 20, 23, new GeneratedTerrainCell
            {
                GroundBlockRuntimeId = groundRuntimeId
            });

            for (int faceDepth = 0;
                 faceDepth < TerrainMaterialVerticalProjectionUtility.GroundFaceDepth;
                 faceDepth++)
            {
                Assert.That(
                    TerrainMaterialVerticalProjectionUtility.TryProjectGroundFace(
                        neighborhood,
                        20,
                        22 - faceDepth,
                        out TerrainMaterialFaceProjection projection),
                    Is.True);
                Assert.That(projection.FaceDepth, Is.EqualTo(faceDepth));
                Assert.That(projection.SourceCell.GroundBlockRuntimeId, Is.EqualTo(groundRuntimeId));
            }

            SetCell(center, 21, 22, new GeneratedTerrainCell
            {
                LiquidRuntimeId = new DefinitionRuntimeId(301)
            });
            SetCell(center, 21, 23, new GeneratedTerrainCell
            {
                GroundBlockRuntimeId = groundRuntimeId
            });
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.TryProjectGroundFace(
                    neighborhood, 21, 21, out _),
                Is.False,
                "液体を越えて奥のGroundを乾燥崖として投影してはいけません。");
        }

        /// <summary>
        /// チャンク最上行の乾燥穴が北隣チャンク0行目のGroundを参照し、継ぎ目で崖面が欠けないことを保証します。
        /// </summary>
        [Test]
        public void GroundProjection_CrossesCompletedChunkBoundary()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out _,
                out NativeArray<GeneratedTerrainCell> north);
            DefinitionRuntimeId groundRuntimeId = new DefinitionRuntimeId(401);
            SetCell(north, 7, 0, new GeneratedTerrainCell
            {
                GroundBlockRuntimeId = groundRuntimeId
            });

            Assert.That(
                TerrainMaterialVerticalProjectionUtility.TryProjectGroundFace(
                    neighborhood,
                    7,
                    WorldGridConstants.LogicalChunkSize - 1,
                    out TerrainMaterialFaceProjection projection),
                Is.True);
            Assert.That(projection.SourceLocalY, Is.EqualTo(WorldGridConstants.LogicalChunkSize));
            Assert.That(projection.FaceDepth, Is.EqualTo(0));
        }

        /// <summary>穴上端から48pxまでAlphaが線形に減り、16px段境界で濃さが戻らないことを保証します。</summary>
        [Test]
        public void DryCliffFade_IsContinuousAcrossThreeStages()
        {
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.GetDryCliffFaceAlpha(0),
                Is.EqualTo(1f));
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.GetDryCliffFaceAlpha(16),
                Is.EqualTo(2f / 3f).Within(0.00001f));
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.GetDryCliffFaceAlpha(32),
                Is.EqualTo(1f / 3f).Within(0.00001f));
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.GetDryCliffFaceAlpha(48),
                Is.EqualTo(0f));
            Assert.That(
                TerrainMaterialVerticalProjectionUtility.GetDryCliffFaceAlpha(64),
                Is.EqualTo(0f));
        }

        /// <summary>
        /// GroundとWall上面が同じSpriteを選び、Wallだけ0.85 Tint、1段目だけ用途別境界完成Spriteになることを保証します。
        /// </summary>
        [Test]
        public void ProductResolver_SharesTopAndSelectsPurposeSpecificBoundary()
        {
            Sprite top = CreateSprite("Shared Top", new Color32(90, 140, 70, 255));
            Sprite face = CreateSprite("Face", new Color32(80, 65, 50, 255));
            Sprite groundBoundary = CreateSprite("Ground Boundary", new Color32(70, 90, 50, 255));
            Sprite wallBoundary = CreateSprite("Wall Boundary", new Color32(60, 75, 45, 255));
            TerrainMaterialPresentationCatalog catalog = CreateCompleteCatalog(
                top,
                face,
                groundBoundary,
                wallBoundary);
            TerrainMaterialPresentationRuntimeLookup lookup = CreateRuntimeLookup();
            var coordinate = new WorldTileCoordinate(WorldLayer.Surface, -17, 31);
            DefinitionRuntimeId groundRuntimeId = new DefinitionRuntimeId(1001);
            DefinitionRuntimeId wallRuntimeId = new DefinitionRuntimeId(2001);

            Assert.That(TerrainMaterialProductTileResolver.TryResolveTop(
                catalog, lookup, 55UL, coordinate, groundRuntimeId, false, out TerrainMaterialProductTileSelection groundTop), Is.True);
            Assert.That(TerrainMaterialProductTileResolver.TryResolveTop(
                catalog, lookup, 55UL, coordinate, wallRuntimeId, true, out TerrainMaterialProductTileSelection wallTop), Is.True);
            Assert.That(groundTop.Sprite, Is.SameAs(top));
            Assert.That(wallTop.Sprite, Is.SameAs(top));
            Assert.That(groundTop.Color, Is.EqualTo(Color.white));
            Assert.That(wallTop.Color.r, Is.EqualTo(0.85f).Within(0.00001f));

            Assert.That(TerrainMaterialProductTileResolver.TryResolveFace(
                catalog, lookup, 55UL, coordinate, groundRuntimeId, 0, false, out TerrainMaterialProductTileSelection groundFace), Is.True);
            Assert.That(TerrainMaterialProductTileResolver.TryResolveFace(
                catalog, lookup, 55UL, coordinate, wallRuntimeId, 0, true, out TerrainMaterialProductTileSelection wallFace), Is.True);
            Assert.That(groundFace.Sprite, Is.SameAs(groundBoundary));
            Assert.That(wallFace.Sprite, Is.SameAs(wallBoundary));
            Assert.That(wallFace.Color, Is.EqualTo(Color.white), "Face本体をWall上面と一緒に暗色化してはいけません。");

            Assert.That(TerrainMaterialProductTileResolver.TryResolveFace(
                catalog, lookup, 55UL, coordinate, groundRuntimeId, 1, false, out TerrainMaterialProductTileSelection lowerFace), Is.True);
            Assert.That(lowerFace.Sprite, Is.SameAs(face));
            Assert.That(lowerFace.Color.a, Is.EqualTo(2f / 3f).Within(0.01f));

            // 水中Faceも同じ完成Spriteを使いますが、Alpha 0の識別値で乾燥減衰を回避します。
            for (int depth = 0; depth < TerrainLiquidPresentationUtility.MaximumFaceDepth; depth++)
            {
                Assert.That(TerrainMaterialProductTileResolver.TryResolveSubmergedGroundFace(
                    catalog, lookup, 55UL, coordinate, groundRuntimeId, depth,
                    out TerrainMaterialProductTileSelection submergedFace), Is.True);
                Assert.That(submergedFace.Sprite, Is.SameAs(depth == 0 ? groundBoundary : face));
                Assert.That(submergedFace.Color, Is.EqualTo(new Color(1f, 1f, 1f, 0f)));
            }
        }

        /// <summary>同じSprite・TintのTileと深部黒Tileを再利用し、セル数に応じてUnity Objectが増えないことを保証します。</summary>
        [Test]
        public void RuntimeTileCache_ReusesSpriteTintPairsAndSingleDeepBlackTile()
        {
            Sprite sprite = CreateSprite("Cached Face", new Color32(80, 65, 50, 255));
            var selection = new TerrainMaterialProductTileSelection(sprite, Color.white);
            var cache = new TerrainMaterialRuntimeTileCache();
            try
            {
                TileBase first = cache.GetOrCreate(selection);
                TileBase second = cache.GetOrCreate(selection);
                TileBase faded = cache.GetOrCreate(
                    new TerrainMaterialProductTileSelection(
                        sprite,
                        new Color(1f, 1f, 1f, 2f / 3f)));
                TileBase blackFirst = cache.GetOrCreateDeepBlackTile();
                TileBase blackSecond = cache.GetOrCreateDeepBlackTile();

                Assert.That(second, Is.SameAs(first));
                Assert.That(faded, Is.Not.SameAs(first));
                Assert.That(blackSecond, Is.SameAs(blackFirst));
                Assert.That(cache.Count, Is.EqualTo(2), "キャッシュ件数はセル数ではなくSprite・Tint組の種類数に一致します。");
            }
            finally
            {
                cache.Dispose();
            }
        }

        /// <summary>製品用Materialが検品専用Shaderではなく、48px連続減衰用の製品Shaderを参照することを保証します。</summary>
        [Test]
        public void DryCliffMaterial_UsesProductShader()
        {
            const string materialPath =
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainDryCliffFace.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.That(material, Is.Not.Null, materialPath);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Farmware/Terrain/Dry Cliff Face"));
        }

        /// <summary>空の9チャンクを作り、中心と北チャンクを個別に変更できる完成近傍として返します。</summary>
        /// <param name="center">中心チャンクの書き込み可能配列です。</param>
        /// <param name="north">北隣チャンクの書き込み可能配列です。</param>
        /// <returns>全9位置が完成Bufferを持つ地形近傍です。</returns>
        private CompletedChunkTerrainNeighborhood CreateEmptyNeighborhood(
            out NativeArray<GeneratedTerrainCell> center,
            out NativeArray<GeneratedTerrainCell> north)
        {
            NativeArray<GeneratedTerrainCell> southWest = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> south = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> southEast = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> west = CreateTerrainArray();
            center = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> east = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> northWest = CreateTerrainArray();
            north = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> northEast = CreateTerrainArray();
            return new CompletedChunkTerrainNeighborhood(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast);
        }

        /// <summary>4096件をゼロ初期化した完成地形配列を作り、後始末一覧へ登録します。</summary>
        /// <returns>テスト終了まで有効なPersistent NativeArrayです。</returns>
        private NativeArray<GeneratedTerrainCell> CreateTerrainArray()
        {
            var array = new NativeArray<GeneratedTerrainCell>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            terrainArrays.Add(array);
            return array;
        }

        /// <summary>指定したチャンク内セルだけをテスト地形値へ置き換えます。</summary>
        /// <param name="array">4096件の完成地形配列です。</param>
        /// <param name="x">0〜63の水平位置です。</param>
        /// <param name="y">0〜63の垂直位置です。</param>
        /// <param name="cell">設定する完成地形値です。</param>
        private static void SetCell(
            NativeArray<GeneratedTerrainCell> array,
            int x,
            int y,
            GeneratedTerrainCell cell)
        {
            array[WorldGrid.ToTileIndex(x, y)] = cell;
        }

        /// <summary>41材質の完全な製品カタログを作り、GrassLoamだけ用途別境界Spriteを設定します。</summary>
        /// <param name="top">全材質で使う有効な16×16上面Spriteです。</param>
        /// <param name="face">全材質で使う有効な16×16断面Spriteです。</param>
        /// <param name="groundBoundary">GrassLoamのGround用境界完成Spriteです。</param>
        /// <param name="wallBoundary">GrassLoamのWall用境界完成Spriteです。</param>
        /// <returns>完全性検証を通る一時カタログです。</returns>
        private TerrainMaterialPresentationCatalog CreateCompleteCatalog(
            Sprite top,
            Sprite face,
            Sprite groundBoundary,
            Sprite wallBoundary)
        {
            var definitions = new TerrainMaterialPresentationDefinition[
                TerrainBlockMaterialDefinitions.Count];
            for (int index = 0; index < definitions.Length; index++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)(index + 1);
                bool hasBoundary = stableId == TerrainBlockMaterialId.GrassLoam;
                definitions[index] = new TerrainMaterialPresentationDefinition(
                    stableId,
                    new[] { top },
                    new[] { face },
                    hasBoundary ? 1 : 0,
                    hasBoundary ? new[] { groundBoundary } : Array.Empty<Sprite>(),
                    hasBoundary ? new[] { wallBoundary } : Array.Empty<Sprite>(),
                    darkenWallSurface: true,
                    wallDarkenFactor: 0.85f);
            }

            TerrainMaterialPresentationCatalog catalog =
                ScriptableObject.CreateInstance<TerrainMaterialPresentationCatalog>();
            catalog.SetDefinitions(definitions);
            Assert.That(catalog.TryInitialize(out string error), Is.True, error);
            catalogs.Add(catalog);
            return catalog;
        }

        /// <summary>安定ID1〜41をGround 1001〜1041、Wall 2001〜2041へ対応付けた検索表を作ります。</summary>
        /// <returns>役割をまたぐID重複がない検証済み検索表です。</returns>
        private static TerrainMaterialPresentationRuntimeLookup CreateRuntimeLookup()
        {
            var definitions = new TerrainBlockMaterialDefinitionBlob[
                TerrainBlockMaterialDefinitions.Count];
            for (int index = 0; index < definitions.Length; index++)
            {
                definitions[index] = new TerrainBlockMaterialDefinitionBlob
                {
                    StableId = (TerrainBlockMaterialId)(index + 1),
                    ItemRuntimeId = new DefinitionRuntimeId(3001 + index),
                    GroundRuntimeId = new DefinitionRuntimeId(1001 + index),
                    WallRuntimeId = new DefinitionRuntimeId(2001 + index)
                };
            }

            Assert.That(
                TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    definitions,
                    out TerrainMaterialPresentationRuntimeLookup lookup,
                    out string error),
                Is.True,
                error);
            return lookup;
        }

        /// <summary>16×16、16 PPU、Point Filterの一時Spriteを作り、製品カタログ検証と選択へ使用します。</summary>
        /// <param name="name">失敗時に候補を区別できる名前です。</param>
        /// <param name="color">全画素へ設定する不透明色です。</param>
        /// <returns>テスト終了まで有効なSpriteです。</returns>
        private Sprite CreateSprite(string name, Color32 color)
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                name = name + " Texture",
                filterMode = FilterMode.Point
            };
            var pixels = new Color32[16 * 16];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 16f, 16f),
                new Vector2(0.5f, 0.5f),
                16f);
            sprite.name = name;
            textures.Add(texture);
            sprites.Add(sprite);
            return sprite;
        }
    }
}
