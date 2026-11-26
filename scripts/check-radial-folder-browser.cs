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
        File.WriteAllText(Path.Combine(root, "计划.txt"), "");
        File.WriteAllText(Path.Combine(year, "合同.txt"), "");
        File.WriteAllText(Path.Combine(contract, "合同.txt"), "");
        Directory.CreateDirectory(Path.Combine(root, "空目录"));
        var single = LocalFolderBrowser.ReadAsync(root, "合同", 0, 0, 1, CancellationToken.None).GetAwaiter().GetResult();
        var deep = LocalFolderBrowser.ReadAsync(root, "合同", 0, 0, 1, CancellationToken.None,
            includeSubfolders: true).GetAwaiter().GetResult();
        Check(single.Total == 0 && deep.Total == 2 && deep.Items.All(item => item.Name == "合同.txt"),
            "Recursive scope changed the default or missed folders that don't match the search");

        using var config = new ConfigManager();
        config.Config.RememberLastView = false;
        config.Config.Entries.Clear();
        for (var i = 0; i < 25; i++)
            config.Config.Entries.Add(new QuickEntry { Name = $"目录 {i:00}", Path = root, Type = EntryType.Folder });
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
        var point = new Point((int)(arrow.Left + arrow.Width / 2), (int)(arrow.Top + arrow.Height / 2));
        Call(wheel, "OnInteractiveMouseDown", null, new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));

        var browser = Field<Form>(wheel, "_folderBrowser");
        Check(!wheel.Visible && browser.Visible && screen.WorkingArea.Contains(browser.Bounds),
            $"Arrow must replace the wheel at the bottom-right edge: wheel={wheel.Visible}, browser={browser.Visible}, bounds={browser.Bounds}, work={screen.WorkingArea}");
        var results = Field<ListView>(browser, "_results");
        var query = Field<TextBox>(browser, "_query");
        var scope = Field<CheckBox>(browser, "_includeSubfolders");
        var status = Field<Label>(browser, "_status");
        var search = Field<TableLayoutPanel>(browser, "_search");
        Check(search.ClientRectangle.Contains(scope.Bounds) && search.ClientRectangle.Contains(query.Parent!.Bounds)
            && query.Parent.ClientRectangle.Contains(query.Bounds), "The search row clips its text field or scope toggle");
        WaitFor(() => Field<FileSearchPage?>(browser, "_page")?.Total == 3, "Root contents did not load");
        var child = results.Items.Cast<ListViewItem>().Single(item => item.Text == "2026");
        var arrowCell = child.SubItems[2].Bounds;
        var arrowPoint = new Point(arrowCell.Left + arrowCell.Width / 2, arrowCell.Top + arrowCell.Height / 2);
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
        Check(Field<string>(browser, "_folder") == year && results.Items.Count == 2,
            $"Back lost the previous folder view: folder={Field<string>(browser, "_folder")}, count={results.Items.Count}, status={status.Text}");
        Call(browser, "GoBack");
        Check(Field<string>(browser, "_folder") == root && results.Items.Count == 3, "Back lost the root folder view");

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
        Console.WriteLine("PASS arrow, edge placement, three levels, scroll restoration, relative paths and search scope");
    }
}
