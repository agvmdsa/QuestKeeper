import { readdirSync, readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, relative } from 'node:path';
import { gzipSync } from 'node:zlib';

const dist = fileURLToPath(new URL('../dist/', import.meta.url));
/** What the first load of the page may weigh: the page, its entry script, the styles, the manifest and the service worker. */
const BUDGET = 60 * 1024;
/** What one chunk loaded later (the Skua control, with the Skua tab) may weigh, on top of the first load. */
const LAZY_BUDGET = 20 * 1024;

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
}

// The entry script is the one index.html loads; any other script is a chunk the page imports when it needs it.
const html = readFileSync(join(dist, 'index.html'), 'utf8');
const entries = new Set([...html.matchAll(/<script[^>]*\bsrc="\/([^"]+)"/g)].map((m) => m[1]));

let raw = 0;
let packed = 0;
let lazyFailed = false;
for (const file of walk(dist)) {
  const name = relative(dist, file).replaceAll('\\', '/');
  if (name.startsWith('icons/')) continue; // images are cached by the service worker and are not on the critical path
  const bytes = readFileSync(file);
  const gz = gzipSync(bytes).length;
  const lazy = name.startsWith('assets/') && name.endsWith('.js') && !entries.has(name);
  console.log(`${name.padEnd(36)} ${String(bytes.length).padStart(7)} B ${String(gz).padStart(7)} B gzip${lazy ? ' (loaded later)' : ''}`);
  if (lazy) {
    if (bytes.length > LAZY_BUDGET) {
      console.error(`${name} is over the ${LAZY_BUDGET} B budget of a chunk loaded later.`);
      lazyFailed = true;
    }
    continue;
  }
  raw += bytes.length;
  packed += gz;
}
console.log(`first load ${raw} B, ${packed} B gzip (budget ${BUDGET} B raw, icons and chunks loaded later excluded)`);
if (entries.size === 0) {
  console.error('No entry script found in index.html.');
  process.exit(1);
}
if (raw > BUDGET || lazyFailed) {
  console.error('The web budget is exceeded.');
  process.exit(1);
}
