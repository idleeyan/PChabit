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
public class ThemeOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public class LanguageOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

}

