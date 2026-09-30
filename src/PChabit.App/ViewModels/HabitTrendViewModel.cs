using CommunityToolkit.Mvvm.ComponentModel;

namespace PChabit.App.ViewModels;

/// <summary>习惯养成轨迹卡片。</summary>
public sealed partial class HabitTrendViewModel : ObservableObject
{
    public string Name { get; init; } = "";
    public string Direction { get; init; } = "stable"; // better | worse | stable
    public string Detail { get; init; } = "";
    public int SupportWeeks { get; init; }
    public double Strength { get; init; }

    public string DirectionText => Direction switch
    {
        "better" => "变好",
        "worse" => "变差",
        _ => "稳定"
    };

    public string BadgeText => Direction switch
    {
        "better" => "↑",
        "worse" => "↓",
        _ => "="
    };

    public string BadgeColor => Direction switch
    {
        "better" => "#2E7D32",
        "worse" => "#C62828",
        _ => "#616161"
    };

    public string SupportText => SupportWeeks > 0 ? $"连续 {SupportWeeks} 周" : "";
}
