# 本地 Office 预览

Quickstart 搜索窗口右侧的离线预览：DOCX、XLSX/XLS、PDF。前端资源作为程序集资源嵌入，包括单 EXE 发布；不需要 Node.js、Office、HTTP 服务或联网 CDN 才能预览。

- DOCX：使用 vue-office 底层的 `docx-preview`，浏览器文字选择、复制选中和复制全文。
- Excel：使用 vue-office 官方原生 JS 包 `@js-preview/excel`，保留只读模式。`viewer.js` 从当前工作表的选区读取显示值，复制为制表符/换行分隔的文本，不启用编辑。公式使用文件中缓存的结果，不重新计算；复杂图表、旧 XLS 格式等显示可能与 Office 不完全一致。
- PDF：WebView2 内置 PDF 查看器，文字型 PDF 可框选后 Ctrl+C；扫描件没有 OCR。
- Markdown：MD/Markdown 使用固定版本的 marked 和 DOMPurify 离线渲染，支持表格、代码块和文字复制；支持 UTF-8、带 BOM 的 UTF-16。本地图片按 Markdown 所在目录解析，支持相对路径、上级目录、本机绝对路径和 file URI；不加载网络图片。单张引用图片最多 16 MiB，每份文档合计 64 MiB、最多 128 个引用；图片缺失或不支持时显示占位，不影响正文。
- 图片：PNG/JPG/JPEG/GIF/BMP/WebP/ICO 使用浏览器原生解码；点击切换适应窗口/原始大小，复制按钮将当前帧转成位图写入剪贴板（最多 3200 万像素）。

## 部署与构建

需要安装 Microsoft Edge WebView2 Evergreen Runtime。Quickstart 只检测，不自动下载或安装；缺失时显示提示，原程序打开入口仍可用。运行时资料：https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution

普通 `dotnet build` / 发布直接使用已入库的 `wwwroot`，无需 npm。更新 JS 依赖后，在本目录执行：

```powershell
npm.cmd ci --ignore-scripts
npm.cmd run vendor
```

这会重建 `wwwroot/vendor` 中的浏览器模块、许可文件及 `vendor-sha256.json`。`@js-preview/docx` 当前包含未打包的 Node/core-js 引用，因此直接打包相同 DOCX 引擎；没有引入 Vue 应用或 Electron。

## 生命周期

只在搜索窗口首次显示且允许预览时异步创建一个 WebView2；点击切换延迟 180 ms 合并连续选择，浏览器实例复用。每次文档导航创建新的页面上下文，避免旧解析任务、全局事件监听器或旧选区串到新文件。文件读取和 ZIP 元信息检查在后台执行；旧任务通过取消与版本号丢弃。隐藏窗口/取消预览后释放文档字节并跳回空白预览页，保留浏览器便于再次使用。WebView2 用户数据位于 `AppPaths.Root/office-preview/webview`。

前端仅通过随机文件令牌读取当前文件的内存快照；不暴露本地目录，也不启用 HTTP 监听。拒绝外网请求、跳转、弹窗、下载和权限请求，DOCX HTML 插入关闭。Markdown 图片使用当前文档令牌的专用通道读取，仅接受图片请求和支持的位图扩展名，切换文档取消旧图片加载。文件不写回磁盘；用户主动复制才写入剪贴板。

第一版限制：文件最大 64 MiB；DOCX/XLSX 解压大小最大 256 MiB、2 万 ZIP 条目；单次复制最多 100 万单元格/8 MiB 字符。受密码保护、损坏或超过限制的文档提示用默认程序打开。浏览器崩溃后再次选择文件会重新初始化。

## 验证

从仓库根运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-office-preview.ps1
# 保留验收窗口，检查单击预览、区域复制和原生 PDF Ctrl+C：
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-office-preview.ps1 -Interactive
# 验证单 EXE 的资源嵌入与 WebView2Loader 自解压：
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-office-preview.ps1 -Portable
```

测试夹具、配置和截图均在 `artifacts/debug/checks/office-preview`，不读取用户文档。测试包含中文 DOCX、XLSX、BIFF8 XLS、万行表格、两页混合纸张 PDF、损坏/丢失/超限文件、快速切换和本地资源隔离。复制测试会使用系统剪贴板。
