# PCL 内核剥离与 Avalonia 全面迁移评估

> 本文是工程与合规评估，不构成法律意见。涉及许可证结论时，应由项目负责人复核，必要时咨询律师。
>
> 评估日期：2026-11-21
> 评估范围：仓库全部分支代码、资源、依赖与构建/发布流程

## 1. 结论摘要

1. **只删除 WPF 客户端不能解除《PCL 分发有限许可》的义务。** 只要仓库中仍保留 PCL 派生代码或资源，就仍属于“重度使用”，需要继续满足命名、署名、开源、正版劝导和 Windows 蓝色主色相等条件。
2. **要真正独立演进，必须同时移除三层内容**：
   - PCL 派生业务内核：`PCL.Launcher.Core/`、`PCL.Core/` 中与 PCL 同源的部分；
   - WPF 适配与 UI：`PCL.Core.Wpf/`、`Plain Craft Launcher 2/`；
   - PCL 品牌与内容资产：本地化字符串、图标/图片、更新源、PCL 专有签名与赞助逻辑。
3. **推荐路线**：以 Avalonia 为新 UI，建立干净的 `Kernel + Platforms + Desktop` 三层架构，按模块重写内核；能使用 Avalonia/.NET 等价物的，不引入 Rust；仅在 .NET 没有可靠跨平台实现时使用 Rust 原生库。
4. **当前代码规模**：拟剥离的旧代码约 16.1 万行 C# 加 103 个 XAML 文件，其中 `PCL.Launcher.Core` 明确标注为从 PCL 模块拆出。该规模不适合一次性删除，必须按“先建替代、再删旧、最后换 UI”的顺序推进。
5. **本次仅完成评估，没有删除或修改任何业务代码。** 第一批可执行动作见第 8 节。

## 2. 现状盘点

### 2.1 项目与代码规模

| 项目 | 目标框架 | 许可声明 | C# 行数 | XAML/AXAML | 角色 | 建议处置 |
|---|---|---:|---:|---:|---|---|
| `Plain Craft Launcher 2/` | `net10.0-windows` | `PCL 分发有限许可` | 72,327 | 95 | 原 PCL WPF 客户端 | 删除，不迁移代码 |
| `PCL.Core.Wpf/` | `net10.0-windows` | 注释声明 Apache-2.0 | 12,666 | 8 | WPF 适配层、控件、动画、效果、平台服务 | 删除；仅作行为参考，禁止整文件搬运 |
| `PCL.Launcher.Core/` | `net10.0` | `PCL 分发有限许可` | 37,767 | 0 | 从 PCL `Modules` 拆出的业务逻辑 | 删除并重写 |
| `PCL.Core/` | `net10.0` | 根 LICENSE 声明 Apache-2.0 | 37,790 | 0 | 共享内核、配置、网络、Java、账户、联机等 | 先做逐文件溯源；无法证明干净的部分重写 |
| `PCL.Avalonia/` | `net10.0` | 跟随根 LICENSE | 1,541 | 1 | Avalonia 预览 UI | 保留产品方向，重做界面与依赖 |
| `PCL.Core.Test/` | `net10.0` | Apache-2.0 | 5,602 | 0 | 核心测试 | 随新内核重写 |
| `PCL.CrossPlatform.Test/` | `net10.0` | Apache-2.0 | 927 | 0 | 跨平台测试 | 随新内核重写 |

### 2.2 直接暴露的 PCL 同源证据

- `PCL.Launcher.Core/PCL.Launcher.Core.csproj` 明确写着“从 Plain Craft Launcher 2/Modules 拆出的业务逻辑，自定义许可”，并随包分发 `LICENCE.Launcher`。
- `PCL.Launcher.Core/Launch/Core.cs` 保留 `McLaunchPrecheck()`，内含 `https://afdian.com/a/LTCat` 赞助链接，对应许可指南中的强制条款。
- `PCL.Launcher.Core/Network/Http/RequestSigning.cs` 使用 `PCL2/... PCLCE/...` User-Agent，并内置 CurseForge 密钥读取逻辑。
- `PCL.Launcher.Core/Updates/UpdateManager.cs` 的更新源指向 `PCL-Community/PCL2_CE_Server`、`pysio.online` 等上游/社区地址。
- `PCL.Avalonia/MainWindow.Tools.cs` 仍在关于页标注“上游作者：龙腾猫跃 / PCL Community”。
- 共 21 个 C# 文件直接引用 `RequestSigning`、`McLaunchPrecheck`、`LTCat`、`PCL-Community` 或 `pclc.cc`。
- `PCL.Core.Wpf/App/Localization/Languages/` 保存 PCL 的 7 种语言字典，每种约 3,124 个键；Avalonia 当前通过嵌入这些 `.xaml` 文件复用。
- `Plain Craft Launcher 2/metadata.json` 包含 PCL 版本号、上游版本、PCL 品牌名称与第三方资产署名。

### 2.3 WPF / Windows 专属依赖

| 依赖或 API | 使用位置 | 用途 | 替代方案（优先 Avalonia/.NET） |
|---|---|---|---|
| WPF / `UseWPF` | WPF 客户端、`PCL.Core.Wpf` | 整个桌面 UI | Avalonia 11 |
| `Markdig.Wpf` | WPF 客户端 | Markdown 渲染 | `Markdig` + Avalonia 渲染层（HTML 或自绘） |
| `Microsoft.Xaml.Behaviors.Wpf` | `PCL.Core.Wpf` | XAML 行为 | `Avalonia.Xaml.Behaviors` / 直接事件绑定 |
| `NAudio` | WPF 客户端 | 音频播放 | 已有 `LibVLCSharp`；或跨平台 .NET 音频库 |
| `System.Management`（WMI） | `PCL.Core/Utils/OS/HardwareInfo.cs` | 硬件信息 | Linux `/proc`、macOS `sysctl`、Windows 可选 WMI |
| `System.Security.Cryptography.ProtectedData`、`PlainToolkit.CngProtectedData` | `PCL.Core/Utils/Secret/` | DPAPI 密钥保护 | Windows DPAPI + Unix libsecret/Keychain 抽象 |
| `Windows.UI.Notifications` / WinRT | `PCL.Core/UI/ToastNotification.cs` 等 | Windows Toast | 各平台原生通知封装 |
| 注册表 | `PCL.Core/Minecraft/Java/Scanner/RegistryJavaScanner.cs` 等 | Java/系统探测 | 文件系统与环境变量探测，注册表作为可选增强 |
| `System.Drawing` | `PCL.Core/Utils/IconHelper.cs` | 图标处理 | SkiaSharp（Avalonia 已依赖） |
| WPF HLSL 像素着色器 | `PCL.Core.Wpf/UI/Effects/`、`UI/Assets/Shaders/` | 模糊/背景效果 | Avalonia Effect 或 SkiaShader；不建议为此引入 Rust |
| WPF `SvgIcon`、`NColor`、动画与控件 | `PCL.Core.Wpf/UI/` | UI 基础设施 | Avalonia 控件、`Avalonia.Animation`；图标可用 Lucide（ISC） |
| WPF 剪贴板、拖放、窗口互操作 | `PCL.Core.Wpf/Utils/OS/` | 平台服务 | Avalonia 平台 API |

`PCL.Core` 中约 30 个文件直接使用 Win32、WMI、DPAPI、WinRT 或注册表；这些文件即使留在 Apache-2.0 项目中，也需要平台抽象或条件编译，否则无法真正跨平台。

### 2.4 可以保留的宽松许可依赖

以下依赖本身许可宽松，可在新内核中继续使用，但需要统一登记到第三方声明与 SBOM：

`CommunityToolkit.Mvvm`、`Dapper`、`Microsoft.Data.Sqlite`、`SQLitePCLRaw`、`LiteDB`、`fNbt`、`SharpZipLib`、`protobuf-net`、`Polly`、`Downloader`、`DotNet.Glob`、`Wacton.Unicolour`、`Ae.Dns.Client`、`YamlDotNet`、`Humanizer`、`FluentValidation`、`Microsoft.Extensions.*`、`System.IdentityModel.Tokens.Jwt`、`SkiaSharp`、`Avalonia.*`、`LibVLCSharp`。

需要单独处理：

- **LegacyFix（GPL-3.0）**：若与启动器一起分发，可能触发 GPL 义务。应改为运行时可选下载的独立组件，并单独展示其许可证。
- **Iconmonstr / Iconfont 资产**：授权条款需核验，建议替换为 Lucide 或自绘资源。
- **PCL 本地化字典与图片**：属于 PCL 内容资产，不能作为新 UI 的基础。

## 3. 许可与合规分析

### 3.1 当前许可结构的问题

- 根 `LICENSE` 将除 `Plain Craft Launcher 2/` 外的目录声明为 Apache-2.0。
- `PCL.Launcher.Core/LICENCE` 却是 PCL 自定义许可，且该库被 Avalonia 一并分发。
- `PCL.Core.Wpf` 注释声明 Apache-2.0，但其控件、动画和效果与 WPF 客户端高度同源，是否能独立视为社区原创需要证据。
- `PCL.Core` 声明 Apache-2.0，但仓库只有一个压缩后的提交，无法用 Git 历史证明哪些文件是干净重写、哪些是 PCL 代码改写。

**结论**：当前“目录级许可”与“代码实际来源”并不一致。继续保留这些代码时，Apache-2.0 声明存在被挑战的风险；删除 WPF 客户端也不会自动消除该风险。

### 3.2 PCL 许可的关键约束

根据 `Plain Craft Launcher 2/LICENCE` 与《PCL 存储库合理使用指南》：

- 仅“原样副本”的存储、镜像、分发获得明确许可。
- 基于仓库修改、或大量参考后重新实现实质功能，属于“重度使用”。
- 重度使用要求：
  1. 明确标注为第三方基于 PCL 的二次创作；
  2. 名称必须以 `Plain Craft Launcher (PCL)` 开头并加第三方后缀；
  3. 关于页首位署名龙腾猫跃与赞助链接，公开源代码，并继续使用该指南；
  4. 若含 Minecraft 启动功能，需实现与 `McLaunchPrecheck` 一致或更激进的正版/赞助劝导；
  5. Windows 上不得出现类似 PCL 赞助解锁的功能或表现，例如主色相必须为蓝色。

### 3.3 合规路径选择

| 路径 | 是否解除上述义务 | 可行性 | 说明 |
|---|---|---|---|
| 只删除 WPF 客户端 | 否 | 高 | 内核和资源仍同源 |
| 删除 WPF + `PCL.Core.Wpf`，保留两个 Core | 否 | 中 | `PCL.Launcher.Core` 明确同源 |
| 保留 `PCL.Core`，重写其余部分 | 取决于审计 | 中 | 需逐文件证明 `PCL.Core` 干净 |
| 重写内核 + 新 UI + 替换资产 | 是 | 低到中 | 唯一能彻底退出的路径 |

**推荐**：按“重写内核 + 新 UI + 替换资产”推进；在完成前，继续遵守 PCL 许可，不要提前移除署名或更改许可声明。

## 4. 目标架构

### 4.1 项目划分

```text
PCL.slnx
├─ PCL.Kernel/                 # 干净内核，Apache-2.0
│  ├─ App/                     # 配置、日志、任务、生命周期
│  ├─ Minecraft/               # 版本清单、安装、修复、启动参数
│  ├─ Loaders/                 # Fabric/Forge/NeoForge/Quilt/OptiFine
│  ├─ Modpacks/                # mrpack、CurseForge、HMCL、MultiMC 等
│  ├─ Resources/               # Modrinth/CurseForge 资源目录
│  ├─ Accounts/                # 微软 OAuth、离线、Yggdrasil
│  ├─ Saves/                   # NBT、存档、servers.dat
│  ├─ Networking/              # HTTP、下载、镜像、更新
│  └─ Link/                    # 联机协议与大厅
├─ PCL.Kernel.Abstractions/    # 平台接口，不依赖 UI
├─ PCL.Desktop/                # Avalonia UI + ViewModels + 平台实现
├─ PCL.Native/                 # 可选 Rust cdylib，仅在 .NET 无等价物时引入
└─ PCL.Tests/                  # 单元、集成、无头 UI 测试
```

### 4.2 边界规则

- `PCL.Kernel` 与 `PCL.Kernel.Abstractions` 禁止引用任何 UI 框架。
- 平台能力通过接口注入：通知、密钥存储、硬件信息、剪贴板、文件对话框、主题、窗口宿主。
- UI 只负责呈现与交互，不复制业务算法。
- 所有对外协议、User-Agent、更新源、API 密钥使用本项目自有配置，不再复用 PCL/上游 CE 的标识与凭据。
- 新代码不得从旧项目整文件复制；确需参考行为时，只记录行为规格并重新实现。

### 4.3 UI 方向

- 使用 Avalonia 11 + `CommunityToolkit.Mvvm`，采用新的信息架构与视觉语言，不复刻 PCL 的页面布局、图标和配色。
- 建立新的字符串目录，不再使用 PCL 的 3,124 键字典；首批至少覆盖简体中文与英文。
- 换 UI 同时解决许可中的“Windows 蓝色主色相”和“赞助解锁类似功能”约束：新设计不依赖该模式。
- 若彻底重写，可同步评估是否更换产品名；若仍保留任何 PCL 派生代码，则名称必须继续以 PCL 开头。

## 5. 迁移阶段

### 阶段 0：冻结与隔离

- 将 WPF 客户端与 `PCL.Core.Wpf` 标记为冻结，不再新增功能。
- CI 的默认构建与发布切换到 Avalonia；WPF 仅保留临时验证任务，随后移除。
- 建立 PCL 专有逻辑清单：`RequestSigning`、`McLaunchPrecheck`、赞助链接、PCL 更新源、PCL 品牌与本地化。
- 建立 `docs/third-party/` 依赖与资产台账，记录来源、许可、是否随包分发。

### 阶段 1：干净内核骨架 + 纵向切片

- 新建 `PCL.Kernel`、`PCL.Kernel.Abstractions`、`PCL.Tests`。
- 先实现一条完整链路：版本清单 → 下载 → 校验 → 安装原版 → 生成启动参数 → 启动游戏。
- 在 Windows、Linux、macOS 三端跑通，不依赖 WPF 或 Windows 专属 API。
- 此阶段完成前不删除旧内核，避免项目不可用。

### 阶段 2：功能补齐

按依赖关系依次重写：

1. Java 探测与运行时安装；
2. Fabric/Legacy Fabric、Forge/NeoForge/Cleanroom、OptiFine、LiteLoader/LabyMod；
3. Modrinth/CurseForge 资源搜索、依赖解析与下载；
4. 整合包各格式与覆盖层；
5. 多账户、微软设备码、第三方认证、皮肤/披风；
6. 存档、服务器、截图、日志与崩溃分析；
7. 联机大厅、NAT/IPv6 探测、EasyTier；
8. 更新器与发布校验；
9. 平台服务：通知、密钥存储、硬件信息、剪贴板、文件对话框、主题。

### 阶段 3：新 UI

- 以 `PCL.Desktop` 替换 `PCL.Avalonia` 的临时界面。
- 新字符串目录、新图标与配色、新页面结构。
- 保持“关于”页的开源许可与第三方声明，但不再承担 PCL 署名义务（前提是内核已完全干净）。

### 阶段 4：删除旧代码与收尾

- 删除 `Plain Craft Launcher 2/`、`PCL.Core.Wpf/`、`PCL.Launcher.Core/`，以及 `PCL.Core/` 中确认同源的文件。
- 更新解决方案、CI、发布脚本、README、LICENSE、NOTICE。
- 只对新代码使用 Apache-2.0 或项目选定许可。

### 阶段 5：合规验收

- 全仓库扫描 PCL 专有标识：`PCL2`、`McLaunchPrecheck`、`LTCat`、`PCL-Community`、`pclc.cc`、`afdian`。
- 全仓库扫描 WPF/Windows 专属目标框架与包引用。
- 生成依赖清单与 SBOM，逐项核对许可。
- 保留 clean-room 记录：需求来源、实现者、参考范围、评审结论。

## 6. 需要重点决策的问题

1. **是否保留 `PCL.Core`**：若无法逐文件证明来源，建议一并重写；否则需要建立完整的溯源证据。
2. **是否更换产品名**：完全重写后可自由命名；继续使用 PCL 名称仍会被视为与 PCL 相关。
3. **Rust 的引入时机**：按当前策略，先用 Avalonia/.NET；只有硬件信息、密钥存储、原生通知等确实缺少可靠 .NET 实现时，才建 `PCL.Native`。
4. **LegacyFix 的分发方式**：改为运行时可选下载，避免 GPL-3.0 与主程序一起分发。
5. **上游 API 与凭据**：更新源、CurseForge 密钥、微软客户端 ID、联机大厅密钥都需要自有配置，不能继续依赖上游。

## 7. 风险

- **溯源风险**：仓库历史只有一个压缩提交，无法用提交记录证明代码来源，合规证据需要重新建立。
- **功能回退**：WPF 客户端有 95 个 XAML 页面，Avalonia 当前只有约 1,541 行 UI；直接删除会造成明显功能缺口。
- **资产风险**：本地化、图标、图片、字体可能包含 PCL 版权内容，不能默认视为可复用。
- **许可混用**：LegacyFix 的 GPL-3.0 与主程序分发方式需要单独设计。
- **密钥与隐私**：账户密钥、设备标识、日志脱敏逻辑需要在新内核中重做，不能沿用可疑实现。

## 8. 第一批可执行动作（建议按顺序）

1. **建立台账**：把 PCL 专有逻辑、同源文件、第三方资产记录成清单，作为后续删除与验收依据。
2. **冻结 WPF**：CI 默认只构建 `PCL.CrossPlatform.slnx`；WPF 构建改为手动临时任务。
3. **清理不可保留的标识**：在新代码与新 UI 中移除 `PCL2` User-Agent、`McLaunchPrecheck`、`LTCat` 赞助链接和上游更新源。
4. **建立新内核骨架**：创建 `PCL.Kernel`、`PCL.Kernel.Abstractions`、`PCL.Tests`，先跑通原版安装与启动纵向切片。
5. **替换平台依赖**：按第 2.3 节优先级，先处理密钥存储、通知、硬件信息、注册表 Java 探测、音频与 Markdown。
6. **设计新 UI**：确定新信息架构、字符串目录和视觉规范，再开始页面实现。
7. **最后删除旧代码**：当新内核覆盖对应功能后，逐模块删除，避免一次性破坏可运行性。

## 9. 验收标准

- 新内核与 UI 中不存在 PCL 专有标识或同源代码。
- 构建图中不再出现 `net10.0-windows`、`UseWPF`、`Markdig.Wpf`、`Microsoft.Xaml.Behaviors.Wpf`、`NAudio`、`System.Management`、`ProtectedData`、WinRT Toast。
- Windows、Linux、macOS 三端均可构建、测试并发布。
- 依赖许可与资产来源全部登记，`LICENSE`、`NOTICE`、第三方声明与 SBOM 一致。
- 删除旧项目后，README、docs、CI 与发布产物不再引用 WPF 客户端。

## 10. 核查方法

以下命令用于后续复核评估数据：

```bash
# PCL 专有标识
rg -l 'PCL2|McLaunchPrecheck|LTCat|PCL-Community|pclc\.cc|afdian' -g '*.cs'

# WPF / Windows 专属目标
rg -l 'UseWPF|net10\.0-windows|Markdig\.Wpf|Xaml\.Behaviors|NAudio' -g '*.csproj'

# Windows 专属 API
rg -l 'System\.Management|ProtectedData|Windows\.UI\.Notifications|Microsoft\.Win32|Registry' -g '*.cs'

# 旧项目规模
find 'Plain Craft Launcher 2' PCL.Core PCL.Core.Wpf PCL.Launcher.Core -name '*.cs' | xargs wc -l | tail -1
```
