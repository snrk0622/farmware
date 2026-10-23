// このファイルは、地質分布監査が使用する広域サンプル設定だけを保持します。
// C5-Eのバイオーム監査は完成チャンクを調べるため、BiomeDistributionAuditSettingsを別に使用します。

using System;
using System.Collections.Generic;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 地質分布監査で使用するシード、サンプル範囲、間隔、生成器バージョンをまとめます。
    /// GeologyDistributionAuditMenuとEditModeテストが同じ入力形式を共有します。
    /// </summary>
    public sealed class WorldGenerationDistributionAuditSettings
    {
        /// <summary>監査対象にするワールドシード文字列です。監査開始後は変更されません。</summary>
        public IReadOnlyList<string> SeedTexts { get; }

        /// <summary>原点から縦横へ何サンプル調べるかを保持します。実タイル距離ではありません。</summary>
        public int SampleRadiusInSteps { get; }

        /// <summary>隣り合う代表サンプル間の距離です。単位はワールドタイルです。</summary>
        public int SampleSpacingInTiles { get; }

        /// <summary>監査対象のGeneratorVersionです。通常のワールド再生成と同じ値を渡します。</summary>
        public int GeneratorVersion { get; }

        /// <summary>
        /// 検証条件を固定し、同じ条件の監査結果を調整前後で比較できる設定を作ります。
        /// </summary>
        /// <param name="seedTexts">1件以上のワールドシード文字列です。</param>
        /// <param name="sampleRadiusInSteps">原点から各軸へ調べるサンプル数です。0以上を指定します。</param>
        /// <param name="sampleSpacingInTiles">代表サンプル同士の間隔です。1タイル以上を指定します。</param>
        /// <param name="generatorVersion">生成結果へ渡すGeneratorVersionです。</param>
        public WorldGenerationDistributionAuditSettings(
            string[] seedTexts,
            int sampleRadiusInSteps,
            int sampleSpacingInTiles,
            int generatorVersion)
        {
            if (seedTexts == null || seedTexts.Length == 0)
            {
                throw new ArgumentException("分布監査には1件以上のシードが必要です。", nameof(seedTexts));
            }

            if (sampleRadiusInSteps < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleRadiusInSteps),
                    "サンプル半径は0以上である必要があります。");
            }

            if (sampleSpacingInTiles <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleSpacingInTiles),
                    "サンプル間隔は1タイル以上である必要があります。");
            }

            string[] ownedSeedTexts = (string[])seedTexts.Clone();
            for (int seedIndex = 0; seedIndex < ownedSeedTexts.Length; seedIndex++)
            {
                if (string.IsNullOrWhiteSpace(ownedSeedTexts[seedIndex]))
                {
                    throw new ArgumentException(
                        $"分布監査のシード文字列が空です。index={seedIndex}",
                        nameof(seedTexts));
                }
            }

            // 呼び出し元が監査中に配列を書き換えて結果を変えないよう、複製を読み取り専用の型で公開します。
            SeedTexts = ownedSeedTexts;
            SampleRadiusInSteps = sampleRadiusInSteps;
            SampleSpacingInTiles = sampleSpacingInTiles;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>
        /// 通常の開発サイクルでEditorを長時間停止させず、広域傾向を比較できる既定設定を返します。
        /// </summary>
        /// <returns>3シードを32タイル間隔の129×129地点で調べる設定です。</returns>
        public static WorldGenerationDistributionAuditSettings CreateDefault()
        {
            return new WorldGenerationDistributionAuditSettings(
                new[]
                {
                    "distribution-audit-plain",
                    "distribution-audit-river",
                    "distribution-audit-volcanic"
                },
                64,
                32,
                WorldGenerationSettings.CurrentGeneratorVersion);
        }
    }
}
