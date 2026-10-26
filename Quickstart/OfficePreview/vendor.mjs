// Refresh pinned, offline browser modules: npm ci --ignore-scripts && npm run vendor
import { mkdir, copyFile, readFile, writeFile, readdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
const root = new URL('./', import.meta.url);
const hashes = {};
await mkdir(new URL('wwwroot/vendor/', root), { recursive: true });
for (const kind of ['docx', 'excel']) {
  for (const ext of ['js', 'css']) {
    const source = new URL(kind === 'docx' ? 'docx-adapter.js' : `node_modules/@js-preview/${kind}/lib/index.${ext}`, root);
    const target = `wwwroot/vendor/${kind}.${ext}`;
    const destination = new URL(target, root);
    if (ext === 'js') {
      // Resolve all dependencies into offline browser modules.
      await build({ entryPoints: [fileURLToPath(source)], outfile: fileURLToPath(destination), bundle: true,
        format: 'esm', platform: 'browser', target: 'chrome110', minify: true, legalComments: 'inline' });
    } else if (kind === 'docx') {
      await writeFile(destination, '.docx-wrapper { min-height: 100%; box-sizing: border-box; }\n');
    } else await copyFile(source, destination);
    hashes[target] = createHash('sha256').update(await readFile(destination)).digest('hex');
  }
}
await writeFile(new URL('vendor-sha256.json', root), JSON.stringify(hashes, null, 2) + '\n');
// Preserve installed production dependency license files alongside the browser assets.
const lock = JSON.parse(await readFile(new URL('package-lock.json', root), 'utf8'));
let licenses = 'Licenses from the pinned npm production dependencies.\n\n';
for (const [path, info] of Object.entries(lock.packages)) {
  if (!path || info.dev || info.optional) continue;
  const dir = new URL(path + '/', root);
  for (const file of await readdir(dir)) {
    if (/^(license|licence|notice)(\.|$)/i.test(file)) {
      licenses += `\n===== ${path} ${info.version}: ${file} =====\n\n` + await readFile(new URL(file, dir), 'utf8') + '\n';
    }
  }
}
await writeFile(new URL('wwwroot/vendor/DEPENDENCY-LICENSES.txt', root), licenses);
