import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const lock = readFileSync(new URL('../package-lock.json', import.meta.url), 'utf8');
const resolved = [...lock.matchAll(/"resolved":\s*"([^"]+)"/g)].map((m) => m[1]!);

describe('package-lock.json', () => {
  it('has resolved entries', () => expect(resolved.length).toBeGreaterThan(0));
  it('resolves every package from the public npm registry', () => {
    const bad = resolved.filter((u) => !u.startsWith('https://registry.npmjs.org/'));
    expect(bad).toEqual([]);
  });
  it('mentions no private registry host', () => expect(/apple/i.test(lock)).toBe(false));
});
