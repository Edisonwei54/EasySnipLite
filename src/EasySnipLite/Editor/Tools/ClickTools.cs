using System.Globalization;
using System.Windows.Media;
using EasySnipLite.Editor.Models;

namespace EasySnipLite.Editor.Tools;

/// <summary>文本度量纯函数：FormattedText 计算像素尺寸（pixelsPerDip=1.0，与布局尺寸一致）。</summary>
public static class TextMetrics
{
    public static Size Measure(string text, double fontSize, string fontFamily = "Microsoft YaHei UI")
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(fontFamily), fontSize, Brushes.Black, 1.0);
        return new Size(ft.Width, ft.Height);
    }
}

/// <summary>文字工具：点击位置触发 Clicked 事件，输入完成后由 Create 生成 TextObject（尺寸经 TextMetrics 度量）。</summary>
public sealed class TextTool(Color color, double fontSize) : IAnnotationTool
{
    private Point? _click;

    /// <summary>点击发生（Editor 应弹出文本输入）。</summary>
    public event Action<Point>? Clicked;

    public bool IsActive => false;

    public void MouseDown(Point p) => _click = p;

    public void MouseMove(Point p)
    {
    }

    public void MouseUp(Point p) => Clicked?.Invoke(_click ?? p);

    public AnnotationObject? TakeResult() => null;

    public AnnotationObject Create(Point topLeft, string text)
    {
        var size = TextMetrics.Measure(text, fontSize);
        return new TextObject(new Rect(topLeft.X, topLeft.Y, size.Width, size.Height), text, fontSize, color);
    }
}

/// <summary>表情工具：点击位置触发 Clicked 事件，选择后由 Create 生成 EmojiObject（尺寸经 Segoe UI Emoji 度量）。</summary>
public sealed class EmojiTool(double fontSize) : IAnnotationTool
{
    private Point? _click;

    /// <summary>点击发生（Editor 应弹出表情面板）。</summary>
    public event Action<Point>? Clicked;

    public bool IsActive => false;

    public void MouseDown(Point p) => _click = p;

    public void MouseMove(Point p)
    {
    }

    public void MouseUp(Point p) => Clicked?.Invoke(_click ?? p);

    public AnnotationObject? TakeResult() => null;

    public AnnotationObject Create(Point topLeft, string emoji)
    {
        var size = TextMetrics.Measure(emoji, fontSize, "Segoe UI Emoji");
        return new EmojiObject(new Rect(topLeft.X, topLeft.Y, size.Width, size.Height), emoji, fontSize);
    }
}
