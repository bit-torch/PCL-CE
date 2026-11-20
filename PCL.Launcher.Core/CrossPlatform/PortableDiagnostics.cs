namespace PCL.CrossPlatform;

public sealed record DiagnosticFinding(string Cause, string Confidence, IReadOnlyList<string> Evidence);

/// <summary>Public portable facade over the launcher's existing crash detector.</summary>
public static class PortableDiagnostics
{
    public static IReadOnlyList<DiagnosticFinding> Analyze(string gameLog, string? crashReport = null, string? hsErr = null)
    {
        var logs = new CrashLogSet
        {
            Game = string.IsNullOrWhiteSpace(gameLog) ? null : new CrashLogText { Kind = CrashLogKind.Game, Text = gameLog },
            CrashReport = string.IsNullOrWhiteSpace(crashReport) ? null : new CrashLogText { Kind = CrashLogKind.CrashReport, Text = crashReport },
            HsErr = string.IsNullOrWhiteSpace(hsErr) ? null : new CrashLogText { Kind = CrashLogKind.HsErr, Text = hsErr },
            All = string.Join('\n', gameLog, crashReport, hsErr)
        };
        return new CrashDetector().Analyze(logs, null).Findings
            .Select(finding => new DiagnosticFinding(finding.Cause.ToString(), finding.Confidence.ToString(), finding.Details)).ToArray();
    }
}
