# M5 贴屏 + 托盘完善 + 单实例 — 设计文档

日期：2026-08-08
状态：已批准（用户逐项确认，brainstorming 流程）

## 1. 目标

贴屏：截图/标注完成后以**物理像素 1:1** 悬浮置顶在屏幕上，可拖动、缩放、调透明度、鼠标穿透，支持**多张同时贴屏**、重叠点击置顶（参考 Snipaste）。同时完成托盘菜单完善与单实例（Mutex）。开机自启**留到 M6**（用户确认）。

## 2. 交互决策（用户逐项确认）

| 决策点 | 结论 |
|--------|------|
| 贴屏入口 | **只在编辑器动作条**「贴到屏幕」；托盘不放贴屏项 |
| 贴屏后编辑器 | **关闭**（贴屏是流程出口，类似「完成」但贴屏而非复制） |
| 初始位置 | 截图区域在屏幕上的**原位置**（物理像素） |
| 初始尺寸 | 物理像素 1:1（`像素 / DpiScale` 布局尺寸） |
| 拖动/置顶 | 默认可拖动（穿透关闭时）；Topmost 组内点击激活即置前，天然满足多张重叠点击置顶 |
| 缩放 | Ctrl+滚轮，50%~300%，步进 10%（×1.1/÷1.1），右键菜单回 100%；只影响显示不改像素 |
| 穿透切换 | 右键菜单勾选 + **全局热键 Ctrl+Shift+P**（切换全部贴屏窗口穿透；穿透后鼠标点不到窗口，热键是恢复手段） |
| 透明度 | 右键菜单子菜单预设：100/85/70/50%（WPF `Window.Opacity`） |
| 单实例 | Mutex；二次启动弹提示「已在运行」后退出 |
| 开机自启 | M6 做（注册表 Run + 设置页开关） |
| 托盘菜单 | 保留 区域截图 / 退出；**移除「滚动长截图」**（M4 已跳过，半成品入口移除，将来重启 M4 再加回） |

## 3. 实现方案

**方案 A（已选）**：WPF 无边框窗口 + Win32 扩展样式。
- `WindowStyle=None, AllowsTransparency=True, Topmost, ShowInTaskbar=False`
- **穿透** = `SetWindowLongPtr(GWL_EXSTYLE)` 增删 `WS_EX_TRANSPARENT`（Win32.cs 已声明）
- **透明度** = WPF `Window.Opacity`（不用 `SetLayeredWindowAttributes`——WPF AllowsTransparency 窗口由 DWM 合成，改 LWA_ALPHA 无效）
- 拖动 = 左键 `DragMove()`；缩放 = 窗口 `Width/Height` 变化 + `Image Stretch=Fill`
- 备选已否：原生 LayeredWindow（代码量大、风格割裂）、WinForms Form（与 WPF 主栈不一致）

## 4. 架构与组件

```
Pin/                      新目录
  PinWindow.xaml(.cs)     单张贴屏窗口（WPF 无边框 + WS_EX_TRANSPARENT 穿透切换）
  PinMath.cs              纯逻辑：物理像素↔WPF 布局换算 + 缩放钳制（TDD 先行）
```

- **PinWindow**：构造参数 `BitmapSource`（物理像素）+ 截图区域屏幕物理坐标 (x, y)。初始 `Width/Height = 物理像素 / DpiScale`，初始 `Left/Top = 物理坐标 / DpiScale`。内容：`Image` + 细边框。状态：`IsPassthrough`、`Opacity`（档位）、`Zoom`。右键 `ContextMenu`：穿透(勾选)/透明度子菜单/100% 缩放/复制/保存/关闭。Ctrl+滚轮 `PreviewMouseWheel` 缩放。`Closed` 事件通知 App 移除。
- **PinMath**（纯函数，单测）：
  - `LayoutSize(int pixelW, int pixelH, double dpiScale, double zoom)` → `(double w, double h)`
  - `LayoutPosition(int pixelX, int pixelY, double dpiScale)` → `(double x, double y)`
  - `NextZoom(double current, double wheelDeltaSign)` 步进 ×1.1/÷1.1，钳制 [0.5, 3.0]
- **App.xaml.cs**：
  - `List<PinWindow> _pins` 持有全部贴屏窗口（防 GC），`Closed` 移除
  - `OpenEditor` 改实例方法，携带 `SelectionSession.SelectedRegion`（已有属性）截图物理坐标
  - 编辑器「贴到屏幕」→ `EditorViewModel.Compose()`（已有）→ 关闭编辑器 → `OpenPin(bitmap, region)`
  - 单实例：`OnStartup` 最先 `new Mutex(true, "EasySnipLite_SingleInstance", out createdNew)`；非首次 → `MessageBox`「已在运行」→ `Shutdown()`；实例持有字段防 GC
  - 全局热键 Ctrl+Shift+P：`OnKey` 判定（KeyboardHookService 复用），切换全部 PinWindow 穿透
- **Tray/TrayIconService.cs**：移除「滚动长截图」菜单项与 `LongCaptureRequested` 关联（App 中相关代码同步清理），保留 区域截图/退出
- **Editor/EditorWindow.xaml(.cs)**：动作条新增「贴到屏幕」按钮（`Compose()` 后经事件交给 App）

## 5. 数据流

```
编辑器点「贴到屏幕」→ EditorViewModel.Compose() → EditorWindow.Close()
  → App.OpenPin(bitmap, region 物理坐标) → new PinWindow(bitmap, x, y).Show() → _pins.Add
PinWindow.Closed → _pins.Remove（多张并存互不影响）
单实例：App 启动 → Mutex 判定 → 已在运行则提示并退出
全局热键 Ctrl+Shift+P → 遍历 _pins 切换 IsPassthrough
```

复制走 `ClipboardEx`（DIB+PNG 双格式）、保存走 `ImageFile.SavePngWithDialog`，与现有流程一致。

## 6. 错误处理

- 单实例二次启动：提示后退出
- 穿透开启后窗口不可鼠标操作：Ctrl+Shift+P 全局热键恢复（唯一恢复途径，热键不依赖鼠标）
- 复制/保存失败：沿用 ClipboardEx/ImageFile 现有行为

## 7. 测试与验证

- **单测（TDD 先行）**：`PinMathTests` — 1:1 换算、DPI 变化（1.0/1.25/1.5）、缩放钳制边界（0.5/3.0）、滚轮步进
- **手工 E2E 清单**：
  1. 截图 300x200 → 标注 → 贴屏：窗口物理尺寸 300x200，位于截图原位置
  2. 拖动贴屏窗口
  3. 穿透勾选后点击透传到下层窗口；Ctrl+Shift+P 恢复
  4. 透明度 100/85/70/50% 生效
  5. Ctrl+滚轮缩放 50%~300%，100% 还原
  6. 多张贴屏重叠，点击置顶
  7. 二次启动提示「已在运行」并退出
  8. 托盘：区域截图/退出正常，无「滚动长截图」项

## 8. 里程碑范围

M5 = 贴屏（PinWindow + PinMath + 编辑器入口）+ 托盘菜单完善 + 单实例。**不在范围**：开机自启（M6）、托盘贴屏入口（用户否决）、设置页（M6）、i18n（M6）。
