// このファイルは、Inspectorで編集したワールド生成設定をBaker経由でECSのSingleton Componentへ変換します。
// MonoBehaviourは入力用に限定し、実行中の生成Systemは変換後のWorldGenerationSettingsだけを正として参照します。

using Unity.Entities;
using UnityEngine;

namespace Farmware.WorldGeneration.Authoring
{
    /// <summary>
    /// SceneまたはSubSceneからワールド生成設定を入力するためのAuthoring用MonoBehaviourです。
    /// 開発用シードと気候閾値をInspectorで調整し、Bakerが実行時Entityデータへ変換します。
    /// </summary>
    public sealed class WorldGenerationAuthoring : MonoBehaviour
    {
        // 開発者がInspectorで入力し、Bake時に安定64bit値へ変換されるシード文字列です。
        [Tooltip("開発用ワールドのシード文字列です。数字以外や日本語も使用できます。同じ文字列とGeneratorVersionなら同じ地形になります。")]
        [SerializeField] private string seedText = "Farmware";

        // 互換性のない生成規則変更時に開発者が更新し、生成済みチャンクへ記録するバージョンです。
        [Tooltip("生成規則の互換バージョンです。地形結果が変わる修正を行ったときだけ増やします。既存チャンクは記録済みのバージョンを維持します。")]
        [Min(1)]
        [SerializeField] private int generatorVersion = 2;

        // 標高の広がりを決める最も低周波な格子サイズで、Bake時からワールド寿命中は固定します。
        [Tooltip("標高ノイズの基準格子サイズです。単位はタイルです。大きくすると水域と岩地が広く続き、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int elevationCellSizeInTiles = 192;

        // 気温バイオームの広がりを決める格子サイズで、複数チャンクにまたがるよう初期値を設定します。
        [Tooltip("気温ノイズの基準格子サイズです。単位はタイルです。大きくすると同じ気温帯が広く続き、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int temperatureCellSizeInTiles = 192;

        // 湿度バイオームの広がりを決める格子サイズで、森林と草原のまとまりへ影響します。
        [Tooltip("湿度ノイズの基準格子サイズです。単位はタイルです。大きくすると森林や乾燥地が広く続き、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int moistureCellSizeInTiles = 160;

        // 低周波へ重ねる細部の段数で、Bake時にECS設定へコピーされます。
        [Tooltip("重ねるノイズ解像度の数です。増やすと細部が増えますが生成計算が重くなり、生成結果も変わります。初期候補は3〜5です。")]
        [Range(1, 6)]
        [SerializeField] private int octaveCount = 4;

        // 論理Biome IDを変えず、境界付近の地面種別だけを座標ハッシュで段階的に混ぜる幅です。
        [Tooltip("バイオーム境界で草地・植生・砂地を混ぜる帯のおおよその幅です。単位はタイルです。0で無効、2〜8を推奨します。変更すると生成結果が変わります。")]
        [Range(0, 16)]
        [SerializeField] private int biomeTransitionWidthInTiles = 6;

        // 0〜1の標高に対する水面境界で、Inspector編集後にushortの固定値へ変換します。
        [Tooltip("これ未満の標高を水域にする閾値です。0〜1で指定し、大きくすると水域が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float waterThreshold = 0.305f;

        // 0〜1の標高に対する岩壁境界で、水面より必ず高くなるようOnValidateで補正します。
        [Tooltip("これ以上の標高を岩壁にする閾値です。0〜1で指定し、小さくすると岩壁が増えます。水域閾値より大きくしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float rockWallThreshold = 0.816f;

        // 砂漠判定に使う0〜1の気温境界で、Bake時に固定小数点の閾値へ変換します。
        [Tooltip("これ以上を高温とみなす気温閾値です。0〜1で指定し、小さくすると砂漠候補が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float hotTemperatureThreshold = 0.61f;

        // 砂漠判定に使う0〜1の乾燥境界で、湿潤境界より必ず低く保ちます。
        [Tooltip("これ未満を乾燥とみなす湿度閾値です。0〜1で指定し、大きくすると砂漠候補が増えます。森林閾値より小さくしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float dryMoistureThreshold = 0.435f;

        // 森林判定に使う0〜1の湿潤境界で、Bake時に固定小数点の閾値へ変換します。
        [Tooltip("これ以上を森林候補とみなす湿度閾値です。0〜1で指定し、小さくすると森林が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float wetMoistureThreshold = 0.603f;

        // 可視範囲の外側で事前生成する幅で、最低1チャンクを確保してカメラ移動時の空白を減らします。
        [Tooltip("カメラ表示範囲の外側へ先読みする幅です。単位は論理チャンクです。大きくすると高速パンに強くなりますが、生成量とメモリ使用量が増えます。")]
        [Min(1)]
        [SerializeField] private int preloadMarginInChunks = 1;

        // プリロード外へ出た生成済みデータをすぐ捨てず、往復時の再生成を抑える保持幅です。
        [Tooltip("プリロード範囲のさらに外側で生成済みデータを保持する幅です。単位は論理チャンクです。大きくすると往復時の再生成が減りますが、メモリ使用量が増えます。")]
        [Min(0)]
        [SerializeField] private int retentionMarginInChunks = 1;

        // 同時に生成待ちまたは生成中にできるチャンク数で、急なカメラ移動時のCPU負荷を制限します。
        [Tooltip("同時に生成待ちまたは生成中にできるチャンク数です。増やすと地形が早く揃いますが、一時的なCPU負荷が高くなります。")]
        [Min(1)]
        [SerializeField] private int maximumConcurrentGenerations = 2;

        // Tilemapへ1フレームに反映・消去できる16×16タイル領域数で、メインスレッドの停止時間を制御します。
        [Tooltip("1フレームにTilemapへ反映または消去する16×16サブチャンク数です。増やすと表示が早く揃いますが、メインスレッド負荷が高くなります。")]
        [Min(1)]
        [SerializeField] private int maximumSubChunkApplicationsPerFrame = 4;

        /// <summary>
        /// Inspector値が変更されたときにUnity Editorから呼ばれ、矛盾する閾値を安全な順序へ補正します。
        /// 実行時の地形を変更する処理ではなく、Bake前のAuthoringデータだけを整えます。
        /// </summary>
        private void OnValidate()
        {
            const float minimumGap = 0.001f;

            // 水面と岩壁が逆転すると陸地分類が成立しないため、岩壁側をわずかに上へ保ちます。
            waterThreshold = Mathf.Min(waterThreshold, 1f - minimumGap);
            rockWallThreshold = Mathf.Max(rockWallThreshold, waterThreshold + minimumGap);
            rockWallThreshold = Mathf.Clamp01(rockWallThreshold);

            // 乾燥と湿潤の領域が逆転しないよう、森林閾値を乾燥閾値より上へ保ちます。
            dryMoistureThreshold = Mathf.Min(dryMoistureThreshold, 1f - minimumGap);
            wetMoistureThreshold = Mathf.Max(wetMoistureThreshold, dryMoistureThreshold + minimumGap);
            wetMoistureThreshold = Mathf.Clamp01(wetMoistureThreshold);
        }

        /// <summary>
        /// 0〜1のInspector値を、生成Jobが整数演算で使う0〜65535の閾値へ変換します。
        /// </summary>
        /// <param name="normalizedValue">Inspectorから渡される0〜1の値です。</param>
        /// <returns>最も近い0〜65535の整数値です。</returns>
        private static ushort ToNormalizedUShort(float normalizedValue)
        {
            return (ushort)Mathf.RoundToInt(Mathf.Clamp01(normalizedValue) * ushort.MaxValue);
        }

        /// <summary>
        /// WorldGenerationAuthoringを実行時のWorldGenerationSettings Entityへ変換します。
        /// Bake時だけMonoBehaviourを読み、実行中の生成JobへUnityEngine.Object参照を持ち込みません。
        /// </summary>
        private sealed class WorldGenerationBaker : Baker<WorldGenerationAuthoring>
        {
            /// <summary>
            /// Authoring値を読み取り、Transformを必要としない設定EntityへComponentを追加します。
            /// </summary>
            /// <param name="authoring">SceneまたはPrefabに配置された入力元MonoBehaviourです。</param>
            public override void Bake(WorldGenerationAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new WorldGenerationSettings
                {
                    WorldSeed = WorldSeedUtility.FromString(authoring.seedText),
                    GeneratorVersion = authoring.generatorVersion,
                    ElevationCellSizeInTiles = authoring.elevationCellSizeInTiles,
                    TemperatureCellSizeInTiles = authoring.temperatureCellSizeInTiles,
                    MoistureCellSizeInTiles = authoring.moistureCellSizeInTiles,
                    OctaveCount = authoring.octaveCount,
                    BiomeTransitionWidthInTiles = authoring.biomeTransitionWidthInTiles,
                    WaterThreshold = ToNormalizedUShort(authoring.waterThreshold),
                    RockWallThreshold = ToNormalizedUShort(authoring.rockWallThreshold),
                    HotTemperatureThreshold = ToNormalizedUShort(authoring.hotTemperatureThreshold),
                    DryMoistureThreshold = ToNormalizedUShort(authoring.dryMoistureThreshold),
                    WetMoistureThreshold = ToNormalizedUShort(authoring.wetMoistureThreshold)
                });
                AddComponent(entity, new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = authoring.preloadMarginInChunks,
                    RetentionMarginInChunks = authoring.retentionMarginInChunks,
                    MaximumConcurrentGenerations = authoring.maximumConcurrentGenerations,
                    MaximumSubChunkApplicationsPerFrame = authoring.maximumSubChunkApplicationsPerFrame
                });
            }
        }
    }
}
