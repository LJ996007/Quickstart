import Foundation

struct AddURLRequest: Equatable {
    let url: URL
    let title: String
}

enum QuickstartURLHandlerError: LocalizedError {
    case invalidScheme
    case invalidTarget
    case missingURLParameter
    case unsupportedURL(String)

    var errorDescription: String? {
        switch self {
        case .invalidScheme:
            return "链接不是 quickstart:// 协议。"
        case .invalidTarget:
            return "当前仅支持 quickstart://add-url。"
        case .missingURLParameter:
            return "缺少 url 参数。"
        case .unsupportedURL(let value):
            return "不支持的网址：\(value)"
        }
    }
}

final class QuickstartURLHandler {
    static let scheme = "quickstart"
    static let bookmarklet = "javascript:(()=>{const u=encodeURIComponent(location.href);const t=encodeURIComponent(document.title||location.hostname);location.href='quickstart://add-url?url='+u+'&title='+t;})();"

    func isProtocolURL(_ url: URL) -> Bool {
        url.scheme?.caseInsensitiveCompare(Self.scheme) == .orderedSame
    }

    func parseAddURLRequest(from url: URL) throws -> AddURLRequest {
        guard isProtocolURL(url) else {
            throw QuickstartURLHandlerError.invalidScheme
        }

        let target = {
            if let host = url.host, !host.isEmpty {
                return host
            }

            return url.path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        }()

        guard target.caseInsensitiveCompare("add-url") == .orderedSame else {
            throw QuickstartURLHandlerError.invalidTarget
        }

        guard let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let rawURL = components.queryItems?.first(where: { $0.name.caseInsensitiveCompare("url") == .orderedSame })?.value,
              !rawURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw QuickstartURLHandlerError.missingURLParameter
        }

        guard let parsedURL = URL(string: rawURL),
              let scheme = parsedURL.scheme?.lowercased(),
              scheme == "http" || scheme == "https" else {
            throw QuickstartURLHandlerError.unsupportedURL(rawURL)
        }

        let rawTitle = components.queryItems?.first(where: { $0.name.caseInsensitiveCompare("title") == .orderedSame })?.value
        let title = rawTitle?.trimmingCharacters(in: .whitespacesAndNewlines)
        let resolvedTitle = (title?.isEmpty == false ? title! : (parsedURL.host ?? parsedURL.absoluteString))

        return AddURLRequest(url: parsedURL, title: resolvedTitle)
    }
}
