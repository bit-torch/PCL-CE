using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PCL.Kernel.Mojang;

namespace PCL.Kernel.Launch;

public static partial class ArgumentParser
{
    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex PlaceholderRegex();

    public static IReadOnlyList<string> Parse(
        JsonElement? arguments,
        string section,
        IReadOnlyDictionary<string, string> variables,
        string? legacyArguments = null)
    {
        var result = new List<string>();

        if (arguments is { ValueKind: JsonValueKind.Object } document &&
            document.TryGetProperty(section, out var sectionElement) &&
            sectionElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in sectionElement.EnumerateArray())
                AppendEntry(entry, result, variables);

            return result;
        }

        if (section == "game" && !string.IsNullOrWhiteSpace(legacyArguments))
        {
            foreach (var token in SplitLegacyArguments(legacyArguments))
                result.Add(Substitute(token, variables));
        }

        return result;
    }

    public static string Substitute(string value, IReadOnlyDictionary<string, string> variables) =>
        PlaceholderRegex().Replace(value, match =>
            variables.TryGetValue(match.Groups[1].Value, out var replacement)
                ? replacement
                : "");

    public static IReadOnlyList<string> SplitLegacyArguments(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var character in arguments)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private static void AppendEntry(
        JsonElement entry,
        ICollection<string> result,
        IReadOnlyDictionary<string, string> variables)
    {
        if (entry.ValueKind == JsonValueKind.String)
        {
            result.Add(Substitute(entry.GetString() ?? "", variables));
            return;
        }

        if (entry.ValueKind != JsonValueKind.Object)
            return;

        if (entry.TryGetProperty("rules", out var rulesElement))
        {
            var rules = rulesElement.Deserialize<List<MojangRule>>(JsonDefaults.Options);
            if (!RuleEvaluator.IsAllowed(rules))
                return;
        }

        if (!entry.TryGetProperty("value", out var valueElement))
            return;

        if (valueElement.ValueKind == JsonValueKind.String)
        {
            result.Add(Substitute(valueElement.GetString() ?? "", variables));
            return;
        }

        if (valueElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (var value in valueElement.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
                result.Add(Substitute(value.GetString() ?? "", variables));
        }
    }
}
