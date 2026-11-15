using System.Net.Http;
using PCL.Core.Link.Scaffolding;
using PCL.Core.Link.Scaffolding.Client.Models;
using PCL.Core.Link.Scaffolding.Client.Requests;
using PCL.Core.Link.Scaffolding.EasyTier;

namespace PCL.CrossPlatform;

/// <summary>Reuses the launcher's lobby protocol with platform-native EasyTier executables.</summary>
public sealed class PortableLobby : IAsyncDisposable
{
    private ScaffoldingServerEntity? _server;
    private ScaffoldingClientEntity? _client;
    private volatile string[] _players = [];
    public IReadOnlyList<string> Players => _players;
    public bool IsRunning => _server is not null || _client is not null;
    public string? RoomCode => _server?.EasyTier.Lobby.FullCode;

    public async Task<string> HostAsync(int port, string playerName, CancellationToken token)
    {
        await DisposeAsync(); token.ThrowIfCancellationRequested();
        var server = await Task.Run(() => ScaffoldingFactory.CreateServer(port, playerName), token);
        server.Server.PlayerProfilePing += profiles => _players = profiles.Select(p => p.Name + " · " + p.Vendor).ToArray();
        server.Server.ServerStarted += profiles => _players = profiles.Select(p => p.Name + " · " + p.Vendor).ToArray();
        Exception? startFailure = null;
        server.Server.ServerException += error => startFailure = error ?? new IOException("大厅监听失败。");
        try
        {
            token.ThrowIfCancellationRequested(); server.Server.Start();
            if (startFailure is not null) throw new IOException("无法启动大厅监听。", startFailure);
            _server = server; return server.EasyTier.Lobby.FullCode;
        }
        catch { await server.Server.DisposeAsync(); await server.EasyTier.StopAsync(); throw; }
    }
    public async Task<string> JoinAsync(string code, string playerName, CancellationToken token)
    {
        await DisposeAsync();
        var client = await ScaffoldingFactory.CreateClientAsync(playerName, code, LobbyType.PCLCE, token);
        try
        {
            client.Client.Heartbeat += (profiles, _) => _players = profiles.Select(p => p.Name + " · " + p.Vendor).ToArray();
            await client.Client.ConnectAsync(token);
            var port = await client.Client.SendRequestAsync(new GetServerPortRequest(), token);
            var localPort = await client.EasyTier.AddPortForwardAsync(client.HostInfo.Ip, port);
            token.ThrowIfCancellationRequested(); _client = client;
            return "127.0.0.1:" + localPort;
        }
        catch { await client.Client.DisposeAsync(); await client.EasyTier.StopAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        _players = [];
        if (_server is { } server) { _server = null; await server.Server.DisposeAsync(); await server.EasyTier.StopAsync(); }
        if (_client is { } client) { _client = null; await client.Client.DisposeAsync(); await client.EasyTier.StopAsync(); }
    }

    public static async Task InstallDependencyAsync(HttpClient client, CancellationToken token)
    {
        var folder = EasyTierMetadata.EasyTierFilePath;
        if (Directory.Exists(folder)) throw new IOException("EasyTier 已存在；可在设置中选择其他可执行文件目录。");
        var parent = Path.GetDirectoryName(folder)!; Directory.CreateDirectory(parent);
        var archive = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".zip");
        var stage = folder + ".stage-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(stage);
        try
        {
            var name = $"easytier-{EasyTierMetadata.PlatformName}-{EasyTierMetadata.ArchitectureName}-v{EasyTierMetadata.CurrentEasyTierVer}.zip";
            await new GameInstaller(client).DownloadAsync($"https://github.com/EasyTier/EasyTier/releases/download/v{EasyTierMetadata.CurrentEasyTierVer}/{name}", archive, null, null, token);
            await InstanceFiles.ExtractAsync(archive, stage, token: token);
            var core = Directory.EnumerateFiles(stage, EasyTierMetadata.ExecutableName("easytier-core"), SearchOption.AllDirectories).Single();
            var source = Path.GetDirectoryName(core)!;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(core, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(Path.Combine(source, "easytier-cli"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Directory.Move(source, folder);
        }
        finally { if (File.Exists(archive)) File.Delete(archive); if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
