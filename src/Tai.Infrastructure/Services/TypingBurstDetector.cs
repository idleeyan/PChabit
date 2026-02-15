namespace Tai.Infrastructure.Services;

public class TypingBurstDetector
{
    private readonly TimeSpan _burstThreshold = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _minBurstDuration = TimeSpan.FromMilliseconds(500);
    private readonly int _minKeysForBurst = 5;
    
    private DateTime _burstStartTime;
    private int _burstKeyCount;
    private string? _currentProcess;
    private bool _isInBurst;
    
    public event EventHandler<TypingBurstEventArgs>? OnBurstCompleted;
    
    public void OnKeyPress(string? processName, DateTime timestamp)
    {
        if (processName != _currentProcess)
        {
            CompleteBurst(timestamp);
            _currentProcess = processName;
            _burstStartTime = timestamp;
            _burstKeyCount = 0;
            _isInBurst = true;
        }
        
        if (!_isInBurst)
        {
            _burstStartTime = timestamp;
            _isInBurst = true;
        }
        
        _burstKeyCount++;
    }
    
    public void OnIdle(DateTime timestamp)
    {
        if (_isInBurst)
        {
            CompleteBurst(timestamp);
        }
    }
    
    public void CheckForCompletion(DateTime currentTime)
    {
        if (_isInBurst && (currentTime - _burstStartTime) > _burstThreshold)
        {
            CompleteBurst(currentTime);
        }
    }
    
    private void CompleteBurst(DateTime endTime)
    {
        if (!_isInBurst) return;
        
        var duration = endTime - _burstStartTime;
        
        if (duration >= _minBurstDuration && _burstKeyCount >= _minKeysForBurst)
        {
            var wpm = CalculateWpm(_burstKeyCount, duration);
            
            var args = new TypingBurstEventArgs(
                _burstStartTime,
                duration,
                _burstKeyCount,
                wpm,
                _currentProcess);
            
            OnBurstCompleted?.Invoke(this, args);
        }
        
        _isInBurst = false;
        _burstKeyCount = 0;
    }
    
    private static double CalculateWpm(int keyCount, TimeSpan duration)
    {
        if (duration.TotalMinutes <= 0) return 0;
        
        var words = keyCount / 5.0;
        return words / duration.TotalMinutes;
    }
    
    public TypingBurstStats GetCurrentStats()
    {
        return new TypingBurstStats
        {
            IsInBurst = _isInBurst,
            KeyCount = _burstKeyCount,
            StartTime = _isInBurst ? _burstStartTime : null,
            CurrentProcess = _currentProcess
        };
    }
}

public class TypingBurstEventArgs : EventArgs
{
    public DateTime StartTime { get; }
    public TimeSpan Duration { get; }
    public int KeyCount { get; }
    public double Wpm { get; }
    public string? ProcessName { get; }
    
    public TypingBurstEventArgs(DateTime startTime, TimeSpan duration, int keyCount, double wpm, string? processName)
    {
        StartTime = startTime;
        Duration = duration;
        KeyCount = keyCount;
        Wpm = wpm;
        ProcessName = processName;
    }
}

public class TypingBurstStats
{
    public bool IsInBurst { get; init; }
    public int KeyCount { get; init; }
    public DateTime? StartTime { get; init; }
    public string? CurrentProcess { get; init; }
}
