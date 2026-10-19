// このファイルは、ワールドとプレイヤーの安定ID、ローカル入力経路、操作主体の共通検証を定義します。
// InputはローカルContextから明示的な主体を取得し、Commandや停止操作は同じ検証を通してECS Simulationへ渡します。

using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// セーブスロット名や作成順から独立して1つのワールドを識別する128bit IDです。
    /// SAVE-01でワールドヘッダーへ保存し、別ワールドのCommandやプレイヤー状態を混ぜないために使用します。
    /// </summary>
    public readonly struct WorldId : IEquatable<WorldId>, IComparable<WorldId>
    {
        /// <summary>128bit値の上位64bitです。ワールド作成後は変更しません。</summary>
        public readonly ulong High;

        /// <summary>128bit値の下位64bitです。ワールド作成後は変更しません。</summary>
        public readonly ulong Low;

        /// <summary>未設定または破損を表す、全bitが0の予約値です。</summary>
        public static WorldId Invalid => default;

        /// <summary>
        /// SAVE-01の新規ワールド作成が接続されるまで、既存SampleSceneとテストが使用する固定IDです。
        /// 製品セーブ用IDとして新規発行せず、保存基盤接続時に作成済みワールドの移行方針を明示します。
        /// </summary>
        public static WorldId DevelopmentDefault =>
            new WorldId(0x46574f524c443031ul, 1ul);

        /// <summary>上位または下位に値があり、有効なワールドIDとして使用できるかを返します。</summary>
        public bool IsValid => High != 0 || Low != 0;

        /// <summary>
        /// 上位64bitと下位64bitからワールドIDを作成します。
        /// </summary>
        /// <param name="high">文字列表現の先頭16桁に対応する上位64bitです。</param>
        /// <param name="low">文字列表現の末尾16桁に対応する下位64bitです。</param>
        public WorldId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        /// <summary>
        /// 32桁形式またはハイフン付きGUID文字列を、保存に使う正規化済みワールドIDへ変換します。
        /// SAVE-01のファイル境界など低頻度な処理で使用し、毎Tickの処理では呼び出しません。
        /// </summary>
        /// <param name="text">解析するGUID文字列です。</param>
        /// <param name="worldId">成功時に正規化した128bit IDを受け取ります。</param>
        /// <returns>文字列がGUIDとして有効で、全0ではない場合はtrueです。</returns>
        public static bool TryParse(string text, out WorldId worldId)
        {
            worldId = Invalid;
            if (!Guid.TryParse(text, out Guid parsedGuid))
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

            WorldId parsedWorldId = new WorldId(high, low);
            if (!parsedWorldId.IsValid)
            {
                return false;
            }

            worldId = parsedWorldId;
            return true;
        }

        /// <summary>
        /// ワールドIDを大文字小文字やハイフンに依存しない32桁の小文字16進数へ変換します。
        /// </summary>
        /// <returns>上位64bit、下位64bitの順に並べた32文字です。</returns>
        public override string ToString()
        {
            return High.ToString("x16", CultureInfo.InvariantCulture)
                + Low.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 上位値、下位値の順に比較し、複数ワールドを安定順へ並べるための結果を返します。
        /// </summary>
        /// <param name="other">比較するもう一方のワールドIDです。</param>
        /// <returns>このIDが前なら負、同じなら0、後なら正の値です。</returns>
        public int CompareTo(WorldId other)
        {
            int highComparison = High.CompareTo(other.High);
            return highComparison != 0 ? highComparison : Low.CompareTo(other.Low);
        }

        /// <summary>128bit全体が一致するかを返します。</summary>
        /// <param name="other">比較するもう一方のワールドIDです。</param>
        /// <returns>上位と下位が両方一致する場合はtrueです。</returns>
        public bool Equals(WorldId other)
        {
            return High == other.High && Low == other.Low;
        }

        /// <summary>objectがWorldIdで、128bit全体が一致するかを返します。</summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じワールドIDならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is WorldId other && Equals(other);
        }

        /// <summary>Dictionary等の低頻度検索へ使用できる32bitハッシュ値を返します。</summary>
        /// <returns>上位と下位を組み合わせたハッシュ値です。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (High.GetHashCode() * 397) ^ Low.GetHashCode();
            }
        }
    }

    /// <summary>
    /// 1つのワールド内でプレイヤー別の状態とCommand操作主体を識別する64bit IDです。
    /// 初回はローカルプレイヤー1人だけへ1を割り当て、0は未設定として拒否します。
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        /// <summary>未設定を表す予約値です。有効なプレイヤーへは割り当てません。</summary>
        public const ulong InvalidValue = 0;

        /// <summary>現在のワールド内で一意な1以上の値です。PlayerId別の保存データが保持します。</summary>
        public readonly ulong Value;

        /// <summary>1以上のプレイヤーIDとして使用できるかを返します。</summary>
        public bool IsValid => Value != InvalidValue;

        /// <summary>
        /// ワールド内の数値からプレイヤーIDを作成します。
        /// </summary>
        /// <param name="value">0を無効、1以上を有効とするワールド内IDです。</param>
        public PlayerId(ulong value)
        {
            Value = value;
        }

        /// <summary>数値が一致するかを返します。</summary>
        /// <param name="other">比較するもう一方のプレイヤーIDです。</param>
        /// <returns>同じ値ならtrueです。</returns>
        public bool Equals(PlayerId other)
        {
            return Value == other.Value;
        }

        /// <summary>objectがPlayerIdで、数値が一致するかを返します。</summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じプレイヤーIDならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is PlayerId other && Equals(other);
        }

        /// <summary>プレイヤーIDの数値順を返します。</summary>
        /// <param name="other">比較するもう一方のプレイヤーIDです。</param>
        /// <returns>このIDが前なら負、同じなら0、後なら正の値です。</returns>
        public int CompareTo(PlayerId other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>PlayerIdの64bit値から作った32bitハッシュ値を返します。</summary>
        /// <returns>Dictionary等で使用するハッシュ値です。</returns>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <summary>ログや診断で確認できる10進文字列を返します。</summary>
        /// <returns>ワールド内のプレイヤー番号です。</returns>
        public override string ToString()
        {
            return Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 現在のECS Worldがどの製品ワールドを表すかを保持するSingleton Componentです。
    /// ワールド共有状態、Command検証、保存ヘッダー、状態ハッシュが参照し、実行途中では変更しません。
    /// </summary>
    public struct WorldSessionIdentity : IComponentData
    {
        /// <summary>現在のECS Worldへ読み込まれた、全0ではない安定ワールドIDです。</summary>
        public WorldId Id;
    }

    /// <summary>
    /// プレイヤー別の所持品、研究、健康、位置等をまとめるEntityの識別Componentです。
    /// 後続機能は同じEntityへPlayerId別Componentを追加し、ワールド共有Singletonへ混在させません。
    /// </summary>
    public struct PlayerIdentity : IComponentData
    {
        /// <summary>現在のワールド内で一意な1以上のプレイヤーIDです。</summary>
        public PlayerId Id;
    }

    /// <summary>
    /// この端末のInputとUIが操作するPlayerIdを保持する、保存対象外のセッションSingletonです。
    /// プレイヤー進行の正はPlayerIdentity Entity側に置き、この値はCommand主体を選ぶ経路だけに使用します。
    /// </summary>
    public struct LocalPlayerContext : IComponentData
    {
        /// <summary>InputがCommand作成時に明示する、現在のローカルプレイヤーIDです。</summary>
        public PlayerId PlayerId;
    }

    /// <summary>
    /// InputからSimulationへ渡す操作主体を、対象WorldIdとPlayerIdの組で表す値です。
    /// ECS World切替直後に残った古いUI入力も、WorldId不一致として状態変更前に拒否できます。
    /// </summary>
    public readonly struct WorldCommandActor
    {
        /// <summary>Commandを適用しようとした製品ワールドの安定IDです。</summary>
        public readonly WorldId WorldId;

        /// <summary>Commandを発行したプレイヤーのワールド内IDです。</summary>
        public readonly PlayerId PlayerId;

        /// <summary>WorldIdとPlayerIdが両方とも有効かを返します。</summary>
        public bool IsValid => WorldId.IsValid && PlayerId.IsValid;

        /// <summary>
        /// 対象ワールドと操作プレイヤーからCommand主体を作成します。
        /// </summary>
        /// <param name="worldId">Commandを適用するワールドIDです。</param>
        /// <param name="playerId">Commandを発行するプレイヤーIDです。</param>
        public WorldCommandActor(WorldId worldId, PlayerId playerId)
        {
            WorldId = worldId;
            PlayerId = playerId;
        }
    }

    /// <summary>
    /// Command、停止、将来の睡眠・関心範囲が共通主体検証に失敗した理由です。
    /// UIは値を直接ゲーム結果へ使わず、操作を受理できなかった診断と案内に利用します。
    /// </summary>
    public enum WorldSessionAccessFailure : byte
    {
        /// <summary>主体と必要なSingletonが有効で、操作を続けられます。</summary>
        None = 0,

        /// <summary>WorldIdまたはPlayerIdが予約済みの無効値です。</summary>
        InvalidActor = 1,

        /// <summary>現在Worldの識別Singletonが存在しません。</summary>
        WorldIdentityMissing = 2,

        /// <summary>現在Worldの識別Singletonが複数あり、対象を一意に決められません。</summary>
        WorldIdentityDuplicated = 3,

        /// <summary>現在Worldの識別Singletonが全0の無効なWorldIdを保持しています。</summary>
        WorldIdentityInvalid = 4,

        /// <summary>操作主体のWorldIdが現在読み込まれているWorldと一致しません。</summary>
        WorldMismatch = 5,

        /// <summary>登録済みPlayerIdentityが1件もありません。</summary>
        PlayerIdentityMissing = 6,

        /// <summary>登録済みPlayerIdentityに0の無効値があります。</summary>
        PlayerIdentityInvalid = 7,

        /// <summary>同じPlayerIdを持つEntityが複数あり、所有者を一意に決められません。</summary>
        PlayerIdentityDuplicated = 8,

        /// <summary>操作主体のPlayerIdが現在Worldへ登録されていません。</summary>
        PlayerNotFound = 9,

        /// <summary>ローカルInputが使用するContext Singletonが存在しません。</summary>
        LocalPlayerContextMissing = 10,

        /// <summary>ローカルInputが使用するContext Singletonが複数あります。</summary>
        LocalPlayerContextDuplicated = 11,

        /// <summary>固定Tickを停止するPause Singletonが存在しません。</summary>
        PauseStateMissing = 12,

        /// <summary>固定Tickを停止するPause Singletonが複数あります。</summary>
        PauseStateDuplicated = 13,

        /// <summary>Pause値が0または1以外で、現在状態を安全に変更できません。</summary>
        PauseStateInvalid = 14
    }

    /// <summary>
    /// 現在Worldに登録された主体だけがCommandや制御操作へ進めることを保証する共通検証APIです。
    /// 睡眠CommandとSimulation関心範囲を後続PRで追加するときも、この入口を再利用します。
    /// </summary>
    public static class WorldSessionAuthority
    {
        /// <summary>
        /// 保存対象外のLocalPlayerContextから明示的なWorldCommandActorを取得します。
        /// Context、World、Player登録のいずれかが曖昧なら主体を推測せずfalseを返します。
        /// </summary>
        /// <param name="entityManager">現在のWorld、Player、ローカルContextを所有するEntityManagerです。</param>
        /// <param name="actor">成功時にInputがCommandへ設定する操作主体です。</param>
        /// <param name="failure">失敗時に最初に検出した不整合です。</param>
        /// <returns>現在のローカル操作主体を一意に解決できた場合はtrueです。</returns>
        public static bool TryGetLocalActor(
            EntityManager entityManager,
            out WorldCommandActor actor,
            out WorldSessionAccessFailure failure)
        {
            actor = default;
            EntityQuery worldQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldSessionIdentity>());
            EntityQuery localPlayerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalPlayerContext>());
            try
            {
                int worldCount = worldQuery.CalculateEntityCount();
                if (worldCount != 1)
                {
                    failure = worldCount == 0
                        ? WorldSessionAccessFailure.WorldIdentityMissing
                        : WorldSessionAccessFailure.WorldIdentityDuplicated;
                    return false;
                }

                int localPlayerCount = localPlayerQuery.CalculateEntityCount();
                if (localPlayerCount != 1)
                {
                    failure = localPlayerCount == 0
                        ? WorldSessionAccessFailure.LocalPlayerContextMissing
                        : WorldSessionAccessFailure.LocalPlayerContextDuplicated;
                    return false;
                }

                WorldSessionIdentity worldIdentity =
                    worldQuery.GetSingleton<WorldSessionIdentity>();
                LocalPlayerContext localPlayerContext =
                    localPlayerQuery.GetSingleton<LocalPlayerContext>();
                WorldCommandActor candidate = new WorldCommandActor(
                    worldIdentity.Id,
                    localPlayerContext.PlayerId);
                if (!TryValidateActor(entityManager, candidate, out failure))
                {
                    return false;
                }

                actor = candidate;
                return true;
            }
            finally
            {
                // Queryはこの解決処理だけが所有するため、成功・失敗の両方で必ず解放します。
                worldQuery.Dispose();
                localPlayerQuery.Dispose();
            }
        }

        /// <summary>
        /// 操作主体が現在のWorldIdと、重複のない登録済みPlayerIdへ一致することを検証します。
        /// 検証は状態を変更せず、Command受付番号や一時停止状態も消費しません。
        /// </summary>
        /// <param name="entityManager">検証対象のWorldとPlayer Entityを所有するEntityManagerです。</param>
        /// <param name="actor">Inputまたはテストが明示した操作主体です。</param>
        /// <param name="failure">失敗時に最初に検出した不整合です。</param>
        /// <returns>現在Worldでこのプレイヤーが操作できる場合はtrueです。</returns>
        public static bool TryValidateActor(
            EntityManager entityManager,
            WorldCommandActor actor,
            out WorldSessionAccessFailure failure)
        {
            if (!actor.IsValid)
            {
                failure = WorldSessionAccessFailure.InvalidActor;
                return false;
            }

            EntityQuery worldQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldSessionIdentity>());
            EntityQuery playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerIdentity>());
            try
            {
                int worldCount = worldQuery.CalculateEntityCount();
                if (worldCount != 1)
                {
                    failure = worldCount == 0
                        ? WorldSessionAccessFailure.WorldIdentityMissing
                        : WorldSessionAccessFailure.WorldIdentityDuplicated;
                    return false;
                }

                WorldSessionIdentity worldIdentity =
                    worldQuery.GetSingleton<WorldSessionIdentity>();
                if (!worldIdentity.Id.IsValid)
                {
                    failure = WorldSessionAccessFailure.WorldIdentityInvalid;
                    return false;
                }

                if (!worldIdentity.Id.Equals(actor.WorldId))
                {
                    failure = WorldSessionAccessFailure.WorldMismatch;
                    return false;
                }

                int playerCount = playerQuery.CalculateEntityCount();
                if (playerCount == 0)
                {
                    failure = WorldSessionAccessFailure.PlayerIdentityMissing;
                    return false;
                }

                using NativeArray<PlayerIdentity> players =
                    playerQuery.ToComponentDataArray<PlayerIdentity>(Allocator.Temp);
                int actorMatchCount = 0;
                for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
                {
                    PlayerId currentId = players[playerIndex].Id;
                    if (!currentId.IsValid)
                    {
                        failure = WorldSessionAccessFailure.PlayerIdentityInvalid;
                        return false;
                    }

                    if (currentId.Equals(actor.PlayerId))
                    {
                        actorMatchCount++;
                    }

                    // 初回は1人ですが、将来Player Entityが増えても重複所有者を任意選択しないよう全組を確認します。
                    for (int previousIndex = 0;
                         previousIndex < playerIndex;
                         previousIndex++)
                    {
                        if (players[previousIndex].Id.Equals(currentId))
                        {
                            failure = WorldSessionAccessFailure.PlayerIdentityDuplicated;
                            return false;
                        }
                    }
                }

                if (actorMatchCount == 0)
                {
                    failure = WorldSessionAccessFailure.PlayerNotFound;
                    return false;
                }

                failure = WorldSessionAccessFailure.None;
                return true;
            }
            finally
            {
                // 一時Queryは呼び出しを越えて保持せず、検証終了時に必ず解放します。
                worldQuery.Dispose();
                playerQuery.Dispose();
            }
        }
    }

    /// <summary>
    /// 全画面メニュー等が固定Tickの停止と再開を要求する共通窓口です。
    /// 停止中はCommand Queue自体が進まないため、この制御だけはQueue外で主体を検証して即時反映します。
    /// </summary>
    public static class SimulationPauseControl
    {
        /// <summary>
        /// 登録済み操作主体からの要求だけを受け付け、SimulationPauseStateを停止または再開へ変更します。
        /// WorldId違い、PlayerId不明、Pause Singleton不整合では状態を変更しません。
        /// </summary>
        /// <param name="entityManager">World識別、Player登録、Pause状態を所有するEntityManagerです。</param>
        /// <param name="actor">停止または再開を要求する明示的な操作主体です。</param>
        /// <param name="isPaused">停止する場合はtrue、通常進行へ戻す場合はfalseです。</param>
        /// <param name="failure">失敗時に最初に検出した不整合です。</param>
        /// <returns>有効な主体からの要求をPause状態へ反映できた場合はtrueです。</returns>
        public static bool TrySetPaused(
            EntityManager entityManager,
            WorldCommandActor actor,
            bool isPaused,
            out WorldSessionAccessFailure failure)
        {
            if (!WorldSessionAuthority.TryValidateActor(
                    entityManager,
                    actor,
                    out failure))
            {
                return false;
            }

            EntityQuery pauseQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<SimulationPauseState>());
            try
            {
                int pauseCount = pauseQuery.CalculateEntityCount();
                if (pauseCount != 1)
                {
                    failure = pauseCount == 0
                        ? WorldSessionAccessFailure.PauseStateMissing
                        : WorldSessionAccessFailure.PauseStateDuplicated;
                    return false;
                }

                Entity pauseEntity = pauseQuery.GetSingletonEntity();
                SimulationPauseState pauseState =
                    entityManager.GetComponentData<SimulationPauseState>(pauseEntity);
                if (pauseState.Value > 1)
                {
                    failure = WorldSessionAccessFailure.PauseStateInvalid;
                    return false;
                }

                pauseState.SetPaused(isPaused);
                entityManager.SetComponentData(pauseEntity, pauseState);
                failure = WorldSessionAccessFailure.None;
                return true;
            }
            finally
            {
                // Pause Queryはこの制御呼び出しだけで使用し、終了時に解放します。
                pauseQuery.Dispose();
            }
        }
    }
}
