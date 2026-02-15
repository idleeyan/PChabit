using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Tai.App.ViewModels;

namespace Tai.App.Views;

public sealed partial class WebDetailsPage : Page
{
    public WebDetailsViewModel ViewModel { get; }
    
    public WebDetailsPage()
    {
        Log.Information("WebDetailsPage: 构造函数开始");
        InitializeComponent();
        Log.Information("WebDetailsPage: InitializeComponent 完成");
        ViewModel = App.GetService<WebDetailsViewModel>();
        Log.Information("WebDetailsPage: ViewModel 获取完成");
        
        Log.Information("WebDetailsPage: 开始设置日期选择器");
        try
        {
            StartDatePicker.Date = new DateTimeOffset(ViewModel.StartDate);
            Log.Information("WebDetailsPage: StartDatePicker 设置完成");
            EndDatePicker.Date = new DateTimeOffset(ViewModel.EndDate);
            Log.Information("WebDetailsPage: EndDatePicker 设置完成");
            CategoryComboBox.SelectedIndex = 0;
            Log.Information("WebDetailsPage: CategoryComboBox 设置完成");
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "WebDetailsPage: 设置控件失败");
        }
        
        Log.Information("WebDetailsPage: 开始订阅事件");
        StartDatePicker.DateChanged += StartDatePicker_DateChanged;
        EndDatePicker.DateChanged += EndDatePicker_DateChanged;
        SearchTextBox.TextChanged += SearchTextBox_TextChanged;
        CategoryComboBox.SelectionChanged += CategoryComboBox_SelectionChanged;
        Log.Information("WebDetailsPage: 构造函数完成");
    }
    
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("WebDetailsPage: OnNavigatedTo 开始");
        try
        {
            await ViewModel.LoadDataAsync();
            UpdateUI();
            Log.Information("WebDetailsPage: LoadDataAsync 完成");
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "WebDetailsPage: 导航失败");
        }
    }
    
    private void UpdateUI()
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
    }
    
    private void StartDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            ViewModel.SetStartDate(args.NewDate.Value.DateTime);
        }
    }
    
    private void EndDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            ViewModel.SetEndDate(args.NewDate.Value.DateTime);
        }
    }
    
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchText = SearchTextBox.Text;
    }
    
    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedItem is ComboBoxItem item)
        {
            ViewModel.SelectedCategory = item.Content?.ToString() ?? "全部分类";
        }
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
    
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshDataAsync();
    }
    
    private void Search_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshDataAsync();
    }
    
    private async System.Threading.Tasks.Task RefreshDataAsync()
    {
        await ViewModel.LoadDataAsync();
        UpdateUI();
    }
}
