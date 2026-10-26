// このファイルは、製品Liquid表示の3pxクリップ、深度勾配、水際3bit、通常Face継続、反射Proxy分類を検証します。
// 実SceneやAssetを変更せず、完成済み3×3チャンクを準備してDirtyサブチャンク分類と同じ純粋計算を保証します。

using System.Collections.Generic;
using System.IO;
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
    /// 淡水・海水・マグマの見た目が、論理Liquid状態を変えずに確定した製品表示規則へ分類されることを保証します。
    /// </summary>
    public sealed class TerrainLiquidPresentationUtilityTests
    {
        /// <summary>各テストが所有し、終了時に破棄する完成地形配列です。</summary>
        private readonly List<NativeArray<GeneratedTerrainCell>> terrainArrays =
            new List<NativeArray<GeneratedTerrainCell>>();

        /// <summary>カタログ検証テストが作成し、終了時に即時破棄する一時ScriptableObjectです。</summary>
        private readonly List<Object> ownedUnityObjects = new List<Object>();

        /// <summary>淡水、海水、マグマをテスト内の固定Runtime IDへ対応付けます。</summary>
        private BuiltInTerrainBlockRuntimeIds runtimeIds;

        /// <summary>各テスト前にLiquid分類へ必要な有効Runtime IDだけを準備します。</summary>
        [SetUp]
        public void SetUp()
        {
            runtimeIds = new BuiltInTerrainBlockRuntimeIds
            {
                FreshWater = new DefinitionRuntimeId(101),
                SaltWater = new DefinitionRuntimeId(102),
                Magma = new DefinitionRuntimeId(103)
            };
        }

        /// <summary>テスト後に全NativeArrayを破棄し、別テストへ地形状態を残しません。</summary>
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

            terrainArrays.Clear();

            // EditModeではDestroyが次フレームまで遅延するため、テストごとに即時破棄して参照を残しません。
            for (int index = ownedUnityObjects.Count - 1; index >= 0; index--)
            {
                if (ownedUnityObjects[index] != null)
                {
                    Object.DestroyImmediate(ownedUnityObjects[index]);
                }
            }

            ownedUnityObjects.Clear();
        }

        /// <summary>全液体で上端3pxを隠し、水だけが床有無に応じた45px・93px勾配を使う基準値を保証します。</summary>
        [Test]
        public void DepthOverlay_ClipsThreePixelsAndUsesContinuousDocumentedGradients()
        {
            Assert.That(TerrainLiquidPresentationUtility.SurfaceClipPixels, Is.EqualTo(3));
            Assert.That(
                TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(3, true),
                Is.EqualTo(0f));
            Assert.That(
                TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(48, true),
                Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(
                TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(96, false),
                Is.EqualTo(1f).Within(0.00001f));
            Assert.That(
                TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(64, true),
                Is.EqualTo(0.5f),
                "床ありは最大50%へ到達した後に濃くしてはいけません。");
        }

        /// <summary>奥・左・右だけを3bitへ詰め、画面手前の岸を波線へ含めないことを保証します。</summary>
        [Test]
        public void EdgeMask_SelectsSevenCombinationsWithoutFrontEdge()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 20, 20, Liquid(runtimeIds.FreshWater));
            SetCell(center, 20, 21, Ground(201));
            SetCell(center, 19, 20, Ground(202));
            SetCell(center, 21, 20, Wall(301));
            SetCell(center, 20, 19, Ground(203));

            TerrainLiquidEdgeMask mask = TerrainLiquidPresentationUtility.SelectEdgeMask(
                neighborhood,
                20,
                20);

            Assert.That(
                mask,
                Is.EqualTo(
                    TerrainLiquidEdgeMask.Back
                    | TerrainLiquidEdgeMask.Left
                    | TerrainLiquidEdgeMask.Right));
            Assert.That((byte)mask, Is.EqualTo(7));
            Assert.That(
                TerrainLiquidPresentationUtility.ShouldClipSurfaceTop(
                    neighborhood,
                    20,
                    20),
                Is.True,
                "奥側が岸へ接する先頭セルだけを3pxクリップします。");

            SetCell(center, 20, 21, Liquid(runtimeIds.FreshWater));
            Assert.That(
                TerrainLiquidPresentationUtility.ShouldClipSurfaceTop(
                    neighborhood,
                    20,
                    20),
                Is.False,
                "連続水域の内部セルを切ると16pxごとの横縞になるため、クリップしてはいけません。");
        }

        /// <summary>同じ水が続く最大6段だけ通常Ground Faceを投影し、マグマや別液体を越えないことを保証します。</summary>
        [Test]
        public void WaterFaceProjection_ContinuesNormalFaceAndStopsAtDifferentLiquid()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 10, 16, Ground(401));
            for (int y = 10; y < 16; y++)
            {
                SetCell(center, 10, y, Liquid(runtimeIds.SaltWater));
            }

            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    neighborhood,
                    10,
                    10,
                    runtimeIds,
                    out TerrainLiquidFaceProjection projection),
                Is.True);
            Assert.That(projection.FaceDepth, Is.EqualTo(5));
            Assert.That(projection.SourceCell.GroundBlockRuntimeId.Value, Is.EqualTo(401));

            SetCell(center, 11, 10, Liquid(runtimeIds.FreshWater));
            SetCell(center, 11, 11, Liquid(runtimeIds.SaltWater));
            SetCell(center, 11, 12, Ground(402));
            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    neighborhood,
                    11,
                    10,
                    runtimeIds,
                    out _),
                Is.False,
                "別種の水を越えて遠方材質を投影してはいけません。");

            SetCell(center, 12, 10, Liquid(runtimeIds.Magma));
            SetCell(center, 12, 11, Ground(403));
            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    neighborhood,
                    12,
                    10,
                    runtimeIds,
                    out _),
                Is.False,
                "不透明マグマの背後へ通常Faceを表示してはいけません。");
        }

        /// <summary>淡水・海水だけが32px以内のWall Faceを反射し、Ground上面とマグマを対象外にすることを保証します。</summary>
        [Test]
        public void ReflectionProjection_UsesOnlyVerticalWallFacesWithinTwoStages()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 30, 30, Liquid(runtimeIds.FreshWater));
            SetCell(center, 30, 31, Liquid(runtimeIds.FreshWater));
            SetCell(center, 30, 32, Wall(501));

            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectWallReflection(
                    neighborhood,
                    30,
                    30,
                    runtimeIds,
                    out TerrainMaterialFaceProjection reflection),
                Is.True);
            Assert.That(reflection.FaceDepth, Is.EqualTo(1));
            Assert.That(reflection.IsWall, Is.True);

            SetCell(center, 31, 30, Liquid(runtimeIds.FreshWater));
            SetCell(center, 31, 31, Ground(502));
            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectWallReflection(
                    neighborhood,
                    31,
                    30,
                    runtimeIds,
                    out _),
                Is.False,
                "水平なGroundTopは反射対象にしてはいけません。");

            SetCell(center, 32, 30, Liquid(runtimeIds.Magma));
            SetCell(center, 32, 31, Wall(503));
            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectWallReflection(
                    neighborhood,
                    32,
                    30,
                    runtimeIds,
                    out _),
                Is.False,
                "マグマへ水面反射Proxyを作ってはいけません。");
        }

        /// <summary>隣接する画素行が別位相となり、全体を一枚板のように同じ量だけ動かさないことを保証します。</summary>
        [Test]
        public void ReflectionOffset_ChangesPhaseForEveryPixelRow()
        {
            Assert.That(
                TerrainLiquidPresentationUtility.GetReflectionHorizontalOffset(0, 0),
                Is.EqualTo(0));
            Assert.That(
                TerrainLiquidPresentationUtility.GetReflectionHorizontalOffset(0, 1),
                Is.EqualTo(1));
            Assert.That(
                TerrainLiquidPresentationUtility.GetReflectionHorizontalOffset(0, 2),
                Is.EqualTo(0));
            Assert.That(
                TerrainLiquidPresentationUtility.GetReflectionHorizontalOffset(0, 3),
                Is.EqualTo(-1));
            Assert.That(
                TerrainLiquidPresentationUtility.GetReflectionHorizontalOffset(-1, 0),
                Is.EqualTo(-1),
                "負の入力でもC#の負剰余へ依存せず同じ4段階へ正規化します。");
        }

        /// <summary>製品用Surface、深度2種、反射Materialが検品専用ではないShaderと確定値を参照することを保証します。</summary>
        [Test]
        public void ProductLiquidMaterials_UseProductShadersAndDocumentedValues()
        {
            Material surface = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidSurface.mat");
            Material withFloor = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidDepthWithFloor.mat");
            Material withoutFloor = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidDepthWithoutFloor.mat");
            Material reflection = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWaterReflection.mat");

            Assert.That(surface, Is.Not.Null);
            Assert.That(surface.shader.name, Is.EqualTo("Farmware/Terrain/Liquid Surface"));
            Assert.That(surface.GetFloat("_ClipPixels"), Is.EqualTo(3f));
            Assert.That(withFloor, Is.Not.Null);
            Assert.That(withFloor.shader.name, Is.EqualTo("Farmware/Terrain/Liquid Depth Overlay"));
            Assert.That(withFloor.GetFloat("_FadePixels"), Is.EqualTo(45f));
            Assert.That(withFloor.GetFloat("_MaximumAlpha"), Is.EqualTo(0.5f));
            Assert.That(withoutFloor, Is.Not.Null);
            Assert.That(withoutFloor.GetFloat("_FadePixels"), Is.EqualTo(93f));
            Assert.That(withoutFloor.GetFloat("_MaximumAlpha"), Is.EqualTo(1f));
            Assert.That(reflection, Is.Not.Null);
            Assert.That(reflection.shader.name, Is.EqualTo("Farmware/Terrain/Water Reflection Proxy"));
            Assert.That(reflection.GetFloat("_MaximumAlpha"), Is.EqualTo(0.22f));
            Assert.That(reflection.GetFloat("_WavePixelsPerSecond"), Is.EqualTo(4f));
        }

        /// <summary>反射Shaderが画素行を位相へ加え、移動後Spriteを元シルエットAlphaで切り抜くことを保証します。</summary>
        [Test]
        public void ProductReflectionShader_UsesPerRowPhaseAndOriginalSilhouetteMask()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string shaderPath = Path.Combine(
                projectRoot,
                "Assets/Farmware/Shaders/TerrainWaterReflection.shader");
            string source = File.ReadAllText(shaderPath);

            Assert.That(source, Does.Contain("animationStep + pixelRow"));
            Assert.That(source, Does.Contain("horizontalPixelOffset * _MainTex_TexelSize.x"));
            Assert.That(source, Does.Contain("originalSilhouette"));
            Assert.That(source, Does.Contain("sampler_PointClamp"));
        }

        /// <summary>液面Shaderが岸側Tileの印だけをクリップし、内部Tileの見た目Alphaへ印を混ぜないことを保証します。</summary>
        [Test]
        public void ProductSurfaceShader_ClipsOnlyMarkedShoreTiles()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string shaderPath = Path.Combine(
                projectRoot,
                "Assets/Farmware/Shaders/TerrainLiquidSurface.shader");
            string source = File.ReadAllText(shaderPath);

            Assert.That(source, Does.Contain("if (input.color.a < 0.5h)"));
            Assert.That(source, Does.Contain("sampled.a * input.materialAlpha"));
        }

        /// <summary>淡水・海水・マグマと各7形状がそろう場合だけ、種別順によらず検索できることを保証します。</summary>
        [Test]
        public void PresentationCatalog_RequiresThreeCompleteLiquidDefinitions()
        {
            TerrainLiquidPresentationCatalog catalog = CreateOwned<TerrainLiquidPresentationCatalog>();
            TerrainLiquidPresentationDefinition salt = CreateLiquidDefinition(
                TerrainLiquidVisualKind.SaltWater);
            TerrainLiquidPresentationDefinition magma = CreateLiquidDefinition(
                TerrainLiquidVisualKind.Magma);
            TerrainLiquidPresentationDefinition fresh = CreateLiquidDefinition(
                TerrainLiquidVisualKind.FreshWater);
            catalog.SetDefinitions(new[] { salt, magma, fresh });

            Assert.That(catalog.TryInitialize(out string error), Is.True, error);
            Assert.That(
                catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.FreshWater,
                    out TerrainLiquidPresentationDefinition actual),
                Is.True);
            Assert.That(actual, Is.SameAs(fresh));
            Assert.That(
                fresh.GetEdgeWaveTile(TerrainLiquidEdgeMask.Back | TerrainLiquidEdgeMask.Right),
                Is.Not.Null,
                "3bit値5は7要素配列の4番目へ対応します。");
        }

        /// <summary>同じ液体種や同じ水際Tileの再利用を拒否し、見た目の欠落を別Assetで補完しないことを保証します。</summary>
        [Test]
        public void PresentationCatalog_RejectsDuplicateKindAndDuplicateEdgeShape()
        {
            TerrainLiquidPresentationCatalog duplicateKindCatalog =
                CreateOwned<TerrainLiquidPresentationCatalog>();
            duplicateKindCatalog.SetDefinitions(new[]
            {
                CreateLiquidDefinition(TerrainLiquidVisualKind.FreshWater),
                CreateLiquidDefinition(TerrainLiquidVisualKind.FreshWater),
                CreateLiquidDefinition(TerrainLiquidVisualKind.Magma)
            });

            Assert.That(duplicateKindCatalog.TryInitialize(out string duplicateKindError), Is.False);
            Assert.That(duplicateKindError, Does.Contain("重複"));

            TerrainLiquidPresentationCatalog duplicateEdgeCatalog =
                CreateOwned<TerrainLiquidPresentationCatalog>();
            TerrainLiquidPresentationDefinition invalidFresh = CreateLiquidDefinition(
                TerrainLiquidVisualKind.FreshWater,
                duplicateFirstEdge: true);
            duplicateEdgeCatalog.SetDefinitions(new[]
            {
                invalidFresh,
                CreateLiquidDefinition(TerrainLiquidVisualKind.SaltWater),
                CreateLiquidDefinition(TerrainLiquidVisualKind.Magma)
            });

            Assert.That(duplicateEdgeCatalog.TryInitialize(out string duplicateEdgeError), Is.False);
            Assert.That(duplicateEdgeError, Does.Contain("同じAssetが重複"));
        }

        /// <summary>深度色と反射Spriteが同じならセル数に比例してTileを生成せず、同一参照を共有することを保証します。</summary>
        [Test]
        public void RuntimeTileCache_ReusesDepthAndReflectionTiles()
        {
            var cache = new TerrainLiquidRuntimeTileCache();
            try
            {
                TileBase firstDepth = cache.GetOrCreateDepthOverlay(
                    new Color(0.2f, 0.4f, 0.6f, 1f),
                    0.25f);
                TileBase secondDepth = cache.GetOrCreateDepthOverlay(
                    new Color(0.2f, 0.4f, 0.6f, 1f),
                    0.25f);
                Assert.That(secondDepth, Is.SameAs(firstDepth));
                Assert.That(cache.DepthTileCount, Is.EqualTo(1));

                Tile sourceSurface = CreateOwned<Tile>();
                var surfaceTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                surfaceTexture.SetPixel(0, 0, Color.white);
                surfaceTexture.Apply();
                ownedUnityObjects.Add(surfaceTexture);
                Sprite surfaceSprite = Sprite.Create(
                    surfaceTexture,
                    new Rect(0f, 0f, 16f, 16f),
                    new Vector2(0.5f, 0.5f),
                    16f);
                ownedUnityObjects.Add(surfaceSprite);
                sourceSurface.sprite = surfaceSprite;
                TileBase firstShore = cache.GetOrCreateSurface(sourceSurface, true);
                TileBase secondShore = cache.GetOrCreateSurface(sourceSurface, true);
                TileBase interior = cache.GetOrCreateSurface(sourceSurface, false);
                Assert.That(secondShore, Is.SameAs(firstShore));
                Assert.That(interior, Is.Not.SameAs(firstShore));
                Assert.That(cache.SurfaceTileCount, Is.EqualTo(2));

                var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                ownedUnityObjects.Add(texture);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 16f, 16f),
                    new Vector2(0.5f, 0.5f),
                    16f);
                ownedUnityObjects.Add(sprite);
                var selection = new TerrainMaterialProductTileSelection(sprite, Color.white);

                TileBase firstReflection = cache.GetOrCreateReflection(selection, 0);
                TileBase secondReflection = cache.GetOrCreateReflection(selection, 0);
                Assert.That(secondReflection, Is.SameAs(firstReflection));
                Assert.That(cache.ReflectionTileCount, Is.EqualTo(1));
            }
            finally
            {
                // Cache所有の一時Tileだけを必ず破棄し、例外時も別テストへ残しません。
                cache.Dispose();
            }
        }

        /// <summary>空の3×3チャンクを作り、中心配列だけを書き換えられる完成近傍として返します。</summary>
        /// <param name="center">中心チャンクの書き込み可能配列です。</param>
        /// <returns>全9位置が完成Bufferを持つ地形近傍です。</returns>
        private CompletedChunkTerrainNeighborhood CreateEmptyNeighborhood(
            out NativeArray<GeneratedTerrainCell> center)
        {
            NativeArray<GeneratedTerrainCell> southWest = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> south = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> southEast = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> west = CreateTerrainArray();
            center = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> east = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> northWest = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> north = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> northEast = CreateTerrainArray();
            return new CompletedChunkTerrainNeighborhood(
                southWest,
                south,
                southEast,
                west,
                center,
                east,
                northWest,
                north,
                northEast);
        }

        /// <summary>4096件をゼロ初期化し、テスト終了時の破棄一覧へ登録します。</summary>
        /// <returns>テスト中だけ有効な完成地形配列です。</returns>
        private NativeArray<GeneratedTerrainCell> CreateTerrainArray()
        {
            var array = new NativeArray<GeneratedTerrainCell>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            terrainArrays.Add(array);
            return array;
        }

        /// <summary>テスト終了時に破棄するUnity ScriptableObjectを作ります。</summary>
        /// <typeparam name="T">引数なしで生成できるUnity ScriptableObject型です。</typeparam>
        /// <returns>このテストFixtureが所有する一時Objectです。</returns>
        private T CreateOwned<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            ownedUnityObjects.Add(value);
            return value;
        }

        /// <summary>Surfaceと7個の個別水際Tileを持つ、完全な1液体定義を作ります。</summary>
        /// <param name="kind">作成する淡水、海水、マグマの種別です。</param>
        /// <param name="duplicateFirstEdge">trueなら検証失敗用に先頭2形状を同じTileへします。</param>
        /// <returns>カタログ初期化へ渡せるテスト用定義です。</returns>
        private TerrainLiquidPresentationDefinition CreateLiquidDefinition(
            TerrainLiquidVisualKind kind,
            bool duplicateFirstEdge = false)
        {
            Tile surface = CreateOwned<Tile>();
            var surfaceTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            surfaceTexture.SetPixel(0, 0, Color.white);
            surfaceTexture.Apply();
            ownedUnityObjects.Add(surfaceTexture);
            Sprite surfaceSprite = Sprite.Create(
                surfaceTexture,
                new Rect(0f, 0f, 16f, 16f),
                new Vector2(0.5f, 0.5f),
                16f);
            ownedUnityObjects.Add(surfaceSprite);
            surface.sprite = surfaceSprite;
            var edges = new TileBase[7];
            for (int index = 0; index < edges.Length; index++)
            {
                edges[index] = duplicateFirstEdge && index == 1
                    ? edges[0]
                    : CreateOwned<Tile>();
            }

            return new TerrainLiquidPresentationDefinition(
                kind,
                surface,
                edges,
                new Color(0.1f, 0.2f, 0.3f, 1f));
        }

        /// <summary>指定したチャンク内セルだけをテスト地形へ置き換えます。</summary>
        /// <param name="array">4096件の完成地形配列です。</param>
        /// <param name="x">0〜63の水平位置です。</param>
        /// <param name="y">0〜63の垂直位置です。</param>
        /// <param name="cell">設定する完成済み地形セルです。</param>
        private static void SetCell(
            NativeArray<GeneratedTerrainCell> array,
            int x,
            int y,
            GeneratedTerrainCell cell)
        {
            array[WorldGrid.ToTileIndex(x, y)] = cell;
        }

        /// <summary>指定Runtime IDのGroundだけを持つセルを作ります。</summary>
        /// <param name="runtimeId">テスト用Ground Runtime IDです。</param>
        /// <returns>液体とWallを持たないGroundセルです。</returns>
        private static GeneratedTerrainCell Ground(int runtimeId)
        {
            return new GeneratedTerrainCell
            {
                GroundBlockRuntimeId = new DefinitionRuntimeId(runtimeId)
            };
        }

        /// <summary>指定Runtime IDのWallだけを持つセルを作ります。</summary>
        /// <param name="runtimeId">テスト用Wall Runtime IDです。</param>
        /// <returns>液体を持たないWallセルです。</returns>
        private static GeneratedTerrainCell Wall(int runtimeId)
        {
            return new GeneratedTerrainCell
            {
                WallBlockRuntimeId = new DefinitionRuntimeId(runtimeId)
            };
        }

        /// <summary>指定Runtime IDのLiquidだけを持つセルを作ります。</summary>
        /// <param name="runtimeId">淡水、海水、マグマのいずれかです。</param>
        /// <returns>GroundとWallを持たないLiquidセルです。</returns>
        private static GeneratedTerrainCell Liquid(DefinitionRuntimeId runtimeId)
        {
            return new GeneratedTerrainCell
            {
                LiquidRuntimeId = runtimeId
            };
        }
    }
}
