import AppKit

final class EntryActionService {
    private let workspace: NSWorkspace
    private let pasteboard: NSPasteboard
    private let fileManager: FileManager

    init(workspace: NSWorkspace = .shared, pasteboard: NSPasteboard = .general, fileManager: FileManager = .default) {
        self.workspace = workspace
        self.pasteboard = pasteboard
        self.fileManager = fileManager
    }

    func open(_ entry: QuickEntry) -> Bool {
        switch entry.type {
        case .folder, .file:
            let url = URL(fileURLWithPath: entry.path, isDirectory: entry.type == .folder)
            return workspace.open(url)
        case .url:
            guard let url = URL(string: entry.path) else {
                return false
            }
            return workspace.open(url)
        case .text:
            return copy(entry)
        }
    }

    func revealInFinder(_ entry: QuickEntry) -> Bool {
        guard entry.type == .folder || entry.type == .file else {
            return false
        }

        let url = URL(fileURLWithPath: entry.path)
        workspace.activateFileViewerSelecting([url])
        return true
    }

    func copy(_ entry: QuickEntry) -> Bool {
        switch entry.type {
        case .url, .text:
            return copy(string: entry.path)
        case .folder, .file:
            return copy(string: entry.path)
        }
    }

    func icon(for entry: QuickEntry) -> NSImage {
        let image: NSImage

        switch entry.type {
        case .folder:
            image = fileManager.fileExists(atPath: entry.path)
                ? workspace.icon(forFile: entry.path)
                : (NSImage(systemSymbolName: "folder", accessibilityDescription: nil) ?? NSImage())
        case .file:
            if fileManager.fileExists(atPath: entry.path) {
                image = workspace.icon(forFile: entry.path)
            } else {
                let fileType = URL(fileURLWithPath: entry.path).pathExtension
                image = workspace.icon(forFileType: fileType.isEmpty ? "txt" : fileType)
            }
        case .url:
            image = NSImage(systemSymbolName: "globe", accessibilityDescription: nil) ?? NSImage()
        case .text:
            image = NSImage(systemSymbolName: "text.alignleft", accessibilityDescription: nil) ?? NSImage()
        }

        image.size = NSSize(width: 18, height: 18)
        return image
    }

    private func copy(string: String) -> Bool {
        pasteboard.clearContents()
        return pasteboard.setString(string, forType: .string)
    }
}
