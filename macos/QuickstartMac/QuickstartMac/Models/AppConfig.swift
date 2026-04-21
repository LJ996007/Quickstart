import Foundation

struct AppConfig: Codable, Equatable {
    var entries: [QuickEntry] = []
    var groupLastUsedAt: [String: Date] = [:]
    var totalCommanderPath: String = ""
    var directoryOpusPath: String = ""
    var defaultOpenWith: OpenWith = .totalCommander
    var startWithWindows: Bool = false
    var shellMenuEnabled: Bool = false
    var hotKey: String = ""

    enum CodingKeys: String, CodingKey {
        case entries
        case groupLastUsedAt
        case totalCommanderPath
        case directoryOpusPath
        case defaultOpenWith
        case startWithWindows
        case shellMenuEnabled
        case hotKey
    }

    init() {}

    init(
        entries: [QuickEntry] = [],
        groupLastUsedAt: [String: Date] = [:],
        totalCommanderPath: String = "",
        directoryOpusPath: String = "",
        defaultOpenWith: OpenWith = .totalCommander,
        startWithWindows: Bool = false,
        shellMenuEnabled: Bool = false,
        hotKey: String = ""
    ) {
        self.entries = entries
        self.groupLastUsedAt = groupLastUsedAt
        self.totalCommanderPath = totalCommanderPath
        self.directoryOpusPath = directoryOpusPath
        self.defaultOpenWith = defaultOpenWith
        self.startWithWindows = startWithWindows
        self.shellMenuEnabled = shellMenuEnabled
        self.hotKey = hotKey
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        entries = try container.decodeIfPresent([QuickEntry].self, forKey: .entries) ?? []
        groupLastUsedAt = try container.decodeIfPresent([String: Date].self, forKey: .groupLastUsedAt) ?? [:]
        totalCommanderPath = try container.decodeIfPresent(String.self, forKey: .totalCommanderPath) ?? ""
        directoryOpusPath = try container.decodeIfPresent(String.self, forKey: .directoryOpusPath) ?? ""
        defaultOpenWith = try container.decodeIfPresent(OpenWith.self, forKey: .defaultOpenWith) ?? .totalCommander
        startWithWindows = try container.decodeIfPresent(Bool.self, forKey: .startWithWindows) ?? false
        shellMenuEnabled = try container.decodeIfPresent(Bool.self, forKey: .shellMenuEnabled) ?? false
        hotKey = try container.decodeIfPresent(String.self, forKey: .hotKey) ?? ""
    }
}
