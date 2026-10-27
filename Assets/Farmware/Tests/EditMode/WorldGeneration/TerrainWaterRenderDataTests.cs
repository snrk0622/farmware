// このファイルは、水中断面が実際の岸の真下だけに存在することを検証します。
// 補間の滑らかさを正解にせず、他列の断面や距離を水域へ持ち込まない契約を確認します。
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>完成地形を用い、岸段差に対する断面の描画範囲・投影元を検証する純粋計算テストです。</summary>
    public sealed class TerrainWaterRenderDataTests
    {
        /// <summary>左右の岸Y差があっても、投影は各列自身の岸だけを使うことを保証します。</summary>
        /// <param name="difference">岸Y座標の差です。実際の標高差ではありません。</param>
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(9)]
        public void SteppedBank_ProjectsOnlyItsOwnColumn(int difference)
        {
            using (var owner = new NativeArray<GeneratedTerrainCell>(4096, Allocator.Temp))
            {
                // using所有変数の寿命は維持し、同じメモリの借用コピーから地形を準備します。
                var terrain = owner;
                var ids = new BuiltInTerrainBlockRuntimeIds { SaltWater = new DefinitionRuntimeId(102) };
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        terrain[y * 64 + x] = y < (x <= 20 ? 50 : 50 - difference)
                            ? new GeneratedTerrainCell { LiquidRuntimeId = ids.SaltWater }
                            : new GeneratedTerrainCell { GroundBlockRuntimeId = new DefinitionRuntimeId(201) };
                var n = new CompletedChunkTerrainNeighborhood(default, default, default, default, terrain,
                    default, default, default, default);
                for (int x = 20; x <= 21; x++)
                {
                    int bankY = x == 20 ? 50 : 50 - difference;
                    for (int y = 20; y < bankY; y++)
                    {
                        bool projected = TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(n, x, y, ids, out var face);
                        Assert.That(projected, Is.EqualTo(bankY - y <= 6));
                        if (!projected) continue;
                        Assert.That(face.SourceLocalX, Is.EqualTo(x), "隣列の岸から断面を借りてはいけません。");
                        Assert.That(face.SourceLocalY, Is.EqualTo(bankY));
                        Assert.That(face.FaceDepth, Is.EqualTo(bankY - y - 1));
                    }
                }
            }
        }

        /// <summary>手前・横の陸地へ接するだけでは、水中断面が生成されないことを保証します。</summary>
        [Test]
        public void SideAndFrontLand_DoNotCreateSubmergedFace()
        {
            using (var owner = new NativeArray<GeneratedTerrainCell>(4096, Allocator.Temp))
            {
                var terrain = owner;
                var ids = new BuiltInTerrainBlockRuntimeIds { FreshWater = new DefinitionRuntimeId(101) };
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        terrain[y * 64 + x] = x <= 20 || y <= 20
                            ? new GeneratedTerrainCell { GroundBlockRuntimeId = new DefinitionRuntimeId(201) }
                            : new GeneratedTerrainCell { LiquidRuntimeId = ids.FreshWater };
                var n = new CompletedChunkTerrainNeighborhood(default, default, default, default, terrain,
                    default, default, default, default);
                Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(n, 21, 21, ids, out _), Is.False);
                Assert.That(TerrainLiquidPresentationUtility.GetClampedWaterDepth(n, 21, 21, ids), Is.EqualTo(6));
            }
        }
    }
}
