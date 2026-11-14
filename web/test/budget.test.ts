import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, describe, expect, it } from 'vitest';

const script = fileURLToPath(new URL('../scripts/check-budget.mjs', import.meta.url));
const dirs: string[] = [];
afterEach(() => {
  for (const d of dirs.splice(0)) rmSync(d, { recursive: true, force: true });
});

/** A fake dist folder: file name to size in bytes (or exact text for index.html). */
function dist(html: string, files: Record<string, number>): string {
  const dir = mkdtempSync(join(tmpdir(), 'budget-'));
  dirs.push(dir);
  mkdirSync(join(dir, 'assets'));
  mkdirSync(join(dir, 'icons'));
  writeFileSync(join(dir, 'index.html'), html);
  for (const [name, size] of Object.entries(files)) writeFileSync(join(dir, name), 'x'.repeat(size));
  return dir;
}
const run = (dir: string) => spawnSync(process.execPath, [script, dir], { encoding: 'utf8' });
const KB = 1024;
const page = (extra = '') => `<script type="module" src="/assets/index.js"></script><link rel="stylesheet" href="/assets/style.css">${extra}`;

describe('check-budget', () => {
  it('passes a small first load with a small lazy chunk', () => {
    const r = run(dist(page(), { 'assets/index.js': 40 * KB, 'assets/style.css': 5 * KB, 'assets/lazy.js': 15 * KB, 'icons/big.png': 500 * KB }));
    expect(r.status).toBe(0);
    expect(r.stdout).toContain('assets/lazy.js');
    expect(r.stdout).toMatch(/lazy\.js.*loaded later/);
  });
  it('fails when the first load is over 60 KB', () => expect(run(dist(page(), { 'assets/index.js': 58 * KB, 'assets/style.css': 5 * KB })).status).toBe(1));
  it('fails when a lazy chunk is over 20 KB', () => expect(run(dist(page(), { 'assets/index.js': 10 * KB, 'assets/style.css': 1 * KB, 'assets/lazy.js': 21 * KB })).status).toBe(1));
  it('fails when everything together is over 96 KB, even if each part fits', () => {
    const r = run(dist(page(), { 'assets/index.js': 58 * KB, 'assets/style.css': 1 * KB, 'assets/a.js': 19 * KB, 'assets/b.js': 19 * KB }));
    expect(r.status).toBe(1);
    expect(r.stderr).toMatch(/total/i);
  });
  it('passes a total of 90 KB (the old 80 KB ceiling no longer applies)', () => {
    const r = run(dist(page(), { 'assets/index.js': 58 * KB, 'assets/style.css': 1 * KB, 'assets/a.js': 16 * KB, 'assets/b.js': 15 * KB }));
    expect(r.status).toBe(0);
  });
  it('counts a modulepreload as first load, not loaded later', () => {
    const files = { 'assets/index.js': 40 * KB, 'assets/style.css': 5 * KB, 'assets/pre.js': 18 * KB };
    expect(run(dist(page(), files)).status).toBe(0);
    const r = run(dist(page('<link rel="modulepreload" crossorigin href="/assets/pre.js">'), files));
    expect(r.stdout).not.toMatch(/pre\.js.*loaded later/);
    expect(r.status).toBe(1);
  });
  it('fails without an entry script', () => expect(run(dist('<p>no script</p>', { 'assets/index.js': 1 })).status).toBe(1));
});
