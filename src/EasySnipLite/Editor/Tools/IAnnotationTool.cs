using EasySnipLite.Editor.Models;

namespace EasySnipLite.Editor.Tools;

/// <summary>
/// 工具交互状态机：MouseDown/MouseMove/MouseUp 序列产生标注对象。
/// 拖拽型工具在 MouseUp 后通过 TakeResult 取回对象；点击型工具（文字/表情）通过 Clicked 事件请求附加输入。
/// </summary>
public interface IAnnotationTool
{
    void MouseDown(Point p);
    void MouseMove(Point p);
    void MouseUp(Point p);

    /// <summary>拖拽是否进行中（Editor 用于指针捕获与光标切换）。</summary>
    bool IsActive { get; }

    /// <summary>拖拽进行中的实时预览对象（未提交，仅供渲染）；空闲/未命中时为 null。</summary>
    AnnotationObject? Preview { get; }

    /// <summary>取走 MouseUp 产出的对象（仅一次有效，未完成时返回 null）。</summary>
    AnnotationObject? TakeResult();
}

/// <summary>拖拽型工具基类：按下点 + 当前位置 → 规范化矩形 → 派生类创建对象。</summary>
public abstract class DragToolBase : IAnnotationTool
{
    public const double MinSize = 3;

    protected Point Start;
    protected Point Current;

    private AnnotationObject? _result;

    public bool IsActive { get; private set; }

    /// <summary>实时预览：每次移动按当前矩形重建（issue #23），MouseUp 后清空。</summary>
    public AnnotationObject? Preview { get; private set; }

    protected abstract AnnotationObject CreateObject(Rect rect);

    public virtual void MouseDown(Point p)
    {
        Start = p;
        Current = p;
        IsActive = true;
        _result = null;
        Preview = null;
    }

    public virtual void MouseMove(Point p)
    {
        if (!IsActive) return;
        Current = p;
        Preview = CreateObject(Normalize(Start, Current));
    }

    public virtual void MouseUp(Point p)
    {
        if (!IsActive) return;
        Current = p;
        IsActive = false;
        _result = CreateObject(Normalize(Start, Current));
        Preview = null;
    }

    public AnnotationObject? TakeResult()
    {
        var r = _result;
        _result = null;
        return r;
    }

    /// <summary>规范化为左上-右下矩形，宽高钳制最小尺寸。</summary>
    public static Rect Normalize(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Max(Math.Abs(a.X - b.X), MinSize);
        var h = Math.Max(Math.Abs(a.Y - b.Y), MinSize);
        return new Rect(x, y, w, h);
    }
}
