# M7 打磨+发布 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 打磨收尾：全局错误处理管线（日志+托盘气泡+致命弹窗）、设置保存失败提示、启动成功气泡、边界小修与补测试、单文件发布冒烟与文档终检。

**Architecture:** 新增 `Core/Diagnostics/AppErrors.cs` 静态错误助手（可注入日志路径/气泡委托，Log/Notify/Fatal 三级），App.OnStartup 注册三个异常钩子；TrayIconService 加 `ShowBalloon` 包装；Settings/ImageFile/LocaleService 小修；verify-m7.ps1 脚本化发布冒烟。

**Tech Stack:** C# 14 / WPF / .NET 10 / xUnit / PowerShell 5.1（验证脚本，纯 ASCII）

**Spec:** `docs/superpowers/specs/2026-08-10-m7-polish-release-design.md`（基线 main = ecf5800，分支 feature/m7-polish-release，spec 已提交 6b54c3b）

## Global Constraints

- 分支：`feature/m7-polish-release`（已创建，勿动 main）
- 单测全绿：176/176 → 预计 188/188（新增 ~12 个）
- build 0 错误 0 警告；每次改码后必须重新 build 再验证
- 验证脚本纯 ASCII（PowerShell 5.1 中文乱码踩坑，M2 坑 4）
- 剪贴板必须在 STA（UI）线程调用
- 低级键盘钩子回调内严禁磁盘 I/O/锁/UI（本计划不新增回调内操作）
- 版本号升 1.0.0（Task 9）；文档同步（README/PROGRESS/CLAUDE.md/1.md）在提交前完成
- 不执行任何分支合并/远程分支删除（用户手动合并）；PR 合并后清本地分支
- 新增文案必须三语齐全（resx 中性=英文 + zh-Hans + zh-Hant），验证键数一致
- resx 新键必须同时在 `AppResources.cs` 加访问器（`Get(key)` 缺键回退键名，T10-M2 惯例）

---

### Task 1: AppResources 三语新增 4 个错误/启动键

> 注意：不新增 `UnhandledErrorTitle`——Fatal 弹窗标题按 T10-M1 惯例硬编码 "EasySnipLite"，避免死键（YAGNI）。

**Files:**
- Modify: `src/EasySnipLite/Localization/AppResources.resx`
- Modify: `src/EasySnipLite/Localization/AppResources.zh-Hans.resx`
- Modify: `src/EasySnipLite/Localization/AppResources.zh-Hant.resx`
- Modify: `src/EasySnipLite/Localization/AppResources.cs`

**Interfaces:**
- Consumes: 现有 `Get(string key)` 私有助手（`Manager.GetString(key) ?? key`）
- Produces: 4 个静态属性 `AppResources.AppStarted / SettingsSaveFailed / UnhandledNotify / UnhandledErrorBody`（后续任务引用）

- [ ] **Step 1: 三个 resx 文件末尾（`</root>` 前）各加 4 个 `<data>` 条目**

中性（英文）：
```xml
  <data name="AppStarted" xml:space="preserve"><value>EasySnipLite is ready — running in the system tray.</value></data>
  <data name="SettingsSaveFailed" xml:space="preserve"><value>Failed to save settings. They still apply for this session and will be retried on next launch.</value></data>
  <data name="UnhandledNotify" xml:space="preserve"><value>An internal error occurred. Details were written to the log.</value></data>
  <data name="UnhandledErrorBody" xml:space="preserve"><value>An unexpected error occurred and the app will exit. Details were written to the log.</value></data>
```
zh-Hans：
```xml
  <data name="AppStarted" xml:space="preserve"><value>EasySnipLite 已启动，正在系统托盘运行。</value></data>
  <data name="SettingsSaveFailed" xml:space="preserve"><value>设置保存失败：本次会话仍生效，下次启动将重试。</value></data>
  <data name="UnhandledNotify" xml:space="preserve"><value>发生内部错误，详情已写入日志。</value></data>
  <data name="UnhandledErrorBody" xml:space="preserve"><value>发生未预期的错误，应用即将退出。详情已写入日志。</value></data>
```
zh-Hant：
```xml
  <data name="AppStarted" xml:space="preserve"><value>EasySnipLite 已啟動，正在系統匣執行。</value></data>
  <data name="SettingsSaveFailed" xml:space="preserve"><value>設定儲存失敗：本次工作階段仍生效，下次啟動將重試。</value></data>
  <data name="UnhandledNotify" xml:space="preserve"><value>發生內部錯誤，詳情已寫入日誌。</value></data>
  <data name="UnhandledErrorBody" xml:space="preserve"><value>發生未預期的錯誤，應用程式即將結束。詳情已寫入日誌。</value></data>
```

- [ ] **Step 2: AppResources.cs 的「设置窗口」区块后加访问器**

```csharp
    // 错误与启动提示（M7）
    public static string AppStarted => Get("AppStarted");
    public static string SettingsSaveFailed => Get("SettingsSaveFailed");
    public static string UnhandledNotify => Get("UnhandledNotify");
    public static string UnhandledErrorBody => Get("UnhandledErrorBody");
```

- [ ] **Step 3: 验证键数一致并 build**

Run: `grep -c "data name" src/EasySnipLite/Localization/AppResources*.resx`
Expected: 三个文件各 76（72+4）
Run: `dotnet build EasySnipLite.slnx`
Expected: 0 error, 0 warning

- [ ] **Step 4: Commit**

```bash
git add src/EasySnipLite/Localization/
git commit -m "feat: 三语新增启动/错误提示资源键(5 键×3, M7)"
```

---

### Task 2: AppErrors 静态助手 + AppErrorsTests（TDD）

**Files:**
- Create: `src/EasySnipLite/Core/Diagnostics/AppErrors.cs`
- Create: `tests/EasySnipLite.Tests/AppErrorsTests.cs`

**Interfaces:**
- Consumes: 无（独立）
- Produces:
  - `AppErrors.LogPath`（`string`，get/set，默认 `%AppData%\EasySnipLite\error.log`）
  - `AppErrors.MaxLogSize`（`long`，get/set，默认 `512 * 1024`）
  - `AppErrors.TrayNotify`（`Action<string>?`，get/set，App 装配注入）
  - `AppErrors.Log(Exception ex)`（void，追加日志，>MaxLogSize 时旧日志改名 `.old` 重建；任何失败静默）
  - `AppErrors.Notify(Exception ex, string message)`（void，Log + `TrayNotify?.Invoke(message)`）
  - `AppErrors.Fatal(Exception ex, string message)`（void，Log + MessageBox + `Application.Current.Shutdown()`；Task 3 使用）

- [ ] **Step 1: 写失败测试**

`tests/EasySnipLite.Tests/AppErrorsTests.cs`：
```csharp
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
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test EasySnipLite.slnx --filter "FullyQualifiedName~AppErrorsTests"`
Expected: FAIL — 编译错误（AppErrors 不存在）

- [ ] **Step 3: 实现 AppErrors**

`src/EasySnipLite/Core/Diagnostics/AppErrors.cs`：
```csharp
using System.IO;
using System.Windows;

namespace EasySnipLite.Core.Diagnostics;

/// <summary>
/// 错误兜底助手（M7）：日志（error.log，超限归档 .old）+ 托盘气泡（App 注入 TrayNotify）+ 致命弹窗退出。
/// 所有路径尽力而为：日志/气泡失败一律静默，绝不让错误处理本身崩溃。
/// </summary>
public static class AppErrors
{
    /// <summary>日志文件路径（测试可注入临时目录）。</summary>
    public static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EasySnipLite", "error.log");

    /// <summary>日志上限：超过则归档 error.log.old 并重建（保留两份防膨胀）。</summary>
    public static long MaxLogSize { get; set; } = 512 * 1024;

    /// <summary>托盘气泡委托（App 装配注入 _tray.ShowBalloon）；未注入时仅记录日志。</summary>
    public static Action<string>? TrayNotify { get; set; }

    public static void Log(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSize)
                File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* 磁盘满/只读/被锁：日志尽力而为，静默 */ }
    }

    /// <summary>非致命错误：记录日志 + 托盘气泡提示（气泡失败静默）。</summary>
    public static void Notify(Exception ex, string message)
    {
        Log(ex);
        try { TrayNotify?.Invoke(message); } catch { /* 气泡尽力而为 */ }
    }

    /// <summary>致命错误：记录日志 + 弹窗告知 + 请求退出（进程可能即将终止，尽力而为）。</summary>
    public static void Fatal(Exception ex, string message)
    {
        Log(ex);
        try
        {
            MessageBox.Show(message, "EasySnipLite", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
        catch { /* 进程即将终止，尽力而为 */ }
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test EasySnipLite.slnx --filter "FullyQualifiedName~AppErrorsTests"`
Expected: 4 passed, 0 failed（编译需 `Core/Diagnostics` 目录已创建）

- [ ] **Step 5: 全量测试 + build 确认**

Run: `dotnet test EasySnipLite.slnx` → 180/180 全绿；`dotnet build EasySnipLite.slnx` → 0 error 0 warning

- [ ] **Step 6: Commit**

```bash
git add src/EasySnipLite/Core/Diagnostics/AppErrors.cs tests/EasySnipLite.Tests/AppErrorsTests.cs
git commit -m "feat: AppErrors 错误兜底助手(日志+气泡+致命退出, 4 单测)"
```

---

### Task 3: 错误处理接线（托盘气泡 + App 三钩子 + 启动气泡 + 保存失败提示）

**Files:**
- Modify: `src/EasySnipLite/Tray/TrayIconService.cs`
- Modify: `src/EasySnipLite/App.xaml.cs`
- Modify: `src/EasySnipLite/Settings/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `AppErrors`（Task 2）、`AppResources` 5 新键（Task 1）
- Produces:
  - `TrayIconService.ShowBalloon(string text)`（void）
  - `App.ApplySettings(Settings)` 返回类型由 `void` 改为 `bool`（落盘成败）
  - `SettingsWindow` 构造第 3 参 `Action<Settings>` → `Func<Settings, bool>`

- [ ] **Step 1: TrayIconService 加 ShowBalloon 包装**

`TrayIconService.cs` 的 `Rebuild` 方法后加：
```csharp
    /// <summary>托盘气泡提示（M7 错误/启动提示通道；Win10 显示为托盘附近气泡）。</summary>
    public void ShowBalloon(string text) =>
        _icon.ShowBalloonTip(3000, "EasySnipLite", text, ToolTipIcon.Info);
```
（`ToolTipIcon` 在 `System.Windows.Forms` 命名空间，文件已 `using System.Windows.Forms;`）

- [ ] **Step 2: App.xaml.cs 注册三个异常钩子（OnStartup 最前）**

`OnStartup` 中 `base.OnStartup(e);` 之后立即插入：
```csharp
        // 错误兜底（M7）：任何初始化/运行期未捕获异常都有日志+提示，注册于所有初始化之前
        // 注：lambda 参数名 e 合法遮蔽 OnStartup 的 StartupEventArgs e（C# 允许，阴影仅在 lambda 作用域内）
        DispatcherUnhandledException += (_, e) =>
        {
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
```

- [ ] **Step 3: App.xaml.cs 注入 TrayNotify + 启动气泡**

原代码：
```csharp
        _tray = new TrayIconService();
        _tray.CaptureRequested += StartCapture;
        _tray.SettingsRequested += ShowSettings;
        _tray.ExitRequested += Shutdown;
        RebuildTray();
```
改为：
```csharp
        var tray = new TrayIconService();
        _tray = tray;
        AppErrors.TrayNotify = tray.ShowBalloon; // 错误气泡通道（托盘就绪后即可用）
        tray.CaptureRequested += StartCapture;
        tray.SettingsRequested += ShowSettings;
        tray.ExitRequested += Shutdown;
        RebuildTray();
        tray.ShowBalloon(AppResources.AppStarted); // 启动成功气泡（总是显示，含开机自启）
```

- [ ] **Step 4: App.xaml.cs ApplySettings 改返回 bool + 保存失败气泡**

原代码：
```csharp
    private void ApplySettings(Settings next)
    {
        _settings = next;
        try { SettingsStore.Save(SettingsStore.DefaultPath(), next); }
        catch { /* 磁盘满/只读/文件被锁等：与 RegistryAutoStart 一致静默忽略；内存态已更新，下次启动再尝试落盘 */ }
        ImageFile.DefaultSaveDirProvider = () => next.SaveDirectory;
```
改为：
```csharp
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
```
（方法末尾 `foreach (var pin in _pins) pin.ApplyLocale(next.ZoomFactor);` 之后补 `return saved;`）

- [ ] **Step 5: SettingsWindow 构造签名与 Save_Click 适配**

`SettingsWindow.xaml.cs`：
```csharp
    private readonly Func<Settings, bool> _apply; // 原 Action<Settings>
    ...
    public SettingsWindow(Settings current, Func<HotkeyKind, Task<HotkeySpec?>> record, Func<Settings, bool> apply)
```
`Save_Click` 改为：
```csharp
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _apply(_draft); // 返回值为落盘成败：失败已由 AppErrors 气泡提示，窗口照常关闭
        _saved = true;  // 必须在 Close 前：Closed 事件据此刻断是否恢复预览语言
        Close();
    }
```

- [ ] **Step 6: build + 全量测试**

Run: `dotnet build EasySnipLite.slnx` → 0 error 0 warning
Run: `dotnet test EasySnipLite.slnx` → 180/180 全绿

- [ ] **Step 7: Commit**

```bash
git add src/EasySnipLite/Tray/TrayIconService.cs src/EasySnipLite/App.xaml.cs src/EasySnipLite/Settings/SettingsWindow.xaml.cs
git commit -m "feat: 错误处理接线(三异常钩子/托盘气泡注入/启动气泡/设置保存失败提示)"
```

---

### Task 4: Settings.ValidSpec 修饰键位校验 + 单测（TDD）

**Files:**
- Modify: `src/EasySnipLite/Core/Settings/Settings.cs`
- Modify: `tests/EasySnipLite.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `HotkeyModifiers`（`[Flags]` Ctrl=1/Shift=2/Alt=4，HotkeySpec.cs）
- Produces: 无新公开 API（ValidSpec 行为扩展：非法修饰键位 → 回退默认）

- [ ] **Step 1: 写失败测试**

`SettingsStoreTests.cs` 的 `ModifierKeyAsTargetKey_NormalizesToDefault` 测试后加：
```csharp
    [Fact]
    public void InvalidModifierBits_NormalizesToDefault()
    {
        var path = TempPath();
        File.WriteAllText(path, """{"PassthroughHotkey":{"Kind":1,"Modifiers":999,"VirtualKey":80}}"""); // 999 含非法标志位

        var loaded = SettingsStore.Load(path);

        Assert.Equal(HotkeySpec.DefaultPassthrough, loaded.ResolvedPassthroughHotkey);
    }
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test EasySnipLite.slnx --filter "FullyQualifiedName~InvalidModifierBits"`
Expected: FAIL（999 当前会通过 ValidSpec）

- [ ] **Step 3: 实现位掩码校验**

`Settings.cs` 原代码：
```csharp
    private static HotkeySpec? ValidSpec(HotkeySpec? spec) =>
        spec is { VirtualKey: > 0 } && Enum.IsDefined(spec.Kind) && !ModifierKey.IsModifier(spec.VirtualKey)
            ? spec
            : null;
```
改为：
```csharp
    private static HotkeySpec? ValidSpec(HotkeySpec? spec) =>
        spec is { VirtualKey: > 0 } && Enum.IsDefined(spec.Kind) && !ModifierKey.IsModifier(spec.VirtualKey)
        && ValidModifiers(spec.Modifiers)
            ? spec
            : null;

    /// <summary>Flags 枚举不能用 Enum.IsDefined（组合值如 Ctrl|Shift=3 未定义），必须位掩码校验。</summary>
    private static bool ValidModifiers(HotkeyModifiers m) =>
        (m & ~(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt)) == 0;
```

- [ ] **Step 4: 运行确认通过 + 全量**

Run: `dotnet test EasySnipLite.slnx --filter "FullyQualifiedName~InvalidModifierBits"` → PASS
Run: `dotnet test EasySnipLite.slnx` → 181/181；`dotnet build EasySnipLite.slnx` → 0/0

- [ ] **Step 5: Commit**

```bash
git add src/EasySnipLite/Core/Settings/Settings.cs tests/EasySnipLite.Tests/SettingsStoreTests.cs
git commit -m "fix: Settings 修饰键位掩码校验(污染 JSON 非法位回退默认)"
```

---

### Task 5: LocaleService.LocaleChanged 死事件移除

**Files:**
- Modify: `src/EasySnipLite/Localization/LocaleService.cs`

**Interfaces:**
- Consumes: 无
- Produces: `LocaleService` 仅剩 `ResolveCulture` + `SetLocale`（SetLocale 语义不变：设 CurrentUICulture，不再广播——确认无订阅者后删除）

- [ ] **Step 1: 确认零订阅者**

Run: `grep -rn "LocaleChanged" src tests`
Expected: 仅 `LocaleService.cs` 两处（定义 + `?.Invoke()`）；无 src/tests 订阅者（M6 历史设计/计划文档是存档记录，保留原样不改写）

- [ ] **Step 2: 删除事件**

`LocaleService.cs`：
```csharp
public static class LocaleService
{
    public static event Action? LocaleChanged;   // ← 删除此行

    /// <summary>纯逻辑映射（installed 注入以便测试 System 分支）。</summary>
    public static CultureInfo ResolveCulture(...)  // 不变
    ...

    public static void SetLocale(AppLanguage lang)
    {
        CultureInfo.CurrentUICulture = ResolveCulture(lang, CultureInfo.InstalledUICulture);
        LocaleChanged?.Invoke();                   // ← 删除此行
    }
}
```

- [ ] **Step 3: build + 全量测试**

Run: `dotnet build EasySnipLite.slnx` → 0/0；`dotnet test EasySnipLite.slnx` → 181/181（LocaleServiceTests 只测 ResolveCulture，不受影响）

- [ ] **Step 4: Commit**

```bash
git add src/EasySnipLite/Localization/LocaleService.cs
git commit -m "refactor: 移除 LocaleChanged 零订阅者死事件(YAGNI)"
```

---

### Task 6: ImageFile 可写性探测 + ImageFileTests（TDD）

**Files:**
- Modify: `src/EasySnipLite/Core/Imaging/ImageFile.cs`
- Create: `tests/EasySnipLite.Tests/ImageFileTests.cs`

**Interfaces:**
- Consumes: `ImageFile.DefaultSaveDirProvider`（`Func<string?>?`，现有）
- Produces:
  - `ImageFile.WriteProbe`（`Func<string, bool>?`，get/set，测试注入；null = 真实 IO 探测）
  - `ImageFile.DefaultSaveDir()` 行为：候选目录逐个 CreateDirectory+可写性探测，不可写则回退下一候选；最终回退图片库

- [ ] **Step 1: 写失败测试**

`tests/EasySnipLite.Tests/ImageFileTests.cs`：
```csharp
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
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test EasySnipLite.slnx --filter "FullyQualifiedName~ImageFileTests"`
Expected: FAIL（WriteProbe 属性不存在，编译错误）

- [ ] **Step 3: 实现探测**

`ImageFile.cs` 原代码：
```csharp
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
```
改为：
```csharp
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
```

- [ ] **Step 4: 运行确认通过 + 全量**

Run: `dotnet test EasySnipLite.slnx --filter "FullyQualifiedName~ImageFileTests"` → 2 passed
Run: `dotnet test EasySnipLite.slnx` → 183/183；`dotnet build EasySnipLite.slnx` → 0/0

- [ ] **Step 5: Commit**

```bash
git add src/EasySnipLite/Core/Imaging/ImageFile.cs tests/EasySnipLite.Tests/ImageFileTests.cs
git commit -m "fix: ImageFile 可写性探测(已存在只读目录回退下一候选, 2 单测)"
```

---

### Task 7: 检测器/录制器补测试（Minor 账本，仅测试）

**Files:**
- Modify: `tests/EasySnipLite.Tests/ChordDetectorTests.cs`
- Modify: `tests/EasySnipLite.Tests/ComboDetectorTests.cs`
- Modify: `tests/EasySnipLite.Tests/HotkeyRecorderTests.cs`

**Interfaces:**
- Consumes: 现有 `ChordDetector`/`ComboDetector`/`HotkeyRecorder` 公开 API + 各测试文件既有助手（`SpaceUp`/`Down`/`Up` 等）
- Produces: 无代码变更（纯补测试，验证既有行为）

- [ ] **Step 1: ChordDetectorTests 加 2 测试（文件末尾 `CustomTargetKey_Fires` 后）**

```csharp
    [Fact]
    public void ModifierMissingOnSecondTap_DoesNotFire_AndDoesNotPolluteTiming()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(SpaceUp(false, t0 + TimeSpan.FromMilliseconds(100)))); // Ctrl 缺失：不触发
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(200))));   // 时序未被污染：与第一击仍在窗口内
    }

    [Fact]
    public void InterleavedModifierKeyUp_BetweenTaps_DoesNotPollute()
    {
        var detector = new ChordDetector(Window);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(detector.HandleKey(SpaceUp(true, t0)));
        Assert.False(detector.HandleKey(new(KeyEventType.KeyUp, VkShift, true, false, false, t0 + TimeSpan.FromMilliseconds(50)))); // Shift 释放夹杂
        Assert.True(detector.HandleKey(SpaceUp(true, t0 + TimeSpan.FromMilliseconds(100))));
    }
```
（`VkShift` 常量文件内已有，第 9 行。）

- [ ] **Step 2: ComboDetectorTests 加 1 测试（`CustomSpec_FiresOnItsOwnCombo` 后）**

```csharp
    [Fact]
    public void ExtraModifierBeyondDeclared_DoesNotFire()
    {
        var detector = Default(); // 声明 Ctrl+Shift
        Assert.False(detector.HandleKey(Down(VkP, ctrl: true, shift: true, alt: true))); // 多按 Alt：精确匹配拒绝
    }
```

- [ ] **Step 3: HotkeyRecorderTests 加 2 测试（`AutoMode_Esc_StillCancels` 后）**

```csharp
    [Fact]
    public void ComboMode_KeyUp_DoesNotRecordTwice()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Combo, Window);
        var count = 0;
        recorder.Recorded += _ => count++;

        recorder.HandleKey(Down(VkCtrl));
        recorder.HandleKey(Down(VkP, ctrl: true));
        recorder.HandleKey(Up(VkP, ctrl: true));

        Assert.Equal(1, count); // 仅 KeyDown 记录一次，KeyUp 不重复
    }

    [Fact]
    public void Cancel_ThenRecordingWorksAgain()
    {
        var recorder = new HotkeyRecorder(HotkeyKind.Combo, Window);
        var cancelled = false;
        var recorded = false;
        recorder.Cancelled += () => cancelled = true;
        recorder.Recorded += _ => recorded = true;

        recorder.HandleKey(Down(VkEsc));           // 取消
        recorder.HandleKey(Down(VkCtrl));
        recorder.HandleKey(Down(VkP, ctrl: true)); // 取消后仍可正常录制

        Assert.True(cancelled);
        Assert.True(recorded);
    }
```

- [ ] **Step 4: 全量测试**

Run: `dotnet test EasySnipLite.slnx`
Expected: 188/188 全绿（180+1+2+2+1+2 = 188）

- [ ] **Step 5: Commit**

```bash
git add tests/EasySnipLite.Tests/ChordDetectorTests.cs tests/EasySnipLite.Tests/ComboDetectorTests.cs tests/EasySnipLite.Tests/HotkeyRecorderTests.cs
git commit -m "test: 检测器/录制器补测试(修饰键缺失/多余/交错/KeyUp 不重复/取消后可再录)"
```

---

### Task 8: verify-m7.ps1 单文件发布冒烟

**Files:**
- Create: `tools/verify-m7.ps1`

**Interfaces:**
- Consumes: dist 发布产物（`dotnet publish -c Release -r win-x64 -o dist`）、全局热键 Ctrl+双击空格、剪贴板
- Produces: `tools/verify-m7-result.txt` 结果日志；退出码 0=通过

- [ ] **Step 1: 写脚本（纯 ASCII）**

`tools/verify-m7.ps1`：
```powershell
# M7 smoke: publish single-file -> run dist exe -> hotkey capture -> clipboard -> no error.log
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$log = 'D:\EasySnipLite\tools\verify-m7-result.txt'
Remove-Item $log -ErrorAction SilentlyContinue
function Log($msg) { Add-Content -Path $log -Value $msg; Write-Host $msg }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinEnum {
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder t, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    public static string[] List(int targetPid) {
        var res = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid) {
                var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
                res.Add(String.Format("{0}|{1}|{2}", h.ToInt64(), IsWindowVisible(h) ? 1 : 0, sb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return res.ToArray();
    }
}
'@

Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
'@ -Name Native -Namespace V

$dist = 'D:\EasySnipLite\dist'
$exe = Join-Path $dist 'EasySnipLite.exe'

# 1. publish single-file
Log 'STEP: publish single-file (Release, win-x64)...'
& dotnet publish 'D:\EasySnipLite\src\EasySnipLite' -c Release -r win-x64 -o $dist | Out-Null
if ($LASTEXITCODE -ne 0) { Log "FAIL: publish exit=$LASTEXITCODE"; exit 1 }
if (-not (Test-Path $exe)) { Log 'FAIL: dist exe missing'; exit 1 }
if (Test-Path (Join-Path $dist 'EasySnipLite.dll')) { Log 'FAIL: EasySnipLite.dll present (not single-file)'; exit 1 }
Log 'OK: published single-file exe'

# 2. clean error.log from previous runs
$errorLog = Join-Path $env:APPDATA 'EasySnipLite\error.log'
Remove-Item $errorLog -ErrorAction SilentlyContinue

# 3. start dist exe
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 2
if ($proc.HasExited) { Log "FAIL: app exited code=$($proc.ExitCode)"; exit 1 }
Log "OK: app started pid=$($proc.Id)"

# 4. Ctrl + double-tap Space -> capture overlay
$KEYUP = 2; $VK_CTRL = 0x11; $VK_SPACE = 0x20
[V.Native]::keybd_event($VK_CTRL, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
[V.Native]::keybd_event($VK_SPACE, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event($VK_SPACE, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 100
[V.Native]::keybd_event($VK_CTRL, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1

$wins = [WinEnum]::List($proc.Id)
$overlay = $wins | Where-Object { $_ -match '^[0-9]+\|1\|EasySnipLite$' }
if ($overlay.Count -eq 0) { Log "FAIL: no visible overlay. all=$($wins -join '; ')"; Stop-Process -Id $proc.Id -Force; exit 1 }
Log "OK: overlay visible (count=$($overlay.Count))"

# 5. drag 300x200 region and confirm with Enter
$x0 = 400; $y0 = 300
[V.Native]::SetCursorPos($x0, $y0)
Start-Sleep -Milliseconds 150
[V.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
for ($i = 1; $i -le 10; $i++) {
    [V.Native]::SetCursorPos($x0 + 30 * $i, $y0 + 20 * $i)
    Start-Sleep -Milliseconds 20
}
[V.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
Start-Sleep -Milliseconds 300
Log 'OK: drag done'
[V.Native]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
[V.Native]::keybd_event(0x0D, 0, $KEYUP, [UIntPtr]::Zero)
Start-Sleep -Seconds 1

if ($proc.HasExited) { Log "FAIL: app crashed after Enter code=$($proc.ExitCode)"; exit 1 }
$winsAfter = [WinEnum]::List($proc.Id)
$overlayAfter = $winsAfter | Where-Object { $_ -match '^[0-9]+\|1\|EasySnipLite$' }
if ($overlayAfter.Count -gt 0) { Log 'FAIL: overlay still open after Enter'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: overlay closed after Enter'

# 6. clean run must NOT create error.log
if (Test-Path $errorLog) { Log 'FAIL: error.log created on clean run'; Stop-Process -Id $proc.Id -Force; exit 1 }
Log 'OK: no error.log on clean run'

# kill app first, then read clipboard (proves data physically copied)
Stop-Process -Id $proc.Id -Force
Start-Sleep -Milliseconds 500

# 7. clipboard verification
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) {
    Log 'FAIL: no image in clipboard'
    $formats = [System.Windows.Forms.Clipboard]::GetDataObject().GetFormats() -join ','
    Log "INFO: formats: $formats"
    exit 1
}
Log "OK: clipboard image $($img.Width)x$($img.Height)"
$out = 'D:\EasySnipLite\tools\m7-capture.png'
$img.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Log "OK: saved $out"
Log 'M7 verification PASSED'
```

- [ ] **Step 2: 运行冒烟（需解锁桌面；脚本自 publish，约 1-2 分钟）**

Run: `powershell -ExecutionPolicy Bypass -File tools/verify-m7.ps1`
Expected: 全部 OK 行，结尾 `M7 verification PASSED`，退出码 0
注意：运行前 `tasklist | grep EasySnipLite` 确认无残留进程（M3 坑 6）；锁屏会吞合成输入（M2 坑 4）

- [ ] **Step 3: Commit**

```bash
git add tools/verify-m7.ps1
git commit -m "test: verify-m7 单文件发布冒烟(publish→热键→剪贴板→无异常日志)"
```

---

### Task 9: 版本 1.0.0 + 文档终检 + 全量验证 + 收尾提交

**Files:**
- Modify: `src/EasySnipLite/EasySnipLite.csproj`
- Modify: `README.md`
- Modify: `docs/PROGRESS.md`
- Modify: `CLAUDE.md`
- Modify: `1.md`

**Interfaces:**
- Consumes: 全部分支实现（Task 1-8）；真实测试数 N（预期 188）
- Produces: 与仓库一致的最终文档；可提交状态

- [ ] **Step 1: 版本号升 1.0.0**

`EasySnipLite.csproj`：`<Version>0.1.0</Version>` → `<Version>1.0.0</Version>`
Run: `dotnet build EasySnipLite.slnx` → 0/0

- [ ] **Step 2: README.md 终检**

- 顶部进度块引用行更新：追加 M7 完成描述（单测 N/N，发布冒烟验证通过）
- 功能表「托盘常驻」行补启动/错误气泡提示；「设置」行不变
- 里程碑表 M7 行：`| M7 | 打磨 + 单文件发布 | ✅ 完成（单测 N/N，verify-m7 冒烟验证通过）|`
- 新增「发布」小节（放「快速开始」后，`## 📦 发布` + 缩进代码块，避免嵌套围栏）：
```markdown
## 📦 发布

    # 单文件发布（self-contained，含 .NET 运行时，产物在 dist/）
    dotnet publish src/EasySnipLite -c Release -r win-x64 -o dist

    # 发布冒烟验证（热键→框选→剪贴板，需解锁桌面）
    powershell -ExecutionPolicy Bypass -File tools/verify-m7.ps1
```
- 「参考资料」更新模型名不变；确认无其他陈旧表述（如 M7 待开发）

- [ ] **Step 3: docs/PROGRESS.md 终检**

- 「已完成」末尾新增 M7 节（交付描述：错误处理管线三钩子/启动气泡/保存失败气泡/ImageFile 探测/Settings 位校验/LocaleChanged 移除/补测试 12 个/verify-m7 冒烟；新增文件 Core/Diagnostics/AppErrors.cs + tests AppErrorsTests/ImageFileTests；修改清单；踩坑记录——按实际发生如实记录，无坑则写「无新增踩坑」）
- 「接下来要做什么」表：M7 行标 `✅ 已完成（单测 N/N，发布冒烟通过）`，附「发布说明见 README」；表格可加注释行「M0-M7 全部完成」
- 「五、总计划」流程段更新：发布冒烟已加入里程碑验证

- [ ] **Step 4: CLAUDE.md 终检**

- 常用命令加：`powershell -ExecutionPolicy Bypass -File tools/verify-m7.ps1   # M7 发布冒烟（publish 单文件→热键→剪贴板）`
- 目录结构 Core 行补 `Diagnostics/（AppErrors 错误兜底：日志/气泡/致命弹窗）`
- 现状节：`M7 打磨+发布 ✅（错误处理管线/启动+保存失败气泡/边界小修/单文件 publish 冒烟/版本 1.0.0；单测 N/N 全绿，verify-m7 通过）`，并标注「全部里程碑完成，进入维护期」

- [ ] **Step 5: 1.md 收尾总结**

追加本次对话总结块（与历次格式一致：项目原本有什么/本次对话做了什么/结果新增了什么/现在项目怎么样了/下一步需要做什么）。「下一步」：项目完结——维护期（缺陷修复/新功能建议），无需新里程碑规划。

- [ ] **Step 6: 最终验证 + 收尾提交**

Run: `dotnet build EasySnipLite.slnx` → 0 error 0 warning
Run: `dotnet test EasySnipLite.slnx` → N/N 全绿
Run: `powershell -ExecutionPolicy Bypass -File tools/verify-m7.ps1` → PASSED（若 Task 8 已跑且代码未变可跳过，但版本号变更需重跑一次确认）
Run: `git status` 确认仅计划内文件变更
```bash
git add -A
git commit -m "release: v1.0.0 发布(版本号/文档终检/里程碑完结)"
```

- [ ] **Step 7: 推送分支 + 创建 PR（合并由用户手动执行）**

```bash
git push -u origin feature/m7-polish-release
gh pr create --title "M7 打磨+发布" --body "错误处理管线(日志+气泡+致命弹窗)/启动与保存失败气泡/边界小修(ImageFile 探测/Settings 位校验)/补测试 12 个(单测 188/188)/verify-m7 发布冒烟通过/版本 1.0.0
🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
注意：创建 PR 后停下，等用户手动合并；合并后用户会自行清本地分支（或按规则 `git branch -d feature/m7-polish-release`）
