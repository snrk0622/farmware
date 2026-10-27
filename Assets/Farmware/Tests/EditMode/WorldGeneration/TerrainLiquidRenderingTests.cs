// このファイルは、製品のTilemapとShaderを実際にGPU描画し、水深勾配と水中断面の連続性を検証します。
// CPU側の計算だけでは検出できないLinear色空間への変換や、乾燥崖Shaderの誤適用を画素読戻しで検出します。

using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
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

        /// <summary>実Tilemapで0〜96pxの全画素を描き、段開始値の色変換や16px境界の濃度リセットを検出します。</summary>
        /// <param name="hasFloor">床あり45px/50%、床なし93px/100%のどちらを検査するかです。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void DepthTilemap_RendersContinuousAlphaAcrossAllStages(bool hasFloor)
        {
            var cache = new Farmware.WorldGeneration.Presentation.TerrainLiquidRuntimeTileCache();
            Material source = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidDepthWithoutFloor.mat");
            var material = new Material(source);
            try
            {
                float maximumAlpha = hasFloor ? 0.5f : 1f;
                float fadePixels = hasFloor ? 45f : 93f;
                material.SetFloat("_MaximumAlpha", maximumAlpha);
                material.SetFloat("_FadePixels", fadePixels);
                // 白い下地へ黒を合成すると、出力R=1-Alphaとなり見た目色の調整と独立して勾配を測れます。
                material.SetColor("_FreshWaterColor", Color.black);
                material.SetColor("_SaltWaterColor", Color.black);
                Tilemap tilemap = CreateTilemap(material);
                for (int stage = 0; stage <= 6; stage++)
                {
                    float alpha = Farmware.WorldGeneration.Presentation.TerrainLiquidPresentationUtility
                        .GetDepthStageStartAlpha(stage, hasFloor);
                    TileBase tile = cache.GetOrCreateDepthOverlay(
                        Farmware.WorldGeneration.Presentation.TerrainLiquidVisualKind.FreshWater,
                        alpha, alpha, alpha);
                    for (int x = 0; x < 3; x++)
                    {
                        tilemap.SetTile(new Vector3Int(x, 6 - stage, 0), tile);
                    }
                }

                RenderPixels();
                for (int depth = 3; depth < 112; depth++)
                {
                    float expectedAlpha = Mathf.Clamp01((depth + 0.5f - 3f) / fadePixels) * maximumAlpha;
                    float actualAlpha = 1f - readback.GetPixel(24, 111 - depth).r;
                    Assert.That(actualAlpha, Is.EqualTo(expectedAlpha).Within(0.015f),
                        $"穴上端から{depth}pxで濃度が不連続です。実Tilemapの頂点色から計算したAlphaを確認してください。");
                }
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        /// <summary>列ごとに岸が1段ずれる配置をGPU描画し、縦横のTile境界でも濃度差が1px分だけになることを保証します。</summary>
        [Test]
        public void DepthTilemap_StairShoreIsContinuousInBothDirections()
        {
            var cache = new Farmware.WorldGeneration.Presentation.TerrainLiquidRuntimeTileCache();
            var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidDepthWithoutFloor.mat"));
            try
            {
                material.SetColor("_FreshWaterColor", Color.black);
                Tilemap tilemap = CreateTilemap(material);
                for (int y = 0; y < 7; y++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        int stage = 6 - y + x;
                        float left = Farmware.WorldGeneration.Presentation.TerrainLiquidPresentationUtility
                            .GetDepthStageStartAlpha(Mathf.Max(0, stage - 1), false);
                        float center = Farmware.WorldGeneration.Presentation.TerrainLiquidPresentationUtility
                            .GetDepthStageStartAlpha(stage, false);
                        float right = Farmware.WorldGeneration.Presentation.TerrainLiquidPresentationUtility
                            .GetDepthStageStartAlpha(stage + 1, false);
                        tilemap.SetTile(new Vector3Int(x, y, 0), cache.GetOrCreateDepthOverlay(
                            Farmware.WorldGeneration.Presentation.TerrainLiquidVisualKind.FreshWater,
                            left, center, right));
                    }
                }

                RenderPixels();
                // 飽和前の斜め勾配を検査します。1タイル分の段差が境界1pxへ集中すれば、この差分は大きくなります。
                for (int y = 49; y < 79; y++)
                {
                    for (int x = 9; x < 39; x++)
                    {
                        float value = readback.GetPixel(x, y).r;
                        Assert.That(readback.GetPixel(x - 1, y).r - value,
                            Is.EqualTo(1f / 93f).Within(0.006f), $"横境界({x}, {y})の濃度が飛んでいます。");
                        Assert.That(value - readback.GetPixel(x, y - 1).r,
                            Is.EqualTo(1f / 93f).Within(0.006f), $"縦境界({x}, {y})の濃度が戻っています。");
                    }
                }
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(material);
            }
        }

        /// <summary>水中断面の実Tilemapを6段描き、各段の下端まで不透明で乾燥減衰が混入しないことを保証します。</summary>
        [Test]
        public void SubmergedFaceTilemap_DoesNotFadeAtEachTileBottom()
        {
            var cache = new Farmware.WorldGeneration.Presentation.TerrainMaterialRuntimeTileCache();
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[256];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Farmware/Art/WorldGeneration/Materials/TerrainDryCliffFace.mat");
            try
            {
                renderCamera.backgroundColor = Color.black;
                Tilemap tilemap = CreateTilemap(material);
                var selection = new Farmware.WorldGeneration.Presentation.TerrainMaterialProductTileSelection(
                    sprite, new Color(1f, 1f, 1f, 0f));
                TileBase tile = cache.GetOrCreate(selection);
                for (int stage = 0; stage < 6; stage++)
                {
                    for (int x = 0; x < 3; x++)
                    {
                        tilemap.SetTile(new Vector3Int(x, 6 - stage, 0), tile);
                    }
                }

                RenderPixels();
                for (int depth = 0; depth < 96; depth++)
                {
                    Assert.That(readback.GetPixel(24, 111 - depth).r, Is.EqualTo(1f).Within(0.005f),
                        $"水中断面{depth}pxを乾燥崖の減衰で透明にしてはいけません。");
                }
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
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

        /// <summary>現在のURPで隔離Cameraを同期描画し、GPU結果をCPU Textureへ読み戻します。</summary>
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
                readback.ReadPixels(new Rect(0, 0, RenderWidth, RenderHeight), 0, 0);
                readback.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
