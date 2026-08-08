namespace EasySnipLite.Pin;

/// <summary>
/// 贴屏窗口的纯逻辑换算：物理像素 ↔ WPF 布局坐标（÷DpiScale）、缩放步进与钳制。
/// 布局尺寸 = 物理像素 / DpiScale × zoom；窗口位置 = 物理像素 / DpiScale。
/// </summary>
public static class PinMath
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 3.0;
    public const double ZoomStep = 1.1;

    public static (double Width, double Height) LayoutSize(
        int pixelW, int pixelH, double dpiScale, double zoom) =>
        (pixelW / dpiScale * zoom, pixelH / dpiScale * zoom);

    public static (double X, double Y) LayoutPosition(int pixelX, int pixelY, double dpiScale) =>
        (pixelX / dpiScale, pixelY / dpiScale);

    public static double NextZoom(double current, bool zoomIn)
    {
        var next = zoomIn ? current * ZoomStep : current / ZoomStep;
        return Math.Clamp(next, MinZoom, MaxZoom);
    }
}
