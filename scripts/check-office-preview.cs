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
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
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
                Check(Math.Abs(split.SplitterDistance - 540 * popup.DeviceDpi / 96d) < 8, "Initial splitter did not scale with DPI");
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
                    await Until(() => Clipboard.ContainsImage(), "Image copy failed: " + extension);
                    using var copied = Clipboard.GetImage();
                    Check(copied != null && copied.Width > 0, "Image clipboard is empty");
                }
                await using (var shot = File.Create(Path.Combine(root, "image-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, shot);
                using (var tall = new Bitmap(320, 2000)) tall.Save(Path.Combine(root, "长图.png"), System.Drawing.Imaging.ImageFormat.Png);
                await pane.PreviewAsync(Path.Combine(root, "长图.png"));
                await Until(() => Field<bool>(pane, "_ready"), "Tall image not ready");
                Check(await Js(web, "document.querySelector('#content img').getBoundingClientRect().height <= innerHeight - 24") == "true", "Tall image did not fit viewport");
                Console.WriteLine("PASS Markdown render/table/code/sanitization/copy/UTF16; PNG/JPG/JPEG/GIF/BMP/WebP/ICO decode, zoom and clipboard images.");
                await pane.PreviewAsync(Path.Combine(root, "文字 PDF.pdf"));
                await Until(() => web.Source?.AbsolutePath.EndsWith(".pdf") == true, "PDF did not navigate");
                await Task.Delay(1000);
                Check(!Field<Label>(pane, "_message").Visible, "PDF failed to load");
                await using (var image = File.Create(Path.Combine(root, "pdf-preview.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                Console.WriteLine("PASS PDF navigation; inspect pdf-preview.png and perform native Ctrl+C in interactive mode.");
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
                Console.WriteLine("ASSETS: " + string.Join(",", Field<Dictionary<string, byte[]>>(pane, "_assets").Keys));
            }
            finally { if (!interactive || failure != 0) { popup.Dispose(); Application.ExitThread(); } }
        };
        popup.ShowSearch("");
        Application.Run();
        return failure;
    }
}
