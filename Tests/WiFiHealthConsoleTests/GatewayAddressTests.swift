import XCTest
@testable import WiFiHealthConsole

final class GatewayAddressTests: XCTestCase {
    func testRecognizesCommonLocalGatewayRanges() {
        XCTAssertTrue(GatewayAddress.isPrivateOrLocal("192.168.31.1"))
        XCTAssertTrue(GatewayAddress.isPrivateOrLocal("10.0.0.1"))
        XCTAssertTrue(GatewayAddress.isPrivateOrLocal("172.20.1.1"))
        XCTAssertTrue(GatewayAddress.isPrivateOrLocal("100.64.0.1"))
        XCTAssertFalse(GatewayAddress.isPrivateOrLocal("203.0.113.1"))
        XCTAssertFalse(GatewayAddress.isPrivateOrLocal("1.1.1.1"))
    }

    func testBuildsManagementURLFromDetectedGateway() {
        XCTAssertEqual(GatewayAddress.managementURL(for: "192.168.1.1")?.absoluteString, "http://192.168.1.1")
        XCTAssertNil(GatewayAddress.managementURL(for: nil))
    }
}
