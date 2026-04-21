import AppKit

@main
final class AppDelegate: NSObject, NSApplicationDelegate, QuickstartPanelLifecycleDelegate {
    private let configStore = ConfigStore()
    private let searchService = EntrySearchService()
    private let actionService = EntryActionService()
    private let urlHandler = QuickstartURLHandler()

    private var statusItem: NSStatusItem!
    private var statusMenu: NSMenu!
    private var panel: QuickstartPanel!
    private var mainViewController: MainViewController!
    private var settingsWindowController: SettingsWindowController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            try configStore.load()
        } catch {
            NSLog("Failed to load config: \(error.localizedDescription)")
        }

        configureStatusItem()
        configureMainPanel()
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        for url in urls {
            handleExternalURL(url)
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showPanel(tab: .files)
        return true
    }

    func quickstartPanelDidRequestHide(_ panel: QuickstartPanel) {
        hidePanel()
    }

    private func configureStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        guard let button = statusItem.button else {
            return
        }

        if let image = NSImage(systemSymbolName: "sparkles.square.fill", accessibilityDescription: "Quickstart") {
            image.isTemplate = true
            button.image = image
        } else {
            button.title = "QS"
        }

        button.target = self
        button.action = #selector(handleStatusItemPress(_:))
        button.sendAction(on: [.leftMouseUp, .rightMouseUp])

        statusMenu = NSMenu()
        statusMenu.addItem(NSMenuItem(title: "显示 Quickstart", action: #selector(togglePanel(_:)), keyEquivalent: ""))
        statusMenu.addItem(NSMenuItem(title: "设置", action: #selector(showSettingsWindow), keyEquivalent: ","))
        statusMenu.addItem(.separator())
        statusMenu.addItem(NSMenuItem(title: "退出", action: #selector(quitApp), keyEquivalent: "q"))
        statusMenu.items.forEach { $0.target = self }
    }

    private func configureMainPanel() {
        mainViewController = MainViewController(
            configStore: configStore,
            searchService: searchService,
            actionService: actionService
        )
        mainViewController.onShowSettings = { [weak self] in
            self?.showSettingsWindow()
        }
        mainViewController.onDismissRequested = { [weak self] in
            self?.hidePanel()
        }

        panel = QuickstartPanel(contentViewController: mainViewController)
        panel.panelLifecycleDelegate = self
    }

    @objc
    private func handleStatusItemPress(_ sender: Any?) {
        guard let event = NSApp.currentEvent else {
            togglePanel(nil)
            return
        }

        if event.type == .rightMouseUp {
            statusItem.popUpMenu(statusMenu)
        } else {
            togglePanel(nil)
        }
    }

    @objc
    private func togglePanel(_ sender: Any?) {
        if panel.isVisible {
            hidePanel()
        } else {
            showPanel(tab: .files)
        }
    }

    @objc
    private func showSettingsWindow() {
        hidePanel()

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

    private func showPanel(tab: EntryTab) {
        mainViewController.prepareForPresentation(tab: tab)
        positionPanel()
        NSApp.activate(ignoringOtherApps: true)
        panel.makeKeyAndOrderFront(nil)
        mainViewController.focusSearchField()
    }

    private func hidePanel() {
        panel.orderOut(nil)
    }

    private func handleExternalURL(_ url: URL) {
        do {
            let request = try urlHandler.parseAddURLRequest(from: url)
            showPanel(tab: .urls)
            mainViewController.handleAddURLRequest(request)
        } catch {
            let alert = NSAlert()
            alert.alertStyle = .warning
            alert.messageText = "无法处理 Quickstart 链接"
            alert.informativeText = error.localizedDescription
            alert.beginSheetModal(for: panel)
        }
    }

    private func positionPanel() {
        guard let button = statusItem.button,
              let statusWindow = button.window else {
            panel.center()
            return
        }

        let buttonFrame = statusWindow.convertToScreen(button.frame)
        let panelSize = panel.frame.size
        let screenFrame = statusWindow.screen?.visibleFrame ?? NSScreen.main?.visibleFrame ?? .zero

        var origin = NSPoint(
            x: buttonFrame.midX - (panelSize.width / 2),
            y: buttonFrame.minY - panelSize.height - 8
        )

        origin.x = max(screenFrame.minX + 12, min(origin.x, screenFrame.maxX - panelSize.width - 12))
        origin.y = max(screenFrame.minY + 12, min(origin.y, screenFrame.maxY - panelSize.height - 12))

        panel.setFrameOrigin(origin)
    }
}
