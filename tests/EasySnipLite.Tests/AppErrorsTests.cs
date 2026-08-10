using EasySnipLite.Core.Diagnostics;

namespace EasySnipLite.Tests;

public class AppErrorsTests : IDisposable
{
    private readonly string _dir;
    private readonly string _originalLogPath;
    private readonly long _originalMaxSize;

    public AppErrorsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "easysniplite-apperrors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _originalLogPath = AppErrors.LogPath;
        _originalMaxSize = AppErrors.MaxLogSize;
        AppErrors.LogPath = Path.Combine(_dir, "error.log");
        AppErrors.MaxLogSize = 1024; // 测试用小上限
    }

    public void Dispose()
    {
        AppErrors.LogPath = _originalLogPath;
        AppErrors.MaxLogSize = _originalMaxSize;
        AppErrors.TrayNotify = null;
        try { Directory.Delete(_dir, recursive: true); } catch { /* 测试清理尽力而为 */ }
    }

    [Fact]
    public void Log_WritesExceptionToFile()
    {
        AppErrors.Log(new InvalidOperationException("boom"));

        Assert.True(File.Exists(AppErrors.LogPath));
        var content = File.ReadAllText(AppErrors.LogPath);
        Assert.Contains("boom", content);
        Assert.Contains("InvalidOperationException", content);
    }

    [Fact]
    public void Log_ExceedsMaxSize_ArchivesOld()
    {
        AppErrors.Log(new Exception(new string('x', 2000))); // 超过 MaxLogSize(1024)
        var first = File.ReadAllText(AppErrors.LogPath);

        AppErrors.Log(new Exception(new string('y', 2000))); // 第二次：先归档再写

        Assert.True(File.Exists(AppErrors.LogPath + ".old"), "旧日志应归档为 error.log.old");
        Assert.Contains("x", File.ReadAllText(AppErrors.LogPath + ".old"));
        Assert.Contains("y", File.ReadAllText(AppErrors.LogPath));
        Assert.Equal(first, File.ReadAllText(AppErrors.LogPath + ".old"));
    }

    [Fact]
    public void Log_UnwritablePath_DoesNotThrow()
    {
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x"); // 占位文件：其下无法创建目录（CreateDirectory 会抛 IOException）
        AppErrors.LogPath = Path.Combine(blocker, "error.log");

        var ex = Record.Exception(() => AppErrors.Log(new Exception("silent")));

        Assert.Null(ex); // 日志尽力而为：任何 IO 失败都静默
    }

    [Fact]
    public void Notify_InvokesTrayNotify_AndLogs()
    {
        string? captured = null;
        AppErrors.TrayNotify = msg => captured = msg;

        AppErrors.Notify(new InvalidOperationException("boom"), "user message");

        Assert.Equal("user message", captured);
        Assert.Contains("boom", File.ReadAllText(AppErrors.LogPath));
    }
}
