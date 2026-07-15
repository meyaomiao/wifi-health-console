using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services;
using WiFiHealthConsole.App.Services.Diagnostics;
using WiFiHealthConsole.App.Services.History;

namespace WiFiHealthConsole.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private Task? _channelRadarInitializationTask;

    [ObservableProperty]
    private NavigationItemViewModel selectedNavigationItem;

    [ObservableProperty]
    private ViewModelBase currentPage;

    public MainWindowViewModel()
    {
        var wifiProvider = WifiTelemetryProviderFactory.CreateDefault();
        var networkContextService = new NetworkContextService();

        var historyStore = OperatingSystem.IsWindows()
            ? new HistoryStore()
            : new HistoryStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WiFiHealthConsole-WindowsPreview",
                "history.json"));
        History = new HistoryPageViewModel(historyStore);
        Diagnosis = new DiagnosisPageViewModel(
            new NetworkDiagnosticService(wifiProvider, networkContextService),
            History);
        Overview = new OverviewPageViewModel(wifiProvider, NavigateAndRunDiagnosisAsync);
        SpeedTest = new SpeedTestPageViewModel();
        ChannelRadar = new ChannelRadarPageViewModel(wifiProvider);
        Router = new RouterPageViewModel(
            networkContextService,
            wifiProvider,
            NavigateAndRunDiagnosisAsync);

        NavigationItems =
        [
            new(AppSection.Overview, "概览", Icon.Grid, Overview),
            new(AppSection.Diagnosis, "60 秒体检", Icon.HeartPulse, Diagnosis),
            new(AppSection.SpeedTest, "网速测速", Icon.Gauge, SpeedTest),
            new(AppSection.ChannelRadar, "信道雷达", Icon.Scan, ChannelRadar),
            new(AppSection.History, "历史趋势", Icon.History, History),
            new(AppSection.Router, "路由管理", Icon.Router, Router)
        ];

        var previewPage = Environment.GetEnvironmentVariable("WIFI_HEALTH_PREVIEW_PAGE");
        selectedNavigationItem = NavigationItems.FirstOrDefault(item =>
            string.Equals(item.Section.ToString(), previewPage, StringComparison.OrdinalIgnoreCase))
            ?? NavigationItems[0];
        currentPage = selectedNavigationItem.Page;
    }

    public string Title => "Wi-Fi 体检台";
    public string PlatformLabel => OperatingSystem.IsWindows() ? "Windows" : "Windows 界面预览";
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public OverviewPageViewModel Overview { get; }
    public DiagnosisPageViewModel Diagnosis { get; }
    public SpeedTestPageViewModel SpeedTest { get; }
    public ChannelRadarPageViewModel ChannelRadar { get; }
    public HistoryPageViewModel History { get; }
    public RouterPageViewModel Router { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await History.InitializeAsync(cancellationToken);
        await Task.WhenAll(Overview.InitializeAsync(cancellationToken), Router.InitializeAsync(cancellationToken));
        SpeedTest.UpdateWifiContext(Overview.CurrentSnapshot);
        await History.AppendWirelessAsync(
            Overview.CurrentSnapshot,
            Overview.CurrentGrade,
            Overview.OverallStatus,
            cancellationToken);

        if (!OperatingSystem.IsWindows()
            || SelectedNavigationItem.Section == AppSection.ChannelRadar)
        {
            await EnsureChannelRadarInitializedAsync(cancellationToken);
        }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel value)
    {
        CurrentPage = value.Page;
        if (value.Section == AppSection.ChannelRadar)
        {
            _ = EnsureChannelRadarInitializedInBackgroundAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(Overview.InitializeAsync(cancellationToken), Router.InitializeAsync(cancellationToken));
        SpeedTest.UpdateWifiContext(Overview.CurrentSnapshot);
        await History.AppendWirelessAsync(
            Overview.CurrentSnapshot,
            Overview.CurrentGrade,
            Overview.OverallStatus,
            cancellationToken);
    }

    [RelayCommand]
    private Task RunDiagnosisAsync(CancellationToken cancellationToken = default) =>
        NavigateAndRunDiagnosisAsync(cancellationToken);

    [RelayCommand]
    private async Task RunSpeedTestAsync()
    {
        SelectedNavigationItem = NavigationItems.Single(item => item.Section == AppSection.SpeedTest);
        await SpeedTest.RunSpeedTestCommand.ExecuteAsync(null);
    }

    private async Task NavigateAndRunDiagnosisAsync(CancellationToken cancellationToken = default)
    {
        SelectedNavigationItem = NavigationItems.Single(item => item.Section == AppSection.Diagnosis);
        await Diagnosis.RunDiagnosisAsync(cancellationToken);
    }

    private Task EnsureChannelRadarInitializedAsync(CancellationToken cancellationToken = default)
    {
        _channelRadarInitializationTask ??= InitializeChannelRadarCoreAsync(cancellationToken);
        return _channelRadarInitializationTask;
    }

    private async Task InitializeChannelRadarCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ChannelRadar.InitializeAsync(cancellationToken);
        }
        catch
        {
            _channelRadarInitializationTask = null;
            throw;
        }
    }

    private async Task EnsureChannelRadarInitializedInBackgroundAsync()
    {
        try
        {
            await EnsureChannelRadarInitializedAsync();
        }
        catch (OperationCanceledException)
        {
            // A later navigation can start the first scan again.
        }
    }
}
