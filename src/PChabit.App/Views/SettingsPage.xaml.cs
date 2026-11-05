using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

/// <summary>
/// 设置页宿主（3.64.0–3.67.0 完成 D3 分区拆分）。
/// 本文件只保留：左侧分区导航 + 右侧分区宿主容器 + 各分区 UserControl 的
/// 注入/刷新编排；具体设置项的加载与保存由 `Views/Settings/*Section` 自治。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        Log.Information("SettingsPage: 构造函数开始");

        try
        {
            ViewModel = App.GetService<SettingsViewModel>();
            Log.Information("SettingsPage: ViewModel 已获取");

            InitializeComponent();
            Log.Information("SettingsPage: InitializeComponent 完成");

            // 分区注入同一 ViewModel 实例（SettingsViewModel 注册为 Transient，
            // 各分区不可自行 App.GetService，否则会拿到不同实例导致读写失效）
            AboutSectionControl.Refresh();
            AiSectionControl.Initialize(ViewModel);
            StickyNotesControl.Initialize(ViewModel);
            GeneralControl.Initialize(ViewModel);
            MonitorControl.Initialize(ViewModel);
            TaskbarControl.Initialize(ViewModel);
            DesktopWidgetControl.Initialize(ViewModel);
            AppearanceControl.Initialize(ViewModel);
            DataControl.Initialize(ViewModel);

            // 选中项必须等 InitializeComponent 之后再设：解析期触发 SelectionChanged
            // 时右侧分区控件还不存在，会空引用崩溃
            SectionList.SelectedIndex = 0;
            ApplySection("general");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: 构造函数失败");
            throw;
        }

        Log.Information("SettingsPage: 构造函数完成");
    }

    /// <summary>左侧分区导航切换：只显示当前分组的卡片，其余折叠。</summary>
    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: ListViewItem { Tag: string tag } })
            ApplySection(tag);
    }

    private void ApplySection(string tag)
    {
        // 防御：XAML 解析期间若被提前触发，部分分区字段可能尚未赋值
        void Show(UIElement? element, bool visible)
        {
            if (element is null) return;
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        Show(GeneralControl, tag == "general");
        Show(AppearanceControl, tag == "general");
        Show(MonitorControl, tag == "monitor");
        Show(TaskbarControl, tag == "interface");
        Show(DesktopWidgetControl, tag == "interface");
        Show(StickyNotesControl, tag == "notes");
        Show(DataControl, tag == "data");
        Show(AiSectionControl, tag == "ai");
        Show(AboutSectionControl, tag == "about");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("SettingsPage: OnNavigatedTo 开始");
        base.OnNavigatedTo(e);

        try
        {
            AboutSectionControl.Refresh();

            await ViewModel.InitializeAsync();
            Log.Information("SettingsPage: InitializeAsync 完成");

            // 各分区重新加载当前值（等价原集中式 LoadSettingsToUI 的按分区切片）
            AiSectionControl.Reload();
            StickyNotesControl.Reload();
            GeneralControl.Reload();
            MonitorControl.Reload();
            TaskbarControl.Reload();
            DesktopWidgetControl.Reload();
            AppearanceControl.Reload();
            DataControl.Reload();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsPage: InitializeAsync 失败");
        }
    }
}
