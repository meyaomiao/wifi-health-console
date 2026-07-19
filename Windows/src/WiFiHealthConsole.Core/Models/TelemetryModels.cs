using System.Diagnostics.CodeAnalysis;

namespace WiFiHealthConsole.Core;

/// <summary>
/// Coarse health level used by every metric and summary card.
/// A more specific Chinese status label is carried by <see cref="MetricAssessment"/>.
/// </summary>
public enum HealthGrade
{
    Good,
    Warning,
    Critical,
    Unavailable,
}

public static class HealthGradeExtensions
{
    public static string Label(this HealthGrade grade) => grade switch
    {
        HealthGrade.Good => "正常",
        HealthGrade.Warning => "注意",
        HealthGrade.Critical => "严重",
        HealthGrade.Unavailable => "未检测",
        _ => "未检测",
    };
}

/// <summary>
/// Describes why an observation can or cannot participate in diagnosis.
/// Unknown is deliberately the zero value so a default-initialized observation is never
/// accidentally treated as real telemetry.
/// </summary>
public enum MetricAvailability
{
    Unknown = 0,
    Available,
    Unavailable,
    NotSupported,
    PermissionDenied,
    Failed,
}

/// <summary>
/// Identifies where evidence came from. Heuristic evidence is suitable for advice such as
/// channel ranking, but must not be presented as measured radio telemetry.
/// </summary>
public enum EvidenceSource
{
    None = 0,
    NativeWlanApi,
    IpHelperApi,
    OperatingSystem,
    IcmpProbe,
    DnsProbe,
    HttpsProbe,
    SpeedTestEndpoint,
    RouterTelemetry,
    UserInput,
    HistoricalRecord,
    Derived,
    Heuristic,
    Mock,
}

/// <summary>
/// A value together with its provenance and availability. Consumers must use
/// <see cref="HasValue"/> or <see cref="TryGetValue"/> rather than assuming a default numeric
/// value is a measurement.
/// </summary>
public sealed record Observed<T>
{
    public T? Value { get; init; }

    public MetricAvailability Availability { get; init; }

    public EvidenceSource Source { get; init; }

    public string? Detail { get; init; }

    public bool HasValue => Availability == MetricAvailability.Available;

    public Observed(
        T? value,
        MetricAvailability availability,
        EvidenceSource source,
        string? detail = null)
    {
        if (availability == MetricAvailability.Available && value is null)
        {
            throw new ArgumentNullException(nameof(value), "可用观测必须包含值。");
        }

        Value = value;
        Availability = availability;
        Source = source;
        Detail = detail;
    }

    public static Observed<T> Available(
        T value,
        EvidenceSource source,
        string? detail = null) =>
        new(value, MetricAvailability.Available, source, detail);

    public static Observed<T> Unavailable(
        MetricAvailability availability = MetricAvailability.Unavailable,
        EvidenceSource source = EvidenceSource.None,
        string? detail = null)
    {
        if (availability == MetricAvailability.Available)
        {
            throw new ArgumentException("不可用观测不能标记为 Available。", nameof(availability));
        }

        return new(default, availability, source, detail);
    }

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        if (HasValue)
        {
            value = Value!;
            return true;
        }

        value = default;
        return false;
    }
}

public static class HealthStatusLabels
{
    public const string Excellent = "优秀";
    public const string Normal = "正常";
    public const string Warning = "注意";
    public const string Critical = "严重";
    public const string Reference = "参考";
    public const string NotSupported = "不支持";
    public const string Unavailable = "未检测";
    public const string Partial = "部分完成";
}

public sealed record MetricAssessment
{
    public HealthGrade Grade { get; init; }

    public string StatusLabel { get; init; }

    public string Interpretation { get; init; }

    public string Standard { get; init; }

    public MetricAssessment(
        HealthGrade grade,
        string statusLabel,
        string interpretation,
        string standard)
    {
        if (!HealthStandards.IsStatusLabelCompatible(statusLabel, grade))
        {
            throw new ArgumentException("状态文字与健康等级不一致。", nameof(statusLabel));
        }

        Grade = grade;
        StatusLabel = statusLabel;
        Interpretation = interpretation;
        Standard = standard;
    }
}
