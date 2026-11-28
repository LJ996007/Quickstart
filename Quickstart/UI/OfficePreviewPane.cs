namespace Quickstart.UI;

using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Quickstart.Core;
using Quickstart.Utils;

/// <summary>One reusable, offline WebView. Each navigation discards the previous renderer and its listeners.</summary>
public sealed class OfficePreviewPane : UserControl
{
    private const string Origin = "https://quickstart-preview.invalid";
    // Give immutable scripts/styles a new URL when the executable changes. Document
    // URLs remain short-lived and are always checked against this pane's current token.
    private static readonly string AssetRoot = "/app/" + typeof(OfficePreviewPane).Module.ModuleVersionId.ToString("N") + "/";
    private static readonly Dictionary<string, string> AssetNames = typeof(OfficePreviewPane).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("OfficePreview/", StringComparison.Ordinal))
        .ToDictionary(name => AssetRoot + name["OfficePreview/".Length..].Replace('\\', '/'), StringComparer.Ordinal);
    private readonly Label _title = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty, Visible = false };
    private readonly Label _message = new() { Dock = DockStyle.Fill, Padding = new Padding(24), Text = "选择文件预览",
        TextAlign = ContentAlignment.MiddleCenter, BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.TextSecondary };
    private readonly Label _hint = new() { Dock = DockStyle.Fill, AutoSize = true, MinimumSize = new Size(0, 28),
        Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.TextSecondary, Visible = false };
    private readonly Button _copy = new() { Text = "复制选中", AutoSize = true, Enabled = false, Visible = false };
    private readonly Button _copyAll = new() { Text = "复制全文", AutoSize = true, Enabled = false, Visible = false };
    private readonly Button _open = new() { Text = "打开", AccessibleName = "用默认程序打开", AutoSize = true, Enabled = false };
    private readonly FileSearchIconButton _pin = new(FileSearchGlyph.Pin, "钉在最前面") { Visible = false };
    private readonly FileSearchIconButton _close = new(FileSearchGlyph.Close, "关闭预览") { Visible = false };
    private readonly TableLayoutPanel _toolbar = new() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1,
        Margin = Padding.Empty, Visible = false };
    private readonly TableLayoutPanel _layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
    private bool _sizingHeader;
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = UiTheme.SurfaceAlt };
    private readonly ToolTip _toolTips = new();
    private WebView2? _web;
    private Task? _initialize;
    private Task? _previewTask;
    private CancellationTokenSource? _readCts;
    private OfficePreviewDocument? _document;
    private string? _path;
    private string? _allowedNavigation;
    private ulong? _navigationId;
    private string? _reportedPreviewToken;
    private int _revision;
    private int _copyRevision;
    private bool _ready;
    private bool _standalone;
    private bool _pinned;
    private readonly SemaphoreSlim _imageGate = new(1, 1);
    private long _imageBytes;

    public event EventHandler? OpenRequested;
    public event EventHandler? EscapeRequested;
    internal static event Action<string>? FilePreviewed;
    /// <summary>只有独立预览窗启用钉住按钮；本地搜索的内嵌预览保持原样。</summary>
    public event EventHandler? PinToggled;
    /// <summary>独立预览窗右上角的关闭按钮（本地搜索的内嵌预览不显示）。</summary>
    public event EventHandler? CloseRequested;
    internal event MouseEventHandler? TitleDragRequested;
    internal event Action<Size>? ImageDecoded;
    public bool HasPreviewFocus => _web?.ContainsFocus == true;
    /// <summary>是否一直显示在最前面（不随失焦自动关闭）。</summary>
    public bool Pinned => _pinned;

    public OfficePreviewPane()
    {
        BackColor = UiTheme.Card;
        ForeColor = UiTheme.TextPrimary;
        Margin = Padding.Empty;
        _title.Font = UiTheme.CreateFont(9.5f, FontStyle.Bold);
        _title.MouseDown += (sender, e) => TitleDragRequested?.Invoke(sender, e);
        foreach (var button in new[] { _copy, _copyAll, _open })
        {
            UiTheme.StyleFlatButton(button, UiTheme.Card);
            button.Dock = DockStyle.Fill;
            button.AutoSize = false;
        }
        _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) _toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _toolbar.Controls.Add(_title, 0, 0);
        _toolbar.Controls.Add(_copy, 1, 0);
        _toolbar.Controls.Add(_copyAll, 2, 0);
        _toolbar.Controls.Add(_open, 3, 0);
        _toolbar.Controls.Add(_pin, 4, 0);
        _toolbar.Controls.Add(_close, 5, 0);
        _pin.BackColor = _close.BackColor = UiTheme.Card;
        _pin.Dock = _close.Dock = DockStyle.Fill;
        _pin.Click += (_, _) => TogglePin();
        _close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _toolTips.SetToolTip(_pin, _pin.AccessibleName);
        _toolTips.SetToolTip(_close, _close.AccessibleName);
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(_toolbar, 0, 0);
        _layout.Controls.Add(_body, 0, 1); _layout.Controls.Add(_hint, 0, 2);
        _body.Controls.Add(_message); Controls.Add(_layout);
        _toolTips.SetToolTip(_open, "用默认程序打开文件");
        _hint.TextChanged += (_, _) => _hint.Visible = _hint.Text.Length > 0;
        _open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _copy.Click += async (_, _) => await CopyAsync(false);
        _copyAll.Click += async (_, _) => await CopyAsync(true);
    }

    private void ApplyHeaderMetrics()
    {
        if (_sizingHeader || _layout.RowStyles.Count == 0) return;
        _sizingHeader = true;
        try
        {
            int S(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            var buttonHeight = S(_standalone ? 27 : 36);
            _layout.RowStyles[0].Height = _path == null ? 0 : S(_standalone ? 30 : 40);
            var inset = _standalone ? S(6) : 0;
            _toolbar.Margin = new Padding(inset, 0, inset, S(_standalone ? 3 : 4));
            foreach (var button in new[] { _copy, _copyAll, _open })
            {
                button.MinimumSize = Size.Empty;
                button.Size = new Size(TextRenderer.MeasureText(button.Text, button.Font).Width + S(_standalone ? 14 : 20), buttonHeight);
                button.Margin = new Padding(S(_standalone ? 3 : 8), 0, 0, 0);
            }
            _pin.MinimumSize = _close.MinimumSize = Size.Empty;
            _pin.Size = _close.Size = new Size(S(29), buttonHeight);
            _pin.Margin = new Padding(S(4), 0, 0, 0);
            _close.Margin = new Padding(S(2), 0, 0, 0);
        }
        finally { _sizingHeader = false; }
    }

    internal void UseStandaloneToolbar()
    {
        _standalone = true;
        _pin.Visible = _close.Visible = true;
        _title.Cursor = Cursors.SizeAll;
        _title.ForeColor = UiTheme.TextPrimary;
        foreach (var button in new[] { _copy, _copyAll, _open })
        {
            button.ForeColor = UiTheme.TextSecondary;
            button.FlatAppearance.MouseOverBackColor = UiTheme.SelectionFill;
        }
        ApplyHeaderMetrics();
    }

    private void TogglePin()
    {
        if (!_standalone) return;
        _pinned = !_pinned;
        ApplyPinState();
        PinToggled?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPinState()
    {
        _pin.BackColor = _pinned ? UiTheme.SelectionFill : UiTheme.Card;
        _pin.AccessibleName = _pinned ? "取消钉住" : "钉在最前面";
        _toolTips.SetToolTip(_pin, _pin.AccessibleName);
        _pin.Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyHeaderMetrics();
    }

    public async Task WarmUpAsync()
    {
        try { await EnsureInitializedAsync(); }
        catch { /* Missing runtime is explained when an actual preview is requested. */ }
    }

    // WebView2 starts the shared browser process with its first controller. Keep
    // that cost off the first click for users who already use local previews.
    private static string BrowserProfile => Path.Combine(AppPaths.Root, "office-preview", "webview");

    internal async Task WarmUpIfPreviouslyUsedAsync()
    {
        if (await Task.Run(() => Directory.Exists(BrowserProfile)) && !IsDisposed && !Disposing)
            await WarmUpAsync();
    }

    private Task EnsureInitializedAsync()
    {
        if (_initialize == null || _initialize.IsFaulted || _initialize.IsCanceled)
            _initialize = InitializeAsync();
        return _initialize;
    }

    private async Task InitializeAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, BrowserProfile);
        if (IsDisposed) return;
        _web?.Dispose(); // A previous controller initialization may have failed.
        _web = new WebView2 { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Color.White };
        _body.Controls.Add(_web);
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
        core.NavigationStarting += (_, e) =>
        {
            e.Cancel = e.Uri != _allowedNavigation;
            if (!e.Cancel) _navigationId = e.NavigationId;
        };
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
            if (_document?.Kind != "pdf" || e.NavigationId != _navigationId || core.Source != _allowedNavigation) return;
            if (!e.IsSuccess) { ShowMessage("PDF 预览失败，请用默认程序打开。"); return; }
            _hint.Text = "选择 PDF 文字后按 Ctrl+C；扫描件不提供文字识别。";
            ReportDisplayedFile();
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
    }

    private void CancelDocument()
    {
        _revision++; _copyRevision++; _readCts?.Cancel();
        _readCts?.Dispose(); _readCts = null;
        _document = null; _path = null; _ready = false;
        _previewTask = null;
        _imageBytes = 0;
        _copy.Enabled = _copyAll.Enabled = false;
    }

    public void Clear(string? message = null)
    {
        var text = message ?? "选择文件预览";
        if (_path == null && !_open.Enabled && _message.Text == text) return;
        CancelDocument();
        _open.Enabled = false;
        _title.Text = "";
        _title.Visible = _toolbar.Visible = _copy.Visible = _copyAll.Visible = false;
        ApplyHeaderMetrics();
        _body.Margin = Padding.Empty;
        _toolTips.SetToolTip(_title, "");
        _hint.Text = "";
        ShowMessage(text);
        UnloadViewer();
    }

    private void UnloadViewer()
    {
        if (_allowedNavigation != null && _allowedNavigation != "about:blank" && _web?.CoreWebView2 != null)
            Navigate("about:blank");
    }

    public Task PreviewAsync(string path)
    {
        SelectFile(path);
        return _previewTask ??= LoadPreviewAsync(path);
    }

    // Keyboard repeat can select several rows before reading any of them. Invalidate
    // the old token and copy actions as soon as the selection actually changes.
    internal bool SelectFile(string path)
    {
        if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return _previewTask == null;
        CancelDocument();
        _path = path;
        var supported = OfficePreviewDocument.IsSupported(path);
        var kind = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var isImage = OfficePreviewDocument.IsImageKind(kind);
        _layout.SuspendLayout();
        _toolbar.SuspendLayout();
        try
        {
            _open.Enabled = true;
            _title.Text = Path.GetFileName(path);
            _title.Visible = _toolbar.Visible = true;
            _copy.Visible = supported && kind != "pdf";
            _copyAll.Visible = supported && kind != "pdf" && !isImage;
            _copy.Text = isImage ? "复制图片" : "复制选中";
            _copyAll.Text = kind is "docx" or "md" or "markdown" ? "复制全文" : "复制工作表";
            ApplyHeaderMetrics();
        }
        finally
        {
            _toolbar.ResumeLayout(true);
            _layout.ResumeLayout(true);
        }
        _toolTips.SetToolTip(_title, path);
        _hint.Text = "";
        if (!supported)
        {
            ShowMessage("此类型暂不支持预览，请双击结果或用默认程序打开。");
            UnloadViewer();
            _previewTask = Task.CompletedTask;
        }
        else ShowMessage("正在加载预览…");
        return supported;
    }

    private async Task LoadPreviewAsync(string path)
    {
        var revision = _revision;
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        try
        {
            // Reading and ZIP metadata validation never run on the search UI thread.
            var read = Task.Run(() => OfficePreviewDocument.ReadAsync(path, token), token);
            // Always observe both tasks, including read errors if runtime initialization fails.
            await Task.WhenAll(EnsureInitializedAsync(), read);
            var document = await read;
            if (IsDisposed || revision != _revision || token.IsCancellationRequested) return;
            _document = document;
            var isPdf = document.Kind == "pdf";
            _hint.Text = isPdf ? "选择 PDF 文字后按 Ctrl+C；扫描件不提供文字识别。" : "正在解析文档…";
            _message.Visible = false;
            _web!.Visible = true; _web.BringToFront();
            Navigate(isPdf ? Origin + document.ResourcePath + (_standalone ? "#view=FitH" : "")
                : $"{Origin}{AssetRoot}index.html?token={document.Token}&kind={document.Kind}{(_standalone ? "&standalone=1" : "")}");
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
            UnloadViewer();
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text; _message.Visible = true; _message.BringToFront();
        if (_web != null) _web.Visible = false;
    }

    private void Navigate(string uri)
    {
        _navigationId = null;
        _allowedNavigation = uri;
        _web?.CoreWebView2?.Navigate(uri);
    }

    private void ReportDisplayedFile()
    {
        if (_path == null || _document == null || _web?.Visible != true || _reportedPreviewToken == _document.Token) return;
        _reportedPreviewToken = _document.Token;
        FilePreviewed?.Invoke(_path);
    }

    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = _web?.CoreWebView2;
        if (core == null) return;
        var uri = new Uri(e.Request.Uri);
        if (uri.GetLeftPart(UriPartial.Authority) == Origin && e.Request.Method == "GET"
            && e.ResourceContext == CoreWebView2WebResourceContext.Image
            && _document is { Kind: "md" or "markdown" } markdown
            && uri.AbsolutePath == $"/image/{markdown.Token}" && uri.Query.StartsWith("?path=", StringComparison.Ordinal)
            && _path != null && _readCts != null)
        {
            var deferral = e.GetDeferral();
            _ = ServeMarkdownImageAsync(e, deferral, core, markdown, _path,
                Uri.UnescapeDataString(uri.Query[6..]), _readCts.Token);
            return;
        }
        byte[]? bytes = null;
        string mime = "application/octet-stream";
        if (uri.GetLeftPart(UriPartial.Authority) == Origin && e.Request.Method == "GET")
        {
            if (_document != null && uri.AbsolutePath == _document.ResourcePath)
            {
                bytes = _document.Bytes;
                mime = _document.ContentType;
            }
            else if (AssetNames.TryGetValue(uri.AbsolutePath, out var name))
            {
                mime = Path.GetExtension(uri.AbsolutePath) switch
                {
                    ".js" => "text/javascript; charset=utf-8", ".css" => "text/css; charset=utf-8",
                    ".html" => "text/html; charset=utf-8", _ => "text/plain; charset=utf-8"
                };
                // Manifest streams refer directly to the bundled resource. Each request
                // gets its own seek position and never copies the Excel/Word bundles.
                var asset = typeof(OfficePreviewPane).Assembly.GetManifestResourceStream(name)!;
                e.Response = core.Environment.CreateWebResourceResponse(asset, 200, "OK",
                    $"Content-Type: {mime}\r\nCache-Control: max-age=31536000, immutable\r\nX-Content-Type-Options: nosniff\r\nContent-Length: {asset.Length}");
                return;
            }
        }
        if (bytes == null)
        {
            // No external requests, sibling documents, or arbitrary file-system access.
            e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "Content-Type: text/plain");
            return;
        }
        var headers = $"Content-Type: {mime}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
        // PDFium may request byte ranges when jumping between pages.
        if (mime == "application/pdf" && e.Request.Headers.Contains("Range"))
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

    private async Task ServeMarkdownImageAsync(CoreWebView2WebResourceRequestedEventArgs request,
        CoreWebView2Deferral deferral, CoreWebView2 core, OfficePreviewDocument markdown,
        string path, string reference, CancellationToken token)
    {
        try
        {
            await _imageGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (_document?.Token != markdown.Token || _imageBytes >= 64L * 1024 * 1024) return;
                var image = await Task.Run(() => MarkdownImageReader.ReadAsync(path, reference, token), token);
                if (IsDisposed || token.IsCancellationRequested || _document?.Token != markdown.Token) return;
                if (_imageBytes + image.Bytes.Length > 64L * 1024 * 1024) return;
                _imageBytes += image.Bytes.Length;
                request.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(image.Bytes, false), 200, "OK",
                    $"Content-Type: {image.ContentType}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
            }
            finally { _imageGate.Release(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException or System.Runtime.InteropServices.COMException)
        { /* Missing or rejected images retain the document and show an inline placeholder. */ }
        finally
        {
            try
            {
                if (request.Response == null)
                    request.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 404, "Not Found", "Content-Type: text/plain");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            try { deferral.Complete(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
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
                    _hint.Text = _document.IsImage ? "点击图片切换原始大小" : "";
                    if (_document.IsImage && root.TryGetProperty("width", out var width) && width.TryGetInt32(out var w)
                        && root.TryGetProperty("height", out var height) && height.TryGetInt32(out var h) && w > 0 && h > 0)
                        ImageDecoded?.Invoke(new Size(w, h));
                    ReportDisplayedFile();
                    break;
                case "copy" when _ready && text.Length is > 0 and <= 8 * 1024 * 1024:
                    await CopyDataAsync(text, _document.Token); break;
                case "copyImage" when _ready && _document.IsImage && text.Length is > 0 and <= 24 * 1024 * 1024:
                    using (var stream = new MemoryStream(Convert.FromBase64String(text)))
                    using (var image = Image.FromStream(stream))
                    {
                        if ((long)image.Width * image.Height > 32000000) { _hint.Text = "图片过大，请用默认程序复制。"; break; }
                        using var bitmap = new Bitmap(image);
                        await CopyDataAsync(new DataObject(DataFormats.Bitmap, bitmap), _document.Token);
                    }
                    break;
                case "hint": _hint.Text = text; break;
                case "error": _ready = false; _copy.Enabled = _copyAll.Enabled = false; _hint.Text = text; break;
                case "escape": EscapeRequested?.Invoke(this, EventArgs.Empty); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        { _hint.Text = "复制未完成，请重试。"; }
    }

    private async Task CopyDataAsync(object data, string token)
    {
        var revision = ++_copyRevision;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (IsDisposed || _document?.Token != token || revision != _copyRevision) return;
            try
            {
                // Clipboard managers can briefly hold the clipboard. Retry without blocking search/UI.
                Clipboard.SetDataObject(data is string text ? new DataObject(DataFormats.UnicodeText, text) : data, true, 0, 0);
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
            _document = null; _web?.Dispose();
            _toolTips.Dispose();
        }
        base.Dispose(disposing);
    }
}
