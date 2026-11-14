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
    const chrome = css.match(/\.chrome\s*\{([^}]*)\}/)![1]!;
    expect(chrome).toMatch(/position:\s*sticky/);
    expect(chrome).toMatch(/top:\s*0\b/);
    expect(chrome).toMatch(/z-index:\s*\d+/);
    expect(css).not.toMatch(/position:\s*fixed/);
    expect(css).toMatch(/\.banner\[hidden\]\s*\{[^}]*display:\s*none/);
  });
  it('pads anchored and focused elements below the sticky header, keeping the notch inset', () => {
    expect(css).toMatch(/html\s*\{[^}]*scroll-padding-top:[^;}]*var\(--chrome-h/);
    expect(css).toMatch(/\.top\s*\{[^}]*env\(safe-area-inset-top\)/);
  });
  it('gives the theme buttons a 3rem tap target', () => {
    expect(css).toMatch(/(^|\n)button\s*\{[^}]*min-height:\s*3rem/);
    const rule = css.match(/\.theme-btn\s*\{([^}]*)\}/)![1]!;
    expect(rule).not.toMatch(/min-height:\s*[^3]/);
    expect(rule).not.toMatch(/(^|[;\s])height:/);
  });
});

describe('script list rows', () => {
  it('clamps the description to two lines and keeps the row at least 3rem tall', () => {
    const desc = css.match(/\.script-desc\s*\{([^}]*)\}/)![1]!;
    expect(desc).toMatch(/-webkit-line-clamp:\s*2/);
    expect(desc).toMatch(/line-clamp:\s*2/);
    expect(desc).toMatch(/overflow:\s*hidden/);
    expect(desc).toMatch(/overflow-wrap:\s*anywhere/);
    expect(css).toMatch(/(^|\n)button\s*\{[^}]*min-height:\s*3rem/);
    expect(css.match(/\.script-btn\s*\{([^}]*)\}/)![1]!).not.toMatch(/(^|[;\s])height:/);
  });
  it('wraps long text of the file hint', () => expect(css).toMatch(/\.script-file\s*\{[^}]*overflow-wrap:\s*anywhere/));
});

describe('skua tab styles', () => {
  it('wraps every server-provided line of the Skua tab', () => {
    for (const sel of ['\\.skua-line', '\\.skua-log p', '\\.now-name', '\\.name']) {
      expect(css).toMatch(new RegExp(`${sel}\\s*\\{[^}]*overflow-wrap:\\s*anywhere`));
    }
  });
  it('styles the card class the Now card shares', () => expect(css).toMatch(/\.card\s*\{[^}]*background:\s*var\(--surface\)[^}]*border-radius/));
  it('gives the filter buttons a 3rem tap target and lets them wrap without scrolling', () => {
    const rule = css.match(/\.filter-btn\s*\{([^}]*)\}/)![1]!;
    expect(rule).not.toMatch(/min-height:\s*[^3]/);
    expect(rule).not.toMatch(/(^|[;\s])height:/);
    const row = css.match(/\.filters\s*\{([^}]*)\}/)![1]!;
    expect(row).toMatch(/flex-wrap:\s*wrap/);
    expect(row).not.toMatch(/overflow/);
  });
  it('shows the pressed filter with a thicker border, not colour alone', () => expect(css).toMatch(/\.filter-btn\[aria-pressed="true"\]\s*\{[^}]*border-width:\s*3px/));
  it('gives the ready marker a shape of its own (a diamond, not the round status dot)', () => {
    expect(css).toMatch(/\.dot\[data-kind="ready"\]\s*\{[^}]*rotate\(45deg\)/);
  });
});

describe('index.html', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  it('wraps the title strip and the banner in one sticky container', () => {
    expect(html).toMatch(/<div class="chrome">\s*<header class="top">[\s\S]*<\/header>\s*<p id="banner"[^>]*\bhidden\b[^>]*><\/p>\s*<\/div>\s*<main/);
  });
});
