using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Native;

namespace EasySnipLite.Selection;

/// <summary>
/// 一次框选会话:管理每显示器一个的遮罩窗口,统一使用虚拟屏幕物理像素坐标。
/// 状态机:Idle(无选区)→ Selecting(拖拽框选)→ Adjusting(8 手柄/内部移动/方向键微调)。
/// 拖拽期间用 DispatcherTimer 轮询光标(支持跨显示器连续拖拽),
/// 确认后从各显示器冻结帧裁剪组装结果位图。
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

    public event Action<BitmapSource>? Completed;
    public event Action? Cancelled;
    public event Action<BitmapSource>? SaveRequested;

    public int FrameCount => _frames.Count;

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

    public void OnLeftButtonDown(RegionSelectionWindow source, Point localPos)
    {
        var virtualPos = source.LocalToVirtual(localPos);
        _activeWindow = source;

        // Adjusting 态命中选区(手柄/主体)→ 开始调整拖动;命中外部 → 重新框选
        if (_mode == SessionMode.Adjusting && _selection is { } sel)
        {
            var handle = SelectionMath.HitTest(sel, virtualPos, HandleHitRadius);
            if (handle != SelectionHandle.None)
            {
                _activeHandle = handle;
                _dragStart = virtualPos;
                _dragStartSelection = sel;
                _pollTimer.Start();
                source.ShowMagnifier();
                return;
            }
        }

        // 新框选
        _mode = SessionMode.Selecting;
        _dragStart = virtualPos;
        _selection = new Int32Rect((int)Math.Round(virtualPos.X), (int)Math.Round(virtualPos.Y), 0, 0);
        _pollTimer.Start();
        source.ShowMagnifier();
        Broadcast();
    }

    public void OnLeftButtonUp()
    {
        if (_mode == SessionMode.Idle) return;
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
        }
        _pollTimer.Stop();
    }

    /// <summary>方向键微调:1px 步进,Shift 10px,钳制在虚拟屏幕内。</summary>
    public void OnNudge(int dx, int dy, bool shift)
    {
        if (_mode != SessionMode.Adjusting || _selection is not { } sel) return;
        int step = shift ? 10 : 1;
        _selection = SelectionMath.Nudge(sel, dx * step, dy * step, _virtualBounds);
        Broadcast();
    }

    /// <summary>Esc:有选区先清空回到框选态,无选区才取消整个会话。</summary>
    public void OnCancel()
    {
        if (_selection is not null)
        {
            _selection = null;
            _mode = SessionMode.Idle;
            Broadcast();
            return;
        }
        Cancelled?.Invoke();
    }

    public void OnConfirm()
    {
        if (_selection is not { Width: > 0, Height: > 0 }) return;
        Completed?.Invoke(Compose(_selection.Value));
    }

    /// <summary>Ctrl+S:保存当前选区(由 App 弹保存对话框)。</summary>
    public void OnSave()
    {
        if (_selection is not { Width: > 0, Height: > 0 }) return;
        SaveRequested?.Invoke(Compose(_selection.Value));
    }

    /// <summary>鼠标悬停(未按下):命中测试更新光标。</summary>
    public void OnHover(RegionSelectionWindow source, Point localPos)
    {
        var virtualPos = source.LocalToVirtual(localPos);
        var handle = _mode == SessionMode.Adjusting && _selection is { } sel
            ? SelectionMath.HitTest(sel, virtualPos, HandleHitRadius)
            : SelectionHandle.None;
        source.SetCursorForHandle(handle);
    }

    // ---- 拖拽轮询 ----

    private void PollCursor()
    {
        if (!Win32.GetCursorPos(out var pt)) return;
        var cursor = new Point(pt.X, pt.Y);

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
        foreach (var window in _windows)
        {
            window.UpdateSelection(_selection, showHandles);
        }
    }

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
        foreach (var window in _windows)
        {
            window.Close();
        }
        _windows.Clear();
    }
}
