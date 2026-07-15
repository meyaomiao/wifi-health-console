using System.Globalization;

namespace WiFiHealthConsole.Core;

/// <summary>
/// Shared product thresholds. These boundaries intentionally match the macOS edition so the
/// same measurement cannot be called normal on one platform and abnormal on another.
/// </summary>
public static class HealthStandards
{
    public const string RssiStandard = "> -55 dBm 优秀；-55～-67 dBm 正常；-68～-75 dBm 注意；< -75 dBm 严重。";
    public const string NoiseStandard = "噪声没有脱离设备、驱动和频段的统一故障阈值；仅展示真实采集值，不做估算。";
    public const string SnrStandard = "≥ 40 dB 优秀；30～39 dB 正常；20～29 dB 注意；< 20 dB 严重。";
    public const string CcaStandard = "≤ 50% 正常；> 50%～80% 注意；> 80% 严重。需从路由器或驱动侧真实读取。";
    public const string GatewayLatencyStandard = "≤ 10 ms 优秀；> 10～30 ms 正常；> 30～100 ms 注意；> 100 ms 严重。";
    public const string GatewayJitterStandard = "≤ 10 ms 正常；> 10～30 ms 注意；> 30 ms 严重。";
    public const string GatewayLossStandard = "≤ 1% 正常；> 1%～5% 注意；> 5% 严重。";
    public const string InternetLatencyStandard = "≤ 80 ms 正常；> 80～150 ms 注意；> 150 ms 严重。";
    public const string DnsStandard = "≤ 100 ms 正常；> 100～300 ms 注意；> 300 ms 或解析失败为严重。";
    public const string HttpsStandard = "≤ 800 ms 正常；> 800～2000 ms 注意；> 2000 ms 或请求失败为严重。";
    public const string PublicIcmpLossStandard = "≤ 10% 为正常参考；> 10% 需要注意。公网可能降低 ICMP 优先级，因此不单独判为严重。";
    public const string DownloadStandard = "≥ 100 Mbps 优秀；25～99.9 Mbps 正常；10～24.9 Mbps 注意；< 10 Mbps 严重。";
    public const string UploadStandard = "≥ 20 Mbps 优秀；10～19.9 Mbps 正常；5～9.9 Mbps 注意；< 5 Mbps 严重。";
    public const string IdleLatencyStandard = "≤ 40 ms 正常；> 40～100 ms 注意；> 100 ms 严重。";
    public const string ResponsivenessStandard = "≥ 600 RPM 优秀；200～599 RPM 注意；< 200 RPM 严重。RPM 越高越好。";
    public const string ChannelWidthStandard = "2.4 GHz 通常优先 20 MHz；5/6 GHz 的 80 MHz 较均衡；160 MHz 仅在频谱干净时更合适。频宽本身不单独判故障。";

    public static MetricAssessment Rssi(Observed<int> observation) =>
        observation.TryGetValue(out var value)
            ? Rssi(value)
            : UnavailableObservation(observation, "RSSI", RssiStandard);

    public static MetricAssessment Rssi(int? value)
    {
        if (value is null)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得 RSSI。", RssiStandard);
        }

        if (value > -55)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Excellent, $"当前 {value} dBm，信号优秀，覆盖不是首要问题。", RssiStandard);
        }

        if (value >= -67)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"当前 {value} dBm，处于正常使用范围。", RssiStandard);
        }

        if (value >= -75)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {value} dBm，信号偏弱，速率和稳定性余量已经下降。", RssiStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"当前 {value} dBm，信号很弱，容易降速、重传或断续。", RssiStandard);
    }

    public static MetricAssessment Noise(Observed<int> observation)
    {
        if (!observation.TryGetValue(out var value))
        {
            return UnavailableObservation(observation, "噪声", NoiseStandard);
        }

        return Make(
            HealthGrade.Unavailable,
            HealthStatusLabels.Reference,
            $"当前噪声 {value} dBm。该值只作为真实观测展示，不脱离 RSSI、SNR 和频段单独判故障。",
            NoiseStandard);
    }

    public static MetricAssessment Snr(Observed<int> observation) =>
        observation.TryGetValue(out var value)
            ? Snr(value)
            : UnavailableObservation(observation, "SNR", SnrStandard);

    public static MetricAssessment Snr(int? value)
    {
        if (value is null)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得 SNR；未使用 RSSI 或质量百分比估算。", SnrStandard);
        }

        if (value >= 40)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Excellent, $"当前 {value} dB，信号明显高于噪声，抗干扰余量充足。", SnrStandard);
        }

        if (value >= 30)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"当前 {value} dB，信噪比处于正常范围。", SnrStandard);
        }

        if (value >= 20)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {value} dB，抗干扰余量不足，繁忙时更容易降速或重传。", SnrStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"当前 {value} dB，信号与噪声过于接近，连接容易不稳定。", SnrStandard);
    }

    public static MetricAssessment Cca(Observed<double> observation) =>
        observation.TryGetValue(out var value)
            ? Cca(value)
            : UnavailableObservation(observation, "CCA", CcaStandard);

    public static MetricAssessment Cca(double? value)
    {
        if (!IsFinite(value))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得 CCA；Windows 公共 WLAN API 不提供该指标，本次未用估算值参与判定。", CcaStandard);
        }

        if (value > 80)
        {
            return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"当前 {Number(value)}%，空口绝大多数时间繁忙，属于严重拥塞。", CcaStandard);
        }

        if (value > 50)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {Number(value)}%，空口竞争明显，可能增加等待和重传。", CcaStandard);
        }

        return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"当前 {Number(value)}%，空口占用在正常范围。", CcaStandard);
    }

    public static MetricAssessment GatewayLatency(double? value) => Latency(
        value,
        excellentUpper: 10,
        normalUpper: 30,
        warningUpper: 100,
        subject: "电脑到路由器",
        standard: GatewayLatencyStandard);

    public static MetricAssessment GatewayJitter(double? value)
    {
        if (!IsFinite(value))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得网关抖动。", GatewayJitterStandard);
        }

        if (value <= 10)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"当前 {Number(value)} ms，局域网延迟波动正常。", GatewayJitterStandard);
        }

        if (value <= 30)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {Number(value)} ms，波动偏大，实时业务可能受影响。", GatewayJitterStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"当前 {Number(value)} ms，波动严重，通话、游戏和远程控制容易卡顿。", GatewayJitterStandard);
    }

    public static MetricAssessment GatewayLoss(double? value)
    {
        if (!IsFinite(value))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得网关丢包率。", GatewayLossStandard);
        }

        if (value <= 1)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"当前 {Number(value)}%，家庭内部链路处于正常范围。", GatewayLossStandard);
        }

        if (value <= 5)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {Number(value)}%，家庭内部链路已出现异常丢包。", GatewayLossStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"当前 {Number(value)}%，家庭内部链路丢包严重。", GatewayLossStandard);
    }

    public static MetricAssessment InternetLatency(double? value) => Latency(
        value,
        excellentUpper: null,
        normalUpper: 80,
        warningUpper: 150,
        subject: "公网响应",
        standard: InternetLatencyStandard);

    public static MetricAssessment PublicIcmpLoss(double? value)
    {
        if (!IsFinite(value))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得公网 ICMP 丢包率。", PublicIcmpLossStandard);
        }

        if (value <= 10)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"当前 {Number(value)}%，处于参考范围；仍需与 DNS、HTTPS 一起判断。", PublicIcmpLossStandard);
        }

        return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {Number(value)}%，需要关注，但公网目标可能降低 ICMP 优先级，不能据此单独判定断网。", PublicIcmpLossStandard);
    }

    public static MetricAssessment Dns(EndpointTiming? timing)
    {
        if (timing is null)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未执行 DNS 测试。", DnsStandard);
        }

        if (!timing.Succeeded)
        {
            return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"DNS 解析失败：{timing.Detail}", DnsStandard);
        }

        return Latency(timing.Milliseconds, null, 100, 300, "DNS 解析", DnsStandard);
    }

    public static MetricAssessment Https(EndpointTiming? timing)
    {
        if (timing is null)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未执行 HTTPS 测试。", HttpsStandard);
        }

        if (!timing.Succeeded)
        {
            return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"HTTPS 请求失败：{timing.Detail}", HttpsStandard);
        }

        return Latency(timing.Milliseconds, null, 800, 2_000, "HTTPS 建连与响应", HttpsStandard);
    }

    public static MetricAssessment Download(double? mbps)
    {
        if (!IsFinite(mbps))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得下载速度。", DownloadStandard);
        }

        if (mbps >= 100)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Excellent, "当前下载速度适合多设备 4K、云盘和大型游戏下载。", DownloadStandard);
        }

        if (mbps >= 25)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, "当前下载速度可满足网页、高清视频和单路 4K。", DownloadStandard);
        }

        if (mbps >= 10)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, "当前下载速度偏低，大型下载和高码率场景会明显等待。", DownloadStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, "当前下载速度很低，网页、高清视频和大文件下载都可能明显缓慢。", DownloadStandard);
    }

    public static MetricAssessment Upload(double? mbps)
    {
        if (!IsFinite(mbps))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得上传速度。", UploadStandard);
        }

        if (mbps >= 20)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Excellent, "当前上传速度适合视频会议、直播和云盘同步。", UploadStandard);
        }

        if (mbps >= 10)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, "当前上传速度可满足视频会议和日常云同步。", UploadStandard);
        }

        if (mbps >= 5)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, "当前上传速度偏低，视频会议余量有限，大文件上传会较慢。", UploadStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, "当前上传速度很低，视频会议、云同步和发送大文件容易受影响。", UploadStandard);
    }

    public static MetricAssessment IdleLatency(double? value) => Latency(
        value,
        excellentUpper: null,
        normalUpper: 40,
        warningUpper: 100,
        subject: "空闲延迟",
        standard: IdleLatencyStandard);

    public static MetricAssessment Responsiveness(double? value, string subject)
    {
        if (!IsFinite(value))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, $"未取得{subject}响应能力。", ResponsivenessStandard);
        }

        if (value >= 600)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Excellent, $"{subject} {value:0} RPM，负载下仍能保持较快响应。", ResponsivenessStandard);
        }

        if (value >= 200)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"{subject} {value:0} RPM，负载下响应一般，可能出现排队延迟。", ResponsivenessStandard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"{subject} {value:0} RPM，负载下响应很差，容易出现明显卡顿。", ResponsivenessStandard);
    }

    public static MetricAssessment ChannelWidth(Observed<int> width, WiFiBand band) =>
        width.TryGetValue(out var value)
            ? ChannelWidth(value, band)
            : UnavailableObservation(width, "信道频宽", ChannelWidthStandard);

    public static MetricAssessment ChannelWidth(int? width, WiFiBand band)
    {
        if (width is null or <= 0)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, "未取得信道频宽。", ChannelWidthStandard);
        }

        if (band == WiFiBand.Band2_4GHz && width > 20)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"当前 {width} MHz；2.4 GHz 上超过 20 MHz 会覆盖更多相邻信道，通常更容易产生重叠。", ChannelWidthStandard);
        }

        if (width >= 160)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Reference, $"当前 {width} MHz，峰值高但占用范围很宽；是否需要降到 80 MHz 要结合信道重叠、CCA 和重传。", ChannelWidthStandard);
        }

        if (width == 80 && band is WiFiBand.Band5GHz or WiFiBand.Band6GHz)
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Reference, "当前 80 MHz，在 5/6 GHz 上通常兼顾速度与频谱占用。", ChannelWidthStandard);
        }

        return Make(HealthGrade.Unavailable, HealthStatusLabels.Reference, $"当前 {width} MHz；频宽需要结合频段、附近网络和实际吞吐判断。", ChannelWidthStandard);
    }

    public static MetricAssessment Reference(bool available, string interpretation, string standard) =>
        available
            ? Make(HealthGrade.Unavailable, HealthStatusLabels.Reference, interpretation, standard)
            : Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, interpretation, standard);

    public static MetricAssessment PathState(bool active, string detail, string standard) =>
        active
            ? Make(HealthGrade.Warning, HealthStatusLabels.Warning, detail, standard)
            : Make(HealthGrade.Good, HealthStatusLabels.Normal, detail, standard);

    public static HealthGrade Worst(params HealthGrade[] grades) => Worst((IEnumerable<HealthGrade>)grades);

    public static HealthGrade Worst(IEnumerable<HealthGrade> grades)
    {
        var materialized = grades.ToArray();
        if (materialized.Contains(HealthGrade.Critical))
        {
            return HealthGrade.Critical;
        }

        if (materialized.Contains(HealthGrade.Warning))
        {
            return HealthGrade.Warning;
        }

        if (materialized.Contains(HealthGrade.Good))
        {
            return HealthGrade.Good;
        }

        return HealthGrade.Unavailable;
    }

    public static HealthGrade Worst(IEnumerable<MetricAssessment> assessments) =>
        Worst(assessments.Select(assessment => assessment.Grade));

    public static string SummaryStatusLabel(IEnumerable<MetricAssessment> assessments)
    {
        var decisive = assessments
            .Where(assessment => assessment.StatusLabel != HealthStatusLabels.Reference)
            .Where(assessment => assessment.Grade != HealthGrade.Unavailable)
            .ToArray();

        return Worst(decisive) switch
        {
            HealthGrade.Good => decisive.Length == 0 || decisive.Any(assessment => assessment.StatusLabel != HealthStatusLabels.Excellent)
                ? HealthStatusLabels.Normal
                : HealthStatusLabels.Excellent,
            HealthGrade.Warning => HealthStatusLabels.Warning,
            HealthGrade.Critical => HealthStatusLabels.Critical,
            _ => HealthStatusLabels.Unavailable,
        };
    }

    public static bool IsStatusLabelCompatible(string label, HealthGrade grade) => grade switch
    {
        HealthGrade.Good => label is HealthStatusLabels.Excellent or HealthStatusLabels.Normal,
        HealthGrade.Warning => label == HealthStatusLabels.Warning,
        HealthGrade.Critical => label == HealthStatusLabels.Critical,
        HealthGrade.Unavailable => label is HealthStatusLabels.Unavailable or HealthStatusLabels.Reference or HealthStatusLabels.Partial,
        _ => false,
    };

    private static MetricAssessment Latency(
        double? value,
        double? excellentUpper,
        double normalUpper,
        double warningUpper,
        string subject,
        string standard)
    {
        if (!IsFinite(value))
        {
            return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, $"未取得{subject}时间。", standard);
        }

        if (excellentUpper is not null && value <= excellentUpper)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Excellent, $"{subject} {Number(value)} ms，响应优秀。", standard);
        }

        if (value <= normalUpper)
        {
            return Make(HealthGrade.Good, HealthStatusLabels.Normal, $"{subject} {Number(value)} ms，处于正常范围。", standard);
        }

        if (value <= warningUpper)
        {
            return Make(HealthGrade.Warning, HealthStatusLabels.Warning, $"{subject} {Number(value)} ms，已经偏高。", standard);
        }

        return Make(HealthGrade.Critical, HealthStatusLabels.Critical, $"{subject} {Number(value)} ms，明显过高。", standard);
    }

    private static MetricAssessment UnavailableObservation<T>(
        Observed<T> observation,
        string metric,
        string standard)
    {
        var reason = string.IsNullOrWhiteSpace(observation.Detail)
            ? $"未取得 {metric}。"
            : observation.Detail!;

        return Make(HealthGrade.Unavailable, HealthStatusLabels.Unavailable, reason, standard);
    }

    private static MetricAssessment Make(
        HealthGrade grade,
        string statusLabel,
        string interpretation,
        string standard) =>
        new(grade, statusLabel, interpretation, standard);

    private static bool IsFinite(double? value) => value is not null && double.IsFinite(value.Value);

    private static string Number(double? value) => value!.Value.ToString("0.0", CultureInfo.InvariantCulture);
}
