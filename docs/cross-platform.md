# 跨平台桌面端（Avalonia 预览）

仓库现在提供两个桌面入口：`PCL.Avalonia` 面向 Windows、Linux 和 macOS；
`Plain Craft Launcher 2` 保留原有 WPF 桌面端。共享的 `PCL.Core` 与
`PCL.Launcher.Core` 目标框架改为 `net10.0`，不再引用 WPF。
WPF 控件、行为及 Windows 桌面入口仍使用 `net10.0-windows`。

**这是可运行的跨平台初版，不是原有 WPF 全部功能的等价替换。**
原启动器中仍有 Windows 专用业务逻辑；Avalonia 入口使用 `PCL.Launcher.Core/CrossPlatform`
中的独立安装与启动服务，避免调用这些旧流程。现有 Windows 发布流程继续发布 WPF 版本。

## 构建与运行

需要 .NET 11 SDK（仓库现有代码使用 C# 15）和 .NET 10 Runtime。
自包含发布产物不需要单独安装 .NET。Linux 桌面需要 X11 或 XWayland、fontconfig
及系统图形依赖。macOS 产物目前没有 Apple 签名或公证。

```bash
# 仅构建跨平台项目，不加载 WPF
# 关闭 SDK 和 Avalonia 的构建遥测
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
dotnet build PCL.CrossPlatform.slnx -c Release
dotnet run --project PCL.Avalonia/PCL.Avalonia.csproj

# 无外网、无显示服务器要求的回归测试
dotnet test PCL.CrossPlatform.Test/PCL.CrossPlatform.Test.csproj -c CI

# RID 可替换为 win-x64、win-arm64、linux-x64、linux-arm64、osx-x64、osx-arm64
dotnet publish PCL.Avalonia/PCL.Avalonia.csproj -c Release -r linux-x64 --self-contained true -o artifact/linux-x64
```

PowerShell 中设置环境变量使用 `$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'` 和
`$env:AVALONIA_TELEMETRY_OPTOUT='1'`。

CI 在 Windows、Linux 和 macOS 分别运行测试，为六个 RID 输出自包含压缩包。
macOS 包含 `.app` 目录。交叉发布 ARM64 不代表已经在 ARM64 真机运行过游戏。

## 使用

1. 在“设置”选择 Minecraft 目录、Java 可执行文件及最大内存。默认读取系统惯用的 Minecraft 目录。
2. 在“下载”获取 Mojang 版本列表并安装原版，或在“启动”刷新已有版本。
3. 使用离线用户名，或在“账户”使用获准访问 Minecraft API 的 Microsoft OAuth 客户端 ID 登录。
   登录复用核心现有的 Microsoft/Xbox/Minecraft 认证模块，不包含任何内置私人客户端凭据。
4. 选择版本后启动；已有文件缺失或损坏时可使用“检查并修复文件”。游戏日志显示在“日志”。
   游戏运行期间关闭启动器会被阻止，避免关闭标准输出管道影响游戏；退出游戏或使用“停止游戏”后即可关闭。

## 当前覆盖范围

| 功能 | Avalonia 入口 |
|---|---|
| 原生桌面窗口、主题、文件选择、状态与取消 | 已实现 |
| 原版版本列表、客户端下载、库与资源校验修复 | 已实现，使用 Mojang 官方地址 |
| 安全解包本地库、版本继承、平台规则 | 已实现 |
| 带空格路径、平台 classpath、macOS 首线程参数 | 已实现 |
| Windows PE Java 检测 | 复用现有解析器 |
| Linux/macOS Java 检测 | 解析 JDK `release`，支持符号链接、PATH、JAVA_HOME、SDKMAN、常见安装目录 |
| 离线账户、微软设备码登录、令牌刷新 | 已接入；微软登录需要可用客户端 ID |
| 已安装的标准 Fabric/Forge 版本 JSON | 支持通用继承和参数；尚未完成各代加载器的实机兼容验证 |
| Forge/Fabric 安装器、整合包导入、Mod 管理 | 尚未移植至新界面 |
| 第三方认证界面、联机、服务器工具 | 尚未移植至新界面 |
| 自定义主页、动画、背景音乐、原窗口视觉样式 | 尚未移植至新界面 |
| 自动下载 Java、自动更新启动器 | 尚未移植至新入口 |
| 旧式只有 Windows 本地库的版本在 Unix/ARM64 运行 | 不承诺兼容；缺少 ARM64 本地库时明确拒绝使用 x64 库 |

## 数据与安全

Avalonia 设置位于 `Paths.SharedData/avalonia.settings.json`，与 WPF 设置分开；
游戏目录可选择现有目录，文件布局继续遵循 Mojang 约定。
Linux/macOS 应用数据写入用户配置目录，避免向只读 `.app` 或安装目录写入。

账户数据通过现有 `EncryptHelper` 加密后保存。Windows 保留原 DPAPI v1/v2 兼容及迁移行为。
Unix 使用独立的 `UserKey.unix.bin`（密钥文件格式 v3），以创建时 `0600` 权限保存 32 字节随机密钥。
**Unix 密钥文件依赖文件系统访问权限，不具备系统钥匙串的硬件或用户会话保护**；
备份设置与密钥时应将它们都视为敏感资料。读取时发现密钥对其他用户可读写会报错，并保留原文件。
Windows 密钥文件不与 Unix 密钥混用。

下载先写入唯一临时文件，验证元数据中的大小与 SHA-1 后原子替换；失败和取消会清理临时文件。
下载 URL 要求 HTTPS；版本、库与 ZIP 路径拒绝绝对路径及 `..` 跳转。
启动使用 `ProcessStartInfo.ArgumentList`，不通过 shell 拼接 Java 参数。
每次游戏启动使用独立本地库目录，游戏退出后清理；日志不会输出启动命令或账户访问令牌。

## 验证边界

测试包含平台规则、架构本地库、版本继承、离线 UUID、参数边界、下载校验与取消、
安装修复、ZIP 路径越界、Unix 密钥持久化及权限、Unix 子进程启动和 Avalonia 无界面窗口加载。
下载测试使用内存 HTTP 响应，不下载游戏。无界面窗口测试不能代替桌面真机操作、
真实 Microsoft 登录或 Minecraft OpenGL 游戏运行测试。
