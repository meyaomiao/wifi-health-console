using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using WiFiHealthConsole.App.Services;
using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.ViewModels;

public partial class RouterPageViewModel : ViewModelBase
{
    private readonly NetworkContextService _networkContextService;
    private readonly IWifiTelemetryProvider _wifiProvider;
    private readonly Func<CancellationToken, Task>? _runDiagnosis;

    [ObservableProperty]
    private string subtitle = "正在自动检测当前 Wi-Fi 网关";

    [ObservableProperty]
    private string gateway = "--";

    [ObservableProperty]
    private string gatewaySource = "等待 Windows 网卡信息";

    [ObservableProperty]
    private string gatewayExplanation = "工具不会使用固定厂商地址。";

    [ObservableProperty]
    private string detectionStatus = "正在检测";

    [ObservableProperty]
    private bool isDetecting;

    [ObservableProperty]
    private bool canOpenGateway;

    [ObservableProperty]
    private bool showScanEvidenceNotice;

    [ObservableProperty]
    private string scanEvidenceNotice = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool hasError;

    public RouterPageViewModel(
        NetworkContextService? networkContextService = null,
        IWifiTelemetryProvider? wifiProvider = null,
        Func<CancellationToken, Task>? runDiagnosis = null)
    {
        _networkContextService = networkContextService ?? new NetworkContextService();
        _wifiProvider = wifiProvider ?? WifiTelemetryProviderFactory.CreateDefault();
        _runDiagnosis = runDiagnosis;
    }

    public string Title => "路由管理";
    public ObservableCollection<SuggestionCardViewModel> Suggestions { get; } = [];

    partial void OnErrorMessageChanged(string? value) =>
        HasError = !string.IsNullOrWhiteSpace(value);

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsDetecting) return;
        IsDetecting = true;
        ErrorMessage = null;
        ShowScanEvidenceNotice = false;
        ScanEvidenceNotice = string.Empty;
        try
        {
            var contextTask = _networkContextService.GetCurrentAsync(cancellationToken);
            var currentTask = _wifiProvider.GetCurrentAsync(cancellationToken);
            var context = await contextTask;
            var current = await currentTask;

            ApplyGateway(context.WifiDefaultGateway, context);
            IReadOnlyList<NearbyNetwork> networks = [];
            try
            {
                networks = await _wifiProvider.ScanAsync(cancellationToken);
            }
            catch (WifiLocationPermissionException error)
            {
                ShowScanEvidenceNotice = true;
                ScanEvidenceNotice =
                    $"{error.Message} 当前频段与频宽建议仍基于已连接 Wi-Fi 的真实数据；信道建议会标记为证据不足。";
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                ShowScanEvidenceNotice = true;
                ScanEvidenceNotice =
                    $"附近网络扫描未完成：{error.Message} 当前频段与频宽建议仍保留，信道建议不会伪造扫描证据。";
            }

            ApplySuggestions(current, networks);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ErrorMessage = error.Message;
            ApplyGateway(null, null);
            ApplySuggestions(WifiSnapshot.Unavailable, []);
        }
        finally
        {
            IsDetecting = false;
        }
    }

    [RelayCommand]
    private async Task OpenRouterAsync(CancellationToken cancellationToken = default)
    {
        if (!await _networkContextService.OpenGatewayAsync(false, cancellationToken))
        {
            ErrorMessage = "未能打开 HTTP 管理页；路由器可能只支持 HTTPS、厂商 App 或自定义端口。";
        }
    }

    [RelayCommand]
    private async Task OpenRouterHttpsAsync(CancellationToken cancellationToken = default)
    {
        if (!await _networkContextService.OpenGatewayAsync(true, cancellationToken))
        {
            ErrorMessage = "未能打开 HTTPS 管理页。";
        }
    }

    [RelayCommand]
    private Task RunRetestAsync(CancellationToken cancellationToken = default) =>
        _runDiagnosis?.Invoke(cancellationToken) ?? Task.CompletedTask;

    private void ApplyGateway(IPAddress? address, NetworkContextSnapshot? context)
    {
        CanOpenGateway = address is not null;
        Gateway = address?.ToString() ?? "--";
        Subtitle = address is null ? "未检测到当前 Wi-Fi 网关" : $"当前 Wi-Fi 网关：{address}";
        GatewaySource = context?.WifiInterfaceName is { Length: > 0 } interfaceName
            ? $"Windows Wi-Fi 网卡自动检测 · 接口：{interfaceName}"
            : "Windows 未报告活动 Wi-Fi 网关";
        DetectionStatus = address is null ? "未检测到网关" : "已检测到本地网关";
        GatewayExplanation = address is null
            ? "未找到当前 Wi-Fi 网卡的默认网关，不会回退到固定的厂商 IP。请确认已连接 Wi-Fi 后重新检测。"
            : "多数家用路由器会在默认网关提供管理页。地址由当前网络自动检测，不依赖小米、华为、TP-Link 等厂商预设。";
    }

    private void ApplySuggestions(WifiSnapshot current, IReadOnlyList<NearbyNetwork> networks)
    {
        Suggestions.Clear();
        var glyphs = new Dictionary<ChannelSuggestionCategory, Icon>
        {
            [ChannelSuggestionCategory.Band] = Icon.WiFiSettings,
            [ChannelSuggestionCategory.Bandwidth] = Icon.ArrowAutofitWidth,
            [ChannelSuggestionCategory.Channel] = Icon.Channel
        };
        foreach (var suggestion in ChannelAnalysis.Suggestions(current, networks))
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
