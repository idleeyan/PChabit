using Tai.Core.Interfaces;

namespace Tai.Core.Entities;

public class MouseSession : EntityBase, IAggregateRoot
{
    public DateTime Date { get; set; }
    public int Hour { get; set; }
    public string? ProcessName { get; set; }
    
    public int LeftClickCount { get; set; }
    public int RightClickCount { get; set; }
    public int MiddleClickCount { get; set; }
    public int TotalClicks => LeftClickCount + RightClickCount + MiddleClickCount;
    public Dictionary<string, int> ClickByArea { get; set; } = new();
    
    public double TotalMoveDistance { get; set; }
    public double TotalDistance => TotalMoveDistance;
    public int MoveEventCount { get; set; }
    
    public int ScrollCount { get; set; }
    public int ScrollLines { get; set; }
    public Dictionary<string, int> ScrollByDirection { get; set; } = new();
    
    public double AverageClickInterval { get; set; }
    
    public List<MouseTrail> Trails { get; set; } = [];
    public List<ClickCluster> ClickClusters { get; set; } = [];
}

public class MouseTrail
{
    public DateTime Timestamp { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Velocity { get; set; }
    public string? ScreenRegion { get; set; }
}

public class ClickCluster
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public int ClickCount { get; set; }
    public double Radius { get; set; }
}
