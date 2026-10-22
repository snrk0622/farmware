// このファイルは、C4-E1aの完成チャンク診断器が製品Generatorを順番どおり実行し、所有配列を安全に破棄することを検証します。
// 1チャンクの有限条件だけを使い、NativeArrayの件数、二重破棄耐性、未接続Layer拒否をEditModeで保証します。

using Farmware.Simulation;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// C3とC4-A〜Dの完成結果を再構築するEditor診断境界と、Persistent配列の所有権を検証します。
    /// 複数Seed集計とEditorメニューはC4-E1bで別に検証します。
    /// </summary>
    public sealed class HydrologyDiagnosticChunkTests
    {
        /// <summary>
        /// Surface原点チャンクを完成生成し、4配列を仕様件数で公開した後、重複せず破棄できることを保証します。
        /// </summary>
        [Test]
        [Timeout(120000)]
        public void DiagnosticChunk_GeneratesCompleteC4BuffersAndCanBeDisposedRepeatedly()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("derived-hydrology-diagnostic-buffer-test"),
                1);
            var coordinate = new ChunkCoordinate(WorldLayer.Surface, 0, 0);

            bool generated = HydrologyDiagnosticChunk.TryGenerate(
                settings,
                coordinate,
                out HydrologyDiagnosticChunk chunk,
                out HydrologyDiagnosticGenerationStage stage);

            Assert.That(generated, Is.True);
            Assert.That(stage, Is.EqualTo(HydrologyDiagnosticGenerationStage.Complete));
            Assert.That(chunk, Is.Not.Null);
            using (chunk)
            {
                Assert.That(chunk.Coordinate, Is.EqualTo(coordinate));
                Assert.That(chunk.TerrainTiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(
                    chunk.HydrologyCells.Length,
                    Is.EqualTo(DeterministicHydrologyGenerator.CellsPerChunk));
                Assert.That(
                    chunk.SurfaceWaterTiles.Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(
                    chunk.HydrologyTiles.Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
            }

            // IDisposableは例外経路やusingの重なりでも再度呼ばれ得るため、
            // 2回目が解放済みNativeArrayを二重破棄せず、公開値も未確保へ戻ることを確認します。
            chunk.Dispose();
            Assert.That(chunk.TerrainTiles.IsCreated, Is.False);
            Assert.That(chunk.HydrologyCells.IsCreated, Is.False);
            Assert.That(chunk.SurfaceWaterTiles.IsCreated, Is.False);
            Assert.That(chunk.HydrologyTiles.IsCreated, Is.False);
        }

        /// <summary>
        /// 自然生成未接続のUnderwaterを渡し、地上結果を複製せず失敗として返し、NativeArrayを公開しないことを保証します。
        /// </summary>
        [Test]
        public void DiagnosticChunk_RejectsUnconnectedLayerWithoutPublishingArrays()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("derived-hydrology-layer-rejection"),
                1);

            bool generated = HydrologyDiagnosticChunk.TryGenerate(
                settings,
                new ChunkCoordinate(WorldLayer.Underwater, 0, 0),
                out HydrologyDiagnosticChunk chunk,
                out HydrologyDiagnosticGenerationStage stage);

            Assert.That(generated, Is.False);
            Assert.That(chunk, Is.Null);
            Assert.That(stage, Is.EqualTo(HydrologyDiagnosticGenerationStage.UnsupportedLayer));
        }
    }
}
