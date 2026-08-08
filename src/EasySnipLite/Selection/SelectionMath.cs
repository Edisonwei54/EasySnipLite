using System.Windows;
using System.Windows.Media.Imaging;

namespace EasySnipLite.Selection;

/// <summary>选区命中目标:无 / 主体 / 四边 / 四角。</summary>
public enum SelectionHandle
{
    None,
    Body,
    EdgeTop,
    EdgeBottom,
    EdgeLeft,
    EdgeRight,
    CornerNW,
    CornerNE,
    CornerSW,
    CornerSE,
}

/// <summary>
/// M2 选区几何纯逻辑:命中测试、手柄缩放(对边固定)、整体移动钳制、放大镜定位。
/// 全部使用虚拟屏幕物理像素坐标,无 UI 依赖,可单测。
/// </summary>
public static class SelectionMath
{
    /// <summary>选区允许的最小边长(物理像素)。</summary>
    public const double MinSelectionSize = 3;

    /// <summary>放大镜与鼠标之间的间隔(窗口逻辑像素)。</summary>
    public const double MagnifierOffset = 16;

    /// <summary>命中测试:返回 point(虚拟物理坐标)命中的目标。角优先于边,边优先于主体。</summary>
    public static SelectionHandle HitTest(Int32Rect rect, Point point, double hitRadius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return SelectionHandle.None;

        double l = rect.X, t = rect.Y;
        double r = rect.X + rect.Width, b = rect.Y + rect.Height;
        double x = point.X, y = point.Y;

        // 四角优先(极小选区可能多角同时命中,取最近角)
        var corner = BestCorner(x, y, l, t, r, b, hitRadius);
        if (corner != SelectionHandle.None) return corner;

        // 四边
        if (Math.Abs(x - l) <= hitRadius && y >= t && y <= b) return SelectionHandle.EdgeLeft;
        if (Math.Abs(x - r) <= hitRadius && y >= t && y <= b) return SelectionHandle.EdgeRight;
        if (Math.Abs(y - t) <= hitRadius && x >= l && x <= r) return SelectionHandle.EdgeTop;
        if (Math.Abs(y - b) <= hitRadius && x >= l && x <= r) return SelectionHandle.EdgeBottom;

        // 主体
        if (x >= l && x <= r && y >= t && y <= b) return SelectionHandle.Body;

        return SelectionHandle.None;
    }

    /// <summary>返回命中半径内距离鼠标最近的角手柄;无命中返回 None。</summary>
    private static SelectionHandle BestCorner(
        double x, double y, double l, double t, double r, double b, double radius)
    {
        double r2 = radius * radius;
        double bestDist = double.MaxValue;
        var best = SelectionHandle.None;

        double d = Dist2(x, y, l, t);
        if (d <= r2 && d < bestDist) { bestDist = d; best = SelectionHandle.CornerNW; }
        d = Dist2(x, y, r, t);
        if (d <= r2 && d < bestDist) { bestDist = d; best = SelectionHandle.CornerNE; }
        d = Dist2(x, y, l, b);
        if (d <= r2 && d < bestDist) { bestDist = d; best = SelectionHandle.CornerSW; }
        d = Dist2(x, y, r, b);
        if (d <= r2 && d < bestDist) { bestDist = d; best = SelectionHandle.CornerSE; }
        return best;
    }

    private static double Dist2(double x, double y, double cx, double cy)
    {
        double dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 手柄拖动:被拖手柄的对边保持固定,拖动手边跟随鼠标,最小尺寸为 MinSelectionSize。
    /// Body/None 原样返回。
    /// </summary>
    public static Int32Rect Resize(Int32Rect rect, SelectionHandle handle, Point mouse)
    {
        double l = rect.X, t = rect.Y;
        double r = rect.X + rect.Width, b = rect.Y + rect.Height;

        switch (handle)
        {
            case SelectionHandle.CornerNW:
                l = Math.Min(mouse.X, r - MinSelectionSize);
                t = Math.Min(mouse.Y, b - MinSelectionSize);
                break;
            case SelectionHandle.CornerNE:
                r = Math.Max(mouse.X, l + MinSelectionSize);
                t = Math.Min(mouse.Y, b - MinSelectionSize);
                break;
            case SelectionHandle.CornerSW:
                l = Math.Min(mouse.X, r - MinSelectionSize);
                b = Math.Max(mouse.Y, t + MinSelectionSize);
                break;
            case SelectionHandle.CornerSE:
                r = Math.Max(mouse.X, l + MinSelectionSize);
                b = Math.Max(mouse.Y, t + MinSelectionSize);
                break;
            case SelectionHandle.EdgeTop:
                t = Math.Min(mouse.Y, b - MinSelectionSize);
                break;
            case SelectionHandle.EdgeBottom:
                b = Math.Max(mouse.Y, t + MinSelectionSize);
                break;
            case SelectionHandle.EdgeLeft:
                l = Math.Min(mouse.X, r - MinSelectionSize);
                break;
            case SelectionHandle.EdgeRight:
                r = Math.Max(mouse.X, l + MinSelectionSize);
                break;
            default:
                return rect;
        }

        return new Int32Rect(
            (int)Math.Round(l), (int)Math.Round(t),
            (int)Math.Round(r - l), (int)Math.Round(b - t));
    }

    /// <summary>整体移动:按 dx/dy 平移并钳制在 bounds(虚拟屏幕)内。</summary>
    public static Int32Rect Move(Int32Rect rect, int dx, int dy, Int32Rect bounds)
    {
        int newX = Math.Max(bounds.X, Math.Min(bounds.X + bounds.Width - rect.Width, rect.X + dx));
        int newY = Math.Max(bounds.Y, Math.Min(bounds.Y + bounds.Height - rect.Height, rect.Y + dy));
        return new Int32Rect(newX, newY, rect.Width, rect.Height);
    }

    /// <summary>方向键微调:1px 步进(Shift 由调用方传 10),同样钳制在屏幕内。</summary>
    public static Int32Rect Nudge(Int32Rect rect, int dx, int dy, Int32Rect bounds) =>
        Move(rect, dx, dy, bounds);

    /// <summary>
    /// 计算选区外暗化遮罩的四块矩形(窗口逻辑坐标)。
    /// selection 为 null / 空 / 含 NaN·Inf 时视为无选区:整窗作为 top 遮罩,其余为空。
    /// 所有结果尺寸均有限且 ≥ 0,可直接用于 WPF 布局。
    /// </summary>
    public static (Rect Top, Rect Bottom, Rect Left, Rect Right) MaskRectangles(Rect window, Rect? selection)
    {
        var sel = selection ?? Rect.Empty;
        bool valid = !double.IsNaN(sel.X) && !double.IsPositiveInfinity(sel.X)
                     && !double.IsNaN(sel.Y) && !double.IsPositiveInfinity(sel.Y)
                     && !double.IsNaN(sel.Width) && sel.Width > 0
                     && !double.IsNaN(sel.Height) && sel.Height > 0;
        if (!valid)
        {
            return (window, Rect.Empty, Rect.Empty, Rect.Empty);
        }

        double sideHeight = Math.Max(0, sel.Height);
        return (
            new Rect(0, 0, window.Width, Math.Max(0, sel.Top)),
            new Rect(0, sel.Bottom, window.Width, Math.Max(0, window.Bottom - sel.Bottom)),
            new Rect(0, sel.Top, Math.Max(0, sel.Left), sideHeight),
            new Rect(sel.Right, sel.Top, Math.Max(0, window.Right - sel.Right), sideHeight));
    }

    /// <summary>
    /// 放大镜左上角位置(窗口逻辑坐标):默认右下偏移,放不下依次翻转左下/右上/左上,极端情况钳制在窗口内。
    /// </summary>
    public static Point MagnifierTopLeft(Point mouse, Size windowSize, Size magnifierSize)
    {
        double mw = magnifierSize.Width, mh = magnifierSize.Height;
        var candidates = new[]
        {
            new Point(mouse.X + MagnifierOffset, mouse.Y + MagnifierOffset),
            new Point(mouse.X - mw - MagnifierOffset, mouse.Y + MagnifierOffset),
            new Point(mouse.X + MagnifierOffset, mouse.Y - mh - MagnifierOffset),
            new Point(mouse.X - mw - MagnifierOffset, mouse.Y - mh - MagnifierOffset),
        };
        foreach (var p in candidates)
        {
            if (p.X >= 0 && p.Y >= 0 && p.X + mw <= windowSize.Width && p.Y + mh <= windowSize.Height)
            {
                return p;
            }
        }
        return new Point(
            Math.Max(0, Math.Min(windowSize.Width - mw, mouse.X)),
            Math.Max(0, Math.Min(windowSize.Height - mh, mouse.Y)));
    }
}
