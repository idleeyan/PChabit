using FluentAssertions;
using Moq;
using Tai.Core.Interfaces;
using Tai.Infrastructure.Monitoring;
using Tai.Infrastructure.Services;
using Xunit;

namespace Tai.Tests.Monitors;

public class AppMonitorTests
{
    private readonly AppMonitor _appMonitor;
    
    public AppMonitorTests()
    {
        _appMonitor = new AppMonitor();
    }
    
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        _appMonitor.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public void Start_ShouldSetIsRunningToTrue()
    {
        _appMonitor.Start();
        
        _appMonitor.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public void Stop_ShouldSetIsRunningToFalse()
    {
        _appMonitor.Start();
        _appMonitor.Stop();
        
        _appMonitor.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public void Start_WhenAlreadyRunning_ShouldNotThrow()
    {
        _appMonitor.Start();
        
        var act = () => _appMonitor.Start();
        
        act.Should().NotThrow();
    }
    
    [Fact]
    public void Stop_WhenNotRunning_ShouldNotThrow()
    {
        var act = () => _appMonitor.Stop();
        
        act.Should().NotThrow();
    }
}

public class KeyboardMonitorTests
{
    private readonly KeyboardMonitor _keyboardMonitor;
    
    public KeyboardMonitorTests()
    {
        _keyboardMonitor = new KeyboardMonitor();
    }
    
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        _keyboardMonitor.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public void Start_ShouldSetIsRunningToTrue()
    {
        _keyboardMonitor.Start();
        
        _keyboardMonitor.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public void Stop_ShouldSetIsRunningToFalse()
    {
        _keyboardMonitor.Start();
        _keyboardMonitor.Stop();
        
        _keyboardMonitor.IsRunning.Should().BeFalse();
    }
}

public class MouseMonitorTests
{
    private readonly MouseMonitor _mouseMonitor;
    
    public MouseMonitorTests()
    {
        _mouseMonitor = new MouseMonitor();
    }
    
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        _mouseMonitor.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public void Start_ShouldSetIsRunningToTrue()
    {
        _mouseMonitor.Start();
        
        _mouseMonitor.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public void Stop_ShouldSetIsRunningToFalse()
    {
        _mouseMonitor.Start();
        _mouseMonitor.Stop();
        
        _mouseMonitor.IsRunning.Should().BeFalse();
    }
}

public class MonitorManagerTests
{
    private readonly Mock<IAppMonitor> _appMonitorMock;
    private readonly Mock<IKeyboardMonitor> _keyboardMonitorMock;
    private readonly Mock<IMouseMonitor> _mouseMonitorMock;
    private readonly Mock<IWebMonitor> _webMonitorMock;
    private readonly Mock<WebSocketServer> _webSocketServerMock;
    private readonly MonitorManager _monitorManager;
    
    public MonitorManagerTests()
    {
        _appMonitorMock = new Mock<IAppMonitor>();
        _keyboardMonitorMock = new Mock<IKeyboardMonitor>();
        _mouseMonitorMock = new Mock<IMouseMonitor>();
        _webMonitorMock = new Mock<IWebMonitor>();
        _webSocketServerMock = new Mock<WebSocketServer>();
        
        _monitorManager = new MonitorManager(
            _appMonitorMock.Object,
            _keyboardMonitorMock.Object,
            _mouseMonitorMock.Object,
            _webMonitorMock.Object,
            _webSocketServerMock.Object);
    }
    
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        _monitorManager.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public async Task StartAllAsync_ShouldStartAllMonitors()
    {
        await _monitorManager.StartAllAsync();
        
        _appMonitorMock.Verify(m => m.Start(), Times.Once);
        _keyboardMonitorMock.Verify(m => m.Start(), Times.Once);
        _mouseMonitorMock.Verify(m => m.Start(), Times.Once);
        _monitorManager.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public async Task StopAllAsync_ShouldStopAllMonitors()
    {
        await _monitorManager.StartAllAsync();
        await _monitorManager.StopAllAsync();
        
        _appMonitorMock.Verify(m => m.Stop(), Times.Once);
        _keyboardMonitorMock.Verify(m => m.Stop(), Times.Once);
        _mouseMonitorMock.Verify(m => m.Stop(), Times.Once);
        _monitorManager.IsRunning.Should().BeFalse();
    }
}
