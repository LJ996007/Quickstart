namespace Quickstart.Core;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Quickstart.Utils;

public sealed record FileDialogTarget(IntPtr Window, uint ProcessId, bool IsWps, bool IsSave, string Title)
{
    internal int[] FileNameRuntimeId { get; init; } = [];
    internal IntPtr FileNameWindow { get; init; }
    public string Description => $"{(IsWps ? "WPS " : "")}{(IsSave ? "保存" : "打开")}对话框";
}

/// <summary>
/// Navigate an existing file dialog without submitting a file or changing the user's filename.
/// UIA calls run on MTA workers, never in the mouse hook or the WinForms message pump.
/// WPS's Qt picker is identified by file controls or its known Kcfd picker structure.
/// </summary>
public static class FileDialogNavigator
{
    private static readonly SemaphoreSlim AutomationGate = new(1, 1);
    public static Action<string>? NavigationDiagnostic { get; set; }
    private static readonly HashSet<string> WpsProcesses = new(StringComparer.OrdinalIgnoreCase) { "wps", "et", "wpp", "wpspdf" };

    public static IntPtr CaptureForegroundWindow() => GetForegroundWindow();
    internal static uint GetProcessId(IntPtr window) { GetWindowThreadProcessId(window, out var pid); return pid; }

    // A toolbar/menu may remember the WPS owner rather than its owned Qt picker.
    public static IntPtr ResolveDialogWindow(IntPtr source)
    {
        if (source == IntPtr.Zero || !IsWindow(source)) return IntPtr.Zero;
        var root = GetAncestor(source, 2);
        if (root != IntPtr.Zero) source = root;
        GetWindowThreadProcessId(source, out var pid);
        for (var i = 0; i < 8; i++)
        {
            var popup = GetLastActivePopup(source);
            GetWindowThreadProcessId(popup, out var popupPid);
            if (popup == source || popup == IntPtr.Zero || !IsWindowVisible(popup) || popupPid != pid) break;
            source = popup;
        }
        return source;
    }

    public static bool IsOwnWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return pid == Environment.ProcessId;
    }

    public static bool IsPossibleDialog(IntPtr source)
    {
        source = ResolveDialogWindow(source);
        if (source == IntPtr.Zero || IsOwnWindow(source)) return false;
        var name = new StringBuilder(256);
        GetClassName(source, name, name.Capacity);
        if (name.ToString() == "#32770") return true;
        try
        {
            GetWindowThreadProcessId(source, out var pid);
            using var process = Process.GetProcessById((int)pid);
            if (!WpsProcesses.Contains(process.ProcessName)) return false;
            // Some WPS Qt pickers paint their title without exposing a native caption.
            // Admit only the Qt window class for that case; Detect still requires the
            // writable filename, file type and accept/cancel controls before attaching.
            var caption = new StringBuilder(256);
            GetWindowText(source, caption, caption.Capacity);
            return IsWpsPickerCandidate(caption.ToString(), name.ToString());
        }
        catch { return false; }
    }

    public static bool IsWpsPickerCaption(string caption) => caption.Trim().ToLowerInvariant() is
        "打开" or "打开文件" or "选择文件" or "保存" or "保存文件" or "另存为"
        or "open" or "open file" or "select file" or "save" or "save file" or "save as";

    public static bool IsWpsPickerCandidate(string caption, string className) =>
        IsWpsPickerCaption(caption)
        || (string.IsNullOrWhiteSpace(caption) && className == "Qt5QWindowIcon");

    public static bool TryGetBounds(FileDialogTarget target, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!IsLiveWindow(target) || IsIconic(target.Window) || !GetWindowRect(target.Window, out var rect)) return false;
        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public static Task<FileDialogTarget?> DetectAsync(IntPtr sourceWindow, CancellationToken token = default, Action<string>? diagnostic = null)
        => FileDialogProbe.DetectAsync(sourceWindow, token, diagnostic);

    internal static Task<FileDialogTarget?> DetectInWorkerAsync(IntPtr sourceWindow)
        => RunAsync(_ => IsPossibleDialog(sourceWindow) ? Detect(sourceWindow, Console.Error.WriteLine) : null, CancellationToken.None);

    private static FileDialogTarget? Detect(IntPtr source, Action<string>? diagnostic)
    {
        source = ResolveDialogWindow(source);
        if (source == IntPtr.Zero) return null;
        if (!IsWindowVisible(source)) return null;
        GetWindowThreadProcessId(source, out var pid);
        if (pid == Environment.ProcessId) return null;
        using var process = Process.GetProcessById((int)pid);
        var wps = WpsProcesses.Contains(process.ProcessName);
        var className = new StringBuilder(256);
        GetClassName(source, className, className.Capacity);
        if (!wps && className.ToString() != "#32770") return null;
        if (className.ToString() == "#32770")
        {
            var nativeEdit = FindNativeFileName(source);
            var accept = GetDlgItem(source, 1);
            var nativeCancel = GetDlgItem(source, 2);
            if (nativeEdit != IntPtr.Zero && IsWindowVisible(accept) && IsWindowVisible(nativeCancel)
                && (IsWindowVisible(GetDlgItem(source, 1136)) || IsWindowVisible(GetDlgItem(source, 1149))))
            {
                var editElement = AutomationElement.FromHandle(nativeEdit);
                var acceptText = new StringBuilder(128);
                var title = new StringBuilder(256);
                GetWindowText(accept, acceptText, acceptText.Capacity);
                GetWindowText(source, title, title.Capacity);
                return new(source, pid, wps, Has(acceptText.ToString(), "保存", "save"), title.ToString())
                    { FileNameRuntimeId = editElement.GetRuntimeId(), FileNameWindow = nativeEdit };
            }
        }
        var element = AutomationElement.FromHandle(source);
        var dialogControls = EnumerateDialogControls(element).ToArray();
        var edit = FindFileName(element, dialogControls);
        if (edit == null) { diagnostic?.Invoke("No visible, enabled, writable filename edit."); return null; }
        var controls = dialogControls.Where(c => c.Current.ControlType == ControlType.Button
            || c.Current.ControlType == ControlType.SplitButton || c.Current.ControlType == ControlType.ComboBox).ToArray();
        var visibleControls = controls.Where(IsVisible).ToArray();
        if (diagnostic != null)
            foreach (var control in controls)
                diagnostic($"{control.Current.ControlType.ProgrammaticName}: id={control.Current.AutomationId}, visible={IsVisible(control)}, name={control.Current.Name}");
        var names = visibleControls.Select(c => c.Current.Name).ToArray();
        var save = names.Any(n => Has(n, "保存", "另存为", "save"));
        var open = names.Any(n => Has(n, "打开", "open"));
        var cancel = names.Any(n => Has(n, "取消", "cancel"));
        var fileType = names.Any(n => Has(n, "文件类型", "保存类型", "file type", "files of type", "save as type"));
        // The Kcfd picker omits Cancel and the file-type combo from UIA. Its named
        // type label and dedicated content/filter/title classes provide the missing
        // evidence; a writable filename and an Open/Save button are still mandatory.
        if (wps)
        {
            var visible = dialogControls.Where(IsVisible).ToArray();
            var pickerTitle = visible.FirstOrDefault(c => c.Current.ClassName == "KcfdTitleLabel")?.Current.Name ?? "";
            var knownPicker = IsWpsCustomPickerStructure(visible.Select(c => c.Current.ClassName), pickerTitle);
            var typeLabel = visible.Any(c => c.Current.ControlType == ControlType.Text
                && Has(c.Current.Name, "文件类型", "保存类型", "file type", "files of type", "save as type"));
            if (knownPicker && typeLabel) { fileType = true; cancel = true; }
        }
        // Common item dialogs use a SplitButton for IDOK. Their captions can also be
        // customized (e.g. Select/Upload) or localized; the standard IDs stay stable.
        if (!wps && className.ToString() == "#32770")
        {
            var standardFileType = visibleControls.Any(c => c.Current.ControlType == ControlType.ComboBox
                && c.Current.AutomationId is "1136" or "1149");
            // Some common-dialog providers omit IDOK entirely from their UIA tree,
            // although the native split button is visible. Inspect its HWND as well.
            var acceptWindow = GetDlgItem(source, 1);
            var cancelWindow = GetDlgItem(source, 2);
            var accept = IsWindowVisible(acceptWindow) || visibleControls.Any(c =>
                (c.Current.ControlType == ControlType.Button || c.Current.ControlType == ControlType.SplitButton) && c.Current.AutomationId == "1");
            var dismiss = IsWindowVisible(cancelWindow) || visibleControls.Any(c => c.Current.ControlType == ControlType.Button && c.Current.AutomationId == "2");
            var caption = new StringBuilder(128);
            if (acceptWindow != IntPtr.Zero) GetWindowText(acceptWindow, caption, caption.Capacity);
            save |= Has(caption.ToString(), "保存", "另存为", "save");
            if (standardFileType && accept && dismiss) { open = !save; cancel = true; }
        }
        // Neither a WPS document nor a generic #32770 message/login box should be a target.
        if (!(save || open) || !cancel || (wps && !fileType))
        { diagnostic?.Invoke($"Control check: WPS={wps}, open={open}, save={save}, cancel={cancel}, fileType={fileType}, visible={visibleControls.Length}"); return null; }
        return new FileDialogTarget(source, pid, wps, save, element.Current.Name)
            { FileNameRuntimeId = edit.GetRuntimeId(), FileNameWindow = new IntPtr(edit.Current.NativeWindowHandle) };
    }

    public static async Task NavigateAsync(FileDialogTarget target, string folder, CancellationToken token = default)
    {
        // Validate before bringing another application to the foreground.
        var directory = await RunAsync(_ => NormalizeExistingDirectory(folder), token);
        if (!IsLiveWindow(target)) throw new InvalidOperationException("原打开/保存对话框已关闭，请重新呼出搜索。");
        WindowActivator.TryForceForeground(target.Window);
        // A shell provider can take several seconds to expose the new breadcrumb.
        // This is only an explicit user navigation, not background detection.
        await RunAsync(ct => Navigate(target, directory, ct), token, TimeSpan.FromSeconds(15));
    }

    public static string NormalizeExistingDirectory(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)
            || folder.IndexOfAny(['\0', '\r', '\n', '"', '*', '?']) >= 0)
            throw new ArgumentException("请选择有效的完整文件夹路径。");
        var path = Path.GetFullPath(folder);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("文件夹不存在或暂时无法访问。");
        return Path.TrimEndingDirectorySeparator(path) + (Path.EndsInDirectorySeparator(Path.TrimEndingDirectorySeparator(path)) ? "" : "\\");
    }

    private static bool Navigate(FileDialogTarget target, string directory, CancellationToken token)
    {
        var navigationClock = Stopwatch.StartNew();
        void Trace(string stage) => NavigationDiagnostic?.Invoke($"Navigation {navigationClock.ElapsedMilliseconds}ms: {stage}");
        token.ThrowIfCancellationRequested();
        Trace("root");
        var root = GetLiveRoot(target);
        Trace("filename lookup");
        var fileName = FindTargetFileName(target, root) ?? throw new InvalidOperationException("找不到原对话框的文件名输入框。");
        Trace("filename found");
        if (!fileName.GetRuntimeId().SequenceEqual(target.FileNameRuntimeId))
            throw new InvalidOperationException("文件对话框已更换，请重新呼出搜索。");
        var original = Value(fileName);
        Trace("filename captured");
        WaitForKeysReleased(token);
        RequireForeground(target);

        // Standard Windows pickers: an address bar keeps the filename completely untouched.
        if (!target.IsWps)
        {
            SendChord(0x11, 0x4C); // Ctrl+L: address bar; avoid Listary's global Alt+D.
            Trace("address chord sent");
            Thread.Sleep(100);
            token.ThrowIfCancellationRequested();
            RequireForeground(target);
            var nativeFocus = FindNativeAddress(target.Window);
            for (var attempt = 0; nativeFocus == IntPtr.Zero && attempt < 10; attempt++)
            {
                token.ThrowIfCancellationRequested(); RequireForeground(target);
                Thread.Sleep(100);
                nativeFocus = FindNativeAddress(target.Window);
            }
            if (nativeFocus == IntPtr.Zero) nativeFocus = GetNativeFocus(target);
            var focused = nativeFocus != IntPtr.Zero ? AutomationElement.FromHandle(nativeFocus) : AutomationElement.FocusedElement;
            Trace("address focus read");
            Trace($"focus type={focused.Current.ControlType.ProgrammaticName}, name={focused.Current.Name}, id={focused.Current.AutomationId}, native={nativeFocus}");
            if ((focused.Current.ControlType == ControlType.Edit || focused.Current.ControlType == ControlType.ComboBox) && BelongsTo(focused, root)
                && !focused.GetRuntimeId().SequenceEqual(fileName.GetRuntimeId())
                && Has(focused.Current.Name, "地址", "address"))
            {
                focused.SetFocus();
                SetValue(focused, directory);
                Trace("address set");
                RequireFocused(target, focused, directory);
                Trace("address focus verified");
                token.ThrowIfCancellationRequested();
                SendEnter();
                Trace("address enter sent");
                for (var i = 0; i < 30; i++)
                {
                    Thread.Sleep(60);
                    token.ThrowIfCancellationRequested();
                    RequireForeground(target);
                    if (AddressMatches(root, directory))
                    {
                        Trace("address confirmed");
                        RestoreFileName(target, original, directory, token);
                        Trace("filename restored");
                        return true;
                    }
                }
                // Do not try a second strategy after Enter: navigation may still be in flight.
                throw new InvalidOperationException("已发送目录跳转，但未确认新地址；请检查原对话框后重试。");
            }
            // A standard Save dialog may submit its cached filename when Enter is sent to
            // the filename field. Never use the WPS-specific fallback for these dialogs.
            throw new InvalidOperationException("无法确认地址栏焦点，已停止跳转，未发送打开/保存按键。");
        }

        // WPS Qt pickers and legacy Windows dialogs accept an existing directory + trailing slash
        // in the filename field. Never insert a filename or click Save/Open programmatically.
        token.ThrowIfCancellationRequested();
        RequireForeground(target);
        fileName.SetFocus();
        SetValue(fileName, directory);
        try
        {
            RequireFocused(target, fileName, directory);
            token.ThrowIfCancellationRequested();
            SendEnter();
            var consumed = false;
            for (var i = 0; i < 40; i++)
            {
                Thread.Sleep(60);
                token.ThrowIfCancellationRequested();
                RequireForeground(target);
                var current = FindFileName(GetLiveRoot(target));
                if (current != null && Value(current) != directory) { consumed = true; break; }
            }
            if (!consumed) throw new InvalidOperationException("对话框未响应目录跳转，请确认它处于本地文件选择界面。");
            RestoreFileName(target, original, directory, token);
            return true;
        }
        catch
        {
            // Only undo our own injected value; never overwrite subsequent user input.
            if (IsLiveWindow(target) && GetForegroundWindow() == target.Window)
            {
                try { var edit = FindFileName(GetLiveRoot(target)); if (edit != null && Value(edit) == directory) SetValue(edit, original); }
                catch { /* Closed or changed while recovering. */ }
            }
            throw;
        }
    }

    private static void RestoreFileName(FileDialogTarget target, string original, string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireForeground(target);
        var edit = FindTargetFileName(target, GetLiveRoot(target)) ?? throw new InvalidOperationException("跳转后未找到文件名输入框。");
        var value = Value(edit);
        // WPS Save As restores the original basename without its selected file-type
        // extension when changing directories. This is a known provider rewrite.
        var wpsBasename = target.IsWps && target.IsSave && Path.GetFileName(original) == original
            && Path.HasExtension(original) && value == Path.GetFileNameWithoutExtension(original);
        if (value != original && value != "" && value != directory && value != "*.*" && !wpsBasename)
            throw new InvalidOperationException("文件名已被修改，已停止自动操作。");
        if (value != original) SetValue(edit, original);
        edit.SetFocus();
    }

    private static IEnumerable<AutomationElement> EnumerateDialogControls(AutomationElement root)
    {
        // One provider-side query is much faster than cross-process calls for every tree node.
        // Only picker controls are returned; process isolation supplies the hard time bound.
        var kinds = new[] { ControlType.Edit, ControlType.Button, ControlType.SplitButton, ControlType.ComboBox, ControlType.Text };
        var conditions = kinds.Select(kind => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, kind))
            .Concat(new[] { "KcfdFileDialogContentWidget", "KcfdFilterWidget" }.Select(name =>
                (Condition)new PropertyCondition(AutomationElement.ClassNameProperty, name))).ToArray();
        var condition = new OrCondition(conditions);
        return root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>().Take(256);
    }
    public static bool IsWpsCustomPickerStructure(IEnumerable<string> classes, string title)
    {
        var names = classes.ToHashSet(StringComparer.Ordinal);
        return IsWpsPickerCaption(title) && names.Contains("KcfdFileDialogContentWidget")
            && names.Contains("KcfdFilterWidget") && names.Contains("KcfdTitleLabel");
    }
    private static AutomationElement? FindFileName(AutomationElement root, IEnumerable<AutomationElement>? controls = null)
    {
        if (root.Current.ClassName == "#32770")
        {
            var native = FindNativeFileName(new IntPtr(root.Current.NativeWindowHandle));
            if (native != IntPtr.Zero) return AutomationElement.FromHandle(native);
        }
        var edits = controls == null
            ? root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>()
            : controls.Where(c => c.Current.ControlType == ControlType.Edit);
        foreach (AutomationElement edit in edits)
        {
            if (!IsVisible(edit) || !edit.Current.IsEnabled || !edit.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
                || ((ValuePattern)value).Current.IsReadOnly) continue;
            var parent = TreeWalker.ControlViewWalker.GetParent(edit);
            var standardNameControl = root.Current.ClassName == "#32770"
                && (edit.Current.AutomationId is "1148" or "1152"
                    || (parent?.Current.ControlType == ControlType.ComboBox && parent.Current.AutomationId == "1148"));
            if (standardNameControl || Has(edit.Current.Name, "文件名", "文件名称", "file name", "filename")
                || (parent != null && parent.Current.ControlType == ControlType.ComboBox
                    && Has(parent.Current.Name, "文件名", "文件名称", "file name", "filename")))
                return edit;
        }
        return null;
    }

    private static AutomationElement? FindTargetFileName(FileDialogTarget target, AutomationElement root)
    {
        if (target.FileNameWindow != IntPtr.Zero && IsWindowVisible(target.FileNameWindow)
            && IsChild(target.Window, target.FileNameWindow) && GetProcessId(target.FileNameWindow) == target.ProcessId)
        {
            var edit = AutomationElement.FromHandle(target.FileNameWindow);
            if (edit.GetRuntimeId().SequenceEqual(target.FileNameRuntimeId)) return edit;
        }
        return FindFileName(root);
    }

    private static IntPtr FindNativeFileName(IntPtr dialog)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(dialog, (window, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "Edit" || !IsWindowVisible(window) || !IsWindowEnabled(window)) return true;
            var id = GetDlgCtrlID(window);
            var parent = GetParent(window);
            if (id is 1148 or 1152 || GetDlgCtrlID(parent) == 1148 || GetDlgCtrlID(GetParent(parent)) == 1148)
            { result = window; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr FindNativeAddress(IntPtr dialog)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(dialog, (window, _) =>
        {
            if (!IsWindowVisible(window) || !IsWindowEnabled(window)) return true;
            var parent = GetParent(window);
            if (GetDlgCtrlID(window) != 41477 && GetDlgCtrlID(parent) != 41477 && GetDlgCtrlID(GetParent(parent)) != 41477) return true;
            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "Edit") { result = window; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static bool AddressMatches(AutomationElement root, string directory)
    {
        var nativeToolbars = new List<IntPtr>();
        EnumChildWindows(new IntPtr(root.Current.NativeWindowHandle), (window, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "ToolbarWindow32" && IsWindowVisible(window)) nativeToolbars.Add(window);
            return true;
        }, IntPtr.Zero);
        var normalized = Path.TrimEndingDirectorySeparator(directory);
        var hasNativeAddress = false;
        foreach (var window in nativeToolbars)
        {
            var name = AutomationElement.FromHandle(window).Current.Name;
            if (!Has(name, "地址", "address")) continue;
            var colon = name.IndexOfAny([':', '：']);
            if (colon < 0) continue;
            hasNativeAddress = true;
            if (string.Equals(Path.TrimEndingDirectorySeparator(name[(colon + 1)..].Trim()), normalized, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (hasNativeAddress) return false;
        var elements = root.FindAll(TreeScope.Descendants,
            new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
        foreach (AutomationElement element in elements)
        {
            var name = element.Current.Name;
            if (!Has(name, "地址", "address")) continue;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)
                && string.Equals(Path.TrimEndingDirectorySeparator(((ValuePattern)value).Current.Value), normalized, StringComparison.OrdinalIgnoreCase)) return true;
            var colon = name.IndexOfAny([':', '：']);
            if (colon >= 0 && string.Equals(Path.TrimEndingDirectorySeparator(name[(colon + 1)..].Trim()), normalized, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsVisible(AutomationElement element) => !element.Current.IsOffscreen;
    private static bool Has(string text, params string[] fragments) => fragments.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase));
    private static string Value(AutomationElement edit) => ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    private static void SetValue(AutomationElement edit, string text) => ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
    private static bool BelongsTo(AutomationElement element, AutomationElement root)
    {
        var rootHandle = new IntPtr(root.Current.NativeWindowHandle);
        var elementHandle = new IntPtr(element.Current.NativeWindowHandle);
        if (rootHandle != IntPtr.Zero && elementHandle != IntPtr.Zero && IsChild(rootHandle, elementHandle)) return true;
        for (var depth = 0; element != null && depth < 32; depth++, element = TreeWalker.ControlViewWalker.GetParent(element))
            if (Automation.Compare(element, root)) return true;
        return false;
    }
    public static bool IsLiveWindow(FileDialogTarget target)
    {
        GetWindowThreadProcessId(target.Window, out var pid);
        return pid == target.ProcessId && IsWindowVisible(target.Window);
    }
    private static AutomationElement GetLiveRoot(FileDialogTarget target)
    {
        if (!IsLiveWindow(target)) throw new InvalidOperationException("原文件对话框已关闭。");
        return AutomationElement.FromHandle(target.Window);
    }
    private static void RequireForeground(FileDialogTarget target)
    {
        if (!IsLiveWindow(target) || GetForegroundWindow() != target.Window)
            throw new InvalidOperationException("焦点已离开原文件对话框，已停止跳转。");
    }
    private static void RequireFocused(FileDialogTarget target, AutomationElement edit, string value)
    {
        // Qt providers publish keyboard focus asynchronously after SetFocus/SetValue.
        for (var i = 0; i < 20; i++)
        {
            RequireForeground(target);
            if (!target.IsWps && edit.Current.NativeWindowHandle != 0
                && GetNativeFocus(target) == new IntPtr(edit.Current.NativeWindowHandle) && Value(edit) == value) return;
            var focused = AutomationElement.FocusedElement;
            var parent = TreeWalker.ControlViewWalker.GetParent(edit);
            // WPS reports the filename ComboBox as FocusedElement while its child Edit
            // reports HasKeyboardFocus. Accept only that exact parent/child pair.
            var focusedComboEdit = target.IsWps && edit.Current.HasKeyboardFocus && parent != null
                && parent.Current.ControlType == ControlType.ComboBox && Automation.Compare(focused, parent);
            if ((Automation.Compare(focused, edit) || focusedComboEdit) && Value(edit) == value) return;
            Thread.Sleep(25);
        }
        throw new InvalidOperationException("无法确认目录输入框的焦点，已停止跳转。");
    }

    private static IntPtr GetNativeFocus(FileDialogTarget target)
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) && IsChild(target.Window, info.Focus) ? info.Focus : IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public int Size; public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public NativeRect CaretBounds;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    private static void WaitForKeysReleased(CancellationToken token)
    {
        for (var i = 0; i < 40; i++)
        {
            token.ThrowIfCancellationRequested();
            if (new[] { 0x10, 0x11, 0x12, 0x0D, 0x5B, 0x5C, 0x01, 0x02, 0x04 }.All(k => (GetAsyncKeyState(k) & 0x8000) == 0)) return;
            Thread.Sleep(25);
        }
        throw new InvalidOperationException("请松开鼠标按键和快捷键后重试。");
    }

    private static async Task<T> RunAsync<T>(Func<CancellationToken, T> operation, CancellationToken token, TimeSpan? timeout = null)
    {
        if (!await AutomationGate.WaitAsync(0, token)) throw new InvalidOperationException("上一次对话框操作尚未完成，请稍后重试。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        var ct = deadline.Token;
        var task = Task.Run(() => { try { ct.ThrowIfCancellationRequested(); return operation(ct); } finally { AutomationGate.Release(); } });
        // UIA providers can be unresponsive; keep the UI usable and bound concurrent worker creation.
        return await task.WaitAsync(ct);
    }

    private static void SendEnter() => SendKeys([Key(0x0D), Key(0x0D, true)]);
    private static void SendChord(ushort modifier, ushort key) => SendKeys([Key(modifier), Key(key), Key(key, true), Key(modifier, true)]);
    private static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } } };
    private static void SendKeys(Input[] keys)
    {
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) != keys.Length)
            throw new InvalidOperationException("无法发送按键，请确认 Quickstart 与目标程序权限一致。");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
