import Foundation
import XCTest
@testable import QuickstartMac

final class QuickstartURLHandlerTests: XCTestCase {
    private let handler = QuickstartURLHandler()

    func testParsesAddURLRequestWithExplicitTitle() throws {
        let url = URL(string: "quickstart://add-url?url=https%3A%2F%2Fexample.com%2Fdocs&title=%E6%96%87%E6%A1%A3")!
        let request = try handler.parseAddURLRequest(from: url)

        XCTAssertEqual(request.url.absoluteString, "https://example.com/docs")
        XCTAssertEqual(request.title, "文档")
    }

    func testFallsBackToHostWhenTitleMissing() throws {
        let url = URL(string: "quickstart://add-url?url=https%3A%2F%2Fexample.com%2Fdocs")!
        let request = try handler.parseAddURLRequest(from: url)

        XCTAssertEqual(request.title, "example.com")
    }

    func testRejectsUnsupportedSchemes() {
        let url = URL(string: "quickstart://add-url?url=file%3A%2F%2Ftmp%2Fdemo.txt")!

        XCTAssertThrowsError(try handler.parseAddURLRequest(from: url))
    }
}
