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
        /// <summary>旧Authoringの小数0.305が変換されていた海面閾値です。既存Sceneの既定値移行だけに使用します。</summary>
        private const ushort LegacySeaLevelThreshold = 19988;

        /// <summary>旧Authoringの小数0.61が変換されていた高温閾値です。既存Sceneの既定値移行だけに使用します。</summary>
        private const ushort LegacyHotTemperatureThreshold = 39976;

        /// <summary>旧Authoringの小数0.435が変換されていた乾燥閾値です。既存Sceneの既定値移行だけに使用します。</summary>
        private const ushort LegacyDryMoistureThreshold = 28508;

        /// <summary>旧Authoringの小数0.603が変換されていた湿潤閾値です。既存Sceneの既定値移行だけに使用します。</summary>
        private const ushort LegacyWetMoistureThreshold = 39518;

        // 開発者がInspectorで入力し、Bake時に安定64bit値へ変換されるシード文字列です。
        [Tooltip("開発用ワールドのシード文字列です。数字以外や日本語も使用できます。同じ文字列とGeneratorVersionなら同じ地形になります。")]
        [SerializeField] private string seedText = "Farmware";

        // 初期開発中は1へ固定し、正式なセーブ互換運用を始めた後だけ非互換変更に合わせて更新します。
        [Tooltip("生成規則の互換バージョンです。初期開発中は1へ固定し、正式なセーブ互換運用を始めた後だけ非互換変更時に増やします。")]
        [Min(1)]
        [SerializeField] private int generatorVersion = 1;

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

        // 火山などの希少バイオーム候補を決める独立ノイズで、気温・湿度だけでは得られない分布差を作ります。
        [Tooltip("火山など特殊地域の候補を決めるWeirdnessノイズの基準格子サイズです。単位はタイルです。大きくすると特殊地域が広く続きます。")]
        [Min(16)]
        [SerializeField] private int weirdnessCellSizeInTiles = 112;

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

        // 7種類の適性スコアを閾値の前後で滑らかに入れ替え、一本線の境界や小さな飛び地を減らします。
        [Tooltip("バイオーム適性が気候閾値付近で滑らかに入れ替わる帯のおおよその幅です。単位はタイルです。大きくすると緩衝的な地域が広がります。")]
        [Range(0, 32)]
        [SerializeField] private int biomeClassificationTransitionWidthInTiles = 12;

        // 論理Biome IDを変えず、境界付近の地面種別だけを座標ハッシュで段階的に混ぜる幅です。
        [Tooltip("バイオーム境界で草地・植生・砂地を混ぜる帯のおおよその幅です。単位はタイルです。0で無効、2〜8を推奨します。変更すると生成結果が変わります。")]
        [Range(0, 16)]
        [SerializeField] private int biomeTransitionWidthInTiles = 6;

        // 0〜1の標高に対する海面境界です。既存Sceneとの互換のためフィールド名はwaterThresholdを維持します。
        [Tooltip("これ未満の標高を海水域にする海面閾値です。0〜1で指定し、大きくすると海が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float waterThreshold =
            WorldGenerationSettings.DefaultSeaLevelThreshold / (float)ushort.MaxValue;

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

        // 森林のような湿潤域では池を少し増やすため、湿度に応じて池ノイズ下限を連続的に下げます。
        [Tooltip("森林に近い湿潤条件で池ノイズ下限を最大どれだけ下げるかを0〜1で指定します。大きくすると森林内の池が増えます。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float wetClimatePondThresholdReduction = 8500f / ushort.MaxValue;

        // 湿地では森林分に加えて池を増やし、陸地を残しながら水面が点在する分布を作ります。
        [Tooltip("低地かつ高湿度の湿地条件で、森林分に追加して池ノイズ下限を最大どれだけ下げるかを0〜1で指定します。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float wetlandPondThresholdReduction = 8500f / ushort.MaxValue;

        // 砂漠では池を希少にするため、高温と乾燥の両方が強いほど池ノイズ下限を上げます。
        [Tooltip("高温かつ乾燥した砂漠条件で池ノイズ下限を最大どれだけ上げるかを0〜1で指定します。大きくすると砂漠の池が減ります。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float desertPondThresholdIncrease = 5000f / ushort.MaxValue;

        // 岩地では池を希少にするため、標高が高地条件へ近づくほど池ノイズ下限を上げます。
        [Tooltip("高地の岩地条件で池ノイズ下限を最大どれだけ上げるかを0〜1で指定します。大きくすると岩地の池が減ります。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float highlandPondThresholdIncrease = 3500f / ushort.MaxValue;

        // 火山中心部では池を禁止し、周縁では急に途切れないよう火山適性に比例して抑制します。
        [Tooltip("高温・高地・高Weirdnessの火山条件で池ノイズ下限を最大どれだけ上げるかを0〜1で指定します。既定値では火山中心部の池を禁止します。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float volcanicPondThresholdIncrease = 16000f / ushort.MaxValue;

        // 湿地内の川を見つけやすくするため、低地かつ高湿度の連続重みに応じて川幅を広げます。
        [Tooltip("低地かつ高湿度の湿地条件で川の中心線半幅へ最大どれだけ加えるかを0〜1で指定します。大きくすると湿地の川が太くなります。")]
        [Range(0f, 0.1f)]
        [SerializeField] private float wetlandRiverHalfWidthIncrease = 500f / ushort.MaxValue;

        // 火山でも川を完全には切らず、自然に横断できるよう最小幅を残しながら細くします。
        [Tooltip("高温・高地・高Weirdnessの火山条件で川の中心線半幅から最大どれだけ引くかを0〜1で指定します。川を完全には消しません。")]
        [Range(0f, 0.1f)]
        [SerializeField] private float volcanicRiverHalfWidthReduction = 450f / ushort.MaxValue;

        // 淡水を生成できる標高上限で、山頂まで川や池が伸びることを防ぎます。
        [Tooltip("川と池を生成できる最大標高です。0〜1で指定し、小さくすると淡水が低地へ限定されます。海面より高くしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float freshWaterMaximumElevation = 47000f / ushort.MaxValue;

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

        // 本体より低い閾値で周囲へ淡水抑制域を作り、川・池とマグマが直接接しにくくします。
        [Tooltip("マグマ周囲で川と池を抑制する専用ノイズ下限です。0〜1で指定し、マグマ本体の下限以下にしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float magmaInfluenceNoiseThreshold = 30000f / ushort.MaxValue;

        // 離散火山内でさらに中心部へ絞りたい場合の追加下限です。既定値0では火山全域を面積比の母集団にします。
        [Tooltip("火山内でマグマ本体を追加制限する連続火山適性の下限です。0〜1で指定し、既定値0では火山全域を許可します。")]
        [Range(0f, 1f)]
        [SerializeField] private float magmaMinimumVolcanicInfluence;

        // 本体より緩い追加下限で淡水抑制域を作ります。既定値0では専用ノイズだけで外周を決めます。
        [Tooltip("火山内で川と池を抑制する影響域の連続適性下限です。0〜1で指定し、マグマ本体の追加下限以下にしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float magmaInfluenceMinimumVolcanicInfluence;

        // 岩地では火山と別の細かなノイズを使い、広いマグマ海ではなく小さな溜まりに限定します。
        [Tooltip("岩地バイオームの小さなマグマ溜まりを作るノイズ格子サイズです。単位はタイルです。小さくすると各溜まりも小さくなります。")]
        [Min(8)]
        [SerializeField] private int rocklandMagmaCellSizeInTiles = 12;

        // 高い下限を超えたピークだけを採用し、岩地でのマグマを稀な障害物として扱います。
        [Tooltip("岩地バイオームへマグマを生成する専用ノイズ下限です。0〜1で指定し、大きくすると発生がより稀になります。")]
        [Range(0f, 1f)]
        [SerializeField] private float rocklandMagmaNoiseThreshold = 47000f / ushort.MaxValue;

        // 本体の少し外側だけ淡水を抑え、小規模なマグマへ川や池が直接重なることを防ぎます。
        [Tooltip("岩地マグマ周囲で川と池を抑制する専用ノイズ下限です。0〜1で指定し、岩地マグマ本体の下限以下にしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float rocklandMagmaInfluenceNoiseThreshold = 44000f / ushort.MaxValue;

        // 火山性が少し残る岩地移行域だけを許可し、通常の岩地全域へマグマが散ることを防ぎます。
        [Tooltip("岩地マグマを火山との移行域へ限定する連続火山適性の下限です。0〜1で指定します。")]
        [Range(0f, 1f)]
        [SerializeField] private float rocklandMagmaMinimumVolcanicInfluence = 100f / ushort.MaxValue;

        // 淡水抑制域は本体より緩い火山適性にし、マグマ周囲へ細い陸地の余白を作ります。
        [Tooltip("岩地マグマ周囲の淡水抑制域を許可する連続火山適性下限です。0〜1で指定し、本体の下限以下にしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float rocklandMagmaInfluenceMinimumVolcanicInfluence = 50f / ushort.MaxValue;

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
        [Tooltip("これ以上を高温とみなす気温閾値です。0〜1で指定し、小さくすると砂漠候補が増えます。変更すると生成結果が変わります。")]
        [Range(0f, 1f)]
        [SerializeField] private float hotTemperatureThreshold =
            WorldGenerationSettings.DefaultHotTemperatureThreshold / (float)ushort.MaxValue;

        // 雪原判定に使う0〜1の低温境界で、高温境界より必ず低く保ちます。
        [Tooltip("これ未満を低温とみなす気温閾値です。0〜1で指定し、大きくすると雪原候補が増えます。高温閾値より小さくしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float coldTemperatureThreshold = 23000f / ushort.MaxValue;

        // 砂漠判定に使う0〜1の乾燥境界で、湿潤境界より必ず低く保ちます。
        [Tooltip("これ未満を乾燥とみなす湿度閾値です。0〜1で指定し、大きくすると砂漠候補が増えます。森林閾値より小さくしてください。")]
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
        [Tooltip("湿地候補にできる最大標高です。0〜1で指定し、大きくすると湿地が高い場所まで広がります。高地閾値より小さくしてください。")]
        [Range(0f, 1f)]
        [SerializeField] private float wetlandMaximumElevation = 36000f / ushort.MaxValue;

        // 岩地と火山を高地へ寄せる0〜1の標高境界です。
        [Tooltip("これ以上を岩地・火山候補とみなす高地閾値です。0〜1で指定し、小さくすると岩地が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float highlandElevationThreshold = 43000f / ushort.MaxValue;

        // 火山を希少な特殊地域へ限定する0〜1のWeirdness境界です。
        [Tooltip("これ以上を火山候補とみなすWeirdness閾値です。0〜1で指定し、小さくすると火山が増えます。")]
        [Range(0f, 1f)]
        [SerializeField] private float volcanicWeirdnessThreshold = 47000f / ushort.MaxValue;

        // 強く隣接抑制する候補が僅差のときに、草原または岩地へ置き換えるスコア差です。
        [Tooltip("強く隣接抑制する2バイオームの適性差がこの値以下なら緩衝帯へ置き換えます。0〜65535の整数で、大きくすると緩衝帯が広がります。")]
        [Range(0, ushort.MaxValue)]
        [SerializeField] private int biomeAdjacencyTransitionScoreMargin = 18000;

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
            return new WorldGenerationSettings
            {
                WorldSeed = WorldSeedUtility.FromString(effectiveSeedText),
                GeneratorVersion = generatorVersion,
                ElevationCellSizeInTiles = elevationCellSizeInTiles,
                TemperatureCellSizeInTiles = temperatureCellSizeInTiles,
                MoistureCellSizeInTiles = moistureCellSizeInTiles,
                WeirdnessCellSizeInTiles = weirdnessCellSizeInTiles,
                BiomeWarpCellSizeInTiles = biomeWarpCellSizeInTiles,
                BiomeWarpAmplitudeInTiles = biomeWarpAmplitudeInTiles,
                OctaveCount = octaveCount,
                BiomeClassificationTransitionWidthInTiles =
                    biomeClassificationTransitionWidthInTiles,
                BiomeTransitionWidthInTiles = biomeTransitionWidthInTiles,
                SeaLevelThreshold = ToCanonicalDefaultThreshold(
                    waterThreshold,
                    LegacySeaLevelThreshold,
                    WorldGenerationSettings.DefaultSeaLevelThreshold),
                RiverCellSizeInTiles = riverCellSizeInTiles,
                PondCellSizeInTiles = pondCellSizeInTiles,
                FreshWaterOctaveCount = freshWaterOctaveCount,
                RiverCenterLineHalfWidth = ToNormalizedUShort(riverCenterLineHalfWidth),
                PondThreshold = ToNormalizedUShort(pondThreshold),
                WetClimatePondThresholdReduction =
                    ToNormalizedUShort(wetClimatePondThresholdReduction),
                WetlandPondThresholdReduction =
                    ToNormalizedUShort(wetlandPondThresholdReduction),
                DesertPondThresholdIncrease =
                    ToNormalizedUShort(desertPondThresholdIncrease),
                HighlandPondThresholdIncrease =
                    ToNormalizedUShort(highlandPondThresholdIncrease),
                VolcanicPondThresholdIncrease =
                    ToNormalizedUShort(volcanicPondThresholdIncrease),
                WetlandRiverHalfWidthIncrease =
                    ToNormalizedUShort(wetlandRiverHalfWidthIncrease),
                VolcanicRiverHalfWidthReduction =
                    ToNormalizedUShort(volcanicRiverHalfWidthReduction),
                FreshWaterMaximumElevation = ToNormalizedUShort(freshWaterMaximumElevation),
                MagmaCellSizeInTiles = magmaCellSizeInTiles,
                MagmaDetailCellSizeInTiles = magmaDetailCellSizeInTiles,
                MagmaWarpAmplitudeInTiles = magmaWarpAmplitudeInTiles,
                MagmaOctaveCount = magmaOctaveCount,
                MagmaNoiseThreshold = ToNormalizedUShort(magmaNoiseThreshold),
                MagmaInfluenceNoiseThreshold =
                    ToNormalizedUShort(magmaInfluenceNoiseThreshold),
                MagmaMinimumVolcanicInfluence =
                    ToNormalizedUShort(magmaMinimumVolcanicInfluence),
                MagmaInfluenceMinimumVolcanicInfluence =
                    ToNormalizedUShort(magmaInfluenceMinimumVolcanicInfluence),
                RocklandMagmaCellSizeInTiles = rocklandMagmaCellSizeInTiles,
                RocklandMagmaNoiseThreshold =
                    ToNormalizedUShort(rocklandMagmaNoiseThreshold),
                RocklandMagmaInfluenceNoiseThreshold =
                    ToNormalizedUShort(rocklandMagmaInfluenceNoiseThreshold),
                RocklandMagmaMinimumVolcanicInfluence =
                    ToNormalizedUShort(rocklandMagmaMinimumVolcanicInfluence),
                RocklandMagmaInfluenceMinimumVolcanicInfluence =
                    ToNormalizedUShort(rocklandMagmaInfluenceMinimumVolcanicInfluence),
                RockWallMassCellSizeInTiles = rockWallMassCellSizeInTiles,
                RockWallDetailCellSizeInTiles = rockWallDetailCellSizeInTiles,
                RockWallWarpAmplitudeInTiles = rockWallWarpAmplitudeInTiles,
                RockWallNoiseThreshold = ToNormalizedUShort(rockWallNoiseThreshold),
                RockWallCorridorHalfWidth = ToNormalizedUShort(rockWallCorridorHalfWidth),
                HotTemperatureThreshold = ToCanonicalDefaultThreshold(
                    hotTemperatureThreshold,
                    LegacyHotTemperatureThreshold,
                    WorldGenerationSettings.DefaultHotTemperatureThreshold),
                ColdTemperatureThreshold = ToNormalizedUShort(coldTemperatureThreshold),
                DryMoistureThreshold = ToCanonicalDefaultThreshold(
                    dryMoistureThreshold,
                    LegacyDryMoistureThreshold,
                    WorldGenerationSettings.DefaultDryMoistureThreshold),
                WetMoistureThreshold = ToCanonicalDefaultThreshold(
                    wetMoistureThreshold,
                    LegacyWetMoistureThreshold,
                    WorldGenerationSettings.DefaultWetMoistureThreshold),
                WetlandMoistureThreshold = ToNormalizedUShort(wetlandMoistureThreshold),
                WetlandMaximumElevation = ToNormalizedUShort(wetlandMaximumElevation),
                HighlandElevationThreshold = ToNormalizedUShort(highlandElevationThreshold),
                VolcanicWeirdnessThreshold = ToNormalizedUShort(volcanicWeirdnessThreshold),
                BiomeAdjacencyTransitionScoreMargin =
                    (ushort)biomeAdjacencyTransitionScoreMargin
            };
        }

        /// <summary>
        /// Inspector値が変更されたときにUnity Editorから呼ばれ、矛盾する閾値を安全な順序へ補正します。
        /// 実行時の地形を変更する処理ではなく、Bake前のAuthoringデータだけを整えます。
        /// </summary>
        private void OnValidate()
        {
            const float minimumGap = 0.001f;

            // 海面が正規化範囲の上端へ達すると淡水上限を置けないため、わずかな余白を残します。
            waterThreshold = Mathf.Min(waterThreshold, 1f - minimumGap);

            // 淡水の上限を海面以上へ収め、海水だけの標高帯と逆転しないようにします。
            freshWaterMaximumElevation = Mathf.Max(
                freshWaterMaximumElevation,
                waterThreshold + minimumGap);
            freshWaterMaximumElevation = Mathf.Clamp01(freshWaterMaximumElevation);

            // 火山用・岩地用の両方で影響域を本体より広く保ち、マグマの直前まで淡水が入り込む逆転設定を防ぎます。
            magmaInfluenceNoiseThreshold = Mathf.Min(
                magmaInfluenceNoiseThreshold,
                magmaNoiseThreshold);
            magmaInfluenceMinimumVolcanicInfluence = Mathf.Min(
                magmaInfluenceMinimumVolcanicInfluence,
                magmaMinimumVolcanicInfluence);
            rocklandMagmaInfluenceNoiseThreshold = Mathf.Min(
                rocklandMagmaInfluenceNoiseThreshold,
                rocklandMagmaNoiseThreshold);
            rocklandMagmaInfluenceMinimumVolcanicInfluence = Mathf.Min(
                rocklandMagmaInfluenceMinimumVolcanicInfluence,
                rocklandMagmaMinimumVolcanicInfluence);

            // 乾燥・森林・湿地の3領域をすべて残すには上端側へ2つの間隔が必要なため、乾燥閾値を0.998以下へ収めます。
            // 乾燥閾値を0.999まで許すと、森林閾値も上限0.999へ補正されて同値になり、中湿度の森林候補帯が消えてしまいます。
            dryMoistureThreshold = Mathf.Min(
                dryMoistureThreshold,
                1f - (minimumGap * 2f));
            wetMoistureThreshold = Mathf.Max(wetMoistureThreshold, dryMoistureThreshold + minimumGap);
            wetMoistureThreshold = Mathf.Min(wetMoistureThreshold, 1f - minimumGap);
            wetlandMoistureThreshold = Mathf.Max(
                wetlandMoistureThreshold,
                wetMoistureThreshold + minimumGap);
            wetlandMoistureThreshold = Mathf.Clamp01(wetlandMoistureThreshold);

            // 雪原と高温域の基準が逆転しないよう、低温閾値を高温閾値より下へ保ちます。
            hotTemperatureThreshold = Mathf.Max(hotTemperatureThreshold, minimumGap);
            coldTemperatureThreshold = Mathf.Min(
                coldTemperatureThreshold,
                hotTemperatureThreshold - minimumGap);
            coldTemperatureThreshold = Mathf.Clamp01(coldTemperatureThreshold);

            // 湿地上限と高地下限を分け、湿地と岩地の両方が同じ標高条件で最大適性にならないようにします。
            wetlandMaximumElevation = Mathf.Min(wetlandMaximumElevation, 1f - minimumGap);
            highlandElevationThreshold = Mathf.Max(
                highlandElevationThreshold,
                wetlandMaximumElevation + minimumGap);
            highlandElevationThreshold = Mathf.Clamp01(highlandElevationThreshold);

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
        /// 既存Sceneへ保存済みの丸めた旧既定値だけを、純粋計算と共有する正確な固定小数点既定値へ移行します。
        /// それ以外のInspector調整値は変更せず、開発者が意図したカスタム設定を保ちます。
        /// </summary>
        /// <param name="normalizedValue">InspectorまたはSceneへ保存された0〜1の値です。</param>
        /// <param name="legacyDefault">旧小数値を0〜65535へ変換した値です。</param>
        /// <param name="canonicalDefault">現在のCreateDefaultと共有する正確な既定値です。</param>
        /// <returns>旧既定値なら現在の既定値、それ以外なら入力を通常変換した値です。</returns>
        private static ushort ToCanonicalDefaultThreshold(
            float normalizedValue,
            ushort legacyDefault,
            ushort canonicalDefault)
        {
            ushort convertedValue = ToNormalizedUShort(normalizedValue);
            return convertedValue == legacyDefault ? canonicalDefault : convertedValue;
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
