// このファイルは、一体型の土地・岩壁Sprite、液面アニメーション、岩壁際の資源レイヤー分類をSceneなしのEditModeで検証します。
// ChunkTilemapPresenterとEditor構築ツールが同じ定数を使い、Pivotや表示高がずれないことを素早く保証します。
// 影マスクの同期Burst処理も、チャンク境界で水文・Biome確定後の完成地形と一致することを保証します。

using System;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地形の寸法・分類と、確定済み地形から影マスクを構築する表示専用計算を保証します。
    /// </summary>
    public sealed class TerrainLayerPresentationUtilityTests
    {
        /// <summary>
        /// 3×3へ水面・マグマを含む完成データを用意し、全6400セルが借用Bufferの岩壁状態と一致することを保証します。
        /// 同じ出力配列を再利用しても古いtrueを残さず、入力地形を書き換えないことも確認します。
        /// </summary>
        [Test]
        public void FillShadowMask_MatchesCompletedTerrainAcrossChunkBoundaries()
        {
            using var southWest = CreateFilledChunk(GroundType.RockWall);
            using var south = CreateFilledChunk(GroundType.RockWall);
            using var southEast = CreateFilledChunk(GroundType.RockWall);
            using var west = CreateFilledChunk(GroundType.RockWall);
            using var centerStorage = CreateFilledChunk(GroundType.RockWall);
            using var eastStorage = CreateFilledChunk(GroundType.RockWall);
            using var northWest = CreateFilledChunk(GroundType.RockWall);
            using var northStorage = CreateFilledChunk(GroundType.RockWall);
            using var northEast = CreateFilledChunk(GroundType.RockWall);
            // using変数の要素変更用に借用コピーを分け、完成水面とマグマが基礎岩壁候補を抑制した状態を再現します。
            NativeArray<GeneratedTile> center = centerStorage;
            NativeArray<GeneratedTile> east = eastStorage;
            NativeArray<GeneratedTile> north = northStorage;
            east[WorldGrid.ToTileIndex(0, 32)] = new GeneratedTile { Ground = GroundType.Water };
            north[WorldGrid.ToTileIndex(16, 0)] = new GeneratedTile { Ground = GroundType.Magma };
            var neighborhood = new CompletedChunkTerrainNeighborhood(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast);
            int size = RockWallAppearanceUtility.ShadowMaskSizeInTiles;
            using var mask = new NativeArray<bool>(size * size, Allocator.TempJob);

            // 2回目は中心を全水面へ変え、同じ出力に1回目の岩壁が残らないことも検証します。
            for (int pass = 0; pass < 2; pass++)
            {
                RockWallAppearanceUtility.FillShadowMask(neighborhood, mask);
                for (int y = 0; y < size; y++)
                {
                    int localY = y - RockWallAppearanceUtility.ShadowSearchRadiusInTiles - 1;
                    for (int x = 0; x < size; x++)
                    {
                        int localX = x - RockWallAppearanceUtility.ShadowSearchRadiusInTiles - 1;
                        bool expected = neighborhood.TryGetTile(
                                localX,
                                localY,
                                out GeneratedTile tile)
                            && tile.Ground == GroundType.RockWall;

                        Assert.That(mask[y * size + x], Is.EqualTo(expected),
                            $"Local=({localX},{localY}), Pass={pass}");
                    }
                }

                if (pass == 0)
                {
                    Assert.That(
                        east[WorldGrid.ToTileIndex(0, 32)].Ground,
                        Is.EqualTo(GroundType.Water),
                        "外周の完成水面を入力側で書き換えてはいけません。");
                    for (int index = 0; index < center.Length; index++)
                    {
                        center[index] = new GeneratedTile { Ground = GroundType.Water };
                    }
                }
            }
        }

        /// <summary>不正な出力配列長を準備し、Jobを実行する前に明確な例外で拒否することを保証します。</summary>
        [TestCase(0)]
        [TestCase(6399)]
        [TestCase(6401)]
        public void FillShadowMask_InvalidArrayLength_RejectsBeforeRunning(int maskCount)
        {
            using var mask = new NativeArray<bool>(maskCount, Allocator.TempJob);
            Assert.Throws<ArgumentException>(() => RockWallAppearanceUtility.FillShadowMask(
                default, mask));
        }

        /// <summary>
        /// 土地Spriteが上面16pxと断面8pxを持ち、論理セル下端へ合わせるPivotが2/3になることを検証します。
        /// </summary>
        [Test]
        public void CombinedTerrainSprite_UsesSixteenPixelSurfaceAndEightPixelFace()
        {
            Assert.That(TerrainLayerPresentationUtility.TerrainSurfaceHeightInPixels, Is.EqualTo(16));
            Assert.That(TerrainLayerPresentationUtility.TerrainFrontFaceHeightInPixels, Is.EqualTo(8));
            Assert.That(TerrainLayerPresentationUtility.CombinedTerrainSpriteHeightInPixels, Is.EqualTo(24));
            Assert.That(
                TerrainLayerPresentationUtility.CombinedTerrainPivotY,
                Is.EqualTo(2f / 3f).Within(0.0001f));
        }

        /// <summary>
        /// 岩壁Spriteが上面16pxと側面12pxを持ち、基準セルへ合わせるPivotが8/28になることを検証します。
        /// </summary>
        [Test]
        public void CombinedRockWallSprite_UsesSixteenPixelSurfaceAndTwelvePixelFace()
        {
            Assert.That(TerrainLayerPresentationUtility.RockWallElevationInPixels, Is.EqualTo(12));
            Assert.That(TerrainLayerPresentationUtility.CombinedRockWallSpriteHeightInPixels, Is.EqualTo(28));
            Assert.That(
                TerrainLayerPresentationUtility.CombinedRockWallPivotY,
                Is.EqualTo(2f / 7f).Within(0.0001f));
            Assert.That(TerrainLayerPresentationUtility.RockWallElevationInCells, Is.EqualTo(0.75f));
        }

        /// <summary>
        /// 液面が16pxセルの半分に当たる8pxだけ画面下へ移動し、整数ピクセル境界を維持することを検証します。
        /// </summary>
        [Test]
        public void LiquidSurfaceOffset_MovesDownByHalfTile()
        {
            Assert.That(TerrainLayerPresentationUtility.LiquidSurfaceDepthInPixels, Is.EqualTo(8));
            Assert.That(
                TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells,
                Is.EqualTo(-0.5f));
        }

        /// <summary>
        /// 淡水・海水・マグマが別のアニメーション種類へ分類され、通常地形には何も重ならないことを検証します。
        /// </summary>
        [Test]
        public void ClassifyLiquidAnimation_DistinguishesLiquidTypes()
        {
            GeneratedTile freshWater = new GeneratedTile
            {
                Ground = GroundType.Water,
                WaterSalinity = WaterSalinity.FreshWater
            };
            GeneratedTile saltWater = new GeneratedTile
            {
                Ground = GroundType.Water,
                WaterSalinity = WaterSalinity.SaltWater
            };
            GeneratedTile magma = new GeneratedTile { Ground = GroundType.Magma };
            GeneratedTile land = new GeneratedTile { Ground = GroundType.Land };

            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(freshWater),
                Is.EqualTo(LiquidAnimationKind.FreshWater));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(saltWater),
                Is.EqualTo(LiquidAnimationKind.SaltWater));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(magma),
                Is.EqualTo(LiquidAnimationKind.Magma));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(land),
                Is.EqualTo(LiquidAnimationKind.None));
        }

        /// <summary>
        /// 通常地面の資源だけが、画面手前方向の隣接岩壁によって背面レイヤーへ振り分けられることを検証します。
        /// </summary>
        [Test]
        public void ShouldRenderResourceBehindRockWall_RequiresResourceOnLandAndFrontWall()
        {
            GeneratedResourceTile tree = new GeneratedResourceTile
            {
                Type = GeneratedResourceType.Tree
            };
            GeneratedResourceTile noResource = default;
            GeneratedTile land = new GeneratedTile { Ground = GroundType.Land };
            GeneratedTile rockWall = new GeneratedTile { Ground = GroundType.RockWall };

            Assert.That(
                TerrainLayerPresentationUtility.ShouldRenderResourceBehindRockWall(
                    tree,
                    land,
                    rockWall),
                Is.True,
                "通常地面の木は、直前方の岩壁より後ろへ描画する必要があります。");
            Assert.That(
                TerrainLayerPresentationUtility.ShouldRenderResourceBehindRockWall(
                    tree,
                    land,
                    land),
                Is.False,
                "直前方が通常地面なら、資源を岩壁背面レイヤーへ移してはいけません。");
            Assert.That(
                TerrainLayerPresentationUtility.ShouldRenderResourceBehindRockWall(
                    noResource,
                    land,
                    rockWall),
                Is.False,
                "資源のないセルを背面レイヤーへ分類してはいけません。");
            Assert.That(
                TerrainLayerPresentationUtility.ShouldRenderResourceBehindRockWall(
                    tree,
                    rockWall,
                    rockWall),
                Is.False,
                "岩壁上資源は高さ12pxの専用レイヤーを使うため、通常資源の背面レイヤーへ分類してはいけません。");
        }

        /// <summary>全セルを同じ完成地面で埋めた、呼び出し側所有の4096要素配列を作ります。</summary>
        /// <param name="ground">各セルへ設定する水文・Biome確定後の地面です。</param>
        /// <returns>テスト終了時に呼び出し側が破棄するNativeArrayです。</returns>
        private static NativeArray<GeneratedTile> CreateFilledChunk(GroundType ground)
        {
            var tiles = new NativeArray<GeneratedTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.TempJob);
            for (int index = 0; index < tiles.Length; index++)
            {
                tiles[index] = new GeneratedTile { Ground = ground };
            }

            return tiles;
        }

    }
}
