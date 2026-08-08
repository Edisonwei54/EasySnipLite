using System.Globalization;
using System.Windows.Media;

namespace EasySnipLite.Editor.Models;

/// <summary>编辑器激活的工具。Selection 为选择/移动模式（不产出对象），其余与派生类一一对应。</summary>
public enum AnnotationTool
{
    Selection,
    Rectangle,
    Ellipse,
    Arrow,
    Freehand,
    Highlighter,
    Mosaic,
    Text,
    Emoji,
}

/// <summary>
/// 标注对象抽象基类。坐标体系：编辑器画布物理像素（与选区/截图一致）。
/// Bounds 为对象外接矩形；派生类在几何变化时负责保持 Bounds 与内部数据同步。
/// </summary>
public abstract class AnnotationObject
{
    public Rect Bounds { get; set; }
    public Color Color { get; set; } = Colors.Red;
    public double StrokeWidth { get; set; } = 2;
    public bool IsSelected { get; set; }

    public abstract AnnotationObject Clone();
    public abstract void Render(DrawingContext dc);

    /// <summary>整体平移（默认只移动 Bounds，点集类派生覆盖）。</summary>
    public virtual void Offset(double dx, double dy) =>
        Bounds = new Rect(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height);
}

/// <summary>矩形框。</summary>
public sealed class RectangleObject : AnnotationObject
{
    public RectangleObject(Rect bounds, Color color, double strokeWidth)
    {
        Bounds = bounds;
        Color = color;
        StrokeWidth = strokeWidth;
    }

    public override AnnotationObject Clone() => new RectangleObject(Bounds, Color, StrokeWidth);

    public override void Render(DrawingContext dc)
    {
        var brush = new SolidColorBrush(Color);
        dc.DrawRectangle(null, new Pen(brush, StrokeWidth), Bounds);
    }
}

/// <summary>椭圆框。</summary>
public sealed class EllipseObject : AnnotationObject
{
    public EllipseObject(Rect bounds, Color color, double strokeWidth)
    {
        Bounds = bounds;
        Color = color;
        StrokeWidth = strokeWidth;
    }

    public override AnnotationObject Clone() => new EllipseObject(Bounds, Color, StrokeWidth);

    public override void Render(DrawingContext dc)
    {
        var brush = new SolidColorBrush(Color);
        dc.DrawEllipse(null, new Pen(brush, StrokeWidth), new Point(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2), Bounds.Width / 2, Bounds.Height / 2);
    }
}

/// <summary>带箭头的直线。Bounds 覆盖线段与头部三角（含线宽外扩）。</summary>
public sealed class ArrowObject : AnnotationObject
{
    public const double HeadLength = 10;

    public Point Start { get; set; }
    public Point End { get; set; }

    public ArrowObject(Point start, Point end, Color color, double strokeWidth)
    {
        Start = start;
        End = end;
        Color = color;
        StrokeWidth = strokeWidth;
        RecomputeBounds();
    }

    public override AnnotationObject Clone() =>
        new ArrowObject(Start, End, Color, StrokeWidth);

    public override void Offset(double dx, double dy)
    {
        Start = new Point(Start.X + dx, Start.Y + dy);
        End = new Point(End.X + dx, End.Y + dy);
        RecomputeBounds();
    }

    private void RecomputeBounds()
    {
        var half = StrokeWidth / 2;
        var minX = Math.Min(Start.X, End.X) - half;
        var minY = Math.Min(Start.Y, End.Y) - half;
        var maxX = Math.Max(Start.X, End.X) + half;
        var maxY = Math.Max(Start.Y, End.Y) + half;

        var (ux, uy) = Unit(Start, End);
        if (ux != 0 || uy != 0)
        {
            // 头部三角形三个顶点（顶点 + 两翼）纳入外接矩形
            var tip = new Point(End.X + ux * HeadLength, End.Y + uy * HeadLength);
            var wing1 = new Point(End.X + ux * HeadLength * 0.7 - uy * HeadLength * 0.7,
                                  End.Y + uy * HeadLength * 0.7 + ux * HeadLength * 0.7);
            var wing2 = new Point(End.X + ux * HeadLength * 0.7 + uy * HeadLength * 0.7,
                                  End.Y + uy * HeadLength * 0.7 - ux * HeadLength * 0.7);
            minX = Math.Min(minX, Math.Min(tip.X, Math.Min(wing1.X, wing2.X)));
            minY = Math.Min(minY, Math.Min(tip.Y, Math.Min(wing1.Y, wing2.Y)));
            maxX = Math.Max(maxX, Math.Max(tip.X, Math.Max(wing1.X, wing2.X)));
            maxY = Math.Max(maxY, Math.Max(tip.Y, Math.Max(wing1.Y, wing2.Y)));
        }

        Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static (double ux, double uy) Unit(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len <= 0 ? (0, 0) : (dx / len, dy / len);
    }

    public override void Render(DrawingContext dc)
    {
        var brush = new SolidColorBrush(Color);
        var pen = new Pen(brush, StrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        dc.DrawLine(pen, Start, End);

        var (ux, uy) = Unit(Start, End);
        if (ux == 0 && uy == 0) return;

        var tip = new Point(End.X + ux * HeadLength, End.Y + uy * HeadLength);
        var wing1 = new Point(End.X + ux * HeadLength * 0.7 - uy * HeadLength * 0.7,
                              End.Y + uy * HeadLength * 0.7 + ux * HeadLength * 0.7);
        var wing2 = new Point(End.X + ux * HeadLength * 0.7 + uy * HeadLength * 0.7,
                              End.Y + uy * HeadLength * 0.7 - ux * HeadLength * 0.7);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(End, false, false);
            ctx.LineTo(tip, true, false);
            ctx.LineTo(wing1, true, false);
            ctx.BeginFigure(End, false, false);
            ctx.LineTo(wing2, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>自由画笔：折线点集，Bounds 随点集与线宽。</summary>
public class FreehandObject : AnnotationObject
{
    public Point[] Points { get; private set; }

    public FreehandObject(Point[] points, Color color, double strokeWidth)
    {
        Points = points;
        Color = color;
        StrokeWidth = strokeWidth;
        RecomputeBounds();
    }

    public override AnnotationObject Clone() =>
        new FreehandObject([.. Points], Color, StrokeWidth);

    public override void Offset(double dx, double dy)
    {
        for (var i = 0; i < Points.Length; i++)
            Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy);
        RecomputeBounds();
    }

    protected void RecomputeBounds()
    {
        if (Points.Length == 0)
        {
            Bounds = Rect.Empty;
            return;
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var p in Points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        var half = StrokeWidth / 2;
        Bounds = new Rect(minX - half, minY - half, maxX - minX + StrokeWidth, maxY - minY + StrokeWidth);
    }

    public override void Render(DrawingContext dc)
    {
        if (Points.Length < 2) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Points[0], false, false);
            for (var i = 1; i < Points.Length; i++)
                ctx.LineTo(Points[i], true, false);
        }
        geometry.Freeze();

        var brush = new SolidColorBrush(Color);
        var pen = new Pen(brush, StrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>荧光笔：粗半透明折线，结构与 Freehand 相同，语义区分。</summary>
public sealed class HighlighterObject(Point[] points, Color color, double strokeWidth)
    : FreehandObject(points, color, strokeWidth)
{
    public override AnnotationObject Clone() =>
        new HighlighterObject([.. Points], Color, StrokeWidth);
}
