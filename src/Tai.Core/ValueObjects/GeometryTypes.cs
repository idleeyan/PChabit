namespace Tai.Core.ValueObjects;

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public static Rect Empty => new(0, 0, 0, 0);
    
    public bool IsEmpty => Width == 0 && Height == 0;
}

public readonly record struct Point(double X, double Y)
{
    public static Point Empty => new(0, 0);
    
    public double DistanceTo(Point other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
