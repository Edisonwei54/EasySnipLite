using EasySnipLite.Editor.Models;

namespace EasySnipLite.Editor.Tools;

/// <summary>矩形工具：拖拽生成 RectangleObject。</summary>
public sealed class RectangleTool(Color color, double strokeWidth) : DragToolBase
{
    protected override AnnotationObject CreateObject(Rect rect) =>
        new RectangleObject(rect, color, strokeWidth);
}

/// <summary>椭圆工具：拖拽生成 EllipseObject。</summary>
public sealed class EllipseTool(Color color, double strokeWidth) : DragToolBase
{
    protected override AnnotationObject CreateObject(Rect rect) =>
        new EllipseObject(rect, color, strokeWidth);
}

/// <summary>马赛克工具：拖拽生成 MosaicObject（块大小默认 16px）。</summary>
public sealed class MosaicTool(int blockSize = 16) : DragToolBase
{
    protected override AnnotationObject CreateObject(Rect rect) =>
        new MosaicObject(rect, blockSize);
}

/// <summary>箭头工具：起点→终点，保留方向（不规范化）。</summary>
public sealed class ArrowTool(Color color, double strokeWidth) : IAnnotationTool
{
    private Point _start;
    private AnnotationObject? _result;

    public bool IsActive { get; private set; }

    public void MouseDown(Point p)
    {
        _start = p;
        IsActive = true;
        _result = null;
    }

    public void MouseMove(Point p)
    {
    }

    public void MouseUp(Point p)
    {
        if (!IsActive) return;
        IsActive = false;
        _result = new ArrowObject(_start, p, color, strokeWidth);
    }

    public AnnotationObject? TakeResult()
    {
        var r = _result;
        _result = null;
        return r;
    }
}

/// <summary>自由画笔：按最小采样距离收集点集，生成 FreehandObject。</summary>
public class FreehandTool : IAnnotationTool
{
    /// <summary>相邻采样点最小距离（px），低于此距离丢弃，避免抖动产生密集重复点。</summary>
    public const double MinSampleDistance = 1;

    private readonly List<Point> _points = [];
    private AnnotationObject? _result;

    /// <summary>已采样点（派生类创建对象时读取）。</summary>
    protected Point[] Points => [.. _points];

    protected Color Color { get; }
    protected double StrokeWidth { get; }

    public FreehandTool(Color color, double strokeWidth)
    {
        Color = color;
        StrokeWidth = strokeWidth;
    }

    public bool IsActive { get; private set; }

    public void MouseDown(Point p)
    {
        _points.Clear();
        _points.Add(p);
        IsActive = true;
        _result = null;
    }

    public void MouseMove(Point p)
    {
        if (!IsActive) return;
        var last = _points[^1];
        var dx = p.X - last.X;
        var dy = p.Y - last.Y;
        if (dx * dx + dy * dy >= MinSampleDistance * MinSampleDistance)
            _points.Add(p);
    }

    public void MouseUp(Point p)
    {
        if (!IsActive) return;
        MouseMove(p); // 终点采样（IsActive 仍为 true；与上一点过近则跳过）
        IsActive = false;
        _result = CreateObject();
    }

    public AnnotationObject? TakeResult()
    {
        var r = _result;
        _result = null;
        return r;
    }

    protected virtual AnnotationObject CreateObject() =>
        new FreehandObject([.. _points], Color, StrokeWidth);
}

/// <summary>荧光笔：粗半透明折线。</summary>
public sealed class HighlighterTool(Color color, double strokeWidth) : FreehandTool(color, strokeWidth)
{
    protected override AnnotationObject CreateObject() =>
        new HighlighterObject([.. Points], Color, StrokeWidth);
}
