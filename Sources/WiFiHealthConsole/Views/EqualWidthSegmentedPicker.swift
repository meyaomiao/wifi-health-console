import AppKit
import SwiftUI

struct EqualWidthSegmentedPicker<Option: Hashable>: NSViewRepresentable {
    let options: [Option]
    @Binding var selection: Option
    let title: (Option) -> String
    let isEnabled: Bool
    let accessibilityLabel: String

    func makeCoordinator() -> Coordinator {
        Coordinator(selection: $selection, options: options)
    }

    func makeNSView(context: Context) -> NSSegmentedControl {
        let control = NSSegmentedControl(
            labels: options.map(title),
            trackingMode: .selectOne,
            target: context.coordinator,
            action: #selector(Coordinator.selectionChanged(_:))
        )
        control.segmentStyle = .automatic
        control.segmentDistribution = .fillEqually
        control.controlSize = .regular
        control.setAccessibilityLabel(accessibilityLabel)
        update(control, coordinator: context.coordinator)
        return control
    }

    func updateNSView(_ nsView: NSSegmentedControl, context: Context) {
        context.coordinator.selection = $selection
        context.coordinator.options = options

        if nsView.segmentCount != options.count {
            nsView.segmentCount = options.count
        }
        for (index, option) in options.enumerated() {
            nsView.setLabel(title(option), forSegment: index)
            nsView.setEnabled(isEnabled, forSegment: index)
        }
        nsView.segmentDistribution = .fillEqually
        nsView.isEnabled = isEnabled
        nsView.setAccessibilityLabel(accessibilityLabel)
        update(nsView, coordinator: context.coordinator)
    }

    private func update(_ control: NSSegmentedControl, coordinator: Coordinator) {
        control.selectedSegment = options.firstIndex(of: selection) ?? -1
        coordinator.selection = $selection
        coordinator.options = options
    }

    final class Coordinator: NSObject {
        var selection: Binding<Option>
        var options: [Option]

        init(selection: Binding<Option>, options: [Option]) {
            self.selection = selection
            self.options = options
        }

        @objc func selectionChanged(_ sender: NSSegmentedControl) {
            guard options.indices.contains(sender.selectedSegment) else { return }
            selection.wrappedValue = options[sender.selectedSegment]
        }
    }
}
