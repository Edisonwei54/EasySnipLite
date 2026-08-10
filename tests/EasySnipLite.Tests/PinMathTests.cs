using EasySnipLite.Pin;

namespace EasySnipLite.Tests;

public class PinMathTests
{
    // ---- 1:1 布局换算 ----

    [Fact]
    public void LayoutSize_Dpi100_IsPixelSize()
    {
        var (w, h) = PinMath.LayoutSize(300, 200, 1.0, 1.0);
        Assert.Equal(300, w, 3);
        Assert.Equal(200, h, 3);
    }

    [Fact]
    public void LayoutSize_Dpi150_ShrinksToLayoutUnits()
    {
        var (w, h) = PinMath.LayoutSize(300, 200, 1.5, 1.0);
        Assert.Equal(200, w, 3);
        Assert.Equal(200 / 1.5, h, 3);
    }

    [Fact]
    public void LayoutSize_Zoom2_DoublesLayout()
    {
        var (w, h) = PinMath.LayoutSize(300, 200, 1.0, 2.0);
        Assert.Equal(600, w, 3);
        Assert.Equal(400, h, 3);
    }

    [Fact]
    public void LayoutSize_Dpi150_Zoom05_Combined()
    {
        var (w, h) = PinMath.LayoutSize(300, 200, 1.5, 0.5);
        Assert.Equal(100, w, 3);
        Assert.Equal(200.0 / 3.0, h, 3);
    }

    [Fact]
    public void LayoutPosition_Dpi100_IsPixelCoords()
    {
        var (x, y) = PinMath.LayoutPosition(1920, 1080, 1.0);
        Assert.Equal(1920, x, 3);
        Assert.Equal(1080, y, 3);
    }

    [Fact]
    public void LayoutPosition_Dpi150_DividesByScale()
    {
        var (x, y) = PinMath.LayoutPosition(1920, 1080, 1.5);
        Assert.Equal(1280, x, 3);
        Assert.Equal(720, y, 3);
    }

    // ---- 缩放步进与钳制 ----

    [Fact]
    public void NextZoom_ZoomIn_MultipliesByStep()
    {
        Assert.Equal(1.1, PinMath.NextZoom(1.0, zoomIn: true), 6);
        Assert.Equal(2.0 * 1.1, PinMath.NextZoom(2.0, zoomIn: true), 6);
    }

    [Fact]
    public void NextZoom_ZoomOut_DividesByStep()
    {
        Assert.Equal(1.0, PinMath.NextZoom(1.1, zoomIn: false), 6);
        Assert.Equal(2.0 / 1.1, PinMath.NextZoom(2.0, zoomIn: false), 6);
    }

    [Fact]
    public void NextZoom_ClampsAtMax()
    {
        Assert.Equal(3.0, PinMath.NextZoom(3.0, zoomIn: true), 6);
        Assert.Equal(3.0, PinMath.NextZoom(2.9, zoomIn: true), 6);
    }

    [Fact]
    public void NextZoom_ClampsAtMin()
    {
        Assert.Equal(0.5, PinMath.NextZoom(0.5, zoomIn: false), 6);
        Assert.Equal(0.5, PinMath.NextZoom(0.51, zoomIn: false), 6);
    }

    [Fact]
    public void NextZoom_CustomStep_AppliesFactor()
    {
        Assert.Equal(1.0 * 1.2, PinMath.NextZoom(1.0, zoomIn: true, step: 1.2));
        Assert.Equal(1.0 / 1.05, PinMath.NextZoom(1.0, zoomIn: false, step: 1.05));
    }

    [Fact]
    public void NextZoom_CustomStep_StillClamped()
    {
        Assert.Equal(PinMath.MaxZoom, PinMath.NextZoom(2.9, zoomIn: true, step: 1.2));
        Assert.Equal(PinMath.MinZoom, PinMath.NextZoom(0.51, zoomIn: false, step: 1.05));
    }
}
