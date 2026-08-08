using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.ClipboardServices;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Native;

namespace EasySnipLite.Stitching;

/// <summary>
/// M4 长截图实时预览:1:1 显示拼接结果 + 进度 + 停止/重试(接缝续跑)/复制/保存/完成。
/// 捕获循环在 UI 线程 async 跑(截帧要求 STA),期间 await 让出不卡界面。
/// </summary>
public partial class StitchPreviewWindow : Window
{
    private readonly int _x, _y, _w, _h;
    private readonly bool _autoCopy;
    private readonly List<int> _seams = [];
    private CancellationTokenSource? _cts;
    private ScrollCaptureCheckpoint? _checkpoint;
    private ScrollCaptureResult? _result;

    /// <param name="autoCopyOnComplete">捕获完成(到底/上限)后自动复制到剪贴板;命令行验证模式用。</param>
    public StitchPreviewWindow(int x, int y, int w, int h, bool autoCopyOnComplete = false)
    {
        InitializeComponent();
        _x = x;
        _y = y;
        _w = w;
        _h = h;
        _autoCopy = autoCopyOnComplete;
        PlaceAwayFromRegion(x, y, w, h);
    }

    /// <summary>定位到目标区域外(右/下/上/左依次尝试),避免预览窗口遮挡被捕获窗口导致滚轮/截帧错乱;无空间则最小化。</summary>
    private void PlaceAwayFromRegion(int rx, int ry, int rw, int rh)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rx + rw / 2, ry + rh / 2));
        var wa = screen.WorkingArea;
        double pw = Width, ph = Height;
        double left = 0, top = 0;
        bool placed = false;

        if (rx + rw + pw + 12 <= wa.Right)        // 右侧
        {
            left = rx + rw + 12;
            top = Math.Clamp(ry + rh / 2 - ph / 2, wa.Top, wa.Bottom - ph);
            placed = true;
        }
        else if (ry + rh + ph + 12 <= wa.Bottom)  // 下方
        {
            left = Math.Clamp(rx + rw / 2 - pw / 2, wa.Left, wa.Right - pw);
            top = ry + rh + 12;
            placed = true;
        }
        else if (ry - ph - 12 >= wa.Top)          // 上方
        {
            left = Math.Clamp(rx + rw / 2 - pw / 2, wa.Left, wa.Right - pw);
            top = ry - ph - 12;
            placed = true;
        }
        else if (rx - pw - 12 >= wa.Left)         // 左侧
        {
            left = rx - pw - 12;
            top = Math.Clamp(ry + rh / 2 - ph / 2, wa.Top, wa.Bottom - ph);
            placed = true;
        }

        if (placed)
        {
            Left = left;
            Top = top;
        }
        else
        {
            WindowState = WindowState.Minimized; // 目标区域几乎占满屏幕:最小化,不遮挡
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => StartCapture();

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
        // 未移交的结果画布在此释放;重试续跑的画布在最终结果/关闭时释放
        _result?.Image?.Dispose();
        _checkpoint?.Canvas.Dispose();
    }

    private async void StartCapture()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var checkpoint = _checkpoint;
        _checkpoint = null;

        int cx = _x + _w / 2, cy = _y + _h / 2;
        var engine = new ScrollCaptureEngine(
            () => ScreenCapture.CaptureRegionBitmap(_x, _y, _w, _h),
            () => ScrollInput.ScrollDownAt(cx, cy, 3),
            new ScrollCaptureOptions { WheelNotches = 3, StabilizeDelayMs = 350 });
        engine.PreviewUpdated += OnPreviewUpdated;
        engine.ProgressChanged += OnProgressChanged;

        SetCapturing(true);
        var result = await (checkpoint is null
            ? engine.RunAsync(ct)
            : engine.ContinueAsync(checkpoint, ct));
        SetCapturing(false);

        _result = result;
        _seams.AddRange(result.FailedSeams);
        RenderSeams();

        if (result.Error is not null)
        {
            _checkpoint = result.Checkpoint;
            StatusText.Text = $"捕获失败:{result.Error} 可重试";
            BtnRetry.Visibility = Visibility.Visible;
            return;
        }

        if (_autoCopy && result.Image is not null)
        {
            ClipboardEx.SetImage(ToBitmapSource(result.Image));
        }
        BtnCopy.Visibility = Visibility.Visible;
        BtnSave.Visibility = Visibility.Visible;
        BtnComplete.Visibility = Visibility.Visible;
        StatusText.Text = result.Cancelled
            ? $"已停止(高 {result.Height}px,{result.FrameCount} 帧)"
            : result.HeightLimitReached
                ? $"已达高度上限 {result.Height}px"
                : $"捕获完成(高 {result.Height}px,{result.FrameCount} 帧)";
    }

    private void SetCapturing(bool busy)
    {
        BtnStop.IsEnabled = busy;
        BtnRetry.Visibility = busy ? Visibility.Collapsed : BtnRetry.Visibility;
        StatusText.Text = busy ? "正在捕获…" : StatusText.Text;
    }

    /// <summary>预览更新:canvas 是引擎内部对象,立即转快照位图。</summary>
    private void OnPreviewUpdated(Bitmap canvas) => PreviewImage.Source = ToBitmapSource(canvas);

    private void OnProgressChanged(int height, int frames) =>
        StatusText.Text = $"正在捕获… 高 {height}px,{frames} 帧";

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Win32.DeleteObject(hBitmap);
        }
    }

    /// <summary>在 1:1 预览上叠加接缝红线。</summary>
    private void RenderSeams()
    {
        var img = PreviewImage.Source as BitmapSource;
        if (img is null || _seams.Count == 0)
        {
            SeamOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        SeamOverlay.Width = img.PixelWidth;
        SeamOverlay.Height = img.PixelHeight;
        SeamOverlay.Children.Clear();
        foreach (int y in _seams)
        {
            var line = new System.Windows.Shapes.Rectangle
            {
                Width = img.PixelWidth,
                Height = 2,
                Fill = Brushes.Red,
                Opacity = 0.8,
            };
            Canvas.SetTop(line, Math.Max(0, y - 1));
            SeamOverlay.Children.Add(line);
        }
        SeamOverlay.Visibility = Visibility.Visible;
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Retry_Click(object sender, RoutedEventArgs e) => StartCapture();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (ToBitmapSourceOrNull() is { } source) ClipboardEx.SetImage(source);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ToBitmapSourceOrNull() is { } source)
            ImageFile.SavePngWithDialog(source, ImageFile.DefaultFileName());
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        Copy_Click(sender, e);
        Close();
    }

    private BitmapSource? ToBitmapSourceOrNull() =>
        _result?.Image is { } bmp ? ToBitmapSource(bmp) : null;
}
