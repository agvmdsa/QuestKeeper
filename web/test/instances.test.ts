import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  effectiveFactions, instanceStateText, loadInstance, neighbourLabel, parseSkuaInstances, saveInstance, selectInstance, SKUA_INSTANCE_KEY,
} from '../src/skua';
import { noticeLine, parseState } from '../src/state';
import { skuaView, listView, type Model, type VNode } from '../src/views';
import type { Faction, RemoteState, SkuaInstance } from '../src/types';

const NOW = Date.parse('2026-11-11T12:12:00Z');
const status = (over: object = {}) => ({
  loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: false, runningSince: null, quests: null, factions: null,
  control: 'idle', settleSecondsLeft: null, optionsStuck: false, ...over,
});
const wireInstance = (label: string, over: Record<string, unknown> = {}, st: object = {}) => ({
  instanceId: `id-${label}`, label, connection: 'Connected', disconnected: false, hello: { pluginVersion: '0.2.0', skuaVersion: '1.4.5.0', instanceId: `id-${label}` },
  status: status({ character: label, ...st }), log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: `Sold by ${label}` }], ...over,
});
const inst = (label: string, over: Record<string, unknown> = {}, st: object = {}): SkuaInstance => parseSkuaInstances([wireInstance(label, over, st)])![0]!;
const many = (n: number): SkuaInstance[] => Array.from({ length: n }, (_, i) => inst(`Hero${i + 1}`));
const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];
const texts = (n: VNode) => all(n).map((x) => x.text).filter((t): t is string => t !== undefined);
const model = (instances: SkuaInstance[] | undefined, saved: string | null = null): Model => ({
  state: { snapshot: null, notices: [], skua: instances?.[0] ?? null, skuaInstances: instances }, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, skuaInstance: saved,
});

describe('parseSkuaInstances', () => {
  it('reads the server shape', () => {
    const [i] = parseSkuaInstances([wireInstance('Hero', {}, { control: 'settling', settleSecondsLeft: 7 })])!;
    expect(i).toMatchObject({ instanceId: 'id-Hero', label: 'Hero', connection: 'Connected', disconnected: false });
    expect(i!.status).toMatchObject({ control: 'settling', settleSecondsLeft: 7 });
    expect(i!.status!.optionsStuck).toBeFalsy();
    expect(i!.log).toHaveLength(1);
  });
  it.each([[null], [undefined], ['x'], [{}]])('is undefined for %j (an older app, or no Skua feed)', (v) => expect(parseSkuaInstances(v)).toBeUndefined());
  it('keeps an empty list as empty', () => expect(parseSkuaInstances([])).toEqual([]));
  it('drops damaged entries on their own and keeps order', () => {
    const r = parseSkuaInstances([wireInstance('A'), { label: 'no id' }, 7, wireInstance('B', { connection: 'Maybe' }), wireInstance('C')])!;
    expect(r.map((i) => i.label)).toEqual(['A', 'C']);
  });
  it('caps at 8 instances and cuts long labels', () => {
    expect(parseSkuaInstances(Array.from({ length: 12 }, (_, i) => wireInstance(`H${i}`)))).toHaveLength(8);
    expect(parseSkuaInstances([wireInstance('x'.repeat(1000))])![0]!.label.length).toBeLessThanOrEqual(300);
  });
  it('ignores an unknown control value and a bad settle count', () => {
    const s = inst('A', {}, { control: 'dancing', settleSecondsLeft: -3, optionsStuck: 'yes' }).status!;
    expect(s.control).toBeUndefined();
    expect(s.settleSecondsLeft).toBeUndefined();
    expect(s.optionsStuck).toBeFalsy();
  });
  it('is read by parseState next to the old skua field', () => {
    const s = parseState(JSON.stringify({ snapshot: null, notices: [], skua: wireInstance('Hero'), skuaInstances: [wireInstance('Hero'), wireInstance('Mule')] }))!;
    expect(s.skuaInstances?.map((i) => i.label)).toEqual(['Hero', 'Mule']);
    expect(s.skua?.connection).toBe('Connected');
    expect(parseState('{"snapshot":null,"notices":[]}')!.skuaInstances).toBeUndefined();
  });
});

describe('selectInstance', () => {
  it('is null for no instances', () => expect(selectInstance([], 'Hero')).toBeNull());
  it('returns the remembered one by character label', () => expect(selectInstance(many(3), 'Hero2')!.label).toBe('Hero2'));
  it('falls back to the first connected when the label is gone or unknown', () => {
    const list = [inst('Off', { disconnected: true, connection: 'NotConnected' }), inst('On')];
    expect(selectInstance(list, 'Gone')!.label).toBe('On');
    expect(selectInstance(list, null)!.label).toBe('On');
  });
  it('falls back to the first when none is connected', () => {
    const list = [inst('A', { disconnected: true, connection: 'NotConnected' }), inst('B', { disconnected: true, connection: 'NotConnected' })];
    expect(selectInstance(list, null)!.label).toBe('A');
  });
  it('keeps a remembered disconnected instance (it stays on its chip)', () => {
    const list = [inst('On'), inst('Off', { disconnected: true, connection: 'NotConnected' })];
    expect(selectInstance(list, 'Off')!.label).toBe('Off');
  });
});

describe('neighbourLabel', () => {
  it('moves and wraps', () => {
    const l = many(3);
    expect(neighbourLabel(l, 'Hero1', 1)).toBe('Hero2');
    expect(neighbourLabel(l, 'Hero3', 1)).toBe('Hero1');
    expect(neighbourLabel(l, 'Hero1', -1)).toBe('Hero3');
  });
});

describe('the remembered selection', () => {
  const mem = (initial?: string) => {
    const data = new Map<string, string>(initial === undefined ? [] : [[SKUA_INSTANCE_KEY, initial]]);
    return { data, getItem: (k: string) => data.get(k) ?? null, setItem: (k: string, v: string) => void data.set(k, v) };
  };
  it('uses the key sl-skua-instance', () => expect(SKUA_INSTANCE_KEY).toBe('sl-skua-instance'));
  it('round trips the label', () => {
    const s = mem();
    saveInstance(s, 'Hero2');
    expect(s.data.get('sl-skua-instance')).toBe('Hero2');
    expect(loadInstance(s)).toBe('Hero2');
  });
  it('survives blocked storage and a missing store', () => {
    const blocked = { getItem: () => { throw new Error('x'); }, setItem: () => { throw new Error('x'); } };
    expect(loadInstance(blocked)).toBeNull();
    expect(() => saveInstance(blocked, 'a')).not.toThrow();
    expect(loadInstance(null)).toBeNull();
  });
  it('ignores an oversized stored value', () => expect(loadInstance(mem('x'.repeat(5000)))).toBeNull());
});

describe('instanceStateText', () => {
  it.each([
    ['Running', {}, { control: 'running', scriptRunning: true }],
    ['Idle', {}, {}],
    ['Stopping', {}, { control: 'stopping' }],
    ['Settling 7 s', {}, { control: 'settling', settleSecondsLeft: 7 }],
    ['Settling', {}, { control: 'settling', settleSecondsLeft: null }],
    ['Waiting for you', {}, { control: 'awaitingOptions' }],
    ['Waiting for you', {}, { optionsStuck: true }],
    ['Not logged in', {}, { loggedIn: false }],
    ['Disconnected', { disconnected: true, connection: 'NotConnected' }, {}],
    ['Disconnected', { disconnected: true }, { control: 'running' }],
  ])('says %s', (text, over, st) => expect(instanceStateText(inst('A', over, st))).toBe(text));
  it('falls back to scriptRunning for an older plugin without control', () => {
    const i = inst('A', {}, { control: undefined, scriptRunning: true });
    expect(instanceStateText(i)).toBe('Running');
  });
  it('says Not logged in when there is no status yet', () => expect(instanceStateText(inst('A', { status: null }))).toBe('Not logged in'));
  it('says Update needed on a version mismatch', () => expect(instanceStateText(inst('A', { connection: 'VersionMismatch' }))).toBe('Update needed'));
});

describe('the selector in the Skua view', () => {
  const tabs = (v: VNode) => all(v).filter((n) => n.attrs?.role === 'tab');
  it('shows no selector for one instance', () => {
    const v = skuaView(model(many(1)));
    expect(tabs(v)).toHaveLength(0);
    expect(all(v).some((n) => n.attrs?.role === 'tablist')).toBe(false);
    expect(texts(v)).toContain('Hero1');
  });
  it('falls back to the old single skua without instances (an older app)', () => {
    const m = model(undefined);
    m.state = { ...m.state!, skua: { connection: 'Connected', hello: null, status: null, log: [] } };
    expect(all(skuaView(m)).some((n) => n.attrs?.role === 'tablist')).toBe(false);
  });
  it.each([2, 3, 5])('shows a tablist with %i chips', (n) => {
    const v = skuaView(model(many(n)));
    const list = all(v).filter((x) => x.attrs?.role === 'tablist');
    expect(list).toHaveLength(1);
    expect(list[0]!.attrs!['aria-label']).toBe('Skua instances');
    expect(tabs(v)).toHaveLength(n);
  });
  it('marks exactly the selected chip and makes only it tabbable', () => {
    const t = tabs(skuaView(model(many(3), 'Hero2')));
    expect(t.map((x) => x.attrs!['aria-selected'])).toEqual(['false', 'true', 'false']);
    expect(t.map((x) => x.attrs!.tabindex)).toEqual(['-1', '0', '-1']);
    expect(t[1]!.attrs!['data-value']).toBe('Hero2');
    expect(t[1]!.attrs!['data-action']).toBe('skua-instance');
  });
  it('writes the state as text on every chip', () => {
    const l = [inst('A', {}, { control: 'running', scriptRunning: true }), inst('B', {}, { control: 'settling', settleSecondsLeft: 4 })];
    const t = texts(skuaView(model(l)));
    expect(t).toContain('Running');
    expect(t).toContain('Settling 4 s');
  });
  it('shows more than 5 as a list of buttons with the same states, no tablist', () => {
    const v = skuaView(model(many(6), 'Hero6'));
    expect(all(v).some((n) => n.attrs?.role === 'tablist')).toBe(false);
    const buttons = all(v).filter((n) => n.attrs?.['data-action'] === 'skua-instance');
    expect(buttons).toHaveLength(6);
    expect(buttons.map((b) => b.attrs!['aria-pressed'])).toEqual(['false', 'false', 'false', 'false', 'false', 'true']);
    expect(texts(v).filter((x) => x === 'Idle')).toHaveLength(6);
  });
  it('handles 8 instances', () => expect(all(skuaView(model(many(8)))).filter((n) => n.attrs?.['data-action'] === 'skua-instance')).toHaveLength(8));
  it('keeps a disconnected instance as a dashed chip with the text Disconnected', () => {
    const l = [inst('On'), inst('Off', { disconnected: true, connection: 'NotConnected', status: null })];
    const v = skuaView(model(l));
    const off = tabs(v).find((x) => x.attrs!['data-value'] === 'Off')!;
    expect(off.className).toContain('off');
    expect(texts(off)).toContain('Disconnected');
  });
  it('shows everything below the selector for the selected instance only', () => {
    const l = [
      inst('Alpha', {}, { quests: [{ id: 1, name: 'AlphaQuest', ready: true }], factions: [{ id: 1, name: 'AlphaFaction', rank: 1, totalRep: 5, rep: 5, requiredRep: 100 }] }),
      inst('Beta', { log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: 'Beta sold a thing' }] }, { quests: [{ id: 2, name: 'BetaQuest', ready: false }] }),
    ];
    const a = texts(skuaView(model(l, 'Alpha')));
    expect(a).toContain('AlphaQuest');
    expect(a).toContain('AlphaFaction');
    expect(a).not.toContain('BetaQuest');
    expect(a).not.toContain('Beta sold a thing');
    const b = texts(skuaView(model(l, 'Beta')));
    expect(b).toContain('BetaQuest');
    expect(b).toContain('Beta sold a thing');
    expect(b).not.toContain('AlphaQuest');
  });
  it('shows Skua is not connected below a disconnected selected instance', () => {
    const l = [inst('On'), inst('Off', { disconnected: true, connection: 'Connected' })];
    expect(texts(skuaView(model(l, 'Off')))).toContain('Skua is not connected.');
  });
  it('puts hostile and long names in text only', () => {
    const evil = '<img src=x onerror=alert(1)>' + 'W'.repeat(400);
    const l = [inst(evil), inst('<b>x</b>')];
    const v = skuaView(model(l));
    for (const n of all(v)) {
      expect(n.tag).not.toBe('img');
      expect(n.tag).not.toBe('b');
    }
    expect(tabs(v)[1]!.children.some((c) => c.text === '<b>x</b>')).toBe(true);
    for (const n of all(v)) expect(Object.keys(n.attrs ?? {}).filter((k) => k.startsWith('on') || k === 'style')).toEqual([]);
  });
  it('shows the Skua card on the list view as before', () => expect(texts(listView(model(many(2))))).toContain('Skua connected.'));
});

describe('effectiveFactions across instances', () => {
  const capture: Faction[] = [{ name: 'Captured', rank: 2, rep: 5, toNext: 95, fraction: 0.05, max: false }];
  const fac = (name: string) => [{ id: 1, name, rank: 3, totalRep: 10, rep: 10, requiredRep: 100 }];
  const remote = (list: SkuaInstance[], selected: string | null = 'Mule'): RemoteState => ({
    snapshot: { contract: 1, capture: { state: 'Listening.', listening: true }, selected, characters: [], factions: capture, hardFarms: null },
    notices: [], skua: list[0] ?? null, skuaInstances: list,
  });
  it('finds the selected character in a later instance', () => {
    const r = effectiveFactions(remote([inst('Hero', {}, { factions: fac('H') }), inst('Mule', {}, { factions: fac('FromMule') })]));
    expect(r.source).toBe('skua');
    expect(r.factions[0]!.name).toBe('FromMule');
  });
  it('matches case-insensitively', () => expect(effectiveFactions(remote([inst('mule', {}, { factions: fac('X') })], 'MULE')).source).toBe('skua'));
  it('ignores disconnected instances', () => {
    const r = effectiveFactions(remote([inst('Mule', { disconnected: true }, { factions: fac('Stale') })]));
    expect(r).toEqual({ factions: capture, source: 'capture' });
  });
  it('skips a match with an empty list and uses a later one', () => {
    const r = effectiveFactions(remote([inst('Mule', {}, { factions: [] }), inst('Mule', {}, { factions: fac('Second') })]));
    expect(r.factions[0]!.name).toBe('Second');
  });
  it('falls back to the capture list when nobody matches', () => {
    expect(effectiveFactions(remote([inst('Hero', {}, { factions: fac('H') })]))).toEqual({ factions: capture, source: 'capture' });
  });
  it('still works from the old skua field without instances', () => {
    const r = remote([]);
    r.skuaInstances = undefined;
    r.skua = inst('Mule', {}, { factions: fac('Old') });
    expect(effectiveFactions(r).source).toBe('skua');
  });
});

describe('the new notice kinds', () => {
  const n = (kind: string, character: string | null) => ({ id: '1', kind, character, title: 'server title', body: 'server body', atUtc: new Date(NOW - 120_000).toISOString() }) as never;
  it('render with catalog texts and the label', () => {
    expect(noticeLine(n('SkuaDisconnected', 'Hero'), NOW)).toMatchObject({ title: 'Skua disconnected', body: 'Hero: Skua closed or the link was lost.' });
    expect(noticeLine(n('SkuaReconnected', 'Hero'), NOW)).toMatchObject({ title: 'Skua is back', body: 'Hero: Skua is connected again.' });
    expect(noticeLine(n('SkuaOptionsWaiting', 'Hero'), NOW)).toMatchObject({ title: 'Waiting for you on the PC', body: "Hero: close the script's options window on the computer." });
  });
  it('keep the server text for older kinds and when no label is known', () => {
    expect(noticeLine(n('RankUp', 'Hero'), NOW).title).toBe('server title');
    expect(noticeLine(n('SkuaDisconnected', null), NOW).title).toBe('server title');
  });
});

describe('instance styles', () => {
  const css = readFileSync(new URL('../src/styles/app.css', import.meta.url), 'utf8');
  const rule = (sel: string) => css.match(new RegExp(`${sel}\\s*\\{([^}]*)\\}`))?.[1] ?? '';
  it('gives chips a 3rem minimum height, scrolls the row sideways with a gap of 0.5rem', () => {
    expect(rule('\\.chip')).toMatch(/min-height:\s*3rem/);
    const row = rule('\\.chips');
    expect(row).toMatch(/gap:\s*0\.5rem/);
    expect(row).toMatch(/overflow-x:\s*auto/);
  });
  it('fades the edge of the chip row', () => expect(css).toMatch(/\.chip-wrap::after\s*\{[^}]*linear-gradient/));
  it('dashes the disconnected chip without lowering contrast through opacity', () => {
    expect(css).toMatch(/\.chip\.off[^{]*\{[^}]*border-style:\s*dashed/);
    expect(css).not.toMatch(/opacity/);
  });
  it('wraps long names in the list and cuts them with an ellipsis on chips', () => {
    expect(rule('\\.chip-name')).toMatch(/text-overflow:\s*ellipsis/);
    expect(rule('\\.inst-btn \\.chip-name')).toMatch(/white-space:\s*normal/);
  });
  it('scrolls without animation for people who ask', () => {
    expect(css).toMatch(/prefers-reduced-motion:\s*reduce\)\s*\{[^}]*\.chips\s*\{[^}]*scroll-behavior:\s*auto/);
  });
});

describe('contrast of the instance styles', () => {
  const tokens = readFileSync(new URL('../src/styles/tokens.css', import.meta.url), 'utf8');
  const css = readFileSync(new URL('../src/styles/app.css', import.meta.url), 'utf8');
  const block = (t: string) => Object.fromEntries([...t.matchAll(/--([a-z-]+):\s*(#[0-9A-Fa-f]{6})\s*;/g)].map((m) => [m[1]!, m[2]!]));
  const body = (re: RegExp) => tokens.match(re)![1]!;
  const light = block(body(/^:root\s*\{([^}]*)\}/m));
  const sysDark = { ...light, ...block(body(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme='light'\]\)\s*\{([^}]*)\}/)) };
  const forcedDark = { ...light, ...block(body(/^:root\[data-theme='dark'\]\s*\{([^}]*)\}/m)) };
  const lum = (hex: string) => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)) as [number, number, number];
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const ratio = (a: string, b: string) => {
    const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x) as [number, number];
    return (hi + 0.05) / (lo + 0.05);
  };
  const decl = (sel: string, prop: string) => css.match(new RegExp(`${sel}\\s*\\{[^}]*(?:^|[;{\\s])${prop}:\\s*var\\(--([a-z-]+)\\)`))?.[1];
  // [selector, text property token, background property token, minimum ratio]
  const pairs: [string, string, string, string, number][] = [
    ['chip name', '\\.chip', 'color', 'background', 7],
    ['chip state', '\\.chip-state', 'color', '', 4.5],
    ['selected chip', '\\.chip\\[aria-selected="true"\\]', 'color', 'background', 7],
    ['selected list button', '\\.inst-btn\\[aria-pressed="true"\\]', 'color', 'background', 7],
  ];
  describe.each([['light', light], ['system dark', sysDark], ['forced dark', forcedDark]] as const)('%s', (_n, t) => {
    it.each(pairs)('%s reads at its ratio', (_label, sel, fg, bg, min) => {
      const fgToken = decl(sel, fg)!;
      expect(fgToken, `${sel} ${fg}`).toBeTruthy();
      const bgToken = bg ? decl(sel, bg)! : 'surface';
      expect(bgToken, `${sel} ${bg}`).toBeTruthy();
      expect(ratio(t[fgToken]!, t[bgToken]!)).toBeGreaterThanOrEqual(min);
    });
    it('the selected chip stands out from the others by more than colour: a ring and the fill', () => {
      expect(css).toMatch(/\.chip\[aria-selected="true"\]\s*\{[^}]*box-shadow:[^;}]*var\(--accent\)/);
      expect(ratio(t.border!, t.surface!)).toBeGreaterThanOrEqual(3);
      expect(Math.max(ratio(t.top!, t.surface!), ratio(t.accent!, t.surface!))).toBeGreaterThanOrEqual(3);
    });
  });
});
