import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  effectiveFactions, followSelection, instanceStateText, loadInstance, neighbourId, parseSkuaInstances, reputationHome, reputationSummary, saveInstance, selectInstance,
  SKUA_INSTANCE_KEY,
} from '../src/skua';
import { noticeLine, parseState } from '../src/state';
import { characterView, skuaView, listView, type Model, type VNode } from '../src/views';
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

describe('neighbourId', () => {
  it('moves and wraps', () => {
    const l = many(3);
    expect(neighbourId(l, 'id-Hero1', 1)).toBe('id-Hero2');
    expect(neighbourId(l, 'id-Hero3', 1)).toBe('id-Hero1');
    expect(neighbourId(l, 'id-Hero1', -1)).toBe('id-Hero3');
  });
  it('steps by position, so two Skua with the same label are both reachable', () => {
    const l = [inst('Skua 1', { instanceId: 'a' }), inst('Skua 1', { instanceId: 'b' }), inst('Hero', { instanceId: 'c' })];
    expect(neighbourId(l, 'a', 1)).toBe('b');
    expect(neighbourId(l, 'b', 1)).toBe('c');
    expect(neighbourId([], 'a', 1)).toBeNull();
  });
});

describe('followSelection (the selection follows the chosen Skua within the session)', () => {
  it('keeps the chosen instance when its label changes, and updates the stored label', () => {
    const before = [inst('Skua 1', { instanceId: 'a' }), inst('Mule', { instanceId: 'b' })];
    const after = [inst('Hero', { instanceId: 'a' }), inst('Mule', { instanceId: 'b' })];
    const chosen = followSelection(before, { label: 'Skua 1', id: 'a' });
    expect(chosen.instance!.instanceId).toBe('a');
    const next = followSelection(after, chosen);
    expect(next).toMatchObject({ label: 'Hero', id: 'a' });
    expect(next.instance!.instanceId).toBe('a');
  });
  it('tells apart two instances with the same label by id', () => {
    const l = [inst('Skua 1', { instanceId: 'a' }), inst('Skua 1', { instanceId: 'b' })];
    expect(followSelection(l, { label: 'Skua 1', id: 'b' }).instance!.instanceId).toBe('b');
    expect(selectInstance(l, 'Skua 1', 'b')!.instanceId).toBe('b');
  });
  it('takes the id of the remembered character when it appears', () => {
    const l = [inst('Mule', { instanceId: 'b' }), inst('Hero', { instanceId: 'a' })];
    expect(followSelection(l, { label: 'Hero', id: null })).toMatchObject({ label: 'Hero', id: 'a' });
  });
  it('a fallback keeps the remembered label and takes no id, so the character is found when it logs in', () => {
    const l = [inst('Skua 1', { instanceId: 'a' })];
    const r = followSelection(l, { label: 'Hero', id: null });
    expect(r).toMatchObject({ label: 'Hero', id: null });
    expect(r.instance!.instanceId).toBe('a');
  });
  it('an id that is gone falls back like a label that is gone', () => {
    const l = [inst('Mule', { instanceId: 'b' })];
    expect(followSelection(l, { label: 'Hero', id: 'a' })).toMatchObject({ label: 'Hero', id: null });
  });
  it('is empty for no instances', () => expect(followSelection([], { label: 'Hero', id: 'a' }).instance).toBeNull());
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
    expect(t[1]!.attrs!['data-value']).toBe('id-Hero2');
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
    const off = tabs(v).find((x) => x.attrs!['data-value'] === 'id-Off')!;
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
  it.each([['NotConnected'], ['VersionMismatch']])('ignores an instance whose connection is %s even when it is not marked disconnected', (connection) => {
    const r = effectiveFactions(remote([inst('Mule', { connection }, { factions: fac('Stale') })]));
    expect(r).toEqual({ factions: capture, source: 'capture' });
  });
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
  it('fades it only when the row overflows', () => {
    expect(rule('\\.chip-wrap::after')).toMatch(/display:\s*none/);
    expect(rule('\\.chip-wrap\\[data-overflow="true"\\]::after')).toMatch(/display:\s*block/);
  });
  it('marks the chips with the instance id, so duplicate labels stay apart', () => {
    const l = [inst('Skua 1', { instanceId: 'a' }), inst('Skua 1', { instanceId: 'b' })];
    const v = skuaView({ ...model(l), skuaInstanceId: 'b' });
    const chips = all(v).filter((n) => n.attrs?.role === 'tab');
    expect(chips.map((c) => [c.attrs!['data-value'], c.attrs!['aria-selected']])).toEqual([['a', 'false'], ['b', 'true']]);
  });
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

describe('reputation in one place', () => {
  const capture: Faction[] = [
    { name: 'Good', rank: 10, rep: 302500, toNext: 0, fraction: 1, max: true },
    { name: 'Captured', rank: 2, rep: 5, toNext: 95, fraction: 0.05, max: false },
  ];
  const fac = (id: number, name: string, rep: number, requiredRep: number, rank = 3) => ({ id, name, rank, totalRep: 10, rep, requiredRep });
  const character = (name: string) => ({
    name, level: 80, live: true, savedAtUtc: '2026-11-11T12:00:00Z', lastLiveUpdateUtc: null, liveUpdates: 0,
    figures: { gold: 1, exp: 1, expToLevel: 2, ac: 1, classPoints: 1, daysPlayed: 1 }, lastKill: null,
  });
  const remote = (list: SkuaInstance[], characters: string[], selected: string | null = characters[0] ?? null): RemoteState => ({
    snapshot: { contract: 1, capture: { state: 'Listening.', listening: true }, selected, characters: characters.map(character), factions: capture, hardFarms: null },
    notices: [], skua: list[0] ?? null, skuaInstances: list,
  });
  const at = (state: RemoteState, saved: string | null = null): Model => ({ state, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, skuaInstance: saved });
  const headings = (v: VNode) => all(v).filter((n) => n.tag === 'h2').map((n) => n.text);
  const reputationCards = (v: VNode) => all(v).filter((n) => n.tag === 'section' && n.children[0]?.text === 'Reputation');

  it('finds the character screen of a Skua whose character is captured and selected, ignoring case', () => {
    expect(reputationHome(remote([], ['Hero']), inst('hero'))).toBe('Hero');
  });
  it('finds none when the character is not captured, not selected, or nobody is logged in', () => {
    expect(reputationHome(remote([], ['Other']), inst('Hero'))).toBeNull();
    expect(reputationHome(remote([], ['Hero', 'Other'], 'Other'), inst('Hero'))).toBeNull();
    expect(reputationHome(remote([], ['Hero'], null), inst('Hero'))).toBeNull();
    expect(reputationHome(remote([], ['Hero']), inst('Hero', {}, { character: null }))).toBeNull();
    expect(reputationHome({ snapshot: null, notices: [] }, inst('Hero'))).toBeNull();
    expect(reputationHome(null, inst('Hero'))).toBeNull();
  });

  it('sums up an ordered list: the count and the first faction not at the maximum rank', () => {
    expect(reputationSummary(capture)).toEqual({ count: 2, closest: capture[1] });
    const ordered: Faction[] = [{ ...capture[1]!, name: 'C', toNext: 5 }, { ...capture[1]!, name: 'A', toNext: 10 }, capture[0]!];
    expect(reputationSummary(ordered).closest!.name).toBe('C');
    expect(reputationSummary([capture[0]!])).toEqual({ count: 1, closest: null });
    expect(reputationSummary([])).toEqual({ count: 0, closest: null });
  });

  it('a captured, selected character: the Skua tab links to its screen instead of drawing the list', () => {
    const list = [inst('Hero', {}, { factions: [fac(1, 'Yokai', 100, 1000), fac(2, 'Nation', 900, 1000)] })];
    const v = skuaView(at(remote(list, ['Hero'])));
    const cards = reputationCards(v);
    expect(cards).toHaveLength(1);
    expect(all(cards[0]!).some((n) => n.attrs?.role === 'progressbar')).toBe(false);
    const link = all(cards[0]!).find((n) => n.tag === 'a')!;
    expect(link.attrs!.href).toBe('#/c/Hero');
    expect(texts(link)).toEqual(['2 factions for Hero', 'Closest to next rank: Nation (100 to go)']);
  });
  it('the summary follows what the character screen shows: the capture list when the live list is empty', () => {
    const v = skuaView(at(remote([inst('Hero', {}, { factions: [] })], ['Hero'])));
    expect(texts(v)).toEqual(expect.arrayContaining(['2 factions for Hero', 'Closest to next rank: Captured (95 to go)']));
  });
  it('says all are at the maximum rank when none has a next rank', () => {
    const v = skuaView(at(remote([inst('Hero', {}, { factions: [fac(1, 'Good', 0, 0, 10)] })], ['Hero'])));
    expect(texts(v)).toEqual(expect.arrayContaining(['1 faction for Hero', 'All at max rank']));
  });
  it('a character that is not captured keeps the full card on the Skua tab, so nothing is lost', () => {
    const list = [inst('Stranger', {}, { factions: [fac(1, 'Yokai', 100, 1000)] })];
    const v = skuaView(at(remote(list, ['Hero'])));
    const cards = reputationCards(v);
    expect(cards).toHaveLength(1);
    expect(all(cards[0]!).some((n) => n.attrs?.role === 'progressbar')).toBe(true);
    expect(texts(cards[0]!)).toEqual(['Reputation', 'Yokai', 'Rank 3', '900 to next rank']);
    expect(all(cards[0]!).some((n) => n.tag === 'a')).toBe(false);
  });
  it('a captured character that is not the selected one keeps the full card too (its screen shows no reputation)', () => {
    const list = [inst('Hero', {}, { factions: [fac(1, 'Yokai', 100, 1000)] })];
    const v = skuaView(at(remote(list, ['Hero', 'Other'], 'Other')));
    expect(all(reputationCards(v)[0]!).some((n) => n.attrs?.role === 'progressbar')).toBe(true);
  });
  it('shows no reputation card when the plugin does not report reputation', () => {
    expect(headings(skuaView(at(remote([inst('Hero')], ['Hero']))))).not.toContain('Reputation');
  });
  it('the character screen titles its single card Reputation and takes the live list when Skua matches', () => {
    const list = [inst('Hero', {}, { factions: [fac(1, 'LiveFaction', 100, 1000)] })];
    const live = characterView(at(remote(list, ['Hero'])), 'Hero');
    expect(reputationCards(live)).toHaveLength(1);
    expect(texts(live)).toEqual(expect.arrayContaining(['Live from Skua', 'LiveFaction']));
    const captured = characterView(at(remote([inst('Stranger', {}, { factions: [fac(1, 'LiveFaction', 100, 1000)] })], ['Hero'])), 'Hero');
    expect(texts(captured)).toContain('Captured');
    expect(texts(captured)).not.toContain('Live from Skua');
    expect(texts(captured)).not.toContain('LiveFaction');
  });
  it('writes hostile names as text only, in the link and the summary', () => {
    const evil = '<img src=x onerror=alert(1)>';
    const list = [inst(evil, {}, { factions: [fac(1, '<b>F</b>', 100, 1000)] })];
    const v = skuaView(at(remote(list, [evil])));
    expect(texts(v)).toEqual(expect.arrayContaining([`1 faction for ${evil}`, 'Closest to next rank: <b>F</b> (900 to go)']));
    const link = all(v).find((n) => n.tag === 'a' && n.attrs?.href?.startsWith('#/c/'))!;
    expect(link.attrs!.href).toBe(`#/c/${encodeURIComponent(evil)}`);
    for (const n of all(v)) {
      expect(['img', 'b', 'script']).not.toContain(n.tag);
      for (const k of Object.keys(n.attrs ?? {})) expect(k === 'style' || k.startsWith('on')).toBe(false);
    }
  });
});
