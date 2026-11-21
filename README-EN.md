[简体中文](README.md) | **English** | [繁體中文](README-ZH_TW.md)

> [!IMPORTANT]
> This repository (`bit-torch/PCL-CE`) is maintained by [bit-torch](https://github.com/bit-torch) and derived from the upstream repository [PCL-Community/PCL-CE](https://github.com/PCL-Community/PCL-CE). It no longer rebases upstream changes and now evolves independently.
> This repository provides unofficial distributions and support only.
> Because it diverges significantly from upstream and adds cross-platform support, testing and feedback are welcome.
> The upstream PCL CE project is a secondary development based on [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL).
> This project has left the original fork network.

<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Community Edition


<a href="https://trendshift.io/repositories/21800?utm_source=trendshift-badge&amp;utm_medium=badge&amp;utm_campaign=badge-trendshift-21800" target="_blank" rel="noopener noreferrer"><img src="https://trendshift.io/api/badge/trendshift/repositories/21800/daily?language=C%23" alt="PCL-Community%2FPCL-CE | Trendshift" width="250" height="55"/></a>
<a href="https://trendshift.io/repositories/21800" target="_blank"><img src="https://trendshift.io/api/badge/repositories/21800" alt="PCL-Community%2FPCL-CE | Trendshift" style="width: 250px; height: 55px;" width="250" height="55"/></a>

[download](https://github.com/PCL-Community/PCL-CE/releases/latest) |
[Upstream repo](https://github.com/Meloong-Git/PCL)

[Submit issues](https://github.com/PCL-Community/PCL-CE/issues/new/choose) |
[Contribution Guidelines](https://github.com/PCL-Community/PCL-CE/wiki/开发指南)

</div>

PCL CE is a community edition developed as a secondary project based on PCL’s open-source code. It includes features and improvements that have not yet been implemented in the main branch.

The version numbers of the community edition do not strictly correspond to those of the main branch, so please do not report community edition issues to the official repository.

Everyone is welcome to try it out!

**Developing group:** [599620549](https://qm.qq.com/q/PNQA4Md8wc) | **User group 1:** [1028074835](https://qm.qq.com/q/qrMGJo8BQQ) | **User group 2:** [1070029541](https://qm.qq.com/q/lBywbDMWhG)

## Cross-platform desktop preview

`PCL.Avalonia` is a native Windows/Linux/macOS preview with game/loader installation, resource and modpack management, worlds, servers, accounts, multiplayer tools, updates and media settings. It is not yet a full replacement for the WPF feature set. See [build instructions and the feature matrix](docs/cross-platform.md). Build with .NET 11 SDK using `PCL.CrossPlatform.slnx`, or run `dotnet run --project PCL.Avalonia/PCL.Avalonia.csproj`.

## 💻 Supported Platforms

| Operating System | Support Status | Requirements |
|---|---|---|
| Windows 10 1809 (17763) or later | ✅ Fully supported | [.NET 10 Desktop Runtime](http://get.dot.net/10) |
| Windows 8 to Windows 10 1809 (17763) | ⚠️ Expected to run; community support offered at discretion | [.NET 10 Desktop Runtime](http://get.dot.net/10) |
| Windows 7 or earlier | ❌ Not supported | N/A |
| macOS / Linux | 🧪 Avalonia desktop preview | .NET 10 Runtime, or self-contained package |
| Other OS | ❌ No desktop target | N/A |

**✅ Fully supported**: We will provide as much support as possible, but you must ensure you are using the latest version of the launcher.

**⚠️ Expected to run; community support offered at discretion**: PCL CE is expected to run on these platforms, but full functionality is not guaranteed. You may need to upgrade to a fully supported OS version to receive further community technical support.

**❌ Not supported**: PCL CE may not be usable on these platforms and might not even launch at all. Please upgrade your OS to use PCL CE.

**🧪 Desktop preview**: Use the Avalonia entry point on macOS and Linux. The existing WPF entry point still runs on Windows only. See the feature matrix above for the migration scope.

**Note**:     
The community only supports the latest version of the launcher.    
Depending on the nature of certain issues (e.g., an incomplete system environment), you may still need to upgrade your OS to continue receiving support.    
PCL CE always recommends using the latest version of your OS for the best experience.    
You can still try using the latest launcher version on unsupported platforms, but you may encounter many additional problems.

## 🔒 License

- `Plain Craft Launcher 2/` uses [Custom License](https://github.com/PCL-Community/PCL-CE/blob/dev/Plain%20Craft%20Launcher%202/LICENCE)
- `All other directories` use [Apache License 2.0](https://github.com/PCL-Community/PCL-CE/blob/dev/LICENSE)

## 🌟 Statistic
![Alt](https://repobeats.axiom.co/api/embed/3e46296e6e3a134991a783480fd2f62723bb0353.svg "Repobeats analytics image")

[![Star History Chart](https://api.star-history.com/svg?repos=PCL-Community/PCL-CE&type=Date)](https://www.star-history.com/#PCL-Community/PCL-CE&Date)

**Views** (Total / Today): [![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/PCL-Community/PCL-CE)](https://hits.zkitefly.eu.org/?tag=https://github.com/PCL-Community/PCL-CE&web=true)
## ❤️ Contributors

[![](https://contrib.rocks/image?repo=PCL-Community/PCL-CE)](https://github.com/PCL-Community/PCL-CE/graphs/contributors)
