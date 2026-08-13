# EasySnipLite — Claude Code 项目说明

Windows 轻量截图与标注工具（C# 14 / WPF / .NET 10）。全局热键唤起、框选、矢量标注、复制/保存/贴到屏幕、自动滚动长截图、托盘常驻、英/简/繁三语。

## 常用命令

```bash
# CI: .github/workflows/ci.yml（PR / push main 自动 build+test，GitHub Actions 徽章见 README）
# Release: git tag v1.0.1 && git push origin v1.0.1 → 自动 publish 单文件 exe 挂 GitHub Releases（.github/workflows/release.yml）
dotnet build EasySnipLite.slnx            # 构建（Debug 输出在 bin/Debug/net10.0-windows/win-x64/）
dotnet test EasySnipLite.slnx             # 运行全部单测（xUnit）
dotnet test --filter "FullyQualifiedName~ChordDetectorTests"   # 跑指定测试
powershell -ExecutionPolicy Bypass -File tools/verify-m1.ps1   # M1 端到端验证（热键→框选→剪贴板）
powershell -ExecutionPolicy Bypass -File tools/verify-issue20.ps1   # issue #20 内联标注 E2E（工具栏出现/内联绘制/Esc 三级/无 error.log）
powershell -ExecutionPolicy Bypass -File tools/verify-issue23.ps1   # issue #23 缺陷修复 E2E（实时预览/工具栏持久/选区主体拖拽移动；含环境预检+重试）
powershell -ExecutionPolicy Bypass -File tools/verify-m7.ps1   # M7 发布冒烟（publish 单文件→热键→剪贴板）
dotnet publish src/EasySnipLite -c Release -r win-x64 -o dist  # 单文件发布（M7 冒烟用）
```

## 目录结构

```
.github/                   GitHub Actions（ci.yml 构建测试 / codeql.yml 安全扫描 / release.yml 打 tag 自动发布）+ dependabot.yml + Issue/PR 模板
src/EasySnipLite/          WPF 主程序（net10.0-windows, UseWPF+UseWindowsForms）
  Core/Native/Win32.cs     全部 P/Invoke 集中于此
  Core/Hotkeys/            ChordDetector/ComboDetector（纯逻辑）+ KeyboardHookService（WH_KEYBOARD_LL）+ HotkeyRecorder/HotkeyFormat/ModifierMatch
  Core/Imaging/            ScreenCapture（BitBlt 冻结，物理像素）
  Core/Clipboard/          ClipboardEx（DIB+PNG 双格式，命名空间 ClipboardServices）
  Core/Diagnostics/        AppErrors 错误兜底（日志/气泡/致命弹窗）
  Core/Settings/           HotkeySpec + Settings 模型 + SettingsStore（settings.json 原子持久化）+ RegistryAutoStart（开机自启）+ ModifierKey（修饰键码判定）
  Localization/            AppResources.resx 三语（英/简/繁）+ LocaleService（运行时切换）
  Selection/               SelectionSession（会话协调）+ RegionSelectionWindow（全屏遮罩）+ AnnotationToolbarWindow（内联标注悬浮工具栏，issue #20）
  Stitching/               滚动长截图（M4 已实现但功能跳过，进度存档）
  Editor/                  标注引擎（矢量标注/撤销重做；EditorWindow 自 issue #20 起不再被调用，源码保留）+ AnnotationCanvas（遮罩内联标注层复用）
  Settings/                SettingsWindow（M6 设置窗口，命名空间 SettingsUI——避开与 Settings 类型同名）
  Pin/                     贴屏（M5 已实现：贴屏窗口/穿透/缩放）
  Tray/                    TrayIconService（WinForms NotifyIcon）
tests/EasySnipLite.Tests/  xUnit 纯逻辑单测
tools/                     PowerShell 端到端验证脚本（纯 ASCII，勿加中文）
docs/                      PROGRESS.md（里程碑进度）、superpowers/specs/（设计文档）、superpowers/plans/（实施计划）
```

## 关键约定（务必遵守）

1. **全局类型别名**：`GlobalUsings.cs` 消解 WPF/WinForms 双引入二义性（Point/Rect/Size/Rectangle/Application/DataObject/Clipboard 等）。新增代码遇二义性时优先在此加别名，不要到处写全限定。
2. **坐标体系**：截图与 Win32 几何一律用**虚拟屏幕物理像素**；WPF 布局坐标 = 物理像素 / 显示器 DpiScale。禁用 `VisualTreeHelper` 之外的隐式换算。
3. **低级键盘钩子回调内严禁任何慢操作**（磁盘 I/O、加锁、UI 交互）——系统会超时静默移除钩子。诊断日志只允许在 UI 线程/非回调路径。
4. **纯逻辑 TDD 先行**：ChordDetector、UndoStack、ImageAligner、SelectionMath、PinMath 等无 UI 依赖的类必须先写测试再实现（red→green）。
5. **剪贴板写入**：DIB 编码交给 WinForms `DataObject.SetImage`（手写 DIB 头不被 GetImage 识别），PNG 格式自己补写；必须在 STA（UI）线程调用。
6. **新增类命名空间**：避开与常用类型同名的命名空间（如 `Core.Clipboard` 与 WPF `Clipboard` 类冲突，已改名 `ClipboardServices`）。
7. **验证纪律**：每个里程碑结束运行对应验证脚本 + `dotnet test` 全绿后才提交；改完代码必须重新 build（构建失败时跑的是旧 exe，验证结果无效）。
8. **进度跟踪**：里程碑进度与踩坑记录见 `docs/PROGRESS.md`，动手前先看「接下来要做什么」。
9. **分支合并与删除（硬性规则）**：**分支合并动作完全不允许执行**（含 `gh pr merge`/`git merge`/squash 等任何形式），创建 PR 后停下由用户手动合并；**远程分支完全不允许删除**（含 `gh pr merge --delete-branch`、`git push origin --delete`），PR 合并后远程分支保留；**本地分支在合并后必须清除**（`git branch -d`），保持本地仓库干净（远程保留、本地清）。
10. **提交前文档同步**：开发完成、提交之前，必须检查并更新 `CLAUDE.md`、`README.md` 及相关进度/临时文档（如 `docs/PROGRESS.md`、`user/*.md`），文档与代码/仓库实际状态一致后再提交。

## 现状

- M0 脚手架 ✅ / M1 截图主干 ✅ / M2 选区完善 ✅ / M3 标注编辑器 ✅（均 E2E 已验证；**issue #20 起流程改为内联标注**：框选完成即冻结选区 + 选区下方悬浮工具栏，不再打开独立编辑器窗口，EditorWindow 源码保留未调用）
- M4 滚动长截图：实现 + 单测已存档（PR #4 合并进 main），因 E2E 阻塞已决定**跳过**；托盘「滚动长截图」入口已由 M5 移除
- M5 贴屏+托盘 ✅（单实例 Mutex/贴屏窗口/编辑器「贴到屏幕」入口/Ctrl+Shift+P 穿透热键）
- M6 设置+i18n ✅（设置窗口/截图+穿透双热键录制（截图单击/双击自动识别）/三语 resx 即时切换/settings.json 持久化/开机自启；单测 176/176 全绿，手工 E2E 已验证，PR #6 已合并）
- M7 打磨+发布 ✅（错误处理管线/启动+保存失败气泡/边界小修/单文件 publish 冒烟/版本 1.0.0；单测 188/188 全绿，verify-m7 通过，PR #7 已合并）
- issue #20 内联标注 ✅（取消独立编辑器：悬浮工具栏 + 框选调整与标注同时进行；单测 197/197 全绿，verify-issue20 E2E 通过，Esc 三级语义）
- issue #23 缺陷修复 ✅（实时标注预览 / 工具栏 Owner 恒在遮罩之上不被盖住 / Selection 工具主体拖拽移动选区 + OnConfirm 解耦先关窗再复制；单测 203/203 全绿，verify-issue23 + verify-issue20 E2E 均 PASSED，PR #24 已合并，issue #23 已关闭）
- 全部里程碑完成，进入维护期（缺陷修复/新功能建议）
- 维护期 GitHub 配置：CI（PR/push main 自动 build+test+CodeQL 扫描）、分支保护（main 必须 PR 合并且 build-test 通过）、Dependabot（依赖漏洞+每周版本巡检）、Release 自动发布（`git tag v1.0.0 && git push origin v1.0.0` → 单文件 exe 挂 Releases，首个 v1.0.0 已发布）、CONTRIBUTING/SECURITY/Issue+PR 模板
