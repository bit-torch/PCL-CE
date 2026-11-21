using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PCL.Kernel.Mojang;

/// <summary>
/// Mojang 的规则系统：无规则视为允许；有规则时默认拒绝，按顺序应用匹配规则。
/// </summary>
public static class RuleEvaluator
{
    public static bool IsAllowed(
        IReadOnlyList<MojangRule>? rules,
        IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0)
            return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!Matches(rule, features))
                continue;

            allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }

    private static bool Matches(MojangRule rule, IReadOnlyDictionary<string, bool>? features)
    {
        if (rule.Os is { } os && !MatchesOs(os))
            return false;

        if (rule.Features is { Count: > 0 })
        {
            foreach (var (key, expected) in rule.Features)
            {
                var actual = features?.GetValueOrDefault(key) ?? false;
                if (actual != expected)
                    return false;
            }
        }

        return true;
    }

    private static bool MatchesOs(RuleOs os)
    {
        if (!string.IsNullOrWhiteSpace(os.Name) &&
            !string.Equals(os.Name, PlatformInfo.OsName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(os.Arch))
        {
            var actual = RuntimeInformation.OSArchitecture;
            var matches = os.Arch.ToLowerInvariant() switch
            {
                "x86" => actual == Architecture.X86,
                "x64" or "x86_64" => actual == Architecture.X64,
                "arm64" or "aarch64" => actual == Architecture.Arm64,
                "arm" => actual == Architecture.Arm,
                _ => false
            };

            if (!matches)
                return false;
        }

        if (!string.IsNullOrWhiteSpace(os.Version))
        {
            try
            {
                if (!Regex.IsMatch(Environment.OSVersion.VersionString, os.Version))
                    return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return true;
    }
}
