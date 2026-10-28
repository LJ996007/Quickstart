const params = new URLSearchParams(location.search);
const token = params.get('token');
const kind = params.get('kind');
const isMarkdown = ['md', 'markdown'].includes(kind);
const isImage = ['png', 'jpg', 'jpeg', 'gif', 'bmp', 'webp', 'ico'].includes(kind);
const isText = kind === 'docx' || isMarkdown;
const content = document.querySelector('#content');
const message = document.querySelector('#message');
let preview;
let ready = false;

function notify(type, text = '') {
  window.chrome?.webview?.postMessage({ type, token, text });
}
function fail(error) {
  ready = false;
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
      ? (all ? content.innerText : window.getSelection()?.toString() || '')
      : sheetText(all);
    if (text.length > 8 * 1024 * 1024) throw new Error('复制内容过大，请缩小选择范围。');
    if (text) notify('copy', text);
    else notify('hint', all ? '没有可复制的文字。' : '请先选择文字或单元格。');
  } catch (error) { notify('hint', error.message); }
}
window.quickstartCopy = copy;
document.addEventListener('copy', event => {
  if (!ready || isText) return;
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
    document.body.className = isImage ? 'image' : 'markdown';
    const url = `/document/${token}.${kind}`;
    if (isImage) {
      const img = document.createElement('img');
      img.alt = '图片预览'; img.src = url;
      await img.decode();
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
    notify('ready', `${Math.round(performance.now() - start)} ms`);
    return;
  }
  document.body.className = kind === 'docx' ? 'docx' : 'excel';
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
    : { xls: kind === 'xls', minRowLength: 30, minColLength: 10, showContextmenu: false });
  await preview.preview(buffer);
  ready = true;
  notify('ready', `${Math.round(performance.now() - start)} ms`);
}
load().catch(fail);
