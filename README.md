# EasySnipLite

轻量快速的 Windows 截图与标注工具 —— 按下全局快捷键，拖拽框选区域，标注后复制、保存或贴到屏幕。专注日常截图与无缝长截图。

> 当前进度：M0 脚手架、M1 截图主干、M2 选区完善已完成（热键唤起 → 冻结屏幕 → 框选 → 8 手柄调整 / 方向键微调 / 放大镜 → 复制或保存已可用）；标注、长截图、贴屏等模块开发中。详见 [docs/PROGRESS.md](docs/PROGRESS.md)。

## ✨ 功能

| 功能        | 说明                                                   | 状态      |
| ----------- | ------------------------------------------------------ | --------- |
| 全局快捷键  | 任意应用按 **Ctrl + 双击空格** 唤起（可自定义）        | ✅        |
| 区域框选    | 拖拽框选，选区可 8 手柄调整、内部拖动、方向键微调      | 🔧 完善中 |
| 标注工具    | 矩形、椭圆、箭头、画笔、荧光笔、马赛克、文字、表情贴纸 | 🚧 开发中 |
| 滚动长截图  | 浏览器 / PDF / 聊天窗口自动滚动无缝拼接，实时预览      | 🚧 开发中 |
| 贴在屏幕    | 截图以真实尺寸悬浮置顶，支持鼠标穿透、调透明度         | 🚧 开发中 |
| 复制 / 保存 | 复制到剪贴板（画图、Office、IM 均可粘贴）；保存为 PNG  | ✅        |
| 托盘常驻    | 不占任务栏；开机自启可选                               | ✅ 基础版 |
| 多语言      | 英文 / 简体中文 / 繁体中文                             | 🚧 开发中 |

## 🚀 快速开始

```bash
# 构建
dotnet build EasySnipLite.slnx

# 运行（Debug 构建产物）
src/EasySnipLite/bin/Debug/net10.0-windows/win-x64/EasySnipLite.exe

# 测试
dotnet test EasySnipLite.slnx
```

**使用**：启动后常驻托盘。任意应用按下 `Ctrl + 双击空格` → 拖拽框选区域 → `Enter` 确认 → 图像已复制到剪贴板（`Esc` 取消）。托盘右键菜单可再次唤起区域截图或退出。

## 📦 技术栈

- C# 14 / WPF / .NET 10（`net10.0-windows`），PerMonitorV2 高 DPI 感知
- 零第三方运行时依赖（框架内置 + Win32 P/Invoke）
- 多显示器支持（每显示器独立捕获与遮罩窗口，物理像素坐标统一换算）
- 发布目标：单文件绿色版 exe（`PublishSingleFile` + self-contained）

## 📁 项目结构

```
EasySnipLite.slnx
├─ src/EasySnipLite/        WPF 主程序（Core / Selection / Editor / Stitching / Pin / Tray）
├─ tests/EasySnipLite.Tests/ xUnit 单测（拼接对齐、撤销栈、热键时序等纯逻辑）
├─ tools/                   端到端验证脚本
└─ docs/                    设计文档与里程碑进度
```

## 🧭 开发状态

| 里程碑 | 内容                                         | 状态                    |
| ------ | -------------------------------------------- | ----------------------- |
| M0     | 解决方案脚手架                               | ✅ 完成                 |
| M1     | 截图主干（热键/冻结/框选/复制）              | ✅ 完成（E2E 验证通过） |
| M2     | 选区完善（手柄/微调/放大镜/保存）            | ✅ 完成（E2E 验证通过） |
| M3     | 标注编辑器（矢量对象 + 撤销重做）            | 待开发                  |
| M4     | 滚动长截图（自动滚动 + 图像拼接 + 实时预览） | 待开发                  |
| M5     | 贴屏 + 托盘完善 + 单实例                     | 待开发                  |
| M6     | 设置页 + 英/简/繁多语言                      | 待开发                  |
| M7     | 打磨 + 单文件发布                            | 待开发                  |

## 📄 文档

- [设计文档](docs/superpowers/specs/2026-08-07-easysniplite-design.md)
- [里程碑进度与踩坑记录](docs/PROGRESS.md)

## 📝 项目来源

本仓库原本是一个空项目。第一版产品需求描述参考了 [LiteSnap](https://github.com/HuibingLin/LiteSnap) 项目的简介与功能清单撰写,在此基础上确定了 EasySnipLite 的产品定位与功能范围。

代码由 Claude Code(deepseek-v4-flash 模型)辅助生成。

## License

见 [LICENSE](LICENSE)。
