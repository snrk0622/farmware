// このファイルは、広域マップビューアが実ゲームと同じ座標・設定・Generator結果を表示することをEditModeで検証します。
// EditorWindowそのものを開かず、バックグラウンド化できる純粋サンプラーとAuthoring設定変換を対象にします。

using System.Threading;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// マップビューア用の代表座標、色変換、Authoring共有設定が実際の地形生成と一致することを保証します。
    /// </summary>
    public sealed class WorldGenerationMapViewerTests
    {
        /// <summary>
        /// 小さな表示範囲を生成し、全ピクセルが同じ座標へ直接GenerateTileした結果の色と一致することを検証します。
        /// ビューア専用の簡易バイオーム計算を追加して実マップとずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void Sample_EveryPixelMatchesDirectDeterministicGeneratorResult()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("map-viewer-shared-generator-test"),
                1);
            WorldGenerationMapSampleResult result = WorldGenerationMapSampler.Sample(
                settings,
                -65,
                130,
                7,
                5,
                8,
                WorldGenerationMapViewMode.Terrain,
                CancellationToken.None);

            // 全ピクセルを直接Generator結果と比較し、座標変換・配列順・色変換をまとめて保証します。
            for (int pixelY = 0; pixelY < result.Height; pixelY++)
            {
                for (int pixelX = 0; pixelX < result.Width; pixelX++)
                {
                    result.GetSampledWorldTile(
                        pixelX,
                        pixelY,
                        out int worldTileX,
                        out int worldTileY);
                    GeneratedTile directTile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        worldTileX,
                        worldTileY);
                    Color32 expectedColor = WorldGenerationMapSampler.GetDisplayColor(
                        WorldGenerationMapViewMode.Terrain,
                        directTile);

                    Assert.That(
                        result.PixelColors[(pixelY * result.Width) + pixelX],
                        Is.EqualTo(expectedColor),
                        $"pixel=({pixelX}, {pixelY}), tile=({worldTileX}, {worldTileY})");
                }
            }
        }

        /// <summary>
        /// 左上原点のPointer座標を負座標を含むワールド座標へ変換し、Canvas中央が指定中心へ一致することを検証します。
        /// </summary>
        [Test]
        public void CalculateWorldTileAtPointer_CenterAndCornersUseExpectedAxes()
        {
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                -100,
                200,
                4,
                400f,
                300f,
                200f,
                150f,
                out int centerX,
                out int centerY);
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                -100,
                200,
                4,
                400f,
                300f,
                0f,
                0f,
                out int topLeftX,
                out int topLeftY);

            Assert.That(centerX, Is.EqualTo(-100));
            Assert.That(centerY, Is.EqualTo(200));
            Assert.That(topLeftX, Is.EqualTo(-900));
            Assert.That(topLeftY, Is.EqualTo(800));
        }

        /// <summary>
        /// カーソル中心ズーム後も、そのPointer位置が同じワールドタイル付近を指すことを検証します。
        /// 整数タイルへ丸めるため、許容差は新しい1ピクセル幅以内とします。
        /// </summary>
        [Test]
        public void CalculateZoomedCenter_PreservesWorldPositionUnderPointer()
        {
            const float pointerX = 310f;
            const float pointerY = 70f;
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                50,
                -75,
                8,
                400f,
                300f,
                pointerX,
                pointerY,
                out int beforeX,
                out int beforeY);

            WorldGenerationMapSampler.CalculateZoomedCenter(
                50,
                -75,
                8,
                4,
                400f,
                300f,
                pointerX,
                pointerY,
                out int newCenterX,
                out int newCenterY);
            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                newCenterX,
                newCenterY,
                4,
                400f,
                300f,
                pointerX,
                pointerY,
                out int afterX,
                out int afterY);

            Assert.That(afterX, Is.EqualTo(beforeX).Within(4));
            Assert.That(afterY, Is.EqualTo(beforeY).Within(4));
        }

        /// <summary>
        /// Authoringへ別シードを渡した設定Snapshotが、その文字列を実ゲームと同じ安定64bit値へ変換することを検証します。
        /// Bakerも同じSnapshotメソッドを呼ぶため、EditorWindowだけ別の変換経路を持たないことを保証します。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_SeedOverrideUsesSharedStableConversion()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot("viewer-seed-override");

                Assert.That(
                    snapshot.WorldSeed,
                    Is.EqualTo(WorldSeedUtility.FromString("viewer-seed-override")));
                Assert.That(snapshot.GeneratorVersion, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>
        /// 新規Authoringの既定値と純粋計算・分布監査が使うCreateDefaultを準備し、主要な気候閾値が一致することを保証します。
        /// Inspectorの丸めた小数値だけが別の固定小数点へ変換され、実ゲームと監査結果がずれる退行を防ぎます。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_DefaultClimateThresholdsMatchSharedDefaults()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Defaults Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot();
                WorldGenerationSettings expected = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(authoring.SeedText),
                    1);

                Assert.That(snapshot.SeaLevelThreshold, Is.EqualTo(expected.SeaLevelThreshold));
                Assert.That(
                    snapshot.HotTemperatureThreshold,
                    Is.EqualTo(expected.HotTemperatureThreshold));
                Assert.That(
                    snapshot.DryMoistureThreshold,
                    Is.EqualTo(expected.DryMoistureThreshold));
                Assert.That(
                    snapshot.WetMoistureThreshold,
                    Is.EqualTo(expected.WetMoistureThreshold));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }

        /// <summary>
        /// 既存Sceneに保存されている旧丸め小数を準備し、Snapshot変換時だけ現在の共通既定値へ移行されることを保証します。
        /// Sceneファイルを外部から書き換えず、開いているUnity Editorの未保存状態と競合しない移行経路を検証します。
        /// </summary>
        [Test]
        public void AuthoringSnapshot_LegacyRoundedClimateDefaultsUseSharedDefaults()
        {
            GameObject authoringObject = new GameObject("WorldGenerationAuthoring Legacy Defaults Test");
            try
            {
                WorldGenerationAuthoring authoring =
                    authoringObject.AddComponent<WorldGenerationAuthoring>();
                SerializedObject serializedAuthoring = new SerializedObject(authoring);
                serializedAuthoring.FindProperty("waterThreshold").floatValue = 0.305f;
                serializedAuthoring.FindProperty("hotTemperatureThreshold").floatValue = 0.61f;
                serializedAuthoring.FindProperty("dryMoistureThreshold").floatValue = 0.435f;
                serializedAuthoring.FindProperty("wetMoistureThreshold").floatValue = 0.603f;
                serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();

                WorldGenerationSettings snapshot =
                    authoring.CreateWorldGenerationSettingsSnapshot();

                Assert.That(
                    snapshot.SeaLevelThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultSeaLevelThreshold));
                Assert.That(
                    snapshot.HotTemperatureThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultHotTemperatureThreshold));
                Assert.That(
                    snapshot.DryMoistureThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultDryMoistureThreshold));
                Assert.That(
                    snapshot.WetMoistureThreshold,
                    Is.EqualTo(WorldGenerationSettings.DefaultWetMoistureThreshold));
            }
            finally
            {
                Object.DestroyImmediate(authoringObject);
            }
        }
    }
}
