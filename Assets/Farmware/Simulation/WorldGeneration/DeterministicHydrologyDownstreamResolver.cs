// このファイルは、TERRAIN-01C3-Bの下流経路をLevel 0〜12のSummaryへまとめ、最大4,096辺で固定終端を分類します。
// DeterministicHydrologyGeneratorのD8一辺だけを読み、ECS、ロード済みチャンク、共有キャッシュへ依存しない純粋計算を提供します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 開始セルから最大2のLevel乗辺を進んだ下流経路の要約です。
    /// キャッシュはこの値を開始座標とLevelで保持できますが、結果の正しさはキャッシュの存在に依存しません。
    /// </summary>
    public struct HydrologyDownstreamSummary
    {
        /// <summary>要約を開始する水文セルの水平絶対座標です。</summary>
        public int StartCellX;

        /// <summary>要約を開始する水文セルの垂直絶対座標です。</summary>
        public int StartCellY;

        /// <summary>終端へ到達したセル、または要約の辺数をすべて進んだセルの水平絶対座標です。</summary>
        public int ReachedCellX;

        /// <summary>終端へ到達したセル、または要約の辺数をすべて進んだセルの垂直絶対座標です。</summary>
        public int ReachedCellY;

        /// <summary>実際に進んだD8辺数です。途中で終端へ着いた場合はLevelの最大辺数より小さくなります。</summary>
        public ushort EdgeCount;

        /// <summary>0は最大1辺、12は最大4,096辺を表す二分SummaryのLevelです。</summary>
        public byte Level;

        /// <summary>途中で見つかったMarineまたはClosedBasinです。終端未到達ならNoneです。</summary>
        public HydrologyConnectionFlags Terminal;

        /// <summary>
        /// 要約が海洋または閉鎖流域の確定終端を含むかを返します。
        /// </summary>
        public bool HasTerminal => Terminal == HydrologyConnectionFlags.Marine
            || Terminal == HydrologyConnectionFlags.ClosedBasin;
    }

    /// <summary>
    /// 最大4,096辺の下流探索を終えた、1水文セルの固定終端結果です。
    /// GeneratedHydrologyCellへコピーし、後続の集水・湖・診断処理から再利用します。
    /// </summary>
    public struct HydrologyDownstreamResult
    {
        /// <summary>終端セル、または4,096辺進んだ打切り位置の水平絶対座標です。</summary>
        public int ReachedCellX;

        /// <summary>終端セル、または4,096辺進んだ打切り位置の垂直絶対座標です。</summary>
        public int ReachedCellY;

        /// <summary>開始セルから実際に進んだD8辺数です。開始セル自身が終端なら0です。</summary>
        public ushort EdgeCount;

        /// <summary>相互排他的なMarine、ClosedBasin、LongDrainageのいずれか一つです。</summary>
        public HydrologyConnectionFlags Terminal;
    }

    /// <summary>
    /// 固定D8流向を有限回たどり、二分Summaryと下流終端を決定論的に求める純粋計算です。
    /// Burst Jobから呼べるよう、管理配列、delegate、再帰、例外、共有mutableキャッシュを使用しません。
    /// </summary>
    public static class DeterministicHydrologyDownstreamResolver
    {
        /// <summary>C3-Bで固定した最大Summary Levelです。Level 12が4,096辺を表します。</summary>
        private const int MaximumSupportedSummaryLevel = 12;

        /// <summary>
        /// 開始セルから最大4,096辺を進み、Marine、ClosedBasin、LongDrainageの一つへ固定します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、標高、水文設定を持つ固定ワールド設定です。</param>
        /// <param name="startCellX">開始する水平水文セル座標です。</param>
        /// <param name="startCellY">開始する垂直水文セル座標です。</param>
        /// <param name="result">成功時の到達座標、辺数、相互排他的な終端分類です。</param>
        /// <returns>設定と経路上の全座標が有効で、固定上限内の探索を完了できた場合はtrueです。</returns>
        public static bool TryResolve(
            in WorldGenerationSettings settings,
            int startCellX,
            int startCellY,
            out HydrologyDownstreamResult result)
        {
            return TryResolveWithStartCell(
                settings,
                startCellX,
                startCellY,
                out _,
                out result);
        }

        /// <summary>
        /// 開始セルのD8一辺と下流終端を一度の開始セル生成から求め、Chunk生成時の重複サンプルを避けます。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCellX">開始する水平水文セル座標です。</param>
        /// <param name="startCellY">開始する垂直水文セル座標です。</param>
        /// <param name="startCell">成功時の開始セル代表値とD8一辺です。</param>
        /// <param name="result">成功時の到達座標、辺数、固定終端分類です。</param>
        /// <returns>開始セルと経路を固定上限内で生成できた場合はtrueです。</returns>
        internal static bool TryResolveWithStartCell(
            in WorldGenerationSettings settings,
            int startCellX,
            int startCellY,
            out GeneratedHydrologyCell startCell,
            out HydrologyDownstreamResult result)
        {
            startCell = default;
            result = default;
            if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    startCellX,
                    startCellY,
                    out startCell)
                || !TryBuildSummaryFromGeneratedStart(
                    settings,
                    startCell,
                    settings.Hydrology.MaximumDownstreamSummaryLevel,
                    out HydrologyDownstreamSummary summary)
                || !TryClassifySummary(settings.Hydrology, summary, out result))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 最大LevelのSummaryを、相互排他的な固定終端結果へ変換します。
        /// テストや将来の共有キャッシュも同じ上限判定を使い、未到達をロード不足と誤認しないための境界です。
        /// </summary>
        /// <param name="settings">最大辺数と最大Levelを持つ固定水文設定です。</param>
        /// <param name="summary">分類する最大LevelのSummaryです。</param>
        /// <param name="result">成功時のMarine、ClosedBasin、LongDrainageのいずれか一つです。</param>
        /// <returns>SummaryのLevel、辺数、終端値が固定設定と整合する場合はtrueです。</returns>
        public static bool TryClassifySummary(
            in HydrologyGenerationSettings settings,
            in HydrologyDownstreamSummary summary,
            out HydrologyDownstreamResult result)
        {
            result = default;
            if (!DeterministicHydrologyGenerator.ValidateSettings(settings)
                || summary.Level != settings.MaximumDownstreamSummaryLevel
                || summary.EdgeCount > settings.MaximumDownstreamEdges
                || !IsSummaryTerminal(summary.Terminal)
                || (!summary.HasTerminal && summary.EdgeCount != settings.MaximumDownstreamEdges)
                || (summary.EdgeCount == 0
                    && (summary.ReachedCellX != summary.StartCellX
                        || summary.ReachedCellY != summary.StartCellY)))
            {
                return false;
            }

            HydrologyConnectionFlags terminal = summary.HasTerminal
                ? summary.Terminal
                : HydrologyConnectionFlags.LongDrainage;
            result = new HydrologyDownstreamResult
            {
                ReachedCellX = summary.ReachedCellX,
                ReachedCellY = summary.ReachedCellY,
                EdgeCount = summary.EdgeCount,
                Terminal = terminal
            };
            return true;
        }

        /// <summary>
        /// 指定Levelの最大辺数までD8経路をLevel 0単位でたどります。
        /// 共有Summaryがない基準経路として、終端へ早く到達した場合は残りを進めず実辺数と終端を返します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCellX">開始する水平水文セル座標です。</param>
        /// <param name="startCellY">開始する垂直水文セル座標です。</param>
        /// <param name="level">0〜12のSummary Levelです。</param>
        /// <param name="summary">成功時の下流経路要約です。</param>
        /// <returns>設定、Level、経路上の座標をすべて検証できた場合はtrueです。</returns>
        public static bool TryBuildSummary(
            in WorldGenerationSettings settings,
            int startCellX,
            int startCellY,
            int level,
            out HydrologyDownstreamSummary summary)
        {
            summary = default;
            if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    startCellX,
                    startCellY,
                    out GeneratedHydrologyCell current))
            {
                return false;
            }

            return TryBuildSummaryFromGeneratedStart(settings, current, level, out summary);
        }

        /// <summary>
        /// 検証済みの開始セルから指定Levelを展開し、呼出元が同じ開始セルを再生成しないための内部処理です。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCell">開始セルの代表値とD8一辺です。</param>
        /// <param name="level">0〜12のSummary Levelです。</param>
        /// <param name="summary">成功時の下流経路要約です。</param>
        /// <returns>設定とLevelが有効で、経路上の全座標を生成できた場合はtrueです。</returns>
        private static bool TryBuildSummaryFromGeneratedStart(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell startCell,
            int level,
            out HydrologyDownstreamSummary summary)
        {
            summary = default;
            if (!DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || level < 0
                || level > settings.Hydrology.MaximumDownstreamSummaryLevel)
            {
                return false;
            }

            GeneratedHydrologyCell current = startCell;
            int startCellX = startCell.CellX;
            int startCellY = startCell.CellY;

            HydrologyConnectionFlags currentTerminal = GetTerminal(current);
            if (currentTerminal != HydrologyConnectionFlags.None)
            {
                // 開始セル自体が終端の場合は辺を進めず、0辺Summaryとして分類を返します。
                summary = CreateSummary(
                    startCellX,
                    startCellY,
                    startCellX,
                    startCellY,
                    0,
                    level,
                    currentTerminal);
                return true;
            }

            int maximumEdges = 1 << level;
            int traversedEdges = 0;

            for (int edgeIndex = 0; edgeIndex < maximumEdges; edgeIndex++)
            {
                if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    current.DownstreamCellX,
                    current.DownstreamCellY,
                    out GeneratedHydrologyCell reached))
                {
                    // ワールド座標端で次セルを生成できない場合はLongDrainageへ丸めず、チャンク生成自体を失敗させます。
                    return false;
                }

                currentTerminal = GetTerminal(reached);
                traversedEdges++;
                current = reached;
                if (currentTerminal != HydrologyConnectionFlags.None)
                {
                    break;
                }
            }

            // キャッシュ利用時はTryCombineで同じ到達値を作れます。ここでは基準となるLevel 0展開結果を直接閉じます。
            summary = CreateSummary(
                startCellX,
                startCellY,
                current.CellX,
                current.CellY,
                traversedEdges,
                level,
                currentTerminal);
            return true;
        }

        /// <summary>
        /// 同じLevelで座標が連続する二つのSummaryを、一段上のSummaryへ決定論的に連結します。
        /// </summary>
        /// <param name="first">経路の前半です。途中終端を含まない完全長Summaryである必要があります。</param>
        /// <param name="second">firstの到達座標から始まる経路の後半です。</param>
        /// <param name="combined">成功時のLevelが一つ高い連結結果です。</param>
        /// <returns>Level、完全長、接続座標、最大Levelがすべて有効ならtrueです。</returns>
        public static bool TryCombine(
            in HydrologyDownstreamSummary first,
            in HydrologyDownstreamSummary second,
            out HydrologyDownstreamSummary combined)
        {
            combined = default;
            if (first.Level != second.Level
                || first.Level >= MaximumSupportedSummaryLevel
                || first.Terminal != HydrologyConnectionFlags.None
                || first.EdgeCount != (1 << first.Level)
                || first.ReachedCellX != second.StartCellX
                || first.ReachedCellY != second.StartCellY
                || second.EdgeCount == 0
                || second.EdgeCount > (1 << second.Level)
                || !IsSummaryTerminal(second.Terminal)
                || (second.Terminal == HydrologyConnectionFlags.None
                    && second.EdgeCount != (1 << second.Level)))
            {
                return false;
            }

            int combinedEdgeCount = first.EdgeCount + second.EdgeCount;
            combined = CreateSummary(
                first.StartCellX,
                first.StartCellY,
                second.ReachedCellX,
                second.ReachedCellY,
                combinedEdgeCount,
                first.Level + 1,
                second.Terminal);
            return true;
        }

        /// <summary>流向セル自身が海洋または陸上末端なら対応する終端を返し、それ以外はNoneを返します。</summary>
        private static HydrologyConnectionFlags GetTerminal(in GeneratedHydrologyCell cell)
        {
            if (cell.IsOceanOutlet)
            {
                return HydrologyConnectionFlags.Marine;
            }

            return cell.HasDownstream
                ? HydrologyConnectionFlags.None
                : HydrologyConnectionFlags.ClosedBasin;
        }

        /// <summary>Summaryが保持できるNone、Marine、ClosedBasinのいずれか一つかを検証します。</summary>
        private static bool IsSummaryTerminal(HydrologyConnectionFlags terminal)
        {
            return terminal == HydrologyConnectionFlags.None
                || terminal == HydrologyConnectionFlags.Marine
                || terminal == HydrologyConnectionFlags.ClosedBasin;
        }

        /// <summary>検証済みの経路値から、管理参照を持たないSummary値を作ります。</summary>
        private static HydrologyDownstreamSummary CreateSummary(
            int startCellX,
            int startCellY,
            int reachedCellX,
            int reachedCellY,
            int edgeCount,
            int level,
            HydrologyConnectionFlags terminal)
        {
            return new HydrologyDownstreamSummary
            {
                StartCellX = startCellX,
                StartCellY = startCellY,
                ReachedCellX = reachedCellX,
                ReachedCellY = reachedCellY,
                EdgeCount = (ushort)edgeCount,
                Level = (byte)level,
                Terminal = terminal
            };
        }
    }
}
