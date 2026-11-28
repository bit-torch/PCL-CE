**简体中文** | [English](README-EN.md) | [繁體中文](README-ZH_TW.md)

> [!IMPORTANT]
> 本仓库（`bit-torch/PCL-CE`）由[比特火炬](https://github.com/bit-torch)维护，派生自上游仓库 [PCL-Community/PCL-CE](https://github.com/PCL-Community/PCL-CE)。本仓库不再 rebase 上游更改，转为独立演进。
> 本仓库仅提供非官方发布与支持。
> 由于对上游改动较大并添加跨平台，欢迎测试并反馈问题。
> 上游项目 PCL CE 基于 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 二次开发。
> 本项目已脱离原fork网络（不要再查了）

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Community Edition

[社区版下载](https://github.com/bit-torch/PCL-CE/releases/latest) |
[上上游存储库](https://github.com/Meloong-Git/PCL)

[提交问题](https://github.com/PCL-Community/PCL-CE/issues/new/choose) |
[贡献指南](https://github.com/PCL-Community/PCL-CE/wiki/开发指南)

</div>

PCL CE 是基于 PCL 开源代码二次开发的社区版本，包括了主线暂未制作的功能和改进！

社区版的版本号与主线并非严格对应关系，也请不要向官方仓库反馈社区版问题。

欢迎大家来用用看！


## 💻 支持平台

| 操作系统 | 支持情况 | 环境要求 |
|---|---|---|
| Windows 10 1809 (17763) 或更高 | ✅ 完整支持（Avalonia 桌面端） | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 8 - Windows 10 1809 (17763) | ⚠️ 理论能跑，酌情提供社区支持 | [.NET 10 Desktop Runtime](https://get.dot.net/10) |
| Windows 7 或更低版本 | ❌ 不支持 | / |
| macOS / Linux | 🧪 Avalonia 桌面端（默认发布，持续补齐功能） | [.NET 10 Runtime](https://get.dot.net/10)；自包含包无需安装 |
| 其他操作系统 | ❌ 暂无桌面运行支持 | / |

**✅ 完整支持**：尽可能提供一切相关支持，但必须确保启动器为最新版本。

**⚠️ 理论能跑，酌情提供社区支持**：PCL CE 应该可以在这些平台上运行，但不保证功能完全可用。你可能需要升级到完整支持的系统版本以获得进一步社区技术支持。

**❌ 不支持**：PCL CE 在这些平台的可用性较低，甚至压根打不开。请升级操作系统以使用 PCL CE。

**🧪 新界面预览**：`PCL.Desktop` 基于 Avalonia 与干净内核 `PCL.Kernel`，是 Windows / macOS / Linux 的默认构建与发布入口。旧 `PCL.Avalonia` 与 WPF 客户端已冻结，仅作迁移对照，不再随发布构建。

```bash
dotnet run --project PCL.Desktop/PCL.Desktop.csproj
```

开发使用 .NET 10 SDK。默认构建、测试与发布均使用 `PCL.Rebuild.slnx`；旧 WPF 与 `PCL.Avalonia` 项目保留在仓库中，仅供迁移对照。

**注**：    
社区仅对最新版本的启动器提供支持。    
取决于部分问题的特殊性（如系统不完整），有时你仍然必须升级操作系统以继续获得支持。    
PCL CE 始终建议使用最新版本的操作系统以获得最佳体验。    
你仍然可以尝试在不受支持的系统上使用 PCL CE，但可能会遇到很多额外问题。

## 🔒 许可证

- `Plain Craft Launcher 2/` 使用 [自定义许可证](https://github.com/PCL-Community/PCL-CE/blob/dev/Plain%20Craft%20Launcher%202/LICENCE)
- `其余所有目录` 使用 [Apache License 2.0](https://github.com/PCL-Community/PCL-CE/blob/dev/LICENSE)

## 🌟 统计数据

**此页浏览量**（总计 / 今日）：[![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/bit-torch/PCL-CE)](https://hits.zkitefly.eu.org/?tag=https://github.com/bit-torch/PCL-CE&web=true)

