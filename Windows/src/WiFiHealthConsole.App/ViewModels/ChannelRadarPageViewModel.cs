using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public partial class ChannelRadarPageViewModel : ViewModelBase
{
    private static readonly string[] CurveColors =
    [
        "#2997FF", "#BF5AF2", "#36D7C7", "#FF4D6D", "#FF9F0A", "#64D2FF", "#30D158", "#FFD60A"
    ];

    private readonly IWifiTelemetryProvider _wifiProvider;
    private WifiSnapshot _current = WifiSnapshot.Unavailable;
    private IReadOnlyList<NearbyNetwork> _rawNetworks = [];

    [ObservableProperty]
    private string selectedBand = "总览";

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private bool isBand2Selected;

    [ObservableProperty]
    private bool isOverviewSelected = true;

    [ObservableProperty]
    private bool isBand5Selected;

    [ObservableProperty]
    private bool isBand6Selected;

    [ObservableProperty]
    private bool showPermissionBanner;

    [ObservableProperty]
    private string permissionTitle = "附近网络扫描需要 Windows 定位权限";

    [ObservableProperty]
    private string permissionDetail = "授权后才能读取 BSSID 和附近信道；应用不会读取或保存经纬度。";

    [ObservableProperty]
    private string networkCount = "0";

    [ObservableProperty]
    private string activeChannelCount = "0";

    [ObservableProperty]
    private string overlapCount = "0";

    [ObservableProperty]
    private string currentSummary = "当前：未检测";

    [ObservableProperty]
    private string chartAccessibilitySummary = "尚无附近网络扫描结果。";

    [ObservableProperty]
    private string? errorMessage;

    public ChannelRadarPageViewModel(IWifiTelemetryProvider? wifiProvider = null)
    {
        _wifiProvider = wifiProvider ?? WifiTelemetryProviderFactory.CreateDefault();
    }

    public string Title => "信道雷达";
    public string Subtitle => "查看可见网络频谱，并获得频段、频宽和信道建议";
    public IReadOnlyList<string> Bands { get; } = ["总览", "2.4 GHz", "5 GHz", "6 GHz"];
    public double ChartHeight => IsOverviewSelected ? 520 : 360;
    public string ScanButtonText => IsScanning ? "扫描中" : "重新扫描";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasVisibleNetworks => VisibleNetworks.Count > 0;
    public bool ShowChart => !IsScanning && HasVisibleNetworks;
    public bool ShowEmptyState => !IsScanning && !HasVisibleNetworks && !ShowPermissionBanner;

    public ObservableCollection<ChannelNetworkViewModel> Networks { get; } = [];
    public ObservableCollection<ChannelNetworkViewModel> VisibleNetworks { get; } = [];
    public ObservableCollection<SuggestionCardViewModel> Suggestions { get; } = [];

    partial void OnIsScanningChanged(bool value) => NotifyPresentationState();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await ScanAsync(cancellationToken);

    [RelayCommand]
    private async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        IsScanning = true;
        OnPropertyChanged(nameof(ScanButtonText));
        ErrorMessage = null;
        try
        {
            _current = await _wifiProvider.GetCurrentAsync(cancellationToken);
            _rawNetworks = await _wifiProvider.ScanAsync(cancellationToken);
            ShowPermissionBanner = false;
            ApplyNetworks();
        }
        catch (WifiLocationPermissionException error)
        {
            ShowPermissionBanner = true;
            PermissionTitle = "Windows 已阻止附近 Wi-Fi 扫描";
            PermissionDetail = error.Message;
            ErrorMessage = error.Message;
            _rawNetworks = [];
            ApplyNetworks();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ErrorMessage = error.Message;
            _rawNetworks = [];
            ApplyNetworks();
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(ScanButtonText));
        }
    }

    [RelayCommand]
    private async Task OpenLocationSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _wifiProvider.OpenLocationPrivacySettingsAsync(cancellationToken);
    }

    [RelayCommand]
    private void SelectBand(string band)
    {
        SelectedBand = band;
        IsOverviewSelected = band == "总览";
        IsBand2Selected = band == "2.4 GHz";
        IsBand5Selected = band == "5 GHz";
        IsBand6Selected = band == "6 GHz";
        OnPropertyChanged(nameof(ChartHeight));
        UpdateStats();
    }

    private void ApplyNetworks()
    {
        var currentBssid = _current.Bssid.TryGetValue(out var bssid) ? bssid : null;
        Networks.Clear();
        var nextCurveColor = 1;
        foreach (var network in _rawNetworks.OrderBy(network => network.Band).ThenBy(network => network.PrimaryChannel).ThenByDescending(network => network.RssiDbm))
        {
            var isCurrent = currentBssid is not null
                && string.Equals(network.Bssid, currentBssid, StringComparison.OrdinalIgnoreCase);
            var color = isCurrent
                ? CurveColors[0]
                : CurveColors[((nextCurveColor++ - 1) % (CurveColors.Length - 1)) + 1];
            Networks.Add(new ChannelNetworkViewModel(
                network.Id,
                string.IsNullOrWhiteSpace(network.Ssid) ? "隐藏网络" : network.Ssid!,
                network.Band.DisplayName(),
                network.PrimaryChannel,
                network.ChannelWidthMHz,
                network.RssiDbm,
                color,
                isCurrent));
        }

        SelectBand("总览");
        ApplySuggestions();
    }

    private void UpdateStats()
    {
        var visible = SelectedBand == "总览"
            ? Networks.ToArray()
            : Networks.Where(network => network.Band == SelectedBand).ToArray();
        VisibleNetworks.Clear();
        foreach (var network in visible)
        {
            VisibleNetworks.Add(network);
        }
        NetworkCount = visible.Length.ToString();
        ActiveChannelCount = visible
            .Select(network => $"{network.Band}:{network.Channel}")
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ToString();
        var current = SelectedBand == "总览"
            ? Networks.FirstOrDefault(network => network.IsCurrent)
            : visible.FirstOrDefault(network => network.IsCurrent);
        if (current is null)
        {
            OverlapCount = "--";
            CurrentSummary = SelectedBand == "总览" ? "当前：未识别已连接网络" : "当前：此频段未连接";
            ChartAccessibilitySummary = BuildChartAccessibilitySummary(visible, null);
            NotifyPresentationState();
            return;
        }

        OverlapCount = ChannelAnalysis.OverlapCount(_current, _rawNetworks).ToString();
        var bandPrefix = SelectedBand == "总览" ? $"{current.Band} · " : string.Empty;
        CurrentSummary = current.WidthMHz is { } width
            ? $"当前：{bandPrefix}Ch {current.Channel} / {width} MHz"
            : $"当前：{bandPrefix}Ch {current.Channel} / 频宽未知";
        ChartAccessibilitySummary = BuildChartAccessibilitySummary(visible, current);
        NotifyPresentationState();
    }

    private static string BuildChartAccessibilitySummary(
        IReadOnlyCollection<ChannelNetworkViewModel> visible,
        ChannelNetworkViewModel? current)
    {
        if (visible.Count == 0)
        {
            return "当前视图没有可见网络。";
        }

        var bandCounts = visible
            .GroupBy(network => network.Band)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} {group.Count()} 个")
            .ToArray();
        var currentText = current is null
            ? "当前连接未出现在此视图。"
            : $"当前网络 {current.Ssid}，{current.Band} 信道 {current.Channel}，" +
              (current.WidthMHz is { } width ? $"频宽 {width} MHz" : "频宽未知") +
              $"，RSSI {current.Rssi} dBm。";
        return $"共 {visible.Count} 个可见网络：{string.Join("，", bandCounts)}。{currentText}";
    }

    private void NotifyPresentationState()
    {
        OnPropertyChanged(nameof(HasVisibleNetworks));
        OnPropertyChanged(nameof(ShowChart));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void ApplySuggestions()
    {
        Suggestions.Clear();
        var glyphs = new Dictionary<ChannelSuggestionCategory, Icon>
        {
            [ChannelSuggestionCategory.Band] = Icon.WiFiSettings,
            [ChannelSuggestionCategory.Bandwidth] = Icon.ArrowAutofitWidth,
            [ChannelSuggestionCategory.Channel] = Icon.Channel
        };

        foreach (var suggestion in ChannelAnalysis.Suggestions(_current, _rawNetworks))
        {
            var tone = suggestion.Grade switch
            {
                HealthGrade.Good => StatusTone.Good,
                HealthGrade.Warning => StatusTone.Warning,
                HealthGrade.Critical => StatusTone.Critical,
                _ => StatusTone.Reference
            };
            var colors = UiPalette.For(tone);
            Suggestions.Add(new SuggestionCardViewModel(
                suggestion.Category.DisplayName(),
                glyphs[suggestion.Category],
                suggestion.Title,
                suggestion.Detail,
                suggestion.StatusLabel,
                colors.foreground,
                colors.background));
        }
    }
}
