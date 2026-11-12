// Run: dotnet run --file scripts/check-folder-labels.cs -p:NuGetAudit=false
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.UI;

static class FolderLabelChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(DialogQuickBar).Assembly;
    static readonly Type Labels = App.GetType("Quickstart.UI.FolderPathLabel")!;
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    static string Label(string name, string path, Font font, int width)
        => (string)Labels.GetMethod("Format")!.Invoke(null, [name, path, font, width])!;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Width(string text, Font font) => TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

    [STAThread]
    static void Main()
    {
        var root = Path.GetFullPath("artifacts/debug/checks/folder-labels");
        AppPaths.SetRootForTests(Path.Combine(root, "config"));
        Directory.CreateDirectory(root);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var paths = new[] { @"C:\项目甲-图书馆视频监控增补项目\05 开评标文件", @"C:\项目乙-设备采购与维护项目\05 开评标文件",
            @"C:\名称很长的项目编号20261109与采购人信息和完整项目名称\02 采购文件过程稿", @"C:\研发 & 设计📁项目\资料 & 报表📄目录" };
        var cases = 0;
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f })
        using (var font = new Font("Microsoft YaHei UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        foreach (var available in new[] { 110, 150, 240, 600 })
        foreach (var path in paths.Concat([@"C:\", @"C:\同名目录", @"\\server\share\项目甲\同名目录"]))
        {
            var name = FolderDirectoryService.FromPath(path).Name;
            var width = (int)(available * scale);
            var label = Label(name, path, font, width);
            Check(Width(label, font) <= width, $"Label exceeds width: {label}");
            if (path != @"C:\") Check(label.Contains(" · "), "Parent context disappeared");
            Check(!label.Any(char.IsControl), "Label contains a control character");
            for (var i = 0; i < label.Length; i++)
            {
                if (char.IsHighSurrogate(label[i])) Check(i + 1 < label.Length && char.IsLowSurrogate(label[++i]), "Broken surrogate pair");
                else Check(!char.IsLowSurrogate(label[i]), "Orphan surrogate");
            }
            cases++;
        }
        using var config = new ConfigManager();
        config.Config.TrackOpusRecentFolders = false;
        config.Config.RecentFolders.Clear();
        for (var i = 0; i < paths.Length; i++) config.Config.RecentFolders[paths[i]] = DateTime.UtcNow.AddMinutes(-i);
        using var dialog = new Form { Text = "Folder label fixture", Bounds = new Rectangle(100, 100, 900, 500) };
        dialog.Show(); dialog.Hide(); dialog.Show();
        using var bar = new DialogQuickBar(config);
        bar.Bind(new FileDialogTarget(dialog.Handle, (uint)Environment.ProcessId, false, false, dialog.Text));
        foreach (var width in new[] { 480, 640, 900 })
        {
            dialog.Width = width;
            Call(bar, "ShowRecent");
            bar.ShowAttached();
            Application.DoEvents();
            var shortcuts = Field<FlowLayoutPanel>(bar, "_shortcuts");
            var buttons = shortcuts.Controls.OfType<Button>().ToArray();
            Check(buttons.Length == 4, "Recent shortcuts missing");
            foreach (var button in buttons)
            {
                Check(shortcuts.ClientRectangle.Contains(button.Bounds), "Shortcut clipped or inaccessible");
                Check(button.Text.Contains('\n'), "Shortcut lacks parent line");
                Check(button.Text.Split('\n').All(line => Width(line, button.Font) <= button.Width - button.Padding.Horizontal), "Shortcut text overflows");
                var preferred = button.GetPreferredSize(Size.Empty);
                Check(preferred.Width <= button.Width && preferred.Height <= button.Height, "Native button wraps a line and hides its parent");
                Check(Field<ToolTip>(bar, "_tips").GetToolTip(button) == (string)button.Tag!, "Full path tooltip lost");
            }
            Check(buttons[0].Text != buttons[1].Text, "Abbreviation hid the distinction between projects");
            var rows = Field<ListView>(bar, "_results").Items.Cast<ListViewItem>().ToArray();
            Check(rows.Length == 4 && rows[0].Text == rows[1].Text && rows[0].SubItems[1].Text != rows[1].SubItems[1].Text,
                "Same-name folders cannot be distinguished");
            Check(rows.Select(r => (string)r.Tag!).SequenceEqual(paths), "Display changes altered target paths");
            var textChanges = 0;
            var columnChanges = 0;
            foreach (var button in buttons) button.TextChanged += (_, _) => textChanges++;
            Field<ListView>(bar, "_results").ColumnWidthChanged += (_, _) => columnChanges++;
            var reposition = System.Diagnostics.Stopwatch.StartNew();
            for (var tick = 0; tick < 30; tick++) bar.Reposition();
            dialog.Left += 10;
            bar.Reposition();
            Console.WriteLine($"REPOSITION {width}: {reposition.Elapsed.TotalMilliseconds:F1}ms, text changes={textChanges}, column changes={columnChanges}");
            Check(textChanges == 0 && columnChanges == 0, "Idle polling rewrites unchanged labels/columns");
            using var bitmap = new Bitmap(bar.Width, bar.Height);
            bar.DrawToBitmap(bitmap, bar.ClientRectangle);
            bitmap.Save(Path.Combine(root, $"quickbar-{width}.png"));
        }
        var type = App.GetType("Quickstart.UI.MiddleClickPopup")!;
        using var middle = (Form)Activator.CreateInstance(type)!;
        var folders = paths.Select(FolderDirectoryService.FromPath).ToArray();
        Call(middle, "PopulateRecentFolders", (object)folders);
        var menu = Field<ContextMenuStrip>(middle, "_recentMenu");
        Check(menu.Items.Count == 4 && menu.Items.Cast<ToolStripMenuItem>().All(i => i.Text!.Contains(" · ")), "Menu lacks parent labels");
        Check(menu.Items.Cast<ToolStripMenuItem>().Select(i => i.ToolTipText).SequenceEqual(paths), "Menu full paths changed");
        string? requested = null;
        type.GetEvent("FolderRequested")!.AddEventHandler(middle, new Action<string, IntPtr>((path, _) => requested = path));
        ((ToolStripMenuItem)menu.Items[1]).PerformClick();
        Check(requested == paths[1], "Abbreviated label navigated to the wrong directory");
        menu.Show(dialog, new Point(10, 40));
        Application.DoEvents();
        using (var bitmap = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(bitmap, menu.ClientRectangle);
            bitmap.Save(Path.Combine(root, "recent-menu.png"));
        }
        menu.Close();
        Console.WriteLine($"PASS {cases} measured label cases, roots/UNC/Unicode, same-name parents, responsive shortcuts, full-path tooltips and exact navigation targets.");
    }
}
