namespace PChabit.App.Controls;

/// <summary>
/// 小时分布图的一行（3.35.0）。
///
/// 项目里原有 4 处「每小时分布」图表，数据源类型各不相同
/// （<c>HourlyUsageItem</c> / <c>HourlyActivityItem</c> / <c>HourlyKeyItem</c> /
///  <c>HourMouseItem</c>），字段名也不统一（<c>Hour</c> / <c>HourLabel</c>、
///  <c>Activity</c> / <c>Presses</c> / <c>VisitCount</c>…）。
/// 各 ViewModel 负责投影成本类型，图表本身只负责呈现。
/// </summary>
/// <param name="Label">横轴标签（如 "08:00"）。</param>
/// <param name="Value">条形长度对应的数值。</param>
/// <param name="Display">右侧显示文本；为空则显示 <paramref name="Value"/>。</param>
public sealed record HourlyBarRow(string Label, double Value, string? Display = null);