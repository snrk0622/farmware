// このファイルは、Inspectorで編集した地形・資源生成設定をBaker経由でECSのSingleton Componentへ変換します。
// MonoBehaviourは入力用に限定し、実行中の生成Systemは変換後のECS設定だけを正として参照します。

using Unity.Entities;
using UnityEngine;

namespace Farmware.WorldGeneration.Authoring
{
    /// <summary>
    /// SceneまたはSubSceneからワールド生成設定を入力するためのAuthoring用MonoBehaviourです。
    /// 開発用シード、気候閾値、資源密度をInspectorで調整し、Bakerが実行時Entityデータへ変換します。
    /// </summary>
    public sealed class WorldGenerationAuthoring : MonoBehaviour
    {
        // 開発者がInspectorで入力し、Bake時に安定64bit値へ変換されるシード文字列です。
        [Tooltip("開発用ワールドのシード文字列です。数字以外や日本語も使用できます。同じ文字列とGeneratorVersionなら同じ地形になります。")]
        [SerializeField] private string seedText = "Farmware";

        // 初期開発中は1へ固定し、正式なセーブ互換運用を始めた後だけ非互換変更に合わせて更新します。
        [Tooltip("生成規則の互換バージョンです。初期開発中は1へ固定し、正式なセーブ互換運用を始めた後だけ非互換変更時に増やします。")]
        [Min(1)]
        [SerializeField] private int generatorVersion = 1;

        // 標高の広がりを決める最も低周波な格子サイズで、Bake時からワールド寿命中は固定します。
        [Tooltip("標高ノイズの基準格子サイズです。単位はタイルです。大きくすると水域と岩地が広く続き、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int elevationCellSizeInTiles = 192;

        // 気温バイオームの広がりを決める格子サイズで、約1.5論理チャンクのまとまりになる初期値です。
        [Tooltip("気温ノイズの基準格子サイズです。単位はタイルです。大きくすると同じ気温帯が広く続き、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int temperatureCellSizeInTiles = 96;

        // 湿度バイオームの広がりを決める格子サイズで、約1.25論理チャンクのまとまりになる初期値です。
        [Tooltip("湿度ノイズの基準格子サイズです。単位はタイルです。大きくすると森林や乾燥地が広く続き、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int moistureCellSizeInTiles = 80;

        // 低周波へ重ねる細部の段数で、Bake時にECS設定へコピーされます。
        [Tooltip("重ねるノイズ解像度の数です。増やすと細部が増えますが生成計算が重くなり、生成結果も変わります。初期候補は3〜5です。")]
        [Range(1, 6)]
        [SerializeField] private int octaveCount = 4;

        // 論理Biome IDを変えず、境界付近の地面種別だけを座標ハッシュで段階的に混ぜる幅です。
        [Tooltip("バイオーム境界で草地・植生・砂地を混ぜる帯のおおよその幅です。単位はタイルです。0で無効、2〜8を推奨します。変更すると生成結果が変わります。")]
        [Range(0, 16)]
        [SerializeField] private int biomeTransitionWidthInTiles = 6;

        // 0〜1の標高に対する海面境界です。既存Sceneとの互換のためフィールド名はwaterThresholdを維持します。
        [Tooltip("これ未満の標高を海水域にする海面閾値です。0〜1で指定し、大きくすると海が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float waterThreshold = 0.305f;

        // 川の大きな蛇行を決める低周波格子で、チャンク境界より大きくして短い断片を減らします。
        [Tooltip("川の蛇行を作るノイズ格子サイズです。単位はタイルです。大きくすると川が緩やかに曲がり、変更すると生成結果が変わります。")]
        [Min(16)]
        [SerializeField] private int riverCellSizeInTiles = 96;

        // 池のまとまりを決める格子で、川より小さくして内陸に局所的な水面を作ります。
        [Tooltip("池の形を作るノイズ格子サイズです。単位はタイルです。大きくすると池が広くまとまり、変更すると生成結果が変わります。")]
        [Min(8)]
        [SerializeField] private int pondCellSizeInTiles = 48;

        // 淡水形状へ重ねる解像度数で、細かすぎる水路を避けるため地形より少なくします。
        [Tooltip("川と池の輪郭へ重ねるノイズ解像度数です。増やすと輪郭が細かくなりますが生成負荷も増えます。")]
        [Range(1, 4)]
        [SerializeField] private int freshWaterOctaveCount = 2;

        // ノイズ中心値の周囲を川として扱う幅で、0〜1正規化値をBake時に固定小数点へ変換します。
        [Tooltip("川の中心線から許容するノイズ差です。0〜1で指定し、大きくすると川幅と本数が増えます。")]
        [Range(0.001f, 0.1f)]
        [SerializeField] private float riverCenterLineHalfWidth = 600f / ushort.MaxValue;

        // 池ノイズの下限で、高い場所だけを水面にして群れ状の池を作ります。
        [Tooltip("池を生成するノイズ下限です。0〜1で指定し、小さくすると池が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float pondThreshold = 57000f / ushort.MaxValue;

        // 淡水を生成できる標高上限で、岩壁内や山頂を川が横切ることを防ぎます。
        [Tooltip("川と池を生成できる最大標高です。0〜1で指定し、小さくすると淡水が低地へ限定されます。海面より高く、岩壁閾値より低くしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float freshWaterMaximumElevation = 47000f / ushort.MaxValue;

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

        // 木・石・水・砂が1タイルずつ散らばらず、まとまった採取場所になるように使う低周波格子です。
        [Tooltip("基礎資源の群れを作るノイズ格子サイズです。単位はタイルです。大きくすると同じ資源が広くまとまり、変更すると資源配置が変わります。")]
        [Min(4)]
        [SerializeField] private int resourceClusterCellSizeInTiles = 24;

        // 資源群の輪郭へ追加する細部の段数で、地形より少なくして細かな点在を抑えます。
        [Tooltip("資源群ノイズへ重ねる解像度の数です。増やすと輪郭が細かくなりますが生成負荷も増えます。初期値は2です。")]
        [Range(1, 4)]
        [SerializeField] private int resourceOctaveCount = 2;

        // 樹木群の密度を決める0〜1の下限で、低くすると配置タイルが増えます。
        [Tooltip("樹木資源を配置するノイズ下限です。0〜1で指定し、小さくすると樹木群が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float treeResourceThreshold = 50000f / ushort.MaxValue;

        // 石群の密度を決める0〜1の下限で、木より希少になる初期値です。
        [Tooltip("石資源を配置するノイズ下限です。0〜1で指定し、小さくすると石群が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float stoneResourceThreshold = 55500f / ushort.MaxValue;

        // 砂地内の採取可能な砂資源群を決める0〜1の下限です。
        [Tooltip("砂資源を配置するノイズ下限です。0〜1で指定し、小さくすると砂地内の採取候補が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float sandResourceThreshold = 51500f / ushort.MaxValue;

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

            // 海面と岩壁が逆転すると陸地分類が成立しないため、岩壁側をわずかに上へ保ちます。
            waterThreshold = Mathf.Min(waterThreshold, 1f - minimumGap);
            rockWallThreshold = Mathf.Max(rockWallThreshold, waterThreshold + minimumGap);
            rockWallThreshold = Mathf.Clamp01(rockWallThreshold);

            // 淡水の上限を海面と岩壁の間へ収め、海や岩壁の分類と競合しないようにします。
            freshWaterMaximumElevation = Mathf.Max(
                freshWaterMaximumElevation,
                waterThreshold + minimumGap);
            freshWaterMaximumElevation = Mathf.Min(
                freshWaterMaximumElevation,
                rockWallThreshold - minimumGap);

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
        /// WorldGenerationAuthoringを実行時の地形・資源・ストリーミング設定Entityへ変換します。
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
                    SeaLevelThreshold = ToNormalizedUShort(authoring.waterThreshold),
                    RiverCellSizeInTiles = authoring.riverCellSizeInTiles,
                    PondCellSizeInTiles = authoring.pondCellSizeInTiles,
                    FreshWaterOctaveCount = authoring.freshWaterOctaveCount,
                    RiverCenterLineHalfWidth = ToNormalizedUShort(authoring.riverCenterLineHalfWidth),
                    PondThreshold = ToNormalizedUShort(authoring.pondThreshold),
                    FreshWaterMaximumElevation = ToNormalizedUShort(authoring.freshWaterMaximumElevation),
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
                AddComponent(entity, new ResourceGenerationSettings
                {
                    ClusterCellSizeInTiles = authoring.resourceClusterCellSizeInTiles,
                    OctaveCount = authoring.resourceOctaveCount,
                    TreeThreshold = ToNormalizedUShort(authoring.treeResourceThreshold),
                    StoneThreshold = ToNormalizedUShort(authoring.stoneResourceThreshold),
                    SandThreshold = ToNormalizedUShort(authoring.sandResourceThreshold)
                });
            }
        }
    }
}
