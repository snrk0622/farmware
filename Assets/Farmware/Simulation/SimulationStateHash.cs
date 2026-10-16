// このファイルは、Farmwareの論理Simulation状態を比較するための128bit状態ハッシュ基盤を定義します。
// 各機能は生のECSメモリではなく、安定IDで正規化した値をこのBuilderへ明示的に書き込み、Job実行順やEntity格納順を結果から除外します。

using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 状態ハッシュへ書き込むデータ形式のバージョンを定義します。
    /// ハッシュ対象、フィールド順、正規化規則を変更した場合は、古い値を誤って比較しないように値を増やします。
    /// </summary>
    public static class SimulationStateHashSchema
    {
        /// <summary>SIM-01Fで導入する最初の状態ハッシュ形式です。</summary>
        public const uint CurrentVersion = 1;

        /// <summary>
        /// 機能固有セクションへ割り当てられる最初のIDです。
        /// 1から1023は共通Simulation状態用に予約し、後続機能はこの値以上の安定したIDを使用します。
        /// </summary>
        public const uint FirstFeatureSectionId = 1024;
    }

    /// <summary>
    /// 固定Tick境界にある論理Simulation状態を表す128bitの比較値です。
    /// 元の状態を復元する用途や改ざん検出には使わず、同じ入力から同じ結果になったかをテストと診断で比較します。
    /// </summary>
    public readonly struct SimulationStateHash : IEquatable<SimulationStateHash>
    {
        /// <summary>この値を作った状態ハッシュ形式です。異なるバージョン同士は比較対象にしません。</summary>
        public readonly uint SchemaVersion;

        /// <summary>128bit値の先頭32bitです。</summary>
        public readonly uint Part0;

        /// <summary>128bit値の2番目の32bitです。</summary>
        public readonly uint Part1;

        /// <summary>128bit値の3番目の32bitです。</summary>
        public readonly uint Part2;

        /// <summary>128bit値の末尾32bitです。</summary>
        public readonly uint Part3;

        /// <summary>
        /// スキーマバージョンとxxHash3-128の4要素から比較可能な状態ハッシュを作成します。
        /// </summary>
        /// <param name="schemaVersion">値の並べ方を識別する1以上のバージョンです。</param>
        /// <param name="part0">128bit値の先頭32bitです。</param>
        /// <param name="part1">128bit値の2番目の32bitです。</param>
        /// <param name="part2">128bit値の3番目の32bitです。</param>
        /// <param name="part3">128bit値の末尾32bitです。</param>
        public SimulationStateHash(
            uint schemaVersion,
            uint part0,
            uint part1,
            uint part2,
            uint part3)
        {
            SchemaVersion = schemaVersion;
            Part0 = part0;
            Part1 = part1;
            Part2 = part2;
            Part3 = part3;
        }

        /// <summary>
        /// スキーマバージョンと128bit値がすべて一致するかを返します。
        /// </summary>
        /// <param name="other">比較するもう一方の状態ハッシュです。</param>
        /// <returns>同じ形式で同じ論理状態を表す値ならtrueです。</returns>
        public bool Equals(SimulationStateHash other)
        {
            return SchemaVersion == other.SchemaVersion
                && Part0 == other.Part0
                && Part1 == other.Part1
                && Part2 == other.Part2
                && Part3 == other.Part3;
        }

        /// <summary>
        /// objectが状態ハッシュで、全要素が一致するかを返します。
        /// </summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じ状態ハッシュならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is SimulationStateHash other && Equals(other);
        }

        /// <summary>
        /// Dictionaryなどの実行時検索へ使う32bit値を返します。
        /// 決定論比較ではこの値ではなく、128bit全体とSchemaVersionを比較します。
        /// </summary>
        /// <returns>全フィールドを組み合わせた32bit値です。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)SchemaVersion;
                hashCode = (hashCode * 397) ^ (int)Part0;
                hashCode = (hashCode * 397) ^ (int)Part1;
                hashCode = (hashCode * 397) ^ (int)Part2;
                return (hashCode * 397) ^ (int)Part3;
            }
        }

        /// <summary>
        /// ログとテスト失敗メッセージで比較しやすい、32桁の小文字16進数を返します。
        /// </summary>
        /// <returns>128bit値を先頭から並べた32文字です。</returns>
        public override string ToString()
        {
            return Part0.ToString("x8", CultureInfo.InvariantCulture)
                + Part1.ToString("x8", CultureInfo.InvariantCulture)
                + Part2.ToString("x8", CultureInfo.InvariantCulture)
                + Part3.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 整数値を固定のリトルエンディアンでxxHash3-128へ渡す状態ハッシュBuilderです。
    /// 構造体のpadding、CPUのバイト順、Entityのメモリアドレスへ依存しないよう、書き込み可能な型を明示的に限定します。
    /// </summary>
    public struct SimulationStateHashBuilder
    {
        /// <summary>別用途のxxHash3値と状態ハッシュを区別する固定ヘッダーです。</summary>
        private const uint FormatMagic = 0x46575348;

        /// <summary>Unity Collectionsが提供する128bit xxHash3の逐次入力状態です。</summary>
        private xxHash3.StreamingState streamingState;

        /// <summary>完成値へ記録する状態ハッシュ形式です。</summary>
        private uint schemaVersion;

        /// <summary>セクションIDが常に昇順で一度だけ現れることを検証する直前のIDです。</summary>
        private uint lastSectionId;

        /// <summary>0なら入力受付中、1ならComplete済みで再利用できない状態です。</summary>
        private byte completedValue;

        /// <summary>
        /// 指定スキーマの新しい128bit状態ハッシュ計算を開始します。
        /// </summary>
        /// <param name="schemaVersion">データ形式を識別する1以上のバージョンです。</param>
        public SimulationStateHashBuilder(uint schemaVersion)
        {
            if (schemaVersion == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schemaVersion),
                    "状態ハッシュのスキーマバージョンには1以上を指定してください。");
            }

            streamingState = new xxHash3.StreamingState(isHash64: false);
            this.schemaVersion = schemaVersion;
            lastSectionId = 0;
            completedValue = 0;

            // 固定ヘッダーとスキーマを先頭へ含め、別用途のハッシュや異なる形式を同じ値として扱いません。
            WriteUInt32(FormatMagic);
            WriteUInt32(schemaVersion);
        }

        /// <summary>
        /// 共通状態または機能固有状態の入力区間を開始します。
        /// セクションIDは0を使用せず、呼び出し全体で昇順かつ重複なしにする必要があります。
        /// </summary>
        /// <param name="sectionId">データ種別へ固定して割り当てる1以上の安定IDです。</param>
        public void BeginSection(uint sectionId)
        {
            EnsureWritable();
            if (sectionId == 0 || sectionId <= lastSectionId)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sectionId),
                    "状態ハッシュのセクションIDは1以上かつ昇順で、重複させないでください。");
            }

            lastSectionId = sectionId;
            WriteUInt32(sectionId);
        }

        /// <summary>
        /// 1byteの符号なし整数をそのまま状態ハッシュへ追加します。
        /// </summary>
        /// <param name="value">追加する0から255の値です。</param>
        public unsafe void WriteByte(byte value)
        {
            EnsureWritable();
            byte* bytes = stackalloc byte[1];
            bytes[0] = value;
            streamingState.Update(bytes, 1);
        }

        /// <summary>
        /// 16bit符号なし整数を環境に依存しないリトルエンディアンで追加します。
        /// </summary>
        /// <param name="value">追加する16bit値です。</param>
        public unsafe void WriteUInt16(ushort value)
        {
            EnsureWritable();
            byte* bytes = stackalloc byte[2];
            bytes[0] = (byte)value;
            bytes[1] = (byte)(value >> 8);
            streamingState.Update(bytes, 2);
        }

        /// <summary>
        /// 32bit符号なし整数を環境に依存しないリトルエンディアンで追加します。
        /// </summary>
        /// <param name="value">追加する32bit値です。</param>
        public unsafe void WriteUInt32(uint value)
        {
            EnsureWritable();
            byte* bytes = stackalloc byte[4];
            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                // CPUのエンディアンに依存せず、下位byteから順番に固定して書き込みます。
                bytes[byteIndex] = (byte)(value >> (byteIndex * 8));
            }

            streamingState.Update(bytes, 4);
        }

        /// <summary>
        /// 32bit符号付き整数をbit表現を保ったままリトルエンディアンで追加します。
        /// </summary>
        /// <param name="value">負数を含む32bit値です。</param>
        public void WriteInt32(int value)
        {
            WriteUInt32(unchecked((uint)value));
        }

        /// <summary>
        /// 64bit符号なし整数を環境に依存しないリトルエンディアンで追加します。
        /// </summary>
        /// <param name="value">追加する64bit値です。</param>
        public unsafe void WriteUInt64(ulong value)
        {
            EnsureWritable();
            byte* bytes = stackalloc byte[8];
            for (int byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                // Entity IDやポインターではなく、呼び出し側が選んだ論理整数だけを固定順で追加します。
                bytes[byteIndex] = (byte)(value >> (byteIndex * 8));
            }

            streamingState.Update(bytes, 8);
        }

        /// <summary>
        /// すべての入力から128bit値を確定し、このBuilderを再利用不可にします。
        /// </summary>
        /// <returns>作成時のSchemaVersionとxxHash3-128結果を持つ状態ハッシュです。</returns>
        public SimulationStateHash Complete()
        {
            EnsureWritable();
            completedValue = 1;
            var digest = streamingState.DigestHash128();
            return new SimulationStateHash(
                schemaVersion,
                digest.x,
                digest.y,
                digest.z,
                digest.w);
        }

        /// <summary>
        /// Complete後の書き込みや二重Completeを拒否し、途中までの値を誤って正式結果として使わせません。
        /// </summary>
        private readonly void EnsureWritable()
        {
            if (completedValue != 0)
            {
                throw new InvalidOperationException(
                    "完成済みの状態ハッシュBuilderへ値を追加したり、再度Completeしたりできません。");
            }
        }
    }

    /// <summary>
    /// 共通状態ハッシュを作れない理由を表します。
    /// 失敗時に不完全な値を比較へ使わず、テストから原因の種類を確認するために使用します。
    /// </summary>
    public enum SimulationStateHashErrorCode : byte
    {
        /// <summary>エラーはありません。</summary>
        None = 0,

        /// <summary>固定Tick、永続ID、Command Queueの必須Singletonが存在しません。</summary>
        RequiredSingletonMissing = 1,

        /// <summary>1件でなければならない必須または任意Singletonが複数存在します。</summary>
        SingletonDuplicated = 2,

        /// <summary>未処理Commandが残り、固定Tick完了後の安定した状態ではありません。</summary>
        PendingCommandExists = 3,

        /// <summary>一時停止または永続IDアロケーターの値が共通契約を満たしません。</summary>
        CoreStateIsInvalid = 4,

        /// <summary>定義カタログのBlob、並び、Runtime ID、参照範囲が不正です。</summary>
        DefinitionCatalogIsInvalid = 5
    }

    /// <summary>
    /// 状態ハッシュ計算の失敗理由を呼び出し側へ返す値です。
    /// 現在は共通エラー分類だけを保持し、機能固有の詳細は各セクション側の検証結果で追加します。
    /// </summary>
    public readonly struct SimulationStateHashError
    {
        /// <summary>失敗していなければNone、失敗時は最初に見つかった理由です。</summary>
        public readonly SimulationStateHashErrorCode Code;

        /// <summary>
        /// 指定した理由を持つ結果を作成します。
        /// </summary>
        /// <param name="code">成功ならNone、失敗なら具体的な分類です。</param>
        public SimulationStateHashError(SimulationStateHashErrorCode code)
        {
            Code = code;
        }

        /// <summary>計算を中止するエラーがなかったかを返します。</summary>
        public bool Succeeded => Code == SimulationStateHashErrorCode.None;
    }

    /// <summary>
    /// 固定Tick、永続ID、定義カタログというFarmware共通の論理状態をハッシュへ追加します。
    /// 後続機能はこの処理の後に1024以上のセクションIDで設備、物流、動物などを追加します。
    /// </summary>
    public static class SimulationCoreStateHasher
    {
        /// <summary>固定Tick設定、現在Tick、一時停止状態を表す予約セクションです。</summary>
        private const uint SimulationTimeSectionId = 1;

        /// <summary>次回発行IDを表す予約セクションです。</summary>
        private const uint PersistentIdAllocatorSectionId = 2;

        /// <summary>静的定義と参照関係を表す予約セクションです。</summary>
        private const uint DefinitionCatalogSectionId = 3;

        /// <summary>
        /// 共通Simulation状態だけから完成した状態ハッシュを計算します。
        /// Command処理中やSingleton不整合ではfalseを返し、不完全な比較値を公開しません。
        /// </summary>
        /// <param name="entityManager">固定Tick完了後のWorldを所有するEntityManagerです。</param>
        /// <param name="hash">成功時に共通状態の128bit値を受け取ります。</param>
        /// <param name="error">失敗時に最初に検出した不整合を受け取ります。</param>
        /// <returns>共通状態を完全に読み取り、ハッシュを確定できた場合はtrueです。</returns>
        public static bool TryCompute(
            EntityManager entityManager,
            out SimulationStateHash hash,
            out SimulationStateHashError error)
        {
            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                SimulationStateHashSchema.CurrentVersion);
            if (!TryAppendTo(entityManager, ref builder, out error))
            {
                hash = default;
                return false;
            }

            hash = builder.Complete();
            return true;
        }

        /// <summary>
        /// 既存Builderへ共通Simulationの3セクションを追加します。
        /// 呼び出し側は成功後に機能固有セクションを追加し、最後にCompleteを1回だけ呼びます。
        /// </summary>
        /// <param name="entityManager">固定Tick完了後のWorldを所有するEntityManagerです。</param>
        /// <param name="builder">共通セクションより前へ値を書いていない状態のBuilderです。</param>
        /// <param name="error">失敗時に最初に検出した不整合を受け取ります。</param>
        /// <returns>必要な共通状態をすべて追加できた場合はtrueです。</returns>
        public static bool TryAppendTo(
            EntityManager entityManager,
            ref SimulationStateHashBuilder builder,
            out SimulationStateHashError error)
        {
            error = new SimulationStateHashError(SimulationStateHashErrorCode.None);
            if (!TryGetRequiredSingleton(
                    entityManager,
                    out FixedTickConfiguration configuration,
                    out error)
                || !TryGetRequiredSingleton(
                    entityManager,
                    out SimulationTickState tickState,
                    out error)
                || !TryGetRequiredSingleton(
                    entityManager,
                    out SimulationPauseState pauseState,
                    out error)
                || !TryGetRequiredSingleton(
                    entityManager,
                    out PersistentIdAllocatorState allocatorState,
                    out error))
            {
                return false;
            }

            if (configuration.TicksPerSecond == 0
                || configuration.MaximumTicksPerFrame == 0
                || pauseState.Value > 1
                || !IsValidAllocatorState(allocatorState))
            {
                error = new SimulationStateHashError(
                    SimulationStateHashErrorCode.CoreStateIsInvalid);
                return false;
            }

            if (!ValidateCommandBoundary(entityManager, out error))
            {
                return false;
            }

            if (!TryGetOptionalDefinitionCatalog(
                    entityManager,
                    out bool hasDefinitionCatalog,
                    out DefinitionCatalog definitionCatalog,
                    out error))
            {
                return false;
            }

            builder.BeginSection(SimulationTimeSectionId);
            builder.WriteUInt32(configuration.TicksPerSecond);
            builder.WriteUInt32(configuration.MaximumTicksPerFrame);
            builder.WriteUInt64(tickState.CurrentTick);
            builder.WriteByte(pauseState.Value);

            builder.BeginSection(PersistentIdAllocatorSectionId);
            builder.WriteUInt64(allocatorState.NextId);
            builder.WriteByte(allocatorState.ExhaustedValue);

            builder.BeginSection(DefinitionCatalogSectionId);
            builder.WriteByte(hasDefinitionCatalog ? (byte)1 : (byte)0);
            if (!hasDefinitionCatalog)
            {
                return true;
            }

            return TryAppendDefinitionCatalog(definitionCatalog, ref builder, out error);
        }

        /// <summary>
        /// 指定ComponentがWorldにちょうど1件ある場合だけ値を取得します。
        /// 欠落と重複を分け、任意のEntityを正として選ぶ非決定的な処理を避けます。
        /// </summary>
        /// <typeparam name="T">取得するunmanaged Singleton Component型です。</typeparam>
        /// <param name="entityManager">検索対象WorldのEntityManagerです。</param>
        /// <param name="value">成功時に唯一のComponent値を受け取ります。</param>
        /// <param name="error">欠落または重複時の理由です。</param>
        /// <returns>対象Componentが1件だけ存在する場合はtrueです。</returns>
        private static bool TryGetRequiredSingleton<T>(
            EntityManager entityManager,
            out T value,
            out SimulationStateHashError error)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            try
            {
                int count = query.CalculateEntityCount();
                if (count != 1)
                {
                    value = default;
                    error = new SimulationStateHashError(
                        count == 0
                            ? SimulationStateHashErrorCode.RequiredSingletonMissing
                            : SimulationStateHashErrorCode.SingletonDuplicated);
                    return false;
                }

                value = query.GetSingleton<T>();
                error = new SimulationStateHashError(SimulationStateHashErrorCode.None);
                return true;
            }
            finally
            {
                query.Dispose();
            }
        }

        /// <summary>
        /// Command Queueが1件だけ存在し、未処理要素がない固定Tick境界であることを確認します。
        /// 完了結果EntityはPresentation向けの一時通知なので、Queueが空なら状態ハッシュへ含めません。
        /// </summary>
        /// <param name="entityManager">Command Queueを所有するWorldのEntityManagerです。</param>
        /// <param name="error">欠落、重複、未処理Commandのいずれかです。</param>
        /// <returns>安定した固定Tick完了状態ならtrueです。</returns>
        private static bool ValidateCommandBoundary(
            EntityManager entityManager,
            out SimulationStateHashError error)
        {
            EntityQuery queueQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationCommandQueueState>(),
                ComponentType.ReadOnly<SimulationCommandQueueElement>());
            try
            {
                int queueCount = queueQuery.CalculateEntityCount();
                if (queueCount != 1)
                {
                    error = new SimulationStateHashError(
                        queueCount == 0
                            ? SimulationStateHashErrorCode.RequiredSingletonMissing
                            : SimulationStateHashErrorCode.SingletonDuplicated);
                    return false;
                }

                Entity queueEntity = queueQuery.GetSingletonEntity();
                DynamicBuffer<SimulationCommandQueueElement> queue =
                    entityManager.GetBuffer<SimulationCommandQueueElement>(queueEntity, true);
                if (queue.Length != 0)
                {
                    error = new SimulationStateHashError(
                        SimulationStateHashErrorCode.PendingCommandExists);
                    return false;
                }

                error = new SimulationStateHashError(SimulationStateHashErrorCode.None);
                return true;
            }
            finally
            {
                queueQuery.Dispose();
            }
        }

        /// <summary>
        /// DefinitionCatalogは後続定義がまだないWorldでは0件を許可し、存在する場合は1件だけ取得します。
        /// 0件と空カタログを別状態としてハッシュへ記録するため、存在有無も呼び出し側へ返します。
        /// </summary>
        /// <param name="entityManager">定義カタログ候補を所有するWorldです。</param>
        /// <param name="hasCatalog">1件存在した場合はtrueです。</param>
        /// <param name="catalog">存在した場合のBlob Asset参照です。</param>
        /// <param name="error">複数存在した場合の失敗理由です。</param>
        /// <returns>0件または1件で一意に解釈できる場合はtrueです。</returns>
        private static bool TryGetOptionalDefinitionCatalog(
            EntityManager entityManager,
            out bool hasCatalog,
            out DefinitionCatalog catalog,
            out SimulationStateHashError error)
        {
            EntityQuery catalogQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DefinitionCatalog>());
            try
            {
                int count = catalogQuery.CalculateEntityCount();
                if (count > 1)
                {
                    hasCatalog = false;
                    catalog = default;
                    error = new SimulationStateHashError(
                        SimulationStateHashErrorCode.SingletonDuplicated);
                    return false;
                }

                hasCatalog = count == 1;
                catalog = hasCatalog
                    ? catalogQuery.GetSingleton<DefinitionCatalog>()
                    : default;
                error = new SimulationStateHashError(SimulationStateHashErrorCode.None);
                return true;
            }
            finally
            {
                catalogQuery.Dispose();
            }
        }

        /// <summary>
        /// GUID順に構築済みの定義と、各定義内で意味を持つ参照順を検証して追加します。
        /// Blob Assetのメモリアドレスは実行ごとに変わるため、論理値だけを読み取ります。
        /// </summary>
        /// <param name="catalog">BakerまたはBuilderが作成した定義カタログです。</param>
        /// <param name="builder">DefinitionCatalogセクションへ値を追加するBuilderです。</param>
        /// <param name="error">Blob内容が不正な場合の失敗理由です。</param>
        /// <returns>全定義と参照を検証して追加できた場合はtrueです。</returns>
        private static bool TryAppendDefinitionCatalog(
            DefinitionCatalog catalog,
            ref SimulationStateHashBuilder builder,
            out SimulationStateHashError error)
        {
            if (!catalog.Value.IsCreated)
            {
                error = new SimulationStateHashError(
                    SimulationStateHashErrorCode.DefinitionCatalogIsInvalid);
                return false;
            }

            ref DefinitionCatalogBlob root = ref catalog.Value.Value;
            builder.WriteInt32(root.Definitions.Length);
            builder.WriteInt32(root.References.Length);

            int expectedReferenceStart = 0;
            DefinitionGuid previousGuid = DefinitionGuid.Invalid;
            for (int definitionIndex = 0;
                 definitionIndex < root.Definitions.Length;
                 definitionIndex++)
            {
                DefinitionBlobRecord definition = root.Definitions[definitionIndex];
                bool invalidOrder = definitionIndex > 0
                    && previousGuid.CompareTo(definition.Guid) >= 0;
                bool invalidReferenceRange = definition.ReferenceStartIndex
                        != expectedReferenceStart
                    || definition.ReferenceCount < 0
                    || definition.ReferenceCount
                        > root.References.Length - expectedReferenceStart;
                if (!definition.Guid.IsValid
                    || definition.RuntimeId.Value != definitionIndex + 1
                    || definition.Kind <= DefinitionKind.None
                    || definition.Kind > DefinitionKind.AnimalSpecies
                    || invalidOrder
                    || invalidReferenceRange)
                {
                    error = new SimulationStateHashError(
                        SimulationStateHashErrorCode.DefinitionCatalogIsInvalid);
                    return false;
                }

                builder.WriteUInt64(definition.Guid.High);
                builder.WriteUInt64(definition.Guid.Low);
                builder.WriteInt32(definition.RuntimeId.Value);
                builder.WriteByte((byte)definition.Kind);
                builder.WriteInt32(definition.ReferenceCount);

                int referenceEnd = expectedReferenceStart + definition.ReferenceCount;
                for (int referenceIndex = expectedReferenceStart;
                     referenceIndex < referenceEnd;
                     referenceIndex++)
                {
                    int targetRuntimeId = root.References[referenceIndex].TargetRuntimeId.Value;
                    if (targetRuntimeId <= DefinitionRuntimeId.InvalidValue
                        || targetRuntimeId > root.Definitions.Length)
                    {
                        error = new SimulationStateHashError(
                            SimulationStateHashErrorCode.DefinitionCatalogIsInvalid);
                        return false;
                    }

                    // 同じ定義内の参照順はレシピ材料などの意味を持てるため、並べ替えずBlobの正規順を維持します。
                    builder.WriteInt32(targetRuntimeId);
                }

                expectedReferenceStart = referenceEnd;
                previousGuid = definition.Guid;
            }

            if (expectedReferenceStart != root.References.Length)
            {
                error = new SimulationStateHashError(
                    SimulationStateHashErrorCode.DefinitionCatalogIsInvalid);
                return false;
            }

            error = new SimulationStateHashError(SimulationStateHashErrorCode.None);
            return true;
        }

        /// <summary>
        /// 永続IDの次回値と上限状態が、PersistentIdAllocatorの発行規則を満たすか確認します。
        /// </summary>
        /// <param name="state">固定Tick境界から読み取ったアロケーター状態です。</param>
        /// <returns>次回発行または上限到達状態として解釈できる場合はtrueです。</returns>
        private static bool IsValidAllocatorState(PersistentIdAllocatorState state)
        {
            if (state.NextId == PersistentObjectId.InvalidValue
                || state.ExhaustedValue > 1)
            {
                return false;
            }

            return !state.IsExhausted || state.NextId == ulong.MaxValue;
        }
    }
}
