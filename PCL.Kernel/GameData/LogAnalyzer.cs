namespace PCL.Kernel.GameData;

/// <summary>
/// 扫描最新游戏日志与崩溃报告，匹配常见错误关键字并给出排查建议。
/// 只做本地文本匹配，不上传任何日志。
/// </summary>
public static class LogAnalyzer
{
    private sealed record Rule(string Pattern, string Title, string Hint);

    private static readonly Rule[] Rules =
    [
        new("OutOfMemoryError", "内存不足", "提高最大内存或减少同时加载的模组。"),
        new("UnsupportedClassVersionError", "Java 版本不匹配", "游戏使用的 Java 版本过低，请在设置中更换 Java 运行时。"),
        new("Unsupported class file major version", "Java 版本不匹配", "模组与当前 Java 版本不兼容，请调整 Java 版本。"),
        new("Missing or unsupported mandatory dependencies", "缺少必选依赖", "整合包或模组缺少必选依赖，请补全对应模组。"),
        new("ModResolutionException", "模组依赖冲突", "多个模组之间存在依赖冲突或版本不匹配。"),
        new("Duplicate mods", "重复的模组", "mods 目录中存在同一模组的多个版本，请删除多余文件。"),
        new("NoClassDefFoundError", "缺少类文件", "通常由模组或加载器版本不兼容导致，请检查加载器版本。"),
        new("ClassNotFoundException", "缺少类文件", "模组引用了不存在的类，请检查依赖与加载器版本。"),
        new("Mixin apply failed", "Mixin 注入失败", "模组与当前游戏或加载器版本不兼容。"),
        new("Failed to create display", "无法创建显示窗口", "缺少图形环境或显卡驱动异常。"),
        new("java.lang.IllegalStateException: GLFW", "图形初始化失败", "显卡驱动或 OpenGL 环境异常。")
    ];

    public static LogAnalysisResult Analyze(string gameDirectory)
    {
        var sources = new List<(string Name, string Text)>();

        var latestLog = Path.Combine(gameDirectory, "logs", "latest.log");
        AddSource(sources, latestLog);

        var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
        if (Directory.Exists(crashDirectory))
        {
            var reports = Directory.EnumerateFiles(crashDirectory, "*.txt")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(3);

            foreach (var report in reports)
                AddSource(sources, report.FullName);
        }

        var issues = new List<LogIssue>();
        foreach (var (name, text) in sources)
        {
            foreach (var rule in Rules)
            {
                if (!text.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                var line = text
                    .Split('\n')
                    .FirstOrDefault(candidate =>
                        candidate.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                    ?.Trim() ?? rule.Pattern;

                if (line.Length > 240)
                    line = line[..240] + "…";

                issues.Add(new LogIssue(name, rule.Title, rule.Hint, line));
            }
        }

        return new LogAnalysisResult(issues, sources.Count);
    }

    private static void AddSource(List<(string Name, string Text)> sources, string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            sources.Add((Path.GetFileName(path), File.ReadAllText(path)));
        }
        catch (IOException)
        {
            // 日志可能正被游戏占用，跳过即可。
        }
    }
}
