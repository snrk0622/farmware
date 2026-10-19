// このファイルは、野生動物の地域初期化状態と個体状態をECSの純粋データとして定義します。
// 地形を参照する生成処理はWorldGeneration Runtime Assemblyに置き、この共通Simulation Assemblyは表示や地形型へ依存しません。

using Unity.Entities;

namespace Farmware.Simulation.Animals
{
    /// <summary>
    /// 地形Seedとは独立して、新規ワールドごとの野生動物配置を決めるSingleton Componentです。
    /// 新規ワールドではAnimalPopulationSeedInitializationSystemが一度だけ作り、SAVE-01B以降は保存値を先に復元します。
    /// </summary>
    public struct AnimalPopulationSeed : IComponentData
    {
        /// <summary>未設定を表す予約値です。有効な新規ワールドでは使用しません。</summary>
        public const ulong InvalidValue = 0;

        /// <summary>地域ごとの抽選状態を初期化する64bit値です。地形生成のWorldSeedとは共有しません。</summary>
        public ulong Value;

        /// <summary>0ではない有効な個体群Seedかを返します。</summary>
        public readonly bool IsValid => Value != InvalidValue;
    }

    /// <summary>
    /// 64×64タイルの地域を一度初期化した事実と、次回抽選へ引き継ぐ乱数状態を保持します。
    /// 地形チャンクEntityとは分離し、表示用チャンクが破棄されても同一プレイ中に再抽選しないために使用します。
    /// </summary>
    public struct AnimalPopulationRegionState : IComponentData
    {
        /// <summary>地域が属する固定ワールドレイヤーです。同じX/Yの別層は独立した個体群として扱います。</summary>
        public WorldLayer Layer;

        /// <summary>地域を識別する論理チャンクの水平座標です。負座標を含みます。</summary>
        public int RegionX;

        /// <summary>地域を識別する論理チャンクの垂直座標です。負座標を含みます。</summary>
        public int RegionY;

        /// <summary>初期補充後の地域専用乱数状態です。将来のリスポーンが同じ抽選列を継続するために保存します。</summary>
        public ulong RandomState;

        /// <summary>この地域で次に生成する個体へ使う1始まりの安定した受付順です。初期補充とリスポーンで共有します。</summary>
        public ulong NextSpawnSequence;

        /// <summary>初期補充を確定した固定Tick番号です。診断と保存状態の比較に使用します。</summary>
        public ulong InitializedAtTick;
    }

    /// <summary>
    /// 完成した地形チャンクについて、対応する動物地域の初期化確認を済ませたことを示すタグです。
    /// このタグはストリーミング中の重複確認を避けるキャッシュであり、地域状態の正やセーブ対象ではありません。
    /// </summary>
    public struct AnimalPopulationRegionProcessed : IComponentData
    {
    }

    /// <summary>
    /// 野生動物Entityを、牧場所属個体や将来の別状態から区別するタグです。
    /// ANIMAL-01Bでは生成した全個体へ付け、捕獲や牧場処理は後続PRで状態を追加します。
    /// </summary>
    public struct WildAnimal : IComponentData
    {
    }

    /// <summary>
    /// 1個体が参照する動物種の実行時IDです。
    /// 文字列やDefinition GUIDを通常Tickで検索せず、AnimalSpeciesCatalogBlobを直接参照するために使用します。
    /// </summary>
    public struct AnimalSpecies : IComponentData
    {
        /// <summary>共通DefinitionCatalogで解決済みの1始まりの動物種IDです。</summary>
        public DefinitionRuntimeId RuntimeId;
    }

    /// <summary>
    /// 動物のSimulation上の整数タイル座標です。
    /// Transformや表示補間値をゲーム状態の正にせず、ECO-01の移動Systemが固定Tick境界で更新します。
    /// </summary>
    public struct AnimalTilePosition : IComponentData
    {
        /// <summary>個体が存在する固定ワールドレイヤーです。移動Systemだけが固定Tick境界で変更します。</summary>
        public WorldLayer Layer;

        /// <summary>絶対ワールド水平タイル座標です。単位はタイルです。</summary>
        public int TileX;

        /// <summary>絶対ワールド垂直タイル座標です。単位はタイルです。</summary>
        public int TileY;
    }

    /// <summary>
    /// 同じ初期群れに属する個体を安定したIDで関連付けます。
    /// 群れの先頭個体へ共通アロケーターが発行した永続IDを全成員で共有し、Entity参照の寿命へ依存させません。
    /// </summary>
    public struct AnimalHerdMembership : IComponentData
    {
        /// <summary>群れの先頭個体が持つ1以上のPersistentObjectId値です。</summary>
        public ulong HerdId;
    }

    /// <summary>
    /// 同じ動物種内の外見差分番号を保持します。
    /// ECO-01のPresentationはこの値からSpriteを選び、再表示のたびに見た目を再抽選しません。
    /// </summary>
    public struct AnimalAppearanceVariant : IComponentData
    {
        /// <summary>0始まりで、動物種定義のAppearanceVariantCount未満に固定する番号です。</summary>
        public byte Value;
    }
}
