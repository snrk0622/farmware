// このファイルは、一体型の土地・岩壁Sprite、液面アニメーション、岩壁際の資源レイヤー分類をSceneなしのEditModeで検証します。
// ChunkTilemapPresenterとEditor構築ツールが同じ定数を使い、Pivotや表示高がずれないことを素早く保証します。
// 影マスクの同期Burst処理も、チャンク境界とワールド端で従来の直接判定と一致することを保証します。

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
        /// 中心に生成式と異なる確定データを用意し、全6400セルが従来のBuffer参照／外周再生成と一致することを保証します。
        /// 負座標・遠方・int四隅と別Seedを含み、ワールド外を非岩壁として扱う規則も確認します。
        /// </summary>
        [TestCase("Farmware", 0, 0)]
        [TestCase("Farmware", 7, -1)]
        [TestCase("Farmware", 15, -1)]
        [TestCase("other-shadow-seed", 7, -1)]
        [TestCase("Farmware", -8, -1)]
        [TestCase("Farmware", 100000, -100000)]
        [TestCase("Farmware", 33554431, 33554431)]
        [TestCase("Farmware", -33554432, -33554432)]
        [TestCase("Farmware", 33554431, -33554432)]
        [TestCase("Farmware", -33554432, 33554431)]
        public void FillShadowMask_MatchesBufferAndDirectHalo(string seed, int chunkX, int chunkY)
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seed), 1);
            using var tileStorage = new NativeArray<GeneratedTile>(WorldGridConstants.TilesPerChunk, Allocator.TempJob);
            // using変数は値型として読み取り専用になるため、要素を書き込む借用コピーを分けます。破棄はtileStorageだけが担当します。
            NativeArray<GeneratedTile> tiles = tileStorage;
            int size = RockWallAppearanceUtility.ShadowMaskSizeInTiles;
            using var mask = new NativeArray<bool>(size * size, Allocator.TempJob);
            for (int index = 0; index < tiles.Length; index++)
            {
                // 任意の確定地形を渡し、中心まで再生成する誤実装を検出します。
                tiles[index] = new GeneratedTile { Ground = index % 3 == 0 ? GroundType.RockWall : GroundType.Water };
            }

            // 同じ出力を異なる中心地形で再利用し、前回のtrue／falseを残さず全件上書きすることも確認します。
            for (int pass = 0; pass < 2; pass++)
            {
                RockWallAppearanceUtility.FillShadowMask(settings, new ChunkCoordinate(chunkX, chunkY), tiles, mask);
                for (int y = 0; y < size; y++)
                {
                    int localY = y - RockWallAppearanceUtility.ShadowSearchRadiusInTiles - 1;
                    for (int x = 0; x < size; x++)
                    {
                        int localX = x - RockWallAppearanceUtility.ShadowSearchRadiusInTiles - 1;
                        bool expected = false;
                        if (localX >= 0 && localX < 64 && localY >= 0 && localY < 64)
                        {
                            expected = tiles[WorldGrid.ToTileIndex(localX, localY)].Ground == GroundType.RockWall;
                        }
                        else if (WorldGrid.TryToWorldTileCoordinate(chunkX, chunkY, localX, localY,
                                     out int worldX, out int worldY))
                        {
                            expected = DeterministicChunkGenerator.IsRockWall(settings, worldX, worldY);
                        }

                        Assert.That(mask[y * size + x], Is.EqualTo(expected),
                            $"Chunk=({chunkX},{chunkY}), Local=({localX},{localY}), Pass={pass}");
                    }
                }

                for (int index = 0; index < tiles.Length; index++)
                {
                    bool wasRockWall = pass == 0 && index % 3 == 0;
                    Assert.That(tiles[index].Ground == GroundType.RockWall, Is.EqualTo(wasRockWall),
                        "読み取り専用の入力地形を書き換えてはいけません。");
                    // 次回は非岩壁だけにして、マスク内に古い岩壁が残る実装を検出します。
                    if (pass == 0)
                    {
                        tiles[index] = new GeneratedTile { Ground = GroundType.Water };
                    }
                }
            }
        }

        /// <summary>不正な配列長を準備し、Jobを実行する前に明確な例外で拒否することを保証します。</summary>
        [TestCase(0, 6400)]
        [TestCase(4095, 6400)]
        [TestCase(4096, 6399)]
        public void FillShadowMask_InvalidArrayLengths_RejectsBeforeRunning(int tileCount, int maskCount)
        {
            using var tiles = new NativeArray<GeneratedTile>(tileCount, Allocator.TempJob);
            using var mask = new NativeArray<bool>(maskCount, Allocator.TempJob);
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(1, 1);
            Assert.Throws<ArgumentException>(() => RockWallAppearanceUtility.FillShadowMask(
                settings, new ChunkCoordinate(0, 0), tiles, mask));
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

    }
}
