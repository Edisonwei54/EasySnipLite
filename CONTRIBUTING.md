# 贡献指南

欢迎向 EasySnipLite 提交贡献！无论是提 bug、建议新功能，还是直接提 PR，都感谢你的参与。

## 开发环境

- Windows 10/11（应用为 WPF 桌面程序，仅支持 Windows）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)（仓库 `global.json` 已锁定版本，本地版本不一致会自动提示）
- 建议使用 Visual Studio 2022+ 或 VS Code + C# Dev Kit

## 常用命令

```bash
dotnet build EasySnipLite.slnx            # 构建
dotnet test EasySnipLite.slnx             # 运行全部单测（xUnit）
dotnet test --filter "FullyQualifiedName~ChordDetectorTests"   # 跑指定测试
```

## 提 Issue

- **Bug**：请使用 Bug 模板，尽量描述复现步骤、预期/实际行为、环境（Windows 版本、分辨率/DPI、多显示器）。
- **新功能**：请使用 Feature 模板，说明想解决的问题场景和期望方案。

## 提 PR 流程

1. **Fork 仓库**并克隆到本地。
2. **新建功能分支**：`git checkout -b feature/你的改动描述`。
3. **纯逻辑先写测试**（TDD）：`ChordDetector`、`UndoStack`、`SelectionMath` 等无 UI 依赖的类，必须先写单测再实现（red → green）。
4. **本地验证通过后再提交**：
   ```bash
   dotnet build EasySnipLite.slnx    # 0 错误 0 警告
   dotnet test EasySnipLite.slnx     # 全绿
   ```
   涉及截图/热键等端到端功能时，运行对应验证脚本（`tools/verify-m1.ps1` 等，需在解锁桌面运行）。
5. **推送分支并创建 PR**：PR 会自动触发 CI（build + 全部单测），CI 通过且获得 review 后即可合并。
6. **合并方式**：维护者手动合并（Merge commit），请勿在 PR 里请求自动合并。

## 代码约定（重要）

- **坐标体系**：截图与 Win32 几何一律用虚拟屏幕物理像素；WPF 布局坐标 = 物理像素 / 显示器 DpiScale。
- **低级键盘钩子回调内严禁任何慢操作**（磁盘 I/O、加锁、UI 交互）——系统会超时静默移除钩子。
- **剪贴板写入**：DIB 编码交给 WinForms `DataObject.SetImage`（手写 DIB 头不被 GetImage 识别），PNG 格式自己补写；必须在 STA（UI）线程调用。
- **命名空间**：新增类避开与常用类型同名的命名空间（如用 `ClipboardServices` 而非 `Core.Clipboard`）。
- **全局类型别名**：WPF/WinForms 双引入的二义性在 `GlobalUsings.cs` 统一消解，不要到处写全限定。
- **多语言**：新增用户可见文案需同步英/简/繁三份 `AppResources.resx`。

## 提交信息风格

```
类型: 简述（影响范围）
```

例如：`feat: 新增 XX 功能`、`fix: 修复 XX 问题`、`docs: 更新 XX 文档`。类型可参考：`feat` / `fix` / `docs` / `refactor` / `test` / `ci` / `build`。
