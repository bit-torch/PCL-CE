# 跨平台桌面端（Avalonia 预览）

仓库现在提供两个桌面入口：`PCL.Avalonia` 面向 Windows、Linux 和 macOS；
`Plain Craft Launcher 2` 保留原有 WPF 桌面端。共享的 `PCL.Core` 与
`PCL.Launcher.Core` 目标框架改为 `net10.0`，不再引用 WPF。
WPF 控件、行为及 Windows 桌面入口仍使用 `net10.0-windows`。

**新版入口已接入主要业务页面，框架专属界面与外部服务仍有兼容边界。**
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
   可设置启动后保留、隐藏、最小化或退出启动器。退出模式不重定向游戏输出；大厅运行时保留启动器以维持联机。其余模式在游戏运行期间阻止手动关闭，退出游戏后恢复窗口。

## 当前覆盖范围

| 功能 | Avalonia 入口 |
|---|---|
| 原版安装、文件修复、版本选择 | 已接入 |
| Fabric / Legacy Fabric | Meta API 版本列表与配置安装 |
| Forge / NeoForge / Cleanroom | 在线安装器、原有 Java 安装桥与依赖预下载 |
| OptiFine | 官方页面版本发现、本地安装桥；安装要求 JDK |
| LiteLoader / LabyMod | 对应版本清单与依赖安装 |
| Modrinth / CurseForge 资源 | 搜索、版本筛选、必需依赖、收藏；CurseForge 需要 API 密钥并遵守作者下载限制 |
| 本地 Mod / 资源包 / 光影 / 投影 | 导入、启停、备份、元数据与内嵌 Mod、Modrinth 更新检查 |
| 整合包 | mrpack、CurseForge、MCBBS、HMCL、MultiMC、普通版本目录压缩包；独立暂存后发布 |
| 版本隔离与启动设置 | 独立数据目录、每实例 Java/内存覆盖、窗口尺寸、附加 JVM/游戏参数、环境变量、服务器快速加入 |
| 存档 | 复用原解析器与编辑器，导入/导出、设置修改前备份、所选存档数据包管理 |
| 服务器 / 截图 | 读写原 servers.dat、状态检查、快速加入；截图预览与备份 |
| 账户 | 多账户、旧账户存储迁移、微软设备码、第三方密码 / Yggdrasil Connect、令牌刷新 |
| 皮肤 / 披风 | 微软账户在线读取、上传、重置、披风切换 |
| Java | 常见目录与 PATH 检测，Adoptium JDK 下载、SHA-256 校验与安全解压 |
| 联机 | 复用 Scaffolding 大厅协议，EasyTier 按系统下载或选择已有目录，创建 / 加入 / 退出 |
| 崩溃分析 | 复用原规则，手动日志与最新日志分析、报告导出 |
| 更新与发布 | 六 RID 发布包、下载校验、退出后替换与重启、保留旧程序；发布包作为 draft release 附件 |
| 首页 | Minecraft 新闻；可载入受限的声明式自定义 XAML |
| 外观与媒体 | 明暗主题、主题色、字体、背景透明度与图片、音乐播放与循环/音量、背景视频；需要系统 LibVLC |
| 本地化 | 读取原有七种语言的字符串字典；新界面未匹配的文本仍回退到中文 |

这些入口已实现，但不能将构建或内存 HTTP fixtures 视为所有历史加载器、真实账户、
异地联机或每种显卡/媒体后端已经通过真机验证。原 WPF 入口继续保留，迁移对照见
[功能清单](cross-platform-features.md)。

### 自定义主页兼容范围

主页由受限 XML 控件解析器呈现，不执行 CLR 类型、代码、绑定或任意命令。
现阶段支持常见文本、卡片、堆叠布局、Grid 行列与跨格、WrapPanel、滚动容器及按钮链接等声明元素；复杂 WPF 专属控件、
标记扩展与可执行事件不能直接照搬。HTTP(S) 按钮仅在用户点击时打开浏览器。

### 媒体运行依赖

LibVLCSharp 仅在请求播放时加载 native LibVLC，因此缺少播放器不影响启动、下载或测试。
请安装与系统架构匹配的 VLC / libvlc；也可在外观页指定库目录。Linux 需要 libvlc 和相关
音视频插件，macOS 可使用 VLC 应用内的 lib 目录，Windows 可选择 VLC 安装目录。
系统 LibVLC 未打包进入自包含发布包，绑定的 LGPL 声明随应用分发。

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
