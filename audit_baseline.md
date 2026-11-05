# PChabit code baseline audit

- Generated: 2026-11-06 04:43:37
- Empty catch count: 13
- catch(Exception) count: 329
- async void count: 62
- Task.Run count: 42
- using Infrastructure in App/ViewModel: 85

## Empty catch by file

| File | Count |
|---|---|
| src\PChabit.App\Services\HardwareSampleWriter.cs | 1 |
| src\PChabit.HardwareMonitor\Hardware\BatteryService.cs | 1 |
| src\PChabit.App\Views\AppStatsTab.xaml.cs | 1 |
| src\PChabit.Infrastructure\Platform\GlobalHotkeyService.cs | 1 |
| src\PChabit.App\Services\GlobalOpLog.cs | 1 |
| src\PChabit.App\App.xaml.cs | 1 |
| src\PChabit.HardwareMonitor\Hardware\ProcessResourceMonitor.cs | 1 |
| src\PChabit.App\Controls\MarkdownText.cs | 1 |
| src\PChabit.App\ViewModels\DataManagementViewModel.Utils.cs | 1 |
| src\PChabit.App\Views\ShellPage.xaml.cs | 1 |
| src\PChabit.Infrastructure\Services\WebDAVSyncService.cs | 1 |
| src\PChabit.Infrastructure\Services\WebSocketServer.cs | 1 |
| src\PChabit.App\ViewModels\NotesViewModel.cs | 1 |

## catch(Exception) top files

| File | Count |
|---|---|
| src\PChabit.App\App.xaml.cs | 31 |
| src\PChabit.App\Services\DataCollectionService.cs | 12 |
| src\PChabit.App\Views\DashboardPage.xaml.cs | 11 |
| src\PChabit.App\Services\AppIconService.cs | 10 |
| src\PChabit.App\Views\SettingsPage.xaml.cs | 10 |
| src\PChabit.HardwareMonitor\Hardware\HardwareMonitor.cs | 9 |
| src\PChabit.Infrastructure\Services\SettingsService.cs | 9 |
| src\PChabit.App\App.StickyNotes.cs | 9 |
| src\PChabit.HardwareMonitor\Hardware\ProcessResourceMonitor.cs | 9 |
| src\PChabit.App\ViewModels\DashboardViewModel.cs | 9 |

## Top 20 files by line count

| File | Lines |
|---|---|
| src\PChabit.Infrastructure\Services\SettingsService.cs | 1468 |
| src\PChabit.App\ViewModels\DashboardViewModel.cs | 1162 |
| src\PChabit.App\App.xaml.cs | 1150 |
| src\PChabit.App\Views\SettingsPage.xaml | 1057 |
| src\PChabit.App\Views\SettingsPage.xaml.cs | 912 |
| src\PChabit.HardwareMonitor\Hardware\ProcessNetworkMonitor.cs | 911 |
| src\PChabit.Infrastructure\Data\DatabaseInitializer.cs | 822 |
| src\PChabit.Infrastructure\Analysis\AnalyticsEngine.cs | 815 |
| src\PChabit.App\Services\TaskbarWidget.cs | 786 |
| src\PChabit.App\Services\DesktopWidget.cs | 761 |
| src\PChabit.App\Views\DashboardPage.xaml | 683 |
| src\PChabit.HardwareMonitor\Hardware\HardwareMonitor.cs | 627 |
| src\PChabit.App\Views\HardwareMonitorPage.xaml | 607 |
| src\PChabit.App\Views\WebStatsTab.xaml | 603 |
| src\PChabit.App\Services\DataCollectionService.Collection.cs | 598 |
| src\PChabit.HardwareMonitor\Hardware\NetworkManager.cs | 564 |
| src\PChabit.HardwareMonitor\Hardware\HardwareValueProvider.cs | 564 |
| src\PChabit.App\Services\DataCollectionService.Maintenance.cs | 562 |
| src\PChabit.App\Services\DataCollectionService.Web.cs | 553 |
| src\PChabit.App\ViewModels\SettingsViewModel.cs | 545 |

