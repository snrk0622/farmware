// このファイルは、ワールド共通の永続IDを単体または安定順の一括処理で発行し、保存状態を変換します。
// 呼び出し側が用意したNativeArrayだけを使用し、動物や設備などの機能固有データとID発行規則を分離します。

using System;
using Unity.Collections;

namespace Farmware.Simulation
{
    /// <summary>
    /// 同一Tickの複数生成要求を、Jobの完了順に依存しない順序へ並べるための共通キーです。
    /// 呼び出し側は地域座標、Command受付番号などをMajor、Minor、Sequenceへ割り当て、3値の組を重複させないでください。
    /// </summary>
    public struct PersistentIdAllocationRequest : IComparable<PersistentIdAllocationRequest>
    {
        /// <summary>
        /// 最初に比較する符号付きキーです。地域XやCommand分類など、最上位の安定した並びを呼び出し側が設定します。
        /// </summary>
        public long MajorOrder;

        /// <summary>
        /// MajorOrderが同じ場合に比較する符号付きキーです。地域Yやオブジェクト種別などを呼び出し側が設定します。
        /// </summary>
        public long MinorOrder;

        /// <summary>
        /// MajorOrderとMinorOrderが同じ要求を区別する受付順です。生成要求ごとに一意で安定した値を設定します。
        /// </summary>
        public ulong Sequence;

        /// <summary>
        /// 2つの要求をMajorOrder、MinorOrder、Sequenceの順に比較します。
        /// NativeArray.Sortがこの比較を使い、入力配列の書き込み順が違っても同じ割り当て順を作ります。
        /// </summary>
        /// <param name="other">並び順を比較するもう一方の生成要求です。</param>
        /// <returns>この要求が前なら負、同じキーなら0、後なら正の値です。</returns>
        public readonly int CompareTo(PersistentIdAllocationRequest other)
        {
            int majorComparison = MajorOrder.CompareTo(other.MajorOrder);
            if (majorComparison != 0)
            {
                return majorComparison;
            }

            int minorComparison = MinorOrder.CompareTo(other.MinorOrder);
            if (minorComparison != 0)
            {
                return minorComparison;
            }

            return Sequence.CompareTo(other.Sequence);
        }
    }

    /// <summary>
    /// 安定順へ並べた生成要求と、その要求へ発行した永続IDの組です。
    /// 一括発行の呼び出し側は、この配列順にEntity作成またはID設定を行えます。
    /// </summary>
    public struct PersistentIdAssignment : IComparable<PersistentIdAssignment>
    {
        /// <summary>呼び出し側が指定した、生成要求を一意に並べるための安定キーです。</summary>
        public PersistentIdAllocationRequest Request;

        /// <summary>Requestへ割り当てたIDです。発行失敗時は0の無効値を保持します。</summary>
        public PersistentObjectId Id;

        /// <summary>
        /// 埋め込まれたRequest同士を比較し、割り当て結果を安定キー順へ整列します。
        /// </summary>
        /// <param name="other">並び順を比較するもう一方の割り当て候補です。</param>
        /// <returns>Requestの比較結果です。</returns>
        public readonly int CompareTo(PersistentIdAssignment other)
        {
            return Request.CompareTo(other.Request);
        }
    }

    /// <summary>
    /// 1以上の永続IDを単調増加で発行し、削除済みIDを再利用しない共通アロケーターです。
    /// 状態はPersistentIdAllocatorState Singletonに置き、この型のメソッドを通してだけ変更します。
    /// </summary>
    public static class PersistentIdAllocator
    {
        /// <summary>
        /// 現在のNextIdを1件発行し、次回発行位置を進めます。
        /// 無効な状態または上限到達後は失敗し、その場合はアロケーター状態を変更しません。
        /// </summary>
        /// <param name="state">Worldが所有し、この呼び出しが成功した場合だけ更新するアロケーター状態です。</param>
        /// <param name="id">成功時は発行したID、失敗時は0の無効IDです。</param>
        /// <returns>有効なIDを1件発行できた場合はtrue、それ以外はfalseです。</returns>
        public static bool TryAllocate(
            ref PersistentIdAllocatorState state,
            out PersistentObjectId id)
        {
            id = PersistentObjectId.CreateInvalid();
            if (!IsValidState(state) || state.IsExhausted)
            {
                return false;
            }

            id = new PersistentObjectId
            {
                Value = state.NextId
            };

            // ulong.MaxValueも有効IDとして1回だけ発行し、0へ周回させず明示的な発行停止状態へ移します。
            if (state.NextId == ulong.MaxValue)
            {
                state.ExhaustedValue = 1;
            }
            else
            {
                state.NextId++;
            }

            return true;
        }

        /// <summary>
        /// 生成要求を安定キー順へ並べ、連続する永続IDを一括発行します。
        /// 入出力配列は呼び出し側が確保・破棄し、要求キーの重複、長さ不一致、ID不足時は状態を変更しません。
        /// </summary>
        /// <param name="state">Worldが所有し、全要求の検証と発行が成功した場合だけ更新するアロケーター状態です。</param>
        /// <param name="requests">処理順が不定でもよい生成要求です。内容と配列順は変更しません。</param>
        /// <param name="assignments">requestsと同じ長さを持ち、成功時に安定キー順の要求とIDを受け取る配列です。</param>
        /// <returns>すべての要求へ重複しないIDを発行できた場合はtrue、それ以外はfalseです。</returns>
        public static bool TryAllocateBatch(
            ref PersistentIdAllocatorState state,
            NativeArray<PersistentIdAllocationRequest> requests,
            NativeArray<PersistentIdAssignment> assignments)
        {
            if (!requests.IsCreated ||
                !assignments.IsCreated ||
                requests.Length != assignments.Length)
            {
                return false;
            }

            // 出力配列を作業領域として使うことで、アロケーター内部の一時メモリ確保と破棄漏れを避けます。
            for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                assignments[requestIndex] = new PersistentIdAssignment
                {
                    Request = requests[requestIndex],
                    Id = PersistentObjectId.CreateInvalid()
                };
            }

            assignments.Sort();

            // 同一キーはJob完了順など不安定な順序へ戻る余地があるため、IDを消費する前に一括処理全体を拒否します。
            for (int assignmentIndex = 1;
                 assignmentIndex < assignments.Length;
                 assignmentIndex++)
            {
                if (assignments[assignmentIndex - 1]
                    .Request
                    .CompareTo(assignments[assignmentIndex].Request) == 0)
                {
                    return false;
                }
            }

            if (!CanAllocateCount(state, assignments.Length))
            {
                return false;
            }

            // 事前検証後は途中失敗しないため、各要求へ安定順で連番を設定してから状態を一度だけ確定できます。
            ulong firstId = state.NextId;
            for (int assignmentIndex = 0;
                 assignmentIndex < assignments.Length;
                 assignmentIndex++)
            {
                PersistentIdAssignment assignment = assignments[assignmentIndex];
                assignment.Id = new PersistentObjectId
                {
                    Value = firstId + (ulong)assignmentIndex
                };
                assignments[assignmentIndex] = assignment;
            }

            if (assignments.Length == 0)
            {
                return true;
            }

            ulong lastId = firstId + (ulong)(assignments.Length - 1);
            if (lastId == ulong.MaxValue)
            {
                state.NextId = ulong.MaxValue;
                state.ExhaustedValue = 1;
            }
            else
            {
                state.NextId = lastId + 1;
            }

            return true;
        }

        /// <summary>
        /// 現在のアロケーター状態を、EntityやWorldへ依存しない保存用Snapshotへ変換します。
        /// </summary>
        /// <param name="state">固定Tick境界で読み取った現在のアロケーター状態です。</param>
        /// <param name="snapshot">成功時にNextIdと上限到達状態を受け取る保存用データです。</param>
        /// <returns>状態が保存可能な正規形ならtrue、破損した状態ならfalseです。</returns>
        public static bool TryCreateSnapshot(
            PersistentIdAllocatorState state,
            out PersistentIdAllocatorSnapshot snapshot)
        {
            snapshot = default;
            if (!IsValidState(state))
            {
                return false;
            }

            snapshot = new PersistentIdAllocatorSnapshot
            {
                NextId = state.NextId,
                ExhaustedValue = state.ExhaustedValue
            };
            return true;
        }

        /// <summary>
        /// セーブから読み取ったSnapshotを検証し、新しいWorldへ設定できるアロケーター状態へ復元します。
        /// </summary>
        /// <param name="snapshot">セーブ形式から読み取った次回発行位置と上限到達状態です。</param>
        /// <param name="state">成功時にSingletonへ設定できる復元済み状態です。</param>
        /// <returns>Snapshotが正規形で安全に復元できる場合はtrue、それ以外はfalseです。</returns>
        public static bool TryRestoreSnapshot(
            PersistentIdAllocatorSnapshot snapshot,
            out PersistentIdAllocatorState state)
        {
            state = new PersistentIdAllocatorState
            {
                NextId = snapshot.NextId,
                ExhaustedValue = snapshot.ExhaustedValue
            };

            if (IsValidState(state))
            {
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>
        /// 指定件数を連続発行してもulongの上限を越えず、現在状態が正しいかをID消費前に確認します。
        /// </summary>
        /// <param name="state">検査する現在のアロケーター状態です。</param>
        /// <param name="count">今回まとめて発行する要求数です。0なら状態を変更せず成功できます。</param>
        /// <returns>指定件数をすべて発行できる場合はtrue、それ以外はfalseです。</returns>
        private static bool CanAllocateCount(
            PersistentIdAllocatorState state,
            int count)
        {
            if (count < 0 || !IsValidState(state))
            {
                return false;
            }

            if (count == 0)
            {
                return true;
            }

            if (state.IsExhausted)
            {
                return false;
            }

            // 最後のIDとの差だけを比較し、残数計算そのものがulongで周回しない形にします。
            ulong requiredOffset = (ulong)(count - 1);
            return requiredOffset <= ulong.MaxValue - state.NextId;
        }

        /// <summary>
        /// 実行時状態またはSnapshotが、0除外と上限到達表現の規則を満たすかを確認します。
        /// </summary>
        /// <param name="state">正規形かを確認するアロケーター状態です。</param>
        /// <returns>NextIdとExhaustedValueの組が有効ならtrueです。</returns>
        private static bool IsValidState(PersistentIdAllocatorState state)
        {
            if (state.NextId == PersistentObjectId.InvalidValue ||
                state.ExhaustedValue > 1)
            {
                return false;
            }

            // 発行停止はulong.MaxValueを実際に発行した後だけ成立し、途中値での停止状態は破損として拒否します。
            return !state.IsExhausted || state.NextId == ulong.MaxValue;
        }
    }
}
