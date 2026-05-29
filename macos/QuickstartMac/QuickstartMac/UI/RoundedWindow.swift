import AppKit

final class RoundedWindowContentView: NSView {
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.cornerRadius = 14
        layer?.masksToBounds = true
        layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }
}

class RoundedWindow: NSWindow {
    init(contentRect: NSRect, title: String) {
        super.init(
            contentRect: contentRect,
            styleMask: [.borderless, .resizable],
            backing: .buffered,
            defer: false
        )

        self.title = title
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        isRestorable = false
        isMovableByWindowBackground = true
        contentView = RoundedWindowContentView(frame: contentRect)
    }

    override var canBecomeKey: Bool {
        true
    }

    override var canBecomeMain: Bool {
        true
    }
}

extension NSButton {
    static func flatAction(title: String, target: AnyObject?, action: Selector?) -> NSButton {
        let button = NSButton(title: title, target: target, action: action)
        button.isBordered = false
        button.bezelStyle = .regularSquare
        button.wantsLayer = true
        button.layer?.cornerRadius = 7
        button.layer?.backgroundColor = NSColor.controlAccentColor.withAlphaComponent(0.12).cgColor
        button.contentTintColor = .controlAccentColor
        return button
    }

    static func flatClose(target: AnyObject?, action: Selector?) -> NSButton {
        let button = NSButton(title: "×", target: target, action: action)
        button.isBordered = false
        button.font = .systemFont(ofSize: 17, weight: .semibold)
        button.wantsLayer = true
        button.layer?.cornerRadius = 9
        button.layer?.backgroundColor = NSColor.separatorColor.withAlphaComponent(0.22).cgColor
        button.contentTintColor = .secondaryLabelColor
        return button
    }
}
