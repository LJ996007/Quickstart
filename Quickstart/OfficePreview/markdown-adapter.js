import { marked } from 'marked';
import DOMPurify from 'dompurify';

export function render(text, target, token) {
  // Template contents stay inert until every image URL is rewritten and HTML sanitized.
  const template = document.createElement('template');
  template.innerHTML = marked.parse(text, { gfm: true });
  let count = 0;
  for (const img of template.content.querySelectorAll('img')) {
    const source = img.getAttribute('src') || '';
    img.removeAttribute('srcset');
    if (++count <= 128 && source) {
      img.setAttribute('src', `/image/${token}?path=${encodeURIComponent(source)}`);
    } else img.removeAttribute('src');
  }
  const fragment = DOMPurify.sanitize(template.innerHTML, {
    USE_PROFILES: { html: true }, FORBID_TAGS: ['style', 'input', 'form'],
    FORBID_ATTR: ['style', 'srcset'], RETURN_DOM_FRAGMENT: true
  });
  for (const img of fragment.querySelectorAll('img')) {
    img.addEventListener('error', () => {
      const label = document.createElement('span');
      label.className = 'image-missing';
      label.textContent = img.alt ? `[图片无法加载：${img.alt}]` : '[图片无法加载]';
      img.replaceWith(label);
    }, { once: true });
  }
  target.replaceChildren(fragment);
}
