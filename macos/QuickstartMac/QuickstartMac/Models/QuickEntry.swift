import Foundation

enum EntryType: Int, Codable, CaseIterable {
    case folder = 0
    case file = 1
    case url = 2
    case text = 3

    var displayName: String {
        switch self {
        case .folder:
            return "文件夹"
        case .file:
            return "文件"
        case .url:
            return "网页"
        case .text:
            return "文本"
        }
    }
}

enum OpenWith: Int, Codable {
    case totalCommander = 0
    case explorer = 1
    case directoryOpus = 2
    case lastUsed = 3
}

struct QuickEntry: Codable, Equatable, Identifiable {
    var id: String = QuickEntry.makeID()
    var name: String = ""
    var path: String = ""
    var type: EntryType = .folder
    var group: String = ""
    var sortOrder: Int = 0
    var addedAt: Date = Date()
    var lastUsedAt: Date = DotNetJSONCoding.dotNetMinDate

    enum CodingKeys: String, CodingKey {
        case id
        case name
        case path
        case type
        case group
        case sortOrder
        case addedAt
        case lastUsedAt
    }

    init() {}

    init(
        id: String = QuickEntry.makeID(),
        name: String = "",
        path: String = "",
        type: EntryType = .folder,
        group: String = "",
        sortOrder: Int = 0,
        addedAt: Date = Date(),
        lastUsedAt: Date = DotNetJSONCoding.dotNetMinDate
    ) {
        self.id = id
        self.name = name
        self.path = path
        self.type = type
        self.group = group
        self.sortOrder = sortOrder
        self.addedAt = addedAt
        self.lastUsedAt = lastUsedAt
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decodeIfPresent(String.self, forKey: .id) ?? QuickEntry.makeID()
        name = try container.decodeIfPresent(String.self, forKey: .name) ?? ""
        path = try container.decodeIfPresent(String.self, forKey: .path) ?? ""
        type = try container.decodeIfPresent(EntryType.self, forKey: .type) ?? .folder
        group = try container.decodeIfPresent(String.self, forKey: .group) ?? ""
        sortOrder = try container.decodeIfPresent(Int.self, forKey: .sortOrder) ?? 0
        addedAt = try container.decodeIfPresent(Date.self, forKey: .addedAt) ?? Date()
        lastUsedAt = try container.decodeIfPresent(Date.self, forKey: .lastUsedAt) ?? DotNetJSONCoding.dotNetMinDate
    }

    static func makeID() -> String {
        UUID().uuidString
            .replacingOccurrences(of: "-", with: "")
            .prefix(8)
            .lowercased()
    }
}
