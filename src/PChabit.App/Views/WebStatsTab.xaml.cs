using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using PChabit.App.ViewModels;
using PChabit.Infrastructure.Data;

namespace PChabit.App.Views;

public sealed partial class WebStatsTab : UserControl
{
    public WebDetailsViewModel ViewModel { get; }

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
        TopDomainText.Text = ViewModel.TopDomain;

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
}
