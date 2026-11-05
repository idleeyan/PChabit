# PChabit code baseline audit

- Generated: 2026-11-06 05:21:45
- Empty catch count: 
- Silent catch count (no Log./throw inside): 6
- catch(Exception) count: 398
- async void count: 62
- Task.Run count: 42
- using Infrastructure in App/ViewModel: 85

## Empty catch by file

| File | Count |
|---|---|

## Silent catch top files (no Log./throw inside)

| File | Count |
|---|---|
| src\PChabit.Infrastructure\Services\WebSocketServer.cs | 2 |
| src\PChabit.App\Services\AppIconService.cs | 1 |
| src\PChabit.App\Services\HardwareSampleWriter.cs | 1 |
| src\PChabit.Core\Interfaces\IBackgroundAppSettings.cs | 1 |
| src\PChabit.App\Services\DataCollectionService.Persistence.cs | 1 |

## Silent catch locations

- src\PChabit.App\Services\AppIconService.cs:137
- src\PChabit.App\Services\DataCollectionService.Persistence.cs:374
- src\PChabit.App\Services\HardwareSampleWriter.cs:120
- src\PChabit.Core\Interfaces\IBackgroundAppSettings.cs:94
- src\PChabit.Infrastructure\Services\WebSocketServer.cs:143
- src\PChabit.Infrastructure\Services\WebSocketServer.cs:229

## catch(Exception) top files

| File | Count |
|---|---|
| src\PChabit.App\App.xaml.cs | 37 |
| src\PChabit.HardwareMonitor\Hardware\ProcessResourceMonitor.cs | 16 |
| src\PChabit.App\Services\DataCollectionService.cs | 12 |
| src\PChabit.App\Views\DashboardPage.xaml.cs | 12 |
| src\PChabit.App\Services\AppIconService.cs | 12 |
| src\PChabit.App\Views\SettingsPage.xaml.cs | 12 |
| src\PChabit.Infrastructure\Services\SettingsService.cs | 10 |
| src\PChabit.App\App.StickyNotes.cs | 9 |
| src\PChabit.App\ViewModels\DashboardViewModel.cs | 9 |
| src\PChabit.HardwareMonitor\Hardware\HardwareMonitor.cs | 9 |

## Top 20 files by line count

| File | Lines |
|---|---|
| src\PChabit.Infrastructure\Services\SettingsService.cs | 1470 |
| src\PChabit.App\App.xaml.cs | 1171 |
| src\PChabit.App\ViewModels\DashboardViewModel.cs | 1162 |
| src\PChabit.App\Views\SettingsPage.xaml | 1057 |
| src\PChabit.App\Views\SettingsPage.xaml.cs | 918 |
| src\PChabit.HardwareMonitor\Hardware\ProcessNetworkMonitor.cs | 916 |
| src\PChabit.Infrastructure\Data\DatabaseInitializer.cs | 824 |
| src\PChabit.Infrastructure\Analysis\AnalyticsEngine.cs | 819 |
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

