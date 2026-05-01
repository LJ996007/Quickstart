import AppKit

protocol QuickstartPanelLifecycleDelegate: AnyObject {
    func quickstartPanelDidRequestHide(_ panel: QuickstartPanel)
}

final class QuickstartPanel: RoundedWindow, NSWindowDelegate {
    weak var panelLifecycleDelegate: QuickstartPanelLifecycleDelegate?

    init(contentViewController: NSViewController) {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 760, height: 500), title: "Quickstart")

        self.contentViewController = contentViewController
        contentView?.wantsLayer = true
        contentView?.layer?.cornerRadius = 14
        contentView?.layer?.masksToBounds = true
        delegate = self
        minSize = NSSize(width: 640, height: 420)
    }

    override func cancelOperation(_ sender: Any?) {
        panelLifecycleDelegate?.quickstartPanelDidRequestHide(self)
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        panelLifecycleDelegate?.quickstartPanelDidRequestHide(self)
        return false
    }
}
