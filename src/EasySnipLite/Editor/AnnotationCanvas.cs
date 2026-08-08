using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Editor.Models;

namespace EasySnipLite.Editor;

/// <summary>
/// 编辑器渲染层：底图 → 标注对象（矢量）→ 选中装饰（虚线边框）。
/// 对象/选中变化时由外部调用 InvalidateVisual 触发重绘。
/// </summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    public BitmapSource? Image { get; set; }
    public IList<AnnotationObject>? Objects { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Image is not null)
            dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));

        if (Objects is null) return;
        foreach (var obj in Objects) obj.Render(dc);

        foreach (var obj in Objects.Where(o => o.IsSelected))
        {
            var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
            var r = Rect.Inflate(obj.Bounds, 2, 2);
            dc.DrawRectangle(null, pen, r);
        }
    }
}

/// <summary>表情分类目录（Segoe UI Emoji 文本，零额外资源）。</summary>
public static class EmojiCatalog
{
    public static readonly (string Category, string[] Emojis)[] Categories =
    [
        ("笑脸", ["😀", "😁", "😂", "🤣", "😊", "😍", "😘", "😎", "🤔", "😴", "😭", "😡", "😱", "🤩"]),
        ("手势", ["👍", "👎", "👌", "🙏", "👏", "💪", "🤝", "✌️", "👋", "🤙", "🤞", "✊"]),
        ("动物", ["🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼", "🐨", "🐯", "🦁", "🐮", "🐷", "🐸"]),
        ("食物", ["🍎", "🍊", "🍋", "🍉", "🍇", "🍓", "🍑", "🥭", "🍍", "🥥", "🍔", "🍕", "🍟", "🍰"]),
        ("物品", ["⚽", "🏀", "🎮", "🎨", "🎵", "🎁", "🚀", "⭐", "❤️", "💰", "🔥", "💯", "✅", "❌"]),
    ];
}
