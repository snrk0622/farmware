// このファイルは、製品Wallの固定接地影・32px折れ目影・露出根元だけを使う動的影Caster圧縮をEditModeで検証します。
// 材質Spriteが41種そろう前でも、寸法、異材質接続、再利用、最下段Face根元の起点を固定します。

using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 完成済み地形近傍、16×16根元マスク、小さなRuntime表示を準備し、影形状が材質や更新順に依存しないことを保証します。
    /// </summary>
    public sealed class TerrainWallShadowUtilityTests
    {
        // 製品用固定接地影MaterialのAssetDatabase相対パスです。
        private const string ContactShadowMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWallContactShadow.mat";

        // 製品用32px折れ目影の上段MaterialのAssetDatabase相対パスです。
        private const string FoldShadowTopMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWallFoldShadowTop.mat";

        // 製品用32px折れ目影の下段MaterialのAssetDatabase相対パスです。
        private const string FoldShadowBottomMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWallFoldShadowBottom.mat";

        /// <summary>各テストが作成した動的影表示です。TearDownでHierarchyごと破棄します。</summary>
        private TerrainWallDynamicShadowChunkPresentation presentation;

        /// <summary>動的影Casterの矩形輪郭へ使うテスト専用Spriteです。</summary>
        private Sprite sprite;

        /// <summary>テスト専用Spriteが参照する1×1 Textureです。</summary>
        private Texture2D texture;

        /// <summary>動的影表示の親Transformを提供するテスト専用GameObjectです。</summary>
        private GameObject parent;

        /// <summary>露出根元テストが借用し、TearDownで破棄する完成済み地形配列です。</summary>
        private readonly List<NativeArray<GeneratedTerrainCell>> terrainArrays =
            new List<NativeArray<GeneratedTerrainCell>>();

        /// <summary>各テスト後に作成した地形配列、GameObject、Sprite、Textureを所有順の逆で破棄します。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < terrainArrays.Count; index++)
            {
                NativeArray<GeneratedTerrainCell> terrain = terrainArrays[index];
                if (terrain.IsCreated)
                {
                    terrain.Dispose();
                }
            }

            terrainArrays.Clear();
            presentation?.Destroy();
            presentation = null;
            if (parent != null)
            {
                Object.DestroyImmediate(parent);
                parent = null;
            }

            if (sprite != null)
            {
                Object.DestroyImmediate(sprite);
                sprite = null;
            }

            if (texture != null)
            {
                Object.DestroyImmediate(texture);
                texture = null;
            }
        }

        /// <summary>
        /// 同じ行で接する露出根元を1区間へまとめ、空きセルと行境界では別区間に分けることを保証します。
        /// </summary>
        [Test]
        public void BuildHorizontalRuns_MergesAdjacentRootsAndSeparatesGaps()
        {
            bool[] mask = CreateEmptyMask();
            SetRoot(mask, 1, 2);
            SetRoot(mask, 2, 2);
            SetRoot(mask, 3, 2);
            SetRoot(mask, 5, 2);
            SetRoot(mask, 1, 3);
            var runs = new List<TerrainWallShadowRun>();

            TerrainWallShadowUtility.BuildHorizontalRuns(mask, 16, 32, runs);

            Assert.That(runs.Count, Is.EqualTo(3));
            AssertRun(runs[0], 17, 34, 3);
            AssertRun(runs[1], 21, 34, 1);
            AssertRun(runs[2], 17, 35, 1);
        }

        /// <summary>
        /// 入力が露出根元の有無だけなので、異材質Wallを表す隣接セルも分断せず1区間になる契約を保証します。
        /// </summary>
        [Test]
        public void BuildHorizontalRuns_IgnoresMaterialIdentityByContract()
        {
            bool[] mask = CreateEmptyMask();
            // 左右セルが別材質である状況でも、入力契約は両方trueだけを渡して材質IDを影形状へ含めません。
            SetRoot(mask, 7, 8);
            SetRoot(mask, 8, 8);
            var runs = new List<TerrainWallShadowRun>();

            TerrainWallShadowUtility.BuildHorizontalRuns(mask, 0, 0, runs);

            Assert.That(runs.Count, Is.EqualTo(1));
            AssertRun(runs[0], 7, 8, 2);
        }

        /// <summary>
        /// 奥側Wallの根元を除外し、チャンク境界の南隣Wallも完成済み近傍から読んで自己影を防ぐことを保証します。
        /// </summary>
        [Test]
        public void FillExposedSouthRootMask_UsesCompletedSouthNeighborAcrossChunkBoundary()
        {
            NativeArray<GeneratedTerrainCell> center = CreateTerrainArray();
            NativeArray<GeneratedTerrainCell> south = CreateTerrainArray();
            DefinitionRuntimeId wallId = new DefinitionRuntimeId(7001);

            // 同じチャンクで縦に並ぶ2セルでは、南側の前縁だけを根元として残します。
            SetTerrainWall(center, 3, 1, wallId);
            SetTerrainWall(center, 3, 0, wallId);
            // 中心チャンク最下行でも、南隣チャンク最上行がWallなら内部なので根元にしません。
            SetTerrainWall(center, 4, 0, wallId);
            SetTerrainWall(south, 4, WorldGridConstants.LogicalChunkSize - 1, wallId);
            // 南隣の同座標が空のセルは、チャンク境界上でも前縁として残します。
            SetTerrainWall(center, 5, 0, wallId);

            var neighborhood = new CompletedChunkTerrainNeighborhood(
                default,
                south,
                default,
                default,
                center,
                default,
                default,
                default,
                default);
            bool[] rootMask = CreateEmptyMask();

            TerrainWallShadowUtility.FillExposedSouthRootMask(
                neighborhood,
                0,
                0,
                rootMask);

            Assert.That(GetMask(rootMask, 3, 1), Is.False, "奥側Wallへ根元Casterを作ってはいけません。");
            Assert.That(GetMask(rootMask, 3, 0), Is.True, "同じ列の最前縁は根元として残す必要があります。");
            Assert.That(GetMask(rootMask, 4, 0), Is.False, "南隣チャンクのWallを無視してはいけません。");
            Assert.That(GetMask(rootMask, 5, 0), Is.True, "南隣チャンク側が空なら前縁として扱います。");
        }

        /// <summary>
        /// 32px折れ目影が1段目上端0.35から2段目下端0へ単調減衰し、16px境界で濃く戻らないことを保証します。
        /// </summary>
        [Test]
        public void FoldShadowAlpha_IsContinuousAcrossTwoFaceStages()
        {
            float row0 = TerrainWallShadowUtility.CalculateFoldShadowAlpha(0);
            float row15 = TerrainWallShadowUtility.CalculateFoldShadowAlpha(15);
            float row16 = TerrainWallShadowUtility.CalculateFoldShadowAlpha(16);
            float row31 = TerrainWallShadowUtility.CalculateFoldShadowAlpha(31);

            Assert.That(row0, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(row15, Is.GreaterThan(row16));
            Assert.That(row31, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(TerrainWallShadowUtility.CalculateFoldShadowAlpha(32), Is.EqualTo(0f));
        }

        /// <summary>採用済みの左右1px、手前3px、各Alpha 22%と、Casterが論理Wall根元からずれないことを固定します。</summary>
        [Test]
        public void ShadowConstants_MatchAdoptedPixelDimensions()
        {
            Assert.That(TerrainWallShadowUtility.ContactShadowHorizontalSpreadPixels, Is.EqualTo(1));
            Assert.That(TerrainWallShadowUtility.ContactShadowOffsetYPixels, Is.EqualTo(-3));
            Assert.That(TerrainWallShadowUtility.ContactShadowLayerOpacity, Is.EqualTo(0.22f));
            Assert.That(TerrainWallShadowUtility.FoldShadowDepthPixels, Is.EqualTo(32));
            Assert.That(TerrainWallShadowUtility.DynamicShadowCasterOffsetYInTiles, Is.Zero);
        }

        /// <summary>
        /// 製品用Materialが検品用生成Assetではなく専用Shaderを参照し、採用したAlphaと段開始行を保存することを保証します。
        /// </summary>
        [Test]
        public void ProductShadowMaterials_UseExpectedShadersAndValues()
        {
            Material contact = AssetDatabase.LoadAssetAtPath<Material>(ContactShadowMaterialPath);
            Material foldTop = AssetDatabase.LoadAssetAtPath<Material>(FoldShadowTopMaterialPath);
            Material foldBottom = AssetDatabase.LoadAssetAtPath<Material>(FoldShadowBottomMaterialPath);

            Assert.That(contact, Is.Not.Null);
            Assert.That(contact.shader.name, Is.EqualTo("Farmware/Terrain/Wall Contact Shadow"));
            Assert.That(contact.GetColor("_ShadowColor").a,
                Is.EqualTo(TerrainWallShadowUtility.ContactShadowLayerOpacity).Within(0.0001f));

            Assert.That(foldTop, Is.Not.Null);
            Assert.That(foldBottom, Is.Not.Null);
            Assert.That(foldTop.shader.name, Is.EqualTo("Farmware/Terrain/Wall Fold Shadow"));
            Assert.That(foldBottom.shader, Is.SameAs(foldTop.shader));
            Assert.That(ShaderUtil.ShaderHasError(contact.shader), Is.False);
            Assert.That(ShaderUtil.ShaderHasError(foldTop.shader), Is.False);
            Assert.That(foldTop.GetFloat("_TopAlpha"),
                Is.EqualTo(TerrainWallShadowUtility.FoldShadowTopAlpha).Within(0.0001f));
            Assert.That(foldTop.GetFloat("_FadePixelCount"),
                Is.EqualTo(TerrainWallShadowUtility.FoldShadowDepthPixels));
            Assert.That(foldTop.GetFloat("_StartPixelRow"), Is.EqualTo(0f));
            Assert.That(foldBottom.GetFloat("_StartPixelRow"), Is.EqualTo(16f));
        }

        /// <summary>
        /// Dirty再反映で既存Casterを再利用し、区間長と最下段Face根元位置だけを更新することを保証します。
        /// </summary>
        [Test]
        public void DynamicShadowPresentation_ReusesCasterAndMovesToFaceRoot()
        {
            CreatePresentation();
            var firstRuns = new List<TerrainWallShadowRun>
            {
                new TerrainWallShadowRun(4, 6, 3)
            };

            presentation.UpdateSubChunk(0, firstRuns);
            SpriteRenderer[] firstRenderers =
                presentation.Root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            Assert.That(firstRenderers.Length, Is.EqualTo(1));
            GameObject firstCaster = firstRenderers[0].gameObject;
            Assert.That(firstCaster.transform.localPosition.x, Is.EqualTo(5.5f));
            Assert.That(firstCaster.transform.localPosition.y, Is.EqualTo(6.5f));
            Assert.That(firstCaster.transform.localScale.x, Is.EqualTo(3f));

            var updatedRuns = new List<TerrainWallShadowRun>
            {
                new TerrainWallShadowRun(8, 10, 2)
            };
            presentation.UpdateSubChunk(0, updatedRuns);
            SpriteRenderer[] updatedRenderers =
                presentation.Root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);

            Assert.That(updatedRenderers.Length, Is.EqualTo(1), "Dirty更新でCaster GameObjectを増やしてはいけません。");
            Assert.That(updatedRenderers[0].gameObject, Is.SameAs(firstCaster));
            Assert.That(firstCaster.transform.localPosition.x, Is.EqualTo(9f));
            Assert.That(firstCaster.transform.localPosition.y, Is.EqualTo(10.5f));
            Assert.That(firstCaster.transform.localScale.x, Is.EqualTo(2f));
        }

        /// <summary>256要素をfalseで初期化したサブチャンク根元マスクを作ります。</summary>
        /// <returns>テストが所有する新しい根元マスクです。</returns>
        private static bool[] CreateEmptyMask()
        {
            return new bool[WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize];
        }

        /// <summary>サブチャンク内の1セルを露出根元として設定します。</summary>
        /// <param name="mask">変更する256要素根元マスクです。</param>
        /// <param name="x">0〜15のXです。</param>
        /// <param name="y">0〜15のYです。</param>
        private static void SetRoot(bool[] mask, int x, int y)
        {
            mask[(y * WorldGridConstants.SubChunkSize) + x] = true;
        }

        /// <summary>サブチャンク根元マスクの指定セルを読みます。</summary>
        /// <param name="mask">行優先256要素の根元マスクです。</param>
        /// <param name="x">0〜15のXです。</param>
        /// <param name="y">0〜15のYです。</param>
        /// <returns>指定セルが露出根元ならtrueです。</returns>
        private static bool GetMask(bool[] mask, int x, int y)
        {
            return mask[(y * WorldGridConstants.SubChunkSize) + x];
        }

        /// <summary>全セルを空で初期化した完成地形配列を作り、TearDownの破棄対象へ登録します。</summary>
        /// <returns>4096要素のテスト用地形配列です。</returns>
        private NativeArray<GeneratedTerrainCell> CreateTerrainArray()
        {
            var terrain = new NativeArray<GeneratedTerrainCell>(
                WorldGridConstants.TilesPerChunk,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);
            terrainArrays.Add(terrain);
            return terrain;
        }

        /// <summary>完成地形配列の1セルへWall Runtime IDを設定します。</summary>
        /// <param name="terrain">変更する4096要素の地形配列です。</param>
        /// <param name="x">チャンク内0〜63のXです。</param>
        /// <param name="y">チャンク内0〜63のYです。</param>
        /// <param name="wallId">有効なテスト用Wall Runtime IDです。</param>
        private static void SetTerrainWall(
            NativeArray<GeneratedTerrainCell> terrain,
            int x,
            int y,
            DefinitionRuntimeId wallId)
        {
            terrain[WorldGrid.ToTileIndex(x, y)] = new GeneratedTerrainCell
            {
                WallBlockRuntimeId = wallId
            };
        }

        /// <summary>1つの連続区間が期待した位置と長さを持つことを検証します。</summary>
        /// <param name="run">検証対象区間です。</param>
        /// <param name="expectedX">期待する開始Xです。</param>
        /// <param name="expectedY">期待するYです。</param>
        /// <param name="expectedLength">期待する長さです。</param>
        private static void AssertRun(
            TerrainWallShadowRun run,
            int expectedX,
            int expectedY,
            int expectedLength)
        {
            Assert.That(run.StartX, Is.EqualTo(expectedX));
            Assert.That(run.Y, Is.EqualTo(expectedY));
            Assert.That(run.Length, Is.EqualTo(expectedLength));
        }

        /// <summary>透明矩形Spriteと親Transformを用意し、動的影表示を1つ作ります。</summary>
        private void CreatePresentation()
        {
            texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f,
                0,
                SpriteMeshType.FullRect);
            parent = new GameObject("Terrain Wall Shadow Test Parent");
            presentation = new TerrainWallDynamicShadowChunkPresentation(
                parent.transform,
                sprite,
                parent.layer);
        }
    }
}
