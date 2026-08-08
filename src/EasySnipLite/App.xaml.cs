using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Selection;
using EasySnipLite.Pin;
using EasySnipLite.Stitching;
using EasySnipLite.Tray;

namespace EasySnipLite;

public partial class App : Application
{
    private KeyboardHookService? _hook;
    private readonly ChordDetector _chord = new(TimeSpan.FromMilliseconds(300));
    private SelectionSession? _session;
    private TrayIconService? _tray;
    private Mutex? _mutex; // 单实例，持有引用防 GC
    private readonly List<PinWindow> _pins = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：二次启动提示后退出（M5）
        _mutex = new Mutex(true, "EasySnipLite_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("EasySnipLite 已在运行。", "EasySnipLite",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _tray = new TrayIconService();
        _tray.CaptureRequested += StartCapture;
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
            return;
        }
        // 贴屏穿透全局热键：Ctrl+Shift+P（穿透后鼠标点不到窗口，此为恢复手段）
        if (evt.Type == KeyEventType.KeyDown && evt.VirtualKey == 0x50 /*VK_P*/
            && evt.CtrlDown && evt.ShiftDown)
        {
            Dispatcher.BeginInvoke(TogglePinPassthrough);
        }
    }

    /// <summary>切换全部贴屏窗口穿透（统一开关）。</summary>
    private void TogglePinPassthrough()
    {
        if (_pins.Count == 0) return;
        var next = !_pins[0].IsPassthrough;
        foreach (var pin in _pins) pin.IsPassthrough = next;
    }

    private void StartCapture()
    {
        if (_session is not null) return; // 已在截图流程中
        var session = new SelectionSession();
        session.Completed += result =>
        {
            var region = session.SelectedRegion; // 先取区域(物理像素)再释放会话
            FinishSession(); // 先关闭全屏 Topmost 遮罩，否则会挡住编辑器
            // 延迟到事件处理之外再打开编辑器：在按键事件栈上关闭遮罩窗口后开模态循环会崩溃
            Dispatcher.BeginInvoke(() => OpenEditor(result, region));
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
    private void OpenEditor(BitmapSource image, Int32Rect? region)
    {
        var editor = new Editor.EditorWindow(image);
        editor.PinRequested += pinned => OpenPin(pinned, region);
        editor.ShowDialog();
    }

    /// <summary>打开贴屏窗口：位置=截图原位置(物理像素)，1:1 尺寸；region 缺失时屏幕居中兜底。</summary>
    private void OpenPin(BitmapSource image, Int32Rect? region)
    {
        int x, y;
        if (region is { } r)
        {
            x = r.X;
            y = r.Y;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            x = (int)Math.Round(work.Left + (work.Width - image.PixelWidth) / 2.0);
            y = (int)Math.Round(work.Top + (work.Height - image.PixelHeight) / 2.0);
        }

        var pin = new PinWindow(image, x, y);
        pin.Closed += (_, _) => _pins.Remove(pin);
        _pins.Add(pin);
        pin.Show();
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
        _mutex?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
