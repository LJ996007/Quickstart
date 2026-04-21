import Foundation

enum EntryTab: Int, CaseIterable {
    case files = 0
    case urls = 1
    case texts = 2

    var title: String {
        switch self {
        case .files:
            return "文件"
        case .urls:
            return "网页"
        case .texts:
            return "文本"
        }
    }

    var searchPlaceholder: String {
        switch self {
        case .files:
            return "搜索文件夹或文件... (拼音首字母也可)"
        case .urls:
            return "搜索网页..."
        case .texts:
            return "搜索文本..."
        }
    }

    var defaultEntryType: EntryType {
        switch self {
        case .files:
            return .folder
        case .urls:
            return .url
        case .texts:
            return .text
        }
    }
}

final class EntrySearchService {
    static let allGroupsLabel = "全部"

    func entries(for tab: EntryTab, in config: AppConfig) -> [QuickEntry] {
        switch tab {
        case .files:
            return config.entries.filter { $0.type == .folder || $0.type == .file }
        case .urls:
            return config.entries.filter { $0.type == .url }
        case .texts:
            return config.entries.filter { $0.type == .text }
        }
    }

    func filteredEntries(for tab: EntryTab, activeGroup: String, query: String, in config: AppConfig) -> [QuickEntry] {
        let typeEntries = entries(for: tab, in: config)
        let normalizedActiveGroup = normalizeGroupName(activeGroup)

        let grouped = normalizedActiveGroup.isEmpty || normalizedActiveGroup == Self.allGroupsLabel
            ? typeEntries
            : typeEntries.filter { normalizeGroupName($0.group).caseInsensitiveCompare(normalizedActiveGroup) == .orderedSame }

        let trimmedQuery = query.trimmingCharacters(in: .whitespacesAndNewlines)
        let filtered = trimmedQuery.isEmpty
            ? grouped
            : grouped.filter {
                matchesPinyin($0.name, query: trimmedQuery) || matchesPinyin($0.path, query: trimmedQuery)
            }

        return filtered.sorted { lhs, rhs in
            if lhs.sortOrder != rhs.sortOrder {
                return lhs.sortOrder < rhs.sortOrder
            }

            return lhs.name.localizedCaseInsensitiveCompare(rhs.name) == .orderedAscending
        }
    }

    func orderedGroupNames(for tab: EntryTab, in config: AppConfig) -> [String] {
        entries(for: tab, in: config)
            .map { normalizeGroupName($0.group) }
            .filter { !$0.isEmpty }
            .reduce(into: [String]()) { groups, group in
                if !groups.contains(where: { $0.caseInsensitiveCompare(group) == .orderedSame }) {
                    groups.append(group)
                }
            }
            .sorted { lhs, rhs in
                let lhsDate = lastUsedAt(for: lhs, in: config.groupLastUsedAt)
                let rhsDate = lastUsedAt(for: rhs, in: config.groupLastUsedAt)
                if lhsDate != rhsDate {
                    return lhsDate > rhsDate
                }

                return lhs.localizedCaseInsensitiveCompare(rhs) == .orderedAscending
            }
    }

    func normalizeGroupName(_ group: String?) -> String {
        group?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    func matchesPinyin(_ text: String, query: String) -> Bool {
        let trimmedQuery = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedQuery.isEmpty else {
            return true
        }

        if text.localizedCaseInsensitiveContains(trimmedQuery) {
            return true
        }

        let latin = latinRepresentation(for: text)
        if latin.localizedCaseInsensitiveContains(trimmedQuery) {
            return true
        }

        let initials = pinyinInitials(for: text)
        return initials.localizedCaseInsensitiveContains(trimmedQuery)
    }

    func pinyinInitials(for text: String) -> String {
        transliteratedString(for: text)
            .split(whereSeparator: { !$0.isLetter && !$0.isNumber })
            .compactMap(\.first)
            .map { String($0).uppercased() }
            .joined()
    }

    func latinRepresentation(for text: String) -> String {
        transliteratedString(for: text)
            .replacingOccurrences(of: " ", with: "")
    }

    private func transliteratedString(for text: String) -> String {
        let mutable = NSMutableString(string: text) as CFMutableString
        CFStringTransform(mutable, nil, kCFStringTransformToLatin, false)
        CFStringTransform(mutable, nil, kCFStringTransformStripCombiningMarks, false)

        return (mutable as String)
            .replacingOccurrences(of: "'", with: "")
    }

    private func lastUsedAt(for group: String, in map: [String: Date]) -> Date {
        if let direct = map[group] {
            return direct
        }

        for (key, value) in map where key.caseInsensitiveCompare(group) == .orderedSame {
            return value
        }

        return DotNetJSONCoding.dotNetMinDate
    }
}
