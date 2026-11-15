using System.IO.Pipes;

namespace PCL.CrossPlatform;

/// <summary>Per-user launcher activation over a current-user-only named pipe.</summary>
public sealed class SingleInstanceHost : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    public bool OwnsInstance { get; }
    public event Action? ActivateRequested;

    public SingleInstanceHost(string name)
    {
        _name = name; _mutex = new Mutex(false, name);
        try { OwnsInstance = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { OwnsInstance = true; }
    }
    public void Listen()
    {
        if (!OwnsInstance) throw new InvalidOperationException("Only the primary instance can listen.");
        _listener ??= Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    var buffer = new byte[1];
                    if (await pipe.ReadAsync(buffer, _stop.Token) == 1 && buffer[0] == 1) ActivateRequested?.Invoke();
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (IOException) when (!_stop.IsCancellationRequested) { }
            }
        });
    }
    public async Task ActivatePrimaryAsync(CancellationToken token = default)
    {
        await using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000, token); await pipe.WriteAsync(new byte[] { 1 }, token); await pipe.FlushAsync(token);
    }
    public void Dispose()
    {
        _stop.Cancel();
        if (_listener is not null)
        {
            try { _listener.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
        if (OwnsInstance) _mutex.ReleaseMutex();
        _mutex.Dispose(); _stop.Dispose();
    }
}
