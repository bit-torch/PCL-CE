# PCL 迁移台账（资产 · 专有逻辑 · 第三方依赖）

- 状态日期：2026-11-22
- 关联评估：[pcl-kernel-removal-assessment.md](../pcl-kernel-removal-assessment.md)
- 适用范围：本仓库全部旧 PCL 项目、`PCL.Avalonia`、以及新方案 `PCL.Kernel` / `PCL.Desktop`。

## 1. 使用规则

1. 每一处 PCL 同源代码与资产都必须登记；替换后把状态改为「已移除」，并注明替代项与提交。
2. 新方案不得引用旧项目、旧命名空间或旧资产；新增引用需先在本台账登记。
3. 只要还有「PCL 同源」条目，PCL 分发有限许可义务就继续有效：
   - 名称以 PCL 开头
   - 关于页署名龙腾猫跃
   - 开源
   - 正版购买与赞助劝导
   - Windows 蓝色主色相
4. 第三方组件需登记名称、版本、用途、许可证；许可证未知的标为「待核」，不得默认视为可分发。

状态图例：`PCL 同源` / `第三方` / `新实现` / `已移除`

## 2. 冻结记录

旧 WPF 与旧跨平台客户端已停止默认构建，仅保留手动触发：

| 工作流 | 变更 | 说明 |
| --- | --- | --- |
| `.github/workflows/build-test.yml` | 改为 `workflow_dispatch` | 旧 WPF 客户端仅手动回归 |
| `.github/workflows/test.yml` | 改为 `workflow_dispatch` | 旧测试套件仅手动运行 |
| `.github/workflows/cross-platform.yml` | 移除 push / PR 触发 | 旧 `PCL.Avalonia` 仅由发布调用或手动触发 |
| `.github/workflows/rebuild-ci.yml` | 新增 | 默认构建与测试 `PCL.Rebuild.slnx` |
| `.github/workflows/release-publish.yml` | 仅加冻结说明 | 旧发布路径暂留，直至新桌面包接入 |

旧项目文件未删除，旧解决方案继续保留，便于逐项迁移与对照。

## 3. 资产台账

| 路径 | 类型 | 规模 | 归属 | 状态 | 处理计划 |
| --- | --- | --- | --- | --- | --- |
| `Plain Craft Launcher 2/Images/**` | 图片 | 47 个（方块 16、头像 11、皮肤 9、图标 8、背景 1、根图标 2） | PCL 资产，部分含作者头像 | PCL 同源 | 新 UI 不复用；后续替换为自建图形或合法授权素材 |
| `Plain Craft Launcher 2/Images/icon.ico`、`icon.png` | 应用图标 | 2 | PCL | PCL 同源 | 新桌面端需独立图标 |
| `Plain Craft Launcher 2/Resources/Font.ttf` | 字体 | 1 | 待核 | 待核 | 确认许可证后再决定替换或保留 |
| `Plain Craft Launcher 2/Resources/forge-installer.jar` | 第三方二进制 | 1 | Forge 项目 | 第三方 | 新方案改为直接下载官方安装器，不随包分发 |
| `Plain Craft Launcher 2/Resources/legacyfix.jar` | 第三方二进制 | 1 | 评估记录为 GPL-3.0 | 第三方 | 不进入新方案；如需使用须单独合规评审 |
| `Plain Craft Launcher 2/Resources/java-wrapper.jar` | 包装器 | 1 | 待核 | 待核 | 新方案直接使用 Java 进程，不依赖包装器 |
| `Plain Craft Launcher 2/Resources/lwjgl-unsafe-agent.jar` | Java Agent | 1 | 待核 | 待核 | 迁移期不引入 |
| `Plain Craft Launcher 2/Resources/transformer.jar` | Java Agent | 1 | 待核 | 待核 | 迁移期不引入 |
| `Plain Craft Launcher 2/Resources/linkd.exe` | 联机组件 | 1 | EasyTier 相关 | 第三方 | 新方案联机模块尚未实现，先登记 |
| `Plain Craft Launcher 2/Resources/hints/*.txt` | 文案 | 2 | PCL | PCL 同源 | 替换为新文案 |
| `Plain Craft Launcher 2/Resources/log4j2-*.xml` | 日志配置 | 2 | PCL | PCL 同源 | 新方案按需自建 |
| `Plain Craft Launcher 2/Resources/Markdown.xaml` | 样式 | 1 | PCL | PCL 同源 | 新 UI 不复用 |
| `Plain Craft Launcher 2/Resources/mcmod.buf` | 数据 | 1 | 待核 | 待核 | 迁移期不引入 |
| `Plain Craft Launcher 2/Resources/oauth-complete.html` | 登录页模板 | 1 | PCL | PCL 同源 | 微软登录实现时重写 |
| `PCL.Core.Wpf/App/Localization/Languages/*.xaml` | 本地化 | 7 个语言（zh-CN、zh-TW、en-US、en-GB、ja-JP、fr-FR、es-ES） | PCL | PCL 同源 | 新 UI 目前为内嵌中文，后续建立独立字符串目录 |
| `PCL.Core.Wpf/UI/Assets/IconPacks/lucide/**` | 图标 | 91 个文件（SVG） | Lucide，ISC | 第三方 | 可继续使用，需保留许可证 |
| `PCL.Core.Wpf/UI/Assets/Shaders/*.ps` | 着色器 | 包含在上述 91 个内 | 待核 | 待核 | 新 UI 不复用 |
| `Plain Craft Launcher 2/metadata.json` | 构建元数据 | 1 | PCL | PCL 同源 | 新方案使用自身版本信息 |

新方案资产现状：

| 路径 | 说明 | 状态 |
| --- | --- | --- |
| `PCL.Desktop/Styles/Theme.axaml` | 自建颜色、卡片、按钮、导航样式 | 新实现 |
| `PCL.Desktop/ViewModels/NavItem.cs` | 自绘 `StreamGeometry` 图标，未使用 PCL 图片 | 新实现 |
| `Avalonia.Fonts.Inter` | Inter 字体，OFL-1.1 | 第三方 |

## 4. 专有逻辑台账

| 位置 | 标识 | 说明 | 新方案对应 | 状态 |
| --- | --- | --- | --- | --- |
| `PCL.Launcher.Core/Launch/Core.cs` | `McLaunchPrecheck` | 启动前路径、实例、账户校验 | `LaunchViewModel` 的本地校验 | PCL 同源 |
| `PCL.Launcher.Core/Launch/Core.cs` | `_ShowPurchaseHint` | 启动前正版购买劝导 | `LicensePrecheckWindow`（过渡期保留） | PCL 同源 |
| `PCL.Launcher.Core/Launch/Core.cs` | `_ShowDonateHint` | 赞助劝导 | 关于页赞助入口（过渡期保留） | PCL 同源 |
| `PCL.Launcher.Core/Network/Http/RequestSigning.cs` | 请求签名 | PCL 服务端校验逻辑 | 新方案不实现，使用公开 API | PCL 同源 |
| `PCL.Core/IO/Net/Http/HttpSenderExtension.cs` | `PCL-Community/PCL2-CE` UA | 旧网络标识 | `DownloadClient` 使用 `PCL-CE/1.0 (clean-room launcher)` | 已由新实现替代，旧代码待删 |
| `PCL.Core/App/Localization/**` | `Lang` 体系 | 旧本地化加载与格式化 | 新 UI 直接中文字符串 | PCL 同源 |
| `PCL.Core/App/Configuration/Storage/CatIniFileProvider.cs` | LTCat-style INI | 旧配置格式 | `LauncherSettings` + JSON | PCL 同源 |
| `PCL.Launcher.Core/Updates/**` | `UpdateManager`、镜像与渠道 | 旧更新器与更新源 | 尚未实现 | PCL 同源 |
| `PCL.Launcher.Core/CrossPlatform/DesktopUpdater.cs` | 桌面更新 | 旧桌面包更新 | 尚未实现 | PCL 同源 |
| `PCL.Launcher.Core/Event/CustomEvent.cs` | `docs.pclc.cc` 等链接 | 旧社区入口 | 关于页社区入口 | PCL 同源 |
| `PCL.Core/App/Configuration/**` | 配置中心 | 旧配置与密钥存储 | 部分由 `SettingsStore` 替代 | PCL 同源 |

新方案已替代的模块（旧代码仍存在，后续统一删除）：

- 版本清单、规则求值、下载校验：`PCL.Kernel/Mojang`、`PCL.Kernel/Net`
- 原版安装：`PCL.Kernel/Install/VanillaInstaller.cs`
- Java 运行时：`PCL.Kernel/Java/**`
- Fabric / Quilt：`PCL.Kernel/Loaders/**`
- 启动参数、离线 UUID、进程管理：`PCL.Kernel/Launch/**`
- 版本校验、删除、隔离：`PCL.Kernel/Versions/VersionManager.cs`
- 设置与离线账户：`PCL.Kernel/App/LauncherSettings.cs`

## 5. 第三方依赖台账

### 5.1 新方案（`PCL.Rebuild.slnx`）

| 组件 | 版本 | 用途 | 许可证 |
| --- | --- | --- | --- |
| Avalonia / Avalonia.Desktop / Themes.Fluent | 11.3.23 | 桌面 UI | MIT |
| Avalonia.Fonts.Inter | 11.3.23 | Inter 字体 | OFL-1.1 |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM 源生成 | MIT |
| xUnit / Microsoft.NET.Test.Sdk | 项目内置 | 单元测试 | Apache-2.0 / MIT |

### 5.2 旧方案（仅登记，不再新增）

| 组件 | 版本 | 类别 | 许可证 |
| --- | --- | --- | --- |
| Markdig.Wpf | 0.5.0.1 | Markdown 渲染 | MIT |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.142 | WPF 行为 | MIT |
| NAudio | 2.3.0 | 音频 | MIT |
| LibVLCSharp.Avalonia | 3.10.2 | 视频播放 | LGPL-2.1 |
| System.Management | 10.0.9 | Windows 硬件信息 | MIT |
| System.Security.Cryptography.ProtectedData | 10.0.9 | DPAPI | MIT |
| PlainToolkit.CngProtectedData | 2.1.0 | CNG 加密 | 待核 |
| Dapper | 2.1.79 | 数据访问 | Apache-2.0 |
| LiteDB | 5.0.21 | 本地数据库 | MIT |
| Microsoft.Data.Sqlite / SQLitePCLRaw | 10.0.9 / 3.0.3 | SQLite | MIT / 待核 |
| FluentValidation | 12.1.1 | 校验 | Apache-2.0 |
| Polly | 8.7.0 | 重试 | BSD-3-Clause |
| SharpZipLib | 1.4.2 | 压缩 | MIT |
| YamlDotNet | 18.1.0 | YAML | MIT |
| protobuf-net | 3.2.56 | 序列化 | Apache-2.0 |
| fNbt | 1.0.0 | NBT | MIT |
| DotNet.Glob | 3.1.3 | 通配匹配 | MIT |
| Downloader | 5.9.4 | 下载 | MIT |
| Humanizer.Core.zh-CN | 3.0.10 | 文本 | MIT |
| Wacton.Unicolour | 8.0.0 | 颜色计算 | MIT |
| Ae.Dns.Client | 3.1.0 | DNS | 待核 |
| System.IdentityModel.Tokens.Jwt | 8.19.1 | JWT | MIT |
| Microsoft.Extensions.* | 10.0.9 | 日志 / HTTP | MIT |
| MSTest | 4.3.0 | 旧测试 | MIT |
| Lucide Icons | 仓库内置 | 图标 | ISC |

## 6. 待办与验收

- [ ] 旧项目删除前，逐项确认上表「PCL 同源」已有新方案替代。
- [ ] 建立新 UI 的独立字符串目录与图标资源，替换 `Resources/Font.ttf`、`Images/**`。
- [ ] 为 `legacyfix.jar`、`linkd.exe`、`java-wrapper.jar`、`transformer.jar` 补齐来源与许可证。
- [ ] 完成 Forge / NeoForge、资源下载、账户、联机、更新器的干净实现。
- [ ] 全仓库扫描 `PCL2`、`LTCat`、`pclc.cc`、`afdian`、`McLaunchPrecheck` 等标识，确认仅剩过渡期许可义务。
- [ ] 建立 SBOM（`dotnet list package --include-transitive`）。
- [ ] 新桌面包接入 CI 发布后，下线旧 WPF 发布路径。
