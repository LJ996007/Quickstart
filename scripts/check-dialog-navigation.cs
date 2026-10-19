// Manual integration harness (does not use the real Quickstart config).
// Search process: dotnet run --file scripts/check-dialog-navigation.cs
// Separate standard-dialog host: dotnet run --file scripts/check-dialog-navigation.cs -- --dialogs
// With any tested file dialog focused, Ctrl+Shift+F11 opens the new search picker.
// Single-file release check (same COM/non-trimmed settings as the app):
// dotnet publish scripts/check-dialog-navigation.cs -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/debug/checks/dialog-navigation-check
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property BuiltInComInteropSupport=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.UI;
using Quickstart.Models;

static class DialogChecks
{
    [STAThread]
    static void Main(string[] args)
    {
        var root = Path.GetFullPath("artifacts/debug/checks/dialog-navigation");
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "目标 #100% 中文");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "目录跳转验证.txt"), "Quickstart navigation fixture; not user content.");
        Check(FileDialogNavigator.NormalizeExistingDirectory(target) == target + "\\", "Unicode/special-character directory");
        Check(FileDialogNavigator.NormalizeExistingDirectory(Path.GetPathRoot(target)!) == Path.GetPathRoot(target), "Drive root");
        foreach (var invalid in new[] { "relative", target + "\r\n", target + "\"", Path.Combine(root, "absent"), Path.Combine(target, "目录跳转验证.txt") })
        {
            try { FileDialogNavigator.NormalizeExistingDirectory(invalid); throw new Exception("Invalid folder accepted"); }
            catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException) { }
        }
        Console.WriteLine("PASS path validation: existing directory, drive root, Chinese, #, %, missing paths and filenames.");
        AppPaths.SetRootForTests(Path.Combine(root, "isolated-config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var config = new ConfigManager();
        FileDialogNavigator.NavigationDiagnostic = Console.WriteLine;
        config.Config.TrackOpusRecentFolders = false;
        config.Config.FolderFavoriteSource = FolderFavoriteSource.Quickstart;
        config.RecordRecentFolder(target);
        config.Config.EverythingPath = EverythingDetector.Detect() ?? "";
        using var form = new Harness(config, target, args.Contains("--dialogs"));
        Application.Run(form);
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class Harness : Form
    {
        readonly FileSearchPopup _search;
        readonly string _target;
        readonly bool _dialogs;
        readonly DialogQuickBarController? _bar;
        public Harness(ConfigManager config, string target, bool dialogs)
        {
            _dialogs = dialogs;
            _target = target;
            _search = new FileSearchPopup(config, new ProcessLauncher(config));
            _search.ShowInTaskbar = true; // Expose this test popup to Computer Use's window inventory.
            if (!dialogs)
            {
                config.Config.Entries.Add(new QuickEntry { Name = "测试收藏目录", Path = target, Type = EntryType.Folder });
                config.Config.Entries.Add(new QuickEntry { Name = "测试上级目录", Path = Path.GetDirectoryName(target)!, Type = EntryType.Folder });
                config.Config.Entries.Add(new QuickEntry { Name = "不要显示的文件", Path = Path.Combine(target, "目录跳转验证.txt"), Type = EntryType.File });
                _bar = new DialogQuickBarController(config);
                _bar.Diagnostic = Console.WriteLine;
                _bar.Bar.ShowInTaskbar = true; // Expose only the harness UI to Computer Use.
            }
            Text = dialogs ? "Quickstart 标准对话框测试" : "Quickstart 目录跳转验证";
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(20) };
            layout.Controls.Add(new Label { AutoSize = true, Text = "Ctrl+Shift+F11 从目标对话框呼出测试搜索\n选择：目标 #100% 中文\n验证后取消对话框，不提交文件。" });
            if (dialogs)
            {
                var open = new Button { Text = "标准打开", AutoSize = true };
                open.Click += (_, _) => { using var dialog = new OpenFileDialog { FileName = "keep-original.txt", InitialDirectory = Path.GetDirectoryName(target), Filter = "文本文件|*.txt|所有文件|*.*" }; dialog.ShowDialog(this); };
                var save = new Button { Text = "标准保存", AutoSize = true };
                save.Click += (_, _) => { using var dialog = new SaveFileDialog { FileName = "保留原文件名.txt", InitialDirectory = Path.GetDirectoryName(target), Filter = "文本文件|*.txt|所有文件|*.*" }; var result = dialog.ShowDialog(this); Console.WriteLine($"Save dialog ended: {result}; filename={dialog.FileName}"); };
                layout.Controls.Add(open); layout.Controls.Add(save);
            }
            Controls.Add(layout);
            Shown += (_, _) => { if (!_dialogs && !RegisterHotKey(Handle, 73, 0x4006, (uint)Keys.F11)) throw new Exception("Test hotkey unavailable"); _bar?.Start(); };
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && m.WParam == (IntPtr)73)
            {
                var source = FileDialogNavigator.CaptureForegroundWindow();
                ShowSearchFor(source);
            }
            base.WndProc(ref m);
        }
        private async void ShowSearchFor(IntPtr source)
        {
            try
            {
                if (_bar != null && await _bar.ShowSearchAsync("", source)) return;
            }
            catch (Exception ex) { Console.WriteLine(ex); }
            _search.ShowSearch('"' + _target + '"', source);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { UnregisterHotKey(Handle, 73); _bar?.Dispose(); _search.Dispose(); }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
