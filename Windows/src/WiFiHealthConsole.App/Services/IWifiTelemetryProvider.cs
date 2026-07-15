using WiFiHealthConsole.Core;

namespace WiFiHealthConsole.App.Services;

public interface IWifiTelemetryProvider
{
    bool IsSupported { get; }

    string ProviderName { get; }

    Task<WifiSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NearbyNetwork>> ScanAsync(CancellationToken cancellationToken = default);

    Task<bool> OpenLocationPrivacySettingsAsync(CancellationToken cancellationToken = default);
}

public static class WifiTelemetryProviderFactory
{
    public static IWifiTelemetryProvider CreateDefault() => OperatingSystem.IsWindows()
        ? new WindowsWifiTelemetryProvider()
        : new MockWifiTelemetryProvider();
}

public class WifiTelemetryException : Exception
{
    public WifiTelemetryException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class WifiLocationPermissionException : WifiTelemetryException
{
    public const string LocationSettingsUri = "ms-settings:privacy-location";

    public WifiLocationPermissionException(string operation, Exception? innerException = null)
        : base(
            $"{operation} 被 Windows 拒绝。请在‘设置 > 隐私和安全性 > 位置’中允许桌面应用访问位置，然后重试。",
            innerException)
    {
        Operation = operation;
    }

    public string Operation { get; }

    public string SettingsUri => LocationSettingsUri;
}
