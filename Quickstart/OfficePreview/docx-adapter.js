// The same DOCX engine used by vue-office, bundled directly to avoid the
// @js-preview/docx package's unbundled Node/core-js imports in native browsers.
import { renderAsync } from 'docx-preview';
export default {
  init(container, options) {
    return {
      preview: buffer => renderAsync(buffer, container, container, options),
      destroy: () => container.replaceChildren()
    };
  }
};
