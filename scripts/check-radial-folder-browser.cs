// Run: dotnet run --file scripts/check-radial-folder-browser.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Quickstart.Core;
using Quickstart.Models;

static class RadialFolderBrowserChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Call(object owner, string name, params object?[] args) =>
        owner.GetType().GetMethod(name, Flags)!.Invoke(owner, args);
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Flags)!.GetValue(owner)!;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void WaitFor(Func<bool> condition, string message)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
        Check(condition(), message);
    }

    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var scratch = Path.GetFullPath("artifacts/debug/checks/radial-folder-browser");
        var configRoot = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        AppPaths.SetRootForTests(configRoot);
        var root = Path.Combine(configRoot, "项目归档");
        var year = Path.Combine(root, "2026");
        var contract = Path.Combine(year, "采购");
        Directory.CreateDirectory(contract);
        CheckRecentParent(contract, configRoot);
        File.WriteAllText(Path.Combine(root, "计划.txt"), "");
        foreach (var document in new[] { "报告.docx", "清单.xlsx", "演示.pptx", "采购文件.pdf" })
            File.WriteAllText(Path.Combine(root, document), "");
        File.WriteAllText(Path.Combine(year, "合同.txt"), "");
        File.WriteAllText(Path.Combine(contract, "合同.txt"), "");
        Directory.CreateDirectory(Path.Combine(root, "空目录"));
        Directory.SetLastWriteTimeUtc(year, DateTime.UtcNow.AddDays(-5));
        File.SetLastWriteTimeUtc(Path.Combine(root, "报告.docx"), DateTime.UtcNow.AddHours(1));
        var single = LocalFolderBrowser.ReadAsync(root, "合同", 0, 0, 1, CancellationToken.None).GetAwaiter().GetResult();
        var deep = LocalFolderBrowser.ReadAsync(root, "合同", 0, 0, 1, CancellationToken.None,
            includeSubfolders: true).GetAwaiter().GetResult();
        Check(single.Total == 0 && deep.Total == 2 && deep.Items.All(item => item.Name == "合同.txt"),
            "Recursive scope changed the default or missed folders that don't match the search");

        using var config = new ConfigManager();
        config.Config.RememberLastView = false;
        config.Config.Entries.Clear();
        for (var i = 0; i < 25; i++)
            config.Config.Entries.Add(new QuickEntry
            {
                Name = i == 5 ? "轮盘长标题显示测试文件夹名称继续显示" : $"目录 {i:00}",
                Path = root, Type = EntryType.Folder
            });
        var wheelType = typeof(ProcessLauncher).Assembly.GetType("Quickstart.UI.RadialMenuPopup")!;
        using var wheel = (Form)Activator.CreateInstance(wheelType, config, new ProcessLauncher(config), null)!;
        var screen = Screen.PrimaryScreen!;
        var anchor = new Point(screen.WorkingArea.Right - 3, screen.WorkingArea.Bottom - 3);
        Call(wheel, "ShowAtGesturePoint", anchor, IntPtr.Zero);
        Call(wheel, "EnterInteractiveMode");
        Call(wheel, "TryScrollList", 5, null);
        var scroll = Field<int>(wheel, "_scrollIndex");
        Check(scroll == 5, "Could not scroll the saved-folder list");
        var wheelItems = Field<IEnumerable>(wheel, "_items").Cast<object>().ToArray();
        var slot = wheelItems[scroll];
        var row = (RectangleF)slot.GetType().GetProperty("TargetBounds")!.GetValue(slot)!;
        var arrow = (RectangleF)Call(wheel, "FolderArrowBounds", row)!;
        Check(arrow.Width >= row.Width / 3 && arrow.Width < row.Width / 2 && Math.Abs(row.Right - arrow.Right) < .1f,
            "The right-hand folder action must use about a third of the row without taking over the name");
        var point = new Point((int)Math.Ceiling(arrow.Left) + 4, (int)(arrow.Top + arrow.Height / 2));
        Check(row.Right - point.X > 30f * wheel.DeviceDpi / 96f, "The test click must lie outside the old arrow region");
        Call(wheel, "RenderLayered", false);
        var wheelFrame = Field<Bitmap>(wheel, "_layerBmp");
        var textStart = point.X;
        var textEnd = (int)(row.Right - 36f * wheel.DeviceDpi / 96f);
        Check(Enumerable.Range(textStart, textEnd - textStart).Any(x =>
            Enumerable.Range((int)row.Top + 5, (int)(row.Height * .43f)).Any(y =>
            {
                var pixel = wheelFrame.GetPixel(x, y);
                return pixel.A > 240 && pixel.R < 150 && pixel.G < 150 && pixel.B < 150;
            })), "The widened hit area must still render the folder name");
        Call(wheel, "OnInteractiveMouseDown", null, new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));

        var browser = Field<Form>(wheel, "_folderBrowser");
        Check(!wheel.Visible && browser.Visible && screen.WorkingArea.Contains(browser.Bounds),
            $"Arrow must replace the wheel at the bottom-right edge: wheel={wheel.Visible}, browser={browser.Visible}, bounds={browser.Bounds}, work={screen.WorkingArea}");
        // Keep the fixture open if another app takes foreground while these synthetic inputs run.
        var dismiss = browser.GetType().GetMethod("OnBrowserDeactivate", Flags)!;
        browser.Deactivate -= (EventHandler)dismiss.CreateDelegate(typeof(EventHandler), browser);
        var results = Field<ListView>(browser, "_results");
        var query = Field<TextBox>(browser, "_query");
        var scope = Field<CheckBox>(browser, "_includeSubfolders");
        var status = Field<Label>(browser, "_status");
        var search = Field<TableLayoutPanel>(browser, "_search");
        var grip = Field<Control>(browser, "_dragGrip");
        var navigation = Field<TableLayoutPanel>(browser, "_navigation");
        var breadcrumb = Field<Button>(browser, "_breadcrumb");
        Check(grip.Width > 0 && navigation.ClientRectangle.Contains(grip.Bounds) &&
            grip.Left >= breadcrumb.Right && grip.Cursor == Cursors.SizeAll, "The drag grip overlaps the clickable path");
        var originalLocation = browser.Location;
        Call(browser, "BeginDrag", grip, new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0));
        Check(grip.Capture, "Drag grip did not capture the pointer");
        Call(browser, "MoveDrag", grip, new MouseEventArgs(MouseButtons.Left, 0, -45, -25, 0));
        Call(browser, "EndDrag", grip, new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0));
        Check(browser.Location == new Point(originalLocation.X - 60, originalLocation.Y - 40) && !grip.Capture,
            "Dragging did not move the browser or release pointer capture");
        Check(search.ClientRectangle.Contains(scope.Bounds) && search.ClientRectangle.Contains(query.Parent!.Bounds)
            && query.Parent.ClientRectangle.Contains(query.Bounds), "The search row clips its text field or scope toggle");
        WaitFor(() => Field<FileSearchPage?>(browser, "_page")?.Total == 7, "Root contents did not load");
        Check(results.Items[0].Text == "报告.docx", "Unused entries must be sorted by modification date");
        var icons = Field<ImageList>(browser, "_icons");
        var visibleIconKeys = results.Items.Cast<ListViewItem>().Select(item => item.ImageKey).ToHashSet();
        Check(visibleIconKeys.SetEquals(["folder", "file.txt", "file.docx", "file.xlsx", "file.pptx", "file.pdf"])
            && visibleIconKeys.All(icons.Images.ContainsKey), "Files must display icons keyed by extension");
        icons.ImageSize = new Size(16, 16);
        Call(browser, "RefreshDisplay");
        Check(results.Items.Cast<ListViewItem>().All(item => icons.Images.ContainsKey(item.ImageKey)),
            "Existing rows lost their icons after rebuilding the image list");
        using (var bitmap = new Bitmap(browser.Width, browser.Height))
        {
            browser.DrawToBitmap(bitmap, browser.ClientRectangle);
            bitmap.Save(Path.Combine(scratch, "directory-file-icons.png"));
        }
        var child = results.Items.Cast<ListViewItem>().Single(item => item.Text == "2026");
        var arrowCell = child.SubItems[2].Bounds;
        Check(arrowCell.Width >= 60, "The expanded folder action must span the right-hand hit area");
        var arrowPoint = new Point(arrowCell.Left + 4, arrowCell.Top + arrowCell.Height / 2);
        var hit = results.HitTest(arrowPoint);
        Check(hit.Item == child && hit.SubItem == child.SubItems[2],
            $"The folder arrow is not reachable: cell={arrowCell}, client={results.ClientSize}, hit={hit.Item?.Text}/{hit.SubItem?.Text}");
        Call(browser, "OnResultClick", null, new MouseEventArgs(MouseButtons.Left, 1,
            arrowPoint.X, arrowPoint.Y, 0));
        WaitFor(() => Field<FileSearchPage?>(browser, "_page")?.Total == 2 && Field<string>(browser, "_folder") == year,
            "The child-folder arrow did not enter that folder");
        Call(browser, "EnterFolder", contract);
        WaitFor(() => Field<FileSearchPage?>(browser, "_page")?.Total == 1 && results.Items[0].Text == "合同.txt",
            "Grandchild contents did not load");
        Call(browser, "GoBack");
        WaitFor(() => Field<string>(browser, "_folder") == year && Field<FileSearchPage?>(browser, "_page")?.Total == 2,
            "Back lost the previous folder view");
        Call(browser, "GoBack");
        WaitFor(() => Field<string>(browser, "_folder") == root && Field<FileSearchPage?>(browser, "_page")?.Total == 7,
            "Back lost the root folder view");
        Check(Field<FileSearchPage>(browser, "_page").Items[0].Name == "2026",
            "The most recently visited directory must move above newer unvisited files");

        query.Text = "合同";
        WaitFor(() => status.Text == "未找到匹配项", "Current-layer search should not show nested files");
        scope.Checked = true;
        WaitFor(() => results.Items.Count == 2, "Recursive scope did not locate both matching files");
        Check(results.Items.Cast<ListViewItem>().Select(item => item.SubItems[1].Text)
            .ToHashSet().SetEquals(["2026", Path.Combine("2026", "采购")]), "Relative paths did not identify the duplicate names");
        using (var bitmap = new Bitmap(browser.Width, browser.Height))
        {
            browser.DrawToBitmap(bitmap, browser.ClientRectangle);
            bitmap.Save(Path.Combine(scratch, "directory-search.png"));
        }

        scope.Checked = false;
        query.Text = "无此文件";
        query.Text = "计划";
        WaitFor(() => results.Items.Count == 1 && results.Items[0].Text == "计划.txt", "Stale requests replaced the newest filter");
        Call(browser, "GoBack");
        Check(wheel.Visible && !browser.Visible && Field<int>(wheel, "_scrollIndex") == scroll, "Back did not restore the wheel scroll position");
        Call(wheel, "HidePanels");
        Console.WriteLine("PASS file-type icons, drag grip, edge placement, three levels, scroll restoration, relative paths and search scope");
    }

    private static void CheckRecentParent(string target, string fixtureRoot)
    {
        var linkPath = Path.Combine(fixtureRoot, "最近用过.lnk");
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(linkPath);
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).Save();
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
        var assembly = typeof(ProcessLauncher).Assembly;
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var resolver = assembly.GetType("Quickstart.Core.WindowsRecentFolders")!.GetMethod("ReadShortcutTarget", flags, [typeof(string)])!;
        var resolved = (string)resolver.Invoke(null, [linkPath])!;
        var parent = assembly.GetType("Quickstart.UI.FolderPathLabel")!.GetMethod("ParentName", flags)!;
        Check(resolved == target && (string)parent.Invoke(null, [resolved])! == "2026",
            "The recent-folder label must use the real target's parent, not the shortcut's parent");
    }
}
