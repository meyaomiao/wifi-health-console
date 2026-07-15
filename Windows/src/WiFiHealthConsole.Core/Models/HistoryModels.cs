namespace WiFiHealthConsole.Core;

public enum HistoryMarker
{
    None,
    Before,
    After,
}

public static class HistoryMarkerExtensions
{
    public static string DisplayName(this HistoryMarker marker) => marker switch
    {
        HistoryMarker.Before => "变更前",
        HistoryMarker.After => "变更后",
        _ => "未标记",
    };
}

public enum HistoryGradeScope
{
    Wireless,
    FullDiagnosis,
}

public sealed record HistorySample
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; init; }

    public HistoryMarker Marker { get; init; }

    public required WifiSnapshot Snapshot { get; init; }

    public double? GatewayAverageMs { get; init; }

    public double? GatewayJitterMs { get; init; }

    public double? GatewayLossPercent { get; init; }

    public double? InternetAverageMs { get; init; }

    public HealthGrade OverallGrade { get; init; }

    public string? OverallStatusLabel { get; init; }

    public HistoryGradeScope? GradeScope { get; init; }

    public string? ChangeNote { get; init; }
}
