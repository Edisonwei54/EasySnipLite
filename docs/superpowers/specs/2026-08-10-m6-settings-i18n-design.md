# M6 设置+i18n 设计文档

日期：2026-08-10
状态：已批准（用户逐项确认，2026-08-10）
分支：feature/m6-settings-i18n（PR #6）

## 1. 背景与目标

M5 完成后，全局热键（截图 Ctrl+双击空格、穿透 Ctrl+Shift+P）与全部 UI 字符串（中文）均硬编码。M6 将提供：

1. **设置窗口**（托盘「设置」入口）：语言、保存目录、贴屏滚轮缩放步长、开机自启、两个全局热键的自定义录制
2. **热键统一注册**：消除 App.OnKey 中的硬编码分支，截图热键与穿透热键同一套注册/匹配机制
3. **i18n 三语**：英文 / 简体中文 / 繁体中文，resx 资源 + 即时切换
4. **持久化**：`%AppData%\EasySnipLite\settings.json`

## 2. 已确认的决策（用户澄清）

| 决策 | 结论 |
|------|------|
| 可配置热键范围 | 截图热键 + 穿透热键都可配 |
| 截图热键语义 | **单击/双击自动识别**：录制时单击=普通组合键（combo）、双击=双击时序（chord）；2026-08-10 用户复议 |
| 滚轮步长形式 | 预设档位：小 5% / 中 10% / 大 20% |
| i18n 资源机制 | **resx**（中性英文 + zh-Hans + zh-Hant，自动回退） |
| 语言切换 | **保存后**即时生效（托盘/贴屏菜单/设置窗口立即刷新）；默认跟随系统 |

## 3. 设置模型与持久化

### 3.1 数据模型（纯逻辑，`Core/Settings/`）

```csharp
public enum AppLanguage { System, English, SimplifiedChinese, TraditionalChinese }

public enum ZoomStepPreset { Small = 5, Medium = 10, Large = 20 } // 百分比

[Flags] public enum HotkeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public enum HotkeyKind { Chord, Combo } // Chord=双击时序，Combo=单键组合

public sealed record HotkeySpec(
    HotkeyKind Kind,
    HotkeyModifiers Modifiers, // 修饰键掩码（精确匹配）
    int VirtualKey);           // 目标键 vkCode

public sealed record Settings(
    AppLanguage Language = AppLanguage.System,
    string? SaveDirectory = null,              // null = 默认（图片\EasySnipLite）
    ZoomStepPreset ZoomStep = ZoomStepPreset.Medium,
    bool Autostart = false,
    HotkeySpec? ScreenshotHotkey = null,       // null = 默认 Ctrl+双击Space
    HotkeySpec? PassthroughHotkey = null);     // null = 默认 Ctrl+Shift+P
```

**默认值**（`null` 字段在加载后展开为默认 spec）：
- 截图热键：`Chord, Ctrl, VK_SPACE`
- 穿透热键：`Combo, Ctrl|Shift, VK_P`

### 3.2 SettingsService（`Core/Settings/SettingsService.cs`）

- 路径：`%AppData%\EasySnipLite\settings.json`（`Environment.GetFolderPath(ApplicationData)` 拼 `EasySnipLite` 目录）
- `Load()`：文件缺失 → 默认值；JSON 损坏/字段非法 → 默认值（静默回退，不弹错误）
- `Save(Settings)`：**原子写**——先写 `settings.json.tmp` 再 `File.Replace` 覆盖
- `SettingsChanged` 事件：保存成功后广播，App 据此重建热键/托盘菜单
- 序列化：System.Text.Json（零第三方依赖），`JsonSerializerOptions { WriteIndented = true }`

### 3.3 开机自启（`Core/Settings/RegistryAutoStart.cs`）

- 注册表：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值名 `EasySnipLite`，值 = `Environment.ProcessPath`（exe 结尾含空格无需引号）
- `SetEnabled(bool)` / `IsEnabled()`
- 启动同步：`Settings.Autostart=true` 但注册表缺失 → 补写（用户可能手动删过）；`false` 但存在 → 删除
- 注册表写失败：静默忽略（下次启动再尝试），不阻塞启动

## 4. 热键统一注册

### 4.1 ChordDetector 参数化（改造现有，`Core/Hotkeys/`）

- 构造改为：`ChordDetector(TimeSpan doubleTapWindow, int targetKey, HotkeyModifiers modifiers)`
- 匹配规则：KeyUp 且 vk==targetKey 且**修饰键精确匹配**（声明的修饰键必须按下、未声明的必须未按下——与 Win32 RegisterHotKey 语义一致，避免误触发）。这是对现状的有意收紧：现在只查 CtrlDown，不查其他修饰键
- 现有 Ctrl+Space 行为回归不变（默认参数）

### 4.2 ComboDetector（新增，纯逻辑）

- 判定「修饰键 + 单击目标键」：KeyDown 时 vk 匹配 + 修饰键精确匹配；**每次物理按压只触发一次**（KeyDown 首发，KeyUp 复位，防 auto-repeat）——复刻 App._passthroughKeyPressed 现状语义
- 构造：`ComboDetector(int targetKey, HotkeyModifiers modifiers)`

### 4.3 HotkeyRecorder（新增，纯逻辑）

- 输入键盘事件流（喂入 `KeyEvent`），两种模式 + 一种自动识别：
  - **Chord 录制**：等待「修饰键按下 → 首个非修饰键 KeyUp（记为目标键候选）→ 同键第二次 KeyUp 间隔 ≤ 双击窗口且修饰键仍按下」→ 产出 `Chord` spec（修饰键取 KeyUp 时刻的掩码）。修饰键中途全部松开则复位重来
  - **Combo 录制**：等待任意非修饰键 KeyDown（修饰键掩码取按下时刻）→ 产出 `Combo` spec
  - **autoDetect（截图热键）**：构造 `autoDetect: true` 时 Chord 分支不变，仅「单击候选在双击窗口到期后敲定为 Combo」——由钩子线程 DispatcherTimer（Interval=双击窗口）调用 `HandleTimeout(DateTime now)`（与 HandleKey 同线程，无竞态）；定时器挂钩子线程 dispatcher（`KeyboardHookService.Dispatcher`），挂 UI 线程会与钩子事件竞态
  - **Esc** 任意时刻取消 → `Cancelled`
- 事件：`Recorded(HotkeySpec)` / `Cancelled`
- 录制期间 App 自身热键**屏蔽**（录制状态标志，App.OnKey 先查录制状态）

### 4.4 冲突检测（纯逻辑）

- 规则：新热键与另一热键 **（Modifiers, VirtualKey）相同** → 拒绝（截图=chord、穿透=combo 语义不同，但同键同修饰会互相干扰：如 Ctrl+P 单击触发穿透的同时 Ctrl+双击P 也成立）
- 检测时机：录制捕获后立即校验，冲突弹提示并停留在录制态可重试

### 4.5 格式显示（纯逻辑，`HotkeyFormat`）

- `Format(HotkeySpec)` → 本地化显示：`"Ctrl + 双击 Space"` / `"Ctrl + Shift + P"`（修饰键名与「双击」字样取自 resx；**键名不本地化**——用 `KeyInterop.KeyFromVirtualKey(vk).ToString()` 出英文键名，三语共用）
- 托盘菜单「区域截图」项与设置窗口热键行复用

### 4.6 App 装配

- OnStartup：读设置 → 自启同步 → 按 spec 构建 chord + combo detector → 启动钩子 → 托盘菜单（含格式化的当前热键）
- OnKey：录制状态 → 只喂 recorder；否则 chord.HandleKey → StartCapture；combo.HandleKey → TogglePinPassthrough
- SettingsChanged：重建两个 detector + 重建托盘菜单（顺带刷新热键显示）

## 5. i18n（resx 三语）

### 5.1 资源结构

```
Localization/
  AppResources.resx          英文（中性资源，回退基座）
  AppResources.zh-Hans.resx  简体中文
  AppResources.zh-Hant.resx  繁体中文
  AppResources.Designer.cs   强类型静态属性（AppResources.ToolRect 等）
```

- 中性资源=英文 → zh-Hant 缺词自动回退英文（ResourceManager 原生行为）

### 5.2 LocaleService（`Localization/LocaleService.cs`）

- `SetLocale(AppLanguage)`：映射 CultureInfo（System→`InstalledUICulture`：zh-CN→zh-Hans；zh-HK/zh-TW/zh-MO→zh-Hant；其他→en），设 `CultureInfo.CurrentUICulture`，广播 `LocaleChanged`
- **语言切换时机**：仅在设置窗口点「保存」后调用 SetLocale 并全局刷新（§6）；设置窗口内的语言下拉变化只做窗口内预览，不广播
- 切换时**重建/刷新**：托盘菜单、已打开贴屏窗口的右键菜单（贴屏菜单改为代码动态构建，顺带刷新滚轮步长）、设置窗口自身（重跑 Localize()）
- 编辑器/遮罩为模态短生命周期窗口，下次打开自然用新语言

### 5.3 字符串抽取清单

| 位置 | 内容 |
|------|------|
| 托盘菜单 | 区域截图（含热键格式化显示，动态）、设置、退出 |
| 贴屏右键菜单 | 鼠标穿透、透明度、子菜单档位、100% 缩放、复制、保存、关闭 |
| 编辑器 | 标题、9 工具、8 色 tooltip、线宽、撤销/重做/删除、复制/保存/贴到屏幕/完成、动作 tooltip、5 表情分类 |
| TextInputDialog | 标题、确定、取消 |
| MessageBox | 单实例「已在运行」、复制/保存/贴屏失败（编辑器+贴屏） |
| ImageFile | PNG 过滤器 `PNG Image (*.png)\|*.png` |
| 录制交互 | 「请按新快捷键…」「Esc 取消」、冲突提示、格式串 |
| 设置窗口 | 全部控件文本（见 §6） |

**不抽取**：Stitching（M4 已跳过存档，无入口死路径，保持中文）、日志/异常内部消息

### 5.4 即时切换实现方式

- 所有需即时切换的窗口：静态 XAML 文本改 `x:Name` + 代码 `Localize()` 方法赋值（`x:Static` 不支持运行时切换）
- 动态菜单（托盘/贴屏）直接用 `AppResources.*` 构建

## 6. 设置窗口（`Settings/SettingsWindow.xaml(.cs)`）

- **入口**：托盘菜单「设置」；模态 `ShowDialog`（托盘应用不设 Owner——M3 踩坑：MainWindow 已关闭窗口设 Owner 崩溃）
- 布局：单窗口垂直两区
  - **常规区**：语言下拉（跟随系统/English/简体中文/繁體中文）、保存目录（只读文本框 + 「浏览」按钮，用 WinForms FolderBrowserDialog——UseWindowsForms 已启用）、滚轮步长下拉（小 5%/中 10%/大 20%）、开机自启勾选
  - **快捷键区**：截图热键、穿透热键各一行：说明 + 当前值（HotkeyFormat）+ 「录制」按钮
- 按钮：保存 / 取消 / 重置默认
- **录制交互**：点「录制」→ 该行按钮变「请按新快捷键…（Esc 取消）」→ App 进入录制状态（自身热键屏蔽）→ 捕获成功显示新键 + 冲突校验 → 用户点「确定」应用或再次「录制」重试
- **语言即时预览**：语言下拉变化立即重跑窗口 Localize()（**仅窗口内预览，不广播 LocaleChanged**；取消则下次打开仍为已保存语言）
- 保存 → `SettingsService.Save` → 广播变更 → App 应用热键/托盘/自启

## 7. 集成点

| 位置 | 改动 |
|------|------|
| `Core/Imaging/ImageFile.cs` | 新增静态委托 `DefaultSaveDirProvider`（App 启动注入读设置）；`DefaultSaveDir()` 优先用设置的保存目录（不可写→回退 MyPictures\EasySnipLite） |
| `Pin/PinMath.cs` | `NextZoom(current, zoomIn, step)` 加步长参数；5/10/20% 映射 ×1.05/×1.1/×1.2 |
| `Pin/PinWindow.xaml(.cs)` | 右键菜单改代码动态构建（字符串取 resx + 步长取设置）；`Localize()` 刷新；缩放处理器读步长 |
| `App.xaml.cs` | OnStartup 装配设置/自启/热键/托盘；OnKey 查表分发；单实例提示本地化；SettingsChanged 重建 |
| `Tray/TrayIconService.cs` | 菜单代码构建 + 新增 SettingsRequested 事件 + Rebuild() |
| `Editor/EditorWindow.xaml(.cs)` | 字符串抽 resx，构造时 Localize()（模态窗口，打开时读当前语言即可） |

## 8. 错误处理

- settings.json 损坏 → 默认值静默回退（不阻塞启动）
- 注册表写失败 → 静默忽略，下次启动补写
- 保存目录不可写 → 回退默认图片目录（现状逻辑）
- 热键冲突 → 录制态提示拒绝
- 录制无超时，Esc/取消按钮随时终止

## 9. 测试策略（纯逻辑 TDD 先行）

| 测试目标 | 覆盖 |
|----------|------|
| ChordDetector 参数化回归 | 修饰键掩码组合（Ctrl/Shift/Alt/Ctrl+Shift）、**精确匹配**（未声明修饰键按下→不触发）、现有 Ctrl+双击Space 回归 |
| ComboDetector（新） | 单击触发、修饰键匹配、每次按压单触发（auto-repeat 抑制）、修饰键本身不触发 |
| HotkeyRecorder（新） | chord 录制（修饰键+双击）、combo 录制、Esc 取消、修饰键中途松开复位、双击间隔超窗、autoDetect 自动识别（单击到期敲定 Combo/窗口内双击→Chord/超窗 last-wins 后到期敲定/到期前不录/Esc 仍取消） |
| 冲突检测 | 同 (Modifiers, Key) 拒绝、不同键放行 |
| SettingsStore | 序列化往返、缺文件→默认、坏 JSON→默认、未知枚举→默认 |
| HotkeyFormat | 三种语言下格式串、键名映射 |
| ZoomStep 映射 | 5/10/20 → 1.05/1.1/1.2 |

**手工 E2E 清单**：
1. 改截图热键（如 Ctrl+双击A）→ 重启 → 新热键生效、旧失效、托盘菜单显示新热键
2. 改穿透热键 → 重启生效
3. 冲突（截图与穿透同键同修饰）→ 拒绝提示
4. 三语切换即时生效：托盘/已打开贴屏菜单/设置窗口立即变语言
5. 开机自启开 → 注册表 Run 有值；关 → 删除；重启 app 后状态保持
6. 保存目录设置 → 保存对话框初始目录生效
7. 滚轮步长大档 → 贴屏缩放幅度明显变大

## 10. 范围外（明确不做）

- Stitching 字符串抽取（M4 已跳过存档）
- ~~截图热键单键触发模式~~（2026-08-10 用户复议：改为单击/双击自动识别，见 §2，已实现）
- 编辑器内部快捷键（1-9/Ctrl+Z 等）自定义
- 设置导入/导出
