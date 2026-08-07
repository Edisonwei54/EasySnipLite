using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasySnipLite.Core.ClipboardServices;

/// <summary>
/// 剪贴板写入：DIB（WinForms 可靠编码，画图/Office 可贴）+ PNG（IM 可贴），最大化粘贴兼容性。
/// 必须在 STA（UI）线程调用。
/// </summary>
public static class ClipboardEx
{
    /// <summary>把位图写入剪贴板（DIB + PNG）。</summary>
    public static void SetImage(BitmapSource image)
    {
        var bgra = ConvertToBgra32(image);
        var png = EncodePng(bgra);
        using var gdiBmp = ToGdiBitmap(bgra);

        var data = new System.Windows.Forms.DataObject();
        data.SetImage(gdiBmp); // CF_DIB + CF_BITMAP，编码交给 WinForms 保证兼容
        data.SetData("PNG", new MemoryStream(png));
        System.Windows.Forms.Clipboard.SetDataObject(data, copy: true);
    }

    private static BitmapSource ConvertToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32) return source;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] EncodePng(BitmapSource bgra)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bgra));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static System.Drawing.Bitmap ToGdiBitmap(BitmapSource bgra)
    {
        var bmp = new System.Drawing.Bitmap(
            bgra.PixelWidth, bgra.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            // Bgra32 与 Format32bppArgb 字节序一致，可整块拷贝
            bgra.CopyPixels(
                new System.Windows.Int32Rect(0, 0, bgra.PixelWidth, bgra.PixelHeight),
                data.Scan0, data.Stride * bmp.Height, data.Stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }
}
