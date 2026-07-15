namespace WiFiHealthConsole.Core;

public enum DiagnosticLayer
{
    Wireless,
    LocalNetwork,
    Internet,
    ProxyVpn,
}

public static class DiagnosticLayerExtensions
{
    public static string DisplayName(this DiagnosticLayer layer) => layer switch
    {
        DiagnosticLayer.Wireless => "无线空口",
        DiagnosticLayer.LocalNetwork => "局域网",
        DiagnosticLayer.Internet => "宽带出口",
        DiagnosticLayer.ProxyVpn => "VPN / 代理",
        _ => "未检测",
    };
}

public sealed record DiagnosticMetric
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Value { get; init; }

    public HealthGrade Grade { get; init; }

    public required string StatusLabel { get; init; }

    public required string Interpretation { get; init; }

    public required string Impact { get; init; }

    public required string Standard { get; init; }

    public MetricAssessment Assessment => new(Grade, StatusLabel, Interpretation, Standard);

    public static DiagnosticMetric FromAssessment(
        string id,
        string title,
        string value,
        MetricAssessment assessment,
        string impact) => new()
        {
            Id = id,
            Title = title,
            Value = value,
            Grade = assessment.Grade,
            StatusLabel = assessment.StatusLabel,
            Interpretation = assessment.Interpretation,
            Impact = impact,
            Standard = assessment.Standard,
        };
}

public sealed record PingStatistics
{
    public required string Host { get; init; }

    public int Sent { get; init; }

    public int Received { get; init; }

    public double PacketLossPercent { get; init; }

    public double? MinimumMs { get; init; }

    public double? AverageMs { get; init; }

    public double? MaximumMs { get; init; }

    public double? JitterMs { get; init; }
}

public sealed record EndpointTiming
{
    public bool Succeeded { get; init; }

    public double? Milliseconds { get; init; }

    public required string Detail { get; init; }

    public EvidenceSource Source { get; init; } = EvidenceSource.OperatingSystem;
}

public sealed record LayerResult
{
    public DiagnosticLayer Layer { get; init; }

    public HealthGrade Grade { get; init; }

    public string StatusLabel { get; init; }

    public string Conclusion { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = [];

    public IReadOnlyList<DiagnosticMetric> Metrics { get; init; } = [];

    public string Action { get; init; }

    public LayerResult()
    {
        StatusLabel = HealthStatusLabels.Unavailable;
        Conclusion = "尚未检测";
        Action = "完成检测后查看建议。";
    }

    public LayerResult(
        DiagnosticLayer layer,
        HealthGrade grade,
        string statusLabel,
        string conclusion,
        IReadOnlyList<string>? evidence,
        IReadOnlyList<DiagnosticMetric>? metrics,
        string action)
    {
        if (!HealthStandards.IsStatusLabelCompatible(statusLabel, grade))
        {
            throw new ArgumentException("分层结果状态文字与健康等级不一致。", nameof(statusLabel));
        }

        Layer = layer;
        Grade = grade;
        StatusLabel = statusLabel;
        Conclusion = conclusion;
        Evidence = evidence ?? [];
        Metrics = metrics ?? [];
        Action = action;
    }
}

public sealed record DiagnosticReport
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public string? Gateway { get; init; }

    public string? InterfaceName { get; init; }

    public required string BaselineDescription { get; init; }

    public IReadOnlyList<WifiSnapshot> WirelessSamples { get; init; } = [];

    public PingStatistics? GatewayPing { get; init; }

    public PingStatistics? ExternalPing { get; init; }

    public EndpointTiming? Dns { get; init; }

    public EndpointTiming? Https { get; init; }

    public IReadOnlyList<LayerResult> Layers { get; init; } = [];

    public HealthGrade OverallGrade
    {
        get
        {
            if (Layers.Count == 0 || Layers.Any(layer => layer.Grade == HealthGrade.Critical))
            {
                return Layers.Count == 0 ? HealthGrade.Unavailable : HealthGrade.Critical;
            }

            if (Layers.Any(layer => layer.Grade == HealthGrade.Warning))
            {
                return HealthGrade.Warning;
            }

            if (Layers.Any(layer => layer.Grade == HealthGrade.Unavailable))
            {
                return HealthGrade.Unavailable;
            }

            return HealthGrade.Good;
        }
    }

    public string OverallStatusLabel =>
        OverallGrade == HealthGrade.Unavailable && Layers.Any(layer => layer.Grade == HealthGrade.Good)
            ? HealthStatusLabels.Partial
            : OverallGrade.Label();

    public bool HasUnavailableLayer => Layers.Any(layer => layer.Grade == HealthGrade.Unavailable);
}
