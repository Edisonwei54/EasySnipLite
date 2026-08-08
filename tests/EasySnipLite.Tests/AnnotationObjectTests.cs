using System.Windows;
using System.Windows.Media;
using EasySnipLite.Editor.Models;

namespace EasySnipLite.Tests;

/// <summary>M3 标注对象模型纯逻辑：Bounds 计算 / Offset 移动 / Clone 深拷贝。</summary>
public class AnnotationObjectTests
{
    private static readonly Color TestColor = Color.FromRgb(0xFF, 0x00, 0x00);

    // ---- Rectangle ----

    [Fact]
    public void RectangleObject_Bounds_StoresRect()
    {
        var obj = new RectangleObject(new Rect(10, 20, 100, 50), TestColor, 2);

        Assert.Equal(new Rect(10, 20, 100, 50), obj.Bounds);
        Assert.Equal(TestColor, obj.Color);
    }

    [Fact]
    public void RectangleObject_Offset_MovesBounds()
    {
        var obj = new RectangleObject(new Rect(10, 20, 100, 50), TestColor, 2);

        obj.Offset(5, -3);

        Assert.Equal(new Rect(15, 17, 100, 50), obj.Bounds);
    }

    [Fact]
    public void RectangleObject_Clone_IsIndependent()
    {
        var obj = new RectangleObject(new Rect(10, 20, 100, 50), TestColor, 2);

        var clone = (RectangleObject)obj.Clone();
        clone.Bounds = new Rect(0, 0, 1, 1);
        clone.Color = Colors.Blue;

        Assert.Equal(new Rect(10, 20, 100, 50), obj.Bounds);
        Assert.Equal(TestColor, obj.Color);
    }

    // ---- Ellipse ----

    [Fact]
    public void EllipseObject_Clone_IsIndependent()
    {
        var obj = new EllipseObject(new Rect(0, 0, 80, 60), TestColor, 3);

        var clone = (EllipseObject)obj.Clone();
        clone.StrokeWidth = 10;

        Assert.Equal(3, obj.StrokeWidth);
        Assert.Equal(10, clone.StrokeWidth);
    }

    // ---- Arrow ----

    [Fact]
    public void ArrowObject_Bounds_CoversStartAndEnd_WithStrokeWidthPadding()
    {
        // 45° 斜线，线宽 4 → 外扩 2；起点侧无头部，Left/Top 精确
        var obj = new ArrowObject(new Point(10, 10), new Point(100, 90), TestColor, 4);

        Assert.True(obj.Bounds.Contains(new Point(10, 10)));
        Assert.True(obj.Bounds.Contains(new Point(100, 90)));
        Assert.Equal(8, obj.Bounds.Left);      // 10 - 2
        Assert.Equal(8, obj.Bounds.Top);       // 10 - 2
        Assert.True(obj.Bounds.Right >= 102);  // ≥ 100 + 2（头部延伸只会更大）
        Assert.True(obj.Bounds.Bottom >= 92);  // ≥ 90 + 2
        Assert.True(obj.Bounds.Right <= 102 + ArrowObject.HeadLength + 2); // 头部有界
    }

    [Fact]
    public void ArrowObject_Bounds_IncludesHead()
    {
        // 水平箭头 → 头部在终点右侧延伸
        var obj = new ArrowObject(new Point(10, 50), new Point(100, 50), TestColor, 2);

        Assert.True(obj.Bounds.Right >= 100 + ArrowObject.HeadLength);
    }

    [Fact]
    public void ArrowObject_Offset_MovesStartAndEnd()
    {
        var obj = new ArrowObject(new Point(10, 10), new Point(100, 90), TestColor, 2);

        obj.Offset(50, 0);

        Assert.Equal(new Point(60, 10), obj.Start);
        Assert.Equal(new Point(150, 90), obj.End);
    }

    [Fact]
    public void ArrowObject_Clone_IsIndependent()
    {
        var obj = new ArrowObject(new Point(10, 10), new Point(100, 90), TestColor, 2);

        var clone = (ArrowObject)obj.Clone();
        clone.End = new Point(0, 0);

        Assert.Equal(new Point(100, 90), obj.End);
    }

    // ---- Freehand ----

    private static FreehandObject Freehand(params Point[] points) =>
        new(points, TestColor, 3);

    [Fact]
    public void FreehandObject_Bounds_CoversAllPoints_WithStrokeWidthPadding()
    {
        var obj = Freehand(new Point(10, 20), new Point(50, 60), new Point(100, 30));

        Assert.Equal(8.5, obj.Bounds.Left);   // 10 - 1.5
        Assert.Equal(18.5, obj.Bounds.Top);   // 20 - 1.5
        Assert.Equal(101.5, obj.Bounds.Right);  // 100 + 1.5
        Assert.Equal(61.5, obj.Bounds.Bottom);  // 60 + 1.5
    }

    [Fact]
    public void FreehandObject_Bounds_SinglePoint_IsLineWidth()
    {
        var obj = Freehand(new Point(50, 50));

        Assert.Equal(3, obj.Bounds.Width);  // 线宽
        Assert.Equal(3, obj.Bounds.Height);
    }

    [Fact]
    public void FreehandObject_Clone_DeepCopiesPoints()
    {
        var obj = Freehand(new Point(0, 0), new Point(10, 10));

        var clone = (FreehandObject)obj.Clone();
        clone.Points[0] = new Point(999, 999);

        Assert.Equal(new Point(0, 0), obj.Points[0]);
        Assert.NotSame(obj.Points, clone.Points);
    }

    [Fact]
    public void FreehandObject_Offset_MovesPointsAndBounds()
    {
        var obj = Freehand(new Point(10, 10), new Point(20, 30));

        obj.Offset(100, 50);

        Assert.Equal(new Point(110, 60), obj.Points[0]);
        Assert.Equal(new Point(120, 80), obj.Points[1]);
        Assert.Equal(108.5, obj.Bounds.Left, 2); // 110 - 线宽/2
        Assert.Equal(58.5, obj.Bounds.Top, 2);   // 60 - 线宽/2
    }

    // ---- Highlighter ----

    [Fact]
    public void HighlighterObject_Default_IsSemiTransparent()
    {
        var obj = new HighlighterObject(
            [new Point(0, 0), new Point(100, 0)],
            Color.FromArgb(0x55, 0xFF, 0xEB, 0x3B), 10);

        Assert.Equal(10, obj.StrokeWidth);
        Assert.Equal(0x55, obj.Color.A); // 半透明
    }

    [Fact]
    public void HighlighterObject_Clone_DeepCopiesPoints()
    {
        var obj = new HighlighterObject(
            [new Point(0, 0), new Point(100, 0)],
            Color.FromArgb(0x55, 0xFF, 0xEB, 0x3B), 10);

        var clone = (HighlighterObject)obj.Clone();
        clone.Points[1] = new Point(0, 0);

        Assert.Equal(new Point(100, 0), obj.Points[1]);
    }

    // ---- Mosaic ----

    [Fact]
    public void MosaicObject_StoresBoundsAndBlockSize()
    {
        var obj = new MosaicObject(new Rect(0, 0, 200, 100), 16);

        Assert.Equal(new Rect(0, 0, 200, 100), obj.Bounds);
        Assert.Equal(16, obj.BlockSize);
    }

    [Fact]
    public void MosaicObject_Clone_IsIndependent()
    {
        var obj = new MosaicObject(new Rect(0, 0, 200, 100), 16);

        var clone = (MosaicObject)obj.Clone();
        clone.Bounds = new Rect(50, 50, 10, 10);
        clone.BlockSize = 8;

        Assert.Equal(new Rect(0, 0, 200, 100), obj.Bounds);
        Assert.Equal(16, obj.BlockSize);
    }

    // ---- Text / Emoji ----

    [Fact]
    public void TextObject_Clone_IsIndependent()
    {
        var obj = new TextObject(new Rect(10, 10, 120, 30), "Hello", 24, TestColor);

        var clone = (TextObject)obj.Clone();
        clone.Text = "World";

        Assert.Equal("Hello", obj.Text);
        Assert.Equal(24, obj.FontSize);
    }

    [Fact]
    public void EmojiObject_Clone_IsIndependent()
    {
        var obj = new EmojiObject(new Rect(10, 10, 32, 32), "😀", 32);

        var clone = (EmojiObject)obj.Clone();
        clone.Emoji = "👍";

        Assert.Equal("😀", obj.Emoji);
        Assert.Equal(32, obj.FontSize);
    }

    [Fact]
    public void AllObjects_Offset_MovesBounds()
    {
        // Arrow 的 Bounds 因头部方向不精确等于位移量，单独测试（ArrowObject_Offset_MovesStartAndEnd）
        AnnotationObject[] objs =
        [
            new RectangleObject(new Rect(0, 0, 10, 10), TestColor, 2),
            new EllipseObject(new Rect(0, 0, 10, 10), TestColor, 2),
            new MosaicObject(new Rect(0, 0, 10, 10), 4),
            new TextObject(new Rect(0, 0, 10, 10), "t", 12, TestColor),
            new EmojiObject(new Rect(0, 0, 10, 10), "😀", 12),
        ];

        foreach (var obj in objs) obj.Offset(5, 5);

        Assert.All(objs, o => Assert.Equal(new Point(5, 5), o.Bounds.Location));
    }
}
