import CoreLocation
import XCTest
@testable import WiFiHealthConsole

final class LocationPermissionPolicyTests: XCTestCase {
    func testNotDeterminedRequestsAuthorization() {
        XCTAssertEqual(
            LocationPermissionNextAction.resolve(for: .notDetermined),
            .requestAuthorization
        )
    }

    func testDeniedAndRestrictedOpenSystemSettings() {
        XCTAssertEqual(
            LocationPermissionNextAction.resolve(for: .denied),
            .openSystemSettings
        )
        XCTAssertEqual(
            LocationPermissionNextAction.resolve(for: .restricted),
            .openSystemSettings
        )
    }

    func testAuthorizedStatusesRefresh() {
        XCTAssertEqual(
            LocationPermissionNextAction.resolve(for: .authorizedAlways),
            .refresh
        )
    }

    func testSystemSettingsActionUsesExplicitButtonTitle() {
        XCTAssertEqual(
            LocationPermissionNextAction.openSystemSettings.buttonTitle,
            "打开系统设置"
        )
    }
}
