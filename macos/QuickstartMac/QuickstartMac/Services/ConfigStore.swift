import Foundation

final class ConfigStore {
    private let fileManager: FileManager
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder
    private let lock = NSLock()

    private(set) var config: AppConfig
    let configDirectoryURL: URL
    let configURL: URL
    let backupURL: URL

    init(fileManager: FileManager = .default, baseDirectoryURL: URL? = nil) {
        self.fileManager = fileManager
        encoder = DotNetJSONCoding.makeEncoder()
        decoder = DotNetJSONCoding.makeDecoder()
        config = AppConfig()

        let directory = baseDirectoryURL ?? fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Quickstart", isDirectory: true)

        configDirectoryURL = directory
        configURL = directory.appendingPathComponent("config.json")
        backupURL = directory.appendingPathComponent("config.json.bak")
    }

    func currentConfig() -> AppConfig {
        lock.withLock { config }
    }

    func load() throws {
        try lock.withLock {
            if !fileManager.fileExists(atPath: configURL.path) {
                try fileManager.createDirectory(at: configDirectoryURL, withIntermediateDirectories: true, attributes: nil)
                config = AppConfig()
                try saveUnlocked()
                return
            }

            do {
                config = try decodeConfig(at: configURL)
            } catch {
                if fileManager.fileExists(atPath: backupURL.path) {
                    config = try decodeConfig(at: backupURL)
                } else {
                    config = AppConfig()
                }
            }
        }
    }

    @discardableResult
    func addEntry(_ entry: QuickEntry) throws -> Bool {
        try lock.withLock {
            if config.entries.contains(where: { $0.path.caseInsensitiveCompare(entry.path) == .orderedSame }) {
                return false
            }

            var newEntry = entry
            newEntry.sortOrder = config.entries.count
            config.entries.append(newEntry)
            try saveUnlocked()
            return true
        }
    }

    func updateEntry(_ entry: QuickEntry) throws {
        try lock.withLock {
            guard let index = config.entries.firstIndex(where: { $0.id == entry.id }) else {
                return
            }

            config.entries[index] = entry
            try saveUnlocked()
        }
    }

    func removeEntry(id: String) throws {
        try lock.withLock {
            config.entries.removeAll { $0.id == id }
            try saveUnlocked()
        }
    }

    func touchEntry(id: String) throws {
        try lock.withLock {
            guard let index = config.entries.firstIndex(where: { $0.id == id }) else {
                return
            }

            config.entries[index].lastUsedAt = Date()
            try saveUnlocked()
        }
    }

    func touchGroup(_ group: String) throws {
        let normalized = group.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else {
            return
        }

        try lock.withLock {
            config.groupLastUsedAt[normalized] = Date()
            try saveUnlocked()
        }
    }

    private func decodeConfig(at url: URL) throws -> AppConfig {
        let data = try Data(contentsOf: url)
        return try decoder.decode(AppConfig.self, from: data)
    }

    private func saveUnlocked() throws {
        try fileManager.createDirectory(at: configDirectoryURL, withIntermediateDirectories: true, attributes: nil)

        let data = try encoder.encode(config)
        let tempURL = configDirectoryURL.appendingPathComponent("config.json.tmp")
        try data.write(to: tempURL, options: .atomic)

        if fileManager.fileExists(atPath: configURL.path) {
            if fileManager.fileExists(atPath: backupURL.path) {
                try? fileManager.removeItem(at: backupURL)
            }

            try fileManager.copyItem(at: configURL, to: backupURL)

            _ = try fileManager.replaceItemAt(configURL, withItemAt: tempURL, backupItemName: nil, options: [], resultingItemURL: nil)
        } else {
            try fileManager.moveItem(at: tempURL, to: configURL)
        }
    }
}

private extension NSLock {
    func withLock<T>(_ block: () throws -> T) rethrows -> T {
        lock()
        defer { unlock() }
        return try block()
    }
}
