// このファイルは、製品Liquid表示の3pxクリップ、深度勾配、水際7bit、通常Face継続、反射Proxy分類を検証します。
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


        /// <summary>93px勾配を過ぎた深部は段6へ固定し、最終16px勾配を各タイルで繰り返さないことを保証します。</summary>
        [Test]
        public void WaterDepth_BeyondNinetySixPixelsStaysAtSaturatedStage()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            for (int y = 20; y < 28; y++)
            {
                SetCell(center, 20, y, Liquid(runtimeIds.FreshWater));
            }

            SetCell(center, 20, 28, Ground(201));

            Assert.That(
                TerrainLiquidPresentationUtility.GetClampedWaterDepth(
                    neighborhood,
                    20,
                    22,
                    runtimeIds),
                Is.EqualTo(5),
                "岸まで6セルの位置は、最後の16px勾配を描く段5です。");
            Assert.That(
                TerrainLiquidPresentationUtility.GetClampedWaterDepth(
                    neighborhood,
                    20,
                    21,
                    runtimeIds),
                Is.EqualTo(TerrainLiquidPresentationUtility.SaturatedDepthStage),
                "岸まで7セル以上の位置は、勾配を繰り返さない段6です。");
            Assert.That(
                TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(
                    TerrainLiquidPresentationUtility.SaturatedDepthStage * 16,
                    hasUnderwaterFloor: false),
                Is.EqualTo(1f),
                "床なし深部はタイル上端から完全不透明を維持します。");
            Assert.That(
                TerrainLiquidPresentationUtility.GetDepthOverlayAlpha(
                    TerrainLiquidPresentationUtility.SaturatedDepthStage * 16,
                    hasUnderwaterFloor: true),
                Is.EqualTo(0.5f),
                "床あり深部はタイル上端から最大50%を維持します。");
        }

        /// <summary>奥・左・右の通常辺を選び、画面手前の岸を接触線へ含めないことを保証します。</summary>
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

        /// <summary>斜め奥の岸に対して、同種水がつながる側だけ内角の短い線を補うことを保証します。</summary>
        [Test]
        public void EdgeMask_ConnectsBothInnerCornersOnlyAcrossSameWater()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 20, 20, Liquid(runtimeIds.FreshWater));
            SetCell(center, 20, 21, Liquid(runtimeIds.FreshWater));
            SetCell(center, 19, 20, Liquid(runtimeIds.FreshWater));
            SetCell(center, 21, 20, Liquid(runtimeIds.FreshWater));
            SetCell(center, 19, 21, Ground(201));
            SetCell(center, 21, 21, Wall(301));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 20, 20),
                Is.EqualTo(TerrainLiquidEdgeMask.LeftInnerCorner | TerrainLiquidEdgeMask.RightInnerCorner));

            // 隣が別の液体なら、斜めの岸があってもその液体をまたいで水際を結びません。
            SetCell(center, 19, 20, Liquid(runtimeIds.SaltWater));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 20, 20),
                Is.EqualTo(TerrainLiquidEdgeMask.RightInnerCorner));
            SetCell(center, 20, 21, Ground(202));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 20, 20),
                Is.EqualTo(TerrainLiquidEdgeMask.Back));
        }

        /// <summary>中心チャンクの左右上端から負側・正側の斜めHaloを参照しても、内角が欠けないことを保証します。</summary>
        [Test]
        public void EdgeMask_ConnectsInnerCornersAcrossChunkHalo()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 0, 63, Liquid(runtimeIds.FreshWater));
            SetCell(center, 63, 63, Liquid(runtimeIds.FreshWater));

            // CreateEmptyNeighborhoodの配列順は南西・南・南東・西・中央・東・北西・北・北東です。
            // X=-1とX=64、Y=64がそれぞれ対応するHaloの端へ変換される条件を準備します。
            SetCell(terrainArrays[3], 63, 63, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[5], 0, 63, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[7], 0, 0, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[7], 63, 0, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[6], 63, 0, Ground(201));
            SetCell(terrainArrays[8], 0, 0, Ground(201));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 0, 63),
                Is.EqualTo(TerrainLiquidEdgeMask.LeftInnerCorner));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 63, 63),
                Is.EqualTo(TerrainLiquidEdgeMask.RightInnerCorner));
        }

        /// <summary>岸が奥へ続く側は維持し、裏側へ回り込む終端だけを液体3種で左右独立に選ぶことを保証します。</summary>
        /// <param name="leftEndpoint">左岸だけを終端にする場合はtrue、右岸だけならfalseです。</param>
        /// <param name="liquidId">淡水101、海水102、マグマ103のテスト用Runtime IDです。</param>
        [TestCase(true, 101)]
        [TestCase(false, 101)]
        [TestCase(true, 102)]
        [TestCase(false, 102)]
        [TestCase(true, 103)]
        [TestCase(false, 103)]
        public void EdgeMask_ClipsBackEndpointIndependently(bool leftEndpoint, int liquidId)
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            var liquid = new DefinitionRuntimeId((ushort)liquidId);
            SetCell(center, 20, 20, Liquid(liquid));
            SetCell(center, 20, 21, Liquid(liquid));
            SetCell(center, 19, 20, Ground(201));
            SetCell(center, 21, 20, Wall(301));
            SetCell(center, 19, 21, leftEndpoint ? Liquid(liquid) : Ground(201));
            SetCell(center, 21, 21, leftEndpoint ? Wall(301) : Liquid(liquid));

            TerrainLiquidEdgeMask sides = TerrainLiquidEdgeMask.Left | TerrainLiquidEdgeMask.Right;
            TerrainLiquidEdgeMask endpoint = leftEndpoint
                ? TerrainLiquidEdgeMask.LeftBackEndpoint : TerrainLiquidEdgeMask.RightBackEndpoint;
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 20, 20),
                Is.EqualTo(sides | endpoint));

            // 同種液体が奥へ続かない場所は「地面の裏へ回り込む水際」として接続しません。
            SetCell(center, 20, 21, Liquid(new DefinitionRuntimeId(104)));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 20, 20), Is.EqualTo(sides));
            SetCell(center, 20, 21, default);
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 20, 20), Is.EqualTo(sides));
        }

        /// <summary>中心の上端から左右のHaloへまたがる終端も、継続する縦岸と区別できることを保証します。</summary>
        [Test]
        public void EdgeMask_ClipsBackEndpointsAcrossChunkHalo()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 0, 63, Liquid(runtimeIds.FreshWater));
            SetCell(center, 63, 63, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[3], 63, 63, Ground(201));
            SetCell(terrainArrays[5], 0, 63, Wall(301));
            SetCell(terrainArrays[7], 0, 0, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[7], 63, 0, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[6], 63, 0, Liquid(runtimeIds.FreshWater));
            SetCell(terrainArrays[8], 0, 0, Liquid(runtimeIds.FreshWater));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 0, 63),
                Is.EqualTo(TerrainLiquidEdgeMask.Left | TerrainLiquidEdgeMask.LeftBackEndpoint));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 63, 63),
                Is.EqualTo(TerrainLiquidEdgeMask.Right | TerrainLiquidEdgeMask.RightBackEndpoint));

            // 斜め奥にも岸を足すと縦岸は継続するため、上端3pxを削る印は消えます。
            SetCell(terrainArrays[6], 63, 0, Ground(201));
            SetCell(terrainArrays[8], 0, 0, Wall(301));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 0, 63),
                Is.EqualTo(TerrainLiquidEdgeMask.Left));
            Assert.That(TerrainLiquidPresentationUtility.SelectEdgeMask(neighborhood, 63, 63),
                Is.EqualTo(TerrainLiquidEdgeMask.Right));
        }

        /// <summary>裏側終端だけ上端3pxが欠け、同じセルの反対側の縦線は全16px残ることを保証します。</summary>
        /// <param name="leftEndpoint">左側終端を検査する場合はtrue、右側終端ならfalseです。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void ShorelineShape_BackEndpointClipsOnlyItsOwnSide(bool leftEndpoint)
        {
            TerrainLiquidEdgeMask mask = TerrainLiquidEdgeMask.Left | TerrainLiquidEdgeMask.Right
                | (leftEndpoint ? TerrainLiquidEdgeMask.LeftBackEndpoint : TerrainLiquidEdgeMask.RightBackEndpoint);
            for (int y = 0; y < 16; y++)
            {
                Assert.That(TerrainLiquidPresentationUtility.IsShorelinePixel(mask, leftEndpoint ? 0 : 15, y),
                    Is.EqualTo(y <= 12), "裏側へ回り込む終端だけ3px隠します。");
                Assert.That(TerrainLiquidPresentationUtility.IsShorelinePixel(mask, leftEndpoint ? 15 : 0, y),
                    Is.True, "反対側の連続する岸には隙間を作りません。");
            }
        }

        /// <summary>連続する縦岸は全16px、奥岸と重なる縦線は13px、内角は接点を含む4pxになることを保証します。</summary>
        [Test]
        public void ShorelineShape_ClipsOnlyBackShoreAndBridgesInnerCorner()
        {
            for (int y = 0; y < 16; y++)
            {
                Assert.That(TerrainLiquidPresentationUtility.IsShorelinePixel(TerrainLiquidEdgeMask.Left, 0, y), Is.True);
                Assert.That(TerrainLiquidPresentationUtility.IsShorelinePixel(
                    TerrainLiquidEdgeMask.Back | TerrainLiquidEdgeMask.Left, 0, y), Is.EqualTo(y <= 12));
                Assert.That(TerrainLiquidPresentationUtility.IsShorelinePixel(
                    TerrainLiquidEdgeMask.LeftInnerCorner, 0, y), Is.EqualTo(y >= 12));
            }
        }

        /// <summary>表示原点の画素オフセットが負座標やintの両端でも周期内へ収まり、原点変更で絶対位相を失わないことを保証します。</summary>
        /// <param name="origin">検証する表示原点の絶対タイル座標です。</param>
        [TestCase(-1)]
        [TestCase(64)]
        [TestCase(-512)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void ShorelineOrigin_WrapsWithoutIntegerOverflow(int origin)
        {
            int offset = TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin);
            long expected = ((long)origin * 16) % TerrainLiquidPresentationUtility.ShorelinePatternPeriodPixels;
            if (expected < 0)
            {
                expected += TerrainLiquidPresentationUtility.ShorelinePatternPeriodPixels;
            }
            Assert.That(offset, Is.EqualTo((int)expected));
        }

        /// <summary>旧形式のAnimatedTileカタログを、新形式の色設定として暗黙に受理しないことを保証します。</summary>
        [Test]
        public void PresentationCatalog_RejectsOldFormatUntilRepublished()
        {
            TerrainLiquidPresentationCatalog catalog = CreateOwned<TerrainLiquidPresentationCatalog>();
            Assert.That(catalog.TryInitialize(out string error), Is.False);
            Assert.That(error, Does.Contain("再公開"));
        }

        /// <summary>同じ色と接触形状の水際Tileを再利用し、内部セル用のTileやアニメーションを生成しないことを保証します。</summary>
        [Test]
        public void RuntimeTileCache_ReusesStaticShorelineTiles()
        {
            using (var cache = new TerrainLiquidRuntimeTileCache())
            {
                TileBase first = cache.GetOrCreateShoreline(Color.white, TerrainLiquidEdgeMask.Left);
                Assert.That(cache.GetOrCreateShoreline(Color.white, TerrainLiquidEdgeMask.Left), Is.SameAs(first));
                Assert.That(first, Is.TypeOf<Tile>());
                Assert.That(((Color32)((Tile)first).color).a, Is.EqualTo((byte)TerrainLiquidEdgeMask.Left));
                // 上位bitの終端情報も同じAlphaへ格納し、通常の縦岸Tileとは別に共有します。
                TerrainLiquidEdgeMask endpointMask = TerrainLiquidEdgeMask.Right | TerrainLiquidEdgeMask.RightBackEndpoint;
                TileBase endpoint = cache.GetOrCreateShoreline(Color.white, endpointMask);
                Assert.That(((Color32)((Tile)endpoint).color).a, Is.EqualTo((byte)endpointMask));
                Assert.That(cache.GetOrCreateShoreline(Color.white, endpointMask), Is.SameAs(endpoint));
                Assert.That(cache.GetOrCreateShoreline(Color.white, TerrainLiquidEdgeMask.None), Is.Null);
                Assert.That(cache.ShorelineTileCount, Is.EqualTo(2));
            }
        }

        /// <summary>全3液体で同じ奥岸Groundを選び、Wallがあっても下地の縁を欠落させないことを保証します。</summary>
        /// <param name="kind">確認する液体種です。</param>
        /// <param name="hasWall">岸のGround上にWallがある場合はtrueです。</param>
        [TestCase(TerrainLiquidVisualKind.FreshWater, false)]
        [TestCase(TerrainLiquidVisualKind.SaltWater, false)]
        [TestCase(TerrainLiquidVisualKind.Magma, false)]
        [TestCase(TerrainLiquidVisualKind.FreshWater, true)]
        [TestCase(TerrainLiquidVisualKind.SaltWater, true)]
        [TestCase(TerrainLiquidVisualKind.Magma, true)]
        public void LiquidBank_UsesAdjacentGroundEvenBelowWall(TerrainLiquidVisualKind kind, bool hasWall)
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(out NativeArray<GeneratedTerrainCell> center);
            DefinitionRuntimeId liquidId = kind == TerrainLiquidVisualKind.Magma ? runtimeIds.Magma
                : kind == TerrainLiquidVisualKind.SaltWater ? runtimeIds.SaltWater : runtimeIds.FreshWater;
            SetCell(center, 10, 10, Liquid(liquidId));
            GeneratedTerrainCell shore = Ground(401);
            if (hasWall) shore.WallBlockRuntimeId = new DefinitionRuntimeId(501);
            SetCell(center, 10, 11, shore);
            Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceAtLiquidBank(
                neighborhood, 10, 10, runtimeIds, out TerrainLiquidFaceProjection face), Is.True);
            Assert.That(face.SourceCell.GroundBlockRuntimeId.Value, Is.EqualTo(401));
            Assert.That(face.SourceLocalY, Is.EqualTo(11));
            Assert.That(face.FaceDepth, Is.Zero);

            // 1セル奥でもGroundが離れた場合は縁を延長せず、マグマの内部を透視させません。
            SetCell(center, 10, 11, Liquid(liquidId));
            SetCell(center, 10, 12, shore);
            Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceAtLiquidBank(
                neighborhood, 10, 10, runtimeIds, out _), Is.False);
        }

        /// <summary>最大6段の透明水域へ通常Ground Faceを投影し、淡水・海水の境界は越え、マグマは対象外にすることを保証します。</summary>
        [Test]
        public void WaterFaceProjection_ContinuesAcrossTransparentWaterAndRejectsMagma()
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
                    out TerrainLiquidFaceProjection mixedProjection),
                Is.True,
                "淡水と海水の間で断面を欠落させてはいけません。");
            Assert.That(mixedProjection.FaceDepth, Is.EqualTo(1));
            Assert.That(mixedProjection.SourceCell.GroundBlockRuntimeId.Value, Is.EqualTo(402));

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

        /// <summary>淡水と海水を交互に置いても、全6段で同じ岸と連続する距離を使い、元の水種を維持することを保証します。</summary>
        /// <param name="freshAtBottom">最も手前のセルを淡水にする場合はtrueです。両向きの水種境界を検証します。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void MixedWater_UsesContinuousFaceDepthAndKeepsLiquidKinds(bool freshAtBottom)
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 10, 16, Ground(401));
            for (int y = 10; y < 16; y++)
            {
                // 水種が切り替わるたびに段数が飽和すると、実際には存在しない溝や横帯が現れます。
                bool fresh = ((y - 10) % 2 == 0) == freshAtBottom;
                SetCell(center, 10, y, Liquid(fresh ? runtimeIds.FreshWater : runtimeIds.SaltWater));
            }

            for (int y = 10; y < 16; y++)
            {
                Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    neighborhood, 10, y, runtimeIds, out TerrainLiquidFaceProjection face), Is.True);
                Assert.That(face.SourceLocalX, Is.EqualTo(10));
                Assert.That(face.SourceLocalY, Is.EqualTo(16));
                Assert.That(face.FaceDepth, Is.EqualTo(15 - y));
                Assert.That(TerrainLiquidPresentationUtility.GetClampedWaterDepth(neighborhood, 10, y, runtimeIds), Is.EqualTo(15 - y));
                Assert.That(TerrainLiquidPresentationUtility.ShouldClipSurfaceTop(neighborhood, 10, y), Is.EqualTo(y == 15));
                bool fresh = ((y - 10) % 2 == 0) == freshAtBottom;
                Assert.That(TerrainLiquidPresentationUtility.Classify(center[WorldGrid.ToTileIndex(10, y)], runtimeIds),
                    Is.EqualTo(fresh ? TerrainLiquidVisualKind.FreshWater : TerrainLiquidVisualKind.SaltWater),
                    "探索の連続化によって水種や水色の分類を統合してはいけません。");
            }
        }

        /// <summary>異なる水種を間に挟むWallでも反射元へ到達し、水中断面・距離と同じ岸を使うことを保証します。</summary>
        /// <param name="freshReceiver">受け手セルが淡水ならtrue、海水ならfalseです。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void MixedWater_ReflectionSearchCrossesWaterKindBoundary(bool freshReceiver)
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 10, 10, Liquid(freshReceiver ? runtimeIds.FreshWater : runtimeIds.SaltWater));
            SetCell(center, 10, 11, Liquid(freshReceiver ? runtimeIds.SaltWater : runtimeIds.FreshWater));
            GeneratedTerrainCell shore = Ground(401);
            shore.WallBlockRuntimeId = new DefinitionRuntimeId(501);
            SetCell(center, 10, 12, shore);

            Assert.That(TerrainLiquidPresentationUtility.TryFindWallReflectionSource(neighborhood, 10, 10, runtimeIds,
                freshReceiver ? TerrainLiquidVisualKind.FreshWater : TerrainLiquidVisualKind.SaltWater,
                out TerrainMaterialFaceProjection reflection), Is.True);
            Assert.That(reflection.SourceLocalY, Is.EqualTo(12));
            Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                neighborhood, 10, 10, runtimeIds, out TerrainLiquidFaceProjection face), Is.True);
            Assert.That(face.SourceLocalY, Is.EqualTo(reflection.SourceLocalY));
            Assert.That(face.FaceDepth, Is.EqualTo(1));
            Assert.That(TerrainLiquidPresentationUtility.GetClampedWaterDepth(neighborhood, 10, 10, runtimeIds), Is.EqualTo(1));
        }

        /// <summary>透明水域を連続化しても、マグマ・未知の液体・空セルを越えて断面や反射を借りないことを保証します。</summary>
        /// <param name="freshReceiver">受け手セルを淡水にする場合はtrueです。</param>
        /// <param name="barrierId">探索を遮る液体IDです。0は空セル、103はマグマ、999は未知の液体です。</param>
        [TestCase(true, 0)]
        [TestCase(false, 0)]
        [TestCase(true, 103)]
        [TestCase(false, 103)]
        [TestCase(true, 999)]
        [TestCase(false, 999)]
        public void MixedWater_SearchesStillStopAtNonWater(bool freshReceiver, int barrierId)
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 10, 10, Liquid(freshReceiver ? runtimeIds.FreshWater : runtimeIds.SaltWater));
            SetCell(center, 10, 11, Liquid(new DefinitionRuntimeId(barrierId)));
            GeneratedTerrainCell shore = Ground(401);
            shore.WallBlockRuntimeId = new DefinitionRuntimeId(501);
            SetCell(center, 10, 12, shore);

            Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(neighborhood, 10, 10, runtimeIds, out _), Is.False);
            Assert.That(TerrainLiquidPresentationUtility.GetClampedWaterDepth(neighborhood, 10, 10, runtimeIds), Is.EqualTo(6));
            Assert.That(TerrainLiquidPresentationUtility.TryFindWallReflectionSource(neighborhood, 10, 10, runtimeIds,
                freshReceiver ? TerrainLiquidVisualKind.FreshWater : TerrainLiquidVisualKind.SaltWater, out _), Is.False);
        }

        /// <summary>岸にWallがあっても、その設置面Groundの断面を水中6段へ続け、濃紺の欠けた列を作らないことを保証します。</summary>
        [Test]
        public void WaterFaceProjection_UsesGroundBelowShoreWallForEveryStage()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(
                out NativeArray<GeneratedTerrainCell> center);
            GeneratedTerrainCell shore = Ground(401);
            shore.WallBlockRuntimeId = new DefinitionRuntimeId(501);
            SetCell(center, 10, 16, shore);
            for (int y = 10; y < 16; y++)
            {
                SetCell(center, 10, y, Liquid(runtimeIds.SaltWater));
            }

            // 上面を高く表示するWallの有無で、水面より下のGroundが欠落してはいけません。
            for (int stage = 0; stage < TerrainLiquidPresentationUtility.MaximumFaceDepth; stage++)
            {
                Assert.That(
                    TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                        neighborhood, 10, 15 - stage, runtimeIds,
                        out TerrainLiquidFaceProjection projection), Is.True);
                Assert.That(projection.FaceDepth, Is.EqualTo(stage));
                Assert.That(projection.SourceCell.GroundBlockRuntimeId.Value, Is.EqualTo(401));
                Assert.That(
                    TerrainLiquidPresentationUtility.GetClampedWaterDepth(
                        neighborhood, 10, 15 - stage, runtimeIds), Is.EqualTo(stage));
            }

            // 設置面GroundがないWallは、奥に別材質のGroundがあっても越えて投影しません。
            SetCell(center, 10, 16, Wall(501));
            SetCell(center, 10, 17, Ground(402));
            Assert.That(
                TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    neighborhood, 10, 15, runtimeIds, out _), Is.False);
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
                TerrainLiquidPresentationUtility.TryFindWallReflectionSource(
                    neighborhood,
                    30,
                    30,
                    runtimeIds,
                    TerrainLiquidVisualKind.FreshWater,
                    out TerrainMaterialFaceProjection reflection),
                Is.True);
            Assert.That(reflection.SourceLocalY, Is.EqualTo(32));
            Assert.That(reflection.IsWall, Is.True);
            // 直接隣接だけに限定せず、地面を挟んでも届くWallを投影元として返します。
            SetCell(center, 30, 31, Ground(502));
            Assert.That(TerrainLiquidPresentationUtility.TryFindWallReflectionSource(
                neighborhood, 30, 30, runtimeIds, TerrainLiquidVisualKind.FreshWater, out reflection), Is.True);
            Assert.That(reflection.SourceLocalY, Is.EqualTo(32));

            SetCell(center, 31, 30, Liquid(runtimeIds.FreshWater));
            SetCell(center, 31, 31, Ground(502));
            Assert.That(
                TerrainLiquidPresentationUtility.TryFindWallReflectionSource(
                    neighborhood,
                    31,
                    30,
                    runtimeIds,
                    TerrainLiquidVisualKind.FreshWater,
                    out _),
                Is.False,
                "水平なGroundTopは反射対象にしてはいけません。");

            SetCell(center, 32, 30, Liquid(runtimeIds.Magma));
            SetCell(center, 32, 31, Wall(503));
            Assert.That(
                TerrainLiquidPresentationUtility.TryFindWallReflectionSource(
                    neighborhood,
                    32,
                    30,
                    runtimeIds,
                    TerrainLiquidVisualKind.Magma,
                    out _),
                Is.False,
                "マグマへ水面反射Proxyを作ってはいけません。");
        }

        /// <summary>隣列の地面上に投影された像も探索し、受け手の水域でだけ輪郭を描けることを保証します。</summary>
        [Test]
        public void ReflectionSource_UsesReceivingWaterKindForNeighborLandColumn()
        {
            CompletedChunkTerrainNeighborhood neighborhood = CreateEmptyNeighborhood(out NativeArray<GeneratedTerrainCell> center);
            SetCell(center, 30, 30, Ground(502));
            SetCell(center, 30, 31, Wall(501));
            Assert.That(TerrainLiquidPresentationUtility.TryFindWallReflectionSource(neighborhood, 30, 30,
                runtimeIds, TerrainLiquidVisualKind.FreshWater, out var reflection), Is.True);
            Assert.That(reflection.SourceLocalX, Is.EqualTo(30));
            Assert.That(TerrainLiquidPresentationUtility.TryFindWallReflectionSource(neighborhood, 30, 30,
                runtimeIds, TerrainLiquidVisualKind.Magma, out _), Is.False);
        }

        /// <summary>横揺れの余白は隣の水域まで届く一方、受け手の矩形を越えないことを保証します。</summary>
        /// <param name="origin">負座標でも同じ矩形を返す確認用のタイル原点です。</param>
        [TestCase(0)]
        [TestCase(-64)]
        public void ReflectionGeometry_PadsSourceOutlineButNotReceivingWater(int origin)
        {
            Rect source = new Rect(origin + 1, 4, 1, 1);
            Rect water = new Rect(origin + 2, 3, 1, 13f / 16f);
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(source, 4, 61f / 16f,
                water, 0.75f, 24, out var slice), Is.False);
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(source, 4, 61f / 16f,
                water, 0.75f, 24, out slice, 2), Is.True);
            Assert.That(slice.Destination.xMin, Is.EqualTo(origin + 2));
            Assert.That(slice.Destination.xMax, Is.EqualTo(origin + 2 + 2f / 16f));
            Assert.That(slice.Source.xMin, Is.EqualTo(1f));
            Assert.That(slice.Source.xMax, Is.EqualTo(1f + 2f / 16f));
        }

        /// <summary>上下2枚のWallを、接地点に近い下段から正しく反転投影することを保証します。</summary>
        /// <param name="heightStage">接地点から上への段数です。</param>
        /// <param name="faceDepth">Wall上端から数えた元画像の段数です。</param>
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(2, -1)]
        public void ReflectionSource_OrderStartsAtActualWallFoot(int heightStage, int faceDepth)
        {
            Assert.That(TerrainLiquidPresentationUtility.GetReflectedWallFaceDepth(heightStage), Is.EqualTo(faceDepth));
        }

        /// <summary>反射元の高さに応じて、奥側の対象が水面へ届くかを判定し、岸へ吸着しないことを保証します。</summary>
        /// <param name="height">対象の高さ（タイル）です。</param>
        /// <param name="expected">水面と交差する場合はtrueです。</param>
        [TestCase(1f, false)]
        [TestCase(4f, true)]
        public void ReflectionGeometry_UsesHeightAndContactRatherThanAdjacentTile(float height, bool expected)
        {
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(0, 2, 1, height),
                2f, 2f - 3f / 16f, new Rect(0, 0, 1, 13f / 16f), 0.75f, height * 12f, out var slice), Is.EqualTo(expected));
            if (expected)
            {
                Assert.That(slice.DepthPixels.y, Is.GreaterThan(0f), "水際で距離を0へ戻してはいけません。");
                Assert.That(slice.MaximumDepthPixels, Is.EqualTo(48f));
                Assert.That(slice.Source.yMin, Is.GreaterThan(0f), "届いた画像の一部分だけを表示します。");
            }
        }

        /// <summary>切り抜きが負座標でも一致し、地面を挟むWall反射の最後8pxだけを表示することを保証します。</summary>
        /// <param name="origin">正負を比較するタイル原点です。</param>
        [TestCase(0f)]
        [TestCase(-64f)]
        public void ReflectionGeometry_ClipsToReceiverWithoutRestartingDepth(float origin)
        {
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(0, origin + 3, 1, 1),
                origin + 2, origin + 2 - 3f / 16f, new Rect(0, origin, 1, 13f / 16f),
                0.75f, 24f, out var slice), Is.True);
            Assert.That(slice.Destination.yMin - origin, Is.EqualTo(5f / 16f).Within(0.0001f));
            Assert.That(slice.Destination.yMax - origin, Is.EqualTo(13f / 16f).Within(0.0001f));
            Assert.That(slice.DepthPixels.x, Is.EqualTo(24f).Within(0.0001f));
            Assert.That(slice.DepthPixels.y, Is.EqualTo(16f).Within(0.0001f));
            Assert.That(slice.Source.yMin, Is.EqualTo(1f / 3f).Within(0.0001f));
        }

        /// <summary>反射が水域と重ならない場合や不正な寸法を、描画Objectの生成前に拒否します。</summary>
        [Test]
        public void ReflectionGeometry_RejectsInvalidOrNonOverlappingInput()
        {
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(0, 4, 1, 1), 4, 4,
                new Rect(0, 0, 1, 1), 0.75f, 24f, out _), Is.False);
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(0, 0, 1, 1), 0, 0,
                new Rect(0, -1, 1, 1), 0f, 24f, out _), Is.False);
            Assert.That(TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(0, 0, 1, 1), float.NaN, 0,
                new Rect(0, -1, 1, 1), 0.75f, 24f, out _), Is.False);
        }

        /// <summary>製品用Surface、深度2種、反射Materialが検品専用ではないShaderと確定値を参照することを保証します。</summary>
        [Test]
        public void ProductLiquidMaterials_UseProductShadersAndDocumentedValues()
        {
            TerrainLiquidPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(
                    "Assets/Farmware/Art/WorldGeneration/Liquids/Published/TerrainLiquidPresentationCatalog.asset");
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
            Assert.That(withFloor.HasProperty("_FadePixels"), Is.False, "基底色に投影距離の勾配を残してはいけません。");
            Assert.That(withoutFloor, Is.Not.Null);
            Assert.That(withoutFloor.HasProperty("_MaximumAlpha"), Is.False);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryInitialize(out string catalogError), Is.True, catalogError);
            Assert.That(
                catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.FreshWater,
                    out TerrainLiquidPresentationDefinition freshWater),
                Is.True);
            Assert.That(
                catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.SaltWater,
                    out TerrainLiquidPresentationDefinition saltWater),
                Is.True);
            Assert.That(
                Vector4.Distance(
                    withFloor.GetColor("_FreshWaterColor"),
                    freshWater.DepthOverlayColor),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector4.Distance(
                    withFloor.GetColor("_SaltWaterColor"),
                    saltWater.DepthOverlayColor),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector4.Distance(
                    withoutFloor.GetColor("_FreshWaterColor"),
                    freshWater.DepthOverlayColor),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector4.Distance(
                    withoutFloor.GetColor("_SaltWaterColor"),
                    saltWater.DepthOverlayColor),
                Is.LessThan(0.0001f));
            Assert.That(reflection, Is.Not.Null);
            Assert.That(reflection.shader.name, Is.EqualTo("Farmware/Terrain/Water Reflection Proxy"));
            // MaterialはユーザーがInspectorで調整するため、採用時の初期値へ固定せず有効範囲を検査します。
            Assert.That(reflection.GetFloat("_MaximumAlpha"), Is.InRange(0f, 1f));
            Assert.That(reflection.GetFloat("_WaveAmplitudePixels"), Is.InRange(0f, 2f));
            Assert.That(reflection.GetFloat("_AnimationStepsPerSecond"), Is.InRange(1f, 12f));
            var defaults = new Material(reflection.shader);
            try
            {
                // 初期値の契約は保存Assetとは別に、Shaderから作った一時Materialで保証します。
                Assert.That(defaults.GetFloat("_MaximumAlpha"), Is.EqualTo(0.22f));
                Assert.That(defaults.GetFloat("_WaveAmplitudePixels"), Is.EqualTo(1f));
                Assert.That(defaults.GetFloat("_AnimationStepsPerSecond"), Is.EqualTo(2f));
            }
            finally { Object.DestroyImmediate(defaults); }
        }

        /// <summary>反射Shaderが場所別周期で輪郭ごと動き、変位後のUVだけを元Spriteの矩形で切り抜くことを保証します。</summary>
        [Test]
        public void ProductReflectionShader_UsesLocalPeriodsAndSpriteBounds()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string shaderPath = Path.Combine(
                projectRoot,
                "Assets/Farmware/Shaders/TerrainWaterReflection.shader");
            string source = File.ReadAllText(shaderPath);

            Assert.That(source, Does.Contain("NodeDisplacement"));
            Assert.That(source, Does.Contain("clamp(shiftedUv,input.bounds.xy"));
            Assert.That(source, Does.Contain("offset*_MainTex_TexelSize.x"));
            Assert.That(source, Does.Not.Contain("originalSilhouette"), "元のAlphaを固定マスクとして掛けず、輪郭も動かします。");
            Assert.That(source, Does.Contain("clip(min(min(shiftedUv.x-input.bounds.x"));
            Assert.That(source, Does.Contain("sampler_PointClamp"));
            Assert.That(source, Does.Contain("clamp(round(_WaveAmplitudePixels),0.0,2.0)"));
            Assert.That(TerrainLiquidPresentationUtility.ReflectionHorizontalPaddingPixels, Is.EqualTo(2));
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

        /// <summary>淡水・海水・マグマの参照と水際色がそろう場合だけ、種別順によらず検索できることを保証します。</summary>
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
            Assert.That(fresh.ShorelineColor, Is.EqualTo(Color.white));
        }

        /// <summary>同じ液体種や無効な水際色を拒否し、不正な設定で描画を始めないことを保証します。</summary>
        [Test]
        public void PresentationCatalog_RejectsDuplicateKindAndInvalidShorelineColor()
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

            TerrainLiquidPresentationCatalog invalidColorCatalog =
                CreateOwned<TerrainLiquidPresentationCatalog>();
            TerrainLiquidPresentationDefinition invalidFresh = CreateLiquidDefinition(
                TerrainLiquidVisualKind.FreshWater,
                invalidShorelineColor: true);
            invalidColorCatalog.SetDefinitions(new[]
            {
                invalidFresh,
                CreateLiquidDefinition(TerrainLiquidVisualKind.SaltWater),
                CreateLiquidDefinition(TerrainLiquidVisualKind.Magma)
            });

            Assert.That(invalidColorCatalog.TryInitialize(out string invalidColorError), Is.False);
            Assert.That(invalidColorError, Does.Contain("水際色"));
        }

        /// <summary>液体種・近傍濃度と反射Spriteが同じなら、セル数に比例してTileを生成しないことを保証します。</summary>
        [Test]
        public void RuntimeTileCache_ReusesDepthAndSurfaceTiles()
        {
            var cache = new TerrainLiquidRuntimeTileCache();
            try
            {
                TileBase firstDepth = cache.GetOrCreateDepthOverlay();
                Assert.That(cache.GetOrCreateDepthOverlay(), Is.SameAs(firstDepth));
                Assert.That(cache.DepthTileCount, Is.EqualTo(1), "セルの深度組合せに比例してTileを生成しません。");


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

                Assert.That(TerrainLiquidPresentationUtility.GetReflectionVisibility(0), Is.EqualTo(1f));
                Assert.That(TerrainLiquidPresentationUtility.GetReflectionVisibility(12), Is.EqualTo(0.5f));
                Assert.That(TerrainLiquidPresentationUtility.GetReflectionVisibility(24), Is.Zero);
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

        /// <summary>Surfaceと水際色を持つ、完全な1液体定義を作ります。</summary>
        /// <param name="kind">作成する淡水、海水、マグマの種別です。</param>
        /// <param name="invalidShorelineColor">trueなら検証失敗用に水際色へNaNを入れます。</param>
        /// <returns>カタログ初期化へ渡せるテスト用定義です。</returns>
        private TerrainLiquidPresentationDefinition CreateLiquidDefinition(
            TerrainLiquidVisualKind kind,
            bool invalidShorelineColor = false)
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
            return new TerrainLiquidPresentationDefinition(
                kind,
                surface,
                invalidShorelineColor ? new Color(float.NaN, 1f, 1f, 1f) : Color.white,
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
