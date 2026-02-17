using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;

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
    
    protected async Task RunOnUIThreadAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource();
        
        RunOnUIThread(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        
        await tcs.Task;
    }
}
