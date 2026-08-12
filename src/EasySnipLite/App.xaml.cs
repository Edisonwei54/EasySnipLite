using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasySnipLite.Core.Diagnostics;
using EasySnipLite.Core.Hotkeys;
using EasySnipLite.Core.Imaging;
using EasySnipLite.Core.Settings;
using EasySnipLite.Localization;
using EasySnipLite.Pin;
using EasySnipLite.Selection;
using EasySnipLite.SettingsUI;
using EasySnipLite.Stitching;
using EasySnipLite.Tray;

namespace EasySnipLite;

public partial class App : Application
{
    private static readonly TimeSpan ChordWindow = TimeSpan.FromMilliseconds(300);

    private KeyboardHookService? _hook;
    private ChordDetector? _chord;             // 截图热键（双击时序）
    private ComboDetector? _screenshotCombo;   // 截图热键（单击组合，与 _chord 互斥其一）
    private ComboDetector? _passthroughCombo;  // 穿透热键（单击组合）
    private SelectionSession? _session;
    private TrayIconService? _tray;
    private Mutex? _mutex; // 单实例，持有引用防 GC
    private readonly List<PinWindow> _pins = new();
    private Settings _settings = new();
    private bool _startupComplete; // 启动期异常走 Fatal（否则成静默僵尸进程）

    // 录制状态：录制期间自身热键只喂 recorder；设置窗口打开期间屏蔽自身热键
    // volatile：UI 线程写、钩子线程读（x64 下实际良性，一字保险）
    private volatile HotkeyRecorder? _recorder;
    private TaskCompletionSource<HotkeySpec?>? _recordTcs;
    private volatile bool _settingsWindowOpen;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 错误兜底（M7）：任何初始化/运行期未捕获异常都有日志+提示，注册于所有初始化之前
        // 注：lambda 参数名 e 合法遮蔽 OnStartup 的 StartupEventArgs e（C# 允许，阴影仅在 lambda 作用域内）
        DispatcherUnhandledException += (_, e) =>
        {
            if (!_startupComplete)
            {
                // 启动期异常：静默置 Handled 会成无托盘无热键的僵尸进程（AppDomain 钩子收不到已处理异常）
                AppErrors.Fatal(e.Exception, AppResources.UnhandledErrorBody);
                e.Handled = true;
                return;
            }
            AppErrors.Notify(e.Exception, AppResources.UnhandledNotify); // 非致命：气泡+日志，继续运行
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppErrors.Fatal(e.ExceptionObject as Exception ?? new Exception("Unknown error"), AppResources.UnhandledErrorBody);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppErrors.Log(e.Exception); // Task 内异常：仅日志，防静默吞
            e.SetObserved();
        };

        // 设置加载（M6）：先于单实例检查——单实例提示需按配置语言渲染
        _settings = SettingsStore.Load(SettingsStore.DefaultPath());
        LocaleService.SetLocale(_settings.Language); // 单实例提示/托盘等按设置语言渲染

        // 单实例：二次启动提示后退出（M5）
        _mutex = new Mutex(true, "EasySnipLite_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(AppResources.SingleInstanceMsg, "EasySnipLite",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 装配（M6）
        ImageFile.DefaultSaveDirProvider = () => _settings.SaveDirectory;
        RegistryAutoStart.Sync(_settings.Autostart); // 启动同步：设置开但注册表缺失则补写
        BuildDetectors();

        var tray = new TrayIconService();
        _tray = tray;
        AppErrors.TrayNotify = tray.ShowBalloon; // 错误气泡通道（托盘就绪后即可用）
        tray.CaptureRequested += StartCapture;
        tray.SettingsRequested += ShowSettings;
        tray.ExitRequested += Shutdown;
        RebuildTray();
        tray.ShowBalloon(AppResources.AppStarted); // 启动成功气泡（总是显示，含开机自启）

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

        _startupComplete = true;
    }

    /// <summary>按当前设置构建两个热键探测器（截图按 Kind 派发 chord/combo 其一）。</summary>
    private void BuildDetectors()
    {
        var shot = _settings.ResolvedScreenshotHotkey;
        if (shot.Kind == HotkeyKind.Chord)
        {
            _chord = new ChordDetector(ChordWindow, shot.VirtualKey, shot.Modifiers);
            _screenshotCombo = null;
        }
        else
        {
            _chord = null;
            _screenshotCombo = new ComboDetector(shot.VirtualKey, shot.Modifiers);
        }
        var pass = _settings.ResolvedPassthroughHotkey;
        _passthroughCombo = new ComboDetector(pass.VirtualKey, pass.Modifiers);
    }

    private void RebuildTray()
    {
        if (_tray is null) return;
        var hotkeyText = HotkeyFormat.Format(_settings.ResolvedScreenshotHotkey, AppResources.HotkeyDoubleTap);
        _tray.Rebuild(hotkeyText);
    }

    /// <summary>钩子线程事件：录制优先，其次查表分发（截图 chord / 截图 combo / 穿透 combo）。</summary>
    private void OnKey(KeyEvent evt)
    {
        if (_recorder is not null)
        {
            _recorder.HandleKey(evt);
            return;
        }
        if (_settingsWindowOpen) return; // 设置窗口打开期间不触发全局热键
        if (_chord is not null && _chord.HandleKey(evt))
        {
            Dispatcher.BeginInvoke(StartCapture);
            return;
        }
        if (_screenshotCombo is not null && _screenshotCombo.HandleKey(evt))
        {
            Dispatcher.BeginInvoke(StartCapture);
            return;
        }
        if (_passthroughCombo is not null && _passthroughCombo.HandleKey(evt))
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

    /// <summary>设置窗口入口（模态）：录制回调 + 保存回调。</summary>
    private void ShowSettings()
    {
        _settingsWindowOpen = true;
        var win = new SettingsWindow(_settings, RecordHotkeyAsync, ApplySettings);
        win.ShowDialog();
        _settingsWindowOpen = false;

        // 窗口未完成录制就关闭时清理录制状态，避免全局热键被吞
        if (_recorder is not null)
        {
            _recorder = null;
            _recordTcs?.TrySetResult(null);
        }
    }

    /// <summary>录制入口：截图热键(autoDetect:单击/双击自动识别)→Chord 录制；穿透热键→Combo 录制。</summary>
    private Task<HotkeySpec?> RecordHotkeyAsync(HotkeyKind kind) =>
        RecordHotkeyCore(kind, autoDetect: kind == HotkeyKind.Chord);

    /// <summary>录制核心：设置录制状态（OnKey 优先喂 recorder），等待 Hook 线程事件，完成后返回 spec（取消=null）。</summary>
    private async Task<HotkeySpec?> RecordHotkeyCore(HotkeyKind kind, bool autoDetect)
    {
        _recordTcs = new TaskCompletionSource<HotkeySpec?>();
        // 先订阅后发布：避免钩子线程事件在字段赋值前到达（无订阅者导致 TCS 永不完成）
        var recorder = new HotkeyRecorder(kind, ChordWindow, autoDetect);
        recorder.Recorded += spec =>
        {
            _recorder = null;
            _recordTcs.TrySetResult(spec);
        };
        recorder.Cancelled += () =>
        {
            _recorder = null;
            _recordTcs.TrySetResult(null);
        };
        _recorder = recorder;

        // 自动识别：单击在双击窗口到期后由钩子线程定时器敲定为 Combo（与 HandleKey 同线程，无竞态）
        DispatcherTimer? timer = null;
        if (autoDetect && _hook?.Dispatcher is { } hookDispatcher)
        {
            timer = new DispatcherTimer(DispatcherPriority.Normal, hookDispatcher) { Interval = ChordWindow };
            timer.Tick += (_, _) => { if (_recorder is { } r) r.HandleTimeout(DateTime.UtcNow); };
            timer.Start();
        }
        try
        {
            return await _recordTcs.Task; // await 续回 UI 线程（ShowDialog 嵌套 Dispatcher 循环）
        }
        finally
        {
            timer?.Stop();
        }
    }

    /// <summary>保存回调：落盘 + 全局应用（热键/托盘/贴屏步长/语言/自启/保存目录）。</summary>
    private bool ApplySettings(Settings next)
    {
        _settings = next;
        bool saved = true;
        try { SettingsStore.Save(SettingsStore.DefaultPath(), next); }
        catch (Exception ex)
        {
            saved = false;
            AppErrors.Notify(ex, AppResources.SettingsSaveFailed); // 内存态仍更新：本次生效，下次启动重试落盘
        }
        ImageFile.DefaultSaveDirProvider = () => next.SaveDirectory;
        RegistryAutoStart.Sync(next.Autostart);
        LocaleService.SetLocale(next.Language); // 先切语言再重建托盘：AppResources 动态解析，托盘需按新语言渲染
        BuildDetectors();
        RebuildTray();
        foreach (var pin in _pins) pin.ApplyLocale(next.ZoomFactor);
        return saved;
    }

    private void StartCapture()
    {
        if (_session is not null) return; // 已在截图流程中
        var session = new SelectionSession();
        // issue #20：完成(复制并关闭)/保存/贴屏/取消 均由会话内联触发，不再打开独立编辑器窗口
        session.Completed += _ => FinishSession(); // 复制已在会话内完成，遮罩随会话销毁
        session.SaveRequested += result =>
        {
            SaveImage(result);
            FinishSession();
        };
        session.PinRequested += result =>
        {
            var region = session.SelectedRegion; // 先取区域(物理像素)再释放会话
            FinishSession(); // 先关闭全屏 Topmost 遮罩，否则会挡住贴屏窗口
            OpenPin(result, region);
        };
        session.Cancelled += FinishSession;
        _session = session;
        session.Start();
    }

    /// <summary>打开贴屏窗口：位置=截图原位置(物理像素)，1:1 尺寸；region 缺失时屏幕居中兜底。步长取当前设置。</summary>
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

        var pin = new PinWindow(image, x, y, _settings.ZoomFactor);
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
