import Foundation
import XCTest
@testable import QuickstartMac

final class ConfigCompatibilityTests: XCTestCase {
    func testDecodesWindowsFixtureWithoutDroppingWindowsFields() throws {
        let data = try fixtureData()
        let config = try DotNetJSONCoding.makeDecoder().decode(AppConfig.self, from: data)

        XCTAssertEqual(config.entries.count, 3)
        XCTAssertEqual(config.entries.first?.name, "项目目录")
        XCTAssertEqual(config.totalCommanderPath, #"C:\Tools\TOTALCMD64.EXE"#)
        XCTAssertEqual(config.directoryOpusPath, #"C:\Program Files\GPSoftware\Directory Opus\dopus.exe"#)
        XCTAssertEqual(config.defaultOpenWith, .explorer)
        XCTAssertTrue(config.startWithWindows)
        XCTAssertTrue(config.shellMenuEnabled)
        XCTAssertEqual(config.hotKey, "Ctrl+Shift+Space")
        XCTAssertEqual(config.groupLastUsedAt["工作"], try DotNetJSONCoding.makeDecoder().decode(Date.self, from: Data(#""2026-04-21T11:50:00.7654321+08:00""#.utf8)))
    }

    func testReencodesContractUsingExpectedTopLevelKeys() throws {
        let config = try DotNetJSONCoding.makeDecoder().decode(AppConfig.self, from: fixtureData())
        let data = try DotNetJSONCoding.makeEncoder().encode(config)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]

        XCTAssertNotNil(json?["entries"])
        XCTAssertNotNil(json?["groupLastUsedAt"])
        XCTAssertEqual(json?["totalCommanderPath"] as? String, #"C:\Tools\TOTALCMD64.EXE"#)
        XCTAssertEqual(json?["directoryOpusPath"] as? String, #"C:\Program Files\GPSoftware\Directory Opus\dopus.exe"#)
        XCTAssertEqual(json?["defaultOpenWith"] as? Int, 1)
        XCTAssertEqual(json?["hotKey"] as? String, "Ctrl+Shift+Space")
    }

    private func fixtureData() throws -> Data {
        try Data(contentsOf: fixtureURL())
    }

    private func fixtureURL() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent("fixtures/windows-config-sample.json")
    }
}
