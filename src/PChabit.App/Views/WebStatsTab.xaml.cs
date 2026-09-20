using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.App.Services;
using PChabit.App.ViewModels;
using PChabit.Core.Entities;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;

namespace PChabit.App.Views;

public sealed partial class WebStatsTab : UserControl
{
    public WebDetailsViewModel ViewModel { get; }
    private string? _contextDomain;

    public WebStatsTab()
    {
        var dbFactory = App.GetService<IDbContextFactory<PChabitDbContext>>();
        ViewModel = new WebDetailsViewModel(dbFactory);
        DataContext = ViewModel;

        InitializeComponent();

        StartDatePicker.Date = new DateTimeOffset(ViewModel.StartDate);
        EndDatePicker.Date = new DateTimeOffset(ViewModel.EndDate);

        StartDatePicker.DateChanged += StartDatePicker_DateChanged;
        EndDatePicker.DateChanged += EndDatePicker_DateChanged;
        SearchTextBox.TextChanged += SearchTextBox_TextChanged;
        CategoryComboBox.SelectionChanged += CategoryComboBox_SelectionChanged;

        Loaded += WebStatsTab_Loaded;
    }

    private async void WebStatsTab_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.LoadDataAsync();
            UpdateUI();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebStatsTab: 加载数据失败");
        }
    }

    public void UpdateUI()
    {
        TotalVisitsText.Text = ViewModel.TotalVisits;
        TotalDurationText.Text = ViewModel.TotalDuration;
        UniqueDomainsText.Text = ViewModel.UniqueDomains;
        AvgDurationText.Text = ViewModel.AvgDuration;
        PeakHourText.Text = ViewModel.PeakHour;
        TopDomainText.Content = ViewModel.TopDomain;
        TopDomainText.Tag = ViewModel.TopDomain;

        DomainStatsList.ItemsSource = ViewModel.DomainStats;
        BrowsingPatternsList.ItemsSource = ViewModel.BrowsingPatterns;
        HourlyActivityList.ItemsSource = ViewModel.HourlyActivity;
        DailyTrendList.ItemsSource = ViewModel.DailyTrend;
        RecentVisitsList.ItemsSource = ViewModel.RecentVisits;

        LoadMoreButton.Visibility = ViewModel.HasMoreVisits ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
            ViewModel.SetStartDate(args.NewDate.Value.DateTime);
    }

    private void EndDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
            ViewModel.SetEndDate(args.NewDate.Value.DateTime);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchText = SearchTextBox.Text;
    }

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedItem is ComboBoxItem item)
            ViewModel.SelectedCategory = item.Content?.ToString() ?? "全部分类";
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SetDateRange("today");
        StartDatePicker.Date = new DateTimeOffset(ViewModel.StartDate);
        EndDatePicker.Date = new DateTimeOffset(ViewModel.EndDate);
        _ = RefreshDataAsync();
    }

    private void Week_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SetDateRange("week");
        StartDatePicker.Date = new DateTimeOffset(ViewModel.StartDate);
        EndDatePicker.Date = new DateTimeOffset(ViewModel.EndDate);
        _ = RefreshDataAsync();
    }

    private void Month_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SetDateRange("month");
        StartDatePicker.Date = new DateTimeOffset(ViewModel.StartDate);
        EndDatePicker.Date = new DateTimeOffset(ViewModel.EndDate);
        _ = RefreshDataAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshDataAsync();
    private void Search_Click(object sender, RoutedEventArgs e) => _ = RefreshDataAsync();

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadMoreVisitsCommand.Execute(null);
        UpdateUI();
    }

    private async Task RefreshDataAsync()
    {
        await ViewModel.LoadDataAsync();
        UpdateUI();
    }

    private void OpenDomain_Click(object sender, RoutedEventArgs e)
    {
        var target = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrWhiteSpace(target))
        {
            target = (sender as HyperlinkButton)?.Content as string;
        }

        if (string.IsNullOrWhiteSpace(target) || target == "-")
            return;

        if (!UrlLauncher.TryOpen(target))
        {
            Log.Warning("无法打开: {Target}", target);
        }
    }

    // ── 右键设置网站分类 ──────────────────────────────────────────

    private async void DomainMenu_Opening(object sender, object e)
    {
        try
        {
            var flyout = (MenuFlyout)sender;
            _contextDomain = ExtractDomainFromFlyoutTarget(flyout);
            if (string.IsNullOrWhiteSpace(_contextDomain))
                return;

            // 清掉旧的分类项（Tag == "cat"）
            var toRemove = new List<MenuFlyoutItemBase>();
            foreach (var item in flyout.Items)
            {
                if (item is MenuFlyoutItem { Tag: "cat" })
                    toRemove.Add(item);
            }
            foreach (var r in toRemove) flyout.Items.Remove(r);

            // 插入分类列表（在 ClearCategory 之前）
            var insertAt = 0;
            for (var i = 0; i < flyout.Items.Count; i++)
            {
                if (flyout.Items[i] is MenuFlyoutItem { Text: "清除分类映射" })
                {
                    insertAt = i;
                    break;
                }
            }

            var categories = await Task.Run(() =>
            {
                var svc = App.GetService<IWebsiteCategoryService>();
                return svc.GetAllCategoriesAsync();
            });

            var domain = _contextDomain;
            var current = await Task.Run(async () =>
            {
                var svc = App.GetService<IWebsiteCategoryService>();
                return await svc.GetCategoryForDomainAsync(domain!);
            });

            foreach (var cat in categories)
            {
                var label = string.IsNullOrEmpty(cat.Icon) ? cat.Name : $"{cat.Icon} {cat.Name}";
                if (string.Equals(current, cat.Name, StringComparison.OrdinalIgnoreCase))
                    label += "  ✓";

                var item = new MenuFlyoutItem
                {
                    Text = label,
                    Tag = "cat"
                };
                var catId = cat.Id;
                item.Click += async (_, _) => await AssignCategoryAsync(domain!, catId);
                flyout.Items.Insert(insertAt++, item);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开分类菜单失败");
        }
    }

    private static string? ExtractDomainFromFlyoutTarget(MenuFlyout flyout)
    {
        if (flyout.Target is FrameworkElement fe)
        {
            if (fe.DataContext is WebDetailsViewModel.DomainStatItem d) return d.Domain;
            var parent = fe;
            while (parent != null)
            {
                if (parent is ListViewItem lvi && lvi.Content is WebDetailsViewModel.DomainStatItem dd)
                    return dd.Domain;
                if (parent.DataContext is WebDetailsViewModel.DomainStatItem d2)
                    return d2.Domain;
                parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
            }
        }
        return null;
    }

    private async void ClearCategory_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_contextDomain)) return;
        await AssignCategoryAsync(_contextDomain, null);
    }

    private async Task AssignCategoryAsync(string domain, int? categoryId)
    {
        try
        {
            var svc = App.GetService<IWebsiteCategoryService>();
            var ok = await svc.AssignDomainToCategoryAsync(domain, categoryId);
            Log.Information("右键设置分类: {Domain} → {CatId} ok={Ok}", domain, categoryId, ok);
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "设置域名分类失败: {Domain}", domain);
        }
    }
}
