// このファイルは、64×48液体水際入力から7マスク×4フレームを作るEditor生成規則を検証します。
// PNGやAssetを実際には書き換えず、方向行、3bit順、Alpha合成、固定ファイル名だけを純粋計算で保証します。

using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 人間が12セルだけを描けば、Runtime用の28 Spriteと7形状へ欠落なく展開できることを保証します。
    /// </summary>
    public sealed class TerrainLiquidEdgeWaveGeneratorTests
    {
        /// <summary>4列×3行入力と7列×4行出力の固定寸法を保証します。</summary>
        [Test]
        public void SheetContract_UsesTwelveSourcesAndTwentyEightOutputs()
        {
            Assert.That(TerrainLiquidEdgeWaveGenerator.SourceWidthInPixels, Is.EqualTo(64));
            Assert.That(TerrainLiquidEdgeWaveGenerator.SourceHeightInPixels, Is.EqualTo(48));
            Assert.That(TerrainLiquidEdgeWaveGenerator.OutputWidthInPixels, Is.EqualTo(112));
            Assert.That(TerrainLiquidEdgeWaveGenerator.OutputHeightInPixels, Is.EqualTo(64));
            Assert.That(
                TerrainLiquidEdgeWaveGenerator.FrameCount
                * TerrainLiquidEdgeWaveGenerator.DirectionCount,
                Is.EqualTo(12));
            Assert.That(
                TerrainLiquidEdgeWaveGenerator.FrameCount
                * TerrainLiquidEdgeWaveGenerator.MaskCount,
                Is.EqualTo(28));
        }

        /// <summary>固定3ファイルだけを液体種別へ対応付け、材質Sourceや任意PNGを誤処理しないことを保証します。</summary>
        [Test]
        public void SourcePath_RecognizesOnlyThreeDocumentedFiles()
        {
            string folder = TerrainLiquidEdgeWaveGenerator.SourceFolder;
            Assert.That(
                TerrainLiquidEdgeWaveGenerator.TryGetLiquidKind(
                    $"{folder}/FreshWaterEdgeWave.png",
                    out TerrainLiquidVisualKind fresh),
                Is.True);
            Assert.That(fresh, Is.EqualTo(TerrainLiquidVisualKind.FreshWater));
            Assert.That(
                TerrainLiquidEdgeWaveGenerator.TryGetLiquidKind(
                    $"{folder}/SaltWaterEdgeWave.png",
                    out TerrainLiquidVisualKind salt),
                Is.True);
            Assert.That(salt, Is.EqualTo(TerrainLiquidVisualKind.SaltWater));
            Assert.That(
                TerrainLiquidEdgeWaveGenerator.TryGetLiquidKind(
                    $"{folder}/MagmaEdgeGlow.png",
                    out TerrainLiquidVisualKind magma),
                Is.True);
            Assert.That(magma, Is.EqualTo(TerrainLiquidVisualKind.Magma));
            Assert.That(
                TerrainLiquidEdgeWaveGenerator.TryGetLiquidKind(
                    "Assets/Farmware/Art/WorldGeneration/Source/GrassLoam.png",
                    out _),
                Is.False);
        }

        /// <summary>Back、Left、Rightの各画素がbit 1、2、4と全組合せへ正しく展開されることを保証します。</summary>
        [Test]
        public void ComposeMasks_MapsDirectionRowsToThreeBits()
        {
            var source = new Color32[
                TerrainLiquidEdgeWaveGenerator.SourceWidthInPixels
                * TerrainLiquidEdgeWaveGenerator.SourceHeightInPixels];
            Color32 back = new Color32(10, 20, 30, 255);
            Color32 left = new Color32(40, 50, 60, 255);
            Color32 right = new Color32(70, 80, 90, 255);
            SetSourcePixel(source, frame: 0, rowFromBottom: 2, back);
            SetSourcePixel(source, frame: 0, rowFromBottom: 1, left);
            SetSourcePixel(source, frame: 0, rowFromBottom: 0, right);

            Color32[] output = TerrainLiquidEdgeWaveGenerator.ComposeMasks(source);

            Assert.That(GetOutputPixel(output, maskValue: 1, frame: 0), Is.EqualTo(back));
            Assert.That(GetOutputPixel(output, maskValue: 2, frame: 0), Is.EqualTo(left));
            Assert.That(GetOutputPixel(output, maskValue: 4, frame: 0), Is.EqualTo(right));
            Assert.That(
                GetOutputPixel(output, maskValue: 7, frame: 0),
                Is.EqualTo(right),
                "3方向の交点では後から合成するRightの不透明画素を採用します。");
        }

        /// <summary>入力Sheetの各方向セル左下画素へテスト色を設定します。</summary>
        /// <param name="pixels">64×48入力画素です。</param>
        /// <param name="frame">左から0〜3のフレームです。</param>
        /// <param name="rowFromBottom">下からRight=0、Left=1、Back=2です。</param>
        /// <param name="color">設定する不透明色です。</param>
        private static void SetSourcePixel(
            Color32[] pixels,
            int frame,
            int rowFromBottom,
            Color32 color)
        {
            int x = frame * TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
            int y = rowFromBottom * TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
            pixels[(y * TerrainLiquidEdgeWaveGenerator.SourceWidthInPixels) + x] = color;
        }

        /// <summary>完成Atlasの指定マスク・フレームセル左下画素を読みます。</summary>
        /// <param name="pixels">112×64完成画素です。</param>
        /// <param name="maskValue">1〜7の3bit値です。</param>
        /// <param name="frame">0〜3のフレームです。</param>
        /// <returns>指定セル左下のRGBA色です。</returns>
        private static Color32 GetOutputPixel(
            Color32[] pixels,
            int maskValue,
            int frame)
        {
            int x = (maskValue - 1) * TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
            int y = (TerrainLiquidEdgeWaveGenerator.FrameCount - 1 - frame)
                * TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
            return pixels[(y * TerrainLiquidEdgeWaveGenerator.OutputWidthInPixels) + x];
        }
    }
}
