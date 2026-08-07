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

### M1 截图主干（本次）
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

## 四、接下来要做什么

| 里程碑 | 内容 | 验证方式 |
|--------|------|----------|
| **M2 选区完善**（下一个） | 8 手柄缩放、内部拖动移动、方向键 1px 微调、角点放大镜、Esc/Enter、保存 PNG | 自动化拖拽+键控 + 手工 |
| M3 标注编辑器 | 矢量对象模型 + 8 工具 + 撤销重做 + 复制/保存/完成 | UndoStack 单测 + 手工 |
| M4 滚动长截图 | ImageAligner（TDD）→ 滚动引擎 → 实时预览 | 合成图对齐单测 + 浏览器/PDF 实测 |
| M5 贴屏+托盘 | PinWindow（1:1/穿透/透明度）、托盘菜单完善、单实例、开机自启 | 手工 |
| M6 设置+i18n | 快捷键录制、语言/保存目录/滚轮步长设置、三语切换 | 手工 |
| M7 打磨+发布 | 边界处理、错误提示、单文件 publish 冒烟、README | 冒烟测试 |

## 五、总计划（架构与流程）

- **架构**：App（装配/托盘/i18n/设置）· Core（Win32/热键/捕获/剪贴板）· Selection（遮罩框选）· Editor（标注）· Stitching（长截图）· Pin（贴屏）· Tests（xUnit 纯逻辑单测）。零第三方运行时依赖。
- **流程**：TDD 先行纯逻辑（已用于 ChordDetector）；每里程碑自动化/手工验证通过后 git commit；最终单文件发布 + 双屏混合 DPI 回归。
- 完整设计：`docs/superpowers/specs/2026-08-07-easysniplite-design.md`；实施计划：`C:\Users\situweihao\.claude\plans\windows-delegated-kahan.md`
