import AppKit

final class SettingsWindowController: NSWindowController {
    private let configStore: ConfigStore
    private let bookmarklet: String

    init(configStore: ConfigStore, bookmarklet: String) {
        self.configStore = configStore
        self.bookmarklet = bookmarklet

        let window = RoundedWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 280), title: "Quickstart 设置")
        window.center()

        super.init(window: window)
        configureWindow()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func configureWindow() {
        guard let contentView = window?.contentView else {
            return
        }

        let titleLabel = NSTextField(labelWithString: "Quickstart 设置")
        titleLabel.font = .systemFont(ofSize: 18, weight: .semibold)
        let closeButton = NSButton.flatClose(target: self, action: #selector(closeSettings))

        let titleRow = NSStackView(views: [titleLabel, NSView(), closeButton])
        titleRow.orientation = .horizontal
        titleRow.alignment = .centerY
        titleRow.spacing = 8
        closeButton.translatesAutoresizingMaskIntoConstraints = false
        closeButton.widthAnchor.constraint(equalToConstant: 24).isActive = true
        closeButton.heightAnchor.constraint(equalToConstant: 24).isActive = true

        let configPathLabel = NSTextField(wrappingLabelWithString: configStore.configURL.path)
        configPathLabel.textColor = .secondaryLabelColor

        let tipsLabel = NSTextField(wrappingLabelWithString: "当前版本会保留 Windows 配置字段，但不会在 macOS 界面里暴露 Total Commander / Directory Opus / 右键菜单等 Windows 专属设置。")
        tipsLabel.textColor = .secondaryLabelColor

        let openConfigButton = NSButton.flatAction(title: "打开配置目录", target: self, action: #selector(openConfigDirectory))

        let copyBookmarkletButton = NSButton.flatAction(title: "复制一键添加书签", target: self, action: #selector(copyBookmarklet))

        let doneButton = NSButton.flatAction(title: "关闭", target: self, action: #selector(closeSettings))

        let buttonRow = NSStackView(views: [openConfigButton, copyBookmarkletButton, doneButton])
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 8
        buttonRow.alignment = .centerY

        let stack = NSStackView(views: [
            titleRow,
            NSTextField(labelWithString: "配置文件:"),
            configPathLabel,
            tipsLabel,
            buttonRow
        ])
        stack.orientation = .vertical
        stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false

        contentView.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 20),
            stack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -20),
            stack.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 20),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: contentView.bottomAnchor, constant: -20)
        ])
    }

    @objc
    private func openConfigDirectory() {
        NSWorkspace.shared.activateFileViewerSelecting([configStore.configURL])
    }

    @objc
    private func copyBookmarklet() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(bookmarklet, forType: .string)
    }

    @objc
    private func closeSettings() {
        close()
    }
}
