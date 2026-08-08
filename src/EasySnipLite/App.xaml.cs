using System.Windows;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Imaging;
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
            FinishSession(); // 先关闭全屏 Topmost 遮罩，否则会挡住编辑器
            // 延迟到事件处理之外再打开编辑器：在按键事件栈上关闭遮罩窗口后开模态循环会崩溃
            Dispatcher.BeginInvoke(() => OpenEditor(result));
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

    /// <summary>Enter/双击确认 → 打开标注编辑器（M3）。托盘应用无主窗口，模态独立显示。</summary>
    private static void OpenEditor(BitmapSource image)
    {
        var editor = new Editor.EditorWindow(image);
        editor.ShowDialog();
    }

    /// <summary>Ctrl+S 保存:SaveFileDialog 选择路径后写 PNG。</summary>
    private static void SaveImage(BitmapSource image) =>
        ImageFile.SavePngWithDialog(image, ImageFile.DefaultFileName());

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
