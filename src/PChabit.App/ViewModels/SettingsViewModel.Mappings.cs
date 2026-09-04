using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using PChabit.Core.Entities;
using PChabit.Core.Interfaces;
using PChabit.Infrastructure.Data;
using PChabit.Infrastructure.Services;


namespace PChabit.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private void LoadCategoryMappings()
    {
        CategoryMappings.Clear();
        CategoryMappings.Add(new CategoryMapping { ProcessName = "code.exe", Category = "开发", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "devenv.exe", Category = "开发", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "chrome.exe", Category = "浏览", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "msedge.exe", Category = "浏览", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "slack.exe", Category = "沟通", IsEditable = true });
        CategoryMappings.Add(new CategoryMapping { ProcessName = "spotify.exe", Category = "娱乐", IsEditable = true });
    }

    [RelayCommand]
    private void AddCategoryMapping()
    {
        CategoryMappings.Add(new CategoryMapping { ProcessName = "new.exe", Category = "其他", IsEditable = true });
    }

    [RelayCommand]
    private void RemoveCategoryMapping(CategoryMapping? mapping)
    {
        if (mapping != null)
        {
            CategoryMappings.Remove(mapping);
        }
    }

public class CategoryMapping
{
    public string ProcessName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsEditable { get; init; }
}

}

