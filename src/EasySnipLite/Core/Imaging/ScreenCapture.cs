using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.Native;
using Screen = System.Windows.Forms.Screen;

namespace EasySnipLite.Core.Imaging;

/// <summary>一台显示器的捕获结果。坐标为物理像素（虚拟屏幕坐标系）。</summary>
public sealed record MonitorCapture(
    IntPtr MonitorHandle,
    int PixelX,
    int PixelY,
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    BitmapSource Image)
{
    public Rect PixelBounds => new(PixelX, PixelY, PixelWidth, PixelHeight);
}

/// <summary>
/// 冻结全部显示器画面（BitBlt，物理像素）。所有坐标使用物理像素统一换算。
/// </summary>
public static class ScreenCapture
{
    /// <summary>捕获全部显示器。必须在 UI（STA）线程调用。</summary>
    public static IReadOnlyList<MonitorCapture> CaptureAll()
    {
        var result = new List<MonitorCapture>();
        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            var handle = Win32.MonitorFromPoint(
                new Win32.POINT { X = b.X + b.Width / 2, Y = b.Y + b.Height / 2 },
                Win32.MONITOR_DEFAULTTONEAREST);
            double dpiScale = GetDpiScale(handle);
            var image = CaptureScreenArea(b.X, b.Y, b.Width, b.Height);
            result.Add(new MonitorCapture(handle, b.X, b.Y, b.Width, b.Height, dpiScale, image));
        }
        return result;
    }

    private static double GetDpiScale(IntPtr monitor)
    {
        if (Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
        {
            return dpiX / 96.0;
        }
        return 1.0;
    }

    private static BitmapSource CaptureScreenArea(int x, int y, int width, int height)
    {
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            screenDc = Win32.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) throw new InvalidOperationException("GetDC 失败");
            memDc = Win32.CreateCompatibleDC(screenDc);
            hBitmap = Win32.CreateCompatibleBitmap(screenDc, width, height);
            if (hBitmap == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleBitmap 失败");
            oldBitmap = Win32.SelectObject(memDc, hBitmap);
            if (!Win32.BitBlt(memDc, 0, 0, width, height, screenDc, x, y, Win32.SRCCOPY))
            {
                throw new InvalidOperationException("BitBlt 失败");
            }
            using var bmp = Image.FromHbitmap(hBitmap);
            return ToBitmapSource(bmp);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) Win32.SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero) Win32.DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) Win32.DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Win32.DeleteObject(hBitmap);
        }
    }
}
