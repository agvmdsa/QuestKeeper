import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const css = readFileSync(new URL('../src/styles/tokens.css', import.meta.url), 'utf8');

function block(text: string): Record<string, string> {
  const out: Record<string, string> = {};
  for (const m of text.matchAll(/--([a-z-]+):\s*(#[0-9A-Fa-f]{6})\s*;/g)) {
    out[m[1]!] = m[2]!;
  }
  return out;
}

const split = css.indexOf('@media');
const light = block(css.slice(0, split));
const dark = { ...light, ...block(css.slice(split)) };

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5]
    .map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)) as [number, number, number];
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function ratio(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (hi + 0.05) / (lo + 0.05);
}

const surfaces = ['bg', 'surface', 'raised'] as const;

describe.each([['light', light], ['dark', dark]] as const)('%s scheme', (_scheme, t) => {
  it.each(surfaces)('text on %s is at least 7:1', (s) => expect(ratio(t.text!, t[s]!)).toBeGreaterThanOrEqual(7));
  it.each(surfaces)('muted text on %s is at least 4.5:1', (s) => expect(ratio(t.muted!, t[s]!)).toBeGreaterThanOrEqual(4.5));
  it.each(surfaces)('links on %s are at least 4.5:1', (s) => expect(ratio(t.link!, t[s]!)).toBeGreaterThanOrEqual(4.5));
  it.each(surfaces)('danger text on %s is at least 4.5:1', (s) => expect(ratio(t.danger!, t[s]!)).toBeGreaterThanOrEqual(4.5));
  it.each(surfaces)('the gold edge on %s is at least 3:1', (s) => expect(ratio(t.border!, t[s]!)).toBeGreaterThanOrEqual(3));
  it.each(surfaces)('the bar fill on %s is at least 3:1', (s) => expect(ratio(t.bar!, t[s]!)).toBeGreaterThanOrEqual(3));
  it('dark text on the accent fill is at least 4.5:1', () => expect(ratio(t['on-accent']!, t.accent!)).toBeGreaterThanOrEqual(4.5));
  it('light text on the ok fill is at least 4.5:1', () => expect(ratio(t['on-ok']!, t.ok!)).toBeGreaterThanOrEqual(4.5));
  it('text on the top bar is at least 7:1', () => expect(ratio(t['on-top']!, t.top!)).toBeGreaterThanOrEqual(7));
});
