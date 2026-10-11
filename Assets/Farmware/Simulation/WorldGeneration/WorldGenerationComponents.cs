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

        /// <summary>岩壁になる直前の高地です。</summary>
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
    /// 実際の資源配置はPhase 3で別Systemがこの適性と座標ハッシュを組み合わせて決定します。
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

        /// <summary>水源として利用できます。</summary>
        Water = 1 << 2,

        /// <summary>砂の採取候補にできます。</summary>
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

        /// <summary>これ未満の標高を水域にする0〜65535の閾値です。</summary>
        public ushort WaterThreshold;

        /// <summary>これ以上の標高を岩壁にする0〜65535の閾値です。</summary>
        public ushort RockWallThreshold;

        /// <summary>これ以上を高温とみなす0〜65535の気温閾値です。</summary>
        public ushort HotTemperatureThreshold;

        /// <summary>これ未満を乾燥とみなす0〜65535の湿度閾値です。</summary>
        public ushort DryMoistureThreshold;

        /// <summary>これ以上を森林候補とみなす0〜65535の湿度閾値です。</summary>
        public ushort WetMoistureThreshold;

        /// <summary>
        /// Phase 1〜2の比較基準となる既定設定を作ります。
        /// 調整値はこの1か所に集約し、生成結果を変える変更ではGeneratorVersionも更新します。
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
                TemperatureCellSizeInTiles = 192,
                MoistureCellSizeInTiles = 160,
                OctaveCount = 4,
                BiomeTransitionWidthInTiles = 6,
                WaterThreshold = 20000,
                RockWallThreshold = 53500,
                HotTemperatureThreshold = 40000,
                DryMoistureThreshold = 28500,
                WetMoistureThreshold = 39500
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
}
