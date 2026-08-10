using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnipLite.Localization;

namespace EasySnipLite.Core.Imaging;

/// <summary>图片落盘共享逻辑：SaveFileDialog + PNG 编码（选区保存与编辑器保存共用）。</summary>
public static class ImageFile
{
    /// <summary>用户设置的保存目录提供者（App 启动注入读设置）；返回 null/空或不可写则回退默认。</summary>
    public static Func<string?>? DefaultSaveDirProvider { get; set; }

    /// <summary>弹 SaveFileDialog 保存 PNG；取消返回 null。</summary>
    public static string? SavePngWithDialog(BitmapSource image, string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = AppResources.PngFilter,
            DefaultExt = ".png",
            FileName = defaultFileName,
            InitialDirectory = DefaultSaveDir(),
        };
        if (dialog.ShowDialog() != true) return null;

        SavePng(image, dialog.FileName);
        return dialog.FileName;
    }

    /// <summary>直接写 PNG 到指定路径。</summary>
    public static void SavePng(BitmapSource image, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    public static string DefaultSaveDir()
    {
        var configured = DefaultSaveDirProvider?.Invoke();
        if (!string.IsNullOrEmpty(configured))
        {
            try
            {
                Directory.CreateDirectory(configured);
                return configured;
            }
            catch
            {
                // 设置的目录不可写 → 回退默认
            }
        }
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dir = Path.Combine(pictures, "EasySnipLite");
        try
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            return pictures;
        }
    }

    public static string DefaultFileName() =>
        $"EasySnipLite_{DateTime.Now:yyyyMMdd_HHmmss}.png";
}
