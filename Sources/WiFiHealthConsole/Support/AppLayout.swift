import SwiftUI

enum AppLayout {
    static let pageMaxWidth: CGFloat = 1_120
    static let pagePadding: CGFloat = 20
    static let sectionSpacing: CGFloat = 18

    static let cardCornerRadius: CGFloat = 8
    static let cardPadding: CGFloat = 14

    static let metricTileMinimumWidth: CGFloat = 160
    static let diagnosticMetricMinimumWidth: CGFloat = 360
    static let speedMetricMinimumWidth: CGFloat = 380
    static let speedSupportMinimumWidth: CGFloat = 270
    static let spectrumLegendMinimumWidth: CGFloat = 230
}

struct PageContainer<Content: View>: View {
    private let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: AppLayout.sectionSpacing) {
                content
            }
            .padding(AppLayout.pagePadding)
            .frame(maxWidth: AppLayout.pageMaxWidth, alignment: .leading)
            .frame(maxWidth: .infinity, alignment: .topLeading)
        }
    }
}

private struct AppCardStyleModifier: ViewModifier {
    let tint: Color?
    let tintOpacity: Double
    let borderColor: Color?

    func body(content: Content) -> some View {
        content
            .background {
                if let tint {
                    RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                        .fill(tint.opacity(tintOpacity))
                } else {
                    RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                        .fill(.background.secondary)
                }
            }
            .overlay {
                if let borderColor {
                    RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                        .stroke(borderColor, lineWidth: 1)
                } else {
                    RoundedRectangle(cornerRadius: AppLayout.cardCornerRadius, style: .continuous)
                        .stroke(.separator.opacity(0.45), lineWidth: 1)
                }
            }
    }
}

extension View {
    func appCardStyle(
        tint: Color? = nil,
        tintOpacity: Double = 0.07,
        borderColor: Color? = nil
    ) -> some View {
        modifier(AppCardStyleModifier(
            tint: tint,
            tintOpacity: tintOpacity,
            borderColor: borderColor
        ))
    }
}
