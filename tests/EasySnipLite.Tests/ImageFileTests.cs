using EasySnipLite.Core.Imaging;

namespace EasySnipLite.Tests;

public class ImageFileTests : IDisposable
{
    private readonly Func<string?>? _originalProvider;
    private readonly Func<string, bool>? _originalProbe;

    public ImageFileTests()
    {
        _originalProvider = ImageFile.DefaultSaveDirProvider;
        _originalProbe = ImageFile.WriteProbe;
    }

    public void Dispose()
    {
        ImageFile.DefaultSaveDirProvider = _originalProvider;
        ImageFile.WriteProbe = _originalProbe;
    }

    [Fact]
    public void DefaultSaveDir_WritableConfigured_ReturnsConfigured()
    {
        ImageFile.DefaultSaveDirProvider = () => @"C:\configured\dir";
        ImageFile.WriteProbe = _ => true;

        Assert.Equal(@"C:\configured\dir", ImageFile.DefaultSaveDir());
    }

    [Fact]
    public void DefaultSaveDir_UnwritableConfigured_FallsBackToPictures()
    {
        ImageFile.DefaultSaveDirProvider = () => @"C:\configured\dir";
        ImageFile.WriteProbe = _ => false; // 所有候选都"不可写"

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Assert.Equal(pictures, ImageFile.DefaultSaveDir()); // 最终兜底：图片库（保存失败由 SaveFailed 提示）
    }
}
