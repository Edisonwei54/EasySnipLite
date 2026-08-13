using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasySnipLite.Core.ClipboardServices;
using EasySnipLite.Core.Diagnostics;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Native;
using EasySnipLite.Editor;
using EasySnipLite.Editor.Models;
using EasySnipLite.Editor.Tools;
using EasySnipLite.Localization;

namespace EasySnipLite.Selection;

/// <summary>
/// 一次框选会话:管理每显示器一个的遮罩窗口,统一使用虚拟屏幕物理像素坐标。
/// 状态机:Idle(无选区)→ Selecting(拖拽框选)→ Adjusting(8 手柄/内部移动/方向键微调)。
/// issue #20:框选完成(松开鼠标)即进入内联标注模式——选区冻结为图像,标注工具悬浮在选区下方,
/// 框选调整(手柄/拖主体移动/方向键)与标注可同时进行,无需 Enter 打开独立编辑器。
/// issue #23:主体拖拽在 Selection 工具且未命中标注对象时 = 移动选区(无需 Alt);标注实时预览。
/// 拖拽期间用 DispatcherTimer 轮询光标(支持跨显示器连续拖拽)。
/// </summary>
public sealed class SelectionSession : IDisposable
{
    private const double HandleHitRadius = 6;

    private enum SessionMode { Idle, Selecting, Adjusting }

    private readonly IReadOnlyList<MonitorCapture> _frames;
    private readonly List<RegionSelectionWindow> _windows = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly Int32Rect _virtualBounds;

    private SessionMode _mode = SessionMode.Idle;
    private Point _dragStart;              // 按下时的虚拟物理坐标
    private Int32Rect _dragStartSelection; // Adjusting 拖动开始时的选区快照(从快照重算,避免累积误差)
    private SelectionHandle _activeHandle; // Adjusting 当前拖动的目标
    private Int32Rect? _selection;         // 虚拟屏幕物理像素
    private RegionSelectionWindow? _activeWindow;
    private bool _disposed;

    // ---- 内联标注（issue #20）----
    private EditorViewModel? _vm;              // 标注视图模型(底图随选区调整重组合)
    private AnnotationToolbarWindow? _toolbar; // 悬浮工具栏
    private bool _annotationDrag;              // 标注工具拖拽中(鼠标释放结束,不触碰选区状态)

    public event Action<BitmapSource>? Completed;
    public event Action? Cancelled;
    public event Action<BitmapSource>? SaveRequested;
    public event Action<BitmapSource>? PinRequested;

    public int FrameCount => _frames.Count;

    /// <summary>当前选区(虚拟屏幕物理像素);长截图等流程需用屏幕坐标持续捕获。</summary>
    public Int32Rect? SelectedRegion => _selection;

    /// <summary>内联标注激活(选区有效且标注模型已建立):窗口据此渲染标注层。</summary>
    public bool IsAnnotationActive =>
        _mode == SessionMode.Adjusting && _selection is not null && _vm is not null;

    /// <summary>标注视图模型(窗口渲染标注层用)。</summary>
    public EditorViewModel? AnnotationVm => _vm;

    /// <summary>当前标注底图(选区冻结帧,随选区调整重组合)。</summary>
    public BitmapSource? AnnotationImage { get; private set; }

    public SelectionSession()
    {
        _frames = ScreenCapture.CaptureAll();
        int minX = _frames.Min(f => f.PixelX), minY = _frames.Min(f => f.PixelY);
        int maxX = _frames.Max(f => f.PixelX + f.PixelWidth);
        int maxY = _frames.Max(f => f.PixelY + f.PixelHeight);
        _virtualBounds = new Int32Rect(minX, minY, maxX - minX, maxY - minY);
        _pollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(12),
        };
        _pollTimer.Tick += (_, _) => PollCursor();
    }

    public void Start()
    {
        foreach (var frame in _frames)
        {
            var window = new RegionSelectionWindow(frame, this);
            window.Show();
            window.Activate();
            _windows.Add(window);
        }
    }

    // ---- 由窗口转发来的交互 ----

    public void OnLeftButtonDown(RegionSelectionWindow source, Point localPos, bool altDown)
    {
        var virtualPos = source.LocalToVirtual(localPos);
        _activeWindow = source;

        // Adjusting 态:命中手柄 → 调整拖动;Alt+主体 → 整体移动;主体(无 Alt) → 标注或移动选区;外部 → 重新框选
        if (_mode == SessionMode.Adjusting && _selection is { } sel)
        {
            var handle = SelectionMath.HitTest(sel, virtualPos, HandleHitRadius);
            if (handle != SelectionHandle.None)
            {
                if (handle != SelectionHandle.Body || altDown)
                {
                    _activeHandle = handle;
                    _dragStart = virtualPos;
                    _dragStartSelection = sel;
                    _pollTimer.Start();
                    source.ShowMagnifier();
                    return;
                }
                if (IsAnnotationActive && _vm is not null)
                {
                    // Selection 工具且未命中标注对象 → 主体拖拽 = 移动选区（issue #23，无需 Alt）
                    bool drawingTool = _vm.ActiveTool != AnnotationTool.Selection;
                    bool objectHit = HitTester.HitTest(_vm.Objects, ToRegionLocal(virtualPos, sel)) is not null;
                    if (!drawingTool && !objectHit)
                    {
                        _activeHandle = SelectionHandle.Body;
                        _dragStart = virtualPos;
                        _dragStartSelection = sel;
                        _pollTimer.Start();
                        source.ShowMagnifier();
                        return;
                    }
                    // 标注拖拽:点击选区内部直接绘制,轮询定时器驱动光标(跨屏安全)
                    _annotationDrag = true;
                    _vm.OnMouseDown(ToRegionLocal(virtualPos, sel));
                    _pollTimer.Start();
                    return;
                }
            }
        }

        // 新框选（点击选区外部）：重新框选 = 新的一次截图，旧标注整体清空
        _mode = SessionMode.Selecting;
        _vm?.ClearAll();
        _dragStart = virtualPos;
        _selection = new Int32Rect((int)Math.Round(virtualPos.X), (int)Math.Round(virtualPos.Y), 0, 0);
        _pollTimer.Start();
        source.ShowMagnifier();
        Broadcast();
    }

    public void OnLeftButtonUp(RegionSelectionWindow source, Point localPos)
    {
        if (_mode == SessionMode.Idle) return;

        // 标注工具拖拽结束:释放即提交对象,不触碰选区状态
        if (_annotationDrag)
        {
            _annotationDrag = false;
            _pollTimer.Stop();
            if (_vm is not null && _selection is { } sel)
            {
                _vm.OnMouseUp(ToRegionLocal(source.LocalToVirtual(localPos), sel));
            }
            _activeWindow?.HideMagnifier();
            return;
        }

        _activeWindow?.HideMagnifier();

        if (_mode == SessionMode.Selecting)
        {
            // 点击未拖出尺寸则视为取消本次框选
            if (_selection is { Width: < 3, Height: < 3 })
            {
                _selection = null;
                _mode = SessionMode.Idle;
                Broadcast();
                return;
            }
            _mode = SessionMode.Adjusting;
            EnsureAnnotation(); // 框选完成 → 立即进入内联标注（无需 Enter）
        }
        _pollTimer.Stop();
        Broadcast();
    }

    /// <summary>方向键微调:1px 步进,Shift 10px,钳制在虚拟屏幕内。</summary>
    public void OnNudge(int dx, int dy, bool shift)
    {
        if (_mode != SessionMode.Adjusting || _selection is not { } sel) return;
        int step = shift ? 10 : 1;
        _selection = SelectionMath.Nudge(sel, dx * step, dy * step, _virtualBounds);
        Broadcast();
    }

    /// <summary>
    /// Esc 三级:有标注先清空标注 → 有选区清空选区 → 无选区取消整个会话。
    /// </summary>
    public void OnCancel()
    {
        if (_vm is { } vm && vm.Objects.Count > 0)
        {
            vm.ClearAll();
            Broadcast();
            return;
        }
        if (_selection is not null)
        {
            _selection = null;
            _mode = SessionMode.Idle;
            HideToolbar();
            Broadcast();
            return;
        }
        Cancelled?.Invoke();
    }

    public void OnConfirm()
    {
        if (_selection is not { Width: > 0, Height: > 0 }) return;
        CopyToClipboard(); // 完成 = 复制并关闭（与编辑器 Complete 一致）
        Completed?.Invoke(CurrentImage());
    }

    /// <summary>Ctrl+S:保存当前选区+标注(由 App 弹保存对话框)。</summary>
    public void OnSave()
    {
        if (_selection is not { Width: > 0, Height: > 0 }) return;
        SaveRequested?.Invoke(CurrentImage());
    }

    /// <summary>
    /// 鼠标悬停(未按下):命中测试更新光标。
    /// 主体内:Alt / Selection 工具且未命中对象(可移动选区) → SizeAll;标注中 → 十字。
    /// </summary>
    public void OnHover(RegionSelectionWindow source, Point localPos, bool altDown)
    {
        var virtualPos = source.LocalToVirtual(localPos);
        var handle = _mode == SessionMode.Adjusting && _selection is { } sel
            ? SelectionMath.HitTest(sel, virtualPos, HandleHitRadius)
            : SelectionHandle.None;
        if (IsAnnotationActive && handle == SelectionHandle.Body && _vm is not null && _selection is { } sel2)
        {
            bool drawingTool = _vm.ActiveTool != AnnotationTool.Selection;
            bool objectHit = HitTester.HitTest(_vm.Objects, ToRegionLocal(virtualPos, sel2)) is not null;
            source.SetCursorForHandle(altDown || (!drawingTool && !objectHit)
                ? SelectionHandle.Body
                : SelectionHandle.None);
        }
        else
        {
            source.SetCursorForHandle(handle);
        }
    }

    // ---- 内联标注操作（工具栏/快捷键转发到视图模型） ----

    public void SetAnnotationTool(AnnotationTool tool)
    {
        if (_vm is null) return;
        _vm.ActiveTool = tool;
        _toolbar?.SetTool(tool);
    }

    public void UndoAnnotation() => _vm?.Undo();
    public void RedoAnnotation() => _vm?.Redo();
    public void DeleteSelected() => _vm?.DeleteSelected();
    public void CommitEmoji(Point regionLocal, string emoji) => _vm?.CommitEmoji(regionLocal, emoji);

    /// <summary>复制当前组合(底图+标注)到剪贴板,失败走错误管线。</summary>
    public void CopyToClipboard()
    {
        try
        {
            ClipboardEx.SetImage(CurrentImage());
        }
        catch (Exception ex)
        {
            AppErrors.Notify(ex, AppResources.CopyFailed);
        }
    }

    // ---- 拖拽轮询 ----

    private void PollCursor()
    {
        if (!Win32.GetCursorPos(out var pt)) return;
        var cursor = new Point(pt.X, pt.Y);

        // 标注拖拽:光标直接驱动标注工具(跨显示器连续绘制)
        if (_annotationDrag && _selection is { } sel2 && _vm is not null)
        {
            _vm.OnMouseMove(ToRegionLocal(cursor, sel2));
            return;
        }

        switch (_mode)
        {
            case SessionMode.Selecting:
                _selection = RectFromPoints(_dragStart, cursor);
                break;
            case SessionMode.Adjusting:
                if (_selection is not { } sel) break;
                _selection = _activeHandle == SelectionHandle.Body
                    ? SelectionMath.Move(_dragStartSelection,
                        (int)Math.Round(cursor.X - _dragStart.X), (int)Math.Round(cursor.Y - _dragStart.Y), _virtualBounds)
                    : SelectionMath.Resize(_dragStartSelection, _activeHandle, cursor);
                break;
            default:
                return;
        }

        Broadcast();

        // 放大镜跟随鼠标(可能跨屏,显示在鼠标所在窗口)
        var target = WindowAt(cursor) ?? _activeWindow;
        foreach (var window in _windows)
        {
            if (window == target) window.UpdateMagnifier(cursor);
            else window.HideMagnifier();
        }
    }

    private static Int32Rect RectFromPoints(Point a, Point b) => new(
        (int)Math.Round(Math.Min(a.X, b.X)),
        (int)Math.Round(Math.Min(a.Y, b.Y)),
        (int)Math.Round(Math.Abs(b.X - a.X)),
        (int)Math.Round(Math.Abs(b.Y - a.Y)));

    private RegionSelectionWindow? WindowAt(Point virtualPos) =>
        _windows.FirstOrDefault(w => w.ContainsVirtual(virtualPos));

    private void Broadcast()
    {
        var showHandles = _mode == SessionMode.Adjusting;
        // 选区变化 → 重组合标注底图 + 工具栏跟随重定位
        if (IsAnnotationActive && _vm is not null && _selection is { } sel)
        {
            AnnotationImage = Compose(sel);
            _vm.SetBaseImage(AnnotationImage);
            _toolbar?.Reposition(sel, _virtualBounds, ToolbarDpi(sel));
        }
        foreach (var window in _windows)
        {
            window.UpdateSelection(_selection, showHandles);
        }
    }

    /// <summary>工具栏所在显示器的 DpiScale（按工具栏期望位置取显示器，取不到用选区窗口）。</summary>
    private double ToolbarDpi(Int32Rect region)
    {
        var anchor = _windows.FirstOrDefault(w => w.ContainsVirtual(new Point(region.X + region.Width / 2.0, region.Y + region.Height / 2.0)))
                     ?? _windows.FirstOrDefault();
        return anchor?.DpiScale ?? 1.0;
    }

    // ---- 内联标注装配 ----

    /// <summary>框选完成:建立标注视图模型 + 显示悬浮工具栏。</summary>
    private void EnsureAnnotation()
    {
        if (_selection is not { Width: > 0, Height: > 0 } sel) return;
        if (_vm is null)
        {
            _vm = new EditorViewModel(Compose(sel));
            _vm.RenderInvalidated += () =>
            {
                foreach (var window in _windows) window.InvalidateAnnotationLayer(_vm!);
            };
            _vm.TextInputRequested += ShowTextInput;
            _vm.EmojiInputRequested += ShowEmojiPanel;
        }
        EnsureToolbar();
    }

    private void EnsureToolbar()
    {
        if (_toolbar is not null) return;
        _toolbar = new AnnotationToolbarWindow();
        // issue #23：遮罩窗口激活（点击/标注）时会提到置顶带最前盖住工具栏 →
        // 设 Owner（被拥有的窗口恒在 Owner 之上），工具栏不被遮罩/掩膜盖住、点击不落空
        if (GetAnchorWindow() is { } anchor) _toolbar.Owner = anchor;
        _toolbar.ToolSelected += tool => { if (_vm is not null) _vm.ActiveTool = tool; };
        _toolbar.ColorSelected += color => { if (_vm is not null) _vm.StrokeColor = color; };
        _toolbar.StrokeWidthChanged += width => { if (_vm is not null) _vm.StrokeWidth = width; };
        _toolbar.UndoRequested += () => _vm?.Undo();
        _toolbar.RedoRequested += () => _vm?.Redo();
        _toolbar.DeleteRequested += () => _vm?.DeleteSelected();
        _toolbar.CopyRequested += CopyToClipboard;
        _toolbar.SaveRequested += () =>
        {
            if (_selection is not null) SaveRequested?.Invoke(CurrentImage());
        };
        _toolbar.PinRequested += () =>
        {
            if (_selection is not null) PinRequested?.Invoke(CurrentImage());
        };
        _toolbar.CompleteRequested += OnConfirm;
        _toolbar.SetTool(_vm.ActiveTool); // 初始工具态同步（默认 Selection 按钮点亮）
        _toolbar.Show();
    }

    /// <summary>工具栏 Owner 锚点：选区中心所在显示器窗口（工具栏不会被遮罩盖住）。</summary>
    private RegionSelectionWindow? GetAnchorWindow()
    {
        if (_selection is { } sel)
        {
            var center = new Point(sel.X + sel.Width / 2.0, sel.Y + sel.Height / 2.0);
            if (_windows.FirstOrDefault(w => w.ContainsVirtual(center)) is { } hit) return hit;
        }
        return _activeWindow ?? _windows.FirstOrDefault();
    }

    private void HideToolbar()
    {
        _toolbar?.Close();
        _toolbar = null;
    }

    // ---- 文字/表情输入 ----

    private void ShowTextInput(Point regionLocal)
    {
        if (_selection is not { } sel) return;
        var window = WindowAt(ToVirtual(regionLocal, sel)) ?? _activeWindow ?? _windows.FirstOrDefault();
        var dialog = new TextInputDialog { Owner = window };
        if (dialog.ShowDialog() == true)
            _vm?.CommitText(regionLocal, dialog.Text);
    }

    private void ShowEmojiPanel(Point regionLocal)
    {
        if (_selection is not { } sel) return;
        var window = WindowAt(ToVirtual(regionLocal, sel)) ?? _activeWindow ?? _windows.FirstOrDefault();
        window?.ShowEmojiPanel(regionLocal);
    }

    private static Point ToRegionLocal(Point virtualPos, Int32Rect region) =>
        new(virtualPos.X - region.X, virtualPos.Y - region.Y);

    private static Point ToVirtual(Point regionLocal, Int32Rect region) =>
        new(regionLocal.X + region.X, regionLocal.Y + region.Y);

    /// <summary>当前输出图像:标注激活时组合底图+标注,否则为选区原始图像。</summary>
    private BitmapSource CurrentImage() => IsAnnotationActive && _vm is not null
        ? _vm.Compose()
        : Compose(_selection!.Value);

    // ---- 组装结果位图（跨屏裁剪） ----

    private BitmapSource Compose(Int32Rect rect)
    {
        var output = new WriteableBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null);
        int outStride = rect.Width * 4;
        var outBuffer = new byte[outStride * rect.Height];

        foreach (var frame in _frames)
        {
            var frameBounds = new Int32Rect(frame.PixelX, frame.PixelY, frame.PixelWidth, frame.PixelHeight);
            int ix = Math.Max(rect.X, frameBounds.X);
            int iy = Math.Max(rect.Y, frameBounds.Y);
            int ix2 = Math.Min(rect.X + rect.Width, frameBounds.X + frameBounds.Width);
            int iy2 = Math.Min(rect.Y + rect.Height, frameBounds.Y + frameBounds.Height);
            var inter = new Int32Rect(ix, iy, Math.Max(0, ix2 - ix), Math.Max(0, iy2 - iy));
            if (inter.Width <= 0 || inter.Height <= 0) continue;

            var crop = new CroppedBitmap(
                frame.Image,
                new Int32Rect(inter.X - frame.PixelX, inter.Y - frame.PixelY, inter.Width, inter.Height));
            var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
            int stride = inter.Width * 4;
            var pixels = new byte[stride * inter.Height];
            bgra.CopyPixels(pixels, stride, 0);

            int destX = inter.X - rect.X;
            int destY = inter.Y - rect.Y;
            for (int row = 0; row < inter.Height; row++)
            {
                Buffer.BlockCopy(pixels, row * stride, outBuffer, (destY + row) * outStride + destX * 4, stride);
            }
        }

        output.WritePixels(new Int32Rect(0, 0, rect.Width, rect.Height), outBuffer, outStride, 0);
        output.Freeze();
        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Stop();
        HideToolbar();
        foreach (var window in _windows)
        {
            window.Close();
        }
        _windows.Clear();
    }
}
