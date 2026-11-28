using System.Text.Json;
using PCL.Kernel.Accounts;

namespace PCL.Kernel.App;

public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = "";

    public string? JavaPath { get; set; }

    /// <summary>已记录的 Java 可执行文件路径；删除记录不会删除文件。</summary>
    public List<string> JavaPaths { get; set; } = [];

    public int MaxMemoryMb { get; set; } = 4096;

    public string OfflineUsername { get; set; } = "Player";

    public List<string> OfflineAccounts { get; set; } = [];

    /// <summary>离线、微软正版与第三方 Yggdrasil 账户。</summary>
    public List<LauncherAccount> Accounts { get; set; } = [];

    public string? SelectedAccountId { get; set; }

    /// <summary>Microsoft OAuth 客户端 ID；未填写时回退到环境变量 MS_CLIENT_ID。</summary>
    public string MicrosoftClientId { get; set; } = "";

    public bool IsolateVersions { get; set; }

    public bool DarkTheme { get; set; }

    /// <summary>界面强调色（#RRGGBB）；空字符串表示使用默认色。</summary>
    public string AccentColor { get; set; } = "";

    /// <summary>界面字体名称；空字符串表示使用默认字体链。</summary>
    public string UIFontFamily { get; set; } = "";

    /// <summary>界面语言；空字符串表示跟随系统。</summary>
    public string Language { get; set; } = "";

    /// <summary>启动时自动检查启动器更新。</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>下载源、线程、限速与代理。</summary>
    public DownloadSettings Download { get; set; } = new();

    /// <summary>追加到 JVM 的参数，按空白拆分并支持引号。</summary>
    public string JvmArguments { get; set; } = "";

    /// <summary>追加到游戏进程的参数。</summary>
    public string GameArguments { get; set; } = "";

    public int GameWindowWidth { get; set; } = 854;

    public int GameWindowHeight { get; set; } = 480;

    public bool GameFullScreen { get; set; }

    public bool LicensePrecheckAccepted { get; set; }

    public int LaunchCount { get; set; }

    /// <summary>补齐旧配置缺少的账户列表，并保证当前用户名为有效账户。</summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(OfflineUsername))
            OfflineUsername = "Player";

        OfflineAccounts ??= [];
        Accounts ??= [];
        JavaPaths ??= [];
        Download ??= new DownloadSettings();
        Download.Normalize();
        JvmArguments ??= "";
        GameArguments ??= "";
        AccentColor = (AccentColor ?? "").Trim();
        UIFontFamily = (UIFontFamily ?? "").Trim();
        GameWindowWidth = Math.Clamp(GameWindowWidth, 0, 16384);
        GameWindowHeight = Math.Clamp(GameWindowHeight, 0, 16384);

        if (!OfflineAccounts.Contains(OfflineUsername, StringComparer.OrdinalIgnoreCase))
            OfflineAccounts.Add(OfflineUsername);

        foreach (var name in OfflineAccounts.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var exists = Accounts.Any(account =>
                account.Kind == AccountKind.Offline &&
                string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                Accounts.Add(LauncherAccount.Offline(name));
        }

        if (Accounts.Count == 0)
            Accounts.Add(LauncherAccount.Offline(OfflineUsername));

        Accounts = Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Name))
            .GroupBy(account => (account.Kind, account.Id), TupleComparer.Instance)
            .Select(group => group.First())
            .ToList();

        if (string.IsNullOrWhiteSpace(SelectedAccountId) ||
            Accounts.All(account => account.Id != SelectedAccountId))
        {
            SelectedAccountId = Accounts
                .FirstOrDefault(account =>
                    account.Kind == AccountKind.Offline &&
                    string.Equals(account.Name, OfflineUsername, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? Accounts[0].Id;
        }

        if (string.IsNullOrWhiteSpace(MicrosoftClientId))
            MicrosoftClientId = Environment.GetEnvironmentVariable("MS_CLIENT_ID")?.Trim() ?? "";
    }

    public LauncherAccount? FindAccount(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Accounts.FirstOrDefault(account => account.Id == id);

    private sealed class TupleComparer : IEqualityComparer<(AccountKind Kind, string Id)>
    {
        public static readonly TupleComparer Instance = new();

        public bool Equals((AccountKind Kind, string Id) left, (AccountKind Kind, string Id) right) =>
            left.Kind == right.Kind && string.Equals(left.Id, right.Id, StringComparison.Ordinal);

        public int GetHashCode((AccountKind Kind, string Id) value) =>
            HashCode.Combine(value.Kind, value.Id);
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return CreateDefault();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(json, Options)
                           ?? CreateDefault();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return CreateDefault();
        }
    }

    private static LauncherSettings CreateDefault()
    {
        var settings = new LauncherSettings();
        settings.Normalize();
        return settings;
    }

    public static void Save(string path, LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, Options);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }
}
