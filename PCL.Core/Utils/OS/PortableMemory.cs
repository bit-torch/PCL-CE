using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PCL.Core.Utils.OS;

/// <summary>Portable memory information; does not load kernel32 on Unix.</summary>
internal static class PortableMemory
{
    public static (ulong Total, ulong Available) GetPhysicalMemoryBytes()
    {
        if (OperatingSystem.IsLinux())
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => ulong.Parse(
                    parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0],
                    CultureInfo.InvariantCulture) * 1024);
            var total = values["MemTotal"];
            var available = values.TryGetValue("MemAvailable", out var value) ? value : values["MemFree"];
            return (total, Math.Min(total, available));
        }

        // The GC accounts for the host's memory limit on macOS and other runtimes.
        // This fallback is an estimate, rather than a Windows API failure at startup.
        var info = GC.GetGCMemoryInfo();
        var limit = (ulong)Math.Max(info.TotalAvailableMemoryBytes, 0);
        var used = (ulong)Math.Max(info.MemoryLoadBytes, 0);
        return (limit, limit - Math.Min(limit, used));
    }
}
