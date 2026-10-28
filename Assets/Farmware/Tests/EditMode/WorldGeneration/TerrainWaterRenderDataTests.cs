// このファイルは、水中断面が実際の岸の真下だけに存在することを検証します。
// 補間の滑らかさを正解にせず、他列の断面や距離を水域へ持ち込まない契約と、実Seedの淡水・海水境界回帰を確認します。
using Farmware.Simulation;
using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>人工の完成地形と実Seedの完成生成を用い、岸段差・水種境界での断面範囲と投影元を検証するEditModeテストです。</summary>
    public sealed class TerrainWaterRenderDataTests
    {
        /// <summary>報告地点のSeedと生成器版を固定し、淡水を挟む海水列が実際の岸の断面を失わないことを保証します。</summary>
        [Test]
        [Timeout(120000)]
        public void ReportedCoastalGroove_ProjectsAcrossFreshWaterToGround()
        {
            // (-290,204)はChunk(-5,3)のLocal(30,12)です。生成結果自体を変更する修正ではないため、
            // 版2の標準設定を使い、報告画像と同じ「海水2セル→淡水1セル→Ground」を先に確認します。
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(0x3EE49B3100677454UL, 2);
            Assert.That(HydrologyDiagnosticTerrainCatalogs.TryCreate(out HydrologyDiagnosticTerrainCatalogs catalogs), Is.True);
            using (catalogs)
            {
                Assert.That(HydrologyDiagnosticChunk.TryGenerate(settings, BiomeDefinitionCatalog.CreateDefault(),
                    new ChunkCoordinate(WorldLayer.Surface, -5, 3), catalogs,
                    out HydrologyDiagnosticChunk chunk, out HydrologyDiagnosticGenerationStage generationStage), Is.True);
                using (chunk)
                {
                    Assert.That(generationStage, Is.EqualTo(HydrologyDiagnosticGenerationStage.Complete));
                    var neighborhood = new CompletedChunkTerrainNeighborhood(default, default, default, default,
                        chunk.TerrainCells, default, default, default, default);
                    var ids = chunk.TerrainRuntimeIds;
                    Assert.That(TerrainLiquidPresentationUtility.Classify(chunk.TerrainCells[12 * 64 + 30], ids), Is.EqualTo(TerrainLiquidVisualKind.SaltWater));
                    Assert.That(TerrainLiquidPresentationUtility.Classify(chunk.TerrainCells[13 * 64 + 30], ids), Is.EqualTo(TerrainLiquidVisualKind.SaltWater));
                    Assert.That(TerrainLiquidPresentationUtility.Classify(chunk.TerrainCells[14 * 64 + 30], ids), Is.EqualTo(TerrainLiquidVisualKind.FreshWater));
                    Assert.That(chunk.TerrainCells[15 * 64 + 30].GroundBlockRuntimeId.IsValid, Is.True);

                    // 手前の海水も同じ列のY=207の岸へ届き、誤った飽和段6ではなく段2・1を使います。
                    for (int localY = 12; localY <= 14; localY++)
                    {
                        Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                            neighborhood, 30, localY, ids, out TerrainLiquidFaceProjection face), Is.True);
                        Assert.That(face.SourceLocalX, Is.EqualTo(30));
                        Assert.That(face.SourceLocalY, Is.EqualTo(15));
                        Assert.That(face.FaceDepth, Is.EqualTo(14 - localY));
                        Assert.That(TerrainLiquidPresentationUtility.GetClampedWaterDepth(neighborhood, 30, localY, ids), Is.EqualTo(14 - localY));
                    }
                }
            }
        }

        /// <summary>混在水域の探索が北隣チャンクへ続き、未取得のチャンクだけは推測で越えないことを保証します。</summary>
        /// <param name="northAvailable">隣チャンクが完成している場合はtrue、未取得ならfalseです。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void MixedWater_SearchesCrossOnlyAvailableChunkBoundary(bool northAvailable)
        {
            // NativeArrayはこのテストが所有し、近傍値には読み取り中だけ貸します。
            using (var centerOwner = new NativeArray<GeneratedTerrainCell>(4096, Allocator.Temp))
            using (var northOwner = new NativeArray<GeneratedTerrainCell>(4096, Allocator.Temp))
            {
                var center = centerOwner;
                var north = northOwner;
                var ids = new BuiltInTerrainBlockRuntimeIds
                {
                    FreshWater = new DefinitionRuntimeId(101),
                    SaltWater = new DefinitionRuntimeId(102)
                };
                center[63 * 64 + 30] = new GeneratedTerrainCell { LiquidRuntimeId = ids.SaltWater };
                north[30] = new GeneratedTerrainCell { LiquidRuntimeId = ids.FreshWater };
                north[64 + 30] = new GeneratedTerrainCell
                {
                    GroundBlockRuntimeId = new DefinitionRuntimeId(201),
                    WallBlockRuntimeId = new DefinitionRuntimeId(301)
                };
                var neighborhood = new CompletedChunkTerrainNeighborhood(default, default, default, default, center,
                    default, default, northAvailable ? north : default, default);

                Assert.That(TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    neighborhood, 30, 63, ids, out TerrainLiquidFaceProjection face), Is.EqualTo(northAvailable));
                Assert.That(TerrainLiquidPresentationUtility.GetClampedWaterDepth(neighborhood, 30, 63, ids), Is.EqualTo(northAvailable ? 1 : 6));
                Assert.That(TerrainLiquidPresentationUtility.TryFindWallReflectionSource(neighborhood, 30, 63, ids,
                    TerrainLiquidVisualKind.SaltWater, out TerrainMaterialFaceProjection reflection), Is.EqualTo(northAvailable));
                if (northAvailable)
                {
                    // 戻り値は中心基準の65であり、隣チャンクのLocal=1へ誤って縮めません。
                    Assert.That(face.SourceLocalY, Is.EqualTo(65));
                    Assert.That(face.FaceDepth, Is.EqualTo(1));
                    Assert.That(reflection.SourceLocalY, Is.EqualTo(65));
                }
            }
        }

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
