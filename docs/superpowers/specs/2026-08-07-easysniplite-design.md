# EasySnipLite — Windows 轻量截图与标注工具设计文档

日期：2026-08-07
状态：已批准（用户逐项确认）

## 1. 产品定位

轻量快速的 Windows 截图与标注工具：按下全局快捷键（任意应用唤起），拖拽框选区域，标注后复制、保存或贴到屏幕。专注日常截图与无缝长截图。

## 2. 功能清单

| 功能 | 说明 |
|------|------|
| 全局快捷键 | Ctrl + 双击空格（双击间隔 ≤300ms）唤起，可在设置中录制自定义 |
| 区域可调 | 框选后 8 手柄改大小、内部拖动移动位置、方向键 1px 微调 |
| 标注工具 | 矩形、椭圆、箭头、画笔、荧光笔、马赛克、文字、表情贴纸（共 8 种） |
| 滚动长截图 | 自动滚动 + 纯 C# 图像拼接，实时预览，浏览器/PDF/聊天窗口通用 |
| 贴在屏幕 | 截图以真实尺寸（物理像素 1:1）悬浮置顶，可鼠标穿透、调透明度 |
| 托盘常驻 | NotifyIcon 常驻，无任务栏窗口；开机自启可选 |
| 多语言 | 英文 / 简体中文 / 繁体中文，跟随系统 + 设置内即时切换 |

## 3. 技术决策（用户确认）

1. **技术栈**：C# 14 / WPF / .NET 10（`net10.0-windows`）。SDK 10.0.302 已安装。
2. **长截图方案**：自动滚动（`SendInput` 模拟滚轮）+ 纯 C# 拼接（灰度降采样 + 按行 SAD 相关搜索垂直偏移），实时预览窗口。
3. **标注模型**：全矢量对象模型——每个标注是可选中/移动/缩放/删除/改属性的对象，支持 Ctrl+Z / Ctrl+Y 多步撤销重做。
4. **表情贴纸**：渲染 Windows 自带 Segoe UI Emoji 彩色字体（DirectWrite，零额外体积），分类选择面板。
5. **分发**：单文件绿色版 exe（`PublishSingleFile` + self-contained），设置存 `%AppData%\EasySnipLite\settings.json`。
6. **依赖策略**：零第三方运行时依赖（框架内置 + P/Invoke），测试仅 xUnit。

## 4. 架构与模块

```
EasySnipLite.sln
├─ src/EasySnipLite/        WPF 主程序（net10.0-windows, UseWPF+UseWindowsForms）
│   ├─ App/                 入口、单实例 Mutex、托盘、i18n、服务装配
│   ├─ Core/                Win32 P/Invoke、设置、热键、屏幕捕获、剪贴板
│   ├─ Selection/           全屏透明遮罩框选窗口 + 放大镜
│   ├─ Editor/              标注编辑器（矢量对象 + 工具 + 撤销栈 + Emoji 面板）
│   ├─ Stitching/           滚动长截图引擎（对齐算法 + 调度 + 实时预览）
│   ├─ Pin/                 贴屏窗口
│   └─ Localization/        三语资源
└─ tests/EasySnipLite.Tests/ xUnit（ImageAligner / UndoStack / ChordDetector / DpiMath）
```

### 4.1 核心组件职责

**Core/Native/Win32.cs** — 集中 P/Invoke：`SetWindowsHookEx/CallNextHookEx/UnhookWindowsHookEx`、`BitBlt/CreateCompatibleDC`、`GetDpiForMonitor/GetDpiForWindow`、`SendInput`、`SetWindowLongPtr`（WS_EX_TRANSPARENT）、`MonitorFromWindow`、`GetSystemMetrics`。

**Core/Hotkeys/ChordDetector.cs**（纯逻辑，可单测）— 输入键盘事件流（KeyUp/KeyDown + 时间戳 + 修饰键状态），输出「双击触发」事件。规则：Ctrl 按下期间，Space 两次 KeyUp 间隔 ≤300ms。

**Core/Hotkeys/KeyboardHookService.cs** — `WH_KEYBOARD_LL` 钩子跑在专用 STA 线程（带 Dispatcher 消息循环），事件转交给 ChordDetector 与当前活动窗口。

**Core/Imaging/ScreenCapture.cs** — 对虚拟屏幕每个显示器 `BitBlt` 冻结为 `BitmapSource`（物理像素），返回按显示器分组的捕获结果。所有坐标使用物理像素。

**Core/Clipboard/ClipboardEx.cs** — 同时写入 `DataFormats.Dib`（BITMAPINFOHEADER + 像素流）与 PNG 字节流，兼容所有粘贴目标。

**Selection/RegionSelectionWindow.cs** — 每显示器一个全屏透明置顶窗口（`WindowStyle=None, AllowsTransparency=True, Topmost, ShowInTaskbar=False`），叠加冻结位图；框选、8 手柄、内部移动、方向键微调、放大镜、尺寸标签、Enter/Esc。

**Editor/Models/AnnotationObject.cs** — 抽象基类：`Bounds / Color / StrokeWidth / IsSelected / Clone() / Render(DrawingContext)`；派生：Rectangle/Ellipse/Arrow/Freehand/Highlighter/Mosaic/Text/Emoji。

**Editor/UndoRedo/UndoStack.cs**（纯逻辑，可单测）— 命令式撤销栈（Add/Delete/Transform），`Undo()/Redo()/Push()`，容量上限。

**Editor/Tools/** — 每个工具一个交互状态机类（`MouseDown/MouseMove/MouseUp` 转标注对象），EditorViewModel 管理激活工具与选中对象。

**Stitching/ImageAligner.cs**（纯函数，可单测）— `FindVerticalOffset(BitmapSource frame, BitmapSource tail)`：灰度 + 横向降采样 → 在搜索范围内逐行计算 SAD/归一化互相关 → 返回最佳偏移与置信度。

**Stitching/ScrollCaptureEngine.cs** — 调度循环：`ScrollInput.Scroll(区域中心, 步长)` → 等待稳定（定时器）→ 截帧 → `ImageAligner` 对齐 → 拼接 → 更新预览；终止条件：连续 N 帧无新内容（到底）、总高 ≥20000px、用户停止；对齐失败接缝标记可重试。

**Pin/PinWindow.cs** — 无边框置顶，`Width = 物理像素 / DpiScale` 实现 1:1；`WS_EX_TRANSPARENT | WS_EX_LAYERED` 实现鼠标穿透 + 透明度；工具条：穿透开关、透明度滑条、100% 缩放、复制/保存/关闭。

**Tray/TrayIconService.cs** — `System.Windows.Forms.NotifyIcon`（csproj 同时开 UseWPF + UseWindowsForms，无需第三方包）；菜单：区域截图/长截图/设置/退出。

## 5. 关键交互流程

### 5.1 区域截图
1. 热键触发 → 立即冻结全部显示器 → 显示遮罩窗口
2. 拖拽框选 → 松手进入调整态（手柄/移动/微调）
3. Enter/双击确认 → 裁剪 → 打开标注编辑器
4. Esc 取消

### 5.2 标注编辑器
- 工具条：8 工具 + 颜色/粗细 + 撤销/重做 + 取消
- 动作条：复制 / 保存 / 贴到屏幕 / 完成
- 渲染层：底图 → 标注对象（矢量）→ 选中装饰层

### 5.3 滚动长截图
1. 托盘菜单「长截图」→ 框选滚动捕获区域 → 进入预览窗口
2. 引擎自动滚动 → 截帧 → 拼接 → 预览实时增长
3. 到底/上限/手动停止 → 结果进入标注编辑器

## 6. 关键技术点与边界

- **DPI**：manifest `PerMonitorV2`；捕获与 Win32 几何全用物理像素；WPF 布局经 `VisualTreeHelper.GetDpi` 换算；每显示器独立遮罩窗口规避混合 DPI 模糊。
- **钩子线程**：低级键盘钩子回调所在线程必须运行消息循环，否则钩子被卸载；钩子回调保持极简。
- **马赛克**：路径 + 块大小的矢量对象；渲染时对覆盖区域降采样→最近邻放大，原图不被破坏，可撤销。
- **长图上限**：拼接高度 ≤20000px（WPF 位图与内存安全边界）；拼接完成后立即释放中间帧。
- **剪贴板**：DIB + PNG 双格式，粘贴兼容性最大化。
- **错误处理**：捕获失败/剪贴板占用/保存失败均有用户可见提示；热键录制时检测冲突。

## 7. 测试策略

- 纯逻辑一律 TDD 先行：`ImageAligner`（合成图偏移恢复/无重叠/噪声鲁棒）、`UndoStack`（命令执行/撤销/重做/容量）、`ChordDetector`（时序/修饰键/双击边界）、`DpiMath`（缩放换算）。
- UI 部分手工验证，每个里程碑有端到端走查清单。
- 最终：双显示器（150% + 100% 混合缩放）回归。

## 8. 里程碑

M0 脚手架 → M1 截图主干（热键/冻结/框选/复制）→ M2 选区完善 → M3 标注编辑器 → M4 滚动长截图 → M5 贴屏+托盘+单实例 → M6 设置+i18n → M7 打磨+单文件发布。每个里程碑有可验证交付物并 git commit。
