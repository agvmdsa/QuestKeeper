import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const root = fileURLToPath(new URL('..', import.meta.url));

function files(dir: string, ext: RegExp): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? files(path, ext) : ext.test(path) ? [path] : [];
  });
}

const shipped = [
  ...files(join(root, 'src'), /\.(ts|css|js)$/),
  join(root, 'index.html'),
  join(root, 'public', 'manifest.webmanifest'),
];

describe('what the CSP and the privacy promise need', () => {
  it.each(shipped.map((f) => [relative(root, f), f]))('%s has no inline style, handler, innerHTML or outside URL', (_name, file) => {
    const text = readFileSync(file as string, 'utf8');
    expect(text).not.toMatch(/\sstyle=/);
    expect(text).not.toMatch(/<style[\s>]/);
    expect(text).not.toMatch(/\son[a-z]+=/);
    expect(text).not.toMatch(/innerHTML|outerHTML|insertAdjacentHTML|document\.write/);
    // The SVG namespace of the dock's inline icons is an identifier, never fetched; any other address is.
    expect(text.replaceAll("'http://www.w3.org/2000/svg'", '')).not.toMatch(/https?:\/\//);
  });
});

describe('names', () => {
  const read = (path: string) => readFileSync(join(root, path), 'utf8');
  // The only qkr names left are the storage keys of the Remote, which the page removes if the Remote ran on the same address.
  it.each([...shipped.map((f) => relative(root, f)), 'vite.config.ts', 'dev/mock-api.ts'])('%s keeps no name of the Remote', (file) => {
    expect(read(file)).not.toMatch(/qkr(?!-last-state)|Questkeeper Remote|QK Remote/i);
  });
  it('the installed page is called SkuaLink', () => {
    const manifest = JSON.parse(read('public/manifest.webmanifest')) as { name: string; short_name: string };
    expect(manifest).toMatchObject({ name: 'SkuaLink', short_name: 'SkuaLink' });
    expect(read('index.html')).toContain('<title>SkuaLink</title>');
    expect(read('index.html')).toContain('<meta name="apple-mobile-web-app-title" content="SkuaLink">');
  });
  it('the service worker cache is named qkp-shell-', () => expect(read('vite.config.ts')).toContain('`qkp-shell-${version}`'));
});
