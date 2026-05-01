import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate, QuickstartPanelLifecycleDelegate {
    private let configStore = ConfigStore()
    private let searchService = EntrySearchService()
    private let actionService = EntryActionService()
    private let urlHandler = QuickstartURLHandler()

    private var statusItem: NSStatusItem!
    private var statusMenu: NSMenu!
    private var panel: QuickstartPanel!
    private var mainWindowController: NSWindowController!
    private var mainViewController: MainViewController!
    private var settingsWindowController: SettingsWindowController?
    private var rightDragController: GlobalRightDragController?
    private var rightDragPermissionItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)

        do {
            try configStore.load()
        } catch {
            NSLog("Failed to load config: \(error.localizedDescription)")
        }

        configureStatusItem()
        configureMainWindow()
        configureRightDragGesture()
        if !handleLaunchArguments() {
            DispatchQueue.main.async { [weak self] in
                self?.showMainWindow(tab: .files)
            }
        }
    }

    func application(_ sender: NSApplication, openFile filename: String) -> Bool {
        handleExternalFileURLs([URL(fileURLWithPath: filename)])
        return true
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            handleExternalURL(url)
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showMainWindow(tab: .files)
        return true
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        guard panel != nil,
              panel.isVisible == false,
              settingsWindowController?.window?.isVisible != true else {
            return
        }

        showMainWindow(tab: .files)
    }

    func applicationWillTerminate(_ notification: Notification) {
        rightDragController?.stop()
    }

    func quickstartPanelDidRequestHide(_ panel: QuickstartPanel) {
        hideMainWindow()
    }

    private func configureStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        guard let button = statusItem.button else {
            return
        }

        if let image = NSImage(systemSymbolName: "sparkles.square.fill", accessibilityDescription: "Quickstart") {
            image.isTemplate = true
            button.image = image
        }
        button.title = " Quickstart"
        button.imagePosition = .imageLeading

        button.target = self
        button.action = #selector(handleStatusItemPress(_:))
        button.sendAction(on: [.leftMouseUp, .rightMouseUp])

        statusMenu = NSMenu()
        statusMenu.addItem(NSMenuItem(title: "显示 Quickstart", action: #selector(showMainWindowFromMenu(_:)), keyEquivalent: ""))
        statusMenu.addItem(NSMenuItem(title: "设置", action: #selector(showSettingsWindow), keyEquivalent: ","))
        rightDragPermissionItem = NSMenuItem(title: "启用右键拖拽权限", action: #selector(openRightDragPrivacySettings), keyEquivalent: "")
        statusMenu.addItem(rightDragPermissionItem!)
        statusMenu.addItem(.separator())
        statusMenu.addItem(NSMenuItem(title: "退出", action: #selector(quitApp), keyEquivalent: "q"))
        statusMenu.items.forEach { $0.target = self }
    }

    private func configureMainWindow() {
        mainViewController = MainViewController(
            configStore: configStore,
            searchService: searchService,
            actionService: actionService
        )
        mainViewController.onShowSettings = { [weak self] in
            self?.showSettingsWindow()
        }
        mainViewController.onDismissRequested = { [weak self] in
            self?.hideMainWindow()
        }

        _ = mainViewController.view
        panel = QuickstartPanel(contentViewController: mainViewController)
        panel.panelLifecycleDelegate = self
        mainWindowController = NSWindowController(window: panel)
    }

    private func configureRightDragGesture() {
        let controller = GlobalRightDragController()
        controller.onTriggered = { [weak self] screenPoint in
            self?.showMainWindowForGesture(at: screenPoint)
        }
        controller.onMoved = { [weak self] screenPoint in
            self?.mainViewController.highlightAtScreenPoint(screenPoint)
        }
        controller.onReleased = { [weak self] screenPoint in
            _ = self?.mainViewController.tryReleaseAtScreenPoint(screenPoint)
        }
        controller.onCancelled = { [weak self] in
            self?.hideMainWindow()
        }

        rightDragController = controller
        updateRightDragPermissionMenu(enabled: controller.start())
    }

    @objc
    private func handleStatusItemPress(_ sender: Any?) {
        guard let event = NSApp.currentEvent else {
            showMainWindow(tab: .files)
            return
        }

        if event.type == .rightMouseUp {
            statusItem.menu = statusMenu
            statusItem.button?.performClick(nil)
            DispatchQueue.main.async { [weak self] in
                self?.statusItem.menu = nil
            }
        } else {
            showMainWindow(tab: .files)
        }
    }

    @objc
    private func showMainWindowFromMenu(_ sender: Any?) {
        showMainWindow(tab: .files)
    }

    @objc
    private func openRightDragPrivacySettings() {
        GlobalRightDragController.openPrivacySettings()
        updateRightDragPermissionMenu(enabled: rightDragController?.start() == true)
    }

    @objc
    private func showSettingsWindow() {
        hideMainWindow()

        if settingsWindowController == nil {
            settingsWindowController = SettingsWindowController(
                configStore: configStore,
                bookmarklet: QuickstartURLHandler.bookmarklet
            )
        }

        settingsWindowController?.showWindow(nil)
        settingsWindowController?.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc
    private func quitApp() {
        NSApp.terminate(nil)
    }

    private func showMainWindow(tab: EntryTab) {
        mainViewController.prepareForPresentation(tab: tab)
        showPreparedMainWindow(near: nil)

        DispatchQueue.main.async { [weak self] in
            self?.mainViewController.focusSearchField()
        }
    }

    private func showMainWindowForGesture(at screenPoint: NSPoint) {
        mainViewController.prepareForGesturePresentation()
        showPreparedMainWindow(near: screenPoint)
        mainViewController.highlightAtScreenPoint(screenPoint)
    }

    private func showPreparedMainWindow(near screenPoint: NSPoint?) {
        positionMainWindow(near: screenPoint)
        if panel.isMiniaturized {
            panel.deminiaturize(nil)
        }
        mainWindowController.showWindow(nil)
        panel.makeKeyAndOrderFront(nil)
        panel.orderFrontRegardless()
        NSApp.activate(ignoringOtherApps: true)
    }

    private func hideMainWindow() {
        panel.orderOut(nil)
    }

    @discardableResult
    private func handleLaunchArguments() -> Bool {
        let args = Array(ProcessInfo.processInfo.arguments.dropFirst())
        guard !args.isEmpty else {
            return false
        }

        if args.count >= 2, args[0] == "--add" {
            handleExternalFileURLs([URL(fileURLWithPath: args[1])])
            return true
        }

        if let first = args.first, let url = URL(string: first), url.scheme == "quickstart" {
            handleExternalURL(url)
            return true
        }

        return false
    }

    private func handleExternalURL(_ url: URL) {
        if url.isFileURL {
            handleExternalFileURLs([url])
            return
        }

        do {
            let request = try urlHandler.parseAddURLRequest(from: url)
            showMainWindow(tab: .urls)
            mainViewController.handleAddURLRequest(request)
        } catch {
            let alert = NSAlert()
            alert.alertStyle = .warning
            alert.messageText = "无法处理 Quickstart 链接"
            alert.informativeText = error.localizedDescription
            alert.beginSheetModal(for: panel)
        }
    }

    private func handleExternalFileURLs(_ urls: [URL]) {
        showMainWindow(tab: .files)
        mainViewController.addPathURLs(urls, showSummary: true)
    }

    private func positionMainWindow(near screenPoint: NSPoint?) {
        guard !panel.isVisible else {
            return
        }

        let screen = screenPoint.flatMap { point in
            NSScreen.screens.first { $0.visibleFrame.contains(point) }
        } ?? NSScreen.main ?? panel.screen

        guard let screenFrame = screen?.visibleFrame else {
            panel.center()
            return
        }

        let panelSize = panel.frame.size
        let preferredOrigin = screenPoint.map {
            NSPoint(x: $0.x - 18, y: $0.y - panelSize.height + 36)
        } ?? NSPoint(
            x: screenFrame.midX - panelSize.width / 2,
            y: screenFrame.midY - panelSize.height / 2
        )

        let origin = NSPoint(
            x: max(screenFrame.minX + 8, min(preferredOrigin.x, screenFrame.maxX - panelSize.width - 8)),
            y: max(screenFrame.minY + 8, min(preferredOrigin.y, screenFrame.maxY - panelSize.height - 8))
        )
        panel.setFrameOrigin(origin)
    }

    private func updateRightDragPermissionMenu(enabled: Bool) {
        rightDragPermissionItem?.isHidden = enabled
    }
}
