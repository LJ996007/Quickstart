import AppKit

final class EntryEditorWindowController: NSWindowController {
    private let originalEntry: QuickEntry
    private let isNewEntry: Bool

    private let nameField = NSTextField()
    private let pathField = NSTextField()
    private let groupField = NSTextField()
    private let typePopUp = NSPopUpButton()
    private let browseButton = NSButton(title: "浏览…", target: nil, action: nil)
    private let pathHintLabel = NSTextField(labelWithString: "")

    private var completion: ((QuickEntry?) -> Void)?

    init(entry: QuickEntry, isNewEntry: Bool) {
        originalEntry = entry
        self.isNewEntry = isNewEntry

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 460, height: 250),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = isNewEntry ? "添加条目" : "编辑条目"
        window.center()
        window.isReleasedWhenClosed = false

        super.init(window: window)
        configureWindow()
        loadEntry(entry)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func beginSheet(for parentWindow: NSWindow, completion: @escaping (QuickEntry?) -> Void) {
        self.completion = completion
        guard let window else {
            completion(nil)
            return
        }

        parentWindow.beginSheet(window)
        DispatchQueue.main.async { [weak self] in
            self?.window?.makeFirstResponder(self?.nameField)
        }
    }

    private func configureWindow() {
        guard let contentView = window?.contentView else {
            return
        }

        let contentStack = NSStackView()
        contentStack.orientation = .vertical
        contentStack.spacing = 12
        contentStack.translatesAutoresizingMaskIntoConstraints = false

        nameField.placeholderString = "条目名称"
        pathField.placeholderString = "路径 / 网址 / 文本"
        groupField.placeholderString = "可选"

        typePopUp.addItems(withTitles: EntryType.allCases.map(\.displayName))
        typePopUp.target = self
        typePopUp.action = #selector(typeDidChange(_:))

        browseButton.bezelStyle = .rounded
        browseButton.target = self
        browseButton.action = #selector(browsePath(_:))

        pathHintLabel.textColor = .secondaryLabelColor
        pathHintLabel.lineBreakMode = .byWordWrapping
        pathHintLabel.maximumNumberOfLines = 2

        let buttonRow = NSStackView(views: [
            makeActionButton(title: "取消", action: #selector(cancelEditing(_:))),
            makeActionButton(title: "确定", action: #selector(saveEntry(_:)))
        ])
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 8
        buttonRow.alignment = .centerY
        buttonRow.detachesHiddenViews = true

        let pathInputStack = NSStackView(views: [pathField, browseButton])
        pathInputStack.orientation = .horizontal
        pathInputStack.spacing = 8
        pathInputStack.alignment = .centerY

        contentStack.addArrangedSubview(makeRow(label: "名称", control: nameField))
        contentStack.addArrangedSubview(makeRow(label: "内容", control: pathInputStack))
        contentStack.addArrangedSubview(pathHintLabel)
        contentStack.addArrangedSubview(makeRow(label: "类型", control: typePopUp))
        contentStack.addArrangedSubview(makeRow(label: "分组", control: groupField))
        contentStack.addArrangedSubview(buttonRow)

        contentView.addSubview(contentStack)

        NSLayoutConstraint.activate([
            contentStack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 20),
            contentStack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -20),
            contentStack.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 20),
            contentStack.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -20),
            pathField.widthAnchor.constraint(greaterThanOrEqualToConstant: 240)
        ])
    }

    private func loadEntry(_ entry: QuickEntry) {
        nameField.stringValue = entry.name
        pathField.stringValue = entry.path
        groupField.stringValue = entry.group
        typePopUp.selectItem(at: entry.type.rawValue)
        syncPathControls()
    }

    private func makeRow(label: String, control: NSView) -> NSView {
        let titleLabel = NSTextField(labelWithString: "\(label):")
        titleLabel.alignment = .right
        titleLabel.font = .systemFont(ofSize: NSFont.systemFontSize)
        titleLabel.translatesAutoresizingMaskIntoConstraints = false
        titleLabel.widthAnchor.constraint(equalToConstant: 54).isActive = true

        let row = NSStackView(views: [titleLabel, control])
        row.orientation = .horizontal
        row.spacing = 8
        row.alignment = .centerY
        return row
    }

    private func makeActionButton(title: String, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.bezelStyle = .rounded
        return button
    }

    @objc
    private func typeDidChange(_ sender: Any?) {
        syncPathControls()
    }

    @objc
    private func browsePath(_ sender: Any?) {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = selectedType == .folder
        panel.canChooseFiles = selectedType == .file
        panel.canCreateDirectories = false
        panel.allowsMultipleSelection = false
        panel.prompt = "选择"

        panel.beginSheetModal(for: window!) { [weak self] response in
            guard response == .OK, let url = panel.url else {
                return
            }

            self?.pathField.stringValue = url.path
            if self?.nameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == true {
                self?.nameField.stringValue = url.lastPathComponent
            }
        }
    }

    @objc
    private func cancelEditing(_ sender: Any?) {
        finish(with: nil)
    }

    @objc
    private func saveEntry(_ sender: Any?) {
        let rawValue = pathField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !rawValue.isEmpty else {
            presentValidationError(for: selectedType)
            return
        }

        var updated = originalEntry
        updated.type = selectedType
        updated.path = rawValue
        updated.group = groupField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)

        let rawName = nameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        if rawName.isEmpty {
            switch selectedType {
            case .url, .text:
                updated.name = String(rawValue.prefix(30))
            case .folder, .file:
                updated.name = URL(fileURLWithPath: rawValue).lastPathComponent
            }
        } else {
            updated.name = rawName
        }

        if isNewEntry {
            updated.sortOrder = originalEntry.sortOrder
        }

        finish(with: updated)
    }

    private var selectedType: EntryType {
        EntryType(rawValue: typePopUp.indexOfSelectedItem) ?? .folder
    }

    private func syncPathControls() {
        switch selectedType {
        case .folder:
            browseButton.isHidden = false
            pathHintLabel.stringValue = "选择要收藏的文件夹路径。"
            pathField.placeholderString = "文件夹路径"
        case .file:
            browseButton.isHidden = false
            pathHintLabel.stringValue = "选择要收藏的文件路径。"
            pathField.placeholderString = "文件路径"
        case .url:
            browseButton.isHidden = true
            pathHintLabel.stringValue = "请输入完整网址，如 https://example.com。"
            pathField.placeholderString = "https://"
        case .text:
            browseButton.isHidden = true
            pathHintLabel.stringValue = "保存一段常用文本，主界面按回车即可复制。"
            pathField.placeholderString = "文本内容"
        }
    }

    private func presentValidationError(for type: EntryType) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "内容不能为空"
        switch type {
        case .folder, .file:
            alert.informativeText = "请输入路径。"
        case .url:
            alert.informativeText = "请输入网址。"
        case .text:
            alert.informativeText = "请输入文本内容。"
        }

        if let window {
            alert.beginSheetModal(for: window)
        } else {
            alert.runModal()
        }
    }

    private func finish(with entry: QuickEntry?) {
        let completion = completion
        self.completion = nil
        if let window, let parent = window.sheetParent {
            parent.endSheet(window)
            window.orderOut(nil)
        } else {
            close()
        }
        completion?(entry)
    }
}
