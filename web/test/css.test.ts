import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../src/styles/app.css', import.meta.url), 'utf8');

describe('app.css', () => {
  it('never uses the accent orange as a text colour (it is 1.9:1 on cream)', () => {
    expect(css).not.toMatch(/(^|[;{\s])color:\s*var\(--accent\)/);
  });
  it('removes motion for people who ask', () => expect(css).toMatch(/prefers-reduced-motion:\s*reduce/));
  it('has a visible keyboard focus', () => expect(css).toMatch(/:focus-visible/));
  it('keeps tap targets at least 3rem tall', () => expect(css).toMatch(/\.row\s*\{[^}]*min-height:\s*3rem/));
  it('keeps the title strip and banner fixed while scrolling, without position: fixed', () => {
    expect(css).toMatch(/\.chrome\s*\{[^}]*position:\s*sticky;[^}]*top:\s*0;[^}]*z-index:\s*\d+/);
    expect(css).not.toMatch(/position:\s*fixed/);
    expect(css).toMatch(/\.banner\[hidden\]\s*\{[^}]*display:\s*none/);
  });
  it('pads anchored and focused elements below the sticky header, keeping the notch inset', () => {
    expect(css).toMatch(/html\s*\{[^}]*scroll-padding-top:[^;}]*env\(safe-area-inset-top\)/);
    expect(css).toMatch(/\.top\s*\{[^}]*env\(safe-area-inset-top\)/);
  });
  it('gives the theme buttons a 3rem tap target', () => expect(css).toMatch(/button\s*\{[^}]*min-height:\s*3rem/));
});

describe('index.html', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  it('wraps the title strip and the banner in one sticky container', () => {
    expect(html).toMatch(/<div class="chrome">\s*<header class="top">[\s\S]*<\/header>\s*<p id="banner"[^>]*\bhidden\b[^>]*><\/p>\s*<\/div>\s*<main/);
  });
});
