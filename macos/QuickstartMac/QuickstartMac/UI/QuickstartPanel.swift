import AppKit

protocol QuickstartPanelLifecycleDelegate: AnyObject {
    func quickstartPanelDidRequestHide(_ panel: QuickstartPanel)
}

final class QuickstartPanel: NSWindow, NSWindowDelegate {
    weak var panelLifecycleDelegate: QuickstartPanelLifecycleDelegate?

    init(contentViewController: NSViewController) {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 700, height: 460),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false
        )

        self.contentViewController = contentViewController
        title = "Quickstart"
        hasShadow = true
        isReleasedWhenClosed = false
        isRestorable = false
        delegate = self
    }

    override func cancelOperation(_ sender: Any?) {
        panelLifecycleDelegate?.quickstartPanelDidRequestHide(self)
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        panelLifecycleDelegate?.quickstartPanelDidRequestHide(self)
        return false
    }
}
