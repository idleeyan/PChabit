namespace PChabit.Core.Interfaces;

/// <summary>
/// 数据采集编排服务接口（工程改造 E2-b/c，2026-11-06）。
/// 用例编排已迁往 <c>PChabit.Application/UseCases/Collection</c>（原名
/// <c>PChabit.App.Services.DataCollectionService</c>，六分部整类迁移——
/// partial 不能跨程序集）。App 层（启动/关机/悬浮插件）只依赖本接口。
/// </summary>
public interface IDataCollectionService
{
    /// <summary>开始订阅监视器事件并启动串行落库消费者（幂等）。</summary>
    void Start();

    /// <summary>停止采集并提交残余数据。</summary>
    void Stop();

    /// <summary>当前前台应用进程名（锁内读取；供悬浮插件等消费）。</summary>
    string CurrentProcessName { get; }
}
