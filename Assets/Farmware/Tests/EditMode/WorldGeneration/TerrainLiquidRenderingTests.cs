// このファイルは、製品のTilemapとShaderを実際にGPU描画し、水色の均一性、水中断面の範囲、深さによる透過合成を検証します。
// CPU側の計算だけでは検出できない色変換・崖Shader誤適用と、水際の画素・階調・コマ保持を読戻しで検出します。

using System.Collections;
using Farmware.WorldGeneration.Presentation;
using Farmware.Simulation;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>保存しないPreview Sceneで製品Tileを描き、タイル境界を含む実際の出力を検査します。</summary>
    public sealed class TerrainLiquidRenderingTests
    {
        /// <summary>横3セル・縦8セルを16px/セルで描く読戻し画像の幅です。</summary>
        private const int RenderWidth = 48;

        /// <summary>水際から96px以深までを含める読戻し画像の高さです。</summary>
        private const int RenderHeight = 128;

        /// <summary>各テストが作り、終了時に閉じる未保存の隔離Sceneです。</summary>
        private Scene previewScene;

        /// <summary>隔離Sceneだけを描き、ゲームのCameraには触れないテスト所有Cameraです。</summary>
        private Camera renderCamera;

        /// <summary>Shaderの出力をLinear浮動小数で保持するテスト所有RenderTextureです。</summary>
        private RenderTexture renderTarget;

        /// <summary>GPUの出力を検査するための、テスト所有CPU側Textureです。</summary>
        private Texture2D readback;

        /// <summary>各テスト前に、製品と同じTilemapを描ける隔離Cameraと描画先を準備します。</summary>
        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("GPUのない実行環境では画素読戻しを検証できません。");
            }

            previewScene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Liquid Rendering Test Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
            renderCamera = cameraObject.AddComponent<Camera>();
            renderCamera.enabled = false;
            renderCamera.scene = previewScene;
            renderCamera.orthographic = true;
            renderCamera.orthographicSize = 4f;
            renderCamera.aspect = RenderWidth / (float)RenderHeight;
            renderCamera.transform.position = new Vector3(1.5f, 4f, -10f);
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = Color.white;

            // sRGBへ戻す前の値でAlpha合成を検査し、画面やOSの色管理による誤差を除きます。
            // URPのRender Graphが要求する深度バッファも確保し、描画要求の警告や未描画を避けます。
            renderTarget = new RenderTexture(
                RenderWidth, RenderHeight, 24, RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            renderTarget.Create();
            readback = new Texture2D(
                RenderWidth, RenderHeight, TextureFormat.RGBAFloat, false, true);
        }

        /// <summary>成功・失敗にかかわらず、隔離SceneとGPU/CPU描画先を破棄してEditorへ状態を残しません。</summary>
        [TearDown]
        public void TearDown()
        {
            if (previewScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            if (renderTarget != null)
            {
                renderTarget.Release();
                Object.DestroyImmediate(renderTarget);
            }

            Object.DestroyImmediate(readback);
        }


        /// <summary>水の基底色は投影段数・床有無で変わらず、水種と実際の3pxクリップだけを反映することを保証します。</summary>
        /// <param name="salt">海水の場合はtrueです。</param>
        /// <param name="clip">奥岸上端を切る場合はtrueです。</param>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void WaterBody_IsUniformAcrossAllProjectionStages(bool salt, bool clip)
        {
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                var material = new Material(Shader.Find("Farmware/Terrain/Liquid Depth Overlay"));
                try
                {
                    renderCamera.backgroundColor = Color.black;
                    material.SetColor("_FreshWaterColor", Color.red);
                    material.SetColor("_SaltWaterColor", Color.blue);
                    Tilemap map = CreateTilemap(material);
                    for (int stage = 0; stage <= 6; stage++)
                        for (int floor = 0; floor <= 1; floor++)
                        {
                            SetWaterDataTile(map, new Vector3Int(1, 1, 0), cache.GetOrCreateDepthOverlay(),
                                stage, salt ? TerrainLiquidVisualKind.SaltWater : TerrainLiquidVisualKind.FreshWater, floor == 1, clip);
                            RenderPixels();
                            Color pixel = readback.GetPixel(24, 24);
                            Assert.That(salt ? pixel.b : pixel.r, Is.EqualTo(1f).Within(0.005f));
                            Assert.That(salt ? pixel.r : pixel.b, Is.LessThan(0.005f));
                            Color top = readback.GetPixel(24, 31);
                            Assert.That(salt ? top.b : top.r, Is.EqualTo(clip ? 0f : 1f).Within(0.005f));
                        }
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>断面を基底色へ重ねた最終画素が、既存45px／93pxの光学的減衰と一致することを保証します。</summary>
        /// <param name="hasFloor">床ありの場合はtrueです。</param>
        /// <param name="origin">負座標の表示原点でも同じ画素になることを確認します。</param>
        [TestCase(false, 0)]
        [TestCase(true, 0)]
        [TestCase(false, -64)]
        [TestCase(true, -64)]
        public void SubmergedFace_PreservesDocumentedFadeAndGroundLip(bool hasFloor, int origin)
        {
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                var bodyMaterial = new Material(Shader.Find("Farmware/Terrain/Liquid Depth Overlay"));
                var faceMaterial = new Material(Shader.Find("Farmware/Terrain/Submerged Ground Face"));
                try
                {
                    renderCamera.backgroundColor = Color.black;
                    renderCamera.transform.position += new Vector3(origin, origin, 0);
                    bodyMaterial.SetColor("_FreshWaterColor", Color.black);
                    Tilemap body = CreateTilemap(bodyMaterial);
                    Tilemap face = CreateTilemap(faceMaterial);
                    body.transform.position = face.transform.position = new Vector3(origin, origin, 0);
                    face.GetComponent<TilemapRenderer>().sortingOrder = 1;
                    for (int y = 0; y < 7; y++)
                        for (int x = 0; x < 3; x++)
                        {
                            SetWaterDataTile(body, new Vector3Int(x, y, 0), cache.GetOrCreateDepthOverlay(),
                                6 - y, TerrainLiquidVisualKind.FreshWater, hasFloor, y == 6);
                            SetWaterDataTile(face, new Vector3Int(x, y, 0), cache.GetOrCreateDepthOverlay(),
                                6 - y, TerrainLiquidVisualKind.FreshWater, hasFloor, y == 6);
                        }
                    RenderPixels();
                    for (int depth = 0; depth < 112; depth++)
                    {
                        float expected = 1f - TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(depth + 0.5f, hasFloor);
                        Assert.That(readback.GetPixel(24, 111 - depth).r, Is.EqualTo(expected).Within(0.012f),
                            $"岸から{depth}pxで断面と基底色の合成が変わっています。");
                    }
                    // 水面を切っても、上端3pxの地面断面は不透明に残します。
                    for (int y = 109; y < 112; y++)
                        Assert.That(readback.GetPixel(24, y).r, Is.EqualTo(1f).Within(0.005f));
                }
                finally { Object.DestroyImmediate(bodyMaterial); Object.DestroyImmediate(faceMaterial); }
            }
        }

        /// <summary>報告形状の岸段差で、水色単独の縦帯と、投影範囲外の断面を別々に検出します。</summary>
        /// <param name="difference">奥岸Yの差です。</param>
        /// <param name="origin">描画原点です。</param>
        [TestCase(1, 0)]
        [TestCase(5, 0)]
        [TestCase(9, 0)]
        [TestCase(5, -64)]
        public void SteppedBank_HasUniformWaterAndNoBorrowedFace(int difference, int origin)
        {
            using (var owner = new NativeArray<GeneratedTerrainCell>(4096, Allocator.Temp))
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                var terrain = owner;
                var ids = new BuiltInTerrainBlockRuntimeIds { SaltWater = new DefinitionRuntimeId(102) };
                int lowShore = 50 - difference;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        terrain[y * 64 + x] = y < (x <= 20 ? 50 : lowShore)
                            ? new GeneratedTerrainCell { LiquidRuntimeId = ids.SaltWater }
                            : new GeneratedTerrainCell { GroundBlockRuntimeId = new DefinitionRuntimeId(201) };
                var n = new CompletedChunkTerrainNeighborhood(default, default, default, default, terrain,
                    default, default, default, default);
                var bodyMaterial = new Material(Shader.Find("Farmware/Terrain/Liquid Depth Overlay"));
                var faceMaterial = new Material(Shader.Find("Farmware/Terrain/Submerged Ground Face"));
                try
                {
                    renderCamera.backgroundColor = Color.black;
                    renderCamera.transform.position += new Vector3(origin, origin, 0);
                    bodyMaterial.SetColor("_SaltWaterColor", Color.black);
                    Tilemap body = CreateTilemap(bodyMaterial);
                    Tilemap face = CreateTilemap(faceMaterial);
                    body.transform.position = face.transform.position = new Vector3(origin, origin, 0);
                    TilemapRenderer faceRenderer = face.GetComponent<TilemapRenderer>();
                    faceRenderer.sortingOrder = 1;
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 3; x++)
                        {
                            int worldY = lowShore - 3 + y;
                            if (worldY >= (x == 0 ? 50 : lowShore)) continue;
                            int stage = TerrainLiquidPresentationUtility.GetClampedWaterDepth(n, 20 + x, worldY, ids);
                            bool clip = stage == 0;
                            var cell = new Vector3Int(x, y, 0);
                            SetWaterDataTile(body, cell, cache.GetOrCreateDepthOverlay(), stage, TerrainLiquidVisualKind.SaltWater, false, clip);
                            if (TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(n, 20 + x, worldY, ids, out var projection))
                            {
                                Assert.That(projection.SourceLocalX, Is.EqualTo(20 + x));
                                SetWaterDataTile(face, cell, cache.GetOrCreateDepthOverlay(), stage, TerrainLiquidVisualKind.SaltWater, false, clip);
                            }
                        }
                    faceRenderer.enabled = false;
                    RenderPixels();
                    // 両列とも水の領域は基底色が完全に同じで、岸のY差が水色の帯を作りません。
                    for (int y = 0; y < 45; y++)
                        for (int x = 0; x < 48; x++)
                            Assert.That(readback.GetPixel(x, y).r, Is.LessThan(0.005f), "水色だけで帯が出ています。");
                    faceRenderer.enabled = true;
                    RenderPixels();
                    for (int y = 0; y < 45; y++)
                    {
                        float leftDepth = (3 + difference) * 16f - y - 0.5f;
                        float expected = 1f - TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(leftDepth, false);
                        // 左列の全16pxを調べ、右岸から借りた断面が境界付近だけ現れる回帰を検出します。
                        for (int x = 0; x < 16; x++)
                            Assert.That(readback.GetPixel(x, y).r, Is.EqualTo(expected).Within(0.012f),
                                $"投影範囲外の断面が出ています: ({x},{y})");
                    }
                }
                finally { Object.DestroyImmediate(bodyMaterial); Object.DestroyImmediate(faceMaterial); }
            }
        }

        /// <summary>同じTileの投影段数だけのDirty更新も描画へ反映されることを保証します。</summary>
        [UnityTest]
        public IEnumerator SubmergedFace_ColorOnlyUpdateAppearsAfterFrame()
        {
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                var material = new Material(Shader.Find("Farmware/Terrain/Submerged Ground Face"));
                try
                {
                    renderCamera.backgroundColor = Color.black;
                    Tilemap map = CreateTilemap(material);
                    TileBase tile = cache.GetOrCreateDepthOverlay();
                    SetWaterDataTile(map, new Vector3Int(1, 1, 0), tile, 1, TerrainLiquidVisualKind.FreshWater, false, false);
                    yield return null;
                    RenderPixels();
                    Assert.That(readback.GetPixel(24, 24).r, Is.InRange(0.77f, 0.79f));
                    SetWaterDataTile(map, new Vector3Int(1, 1, 0), tile, 6, TerrainLiquidVisualKind.FreshWater, false, false);
                    yield return null;
                    RenderPixels();
                    Assert.That(map.GetTile(new Vector3Int(1, 1, 0)), Is.SameAs(tile));
                    Assert.That(readback.GetPixel(24, 24).r, Is.LessThan(0.005f));
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>4倍描画でも水中断面の濃度が素材1pxの内部で変わらず、細かい滑らかな帯へ戻らないことを保証します。</summary>
        [Test]
        public void SubmergedFace_EnlargementKeepsEachSourcePixelUniform()
        {
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                var material = new Material(Shader.Find("Farmware/Terrain/Submerged Ground Face"));
                try
                {
                    renderCamera.backgroundColor = Color.black;
                    // 視野は変えず描画先だけ4倍にし、同じ素材画素に対応する4×4画素を比較します。
                    renderTarget.Release();
                    renderTarget.width = RenderWidth * 4;
                    renderTarget.height = RenderHeight * 4;
                    renderTarget.Create();
                    Object.DestroyImmediate(readback);
                    readback = new Texture2D(RenderWidth * 4, RenderHeight * 4, TextureFormat.RGBAFloat, false, true);
                    Tilemap map = CreateTilemap(material);
                    SetWaterDataTile(map, new Vector3Int(1, 1, 0), cache.GetOrCreateDepthOverlay(),
                        1, TerrainLiquidVisualKind.FreshWater, false, false);
                    RenderPixels();
                    for (int y = 16; y < 32; y++)
                        for (int x = 16; x < 32; x++)
                        {
                            float expected = readback.GetPixel(x * 4, y * 4).r;
                            for (int dy = 0; dy < 4; dy++)
                                for (int dx = 0; dx < 4; dx++)
                                    Assert.That(readback.GetPixel(x * 4 + dx, y * 4 + dy).r,
                                        Is.EqualTo(expected).Within(0.0001f), $"素材画素({x},{y})内で濃度が変化しています。");
                        }
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>製品と同じ一括反映経路で、段数・水種・床・奥岸印を渡します。</summary>
        /// <param name="map">テスト所有Tilemapです。</param>
        /// <param name="cell">書込先です。</param>
        /// <param name="tile">共有SpriteのTileです。</param>
        /// <param name="stage">その列の投影段数です。</param>
        /// <param name="kind">液体種です。</param>
        /// <param name="floor">直下床の有無です。</param>
        /// <param name="clip">実際の奥岸かどうかです。</param>
        private static void SetWaterDataTile(Tilemap map, Vector3Int cell, TileBase tile,
            int stage, TerrainLiquidVisualKind kind, bool floor, bool clip)
        {
            TerrainWaterRenderUtility.ApplyTiles(map, new[] { new TileChangeData(cell, tile,
                TerrainWaterRenderUtility.EncodeCell(kind, stage, floor, clip), Matrix4x4.identity) });
        }

        /// <summary>接地点から投影した反射Meshを実描画し、実水面3pxの下から24pxで消えることを保証します。</summary>
        /// <param name="originY">負座標でも水面を復元できることを確認する表示原点です。</param>
        [TestCase(0)]
        [TestCase(-64)]
        public void ReflectionMesh_StartsAtContactAndFadesAcrossBothStages(int originY)
        {
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                var texture = new Texture2D(1, 1);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                var material = new Material(Shader.Find("Farmware/Terrain/Water Reflection Proxy"));
                try
                {
                    material.SetColor("_Color", Color.black);
                    material.SetFloat("_MaximumAlpha", 1f);
                    material.SetFloat("_WaveAmplitudePixels", 0f);
                    var root = new GameObject("Reflection Test Grid");
                    SceneManager.MoveGameObjectToScene(root, previewScene);
                    root.transform.position = new Vector3(0, originY, 0);
                    var settings = root.AddComponent<MeshRenderer>();
                    settings.sharedMaterial = material;
                    renderCamera.transform.position += new Vector3(0, originY, 0);
                    using (var mesh = new TerrainWaterReflectionMesh(root.transform))
                    {
                        mesh.Begin(0);
                        for (int stage = 0; stage < 2; stage++)
                        {
                            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(
                                new Rect(1, 4 + stage, 1, 1), 4f, 4f - 3f / 16f,
                                new Rect(1, 0, 1, 4f - 3f / 16f), 0.75f, 24f, out var slice), Is.True);
                            mesh.Append(0, sprite, slice, settings);
                        }
                        mesh.Upload(0);
                        RenderPixels();
                    }
                    // 論理上端64pxから3px下の61pxが水面です。反射もそこから始まり、37pxで消えます。
                    for (int y = 35; y <= 64; y++)
                    {
                        float expected = TerrainLiquidPresentationUtility.GetReflectionVisibility(61f - y - 0.5f);
                        Assert.That(1f - readback.GetPixel(24, y).r, Is.EqualTo(expected).Within(0.005f), $"反射行{y}");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(material);
                    Object.DestroyImmediate(sprite);
                    Object.DestroyImmediate(texture);
                }
            }
        }


        /// <summary>地面を1セル挟んだ反射を実描画し、水の上端3pxと陸地へ反射を出さないことを保証します。</summary>
        [Test]
        public void ReflectionMesh_DrawsOnlyTheDistantProjectionWaterIntersection()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Reflection Proxy"));
            Texture2D texture = CreateReflectionTestTexture(false);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            var parent = new GameObject("Clipped Reflection Test");
            SceneManager.MoveGameObjectToScene(parent, previewScene);
            Renderer settings = parent.AddComponent<MeshRenderer>(); settings.sharedMaterial = material;
            try
            {
                material.SetFloat("_WaveAmplitudePixels", 0);
                material.SetFloat("_MaximumAlpha", 1);
                material.SetColor("_Color", Color.black);
                using (var mesh = new TerrainWaterReflectionMesh(parent.transform))
                {
                    Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(1, 3, 1, 1),
                        2, 2f - 3f / 16f, new Rect(1, 0, 1, 13f / 16f), 0.75f, 24f, out var slice), Is.True);
                    mesh.Append(0, sprite, slice, settings); mesh.Upload(0); RenderPixels();
                    for (int y = 0; y < RenderHeight; y++)
                    {
                        for (int x = 0; x < RenderWidth; x++)
                        {
                            float alpha = x >= 16 && x < 32 && y >= 5 && y < 13
                                ? 1f - (29f - y - 0.5f) / 24f : 0f;
                            Assert.That(1f - readback.GetPixel(x, y).r, Is.EqualTo(alpha).Within(0.005f));
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(material); }
        }

        /// <summary>Atlas境界を守りながら画像と透明部分を動かし、コマ保持・場所別の揺れ・原点変更も保証します。</summary>
        [Test]
        public void ReflectionMesh_PreservesSilhouetteAtlasBoundsAndLocalSteppedAnimation()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Reflection Proxy"));
            Texture2D texture = CreateReflectionTestTexture(true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            var parent = new GameObject("Animated Reflection Test");
            SceneManager.MoveGameObjectToScene(parent, previewScene);
            Renderer settings = parent.AddComponent<MeshRenderer>(); settings.sharedMaterial = material;
            try
            {
                material.SetColor("_ReflectionTint", Color.white);
                material.SetFloat("_MaximumAlpha", 1);
                material.SetFloat("_WaveAmplitudePixels", 2);
                using (var mesh = new TerrainWaterReflectionMesh(parent.transform))
                {
                    // 3列・上下2画像を共通の絶対座標場へ置き、Spriteやセルごとの再生ずれを避けます。
                    for (int x = 0; x < 3; x++)
                    {
                        for (int stage = 0; stage < 2; stage++)
                        {
                            TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(x, 4 + stage, 1, 1),
                                4f, 4f - 3f / 16f, new Rect(0, 0, 3, 61f / 16f), 0.75f, 24f, out var slice, 2);
                            mesh.Append(0, sprite, slice, settings);
                        }
                    }
                    mesh.Upload(0);
                    material.SetFloat("_AnimationTimeSeconds", 1.01f); RenderPixels();
                    Color[] first = readback.GetPixels();
                    material.SetFloat("_AnimationTimeSeconds", 1.49f); RenderPixels();
                    Assert.That(readback.GetPixels(), Is.EqualTo(first), "2fpsの保持中に画像が変わっています。");
                    // 4096px周期の倍数ではない負原点を使い、原点補正を省く不具合も検出します。
                    parent.transform.position = new Vector3(65, 67, 0);
                    renderCamera.transform.position += new Vector3(65, 67, 0);
                    material.SetVector("_WorldPixelOffset", new Vector4(
                        TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(-65),
                        TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(-67), 0, 0));
                    RenderPixels();
                    Assert.That(readback.GetPixels(), Is.EqualTo(first), "原点変更で揺れが抽選し直されています。");
                    bool mixed = false;
                    Color[] previous = first;
                    for (int frame = 4; frame < 40; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.5f); RenderPixels();
                        Color[] next = readback.GetPixels();
                        bool increase = false; bool decrease = false;
                        for (int y = 37; y < 61; y++)
                        {
                            for (int x = 0; x < RenderWidth; x++)
                            {
                                int i = y * RenderWidth + x;
                                // 隣の赤いSpriteが混入するとR/Gが異なります。元画像は灰色だけです。
                                Assert.That(next[i].r, Is.EqualTo(next[i].g).Within(0.0001f));
                                if (x == 15 || x == 16 || x == 31 || x == 32)
                                {
                                    // 元Spriteの外を透明にしても、隣の画像が同じ逆変位で引き継ぎ、内部境界を欠かしません。
                                    float visibility = 1f - (61f - y - 0.5f) / 24f;
                                    Assert.That(next[i].r, Is.LessThan(1f - visibility * 0.1f), "内部タイル境界に反射の欠けがあります。");
                                }
                                if (y >= 59) Assert.That(next[i].r, Is.EqualTo(first[i].r).Within(0.0001f), "反射の付け根が動いています。");
                                increase |= next[i].r > previous[i].r + 0.001f;
                                decrease |= next[i].r < previous[i].r - 0.001f;
                            }
                        }
                        mixed |= increase && decrease; previous = next;
                    }
                    Assert.That(mixed, Is.True, "全領域が同じ変位で同期しています。");
                }
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(material); }
        }

        /// <summary>実設定の濃度でも外周と透明な穴が一緒に動き、水域・固定した付け根を越えないことを保証します。</summary>
        /// <param name="amplitude">最大変位です。採用値1pxと調整上限2pxを検査します。</param>
        /// <param name="narrowWater">左右が陸地の1セル幅へ切り抜く場合はtrueです。</param>
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        public void ReflectionMesh_MovesWholeOutlineAndHolesWithinWater(int amplitude, bool narrowWater)
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Reflection Proxy"));
            Texture2D texture = CreateReflectionTestTexture(true);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            var parent = new GameObject("Whole Reflection Outline Test");
            SceneManager.MoveGameObjectToScene(parent, previewScene);
            Renderer settings = parent.AddComponent<MeshRenderer>(); settings.sharedMaterial = material;
            try
            {
                // 黒で輪郭だけを読み取り、実採用の低濃度でもAlphaが時間変化していることを検査します。
                material.SetColor("_Color", Color.black);
                material.SetFloat("_MaximumAlpha", 0.22f);
                material.SetFloat("_WaveAmplitudePixels", amplitude);
                int firstWaterPixel = narrowWater ? 16 : 0;
                int lastWaterPixel = narrowWater ? 32 : 48;
                using (var mesh = new TerrainWaterReflectionMesh(parent.transform))
                {
                    Rect water = new Rect(firstWaterPixel / 16f, 0, (lastWaterPixel - firstWaterPixel) / 16f, 61f / 16f);
                    for (int stage = 0; stage < 2; stage++)
                    {
                        Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(1, 4 + stage, 1, 1),
                            4, 61f / 16f, water, 0.75f, 24, out var slice, 2), Is.True);
                        mesh.Append(0, sprite, slice, settings);
                    }
                    mesh.Upload(0);
                    bool expanded = false;
                    bool retreated = false;
                    bool movedHole = false;
                    Color[] first = null;
                    bool changedOutline = false;
                    for (int frame = 0; frame < 32; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.5f); RenderPixels();
                        Color[] pixels = readback.GetPixels();
                        for (int y = 35; y < 64; y++)
                        {
                            float expectedAlpha = 0.22f * TerrainLiquidPresentationUtility.GetReflectionVisibility(61f - y - 0.5f);
                            for (int x = 0; x < RenderWidth; x++)
                            {
                                int i = y * RenderWidth + x;
                                float alpha = 1f - pixels[i].r;
                                bool inWater = x >= firstWaterPixel && x < lastWaterPixel && y >= 37 && y < 61;
                                if (!inWater)
                                    Assert.That(alpha, Is.EqualTo(0).Within(0.0001f), "輪郭の揺れが陸地または上端3pxへ漏れています。");
                                if (!inWater) continue;
                                // 隣画像との重複で2回合成された場合も検出します。輪郭内は元の濃度、外は透明です。
                                Assert.That(alpha, Is.EqualTo(0).Within(0.0001f).Or.EqualTo(expectedAlpha).Within(0.0001f));
                                if (y >= 59)
                                    Assert.That(alpha, Is.EqualTo(x >= 16 && x < 32 && x != 23 && x != 24 ? expectedAlpha : 0)
                                        .Within(0.0001f), "反射の付け根が動いています。");
                                expanded |= (x < 16 || x >= 32) && alpha > 0.0001f;
                                retreated |= (x == 16 || x == 31) && alpha < 0.0001f;
                                movedHole |= (x == 23 || x == 24) && alpha > 0.0001f;
                                if (first != null && (x <= 17 || x >= 30))
                                    changedOutline |= Mathf.Abs(first[i].r - pixels[i].r) > 0.0001f;
                            }
                        }
                        if (first == null) first = pixels;
                    }
                    Assert.That(changedOutline && retreated && movedHole, Is.True,
                        "画像内部だけでなく、外周と透明な穴も時間に応じて動く必要があります。");
                    Assert.That(expanded, Is.EqualTo(!narrowWater), "開いた水面側だけへ輪郭を広げる必要があります。");
                }
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(material); }
        }

        /// <summary>岸の高さが違う隣接画像も受け手の変位を共有し、境界で欠けたり二重合成されたりしないことを保証します。</summary>
        [Test]
        public void ReflectionMesh_DifferentContactsShareReceiverDisplacementAtSeam()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Reflection Proxy"));
            Texture2D texture = CreateReflectionTestTexture(false);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
            var parent = new GameObject("Reflection Contact Seam Test");
            SceneManager.MoveGameObjectToScene(parent, previewScene);
            Renderer settings = parent.AddComponent<MeshRenderer>(); settings.sharedMaterial = material;
            try
            {
                material.SetColor("_Color", Color.black);
                material.SetFloat("_MaximumAlpha", 1);
                material.SetFloat("_WaveAmplitudePixels", 2);
                using (var mesh = new TerrainWaterReflectionMesh(parent.transform))
                {
                    for (int x = 0; x < 2; x++)
                    {
                        float contact = 4 + x;
                        for (int stage = 0; stage < 2; stage++)
                        {
                            if (TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(x, contact + stage, 1, 1),
                                contact, contact - 3f / 16f, new Rect(0, 0, 3, 61f / 16f),
                                0.75f, 24, out var slice, 2))
                                mesh.Append(0, sprite, slice, settings, 61f / 16f);
                        }
                    }
                    mesh.Upload(0);
                    for (int frame = 0; frame < 32; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.5f); RenderPixels();
                        // この行範囲には両方の像が届きますが、画素を担当する元画像はどちらか1枚だけです。
                        for (int y = 54; y < 61; y++)
                        {
                            float leftAlpha = 1f - (61f - y - 0.5f) / 24f;
                            float rightAlpha = 1f - (77f - y - 0.5f) / 24f;
                            for (int x = 14; x <= 17; x++)
                                Assert.That(1f - readback.GetPixel(x, y).r,
                                    Is.EqualTo(leftAlpha).Within(0.0001f).Or.EqualTo(rightAlpha).Within(0.0001f),
                                    "接地点の違いで画像間に穴または二重合成が生じています。");
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(material); }
        }

        /// <summary>反射テスト用の灰色画像と、混入検出用の隣接する赤画像を作ります。</summary>
        /// <param name="pattern">灰色勾配と透明な縦穴を含める場合はtrueです。</param>
        /// <returns>呼出側が破棄する、Point Sampleの32×16 Textureです。</returns>
        private static Texture2D CreateReflectionTestTexture(bool pattern)
        {
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 32; x++)
                    texture.SetPixel(x, y, x >= 16 ? Color.red : new Color(pattern ? 0.2f + 0.6f * x / 15f : 1f,
                        pattern ? 0.2f + 0.6f * x / 15f : 1f, pattern ? 0.2f + 0.6f * x / 15f : 1f,
                        pattern && x >= 7 && x <= 8 ? 0f : 1f));
            texture.Apply(); return texture;
        }

        /// <summary>隔離SceneへGridと描画Tilemapを追加し、指定した製品Materialだけを使います。</summary>
        /// <param name="material">検査するShaderを持つ一時Materialです。</param>
        /// <returns>テスト終了時にPreview Sceneとともに破棄されるTilemapです。</returns>
        private Tilemap CreateTilemap(Material material)
        {
            var gridObject = new GameObject("Liquid Rendering Test Grid", typeof(Grid));
            SceneManager.MoveGameObjectToScene(gridObject, previewScene);
            var tilemapObject = new GameObject("Liquid Rendering Test Tilemap", typeof(Tilemap), typeof(TilemapRenderer));
            tilemapObject.transform.SetParent(gridObject.transform, false);
            tilemapObject.GetComponent<TilemapRenderer>().sharedMaterial = material;
            return tilemapObject.GetComponent<Tilemap>();
        }

        /// <summary>全127マスクを実Tilemapで描き、縦岸・奥岸・内角・裏側終端の画素がCPU契約と一致することを保証します。</summary>
        [Test]
        public void ShorelineTilemap_AllShapesMatchPixelContract()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    // 不透明な黒線と白背景で形状だけを検査し、時間による明暗を判定から除外します。
                    material.SetFloat("_BaseOpacity", 1f);
                    material.SetFloat("_HighlightOpacity", 1f);
                    Tilemap tilemap = CreateTilemap(material);
                    for (int mask = 1; mask <= TerrainLiquidPresentationUtility.MaximumEdgeMask; mask++)
                    {
                        tilemap.SetTile(new Vector3Int(1, 3, 0),
                            cache.GetOrCreateShoreline(Color.black, (TerrainLiquidEdgeMask)mask));
                        RenderPixels();
                        for (int y = 0; y < 16; y++)
                        {
                            for (int x = 0; x < 16; x++)
                            {
                                float expected = TerrainLiquidPresentationUtility.IsShorelinePixel(
                                    (TerrainLiquidEdgeMask)mask, x, y) ? 0f : 1f;
                                Assert.That(readback.GetPixel(16 + x, 48 + y).r,
                                    Is.EqualTo(expected).Within(0.005f), $"mask={mask} pixel=({x},{y})");
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>通常描画と負側への原点変更後で、同じ絶対地点の水際明暗が一致することを保証します。</summary>
        [Test]
        public void ShorelineTilemap_FloatingOriginPreservesAbsolutePattern()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    material.SetFloat("_AnimationTimeSeconds", 0.75f);
                    Tilemap tilemap = CreateTilemap(material);
                    tilemap.SetTile(new Vector3Int(1, 3, 0), cache.GetOrCreateShoreline(
                        Color.black, TerrainLiquidEdgeMask.Left | TerrainLiquidEdgeMask.Right));
                    RenderPixels();
                    Color[] original = readback.GetPixels();

                    // 同じ絶対地形を相対座標+64へ移し、原点-64の補正をMaterialへ渡します。
                    tilemap.transform.parent.position = new Vector3(64f, 0f, 0f);
                    renderCamera.transform.position += new Vector3(64f, 0f, 0f);
                    material.SetVector("_WorldPixelOffset", new Vector4(
                        TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(-64), 0f, 0f, 0f));
                    RenderPixels();
                    Color[] rebased = readback.GetPixels();
                    for (int index = 0; index < original.Length; index++)
                    {
                        Assert.That(rebased[index].r, Is.EqualTo(original[index].r).Within(0.005f));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>透明を含む水際4階調と、0.25秒のコマ間の保持を実描画で保証します。</summary>
        [Test]
        public void ShorelineTilemap_UsesFourOpacityLevelsAndHoldsQuarterSecondFrames()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    CreateShorelineTestTilemap(material, cache);
                    material.SetFloat("_AnimationTimeSeconds", 0f);
                    RenderPixels();
                    Color[] firstFrame = readback.GetPixels();
                    material.SetFloat("_AnimationTimeSeconds", 0.249f);
                    RenderPixels();
                    Color[] heldFrame = readback.GetPixels();
                    for (int index = 0; index < firstFrame.Length; index++)
                    {
                        Assert.That(heldFrame[index].r,
                            Is.EqualTo(firstFrame[index].r).Within(0.0001f), "コマ間で明暗が補間されています。");
                    }

                    // 黒線＋白背景では1-Rが濃度です。複数コマの全画素を調べ、中間濃度の混入を検出します。
                    var observedLevels = new bool[4];
                    bool changedFrame = false;
                    float minimum = material.GetFloat("_BaseOpacity");
                    float maximum = material.GetFloat("_HighlightOpacity");
                    Assert.That(minimum, Is.EqualTo(0f), "初期値では最も弱い階調を透明にします。");
                    Assert.That(maximum, Is.EqualTo(0.9f).Within(0.001f));
                    // 基準3秒±25%の最長周期も含め、局所的な波の全階調を観測します。
                    for (int frame = 0; frame < 24; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.25f);
                        RenderPixels();
                        Color[] pixels = readback.GetPixels();
                        for (int index = 0; index < pixels.Length; index++)
                        {
                            float opacity = 1f - pixels[index].r;
                            // 色だけでは透明な水際と背景を区別できないため、静止形状の契約で画素を選びます。
                            // 透明な水際も0段目として数え、残る3階調だけで成功扱いにしません。
                            bool onShoreline = TerrainLiquidPresentationUtility.IsShorelinePixel(
                                TerrainLiquidEdgeMask.Back | TerrainLiquidEdgeMask.Left | TerrainLiquidEdgeMask.Right,
                                index % RenderWidth % 16, index / RenderWidth % 16);
                            if (!onShoreline)
                            {
                                Assert.That(opacity, Is.EqualTo(0f).Within(0.001f));
                                continue;
                            }

                            float level = (opacity - minimum) / (maximum - minimum) * 3f;
                            int nearestLevel = Mathf.RoundToInt(level);
                            Assert.That(nearestLevel, Is.InRange(0, 3));
                            Assert.That(level, Is.EqualTo(nearestLevel).Within(0.01f), "水際へ中間階調が混入しています。");
                            observedLevels[nearestLevel] = true;
                            changedFrame |= Mathf.Abs(pixels[index].r - firstFrame[index].r) > 0.01f;
                        }
                    }

                    Assert.That(observedLevels, Is.All.True, "4階調の一部が描画されていません。");
                    Assert.That(changedFrame, Is.True, "時刻を進めても水際が更新されていません。");
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>同じコマで強まる画素と弱まる画素が混在し、岸全体が同時に寄せ引きしないことを保証します。</summary>
        [Test]
        public void ShorelineTilemap_LocalWavesBrightenAndDimInTheSameFrame()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    CreateShorelineTestTilemap(material, cache);
                    material.SetFloat("_AnimationTimeSeconds", 0f);
                    RenderPixels();
                    Color[] previous = readback.GetPixels();
                    int mixedFrames = 0;
                    for (int frame = 1; frame <= 24; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.25f);
                        RenderPixels();
                        Color[] current = readback.GetPixels();
                        bool becameStronger = false;
                        bool becameWeaker = false;
                        for (int index = 0; index < current.Length; index++)
                        {
                            // 黒線＋白背景なので、Rが下がれば線が濃くなり、Rが上がれば薄くなっています。
                            float difference = current[index].r - previous[index].r;
                            becameStronger |= difference < -0.01f;
                            becameWeaker |= difference > 0.01f;
                        }

                        if (becameStronger && becameWeaker)
                        {
                            mixedFrames++;
                        }

                        previous = current;
                    }

                    Assert.That(mixedFrames, Is.GreaterThanOrEqualTo(6),
                        "大半の波が同じタイミングで強弱変化しています。固定中心ごとの位相・周期を確認してください。");
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>全岸共通だった6秒周期へ戻らず、場所ごとの周期差が実描画へ反映されることを保証します。</summary>
        [Test]
        public void ShorelineTilemap_LocalPeriodsDoNotRepeatTheOldGlobalSixSecondCycle()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    CreateShorelineTestTilemap(material, cache);
                    material.SetFloat("_AnimationTimeSeconds", 0f);
                    RenderPixels();
                    Color[] original = readback.GetPixels();
                    material.SetFloat("_AnimationTimeSeconds", 6f);
                    RenderPixels();
                    Color[] afterSixSeconds = readback.GetPixels();
                    int changedPixels = 0;
                    for (int index = 0; index < original.Length; index++)
                    {
                        if (Mathf.Abs(afterSixSeconds[index].r - original[index].r) > 0.01f)
                        {
                            changedPixels++;
                        }
                    }

                    Assert.That(changedPixels, Is.GreaterThan(16),
                        "全水際が同じ6秒周期へ戻っています。局所波ごとの周期差が必要です。");
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>4096px周期の境界でも隣接中心の位相が一致し、岸線の濃度が突然飛ばないことを保証します。</summary>
        [Test]
        public void ShorelineTilemap_LocalWavesRemainContinuousAcrossPatternWrap()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    CreateShorelineTestTilemap(material, cache);
                    // 描画先x=15/16が絶対画素4095/4096をまたぐ配置にし、周期端の中心ID循環を検査します。
                    material.SetVector("_WorldPixelOffset", new Vector4(4080f, 0f, 0f, 0f));
                    for (int frame = 0; frame < 24; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.25f);
                        RenderPixels();
                        for (int y = 0; y < RenderHeight; y++)
                        {
                            float difference = Mathf.Abs(readback.GetPixel(15, y).r - readback.GetPixel(16, y).r);
                            Assert.That(difference, Is.LessThanOrEqualTo(0.301f),
                                "周期境界で1階調を超える濃度差があります。隣接する中心IDの循環を確認してください。");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>4倍解像度でも素材の1px内は同じ値になり、画面画素へ滑らかな勾配を描かないことを保証します。</summary>
        [Test]
        public void ShorelineTilemap_EnlargementKeepsEachSourcePixelUniform()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Liquid Shoreline"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    // Cameraの範囲は変えず描画先だけ4倍にし、素材1pxに相当する4×4画面画素を検査します。
                    renderTarget.Release();
                    renderTarget.width = RenderWidth * 4;
                    renderTarget.height = RenderHeight * 4;
                    renderTarget.Create();
                    Object.DestroyImmediate(readback);
                    readback = new Texture2D(RenderWidth * 4, RenderHeight * 4,
                        TextureFormat.RGBAFloat, false, true);
                    CreateShorelineTestTilemap(material, cache);
                    material.SetFloat("_AnimationTimeSeconds", 1.25f);
                    RenderPixels();
                    for (int y = 0; y < RenderHeight; y++)
                    {
                        for (int x = 0; x < RenderWidth; x++)
                        {
                            float expected = readback.GetPixel(x * 4, y * 4).r;
                            for (int dy = 0; dy < 4; dy++)
                            {
                                for (int dx = 0; dx < 4; dx++)
                                {
                                    Assert.That(readback.GetPixel(x * 4 + dx, y * 4 + dy).r,
                                        Is.EqualTo(expected).Within(0.0001f), $"素材画素({x},{y})内が均一ではありません。");
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>静止した短い筋が疎で、16px境界を跨ぎ、陸地に相当する空セルへ出ないことを実描画で保証します。</summary>
        [Test]
        public void SurfacePatternTilemap_StaticMarksCrossTileEdgesAndRespectWaterMask()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Surface Pattern"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    material.SetFloat("_Animate", 0f);
                    material.SetFloat("_Density", 1f);
                    material.SetFloat("_MinimumLengthPixels", 8f);
                    material.SetFloat("_MaximumLengthPixels", 8f);
                    Tilemap map = CreateSurfacePatternTestTilemap(material, cache);
                    RenderPixels();
                    Color[] full = readback.GetPixels();
                    int count = 0;
                    bool crossesEdge = false;
                    for (int y = 0; y < RenderHeight; y++)
                    {
                        for (int x = 0; x < RenderWidth; x++)
                        {
                            if (full[y * RenderWidth + x].r < 0.99f) count++;
                            if (x > 0 && x % 16 == 0 && full[y * RenderWidth + x].r < 0.99f
                                && full[y * RenderWidth + x - 1].r < 0.99f) crossesEdge = true;
                        }
                    }

                    Assert.That(count, Is.InRange(10, RenderWidth * RenderHeight / 20), "線を敷き詰めず疎に配置します。");
                    Assert.That(crossesEdge, Is.True, "16px境界で筋模様が分割されています。");
                    // 水域の全面Tileを取り去り、以前の模様が残らないことも確認します。
                    map.ClearAllTiles();
                    RenderPixels();
                    foreach (Color pixel in readback.GetPixels()) Assert.That(pixel.r, Is.EqualTo(1f).Within(0.0001f));
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>最大形状を実描画し、長さは不規則でも、折れ・中央の欠けのない厚さ1pxの水平線だけになることを保証します。</summary>
        [Test]
        public void SurfacePatternTilemap_UsesContinuousSinglePixelHorizontalLines()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Surface Pattern"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    material.SetFloat("_Animate", 0f);
                    material.SetFloat("_Density", 1f);
                    material.SetFloat("_MinimumLengthPixels", 3f);
                    material.SetFloat("_MaximumLengthPixels", 8f);
                    material.SetColor("_FreshColor", Color.black);
                    material.SetFloat("_MaximumOpacity", 1f);
                    CreateSurfacePatternTestTilemap(material, cache);

                    // 位置・長さの既存抽選から、水平線だけを塗った期待画像を作ります。
                    // 画面外の中心から入る線も含め、タイルの端や画面の端で誤って形状を限定しません。
                    var expected = new bool[RenderWidth * RenderHeight];
                    var selectedLengths = new bool[9];
                    for (int nodeY = -1; nodeY <= 4; nodeY++)
                    {
                        for (int nodeX = -1; nodeX <= 2; nodeX++)
                        {
                            // 16bit乱数を2048で割ると、32px格子内の整数位置0〜31になります。
                            int centerX = nodeX * 32 + (int)((GetSurfacePatternTestHash(nodeX, nodeY, 0x02e5be93u) & 65535u) / 2048u);
                            int centerY = nodeY * 32 + (int)((GetSurfacePatternTestHash(nodeX, nodeY, 0x967a889bu) & 65535u) / 2048u);
                            int length = 3 + (int)((GetSurfacePatternTestHash(nodeX, nodeY, 0x13a5ba1du) & 65535u) * 6u / 65536u);
                            for (int offset = 0; offset < length; offset++)
                            {
                                int x = centerX - length / 2 + offset;
                                if (x >= 0 && x < RenderWidth && centerY >= 0 && centerY < RenderHeight)
                                {
                                    expected[centerY * RenderWidth + x] = true;
                                    selectedLengths[length] = true;
                                }
                            }
                        }
                    }

                    RenderPixels();
                    Color[] pixels = readback.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Assert.That(pixels[i].r, Is.EqualTo(expected[i] ? 0f : 1f).Within(0.0001f),
                            $"素材画素({i % RenderWidth},{i / RenderWidth})が連続した1px水平線と一致しません。");
                    }

                    int lengthCount = 0;
                    foreach (bool selected in selectedLengths)
                    {
                        if (selected) lengthCount++;
                    }
                    Assert.That(lengthCount, Is.GreaterThan(1), "直線化しても長さのばらつきを維持します。");
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>GPUと同じ固定候補IDから乱数を求め、線の位置・長さの期待画像を作るテスト専用計算です。</summary>
        /// <param name="x">32px候補格子のX座標です。</param>
        /// <param name="y">32px候補格子のY座標です。</param>
        /// <param name="salt">位置・長さの抽選を独立させる用途別の固定値です。</param>
        /// <returns>負座標を含め128格子周期で一致する32bit値です。状態は変更しません。</returns>
        private static uint GetSurfacePatternTestHash(int x, int y, uint salt)
        {
            // GPUの符号なし整数演算と同じ桁あふれを許可し、32bit値を混ぜます。
            unchecked
            {
                uint value = ((uint)x & 127u) * 0x9e3779b9u ^ ((uint)y & 127u) * 0x85ebca6bu ^ salt;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }

        /// <summary>元画素を4倍表示しても画素内の濃度を補間せず、静止位置を保つことを保証します。</summary>
        [Test]
        public void SurfacePatternTilemap_EnlargementKeepsNativePixelsUniform()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Surface Pattern"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    material.SetFloat("_Animate", 0f);
                    material.SetFloat("_Density", 1f);
                    CreateSurfacePatternTestTilemap(material, cache);
                    RenderPixels();
                    Color[] nativePixels = readback.GetPixels();
                    renderTarget.Release();
                    renderTarget.width = RenderWidth * 4;
                    renderTarget.height = RenderHeight * 4;
                    renderTarget.Create();
                    Object.DestroyImmediate(readback);
                    readback = new Texture2D(RenderWidth * 4, RenderHeight * 4, TextureFormat.RGBAFloat, false, true);
                    RenderPixels();
                    for (int y = 0; y < RenderHeight * 4; y++)
                    {
                        for (int x = 0; x < RenderWidth * 4; x++)
                        {
                            Assert.That(readback.GetPixel(x, y).r,
                                Is.EqualTo(nativePixels[y / 4 * RenderWidth + x / 4].r).Within(0.0001f));
                        }
                    }
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>大きな負原点へ移しても模様が一致し、実際の奥岸の上端3pxだけを隠すことを保証します。</summary>
        [Test]
        public void SurfacePatternTilemap_OriginAndNorthBankClippingPreservePattern()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Surface Pattern"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    material.SetFloat("_Animate", 0f);
                    material.SetFloat("_Density", 1f);
                    Tilemap map = CreateSurfacePatternTestTilemap(material, cache);
                    RenderPixels();
                    Color[] original = readback.GetPixels();
                    // 周期の倍数を避け、原点補正が0でなくても一致することを検査します。
                    Vector3 shift = new Vector3(65533f, 65531f, 0f);
                    map.transform.position = shift;
                    renderCamera.transform.position += shift;
                    material.SetVector("_WorldPixelOffset", new Vector4(
                        TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(-65533),
                        TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(-65531), 0f, 0f));
                    RenderPixels();
                    Color[] rebased = readback.GetPixels();
                    for (int i = 0; i < original.Length; i++)
                        Assert.That(rebased[i].r, Is.EqualTo(original[i].r).Within(0.0001f));

                    // 各画素行に必ず模様が通るよう原点をずらし、クリップ対象の有無を両方検査します。
                    map.transform.position = Vector3.zero;
                    renderCamera.transform.position -= shift;
                    int removed = 0;
                    for (int offset = 0; offset < 16; offset++)
                    {
                        material.SetVector("_WorldPixelOffset", new Vector4(0f, offset, 0f, 0f));
                        SetWaterDataTile(map, new Vector3Int(1, 3, 0), cache.GetOrCreateDepthOverlay(),
                            0, TerrainLiquidVisualKind.FreshWater, false, false);
                        RenderPixels();
                        Color[] unclipped = readback.GetPixels();
                        SetWaterDataTile(map, new Vector3Int(1, 3, 0), cache.GetOrCreateDepthOverlay(),
                            0, TerrainLiquidVisualKind.FreshWater, false, true);
                        RenderPixels();
                        Color[] clipped = readback.GetPixels();
                        for (int y = 0; y < RenderHeight; y++)
                        {
                            for (int x = 0; x < RenderWidth; x++)
                            {
                                int i = y * RenderWidth + x;
                                bool top = x >= 16 && x < 32 && y >= 61 && y < 64;
                                Assert.That(clipped[i].r, Is.EqualTo(top ? 1f : unclipped[i].r).Within(0.0001f));
                                if (top && unclipped[i].r < 0.99f) removed++;
                            }
                        }
                    }
                    Assert.That(removed, Is.GreaterThan(0), "実際に模様が通った上端を検査する必要があります。");
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>同じ低FPSコマを保持し、出現・消失が同時刻に混在して全域が同期しないことを保証します。</summary>
        [Test]
        public void SurfacePatternTilemap_HoldsFramesAndAnimatesLocalMarksIndependently()
        {
            var material = new Material(Shader.Find("Farmware/Terrain/Water Surface Pattern"));
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                try
                {
                    material.SetFloat("_Density", 1f);
                    CreateSurfacePatternTestTilemap(material, cache);
                    material.SetFloat("_AnimationTimeSeconds", 1.01f);
                    RenderPixels();
                    Color[] first = readback.GetPixels();
                    material.SetFloat("_AnimationTimeSeconds", 1.49f);
                    RenderPixels();
                    Color[] held = readback.GetPixels();
                    Assert.That(held, Is.EqualTo(first), "2fpsのコマ内で状態が変化しています。");
                    bool mixed = false;
                    Color[] previous = first;
                    for (int frame = 4; frame < 28; frame++)
                    {
                        material.SetFloat("_AnimationTimeSeconds", frame * 0.5f);
                        RenderPixels();
                        Color[] next = readback.GetPixels();
                        bool brighten = false;
                        bool dim = false;
                        for (int i = 0; i < next.Length; i++)
                        {
                            brighten |= next[i].r < previous[i].r - 0.001f;
                            dim |= next[i].r > previous[i].r + 0.001f;
                        }
                        mixed |= brighten && dim;
                        previous = next;
                    }
                    Assert.That(mixed, Is.True, "模様全体の成長・減衰が同期しています。");
                }
                finally { Object.DestroyImmediate(material); }
            }
        }

        /// <summary>水面模様のGPU検証用に淡水の全面Tileを3×8セルへ配置します。</summary>
        /// <param name="material">テスト所有の模様Materialです。</param>
        /// <param name="cache">共有全面Tileの所有者です。</param>
        /// <returns>空セル化・原点移動を検査できるテスト所有Tilemapです。</returns>
        private Tilemap CreateSurfacePatternTestTilemap(Material material, TerrainLiquidRuntimeTileCache cache)
        {
            Tilemap map = CreateTilemap(material);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 3; x++)
                    SetWaterDataTile(map, new Vector3Int(x, y, 0), cache.GetOrCreateDepthOverlay(),
                        6, TerrainLiquidVisualKind.FreshWater, false, false);
            return map;
        }

        /// <summary>明暗テストのため、描画範囲全体へ奥岸・縦岸を持つ黒い水際を準備します。</summary>
        /// <param name="material">テスト所有の水際Materialです。</param>
        /// <param name="cache">テスト終了時に破棄する共有Tileの所有者です。</param>
        private void CreateShorelineTestTilemap(Material material, TerrainLiquidRuntimeTileCache cache)
        {
            Tilemap tilemap = CreateTilemap(material);
            TileBase tile = cache.GetOrCreateShoreline(Color.black,
                TerrainLiquidEdgeMask.Back | TerrainLiquidEdgeMask.Left | TerrainLiquidEdgeMask.Right);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        /// <summary>現在のURPで隔離Cameraを同期描画し、GPU結果を現在の解像度のCPU Textureへ読み戻します。</summary>
        private void RenderPixels()
        {
            var request = new RenderPipeline.StandardRequest { destination = renderTarget };
            Assert.That(RenderPipeline.SupportsRenderRequest(renderCamera, request), Is.True,
                "現在のRender Pipelineでテスト用Cameraの描画要求が使えません。");
            RenderPipeline.SubmitRenderRequest(renderCamera, request);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTarget;
                readback.ReadPixels(new Rect(0, 0, renderTarget.width, renderTarget.height), 0, 0);
                readback.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
