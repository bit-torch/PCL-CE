using Avalonia.Threading;
using PCL.Core.Platform;

namespace PCL.Avalonia;

/// <summary>Shared core dispatcher adapter, including nested frames for existing synchronous dialogs.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public void Invoke(Action action)
    {
        if (CheckAccess()) action();
        else Dispatcher.UIThread.Invoke(action);
    }
    public T? Invoke<T>(Func<T> func) => CheckAccess() ? func() : Dispatcher.UIThread.Invoke(func);
    public async Task InvokeAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);
    public async Task<T> InvokeAsync<T>(Func<T> func) => await Dispatcher.UIThread.InvokeAsync(func);
    public IUiModalFrame CreateModalFrame() => new ModalFrame();

    private sealed class ModalFrame : IUiModalFrame
    {
        private readonly DispatcherFrame _frame = new();
        public void Exit() => Dispatcher.UIThread.Post(() => _frame.Continue = false);
        public void Run() => Dispatcher.UIThread.PushFrame(_frame);
    }
}
