using System.Diagnostics;

namespace PCL.Kernel.Launch;

public sealed record LaunchOptions
{
    public required string VersionId { get; init; }

    public required string JavaPath { get; init; }

    public string Username { get; init; } = "Player";

    public int MaxMemoryMb { get; init; } = 2048;

    public string? GameDirectory { get; init; }

    public IReadOnlyList<string> ExtraJvmArgs { get; init; } = [];

    public IReadOnlyList<string> ExtraGameArgs { get; init; } = [];
}

public sealed class GameSession : IDisposable
{
    public GameSession(Process process)
    {
        Process = process;
        Process.EnableRaisingEvents = true;
        Process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                OutputReceived?.Invoke(args.Data);
        };
        Process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                OutputReceived?.Invoke(args.Data);
        };
        Process.Exited += (_, _) => Exited?.Invoke(Process.ExitCode);
    }

    public Process Process { get; }

    public bool HasExited => Process.HasExited;

    public event Action<string>? OutputReceived;

    public event Action<int>? Exited;

    /// <summary>在订阅事件后调用，避免丢失进程启动初期的输出。</summary>
    public void StartReading()
    {
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    public void Dispose() => Process.Dispose();
}
