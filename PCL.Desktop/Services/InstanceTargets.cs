using PCL.Desktop.Localization;
using PCL.Kernel.App;
using PCL.Kernel.Versions;

namespace PCL.Desktop.Services;

/// <summary>工具页与资源页的操作目标：全局游戏目录或某个启用隔离的实例。</summary>
public sealed record InstanceTarget(string Id, string Label, string GameDirectory)
{
    public override string ToString() => Label;
}

public static class InstanceTargets
{
    public static IReadOnlyList<InstanceTarget> Build(
        LauncherServices services,
        LocalizationService strings,
        string globalLabelKey = "common.target.global")
    {
        var targets = new List<InstanceTarget>
        {
            new("", strings[globalLabelKey], services.Paths.GameDirectory)
        };

        if (!services.Settings.IsolateVersions)
            return targets;

        foreach (var version in InstalledVersionScanner.Scan(services.Paths))
        {
            targets.Add(new InstanceTarget(
                version.Id,
                version.Id,
                services.VersionManager.ResolveGameDirectory(version.Id, true)));
        }

        return targets;
    }
}
