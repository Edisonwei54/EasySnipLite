using System.Windows;
using System.Windows.Media.Imaging;
using EasySnipLite.Selection;

namespace EasySnipLite.Tests;

/// <summary>M2 选区几何纯逻辑:命中测试 / 手柄缩放 / 移动钳制 / 放大镜定位。</summary>
public class SelectionMathTests
{
    private const double R = 6; // 手柄命中半径

    // ---- HitTest ----

    [Fact]
    public void HitTest_PointFarAway_ReturnsNone()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        Assert.Equal(SelectionHandle.None, SelectionMath.HitTest(rect, new Point(0, 0), R));
        Assert.Equal(SelectionHandle.None, SelectionMath.HitTest(rect, new Point(500, 500), R));
    }

    [Fact]
    public void HitTest_Corners_ReturnCornerHandles()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        Assert.Equal(SelectionHandle.CornerNW, SelectionMath.HitTest(rect, new Point(101, 101), R));
        Assert.Equal(SelectionHandle.CornerNE, SelectionMath.HitTest(rect, new Point(299, 101), R));
        Assert.Equal(SelectionHandle.CornerSW, SelectionMath.HitTest(rect, new Point(101, 249), R));
        Assert.Equal(SelectionHandle.CornerSE, SelectionMath.HitTest(rect, new Point(299, 249), R));
    }

    [Fact]
    public void HitTest_Edges_ReturnEdgeHandles()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        Assert.Equal(SelectionHandle.EdgeTop, SelectionMath.HitTest(rect, new Point(200, 100), R));
        Assert.Equal(SelectionHandle.EdgeBottom, SelectionMath.HitTest(rect, new Point(200, 250), R));
        Assert.Equal(SelectionHandle.EdgeLeft, SelectionMath.HitTest(rect, new Point(100, 200), R));
        Assert.Equal(SelectionHandle.EdgeRight, SelectionMath.HitTest(rect, new Point(300, 200), R));
    }

    [Fact]
    public void HitTest_Inside_ReturnsBody()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        Assert.Equal(SelectionHandle.Body, SelectionMath.HitTest(rect, new Point(200, 200), R));
    }

    [Fact]
    public void HitTest_NearCorner_PrefersCornerOverEdge()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        // 右上角:同时靠近 EdgeTop/EdgeRight,应判为角
        Assert.Equal(SelectionHandle.CornerNE, SelectionMath.HitTest(rect, new Point(297, 103), R));
    }

    [Fact]
    public void HitTest_MinimalRect_DoesNotThrow()
    {
        var rect = new Int32Rect(50, 50, 3, 3);
        Assert.Equal(SelectionHandle.CornerSE, SelectionMath.HitTest(rect, new Point(52, 52), R));
    }

    // ---- Resize(手柄缩放,对边固定) ----

    [Fact]
    public void Resize_CornerSE_FollowsMouse()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var result = SelectionMath.Resize(rect, SelectionHandle.CornerSE, new Point(360, 300));
        Assert.Equal(new Int32Rect(100, 100, 260, 200), result);
    }

    [Fact]
    public void Resize_CornerNW_FollowsMouse_KeepsOppositeFixed()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var result = SelectionMath.Resize(rect, SelectionHandle.CornerNW, new Point(60, 80));
        Assert.Equal(new Int32Rect(60, 80, 240, 170), result);
    }

    [Fact]
    public void Resize_EdgeTop_OnlyTopMoves()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var result = SelectionMath.Resize(rect, SelectionHandle.EdgeTop, new Point(999, 70));
        Assert.Equal(new Int32Rect(100, 70, 200, 180), result);
    }

    [Fact]
    public void Resize_EdgeRight_OnlyRightMoves()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var result = SelectionMath.Resize(rect, SelectionHandle.EdgeRight, new Point(330, 999));
        Assert.Equal(new Int32Rect(100, 100, 230, 150), result);
    }

    [Fact]
    public void Resize_CrossingOpposite_ClampsToMinSize()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var min = (int)SelectionMath.MinSelectionSize;
        // 拖右下角越过左边界:宽度钳制为 MinSelectionSize,对边(Left/Top)不动
        var result = SelectionMath.Resize(rect, SelectionHandle.CornerSE, new Point(50, 50));
        Assert.Equal(new Int32Rect(100, 100, min, min), result);
    }

    [Fact]
    public void Resize_BodyOrNone_ReturnsOriginal()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        Assert.Equal(rect, SelectionMath.Resize(rect, SelectionHandle.Body, new Point(1, 1)));
        Assert.Equal(rect, SelectionMath.Resize(rect, SelectionHandle.None, new Point(1, 1)));
    }

    [Fact]
    public void Resize_ShrinksToExactMinimum()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        // 拖 CornerSE 到刚好还剩 4px:允许 4px(大于最小)
        var result = SelectionMath.Resize(rect, SelectionHandle.CornerSE, new Point(104, 104));
        Assert.Equal(new Int32Rect(100, 100, 4, 4), result);
    }

    // ---- Move(整体移动 + 钳制) ----

    [Fact]
    public void Move_TranslatesWithinBounds()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var bounds = new Int32Rect(0, 0, 1920, 1080);
        Assert.Equal(new Int32Rect(160, 140, 200, 150), SelectionMath.Move(rect, 60, 40, bounds));
    }

    [Fact]
    public void Move_ClampsToLeftAndTop()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var bounds = new Int32Rect(0, 0, 1920, 1080);
        Assert.Equal(new Int32Rect(0, 0, 200, 150), SelectionMath.Move(rect, -500, -500, bounds));
    }

    [Fact]
    public void Move_ClampsToRightAndBottom()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var bounds = new Int32Rect(0, 0, 1920, 1080);
        Assert.Equal(new Int32Rect(1720, 930, 200, 150), SelectionMath.Move(rect, 9999, 9999, bounds));
    }

    [Fact]
    public void Move_ZeroDelta_NoChange()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var bounds = new Int32Rect(0, 0, 1920, 1080);
        Assert.Equal(rect, SelectionMath.Move(rect, 0, 0, bounds));
    }

    // ---- 方向键微调(1px / Shift 10px,复用 Move 的钳制) ----

    [Fact]
    public void Nudge_OnePixel_AllDirections()
    {
        var rect = new Int32Rect(100, 100, 200, 150);
        var bounds = new Int32Rect(0, 0, 1920, 1080);
        Assert.Equal(new Int32Rect(101, 100, 200, 150), SelectionMath.Nudge(rect, 1, 0, bounds));
        Assert.Equal(new Int32Rect(100, 101, 200, 150), SelectionMath.Nudge(rect, 0, 1, bounds));
        Assert.Equal(new Int32Rect(99, 100, 200, 150), SelectionMath.Nudge(rect, -1, 0, bounds));
        Assert.Equal(new Int32Rect(100, 99, 200, 150), SelectionMath.Nudge(rect, 0, -1, bounds));
    }

    [Fact]
    public void Nudge_ClampsAtScreenEdge()
    {
        var rect = new Int32Rect(0, 0, 200, 150);
        var bounds = new Int32Rect(0, 0, 1920, 1080);
        Assert.Equal(rect, SelectionMath.Nudge(rect, -1, -1, bounds));
    }

    // ---- 遮罩矩形(选区外的暗化四块) ----

    private static void AssertEmptyMask(Rect r) =>
        Assert.True(r.Width <= 0 || r.Height <= 0, $"expected empty (invisible) mask, got {r}");

    [Fact]
    public void MaskRectangles_NullSelection_FullScreenTopMask()
    {
        var win = new Rect(0, 0, 1920, 1080);
        var (top, bottom, left, right) = SelectionMath.MaskRectangles(win, null);
        Assert.Equal(win, top);
        AssertEmptyMask(bottom);
        AssertEmptyMask(left);
        AssertEmptyMask(right);
    }

    [Fact]
    public void MaskRectangles_Selection_SplitsIntoSides()
    {
        var win = new Rect(0, 0, 1920, 1080);
        var sel = new Rect(400, 300, 300, 200);
        var (top, bottom, left, right) = SelectionMath.MaskRectangles(win, sel);

        Assert.Equal(new Rect(0, 0, 1920, 300), top);
        Assert.Equal(new Rect(0, 500, 1920, 580), bottom);
        Assert.Equal(new Rect(0, 300, 400, 200), left);
        Assert.Equal(new Rect(700, 300, 1220, 200), right);
    }

    [Fact]
    public void MaskRectangles_SelectionTouchingEdges_ZeroSizeSides()
    {
        var win = new Rect(0, 0, 1920, 1080);
        var sel = new Rect(0, 0, 300, 200);
        var (top, bottom, left, right) = SelectionMath.MaskRectangles(win, sel);

        AssertEmptyMask(top);                             // 选区贴顶 → 上遮罩空
        Assert.Equal(new Rect(0, 200, 1920, 880), bottom);
        AssertEmptyMask(left);                            // 选区贴左 → 左遮罩空
        Assert.Equal(new Rect(300, 0, 1620, 200), right);
    }

    [Fact]
    public void MaskRectangles_SelectionOutsideWindow_NoInvalidSizes()
    {
        var win = new Rect(0, 0, 1920, 1080);
        // 选区完全在窗口外(跨屏窗口的常见情况)
        var sel = new Rect(2000, 100, 300, 200);
        var (top, bottom, left, right) = SelectionMath.MaskRectangles(win, sel);

        foreach (var r in new[] { top, bottom, left, right })
        {
            Assert.False(double.IsNaN(r.Width) || double.IsNaN(r.Height), "mask size must not be NaN");
            Assert.False(double.IsPositiveInfinity(r.Width) || double.IsPositiveInfinity(r.Height), "mask size must not be +Inf");
        }
    }

    [Fact]
    public void MaskRectangles_EmptySelection_NoInvalidSizes()
    {
        var win = new Rect(0, 0, 1920, 1080);
        // Rect.Empty(Y=0, Height=-Inf)——必须与 null 同样处理,不得产生 +Inf 尺寸
        var (top, bottom, left, right) = SelectionMath.MaskRectangles(win, Rect.Empty);
        Assert.Equal(win, top);
        AssertEmptyMask(bottom);
        AssertEmptyMask(left);
        AssertEmptyMask(right);
    }

    // ---- 放大镜定位(窗口逻辑坐标) ----

    [Fact]
    public void MagnifierTopLeft_PlacesRightBelowCursor()
    {
        var mouse = new Point(100, 100);
        var window = new Size(1920, 1080);
        var mag = new Size(90, 110);
        var pos = SelectionMath.MagnifierTopLeft(mouse, window, mag);
        Assert.Equal(new Point(100 + SelectionMath.MagnifierOffset, 100 + SelectionMath.MagnifierOffset), pos);
    }

    [Fact]
    public void MagnifierTopLeft_FlipsLeftWhenNoRoomOnRight()
    {
        var mouse = new Point(1900, 100);
        var window = new Size(1920, 1080);
        var mag = new Size(90, 110);
        var pos = SelectionMath.MagnifierTopLeft(mouse, window, mag);
        Assert.Equal(1900 - SelectionMath.MagnifierOffset - 90, pos.X, 5);
        Assert.True(pos.X >= 0, $"magnifier should stay on screen, got X={pos.X}");
    }

    [Fact]
    public void MagnifierTopLeft_TopLeftCorner_UsesDefaultOffset()
    {
        var mouse = new Point(0, 0);
        var window = new Size(1920, 1080);
        var mag = new Size(90, 110);
        var pos = SelectionMath.MagnifierTopLeft(mouse, window, mag);
        Assert.Equal(new Point(SelectionMath.MagnifierOffset, SelectionMath.MagnifierOffset), pos);
    }
}
