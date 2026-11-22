using PCL.Kernel.Accounts;
using PCL.Kernel.App;
using Xunit;

namespace PCL.Tests;

public sealed class AccountTests
{
    [Fact]
    public void Normalize_CreatesOfflineAccountAndSelectsIt()
    {
        var settings = new LauncherSettings { OfflineUsername = "Steve" };

        settings.Normalize();

        var account = Assert.Single(settings.Accounts);
        Assert.Equal(AccountKind.Offline, account.Kind);
        Assert.Equal("Steve", account.Name);
        Assert.Equal(account.Id, settings.SelectedAccountId);
        Assert.Contains("Steve", settings.OfflineAccounts);
    }

    [Fact]
    public void Normalize_MigratesLegacyOfflineAccountList()
    {
        var settings = new LauncherSettings
        {
            OfflineUsername = "Alex",
            OfflineAccounts = ["Alex", "Steve", "Alex"]
        };

        settings.Normalize();

        Assert.Equal(2, settings.Accounts.Count);
        Assert.All(settings.Accounts, account => Assert.Equal(AccountKind.Offline, account.Kind));
        Assert.Equal(2, settings.OfflineAccounts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Normalize_PreservesOnlineAccountAndSelection()
    {
        var settings = new LauncherSettings
        {
            OfflineUsername = "Steve",
            Accounts =
            [
                new LauncherAccount
                {
                    Id = "069a79f444e94726a5befca90e38aaf5",
                    Name = "Notch",
                    Kind = AccountKind.Microsoft,
                    AccessToken = "token"
                }
            ],
            SelectedAccountId = "069a79f444e94726a5befca90e38aaf5"
        };

        settings.Normalize();

        Assert.Contains(settings.Accounts, account => account.Kind == AccountKind.Microsoft);
        Assert.Contains(settings.Accounts, account => account.Kind == AccountKind.Offline);
        Assert.Equal("069a79f444e94726a5befca90e38aaf5", settings.SelectedAccountId);
        Assert.Equal("Notch", settings.FindAccount(settings.SelectedAccountId)!.Name);
    }

    [Fact]
    public void Normalize_FallsBackToOfflineUsernameWhenSelectionMissing()
    {
        var settings = new LauncherSettings
        {
            OfflineUsername = "Alex",
            Accounts =
            [
                new LauncherAccount
                {
                    Id = "uuid",
                    Name = "Online",
                    Kind = AccountKind.Yggdrasil
                }
            ],
            SelectedAccountId = "missing"
        };

        settings.Normalize();

        var selected = settings.FindAccount(settings.SelectedAccountId);
        Assert.NotNull(selected);
        Assert.Equal("Alex", selected!.Name);
    }

    [Theory]
    [InlineData("https://littleskin.cn/api/yggdrasil", "https://littleskin.cn/api/yggdrasil")]
    [InlineData("https://littleskin.cn/api/yggdrasil/", "https://littleskin.cn/api/yggdrasil")]
    [InlineData("https://littleskin.cn/api/yggdrasil/authserver", "https://littleskin.cn/api/yggdrasil")]
    [InlineData("  https://example.com/authserver/  ", "https://example.com")]
    public void NormalizeApiRoot_StripsAuthserverAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, YggdrasilServer.NormalizeApiRoot(input));
    }

    [Fact]
    public void OfflineAccount_UsesOfflineUuid()
    {
        var account = LauncherAccount.Offline("Steve");

        Assert.Equal(AccountKind.Offline, account.Kind);
        Assert.Equal(32, account.Id.Length);
        Assert.Equal(LauncherAccount.Offline("Steve").Id, account.Id);
        Assert.Contains("Offline", account.Display);
    }
}
