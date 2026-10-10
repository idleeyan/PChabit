using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PChabit.App.Controls;

/// <summary>
/// 小时分布条形图（3.35.0）。
///
/// 替代项目里 4 处各自书写的「标签 + ProgressBar + 数值」三列 Grid
/// （AppStatsTab 每小时活动、WebStatsTab 每每小时活动分布、
///  KeyboardDetailsTab 每小时按键分布 / 每小时鼠标统计）——
/// 那些实现列宽、间距、数值格式各不相同，改一次样式要改四处。
///
/// 数据源统一为 <see cref="HourlyBarRow"/>，各 ViewModel 负责投影，
/// 本控件只管呈现。上限为 0 时按数据自动取值并留 10% 余量。
/// 行在代码里生成而非 DataTemplate：行内ProgressBar 的 Maximum 依赖整组数据，
/// 只能整表重建，模板化反而更绕。
/// </summary>
public sealed partial class HourlyBarChart : UserControl
{
    /// <summary>要显示的行集合。</summary>
    public IReadOnlyList<HourlyBarRow> Rows
    {
        get => (IReadOnlyList<HourlyBarRow>)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>进度条上限；0 表示按数据自动取值。</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>标签列宽。</summary>
    public double LabelWidth
    {
        get => (double)GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    /// <summary>数值列宽；0 表示不显示数值列。</summary>
    public double ValueWidth
    {
        get => (double)GetValue(ValueWidthProperty);
        set => SetValue(ValueWidthProperty, value);
    }

    /// <summary>行间距。</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>是否限制最大高度（行数多时避免撑破布局）。</summary>
    public bool Scrollable
    {
        get => (bool)GetValue(ScrollableProperty);
        set => SetValue(ScrollableProperty, value);
    }

    /// <summary>可滚动时的最大高度。</summary>
    public double MaxChartHeight
    {
        get => (double)GetValue(MaxChartHeightProperty);
        set => SetValue(MaxChartHeightProperty, value);
    }

    /// <summary>空数据时的提示文字。</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(nameof(Rows), typeof(IReadOnlyList<HourlyBarRow>),
            typeof(HourlyBarChart), new PropertyMetadata(null, OnVisualChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(HourlyBarChart),
            new PropertyMetadata(0d, OnVisualChanged));

    public static readonly DependencyProperty LabelWidthProperty =
        DependencyProperty.Register(nameof(LabelWidth), typeof(double), typeof(HourlyBarChart),
            new PropertyMetadata(50d, OnVisualChanged));

    public static readonly DependencyProperty ValueWidthProperty =
        DependencyProperty.Register(nameof(ValueWidth), typeof(double), typeof(HourlyBarChart),
            new PropertyMetadata(60d, OnVisualChanged));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(HourlyBarChart),
            new PropertyMetadata(2d, OnVisualChanged));

    public static readonly DependencyProperty ScrollableProperty =
        DependencyProperty.Register(nameof(Scrollable), typeof(bool), typeof(HourlyBarChart),
            new PropertyMetadata(false, OnVisualChanged));

    public static readonly DependencyProperty MaxChartHeightProperty =
        DependencyProperty.Register(nameof(MaxChartHeight), typeof(double), typeof(HourlyBarChart),
            new PropertyMetadata(300d, OnVisualChanged));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(HourlyBarChart),
            new PropertyMetadata("所选范围内暂无数据", OnVisualChanged));

    public HourlyBarChart()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    /// <summary>
    /// 若 <see cref="Rows"/> 传入的是可观察集合，订阅其变化自动重绘。
    /// 这样 ViewModel 只需维护一个集合，无需额外发属性变更通知。
    /// </summary>
    private void OnRowsChanged()
    {
        if (_subscribed is INotifyCollectionChanged old)
        {
            old.CollectionChanged -= Rows_CollectionChanged;
        }

        _subscribed = Rows as INotifyCollectionChanged;
        if (_subscribed != null)
        {
            _subscribed.CollectionChanged += Rows_CollectionChanged;
        }
    }

    private INotifyCollectionChanged? _subscribed;

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HourlyBarChart chart)
        {
            if (e.Property == RowsProperty)
            {
                chart.OnRowsChanged();
            }
            chart.Refresh();
        }
    }

    private void Refresh()
    {
        if (RowHost == null)
        {
            return;
        }

        var rows = Rows;
        bool hasData = rows != null && rows.Count > 0;

        EmptyHintText.Text = EmptyText ?? string.Empty;
        EmptyHintText.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        ChartPanel.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;

        if (!hasData)
        {
            RowHost.Children.Clear();
            return;
        }

        double max = Maximum > 0 ? Maximum : ComputeAutoMaximum(rows);
        var labelWidth = LabelWidth > 0 ? LabelWidth : 50;
        var valueWidth = ValueWidth;
        var spacing = RowSpacing;

        RowHost.Children.Clear();
        foreach (var r in rows)
        {
            RowHost.Children.Add(BuildRow(r, max, labelWidth, valueWidth, spacing));
        }
    }

    private static FrameworkElement BuildRow(HourlyBarRow row, double max,
        double labelWidth, double valueWidth, double spacing)
    {
        var grid = new Grid { Margin = new Thickness(0, spacing, 0, spacing) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = row.Label ?? string.Empty,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = max,
            Value = Math.Max(0, Math.Min(row.Value, max)),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);

        if (valueWidth > 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });
            var value = new TextBlock
            {
                Text = row.Display ?? FormatNumber(row.Value),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);
        }

        return grid;
    }

    private static string FormatNumber(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 0.005)
        {
            return ((long)Math.Round(v)).ToString("N0");
        }
        return v.ToString("0.##");
    }

    private static double ComputeAutoMaximum(IReadOnlyList<HourlyBarRow> rows)
    {
        double max = 0;
        foreach (var r in rows)
        {
            if (r.Value > max) max = r.Value;
        }

        // 留 10% 余量，避免最长条顶满；全为 0 时给非零上限以便 ProgressBar 正常渲染
        return max > 0 ? max * 1.1 : 1d;
    }
}