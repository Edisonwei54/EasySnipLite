# M7 打磨+发布 — 设计文档（2026-08-10）

分支：`feature/m7-polish-release`；基线 main = ecf5800（M6 完成，单测 176/176）

## 一、目标与范围

M7 = 打磨 + 单文件发布。范围（用户已逐项确认，全选 A+B+C+D）：

- **A 设置保存失败提示**：目前 `App.ApplySettings` 落盘失败被静默忽略（App.xaml.cs:202），改为用户可见提示
- **B 全局异常兜底**：当前无任何未捕获异常处理（async void 回调崩溃会终止进程），补非致命/致命两级兜底
- **C 边界小修 + 补测试**：ImageFile 只读目录探测、Settings.Normalize 修饰键位校验、录制器补测试、LocaleChanged 死代码清理
- **D 发布冒烟 + 文档终检**：单文件 publish 脚本化冒烟（verify-m7.ps1）、版本升 1.0.0、README/PROGRESS/CLAUDE.md/1.md 终检

另经用户确认追加：**应用启动成功气泡**（总是显示，含开机自启场景）。

**非目标（YAGNI 明确排除）**：
- 自制 WPF toast 窗口（已选托盘气泡，零新 UI）
- 开机自启 vs 手动启动的区分提示（用户选总是显示，不改 RegistryAutoStart 键值格式）
- 全局快捷键占用冲突的系统级探测（超出打磨范围）
- M4 滚动长截图捡起（维持跳过）

## 二、错误处理管线（A+B 基础）

### 2.1 新增 `Core/Diagnostics/AppErrors.cs`（静态助手）

```csharp
public static class AppErrors
{
    public static Action<string>? TrayNotify { get; set; }  // App 装配注入 _tray.ShowBalloon
    public static string LogPath { get; set; }               // 默认 %AppData%\EasySnipLite\error.log；测试可注入临时目录
    public static void Log(Exception ex);                    // 追加写日志
    public static void Notify(Exception ex, string message); // Log + TrayNotify（气泡）
    public static void Fatal(Exception ex);                  // Log + MessageBox + 退出
}
```

- **日志**：`error.log` 与 settings.json 同目录（`%AppData%\EasySnipLite\`）。追加写：时间戳（`yyyy-MM-dd HH:mm:ss`）+ 异常 ToString()（含内部异常）+ 空行分隔。**大小上限 512KB**：超过则改名 `error.log.old` 重建（防单文件无限膨胀；旧日志保留一份供排障）。写入失败（只读/磁盘满）静默忽略——日志是尽力而为，不因日志失败崩溃。
- **气泡**：`TrayNotify` 为可空委托，App 装配 `_tray.ShowBalloon(title, message)`；未注入（测试/异常早期）时仅 Log。气泡标题 `EasySnipLite`，正文为本地化文案（AppResources 新增键）。
- **致命退出**：`Fatal` 在 MessageBox 后调 `Application.Current.Shutdown()`（AppDomain 未处理异常场景进程即将终止，弹窗是最后告知手段）。

### 2.2 钩子点（App.OnStartup 注册）

| 钩子 | 分类 | 行为 |
|------|------|------|
| `DispatcherUnhandledException` | 非致命（UI 回调内异常） | `Notify`（气泡+日志）；`e.Handled = true` 继续运行 |
| `AppDomain.UnhandledException` | 致命（后台线程等崩溃） | `Fatal`（日志+弹窗，进程随后自然终止） |
| `TaskScheduler.UnobservedTaskException` | 静默（Task 内异常） | 仅 `Log`（防静默吞；不气泡不弹窗，避免刷屏） |

注册时机：OnStartup 最前（单实例检查之前），保证任何后续初始化异常都有兜底。

### 2.3 设置保存失败（A）

- `App.ApplySettings(Settings next)` 改为返回 `bool`（落盘成功与否）；catch 分支：`AppErrors.Notify(ex, AppResources.SettingsSaveFailed)`（气泡「设置保存失败，将在下次启动时重试」），**内存态仍更新**（保留现状语义：本次会话生效，下次启动再尝试落盘）。
- `SettingsWindow.Save_Click` 不再关心返回值（气泡已提示，窗口照常关闭）——签名由 Action 改 Func 仅一处调用点。

### 2.4 启动成功气泡（用户追加）

- OnStartup 托盘构建完成后：`_tray.ShowBalloon(AppResources.AppStarted)`（「EasySnipLite 已启动，常驻托盘」）。
- **总是显示**（用户确认），含开机自启场景；不改 RegistryAutoStart 键值。
- ShowBalloonTip 需要图标已显示：置于 RebuildTray() 之后、钩子启动之前。
- 非目标：不区分手动/自启启动。

### 2.5 TrayIconService.ShowBalloon 包装

```csharp
public void ShowBalloon(string text) =>
    _icon?.ShowBalloonTip(3000, "EasySnipLite", text, ToolTipIcon.Info);
```

`_icon` 为 null（未初始化）时静默忽略。标题品牌名硬编码（M6 惯例 T10-M1：MessageBox 标题同样硬编码）。

## 三、边界小修（C）

### 3.1 ImageFile 可写性探测（T11-M1）

问题：`Directory.CreateDirectory` 对**已存在的只读目录**不抛异常 → `DefaultSaveDir` 原样返回只读目录 → 保存时才失败。

修：CreateDirectory 成功后**探测可写**——创建 `Path.Combine(dir, ".writable-probe")` 临时文件再删除；catch（UnauthorizedAccessException 等）→ 该候选不可用，走下一候选。候选顺序不变：设置目录 → 图片库\EasySnipLite → 图片库。

### 3.2 Settings.Normalize 修饰键位校验（Minor）

问题：`ValidSpec` 校验了 Kind/目标键，但 `Modifiers` 是 `[Flags]` 枚举，污染 JSON 的非法位（如 999）会漏过。Flags 枚举**不能用 `Enum.IsDefined`**（组合值 3 = Ctrl|Shift 未定义），必须位掩码：

```csharp
private static bool ValidModifiers(HotkeyModifiers m) =>
    (m & ~(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt)) == 0;
```

`ValidSpec` 增加 `&& ValidModifiers(spec.Modifiers)`；非法 → 回退默认（null）。补 1 单测（非法位回退默认）。

### 3.3 LocaleService.LocaleChanged 删除（Minor）

事件零订阅者（grep 确认 src/ 仅定义+触发，无订阅；文档 3 处提及同步更新）。删除事件声明与 `LocaleChanged?.Invoke()`，`SetLocale` 保留（设置语言+CurrentUICulture 的语义不变）。YAGNI：需要时再引入，避免死 API。

### 3.4 单实例提示语言

M6 已解决（`SetLocale` 先于 mutex 检查，App.xaml.cs:43），无代码改动；verify-m7 冒烟时回归确认。

## 四、补测试（C，Minor 账本）

| 文件 | 新增用例 | 覆盖 Minor |
|------|----------|-----------|
| ChordDetectorTests | 声明修饰键缺失不触发（双击但无 Ctrl） | T1-M1 |
| ChordDetectorTests | 交错修饰键（按下 Ctrl 前先按下目标键）不触发 | T1-M2 |
| ComboDetectorTests | 多余修饰键按下不触发（精确匹配守卫） | T2-M1 |
| HotkeyRecorderTests | Combo 模式 KeyUp 在双击窗口边界的行为 | T3-M3 |
| HotkeyRecorderTests | 取消后状态清除（可再次录制） | T3-M4 |
| SettingsStoreTests | 非法 Modifiers 位（如 999）往返 → Normalize 回退默认 | T4-M3 |
| AppErrorsTests（新） | Log 写文件+截断重建（注入临时目录） | — |

预计 +7~9 个单测（176 → ~184）。

## 五、发布冒烟（D）

### 5.1 verify-m7.ps1（纯 ASCII，沿用 verify-m1 模式）

流程：
1. `dotnet publish src/EasySnipLite -c Release -r win-x64 -o dist`（PublishSingleFile/self-contained 已在 csproj）
2. 断言 `dist/EasySnipLite.exe` 存在且为单文件（目录内无 EasySnipLite.pdb 残留可忽略——DebugType=embedded 已内嵌；断言无 `EasySnipLite.dll`）
3. 启动 dist exe → 等待就绪（窗口/进程存活）
4. 断言**启动气泡**（人工不可脚本断言，改为断言进程存活+热键链路——气泡属人工复验项）
5. 热键唤起截图（Ctrl+双击空格，与 verify-m1 同法：KeyDown/KeyUp 合成）→ 拖拽 300x200 → Enter → 剪贴板读回 300x200
6. 断言 `%AppData%\EasySnipLite\error.log` 未生成（正常路径无异常）
7. 关闭 app（托盘 Exit 或 kill 进程）→ 清理

脚本化范围说明：气泡显示属 shell UI 无法脚本断言，verify-m7 验证的是**发布产物可用**（进程+热键+剪贴板+无异常日志）；启动气泡/设置保存失败气泡由人工复验。

### 5.2 版本号

- csproj `<Version>0.1.0</Version>` → `1.0.0`（冒烟通过后、提交前）。

### 5.3 文档终检

| 文档 | 更新点 |
|------|--------|
| README.md | M7 行「✅ 完成（单测 N/N，冒烟验证）」、「发布」小节（dist 构建命令 + verify-m7）、功能表终检（热键/设置行提启动气泡与错误提示）、进度表 M7 行 |
| docs/PROGRESS.md | 「已完成」新增 M7 节（交付/新增修改/踩坑）、「接下来要做什么」表 M7 行标完成（仅剩"发布"描述）、五、总计划更新 |
| CLAUDE.md | 现状 M7 ✅（或 M7 完成描述）、常用命令补 verify-m7 / publish 说明 |
| 1.md | 收尾总结（项目原本有什么/本次对话做了什么/结果新增/现在项目/下一步） |
| 本文档 | spec 保持与最终实现一致 |

## 六、测试/验证纪律

1. TDD 先行：AppErrors.Log（路径注入+临时目录）、Settings 位校验、ImageFile 探测（只读属性临时目录，Windows 有效）、录制器补测
2. 每任务 `dotnet build` 0 错误 0 警告；任务完成后 `dotnet test` 全绿（约 184）
3. 全部分支实现完成后：build → 单测 → verify-m7.ps1 冒烟 → 人工复验（启动气泡/设置保存失败气泡/设置窗口回归）
4. 文档同步（五、5.3 表）→ 提交 → 推送 → 创建 PR（合并由用户手动执行，遵守 CLAUDE.md 规则 9/10）

## 七、风险与已知边界

- **DispatcherUnhandledException 继续运行**：回调级异常置 Handled 后状态可能不完整（如某窗口半初始化）——打磨取舍：托盘应用可恢复性优先；致命态由 AppDomain 钩子兜底。人工冒烟含「正常路径无异常」断言。
- **气泡在设置窗口模态期间**：ShowBalloonTip 是 shell UI，模态窗口不遮挡；保存失败气泡在 Save_Click 同步路径触发，用户可立即看到。
- **error.log 截断**：仅保留 `error.log` + `error.log.old` 两份，避免无限膨胀。
