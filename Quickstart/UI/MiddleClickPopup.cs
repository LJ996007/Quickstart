namespace Quickstart.UI;

using System.Runtime.InteropServices;
using Quickstart.Utils;

/// <summary>
/// Middle-click group: a standalone floating search bar above two equal-height columns,
/// each kept as its own frameless window. Like the gestures, the action panel opens to
/// the left of the cursor and the quick-launch list to the right. A click needs no drag
/// continuity, so the group always uses the list rather than the radial ring.
/// </summary>
internal sealed class MiddleClickPopup : Form
{
    private const string SearchHint = "Everything · Enter 搜索 · 单击条目执行 · Esc 关闭";
    private readonly TextBox _query = new()
    {
        PlaceholderText = "搜索文件或文件夹",
        BorderStyle = BorderStyle.None,
    };
    private readonly Label _searchIcon = new()
    {
        Text = "",
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTheme.TextMuted,
    };
    private readonly Label _search = new()
    {
        // The placeholder hides while focused, so the hint names the search engine too.
        // It also replaces the action panel's footer hint, which is hidden in this layout.
        Text = SearchHint,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = UiTheme.TextMuted,
        Cursor = Cursors.Hand,
    };
    private readonly Panel _bar = new() { BackColor = UiTheme.Card, Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 150 };
    private MainPopup? _launcher;
    private AiActionPickerPopup? _actions;
    private bool _arranging;
    private bool _closing;
    private bool _layoutPending;
    // Panel positions relative to the search bar's top-left; dragging the bar moves the group.
    private Point _launcherOffset;
    private Point _actionOffset;
    private Point _anchorOffset;
    private bool _hasLayout;
    private Point _anchor;
    private (string Device, Rectangle Area, int Dpi)? _layoutEnvironment;

    internal IntPtr SourceWindow { get; private set; }
    public event Action<string, IntPtr>? SearchRequested;

    public MiddleClickPopup()
    {
        Text = "Quickstart · 中键面板";
        Font = UiTheme.CreateFont(10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = UiTheme.Border;
        Padding = new Padding(1);
        FormStyler.ApplyRounded(this);
        _searchIcon.Font = new Font("Segoe MDL2 Assets", 10f);
        _search.Font = UiTheme.CreateFont(8.5f);
        _query.BackColor = UiTheme.Card;
        _query.ForeColor = UiTheme.TextPrimary;
        _bar.Controls.AddRange([_searchIcon, _query, _search]);
        _bar.Layout += (_, _) => LayoutBar();
        Controls.Add(_bar);
        _search.Click += (_, _) => SubmitSearch();
        _query.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SubmitSearch();
        };
        // The bar has no title bar; its empty area drags the whole group.
        foreach (var handle in new Control[] { _bar, _searchIcon })
            handle.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            };
        LocationChanged += (_, _) => MovePanels();
        _focusTimer.Tick += (_, _) => CheckFocus();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true; HidePanels();
        };
    }

    private void LayoutBar()
    {
        int S(int value) => UiScaleHelper.Scale(this, value);
        var size = _bar.ClientSize;
        _searchIcon.Bounds = new Rectangle(S(6), 0, S(34), size.Height);
        _search.Text = TextRenderer.MeasureText(SearchHint, _search.Font).Width + S(240) > size.Width
            ? "Enter 搜索" : SearchHint;
        var hintWidth = TextRenderer.MeasureText(_search.Text, _search.Font).Width + S(14);
        _search.Bounds = new Rectangle(size.Width - hintWidth - S(4), 0, hintWidth, size.Height);
        _query.Bounds = new Rectangle(_searchIcon.Right + S(2), (size.Height - _query.PreferredHeight) / 2,
            Math.Max(S(40), _search.Left - _searchIcon.Right - S(8)), _query.PreferredHeight);
    }

    internal void ShowPanels(MainPopup launcher, AiActionPickerPopup actions, Point point, IntPtr sourceWindow)
    {
        PreparePanels(launcher, actions, point, sourceWindow);
        _arranging = true;
        try
        {
            Attach(launcher); Attach(actions);
            // All three windows already have their final bounds. Show them without
            // extra slide/focus cycles, then give keyboard focus to the search bar.
            Show();
            launcher.Show();
            actions.Show();
            actions.EnterInteractiveMode();
            WindowActivator.TryForceForeground(Handle);
            _query.Focus();
            _focusTimer.Start();
        }
        catch { HidePanels(); throw; }
        finally { _arranging = false; }
    }

    internal void Prewarm(MainPopup launcher, AiActionPickerPopup actions, Point point)
    {
        var foreground = GetForegroundWindow();
        try
        {
            PreparePanels(launcher, actions, point, IntPtr.Zero);
            CreateHiddenHandles(this);
            CreateHiddenHandles(launcher);
            CreateHiddenHandles(actions);
        }
        finally
        {
            HidePanels();
            // Native handle creation can select a hidden top-level window. Only undo
            // activation of our own windows; never override a user switching elsewhere.
            var current = GetForegroundWindow();
            if (foreground != IntPtr.Zero && current != foreground
                && (current == Handle || current == launcher.Handle || current == actions.Handle))
                WindowActivator.TryForceForeground(foreground);
        }
    }

    private static void CreateHiddenHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
    }

    private void PreparePanels(MainPopup launcher, AiActionPickerPopup actions, Point point, IntPtr sourceWindow)
    {
        HidePanels();
        _arranging = true;
        _launcher = launcher;
        _actions = actions;
        SourceWindow = sourceWindow;
        _anchor = point;
        _hasLayout = false;
        _query.Clear();
        try
        {
            var screen = Screen.FromPoint(point);
            UiScaleHelper.MoveIntoWorkingArea(this, screen);
            launcher.MiddleClickHost = this;
            actions.MiddleClickHost = this;
            var environment = (screen.DeviceName, screen.WorkingArea, UiScaleHelper.GetDpiForScreen(screen));
            if (_layoutEnvironment != environment)
            {
                launcher.FitMiddleClickPanel(null);
                actions.FitMiddleClickPanel(null);
                _layoutEnvironment = environment;
            }
            launcher.PrepareAtGesturePoint(point, sourceWindow);
            actions.PrepareAtGesturePoint(point);
            ArrangePanels();
        }
        catch { HidePanels(); throw; }
        finally { _arranging = false; }
    }

    private void Attach(Form panel)
    {
        panel.Owner = this;
        panel.VisibleChanged += PanelVisibilityChanged;
        panel.SizeChanged += PanelSizeChanged;
    }

    private void PanelVisibilityChanged(object? sender, EventArgs e)
    {
        // Existing action handlers hide their panel before opening/pasting. Dismiss
        // the entire group synchronously so the original application can take focus.
        if (!_arranging && !_closing && sender is Form { Visible: false }) HidePanels();
    }

    private void PanelSizeChanged(object? sender, EventArgs e)
    {
        // Child SizeChanged also fires in the middle of WinForms DPI scaling.
        // Wait until the entire window tree is scaled before sizing the group.
        QueueDisplayLayout();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        UiScaleHelper.InvalidateDeviceDpiCache();
        base.OnDpiChanged(e);
        QueueDisplayLayout();
    }

    private void QueueDisplayLayout()
    {
        if (!Visible || _arranging || _closing || _layoutPending || IsDisposed) return;
        _layoutPending = true;
        BeginInvoke(() =>
        {
            _layoutPending = false;
            if (!IsDisposed) HandleDisplayEnvironmentChanged();
        });
    }

    private void ArrangePanels()
    {
        if (_launcher == null || _actions == null) return;
        var wasArranging = _arranging;
        _arranging = true;
        try
        {
            var area = Screen.FromPoint(_anchor).WorkingArea;
            int S(int value) => UiScaleHelper.Scale(this, value);
            var barHeight = Math.Max(S(36), _query.PreferredHeight + S(12));
            var layout = PopupScreenLayout.Arrange(area, _anchor, UiScaleHelper.GetDpi(this),
                _launcher.Size, _actions.Size, barHeight);
            _launcher.FitMiddleClickPanel(layout.Launcher.Size);
            _actions.FitMiddleClickPanel(layout.Actions.Size);
            _actionOffset = layout.Actions.Location - new Size(layout.Bar.Location);
            _launcherOffset = layout.Launcher.Location - new Size(layout.Bar.Location);
            _anchorOffset = layout.AnchorOffset;
            _hasLayout = true;
            Bounds = layout.Bar;
            MovePanels();
        }
        finally { _arranging = wasArranging; }
    }

    private void MovePanels()
    {
        if (_closing || _launcher == null || _actions == null || !_hasLayout) return;
        _launcher.Location = Location + new Size(_launcherOffset);
        _actions.Location = Location + new Size(_actionOffset);
    }

    internal void HandleDisplayEnvironmentChanged()
    {
        if (!Visible || _arranging) return;
        _anchor = Location + new Size(_anchorOffset);
        ArrangePanels();
    }

    private void CheckFocus()
    {
        if (!Visible || _arranging || _closing) return;
        if (_launcher is MainPopup { IsContextInteraction: true }) return;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        // Context menus and edit dialogs can be owned by either child panel.
        var root = GetAncestor(window, 2); // GA_ROOT
        for (var i = 0; root != IntPtr.Zero && i < 16; i++, root = GetWindow(root, 4)) // GW_OWNER
            if (root == Handle || root == _launcher?.Handle || root == _actions?.Handle) return;
        HidePanels();
    }

    private void SubmitSearch()
    {
        var query = _query.Text.Trim();
        var source = SourceWindow;
        HidePanels();
        SearchRequested?.Invoke(query, source);
    }

    internal void HidePanels()
    {
        if (_closing) return;
        _closing = true;
        try
        {
            _focusTimer.Stop();
            foreach (var panel in new Form?[] { _launcher, _actions })
            {
                if (panel == null || panel.IsDisposed) continue;
                panel.VisibleChanged -= PanelVisibilityChanged;
                panel.SizeChanged -= PanelSizeChanged;
                panel.Hide();
                panel.Owner = null;
                switch (panel)
                {
                    case MainPopup main: main.MiddleClickHost = null; break;
                    case AiActionPickerPopup actions: actions.MiddleClickHost = null; break;
                }
            }
            _launcher = null; _actions = null;
            Hide();
        }
        finally { _closing = false; }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { HidePanels(); return true; }
        if (keyData is (Keys.Control | Keys.F) or (Keys.Control | Keys.L)) { _query.Focus(); _query.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { HidePanels(); _focusTimer.Dispose(); }
        base.Dispose(disposing);
    }

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
