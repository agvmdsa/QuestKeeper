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
});
