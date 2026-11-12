// Run: dotnet run --file scripts/check-file-search-redraw.cs -p:NuGetAudit=false
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.UI;

static class FileSearchRedrawChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Root = Path.GetFullPath("artifacts/debug/checks/file-search-redraw");

    static void CheckCellOnlyPaint(FileSearchPopup popup)
    {
        var list = Field<ListView>(popup, "_results");
        foreach (var selected in new[] { false, true })
        {
            var item = list.Items[selected ? 1 : 0];
            item.Selected = selected;
            foreach (var column in Enumerable.Range(0, list.Columns.Count).Where(i => list.Columns[i].Width > 0))
            {
                var cell = new Rectangle(0, 0, list.Columns[column].Width, item.Bounds.Height);
                using var bitmap = new Bitmap(cell.Width, cell.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Magenta);
                var args = new DrawListViewSubItemEventArgs(graphics, cell, item, item.SubItems[column], item.Index,
                    column, list.Columns[column], selected ? ListViewItemStates.Selected : ListViewItemStates.Default);
                // Native partial invalidations can request a cell without a new
                // whole-row callback. No full Refresh is allowed to hide that path.
                typeof(ListView).GetMethod("OnDrawSubItem", Private)!.Invoke(list, [args]);
                var background = selected ? Color.FromArgb(236, 244, 255) : Color.White;
                Check(bitmap.GetPixel(cell.Width / 2, cell.Height - 3).ToArgb() == background.ToArgb(),
                    $"Cell-only repaint missing: selected={selected}, column={column}");
                var ink = 0;
                for (var y = 2; y < cell.Height - 2; y++)
                for (var x = 2; x < cell.Width - 2; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R < 180 && pixel.G < 180 && pixel.B < 220) ink++;
                }
                Check(ink > 4, $"Cell text is blank: selected={selected}, column={column}");
            }
        }
    }

    static void CheckVisibleRows(FileSearchPopup popup, string capture)
    {
        var list = Field<ListView>(popup, "_results");
        using var bitmap = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(bitmap, list.ClientRectangle);
        var completeRows = 0;
        foreach (ListViewItem item in list.Items)
        {
            var bounds = item.Bounds;
            if (bounds.Top < 35 || bounds.Bottom >= bitmap.Height) continue;
            var left = 0;
            foreach (ColumnHeader column in list.Columns)
            {
                var right = Math.Min(bitmap.Width, left + column.Width);
                if (right - left > 25)
                {
                    var ink = 0;
                    for (var y = bounds.Top + 3; y < bounds.Bottom - 3; y++)
                    for (var x = left + 3; x < right - 3; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        if (pixel.R < 180 && pixel.G < 180 && pixel.B < 220) ink++;
                    }
                    Check(ink > 4, $"Visible content blank after {capture}: item={item.Index}, column={column.Index}");
                }
                left += column.Width;
            }
            completeRows++;
        }
        Check(completeRows > 3, "Fixture has too few fully visible rows");
        bitmap.Save(Path.Combine(Root, capture + ".png"));
    }

    static async Task RunChecks(FileSearchPopup popup)
    {
        Field<CheckBox>(popup, "_previewEnabled").Checked = false;
        popup.ShowSearch(detectDialog: false);
        popup.ClientSize = new Size(1200, 720);
        // Keep this renderer test independent of WebView initialization/network.
        typeof(FileSearchPopup).GetField("_detectingDialog", Private)!.SetValue(popup, true);
        var entries = Enumerable.Range(0, 90).Select(i => new FileSearchResult(
            Path.Combine(Root, $"测试文件 {i:000}.txt"), false, 2048 + i, new DateTime(2026, 11, 9, 12, 0, 0))).ToArray();
        Call(popup, "ShowPage", new FileSearchPage(90, 0, entries), entries[1].FullPath);
        await Task.Delay(100);
        CheckCellOnlyPaint(popup);
        var list = Field<ListView>(popup, "_results");
        var selection = ((FileSearchResult)list.SelectedItems[0].Tag!).FullPath;
        Field<CheckBox>(popup, "_previewEnabled").Checked = true;
        var split = Field<SplitContainer>(popup, "_split");
        Check(!split.Panel2Collapsed, "Fixture preview is collapsed");
        foreach (var folderView in new[] { false, true })
        {
            typeof(FileSearchPopup).GetField("_folder", Private)!.SetValue(popup, folderView ? Root : null);
            Call(popup, "ResizeColumns");
            foreach (var width in new[] { 530, 720, 490, 620 })
            {
                split.SplitterDistance = Math.Clamp(width, split.Panel1MinSize, split.Width - split.Panel2MinSize - split.SplitterWidth);
                await Task.Delay(25);
                var before = list.TopItem!.Index;
                SendMessage(list.Handle, 0x115, (IntPtr)3, IntPtr.Zero); // WM_VSCROLL / SB_PAGEDOWN
                SendMessage(list.Handle, 0x115, (IntPtr)8, IntPtr.Zero); // SB_ENDSCROLL
                await Task.Delay(25);
                Check(list.TopItem!.Index > before, "Scrollbar did not move");
                CheckVisibleRows(popup, $"{(folderView ? "folder" : "search")}-split-{width}-scrolled");
                SendMessage(list.Handle, 0x115, (IntPtr)6, IntPtr.Zero); // SB_TOP
                await Task.Delay(25);
                CheckVisibleRows(popup, $"{(folderView ? "folder" : "search")}-split-{width}-top");
                Check(list.Items.Count == entries.Length && ((FileSearchResult)list.SelectedItems[0].Tag!).FullPath == selection,
                    "Dragging/scrolling changed results or selection");
            }
        }
        Console.WriteLine("PASS cell-only paints, search/folder columns, splitter resizing, native scrollbar movement, visible row text and stable selection.");
    }

    [STAThread]
    static int Main(string[] args)
    {
        AppPaths.SetRootForTests(Path.Combine(Root, "config"));
        Directory.CreateDirectory(Root);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var config = new ConfigManager();
        using var popup = new FileSearchPopup(config, new ProcessLauncher(config)) { ShowInTaskbar = true };
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) };
        var failure = 0;
        host.Shown += async (_, _) =>
        {
            var keepOpen = false;
            try
            {
                await RunChecks(popup);
                if (args.Contains("--interactive"))
                {
                    popup.Text = "Quickstart · 文件列表重绘测试";
                    // Consume a hidden launcher startup's first ShowWindow.
                    popup.Hide();
                    popup.Show();
                    popup.Activate();
                    popup.VisibleChanged += (_, _) => { if (!popup.Visible) host.Close(); };
                    keepOpen = true;
                    Console.WriteLine("INTERACTIVE: drag the splitter/scrollbar; close the fixture to exit.");
                }
            }
            catch (Exception ex) { failure = 1; Console.Error.WriteLine(ex); }
            finally { if (!keepOpen) { popup.Hide(); host.Close(); } }
        };
        Application.Run(host);
        return failure;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
