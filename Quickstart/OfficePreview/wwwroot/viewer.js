const params = new URLSearchParams(location.search);
const token = params.get('token');
const kind = params.get('kind');
const isMarkdown = ['md', 'markdown'].includes(kind);
const isImage = ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'webp', 'ico'].includes(kind);
const isText = kind === 'docx' || isMarkdown;
const standalone = params.get('standalone') === '1';
const content = document.querySelector('#content');
const message = document.querySelector('#message');
const copyButton = document.querySelector('#selection-copy');
let preview;
let ready = false;
let sheetSelection = false;
let dragging = false;
let selectionFrame = 0;
let fitFrame = 0;
const sheetWidths = new WeakMap();

function hideSelection() { copyButton.hidden = true; }
function textSelection() {
  const selection = window.getSelection();
  if (!selection?.rangeCount || selection.isCollapsed ||
      !content.contains(selection.anchorNode) || !content.contains(selection.focusNode)) return null;
  return selection;
}
function selectionBounds() {
  if (isText) {
    const selection = textSelection();
    return selection?.toString().trim() ? selection.getRangeAt(0).getBoundingClientRect() : null;
  }
  if (!sheetSelection) return null;
  const data = preview?.xs?.sheet?.data;
  const grid = content.querySelector('canvas');
  const cell = data?.getSelectedRect();
  if (!grid || !cell?.width || !cell.height) return null;
  const frame = grid.getBoundingClientRect();
  const scale = Number(preview.wrapper.style.zoom) || 1;
  return new DOMRect(frame.left + (data.cols.indexWidth + cell.left) * scale,
    frame.top + (data.rows.height + cell.top) * scale, cell.width * scale, cell.height * scale);
}
function showSelection() {
  selectionFrame = 0;
  if (!standalone || !ready || isImage || dragging) return hideSelection();
  const bounds = selectionBounds();
  const viewport = content.getBoundingClientRect();
  if (!bounds || bounds.bottom <= viewport.top || bounds.top >= viewport.bottom ||
      bounds.right <= viewport.left || bounds.left >= viewport.right) return hideSelection();
  copyButton.hidden = false;
  const limit = viewport.right - copyButton.offsetWidth - 8;
  const x = Math.max(viewport.left + 8, Math.min(bounds.right + 8, limit));
  const y = bounds.top - copyButton.offsetHeight - 6;
  copyButton.style.left = `${x}px`;
  copyButton.style.top = `${y > viewport.top + 6 ? y : Math.min(bounds.bottom + 6, viewport.bottom - copyButton.offsetHeight - 8)}px`;
}
function scheduleSelection() {
  if (standalone && !selectionFrame) selectionFrame = requestAnimationFrame(showSelection);
}
copyButton.addEventListener('pointerdown', event => event.preventDefault()); // Preserve the document selection.
copyButton.addEventListener('click', () => { copy(); hideSelection(); });
if (standalone) {
  document.addEventListener('selectionchange', () => { if (isText) scheduleSelection(); });
  content.addEventListener('pointerdown', () => { dragging = true; hideSelection(); });
  window.addEventListener('pointerup', () => { if (dragging) { dragging = false; scheduleSelection(); } });
  window.addEventListener('pointercancel', () => { dragging = false; hideSelection(); });
  content.addEventListener('scroll', hideSelection, true);
  content.addEventListener('click', event => {
    if (event.target.closest('.x-spreadsheet-bottombar')) { sheetSelection = false; hideSelection(); scheduleFit(); }
  });
  window.addEventListener('resize', scheduleFit);
}

function scheduleFit() {
  if (standalone && !fitFrame) fitFrame = requestAnimationFrame(fitContent);
}
function fitContent() {
  fitFrame = 0;
  if (!ready) return;
  hideSelection();
  if (kind === 'docx') {
    const pages = [...content.querySelectorAll('.docx-wrapper > section.docx')];
    const widest = pages.reduce((width, page) => Math.max(width, page.offsetWidth), 1);
    const scale = Math.min(1.15, Math.max(1, content.clientWidth - 26) / widest);
    for (const page of pages) page.style.zoom = scale;
  } else if (!isText && !isImage) {
    const sheet = preview?.xs?.sheet;
    if (!sheet || !content.clientWidth || !content.clientHeight) return;
    const data = sheet.data;
    if (!sheetWidths.has(data)) {
      const [, lastColumn] = data.rows.maxCell();
      sheetWidths.set(data, data.cols.indexWidth + data.cols.sumWidth(0, lastColumn + 1) + 18);
    }
    // Fit typical sheets, but keep very wide sheets legible and allow their own grid scrollbar.
    const scale = Math.max(0.8, Math.min(1, content.clientWidth / Math.max(1, sheetWidths.get(data))));
    const wrapper = preview.wrapper;
    wrapper.style.zoom = scale;
    wrapper.style.width = `${Math.floor(content.clientWidth / scale)}px`;
    wrapper.style.height = `${Math.floor(content.clientHeight / scale)}px`;
    sheet.reload();
  }
}

function notify(type, text = '', details) {
  window.chrome?.webview?.postMessage({ type, token, text, ...details });
}
function fail(error) {
  ready = false;
  hideSelection();
  content.replaceChildren();
  message.hidden = false;
  message.textContent = '无法预览此文件，可能已损坏、受密码保护或包含不支持的内容。请用默认程序打开。';
  notify('error', message.textContent);
  console.error(error);
}
function sheetText(all) {
  // The pinned vue-office JS renderer exposes the selected sheet's data.
  // Bypass only its copy handler (which rejects read mode), never enable editing.
  const data = preview?.xs?.sheet?.data;
  if (!data) return '';
  let { sri, eri, sci, eci } = data.selector.range;
  if (all) {
    const rows = data.rows.getData();
    const indices = Object.keys(rows).filter(key => /^\d+$/.test(key)).map(Number);
    if (!indices.length) return '';
    sri = Infinity; eri = 0; sci = 0; eci = 0;
    for (const ri of indices) {
      sri = Math.min(sri, ri); eri = Math.max(eri, ri);
      for (const ci of Object.keys(rows[ri]?.cells || {})) eci = Math.max(eci, Number(ci));
    }
  }
  if ((eri - sri + 1) * (eci - sci + 1) > 1000000) throw new Error('选区超过 100 万个单元格，请缩小范围后复制。');
  const lines = [];
  for (let row = sri; row <= eri; row++) {
    const cells = [];
    for (let col = sci; col <= eci; col++) {
      const text = String(data.getCell(row, col)?.text ?? '');
      // Excel's clipboard text format quotes embedded line breaks/tabs/quotes.
      cells.push(/[\t\r\n"]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text);
    }
    lines.push(cells.join('\t'));
  }
  return lines.join('\r\n');
}
function copy(all = false) {
  if (!ready) return;
  try {
    if (isImage) {
      const img = content.querySelector('img');
      const canvas = document.createElement('canvas');
      canvas.width = img.naturalWidth; canvas.height = img.naturalHeight;
      if (canvas.width * canvas.height > 32000000) throw new Error('图片过大，请用默认程序复制。');
      canvas.getContext('2d').drawImage(img, 0, 0);
      const data = canvas.toDataURL('image/png').split(',')[1];
      if (data.length > 24 * 1024 * 1024) throw new Error('图片过大，请用默认程序复制。');
      notify('copyImage', data);
      return;
    }
    const text = isText
      ? (all ? content.innerText : textSelection()?.toString() || '')
      : sheetText(all);
    if (text.length > 8 * 1024 * 1024) throw new Error('复制内容过大，请缩小选择范围。');
    if (text) notify('copy', text);
    else notify('hint', all ? '没有可复制的文字。' : '请先选择文字或单元格。');
  } catch (error) { notify('hint', error.message); }
}
window.quickstartCopy = copy;
document.addEventListener('copy', event => {
  if (!ready || (isText && !standalone)) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  copy();
}, true);
// Also supports Ctrl+C when the upstream read-only grid has blurred its input.
document.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c' && !isText) {
    event.preventDefault(); event.stopImmediatePropagation(); copy();
  }
  if (event.key === 'Escape') notify('escape');
}, true);
document.addEventListener('click', event => {
  if (event.target.closest('a')) event.preventDefault();
}, true);

async function load() {
  if (!token || !(['docx', 'xlsx', 'xls'].includes(kind) || isMarkdown || isImage)) return;
  const start = performance.now();
  if (isMarkdown || isImage) {
    document.body.classList.add(isImage ? 'image' : 'markdown');
    document.body.classList.toggle('standalone', standalone);
    const url = `/document/${token}.${kind}`;
    let dimensions;
    if (isImage) {
      const img = document.createElement('img');
      img.alt = '图片预览'; img.src = url;
      await img.decode();
      dimensions = { width: img.naturalWidth, height: img.naturalHeight };
      img.addEventListener('click', () => img.classList.toggle('actual-size'));
      content.replaceChildren(img);
    } else {
      const [module, response] = await Promise.all([import('./vendor/markdown.js'), fetch(url, { cache: 'no-store' })]);
      if (!response.ok) throw new Error('无法读取文件。');
      const buffer = await response.arrayBuffer();
      const bytes = new Uint8Array(buffer);
      const encoding = bytes[0] === 255 && bytes[1] === 254 ? 'utf-16le' : bytes[0] === 254 && bytes[1] === 255 ? 'utf-16be' : 'utf-8';
      module.render(new TextDecoder(encoding).decode(buffer), content, token);
    }
    message.hidden = true; ready = true;
    notify('ready', `${Math.round(performance.now() - start)} ms`, dimensions);
    return;
  }
  document.body.classList.add(kind === 'docx' ? 'docx' : 'excel');
  document.body.classList.toggle('standalone', standalone);
  message.textContent = '正在解析文档…';
  const moduleKind = kind === 'docx' ? 'docx' : 'excel';
  const link = document.createElement('link');
  link.rel = 'stylesheet'; link.href = `vendor/${moduleKind}.css`;
  const styleReady = new Promise((resolve, reject) => { link.onload = resolve; link.onerror = reject; });
  document.head.append(link);
  const [module, response] = await Promise.all([
    import(`./vendor/${moduleKind}.js`),
    fetch(`/document/${token}.${kind}`, { cache: 'no-store' }),
    styleReady
  ]);
  if (!response.ok) throw new Error('文件已切换或无法读取。');
  const buffer = await response.arrayBuffer();
  message.hidden = true;
  preview = module.default.init(content, kind === 'docx'
    ? { renderAltChunks: false, ignoreLastRenderedPageBreak: false, useBase64URL: false }
    : { xls: kind === 'xls', minRowLength: 30, minColLength: 10, showContextmenu: false,
        cellSelected: selectedCell, cellsSelected: selectedCell });
  await preview.preview(buffer);
  ready = true;
  scheduleFit();
  notify('ready', `${Math.round(performance.now() - start)} ms`);
}
function selectedCell() {
  if (!ready || !standalone) return;
  sheetSelection = true;
  scheduleSelection();
}
load().catch(fail);
