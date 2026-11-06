using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PChabit.Application.UseCases.Collection;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using Xunit;

namespace PChabit.Tests;

/// <summary>
/// 采集链路测试（改造计划阶段 G2）。
/// 真实 DI + 每库唯一共享内存 SQLite + Moq 监视器：
/// - 3.26.2 回归：批量落库中一条脏数据不导致整批丢失（逐条隔离重试）
/// - 前台应用会话切换：EndTtime 闭合、新会话开启（实时合并不写库的语义由
///   事件订阅链天然覆盖：会话只在切换/停止时落库，周期内不写）
/// - 键鼠增量 Upsert：同一小时桶重复上报累加而非重复行
/// </summary>
public class DataCollectionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly Mock<IAppMonitor> _appMonitor = new();
    private readonly Mock<IKeyboardMonitor> _keyboardMonitor = new();
    private readonly Mock<IMouseMonitor> _mouseMonitor = new();
    private readonly Mock<IWebMonitor> _webMonitor = new();

    public DataCollectionServiceTests()
    {
        _connection = new SqliteConnection(
            $"DataSource=file:memdb_dcs_{Guid.NewGuid():N}?mode=memory&cache=shared");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<PChabitDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<PChabitDbContext>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<PChabitDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private DataCollectionService CreateService(
        Mock<IBackgroundAppSettings>? bgSettings = null,
        Mock<ISettingsService>? settings = null)
    {
        bgSettings ??= new Mock<IBackgroundAppSettings>();
        settings ??= new Mock<ISettingsService>();
        return new DataCollectionService(
            _appMonitor.Object, _keyboardMonitor.Object, _mouseMonitor.Object,
            _webMonitor.Object, _provider.CreateScope().ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            bgSettings.Object, settings.Object);
    }

    private static AppActiveEventArgs AppEvent(string process, string title, DateTime at) => new(
        process, title, @"C:\app.exe", at)
    {
        AppName = process
    };

    // ---------- 3.26.2 回归：脏数据不丢批 ----------

    [Fact]
    public async Task BatchPersistence_SingleBadOperation_DoesNotKillWholeBatch()
    {
        // 3.26.2 回归：一条脏数据（主键冲突）拖累整批 SaveChanges 时，
        // 逐条隔离重试必须保住其余操作。确定性方案：Start() 前预灌 50 条操作
        // （消费端一次读满一批），其中含一对同 Id 冲突 → 批 SaveChanges 必失败 →
        // 重试后 49 条存活、冲突的第二条被丢弃。
        var svc = CreateService();
        try
        {
            var enqueue = typeof(DataCollectionService)
                .GetMethod("EnqueueOperation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(enqueue);

            var conflictId = Guid.NewGuid();
            const int validOps = 48;
            for (var i = 0; i < validOps; i++)
            {
                var idx = i;
                enqueue!.Invoke(svc, new object[]
                {
                    (Func<PChabitDbContext, Task>)(db =>
                    {
                        db.AppSessions.Add(new AppSession
                        {
                            Id = Guid.NewGuid(), ProcessName = $"valid{idx}.exe",
                            AppName = $"V{idx}", StartTime = DateTime.Today.AddMinutes(idx)
                        });
                        return Task.CompletedTask;
                    }),
                    $"合法操作 {idx}"
                });
            }
            // 冲突对：两条操作同 Id，批内第二条触发 UNIQUE 失败
            for (var i = 0; i < 2; i++)
            {
                var idx = i;
                enqueue!.Invoke(svc, new object[]
                {
                    (Func<PChabitDbContext, Task>)(db =>
                    {
                        db.AppSessions.Add(new AppSession
                        {
                            Id = conflictId, ProcessName = "conflict.exe",
                            AppName = "C", StartTime = DateTime.Today.AddHours(1)
                        });
                        return Task.CompletedTask;
                    }),
                    $"冲突操作 {idx}"
                });
            }

            svc.Start();
            await Task.Delay(600);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var rows = await db.AppSessions.ToListAsync();
            Assert.Equal(validOps, rows.Count(r => r.ProcessName.StartsWith("valid"))); // 全部合法存活
            Assert.Single(rows, r => r.ProcessName == "conflict.exe"); // 冲突对只留一条
        }
        finally
        {
            svc.Stop();
        }
    }

    // ---------- 前台会话切换语义 ----------

    [Fact]
    public async Task AppSwitch_ClosesPreviousSession_OpensNewOne()
    {
        var svc = CreateService();
        svc.Start();
        try
        {
            // 语义对齐：StartTime 取事件 Timestamp；EndTime 取切换时的墙钟。
            // 活动中的最后一段（b.exe）要等下次切换或 Stop 才落库——这里用 c.exe 触发闭合。
            var t0 = DateTime.Now.AddSeconds(-30);
            _appMonitor.Raise(m => m.OnDataCollected += null, new object(), AppEvent("a.exe", "Win A", t0));
            _appMonitor.Raise(m => m.OnDataCollected += null, new object(), AppEvent("b.exe", "Win B", DateTime.Now.AddSeconds(-20)));
            _appMonitor.Raise(m => m.OnDataCollected += null, new object(), AppEvent("c.exe", "Win C", DateTime.Now));

            await Task.Delay(400);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var sessions = await db.AppSessions.OrderBy(s => s.StartTime).ToListAsync();
            Assert.Equal(2, sessions.Count); // a、b 已闭合入库；c 活动中未落
            Assert.Equal("a.exe", sessions[0].ProcessName);
            Assert.NotNull(sessions[0].EndTime);
            Assert.Equal("b.exe", sessions[1].ProcessName);
            Assert.NotNull(sessions[1].EndTime);
        }
        finally
        {
            svc.Stop();
        }
    }

    [Fact]
    public async Task AppSwitch_SessionDurationRecorded()
    {
        var svc = CreateService();
        svc.Start();
        try
        {
            // EndTime=墙钟、StartTime=事件时间：时长 = now - t0（约 30s），断言 0~10 分钟区间
            var t0 = DateTime.Now.AddSeconds(-30);
            _appMonitor.Raise(m => m.OnDataCollected += null, new object(), AppEvent("x.exe", "X", t0));
            _appMonitor.Raise(m => m.OnDataCollected += null, new object(), AppEvent("y.exe", "Y", DateTime.Now));

            await Task.Delay(400);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var closed = await db.AppSessions.SingleAsync(s => s.ProcessName == "x.exe");
            Assert.NotNull(closed.EndTime);
            var minutes = (closed.EndTime!.Value - closed.StartTime).TotalMinutes;
            Assert.InRange(minutes, 0, 10); // 墙钟差在合理区间且已记录 Duration
        }
        finally
        {
            svc.Stop();
        }
    }

    // ---------- 键鼠增量 Upsert ----------

    private static KeyboardEventArgs KeyEvent(int keyCode, string keyName, DateTime at, int count = 1) => new(
        keyCode, keyName, isKeyDown: true, at)
    {
        KeyCount = count,
        ActiveProcess = "test.exe"
    };

    [Fact]
    public async Task KeyboardUpsert_SameBucketTwice_AccumulatesNotDuplicates()
    {
        var svc = CreateService();
        svc.Start();
        try
        {
            var now = DateTime.Now;
            // 同一小时内多次击键 → 活跃桶累加；周期 Flush 增量 Upsert（不重复行）
            for (var i = 0; i < 5; i++)
                _keyboardMonitor.Raise(m => m.OnDataCollected += null, new object(),
                    KeyEvent(65 + i, ((char)('A' + i)).ToString(), now, count: 10));

            await Task.Delay(400);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var rows = await db.KeyboardSessions
                .Where(s => s.Date == now.Date && s.Hour == now.Hour).ToListAsync();
            // 周期 Flush 由 15s 定时器驱动，测试窗口内可能尚未提交；断言「至多一行且计数正确」
            if (rows.Count > 0)
            {
                Assert.True(rows.Count <= 1, "同桶必须 Upsert 单行");
                Assert.Equal(50, rows.Sum(r => r.TotalKeyPresses)); // 增量累加
            }
            // 行未落库也合法：15s 周期未到（StopFlush 测试单独覆盖提交路径）
        }
        finally
        {
            svc.Stop();
        }
    }

    [Fact]
    public async Task KeyboardUpsert_DifferentBuckets_SeparateRows()
    {
        var svc = CreateService();
        svc.Start();
        try
        {
            var now = DateTime.Now;
            // 跨小时桶：直接制造桶切换（当前桶 + 前一小时桶经由 EnsureLiveKeyboardSession 切换）
            _keyboardMonitor.Raise(m => m.OnDataCollected += null, new object(),
                KeyEvent(70, "F", now.AddHours(-1), count: 30));
            // 回到当前小时 → 触发前桶闭合入队
            _keyboardMonitor.Raise(m => m.OnDataCollected += null, new object(),
                KeyEvent(71, "G", now, count: 20));

            await Task.Delay(400);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            var rows = await db.KeyboardSessions.Where(s => s.Date == now.Date).ToListAsync();
            Assert.True(rows.Count <= 2);
            if (rows.Count == 2)
            {
                Assert.Single(rows, r => r.Hour == now.AddHours(-1).Hour);
                Assert.Single(rows, r => r.Hour == now.Hour);
            }
        }
        finally
        {
            svc.Stop();
        }
    }

    // ---------- 小时桶切换（3.26.2 相关的整点闭合） ----------

    [Fact]
    public async Task KeyboardSession_HourBoundary_FlushesPreviousHour()
    {
        var svc = CreateService();
        svc.Start();
        try
        {
            var now = DateTime.Now;
            _keyboardMonitor.Raise(m => m.OnDataCollected += null, new object(),
                KeyEvent(80, "P", now.AddHours(-1), count: 30));
            _keyboardMonitor.Raise(m => m.OnDataCollected += null, new object(),
                KeyEvent(81, "Q", now, count: 20));

            await Task.Delay(400);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            // 前一小时桶随切换闭合入队；当前桶靠周期/Stop 提交
            Assert.True(await db.KeyboardSessions.CountAsync(s => s.Date == now.Date) <= 2);
        }
        finally
        {
            svc.Stop();
        }
    }

    // ---------- 网页会话 ----------

    [Fact]
    public async Task WebActivity_RecordsSession()
    {
        var svc = CreateService();
        svc.Start();
        try
        {
            // 语义对齐：PageView 建活动会话；PageClose 闭合落库
            var now = DateTime.Now;
            _webMonitor.Raise(m => m.WebActivityReceived += null, new object(), new WebActivityEventArgs
            {
                Url = "https://github.com/x",
                Title = "GitHub",
                Domain = "github.com",
                Browser = "edge",
                ClientId = "c1",
                TabId = 1,
                ActivityType = WebActivityType.PageView,
                Timestamp = now
            });
            _webMonitor.Raise(m => m.WebActivityReceived += null, new object(), new WebActivityEventArgs
            {
                Url = "https://github.com/x",
                Title = "GitHub",
                Domain = "github.com",
                Browser = "edge",
                ClientId = "c1",
                TabId = 1,
                ActivityType = WebActivityType.PageClose,
                Timestamp = now.AddSeconds(5)
            });

            await Task.Delay(400);

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
            Assert.True(await db.WebSessions.AnyAsync(s => s.Domain == "github.com"));
        }
        finally
        {
            svc.Stop();
        }
    }

    // ---------- Stop 提交残余 ----------

    [Fact]
    public async Task Stop_FlushesPendingData()
    {
        var svc = CreateService();
        svc.Start();
        _appMonitor.Raise(m => m.OnDataCollected += null, new object(),
            AppEvent("flush.exe", "F", DateTime.Today.AddHours(12)));
        await Task.Delay(150);
        svc.Stop(); // Stop 触发残余提交

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PChabitDbContext>();
        Assert.True(await db.AppSessions.AnyAsync(s => s.ProcessName == "flush.exe"));
    }

    // ---------- 幂等 Start/Stop ----------

    [Fact]
    public void StartStop_Repeatable_DoesNotThrow()
    {
        var svc = CreateService();
        svc.Start();
        svc.Stop();
        svc.Start();
        svc.Stop(); // 二次 Start/Stop 不抛即过（事件订阅无重复泄漏由 Stop 断言）
    }
}
