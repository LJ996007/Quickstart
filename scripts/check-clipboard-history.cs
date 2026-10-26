// dotnet run --file scripts/check-clipboard-history.cs
// Uses an isolated config and restores the clipboard after exercising real format round trips.
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using Quickstart.Core;
using Quickstart.UI;

static class ClipboardHistoryChecks
{
    static readonly Type Content = typeof(ClipboardHistoryService).Assembly.GetType("Quickstart.Core.ClipboardHistoryContent")!;
    static ClipboardHistoryItem? Read(IDataObject data) => (ClipboardHistoryItem?)Content.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [data]);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }

    [STAThread]
    static int Main()
    {
        var exit = 0;
        using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
        form.Shown += async (_, _) =>
        {
            var original = SnapshotClipboard();
            try { await RunAsync(form); }
            catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
            finally
            {
                try
                {
                    for (var attempt = 0; ; attempt++)
                    {
                        try { if (original != null) Clipboard.SetDataObject(original, true); else Clipboard.Clear(); break; }
                        catch (System.Runtime.InteropServices.ExternalException) when (attempt < 5) { await Task.Delay(100); }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("Clipboard restore failed: " + ex.Message); exit = 1; }
                form.Close();
            }
        };
        Application.Run(form);
        return exit;
    }

    static DataObject? SnapshotClipboard()
    {
        var source = Clipboard.GetDataObject();
        if (source == null) return null;
        var copy = new DataObject();
        // Eagerly materialize formats: a live OLE object can become invalid after another
        // owner replaces the clipboard, even when retained by managed code.
        foreach (var format in source.GetFormats(false))
        {
            var value = source.GetData(format, false);
            if (value is Stream stream)
            {
                var bytes = new MemoryStream(); stream.Position = 0; stream.CopyTo(bytes); bytes.Position = 0;
                value = bytes;
            }
            else if (value is Image image) value = new Bitmap(image);
            if (value != null) copy.SetData(format, false, value);
        }
        return copy;
    }

    static async Task WaitAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(40);
        if (!predicate()) throw new Exception("Listener did not capture expected payload");
    }

    static async Task RunAsync(Control ui)
    {
        var root = Path.GetFullPath("artifacts/debug/checks/clipboard-history/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AppPaths.SetRootForTests(root);
        using var config = new ConfigManager();
        config.Config.ClipboardHistory.MaxItems = 5;
        config.Config.ClipboardHistory.Persist = true;
        var legacy = JsonSerializer.Deserialize("{\"items\":[{\"id\":\"old\",\"text\":\"legacy text\"}]}", AppConfigJsonContext.Default.ClipboardHistoryFile)!;
        File.WriteAllText(AppPaths.ClipboardHistoryPath, JsonSerializer.Serialize(legacy, AppConfigJsonContext.Default.ClipboardHistoryFile));

        var file = Path.Combine(root, "中文文件.txt");
        var folder = Path.Combine(root, "文件夹");
        File.WriteAllText(file, "fixture"); Directory.CreateDirectory(folder);
        var files = new ClipboardHistoryItem { Kind = ClipboardHistoryKind.Files, FilePaths = [file, folder] };
        using var bitmap = new Bitmap(30, 20, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.FromArgb(100, 50, 120, 200));
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        var image = new ClipboardHistoryItem { Kind = ClipboardHistoryKind.Image, ImagePng = stream.ToArray(), ImageWidth = 30, ImageHeight = 20 };
        var mixed = new DataObject(); mixed.SetText("file text fallback"); mixed.SetData(DataFormats.FileDrop, files.FilePaths);
        Check(Read(mixed)?.Kind == ClipboardHistoryKind.Files, "file drop takes precedence over text");
        var malformed = new DataObject(); malformed.SetData("PNG", false, new MemoryStream([1, 2, 3]));
        malformed.SetText("usable fallback");
        Check(Read(malformed)?.Text == "usable fallback", "malformed custom PNG falls back to available content");
        var oversized = new DataObject(); oversized.SetData("PNG", false, new MemoryStream(new byte[16 * 1024 * 1024 + 1]));
        Check(Read(oversized) == null, "oversized PNG is skipped before decoding");

        using (var service = new ClipboardHistoryService(config))
        {
            service.Start(ui);
            await WaitAsync(() => service.GetItems().Any(i => i.Text == "legacy text"));
            Check(service.GetItems()[0].Kind == ClipboardHistoryKind.Text, "legacy text history loads");
            await service.CopyItemAsync(files);
            Check(Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().SequenceEqual(files.FilePaths), "multiple files/folders round trip as file drop");
            using (var effect = Clipboard.GetDataObject()?.GetData("Preferred DropEffect") as Stream)
            {
                Check(effect != null && effect.ReadByte() == 1, "history file replay is copy, never cut");
            }
            await service.CopyItemAsync(image);
            Check(Clipboard.ContainsImage(), "image bitmap format available");
            var restored = Read(Clipboard.GetDataObject()!)!;
            Check(restored.Kind == ClipboardHistoryKind.Image && restored.ImageWidth == 30 && restored.ImageHeight == 20, "image size round trip");
            using (var png = new MemoryStream(restored.ImagePng))
            using (var decoded = new Bitmap(png))
                Check(decoded.GetPixel(0, 0).A == 100, "PNG transparency preserved");
            var data = new DataObject(); data.SetText("image text fallback"); data.SetData("PNG", false, new MemoryStream(image.ImagePng));
            Check(Read(data)?.Kind == ClipboardHistoryKind.Image, "PNG takes precedence over text");
            await service.CopyItemAsync(files);
            Check(service.GetItems()[0].Kind == ClipboardHistoryKind.Files && service.GetItems().Count == 3, "typed deduplication and move to front");
            CheckHistoryViews(config, service);
            using (var target = new Form { Text = "Quickstart typed paste fixture" })
            using (var receiver = new PasteReceiver { Dock = DockStyle.Fill })
            {
                target.Controls.Add(receiver); target.Show(); receiver.Focus();
                foreach (var item in new[] { files, image, new ClipboardHistoryItem { Text = "paste text fixture" } })
                {
                    receiver.Received = null;
                    receiver.Focus();
                    Check(await service.DeliverItemAsync(item, true, target.Handle), "direct paste shortcut sent: " + item.Kind);
                    await WaitAsync(() => receiver.Received?.Kind == item.Kind);
                    Check(receiver.Received!.HasSameContent(item), "target receives original payload: " + item.Kind);
                }
                target.Hide();
            }
            // A different external copy immediately after self-write must not be dropped by a time window.
            Clipboard.SetText("external immediately after copy");
            try { await WaitAsync(() => service.GetItems()[0].Text == "external immediately after copy"); }
            catch
            {
                var field = typeof(ClipboardHistoryService).GetField("_lastWrittenSequence", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Console.WriteLine($"Listener diagnostics: external still current={Clipboard.GetText() == "external immediately after copy"}, self sequence={field.GetValue(service)}, current sequence={GetClipboardSequenceNumber()}");
                throw;
            }
            Check(true, "external copy is not suppressed after self-write");
            Clipboard.SetDataObject(mixed, true);
            await WaitAsync(() => service.GetItems()[0].Kind == ClipboardHistoryKind.Files);
            Check(true, "listener captures file drops");
            Clipboard.SetImage(bitmap);
            await WaitAsync(() => service.GetItems()[0].Kind == ClipboardHistoryKind.Image);
            Check(true, "listener captures bitmaps");
            Check(!await service.DeliverItemAsync(files, true, IntPtr.Zero) && Clipboard.ContainsFileDropList(), "invalid paste target falls back to typed copy");
            Check(files.SearchText.Contains("中文文件") && files.Preview().Contains("中文文件"), "file names searchable and previewed");
            service.BeginCaptureSuppress();
            Clipboard.SetText("suppressed capture fixture");
            await Task.Delay(200);
            Check(service.GetItems().All(i => i.Text != "suppressed capture fixture"), "selection-capture suppression preserved");
            service.EndCaptureSuppress();
            await Task.Delay(550);
        }

        using (var service = new ClipboardHistoryService(config))
        {
            service.Start(ui);
            await WaitAsync(() => service.GetItems().Any(i => i.Kind == ClipboardHistoryKind.Image));
            Check(service.GetItems().Any(i => i.Kind == ClipboardHistoryKind.Files), "files and images persist across service restart");
            File.Delete(file);
            try { await service.CopyItemAsync(files); throw new Exception("Missing files accepted"); }
            catch (IOException) { Check(true, "missing file gives explicit failure"); }
            for (var i = 0; i < 7; i++) await service.CopyPlainTextAsync("limit fixture " + i);
            Check(service.GetItems().Count == 5, "history count limit enforced");
            var removed = service.GetItems()[0];
            Check(service.Remove(removed.Id) && service.GetItems().Count == 4, "remove history item");
            service.Clear();
            Check(service.GetItems().Count == 0, "clear history");
            var disk = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.ClipboardHistoryPath), AppConfigJsonContext.Default.ClipboardHistoryFile)!;
            Check(disk.Items.Count == 0, "clear also removes persisted images and files");
            config.Config.ClipboardHistory.Persist = false;
            service.ApplyConfigLimits();
            Check(!File.Exists(AppPaths.ClipboardHistoryPath), "disabling persistence removes disk history");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetClipboardSequenceNumber();

    sealed class PasteReceiver : TextBox
    {
        public ClipboardHistoryItem? Received;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0302) { Received = Read(Clipboard.GetDataObject()!); return; }
            base.WndProc(ref m);
        }
    }

    static void CheckHistoryViews(ConfigManager config, ClipboardHistoryService service)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var launcher = new ProcessLauncher(config);
        using (var popup = new MainPopup(config, launcher, service))
        {
            var type = popup.GetType();
            var tab = type.GetField("_activeTab", flags)!;
            tab.SetValue(popup, Enum.Parse(tab.FieldType, "ClipboardHistory"));
            type.GetMethod("RefreshClipboardHistoryList", flags)!.Invoke(popup, null);
            var list = (ListView)type.GetField("_listView", flags)!.GetValue(popup)!;
            Check(list.Items.Cast<ListViewItem>().Any(i => i.Tag is ClipboardHistoryItem { Kind: ClipboardHistoryKind.Image }
                && i.ImageKey.StartsWith("<CLIP:")), "classic panel displays image thumbnail");
            Check(list.Items.Cast<ListViewItem>().Any(i => i.Text.Contains("中文文件")), "classic panel displays file names");
            var search = (TextBox)type.GetField("_searchBox", flags)!.GetValue(popup)!;
            search.Text = "中文文件";
            type.GetMethod("RefreshClipboardHistoryList", flags)!.Invoke(popup, null);
            Check(list.Items.Count == 1 && list.Items[0].Tag is ClipboardHistoryItem { Kind: ClipboardHistoryKind.Files }, "classic panel searches file paths");
        }
        var radialType = typeof(MainPopup).Assembly.GetType("Quickstart.UI.RadialMenuPopup")!;
        using var radial = (Form)Activator.CreateInstance(radialType, config, launcher, service)!;
        var active = radialType.GetField("_activeTab", flags)!;
        active.SetValue(radial, Enum.Parse(Nullable.GetUnderlyingType(active.FieldType)!, "ClipboardHistory"));
        radialType.GetMethod("LoadItemsForActiveTab", flags)!.Invoke(radial, [false]);
        var items = ((System.Collections.IEnumerable)radialType.GetField("_items", flags)!.GetValue(radial)!).Cast<object>().ToArray();
        var imageSlot = items.Single(i => i.GetType().GetProperty("Payload")!.GetValue(i) is ClipboardHistoryItem { Kind: ClipboardHistoryKind.Image });
        Check((bool)imageSlot.GetType().GetProperty("OwnsIcon")!.GetValue(imageSlot)!, "radial panel displays image thumbnail");
    }
}
