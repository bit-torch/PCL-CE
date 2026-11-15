using System;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Utils;

namespace PCL.Core.Link.Scaffolding.EasyTier;

public class CliNetTest
{
    public enum NatType
    {
        Unknown,
        OpenInternet,
        NoPat,
        FullCone,
        Restricted,
        PortRestricted,
        SymmetricEasy,
        Symmetric,
        SymmetricFirewall,
        UdpBlocked
    }
    public record NetStatus
    {
        public required NatType UdpNatType;
        public required NatType TcpNatType;
        public required bool SupportIPv6;
    }

    public async static Task<NetStatus?> GetNetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var cliProcess = new Process();
        cliProcess.StartInfo = new ProcessStartInfo
        {
            FileName = EasyTierMetadata.ExecutablePath("easytier-cli"),
            WorkingDirectory = EasyTierMetadata.EasyTierFilePath,
            Arguments = $"-o json stun",
            ErrorDialog = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };
        cliProcess.EnableRaisingEvents = true;
        cliProcess.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!cliProcess.HasExited) cliProcess.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        // Drain stderr concurrently; a full redirected buffer must not deadlock STUN.
        var stderr = cliProcess.StandardError.ReadToEndAsync(timeout.Token);

        StunInfo? stunInfo = null;
        try
        {
            stunInfo = await JsonSerializer.DeserializeAsync<StunInfo>(cliProcess.StandardOutput.BaseStream, JsonCompat.SerializerOptions, timeout.Token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Link", "Failed to do net test");
        }
        await cliProcess.WaitForExitAsync(timeout.Token);
        await stderr;
        cancellationToken.ThrowIfCancellationRequested();
        if (stunInfo is null || cliProcess.ExitCode != 0) return null;

        var supportIPv6 = false;
        foreach (var ip in stunInfo.Ips)
        {
            if (ip.Contains(":"))
            {
                supportIPv6 = true;
                break;
            }
        }

        return new NetStatus { UdpNatType = GetNatTypeViaCode(stunInfo.UdpNatType), TcpNatType = GetNatTypeViaCode(stunInfo.TcpNatType), SupportIPv6 = supportIPv6 };
    }

    public static NatType GetNatTypeViaCode(int type) => type switch
    {
        0 => NatType.OpenInternet,
        1 => NatType.NoPat,
        2 => NatType.FullCone,
        3 => NatType.Restricted,
        4 => NatType.PortRestricted,
        5 => NatType.SymmetricEasy,
        6 => NatType.Symmetric,
        7 => NatType.SymmetricFirewall,
        8 => NatType.UdpBlocked,
        _ => NatType.Unknown
    };

    public static string GetNatTypeString(NatType type)
    {
        return Lang.Text(type switch
        {
            NatType.OpenInternet or NatType.NoPat => "Link.Nat.Type.Open",
            NatType.FullCone => "Link.Nat.Type.FullCone",
            NatType.PortRestricted => "Link.Nat.Type.PortRestricted",
            NatType.Restricted => "Link.Nat.Type.Restricted",
            NatType.SymmetricEasy => "Link.Nat.Type.SymmetricEasy",
            NatType.Symmetric => "Link.Nat.Type.Symmetric",
            NatType.SymmetricFirewall => "Link.Nat.Type.SymmetricFirewall",
            NatType.UdpBlocked => "Link.Nat.Type.UdpBlocked",
            _ => "Link.Nat.Type.Unknown"
        });
    }
}