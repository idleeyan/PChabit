using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Serilog;

namespace PChabit.App.ViewModels;

public partial class ViewModelBase : ObservableObject
{
    private readonly DispatcherQueue? _dispatcherQueue;
    
    protected ViewModelBase()
    {
        try
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }
        catch
        {
            _dispatcherQueue = null;
        }
    }
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _title = string.Empty;
    
    /// <summary>
    /// 重写 OnPropertyChanged，确保 PropertyChanged 事件始终在 UI 线程触发。
    /// 解决 Task.Run 内设置 [ObservableProperty] 导致 COMException (0x8001010E) 的问题。
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => base.OnPropertyChanged(e));
        }
        else
        {
            base.OnPropertyChanged(e);
        }
    }
    
    protected void RunOnUIThread(Action action)
    {
        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }
    
    /// <summary>
    /// 在 UI 线程异步执行 action 并等待其完成。
    /// 使用 DispatcherQueue.TryEnqueue + TaskCompletionSource，
    /// 避免在 WinUI 3 中直接 await UI 线程操作导致的死锁。
    /// </summary>
    protected async Task RunOnUIThreadAsync(Func<Task> action)
    {
        if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
        {
            // 已在 UI 线程或无 dispatcher，直接执行
            await action();
            return;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var queued = _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!queued)
        {
            // TryEnqueue 失败：fallback 到同步执行
            await action();
            return;
        }

        // ConfigureAwait(false) 避免捕获当前 SynchronizationContext，防止死锁
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 在后台线程执行数据加载，避免 WinUI 3 SynchronizationContext 导致 UI 线程同步阻塞。
    /// 关键：Task.Run 强制将 loadAction 丢到线程池，EF Core 查询不会回到 UI 线程等待。
    /// 调用方必须已在 UI 线程（事件处理 / OnNavigatedTo 等）。
    /// OnPropertyChanged 已自动调度到 UI 线程，loadAction 中可直接设置 [ObservableProperty]。
    /// </summary>
    public async Task LoadInBackgroundAsync(Func<Task> loadAction)
    {
        if (IsLoading) return;

        IsLoading = true;

        try
        {
            await Task.Run(async () =>
            {
                await loadAction();
            });
        }
        finally
        {
            IsLoading = false;
        }
    }
}
