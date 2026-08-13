using System.Windows;
using System.Windows.Media;
using EasySnipLite.Editor.Models;
using EasySnipLite.Editor.Tools;

namespace EasySnipLite.Tests;

/// <summary>M3 工具交互状态机纯逻辑：按下-拖动-完成 → 标注对象。</summary>
public class AnnotationToolTests
{
    private static readonly Color TestColor = Color.FromRgb(0xFF, 0x00, 0x00);

    private static AnnotationObject? Drag(IAnnotationTool tool, params Point[] points)
    {
        tool.MouseDown(points[0]);
        foreach (var p in points.Skip(1).Take(points.Length - 2)) tool.MouseMove(p);
        tool.MouseUp(points[^1]);
        return tool.TakeResult();
    }

    // ---- Rectangle / Ellipse ----

    [Fact]
    public void RectangleTool_ForwardDrag_CreatesNormalizedRect()
    {
        var obj = (RectangleObject)Drag(new RectangleTool(TestColor, 2),
            new Point(10, 20), new Point(100, 80))!;

        Assert.Equal(new Rect(10, 20, 90, 60), obj.Bounds);
        Assert.Equal(TestColor, obj.Color);
    }

    [Fact]
    public void RectangleTool_ReverseDrag_NormalizesCorners()
    {
        var obj = (RectangleObject)Drag(new RectangleTool(TestColor, 2),
            new Point(100, 80), new Point(10, 20))!;

        Assert.Equal(new Rect(10, 20, 90, 60), obj.Bounds);
    }

    [Fact]
    public void RectangleTool_TinyDrag_EnforcesMinSize()
    {
        var obj = (RectangleObject)Drag(new RectangleTool(TestColor, 2),
            new Point(50, 50), new Point(51, 51))!;

        Assert.Equal(3, obj.Bounds.Width);
        Assert.Equal(3, obj.Bounds.Height);
    }

    [Fact]
    public void EllipseTool_Drag_CreatesEllipse()
    {
        var obj = (EllipseObject)Drag(new EllipseTool(TestColor, 3),
            new Point(0, 0), new Point(80, 60))!;

        Assert.Equal(new Rect(0, 0, 80, 60), obj.Bounds);
        Assert.Equal(3, obj.StrokeWidth);
    }

    // ---- Arrow ----

    [Fact]
    public void ArrowTool_Drag_CreatesArrowWithDirection()
    {
        var obj = (ArrowObject)Drag(new ArrowTool(TestColor, 2),
            new Point(10, 10), new Point(100, 90))!;

        Assert.Equal(new Point(10, 10), obj.Start);
        Assert.Equal(new Point(100, 90), obj.End);
    }

    // ---- Freehand / Highlighter ----

    [Fact]
    public void FreehandTool_Drag_CollectsPoints()
    {
        var obj = (FreehandObject)Drag(new FreehandTool(TestColor, 3),
            new Point(0, 0), new Point(10, 10), new Point(20, 30))!;

        Assert.Equal(3, obj.Points.Length);
        Assert.Equal(new Point(0, 0), obj.Points[0]);
        Assert.Equal(new Point(20, 30), obj.Points[^1]);
    }

    [Fact]
    public void FreehandTool_CloseMoves_DeduplicatedByMinDistance()
    {
        var tool = new FreehandTool(TestColor, 3);
        tool.MouseDown(new Point(0, 0));

        // 连续 5 个同点（含亚像素抖动）→ 只保留第一个
        tool.MouseMove(new Point(0.3, 0.1));
        tool.MouseMove(new Point(0.6, 0.2));
        tool.MouseMove(new Point(0.9, 0.3));
        tool.MouseMove(new Point(10, 10)); // 远点 → 追加
        tool.MouseUp(new Point(10, 10));

        var obj = (FreehandObject)tool.TakeResult()!;
        Assert.Equal(2, obj.Points.Length);
    }

    [Fact]
    public void HighlighterTool_Drag_CreatesHighlighterWithStrokeWidth()
    {
        var obj = (HighlighterObject)Drag(new HighlighterTool(
            Color.FromArgb(0x55, 0xFF, 0xEB, 0x3B), 10),
            new Point(0, 0), new Point(100, 0))!;

        Assert.Equal(10, obj.StrokeWidth);
        Assert.Equal(2, obj.Points.Length);
    }

    // ---- Mosaic ----

    [Fact]
    public void MosaicTool_Drag_CreatesMosaicWithBlockSize()
    {
        var obj = (MosaicObject)Drag(new MosaicTool(16),
            new Point(10, 10), new Point(210, 110))!;

        Assert.Equal(new Rect(10, 10, 200, 100), obj.Bounds);
        Assert.Equal(16, obj.BlockSize);
    }

    // ---- 状态机生命周期 ----

    [Fact]
    public void Tool_IsActive_FollowsMouseTransitions()
    {
        var tool = new RectangleTool(TestColor, 2);
        Assert.False(tool.IsActive);

        tool.MouseDown(new Point(0, 0));
        Assert.True(tool.IsActive);

        tool.MouseMove(new Point(5, 5));
        Assert.True(tool.IsActive);

        tool.MouseUp(new Point(5, 5));
        Assert.False(tool.IsActive);
    }

    [Fact]
    public void Tool_MouseUpWithoutDown_ProducesNoResult()
    {
        var tool = new RectangleTool(TestColor, 2);

        tool.MouseUp(new Point(0, 0));

        Assert.Null(tool.TakeResult());
    }

    [Fact]
    public void Tool_TakeResult_ReturnsResultOnce()
    {
        var tool = new RectangleTool(TestColor, 2);
        tool.MouseDown(new Point(0, 0));
        tool.MouseUp(new Point(10, 10));

        Assert.NotNull(tool.TakeResult());
        Assert.Null(tool.TakeResult()); // 第二次取为空
    }

    // ---- Text / Emoji ----

    // ---- 实时预览（issue #23）：拖拽中 Preview 持续更新，完成后清空 ----

    [Fact]
    public void RectangleTool_Move_ExposesLivePreview()
    {
        var tool = new RectangleTool(TestColor, 2);
        tool.MouseDown(new Point(10, 20));
        Assert.Null(tool.Preview);

        tool.MouseMove(new Point(100, 80));

        var preview = Assert.IsType<RectangleObject>(tool.Preview);
        Assert.Equal(new Rect(10, 20, 90, 60), preview.Bounds);
        Assert.Equal(TestColor, preview.Color);

        tool.MouseUp(new Point(100, 80));
        Assert.Null(tool.Preview);
    }

    [Fact]
    public void FreehandTool_Move_PreviewTracksPoints()
    {
        var tool = new FreehandTool(TestColor, 2);
        tool.MouseDown(new Point(0, 0));
        tool.MouseMove(new Point(5, 5));
        tool.MouseMove(new Point(10, 10));

        var preview = Assert.IsType<FreehandObject>(tool.Preview);
        Assert.Equal(3, preview.Points.Length);

        tool.MouseUp(new Point(15, 15));
        Assert.Null(tool.Preview);
    }

    [Fact]
    public void ArrowTool_Move_PreviewEndsAtCursor()
    {
        var tool = new ArrowTool(TestColor, 2);
        tool.MouseDown(new Point(0, 0));
        tool.MouseMove(new Point(40, 30));

        var preview = Assert.IsType<ArrowObject>(tool.Preview);
        Assert.Equal(new Point(40, 30), preview.End);

        tool.MouseUp(new Point(40, 30));
        Assert.Null(tool.Preview);
    }

    [Fact]
    public void TextTool_Preview_AlwaysNull()
    {
        var tool = new TextTool(TestColor, 24);

        Assert.Null(tool.Preview);
    }

    [Fact]
    public void TextTool_Create_MakesTextObjectAtPoint()
    {
        var tool = new TextTool(TestColor, 24);

        var obj = (TextObject)tool.Create(new Point(50, 60), "Hello");

        Assert.Equal("Hello", obj.Text);
        Assert.Equal(24, obj.FontSize);
        Assert.True(obj.Bounds.Width > 0); // 度量出的尺寸
        Assert.True(obj.Bounds.Height > 0);
        Assert.Equal(new Point(50, 60), obj.Bounds.Location);
    }

    [Fact]
    public void TextTool_MouseUp_RaisesClickedEvent()
    {
        var tool = new TextTool(TestColor, 24);
        Point? clicked = null;
        tool.Clicked += p => clicked = p;

        tool.MouseDown(new Point(5, 6));
        tool.MouseUp(new Point(5, 6));

        Assert.Equal(new Point(5, 6), clicked);
    }

    [Fact]
    public void EmojiTool_Create_MakesEmojiObjectAtPoint()
    {
        var tool = new EmojiTool(32);

        var obj = (EmojiObject)tool.Create(new Point(10, 20), "😀");

        Assert.Equal("😀", obj.Emoji);
        Assert.Equal(32, obj.FontSize);
        Assert.Equal(new Point(10, 20), obj.Bounds.Location);
        Assert.True(obj.Bounds.Width > 0);
    }

    // ---- TextMetrics ----

    [Fact]
    public void TextMetrics_Measure_ReturnsPositiveSize()
    {
        var size = TextMetrics.Measure("Hello", 24);

        Assert.True(size.Width > 0);
        Assert.True(size.Height > 0);
    }

    [Fact]
    public void TextMetrics_Measure_LongerTextIsWider()
    {
        var shortSize = TextMetrics.Measure("ab", 24);
        var longSize = TextMetrics.Measure("abcdefghijklmnop", 24);

        Assert.True(longSize.Width > shortSize.Width);
    }
}
