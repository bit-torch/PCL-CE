using System.Text.Json;
using Xunit;

namespace PCL.Tests;

public class LocalizationTests
{
    [Fact]
    public void Chinese_and_english_catalogs_have_the_same_non_empty_keys()
    {
        var root = FindRepositoryRoot();
        var localization = Path.Combine(root, "PCL.Desktop", "Localization");

        var chinese = Read(Path.Combine(localization, "Strings.zh-CN.json"));
        var english = Read(Path.Combine(localization, "Strings.en-US.json"));

        Assert.NotEmpty(chinese);
        Assert.Equal(chinese.Keys.OrderBy(key => key), english.Keys.OrderBy(key => key));
        Assert.All(chinese.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
    }

    private static Dictionary<string, string> Read(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"无法解析字符串目录：{path}");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "PCL.Rebuild.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("未找到仓库根目录。");
    }
}
