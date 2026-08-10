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

    /// <summary>可写性探测覆盖（测试注入）：null = 真实 IO 探测（CreateDirectory + 临时文件写删）。</summary>
    public static Func<string, bool>? WriteProbe { get; set; }

    public static string DefaultSaveDir()
    {
        var configured = DefaultSaveDirProvider?.Invoke();
        if (!string.IsNullOrEmpty(configured) && Writable(configured)) return configured;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var dir = Path.Combine(pictures, "EasySnipLite");
        if (Writable(dir)) return dir;
        return pictures; // 最终兜底：保存失败由 SaveFailed 提示（编辑器的用户可见错误路径）
    }

    /// <summary>候选目录可用性：CreateDirectory 兜底 + 可写性探测（T11-M1：已存在只读目录 CreateDirectory 不抛异常）。</summary>
    private static bool Writable(string dir)
    {
        if (WriteProbe is { } probe) return probe(dir);
        try
        {
            Directory.CreateDirectory(dir);
            var probeFile = Path.Combine(dir, ".writable-probe");
            File.WriteAllText(probeFile, "");
            File.Delete(probeFile);
            return true;
        }
        catch { return false; }
    }

    public static string DefaultFileName() =>
        $"EasySnipLite_{DateTime.Now:yyyyMMdd_HHmmss}.png";
}
