using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Editor.Models;

namespace EasySnipLite.Editor;

/// <summary>
/// 编辑器渲染层：底图 → 标注对象（矢量）→ 实时预览（拖拽中）→ 选中装饰（虚线边框）。
/// 对象/选中变化时由外部调用 InvalidateVisual 触发重绘。
/// </summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    public BitmapSource? Image { get; set; }
    public IList<AnnotationObject>? Objects { get; set; }

    /// <summary>实时预览对象（issue #23）：与 Objects 中同一实例时按偏移平移渲染，避免原位重影。</summary>
    public AnnotationObject? Preview { get; set; }

    /// <summary>预览对象平移量（选择/移动工具拖拽中的累计偏移）。</summary>
    public Vector PreviewOffset { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Image is not null)
            dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));

        if (Objects is null) return;
        foreach (var obj in Objects)
        {
            // 预览与对象为同一实例（选中移动）时跳过原位，只画偏移预览
            if (Preview is not null && ReferenceEquals(obj, Preview)) continue;
            obj.Render(dc);
        }

        if (Preview is not null)
        {
            dc.PushTransform(new TranslateTransform(PreviewOffset.X, PreviewOffset.Y));
            Preview.Render(dc);
            if (Preview.IsSelected)
            {
                var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
                dc.DrawRectangle(null, pen, Rect.Inflate(Preview.Bounds, 2, 2));
            }
            dc.Pop();
        }

        foreach (var obj in Objects.Where(o => o.IsSelected && !ReferenceEquals(o, Preview)))
        {
            var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
            var r = Rect.Inflate(obj.Bounds, 2, 2);
            dc.DrawRectangle(null, pen, r);
        }
    }
}

/// <summary>表情分类目录（Segoe UI Emoji 文本，零额外资源）。分类名由资源提供。</summary>
public static class EmojiCatalog
{
    public static readonly string[][] Categories =
    [
        ["😀", "😁", "😂", "🤣", "😊", "😍", "😘", "😎", "🤔", "😴", "😭", "😡", "😱", "🤩"],
        ["👍", "👎", "👌", "🙏", "👏", "💪", "🤝", "✌️", "👋", "🤙", "🤞", "✊"],
        ["🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼", "🐨", "🐯", "🦁", "🐮", "🐷", "🐸"],
        ["🍎", "🍊", "🍋", "🍉", "🍇", "🍓", "🍑", "🥭", "🍍", "🥥", "🍔", "🍕", "🍟", "🍰"],
        ["⚽", "🏀", "🎮", "🎨", "🎵", "🎁", "🚀", "⭐", "❤️", "💰", "🔥", "💯", "✅", "❌"],
    ];
}
