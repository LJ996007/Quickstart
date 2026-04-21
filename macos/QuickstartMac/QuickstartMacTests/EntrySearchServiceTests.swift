import Foundation
import XCTest
@testable import QuickstartMac

final class EntrySearchServiceTests: XCTestCase {
    private let service = EntrySearchService()

    func testChineseAndPinyinQueriesReturnExpectedEntries() {
        let config = makeConfig()

        let chinese = service.filteredEntries(for: .files, activeGroup: EntrySearchService.allGroupsLabel, query: "项目", in: config)
        XCTAssertEqual(chinese.map(\.name), ["项目目录"])

        let initials = service.filteredEntries(for: .files, activeGroup: EntrySearchService.allGroupsLabel, query: "xmml", in: config)
        XCTAssertEqual(initials.map(\.name), ["项目目录"])

        let englishPath = service.filteredEntries(for: .files, activeGroup: EntrySearchService.allGroupsLabel, query: "quickstart", in: config)
        XCTAssertEqual(englishPath.map(\.name), ["项目目录"])
    }

    func testGroupOrderingHonorsLastUsedDescending() {
        let groups = service.orderedGroupNames(for: .files, in: makeConfig())
        XCTAssertEqual(groups, ["工作", "灵感"])
    }

    func testTabFilteringAndGroupFilteringStayIndependent() {
        let config = makeConfig()

        let urls = service.filteredEntries(for: .urls, activeGroup: EntrySearchService.allGroupsLabel, query: "", in: config)
        XCTAssertEqual(urls.count, 1)
        XCTAssertEqual(urls.first?.type, .url)

        let workFiles = service.filteredEntries(for: .files, activeGroup: "工作", query: "", in: config)
        XCTAssertEqual(workFiles.map(\.name), ["项目目录"])
    }

    private func makeConfig() -> AppConfig {
        AppConfig(
            entries: [
                QuickEntry(
                    id: "a1b2c3d4",
                    name: "项目目录",
                    path: "/Users/markl/Work/Quickstart",
                    type: .folder,
                    group: "工作",
                    sortOrder: 0
                ),
                QuickEntry(
                    id: "1122aabb",
                    name: "灵感便签",
                    path: "把发布流程再精简一点",
                    type: .text,
                    group: "灵感",
                    sortOrder: 1
                ),
                QuickEntry(
                    id: "e5f6a7b8",
                    name: "Quickstart 官网",
                    path: "https://quickstart.example.com",
                    type: .url,
                    group: "工作",
                    sortOrder: 2
                )
            ],
            groupLastUsedAt: [
                "工作": Date(timeIntervalSince1970: 1_765_080_600),
                "灵感": Date(timeIntervalSince1970: 1_765_070_600)
            ],
            totalCommanderPath: "",
            directoryOpusPath: "",
            defaultOpenWith: .explorer,
            startWithWindows: false,
            shellMenuEnabled: false,
            hotKey: ""
        )
    }
}
