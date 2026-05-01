import AppKit
import ApplicationServices

final class GlobalRightDragController {
    private enum GestureState {
        case idle
        case tracking(CGPoint)
        case popupShown(CGPoint)
    }

    private static let dragTriggerDx: CGFloat = 120
    private static let dragTolerateDy: CGFloat = 50
    private static let syntheticTag: Int64 = 0x51534854

    var onTriggered: ((NSPoint) -> Void)?
    var onMoved: ((NSPoint) -> Void)?
    var onReleased: ((NSPoint) -> Void)?
    var onCancelled: (() -> Void)?

    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var state: GestureState = .idle

    var isEnabled: Bool {
        eventTap != nil
    }

    func start() -> Bool {
        stop()

        let mask =
            (1 << CGEventType.rightMouseDown.rawValue) |
            (1 << CGEventType.rightMouseDragged.rawValue) |
            (1 << CGEventType.rightMouseUp.rawValue) |
            (1 << CGEventType.leftMouseDown.rawValue) |
            (1 << CGEventType.otherMouseDown.rawValue)

        guard let tap = CGEvent.tapCreate(
            tap: .cghidEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: CGEventMask(mask),
            callback: Self.eventCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()
        ) else {
            return false
        }

        guard let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0) else {
            CFMachPortInvalidate(tap)
            return false
        }

        eventTap = tap
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        return true
    }

    func stop() {
        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }

        if let eventTap {
            CFMachPortInvalidate(eventTap)
        }

        runLoopSource = nil
        eventTap = nil
        state = .idle
    }

    static func openPrivacySettings() {
        let urls = [
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"
        ]

        for rawValue in urls {
            if let url = URL(string: rawValue), NSWorkspace.shared.open(url) {
                return
            }
        }
    }

    private static let eventCallback: CGEventTapCallBack = { proxy, type, event, userInfo in
        guard let userInfo else {
            return Unmanaged.passUnretained(event)
        }

        let controller = Unmanaged<GlobalRightDragController>.fromOpaque(userInfo).takeUnretainedValue()
        return controller.handle(proxy: proxy, type: type, event: event)
    }

    private func handle(proxy: CGEventTapProxy, type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let eventTap {
                CGEvent.tapEnable(tap: eventTap, enable: true)
            }
            return Unmanaged.passUnretained(event)
        }

        if event.getIntegerValueField(.eventSourceUserData) == Self.syntheticTag {
            return Unmanaged.passUnretained(event)
        }

        let location = event.location

        switch type {
        case .rightMouseDown:
            state = .tracking(location)
            return nil

        case .rightMouseDragged:
            switch state {
            case .tracking(let start):
                if location.x - start.x >= Self.dragTriggerDx,
                   abs(location.y - start.y) <= Self.dragTolerateDy {
                    state = .popupShown(start)
                    dispatch { [weak self] in
                        self?.onTriggered?(self?.appKitPoint(from: location) ?? .zero)
                    }
                }
                return nil
            case .popupShown:
                dispatch { [weak self] in
                    self?.onMoved?(self?.appKitPoint(from: location) ?? .zero)
                }
                return nil
            case .idle:
                return Unmanaged.passUnretained(event)
            }

        case .rightMouseUp:
            let previousState = state
            state = .idle

            switch previousState {
            case .tracking:
                synthesizeRightClick(at: location)
            case .popupShown:
                dispatch { [weak self] in
                    self?.onReleased?(self?.appKitPoint(from: location) ?? .zero)
                }
            case .idle:
                break
            }
            return nil

        case .leftMouseDown, .otherMouseDown:
            if case .idle = state {
                return Unmanaged.passUnretained(event)
            }

            state = .idle
            dispatch { [weak self] in
                self?.onCancelled?()
            }
            return Unmanaged.passUnretained(event)

        default:
            return Unmanaged.passUnretained(event)
        }
    }

    private func synthesizeRightClick(at location: CGPoint) {
        guard let source = CGEventSource(stateID: .combinedSessionState),
              let down = CGEvent(mouseEventSource: source, mouseType: .rightMouseDown, mouseCursorPosition: location, mouseButton: .right),
              let up = CGEvent(mouseEventSource: source, mouseType: .rightMouseUp, mouseCursorPosition: location, mouseButton: .right) else {
            return
        }

        down.setIntegerValueField(.eventSourceUserData, value: Self.syntheticTag)
        up.setIntegerValueField(.eventSourceUserData, value: Self.syntheticTag)
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
    }

    private func appKitPoint(from quartzPoint: CGPoint) -> NSPoint {
        let screenFrame = NSScreen.screens.reduce(NSRect.null) { partial, screen in
            partial.union(screen.frame)
        }

        guard !screenFrame.isNull else {
            return NSPoint(x: quartzPoint.x, y: quartzPoint.y)
        }

        return NSPoint(x: quartzPoint.x, y: screenFrame.maxY - quartzPoint.y)
    }

    private func dispatch(_ work: @escaping () -> Void) {
        if Thread.isMainThread {
            work()
        } else {
            DispatchQueue.main.async(execute: work)
        }
    }
}
