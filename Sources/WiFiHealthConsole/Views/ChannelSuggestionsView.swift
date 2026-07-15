import SwiftUI

struct ChannelSuggestionsView: View {
    let title: String
    let suggestions: [ChannelSuggestion]

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title)
                .font(.headline)

            VStack(spacing: 12) {
                ForEach(ChannelSuggestionCategory.allCases) { category in
                    let categorySuggestions = suggestions.filter { $0.category == category }
                    if !categorySuggestions.isEmpty {
                        suggestionGroup(category: category, suggestions: categorySuggestions)
                    }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func suggestionGroup(
        category: ChannelSuggestionCategory,
        suggestions: [ChannelSuggestion]
    ) -> some View {
        let grade = HealthStandards.worst(suggestions.map(\.grade))
        return VStack(alignment: .leading, spacing: 11) {
            Label(category.rawValue, systemImage: category.systemImage)
                .font(.callout.weight(.semibold))

            ForEach(suggestions) { suggestion in
                VStack(alignment: .leading, spacing: 5) {
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Text(suggestion.title)
                            .font(.callout.weight(.semibold))
                        Spacer()
                        StatusBadge(
                            grade: suggestion.grade,
                            label: suggestion.statusLabel,
                            systemImage: suggestion.badgeSystemImage
                        )
                    }

                    Text(suggestion.detail)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }

                if suggestion.id != suggestions.last?.id {
                    Divider()
                }
            }
        }
        .padding(AppLayout.cardPadding)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .appCardStyle(
            tint: grade.color,
            tintOpacity: 0.02,
            borderColor: grade.color.opacity(0.14)
        )
    }
}
