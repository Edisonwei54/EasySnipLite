using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasySnipLite.Editor.Models;

/// <summary>文字标注：Bounds 由工具创建时用 FormattedText 度量确定。</summary>
public sealed class TextObject : AnnotationObject
{
    public string Text { get; set; }
    public double FontSize { get; set; }

    public TextObject(Rect bounds, string text, double fontSize, Color color)
    {
        Bounds = bounds;
        Text = text;
        FontSize = fontSize;
        Color = color;
    }

    public override AnnotationObject Clone() =>
        new TextObject(Bounds, Text, FontSize, Color) { StrokeWidth = StrokeWidth };

    public override void Render(DrawingContext dc)
    {
        var ft = new FormattedText(
            Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), FontSize,
            new SolidColorBrush(Color), 1.0);
        dc.DrawText(ft, new Point(Bounds.X, Bounds.Y));
    }
}

/// <summary>
/// 表情贴纸：Segoe UI Emoji 彩色渲染，首次 Render 时经 TextBlock + RenderTargetBitmap
/// 转位图并缓存（Win10 .NET Core WPF 支持彩色 emoji）。
/// </summary>
public sealed class EmojiObject : AnnotationObject
{
    public string Emoji { get; set; }
    public double FontSize { get; set; }

    private ImageSource? _cache;

    public EmojiObject(Rect bounds, string emoji, double fontSize)
    {
        Bounds = bounds;
        Emoji = emoji;
        FontSize = fontSize;
    }

    public override AnnotationObject Clone() =>
        new EmojiObject(Bounds, Emoji, FontSize) { Color = Color, StrokeWidth = StrokeWidth };

    public override void Render(DrawingContext dc)
    {
        _cache ??= RenderEmoji();
        dc.DrawImage(_cache, Bounds);
    }

    private ImageSource RenderEmoji()
    {
        var tb = new System.Windows.Controls.TextBlock
        {
            Text = Emoji,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = FontSize * 2, // 2x 采样再缩小，避免低 DPI 模糊
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        tb.Arrange(new Rect(tb.DesiredSize));
        var rtb = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(tb.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(tb.ActualHeight)),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(tb);
        return rtb;
    }
}
