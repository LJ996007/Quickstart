using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using Quickstart.Core;
using Quickstart.UI;

static class PreviewChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void CheckCompactHeader(OfficePreviewPane pane)
    {
        pane.PerformLayout();
        var title = Field<Label>(pane, "_title");
        var toolbar = Field<TableLayoutPanel>(pane, "_toolbar");
        var body = Field<Panel>(pane, "_body");
        var rowHeight = (int)Math.Round(36 * pane.DeviceDpi / 96d);
        Check(title.Parent == toolbar, "Filename is not in the action row");
        Check(Math.Abs(toolbar.Height - rowHeight) <= 1 && body.Top <= rowHeight + 8 * pane.DeviceDpi / 96d,
            $"Preview header wastes vertical space: toolbar={toolbar.Bounds}, body={body.Bounds}");
        var controls = toolbar.Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.Left).ToArray();
        foreach (var control in controls)
            Check(control.Top == title.Top && control.Height == title.Height && toolbar.ClientRectangle.Contains(control.Bounds),
                $"Preview action is not aligned: {control.Text}: {control.Bounds}");
        for (var i = 1; i < controls.Length; i++)
            Check(controls[i - 1].Right <= controls[i].Left, "Preview header controls overlap");
        Check(title.Width > 0, "Filename has no space in the header");
    }
    static async Task Until(Func<bool> condition, string message, int ms = 15000)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try { if (condition()) return; }
            catch (System.Runtime.InteropServices.ExternalException) { /* Clipboard briefly owned by another process. */ }
            if (watch.ElapsedMilliseconds > ms) throw new TimeoutException(message);
            await Task.Delay(50);
        }
    }
    static async Task<string> Js(WebView2 web, string script) => await web.ExecuteScriptAsync(script);
    static async Task UntilJs(WebView2 web, string script, string message)
    {
        var clock = Stopwatch.StartNew();
        while (await Js(web, script) != "true")
        {
            if (clock.ElapsedMilliseconds > 15000) throw new TimeoutException(message);
            await Task.Delay(50);
        }
    }

    static async Task CheckStandalone(string root, string docx, Form owner)
    {
        var type = typeof(OfficePreviewPane).Assembly.GetType("Quickstart.UI.FilePreviewPopup")!;
        using var window = (Form)Activator.CreateInstance(type)!;
        var pane = Field<OfficePreviewPane>(window, "_pane");
        void ShowFile(string path) => type.GetMethod("ShowFile")!.Invoke(window, [path, owner.Bounds]);
        ShowFile(docx);
        await Until(() => Field<bool>(pane, "_ready"), "Standalone DOCX did not render");
        var web = Field<WebView2>(pane, "_web");
        const string widths = "(()=>{const p=document.querySelector('section.docx'), c=document.querySelector('#content');return c.scrollWidth<=c.clientWidth+1&&p.getBoundingClientRect().right<=c.clientWidth+1})()";
        await UntilJs(web, widths, "DOCX overflows at its default width");
        int Hit(int x, int y)
        {
            var p = window.PointToScreen(new Point(x, y));
            return (int)SendMessage(window.Handle, 0x84, IntPtr.Zero, (IntPtr)((p.Y << 16) | (p.X & 0xffff)));
        }
        Check(Hit(window.Width - 2, window.Height / 2) == 11 && Hit(window.Width - 3, window.Height - 3) == 17
            && Hit(40, 40) == 1, "The preview frame must expose native edge and corner sizing without covering the content");
        var dpi = window.DeviceDpi / 96d;
        var toolbar = Field<TableLayoutPanel>(pane, "_toolbar");
        Check(toolbar.Height < 30 * dpi, "Standalone toolbar must stay compact");
        window.Size = new Size((int)(470 * dpi), (int)(500 * dpi));
        type.GetMethod("OnResizeEnd", Private)!.Invoke(window, [EventArgs.Empty]);
        await UntilJs(web, widths, "DOCX overflows after making the preview narrower");
        Check(await Js(web, "Number(document.querySelector('section.docx').style.zoom) < 0.8") == "true", "DOCX did not refit after resize");
        await Js(web, """
            (()=>{const p=[...document.querySelectorAll('section.docx p')].find(e=>e.textContent.startsWith('合同条款'));
            p.scrollIntoView({block:'center'}); const range=document.createRange(); range.selectNodeContents(p);
            getSelection().removeAllRanges();getSelection().addRange(range);})()
            """);
        await UntilJs(web, "!document.querySelector('#selection-copy').hidden", "Selecting DOCX text did not show the floating copy button");
        await using (var shot = File.Create(Path.Combine(root, "standalone-selection.png")))
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
        await Js(web, "document.querySelector('#selection-copy').click()");
        await Until(() => Clipboard.GetText() == "合同条款：中文正文可以选择、复制。金额 1,234.50 元。"
            && !Clipboard.ContainsText(TextDataFormat.Html), "Floating copy did not write plain DOCX text");
        var manualSize = window.Size;
        ShowFile(Path.Combine(root, "预览 测试.xlsx"));
        await Until(() => Field<bool>(pane, "_ready"), "Standalone XLSX did not render");
        Check(window.Size == manualSize, "Opening another file lost the manual size");
        using var point = JsonDocument.Parse(await Js(web, """
            (()=>{const grid=document.querySelector('.x-spreadsheet-overlayer');
            const zoom=Number(document.querySelector('.vue-office-excel').style.zoom)||1;
            const {left,top}=grid.getBoundingClientRect();return {x:left+85*zoom,y:top+57*zoom};})()
            """));
        var x = point.RootElement.GetProperty("x").GetDouble();
        var y = point.RootElement.GetProperty("y").GetDouble();
        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new { type = "mousePressed", x, y, button = "left", clickCount = 1 }));
        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
            JsonSerializer.Serialize(new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }));
        await UntilJs(web, "!document.querySelector('#selection-copy').hidden", "Selecting a cell did not show the floating copy button");
        await Js(web, "document.querySelector('#selection-copy').click()");
        await Until(() => Clipboard.GetText() == "中文测试", "Floating copy did not write plain cell text");
        ShowFile(Path.Combine(root, "文字 PDF.pdf"));
        await Until(() => web.Source?.Fragment == "#view=FitH", "Standalone PDF did not use fit-to-width");
        Console.WriteLine("PASS standalone preview: narrow viewport fit, compact header, manual size retention, DOCX/XLSX floating plain-text copy and PDF fit-to-width.");
    }

    static void CreateFixtures(string root)
    {
        using (var zip = ZipFile.Open(Path.Combine(root, "合同 中文.docx"), ZipArchiveMode.Create))
        {
            void Add(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
            Add("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            Add("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            Add("word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
                <w:p><w:pPr><w:spacing w:after="240"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="36"/></w:rPr><w:t>Quickstart 文档预览</w:t></w:r></w:p>
                <w:p><w:r><w:t>合同条款：中文正文可以选择、复制。金额 1,234.50 元。</w:t></w:r></w:p>
                <w:p><w:r><w:t>Preview copy test — local files stay on this computer.</w:t></w:r></w:p>
                <w:tbl><w:tblPr><w:tblW w:w="8000" w:type="dxa"/></w:tblPr><w:tblGrid><w:gridCol w:w="4000"/><w:gridCol w:w="4000"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>项目名称</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>采购服务</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr></w:body></w:document>
                """);
        }
        // Small, valid two-page text PDF, including mixed page dimensions.
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        void Obj(string text) { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append($"{offsets.Count - 1} 0 obj\n{text}\nendobj\n"); }
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj("<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>");
        Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>");
        Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        foreach (var content in new[] { "BT /F1 20 Tf 50 750 Td (Quickstart PDF copy test) Tj 0 -40 Td (Select this text and press Ctrl+C.) Tj ET", "BT /F1 20 Tf 50 500 Td (Second page - landscape) Tj ET" })
            Obj($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(Path.Combine(root, "文字 PDF.pdf"), pdf.ToString(), Encoding.ASCII);
        File.WriteAllText(Path.Combine(root, "损坏.docx"), "not a zip");
    }

    // Run through MouseUp on a real folder row and wait for the renderer's ready
    // message. These timings include window placement, dispatch, I/O and rendering.
    static int MeasureLatency(string root, ConfigManager config)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "latency-files")).FullName;
        const string word = "合同 中文.docx", excel = "预览 测试.xlsx";
        foreach (var name in new[] { word, excel, "预览 测试.xls", "文字 PDF.pdf", "万行表格.xlsx" })
            File.Copy(Path.Combine(root, name), Path.Combine(folder, name), overwrite: true);
        File.WriteAllText(Path.Combine(folder, "说明.md"), "# 目录预览");
        using (var png = new Bitmap(640, 180)) png.Save(Path.Combine(folder, "图片.png"), System.Drawing.Imaging.ImageFormat.Png);
        var type = typeof(OfficePreviewPane).Assembly.GetType("Quickstart.UI.RadialFolderBrowserPopup")!;
        using var browser = (Form)Activator.CreateInstance(type, config)!;
        using var search = new FileSearchPopup(config, new ProcessLauncher(config));
        var embedded = Field<OfficePreviewPane>(search, "_preview");
        var list = Field<ListView>(browser, "_results");
        var displayed = new List<string>();
        var previewEvent = typeof(OfficePreviewPane).GetEvent("FilePreviewed", BindingFlags.Static | BindingFlags.NonPublic)!;
        Action<string> record = displayed.Add;
        previewEvent.GetAddMethod(true)!.Invoke(null, [record]);
        var result = 0;
        void Click(string name)
        {
            var row = list.Items.Cast<ListViewItem>().Single(item => item.Text == name);
            var bounds = row.SubItems[0].Bounds;
            type.GetMethod("OnResultClick", Private)!.Invoke(browser, [list,
                new MouseEventArgs(MouseButtons.Left, 1, bounds.Left + 4, bounds.Top + bounds.Height / 2, 0)]);
        }
        browser.Shown += async (_, _) =>
        {
            try
            {
                await Until(() => list.Items.Count == 7, "Folder rows did not load");
                await Task.Delay(650); // same think time for cold and warm comparisons
                Check(displayed.Count == 0, "Warmup recorded a preview");
                foreach (var name in new[] { word, excel, "图片.png", "说明.md", word, excel, "预览 测试.xls", "万行表格.xlsx" })
                {
                    var previousCount = displayed.Count;
                    var clock = Stopwatch.StartNew();
                    Click(name);
                    await Until(() => Field<Form?>(browser, "_previewPopup") is { Visible: true } f &&
                        Field<string?>(Field<OfficePreviewPane>(f, "_pane"), "_path") == Path.Combine(folder, name), "Wrong file selected");
                    var shown = clock.ElapsedMilliseconds;
                    var pane = Field<OfficePreviewPane>(Field<Form>(browser, "_previewPopup"), "_pane");
                    await Until(() => Field<bool>(pane, "_ready"), name + " did not render", 30000);
                    Check(displayed.Count == previousCount + 1 && displayed[^1] == Path.Combine(folder, name), "Displayed file missing or reported twice: " + name);
                    Console.WriteLine($"PERF {name}: preview selected {shown} ms, content ready {clock.ElapsedMilliseconds} ms");
                    if (name == "图片.png") Check(Field<Size?>(Field<Form>(browser, "_previewPopup"), "_imagePixels") == new Size(640, 180),
                        "Preview sizing did not use the renderer's decoded dimensions");
                }
                var window = Field<Form>(browser, "_previewPopup");
                var preview = Field<OfficePreviewPane>(window, "_pane");
                var count = displayed.Count;
                Click(word);
                var first = preview.PreviewAsync(Path.Combine(folder, word));
                Check(ReferenceEquals(first, preview.PreviewAsync(Path.Combine(folder, word))), "Duplicate input restarted a pending read");
                Click(excel);
                await Until(() => Field<bool>(preview, "_ready") && Field<OfficePreviewDocument>(preview, "_document").Kind == "xlsx",
                    "A stale selection replaced the last document");
                var web = Field<WebView2>(preview, "_web");
                await Js(web, "window.chrome.webview.postMessage({type:'ready',token:new URLSearchParams(location.search).get('token'),text:''})");
                Click("文字 PDF.pdf");
                await Until(() => web.Source?.Fragment == "#view=FitH" && displayed.LastOrDefault() == Path.Combine(folder, "文字 PDF.pdf"), "PDF navigation was not recorded");
                Check(displayed.Skip(count).SequenceEqual(new[] { Path.Combine(folder, excel), Path.Combine(folder, "文字 PDF.pdf") }), "Stale or duplicate preview entered history");
                count = displayed.Count;
                await preview.PreviewAsync(Path.Combine(root, "损坏.docx"));
                var badPdf = Path.Combine(root, "invalid-preview.pdf");
                File.WriteAllText(badPdf, "invalid PDF fixture");
                await preview.PreviewAsync(badPdf);
                Check(Field<Label>(preview, "_message").Visible && displayed.Count == count, "Failed previews entered history");
                window.Hide();
                Check(Field<OfficePreviewDocument?>(preview, "_document") == null, "Hidden preview retained the document");
                Check(web.CoreWebView2.BrowserProcessId == Field<WebView2>(embedded, "_web").CoreWebView2.BrowserProcessId,
                    "The two preview windows did not reuse the warmed browser process");
                browser.Hide();
                await search.ShowFolderAsync(folder);
                var results = Field<ListView>(search, "_results");
                var item = results.Items.Cast<ListViewItem>().Single(row => row.Text == word);
                results.Focus(); item.Selected = true;
                var cell = item.SubItems[0].Bounds;
                typeof(Control).GetMethod("OnMouseClick", Private)!.Invoke(results, [new MouseEventArgs(MouseButtons.Left, 1, cell.Left + 4, cell.Top + cell.Height / 2, 0)]);
                Check(Field<string?>(embedded, "_path") == Path.Combine(folder, word), "Search click deferred selection");
                await Until(() => Field<bool>(embedded, "_ready"), "Embedded Word preview failed");
                CheckCompactHeader(embedded);
                results.Items.Cast<ListViewItem>().Single(row => row.Text == excel).Selected = true;
                await Until(() => Field<OfficePreviewDocument?>(embedded, "_document")?.Kind == "xlsx" && Field<bool>(embedded, "_ready"), "Search keyboard selection failed");
                Check(displayed.Skip(count).SequenceEqual(new[] { Path.Combine(folder, word), Path.Combine(folder, excel) }), "Search previews did not record the displayed paths");
                Field<CheckBox>(search, "_previewEnabled").Checked = false;
                Check(Field<OfficePreviewDocument?>(embedded, "_document") == null, "Collapsing search preview retained a document");
                Console.WriteLine("PASS directory and search input, shared browser, duplicate reads, image dimensions, latest document, PDF, ready-only preview history and hide cleanup.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
            finally { Application.ExitThread(); }
        };
        async void WarmOnIdle(object? sender, EventArgs e)
        {
            Application.Idle -= WarmOnIdle;
            try
            {
                // Model a returning tray user. Measure the hidden controller startup
                // separately from the user's subsequent directory click.
                Directory.CreateDirectory(Path.Combine(AppPaths.Root, "office-preview", "webview"));
                var clock = Stopwatch.StartNew();
                await (Task)typeof(FileSearchPopup).GetMethod("WarmPreviewIfPreviouslyUsedAsync", Private)!.Invoke(search, null)!;
                Check(!search.Visible && Field<WebView2>(embedded, "_web").CoreWebView2 != null, "Hidden tray warmup did not initialize the preview");
                Console.WriteLine($"PERF idle WebView2 startup {clock.ElapsedMilliseconds} ms");
                var area = Screen.PrimaryScreen!.WorkingArea;
                type.GetMethod("ShowFolder")!.Invoke(browser, [folder, "预览", new Rectangle(area.Left + 10, area.Top + 24, 140, 60)]);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; Application.ExitThread(); }
        }
        Application.Idle += WarmOnIdle;
        Application.Run();
        previewEvent.GetRemoveMethod(true)!.Invoke(null, [record]);
        return result;
    }

    [STAThread]
    static int Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        var interactive = args.Contains("--interactive");
        Directory.CreateDirectory(root);
        var docx = Path.Combine(root, "合同 中文.docx");
        // Overwrite only these test-owned fixture files, never user documents.
        if (File.Exists(docx)) File.Delete(docx);
        CreateFixtures(root);
        AppPaths.SetRootForTests(Path.Combine(root, "config"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        using var config = new ConfigManager();
        config.Config.FileDialogNavigationEnabled = false;
        if (args.Contains("--measure-latency")) return MeasureLatency(root, config);
        using var popup = new FileSearchPopup(config, new ProcessLauncher(config)) { TopMost = false, ShowInTaskbar = true };
        var pane = Field<OfficePreviewPane>(popup, "_preview");
        var list = Field<ListView>(popup, "_results");
        var failure = 0;
        popup.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(200);
                foreach (var name in new[] { "合同 中文.docx", "预览 测试.xlsx", "预览 测试.xls", "文字 PDF.pdf", "万行表格.xlsx", "损坏.docx" })
                {
                    var path = Path.Combine(root, name);
                    list.Items.Add(new ListViewItem([name, root, new FileInfo(path).Length.ToString(), "测试夹具"])
                    { Tag = new FileSearchResult(path, false, new FileInfo(path).Length, DateTime.Now) });
                }
                await pane.WarmUpAsync();
                var diagnosticWeb = Field<WebView2>(pane, "_web");
                diagnosticWeb.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) => Console.WriteLine("JS ERROR: " + e.ParameterObjectAsJson);
                diagnosticWeb.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) =>
                {
                    if (e.ParameterObjectAsJson.Contains("\"type\":\"error\"")) Console.WriteLine("CONSOLE: " + e.ParameterObjectAsJson);
                };
                await diagnosticWeb.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                var stopwatch = Stopwatch.StartNew();
                await pane.PreviewAsync(docx);
                await Until(() => Field<bool>(pane, "_ready"), "DOCX did not render: " + Field<Label>(pane, "_message").Text);
                var web = Field<WebView2>(pane, "_web");
                CheckCompactHeader(pane);
                Console.WriteLine($"PASS DOCX preview (browser prewarmed): {stopwatch.ElapsedMilliseconds} ms");
                Check((await Js(web, "document.body.innerText")).Contains("合同条款"), "Chinese DOCX body missing");
                await Js(web, "window.quickstartCopy(true)");
                await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Contains("采购服务"), "DOCX copy did not reach clipboard");
                // A former file token must never resolve after the selection changes.
                var oldDocument = Field<OfficePreviewDocument>(pane, "_document");
                await using (var image = File.Create(Path.Combine(root, "docx-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                for (var n = 0; n < 2; n++)
                {
                    stopwatch.Restart();
                    await pane.PreviewAsync(Path.Combine(root, n == 0 ? "预览 测试.xlsx" : "预览 测试.xls"));
                    await Until(() => Field<bool>(pane, "_ready"), "Excel did not render: " + Field<Label>(pane, "_message").Text);
                    Console.WriteLine($"PASS {(n == 0 ? "XLSX" : "XLS")} preview: {stopwatch.ElapsedMilliseconds} ms");
                    await Js(web, "window.quickstartCopy(true)");
                    await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Contains("中文测试\t0\t")
                        && (Clipboard.GetText().Contains("1,234.50") || (n == 1 && Clipboard.GetText().Contains("1234.5"))), "Excel formatting/zero/Chinese copy failed");
                    Check(Clipboard.GetText().Contains("合同条款\t2\t2469"), "Cached formula value missing");
                    await using var image = File.Create(Path.Combine(root, n == 0 ? "xlsx-preview.png" : "xls-preview.png"));
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                    // Exercise the real tab click handler, then copy the active sheet.
                    await Js(web, "[...document.querySelectorAll('.x-spreadsheet-bottombar li')].find(e=>e.textContent==='第二页').click()");
                    await Js(web, "window.quickstartCopy(true)");
                    await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Trim() == "第二工作表\r\n切换复制成功", "Active worksheet copy failed");
                    await Js(web, "window.quickstartCopy(false)");
                    await Until(() => Clipboard.ContainsText() && Clipboard.GetText() == "第二工作表", "Selected-cell copy failed");
                }
                var staleStatus = await Js(web, $"fetch('{oldDocument.ResourcePath}').then(r=>window.staleStatus=r.status)");
                await Until(() => web.CoreWebView2 != null, "WebView disposed");
                await Task.Delay(100);
                Check(await Js(web, "window.staleStatus") == "403", "Stale file token was still readable");
                await Js(web, "fetch('https://example.com/').then(r=>window.externalBlocked=r.status===403).catch(()=>window.externalBlocked=true)");
                await Task.Delay(100);
                Check(await Js(web, "window.externalBlocked") == "true", "External request not blocked");
                Console.WriteLine("PASS copy: DOCX, XLSX, XLS; Chinese, zero, formatted number, cached formula; stale token and external requests denied.");
                var abandoned = pane.PreviewAsync(docx);
                var latest = pane.PreviewAsync(Path.Combine(root, "预览 测试.xlsx"));
                await Task.WhenAll(abandoned, latest);
                await Until(() => Field<bool>(pane, "_ready"), "Rapid-switch preview not ready");
                Check(Field<OfficePreviewDocument>(pane, "_document").Kind == "xlsx", "Old request replaced current selection");
                Check(ReferenceEquals(web, Field<WebView2>(pane, "_web")), "Browser was recreated per document");
                var toggle = Field<CheckBox>(popup, "_previewEnabled");
                toggle.Checked = false;
                Check(Field<SplitContainer>(popup, "_split").Panel2Collapsed && Field<OfficePreviewDocument?>(pane, "_document") == null, "Hidden preview retained old document");
                toggle.Checked = true;
                var split = Field<SplitContainer>(popup, "_split");
                // The responsive layout budgets useful pane widths instead of a fixed divider position.
                Check(!split.Panel2Collapsed && split.Panel1.Width >= 360 * popup.DeviceDpi / 96d &&
                    split.Panel2.Width >= 300 * popup.DeviceDpi / 96d, "DPI-scaled list/preview panes are too narrow");
                // A user selection must drive the real debounced search-to-preview route.
                list.Focus(); list.Items[1].Selected = true;
                await Until(() => Field<OfficePreviewDocument?>(pane, "_document")?.Kind == "xlsx" && Field<bool>(pane, "_ready"), "Search selection did not trigger preview");
                Console.WriteLine("PASS search integration: selection preview, collapsed-state cleanup, DPI split, active-sheet and selected-cell copy.");
                await pane.PreviewAsync(Path.Combine(root, "损坏.docx"));
                Check(Field<Label>(pane, "_message").Visible && !Field<Button>(pane, "_copy").Enabled, "Corrupt-file fallback missing");
                await pane.PreviewAsync(Path.Combine(root, "missing.pdf"));
                Check(Field<Label>(pane, "_message").Visible, "Missing-file fallback missing");
                using (var big = File.Create(Path.Combine(root, "oversize.pdf"))) big.SetLength(OfficePreviewDocument.MaxFileBytes + 1);
                await pane.PreviewAsync(Path.Combine(root, "oversize.pdf"));
                Check(Field<Label>(pane, "_message").Text.Contains("64 MB"), "Large-file limit missing");
                Console.WriteLine("PASS lifecycle: browser reuse, rapid switching, missing/corrupt/oversized files.");
                stopwatch.Restart();
                await pane.PreviewAsync(Path.Combine(root, "万行表格.xlsx"));
                await Until(() => Field<bool>(pane, "_ready"), "10k workbook timed out", 30000);
                Console.WriteLine($"PASS 10,000-row workbook preview: {stopwatch.ElapsedMilliseconds} ms");
                var markdown = Path.Combine(root, "说明.md");
                await File.WriteAllTextAsync(markdown, "# 中文 Markdown\n\n**复制测试**\n\n| 名称 | 数值 |\n| --- | --- |\n| 金额 | 123 |\n\n```cs\nvar x = 1;\n```\n\n<script>window.injected=true</script><img src=x onerror='window.injected=true'>");
                await pane.PreviewAsync(markdown);
                await Until(() => Field<bool>(pane, "_ready"), "Markdown not ready");
                Check(await Js(web, "!!document.querySelector('h1') && !!document.querySelector('table') && !!document.querySelector('pre code') && !window.injected && !document.querySelector('#content script, #content [onerror]')") == "true", "Markdown structure/sanitization failed");
                await Js(web, "window.quickstartCopy(true)");
                await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Contains("中文 Markdown"), "Markdown copy failed");
                await using (var shot = File.Create(Path.Combine(root, "markdown-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
                await File.WriteAllTextAsync(Path.Combine(root, "说明.markdown"), "# UTF16 中文", Encoding.Unicode);
                await pane.PreviewAsync(Path.Combine(root, "说明.markdown"));
                await Until(() => Field<bool>(pane, "_ready"), "Markdown alias not ready");
                Check((await Js(web, "document.body.innerText")).Contains("UTF16 中文"), "UTF16 Markdown decoding failed");
                var localRoot = Path.Combine(root, "markdown-images");
                var localDocs = Path.Combine(localRoot, "文档");
                var localAssets = Path.Combine(localDocs, "图片 目录");
                Directory.CreateDirectory(localAssets);
                var localImage = Path.Combine(localAssets, "示例 #1.png");
                using (var sample = new Bitmap(800, 200))
                {
                    using var graphics = Graphics.FromImage(sample);
                    graphics.Clear(Color.CornflowerBlue);
                    graphics.DrawString("Markdown local image", SystemFonts.DefaultFont, Brushes.White, 30, 50);
                    sample.Save(localImage, System.Drawing.Imaging.ImageFormat.Png);
                    sample.Save(Path.Combine(localRoot, "上级.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                var illustrated = Path.Combine(localDocs, "图文.md");
                await File.WriteAllTextAsync(illustrated, "# 本地图文\n\n![编码路径](图片%20目录/示例%20%231.png)\n\n![上级](../上级.png)\n\n"
                    + "![绝对路径](<" + localImage.Replace('\\', '/').Replace("#", "%23") + ">)\n\n"
                    + "<img alt=\"HTML图片\" src=\"./图片 目录/示例 %231.png\">\n\n"
                    + "![文件URI](" + new Uri(localImage).AbsoluteUri + ")\n\n"
                    + "![缺失](missing.png)\n\n![外网](https://example.invalid/a.png)\n\n![非图片](图文.md)\n\n正文仍可复制");
                await pane.PreviewAsync(illustrated);
                await Until(() => Field<bool>(pane, "_ready"), "Illustrated Markdown not ready");
                var localImagesReady = false;
                for (var wait = 0; wait < 100; wait++)
                {
                    localImagesReady = await Js(web, "[...document.querySelectorAll('#content img')].filter(i=>i.complete&&i.naturalWidth===800).length===5 && document.querySelectorAll('.image-missing').length===3") == "true";
                    if (localImagesReady) break;
                    await Task.Delay(50);
                }
                Check(localImagesReady, "Local Markdown images missing: " + await Js(web, "document.querySelector('#content').innerHTML"));
                Check(await Js(web, "document.querySelectorAll('.image-missing').length") == "3", "Missing/external/non-image references must fail inline");
                Check(await Js(web, "[...document.querySelectorAll('#content img')].every(i=>i.getBoundingClientRect().width<=document.querySelector('#content').clientWidth)") == "true", "Markdown image overflows");
                await Js(web, "window.quickstartCopy(true)");
                await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Contains("正文仍可复制"), "Illustrated Markdown copy failed");
                await using (var shot = File.Create(Path.Combine(root, "markdown-local-images.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
                var formerImageToken = Field<OfficePreviewDocument>(pane, "_document").Token;
                await pane.PreviewAsync(markdown);
                await Until(() => Field<bool>(pane, "_ready"), "Switch from illustrated Markdown failed");
                await Js(web, "(()=>{const i=new Image();i.id='stale-image';i.src=" + JsonSerializer.Serialize($"/image/{formerImageToken}?path=../上级.png") + ";document.querySelector('#content').append(i)})()");
                await Task.Delay(150);
                Check(await Js(web, "document.querySelector('#stale-image').naturalWidth") == "0", "Stale image token remained readable");
                Console.WriteLine("PASS Markdown local images: encoded Chinese/spaces/#, parent and absolute paths, file URI, HTML img, missing/remote/non-image rejection, copying and stale tokens.");
                using (var bitmap = new Bitmap(320, 180))
                {
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.Clear(Color.CornflowerBlue);
                    graphics.DrawString("Quickstart 图片", SystemFonts.DefaultFont, Brushes.White, 20, 40);
                    foreach (var pair in new[] { ("png", System.Drawing.Imaging.ImageFormat.Png), ("jpg", System.Drawing.Imaging.ImageFormat.Jpeg), ("jpeg", System.Drawing.Imaging.ImageFormat.Jpeg), ("bmp", System.Drawing.Imaging.ImageFormat.Bmp), ("gif", System.Drawing.Imaging.ImageFormat.Gif) })
                        bitmap.Save(Path.Combine(root, "图片." + pair.Item1), pair.Item2);
                }
                using (var icon = File.Create(Path.Combine(root, "图片.ico"))) SystemIcons.Information.Save(icon);
                var webp = JsonSerializer.Deserialize<string>(await Js(web, "(()=>{const c=document.createElement('canvas');c.width=320;c.height=180;c.getContext('2d').fillRect(0,0,320,180);return c.toDataURL('image/webp').split(',')[1]})()"))!;
                await File.WriteAllBytesAsync(Path.Combine(root, "图片.webp"), Convert.FromBase64String(webp));
                foreach (var extension in new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico" })
                {
                    await pane.PreviewAsync(Path.Combine(root, "图片." + extension));
                    await Until(() => Field<bool>(pane, "_ready"), "Image not ready: " + extension);
                    Check(await Js(web, "document.querySelector('#content img').naturalWidth > 0") == "true", "Image decode failed");
                    Check(Field<Button>(pane, "_copy").Text == "复制图片" && !Field<Button>(pane, "_copyAll").Visible, "Image toolbar incorrect");
                    await Js(web, "document.querySelector('#content img').click()");
                    Check(await Js(web, "document.querySelector('#content img').classList.contains('actual-size')") == "true", "Image zoom failed");
                    Clipboard.Clear();
                    await Js(web, "window.quickstartCopy()");
                    // Clipboard managers/OLE can advertise the format before its image is readable.
                    Image? copied = null;
                    try
                    {
                        await Until(() =>
                        {
                            if (Field<Label>(pane, "_hint").Text != "已复制内容") return false;
                            copied?.Dispose();
                            copied = Clipboard.GetImage();
                            return copied != null && copied.Width > 0;
                        }, "Image clipboard is not readable: " + extension);
                    }
                    finally { copied?.Dispose(); }
                }
                await using (var shot = File.Create(Path.Combine(root, "image-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
                using (var tall = new Bitmap(320, 2000)) tall.Save(Path.Combine(root, "长图.png"), System.Drawing.Imaging.ImageFormat.Png);
                await pane.PreviewAsync(Path.Combine(root, "长图.png"));
                await Until(() => Field<bool>(pane, "_ready"), "Tall image not ready");
                // Native resize is asynchronous; visualViewport retains fractional CSS pixels at high DPI.
                Console.WriteLine("Tall image geometry: " + await Js(web, "JSON.stringify({height:document.querySelector('#content img').getBoundingClientRect().height,viewport:innerHeight,visualHeight:visualViewport.height,maxHeight:getComputedStyle(document.querySelector('#content img')).maxHeight,dpr:devicePixelRatio})"));
                await UntilJs(web, "(()=>{const r=document.querySelector('#content img').getBoundingClientRect();const h=visualViewport.height;const epsilon=1/devicePixelRatio;return r.height<=h-24+epsilon&&r.top>=-epsilon&&r.bottom<=h+epsilon})()", "Tall image did not fit viewport");
                Console.WriteLine("PASS Markdown render/table/code/sanitization/copy/UTF16; PNG/JPG/JPEG/GIF/BMP/WebP/ICO decode, zoom and clipboard images.");
                await pane.PreviewAsync(Path.Combine(root, "文字 PDF.pdf"));
                await Until(() => web.Source?.AbsolutePath.EndsWith(".pdf") == true, "PDF did not navigate");
                await Task.Delay(1000);
                Check(!Field<Label>(pane, "_message").Visible, "PDF failed to load");
                CheckCompactHeader(pane);
                var originalSplit = split.SplitterDistance;
                foreach (var logicalWidth in new[] { 340, 480 })
                {
                    var desired = (int)Math.Round(logicalWidth * popup.DeviceDpi / 96d);
                    split.SplitterDistance = Math.Clamp(split.Width - split.SplitterWidth - desired, split.Panel1MinSize,
                        split.Width - split.SplitterWidth - split.Panel2MinSize);
                    CheckCompactHeader(pane);
                }
                split.SplitterDistance = originalSplit;
                using (var chrome = new Bitmap(pane.Width, Math.Min(pane.Height, (int)Math.Round(44 * pane.DeviceDpi / 96d))))
                {
                    pane.DrawToBitmap(chrome, new Rectangle(0, 0, chrome.Width, chrome.Height));
                    chrome.Save(Path.Combine(root, "compact-preview-header.png"));
                }
                await using (var image = File.Create(Path.Combine(root, "pdf-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                Console.WriteLine("PASS PDF navigation; inspect pdf-preview.png and perform native Ctrl+C in interactive mode.");
                await CheckStandalone(root, docx, popup);
                if (interactive)
                {
                    popup.Text = "Quickstart · Office 预览验收";
                    Console.WriteLine("Interactive fixture window ready. Close the process after UI checks.");
                }
            }
            catch (Exception ex)
            {
                failure = 1; Console.Error.WriteLine(ex);
                Console.WriteLine("MESSAGE: " + Field<Label>(pane, "_message").Text + " / " + Field<Label>(pane, "_hint").Text);
                var web = Field<WebView2>(pane, "_web");
                if (web?.CoreWebView2 != null) Console.WriteLine("PAGE: " + await Js(web, "JSON.stringify({url:location.href,body:document.body?.innerText})"));
                Console.WriteLine("Viewer resource names: " + string.Join(", ", typeof(OfficePreviewPane).Assembly.GetManifestResourceNames()
                    .Where(name => name.StartsWith("OfficePreview/", StringComparison.Ordinal))));
            }
            finally { if (!interactive || failure != 0) { popup.Dispose(); Application.ExitThread(); } }
        };
        popup.ShowSearch("");
        Application.Run();
        return failure;
    }
}
