using System.Windows;
using System.Windows.Media;
using EasySnipLite.Editor.Models;
using EasySnipLite.Editor.Tools;

namespace EasySnipLite.Tests;

/// <summary>M3 选择工具纯逻辑：命中测试（重叠取最上层）/ 选中 / 移动偏移。</summary>
public class SelectionToolTests
{
    private static readonly Color TestColor = Color.FromRgb(0xFF, 0x00, 0x00);

    private static AnnotationObject Rect(double x, double y, double w, double h) =>
        new RectangleObject(new Rect(x, y, w, h), TestColor, 2);

    // ---- HitTester ----

    [Fact]
    public void HitTest_PointInside_ReturnsObject()
    {
        var objects = new List<AnnotationObject> { Rect(0, 0, 100, 100) };

        Assert.Same(objects[0], HitTester.HitTest(objects, new Point(50, 50)));
    }

    [Fact]
    public void HitTest_PointOutside_ReturnsNull()
    {
        var objects = new List<AnnotationObject> { Rect(0, 0, 100, 100) };

        Assert.Null(HitTester.HitTest(objects, new Point(200, 200)));
    }

    [Fact]
    public void HitTest_Overlapping_ReturnsTopmost()
    {
        var bottom = Rect(0, 0, 100, 100);
        var top = Rect(10, 10, 100, 100);
        var objects = new List<AnnotationObject> { bottom, top };

        Assert.Same(top, HitTester.HitTest(objects, new Point(50, 50)));
    }

    [Fact]
    public void HitTest_EmptyList_ReturnsNull()
    {
        Assert.Null(HitTester.HitTest([], new Point(0, 0)));
    }

    // ---- SelectionTool ----

    [Fact]
    public void SelectionTool_DownOnObject_SelectsIt()
    {
        var obj = Rect(0, 0, 100, 100);
        var tool = new SelectionTool(new List<AnnotationObject> { obj });

        tool.MouseDown(new Point(50, 50));

        Assert.Same(obj, tool.Selected);
        Assert.True(tool.IsActive);
    }

    [Fact]
    public void SelectionTool_DownOnEmpty_SelectsNothing()
    {
        var obj = Rect(0, 0, 100, 100);
        var tool = new SelectionTool(new List<AnnotationObject> { obj });

        tool.MouseDown(new Point(500, 500));

        Assert.Null(tool.Selected);
    }

    [Fact]
    public void SelectionTool_Drag_ReportsDeltaAndMoved()
    {
        var obj = Rect(0, 0, 100, 100);
        var tool = new SelectionTool(new List<AnnotationObject> { obj });

        tool.MouseDown(new Point(50, 50));
        tool.MouseMove(new Point(80, 30));

        Assert.Equal(30, tool.DeltaX);
        Assert.Equal(-20, tool.DeltaY);
        Assert.True(tool.Moved);

        tool.MouseUp(new Point(80, 30));
        Assert.False(tool.IsActive);
    }

    [Fact]
    public void SelectionTool_ClickWithoutDrag_NotMoved()
    {
        var obj = Rect(0, 0, 100, 100);
        var tool = new SelectionTool(new List<AnnotationObject> { obj });

        tool.MouseDown(new Point(50, 50));
        tool.MouseUp(new Point(50, 50));

        Assert.False(tool.Moved);
        Assert.Equal(0, tool.DeltaX);
    }

    // ---- 实时预览（issue #23）：选中对象移动时暴露预览与偏移 ----

    [Fact]
    public void SelectionTool_Drag_ExposesPreviewAndDelta()
    {
        var obj = Rect(0, 0, 100, 100);
        var tool = new SelectionTool(new List<AnnotationObject> { obj });

        tool.MouseDown(new Point(50, 50));
        Assert.Same(obj, tool.Preview);

        tool.MouseMove(new Point(80, 30));
        Assert.Same(obj, tool.Preview);
        Assert.Equal(30, tool.DeltaX);
        Assert.Equal(-20, tool.DeltaY);

        tool.MouseUp(new Point(80, 30));
        Assert.Null(tool.Preview);
    }

    [Fact]
    public void SelectionTool_DownOnEmpty_PreviewNull()
    {
        var obj = Rect(0, 0, 100, 100);
        var tool = new SelectionTool(new List<AnnotationObject> { obj });

        tool.MouseDown(new Point(500, 500));

        Assert.Null(tool.Preview);
    }
}
