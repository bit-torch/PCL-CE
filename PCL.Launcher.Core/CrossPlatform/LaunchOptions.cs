namespace PCL.CrossPlatform;

public sealed record LaunchOptions
{
    public bool CaptureLogs { get; init; } = true;
    public string? GameDirectory { get; init; }
    public IReadOnlyList<string> JvmArguments { get; init; } = [];
    public IReadOnlyList<string> GameArguments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? ServerAddress { get; init; }
}
