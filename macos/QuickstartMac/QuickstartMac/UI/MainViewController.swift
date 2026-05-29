import AppKit

final class MainViewController: NSViewController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSDraggingDestination {
    private let configStore: ConfigStore
    private let searchService: EntrySearchService
    private let actionService: EntryActionService

    var onShowSettings: (() -> Void)?
    var onDismissRequested: (() -> Void)?

    private let searchField = NSSearchField()
    private let groupTableView = NSTableView()
    private let entryTableView = ActionTableView()
    private let countLabel = NSTextField(labelWithString: "0 项")
    private let openButton = NSButton(title: "打开", target: nil, action: nil)
    private let secondaryActionButton = NSButton(title: "在 Finder 中显示", target: nil, action: nil)
    private let addButton = NSButton(title: "添加", target: nil, action: nil)
    private let editButton = NSButton(title: "编辑", target: nil, action: nil)
    private let deleteButton = NSButton(title: "删除", target: nil, action: nil)
    private let settingsButton = NSButton(title: "设置", target: nil, action: nil)
    private let closeButton = NSButton.flatClose(target: nil, action: nil)
    private var tabButtons: [EntryTab: SideLabelButton] = [:]

    private var activeTab: EntryTab = .files
    private var activeGroup: String = EntrySearchService.allGroupsLabel
    private var displayedGroups: [String] = [EntrySearchService.allGroupsLabel]
    private var displayedEntries: [QuickEntry] = []
    private var isSyncingSelection = false
    private var activeEditorWindowController: EntryEditorWindowController?

    init(configStore: ConfigStore, searchService: EntrySearchService, actionService: EntryActionService) {
        self.configStore = configStore
        self.searchService = searchService
        self.actionService = actionService
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func loadView() {
        view = NSView(frame: NSRect(x: 0, y: 0, width: 760, height: 500))
        view.wantsLayer = true
        view.layer?.backgroundColor = NSColor.windowBackgroundColor.cgColor
        view.registerForDraggedTypes([.fileURL])
        configureView()
        reloadData()
    }

    func prepareForPresentation(tab: EntryTab) {
        activeTab = tab
        activeGroup = EntrySearchService.allGroupsLabel
        searchField.stringValue = ""
        updateSearchPlaceholder()
        reloadData()
    }

    func prepareForGesturePresentation() {
        searchField.stringValue = ""
        updateSearchPlaceholder()
        reloadData()
    }

    func focusSearchField() {
        view.window?.makeFirstResponder(searchField)
    }

    func handleAddURLRequest(_ request: AddURLRequest) {
        prepareForPresentation(tab: .urls)

        let existing = configStore.currentConfig().entries.first {
            $0.type == .url && $0.path.caseInsensitiveCompare(request.url.absoluteString) == .orderedSame
        }

        if let existing {
            reloadData(selectEntryID: existing.id)
            presentInfoAlert(title: "条目已存在", message: "这个网址已经在 Quickstart 中。")
            return
        }

        let entry = QuickEntry(
            name: request.title,
            path: request.url.absoluteString,
            type: .url,
            group: activeGroup == EntrySearchService.allGroupsLabel ? "" : activeGroup,
            sortOrder: configStore.currentConfig().entries.count
        )

        presentEditor(for: entry, isNew: true)
    }

    func addPathURLs(_ urls: [URL], showSummary: Bool = true) {
        let fileURLs = urls.filter { $0.isFileURL }
        guard !fileURLs.isEmpty else {
            return
        }

        activeTab = .files
        activeGroup = EntrySearchService.allGroupsLabel
        updateSearchPlaceholder()

        var addedCount = 0
        var duplicateCount = 0
        var lastAddedID: String?
        let baseSortOrder = configStore.currentConfig().entries.count

        for (offset, url) in fileURLs.enumerated() {
            var isDirectory: ObjCBool = false
            FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory)

            let entry = QuickEntry(
                name: url.lastPathComponent,
                path: url.path,
                type: isDirectory.boolValue ? .folder : .file,
                group: "",
                sortOrder: baseSortOrder + offset
            )

            do {
                if try configStore.addEntry(entry) {
                    addedCount += 1
                    lastAddedID = entry.id
                } else {
                    duplicateCount += 1
                }
            } catch {
                presentInfoAlert(title: "添加失败", message: error.localizedDescription)
                break
            }
        }

        reloadData(selectEntryID: lastAddedID)

        guard showSummary else {
            return
        }

        if addedCount > 0 || duplicateCount > 0 {
            var message = "已添加 \(addedCount) 个条目。"
            if duplicateCount > 0 {
                message += "\n跳过 \(duplicateCount) 个重复条目。"
            }
            presentInfoAlert(title: "拖放添加完成", message: message)
        }
    }

    func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        sender.draggingPasteboard.canReadObject(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) ? .copy : []
    }

    func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        guard let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty else {
            return false
        }

        addPathURLs(urls)
        return true
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        if tableView == groupTableView {
            return displayedGroups.count
        }

        return displayedEntries.count
    }

    func tableView(_ tableView: NSTableView, heightOfRow row: Int) -> CGFloat {
        tableView == groupTableView ? 28 : 44
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        if tableView == groupTableView {
            let identifier = NSUserInterfaceItemIdentifier("GroupCell")
            let cell = (tableView.makeView(withIdentifier: identifier, owner: self) as? GroupTableCellView) ?? GroupTableCellView()
            cell.identifier = identifier
            let group = displayedGroups[row]
            cell.configure(title: group, isActive: group.caseInsensitiveCompare(activeGroup) == .orderedSame)
            return cell
        }

        let identifier = NSUserInterfaceItemIdentifier("EntryCell")
        let cell = (tableView.makeView(withIdentifier: identifier, owner: self) as? EntryTableCellView) ?? EntryTableCellView()
        cell.identifier = identifier
        let entry = displayedEntries[row]
        cell.configure(entry: entry, icon: actionService.icon(for: entry), isActive: tableView.selectedRow == row)
        cell.toolTip = entry.path
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard let tableView = notification.object as? NSTableView else {
            return
        }

        if tableView == groupTableView {
            handleGroupSelectionChanged()
        } else if tableView == entryTableView {
            entryTableView.reloadData()
            updateButtonState()
        }
    }

    func controlTextDidChange(_ obj: Notification) {
        reloadData()
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        switch commandSelector {
        case #selector(NSResponder.moveDown(_:)):
            guard !displayedEntries.isEmpty else {
                return false
            }

            entryTableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
            view.window?.makeFirstResponder(entryTableView)
            return true
        case #selector(NSResponder.insertNewline(_:)):
            if selectedEntry == nil, !displayedEntries.isEmpty {
                entryTableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
            }
            openSelectedEntry(nil)
            return true
        case #selector(NSResponder.cancelOperation(_:)):
            onDismissRequested?()
            return true
        default:
            return false
        }
    }

    func highlightAtScreenPoint(_ screenPoint: NSPoint) {
        guard let window = view.window else {
            return
        }

        let windowPoint = window.convertPoint(fromScreen: screenPoint)
        if let tab = tab(at: windowPoint) {
            switchTab(tab)
            entryTableView.deselectAll(nil)
            return
        }

        let groupPoint = groupTableView.convert(windowPoint, from: nil)
        if groupTableView.bounds.contains(groupPoint) {
            let row = groupTableView.row(at: groupPoint)
            if row >= 0, row < displayedGroups.count {
                switchGroup(displayedGroups[row])
                entryTableView.deselectAll(nil)
                return
            }
        }

        let entryPoint = entryTableView.convert(windowPoint, from: nil)
        if entryTableView.bounds.contains(entryPoint) {
            let row = entryTableView.row(at: entryPoint)
            if row >= 0, row < displayedEntries.count {
                entryTableView.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
                entryTableView.scrollRowToVisible(row)
                return
            }
        }

        entryTableView.deselectAll(nil)
        updateButtonState()
    }

    @discardableResult
    func tryReleaseAtScreenPoint(_ screenPoint: NSPoint) -> Bool {
        guard let window = view.window else {
            return false
        }

        guard window.frame.contains(screenPoint) else {
            onDismissRequested?()
            return false
        }

        let windowPoint = window.convertPoint(fromScreen: screenPoint)
        let entryPoint = entryTableView.convert(windowPoint, from: nil)
        if entryTableView.bounds.contains(entryPoint) {
            let row = entryTableView.row(at: entryPoint)
            if row >= 0, row < displayedEntries.count {
                entryTableView.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
                openSelectedEntry(nil)
                return true
            }
        }

        if isPointInSideLabels(windowPoint) {
            focusSearchField()
            return false
        }

        focusSearchField()
        return false
    }

    private func configureView() {
        searchField.delegate = self
        updateSearchPlaceholder()

        let groupColumn = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("group"))
        groupColumn.resizingMask = .autoresizingMask
        groupTableView.frame = NSRect(x: 0, y: 0, width: 120, height: 320)
        groupTableView.autoresizingMask = [.width, .height]
        groupTableView.addTableColumn(groupColumn)
        groupTableView.headerView = nil
        groupTableView.rowSizeStyle = .medium
        groupTableView.intercellSpacing = .zero
        groupTableView.delegate = self
        groupTableView.dataSource = self
        groupTableView.selectionHighlightStyle = .none
        groupTableView.focusRingType = .none
        groupTableView.backgroundColor = .clear

        let entryColumn = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("entry"))
        entryColumn.resizingMask = .autoresizingMask
        entryTableView.frame = NSRect(x: 0, y: 0, width: 520, height: 320)
        entryTableView.autoresizingMask = [.width, .height]
        entryTableView.addTableColumn(entryColumn)
        entryTableView.headerView = nil
        entryTableView.rowSizeStyle = .custom
        entryTableView.intercellSpacing = .zero
        entryTableView.delegate = self
        entryTableView.dataSource = self
        entryTableView.selectionHighlightStyle = .none
        entryTableView.focusRingType = .none
        entryTableView.backgroundColor = .clear
        entryTableView.target = self
        entryTableView.doubleAction = #selector(openSelectedEntry(_:))
        entryTableView.onOpen = { [weak self] in self?.openSelectedEntry(nil) }
        entryTableView.onDelete = { [weak self] in self?.deleteSelectedEntry(nil) }
        entryTableView.onCancel = { [weak self] in self?.onDismissRequested?() }
        entryTableView.onSecondary = { [weak self] in self?.performSecondaryAction(nil) }

        let titleLabel = NSTextField(labelWithString: "Quickstart")
        titleLabel.font = .systemFont(ofSize: 16, weight: .semibold)
        titleLabel.textColor = .labelColor

        closeButton.target = self
        closeButton.action = #selector(closeWindow(_:))
        closeButton.translatesAutoresizingMaskIntoConstraints = false

        let topBar = NSStackView(views: [titleLabel, searchField, closeButton])
        topBar.orientation = .horizontal
        topBar.spacing = 12
        topBar.alignment = .centerY
        topBar.translatesAutoresizingMaskIntoConstraints = false

        let groupScrollView = NSScrollView()
        groupScrollView.hasVerticalScroller = true
        groupScrollView.drawsBackground = false
        groupScrollView.documentView = groupTableView
        groupScrollView.translatesAutoresizingMaskIntoConstraints = false

        let entryScrollView = NSScrollView()
        entryScrollView.hasVerticalScroller = true
        entryScrollView.drawsBackground = false
        entryScrollView.documentView = entryTableView
        entryScrollView.translatesAutoresizingMaskIntoConstraints = false

        for button in [openButton, secondaryActionButton, addButton, editButton, deleteButton, settingsButton] {
            button.isBordered = false
            button.wantsLayer = true
            button.layer?.cornerRadius = 7
            button.layer?.backgroundColor = NSColor.separatorColor.withAlphaComponent(0.16).cgColor
            button.translatesAutoresizingMaskIntoConstraints = false
        }

        openButton.target = self
        openButton.action = #selector(openSelectedEntry(_:))
        secondaryActionButton.target = self
        secondaryActionButton.action = #selector(performSecondaryAction(_:))
        addButton.target = self
        addButton.action = #selector(addEntry(_:))
        editButton.target = self
        editButton.action = #selector(editSelectedEntry(_:))
        deleteButton.target = self
        deleteButton.action = #selector(deleteSelectedEntry(_:))
        settingsButton.target = self
        settingsButton.action = #selector(showSettings(_:))

        countLabel.textColor = .secondaryLabelColor

        let bottomBar = NSStackView(views: [
            openButton,
            secondaryActionButton,
            addButton,
            editButton,
            deleteButton,
            settingsButton,
            NSView(),
            countLabel
        ])
        bottomBar.orientation = .horizontal
        bottomBar.spacing = 8
        bottomBar.alignment = .centerY
        bottomBar.translatesAutoresizingMaskIntoConstraints = false

        let spacer = bottomBar.views[6]
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        spacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let typeStack = NSStackView()
        typeStack.orientation = .vertical
        typeStack.spacing = 8
        typeStack.alignment = .centerX
        typeStack.translatesAutoresizingMaskIntoConstraints = false

        for tab in EntryTab.allCases {
            let button = SideLabelButton(title: tab.title)
            button.target = self
            button.action = #selector(changeTabButton(_:))
            button.tag = tab.rawValue
            button.translatesAutoresizingMaskIntoConstraints = false
            tabButtons[tab] = button
            typeStack.addArrangedSubview(button)
            NSLayoutConstraint.activate([
                button.widthAnchor.constraint(equalToConstant: 48),
                button.heightAnchor.constraint(equalToConstant: 68)
            ])
        }

        let leftRail = NSView()
        leftRail.wantsLayer = true
        leftRail.layer?.backgroundColor = NSColor.controlBackgroundColor.withAlphaComponent(0.72).cgColor
        leftRail.layer?.cornerRadius = 12
        leftRail.translatesAutoresizingMaskIntoConstraints = false
        leftRail.addSubview(typeStack)

        let contentStack = NSStackView(views: [entryScrollView])
        contentStack.orientation = .vertical
        contentStack.translatesAutoresizingMaskIntoConstraints = false

        let rightRail = NSView()
        rightRail.wantsLayer = true
        rightRail.layer?.backgroundColor = NSColor.controlBackgroundColor.withAlphaComponent(0.72).cgColor
        rightRail.layer?.cornerRadius = 12
        rightRail.translatesAutoresizingMaskIntoConstraints = false
        rightRail.addSubview(groupScrollView)

        let mainRow = NSStackView(views: [leftRail, contentStack, rightRail])
        mainRow.orientation = .horizontal
        mainRow.spacing = 12
        mainRow.translatesAutoresizingMaskIntoConstraints = false

        view.addSubview(topBar)
        view.addSubview(mainRow)
        view.addSubview(bottomBar)

        NSLayoutConstraint.activate([
            topBar.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 16),
            topBar.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -16),
            topBar.topAnchor.constraint(equalTo: view.topAnchor, constant: 14),
            searchField.widthAnchor.constraint(greaterThanOrEqualToConstant: 280),
            closeButton.widthAnchor.constraint(equalToConstant: 24),
            closeButton.heightAnchor.constraint(equalToConstant: 24),

            mainRow.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 16),
            mainRow.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -16),
            mainRow.topAnchor.constraint(equalTo: topBar.bottomAnchor, constant: 14),
            mainRow.bottomAnchor.constraint(equalTo: bottomBar.topAnchor, constant: -14),

            leftRail.widthAnchor.constraint(equalToConstant: 64),
            rightRail.widthAnchor.constraint(equalToConstant: 132),
            typeStack.topAnchor.constraint(equalTo: leftRail.topAnchor, constant: 12),
            typeStack.centerXAnchor.constraint(equalTo: leftRail.centerXAnchor),
            typeStack.bottomAnchor.constraint(lessThanOrEqualTo: leftRail.bottomAnchor, constant: -12),

            groupScrollView.leadingAnchor.constraint(equalTo: rightRail.leadingAnchor, constant: 6),
            groupScrollView.trailingAnchor.constraint(equalTo: rightRail.trailingAnchor, constant: -6),
            groupScrollView.topAnchor.constraint(equalTo: rightRail.topAnchor, constant: 8),
            groupScrollView.bottomAnchor.constraint(equalTo: rightRail.bottomAnchor, constant: -8),

            bottomBar.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 16),
            bottomBar.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -16),
            bottomBar.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -14)
        ])

        view.window?.makeFirstResponder(searchField)
        applyTabButtonStyles()
        updateButtonState()
    }

    private func updateSearchPlaceholder() {
        searchField.placeholderString = activeTab.searchPlaceholder
    }

    private func reloadData(selectEntryID: String? = nil) {
        let previousSelection = selectEntryID ?? selectedEntry?.id
        let config = configStore.currentConfig()

        displayedGroups = [EntrySearchService.allGroupsLabel] + searchService.orderedGroupNames(for: activeTab, in: config)
        if !displayedGroups.contains(where: { $0.caseInsensitiveCompare(activeGroup) == .orderedSame }) {
            activeGroup = EntrySearchService.allGroupsLabel
        }

        displayedEntries = searchService.filteredEntries(
            for: activeTab,
            activeGroup: activeGroup,
            query: searchField.stringValue,
            in: config
        )

        isSyncingSelection = true
        groupTableView.reloadData()
        entryTableView.reloadData()
        selectGroup(named: activeGroup)
        selectEntry(withID: previousSelection)
        isSyncingSelection = false

        countLabel.stringValue = "\(displayedEntries.count) 项"
        applyTabButtonStyles()
        updateButtonState()
    }

    private func selectGroup(named group: String) {
        let index = displayedGroups.firstIndex { $0.caseInsensitiveCompare(group) == .orderedSame } ?? 0
        groupTableView.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        groupTableView.scrollRowToVisible(index)
    }

    private func selectEntry(withID id: String?) {
        guard let id else {
            if !displayedEntries.isEmpty {
                entryTableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
            } else {
                entryTableView.deselectAll(nil)
            }
            return
        }

        if let index = displayedEntries.firstIndex(where: { $0.id == id }) {
            entryTableView.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
            entryTableView.scrollRowToVisible(index)
        } else if !displayedEntries.isEmpty {
            entryTableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
        } else {
            entryTableView.deselectAll(nil)
        }
    }

    private func handleGroupSelectionChanged() {
        guard !isSyncingSelection else {
            return
        }

        let row = groupTableView.selectedRow
        guard row >= 0, row < displayedGroups.count else {
            return
        }

        let group = displayedGroups[row]
        guard group.caseInsensitiveCompare(activeGroup) != .orderedSame else {
            return
        }

        switchGroup(group)
    }

    private var selectedEntry: QuickEntry? {
        let row = entryTableView.selectedRow
        guard row >= 0, row < displayedEntries.count else {
            return nil
        }
        return displayedEntries[row]
    }

    private func updateButtonState() {
        let hasSelection = selectedEntry != nil
        openButton.isEnabled = hasSelection
        editButton.isEnabled = hasSelection
        deleteButton.isEnabled = hasSelection
        secondaryActionButton.isEnabled = hasSelection

        guard let entry = selectedEntry else {
            secondaryActionButton.title = "在 Finder 中显示"
            return
        }

        switch entry.type {
        case .folder, .file:
            secondaryActionButton.title = "在 Finder 中显示"
        case .url:
            secondaryActionButton.title = "复制网址"
        case .text:
            secondaryActionButton.title = "复制文本"
        }
    }

    @objc
    private func changeTabButton(_ sender: NSButton) {
        guard let tab = EntryTab(rawValue: sender.tag) else {
            return
        }

        switchTab(tab)
    }

    @objc
    private func closeWindow(_ sender: Any?) {
        onDismissRequested?()
    }

    private func switchTab(_ tab: EntryTab) {
        guard activeTab != tab else {
            return
        }

        activeTab = tab
        activeGroup = EntrySearchService.allGroupsLabel
        updateSearchPlaceholder()
        reloadData()
    }

    private func switchGroup(_ group: String) {
        let normalized = group.trimmingCharacters(in: .whitespacesAndNewlines)
        let targetGroup = normalized.isEmpty ? EntrySearchService.allGroupsLabel : normalized
        guard targetGroup.caseInsensitiveCompare(activeGroup) != .orderedSame else {
            return
        }

        activeGroup = targetGroup
        if targetGroup.caseInsensitiveCompare(EntrySearchService.allGroupsLabel) != .orderedSame {
            try? configStore.touchGroup(targetGroup)
        }
        reloadData()
    }

    private func applyTabButtonStyles() {
        for (tab, button) in tabButtons {
            button.setActive(tab == activeTab)
        }
    }

    private func tab(at windowPoint: NSPoint) -> EntryTab? {
        for (tab, button) in tabButtons {
            let point = button.convert(windowPoint, from: nil)
            if button.bounds.contains(point) {
                return tab
            }
        }

        return nil
    }

    private func isPointInSideLabels(_ windowPoint: NSPoint) -> Bool {
        if tab(at: windowPoint) != nil {
            return true
        }

        let groupPoint = groupTableView.convert(windowPoint, from: nil)
        return groupTableView.bounds.contains(groupPoint)
    }

    @objc
    private func openSelectedEntry(_ sender: Any?) {
        guard let entry = selectedEntry else {
            return
        }

        let succeeded = actionService.open(entry)
        guard succeeded else {
            presentInfoAlert(title: "操作失败", message: "无法打开当前条目。")
            return
        }

        do {
            try configStore.touchEntry(id: entry.id)
        } catch {
            presentInfoAlert(title: "保存失败", message: error.localizedDescription)
        }

        reloadData(selectEntryID: entry.id)
        onDismissRequested?()
    }

    @objc
    private func performSecondaryAction(_ sender: Any?) {
        guard let entry = selectedEntry else {
            return
        }

        let succeeded: Bool
        switch entry.type {
        case .folder, .file:
            succeeded = actionService.revealInFinder(entry)
        case .url, .text:
            succeeded = actionService.copy(entry)
        }

        if !succeeded {
            presentInfoAlert(title: "操作失败", message: "无法完成当前操作。")
        }
    }

    @objc
    private func addEntry(_ sender: Any?) {
        var entry = QuickEntry()
        entry.type = activeTab.defaultEntryType
        entry.group = activeGroup == EntrySearchService.allGroupsLabel ? "" : activeGroup
        entry.sortOrder = configStore.currentConfig().entries.count
        presentEditor(for: entry, isNew: true)
    }

    @objc
    private func editSelectedEntry(_ sender: Any?) {
        guard let entry = selectedEntry else {
            return
        }

        presentEditor(for: entry, isNew: false)
    }

    @objc
    private func deleteSelectedEntry(_ sender: Any?) {
        guard let entry = selectedEntry else {
            return
        }

        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "删除条目"
        alert.informativeText = "确定要删除 “\(entry.name)” 吗？"
        alert.addButton(withTitle: "删除")
        alert.addButton(withTitle: "取消")

        guard let window = view.window else {
            return
        }

        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self else {
                return
            }

            do {
                try self.configStore.removeEntry(id: entry.id)
                self.reloadData()
            } catch {
                self.presentInfoAlert(title: "删除失败", message: error.localizedDescription)
            }
        }
    }

    @objc
    private func showSettings(_ sender: Any?) {
        onShowSettings?()
    }

    private func presentEditor(for entry: QuickEntry, isNew: Bool) {
        guard let window = view.window else {
            return
        }

        let editor = EntryEditorWindowController(entry: entry, isNewEntry: isNew)
        activeEditorWindowController = editor
        editor.beginSheet(for: window) { [weak self] result in
            self?.activeEditorWindowController = nil
            guard let self, let result else {
                return
            }

            do {
                if isNew {
                    let inserted = try self.configStore.addEntry(result)
                    if !inserted {
                        self.presentInfoAlert(title: "条目已存在", message: "同一路径或网址已经存在。")
                        return
                    }
                } else {
                    try self.configStore.updateEntry(result)
                }

                self.reloadData(selectEntryID: result.id)
            } catch {
                self.presentInfoAlert(title: "保存失败", message: error.localizedDescription)
            }
        }
    }

    private func presentInfoAlert(title: String, message: String) {
        let alert = NSAlert()
        alert.alertStyle = .informational
        alert.messageText = title
        alert.informativeText = message

        if let window = view.window {
            alert.beginSheetModal(for: window)
        } else {
            alert.runModal()
        }
    }
}

private final class ActionTableView: NSTableView {
    var onOpen: (() -> Void)?
    var onDelete: (() -> Void)?
    var onCancel: (() -> Void)?
    var onSecondary: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 36, 76:
            onOpen?()
        case 51, 117:
            onDelete?()
        case 49:
            onSecondary?()
        case 53:
            onCancel?()
        default:
            super.keyDown(with: event)
        }
    }
}

private final class SideLabelButton: NSButton {
    init(title: String) {
        super.init(frame: .zero)
        self.title = title
        isBordered = false
        font = .systemFont(ofSize: 14, weight: .semibold)
        wantsLayer = true
        layer?.cornerRadius = 11
        setButtonType(.momentaryChange)
        setActive(false)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func setActive(_ active: Bool) {
        layer?.backgroundColor = active
            ? NSColor.controlAccentColor.withAlphaComponent(0.22).cgColor
            : NSColor.clear.cgColor
        contentTintColor = active ? .controlAccentColor : .secondaryLabelColor
    }
}

private final class GroupTableCellView: NSTableCellView {
    private let backgroundView = NSView()
    private let label = NSTextField(labelWithString: "")

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        translatesAutoresizingMaskIntoConstraints = false
        backgroundView.translatesAutoresizingMaskIntoConstraints = false
        backgroundView.wantsLayer = true
        backgroundView.layer?.cornerRadius = 8
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(backgroundView)
        addSubview(label)
        NSLayoutConstraint.activate([
            backgroundView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 4),
            backgroundView.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4),
            backgroundView.topAnchor.constraint(equalTo: topAnchor, constant: 2),
            backgroundView.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -2),

            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            label.centerYAnchor.constraint(equalTo: centerYAnchor)
        ])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func configure(title: String, isActive: Bool) {
        label.stringValue = title
        label.font = isActive ? .systemFont(ofSize: 12, weight: .semibold) : .systemFont(ofSize: 12)
        label.textColor = isActive ? .controlAccentColor : .secondaryLabelColor
        backgroundView.layer?.backgroundColor = isActive
            ? NSColor.controlAccentColor.withAlphaComponent(0.14).cgColor
            : NSColor.clear.cgColor
    }
}

private final class EntryTableCellView: NSTableCellView {
    private let backgroundView = NSView()
    private let iconView = NSImageView()
    private let titleField = NSTextField(labelWithString: "")
    private let subtitleField = NSTextField(labelWithString: "")

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        translatesAutoresizingMaskIntoConstraints = false

        backgroundView.translatesAutoresizingMaskIntoConstraints = false
        backgroundView.wantsLayer = true
        backgroundView.layer?.cornerRadius = 9
        iconView.translatesAutoresizingMaskIntoConstraints = false
        titleField.translatesAutoresizingMaskIntoConstraints = false
        subtitleField.translatesAutoresizingMaskIntoConstraints = false
        subtitleField.textColor = .secondaryLabelColor
        subtitleField.font = .systemFont(ofSize: 11)
        subtitleField.lineBreakMode = .byTruncatingMiddle

        let textStack = NSStackView(views: [titleField, subtitleField])
        textStack.orientation = .vertical
        textStack.spacing = 2
        textStack.alignment = .leading
        textStack.translatesAutoresizingMaskIntoConstraints = false

        addSubview(backgroundView)
        addSubview(iconView)
        addSubview(textStack)

        NSLayoutConstraint.activate([
            backgroundView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2),
            backgroundView.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -2),
            backgroundView.topAnchor.constraint(equalTo: topAnchor, constant: 2),
            backgroundView.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -2),

            iconView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 10),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
            iconView.widthAnchor.constraint(equalToConstant: 18),
            iconView.heightAnchor.constraint(equalToConstant: 18),

            textStack.leadingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 8),
            textStack.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            textStack.centerYAnchor.constraint(equalTo: centerYAnchor)
        ])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func configure(entry: QuickEntry, icon: NSImage, isActive: Bool) {
        iconView.image = icon
        titleField.stringValue = entry.name
        titleField.font = .systemFont(ofSize: 13, weight: .medium)
        subtitleField.stringValue = entry.path
        titleField.textColor = entry.type == .folder || entry.type == .file ? .labelColor : .labelColor
        backgroundView.layer?.backgroundColor = isActive
            ? NSColor.selectedContentBackgroundColor.withAlphaComponent(0.18).cgColor
            : NSColor.clear.cgColor
    }
}
