using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using PChabit.App.ViewModels;

namespace PChabit.App.Views;

public sealed partial class MouseDetailsPage : UserControl
{
    public MouseDetailsViewModel ViewModel { get; }

    public ObservableCollection<string> Periods { get; } = new() { "今日", "本周", "上周" };

    public MouseDetailsPage()
    {
        ViewModel = App.GetService<MouseDetailsViewModel>();
        InitializeComponent();
        Loaded += MouseDetailsPage_Loaded;
    }

    private async void MouseDetailsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadDataCommand.ExecuteAsync(null);
    }
}
