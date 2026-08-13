# EasySnipLite 项目进度记录

> 维护方式：每个里程碑完成后更新「已完成」一节，推进前先看「下一步」。

## 一、原本有什么（起点）

- 空 git 仓库 `d:\EasySnipLite`（main 分支，仅 1 个 initial commit）
- 已有文件：`.gitignore`（忽略 1.md）、`LICENSE`、`1.md`（产品需求描述）
- 本机环境：Windows 10 Pro、VS2022 Community、.NET SDK 10.0.302、双显示器（均 1920x1080，dpi=1）

## 二、产品目标（1.md 需求）

轻量快速的 Windows 截图与标注工具：全局快捷键唤起（Ctrl+双击空格，可自定义）→ 拖拽框选（可调手柄）→ 标注（矩形/椭圆/箭头/画笔/荧光笔/马赛克/文字/表情贴纸）→ 复制/保存/贴到屏幕。含自动滚动长截图（实时预览）、托盘常驻、英/简/繁三语。

**已确认的技术决策**：C# 14 / WPF / .NET 10；长截图=自动滚动+纯 C# 拼接；标注=全矢量对象+撤销重做；表情=Segoe UI Emoji；分发=单文件绿色 exe。设计文档见 `docs/superpowers/specs/2026-08-07-easysniplite-design.md`。

## 三、已完成

### M0 脚手架（已提交 d056882）
- slnx 解决方案 + WPF 主项目（net10.0-windows, UseWPF+UseWindowsForms）+ xUnit 测试项目
- PerMonitorV2 DPI manifest、单文件发布属性、全局类型别名（消解 WPF/WinForms 二义性）
- 空窗口可启动，构建 0 错误 0 警告

### M1 截图主干（已提交 a628cf4）
**交付**：全局热键 → 冻结屏幕 → 框选 → Enter 确认 → 复制到剪贴板，**端到端验证通过**（tools/verify-m1.ps1 自动化验证：热键唤起、遮罩窗口可见、拖拽 300x200、Enter 后窗口关闭、剪贴板读回 300x200 PNG）。

**新增文件**：
- `Core/Native/Win32.cs` — P/Invoke（BitBlt/键盘钩子/DPI/光标/窗口样式）
- `Core/Hotkeys/ChordDetector.cs` — Ctrl+双击空格时序判定（纯逻辑，7 个单测全绿）
- `Core/Hotkeys/KeyboardHookService.cs` — WH_KEYBOARD_LL（专用 STA 消息循环线程）
- `Core/Imaging/ScreenCapture.cs` — 按显示器 BitBlt 冻结（物理像素 + DPI）
- `Core/Clipboard/ClipboardEx.cs` — 剪贴板 DIB+PNG 双格式
- `Selection/SelectionSession.cs` — 会话协调、跨屏拖拽轮询、跨屏裁剪组装
- `Selection/RegionSelectionWindow.xaml(.cs)` — 每显示器全屏透明遮罩 + 选区 + 尺寸标签
- `Tray/TrayIconService.cs` — NotifyIcon 最小版（区域截图/退出）
- `App.xaml.cs` — 服务装配、热键触发截图流程

**踩坑记录（重要）**：
1. **低级键盘钩子回调内严禁磁盘 I/O**——诊断日志每次按键写文件导致回调超时，被系统静默移除钩子（表现为后续按键无响应）。已移除全部回调日志，回调保持极简。
2. **DIB 剪贴板格式**——手写 BITMAPINFOHEADER（BI_RGB+负高度）不被 WinForms GetImage 识别（WinForms 自写格式为 BI_BITFIELDS+正高度+掩码）。改为 `DataObject.SetImage(Bitmap)` 把编码交给 WinForms，PNG 格式自己补写。兼容性最佳。
3. **WPF/WinForms 双引入的类型二义性**——全局别名统一消解（`GlobalUsings.cs`）。
4. PowerShell 5.1 脚本中文需 ASCII（ANSI 解码 UTF-8 会解析错乱）；验证脚本已纯英文。

### M2 选区完善（已提交 4d31f92，PR #2）
**交付**：8 手柄缩放 / 内部移动 / 方向键 1px 微调(Shift=10px) / 角点放大镜(9x9 像素 + 坐标 + RGB) / 尺寸标签 / Esc 两级取消 / Enter 复制 / Ctrl+S 保存 PNG。**端到端验证通过**（tools/verify-m2.ps1：手柄缩放后保存 340x230、微调/移动与基线像素差异、Esc 两级语义、Enter 剪贴板 300x200）。

**新增/修改**：
- `Selection/SelectionMath.cs` — 纯逻辑：命中测试(角>边>体)、手柄缩放(对边固定+最小尺寸)、移动钳制、遮罩四块计算、放大镜定位（27 个单测全绿）
- `Selection/SelectionSession.cs` — 状态机 Idle→Selecting→Adjusting；手柄/移动/微调/放大镜广播
- `Selection/RegionSelectionWindow.xaml(.cs)` — 8 手柄渲染 + 光标映射 + 放大镜控件
- `App.xaml.cs` — Ctrl+S 保存（SaveFileDialog → PNG）
- `tools/verify-m2.ps1` — M2 E2E 自动化（保存对话框用剪贴板粘贴路径自动化）

**踩坑记录（重要）**：
1. **`Rect.Empty` 的 Bottom 是 -Inf**（Y=0, Height=-Inf）→ `Math.Max(0, win.Bottom - sel.Bottom)` 得 +Inf → WPF `set_Height(+Inf)` 抛 ArgumentException 崩溃。M1 从未走 `UpdateSelection(null)` 分支（无 Esc/点击取消场景）所以没暴露；M2 的 Esc 清空选区触发。已提取 `SelectionMath.MaskRectangles` 纯函数 + `PositionMask` NaN/Inf 防御（5 个单测）。
2. **验证脚本枚举格式自伤**：verify-m2 的 WinEnum 输出加了类名字段但匹配模式没同步，overlay 永远匹配不到（误报"overlay not shown"）。验证脚本与枚举模式必须同步。
3. **SaveFileDialog 自动化**：SendKeys 输入路径会插入到预填文件名中 → 先 Ctrl+A 全选再粘贴（Clipboard + keybd_event 比 SendKeys 可靠）。
4. **锁屏桌面拦截模拟键盘输入**：前台为 LockApp 时 keybd_event/SendInput 事件丢失或部分到达（低级钩子收不到），验证必须在解锁桌面运行。诊断方法：独立 WH_KEYBOARD_LL 钩子脚本 + 前台窗口检查（GetForegroundWindow）。
5. **GetAsyncKeyState 在低级钩子回调中反映合成输入状态正常**（本次排查确认无坑）；合成输入丢失是锁屏环境所致。

### M3 标注编辑器（已提交 6e07aef，PR #3）
**交付**：全矢量对象模型（8 种标注）+ 撤销/重做 + 复制/保存/完成。Enter/双击确认 → 打开编辑器（底图 + 矢量对象 + 选中装饰）；工具条：选择 + 8 工具 + 8 色 + 线宽 + 撤销/重做/删除；数字键 1-9 切工具；Delete 删除、Ctrl+Z/Y 撤销重做、Ctrl+C/S 复制保存、Enter 完成（复制并关闭）、Esc 关闭；文字内联输入框、表情分类面板（Segoe UI Emoji 5 类）。**端到端验证通过**（tools/verify-m3.ps1：Enter 开编辑器、D2 切矩形 → 画布拖拽 → Ctrl+C 有矩形、Ctrl+Z 撤销后无矩形、Enter 完成复制 300x200）；M2 回归（verify-m2 场景 F 已更新）通过；**单测 90/90 全绿**（新增 53 个）。

**新增/修改**：
- `Editor/Models/AnnotationObject.cs` — 抽象基类（Bounds/Color/StrokeWidth/IsSelected/Clone/Render/Offset）+ Rectangle/Ellipse/Arrow（头部三角纳入 Bounds）/Freehand/Highlighter
- `Editor/Models/MosaicObject.cs` — 块化渲染（块内像素取块左上，底图不破坏、可撤销）；`TextObject.cs` — Text/Emoji（TextBlock+RenderTargetBitmap 彩色 emoji 缓存）
- `Editor/UndoRedo/UndoStack.cs` — 命令式撤销栈（AddObject/DeleteObject/TransformCommand + Push/Undo/Redo + 容量上限，12 单测）
- `Editor/Tools/` — IAnnotationTool 状态机接口 + DragToolBase（规范化+最小尺寸）；Rectangle/Ellipse/Arrow/Freehand(采样去抖)/Highlighter/Mosaic 拖拽工具；Text/Emoji 点击工具（Clicked 事件 + TextMetrics 度量）；SelectionTool + HitTester（重叠取最上，5 单测）
- `Editor/EditorViewModel.cs` — 工具协调（选择移动入 Transform 命令、工具产出入 Add 命令）、选中管理、Compose 导出
- `Editor/AnnotationCanvas.cs` — 渲染层（底图→对象→选中虚线框）+ EmojiCatalog
- `Editor/EditorWindow.xaml(.cs)` — 工具条/动作条/画布/表情 Popup/快捷键；`TextInputDialog.xaml(.cs)` — 文字输入
- `Core/Imaging/ImageFile.cs` — 保存共享（SavePng/SaveFileDialog/默认目录），App 与编辑器共用
- `App.xaml.cs` — Enter 确认 → 先关遮罩 → BeginInvoke 打开编辑器
- `GlobalUsings.cs` — 新增 Pen/FontFamily/FlowDirection/Brushes/MouseEventArgs/Button/MessageBox 别名
- `tests/` — UndoStackTests(12)/AnnotationObjectTests(19)/AnnotationToolTests(17)/SelectionToolTests(5)
- `tools/verify-m3.ps1` — M3 E2E 自动化；`tools/verify-m2.ps1` 场景 F 适配 M3（Enter 开编辑器）

**踩坑记录（重要）**：
1. **target-typed new 不查找最派生类**：`Clone()` 返回类型是抽象 `AnnotationObject` 时 `new(...)` 直接尝试构造抽象类 → CS0144。必须显式 `new RectangleObject(...)`。
2. **托盘应用（无 MainWindow）设置 Owner 崩溃**：遮罩关闭后 `Application.Current.MainWindow` 指向已关闭窗口，`Owner = MainWindow` 抛"Owner 设置为它本身"→ 模态编辑器不设 Owner。
3. **按键事件栈上关遮罩 + ShowDialog 崩溃**：Enter 确认回调里先 Close 全屏遮罩再 ShowDialog 导致 app 崩溃 → 用 `Dispatcher.BeginInvoke` 延迟打开。
4. **全屏 Topmost 遮罩挡住编辑器**：先开编辑器再关遮罩时编辑器被遮罩盖住，画布点击全部被遮罩吃掉（表现为"绘制无反应"）→ 必须先 FinishSession 再开编辑器。
5. **ScrollViewer 内容默认居中**：ScrollContentPresenter 默认 Horizontal/VerticalContentAlignment=Center → 画布被放到视口中央，任何"画布在 (0,0)"的坐标假设全部错位 → 显式 `HorizontalAlignment="Left" VerticalAlignment="Top"`。
6. **验证脚本残留进程**：失败的 verify 运行可能残留 app 进程（钩子+窗口仍在），干扰后续运行（截图内容错误、窗口计数异常）→ 运行前先 `tasklist | grep EasySnipLite`。
7. **PowerShell `$pid` 是只读自动变量**：不能作函数参数名，否则"变量为只读"错误。
8. **ToggleButton 无 GroupName**（RadioButton 才有）→ 工具互斥手动维护。

### M4 滚动长截图（2026-08-08 已决定跳过；PR #4 合并进 main 9995a59，分支保留）
> 用户决定：M4 功能整体跳过（不做/暂缓）。实现 + 单测经 **PR #4 合并进 main（9995a59）**存档；
> 分支 `feature/m4-scroll-capture` 远端保留（本地已按规则清除，HEAD b2bb023）。以下为存档记录，若将来捡起可直接继续。
**已完成（TDD + 实现，未通过 E2E）**：
- `ImageAligner` 纯逻辑 TDD（15 单测）：灰度降采样 + 按行 SAD 两级搜索垂直偏移；**采样只忽略右缘滚动条**（左缘行号/缩进是重要对齐特征——曾因忽略左缘 6% 导致 notepad 等左对齐文本页面无法对齐）；`ScrollbarStrip`/噪声/纯色/行相似文本（回归）全覆盖
- `ScrollInput`（SendInput 滚轮，INPUt union FieldOffset(8) 布局）+ `Win32` 补充
- `ScrollCaptureEngine`：调度循环（截帧→对齐→拼接→滚动→稳定）+ 到底检测（连续内容未变）+ 20000px 上限 + 对齐失败重试/接缝标记 + **Checkpoint 续跑**（失败后重试不丢已拼内容）；7 单测（合成帧序列：拼接/到底/重试/上限/取消）
- `StitchPreviewWindow`：1:1 实时预览 + 进度 + 停止/重试/复制/保存/完成 + 接缝标红 overlay + **自动定位到目标区域外**（右/下/上/左，无空间最小化——避免预览窗口遮挡被捕获窗口）+ ShowActivated=False 不抢焦点
- App 装配：托盘「滚动长截图」入口（框选区域→滚动捕获）+ `--longcapture x y w h` 命令行模式（自动复制结果，验证脚本用）
- `tools/verify-m4.ps1`：notepad 400 行文本 E2E（MoveWindow 到主屏避开被 Edge 遮挡的第二屏 + 剪贴板尺寸校验）

**已知阻塞（E2E 失败，若续做从这开始）**：「滚动后 BitBlt 截帧陈旧」——滚轮事件 notepad 快速处理（EM_GETFIRSTVISIBLELINE 立即更新 9→18→27），但 app 截帧（BitBlt 与 CopyFromScreen 同刻一致）在滚2 后 1.2s 仍拍到滚2 前内容 → 帧间 offset=0 → 误判到底，长图只拼 1-2 帧。已排除：hBitmap 句柄复用（FromHbitmap 延迟读取，已在句柄删除前拷贝修复）、截帧路径差异（BitBlt==CopyFromScreen）、前台被抢（fg 始终 notepad）、滚轮无效（line 确实在变）。**下一步候选**：滚轮后主动触发目标窗口重绘/前台恢复、稳定延时加大、或对「截帧未变化但滚动已发生」加屏幕重绘等待循环。

**踩坑记录（M4 诊断）**：
1. `Image.FromHbitmap` 返回的 Bitmap 延迟读取 hBitmap——在 `DeleteObject` 前完成像素拷贝（高频截帧时句柄复用会读到旧帧内容）。
2. 低级诊断用 `EM_GETFIRSTVISIBLELINE`（Edit 控件专用）比 `GetScrollInfo` 可靠（后者对 Edit 可能恒 0 误报）。
3. 第二显示器被全屏 Edge 覆盖时，滚轮事件发给光标下的 Edge 而非目标窗口——验证脚本必须把目标窗口移到确定屏幕。
4. 验证脚本剪贴板轮询：单屏高度图（未滚动即到底）既不满足尺寸又不该丢弃，需显式 break 判定失败。

### M5 贴屏+托盘（2026-08-08 本次；单测 121/121，手工 E2E 已验证；PR #5 已合并进 main 2e6ca4d）
**交付**：PinWindow 贴屏（1:1 物理像素/DpiScale 置顶、显示在截图原位置、左键拖动、Ctrl+滚轮缩放 50%~300%、右键菜单：鼠标穿透/透明度/100% 缩放/复制/保存/关闭）；编辑器动作条「贴到屏幕」入口（贴屏后关编辑器）；多张贴屏并存（组内点击置顶）；全局热键 **Ctrl+Shift+P** 切换穿透（穿透后鼠标点不到窗口，此为恢复手段）；单实例 Mutex（二次启动提示「已在运行」后退出）；托盘菜单移除「滚动长截图」入口（M4 已跳过，现仅 区域截图/退出）。**单测 121/121 全绿**（新增 PinMathTests 10 个）。**手工 E2E 已验证**（贴屏 1:1/拖动/穿透切换/透明度/缩放/多张置顶/单实例提示/托盘菜单 8 项全过，2026-08-08）。

**新增/修改**：
- `Pin/PinMath.cs` — 纯逻辑换算（物理像素÷DpiScale→布局坐标；缩放步进 1.1、钳制 50%~300%，10 单测）
- `Pin/PinWindow.xaml(.cs)` — 贴屏窗口（1:1 置顶/左键拖动/Ctrl+滚轮缩放/右键菜单/WS_EX_TRANSPARENT 穿透/Window.Opacity 透明度/复制/保存）
- `Editor/EditorWindow.xaml(.cs)` — 动作条「贴到屏幕」按钮 + PinRequested 事件（贴屏后关编辑器）
- `App.xaml.cs` — OpenPin 装配（截图区域→贴屏位置）、多贴屏列表 _pins、Ctrl+Shift+P 全局穿透热键、单实例 Mutex（持有引用防 GC）
- `Tray/TrayIconService.cs` — 移除「滚动长截图」菜单项（仅 区域截图/退出）
- `Core/Hotkeys/ChordDetector.cs` + `KeyboardHookService.cs` — KeyEvent 增加 ShiftDown 状态（Ctrl+Shift+P 判定）
- `Core/Native/Win32.cs` — SetWindowPos/GetWindowLongPtr/SetWindowLongPtr/VK_SHIFT/WS_EX_TRANSPARENT 声明
- `tests/PinMathTests.cs` — PinMath 10 单测；`ChordDetectorTests.cs` 适配 ShiftDown

**踩坑记录**：无（subagent 流程全程审查通过，无新增踩坑）。

### M6 设置+i18n（2026-08-10 本次；单测 176/176 全绿；手工 E2E 已验证；PR #6 已合并进 main f9237b9）
**交付**：设置窗口（托盘「设置」入口）：常规页（语言三选/保存目录/滚轮步长）+ 快捷键页（截图/穿透双热键录制：截图热键支持**单击/双击自动识别**（单击=普通组合键、双击=双击时序，录制时自动判定，运行时按 Kind 派发：单击/双击分别建 ComboDetector/ChordDetector）、穿透热键单键组合、冲突拒绝、Esc 取消）+ 语言即时预览；三语 resx（英/简/繁，72 键）运行时切换（CurrentUICulture + 动态重建托盘/贴屏菜单，已打开窗口即时变语言）；settings.json 持久化（原子写：临时文件 + 替换，损坏/缺失回退默认）；开机自启（注册表 HKCU Run 键，启动时按设置补写/删除同步）。**单测 176/176 全绿**（M5 121 基础上新增：ComboDetector 6 / HotkeyRecorder 16 / HotkeyFormat 6 / LocaleService 5 / SettingsStore 8，ChordDetector 与 PinMath 同步扩展用例；HotkeyRecorder 含 autoDetect 自动识别 5 用例）。

**新增文件**：
- `Core/Settings/HotkeySpec.cs` — 热键规格（HotkeyKind/HotkeyModifiers/HotkeySpec record，全计划类型一致）
- `Core/Settings/Settings.cs` — 设置模型（Language/ScreenshotHotkey/PassThroughHotkey/SaveDirectory/ZoomFactor + Normalize）
- `Core/Settings/SettingsStore.cs` — settings.json 读写（原子写，损坏/缺失回退默认，7 单测）
- `Core/Settings/RegistryAutoStart.cs` — 开机自启（HKCU Run 键，写失败静默忽略，Sync 补写/删除）
- `Core/Hotkeys/ComboDetector.cs` — 单键组合热键判定（防 auto-repeat，6 单测）
- `Core/Hotkeys/HotkeyRecorder.cs` — 热键录制（Chord/Combo/Esc 取消，8 单测）
- `Core/Hotkeys/ModifierMatch.cs` — 修饰键掩码匹配工具
- `Core/Hotkeys/HotkeyFormat.cs` — 热键本地化显示（Ctrl + double-tap Space，6 单测）
- `Localization/AppResources.cs` + `AppResources.resx`(×3: 默认英/zh-Hans/zh-Hant) — 三语资源（72 键）
- `Localization/LocaleService.cs` — 语言映射 + SetLocale（切 CurrentUICulture + 广播变更，5 单测）
- `Settings/SettingsWindow.xaml(.cs)` — 设置窗口（命名空间 **SettingsUI**，规避与 Settings 类型同名）

**修改**：`ChordDetector`（修饰键掩码精确匹配 + AltDown 支持）、`KeyboardHookService`（KeyEvent.AltDown）、`Core/Native/Win32.cs`（扩展键等声明）、`App.xaml.cs`（装配：热键查表分发/录制状态/设置应用/自启同步/托盘重建）、`Tray/TrayIconService.cs`（菜单代码构建 + 设置入口 + Rebuild，热键/语言即时刷新）、`Pin/PinWindow`（右键菜单代码动态构建 + Localize/步长注入）、`PinMath`（步长参数化）、`Editor/EditorWindow` + `TextInputDialog` + `AnnotationCanvas`（字符串迁移 resx）、`Core/Imaging/ImageFile.cs`（保存目录可配置注入 + PNG 过滤器本地化）

**踩坑记录（重要）**：
1. **`EasySnipLite.Settings` 命名空间与 `Settings` 类型同名 → CS0118**：设置窗口初稿命名空间 `EasySnipLite.Settings`，与同程序集 `Core/Settings/Settings.cs` 的 `Settings` 类型撞名，类型引用处报「已存在或无法解析」——CLAUDE.md 规则 6 的教训重现（上次 `Core.Clipboard` vs WPF `Clipboard`）。已改名 `EasySnipLite.SettingsUI`。
2. **托盘语言切换必须 SetLocale 先于 RebuildTray**：resx 生成的 AppResources 属性按 `CurrentUICulture` 动态解析，而托盘菜单字符串在构建（MenuItems 创建）时固化——顺序反了则菜单仍是旧语言。ApplySettings 固定顺序：`SetLocale → RebuildTray → 贴屏 ApplyLocale`（App.xaml.cs）。
3. **WindowsDesktop SDK 隐式 using 不含 System.IO**：`SettingsStore` 直接用 File/Path/Directory 编译报「当前上下文中不存在」——WPF 项目默认隐式 using 只覆盖基础集，需显式 `using System.IO;`。
4. **录制未完成关窗口会吞全局热键**：设置窗口打开中开始录制（await 挂起）→ 用户直接点 X/保存/取消/重置关窗 → `_settingsWindowOpen=false` 但 `_recorder` 仍非空 → OnKey 先走录制分支吞掉所有按键（截图/穿透全失效）。App 在 ShowDialog 返回后检查 `_recorder` 并清理（置空 + `_recordTcs.TrySetResult(null)` 完成挂起 await）。
5. **语言即时预览需临时切换 CurrentUICulture**：resx 生成的 AppResources 属性按 `CurrentUICulture` 动态解析，仅改 `_draft` 窗口文本纹丝不动——预览时临时 `CurrentUICulture = ResolveCulture(...)` 再 Localize（不广播）；保存路径 ApplySettings 已 SetLocale 终值，未保存关闭（X/取消/重置）由 Closed 事件恢复初始语言。
6. **低级键盘钩子对修饰键报左右专用虚拟键码（VK_LCONTROL 0xA2 等），热键录制 IsModifierKey 只认通用码（0x10/0x11/0x12）导致修饰键被当目标键录制（组合键一按即结束/穿透热键失效/冲突误判）**——已统一识别 0xA0-0xA5（新增 `Core/Settings/ModifierKey.cs`），且 `Settings.ValidSpec` 拒绝修饰键码作目标键（防御已污染的 settings.json 回退默认）。
7. **自动识别单击需双击窗口到期敲定——定时器必须挂钩子线程 dispatcher（与 HandleKey 同线程），挂 UI 线程会与钩子事件竞态**：`HotkeyRecorder.HandleTimeout(now)` 由钩子线程 `DispatcherTimer` 调用（`KeyboardHookService.Dispatcher`，Start 完成后非 null；`new DispatcherTimer(DispatcherPriority.Normal, hookDispatcher)`），单击候选窗口到期敲定为 Combo；Chord 判定逻辑不变，autoDetect 只补「到期完成单次按键」路径。另：截图行录制可能产出 Combo spec，`SettingsWindow._pending` 元组去掉 Kind，CommitPending 改按按钮身份（Btn == CaptureRecordBtn）写目标字段。

### M7 打磨+发布（2026-08-10 本次；单测 188/188 全绿；verify-m7 发布冒烟 PASSED；版本 1.0.0；PR #7 已合并进 main ef33427）
**交付**：错误处理管线（`Core/Diagnostics/AppErrors.cs`：error.log 追加 + 512KB 归档 .old、托盘气泡委托 TrayNotify 注入、Fatal 弹窗退出；App 三钩子：DispatcherUnhandledException 非致命气泡 + 继续运行、AppDomain 致命弹窗退出、Unobserved 仅日志）；启动成功气泡（总是显示，含开机自启状态）+ 设置保存失败气泡（ApplySettings 改返回 bool，内存态仍更新）；边界小修：Settings.ValidSpec 修饰键位掩码校验（非法位回退默认）、ImageFile WriteProbe 可写性探测（已存在只读目录回退下一候选）、LocaleChanged 零订阅者死事件移除（YAGNI）；补测试 12 个（AppErrors 4 / Settings 1 / ImageFile 2 / Chord 2 / Combo 1 / Recorder 2）：**176 → 188**；tools/verify-m7.ps1 单文件发布冒烟（publish → 热键 → 框选 → 编辑器 Complete → 剪贴板 300x200 → 无 error.log）**PASSED**；版本 **0.1.0 → 1.0.0**；README 新增「发布」小节。

**新增文件**：
- `Core/Diagnostics/AppErrors.cs` — 错误兜底（日志 + 托盘气泡 + 致命弹窗退出）
- `tools/verify-m7.ps1` — 单文件发布冒烟脚本（纯 ASCII）
- `tests/EasySnipLite.Tests/AppErrorsTests.cs`（4 单测）、`ImageFileTests.cs`（2 单测）

**修改**：`AppResources.resx`(×3) 新增 4 键（AppStarted/SettingsSaveFailed/UnhandledNotify/UnhandledErrorBody）、`Tray/TrayIconService.cs`（ShowBalloon 气泡）、`App.xaml.cs`（三异常钩子/启动气泡/保存失败气泡/ApplySettings 返回 bool）、`Settings/SettingsWindow.xaml.cs`（ApplySettings Func 签名）、`Core/Settings/Settings.cs`（修饰键位掩码校验）、`Localization/LocaleService.cs`（死事件移除）、`Core/Imaging/ImageFile.cs`（WriteProbe 可写性探测）、检测器/录制器补 5 测试、`EasySnipLite.csproj`（0.1.0→1.0.0）、`README.md`（发布小节+里程碑表）、`docs/PROGRESS.md`、`CLAUDE.md`、`1.md`

**踩坑记录**：
1. **verify-m7 脚本初稿剪贴板步骤陈旧**：Enter 直接复制是 M1 时代流程，M3+ 后 Enter 打开标注编辑器——脚本首次运行 FAIL（剪贴板无图），按 verify-m3 场景 C 加 5b 步（Enter 二次 = Complete 复制+关闭）后 PASSED。
2. **启动期异常必须走 Fatal，不能静默置 Handled**：DispatcherUnhandledException 无条件 Notify + e.Handled=true 时，OnStartup 期异常（如 RegistryAutoStart.Sync/钩子启动失败）会被 Dispatcher 吞掉——AppDomain 钩子收不到已处理异常 → 无托盘无气泡无热键的静默进程占住单实例 mutex，后续启动全部「已在运行」退出（僵尸进程）。终审修复 ae77cd8：`_startupComplete` 标志在 OnStartup 末尾置位，启动期异常改走 `AppErrors.Fatal`（弹窗+退出）。
3. 无其他新增踩坑（subagent 流程全程审查通过）。

### 维护期：CI 自动化（2026-08-11；PR #8）
**交付**：GitHub Actions CI（`.github/workflows/ci.yml`）：PR / push(main) 自动 build(Release) + 单测 188/188 + TRX 上传；`global.json` 锁定 SDK 10.0.302（rollForward latestFeature，本地精确命中 / CI 滚动最新）；README 加 CI 徽章。设计见 `docs/superpowers/specs/2026-08-11-ci-design.md`，计划见 `docs/superpowers/plans/2026-08-11-ci.md`。

**踩坑记录**：runner 为 session 0 无交互桌面，截图/热键 E2E 无法在 CI 运行——CI 只做编译 + 单测，发布冒烟仍走本地 `tools/verify-m7.ps1`。

### 维护期：开源门面配置（2026-08-11 本次；PR #16）
**交付**：分支保护 ruleset（main：必须 PR 合并 + build-test 必检 + 分支最新 + 禁强推/删除，Ruleset id 20680347）；Dependabot 三件套（alerts + security updates 已启用，version updates 每周巡检，首批 6 个更新 PR 全部合并：Test.Sdk 18.8.1 / coverlet 10.0.1 / xunit.runner 3.1.5 / checkout v7 / setup-dotnet v6 / upload-artifact v7）；`CONTRIBUTING.md` + Issue(bug/feature 表单) + PR 模板；`SECURITY.md` + CodeQL workflow（csharp 每周扫描）；Release workflow（tag v* → publish 单文件 exe → GitHub Releases）；README 补 Dependabot/License 徽章。

**踩坑记录**：
1. Dependabot 配置合并后立即执行首次扫描，一次性开出多个更新 PR（并发上限 `open-pull-requests-limit: 5`），属正常行为——逐个合并即可，Dependabot 会自动 rebase 保持分支最新。
2. rulesets 旧版 branch protection API（`/branches/main/protection`）与新规则并存时返回 404 属正常——新版规则用 `/rulesets` 端点查询。
3. Dependabot alerts 页面入口在改版后位置变动，可直接用 API 启用：`PUT /repos/{owner}/{repo}/vulnerability-alerts`（204 成功）。

### 维护期：v1.0.0 首个 Release（2026-08-11；PR #17 修复 + 发布）
**交付**：Release 自动发布链路端到端验证：`git tag v1.0.0 && git push origin v1.0.0` → Release workflow（dotnet publish 单文件 exe 165MB → gh release create 自动 changelog）→ https://github.com/Edisonwei54/EasySnipLite/releases/tag/v1.0.0。之后发版固定为打 tag 即发布。

**踩坑记录**：
1. **PowerShell 不展开 `$GITHUB_REF_NAME`**：runner 默认 shell 为 pwsh，`$GITHUB_REF_NAME` 是普通变量（空值），环境变量需 `$env:` 前缀——`gh release create ""` 报 `tag required when not running interactively`。修复：改用 `${{ github.ref_name }}` GitHub 表达式（任何 shell 都替换为字面值），PR #17 已修。教训：GitHub Actions 里取上下文值优先用 `${{ }}` 表达式，避免 shell 变量语法坑。
2. 首次 tag 已推送但 Release 创建失败——无需保留：`git tag -d v1.0.0` + `git push origin :refs/tags/v1.0.0` 删除后重打即可（未创建 Release 时无残留）。

### 维护期：内联标注（issue #20，2026-08-12 本次；单测 197/197 全绿；verify-issue20 E2E PASSED；verify-m7 冒烟仍 PASSED）
**交付**：取消独立标注编辑器流程——框选完成（松开鼠标）即进入**内联标注**：选区冻结为图像（标注层 = Editor.AnnotationCanvas 复用），标注工具悬浮在选区下方（`Selection/AnnotationToolbarWindow`，无边框置顶、Focusable=False 不抢键盘焦点、按 `SelectionMath.ToolbarPlacement` 定位：下方居中→上方→钳制）；框选调整与标注**同时进行**：8 手柄缩放（标注保留、底图重组合、越界裁剪）、`Alt`+拖主体移动选区、方向键微调；快捷键沿用编辑器（数字 1-9 切工具、Delete、Ctrl+Z/Y、Ctrl+C/S）；动作：复制 / 保存 / 贴到屏幕 / 完成（复制并关闭）；**Esc 三级**：有标注先清空标注 → 有选区清空选区 → 取消会话；点选区外部重新框选 = 新截图（旧标注整体清空）。编辑器窗口（EditorWindow）不再被调用，源码保留（未来可扩展为「打开已有图片标注」入口）。

**新增/修改**：
- `Selection/AnnotationToolbarWindow.xaml(.cs)` — 悬浮工具栏窗口（工具/颜色/线宽/撤销重做/删除/复制/保存/贴屏/完成；事件转发到会话）
- `Selection/SelectionMath.cs` — `ToolbarPlacement` 工具栏定位纯逻辑（7 单测）；`ToolbarMargin` 常量
- `Selection/SelectionSession.cs` — 会话集成：`EditorViewModel` 持有 + 底图随选区重组合（`SetBaseImage`）、标注拖拽路由（轮询定时器驱动，跨屏安全）、工具栏装配、文字/表情输入、复制/保存/贴屏/完成动作、Esc 三级语义、重新框选清空标注
- `Selection/RegionSelectionWindow.xaml(.cs)` — 内联标注层（复用 `Editor.AnnotationCanvas` + LayoutTransform 按 DpiScale 缩放 + ClipToBounds）+ 表情 Popup + Alt 修饰键 + 标注快捷键路由 + 鼠标捕获（拖拽在工具栏上释放不丢 MouseUp）+ 标注模式选区填充透明
- `Editor/EditorViewModel.cs` — `Image` 可设置 + `SetBaseImage`（马赛克源图同步刷新）+ `ClearAll`
- `Editor/UndoRedo/UndoStack.cs` — `Clear()`（2 单测）
- `App.xaml.cs` — Completed→FinishSession（不再开编辑器）、新增 PinRequested 装配、移除 OpenEditor
- `tools/verify-issue20.ps1` — issue #20 E2E（工具栏出现/内联标注/Enter 复制/像素对比/Esc 三级/无 error.log）**PASSED**
- `tools/verify-m7.ps1` — 适配新流程（Enter 一次即完成复制+关闭，移除「Enter 二次开编辑器」步骤）
- `tools/verify-m3.ps1`（Enter 开编辑器流程）已被新流程取代，标注相关 E2E 以 verify-issue20.ps1 为准

**踩坑记录（重要）**：
1. **`Brush` 类型二义性**（WPF `System.Windows.Media.Brush` vs WinForms `System.Drawing.Brush`）：GlobalUsings 未给 Brush 建别名，静态字段声明报 CS0104——全限定 `System.Windows.Media.Brush` 解决（或在 GlobalUsings 补别名）。
2. **完成语义 = 复制并关闭**：OnConfirm 初稿只触发 Completed（App 仅关窗口），Enter/完成按钮不复制——编辑器时代的「Complete」复制逻辑在会话重构中必须显式保留（CopyToClipboard + Completed）。
3. **工具栏窗口吞掉鼠标释放**：标注拖拽/手柄拖拽在工具栏上释放时 MouseUp 发给工具栏窗口（非遮罩）→ 会话永远等不到 OnLeftButtonUp（拖拽卡死）——遮罩窗口 MouseDown 时 `CaptureMouse()`（坐标仍走 Win32 全局轮询，捕获只影响事件送达），MouseUp 后释放。
4. **选区半透明白色填充盖住标注层**：SelectionRect Fill `#12FFFFFF` 在标注模式下仍铺在标注层之上（7% 白色蒙尘）——标注激活时 Fill 换 Transparent（XAML 初值保留给框选阶段）。
5. **重新框选残留旧标注**：点选区外部开新框选后旧标注对象仍按旧选区本地坐标渲染（错位）——新框选分支显式 `_vm.ClearAll()`（重新框选 = 新截图）。
6. **Esc 语义扩展为三级**：Esc 有标注先清标注（Objects + UndoStack 同步 `ClearAll`）→ 再 Esc 清选区 → 再 Esc 取消会话；与 M2 的「两级取消」文档语义需同步更新。

### 维护期：issue #23 缺陷修复（2026-08-13 本次；单测 203/203 全绿；verify-issue23 E2E 脚本就绪——本会话环境输入注入异常（热键无法送达 app 全局钩子，12:05 后持续，非代码问题），待环境恢复后运行确认；verify-issue20 回归同样待环境恢复后重跑）
**交付**：修复内联标注的三个交互缺陷——①**实时标注预览**：拖拽矩形/椭圆/箭头/画笔/荧光笔/马赛克时实时显示预览（之前松手才出现）；选中对象移动也实时跟随（预览按偏移渲染，原位不重影）。②**工具栏持久性**：标注/交互后工具栏被全屏置顶遮罩窗口盖住（遮罩激活时提升到置顶带最前）→ 点击工具栏实为命中遮罩、触发「重新框选」清空标注并塌缩选区（即「功能键无作用 / 工具栏消失」）——工具栏设 `Owner`（被拥有的窗口恒在 Owner 之上），永远不被遮罩/掩膜盖住。③**选区主体拖拽移动**：Selection 工具（默认）且未命中标注对象时，拖动主体 = 移动选区（无需 Alt）；Alt+拖主体与 8 手柄调整保持不变；命中标注对象时仍为对象选择/移动。

**新增/修改**：
- `Editor/Tools/IAnnotationTool.cs` — 接口新增 `Preview`（拖拽实时预览对象）；`DragToolBase` 每次移动重建预览、MouseUp 清空
- `Editor/Tools/DragTools.cs` — `ArrowTool`/`FreehandTool` 预览（箭头按起点→光标、画笔按当前采样点集）
- `Editor/Tools/SelectionTool.cs` — `Preview => IsActive ? Selected : null`（被移动对象本体，渲染层按 Delta 平移）
- `Editor/EditorViewModel.cs` — `OnMouseMove` 拖拽中始终 `Invalidate()`；暴露 `Preview`/`PreviewOffset`（马赛克预览注入底图）
- `Editor/AnnotationCanvas.cs` — 渲染预览层（与 Objects 同实例时跳过原位按偏移平移；选中虚线框跟随）
- `Selection/RegionSelectionWindow.xaml(.cs)` — 标注层同步预览（`SyncPreview`），`InvalidateAnnotationLayer(vm)` 携带预览
- `Selection/SelectionSession.cs` — 工具栏 `Owner` 锚点（选区中心所在窗口）；主体拖拽移动选区（Selection 工具 + 未命中对象）；悬停光标（可移动时 SizeAll）
- `tests/` — 6 个预览纯逻辑单测（矩形/画笔/箭头/文字/选择工具预览与偏移）
- `tools/verify-issue23.ps1` — issue #23 E2E（实时预览截图断言 / 工具栏不被掩膜盖住 / 点击工具栏不塌缩选区 / 手柄缩放 / 主体拖拽移动；场景间重启 app；环境预检：光标可动 + 截图可用 + 热键后遮罩出现，含重试）

**踩坑记录（重要）**：
1. **置顶窗口激活会盖住同置顶带的后显窗口**：RegionSelectionWindow 与工具栏都是 Topmost，用户在选区内按下鼠标（`Focus()`/激活）后全屏遮罩提到置顶带最前 → 工具栏被掩膜（50% 黑）盖住、点击落到遮罩上触发「重新框选」→ 标注清空 + 选区塌缩。修复用 WPF `Owner`（被拥有窗口恒在 Owner 之上），无需动置顶/激活逻辑。
2. **拖拽工具的实时预览需要三层配合**：工具暴露 Preview（每次移动重建）→ VM `OnMouseMove` 无条件 Invalidate → 画布渲染预览（马赛克预览需注入底图 SourceImage）。只加 Invalidate 不渲染预览对象是无效的。
3. **E2E 环境的输入注入会间歇性失效**（光标/键盘注入/CopyFromScreen 偶发几分钟不可用，非代码问题）：verify-issue23 加了环境预检（光标可动 + 截图可用 + 热键后遮罩出现）+ 重试；失效时应等待环境恢复后重跑，勿误判为产品回归。

## 四、接下来要做什么

| 里程碑 | 内容 | 验证方式 |
|--------|------|----------|
| CI 自动化 | GitHub Actions：PR / push main 自动 build + 单测，TRX 上传，README 徽章 | ✅ 已完成（单测 188/188，PR #8 已合并） |
| M7 打磨+发布 | 边界处理、错误提示、单文件 publish 冒烟、README | ✅ 已完成（单测 188/188，发布冒烟通过，PR #7 已合并；发布说明见 README「发布」小节） |
| M6 设置+i18n | 快捷键录制、语言/保存目录/滚轮步长设置、三语切换、开机自启（注册表 Run） | ✅ 已完成（单测 176/176，手工 E2E 已验证，PR #6 已合并） |
| —— | **M0-M7 全部完成**，进入维护期（缺陷修复/新功能建议） | — |
| #20 | 内联标注（取消独立编辑器，悬浮工具栏 + 框选标注同时进行） | ✅ 已完成（单测 197/197，verify-issue20 E2E PASSED） |
| #23 | 缺陷修复（实时标注预览 / 工具栏不被遮罩盖住 / 主体拖拽移动选区） | ✅ 已完成（单测 203/203；verify-issue23 E2E 待环境恢复后运行） |

## 五、总计划（架构与流程）

- **架构**：App（装配/托盘/i18n/设置）· Core（Win32/热键/捕获/剪贴板）· Selection（遮罩框选 + 内联标注）· Editor（标注引擎，EditorWindow 保留未调用）· Stitching（长截图）· Pin（贴屏）· Tests（xUnit 纯逻辑单测）。零第三方运行时依赖。
- **流程**：TDD 先行纯逻辑（已用于 ChordDetector）；每里程碑自动化/手工验证通过后 git commit；PR 由用户手动合并，合并后清除本地分支（远程分支保留）；发布验证：单文件 publish 冒烟已加入里程碑验证（verify-m7.ps1：热键→框选→剪贴板→无异常日志）。
- 完整设计：`docs/superpowers/specs/2026-08-07-easysniplite-design.md`；各里程碑 spec/plan 均在仓库内 `docs/superpowers/specs/` 与 `docs/superpowers/plans/`（早期总计划 `C:\Users\situweihao\.claude\plans\windows-delegated-kahan.md` 为历史存档）
