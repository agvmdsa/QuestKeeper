import { readdirSync, readFileSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, relative } from 'node:path';
import { gzipSync } from 'node:zlib';

const dist = fileURLToPath(new URL('../dist/', import.meta.url));
const BUDGET = 60 * 1024;

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
}

let raw = 0;
let packed = 0;
for (const file of walk(dist)) {
  const name = relative(dist, file).replaceAll('\\', '/');
  if (name.startsWith('icons/')) continue; // images are cached by the service worker and are not on the critical path
  const bytes = readFileSync(file);
  const gz = gzipSync(bytes).length;
  raw += bytes.length;
  packed += gz;
  console.log(`${name.padEnd(36)} ${String(bytes.length).padStart(7)} B ${String(gz).padStart(7)} B gzip`);
}
console.log(`total ${raw} B, ${packed} B gzip (budget ${BUDGET} B raw, icons excluded)`);
if (raw > BUDGET) {
  console.error('The web budget is exceeded.');
  process.exit(1);
}
