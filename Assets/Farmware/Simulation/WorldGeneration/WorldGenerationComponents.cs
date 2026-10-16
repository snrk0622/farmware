// このファイルは、決定論的チャンク生成で使うECS Component、Dynamic Buffer要素、分類IDを定義します。
// ゲーム状態の正をEntity側へ置き、後続のPresentationは生成済みバッファを読み取るだけにする境界です。

using System;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 初回のランダムマップ生成で使用する7種類の陸上バイオームの安定IDです。
    /// セーブデータへ保存する値になるため、既存値の番号は将来も変更しません。
    /// </summary>
    public enum BiomeId : byte
    {
        /// <summary>未設定または生成失敗を表します。</summary>
        None = 0,

        /// <summary>温暖で中程度の湿度を持つ草原です。</summary>
        Grassland = 1,

        /// <summary>湿度が高く植生の多い森林です。</summary>
        Forest = 2,

        /// <summary>高温かつ乾燥した砂漠です。</summary>
        Desert = 3,

        /// <summary>中〜高標高で植生が少なく、鉱物資源に適する岩地です。</summary>
        Rockland = 4,

        /// <summary>低標高かつ非常に湿潤で、池と植生が多い湿地です。</summary>
        Wetland = 5,

        /// <summary>低温域に広がり、将来は凍結した水域表現を重ねられる雪原です。</summary>
        Snowfield = 6,

        /// <summary>高温・高標高・高Weirdnessが重なる希少な火山地帯です。</summary>
        Volcanic = 7
    }

    /// <summary>
    /// 設備・ベルト・橋の配置可否と通行可否へ直接影響する基礎地形種別です。
    /// 資源候補や地表の見た目を表すSoilTypeとは分離し、物理的な地形ルールだけに使用します。
    /// </summary>
    public enum GroundType : byte
    {
        /// <summary>通常の建築可能な陸地です。</summary>
        Land = 0,

        /// <summary>通常設備と通常ベルトは配置不可ですが、橋を配置できる水域です。</summary>
        Water = 1,

        /// <summary>通常設備、通常ベルト、橋を配置できない岩壁です。</summary>
        RockWall = 2,

        // ID 3と4は旧Vegetation、旧Sandが使用していたため、既存データの誤読を防ぐ目的で再利用しません。
        /// <summary>通常設備・通常ベルト・通常移動を拒否し、耐熱橋だけを許可する高温のマグマ地形です。</summary>
        Magma = 5
    }

    /// <summary>
    /// 陸地表面の土壌傾向を表す安定IDです。
    /// Biomeと組み合わせて資源候補を絞り、Presentationではバイオーム用Base Tileへ色調や模様を重ねるために使用します。
    /// </summary>
    public enum SoilType : byte
    {
        /// <summary>水域、岩壁、マグマなど、土壌を持たない地形です。</summary>
        None = 0,

        /// <summary>特定資源へ強く偏らない標準的な土壌です。</summary>
        Normal = 1,

        /// <summary>樹木や植物系資源の候補になりやすい、湿潤で肥沃な土壌です。</summary>
        Fertile = 2,

        /// <summary>砂や乾燥地向け資源の候補になりやすい砂質土壌です。</summary>
        Sandy = 3
    }

    /// <summary>
    /// 水域が淡水か海水かを表す安定IDです。
    /// 採水設備や水生生物はGroundTypeだけでなく、この値を参照して利用可能な水を判定します。
    /// </summary>
    public enum WaterSalinity : byte
    {
        /// <summary>水域ではない陸地または岩壁です。</summary>
        None = 0,

        /// <summary>川または池として生成された淡水です。</summary>
        FreshWater = 1,

        /// <summary>広域の低標高地として生成された海水です。</summary>
        SaltWater = 2
    }

    /// <summary>
    /// 水域の生成由来を表す安定IDです。
    /// 同じ淡水でも川と池を区別し、将来の魚・設備・見た目の条件へ利用できるようにします。
    /// </summary>
    public enum WaterBodyType : byte
    {
        /// <summary>水域ではありません。</summary>
        None = 0,

        /// <summary>連続ノイズの等高線に沿って陸上を横断する川です。</summary>
        River = 1,

        /// <summary>内陸の低地へまとまって生成される池です。</summary>
        Pond = 2,

        /// <summary>海面より低い広域地形として生成される海です。</summary>
        Sea = 3
    }

    /// <summary>
    /// 連続した標高値をゲームルール向けに大まかに分類した値です。
    /// デバッグ表示や地形別の配置条件で使用します。
    /// </summary>
    public enum ElevationClass : byte
    {
        /// <summary>水面より低い領域です。</summary>
        Water = 0,

        /// <summary>水際に近い低地です。</summary>
        Lowland = 1,

        /// <summary>通常の平地です。</summary>
        Plain = 2,

        /// <summary>陸地の標高範囲を3分割したうち最も高い領域です。</summary>
        Highland = 3,

        /// <summary>固体地形として扱う岩壁です。</summary>
        RockWall = 4
    }

    /// <summary>
    /// 1タイルに対するゲームルール上の真偽値をビット単位で保持します。
    /// boolを個別フィールドに並べず、チャンクバッファのメモリ量を抑えるために使用します。
    /// </summary>
    [Flags]
    public enum TileGameplayFlags : byte
    {
        /// <summary>特別なフラグを持ちません。</summary>
        None = 0,

        /// <summary>中央コアを含む通常設備を配置できる地形です。</summary>
        AllowsNormalEquipment = 1 << 0,

        /// <summary>橋を介さない通常ベルトを配置できる地形です。</summary>
        AllowsNormalBelt = 1 << 1,

        /// <summary>ベルトなどの上部構造を支える橋を配置できる地形です。</summary>
        AllowsBridge = 1 << 2,

        /// <summary>橋を配置するとき、橋定義に耐熱性が必要な地形です。AllowsBridgeを持つ場合だけ参照します。</summary>
        RequiresHeatResistance = 1 << 3
    }

    /// <summary>
    /// 自然資源を配置できる地形候補をビット単位で表します。
    /// 実際の資源配置では、チャンク生成Jobがこの適性と用途別ノイズを組み合わせて候補を決定します。
    /// </summary>
    [Flags]
    public enum ResourceSuitability : byte
    {
        /// <summary>初期資源の配置候補ではありません。</summary>
        None = 0,

        /// <summary>木の群生候補にできます。</summary>
        Tree = 1 << 0,

        /// <summary>石の群生または固定資源候補にできます。</summary>
        Stone = 1 << 1,

        // bit 2は旧Water適性が使用していたため、既存データの誤読を防ぐ目的で再利用しません。
        /// <summary>砂の採取候補にできます。</summary>
        Sand = 1 << 3
    }

    /// <summary>
    /// ワールド初回生成で配置する基礎資源の安定IDです。
    /// セーブデータと表示の対応に使うため、既存値の番号は将来も変更しません。
    /// </summary>
    public enum GeneratedResourceType : byte
    {
        /// <summary>このタイルに基礎資源が配置されていない状態です。</summary>
        None = 0,

        /// <summary>木材系の採取元になる樹木資源です。</summary>
        Tree = 1,

        /// <summary>石材系の採取元になる石資源です。</summary>
        Stone = 2,

        // ID 3は旧Water資源が使用していたため、既存データの誤読を防ぐ目的で再利用しません。
        /// <summary>砂系の採取元になる砂資源です。</summary>
        Sand = 4
    }

    /// <summary>
    /// 初期位置の周囲に必要な自然資源を1つの検索条件へまとめるビットフラグです。
    /// 木・石・砂は生成資源、水は水域地形そのものを検索対象にします。
    /// </summary>
    [Flags]
    public enum NaturalResourceTypeMask : byte
    {
        /// <summary>資源の存在を条件にしません。</summary>
        None = 0,

        /// <summary>樹木資源が少なくとも1タイル必要です。</summary>
        Tree = 1 << 0,

        /// <summary>石資源が少なくとも1タイル必要です。</summary>
        Stone = 1 << 1,

        /// <summary>淡水または海水の水域が少なくとも1タイル必要です。</summary>
        Water = 1 << 2,

        /// <summary>砂資源が少なくとも1タイル必要です。</summary>
        Sand = 1 << 3
    }

    /// <summary>
    /// チャンク生成要求の処理段階を表します。
    /// Presentationとセーブ処理は、この状態を見て生成中データを誤って使用しないようにします。
    /// </summary>
    public enum ChunkGenerationPhase : byte
    {
        /// <summary>まだ生成要求されていません。</summary>
        Unrequested = 0,

        /// <summary>生成Jobの対象として待機しています。</summary>
        Queued = 1,

        /// <summary>純粋データを生成中です。</summary>
        GeneratingData = 2,

        /// <summary>4096タイルが生成され、Presentationへ反映できます。</summary>
        ReadyToApply = 3,

        /// <summary>表示範囲内で利用中です。</summary>
        Active = 4,

        /// <summary>データを維持しつつ表示範囲外になっています。</summary>
        Inactive = 5,

        /// <summary>再生成可能な基礎データをメモリから解放済みです。</summary>
        Unloaded = 6,

        /// <summary>設定またはバッファが不正で生成できませんでした。</summary>
        Failed = 7
    }

    /// <summary>
    /// ワールド全体で1つだけ存在する生成設定です。
    /// Authoringから変換され、各チャンク生成Jobへ値コピーで渡されます。
    /// </summary>
    public struct WorldGenerationSettings : IComponentData
    {
        /// <summary>既定の海面を表す0〜65535の固定小数点閾値です。Authoringと分布監査が同じ値を共有します。</summary>
        public const ushort DefaultSeaLevelThreshold = 20000;

        /// <summary>既定の高温域を表す0〜65535の固定小数点閾値です。Authoringと分布監査が同じ値を共有します。</summary>
        public const ushort DefaultHotTemperatureThreshold = 40000;

        /// <summary>既定の乾燥域を表す0〜65535の固定小数点閾値です。Authoringと分布監査が同じ値を共有します。</summary>
        public const ushort DefaultDryMoistureThreshold = 28500;

        /// <summary>既定の湿潤域を表す0〜65535の固定小数点閾値です。Authoringと分布監査が同じ値を共有します。</summary>
        public const ushort DefaultWetMoistureThreshold = 39500;

        /// <summary>ワールド作成からセーブ寿命全体で不変の64bitシードです。</summary>
        public ulong WorldSeed;

        /// <summary>生成互換性を識別する正の整数バージョンです。</summary>
        public int GeneratorVersion;

        /// <summary>標高の最も低周波な格子サイズです。単位はタイルです。</summary>
        public int ElevationCellSizeInTiles;

        /// <summary>気温の最も低周波な格子サイズです。単位はタイルです。</summary>
        public int TemperatureCellSizeInTiles;

        /// <summary>湿度の最も低周波な格子サイズです。単位はタイルです。</summary>
        public int MoistureCellSizeInTiles;

        /// <summary>火山などの特殊地域候補を決めるWeirdnessの最も低周波な格子サイズです。単位はタイルです。</summary>
        public int WeirdnessCellSizeInTiles;

        /// <summary>気候ノイズのサンプリング座標を曲げる低周波格子サイズです。単位はタイルです。</summary>
        public int BiomeWarpCellSizeInTiles;

        /// <summary>気候ノイズのサンプリング座標を最大何タイルずらすかを表します。</summary>
        public int BiomeWarpAmplitudeInTiles;

        /// <summary>各気候値へ重ねるノイズ解像度の数です。</summary>
        public int OctaveCount;

        /// <summary>7バイオームの適性が閾値付近で滑らかに入れ替わる帯のおおよその幅です。単位はタイルです。</summary>
        public int BiomeClassificationTransitionWidthInTiles;

        /// <summary>バイオーム境界で隣接地面を混ぜる帯のおおよその幅です。単位はタイルです。</summary>
        public int BiomeTransitionWidthInTiles;

        /// <summary>これ未満の標高を海水域にする0〜65535の閾値です。</summary>
        public ushort SeaLevelThreshold;

        /// <summary>川の大きな蛇行を決めるノイズ格子サイズです。単位はタイルです。</summary>
        public int RiverCellSizeInTiles;

        /// <summary>池のまとまりを決めるノイズ格子サイズです。単位はタイルです。</summary>
        public int PondCellSizeInTiles;

        /// <summary>川と池の輪郭へ重ねるノイズ解像度の数です。</summary>
        public int FreshWaterOctaveCount;

        /// <summary>川の中心値32768から許容する差です。大きいほど川幅と本数が増えます。</summary>
        public ushort RiverCenterLineHalfWidth;

        /// <summary>池を生成する0〜65535のノイズ下限です。大きいほど池が減ります。</summary>
        public ushort PondThreshold;

        /// <summary>森林に近い湿潤度で池の下限を最大いくつ下げるかを表す0〜65535の補正量です。</summary>
        public ushort WetClimatePondThresholdReduction;

        /// <summary>低地かつ高湿度の湿地条件で、森林分に追加して池の下限を最大いくつ下げるかを表します。</summary>
        public ushort WetlandPondThresholdReduction;

        /// <summary>高温かつ乾燥した砂漠条件で、池の下限を最大いくつ上げるかを表します。</summary>
        public ushort DesertPondThresholdIncrease;

        /// <summary>高地の岩地条件で、池の下限を最大いくつ上げるかを表します。</summary>
        public ushort HighlandPondThresholdIncrease;

        /// <summary>高温・高地・高Weirdnessの火山条件で、池の下限を最大いくつ上げるかを表します。</summary>
        public ushort VolcanicPondThresholdIncrease;

        /// <summary>低地かつ高湿度の湿地条件で、川の中心線半幅へ最大いくつ加えるかを表します。</summary>
        public ushort WetlandRiverHalfWidthIncrease;

        /// <summary>高温・高地・高Weirdnessの火山条件で、川の中心線半幅から最大いくつ引くかを表します。</summary>
        public ushort VolcanicRiverHalfWidthReduction;

        /// <summary>川と池を生成できる0〜65535の最大標高です。山頂まで淡水が伸びることを防ぎます。</summary>
        public ushort FreshWaterMaximumElevation;

        /// <summary>火山バイオームの広いマグマ海を決める大域ノイズ格子サイズです。単位はタイルです。</summary>
        public int MagmaCellSizeInTiles;

        /// <summary>火山マグマの入り江、半島、島を作る細部ノイズ格子サイズです。単位はタイルです。</summary>
        public int MagmaDetailCellSizeInTiles;

        /// <summary>火山マグマ密度をサンプリングする座標の最大歪み幅です。単位はタイルです。</summary>
        public int MagmaWarpAmplitudeInTiles;

        /// <summary>火山マグマの大域形状へ重ねるノイズ解像度の数です。</summary>
        public int MagmaOctaveCount;

        /// <summary>火山バイオームをマグマ本体へ変換する0〜65535の専用ノイズ下限です。大きいほど陸地が増えます。</summary>
        public ushort MagmaNoiseThreshold;

        /// <summary>淡水を遠ざける火山影響域を生成する0〜65535の専用ノイズ下限です。マグマ本体より低く設定します。</summary>
        public ushort MagmaInfluenceNoiseThreshold;

        /// <summary>火山内でマグマを追加制限する0〜65535の連続火山適性下限です。既定値0では離散火山全域を許可します。</summary>
        public ushort MagmaMinimumVolcanicInfluence;

        /// <summary>火山内の淡水抑制域を追加制限する0〜65535の連続火山適性下限です。マグマ本体以下に設定します。</summary>
        public ushort MagmaInfluenceMinimumVolcanicInfluence;

        /// <summary>岩地バイオームの小さなマグマ溜まりを決めるノイズ格子サイズです。単位はタイルです。</summary>
        public int RocklandMagmaCellSizeInTiles;

        /// <summary>岩地バイオームへ稀にマグマを生成する0〜65535の専用ノイズ下限です。</summary>
        public ushort RocklandMagmaNoiseThreshold;

        /// <summary>岩地の小規模マグマ周囲で淡水を抑制する0〜65535の専用ノイズ下限です。</summary>
        public ushort RocklandMagmaInfluenceNoiseThreshold;

        /// <summary>岩地マグマを火山との移行域へ限定する0〜65535の連続火山適性下限です。</summary>
        public ushort RocklandMagmaMinimumVolcanicInfluence;

        /// <summary>岩地マグマの淡水抑制域を許可する0〜65535の連続火山適性下限です。</summary>
        public ushort RocklandMagmaInfluenceMinimumVolcanicInfluence;

        /// <summary>岩壁の大きな塊を決めるノイズ格子の1辺です。単位はタイルです。</summary>
        public int RockWallMassCellSizeInTiles;

        /// <summary>岩壁輪郭の細かな凹凸を決めるノイズ格子の1辺です。単位はタイルです。</summary>
        public int RockWallDetailCellSizeInTiles;

        /// <summary>岩壁密度をサンプリングする座標の最大歪み幅です。単位はタイルです。</summary>
        public int RockWallWarpAmplitudeInTiles;

        /// <summary>これ以上を岩壁候補にする0〜65535の合成ノイズ閾値です。</summary>
        public ushort RockWallNoiseThreshold;

        /// <summary>岩壁から削る通路の中心値32768から許容する0〜65535の差です。</summary>
        public ushort RockWallCorridorHalfWidth;

        /// <summary>これ以上を高温とみなす0〜65535の気温閾値です。</summary>
        public ushort HotTemperatureThreshold;

        /// <summary>これ未満を低温とみなし、雪原適性を高める0〜65535の気温閾値です。</summary>
        public ushort ColdTemperatureThreshold;

        /// <summary>これ未満を乾燥とみなす0〜65535の湿度閾値です。</summary>
        public ushort DryMoistureThreshold;

        /// <summary>これ以上を森林候補とみなす0〜65535の湿度閾値です。</summary>
        public ushort WetMoistureThreshold;

        /// <summary>これ以上を湿地候補とみなす0〜65535の最高湿度閾値です。</summary>
        public ushort WetlandMoistureThreshold;

        /// <summary>これ以下を湿地候補にできる低地とみなす0〜65535の標高上限です。</summary>
        public ushort WetlandMaximumElevation;

        /// <summary>これ以上を岩地・火山候補にできる高地とみなす0〜65535の標高閾値です。</summary>
        public ushort HighlandElevationThreshold;

        /// <summary>これ以上を火山候補にできる異常度とみなす0〜65535のWeirdness閾値です。</summary>
        public ushort VolcanicWeirdnessThreshold;

        /// <summary>強く抑制する2バイオームの適性差がこの値以下なら緩衝バイオームへ置換します。</summary>
        public ushort BiomeAdjacencyTransitionScoreMargin;

        /// <summary>
        /// Phase 1〜2の比較基準となる既定設定を作ります。
        /// 固定小数点の既定閾値はこの型へ集約し、Authoringの初期値も同じ値を0〜1へ戻して使用します。
        /// 初期開発中は呼び出し側から1を渡し、正式なセーブ互換運用開始後だけ非互換変更に合わせて更新します。
        /// </summary>
        /// <param name="worldSeed">文字列から変換済みのワールドシードです。</param>
        /// <param name="generatorVersion">保存対象の生成器バージョンです。</param>
        /// <returns>7バイオームと水域・岩壁を生成できる設定です。</returns>
        public static WorldGenerationSettings CreateDefault(ulong worldSeed, int generatorVersion)
        {
            return new WorldGenerationSettings
            {
                WorldSeed = worldSeed,
                GeneratorVersion = generatorVersion,
                ElevationCellSizeInTiles = 192,
                TemperatureCellSizeInTiles = 96,
                MoistureCellSizeInTiles = 80,
                WeirdnessCellSizeInTiles = 112,
                BiomeWarpCellSizeInTiles = 192,
                BiomeWarpAmplitudeInTiles = 12,
                OctaveCount = 4,
                BiomeClassificationTransitionWidthInTiles = 12,
                BiomeTransitionWidthInTiles = 6,
                SeaLevelThreshold = DefaultSeaLevelThreshold,
                RiverCellSizeInTiles = 96,
                PondCellSizeInTiles = 48,
                FreshWaterOctaveCount = 2,
                RiverCenterLineHalfWidth = 600,
                PondThreshold = 57000,
                WetClimatePondThresholdReduction = 8500,
                WetlandPondThresholdReduction = 8500,
                DesertPondThresholdIncrease = 5000,
                HighlandPondThresholdIncrease = 3500,
                VolcanicPondThresholdIncrease = 16000,
                WetlandRiverHalfWidthIncrease = 500,
                VolcanicRiverHalfWidthReduction = 450,
                FreshWaterMaximumElevation = 47000,
                MagmaCellSizeInTiles = 24,
                MagmaDetailCellSizeInTiles = 8,
                MagmaWarpAmplitudeInTiles = 6,
                MagmaOctaveCount = 3,
                MagmaNoiseThreshold = 34000,
                MagmaInfluenceNoiseThreshold = 30000,
                MagmaMinimumVolcanicInfluence = 0,
                MagmaInfluenceMinimumVolcanicInfluence = 0,
                RocklandMagmaCellSizeInTiles = 12,
                RocklandMagmaNoiseThreshold = 47000,
                RocklandMagmaInfluenceNoiseThreshold = 44000,
                RocklandMagmaMinimumVolcanicInfluence = 100,
                RocklandMagmaInfluenceMinimumVolcanicInfluence = 50,
                RockWallMassCellSizeInTiles = 24,
                RockWallDetailCellSizeInTiles = 8,
                RockWallWarpAmplitudeInTiles = 6,
                RockWallNoiseThreshold = 41000,
                RockWallCorridorHalfWidth = 1100,
                HotTemperatureThreshold = DefaultHotTemperatureThreshold,
                ColdTemperatureThreshold = 23000,
                DryMoistureThreshold = DefaultDryMoistureThreshold,
                WetMoistureThreshold = DefaultWetMoistureThreshold,
                WetlandMoistureThreshold = 50000,
                WetlandMaximumElevation = 36000,
                HighlandElevationThreshold = 43000,
                VolcanicWeirdnessThreshold = 47000,
                BiomeAdjacencyTransitionScoreMargin = 18000
            };
        }
    }

    /// <summary>
    /// 1タイルの淡水判定で使った連続補正値とノイズ値です。
    /// 開発者用HUDとEditModeテストがSimulationと同じ計算結果を読み、表示だけの再実装を防ぎます。
    /// </summary>
    public struct WaterGenerationDebugInfo
    {
        /// <summary>池の形状ノイズです。0〜65535で、大きいほど池候補になりやすい値です。</summary>
        public ushort PondSample;

        /// <summary>気候補正後の池判定下限です。65535のときは池生成を無効として扱います。</summary>
        public ushort EffectivePondThreshold;

        /// <summary>川の形状ノイズです。中心値32768へ近いほど川候補になりやすい値です。</summary>
        public ushort RiverSample;

        /// <summary>川ノイズの中心値32768からの絶対距離です。</summary>
        public ushort RiverDistanceFromCenterLine;

        /// <summary>気候補正後に許容する川中心線からの距離です。</summary>
        public ushort EffectiveRiverHalfWidth;

        /// <summary>森林に近い湿潤条件の0〜65535連続重みです。</summary>
        public ushort WetClimateInfluence;

        /// <summary>低地かつ高湿度の湿地条件の0〜65535連続重みです。</summary>
        public ushort WetlandInfluence;

        /// <summary>高温かつ乾燥した砂漠条件の0〜65535連続重みです。</summary>
        public ushort DesertInfluence;

        /// <summary>高地の岩地条件の0〜65535連続重みです。</summary>
        public ushort HighlandInfluence;

        /// <summary>高温・高地・高Weirdnessの火山条件の0〜65535連続重みです。</summary>
        public ushort VolcanicInfluence;

        /// <summary>海、池、川、陸地の優先判定を終えた最終水域種別です。</summary>
        public WaterBodyType WaterBodyType;
    }

    /// <summary>
    /// 1タイルのマグマ判定に使った火山適性と専用ノイズ値です。
    /// Simulation、開発者HUD、EditModeテストが同じ判定結果を読み、淡水抑制域を含む生成規則のずれを防ぎます。
    /// </summary>
    public struct MagmaGenerationDebugInfo
    {
        /// <summary>高温・高地・高Weirdnessを掛け合わせた0〜65535の連続火山適性です。</summary>
        public ushort VolcanicInfluence;

        /// <summary>火山では大域形状・細部形状・座標ワープを合成した密度、岩地では小規模溜まり用ノイズを0〜65535で保持します。</summary>
        public ushort MagmaSample;

        /// <summary>火山適性を適用する前の生密度が淡水抑制域の閾値以上ならtrueです。</summary>
        public bool RawInfluenceArea;

        /// <summary>火山適性と境界平滑化を適用する前の生密度がマグマ本体の閾値以上ならtrueです。</summary>
        public bool RawMagmaCandidate;

        /// <summary>離散バイオームが火山または火山境界の岩地で、マグマ本体を置ける場合はtrueです。</summary>
        public bool IsEligibleBiome;

        /// <summary>岩地専用の小規模・低頻度プロファイルで判定した場合はtrueです。</summary>
        public bool UsesRocklandProfile;

        /// <summary>淡水を生成しない火山・マグマ影響域に含まれる場合はtrueです。</summary>
        public bool IsInfluenceArea;

        /// <summary>海の優先判定前に、このタイルがマグマ本体の候補になった場合はtrueです。</summary>
        public bool IsMagma;
    }

    /// <summary>
    /// ワールド全体の基礎資源群について、ノイズ密度、群中心の最低間隔、群半径を保持する生成設定です。
    /// AuthoringからSingleton Entityへ変換し、チャンク生成Jobと初期位置探索が同じ値を参照します。
    /// </summary>
    public struct ResourceGenerationSettings : IComponentData
    {
        /// <summary>資源群をまとめるノイズの基準格子サイズです。単位はタイルです。</summary>
        public int ClusterCellSizeInTiles;

        /// <summary>資源群の輪郭へ重ねるノイズ解像度の数です。</summary>
        public int OctaveCount;

        /// <summary>樹木を配置する0〜65535のノイズ下限です。大きいほど樹木が減ります。</summary>
        public ushort TreeThreshold;

        /// <summary>石を配置する0〜65535のノイズ下限です。大きいほど石が減ります。</summary>
        public ushort StoneThreshold;

        /// <summary>砂資源を配置する0〜65535のノイズ下限です。大きいほど砂資源が減ります。</summary>
        public ushort SandThreshold;

        /// <summary>樹木群の中心同士に確保する最小距離です。単位はタイルです。</summary>
        public int TreeMinimumSpacingInTiles;

        /// <summary>石群の中心同士に確保する最小距離です。単位はタイルです。</summary>
        public int StoneMinimumSpacingInTiles;

        /// <summary>砂資源群の中心同士に確保する最小距離です。単位はタイルです。</summary>
        public int SandMinimumSpacingInTiles;

        /// <summary>樹木群を中心から広げる最大半径です。単位はタイルです。</summary>
        public int TreeClusterRadiusInTiles;

        /// <summary>石群を中心から広げる最大半径です。単位はタイルです。</summary>
        public int StoneClusterRadiusInTiles;

        /// <summary>砂資源群を中心から広げる最大半径です。単位はタイルです。</summary>
        public int SandClusterRadiusInTiles;

        /// <summary>
        /// Phase 3の初期検証に使う資源群設定を作ります。
        /// 24タイル格子のノイズで群の輪郭を作り、種類別の最低間隔と半径で密集しすぎない分布へ整える暫定値です。
        /// </summary>
        /// <returns>木、石、砂をワールド全体へ群生させる設定です。</returns>
        public static ResourceGenerationSettings CreateDefault()
        {
            return new ResourceGenerationSettings
            {
                ClusterCellSizeInTiles = 24,
                OctaveCount = 2,
                TreeThreshold = 50000,
                StoneThreshold = 55500,
                SandThreshold = 48000,
                TreeMinimumSpacingInTiles = 14,
                StoneMinimumSpacingInTiles = 20,
                SandMinimumSpacingInTiles = 18,
                TreeClusterRadiusInTiles = 5,
                StoneClusterRadiusInTiles = 4,
                SandClusterRadiusInTiles = 6
            };
        }
    }

    /// <summary>
    /// 新規ワールド開始時に決定した初期スポーン地点と、その根拠になった建設可能領域を保持します。
    /// Simulationが一度だけ作成し、カメラ、将来のドローン生成、セーブ処理、F3表示が同じ座標を参照します。
    /// </summary>
    public struct InitialSpawnLocation : IComponentData
    {
        /// <summary>初期スポーンに選んだ水平ワールドタイル座標です。ワールド存続中は変更しません。</summary>
        public int TileX;

        /// <summary>初期スポーンに選んだ垂直ワールドタイル座標です。ワールド存続中は変更しません。</summary>
        public int TileY;

        /// <summary>選択元となった建設可能矩形の左下水平タイル座標です。</summary>
        public int RegionMinimumTileX;

        /// <summary>選択元となった建設可能矩形の左下垂直タイル座標です。</summary>
        public int RegionMinimumTileY;

        /// <summary>選択元となった建設可能矩形の横幅です。単位はタイルです。</summary>
        public int RegionWidthInTiles;

        /// <summary>選択元となった建設可能矩形の縦幅です。単位はタイルです。</summary>
        public int RegionHeightInTiles;

        /// <summary>
        /// 発見済みの建設可能矩形から、将来プレイヤーやドローンにも利用できる中央寄りの1タイルを選びます。
        /// 偶数幅では中央2タイルのうち右・上側を選び、常に整数タイル座標として記録します。
        /// </summary>
        /// <param name="region">全セルが建設可能と確認済みの矩形です。</param>
        /// <returns>矩形情報と、その内部にある初期スポーンタイルです。</returns>
        public static InitialSpawnLocation FromRegion(in EquipmentBuildableRegion region)
        {
            return new InitialSpawnLocation
            {
                TileX = region.MinimumTileX + (region.WidthInTiles / 2),
                TileY = region.MinimumTileY + (region.HeightInTiles / 2),
                RegionMinimumTileX = region.MinimumTileX,
                RegionMinimumTileY = region.MinimumTileY,
                RegionWidthInTiles = region.WidthInTiles,
                RegionHeightInTiles = region.HeightInTiles
            };
        }
    }

    /// <summary>
    /// 初期スポーン探索が現在どの段階にあるかを表します。
    /// Presentationは失敗も完了状態として扱い、カメラをScene保存位置に保ったままチャンク要求を開始します。
    /// </summary>
    public enum InitialSpawnSearchPhase : byte
    {
        /// <summary>生成設定の準備を待っており、探索Jobをまだ開始していません。</summary>
        WaitingForSettings = 0,

        /// <summary>Worker Threadで初期スポーン候補を探索しています。</summary>
        Searching = 1,

        /// <summary>候補を発見し、InitialSpawnLocation Singletonを記録済みです。</summary>
        Succeeded = 2,

        /// <summary>探索上限内に候補がなく、Scene保存位置を代替の表示開始位置として使います。</summary>
        Failed = 3
    }

    /// <summary>
    /// 初期スポーン探索の進行状況をワールド全体で1件だけ保持します。
    /// InitialSpawnLocationSystemが更新し、初期カメラとストリーミング開始条件が同じ結果を参照します。
    /// </summary>
    public struct InitialSpawnSearchStatus : IComponentData
    {
        /// <summary>現在の探索段階です。System作成からWorld破棄まで保持し、探索完了後も結果を残します。</summary>
        public InitialSpawnSearchPhase Phase;
    }

    /// <summary>
    /// カメラ周辺チャンクの生成・保持・表示反映に使う実行時設定です。
    /// WorldGenerationAuthoringから変換され、ストリーミングSystemとPresentationが同じ予算を参照します。
    /// </summary>
    public struct ChunkStreamingSettings : IComponentData
    {
        /// <summary>可視矩形の外側へ先読みする論理チャンク数です。実行時は最低1へ補正されます。</summary>
        public int PreloadMarginInChunks;

        /// <summary>プリロード範囲の外側で生成済みデータを一時保持する論理チャンク数です。</summary>
        public int RetentionMarginInChunks;

        /// <summary>QueuedまたはGeneratingDataとして同時に保持できる生成要求の上限です。</summary>
        public int MaximumConcurrentGenerations;

        /// <summary>1フレームにTilemapへ反映または消去できる16×16サブチャンク数です。</summary>
        public int MaximumSubChunkApplicationsPerFrame;

        /// <summary>
        /// 初期検証用のストリーミング設定を作ります。
        /// 生成負荷や対象端末を計測した後、AuthoringのInspector値で調整します。
        /// </summary>
        /// <returns>可視範囲の外を1チャンク先読みし、最大2チャンクを並列生成する設定です。</returns>
        public static ChunkStreamingSettings CreateDefault()
        {
            return new ChunkStreamingSettings
            {
                PreloadMarginInChunks = 1,
                RetentionMarginInChunks = 1,
                MaximumConcurrentGenerations = 2,
                MaximumSubChunkApplicationsPerFrame = 4
            };
        }
    }

    /// <summary>
    /// Presentationがカメラ表示範囲から計算し、ECS Simulationへ渡すストリーミング入力です。
    /// 値は論理チャンク座標の包含矩形であり、Transformのfloat座標を生成判定の正にはしません。
    /// </summary>
    public struct ChunkStreamingObserver : IComponentData
    {
        /// <summary>画面に少しでも含まれる最小の水平論理チャンク座標です。</summary>
        public int MinimumVisibleChunkX;

        /// <summary>画面に少しでも含まれる最大の水平論理チャンク座標です。</summary>
        public int MaximumVisibleChunkX;

        /// <summary>画面に少しでも含まれる最小の垂直論理チャンク座標です。</summary>
        public int MinimumVisibleChunkY;

        /// <summary>画面に少しでも含まれる最大の垂直論理チャンク座標です。</summary>
        public int MaximumVisibleChunkY;
    }

    /// <summary>
    /// 1つのチャンクEntityが表す論理チャンク座標です。
    /// Entityの実行時Indexではなく、この値とワールドIDをセーブ上の識別に使用します。
    /// </summary>
    public struct ChunkCoordinateComponent : IComponentData
    {
        /// <summary>論理チャンクの水平座標です。</summary>
        public int X;

        /// <summary>論理チャンクの垂直座標です。</summary>
        public int Y;
    }

    /// <summary>
    /// 1つのチャンクEntityに対する現在の生成段階と生成器バージョンを保持します。
    /// ストリーミングSystemとPresentationが状態遷移を共有するために使用します。
    /// </summary>
    public struct ChunkGenerationStatus : IComponentData
    {
        /// <summary>現在の処理段階です。生成SystemがQueuedからReadyToApplyへ進めます。</summary>
        public ChunkGenerationPhase Phase;

        /// <summary>このチャンクの基礎データを作ったGeneratorVersionです。</summary>
        public int GeneratedWithVersion;
    }

    /// <summary>
    /// チャンクの純粋データ生成が必要であることを示すEnableable Componentです。
    /// 完了時は削除せず無効化し、頻繁な構造変更と毎フレームの生成済みチャンク走査を避けます。
    /// </summary>
    public struct ChunkGenerationRequest : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// 1タイル分の決定論的な基礎地形データです。
    /// 1チャンクEntityのDynamic Bufferへ行優先で4096件格納し、表示用Unityオブジェクトとは分離します。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GeneratedTile : IBufferElementData
    {
        /// <summary>このタイルが属する初期バイオームの安定IDです。</summary>
        public BiomeId Biome;

        /// <summary>建築やリンク判定に使う基礎地面種別です。</summary>
        public GroundType Ground;

        /// <summary>陸地の資源適性と表示差分に使う土壌種別です。陸地以外ではNoneです。</summary>
        public SoilType Soil;

        /// <summary>水域なら淡水または海水、陸地ならNoneです。</summary>
        public WaterSalinity WaterSalinity;

        /// <summary>水域なら川・池・海の生成由来、陸地ならNoneです。</summary>
        public WaterBodyType WaterBodyType;

        /// <summary>連続標高をゲームルール向けに分類した値です。</summary>
        public ElevationClass ElevationClass;

        /// <summary>通常設備、通常ベルト、橋の配置可否と、橋の耐熱要件をまとめたビットフラグです。</summary>
        public TileGameplayFlags GameplayFlags;

        /// <summary>Phase 3の資源配置が参照する候補種別のビットフラグです。</summary>
        public ResourceSuitability ResourceSuitability;

        /// <summary>経路探索用の相対移動コストです。0は通常移動不可を表します。</summary>
        public byte MovementCost;

        /// <summary>0〜65535へ正規化した連続標高値です。</summary>
        public ushort Elevation;

        /// <summary>0〜65535へ正規化した連続気温値です。</summary>
        public ushort Temperature;

        /// <summary>0〜65535へ正規化した連続湿度値です。</summary>
        public ushort Moisture;

        /// <summary>0〜65535へ正規化した火山・特殊地域候補用の連続Weirdness値です。</summary>
        public ushort Weirdness;
    }

    /// <summary>
    /// 1タイルに対応する基礎資源の生成結果です。
    /// GeneratedTileと同じ行優先4096件のDynamic Bufferへ格納し、採取やセーブ差分では地形と別に変更します。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GeneratedResourceTile : IBufferElementData
    {
        /// <summary>このタイルへ初期配置された基礎資源の安定IDです。</summary>
        public GeneratedResourceType Type;

        /// <summary>同じ資源内の見た目差分に使う0〜3の決定論的な番号です。</summary>
        public byte VisualVariant;
    }
}
