// このファイルは、Simulationが保持する絶対タイル座標と、Floating Origin後の表示用相対座標を相互変換します。
// WorldGridのチャンク変換とは責務を分け、Camera・Transform・Tilemapに依存しない純粋計算としてPresentationとInputから共有します。

using System;
using Unity.Mathematics;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 表示空間のローカルセル(0, 0)へ対応させる、Simulation上の絶対タイル座標です。
    /// MAP-02B以降でCameraやTilemapを移動しても、この値は表示だけの基準でありSimulation状態を変更しません。
    /// </summary>
    public readonly struct DisplayOrigin
    {
        /// <summary>
        /// 表示空間のローカルセル(0, 0)に対応する絶対タイル座標です。
        /// 表示原点を移動するPresentationが更新し、座標変換を行う間は同じ値を使用します。
        /// </summary>
        public int2 AbsoluteTileCoordinate { get; }

        /// <summary>
        /// 指定した絶対タイル座標を表示空間の原点として記録します。
        /// </summary>
        /// <param name="absoluteTileCoordinate">ローカルセル(0, 0)へ対応させる絶対タイル座標です。</param>
        public DisplayOrigin(int2 absoluteTileCoordinate)
        {
            AbsoluteTileCoordinate = absoluteTileCoordinate;
        }
    }

    /// <summary>
    /// 表示原点から数えた整数の相対セル座標です。
    /// int全域にある絶対タイルと表示原点の差を失わないよう、各軸を64bit符号付き整数で保持します。
    /// </summary>
    public readonly struct RelativeCellCoordinate : IEquatable<RelativeCellCoordinate>
    {
        /// <summary>表示原点から右を正として数えた相対セル数です。座標変換時に作成し、表示原点が変わるまで使用します。</summary>
        public long X { get; }

        /// <summary>表示原点から上を正として数えた相対セル数です。座標変換時に作成し、表示原点が変わるまで使用します。</summary>
        public long Y { get; }

        /// <summary>
        /// 2軸の相対セル数から表示用の整数座標を作成します。
        /// </summary>
        /// <param name="x">表示原点からの水平セル数です。</param>
        /// <param name="y">表示原点からの垂直セル数です。</param>
        public RelativeCellCoordinate(long x, long y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// 2つの相対セル座標が同じ位置を表すか判定します。
        /// </summary>
        /// <param name="other">比較する相対セル座標です。</param>
        /// <returns>両軸が一致する場合はtrueです。</returns>
        public bool Equals(RelativeCellCoordinate other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>
        /// オブジェクトが同じ相対セル座標を表すか判定します。
        /// </summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じRelativeCellCoordinate値ならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is RelativeCellCoordinate other && Equals(other);
        }

        /// <summary>
        /// Dictionaryなどの実行時検索に使うハッシュ値を返します。
        /// Simulationの状態ハッシュやワールド生成乱数には使用しません。
        /// </summary>
        /// <returns>2軸の64bit値から作った実行時検索用ハッシュです。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }
    }

    /// <summary>
    /// 絶対タイル座標、表示原点、表示用の相対セル・相対ワールド座標を相互変換します。
    /// 1タイルを1ワールド単位とし、セル境界は整数、セル中心は各軸へ0.5を加えた位置として扱います。
    /// </summary>
    public static class WorldCoordinateConversion
    {
        /// <summary>
        /// 絶対タイル座標を、表示原点からの相対セル座標へ変換します。
        /// </summary>
        /// <param name="absoluteTileCoordinate">Simulationが正として保持する絶対タイル座標です。</param>
        /// <param name="displayOrigin">ローカルセル(0, 0)に対応する絶対タイル座標です。</param>
        /// <returns>表示原点から数えた相対セル座標です。int全域同士の差を失わない専用64bit型で返します。</returns>
        public static RelativeCellCoordinate ToRelativeCellCoordinate(
            int2 absoluteTileCoordinate,
            DisplayOrigin displayOrigin)
        {
            // int同士を先に減算すると両端間でオーバーフローするため、各値をlongへ広げてから差を求めます。
            return new RelativeCellCoordinate(
                (long)absoluteTileCoordinate.x - displayOrigin.AbsoluteTileCoordinate.x,
                (long)absoluteTileCoordinate.y - displayOrigin.AbsoluteTileCoordinate.y);
        }

        /// <summary>
        /// 絶対タイルの左下セル境界を、表示原点からの相対ワールド座標へ変換します。
        /// </summary>
        /// <param name="absoluteTileCoordinate">境界位置を求める絶対タイル座標です。</param>
        /// <param name="displayOrigin">ローカルセル(0, 0)に対応する絶対タイル座標です。</param>
        /// <returns>1タイルを1ワールド単位としたセル左下の相対座標です。</returns>
        public static double2 ToRelativeWorldCellCorner(
            int2 absoluteTileCoordinate,
            DisplayOrigin displayOrigin)
        {
            RelativeCellCoordinate relativeCell = ToRelativeCellCoordinate(
                absoluteTileCoordinate,
                displayOrigin);
            return new double2(relativeCell.X, relativeCell.Y);
        }

        /// <summary>
        /// 絶対タイルのセル中心を、表示原点からの相対ワールド座標へ変換します。
        /// </summary>
        /// <param name="absoluteTileCoordinate">中心位置を求める絶対タイル座標です。</param>
        /// <param name="displayOrigin">ローカルセル(0, 0)に対応する絶対タイル座標です。</param>
        /// <returns>セル左下から各軸へ0.5タイル進めた相対中心座標です。</returns>
        public static double2 ToRelativeWorldCellCenter(
            int2 absoluteTileCoordinate,
            DisplayOrigin displayOrigin)
        {
            RelativeCellCoordinate relativeCell = ToRelativeCellCoordinate(
                absoluteTileCoordinate,
                displayOrigin);
            return new double2(relativeCell.X + 0.5d, relativeCell.Y + 0.5d);
        }

        /// <summary>
        /// 整数の相対セル座標を、Simulationで使用する絶対タイル座標へ戻します。
        /// </summary>
        /// <param name="relativeCellCoordinate">表示原点から数えた相対セル座標です。</param>
        /// <param name="displayOrigin">ローカルセル(0, 0)に対応する絶対タイル座標です。</param>
        /// <param name="absoluteTileCoordinate">成功時に得られる絶対タイル座標です。</param>
        /// <returns>両軸がint範囲に収まる場合はtrue、範囲外ならfalseです。</returns>
        public static bool TryToAbsoluteTileCoordinate(
            RelativeCellCoordinate relativeCellCoordinate,
            DisplayOrigin displayOrigin,
            out int2 absoluteTileCoordinate)
        {
            if (!TryAddOrigin(
                    relativeCellCoordinate.X,
                    displayOrigin.AbsoluteTileCoordinate.x,
                    out int absoluteX)
                || !TryAddOrigin(
                    relativeCellCoordinate.Y,
                    displayOrigin.AbsoluteTileCoordinate.y,
                    out int absoluteY))
            {
                absoluteTileCoordinate = default;
                return false;
            }

            absoluteTileCoordinate = new int2(absoluteX, absoluteY);
            return true;
        }

        /// <summary>
        /// 表示空間の連続した相対ワールド座標を、その位置を含む絶対タイル座標へ戻します。
        /// </summary>
        /// <param name="relativeWorldPosition">1タイルを1ワールド単位とする、表示原点からの連続座標です。</param>
        /// <param name="displayOrigin">ローカルセル(0, 0)に対応する絶対タイル座標です。</param>
        /// <param name="absoluteTileCoordinate">成功時に得られる絶対タイル座標です。</param>
        /// <returns>入力が有限値で、床方向へ丸めた結果がint範囲に収まる場合はtrueです。</returns>
        public static bool TryToAbsoluteTileCoordinate(
            double2 relativeWorldPosition,
            DisplayOrigin displayOrigin,
            out int2 absoluteTileCoordinate)
        {
            if (!TryFloorAndAddOrigin(
                    relativeWorldPosition.x,
                    displayOrigin.AbsoluteTileCoordinate.x,
                    out int absoluteX)
                || !TryFloorAndAddOrigin(
                    relativeWorldPosition.y,
                    displayOrigin.AbsoluteTileCoordinate.y,
                    out int absoluteY))
            {
                absoluteTileCoordinate = default;
                return false;
            }

            absoluteTileCoordinate = new int2(absoluteX, absoluteY);
            return true;
        }

        /// <summary>
        /// 相対セル座標へ表示原点を加え、int範囲を越えない場合だけ絶対座標を返します。
        /// </summary>
        /// <param name="relativeCellCoordinate">1軸分の相対セル座標です。</param>
        /// <param name="originCoordinate">同じ軸の表示原点です。</param>
        /// <param name="absoluteCoordinate">成功時に得られる1軸分の絶対座標です。</param>
        /// <returns>加算結果がint範囲内ならtrue、範囲外ならfalseです。</returns>
        private static bool TryAddOrigin(
            long relativeCellCoordinate,
            int originCoordinate,
            out int absoluteCoordinate)
        {
            // long自体の加算を先に行うとlong.MaxValue付近で周回し得るため、許容できる相対範囲を先に比較します。
            long minimumRelativeCoordinate = (long)int.MinValue - originCoordinate;
            long maximumRelativeCoordinate = (long)int.MaxValue - originCoordinate;
            if (relativeCellCoordinate < minimumRelativeCoordinate
                || relativeCellCoordinate > maximumRelativeCoordinate)
            {
                absoluteCoordinate = default;
                return false;
            }

            absoluteCoordinate = (int)(relativeCellCoordinate + originCoordinate);
            return true;
        }

        /// <summary>
        /// 連続した相対座標を床方向へ丸め、表示原点を加えた絶対タイル座標を返します。
        /// </summary>
        /// <param name="relativeWorldCoordinate">1軸分の連続した相対ワールド座標です。</param>
        /// <param name="originCoordinate">同じ軸の表示原点です。</param>
        /// <param name="absoluteCoordinate">成功時に得られる1軸分の絶対座標です。</param>
        /// <returns>入力と結果が有効範囲内ならtrue、NaN・無限大・範囲外ならfalseです。</returns>
        private static bool TryFloorAndAddOrigin(
            double relativeWorldCoordinate,
            int originCoordinate,
            out int absoluteCoordinate)
        {
            if (double.IsNaN(relativeWorldCoordinate)
                || double.IsInfinity(relativeWorldCoordinate))
            {
                absoluteCoordinate = default;
                return false;
            }

            // 負の小数を0方向へ丸めると原点左下のセルを誤るため、数学上の床方向へ丸めます。
            double relativeCellCoordinate = Math.Floor(relativeWorldCoordinate);
            double absoluteCoordinateAsDouble = relativeCellCoordinate + originCoordinate;
            if (absoluteCoordinateAsDouble < int.MinValue
                || absoluteCoordinateAsDouble > int.MaxValue)
            {
                absoluteCoordinate = default;
                return false;
            }

            absoluteCoordinate = (int)absoluteCoordinateAsDouble;
            return true;
        }
    }
}
