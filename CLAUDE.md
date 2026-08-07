# EasySnipLite — Claude Code 项目说明

Windows 轻量截图与标注工具（C# 14 / WPF / .NET 10）。全局热键唤起、框选、矢量标注、复制/保存/贴到屏幕、自动滚动长截图、托盘常驻、英/简/繁三语。

## 常用命令

```bash
dotnet build EasySnipLite.slnx            # 构建（Debug 输出在 bin/Debug/net10.0-windows/win-x64/）
dotnet test EasySnipLite.slnx             # 运行全部单测（xUnit）
dotnet test --filter "FullyQualifiedName~ChordDetectorTests"   # 跑指定测试
powershell -ExecutionPolicy Bypass -File tools/verify-m1.ps1   # M1 端到端验证（热键→框选→剪贴板）
dotnet publish src/EasySnipLite -c Release -r win-x64 -o dist  # 单文件发布（M7 冒烟用）
```

## 目录结构

```
src/EasySnipLite/          WPF 主程序（net10.0-windows, UseWPF+UseWindowsForms）
  Core/Native/Win32.cs     全部 P/Invoke 集中于此
  Core/Hotkeys/            ChordDetector（纯逻辑）+ KeyboardHookService（WH_KEYBOARD_LL）
  Core/Imaging/            ScreenCapture（BitBlt 冻结，物理像素）
  Core/Clipboard/          ClipboardEx（DIB+PNG 双格式，命名空间 ClipboardServices）
  Selection/               SelectionSession（会话协调）+ RegionSelectionWindow（全屏遮罩）
  Stitching/               滚动长截图（未实现，M4）
  Editor/                  标注编辑器（未实现，M3）
  Pin/                     贴屏（未实现，M5）
  Tray/                    TrayIconService（WinForms NotifyIcon）
tests/EasySnipLite.Tests/  xUnit 纯逻辑单测
tools/                     PowerShell 端到端验证脚本（纯 ASCII，勿加中文）
docs/                      PROGRESS.md（里程碑进度）、superpowers/specs/（设计文档）
```

## 关键约定（务必遵守）

1. **全局类型别名**：`GlobalUsings.cs` 消解 WPF/WinForms 双引入二义性（Point/Rect/Size/Rectangle/Application/DataObject/Clipboard 等）。新增代码遇二义性时优先在此加别名，不要到处写全限定。
2. **坐标体系**：截图与 Win32 几何一律用**虚拟屏幕物理像素**；WPF 布局坐标 = 物理像素 / 显示器 DpiScale。禁用 `VisualTreeHelper` 之外的隐式换算。
3. **低级键盘钩子回调内严禁任何慢操作**（磁盘 I/O、加锁、UI 交互）——系统会超时静默移除钩子。诊断日志只允许在 UI 线程/非回调路径。
4. **纯逻辑 TDD 先行**：ChordDetector、UndoStack、ImageAligner、DpiMath 等无 UI 依赖的类必须先写测试再实现（red→green）。
5. **剪贴板写入**：DIB 编码交给 WinForms `DataObject.SetImage`（手写 DIB 头不被 GetImage 识别），PNG 格式自己补写；必须在 STA（UI）线程调用。
6. **新增类命名空间**：避开与常用类型同名的命名空间（如 `Core.Clipboard` 与 WPF `Clipboard` 类冲突，已改名 `ClipboardServices`）。
7. **验证纪律**：每个里程碑结束运行对应验证脚本 + `dotnet test` 全绿后才提交；改完代码必须重新 build（构建失败时跑的是旧 exe，验证结果无效）。
8. **进度跟踪**：里程碑进度与踩坑记录见 `docs/PROGRESS.md`，动手前先看「接下来要做什么」。

## 现状

- M0 脚手架 ✅ / M1 截图主干 ✅（热键→冻结→框选→剪贴板，E2E 已验证）
- M2 选区完善（手柄/微调/放大镜/保存）→ M7 单文件发布：规划中，见 docs/PROGRESS.md
