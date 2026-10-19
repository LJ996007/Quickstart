// Run: dotnet run --file scripts/check-everything-search.cs
// Uses the installed Everything client, an isolated Quickstart config, and no file-opening actions.
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Buffers.Binary;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.UI;

static class SearchChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static object Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    static void U32(byte[] bytes, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);

    static void ProtocolChecks()
    {
        var query = EverythingIpcProtocol.BuildQuery(0x1234567812345678, 73, "中文 ext:docx", 100, 14);
        Check(BitConverter.ToUInt32(query, 0) == 0x12345678 && BitConverter.ToUInt32(query, 4) == 73, "HWND/reply layout");
        Check(BitConverter.ToUInt32(query, 12) == 100 && BitConverter.ToUInt32(query, 24) == 14, "Paging/sort layout");
        Check(Encoding.Unicode.GetString(query, 28, query.Length - 30) == "中文 ext:docx", "Unicode query");
        const string path = @"C:\资料\测试 文档.docx";
        var name = Encoding.Unicode.GetBytes(path + '\0');
        var data = new byte[28 + 4 + name.Length + 16];
        U32(data, 0, 101); U32(data, 4, 1); U32(data, 8, 100); U32(data, 12, EverythingIpcProtocol.RequestFlags);
        U32(data, 24, 28); U32(data, 28, (uint)path.Length); name.CopyTo(data, 32);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(32 + name.Length), 42);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(40 + name.Length), DateTime.UtcNow.ToFileTimeUtc());
        var page = EverythingIpcProtocol.ParseResults(data);
        Check(page.Total == 101 && page.Offset == 100 && page.Items[0].FullPath == path && page.Items[0].Size == 42, "Result decoding");
        Check(!page.Items[0].IsFolder && page.Items[0].ModifiedAt.HasValue, "Metadata");
        for (var length = 0; length < data.Length; length++)
        {
            try { EverythingIpcProtocol.ParseResults(data[..length]); throw new Exception("Truncated packet accepted: " + length); }
            catch (InvalidDataException) { }
        }
        var corrupt = (byte[])data.Clone(); U32(corrupt, 24, uint.MaxValue);
        try { EverythingIpcProtocol.ParseResults(corrupt); throw new Exception("Bad offset accepted"); }
        catch (OverflowException) { }
        U32(data, 20, 1);
        Check(EverythingIpcProtocol.ParseResults(data).Items[0].IsFolder, "Folder flag");
        Console.WriteLine("PASS protocol: Unicode, x64 HWND, paging, metadata, truncated/malformed packets.");
    }

    static void MouseChecks()
    {
        // Exercise the callback directly without installing a global hook or synthesizing physical input.
        var hook = (GlobalMouseHook)RuntimeHelpers.GetUninitializedObject(typeof(GlobalMouseHook));
        var callback = typeof(GlobalMouseHook).GetMethod("HookCallback", Private)!;
        var buffer = Marshal.AllocHGlobal(32);
        var count = 0;
        hook.MiddleClicked += (_, _) => count++;
        IntPtr Mouse(int message, int x, int y, uint time, uint flags = 0)
        {
            Marshal.Copy(new byte[32], 0, buffer, 32);
            Marshal.WriteInt32(buffer, 0, x); Marshal.WriteInt32(buffer, 4, y);
            Marshal.WriteInt32(buffer, 12, (int)flags); Marshal.WriteInt32(buffer, 16, unchecked((int)time));
            return (IntPtr)callback.Invoke(hook, [0, (IntPtr)message, buffer])!;
        }
        try
        {
            hook.UpdateSettings(false, 120, 50, true);
            Check(Mouse(0x207, 10, 10, 100) == (IntPtr)1, "Middle down not captured");
            Check(Mouse(0x208, 10, 10, 180) == (IntPtr)1 && count == 1, "Middle click with right gestures disabled");
            Mouse(0x207, 10, 10, 200); Mouse(0x208, 40, 40, 250);
            Check(count == 1, "Middle drag treated as click");
            Mouse(0x207, 10, 10, 300); Mouse(0x208, 10, 10, 1000);
            Check(count == 1, "Long middle press treated as click");
            Mouse(0x207, 10, 10, 1100);
            hook.UpdateSettings(false, 120, 50, false);
            Check(Mouse(0x208, 10, 10, 1150) == (IntPtr)1 && count == 1, "Disabled setting left unmatched up");
            Mouse(0x207, 10, 10, 1200); Mouse(0x208, 10, 10, 1250);
            Check(count == 1 && !(bool)Field(hook, "_middleDownSuppressed"), "Disabled middle click captured");
            hook.UpdateSettings(false, 120, 50, true);
            Mouse(0x207, 10, 10, 1300, 1); Mouse(0x208, 10, 10, 1350, 1);
            Check(count == 1, "Injected click activated menu");
            Console.WriteLine("PASS mouse: paired suppression, independent enable, drag/hold filtering, injected events.");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [STAThread]
    static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ProtocolChecks(); MouseChecks();
        AppPaths.SetRootForTests(Path.Combine(Path.GetTempPath(), "Quickstart-search-check-" + Guid.NewGuid()));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) };
        var failure = 0;
        host.Shown += async (_, _) =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                using var client = new EverythingSearchClient();
                var executable = EverythingDetector.Detect() ?? throw new Exception("Everything is not installed");
                var page = await client.SearchAsync(executable, "ext:cs", 0, 1, timeout.Token);
                Check(page.Items.Count > 0, "Live file query returned no results");
                Check(page.Items.All(i => !i.IsFolder && i.FullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)), "File query/filter");
                if (page.Total > 100)
                {
                    var second = await client.SearchAsync(executable, "ext:cs", 100, 1, timeout.Token);
                    Check(second.Offset == 100 && second.Items.Count > 0, "Live second page");
                    Check(!page.Items.Select(i => i.FullPath).Intersect(second.Items.Select(i => i.FullPath)).Any(), "Repeated page");
                }
                var folders = await client.SearchAsync(executable, "folder: Quickstart", 0, 14, timeout.Token);
                Check(folders.Items.Count > 0 && folders.Items.All(i => i.IsFolder), "Live folder query");
                var empty = await client.SearchAsync(executable, "Quickstart-no-such-file-" + Guid.NewGuid(), 0, 1, timeout.Token);
                Check(empty.Total == 0 && empty.Items.Count == 0, "Empty result");
                using var cancel = new CancellationTokenSource();
                var old = client.SearchAsync(executable, "ext:cs", 0, 1, cancel.Token);
                cancel.Cancel();
                var latest = client.SearchAsync(executable, "folder: Quickstart", 0, 1, timeout.Token);
                try { await old; } catch (OperationCanceledException) { }
                Check((await latest).Items.All(i => i.IsFolder), "Cancelled query overwrote latest");
                Console.WriteLine($"PASS live IPC: {page.Total} C# files; metadata, pagination, folders, empty results and cancellation.");

                using var config = new ConfigManager();
                config.Config.EverythingPath = executable;
                // This checks ordinary file search; an ambient WPS dialog must not
                // switch the query to directory-only mode during the test.
                config.Config.FileDialogNavigationEnabled = false;
                using var popup = new FileSearchPopup(config, new ProcessLauncher(config));
                popup.ShowSearch("ext:cs Quickstart");
                Console.WriteLine($"UI metrics: DPI={popup.DeviceDpi}, client={popup.ClientSize}, font={popup.Font.Size}, scale={popup.AutoScaleDimensions}");
                var status = (Label)Field(popup, "_status");
                for (var i = 0; i < 150 && !status.Text.StartsWith("共 "); i++) await Task.Delay(100, timeout.Token);
                var list = (ListView)Field(popup, "_results");
                Check(list.Items.Count > 0, "Popup results missing: " + status.Text);
                var folder = Path.GetFullPath("artifacts/search-check");
                Directory.CreateDirectory(folder);
                using var bitmap = new Bitmap(popup.Width, popup.Height);
                popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, popup.Size));
                bitmap.Save(Path.Combine(folder, "search-popup.png"));
                popup.Hide();
                popup.ShowSearch("");
                Check(list.Items.Count == 0 && !((Button)Field(popup, "_next")).Enabled, "Old query leaked into empty search");
                popup.Size = new Size(580, 320);
                using var compact = new Bitmap(popup.Width, popup.Height);
                popup.DrawToBitmap(compact, new Rectangle(Point.Empty, popup.Size));
                compact.Save(Path.Combine(folder, "search-popup-compact.png"));
                popup.Hide();
                Console.WriteLine("PASS UI: live results, clear/reopen state; rendered normal and minimum window sizes.");
                config.Config.FileDialogNavigationEnabled = true;
                using var settings = new SettingsForm(config);
                settings.Show();
                ((ListBox)Field(settings, "_navList")).SelectedIndex = 1;
                await Task.Delay(100);
                var middle = (CheckBox)Field(settings, "_middleClickCheck");
                var hotkey = (TextBox)Field(settings, "_fileSearchHotKeyBox");
                Check(middle.Checked && middle.Visible && hotkey.Text == "Ctrl+Alt+Space" && hotkey.Visible, "Input settings missing");
                using var settingsBitmap = new Bitmap(settings.Width, settings.Height);
                settings.DrawToBitmap(settingsBitmap, new Rectangle(Point.Empty, settings.Size));
                settingsBitmap.Save(Path.Combine(folder, "input-settings.png"));
                Console.WriteLine("PASS settings: middle click and independent search shortcut are visible with expected defaults.");
            }
            catch (Exception ex) { failure = 1; Console.Error.WriteLine(ex); }
            finally { host.Close(); }
        };
        Application.Run(host);
        return failure;
    }
}
