namespace PChabit.App.ViewModels;

/// <summary>
/// 数据库安全 ViewModel 基类。封装两阶段加载架构：
/// Phase 1 (Task.Run): DB 查询 + 纯数据计算
/// Phase 2 (UI 线程): ObservableCollection 更新 + WinRT 对象创建
/// 继承此类即可避免陷阱 5.5/10（跨线程 COMException）。
/// </summary>
public abstract class DbSafeViewModel<TStats> : ViewModelBase where TStats : class?
{
    /// <summary>后台线程加载纯数据，禁止操作 ObservableCollection/WinRT 对象</summary>
    protected abstract Task<TStats> LoadStatsOnBackgroundAsync();

    /// <summary>UI 线程应用数据到 ObservableCollection，允许创建 WinRT 对象</summary>
    protected abstract Task ApplyStatsOnUIAsync(TStats stats);

    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var stats = await Task.Run(LoadStatsOnBackgroundAsync);
            await RunOnUIThreadAsync(() => ApplyStatsOnUIAsync(stats));
        }
        finally
        {
            IsLoading = false;
        }
    }
}
