// このファイルは、静的なゲーム定義を保存用GUIDから実行時IDへ変換する共通カタログを定義します。
// Authoring側のBakerがBlob Assetを構築し、Simulation側は連続IDだけを高頻度処理へ保持します。

using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// アイテム、設備、施設、レシピ、動物種の分類を表す変更禁止の数値です。
    /// セーブ互換性のため既存値を再利用せず、新しい分類は末尾へ追加します。
    /// </summary>
    public enum DefinitionKind : byte
    {
        /// <summary>未設定または破損した分類です。有効な定義には使用しません。</summary>
        None = 0,

        /// <summary>在庫や物流で扱うアイテム定義です。</summary>
        Item = 1,

        /// <summary>プレイヤーが配置する単体設備の定義です。</summary>
        Equipment = 2,

        /// <summary>閉鎖領域から成立する施設の定義です。</summary>
        Facility = 3,

        /// <summary>材料、処理時間、生成物を結び付けるレシピ定義です。</summary>
        Recipe = 4,

        /// <summary>生成条件や移動特性を持つ動物種の定義です。</summary>
        AnimalSpecies = 5
    }

    /// <summary>
    /// 名称や配列位置から独立して静的定義を識別する128bit GUIDです。
    /// セーブにはこの値を記録し、実行時の高頻度処理ではDefinitionRuntimeIdへ解決して使用します。
    /// </summary>
    public readonly struct DefinitionGuid :
        IEquatable<DefinitionGuid>,
        IComparable<DefinitionGuid>
    {
        /// <summary>128bit値の上位64bitです。定義作成後は変更しません。</summary>
        public readonly ulong High;

        /// <summary>128bit値の下位64bitです。定義作成後は変更しません。</summary>
        public readonly ulong Low;

        /// <summary>GUIDが未設定であることを表す、全bitが0の予約値です。</summary>
        public static DefinitionGuid Invalid => default;

        /// <summary>上位64bitまたは下位64bitに値があり、有効な定義GUIDとして使用できるかを返します。</summary>
        public bool IsValid => High != 0 || Low != 0;

        /// <summary>
        /// 128bitの上位値と下位値からGUIDを作成します。
        /// </summary>
        /// <param name="high">文字列表現の先頭16桁に対応する上位64bitです。</param>
        /// <param name="low">文字列表現の末尾16桁に対応する下位64bitです。</param>
        public DefinitionGuid(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        /// <summary>
        /// 32桁形式またはハイフン付きの一般的なGUID文字列を128bit値へ変換します。
        /// Inspectorとセーブ境界だけで使用し、毎Tickの処理では呼び出しません。
        /// </summary>
        /// <param name="text">解析するGUID文字列です。</param>
        /// <param name="guid">成功時に正規化した128bit GUIDを受け取ります。</param>
        /// <returns>文字列が有効かつ全0以外のGUIDならtrueです。</returns>
        public static bool TryParse(string text, out DefinitionGuid guid)
        {
            guid = Invalid;
            if (!System.Guid.TryParse(text, out System.Guid parsedGuid))
            {
                return false;
            }

            string canonicalText = parsedGuid.ToString("N");
            bool parsedHigh = ulong.TryParse(
                canonicalText.Substring(0, 16),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out ulong high);
            bool parsedLow = ulong.TryParse(
                canonicalText.Substring(16, 16),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out ulong low);
            if (!parsedHigh || !parsedLow)
            {
                return false;
            }

            DefinitionGuid parsedDefinitionGuid = new DefinitionGuid(high, low);
            if (!parsedDefinitionGuid.IsValid)
            {
                return false;
            }

            guid = parsedDefinitionGuid;
            return true;
        }

        /// <summary>
        /// GUIDを大文字小文字やハイフンに依存しない32桁の小文字16進数へ変換します。
        /// </summary>
        /// <returns>上位64bit、下位64bitの順に並べた32文字です。</returns>
        public override string ToString()
        {
            return High.ToString("x16", CultureInfo.InvariantCulture) +
                Low.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// GUIDを上位64bit、下位64bitの順に比較し、定義表の安定した並びを作ります。
        /// </summary>
        /// <param name="other">比較するもう一方のGUIDです。</param>
        /// <returns>このGUIDが前なら負、同じなら0、後なら正の値です。</returns>
        public int CompareTo(DefinitionGuid other)
        {
            int highComparison = High.CompareTo(other.High);
            return highComparison != 0 ? highComparison : Low.CompareTo(other.Low);
        }

        /// <summary>
        /// 上位64bitと下位64bitが両方一致するかを確認します。
        /// </summary>
        /// <param name="other">比較するもう一方のGUIDです。</param>
        /// <returns>128bit全体が一致する場合はtrueです。</returns>
        public bool Equals(DefinitionGuid other)
        {
            return High == other.High && Low == other.Low;
        }

        /// <summary>
        /// objectがDefinitionGuidで、128bit全体が一致するかを確認します。
        /// </summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じGUIDならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is DefinitionGuid other && Equals(other);
        }

        /// <summary>
        /// 上位値と下位値を組み合わせたハッシュ値を返します。
        /// Dictionaryなどの低頻度な定義構築処理で使用できます。
        /// </summary>
        /// <returns>128bit値から作った32bitハッシュです。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (High.GetHashCode() * 397) ^ Low.GetHashCode();
            }
        }
    }

    /// <summary>
    /// 1つのカタログ内で定義を参照するための連続整数IDです。
    /// GUID順に1から割り当て、0は未解決を表す予約値として扱います。
    /// </summary>
    public readonly struct DefinitionRuntimeId : IEquatable<DefinitionRuntimeId>
    {
        /// <summary>未解決を表す予約値です。有効な定義へは割り当てません。</summary>
        public const int InvalidValue = 0;

        /// <summary>カタログの寿命中だけ有効な1始まりの連続値です。セーブへ直接記録しません。</summary>
        public readonly int Value;

        /// <summary>1以上の実行時IDとして解決済みかを返します。</summary>
        public bool IsValid => Value > InvalidValue;

        /// <summary>
        /// カタログが割り当てた連続値から実行時IDを作成します。
        /// </summary>
        /// <param name="value">1以上の連続値です。</param>
        public DefinitionRuntimeId(int value)
        {
            Value = value;
        }

        /// <summary>
        /// 2つの実行時IDが同じ連続値かを確認します。
        /// </summary>
        /// <param name="other">比較する実行時IDです。</param>
        /// <returns>値が一致する場合はtrueです。</returns>
        public bool Equals(DefinitionRuntimeId other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// objectがDefinitionRuntimeIdで、連続値が一致するかを確認します。
        /// </summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じ実行時IDならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is DefinitionRuntimeId other && Equals(other);
        }

        /// <summary>実行時IDの整数値をハッシュとして返します。</summary>
        /// <returns>Valueと同じ32bit値です。</returns>
        public override int GetHashCode()
        {
            return Value;
        }
    }

    /// <summary>
    /// Blob Asset内に保存する1件の静的定義です。
    /// GUID順の位置とRuntimeIdを一致させ、参照範囲から解決済みの関連定義を取得できます。
    /// </summary>
    public struct DefinitionBlobRecord
    {
        /// <summary>セーブデータとAuthoringデータを結び付ける変更禁止の128bit GUIDです。</summary>
        public DefinitionGuid Guid;

        /// <summary>カタログ構築時にGUID順で割り当てた1始まりの連続IDです。</summary>
        public DefinitionRuntimeId RuntimeId;

        /// <summary>この定義がアイテム、設備などのどの分類に属するかを表します。</summary>
        public DefinitionKind Kind;

        /// <summary>DefinitionCatalogBlob.References内で、この定義の参照が始まる添字です。</summary>
        public int ReferenceStartIndex;

        /// <summary>この定義が保持する解決済み参照の件数です。</summary>
        public int ReferenceCount;
    }

    /// <summary>
    /// Authoring時のGUID参照を解決した、Blob Asset内の軽量な参照データです。
    /// 実行中はGUID文字列を探さず、この連続IDから対象定義へ到達します。
    /// </summary>
    public struct DefinitionBlobReference
    {
        /// <summary>参照先定義へ割り当てられた1始まりの実行時IDです。</summary>
        public DefinitionRuntimeId TargetRuntimeId;
    }

    /// <summary>
    /// 全静的定義と解決済み参照を不変メモリへまとめたBlob Assetのルートです。
    /// Bakerが1回構築し、SimulationのJobやSystemは読み取り専用で共有します。
    /// </summary>
    public struct DefinitionCatalogBlob
    {
        /// <summary>GUID昇順に並び、RuntimeId.Value - 1で直接参照できる定義配列です。</summary>
        public BlobArray<DefinitionBlobRecord> Definitions;

        /// <summary>各定義のReferenceStartIndexとReferenceCountから読む解決済み参照配列です。</summary>
        public BlobArray<DefinitionBlobReference> References;
    }

    /// <summary>
    /// Bakerが作成した定義カタログをECS Worldへ公開するSingleton Componentです。
    /// 実行中に書き換えず、World破棄時のBlob Asset解放はEntitiesの参照管理へ委ねます。
    /// </summary>
    public struct DefinitionCatalog : IComponentData
    {
        /// <summary>全定義と解決済み参照を保持する読み取り専用Blob Assetです。</summary>
        public BlobAssetReference<DefinitionCatalogBlob> Value;
    }

    /// <summary>
    /// GUIDから実行時IDへの低頻度な解決と、実行時IDから定義・参照への取得を行います。
    /// 通常Tickは解決済みDefinitionRuntimeIdをComponentへ保持し、毎回TryResolveを呼びません。
    /// </summary>
    public static class DefinitionCatalogLookup
    {
        /// <summary>
        /// GUID昇順のBlob配列を二分探索し、保存用GUIDを実行時IDへ解決します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した定義カタログです。</param>
        /// <param name="guid">ロードまたは初期化時に解決する定義GUIDです。</param>
        /// <param name="runtimeId">成功時に1始まりの実行時IDを受け取ります。</param>
        /// <returns>カタログが作成済みでGUIDが登録されていればtrueです。</returns>
        public static bool TryResolve(
            BlobAssetReference<DefinitionCatalogBlob> catalog,
            DefinitionGuid guid,
            out DefinitionRuntimeId runtimeId)
        {
            runtimeId = default;
            if (!catalog.IsCreated || !guid.IsValid)
            {
                return false;
            }

            ref DefinitionCatalogBlob root = ref catalog.Value;
            int lowerIndex = 0;
            int upperIndex = root.Definitions.Length - 1;

            // 定義数が増えても全件走査にならないよう、GUID昇順へ正規化済みの配列を二分探索します。
            while (lowerIndex <= upperIndex)
            {
                int middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
                DefinitionBlobRecord record = root.Definitions[middleIndex];
                int comparison = record.Guid.CompareTo(guid);
                if (comparison == 0)
                {
                    runtimeId = record.RuntimeId;
                    return true;
                }

                if (comparison < 0)
                {
                    lowerIndex = middleIndex + 1;
                }
                else
                {
                    upperIndex = middleIndex - 1;
                }
            }

            return false;
        }

        /// <summary>
        /// 1始まりの実行時IDから定義レコードを配列添字で直接取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した定義カタログです。</param>
        /// <param name="runtimeId">取得する定義の実行時IDです。</param>
        /// <param name="record">成功時にGUID、分類、参照範囲を受け取ります。</param>
        /// <returns>IDがカタログ範囲内ならtrueです。</returns>
        public static bool TryGetDefinition(
            BlobAssetReference<DefinitionCatalogBlob> catalog,
            DefinitionRuntimeId runtimeId,
            out DefinitionBlobRecord record)
        {
            record = default;
            if (!catalog.IsCreated || !runtimeId.IsValid)
            {
                return false;
            }

            ref DefinitionCatalogBlob root = ref catalog.Value;
            int definitionIndex = runtimeId.Value - 1;
            if (definitionIndex < 0 || definitionIndex >= root.Definitions.Length)
            {
                return false;
            }

            record = root.Definitions[definitionIndex];
            return record.RuntimeId.Equals(runtimeId);
        }

        /// <summary>
        /// 定義が持つ参照を0始まりの局所添字で取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した定義カタログです。</param>
        /// <param name="sourceRuntimeId">参照元定義の実行時IDです。</param>
        /// <param name="referenceIndex">参照元定義内での0始まりの参照番号です。</param>
        /// <param name="targetRuntimeId">成功時に解決済みの参照先IDを受け取ります。</param>
        /// <returns>定義と参照番号がどちらも有効ならtrueです。</returns>
        public static bool TryGetReference(
            BlobAssetReference<DefinitionCatalogBlob> catalog,
            DefinitionRuntimeId sourceRuntimeId,
            int referenceIndex,
            out DefinitionRuntimeId targetRuntimeId)
        {
            targetRuntimeId = default;
            if (!TryGetDefinition(catalog, sourceRuntimeId, out DefinitionBlobRecord source) ||
                referenceIndex < 0 ||
                referenceIndex >= source.ReferenceCount)
            {
                return false;
            }

            ref DefinitionCatalogBlob root = ref catalog.Value;
            targetRuntimeId = root.References[source.ReferenceStartIndex + referenceIndex]
                .TargetRuntimeId;
            return targetRuntimeId.IsValid;
        }
    }
}
