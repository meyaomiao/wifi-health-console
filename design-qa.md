# Windows UI Design QA

- Source visual truth: `Windows/design-reference/macos-overview.png`, `macos-diagnosis.png`, `macos-speed-test.png`, `macos-channel-radar.png`, `macos-history.png`, `macos-router.png`
- Implementation screenshots: `Windows/artifacts/ui-parity-current/final/`
- Latest focused evidence: `01-overview-partial-verified.png`, `03-speed-test-updated-verified.png`, `03b-speed-test-upload-verified.png`, `04-channel-radar-overview-verified.png`, and `06-router-verified.png`
- Viewport: 1160 × 760 application window, captured from the macOS-hosted Avalonia preview
- Theme/state: dark theme; connected Overview, completed SpeedTest, populated three-band ChannelRadar, History comparison, detected Router gateway, and Diagnosis empty state

## Full-view comparison evidence

- Window composition now uses one 54 px extended title bar, a 190 px sidebar, 20 px page padding, 18 px section spacing, 8 px card radius, and 14 px card padding, matching the macOS layout tokens.
- All six Windows pages preserve the same quiet desktop-tool hierarchy as the macOS source: navigation, page title, conclusion or primary controls, evidence cards, and next action.
- Fluent system icons render consistently across navigation, metrics, layers, suggestions, and toolbar controls.

## Focused comparison evidence

- Speed test options use the macOS 72 px label column and 520 px two-segment control. Both rows align and each selected background fills exactly half the control.
- Download and upload are separate blue and green charts. Each chart includes Mbps ticks, seconds, phase status, Mbps, MB/s, and an explicit axis caption.
- Channel Radar opens on a single-screen 2.4/5/6 GHz overview, while retaining per-band detail tabs. It includes RSSI labels, channel ticks, threshold lines, channel-width curves, current-network highlighting, collision-aware labels, and a legend containing channel, MHz, and dBm.
- History undetected and waiting states use the neutral reference color. The chart legend explicitly identifies the blue RSSI line and before/after markers.
- Router suggestions use the same status tokens as their text conclusions and are based on the automatically detected gateway and real scan evidence when available.
- Overview now reports neutral `部分完成` when RSSI is known but Windows cannot provide SNR/CCA. Missing evidence can no longer produce a green overall result.
- Speed phase badges use accent blue for process state; green/orange/red remain reserved for health assessments.

## Comparison history

### Iteration 1 — blocked

Earlier findings:

- P1: native title bar plus an internal 54 px toolbar produced a double title bar.
- P1: speed-test segmented controls did not fill equal-width columns.
- P1: speed charts had no axes or units.
- P1: channel radar lacked readable axes, network labels, width/RSSI legend details, and current-network identification.
- P1: History displayed green “正常” for missing values and treated unmarked points as before-change markers.
- P1: Speed and History hard-coded statuses instead of using shared health standards.
- P1: Diagnosis discarded metric interpretation, impact, and standard fields.

Fixes made:

- Added a single extended title bar and aligned the window/sidebar/page tokens to the macOS source.
- Rebuilt segmented controls with fixed, equal columns and shared stretch styling.
- Added fixed-time speed axes, readable throughput scales, phase badges, and unit captions.
- Added complete radar axes, labels, stable colors, current-network emphasis, and detailed legend cards.
- Routed Speed, History, Diagnosis, Overview, Router, and Radar statuses through shared Core assessments.
- Added diagnostic metric cards and wired previously inert actions.
- Replaced unstable text glyphs with Fluent system icons.

Post-fix evidence:

- Six final implementation captures and six side-by-side comparisons in `Windows/artifacts/ui-qa-final/`.
- Release build completed with 0 warnings and 0 errors.
- 99 tests passed: 90 Core and 9 App tests.

### Iteration 2 — passed

Additional findings:

- Overview overstated incomplete Windows telemetry as a green normal result.
- Channel Radar still required switching bands and did not provide the requested all-band overview.
- Current-path speed tests compared a descriptive route label with a real adapter name, causing a false interface mismatch warning.
- Speed duration copy promised a hard 40/60 second maximum even though latency preflight can take longer on a failing network.
- Loaded responsiveness cards had no production measurement path.
- History could compare reversed timestamps or different SSIDs as if they were one router change.

Fixes made:

- Added partial-evidence aggregation for Overview and SpeedTest summaries.
- Added a stacked three-band spectrum overview, detail tabs, label collision avoidance, stable legend columns, and screen-reader summaries.
- Split path description from real adapter identities and compare only two real adapter IDs.
- Changed timing copy to throughput-stage duration and called out latency preflight separately.
- Added real HTTPS round-trip probes during each loaded phase; insufficient samples remain unavailable and are never estimated.
- Validated History timestamp order and SSID identity before calculating deltas, and clear recovered storage errors.

Post-fix evidence:

- Release build completed with 0 warnings and 0 errors.
- 121 tests passed: 92 Core and 29 App tests.
- `dotnet format --verify-no-changes` and `git diff --check` passed.

## Findings

No actionable P0, P1, or P2 mismatch remains.

Accepted differences:

- Overview metric cards are taller because the Windows version keeps both explanation and standard visible inside each card.
- Windows title-bar controls and typography follow platform rendering while preserving the macOS page structure.
- `macos-history.png` is byte-identical to the Diagnosis reference, so History was checked against shared layout tokens and its own semantic correctness rather than a valid page-specific source image.
- Local visual captures run through Avalonia on macOS. The Windows CI validates build, tests, install, launch, upgrade, and uninstall, but final Windows font/DPI rendering still requires a Windows screenshot pass.

## Remaining release constraints

- Windows artifacts are currently unsigned and will show an unknown-publisher warning until an Authenticode certificate is configured.
- ARM64 packaging is checked in CI, but there is no ARM64 Windows device available for a native launch test.

## Final result

final result: passed
