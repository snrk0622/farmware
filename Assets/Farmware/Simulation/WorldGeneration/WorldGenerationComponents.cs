// このファイルは、決定論的チャンク生成で使うECS Component、Dynamic Buffer要素、分類IDを定義します。
// ゲーム状態の正をEntity側へ置き、後続のPresentationは生成済みバッファを読み取るだけにする境界です。

using System;
using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// TERRAIN-01C5以降の地上18バイオームへ割り当てる安定IDです。
    /// 旧7種とは数値の意味に互換性がないため、C5以前の生成結果をこの列挙値へ読み替えてはいけません。
    /// </summary>
    public enum BiomeId : byte
    {
        /// <summary>未設定または生成失敗を表します。</summary>
        None = 0,

        /// <summary>温帯草原です。</summary>
        TemperateGrassland = 1,

        /// <summary>熱帯サバンナ・疎林です。</summary>
        TropicalSavannaWoodland = 2,

        /// <summary>温帯落葉広葉樹林です。</summary>
        TemperateDeciduousForest = 3,

        /// <summary>暖温帯照葉樹林です。</summary>
        WarmTemperateEvergreenForest = 4,

        /// <summary>温帯山地竹林です。</summary>
        TemperateMontaneBambooForest = 5,

        /// <summary>熱帯雨林です。</summary>
        TropicalRainforest = 6,

        /// <summary>熱帯季節林・モンスーン林です。</summary>
        TropicalSeasonalForest = 7,

        /// <summary>亜熱帯海洋性林です。</summary>
        SubtropicalOceanicForest = 8,

        /// <summary>亜寒帯針葉樹林・タイガです。</summary>
        BorealConiferForest = 9,

        /// <summary>ステップです。</summary>
        Steppe = 10,

        /// <summary>乾燥低木地・疎林です。</summary>
        DryShrublandWoodland = 11,

        /// <summary>サボテン砂漠・乾燥高原です。</summary>
        CactusDesertDryPlateau = 12,

        /// <summary>高温砂漠です。</summary>
        HotDesert = 13,

        /// <summary>淡水湿原・氾濫原です。</summary>
        FreshwaterMarshFloodplain = 14,

        /// <summary>湿地林・沼沢林です。</summary>
        SwampForest = 15,

        /// <summary>ツンドラ・極地荒原です。</summary>
        TundraPolarBarrens = 16,

        /// <summary>高山岩地です。</summary>
        AlpineRockland = 17,

        /// <summary>火山・地熱地帯です。</summary>
        VolcanicGeothermal = 18
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
    /// 連続した標高値を、地面・水域・岩壁の表示状態とは独立して分類した安定値です。
    /// デバッグ表示とBiome候補条件で使用し、河川や岩壁でも元の標高区分を保持します。
    /// </summary>
    public enum ElevationClass : byte
    {
        /// <summary>海面閾値より低い海面下の領域です。</summary>
        BelowSeaLevel = 0,

        /// <summary>水際に近い低地です。</summary>
        Lowland = 1,

        /// <summary>低地上限より高く、山地境界より低い平地・台地です。</summary>
        PlainOrPlateau = 2,

        /// <summary>標高43,000以上54,000未満の山地です。</summary>
        Mountain = 3,

        /// <summary>標高54,000以上の高山です。</summary>
        Alpine = 4
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
        /// <summary>
        /// 地上18Biomeの固定標高範囲と両立できる海面閾値の下限です。
        /// これ未満を許可すると海面以上20,000未満に分類先のない陸地が生じるため、設定検証とAuthoring補正で共有します。
        /// </summary>
        public const ushort MinimumSupportedSeaLevelThreshold = 20000;

        /// <summary>C5の新18Biome形式として受理する最小かつ現在の生成器バージョンです。</summary>
        public const int CurrentGeneratorVersion = 2;

        /// <summary>既定の海面を表す0〜65535の固定小数点閾値です。Authoringと分布監査が同じ値を共有します。</summary>
        public const ushort DefaultSeaLevelThreshold = MinimumSupportedSeaLevelThreshold;

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

        /// <summary>気候ノイズのサンプリング座標を曲げる低周波格子サイズです。単位はタイルです。</summary>
        public int BiomeWarpCellSizeInTiles;

        /// <summary>気候ノイズのサンプリング座標を最大何タイルずらすかを表します。</summary>
        public int BiomeWarpAmplitudeInTiles;

        /// <summary>各気候値へ重ねるノイズ解像度の数です。</summary>
        public int OctaveCount;

        /// <summary>
        /// 地形気温、木本植生、起伏、海洋影響、火山活動度を再計算する固定設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public ContinuousEnvironmentSettings EnvironmentAxes;

        /// <summary>
        /// 固定地質Definition、512タイル地域、境界ワープ、サイト探索を再計算する設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public GeologyGenerationSettings Geology;

        /// <summary>
        /// 固定8×8水文セル、代表標高9点、D8流向を再計算する設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public HydrologyGenerationSettings Hydrology;

        /// <summary>
        /// 地下水用の局所地質補正を再計算する固定設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public GroundwaterGenerationSettings Groundwater;

        /// <summary>
        /// 地下水計算後の湖水収支区分と泉生成を再計算する固定設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public LakeSpringGenerationSettings LakeSpring;

        /// <summary>
        /// 恒久淡水源からの自然冠水度と、後続の広域湿原規模を再計算する固定設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public NaturalFloodingGenerationSettings NaturalFlooding;

        /// <summary>
        /// Biome表層、地質差分、露出基盤、泥炭条件を再計算する固定設定です。
        /// GeneratorVersionと一緒にワールド寿命中は変更しません。
        /// </summary>
        public TerrainBlockPaletteSelectionSettings TerrainBlockPaletteSelection;

        /// <summary>
        /// これ未満の標高を海水域にする固定小数点閾値です。
        /// 地上18Biomeの固定標高範囲と両立する20,000以上、保存済み低地上限以下を設定検証で要求します。
        /// </summary>
        public ushort SeaLevelThreshold;

        /// <summary>活動度段階5の火山中核内で、広いマグマ面を決める大域ノイズ格子サイズです。単位はタイルです。</summary>
        public int MagmaCellSizeInTiles;

        /// <summary>火山マグマの入り江、半島、島を作る細部ノイズ格子サイズです。単位はタイルです。</summary>
        public int MagmaDetailCellSizeInTiles;

        /// <summary>火山マグマ密度をサンプリングする座標の最大歪み幅です。単位はタイルです。</summary>
        public int MagmaWarpAmplitudeInTiles;

        /// <summary>火山マグマの大域形状へ重ねるノイズ解像度の数です。</summary>
        public int MagmaOctaveCount;

        /// <summary>活動度段階5の火山中核をマグマ本体へ変換する0〜65535の専用ノイズ下限です。大きいほど陸地が増えます。</summary>
        public ushort MagmaNoiseThreshold;

        /// <summary>活動度段階5の火山中核内で、マグマをさらに制限する0〜65535の追加下限です。既定値0でも段階5未満は許可しません。</summary>
        public ushort MagmaMinimumVolcanicInfluence;

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

        /// <summary>
        /// Phase 1〜2の比較基準となる既定設定を作ります。
        /// 固定小数点の既定閾値はこの型へ集約し、Authoringの初期値も同じ値を0〜1へ戻して使用します。
        /// C5より前の値は生成側の検証で拒否し、新旧Biomeの混在を防ぎます。
        /// </summary>
        /// <param name="worldSeed">文字列から変換済みのワールドシードです。</param>
        /// <param name="generatorVersion">保存対象の生成器バージョンです。</param>
        /// <returns>新18Biomeと固定水文・岩壁を生成できる設定です。</returns>
        public static WorldGenerationSettings CreateDefault(ulong worldSeed, int generatorVersion)
        {
            return new WorldGenerationSettings
            {
                WorldSeed = worldSeed,
                GeneratorVersion = generatorVersion,
                ElevationCellSizeInTiles = 192,
                TemperatureCellSizeInTiles = 96,
                MoistureCellSizeInTiles = 80,
                BiomeWarpCellSizeInTiles = 192,
                BiomeWarpAmplitudeInTiles = 12,
                OctaveCount = 4,
                EnvironmentAxes = ContinuousEnvironmentSettings.CreateDefault(),
                Geology = GeologyGenerationSettings.CreateDefault(),
                Hydrology = HydrologyGenerationSettings.CreateDefault(),
                Groundwater = GroundwaterGenerationSettings.CreateDefault(),
                LakeSpring = LakeSpringGenerationSettings.CreateDefault(),
                NaturalFlooding = NaturalFloodingGenerationSettings.CreateDefault(),
                TerrainBlockPaletteSelection =
                    TerrainBlockPaletteSelectionSettings.CreateDefault(),
                SeaLevelThreshold = DefaultSeaLevelThreshold,
                MagmaCellSizeInTiles = 24,
                MagmaDetailCellSizeInTiles = 8,
                MagmaWarpAmplitudeInTiles = 6,
                MagmaOctaveCount = 3,
                MagmaNoiseThreshold = 34000,
                MagmaMinimumVolcanicInfluence = 0,
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
                HighlandElevationThreshold = 43000
            };
        }
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
    /// 新規ワールド開始時に完成地形から決定した初期スポーン地点を保持します。
    /// Simulationが一度だけ作成し、カメラ、将来のプレイヤー開始位置、セーブ処理、F3表示が同じ座標を参照します。
    /// </summary>
    public struct InitialSpawnLocation : IComponentData
    {
        /// <summary>初期スポーンが属する固定ワールドレイヤーです。現在の開始地点探索はSurfaceだけを選びます。</summary>
        public WorldLayer Layer;

        /// <summary>初期スポーンに選んだ水平ワールドタイル座標です。ワールド存続中は変更しません。</summary>
        public int TileX;

        /// <summary>初期スポーンに選んだ垂直ワールドタイル座標です。ワールド存続中は変更しません。</summary>
        public int TileY;

        /// <summary>完成済みSurfaceタイル座標から保存対象の初期スポーン地点を作ります。</summary>
        /// <param name="tileX">乾いた通行可能セルの水平ワールドタイル座標です。</param>
        /// <param name="tileY">乾いた通行可能セルの垂直ワールドタイル座標です。</param>
        /// <returns>Surfaceと選択座標だけを持つ初期スポーン地点です。</returns>
        public static InitialSpawnLocation FromTile(int tileX, int tileY)
        {
            return new InitialSpawnLocation
            {
                Layer = WorldLayer.Surface,
                TileX = tileX,
                TileY = tileY
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

        /// <summary>有限リングの完成ECSチャンクを生成・走査しています。</summary>
        Searching = 1,

        /// <summary>候補を発見し、InitialSpawnLocation Singletonを記録済みです。</summary>
        Succeeded = 2,

        /// <summary>探索上限内に候補がなく、開始位置を決定できなかった状態です。</summary>
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
        /// <summary>
        /// 表示・先読み対象の固定ワールドレイヤーです。
        /// カメラのTransform Zから推測せず、Presentationが現在の論理レイヤーを明示的に書き込みます。
        /// </summary>
        public WorldLayer Layer;

        /// <summary>画面に少しでも含まれる最小の水平論理チャンク座標です。</summary>
        public int MinimumVisibleChunkX;

        /// <summary>画面に少しでも含まれる最大の水平論理チャンク座標です。</summary>
        public int MaximumVisibleChunkX;

        /// <summary>画面に少しでも含まれる最小の垂直論理チャンク座標です。</summary>
        public int MinimumVisibleChunkY;

        /// <summary>画面に少しでも含まれる最大の垂直論理チャンク座標です。</summary>
        public int MaximumVisibleChunkY;

        /// <summary>
        /// 旧Scene由来の未設定LayerをSurfaceへ補い、各軸の最小値と最大値を包含矩形として正しい順序へ並べます。
        /// SimulationとPresentationが同じ補正結果を使うための互換境界であり、元のComponent値は変更しません。
        /// </summary>
        /// <returns>有効な固定レイヤーと昇順の包含矩形を持つObserverのコピーです。</returns>
        public readonly ChunkStreamingObserver Normalized()
        {
            ChunkStreamingObserver normalized = this;

            // WORLD-01Cより前のObserverにはLayerフィールドがなく、追加後の既定値Noneとして読み込まれます。
            // 旧Sceneは地上専用だったため、互換入口では推測可能なSurfaceへ一度だけ置き換えます。
            if (!WorldLayerUtility.IsPlayable(normalized.Layer))
            {
                normalized.Layer = WorldLayer.Surface;
            }

            if (normalized.MinimumVisibleChunkX > normalized.MaximumVisibleChunkX)
            {
                int temporary = normalized.MinimumVisibleChunkX;
                normalized.MinimumVisibleChunkX = normalized.MaximumVisibleChunkX;
                normalized.MaximumVisibleChunkX = temporary;
            }

            if (normalized.MinimumVisibleChunkY > normalized.MaximumVisibleChunkY)
            {
                int temporary = normalized.MinimumVisibleChunkY;
                normalized.MinimumVisibleChunkY = normalized.MaximumVisibleChunkY;
                normalized.MaximumVisibleChunkY = temporary;
            }

            return normalized;
        }
    }

    /// <summary>
    /// 1つのチャンクEntityが表す論理チャンク座標です。
    /// Entityの実行時Indexではなく、この値とワールドIDをセーブ上の識別に使用します。
    /// </summary>
    public struct ChunkCoordinateComponent : IComponentData
    {
        /// <summary>論理チャンクが属する固定ワールドレイヤーです。Entityの寿命中は変更しません。</summary>
        public WorldLayer Layer;

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
        /// <summary>現在の処理段階です。生成開始でGeneratingData、後続更新の完成結果公開でReadyToApplyへ進みます。</summary>
        public ChunkGenerationPhase Phase;

        /// <summary>このチャンクの基礎データを作ったGeneratorVersionです。</summary>
        public int GeneratedWithVersion;
    }

    /// <summary>
    /// チャンクの純粋データ生成が必要であることを示すEnableable Componentです。
    /// Job開始時に削除せず無効化して二重投入を防ぎ、実行中と完成済みはChunkGenerationStatusで区別します。
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

        /// <summary>水域や岩壁でも上書きしない、連続標高由来の5段階区分です。</summary>
        public ElevationClass ElevationClass;

        /// <summary>通常設備、通常ベルト、橋の配置可否と、橋の耐熱要件をまとめたビットフラグです。</summary>
        public TileGameplayFlags GameplayFlags;

        /// <summary>Phase 3の資源配置が参照する候補種別のビットフラグです。</summary>
        public ResourceSuitability ResourceSuitability;

        /// <summary>経路探索用の相対移動コストです。0は通常移動不可を表します。</summary>
        public byte MovementCost;

        /// <summary>0〜65535へ正規化した連続標高値です。</summary>
        public ushort Elevation;

        /// <summary>0〜65535へ正規化した、標高寒冷補正前の基礎気温値です。</summary>
        public ushort Temperature;

        /// <summary>0〜65535へ正規化した、現在土壌水分とは独立する基礎湿度値です。</summary>
        public ushort Moisture;

        /// <summary>基礎気温へ海面より上の標高寒冷補正を一度だけ適用した0〜65535の地形気温です。</summary>
        public ushort TerrainTemperature;

        /// <summary>配置後の樹木数とは独立した0〜65535の木本植生ポテンシャルです。</summary>
        public ushort WoodyVegetationPotential;

        /// <summary>固定9標高サンプルの局所差から求めた0〜65535の地形起伏です。</summary>
        public ushort TerrainRuggedness;

        /// <summary>固定32標高サンプルの海への近さと開放方向数から求めた0〜65535の海洋影響です。</summary>
        public ushort CoastalInfluence;

        /// <summary>地質と旧Weirdnessから独立した0〜65535の火山活動度です。</summary>
        public ushort VolcanicActivityPotential;

        /// <summary>Biome、Block、資源IDから独立した固定の卓越基盤地質です。</summary>
        public GeologyClass GeologyClass;

        /// <summary>支配地質Definitionが持つカルスト・活動的火山の明示フラグです。</summary>
        public GeologyDefinitionFlags GeologyFlags;

        /// <summary>支配地質Definitionからコピーした0〜65,535の基礎透水性です。</summary>
        public ushort GeologyPermeability;

        /// <summary>支配地質Definitionからコピーした0〜65,535の基礎地下水保持力です。</summary>
        public ushort GeologyGroundwaterStorage;

        /// <summary>支配地質Definitionからコピーした0〜65,535の基礎侵食抵抗です。</summary>
        public ushort GeologyErosionResistance;
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
