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
        session.Cancelled += FinishSession;
        _session = session;
        session.Start();
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
