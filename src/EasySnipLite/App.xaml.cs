using System.Windows;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Selection;
using EasySnipLite.Stitching;
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
        _tray.LongCaptureRequested += StartLongCapture;
        _tray.ExitRequested += Shutdown;

        _hook = new KeyboardHookService();
        _hook.KeyReceived += OnKey;
        _hook.Start();

        // M4 验证脚本入口:--longcapture x y w h 对指定屏幕区域直接跑长截图
        var args = e.Args;
        if (args.Length == 5 && args[0] == "--longcapture"
            && int.TryParse(args[1], out int x) && int.TryParse(args[2], out int y)
            && int.TryParse(args[3], out int w) && int.TryParse(args[4], out int h)
            && w > 0 && h > 0)
        {
            Dispatcher.BeginInvoke(() => OpenStitch(new Int32Rect(x, y, w, h), autoCopy: true));
        }
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

    /// <summary>M4 滚动长截图:先框选目标区域,完成后对区域自动滚动捕获。</summary>
    private void StartLongCapture()
    {
        if (_session is not null) return; // 已在截图流程中
        var session = new SelectionSession();
        session.Completed += _ =>
        {
            var region = session.SelectedRegion;
            FinishSession(); // 先关遮罩(否则挡住预览窗口)
            Dispatcher.BeginInvoke(() => OpenStitch(region));
        };
        session.Cancelled += FinishSession;
        _session = session;
        session.Start();
    }

    /// <summary>对指定屏幕区域(物理像素)打开滚动捕获预览窗口。</summary>
    private static void OpenStitch(Int32Rect? region, bool autoCopy = false)
    {
        if (region is not { } r || r.Width <= 0 || r.Height <= 0) return;
        var win = new StitchPreviewWindow(r.X, r.Y, r.Width, r.Height, autoCopy);
        win.ShowDialog();
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
