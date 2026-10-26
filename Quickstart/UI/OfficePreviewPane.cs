namespace Quickstart.UI;

using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Quickstart.Core;

/// <summary>One reusable, offline WebView. Each navigation discards the previous renderer and its listeners.</summary>
public sealed class OfficePreviewPane : UserControl
{
    private const string Origin = "https://quickstart-preview.invalid";
    private readonly Label _title = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = "文件预览" };
    private readonly Label _message = new() { Dock = DockStyle.Fill, Padding = new Padding(24), Text = "单击 DOCX、XLSX、XLS 或 PDF 文件即可预览。", BackColor = Color.FromArgb(241, 243, 245) };
    private readonly Label _hint = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = "预览内容可选择复制 · 双击结果用默认程序打开" };
    private readonly Button _copy = new() { Text = "复制选中", AutoSize = true, Enabled = false };
    private readonly Button _copyAll = new() { Text = "复制全文", AutoSize = true, Enabled = false };
    private readonly Button _open = new() { Text = "默认程序打开", AutoSize = true, Enabled = false };
    private readonly Panel _body = new() { Dock = DockStyle.Fill };
    private WebView2? _web;
    private Task? _initialize;
    private Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);
    private CancellationTokenSource? _readCts;
    private OfficePreviewDocument? _document;
    private string? _path;
    private string? _allowedNavigation;
    private int _revision;
    private int _copyRevision;
    private bool _ready;

    public event EventHandler? OpenRequested;
    public event EventHandler? EscapeRequested;
    public bool HasPreviewFocus => _web?.ContainsFocus == true;

    public OfficePreviewPane()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange([_copy, _copyAll, _open]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8, 0, 0, 0) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(_title, 0, 0); layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(_body, 0, 2); layout.Controls.Add(_hint, 0, 3);
        _body.Controls.Add(_message); Controls.Add(layout);
        _open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _copy.Click += async (_, _) => await CopyAsync(false);
        _copyAll.Click += async (_, _) => await CopyAsync(true);
    }

    public async Task WarmUpAsync()
    {
        try { await EnsureInitializedAsync(); }
        catch { /* Missing runtime is explained when an actual preview is requested. */ }
    }

    private Task EnsureInitializedAsync() => _initialize ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        // Checks for an installed Evergreen runtime without installing or opening anything.
        _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        _assets = await Task.Run(() =>
        {
            var assembly = typeof(OfficePreviewPane).Assembly;
            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("OfficePreview/", StringComparison.Ordinal)))
            {
                using var source = assembly.GetManifestResourceStream(name)!;
                using var buffer = new MemoryStream(); source.CopyTo(buffer);
                assets["/" + name["OfficePreview/".Length..].Replace('\\', '/')] = buffer.ToArray();
            }
            return assets;
        });
        if (IsDisposed) return;
        _web = new WebView2 { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Color.White };
        _body.Controls.Add(_web);
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Root, "office-preview", "webview"));
        if (IsDisposed) return;
        await _web.EnsureCoreWebView2Async(environment);
        if (IsDisposed) return;
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Save | CoreWebView2PdfToolbarItems.SaveAs | CoreWebView2PdfToolbarItems.Print;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += ResourceRequested;
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != _allowedNavigation;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.WebMessageReceived += MessageReceived;
        core.ProcessFailed += (_, _) =>
        {
            if (IsDisposed) return;
            _revision++; _readCts?.Cancel(); _document = null; _path = null;
            _ready = false; _copy.Enabled = _copyAll.Enabled = false;
            ShowMessage("预览进程已退出，请重新单击文件重试。");
            // Recreate only after a browser failure, never for ordinary file switches.
            _web?.Dispose(); _web = null; _initialize = null;
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (_document?.Kind != "pdf" || core.Source != _allowedNavigation) return;
            if (!e.IsSuccess) { ShowMessage("PDF 预览失败，请用默认程序打开。"); return; }
            _hint.Text = "选择 PDF 文字后按 Ctrl+C；扫描件不提供文字识别。";
        };
        _web.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                BeginInvoke(() => EscapeRequested?.Invoke(this, EventArgs.Empty));
            }
        };
        Navigate(Origin + "/index.html");
    }

    public void Clear(string? message = null)
    {
        _revision++; _copyRevision++; _readCts?.Cancel();
        _document = null; _path = null; _ready = false;
        _copy.Enabled = _copyAll.Enabled = _open.Enabled = false;
        _title.Text = "文件预览";
        _hint.Text = "预览内容可选择复制 · 双击结果用默认程序打开";
        ShowMessage(message ?? "单击 DOCX、XLSX、XLS 或 PDF 文件即可预览。");
        if (_web?.CoreWebView2 != null) Navigate(Origin + "/index.html");
    }

    public async Task PreviewAsync(string path)
    {
        if (path == _path && _document != null) return;
        Clear();
        _path = path;
        _open.Enabled = true;
        _title.Text = Path.GetFileName(path);
        if (!OfficePreviewDocument.IsSupported(path)) { ShowMessage("此类型暂不支持预览，请双击结果或用默认程序打开。"); return; }
        var revision = _revision;
        _readCts?.Dispose();
        var cts = _readCts = new CancellationTokenSource();
        ShowMessage("正在加载预览…");
        try
        {
            // Reading and ZIP metadata validation never run on the search UI thread.
            var read = Task.Run(() => OfficePreviewDocument.ReadAsync(path, cts.Token), cts.Token);
            // Always observe both tasks, including read errors if runtime initialization fails.
            await Task.WhenAll(EnsureInitializedAsync(), read);
            var document = await read;
            if (IsDisposed || revision != _revision || cts.IsCancellationRequested) return;
            _document = document;
            var isPdf = document.Kind == "pdf";
            _copy.Visible = _copyAll.Visible = !isPdf;
            _copyAll.Text = document.Kind == "docx" ? "复制全文" : "复制工作表";
            _hint.Text = isPdf ? "选择 PDF 文字后按 Ctrl+C；扫描件不提供文字识别。" : "正在解析文档…";
            _message.Visible = false;
            _web!.Visible = true; _web.BringToFront();
            Navigate(isPdf ? Origin + document.ResourcePath : $"{Origin}/index.html?token={document.Token}&kind={document.Kind}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsDisposed || revision != _revision) return;
            _path = null;
            var detail = ex.GetBaseException();
            ShowMessage(detail is WebView2RuntimeNotFoundException
                ? "预览需要 Microsoft Edge WebView2 Runtime。安装后重新启动 Quickstart；当前仍可用默认程序打开。"
                : $"无法预览：{detail.Message}\n可用默认程序打开文件。");
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text; _message.Visible = true; _message.BringToFront();
        if (_web != null) _web.Visible = false;
    }

    private void Navigate(string uri)
    {
        _allowedNavigation = uri;
        _web?.CoreWebView2?.Navigate(uri);
    }

    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = _web?.CoreWebView2;
        if (core == null) return;
        var uri = new Uri(e.Request.Uri);
        byte[]? bytes = null;
        string mime = "application/octet-stream";
        var isDocument = false;
        if (uri.GetLeftPart(UriPartial.Authority) == Origin && e.Request.Method == "GET")
        {
            if (_document != null && uri.AbsolutePath == _document.ResourcePath)
            {
                bytes = _document.Bytes;
                isDocument = true;
                if (_document.Kind == "pdf") mime = "application/pdf";
            }
            else if (_assets.TryGetValue(uri.AbsolutePath, out bytes))
            {
                mime = Path.GetExtension(uri.AbsolutePath) switch
                {
                    ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8",
                    ".html" => "text/html; charset=utf-8", _ => "text/plain; charset=utf-8"
                };
            }
        }
        if (bytes == null)
        {
            // No external requests, sibling documents, or arbitrary file-system access.
            e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "Content-Type: text/plain");
            return;
        }
        var headers = $"Content-Type: {mime}\r\nCache-Control: {(isDocument ? "no-store" : "no-cache")}\r\nX-Content-Type-Options: nosniff";
        // PDFium may request byte ranges when jumping between pages.
        if (isDocument && mime == "application/pdf" && e.Request.Headers.Contains("Range"))
        {
            var range = e.Request.Headers.GetHeader("Range");
            var parts = range.StartsWith("bytes=", StringComparison.Ordinal) ? range[6..].Split('-') : [];
            if (parts.Length == 2 && int.TryParse(parts[0], out var start) && start >= 0 && start < bytes.Length
                && (parts[1].Length == 0 || int.TryParse(parts[1], out _)))
            {
                var end = parts[1].Length == 0 ? bytes.Length - 1 : Math.Min(int.Parse(parts[1]), bytes.Length - 1);
                if (end >= start)
                {
                    e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(bytes, start, end - start + 1, false), 206, "Partial Content",
                        headers + $"\r\nAccept-Ranges: bytes\r\nContent-Range: bytes {start}-{end}/{bytes.Length}\r\nContent-Length: {end - start + 1}");
                    return;
                }
            }
            e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{bytes.Length}");
            return;
        }
        e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK", headers + $"\r\nContent-Length: {bytes.Length}");
    }

    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_document == null || e.Source != _allowedNavigation) return;
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            if (root.GetProperty("token").GetString() != _document.Token) return;
            var text = root.GetProperty("text").GetString() ?? "";
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ready = true; _copy.Enabled = _copyAll.Enabled = true;
                    _hint.Text = _document.Kind == "docx" ? "拖选文字后 Ctrl+C · 复杂排版以原文件为准" : "选择单元格或拖选区域后 Ctrl+C · 可粘贴回 Excel";
                    break;
                case "copy" when _ready && text.Length is > 0 and <= 8 * 1024 * 1024:
                    await CopyTextAsync(text, _document.Token); break;
                case "hint": _hint.Text = text; break;
                case "error": _ready = false; _copy.Enabled = _copyAll.Enabled = false; _hint.Text = text; break;
                case "escape": EscapeRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or System.Runtime.InteropServices.ExternalException)
        { _hint.Text = "复制未完成，请重试。"; }
    }

    private async Task CopyTextAsync(string text, string token)
    {
        var revision = ++_copyRevision;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (IsDisposed || _document?.Token != token || revision != _copyRevision) return;
            try
            {
                // Clipboard managers can briefly hold the clipboard. Retry without blocking search/UI.
                Clipboard.SetDataObject(text, true, 0, 0);
                _hint.Text = "已复制内容";
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                if (attempt == 5) _hint.Text = "剪贴板正忙，请再试一次。";
                else await Task.Delay(40 * (attempt + 1));
            }
        }
    }

    private async Task CopyAsync(bool all)
    {
        if (!_ready || _web?.CoreWebView2 == null) return;
        try { await _web.ExecuteScriptAsync($"window.quickstartCopy?.({(all ? "true" : "false")})"); }
        catch (Exception) { if (!IsDisposed) _hint.Text = "复制未完成，请重新选择内容后重试。"; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _revision++; _readCts?.Cancel(); _readCts?.Dispose();
            _document = null; _web?.Dispose(); _assets.Clear();
        }
        base.Dispose(disposing);
    }
}
