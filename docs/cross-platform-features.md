# 原版功能迁移对照

本表按原 WPF 页面分组，对应的是可操作的功能入口，而非窗口视觉效果的逐像素复刻。
跨平台代码在 `PCL.Avalonia/MainWindow.*.cs` 与 `PCL.Launcher.Core/CrossPlatform`；
底层继续复用原认证、NBT、存档、Mod 元数据、崩溃规则与 Scaffolding 协议。

| 原页面 / 模块 | 新入口 | 实现与验证边界 |
|---|---|---|
| PageLaunch、PageSelect | 启动、实例设置 | 原版/继承版本启动、Java、独立数据与参数；快照列表仍在 Minecraft 下载页 |
| PageDownloadClient | 安装与资源 / Minecraft | Mojang 清单、资源与库、校验修复 |
| PageDownloadFabric / LegacyFabric | 安装与资源 / 加载器 | Meta 清单与版本配置安装 |
| PageDownloadForge / NeoForge / Cleanroom | 加载器 | 现有 Forge Java bridge 与安装器；历史版本需继续做真机兼容检查 |
| PageDownloadOptiFine / LiteLoader / LabyMod | 加载器 | 版本发现与安装；OptiFine 本地桥依赖 JDK 编译工具 |
| PageDownloadInstall | 加载器 + 实例资源 | 已接入独立加载器/资源流程；复杂加载器组合需按各自兼容条件使用 |
| PageDownloadCompFavorites | 资源搜索 | Modrinth/CurseForge、版本、依赖与收藏 |
| PageInstanceCompResource / ModDisabled / JarInJar | 实例管理 / Mod、资源包、光影与投影 | 文件导入、启停、备份、详情、内嵌 Mod、更新 |
| PageInstanceSetup / Overall / Install | 实例管理 / 设置 | 启动参数、独立目录、版本副本与备份移除 |
| PageInstanceExport | 安装与资源 / 整合包 | 安全导出，排除已知凭据与密钥 |
| Modpack | 整合包 | 各清单格式、overrides、依赖加载器、暂存与回滚 |
| PageInstanceSaves | 实例管理 / 存档 | 原 SaveManager；修改前完整备份、导入/导出、数据包管理 |
| PageInstanceServer | 实例管理 / 服务器 | servers.dat 保留未知字段、备份、探测与加入 |
| PageInstanceScreenshot | 实例管理 / 截图 | 预览、系统打开、移至备份 |
| PageLoginOffline / Ms / Auth / Profile | 账户 | 多账户、离线、微软、Authlib、Yggdrasil Connect |
| PageLoginProfileSkin / MySkin | 账户 / 皮肤与披风 | 微软账户的在线外观服务；第三方外观由其服务端提供 |
| PageSetupJava | 安装与资源 / Java、常规设置 | 检测、手动选择、校验下载与解压 |
| PageToolsGameLink / PageSetupGameLink | 工具 / 联机 | Scaffolding 大厅；平台原生 EasyTier |
| PageToolsTest | 服务器状态、崩溃分析 | 服务器探测、EasyTier NAT/IPv6 检测、崩溃诊断 |
| PageLog / PageSetupLog | 日志、工具 / 崩溃分析 | 输出、读取、规则分析与报告导出 |
| PageSpeed | 全局任务状态与取消 | 安装、下载任务提供进度；任务退出等待清理 |
| PageSetupLaunch / GameManage | 常规与实例设置 | 内存、Java、目录、窗口、参数、环境与副本；启动后保留/隐藏/最小化/退出、异常退出分析 |
| PageSetupDownloadSource | 设置 / 下载与网络 | 官方/兼容镜像、系统/无/自定义代理、API 密钥 |
| PageSetupUpdate | 工具 / 更新 | 发布检查、下载、校验、准备、退出替换和重启 |
| PageSetupUI / Music / VideoBackground | 设置 / 外观与媒体 | 主题色、字体、背景透明度、图片、LibVLC 音视频；系统播放器依赖不内置 |
| PageSetupLauncherLanguage | 设置 / 语言 | 原七语言字典；新增文本仍有回退 |
| PageHomepageNewsView | 主页 / Minecraft 新闻 | 原数据模型、可信新闻链接校验 |
| PageLaunchRight 自定义主页 | 主页 / 自定义主页 | 受限声明式 XAML，含 Grid 行列/跨格、WrapPanel 与滚动容器；不执行代码或 WPF 标记扩展 |
| PageHelpDetail / SetupAbout / Feedback | 工具 / 帮助与关于 | 文档、来源、许可证与反馈入口 |

## 自动化验证

测试不连接真实账号或远端游戏服务器。下载/加载器/整合包使用内存 HTTP fixtures；
文件操作使用独立临时目录；页面通过 Avalonia/Skia 无界面运行。
同时保留 Windows 原测试与 WPF 构建，三系统 CI 检查发布。

不会把“存在页面按钮”等同于完成外部服务验证。尚未实测的历史加载器、媒体库、
真实皮肤账户操作与异地联机，应在具体环境验收后再标注为真机验证通过。

启动器通过每用户互斥体与当前用户专用管道实现单实例唤醒。IPC 测试在禁止 Unix socket 的容器中明确跳过，由三系统 CI 补充验证。
