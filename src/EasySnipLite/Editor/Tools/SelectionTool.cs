using EasySnipLite.Editor.Models;

namespace EasySnipLite.Editor.Tools;

/// <summary>命中测试纯函数：点是否落在对象 Bounds 内，重叠时取最上层（列表末尾）。</summary>
public static class HitTester
{
    public static AnnotationObject? HitTest(IList<AnnotationObject> objects, Point p)
    {
        for (var i = objects.Count - 1; i >= 0; i--)
        {
            if (objects[i].Bounds.Contains(p)) return objects[i];
        }
        return null;
    }
}

/// <summary>
/// 选择/移动工具：按下命中对象并选中，拖动记录偏移（DeltaX/DeltaY），
/// 不产出新对象——由 EditorViewModel 根据偏移把移动作为 Transform 命令入撤销栈。
/// </summary>
public sealed class SelectionTool(IList<AnnotationObject> objects) : IAnnotationTool
{
    private Point _start;

    public bool IsActive { get; private set; }

    /// <summary>按下的位置命中的对象（未命中时为 null）。</summary>
    public AnnotationObject? Selected { get; private set; }

    /// <summary>实时预览：拖拽中暴露被移动对象本体（渲染层按 Delta 平移，issue #23）。</summary>
    public AnnotationObject? Preview => IsActive ? Selected : null;

    /// <summary>相对按下点的累计偏移。</summary>
    public double DeltaX { get; private set; }
    public double DeltaY { get; private set; }

    /// <summary>是否发生了实际拖动（区分点击与移动）。</summary>
    public bool Moved { get; private set; }

    public void MouseDown(Point p)
    {
        _start = p;
        Selected = HitTester.HitTest(objects, p);
        DeltaX = 0;
        DeltaY = 0;
        Moved = false;
        IsActive = true;
    }

    public void MouseMove(Point p)
    {
        if (!IsActive || Selected is null) return;
        DeltaX = p.X - _start.X;
        DeltaY = p.Y - _start.Y;
        if (DeltaX != 0 || DeltaY != 0) Moved = true;
    }

    public void MouseUp(Point p) => IsActive = false;

    public AnnotationObject? TakeResult() => null;
}
