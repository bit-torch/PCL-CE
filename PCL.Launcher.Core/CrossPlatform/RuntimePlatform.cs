using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.CrossPlatform;

/// <summary>Mojang platform rules, shared by installation and launch.</summary>
public sealed record RuntimePlatform(string Os, string Architecture, string Version, char ClasspathSeparator)
{
    public static RuntimePlatform Current { get; } = new(
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "osx" : "linux",
        RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "amd64",
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => throw new PlatformNotSupportedException("Only x86, x64 and ARM64 desktops are supported.")
        }, Environment.OSVersion.Version.ToString(), Path.PathSeparator);

    public bool Allows(JsonArray? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null) return true;
        var allowed = false;
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (rule["os"] is JsonObject os)
            {
                if (os["name"] is { } name && name.ToString() != Os) continue;
                if (os["arch"] is { } arch && !Matches(Architecture, arch.ToString())) continue;
                if (os["version"] is { } version && !Matches(Version, version.ToString())) continue;
            }
            if (rule["features"] is JsonObject required && required.Any(pair =>
                    (features?.GetValueOrDefault(pair.Key) ?? false) != pair.Value!.GetValue<bool>())) continue;
            allowed = rule["action"]?.ToString() == "allow";
        }
        return allowed;
    }

    private static bool Matches(string value, string pattern) =>
        Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public string? NativeClassifier(JsonObject library)
    {
        var template = library["natives"]?[Os]?.ToString();
        if (template is null) return null;
        var classifier = template.Replace("${arch}", Architecture == "x86" ? "32" : "64");
        if (Architecture == "aarch64" && library["downloads"]?["classifiers"] is JsonObject classifiers)
        {
            foreach (var candidate in new[] { classifier + "-arm64", classifier + "-aarch64" })
                if (classifiers.ContainsKey(candidate)) return candidate;
            if (!classifier.Contains("arm64") && !classifier.Contains("aarch64"))
                throw new PlatformNotSupportedException($"{library["name"]} does not provide {Os} ARM64 natives.");
        }
        return classifier;
    }
}
