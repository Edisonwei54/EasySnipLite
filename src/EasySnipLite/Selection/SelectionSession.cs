using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Native;

namespace EasySnipLite.Selection;

/// <summary>
/// 一次框选会话：管理每显示器一个的遮罩窗口，统一使用虚拟屏幕物理像素坐标，
/// 拖拽期间用 DispatcherTimer 轮询光标（支持跨显示器连续拖拽），
/// 确认后从各显示器冻结帧裁剪组装结果位图。
/// </summary>
public sealed class SelectionSession : IDisposable
{
    private readonly IReadOnlyList<MonitorCapture> _frames;
    private readonly List<RegionSelectionWindow> _windows = new();
    private readonly DispatcherTimer _pollTimer;
    private Point _dragStart;      // 虚拟屏幕物理像素
    private Int32Rect? _selection; // 虚拟屏幕物理像素
    private bool _selecting;
    private bool _disposed;

    public event Action<BitmapSource>? Completed;
    public event Action? Cancelled;

    public int FrameCount => _frames.Count;

    public SelectionSession()
    {
        _frames = ScreenCapture.CaptureAll();
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
        _dragStart = virtualPos;
        _selection = new Int32Rect((int)Math.Round(virtualPos.X), (int)Math.Round(virtualPos.Y), 0, 0);
        _selecting = true;
        _pollTimer.Start();
        Broadcast();
    }

    public void OnLeftButtonUp()
    {
        if (!_selecting) return;
        _selecting = false;
        _pollTimer.Stop();
        // 点击未拖出尺寸则视为取消本次框选
        if (_selection is { Width: < 3, Height: < 3 })
        {
            _selection = null;
            Broadcast();
        }
    }

    public void OnConfirm()
    {
        if (_selection is not { Width: > 0, Height: > 0 }) return;
        var result = Compose(_selection.Value);
        Completed?.Invoke(result);
    }

    public void OnCancel() => Cancelled?.Invoke();

    // ---- 拖拽轮询 ----

    private void PollCursor()
    {
        if (!_selecting) return;
        if (Win32.GetCursorPos(out var pt))
        {
            var current = new Point(pt.X, pt.Y);
            var rect = new Int32Rect(
                (int)Math.Round(Math.Min(_dragStart.X, current.X)),
                (int)Math.Round(Math.Min(_dragStart.Y, current.Y)),
                (int)Math.Round(Math.Abs(current.X - _dragStart.X)),
                (int)Math.Round(Math.Abs(current.Y - _dragStart.Y)));
            _selection = rect;
            Broadcast();
        }
    }

    private void Broadcast()
    {
        foreach (var window in _windows)
        {
            window.UpdateSelection(_selection);
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
