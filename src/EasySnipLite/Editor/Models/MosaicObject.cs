using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasySnipLite.Editor.Models;

/// <summary>
/// 马赛克：矩形覆盖区域 + 块大小。渲染时对底图该区域做块化（块内像素取块左上像素），
/// 原图不被破坏、可撤销。SourceImage 由编辑器渲染前注入。
/// </summary>
public sealed class MosaicObject : AnnotationObject
{
    public int BlockSize { get; set; }

    /// <summary>底图（冻结后的截图），Render 前由渲染层注入。</summary>
    public BitmapSource? SourceImage { get; set; }

    public MosaicObject(Rect bounds, int blockSize)
    {
        Bounds = bounds;
        BlockSize = blockSize;
    }

    public override AnnotationObject Clone() =>
        new MosaicObject(Bounds, BlockSize) { Color = Color, StrokeWidth = StrokeWidth };

    public override void Render(DrawingContext dc)
    {
        if (SourceImage is null) return;

        var x = (int)Math.Floor(Bounds.X);
        var y = (int)Math.Floor(Bounds.Y);
        var w = (int)Math.Ceiling(Bounds.Width);
        var h = (int)Math.Ceiling(Bounds.Height);
        if (w <= 0 || h <= 0) return;

        var cropped = new CroppedBitmap(SourceImage, new Int32Rect(x, y, w, h));
        var stride = w * 4;
        var pixels = new byte[stride * h];
        cropped.CopyPixels(pixels, stride, 0);

        var block = Math.Max(2, BlockSize);
        for (var py = 0; py < h; py++)
        {
            var by = (py / block) * block;
            for (var px = 0; px < w; px++)
            {
                var bx = (px / block) * block;
                var i = (py * w + px) * 4;
                var j = (by * w + bx) * 4;
                pixels[i] = pixels[j];
                pixels[i + 1] = pixels[j + 1];
                pixels[i + 2] = pixels[j + 2];
                pixels[i + 3] = pixels[j + 3];
            }
        }

        var mosaic = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        dc.DrawImage(mosaic, Bounds);
    }
}
