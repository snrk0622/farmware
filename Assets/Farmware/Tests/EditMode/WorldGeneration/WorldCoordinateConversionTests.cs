// このファイルは、MAP-02Aの絶対タイル座標・表示原点・相対表示座標の相互変換をEditModeで検証します。
// SceneやTransformを使わず、負座標、int両端、セル境界、無効入力でも同じ純粋計算結果になることを保証します。

using NUnit.Framework;
using Unity.Mathematics;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// Floating Originの座標変換APIが、表示原点の位置や符号に依存せず絶対タイルを維持することを保証します。
    /// Unity Test RunnerのEditModeテストから実行されます。
    /// </summary>
    public sealed class WorldCoordinateConversionTests
    {
        /// <summary>
        /// 表示原点と同じ絶対タイルを準備し、相対セル0と、そのセルの左下・中心座標へ変換されることを保証します。
        /// </summary>
        [Test]
        public void DisplayOriginTile_MapsToRelativeZeroCell()
        {
            int2 originTile = new int2(12345, -67890);
            DisplayOrigin displayOrigin = new DisplayOrigin(originTile);

            RelativeCellCoordinate relativeCell = WorldCoordinateConversion.ToRelativeCellCoordinate(
                originTile,
                displayOrigin);
            double2 relativeCorner = WorldCoordinateConversion.ToRelativeWorldCellCorner(
                originTile,
                displayOrigin);
            double2 relativeCenter = WorldCoordinateConversion.ToRelativeWorldCellCenter(
                originTile,
                displayOrigin);

            Assert.That(relativeCell, Is.EqualTo(new RelativeCellCoordinate(0L, 0L)));
            Assert.That(relativeCorner, Is.EqualTo(new double2(0d, 0d)));
            Assert.That(relativeCenter, Is.EqualTo(new double2(0.5d, 0.5d)));
        }

        /// <summary>
        /// 正負の絶対座標と表示原点を準備し、int同士の差が負方向でも正しい相対セルになることを保証します。
        /// </summary>
        /// <param name="absoluteX">変換する絶対Xタイル座標です。</param>
        /// <param name="absoluteY">変換する絶対Yタイル座標です。</param>
        /// <param name="originX">表示原点の絶対Xタイル座標です。</param>
        /// <param name="originY">表示原点の絶対Yタイル座標です。</param>
        /// <param name="expectedRelativeX">期待する相対Xセル座標です。</param>
        /// <param name="expectedRelativeY">期待する相対Yセル座標です。</param>
        [TestCase(10, 20, 3, 4, 7L, 16L)]
        [TestCase(-10, -20, -3, -4, -7L, -16L)]
        [TestCase(-1, 0, 0, -1, -1L, 1L)]
        public void AbsoluteTile_ToRelativeCell_UsesSignedDifference(
            int absoluteX,
            int absoluteY,
            int originX,
            int originY,
            long expectedRelativeX,
            long expectedRelativeY)
        {
            RelativeCellCoordinate actual = WorldCoordinateConversion.ToRelativeCellCoordinate(
                new int2(absoluteX, absoluteY),
                new DisplayOrigin(new int2(originX, originY)));

            Assert.That(
                actual,
                Is.EqualTo(new RelativeCellCoordinate(expectedRelativeX, expectedRelativeY)));
        }

        /// <summary>
        /// int最小値と最大値を反対側の表示原点から変換し、32bit減算を行わず全差分を保持することを保証します。
        /// </summary>
        [Test]
        public void AbsoluteTile_ToRelativeCell_PreservesFullIntRangeDifference()
        {
            RelativeCellCoordinate actual = WorldCoordinateConversion.ToRelativeCellCoordinate(
                new int2(int.MaxValue, int.MinValue),
                new DisplayOrigin(new int2(int.MinValue, int.MaxValue)));

            Assert.That(actual.X, Is.EqualTo(4294967295L));
            Assert.That(actual.Y, Is.EqualTo(-4294967295L));
        }

        /// <summary>
        /// int両端を含む絶対タイルを相対セルへ変換して戻し、元の座標が完全に復元されることを保証します。
        /// </summary>
        [Test]
        public void RelativeCell_RoundTrip_RestoresAbsoluteTileAtIntBounds()
        {
            int2 expected = new int2(int.MinValue, int.MaxValue);
            DisplayOrigin displayOrigin = new DisplayOrigin(
                new int2(int.MaxValue, int.MinValue));
            RelativeCellCoordinate relativeCell = WorldCoordinateConversion.ToRelativeCellCoordinate(
                expected,
                displayOrigin);

            bool succeeded = WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                relativeCell,
                displayOrigin,
                out int2 actual);

            Assert.That(succeeded, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 表示原点を加えるとint範囲外になる相対セルを準備し、周回した誤座標を返さず失敗することを保証します。
        /// </summary>
        [Test]
        public void RelativeCell_OutsideAbsoluteIntRange_IsRejected()
        {
            DisplayOrigin displayOrigin = new DisplayOrigin(
                new int2(int.MaxValue, int.MinValue));

            bool succeeded = WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                new RelativeCellCoordinate(1L, -1L),
                displayOrigin,
                out int2 actual);

            Assert.That(succeeded, Is.False);
            Assert.That(actual, Is.EqualTo(default(int2)));
        }

        /// <summary>
        /// セル境界付近の正負小数を準備し、C#の0方向丸めではなく床方向で所属タイルが選ばれることを保証します。
        /// </summary>
        /// <param name="relativeX">表示原点からの相対Xワールド座標です。</param>
        /// <param name="expectedAbsoluteX">期待する絶対Xタイル座標です。</param>
        [TestCase(0d, 100)]
        [TestCase(0.999999d, 100)]
        [TestCase(1d, 101)]
        [TestCase(-0.000001d, 99)]
        [TestCase(-1d, 99)]
        [TestCase(-1.000001d, 98)]
        public void RelativeWorldPosition_UsesFloorForContainingTile(
            double relativeX,
            int expectedAbsoluteX)
        {
            DisplayOrigin displayOrigin = new DisplayOrigin(new int2(100, -50));

            bool succeeded = WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                new double2(relativeX, -0.25d),
                displayOrigin,
                out int2 actual);

            Assert.That(succeeded, Is.True);
            Assert.That(actual, Is.EqualTo(new int2(expectedAbsoluteX, -51)));
        }

        /// <summary>
        /// 同じ絶対タイルを異なる表示原点から相対化し、それぞれ逆変換しても同じ絶対座標へ戻ることを保証します。
        /// </summary>
        [Test]
        public void MovingDisplayOrigin_ChangesOnlyRelativePosition()
        {
            int2 expectedAbsoluteTile = new int2(-2049, 4097);
            DisplayOrigin firstOrigin = new DisplayOrigin(new int2(0, 0));
            DisplayOrigin secondOrigin = new DisplayOrigin(new int2(-2048, 4096));
            double2 firstRelativeCenter =
                WorldCoordinateConversion.ToRelativeWorldCellCenter(
                    expectedAbsoluteTile,
                    firstOrigin);
            double2 secondRelativeCenter =
                WorldCoordinateConversion.ToRelativeWorldCellCenter(
                    expectedAbsoluteTile,
                    secondOrigin);

            bool firstSucceeded = WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                firstRelativeCenter,
                firstOrigin,
                out int2 firstAbsoluteTile);
            bool secondSucceeded = WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                secondRelativeCenter,
                secondOrigin,
                out int2 secondAbsoluteTile);

            Assert.That(firstRelativeCenter, Is.Not.EqualTo(secondRelativeCenter));
            Assert.That(firstSucceeded, Is.True);
            Assert.That(secondSucceeded, Is.True);
            Assert.That(firstAbsoluteTile, Is.EqualTo(expectedAbsoluteTile));
            Assert.That(secondAbsoluteTile, Is.EqualTo(expectedAbsoluteTile));
        }

        /// <summary>
        /// NaN、無限大、int範囲外の相対位置を準備し、不定な絶対タイルへ変換されないことを保証します。
        /// </summary>
        /// <param name="invalidCoordinate">拒否される相対Xワールド座標です。</param>
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(2147483648d)]
        [TestCase(-2147483649d)]
        public void InvalidRelativeWorldPosition_IsRejected(double invalidCoordinate)
        {
            bool succeeded = WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                new double2(invalidCoordinate, 0d),
                new DisplayOrigin(new int2(0, 0)),
                out int2 actual);

            Assert.That(succeeded, Is.False);
            Assert.That(actual, Is.EqualTo(default(int2)));
        }
    }
}
