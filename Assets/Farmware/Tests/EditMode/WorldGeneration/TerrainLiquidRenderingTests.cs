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

        /// <summary>反射2枚の圧縮と頂点段数を実描画し、実水面3pxの下から24pxで消えることを保証します。</summary>
        /// <param name="originY">負座標でも水面を復元できることを確認する表示原点です。</param>
        [TestCase(0)]
        [TestCase(-64)]
        public void ReflectionTilemap_StartsAtWaterAndFadesAcrossBothStages(int originY)
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
                    Tilemap map = CreateTilemap(material);
                    map.transform.position = new Vector3(0, originY, 0);
                    renderCamera.transform.position += new Vector3(0, originY, 0);
                    var selection = new TerrainMaterialProductTileSelection(sprite, Color.white);
                    map.SetTile(new Vector3Int(1, 3, 0), cache.GetOrCreateReflection(selection, 0));
                    map.SetTile(new Vector3Int(1, 2, 0), cache.GetOrCreateReflection(selection, 1));
                    RenderPixels();
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
