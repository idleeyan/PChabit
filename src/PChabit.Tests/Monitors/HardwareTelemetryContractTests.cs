using Moq;
using PChabit.Core.Interfaces;
using Xunit;

namespace PChabit.Tests.Monitors;

/// <summary>
/// E2-a 修订版（3.72.0）接口下沉 Core 的契约测试。
/// 说明：适配器与两个持久化服务位于 App 层（平台 TFM），Tests（net10.0）
/// 无法引用 App/HardwareMonitor，故此处只测 Core 接口契约本身
/// （事件订阅语义、载荷形状、种子回填签名）；装配正确性由运行期验证覆盖
/// （启动日志 + HardwareSamples/NetworkTrafficSamples 新样本 + 冒烟）。
/// </summary>
public class HardwareTelemetryContractTests
{
    [Fact]
    public void IHardwareTelemetrySource_EventSubscribeUnsubscribe_Semantics()
    {
        var telemetry = new Mock<IHardwareTelemetrySource>();
        var fired = 0;
        void Handler() => fired++;

        // 订阅/退订无参 Action 事件不抛（接口契约：事件为 Action?，可多重订阅）
        telemetry.Object.ValuesUpdated += Handler;
        telemetry.Object.ValuesUpdated -= Handler;
        telemetry.Setup(t => t.Get("CPU.Load")).Returns(42.5f);

        Assert.Equal(0, fired); // 未显式触发
        Assert.Equal(42.5f, telemetry.Object.Get("CPU.Load"));
    }

    [Fact]
    public void NetworkProcessTotals_CarriesPureData()
    {
        var t = new NetworkProcessTotals
        {
            ProcessName = "a.exe",
            TodayUpBytes = 10,
            TodayDownBytes = 20,
            UpBytesPerSec = 1.5,
            DownBytesPerSec = 2.5
        };
        Assert.Equal("a.exe", t.ProcessName);
        Assert.Equal(10, t.TodayUpBytes);
        Assert.Equal(20, t.TodayDownBytes);
        Assert.Equal(1.5, t.UpBytesPerSec);
        Assert.Equal(2.5, t.DownBytesPerSec);
    }

    [Fact]
    public async Task IProcessNetworkSource_SeedSemantics_PassThrough()
    {
        var src = new Mock<IProcessNetworkSource>();
        src.Object.SeedTodayFrom(new[] { ("b.exe", 1L, 2L) });
        src.Object.SeedSystemToday(3L, 4L);

        src.Verify(m => m.SeedTodayFrom(It.IsAny<IEnumerable<(string, long, long)>>()), Times.Once);
        src.Verify(m => m.SeedSystemToday(3L, 4L), Times.Once);
        await Task.CompletedTask;
    }

    [Fact]
    public void IProcessNetworkSource_TotalsTuple_Shape()
    {
        var src = new Mock<IProcessNetworkSource>();
        src.Setup(m => m.GetSystemTotals()).Returns((1L, 2L, 3L, 4L));
        var (sessionUp, sessionDown, todayUp, todayDown) = src.Object.GetSystemTotals();
        Assert.Equal(1L, sessionUp);
        Assert.Equal(2L, sessionDown);
        Assert.Equal(3L, todayUp);
        Assert.Equal(4L, todayDown);
    }

    [Fact]
    public void IProcessNetworkSource_RateList_Shape()
    {
        var src = new Mock<IProcessNetworkSource>();
        src.Setup(m => m.GetTopRates(3)).Returns(new List<NetworkRateItem>
        {
            new() { UpBytesPerSec = 9, DownBytesPerSec = 8 }
        });
        var r = Assert.Single(src.Object.GetTopRates(3));
        Assert.Equal(9, r.UpBytesPerSec);
        Assert.Equal(8, r.DownBytesPerSec);
    }
}
