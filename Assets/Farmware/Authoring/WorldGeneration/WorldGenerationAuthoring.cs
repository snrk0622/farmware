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
        /// <summary>
        /// 地上18Biomeの固定標高下限と両立する、Inspector用の正規化海面下限です。
        /// OnValidateとRange表示で共有し、分類先のない20,000未満の陸地を作る入力を残しません。
        /// </summary>
        private const float MinimumSupportedWaterThreshold =
            WorldGenerationSettings.MinimumSupportedSeaLevelThreshold / (float)ushort.MaxValue;

        // 開発者がInspectorで入力し、Bake時に安定64bit値へ変換されるシード文字列です。
        [Tooltip("開発用ワールドのシード文字列です。数字以外や日本語も使用できます。同じ文字列とGeneratorVersionなら同じ地形になります。")]
        [SerializeField] private string seedText = "Farmware";

        // C5より前の旧7Biome結果を新IDへ読み替えないため、現在の生成器バージョンだけをBakeします。
        [Tooltip("生成規則の互換バージョンです。C5の新18Biome形式では2固定で、旧バージョンのキャッシュは再利用しません。")]
        [Min(WorldGenerationSettings.CurrentGeneratorVersion)]
        [SerializeField] private int generatorVersion = WorldGenerationSettings.CurrentGeneratorVersion;

        // 標高の広がりを決める最も低周波な格子サイズで、Bake時からワールド寿命中は固定します。
        [Tooltip("標高ノイズの基準格子サイズです。単位はタイルです。大きくすると海・低地・高地が広く続き、変更すると生成結果が変わります。")]
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

        // 気候ノイズを読む座標そのものを緩やかに曲げ、縦横の帯に見えるバイオーム境界を減らします。
        [Tooltip("バイオーム境界を曲げる低周波ノイズの格子サイズです。単位はタイルです。大きくすると曲がり方が緩やかになります。")]
        [Min(16)]
        [SerializeField] private int biomeWarpCellSizeInTiles = 192;

        // 座標歪みの最大幅で、0なら従来どおり気候ノイズを元の座標から読みます。
        [Tooltip("気候ノイズを読む座標の最大変位量です。単位はタイルです。大きくするとバイオーム輪郭のうねりが強くなります。")]
        [Range(0, 32)]
        [SerializeField] private int biomeWarpAmplitudeInTiles = 12;

        // 低周波へ重ねる細部の段数で、Bake時にECS設定へコピーされます。
        [Tooltip("重ねるノイズ解像度の数です。増やすと細部が増えますが生成計算が重くなり、生成結果も変わります。初期候補は3〜5です。")]
        [Range(1, 6)]
        [SerializeField] private int octaveCount = 4;

        // 0〜1の標高に対する海面境界です。既存Sceneとの互換のためフィールド名はwaterThresholdを維持します。
        [Tooltip("これ未満の標高を海水域にする海面閾値です。地上18Biomeの固定標高範囲と両立する20,000以上の範囲で指定し、大きくすると海が増えます。湿地上限と山地・高山境界の順序を保てる範囲へ自動補正されます。")]
        [Range(MinimumSupportedWaterThreshold, 1f)]
        [SerializeField] private float waterThreshold =
            WorldGenerationSettings.DefaultSeaLevelThreshold / (float)ushort.MaxValue;

        // 火山バイオームの約半分を占めるマグマ海と陸地を決めます。火山領域より格子が大きいと局所的に全域がマグマになるため、24タイルへ抑えます。
        [Tooltip("火山バイオームのマグマ海を作るノイズ格子サイズです。単位はタイルです。大きくすると塊は広がりますが、小さな火山全域がマグマになる場合があります。")]
        [Min(8)]
        [SerializeField] private int magmaCellSizeInTiles = 24;

        // 大域形状より細かな独立ノイズを混ぜ、単純な円形ではなく入り江、半島、島を作ります。
        [Tooltip("火山マグマの入り江、半島、島を作る細部ノイズ格子サイズです。単位はタイルです。小さくすると輪郭が細かくなります。")]
        [Min(4)]
        [SerializeField] private int magmaDetailCellSizeInTiles = 8;

        // 大域・細部ノイズを読む座標自体を曲げ、格子方向へ揃った輪郭や似た形の繰り返しを減らします。
        [Tooltip("火山マグマのノイズ座標を歪ませる最大幅です。単位はタイルです。大きくすると輪郭が不規則になりますが、過大にすると細い飛び地が増えます。")]
        [Range(0, 16)]
        [SerializeField] private int magmaWarpAmplitudeInTiles = 6;

        // マグマ輪郭へ重ねる細部の段数で、地形全体より少なめにして細かな穴や点在を抑えます。
        [Tooltip("マグマ形状へ重ねるノイズ解像度数です。増やすと輪郭が細かくなりますが、生成負荷も増えます。")]
        [Range(1, 5)]
        [SerializeField] private int magmaOctaveCount = 3;

        // 中央値に近い下限で火山のおよそ半分をマグマへ変換し、陸上設備を置ける島も残します。
        [Tooltip("火山バイオームをマグマへ変換する専用ノイズ下限です。0〜1で指定し、小さくすると火山内のマグマが増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float magmaNoiseThreshold = 34000f / ushort.MaxValue;

        // 離散火山内でさらに中心部へ絞りたい場合の追加下限です。既定値0では火山全域を面積比の母集団にします。
        [Tooltip("火山内でマグマ本体を追加制限する連続火山適性の下限です。0〜1で指定し、既定値0では火山全域を許可します。")]
        [Range(0f, 1f)]
        [SerializeField] private float magmaMinimumVolcanicInfluence;

        // 岩壁の大きなまとまりを作る低周波格子で、論理チャンク境界とは独立して連続します。
        [Tooltip("岩壁の大きな塊を作るノイズ格子サイズです。単位はタイルです。大きくすると1つの岩壁域が広がります。")]
        [Range(16, 64)]
        [SerializeField] private int rockWallMassCellSizeInTiles = 24;

        // 大きな塊へ細かな凹凸を足す高周波格子で、同じ楕円形に見える反復を崩します。
        [Tooltip("岩壁輪郭の凹凸を作るノイズ格子サイズです。単位はタイルです。小さくすると輪郭が細かくなります。")]
        [Range(4, 24)]
        [SerializeField] private int rockWallDetailCellSizeInTiles = 8;

        // 低周波ノイズで密度座標を歪ませ、軸へ沿った輪郭や規則的な塊を減らします。
        [Tooltip("岩壁ノイズ座標を歪ませる最大幅です。単位はタイルです。大きくすると輪郭の曲がりが強くなります。")]
        [Range(0, 16)]
        [SerializeField] private int rockWallWarpAmplitudeInTiles = 6;

        // 合成密度を岩壁へ変換する0〜1境界で、Bake時に0〜65535へ変換します。
        [Tooltip("岩壁にする合成ノイズ下限です。0〜1で指定し、小さくすると岩壁の占有率が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float rockWallNoiseThreshold = 41000f / ushort.MaxValue;

        // 別ノイズの中心等高線を削る幅で、長く曲がった物流通路を確保します。
        [Tooltip("岩壁から削る通路の半幅です。0〜1のノイズ差で指定し、大きくすると通路が太く増えます。")]
        [Range(0f, 0.1f)]
        [SerializeField] private float rockWallCorridorHalfWidth = 1100f / ushort.MaxValue;

        // 砂漠判定に使う0〜1の気温境界で、Bake時に固定小数点の閾値へ変換します。
        [Tooltip("これ以上を高温とみなす気温閾値です。0〜1で指定し、小さくすると砂漠候補が増えます。新しい温暖境界32,768より上へ自動補正されます。")]
        [Range(0f, 1f)]
        [SerializeField] private float hotTemperatureThreshold =
            WorldGenerationSettings.DefaultHotTemperatureThreshold / (float)ushort.MaxValue;

        // 雪原判定に使う0〜1の低温境界で、高温境界より必ず低く保ちます。
        [Tooltip("これ未満を低温とみなす気温閾値です。0〜1で指定し、大きくすると雪原候補が増えます。新しい寒冷境界12,000と温暖境界32,768の間へ自動補正されます。")]
        [Range(0f, 1f)]
        [SerializeField] private float coldTemperatureThreshold = 23000f / ushort.MaxValue;

        // 砂漠判定に使う0〜1の乾燥境界で、湿潤境界より必ず低く保ちます。
        [Tooltip("これ未満を乾燥とみなす湿度閾値です。0〜1で指定し、大きくすると砂漠候補が増えます。新しい極乾燥境界16,000より上かつ森林閾値より下へ自動補正されます。")]
        [Range(0f, 1f)]
        [SerializeField] private float dryMoistureThreshold =
            WorldGenerationSettings.DefaultDryMoistureThreshold / (float)ushort.MaxValue;

        // 森林判定に使う0〜1の湿潤境界で、Bake時に固定小数点の閾値へ変換します。
        [Tooltip("これ以上を森林候補とみなす湿度閾値です。0〜1で指定し、小さくすると森林が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float wetMoistureThreshold =
            WorldGenerationSettings.DefaultWetMoistureThreshold / (float)ushort.MaxValue;

        // 湿地判定に使う0〜1の非常に湿潤な境界で、森林閾値より必ず高く保ちます。
        [Tooltip("これ以上を湿地候補とみなす湿度閾値です。0〜1で指定し、小さくすると湿地が増えます。森林閾値より大きくしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float wetlandMoistureThreshold = 50000f / ushort.MaxValue;

        // 湿地を低地へ限定する0〜1の標高上限で、高地判定より低く保ちます。
        [Tooltip("湿地候補にできる最大標高です。0〜1で指定し、大きくすると湿地が高い場所まで広がります。海面以上かつ高地閾値より下へ自動補正されます。")]
        [Range(0f, 1f)]
        [SerializeField] private float wetlandMaximumElevation = 36000f / ushort.MaxValue;

        // 岩地と火山を高地へ寄せ、新しい標高区分の山地開始にも同期する0〜1の標高境界です。
        [Tooltip("これ以上を岩地・火山候補および山地とみなす高地閾値です。0〜1で指定し、小さくすると岩地・山地が増えます。高山境界54,000未満へ自動補正されます。")]
        [Range(0f, 1f)]
        [SerializeField] private float highlandElevationThreshold = 43000f / ushort.MaxValue;

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

        // 砂質土内の採取可能な砂資源群を決める0〜1の下限です。
        [Tooltip("砂資源を配置するノイズ下限です。0〜1で指定し、小さくすると砂質土内の採取候補が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float sandResourceThreshold = 48000f / ushort.MaxValue;

        // 樹木群の中心を離す距離で、群れ同士が連結して巨大な森になることを防ぎます。
        [Tooltip("樹木群の中心同士に確保する最低距離です。単位はタイルです。大きくすると樹木群が疎になります。")]
        [Min(2)]
        [SerializeField] private int treeResourceMinimumSpacingInTiles = 14;

        // 石群は樹木より希少に見せるため、中心間隔を広く取ります。
        [Tooltip("石群の中心同士に確保する最低距離です。単位はタイルです。大きくすると石群が疎になります。")]
        [Min(2)]
        [SerializeField] private int stoneResourceMinimumSpacingInTiles = 20;

        // 砂資源群の中心を離す距離で、砂漠全体が資源アイコンで埋まることを防ぎます。
        [Tooltip("砂資源群の中心同士に確保する最低距離です。単位はタイルです。大きくすると砂資源群が疎になります。")]
        [Min(2)]
        [SerializeField] private int sandResourceMinimumSpacingInTiles = 18;

        // 樹木群の中心から資源候補を置ける最大半径で、最低間隔の半分以下へ補正します。
        [Tooltip("樹木群の最大半径です。単位はタイルです。大きくすると1群が広がりますが、最低間隔の半分以下へ補正されます。")]
        [Min(1)]
        [SerializeField] private int treeResourceClusterRadiusInTiles = 5;

        // 石群の中心から資源候補を置ける最大半径で、小さな鉱床として見える値にします。
        [Tooltip("石群の最大半径です。単位はタイルです。大きくすると1群が広がりますが、最低間隔の半分以下へ補正されます。")]
        [Min(1)]
        [SerializeField] private int stoneResourceClusterRadiusInTiles = 4;

        // 砂資源群の中心から資源候補を置ける最大半径です。
        [Tooltip("砂資源群の最大半径です。単位はタイルです。大きくすると1群が広がりますが、最低間隔の半分以下へ補正されます。")]
        [Min(1)]
        [SerializeField] private int sandResourceClusterRadiusInTiles = 6;

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
        /// Inspectorへ保存されている開発用シード文字列を返します。
        /// Editorのマップビューアが、実際のBake入力と同じシードを初期表示へ使用します。
        /// </summary>
        public string SeedText => seedText;

        /// <summary>
        /// BakerとEditorのマップビューアが共有する、地形生成設定の値コピーを作ります。
        /// このメソッドへ変換処理を一元化し、ビューアだけ別の閾値を使って実マップと食い違うことを防ぎます。
        /// </summary>
        /// <param name="seedTextOverride">
        /// nullならInspectorのシード文字列を使用します。別シードを比較するときだけ任意の文字列を渡します。
        /// </param>
        /// <returns>Bake後のECS Singletonと同じ形式の地形生成設定です。</returns>
        public WorldGenerationSettings CreateWorldGenerationSettingsSnapshot(
            string seedTextOverride = null)
        {
            string effectiveSeedText = seedTextOverride ?? seedText;

            // 基礎岩壁と暫定資源が参照する山地境界を、連続環境軸の同じ境界へ揃えます。
            // Inspector値だけを変えたときにも両者を同期し、設定検証で全チャンクが失敗することを防ぎます。
            ContinuousEnvironmentSettings environmentAxes =
                ContinuousEnvironmentSettings.CreateDefault();
            environmentAxes.MountainElevationThreshold =
                ToNormalizedUShort(highlandElevationThreshold);

            return new WorldGenerationSettings
            {
                WorldSeed = WorldSeedUtility.FromString(effectiveSeedText),
                GeneratorVersion = generatorVersion,
                ElevationCellSizeInTiles = elevationCellSizeInTiles,
                TemperatureCellSizeInTiles = temperatureCellSizeInTiles,
                MoistureCellSizeInTiles = moistureCellSizeInTiles,
                BiomeWarpCellSizeInTiles = biomeWarpCellSizeInTiles,
                BiomeWarpAmplitudeInTiles = biomeWarpAmplitudeInTiles,
                OctaveCount = octaveCount,
                // 新しい軸の確定値は共有既定値を使い、基礎地形でも使う山地境界だけ上で同期します。
                EnvironmentAxes = environmentAxes,
                // TERRAIN-01C2の固定地質値はInspectorへ重複保持せず、純粋生成と同じ共有既定値をBakeします。
                Geology = GeologyGenerationSettings.CreateDefault(),
                // TERRAIN-01C3-Aの水文格子寸法と9点サンプルは調整値にせず、共有既定値をBakeします。
                Hydrology = HydrologyGenerationSettings.CreateDefault(),
                // TERRAIN-01C4-Bの局所地質補正もInspectorへ重複保持せず、共有既定値をBakeします。
                Groundwater = GroundwaterGenerationSettings.CreateDefault(),
                // TERRAIN-01C4-Cの湖水収支・泉設定も調整値として重複させず、共有既定値をBakeします。
                LakeSpring = LakeSpringGenerationSettings.CreateDefault(),
                // TERRAIN-01C4-Dの自然冠水・湿原規模設定もGeneratorVersion対象の共有既定値をBakeします。
                NaturalFlooding = NaturalFloodingGenerationSettings.CreateDefault(),
                SeaLevelThreshold = ToNormalizedUShort(waterThreshold),
                MagmaCellSizeInTiles = magmaCellSizeInTiles,
                MagmaDetailCellSizeInTiles = magmaDetailCellSizeInTiles,
                MagmaWarpAmplitudeInTiles = magmaWarpAmplitudeInTiles,
                MagmaOctaveCount = magmaOctaveCount,
                MagmaNoiseThreshold = ToNormalizedUShort(magmaNoiseThreshold),
                MagmaMinimumVolcanicInfluence =
                    ToNormalizedUShort(magmaMinimumVolcanicInfluence),
                RockWallMassCellSizeInTiles = rockWallMassCellSizeInTiles,
                RockWallDetailCellSizeInTiles = rockWallDetailCellSizeInTiles,
                RockWallWarpAmplitudeInTiles = rockWallWarpAmplitudeInTiles,
                RockWallNoiseThreshold = ToNormalizedUShort(rockWallNoiseThreshold),
                RockWallCorridorHalfWidth = ToNormalizedUShort(rockWallCorridorHalfWidth),
                HotTemperatureThreshold = ToNormalizedUShort(hotTemperatureThreshold),
                ColdTemperatureThreshold = ToNormalizedUShort(coldTemperatureThreshold),
                DryMoistureThreshold = ToNormalizedUShort(dryMoistureThreshold),
                WetMoistureThreshold = ToNormalizedUShort(wetMoistureThreshold),
                WetlandMoistureThreshold = ToNormalizedUShort(wetlandMoistureThreshold),
                WetlandMaximumElevation = ToNormalizedUShort(wetlandMaximumElevation),
                HighlandElevationThreshold = ToNormalizedUShort(highlandElevationThreshold)
            };
        }

        /// <summary>
        /// Inspector値が変更されたときにUnity Editorから呼ばれ、矛盾する閾値を安全な順序へ補正します。
        /// 実行時の地形を変更する処理ではなく、Bake前のAuthoringデータだけを整えます。
        /// </summary>
        private void OnValidate()
        {
            const float minimumGap = 0.001f;

            // 初回リリース前の旧7Biomeデータは移行せず、新18Biomeの現行版だけをAuthoringから出力します。
            generatorVersion = WorldGenerationSettings.CurrentGeneratorVersion;

            // 新しい標高区分の高山境界はTERRAIN-01C1の固定値なので、Inspectorの山地境界は必ずその直前へ収めます。
            // ushortへ丸めた後も山地と高山が1以上離れるよう、固定小数点の1段階を差し引いた上限を使います。
            ContinuousEnvironmentSettings environmentAxes =
                ContinuousEnvironmentSettings.CreateDefault();
            float coldBandBoundary =
                environmentAxes.ColdTemperatureThreshold / (float)ushort.MaxValue;
            float temperateBandBoundary =
                environmentAxes.TemperateTemperatureThreshold / (float)ushort.MaxValue;
            float extremelyDryBandBoundary =
                environmentAxes.DryMoistureThreshold / (float)ushort.MaxValue;
            float maximumHighlandElevationThreshold =
                (environmentAxes.AlpineElevationThreshold - 1f) / ushort.MaxValue;
            float maximumWetlandElevation =
                maximumHighlandElevationThreshold - minimumGap;

            // 海面、湿地上限、山地、高山の順序を保てない入力は、最も高い有効な海面へ戻します。
            // この制約により、Inspectorの値だけが連続環境軸と逆転して全チャンク生成が失敗することを防ぎます。
            waterThreshold = Mathf.Clamp(
                waterThreshold,
                MinimumSupportedWaterThreshold,
                maximumWetlandElevation);

            // 乾燥・森林・湿地の3領域をすべて残すには上端側へ2つの間隔が必要なため、乾燥閾値を0.998以下へ収めます。
            // 乾燥閾値を0.999まで許すと、森林閾値も上限0.999へ補正されて同値になり、中湿度の森林候補帯が消えてしまいます。
            // 新しい極乾燥境界より下へ入る値も、5段階分類との順序を保てる下限へ戻します。
            dryMoistureThreshold = Mathf.Clamp(
                dryMoistureThreshold,
                extremelyDryBandBoundary + minimumGap,
                1f - (minimumGap * 2f));
            wetMoistureThreshold = Mathf.Max(wetMoistureThreshold, dryMoistureThreshold + minimumGap);
            wetMoistureThreshold = Mathf.Min(wetMoistureThreshold, 1f - minimumGap);
            wetlandMoistureThreshold = Mathf.Max(
                wetlandMoistureThreshold,
                wetMoistureThreshold + minimumGap);
            wetlandMoistureThreshold = Mathf.Clamp01(wetlandMoistureThreshold);

            // C6/C7まで残る暫定土壌・資源用の低温／高温境界を、新しい5段階分類の対応範囲へ収めます。
            // 暫定境界同士だけでなく連続環境軸との順序も守り、Inspector調整後の設定を生成可能に保ちます。
            hotTemperatureThreshold = Mathf.Clamp(
                hotTemperatureThreshold,
                temperateBandBoundary + minimumGap,
                1f);
            coldTemperatureThreshold = Mathf.Clamp(
                coldTemperatureThreshold,
                coldBandBoundary + minimumGap,
                temperateBandBoundary - minimumGap);

            // 湿地上限と高地下限を分け、湿地と岩地の両方が同じ標高条件で最大適性にならないようにします。
            // 山地境界を新しい標高区分へ同期するため、高山境界より上のInspector入力もここで有効範囲へ戻します。
            wetlandMaximumElevation = Mathf.Clamp(
                wetlandMaximumElevation,
                waterThreshold,
                maximumWetlandElevation);
            highlandElevationThreshold = Mathf.Clamp(
                highlandElevationThreshold,
                wetlandMaximumElevation + minimumGap,
                maximumHighlandElevationThreshold);

            // 群半径が中心間隔の半分を超えると同種の群が重なるため、Inspector上で安全な上限へ揃えます。
            treeResourceClusterRadiusInTiles = Mathf.Min(
                treeResourceClusterRadiusInTiles,
                Mathf.Max(1, treeResourceMinimumSpacingInTiles / 2));
            stoneResourceClusterRadiusInTiles = Mathf.Min(
                stoneResourceClusterRadiusInTiles,
                Mathf.Max(1, stoneResourceMinimumSpacingInTiles / 2));
            sandResourceClusterRadiusInTiles = Mathf.Min(
                sandResourceClusterRadiusInTiles,
                Mathf.Max(1, sandResourceMinimumSpacingInTiles / 2));
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
                AddComponent(entity, authoring.CreateWorldGenerationSettingsSnapshot());
                // C5-Aの18Biome表は約4KBあるため、全生成Jobが受け取るWorldGenerationSettingsへ埋め込みません。
                // 分類器だけが読む独立シングルトンに分け、既存Jobのスケジュールごとの不要な値コピーを避けます。
                AddComponent(entity, BiomeDefinitionCatalog.CreateDefault());
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
                    SandThreshold = ToNormalizedUShort(authoring.sandResourceThreshold),
                    TreeMinimumSpacingInTiles = authoring.treeResourceMinimumSpacingInTiles,
                    StoneMinimumSpacingInTiles = authoring.stoneResourceMinimumSpacingInTiles,
                    SandMinimumSpacingInTiles = authoring.sandResourceMinimumSpacingInTiles,
                    TreeClusterRadiusInTiles = authoring.treeResourceClusterRadiusInTiles,
                    StoneClusterRadiusInTiles = authoring.stoneResourceClusterRadiusInTiles,
                    SandClusterRadiusInTiles = authoring.sandResourceClusterRadiusInTiles
                });
            }
        }
    }
}
