using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.ClipboardServices;
using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Selection;
using EasySnipLite.Tray;

namespace EasySnipLite;

public partial class App : Application
{
    private KeyboardHookService? _hook;
    private readonly ChordDetector _chord = new(TimeSpan.FromMilliseconds(300));
    private SelectionSession? _session;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _tray = new TrayIconService();
        _tray.CaptureRequested += StartCapture;
        _tray.ExitRequested += Shutdown;

        _hook = new KeyboardHookService();
        _hook.KeyReceived += OnKey;
        _hook.Start();
    }

    /// <summary>钩子线程事件；双击判定后切回 UI 线程启动截图。</summary>
    private void OnKey(KeyEvent evt)
    {
        if (_chord.HandleKey(evt))
        {
            Dispatcher.BeginInvoke(StartCapture);
        }
    }

    private void StartCapture()
    {
        if (_session is not null) return; // 已在截图流程中
        var session = new SelectionSession();
        session.Completed += result =>
        {
            ClipboardEx.SetImage(result);
            FinishSession();
        };
        session.SaveRequested += result =>
        {
            SaveImage(result);
            FinishSession();
        };
        session.Cancelled += FinishSession;
        _session = session;
        session.Start();
    }

    /// <summary>Ctrl+S 保存:SaveFileDialog 选择路径后写 PNG。</summary>
    private static void SaveImage(BitmapSource image)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG 图片 (*.png)|*.png",
            DefaultExt = ".png",
            FileName = $"EasySnipLite_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            InitialDirectory = DefaultSaveDir(),
        };
        if (dialog.ShowDialog() != true) return;

        using var stream = File.Create(dialog.FileName);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    private static string DefaultSaveDir()
    {
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

    private void FinishSession()
    {
        _session?.Dispose();
        _session = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
