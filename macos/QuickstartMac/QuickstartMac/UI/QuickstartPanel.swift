import AppKit

protocol QuickstartPanelLifecycleDelegate: AnyObject {
    func quickstartPanelDidRequestHide(_ panel: QuickstartPanel)
}

final class QuickstartPanel: NSPanel, NSWindowDelegate {
    weak var panelLifecycleDelegate: QuickstartPanelLifecycleDelegate?

    init(contentViewController: NSViewController) {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 700, height: 460),
            styleMask: [.titled, .closable, .fullSizeContentView, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )

        self.contentViewController = contentViewController
        titleVisibility = .hidden
        titlebarAppearsTransparent = true
        isFloatingPanel = true
        level = .statusBar
        hasShadow = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        delegate = self
        animationBehavior = .utilityWindow
    }

    override var canBecomeKey: Bool {
        true
    }

    override var canBecomeMain: Bool {
        false
    }

    override func cancelOperation(_ sender: Any?) {
        panelLifecycleDelegate?.quickstartPanelDidRequestHide(self)
    }

    func windowDidResignKey(_ notification: Notification) {
        guard attachedSheet == nil else {
            return
        }

        panelLifecycleDelegate?.quickstartPanelDidRequestHide(self)
    }
}
