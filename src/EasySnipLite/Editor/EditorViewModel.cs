using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Editor.Models;
using EasySnipLite.Editor.Tools;
using EasySnipLite.Editor.UndoRedo;

namespace EasySnipLite.Editor;

/// <summary>
/// 标注编辑器视图模型（无第三方 MVVM，手写 INPC + 事件）。
/// 职责：工具状态机协调（选择/移动入撤销栈、工具产出对象入撤销栈）、
/// 选中管理、撤销/重做/删除、复制/保存/完成（组合底图+对象渲染）。
/// </summary>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    public BitmapSource Image { get; private set; }
    public ObservableCollection<AnnotationObject> Objects { get; } = [];
    public UndoStack UndoStack { get; } = new();

    public Color StrokeColor { get; set; } = Colors.Red;
    public double StrokeWidth { get; set; } = 3;
    public double FontSize { get; set; } = 24;

    private AnnotationTool _activeTool = AnnotationTool.Selection;

    public AnnotationTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (_activeTool == value) return;
            _activeTool = value;
            OnPropertyChanged();
        }
    }

    private AnnotationObject? _selected;

    public AnnotationObject? Selected
    {
        get => _selected;
        private set
        {
            if (_selected == value) return;
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>画布需要重绘（对象/选中变化）。</summary>
    public event Action? RenderInvalidated;

    /// <summary>文字/表情工具点击后请求附加输入。</summary>
    public event Action<Point>? TextInputRequested;
    public event Action<Point>? EmojiInputRequested;

    private IAnnotationTool? _tool;

    public EditorViewModel(BitmapSource image)
    {
        Image = image;
    }

    // ---- 画布鼠标交互 ----

    public void OnMouseDown(Point p)
    {
        _tool = CreateTool();
        if (_tool is TextTool text) text.Clicked += p2 => TextInputRequested?.Invoke(p2);
        if (_tool is EmojiTool emoji) emoji.Clicked += p2 => EmojiInputRequested?.Invoke(p2);

        _tool.MouseDown(p);
        if (_tool is SelectionTool sel) Select(sel.Selected);
        Invalidate();
    }

    public void OnMouseMove(Point p)
    {
        if (_tool is null) return;
        _tool.MouseMove(p);
        // 选中移动过程中实时刷新
        if (_tool is SelectionTool { Selected: not null } sel && (sel.DeltaX != 0 || sel.DeltaY != 0))
            Invalidate();
    }

    public void OnMouseUp(Point p)
    {
        if (_tool is null) return;
        _tool.MouseUp(p);

        switch (_tool)
        {
            case SelectionTool sel when sel.Selected is { } obj && sel.Moved:
                // 整体移动 → Transform 命令（Push 即执行）
                var (dx, dy) = (sel.DeltaX, sel.DeltaY);
                UndoStack.Push(new TransformCommand(
                    () => obj.Offset(dx, dy),
                    () => obj.Offset(-dx, -dy)));
                break;

            case SelectionTool:
                break; // 点击未拖动：仅选中

            default:
                if (_tool.TakeResult() is { } created)
                {
                    if (created is MosaicObject mosaic) mosaic.SourceImage = Image;
                    UndoStack.Push(new AddObjectCommand<AnnotationObject>(Objects, created));
                    Select(created);
                }
                break;
        }

        _tool = null;
        Invalidate();
    }

    // ---- 文字/表情提交（输入面板完成回调） ----

    public void CommitText(Point topLeft, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var obj = new TextTool(StrokeColor, FontSize).Create(topLeft, text.Trim());
        UndoStack.Push(new AddObjectCommand<AnnotationObject>(Objects, obj));
        Select(obj);
        Invalidate();
    }

    public void CommitEmoji(Point topLeft, string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        var obj = new EmojiTool(FontSize).Create(topLeft, emoji);
        UndoStack.Push(new AddObjectCommand<AnnotationObject>(Objects, obj));
        Select(obj);
        Invalidate();
    }

    // ---- 编辑操作 ----

    public void Undo()
    {
        UndoStack.Undo();
        ClearSelection();
        Invalidate();
    }

    public void Redo()
    {
        UndoStack.Redo();
        ClearSelection();
        Invalidate();
    }

    public void DeleteSelected()
    {
        if (Selected is not { } obj) return;
        UndoStack.Push(new DeleteObjectCommand<AnnotationObject>(Objects, obj));
        ClearSelection();
        Invalidate();
    }

    public void ClearSelection()
    {
        foreach (var o in Objects) o.IsSelected = false;
        Selected = null;
        Invalidate();
    }

    // ---- 底图更换（issue #20 遮罩内联标注：选区调整后底图重组合） ----

    /// <summary>更换标注底图（选区调整后重组合）。马赛克对象同步刷新源图。</summary>
    public void SetBaseImage(BitmapSource image)
    {
        Image = image;
        foreach (var o in Objects)
        {
            if (o is MosaicObject mosaic) mosaic.SourceImage = image;
        }
        Invalidate();
    }

    /// <summary>整体清空标注（对象 + 撤销栈），选区本身保留。</summary>
    public void ClearAll()
    {
        Objects.Clear();
        UndoStack.Clear();
        ClearSelection();
    }

    // ---- 结果组合与输出 ----

    /// <summary>组合底图 + 全部对象渲染为导出位图（复制/保存用，96dpi 物理像素）。</summary>
    public BitmapSource Compose()
    {
        var width = Math.Max(1, Image.PixelWidth);
        var height = Math.Max(1, Image.PixelHeight);
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(Image, new Rect(0, 0, width, height));
            foreach (var obj in Objects) obj.Render(dc);
        }
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    // ---- 内部 ----

    private IAnnotationTool CreateTool() => ActiveTool switch
    {
        AnnotationTool.Rectangle => new RectangleTool(StrokeColor, StrokeWidth),
        AnnotationTool.Ellipse => new EllipseTool(StrokeColor, StrokeWidth),
        AnnotationTool.Arrow => new ArrowTool(StrokeColor, StrokeWidth),
        AnnotationTool.Freehand => new FreehandTool(StrokeColor, StrokeWidth),
        AnnotationTool.Highlighter => new HighlighterTool(Color.FromArgb(0x55, StrokeColor.R, StrokeColor.G, StrokeColor.B), StrokeWidth * 4),
        AnnotationTool.Mosaic => new MosaicTool(),
        AnnotationTool.Text => new TextTool(StrokeColor, FontSize),
        AnnotationTool.Emoji => new EmojiTool(FontSize),
        _ => new SelectionTool(Objects),
    };

    private void Select(AnnotationObject? obj)
    {
        foreach (var o in Objects) o.IsSelected = false;
        if (obj is not null) obj.IsSelected = true;
        Selected = obj;
    }

    private void Invalidate() => RenderInvalidated?.Invoke();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
