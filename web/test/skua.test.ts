import { describe, expect, it } from 'vitest';
import { formatDuration, parseSkua, skuaEmptyText, skuaHeader, skuaLogRows, skuaSummary } from '../src/skua';
import { isUsableState, keepState, parseRoute, parseState, recallKept } from '../src/state';
import { listView, skuaView, type Model, type VNode } from '../src/views';
import type { RemoteState, SkuaState } from '../src/types';

const NOW = Date.parse('2026-11-11T12:12:00Z');
const wire = {
  connection: 'Connected',
  hello: { pluginVersion: '0.1.0', skuaVersion: '1.4.5.0' },
  status: { loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: true, runningSince: '2026-11-11T12:00:00+00:00' },
  log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: 'Sold Blood Orb x50 for 2,500 gold' }],
};
const skua = (over: Partial<SkuaState> = {}): SkuaState => ({ ...parseSkua(wire)!, ...over });
const model = (s: SkuaState | null | undefined): Model => ({
  state: { snapshot: null, notices: [], skua: s }, nowMs: NOW, showAll: false, hfOpen: false, installHint: null,
});
const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];
const texts = (n: VNode) => all(n).map((x) => x.text).filter((t): t is string => t !== undefined);

describe('parseSkua', () => {
  it('accepts the full example', () => expect(parseSkua(wire)).toEqual(wire));
  it('accepts NotConnected with no status and an empty log', () => {
    expect(parseSkua({ connection: 'NotConnected', hello: null, status: null, log: [] })?.connection).toBe('NotConnected');
  });
  it('rejects an unknown connection and a log that is not an array', () => {
    expect(parseSkua({ ...wire, connection: 'Maybe' })).toBeNull();
    expect(parseSkua({ ...wire, log: 'x' })).toBeNull();
    expect(parseSkua('x')).toBeNull();
    expect(parseSkua(null)).toBeNull();
  });
  it('drops entries without text or at, caps text at 300 and the list at 100', () => {
    const many = Array.from({ length: 150 }, (_, i) => ({ at: '2026-11-11T12:00:00Z', kind: 'Sold', text: `e${i}` }));
    const s = parseSkua({ ...wire, log: [{ at: 'x', kind: 'Sold' }, { text: 'no at' }, { at: 'a', kind: 'k', text: 'z'.repeat(1000) }, ...many] })!;
    expect(s.log).toHaveLength(100);
    expect(s.log[0]!.text).toHaveLength(300);
    expect(parseSkua({ ...wire, log: [{ at: 'x', kind: 'Sold' }] })!.log).toEqual([]);
  });
});

describe('formatDuration', () => {
  it.each([[0, 'just now'], [59_000, 'just now'], [180_000, '3 min'], [3_900_000, '1 h 5 min'], [7_200_000, '2 h'], [90_000_000, '1 d 1 h'], [Number.NaN, 'just now'], [-5000, 'just now'], [Infinity, 'just now']])('%s -> %s', (ms, text) =>
    expect(formatDuration(ms)).toBe(text));
});

describe('skua view model', () => {
  it('has the header lines for a running script', () => {
    expect(skuaHeader(skua(), NOW)).toEqual({ title: 'Skua', lines: ['Hero · Artix · battleon', 'Running Farm.cs for 12 min'] });
  });
  it('says no script running, and is null when not connected', () => {
    const st = { ...wire.status, scriptRunning: false };
    expect(skuaHeader(skua({ status: st }), NOW)!.lines[1]).toBe('No script running');
    expect(skuaHeader(skua({ connection: 'NotConnected' }), NOW)).toBeNull();
  });
  it('keeps log order and words the time like a notice', () => {
    const s = skua({ log: [{ at: '2026-11-11T12:10:00Z', kind: 'a', text: 'new' }, { at: '2026-11-11T12:00:00Z', kind: 'a', text: 'old' }] });
    expect(skuaLogRows(s, NOW)).toEqual([{ when: '2 min ago', text: 'new' }, { when: '12 min ago', text: 'old' }]);
  });
  it('has the calm texts', () => {
    expect(skuaEmptyText(null)).toBe('Skua is not connected.');
    expect(skuaEmptyText(skua({ connection: 'NotConnected' }))).toBe('Skua is not connected.');
    expect(skuaEmptyText(skua({ log: [] }))).toBe('Nothing to show yet.');
    expect(skuaEmptyText(skua({ connection: 'VersionMismatch' }))).toBe('Update the Skua plugin and Questkeeper to the same release.');
    expect(skuaEmptyText(skua())).toBe('');
    expect(skuaSummary(skua())).toBe('Skua connected.');
  });
});

describe('state, route and storage', () => {
  const base = { snapshot: null, notices: [] };
  it('treats a payload without skua as null and degrades a string skua to null', () => {
    expect(parseState(JSON.stringify(base))!.skua).toBeNull();
    expect(isUsableState({ ...base, skua: 'x' } as unknown as RemoteState)).toBe(true);
    expect(parseState(JSON.stringify({ ...base, skua: 'x' }))).toEqual({ snapshot: null, notices: [], skua: null });
    expect(parseState(JSON.stringify({ ...base, skua: [1] }))!.skua).toBeNull();
    expect(parseState(JSON.stringify({ ...base, skua: wire }))!.skua).toEqual(wire);
  });
  it('keeps skua through the stored round trip and loads an old stored value', () => {
    const state = parseState(JSON.stringify({ ...base, skua: wire }))!;
    expect(recallKept(keepState(state, NOW - 1000), NOW)!.state.skua).toEqual(wire);
    expect(recallKept(JSON.stringify({ receivedAtMs: NOW - 1000, state: base }), NOW)!.state.skua).toBeNull();
  });
  it('loads a stored value with a garbage skua as null', () => {
    const text = JSON.stringify({ receivedAtMs: NOW - 1000, state: { ...base, skua: { connection: 7 } } });
    expect(recallKept(text, NOW)!.state.skua).toBeNull();
  });
  it('routes #/skua only', () => {
    expect(parseRoute('#/skua')).toEqual({ view: 'skua' });
    expect(parseRoute('#/skua/x')).toEqual({ view: 'list' });
  });
});

describe('views', () => {
  it('shows the Skua card on the list only when skua is not null, linking to #/skua', () => {
    expect(all(listView(model(null))).some((n) => n.attrs?.href === '#/skua')).toBe(false);
    expect(all(listView(model(undefined))).some((n) => n.attrs?.href === '#/skua')).toBe(false);
    const view = listView(model(skua()));
    expect(all(view).some((n) => n.attrs?.href === '#/skua')).toBe(true);
    expect(texts(view)).toContain('Skua connected.');
  });
  it('shows header, back link and log rows', () => {
    const view = skuaView(model(skua()));
    const t = texts(view);
    expect(t).toEqual(expect.arrayContaining(['Back to characters', 'Skua', 'Hero · Artix · battleon', 'Running Farm.cs for 12 min', 'Sold Blood Orb x50 for 2,500 gold', '11 min ago']));
    expect(all(view).find((n) => n.className === 'back')!.attrs).toEqual({ href: '#/' });
  });
  it('shows only the title when connected without a status, and no empty time line', () => {
    expect(skuaHeader(skua({ status: null }), NOW)).toEqual({ title: 'Skua', lines: [] });
    const s = skua({ log: [{ at: 'not a date', kind: 'x', text: 'odd' }] });
    const view = skuaView(model(s));
    expect(texts(view)).toContain('odd');
    expect(all(view).some((n) => n.className === 'muted')).toBe(false);
  });
  it('shows the calm text for the other states', () => {
    expect(texts(skuaView(model(null)))).toContain('Skua is not connected.');
    expect(texts(skuaView(model(skua({ log: [] }))))).toContain('Nothing to show yet.');
    expect(texts(skuaView(model(skua({ connection: 'VersionMismatch' }))))).toContain('Update the Skua plugin and Questkeeper to the same release.');
  });
  it('uses no style or on* attributes', () => {
    const s = skua({ log: [{ at: '2026-11-11T12:00:00Z', kind: 'x', text: '<b>hi</b>' }] });
    for (const view of [skuaView(model(s)), listView(model(s))]) {
      for (const n of all(view)) for (const name of Object.keys(n.attrs ?? {})) expect(name === 'style' || name.startsWith('on')).toBe(false);
    }
    expect(texts(skuaView(model(s)))).toContain('<b>hi</b>');
  });
});
