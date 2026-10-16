// このファイルは、セーブと相互参照に使うワールド内永続ID、共通アロケーター状態、保存用Snapshotを定義します。
// 動物、設備、施設などの各機能は同じ型を使い、Entityの実行時Indexや生成元のシードを永続参照へ流用しません。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// セーブ対象となる実行時オブジェクトを、1つのワールド内で一意に識別するComponentです。
    /// 動物、設備、施設などのEntityへ付与し、範囲移動やチャンクの再ロード後も同じ値を維持します。
    /// </summary>
    public struct PersistentObjectId : IComponentData
    {
        /// <summary>未割り当てを表す予約値です。有効なオブジェクトへは付与しません。</summary>
        public const ulong InvalidValue = 0;

        /// <summary>
        /// ワールド共通アロケーターが発行した値です。0は無効、1以上は割り当て済みIDとしてEntityの寿命を越えて保存します。
        /// </summary>
        public ulong Value;

        /// <summary>このIDがアロケーターから発行可能な1以上の値かを返します。</summary>
        public readonly bool IsValid => Value != InvalidValue;

        /// <summary>
        /// まだ永続IDを割り当てていない状態を作成します。
        /// </summary>
        /// <returns>値が0である無効な永続IDです。</returns>
        public static PersistentObjectId CreateInvalid()
        {
            return new PersistentObjectId
            {
                Value = InvalidValue
            };
        }
    }

    /// <summary>
    /// ワールドで次に発行する永続IDと、発行可能範囲を使い切ったかを保持するSingleton Componentです。
    /// PersistentIdAllocatorだけが更新し、セーブ処理は固定Tick境界でSnapshotへ変換して保存します。
    /// </summary>
    public struct PersistentIdAllocatorState : IComponentData
    {
        /// <summary>
        /// 次回発行するIDです。単位はID番号で、通常は発行のたびに増え、上限到達後はulong.MaxValueを保持します。
        /// </summary>
        public ulong NextId;

        /// <summary>
        /// 0なら発行可能、1ならulongの全範囲を使い切った状態です。Snapshotとの変換時に0または1だけを受け付けます。
        /// </summary>
        public byte ExhaustedValue;

        /// <summary>有効なIDをこれ以上発行できない状態かを返します。</summary>
        public readonly bool IsExhausted => ExhaustedValue != 0;

        /// <summary>
        /// 新規ワールド用に、最初の有効IDである1から発行する初期状態を作成します。
        /// </summary>
        /// <returns>NextIdが1で、まだ発行可能なアロケーター状態です。</returns>
        public static PersistentIdAllocatorState CreateInitial()
        {
            return new PersistentIdAllocatorState
            {
                NextId = 1,
                ExhaustedValue = 0
            };
        }
    }

    /// <summary>
    /// PersistentIdAllocatorStateをセーブ層へ受け渡すための、UnityのEntity参照を含まない保存用データです。
    /// セーブ形式の実装前でも、次回発行位置を失わず復元できる境界をテストするために使用します。
    /// </summary>
    public struct PersistentIdAllocatorSnapshot
    {
        /// <summary>
        /// ロード後に次回発行するIDです。発行済みIDを再利用しないため、保存時の値をそのまま維持します。
        /// </summary>
        public ulong NextId;

        /// <summary>
        /// 0なら発行可能、1なら上限到達済みです。上限到達済みの場合、NextIdはulong.MaxValueでなければなりません。
        /// </summary>
        public byte ExhaustedValue;
    }
}
