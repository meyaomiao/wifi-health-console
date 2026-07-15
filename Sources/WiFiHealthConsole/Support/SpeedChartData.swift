import Foundation

enum SpeedChartSeries: String, CaseIterable, Hashable {
    case download = "下载"
    case upload = "上传"

    var phase: SpeedTestPhase {
        switch self {
        case .download: .download
        case .upload: .upload
        }
    }
}

struct SpeedChartPoint: Identifiable, Equatable {
    struct ID: Hashable {
        var sampleID: UUID
        var series: SpeedChartSeries
    }

    var id: ID
    var elapsedSeconds: Double
    var mbps: Double
    var series: SpeedChartSeries
}

enum SpeedChartData {
    static func points(
        from samples: [SpeedTestSample],
        series: SpeedChartSeries
    ) -> [SpeedChartPoint] {
        samples
            .filter { $0.phase == series.phase }
            .map { sample in
                SpeedChartPoint(
                    id: .init(sampleID: sample.id, series: series),
                    elapsedSeconds: sample.elapsedSeconds,
                    mbps: sample.mbps,
                    series: series
                )
            }
    }
}
