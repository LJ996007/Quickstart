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
        Text = "Everything · Enter 搜索 · 单击条目执行 · Esc 关闭",
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
    // Panel positions relative to the search bar's top-left; dragging the bar moves the group.
    private Point _launcherOffset;
    private Point _actionOffset;
    private Point _anchorOffset;
    private bool _hasLayout;
    private Point _anchor;

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
        DpiChanged += (_, _) => { if (Visible && !_arranging) BeginInvoke(HandleDisplayEnvironmentChanged); };
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
        var hintWidth = TextRenderer.MeasureText(_search.Text, _search.Font).Width + S(14);
        _search.Bounds = new Rectangle(size.Width - hintWidth - S(4), 0, hintWidth, size.Height);
        _query.Bounds = new Rectangle(_searchIcon.Right + S(2), (size.Height - _query.PreferredHeight) / 2,
            Math.Max(S(40), _search.Left - _searchIcon.Right - S(8)), _query.PreferredHeight);
    }

    internal void ShowPanels(MainPopup launcher, AiActionPickerPopup actions, Point point, IntPtr sourceWindow)
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
            Attach(launcher); Attach(actions);
            UiScaleHelper.MoveIntoWorkingArea(this, Screen.FromPoint(point));
            launcher.MiddleClickHost = this; launcher.FitMiddleClickPanel(null);
            launcher.ShowAtGesturePoint(point, sourceWindow);
            launcher.EnterInteractiveMode();
            actions.MiddleClickHost = this; actions.FitMiddleClickPanel(null);
            actions.ShowAtGesturePoint(point);
            actions.EnterInteractiveMode();
            ArrangePanels();
            WindowActivator.ClaimForegroundRights();
            Show();
            MovePanels();
            // Same motion as the gestures: actions slide in leftwards, the list rightwards.
            PopupMotion.SlideIn(actions, direction: -1);
            PopupMotion.SlideIn(launcher, direction: +1);
            WindowActivator.TryForceForeground(Handle);
            _query.Focus();
            _focusTimer.Start();
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
        if (Visible && !_arranging && !_closing) ArrangePanels();
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
            var margin = S(8); var gap = S(12);
            var barHeight = Math.Max(S(36), _query.PreferredHeight + S(12));
            var widthLimit = Math.Max(1, area.Width - margin * 2);
            var bodyLimit = Math.Max(S(180), area.Height - margin * 2 - barHeight - gap);
            var launchMin = S(300);
            var actionMin = S(320);
            var top = barHeight + gap;
            int groupWidth, groupHeight, splitX;
            Point actionAt, launcherAt;
            if (widthLimit >= launchMin + actionMin + gap)
            {
                // Mirror the gestures: actions on the left of the cursor, quick launch on the right.
                // Both columns share the taller natural height so their edges line up.
                var height = Math.Min(bodyLimit, Math.Max(_launcher.Height, _actions.Height));
                var launchWidth = Math.Clamp(_launcher.Width, launchMin, widthLimit - actionMin - gap);
                var actionWidth = Math.Clamp(_actions.Width, actionMin, widthLimit - launchWidth - gap);
                _launcher.FitMiddleClickPanel(new Size(launchWidth, height));
                _actions.FitMiddleClickPanel(new Size(actionWidth, height));
                actionAt = new Point(0, top);
                launcherAt = new Point(actionWidth + gap, top);
                groupWidth = actionWidth + gap + launchWidth;
                groupHeight = top + height;
                splitX = actionWidth + gap / 2;
            }
            else
            {
                // Narrow displays: stack the same complete panels at full width; their lists scroll.
                var launchHeight = Math.Max(S(180), (bodyLimit - gap) / 2);
                _launcher.FitMiddleClickPanel(new Size(widthLimit, launchHeight));
                _actions.FitMiddleClickPanel(new Size(widthLimit, Math.Max(S(120), bodyLimit - gap - launchHeight)));
                launcherAt = new Point(0, top);
                actionAt = new Point(0, top + _launcher.Height + gap);
                groupWidth = widthLimit;
                groupHeight = actionAt.Y + _actions.Height;
                splitX = groupWidth / 2;
            }
            // The group's split point sits under the cursor, then the whole group stays on screen.
            var groupLeft = Math.Clamp(_anchor.X - splitX, area.Left + margin, Math.Max(area.Left + margin, area.Right - margin - groupWidth));
            var groupTop = Math.Clamp(_anchor.Y - top - S(36), area.Top + margin, Math.Max(area.Top + margin, area.Bottom - margin - groupHeight));
            // The bar spans both columns so the three pieces read as one grid.
            _actionOffset = actionAt;
            _launcherOffset = launcherAt;
            _anchorOffset = new Point(splitX, top + S(36));
            _hasLayout = true;
            Bounds = new Rectangle(groupLeft, groupTop, groupWidth, barHeight);
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
                    case MainPopup main: main.MiddleClickHost = null; main.FitMiddleClickPanel(null); break;
                    case AiActionPickerPopup actions: actions.MiddleClickHost = null; actions.FitMiddleClickPanel(null); break;
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
