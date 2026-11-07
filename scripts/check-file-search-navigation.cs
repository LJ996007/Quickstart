// Isolated WinForms integration harness; no Everything queries or external file/folder launches.
// Run: powershell -NoProfile -File scripts/check-file-search-navigation.ps1
// Add -Interactive to open the visual fixture without running focus-dependent checks.
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.UI;

static class FileSearchNavigationChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    static T Property<T>(object owner, string name) => (T)owner.GetType().GetProperty(name, Private)!.GetValue(owner)!;
    static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Private)!.SetValue(owner, value);
    static object? Call(object owner, string name, params object?[] args)
    {
        try { return owner.GetType().GetMethod(name, Private)!.Invoke(owner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static Task CallAsync(object owner, string name, params object?[] args) => (Task)Call(owner, name, args)!;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static async Task Until(Func<bool> condition, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException(message);
            await Task.Delay(25);
        }
    }
    static string? Folder(FileSearchPopup popup) => Field<string?>(popup, "_folder");
    static FileSearchResult[] Items(FileSearchPopup popup) => Field<ListView>(popup, "_results").Items
        .Cast<ListViewItem>().Select(item => (FileSearchResult)item.Tag!).ToArray();
    static string? Selected(FileSearchPopup popup) => Field<ListView>(popup, "_results").SelectedItems
        .Cast<ListViewItem>().Select(item => ((FileSearchResult)item.Tag!).FullPath).FirstOrDefault();
    static void Select(FileSearchPopup popup, string path)
    {
        var list = Field<ListView>(popup, "_results");
        list.SelectedItems.Clear();
        var item = list.Items.Cast<ListViewItem>().Single(item => ((FileSearchResult)item.Tag!).FullPath == path);
        item.Selected = true; item.Focused = true; item.EnsureVisible(); list.Focus();
    }
    static bool Key(FileSearchPopup popup, Keys key)
    {
        object?[] args = [Message.Create(popup.Handle, 0x100, IntPtr.Zero, IntPtr.Zero), key];
        return (bool)typeof(FileSearchPopup).GetMethod("ProcessCmdKey", Private)!.Invoke(popup, args)!;
    }
    static string[] MenuLabels(FileSearchPopup popup)
    {
        var menu = Field<ContextMenuStrip>(popup, "_menu");
        var opening = new CancelEventArgs();
        typeof(ContextMenuStrip).GetMethod("OnOpening", Private)!.Invoke(menu, [opening]);
        Check(!opening.Cancel, "Selected result has no context menu");
        return menu.Items.Cast<ToolStripItem>().Where(item => item.Available).Select(item => item.Text ?? "").ToArray();
    }
    static void SeedSearch(FileSearchPopup popup, string root, string empty)
    {
        Set(popup, "_changing", true);
        try
        {
            Field<TextBox>(popup, "_query").Text = "北航 中文";
            Field<ComboBox>(popup, "_filter").SelectedIndex = 2;
            Field<ComboBox>(popup, "_sort").SelectedIndex = 1;
            Set(popup, "_offset", 100u);
        }
        finally { Set(popup, "_changing", false); }
        var page = new FileSearchPage(205, 100,
            [new FileSearchResult(empty, true, null, DateTime.Today), new FileSearchResult(root, true, null, DateTime.Today)]);
        Call(popup, "ShowPage", page, root);
        Check(Selected(popup) == root, "Fixture selection was not set");
    }
    static void CheckRestoredSearch(FileSearchPopup popup, string root)
    {
        Check(Folder(popup) == null && popup.Visible, "Return did not restore visible global search");
        Check(Field<TextBox>(popup, "_query").Text == "北航 中文", "Original search query was lost");
        Check(Field<ComboBox>(popup, "_filter").SelectedIndex == 2 && Field<ComboBox>(popup, "_sort").SelectedIndex == 1,
            "Original filter/sort was lost");
        var page = Field<FileSearchPage>(popup, "_page");
        Check(page.Total == 205 && page.Offset == 100 && Field<uint>(popup, "_offset") == 100 && Items(popup).Length == 2,
            "Original cached page was not restored");
        Check(Selected(popup) == root, "Original selected folder was not restored");
        Check(!Field<TableLayoutPanel>(popup, "_navigation").Visible, "Folder navigation remained visible in global search");
        Check(Field<Button>(popup, "_previous").Enabled && Field<Button>(popup, "_next").Enabled, "Search pagination was not restored");
    }
    static Rectangle InForm(FileSearchPopup popup, Control control)
        => new(popup.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
    static void DumpGeometry(FileSearchPopup popup, string context)
    {
        var layout = Field<TableLayoutPanel>(popup, "_layout");
        Console.WriteLine($"GEOMETRY {context}: dpi={popup.DeviceDpi}; nativeDpi={GetDpiForWindow(popup.Handle)}; client={popup.ClientSize}; padding={popup.Padding}; fontHeight={popup.Font.Height}");
        Console.WriteLine($"LAYOUT bounds={layout.Bounds}; padding={layout.Padding}; styles={string.Join(",", layout.RowStyles.Cast<RowStyle>().Select(style => $"{style.SizeType}:{style.Height}"))}; actual={string.Join(",", layout.GetRowHeights())}");
        foreach (var name in new[] { "_header", "_top", "_navigation", "_footer" })
        {
            var row = Field<TableLayoutPanel>(popup, name);
            Console.WriteLine($"ROW {name}: bounds={row.Bounds}; actual={string.Join(",", row.GetRowHeights())}; margin={row.Margin}");
            foreach (var control in row.Controls.Cast<Control>())
                Console.WriteLine($"  {control.GetType().Name} [{control.AccessibleName ?? control.Text}]: bounds={control.Bounds}; preferred={control.PreferredSize}; dpi={control.DeviceDpi}; margin={control.Margin}");
        }
    }
    static void CheckGeometry(FileSearchPopup popup, string context)
    {
        var rows = new Control[] { Field<TableLayoutPanel>(popup, "_header"), Field<TableLayoutPanel>(popup, "_top"),
            Field<TableLayoutPanel>(popup, "_navigation"), Field<SplitContainer>(popup, "_split"), Field<TableLayoutPanel>(popup, "_footer") };
        var rectangles = rows.Select(control => InForm(popup, control)).ToArray();
        for (var i = 0; i < rows.Length; i++)
        {
            var bounds = rectangles[i];
            Check(bounds.Width > 0 && bounds.Height > 0 && popup.ClientRectangle.Contains(bounds),
                $"{context}: {rows[i].Name}/{i} extends outside client: {bounds}, client={popup.ClientRectangle}");
            Check(Math.Abs(bounds.Left - rectangles[0].Left) <= 1 && Math.Abs(bounds.Right - rectangles[0].Right) <= 1,
                $"{context}: row {i} has misaligned left/right edges: {bounds}, header={rectangles[0]}");
            if (i > 0) Check(rectangles[i - 1].Bottom <= bounds.Top, $"{context}: rows {i - 1} and {i} overlap");
        }
        var targetHeight = (int)Math.Round(36 * popup.DeviceDpi / 96d);
        var controls = new Control[] { Field<TextBox>(popup, "_query").Parent!, Field<ComboBox>(popup, "_filter"),
            Field<ComboBox>(popup, "_sort"), Field<CheckBox>(popup, "_previewEnabled"), Field<TextBox>(popup, "_location").Parent!,
            Field<Button>(popup, "_back"), Field<Button>(popup, "_up"), Field<Button>(popup, "_returnToSearch"),
            Field<Button>(popup, "_previous"), Field<Button>(popup, "_next"), Field<Button>(popup, "_external"),
            Field<Button>(popup, "_maximize"), Field<Button>(popup, "_close") };
        foreach (var control in controls)
        {
            var label = $"{control.GetType().Name} [{control.AccessibleName ?? control.Text}]";
            Check(Math.Abs(control.Height - targetHeight) <= 2,
                $"{context}: {label} height={control.Height}; expected 36 logical pixels ({targetHeight})");
            Check(control.Parent!.ClientRectangle.Contains(control.Bounds),
                $"{context}: {label} clips its parent: {control.Bounds}, parent={control.Parent.ClientRectangle}");
        }
        foreach (var row in rows.OfType<TableLayoutPanel>())
        {
            var children = row.Controls.Cast<Control>().Where(control => control.Visible).OrderBy(control => control.Left).ToArray();
            for (var i = 1; i < children.Length; i++)
                Check(children[i - 1].Right <= children[i].Left,
                    $"{context}: controls overlap in a row: {children[i - 1].Bounds}, {children[i].Bounds}");
        }
        foreach (var textBox in new[] { Field<TextBox>(popup, "_query"), Field<TextBox>(popup, "_location") })
        {
            Check(textBox.Parent!.ClientRectangle.Contains(textBox.Bounds), $"{context}: text input clips its field");
            Check(Math.Abs(textBox.Top - (textBox.Parent.Height - textBox.Height) / 2d) <= 1,
                $"{context}: text input is not vertically centered: {textBox.Bounds}, parent={textBox.Parent.ClientRectangle}");
        }
        var split = Field<SplitContainer>(popup, "_split");
        Check(split.Panel2Collapsed == (split.Width < (int)Math.Round(840 * popup.DeviceDpi / 96d)),
            $"{context}: preview does not adapt to the available width");
        var icons = Field<ImageList>(popup, "_icons");
        foreach (var item in Field<ListView>(popup, "_results").Items.Cast<ListViewItem>())
            Check(icons.Images.ContainsKey(item.ImageKey),
                $"{context}: icon '{item.ImageKey}' was lost while resizing; imageCount={icons.Images.Count}");
    }
    static async Task GeometryChecks(FileSearchPopup popup, string root, string empty)
    {
        SeedSearch(popup, root, empty);
        await CallAsync(popup, "BrowseFolderAsync", root);
        var nativeDpi = (int)GetDpiForWindow(popup.Handle);
        var preview = Field<CheckBox>(popup, "_previewEnabled");
        var filter = Field<ComboBox>(popup, "_filter");
        var sort = Field<ComboBox>(popup, "_sort");
        var expectedFilter = (filter.SelectedIndex, filter.Text);
        var expectedSort = (sort.SelectedIndex, sort.Text);
        void CheckComboSelection(string context)
        {
            Check((filter.SelectedIndex, filter.Text) == expectedFilter,
                $"{context}: filter selection changed from {expectedFilter} to ({filter.SelectedIndex}, {filter.Text})");
            Check((sort.SelectedIndex, sort.Text) == expectedSort,
                $"{context}: sort selection changed from {expectedSort} to ({sort.SelectedIndex}, {sort.Text})");
        }
        // Geometry does not need a document renderer; keep the split visible without starting WebView.
        Set(popup, "_detectingDialog", true);
        preview.Checked = true;
        try
        {
            foreach (var logicalWidth in new[] { 1180, 900, 720 })
            {
                popup.ClientSize = new Size((int)Math.Round(logicalWidth * popup.DeviceDpi / 96d), (int)Math.Round(720 * popup.DeviceDpi / 96d));
                popup.PerformLayout();
                Call(popup, "UpdatePreviewLayout");
                try { CheckGeometry(popup, $"{logicalWidth} logical px at {popup.DeviceDpi} DPI"); }
                catch
                {
                    DumpGeometry(popup, "before manual ApplyScaledLayout");
                    Call(popup, "ApplyScaledLayout");
                    popup.PerformLayout();
                    DumpGeometry(popup, "after manual ApplyScaledLayout");
                    throw;
                }
            }
            foreach (var dpi in new[] { 96, 144, 168, 192, 240, nativeDpi })
            {
                ChangeDpi(popup, dpi);
                popup.ClientSize = new Size((int)Math.Round(900 * popup.DeviceDpi / 96d), (int)Math.Round(720 * popup.DeviceDpi / 96d));
                popup.PerformLayout();
                Call(popup, "UpdatePreviewLayout");
                CheckGeometry(popup, $"Synthetic {dpi} DPI transition (actual control DPI {popup.DeviceDpi})");
                CheckComboSelection($"Synthetic {dpi} DPI transition");
            }
            foreach (var logicalWidth in new[] { 1180, 720 })
            {
                popup.ClientSize = new Size((int)Math.Round(logicalWidth * popup.DeviceDpi / 96d), (int)Math.Round(720 * popup.DeviceDpi / 96d));
                popup.PerformLayout();
                Call(popup, "UpdatePreviewLayout");
                await Task.Delay(100);
                popup.Refresh();
                await Task.Yield();
                CheckGeometry(popup, $"Render {logicalWidth} logical px at {popup.DeviceDpi} DPI");
                CheckComboSelection($"Settled render {logicalWidth}");
                Console.WriteLine($"COMBO render {logicalWidth}: filter=({filter.SelectedIndex}, '{filter.Text}'), sort=({sort.SelectedIndex}, '{sort.Text}'), DPI={popup.DeviceDpi}");
                using var bitmap = new Bitmap(popup.Width, popup.Height);
                popup.DrawToBitmap(bitmap, popup.ClientRectangle);
                bitmap.Save(Path.GetFullPath($"artifacts/debug/checks/file-search-navigation/folder-browser-{logicalWidth}.png"));
            }
            popup.ClientSize = new Size((int)Math.Round(900 * popup.DeviceDpi / 96d), (int)Math.Round(720 * popup.DeviceDpi / 96d));
            Call(popup, "UpdatePreviewLayout");
            await CallAsync(popup, "ReturnToSearchAsync");
            var split = Field<SplitContainer>(popup, "_split");
            split.SplitterDistance = split.Panel1MinSize;
            Call(popup, "UpdatePreviewLayout");
            var list = Field<ListView>(popup, "_results");
            Check(list.Columns[0].Width >= (int)Math.Round(100 * popup.DeviceDpi / 96d),
                $"Filename column is too narrow at the minimum preview split: {list.Columns[0].Width}px at {popup.DeviceDpi} DPI");
        }
        finally
        {
            ChangeDpi(popup, nativeDpi);
            preview.Checked = false;
            Set(popup, "_detectingDialog", false);
        }
        popup.ClientSize = new Size(4000, 2500);
        popup.ShowSearch("");
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Check(area.Contains(popup.Bounds), $"ShowSearch extends outside the work area: popup={popup.Bounds}, area={area}");
        Check(popup.MinimumSize.Width <= area.Width && popup.MinimumSize.Height <= area.Height, "Minimum size exceeds the current work area");
        Console.WriteLine("PASS geometry: aligned rows, uniform controls, no overlap, centered fields, adaptive preview, 100–250% DPI and work-area fit.");
    }
    static async Task Checks(FileSearchPopup popup, string root, string child, string empty, string textFile, string pagingRoot)
    {
        popup.ShowSearch("");
        SeedSearch(popup, root, empty);
        Call(popup, "OpenSelected");
        await Until(() => Folder(popup) == root && Items(popup).Length == 4, "Folder activation did not show its direct children");
        Check(popup.Visible, "Folder activation hid the plugin");
        Check(Items(popup).All(item => Path.GetDirectoryName(item.FullPath) == root), "Folder browser returned recursive descendants");
        Check(Items(popup).Count(item => item.IsFolder) == 2 && Items(popup).Count(item => !item.IsFolder) == 2,
            "Search folder-only filter leaked into the folder listing");
        Check(Field<TextBox>(popup, "_query").Text.Length == 0, "Search words leaked into the folder filter");
        Check(Field<TableLayoutPanel>(popup, "_navigation").Visible && Field<TextBox>(popup, "_location").ReadOnly
            && Field<TextBox>(popup, "_location").Text == root, "Current folder navigation/path is missing");
        foreach (var name in new[] { "_back", "_up" })
        {
            var button = Field<Button>(popup, name);
            Check(button.Focus() && button.Focused, $"Navigation button {name} could not receive keyboard focus");
            Check(!Key(popup, Keys.Enter), $"Enter on {name} was captured as result activation");
            Check(Folder(popup) == root, $"Enter on {name} unexpectedly opened a selected result");
        }
        Select(popup, child);
        var folderMenu = MenuLabels(popup);
        Check(folderMenu[0].Contains("进入") && folderMenu.Any(label => label.Contains("资源管理器")),
            "Folder menu does not separate internal navigation from Explorer");
        Select(popup, textFile);
        Check(MenuLabels(popup)[0].Contains("打开"), "File menu no longer offers normal open");
        Console.WriteLine("PASS folder activation: direct children, reset filter, path, context menu and navigation-button Enter routing.");

        var query = Field<TextBox>(popup, "_query");
        var filter = Field<ComboBox>(popup, "_filter");
        query.Text = "合同"; filter.SelectedIndex = 1;
        await Until(() => Items(popup).Length == 1 && Items(popup)[0].FullPath == textFile, "Folder name/type filtering did not update");
        query.Text = "子目录"; filter.SelectedIndex = 2;
        await Until(() => Items(popup).Length == 1 && Items(popup)[0].FullPath == child, "Folder filtering did not find the child");
        Select(popup, child);
        var list = Field<ListView>(popup, "_results");
        var bounds = list.SelectedItems[0].Bounds;
        typeof(Control).GetMethod("OnMouseDoubleClick", Private)!.Invoke(list,
            [new MouseEventArgs(MouseButtons.Left, 2, bounds.Left + 10, bounds.Top + bounds.Height / 2, 0)]);
        await Until(() => Folder(popup) == child && Items(popup).Length == 1, "Double-click did not descend into the child folder");
        Check(query.Text.Length == 0 && filter.SelectedIndex == 0, "Nested folder inherited its parent's name/type filter");
        Check(Key(popup, Keys.Alt | Keys.Left), "Alt+Left was not handled");
        await Until(() => Folder(popup) == root && query.Text == "子目录" && Items(popup).Length == 1, "Back did not restore parent folder filter");
        Check(filter.SelectedIndex == 2 && Selected(popup) == child, "Parent filter or selected child was lost on back");
        await CallAsync(popup, "ReturnToSearchAsync");
        CheckRestoredSearch(popup, root);
        Console.WriteLine("PASS filtering and nested navigation: double-click, Alt+Left, preserved parent and original search state.");

        await CallAsync(popup, "BrowseFolderAsync", root);
        await CallAsync(popup, "BrowseFolderAsync", child);
        Check(Key(popup, Keys.Alt | Keys.Up), "Alt+Up was not handled");
        await Until(() => Folder(popup) == root && Items(popup).Length == 4, "Up did not load the direct parent");
        Check(Selected(popup) == child, "Up did not select the folder just left");
        await CallAsync(popup, "BrowseFolderAsync", empty);
        Check(Folder(popup) == empty && Items(popup).Length == 0 && Field<Button>(popup, "_back").Enabled,
            "Empty folder is not navigable");
        Check(Property<string?>(popup, "SelectedDirectory") == empty,
            "An empty folder does not resolve to the current directory for file-dialog navigation");
        await CallAsync(popup, "GoBackAsync");
        Check(Folder(popup) == root && Items(popup).Length == 4, "Cannot return from an empty folder");
        var missing = Path.Combine(root, "不存在的目录");
        await CallAsync(popup, "BrowseFolderAsync", missing);
        Check(Folder(popup) == missing && Items(popup).Length == 0 && Field<Button>(popup, "_back").Enabled
            && Field<Button>(popup, "_returnToSearch").Enabled, "Missing directory left no recovery path");
        Check(Field<Label>(popup, "_status").Text.Contains("无法读取"), "Missing directory was presented as an empty folder");
        await CallAsync(popup, "GoBackAsync");
        Check(Folder(popup) == root && Items(popup).Length == 4, "Back did not recover from the missing directory");
        await CallAsync(popup, "ReturnToSearchAsync");
        CheckRestoredSearch(popup, root);
        Console.WriteLine("PASS parent, empty and missing folders: current view remains recoverable and original search restores.");

        await CallAsync(popup, "BrowseFolderAsync", root);
        Select(popup, child);
        await CallAsync(popup, "BrowseFolderAsync", child);
        var added = Path.Combine(root, "返回刷新新增.txt");
        var removed = Path.Combine(root, "报价.csv");
        try
        {
            // Only change the two exact files owned by this fixture; restore them before continuing.
            File.WriteAllText(added, "Added while the browser is inside the child folder.");
            File.Delete(removed);
            await CallAsync(popup, "GoBackAsync");
            Check(Folder(popup) == root && Items(popup).Any(item => item.FullPath == added)
                && Items(popup).All(item => item.FullPath != removed), "Back restored stale folder contents instead of refreshing");
            Check(Selected(popup) == child, "Refreshed parent lost the previously selected child");
        }
        finally
        {
            File.Delete(added);
            File.WriteAllText(removed, "name,amount\nfixture,12\n");
        }
        await CallAsync(popup, "ReturnToSearchAsync");
        CheckRestoredSearch(popup, root);
        Console.WriteLine("PASS back refresh: newly added/deleted sibling files are reflected while selection is preserved.");

        var lastChild = Path.Combine(pagingRoot, "子目录 119");
        await CallAsync(popup, "BrowseFolderAsync", lastChild);
        Set(popup, "_changing", true);
        try { Field<ComboBox>(popup, "_sort").SelectedIndex = 0; }
        finally { Set(popup, "_changing", false); }
        await CallAsync(popup, "GoUpAsync");
        Check(Folder(popup) == pagingRoot && Field<FileSearchPage>(popup, "_page").Offset == 100
            && Selected(popup) == lastChild, "Up did not choose the page containing the previous folder among 120 siblings");
        await CallAsync(popup, "ReturnToSearchAsync");
        CheckRestoredSearch(popup, root);
        Console.WriteLine("PASS paged parent navigation: Up restores the previous folder on the second page.");

        var pending = CallAsync(popup, "BrowseFolderAsync", root);
        await CallAsync(popup, "ReturnToSearchAsync");
        await pending;
        CheckRestoredSearch(popup, root);
        Console.WriteLine("PASS cancellation: a pending folder read cannot overwrite restored search results.");

        await CallAsync(popup, "BrowseFolderAsync", root);
        popup.Hide(); popup.ShowSearch("");
        Check(Folder(popup) == null && Items(popup).Length == 0 && ((ICollection)Field<object>(popup, "_history")).Count == 0,
            "A new ShowSearch session retained folder navigation");
        Check(!Field<TableLayoutPanel>(popup, "_navigation").Visible && !Field<Button>(popup, "_next").Enabled,
            "A new empty search retained navigation/pagination");
        Console.WriteLine("PASS reopen: empty search resets browsing state without a live Everything query.");
    }

    [STAThread]
    static int Main(string[] args)
    {
        var fixtureRoot = Path.GetFullPath("artifacts/debug/checks/file-search-navigation");
        var root = Path.Combine(fixtureRoot, "fixtures", "测试项目 #100% 中文");
        var child = Path.Combine(root, "子目录");
        var empty = Path.Combine(root, "空目录");
        var pagingRoot = Path.Combine(fixtureRoot, "paging-fixtures");
        Directory.CreateDirectory(child); Directory.CreateDirectory(empty);
        for (var i = 0; i < 120; i++) Directory.CreateDirectory(Path.Combine(pagingRoot, $"子目录 {i:000}"));
        var textFile = Path.Combine(root, "合同 中文.txt");
        File.WriteAllText(textFile, "Quickstart internal folder navigation fixture.");
        File.WriteAllText(Path.Combine(root, "报价.csv"), "name,amount\nfixture,12\n");
        File.WriteAllText(Path.Combine(child, "下一层文件.txt"), "This file must only appear inside the child folder.");
        AppPaths.SetRootForTests(Path.Combine(fixtureRoot, "config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var config = new ConfigManager();
        config.Config.FileDialogNavigationEnabled = false;
        config.Config.TrackOpusRecentFolders = false;
        using var popup = new FileSearchPopup(config, new ProcessLauncher(config)) { TopMost = false, ShowInTaskbar = true };
        Field<CheckBox>(popup, "_previewEnabled").Checked = false;
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) };
        var failure = 0;
        host.Shown += async (_, _) =>
        {
            var keepOpen = false;
            try
            {
                if (args.Contains("--interactive"))
                {
                    // Consume a hidden process startup's first ShowWindow before exposing the visual fixture.
                    popup.ShowSearch("");
                    popup.Hide();
                    popup.ShowSearch("");
                    SeedSearch(popup, root, empty);
                    await CallAsync(popup, "BrowseFolderAsync", root);
                    popup.VisibleChanged += (_, _) => { if (!popup.Visible) host.Close(); };
                    keepOpen = true;
                    Console.WriteLine("INTERACTIVE: fixture folder open; close the popup to exit.");
                }
                else
                {
                    await Checks(popup, root, child, empty, textFile, pagingRoot);
                    await GeometryChecks(popup, root, empty);
                }
            }
            catch (Exception ex) { failure = 1; Console.Error.WriteLine(ex); }
            finally { if (!keepOpen) host.Close(); }
        };
        Application.Run(host);
        return failure;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref Rect rect);
    static void ChangeDpi(Form form, int dpi)
    {
        var scale = dpi / (double)form.DeviceDpi;
        var bounds = form.Bounds;
        var suggested = new Rect { Left = bounds.Left, Top = bounds.Top,
            Right = bounds.Left + (int)Math.Round(bounds.Width * scale), Bottom = bounds.Top + (int)Math.Round(bounds.Height * scale) };
        SendMessage(form.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref suggested);
    }
}
