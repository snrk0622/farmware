// このファイルは、決定論的チャンク生成で使うECS Component、Dynamic Buffer要素、分類IDを定義します。
// ゲーム状態の正をEntity側へ置き、後続のPresentationは生成済みバッファを読み取るだけにする境界です。

using System;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 初期実装で生成する陸上バイオームの安定IDです。
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
        Desert = 3
    }

    /// <summary>
    /// ゲームルールが参照する基礎地面種別です。
    /// 表示用SpriteやRuleTileの種類とは分離し、建築・リンク判定の入力に使用します。
    /// </summary>
    public enum GroundType : byte
    {
        /// <summary>通常の建築可能地面です。</summary>
        Normal = 0,

        /// <summary>通常ノードは建築不可ですが、通常リンクは通過できる水域です。</summary>
        Water = 1,

        /// <summary>建築と通常リンクの両方を遮断する岩壁です。</summary>
        RockWall = 2,

        /// <summary>森林などの植生を持つ地面です。</summary>
        Vegetation = 3,

        /// <summary>砂資源に適する砂地です。</summary>
        Sand = 4
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

        /// <summary>通常ノードを建築できる地面です。</summary>
        IsBuildable = 1 << 0,

        /// <summary>通常物流リンクの直線経路を遮断します。</summary>
        BlocksNormalLink = 1 << 1
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

        /// <summary>各気候値へ重ねるノイズ解像度の数です。</summary>
        public int OctaveCount;

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

        /// <summary>川と池を生成できる0〜65535の最大標高です。山頂まで淡水が伸びることを防ぎます。</summary>
        public ushort FreshWaterMaximumElevation;

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

        /// <summary>これ未満を乾燥とみなす0〜65535の湿度閾値です。</summary>
        public ushort DryMoistureThreshold;

        /// <summary>これ以上を森林候補とみなす0〜65535の湿度閾値です。</summary>
        public ushort WetMoistureThreshold;

        /// <summary>
        /// Phase 1〜2の比較基準となる既定設定を作ります。
        /// 調整値はこの1か所に集約します。初期開発中は呼び出し側から1を渡し、正式なセーブ互換運用開始後だけ非互換変更に合わせて更新します。
        /// </summary>
        /// <param name="worldSeed">文字列から変換済みのワールドシードです。</param>
        /// <param name="generatorVersion">保存対象の生成器バージョンです。</param>
        /// <returns>3バイオームと水域・岩壁を生成できる設定です。</returns>
        public static WorldGenerationSettings CreateDefault(ulong worldSeed, int generatorVersion)
        {
            return new WorldGenerationSettings
            {
                WorldSeed = worldSeed,
                GeneratorVersion = generatorVersion,
                ElevationCellSizeInTiles = 192,
                TemperatureCellSizeInTiles = 96,
                MoistureCellSizeInTiles = 80,
                OctaveCount = 4,
                BiomeTransitionWidthInTiles = 6,
                SeaLevelThreshold = 20000,
                RiverCellSizeInTiles = 96,
                PondCellSizeInTiles = 48,
                FreshWaterOctaveCount = 2,
                RiverCenterLineHalfWidth = 600,
                PondThreshold = 57000,
                FreshWaterMaximumElevation = 47000,
                RockWallMassCellSizeInTiles = 24,
                RockWallDetailCellSizeInTiles = 8,
                RockWallWarpAmplitudeInTiles = 6,
                RockWallNoiseThreshold = 41000,
                RockWallCorridorHalfWidth = 1100,
                HotTemperatureThreshold = 40000,
                DryMoistureThreshold = 28500,
                WetMoistureThreshold = 39500
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
        public static InitialSpawnLocation FromRegion(in NodeBuildableRegion region)
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

        /// <summary>水域なら淡水または海水、陸地ならNoneです。</summary>
        public WaterSalinity WaterSalinity;

        /// <summary>水域なら川・池・海の生成由来、陸地ならNoneです。</summary>
        public WaterBodyType WaterBodyType;

        /// <summary>連続標高をゲームルール向けに分類した値です。</summary>
        public ElevationClass ElevationClass;

        /// <summary>建築可能・リンク遮断などをまとめたビットフラグです。</summary>
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
