import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { effectiveFactions, factionRows, filterOf, formatDuration, LOG_FILTERS, parseLogFilter, parseSkua, questRows, skuaEmptyText, skuaLogRows, skuaNow, skuaSummary, type LogFilter } from '../src/skua';
import { isUsableState, keepState, parseRoute, parseState, recallKept } from '../src/state';
import { characterView, listView, skuaView, type Model, type VNode } from '../src/views';
import { plural } from '../src/i18n';
import type { Faction, RemoteState, SkuaState } from '../src/types';

const NOW = Date.parse('2026-11-11T12:12:00Z');
const wire = {
  connection: 'Connected',
  hello: { pluginVersion: '0.1.0', skuaVersion: '1.4.5.0' },
  status: { loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: true, runningSince: '2026-11-11T12:00:00+00:00', quests: null, factions: null },
  log: [{ at: '2026-11-11T12:00:05+00:00', kind: 'Sold', text: 'Sold Blood Orb x50 for 2,500 gold' }],
};
const skua = (over: Partial<SkuaState> = {}): SkuaState => ({ ...parseSkua(wire)!, ...over });
const model = (s: SkuaState | null | undefined, skuaFilter?: LogFilter): Model => ({
  state: { snapshot: null, notices: [], skua: s }, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, skuaFilter,
});
const withStatus = (over: object, s: Partial<SkuaState> = {}) => skua({ ...s, status: { ...wire.status, ...over } });
const quest = (id: number, name: string, ready: boolean) => ({ id, name, ready });
const faction = (id: number, name: string, rank: number, rep: number, requiredRep: number, totalRep = 0) => ({ id, name, rank, totalRep, rep, requiredRep });
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
  it('cuts a log text without splitting a surrogate pair', () => {
    const s = parseSkua({ ...wire, log: [{ at: 'a', kind: 'k', text: 'a'.repeat(299) + '\u{1F600}tail' }] })!;
    expect(s.log[0]!.text).toBe('a'.repeat(299));
  });
});

describe('parseSkua quests', () => {
  const withQuests = (quests: unknown) => ({ ...wire, status: { ...wire.status, quests } });
  const quests = (value: unknown) => parseSkua(withQuests(value))!.status!.quests;

  it('reads the active quests with their ready flag', () => {
    expect(quests([{ id: 5, name: 'Slay the Beast', ready: true }, { id: 12, name: 'Gather 10 Bones', ready: false }])).toEqual([
      { id: 5, name: 'Slay the Beast', ready: true },
      { id: 12, name: 'Gather 10 Bones', ready: false },
    ]);
  });
  it('keeps null (not reported) apart from an empty list (none active)', () => {
    expect(quests([])).toEqual([]);
    expect(quests(null)).toBeNull();
  });
  it('reads an older payload without quests as not reported', () => {
    const { quests: _omit, ...old } = wire.status;
    expect(parseSkua({ ...wire, status: old })!.status!.quests).toBeNull();
  });
  it('treats a quests value that is not an array as not reported, keeping the rest of the status', () => {
    for (const bad of ['x', 5, true, { id: 1, name: 'a', ready: true }]) {
      const s = parseSkua(withQuests(bad))!;
      expect(s.status!.quests).toBeNull();
      expect(s.status!.character).toBe('Hero');
    }
  });
  it('drops a malformed entry and keeps the others', () => {
    const good = { id: 5, name: 'Slay the Beast', ready: true };
    const bad = [
      null, 'q', 7, [], { id: -1, name: 'n', ready: true }, { id: 1.5, name: 'n', ready: true }, { id: '7', name: 'n', ready: true },
      { id: 2_147_483_648, name: 'n', ready: true }, { id: Number.NaN, name: 'n', ready: true }, { name: 'n', ready: true },
      { id: 7, name: null, ready: true }, { id: 7, ready: true }, { id: 7, name: 'n', ready: 'true' }, { id: 7, name: 'n' },
    ];
    expect(quests([...bad, good])).toEqual([good]);
  });
  it('keeps at most 30 quests and cuts a long name to 300 without splitting a surrogate pair', () => {
    const many = Array.from({ length: 40 }, (_, i) => ({ id: i, name: `Q${i}`, ready: false }));
    const list = quests(many)!;
    expect(list).toHaveLength(30);
    expect(list[29]!.id).toBe(29);
    expect(quests([{ id: 1, name: 'n'.repeat(400), ready: true }])![0]!.name).toHaveLength(300);
    const pair = quests([{ id: 1, name: 'a'.repeat(299) + '\u{1F600}tail', ready: true }])![0]!.name;
    expect(pair).toBe('a'.repeat(299));
  });
  it('never throws on hostile input', () => {
    expect(() => parseSkua(withQuests([{ id: 1, name: { toString: 1 }, ready: true }, Object.create(null)]))).not.toThrow();
  });
});

describe('parseSkua factions', () => {
  const withFactions = (factions: unknown) => ({ ...wire, status: { ...wire.status, factions } });
  const factions = (value: unknown) => parseSkua(withFactions(value))!.status!.factions;
  const good = faction(1, 'Nation', 3, 300, 1200, 5000);

  it('reads the factions', () => {
    expect(factions([good, faction(20, 'Good', 10, 0, 0, 302500)])).toEqual([good, faction(20, 'Good', 10, 0, 0, 302500)]);
  });
  it('keeps null (not reported) apart from an empty list (none)', () => {
    expect(factions([])).toEqual([]);
    expect(factions(null)).toBeNull();
    const { factions: _omit, ...old } = wire.status;
    expect(parseSkua({ ...wire, status: old })!.status!.factions).toBeNull();
  });
  it('treats a factions value that is not an array as not reported, keeping the rest of the status', () => {
    for (const bad of ['x', 5, true, good]) {
      const s = parseSkua(withFactions(bad))!;
      expect(s.status!.factions).toBeNull();
      expect(s.status!.character).toBe('Hero');
    }
  });
  it('drops a malformed entry and keeps the others', () => {
    const bad = [
      null, 'f', 7, [], { ...good, id: -1 }, { ...good, rank: -1 }, { ...good, totalRep: -1 }, { ...good, rep: -1 }, { ...good, requiredRep: -1 },
      { ...good, id: 1.5 }, { ...good, rep: 0.5 }, { ...good, id: '7' }, { ...good, rank: '3' }, { ...good, totalRep: 2_147_483_648 },
      { ...good, rep: Number.NaN }, { ...good, requiredRep: Infinity }, { ...good, name: null }, { ...good, name: 5 },
      { id: 1, rank: 1, totalRep: 1, rep: 1, requiredRep: 1 }, { id: 1, name: 'n', totalRep: 1, rep: 1, requiredRep: 1 }, { id: 1, name: 'n', rank: 1, rep: 1, requiredRep: 1 },
      { id: 1, name: 'n', rank: 1, totalRep: 1, requiredRep: 1 }, { id: 1, name: 'n', rank: 1, totalRep: 1, rep: 1 },
    ];
    expect(factions([...bad, good])).toEqual([good]);
  });
  it('keeps the largest int32 values', () => {
    const max = faction(2_147_483_647, 'Max', 2_147_483_647, 2_147_483_647, 2_147_483_647, 2_147_483_647);
    expect(factions([max])).toEqual([max]);
  });
  it('keeps at most 60 factions and cuts a long name to 300 without splitting a surrogate pair', () => {
    const list = factions(Array.from({ length: 70 }, (_, i) => faction(i, `F${i}`, 1, 1, 900)))!;
    expect(list).toHaveLength(60);
    expect(list[59]!.id).toBe(59);
    expect(factions([{ ...good, name: 'n'.repeat(400) }])![0]!.name).toHaveLength(300);
    expect(factions([{ ...good, name: 'a'.repeat(299) + '\u{1F600}tail' }])![0]!.name).toBe('a'.repeat(299));
  });
  it('never throws on hostile input', () => {
    expect(() => parseSkua(withFactions([{ ...good, name: { toString: 1 } }, Object.create(null)]))).not.toThrow();
  });
});

describe('factionRows', () => {
  const rows = (list: unknown[], over: Partial<SkuaState> = {}) => factionRows(withStatus({ factions: list }, over));

  it('orders by what is missing for the next rank, max rank last, then by name ignoring case, like the capture list', () => {
    const list = [faction(1, 'zeta', 2, 100, 1000), faction(2, 'Good', 10, 0, 0), faction(3, 'alpha', 5, 950, 1000), faction(4, 'Beta', 3, 900, 1000), faction(5, 'Evil', 10, 0, 0), faction(6, 'gamma', 4, 50, 100)];
    expect(rows(list)!.map((r) => r.name)).toEqual(['alpha', 'gamma', 'Beta', 'zeta', 'Evil', 'Good']);
  });
  it('computes rank, the share of the rank, what is left and the rank text', () => {
    expect(rows([faction(1, 'Nation', 3, 300, 1200)])).toEqual([{ name: 'Nation', rank: 3, max: false, fraction: 0.25, toNext: 900 }]);
  });
  it('treats a step of 0 as the maximum rank: full bar, nothing to next rank', () => {
    expect(rows([faction(1, 'Good', 10, 0, 0)])).toEqual([{ name: 'Good', rank: 10, max: true, fraction: 1, toNext: 0 }]);
  });
  it('is the maximum rank at rank 10 or with a step of 0, and otherwise not', () => {
    expect(rows([faction(1, 'Ten', 10, 40, 500)])![0]).toMatchObject({ max: true, fraction: 1, toNext: 0 });
    expect(rows([faction(1, 'Zero', 4, 0, 0)])![0]).toMatchObject({ max: true, fraction: 1, toNext: 0 });
    expect(rows([faction(1, 'Nine', 9, 40, 500)])![0]).toMatchObject({ max: false, toNext: 460 });
    expect(rows([faction(1, 'Alpha', 9, 400, 500), faction(2, 'Ten', 10, 499, 500), faction(3, 'Beta', 1, 0, 0)])!.map((r) => r.name)).toEqual(['Alpha', 'Beta', 'Ten']);
  });
  it('clamps rep beyond the step to a full bar with nothing left', () => {
    expect(rows([faction(1, 'Odd', 4, 1500, 1000)])![0]).toMatchObject({ max: false, fraction: 1, toNext: 0 });
    expect(rows([faction(1, 'Zero', 1, 0, 1000)])![0]).toMatchObject({ fraction: 0, toNext: 1000 });
  });
  it('never gives a NaN fraction', () => {
    for (const f of [faction(1, 'a', 1, Number.NaN, 1000), faction(1, 'b', 1, 5, Number.NaN), faction(1, 'c', 1, Infinity, Infinity)]) {
      const fraction = factionRows({ ...skua(), status: { ...skua().status!, factions: [f] } })![0]!.fraction;
      expect(Number.isFinite(fraction)).toBe(true);
      expect(fraction).toBeGreaterThanOrEqual(0);
      expect(fraction).toBeLessThanOrEqual(1);
    }
  });
  it('is null when not reported, without a status or not connected, and empty when there are none', () => {
    expect(rows(null as unknown as unknown[])).toBeNull();
    expect(factionRows(skua({ status: null }))).toBeNull();
    expect(rows([faction(1, 'N', 1, 1, 2)], { connection: 'VersionMismatch' })).toBeNull();
    expect(rows([faction(1, 'N', 1, 1, 2)], { connection: 'NotConnected' })).toBeNull();
    expect(rows([])).toEqual([]);
  });
});

describe('effectiveFactions', () => {
  const capture: Faction[] = [{ name: 'Captured', rank: 2, rep: 5, toNext: 95, fraction: 0.05, max: false }];
  const remote = (skuaState: SkuaState | null | undefined, selected: string | null = 'Hero', factions: Faction[] = capture): RemoteState => ({
    snapshot: { contract: 1, capture: { state: 'Listening.', listening: true }, selected, characters: [], factions, hardFarms: null },
    notices: [],
    skua: skuaState,
  });
  const live = [faction(1, 'zeta', 2, 100, 1000, 5100), faction(2, 'Good', 10, 0, 0, 9000), faction(3, 'alpha', 5, 950, 1000, 4950)];
  const connected = (over: object = {}, s: Partial<SkuaState> = {}) => withStatus({ character: 'Hero', factions: live, ...over }, s);

  it('uses the Skua list, mapped and ordered like the capture list, when connected for the selected character', () => {
    const r = effectiveFactions(remote(connected()));
    expect(r.source).toBe('skua');
    expect(r.factions).toEqual([
      { name: 'alpha', rank: 5, rep: 4950, toNext: 50, fraction: 0.95, max: false },
      { name: 'zeta', rank: 2, rep: 5100, toNext: 900, fraction: 0.1, max: false },
      { name: 'Good', rank: 10, rep: 9000, toNext: 0, fraction: 1, max: true },
    ]);
  });
  it.each([
    ['not connected', () => connected({}, { connection: 'NotConnected' })],
    ['a version mismatch', () => connected({}, { connection: 'VersionMismatch' })],
    ['factions not reported', () => connected({ factions: null })],
    ['an empty Skua list (a transient empty read must not blank the page)', () => connected({ factions: [] })],
    ['another character in Skua', () => connected({ character: 'Other' })],
    ['no Skua character', () => connected({ character: null })],
    ['no status', () => skua({ status: null })],
    ['no Skua state', () => null],
    ['Skua absent', () => undefined],
  ])('uses the capture list for %s', (_n, make) => {
    const r = effectiveFactions(remote(make()));
    expect(r).toEqual({ factions: capture, source: 'capture' });
  });
  it('matches the Skua character ignoring letter case', () => {
    expect(effectiveFactions(remote(connected({ character: 'hero' }))).source).toBe('skua');
    expect(effectiveFactions(remote(connected({ character: 'HERO' }), 'hErO')).source).toBe('skua');
  });
  it('uses the capture list when no character is selected', () => {
    expect(effectiveFactions(remote(connected(), null)).source).toBe('capture');
  });
  it('uses the capture list when there is no snapshot', () => {
    expect(effectiveFactions({ snapshot: null, notices: [], skua: connected() })).toEqual({ factions: [], source: 'capture' });
  });
  it('shows the note under the heading only for Skua, in the character view', () => {
    const m = (st: RemoteState): Model => ({ state: st, nowMs: NOW, showAll: false, hfOpen: false, installHint: null });
    const hero = { name: 'Hero', level: 1, live: true, savedAtUtc: '2026-11-11T12:00:00Z', lastLiveUpdateUtc: null, liveUpdates: 0, figures: { gold: 0, exp: 0, expToLevel: 1, ac: 0, classPoints: 0, daysPlayed: 0 }, lastKill: null };
    const withHero = (st: RemoteState): RemoteState => ({ ...st, snapshot: { ...st.snapshot!, characters: [hero] } });
    const fromSkua = texts(characterView(m(withHero(remote(connected()))), 'Hero'));
    expect(fromSkua).toContain('Live from Skua');
    expect(fromSkua).toContain('alpha');
    expect(fromSkua).not.toContain('Captured');
    const fromCapture = texts(characterView(m(withHero(remote(connected({ factions: [] })))), 'Hero'));
    expect(fromCapture).not.toContain('Live from Skua');
    expect(fromCapture).toContain('Captured');
  });
});

describe('formatDuration', () => {
  it.each([[0, 'just now'], [59_000, 'just now'], [180_000, '3 min'], [3_900_000, '1 h 5 min'], [7_200_000, '2 h'], [90_000_000, '1 d 1 h'], [Number.NaN, 'just now'], [-5000, 'just now'], [Infinity, 'just now']])('%s -> %s', (ms, text) =>
    expect(formatDuration(ms)).toBe(text));
});

describe('skua view model', () => {
  it('has the Now lines for a running script', () => {
    expect(skuaNow(skua(), NOW)).toEqual({
      loggedIn: true, character: 'Hero', where: 'Artix · battleon', script: { running: true, text: 'Running for 12 min', name: 'Farm.cs' },
    });
  });
  it('says no script running, and is null when not connected or without a status', () => {
    expect(skuaNow(withStatus({ scriptRunning: false }), NOW)!.script).toEqual({ running: false, text: 'No script running', name: null });
    expect(skuaNow(skua({ connection: 'NotConnected' }), NOW)).toBeNull();
    expect(skuaNow(skua({ status: null }), NOW)).toBeNull();
  });
  it('leaves missing parts out of Server · Map and falls back when the start time is unknown', () => {
    expect(skuaNow(withStatus({ server: null }), NOW)!.where).toBe('battleon');
    expect(skuaNow(withStatus({ server: null, map: '' }), NOW)!.where).toBeNull();
    expect(skuaNow(withStatus({ runningSince: null }), NOW)!.script.text).toBe('Running Farm.cs');
    expect(skuaNow(withStatus({ runningSince: null, script: null }), NOW)!.script.text).toBe('Running a script');
  });
  it('shows no character or place while not logged in', () => {
    expect(skuaNow(withStatus({ loggedIn: false }), NOW)).toMatchObject({ loggedIn: false, character: null, where: null });
  });
  it('keeps log order and words the time like a notice', () => {
    const s = skua({ log: [{ at: '2026-11-11T12:10:00Z', kind: 'a', text: 'new' }, { at: '2026-11-11T12:00:00Z', kind: 'a', text: 'old' }] });
    expect(skuaLogRows(s, NOW)).toEqual([{ when: '2 min ago', text: 'new' }, { when: '12 min ago', text: 'old' }]);
  });
  it('has the calm texts', () => {
    expect(skuaEmptyText(null)).toBe('Skua is not connected.');
    expect(skuaEmptyText(skua({ connection: 'NotConnected' }))).toBe('Skua is not connected.');
    expect(skuaEmptyText(skua({ log: [] }))).toBe('Nothing to show yet.');
    expect(skuaEmptyText(skua({ connection: 'VersionMismatch' }))).toBe('Update the Skua plugin and SkuaLink to the same release.');
    expect(skuaEmptyText(skua())).toBe('');
    expect(skuaSummary(skua())).toBe('Skua connected.');
  });
});

describe('log filters', () => {
  it.each([
    ['Bought', 'shop'], ['Sold', 'shop'], ['Dropped', 'drops'], ['MapChanged', 'maps'], ['ScriptStarted', 'script'], ['ScriptStopped', 'script'],
    ['ScriptError', 'script'], ['Died', 'session'], ['LoggedOut', 'session'], ['Relogin', 'session'],
    ['QuestAccepted', null], ['QuestTurnedIn', null], ['Mystery', null], ['', null], ['constructor', null], ['__proto__', null],
  ])('%s -> %s', (kind, filter) => expect(filterOf(kind)).toBe(filter));

  it('places every log kind SkuaLink knows (the shared C# fixture), so a new kind cannot fall under Everything unnoticed', () => {
    const kinds = JSON.parse(readFileSync(new URL('../../tests/AqwCompanion.Tests/Skua/fixtures/log-kinds.json', import.meta.url), 'utf8')) as string[];
    const onlyUnderEverything = ['QuestAccepted', 'QuestTurnedIn'];
    expect(kinds.length).toBeGreaterThan(0);
    for (const kind of kinds) {
      if (onlyUnderEverything.includes(kind)) expect(filterOf(kind), kind).toBeNull();
      else expect(filterOf(kind), kind).not.toBeNull();
    }
  });

  it('lists the filters in order, parsing anything else as Everything', () => {
    expect(LOG_FILTERS).toEqual(['all', 'shop', 'drops', 'maps', 'script', 'session']);
    for (const f of LOG_FILTERS) expect(parseLogFilter(f)).toBe(f);
    for (const bad of [null, '', 'x', 'ALL', 'constructor']) expect(parseLogFilter(bad)).toBe('all');
  });

  const kinds = ['Sold', 'Bought', 'Dropped', 'MapChanged', 'ScriptStarted', 'Died', 'QuestAccepted', 'QuestTurnedIn', 'Mystery'];
  const log = kinds.map((kind, i) => ({ at: '2026-11-11T12:00:00Z', kind, text: `${kind} ${i}` }));
  const rows = (f: LogFilter) => skuaLogRows(skua({ log }), NOW, f).map((r) => r.text);

  it('shows everything, including quest and unknown kinds, only under Everything', () => {
    expect(rows('all')).toHaveLength(9);
    expect(skuaLogRows(skua({ log }), NOW)).toHaveLength(9);
    expect(rows('shop')).toEqual(['Sold 0', 'Bought 1']);
    expect(rows('drops')).toEqual(['Dropped 2']);
    expect(rows('maps')).toEqual(['MapChanged 3']);
    expect(rows('script')).toEqual(['ScriptStarted 4']);
    expect(rows('session')).toEqual(['Died 5']);
    const named = new Set(LOG_FILTERS.filter((f) => f !== 'all').flatMap(rows));
    expect([...named].some((x) => /Quest|Mystery/.test(x))).toBe(false);
  });
  it('says Nothing here yet. for an empty filter, Nothing to show yet. for an empty log', () => {
    const only = skua({ log: [log[0]!] });
    expect(skuaEmptyText(only, 'drops')).toBe('Nothing here yet.');
    expect(skuaEmptyText(only, 'shop')).toBe('');
    expect(skuaEmptyText(only)).toBe('');
    expect(skuaEmptyText(skua({ log: [] }), 'drops')).toBe('Nothing to show yet.');
  });
});

describe('questRows', () => {
  const list = [quest(1, 'A', false), quest(2, 'B', true), quest(3, 'C', false), quest(4, 'D', true)];
  it('puts ready quests first and keeps the game order otherwise', () => {
    expect(questRows(withStatus({ quests: list }))!.map((q) => q.name)).toEqual(['B', 'D', 'A', 'C']);
  });
  it('is null when not reported or not connected, and empty when none are active', () => {
    expect(questRows(withStatus({ quests: null }))).toBeNull();
    expect(questRows(skua({ status: null }))).toBeNull();
    expect(questRows(withStatus({ quests: list }, { connection: 'VersionMismatch' }))).toBeNull();
    expect(questRows(withStatus({ quests: [] }))).toEqual([]);
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
  const sections = (v: VNode) => all(v).filter((n) => n.tag === 'section');
  const headings = (v: VNode) => all(v).filter((n) => n.tag === 'h2').map((n) => n.text);
  const buttons = (v: VNode) => all(v).filter((n) => n.tag === 'button');

  it('shows the Now, Active quests and Log cards in that order, with no back link (the dock leads home)', () => {
    const view = skuaView(model(withStatus({ quests: [quest(1, 'Slay the Beast', true), quest(2, 'Gather Bones', false)] })));
    expect(headings(view)).toEqual(['Now', 'Active quests · 2', 'Log']);
    expect(view.children[0]!.className).toBe('card now-card');
    expect(all(view).some((n) => n.className === 'back')).toBe(false);
    expect(texts(view)).toEqual(expect.arrayContaining(['Hero', 'Artix · battleon', 'Running for 12 min', 'Farm.cs', 'Sold Blood Orb x50 for 2,500 gold', '11 min ago']));
  });
  it('writes the quest count with plural() and lists the quests, ready first, with a shape and words', () => {
    expect(plural('skua.quests.title', 1)).toBe('Active quests · 1');
    expect(plural('skua.quests.title', 3)).toBe('Active quests · 3');
    expect(plural('skua.quests.title', 30)).toBe('Active quests · 30');
    const view = skuaView(model(withStatus({ quests: [quest(1, 'Gather Bones', false), quest(2, 'Slay the Beast', true)] })));
    const card = sections(view)[1]!;
    const items = all(card).filter((n) => n.tag === 'li');
    expect(items.map((li) => texts(li)[0])).toEqual(['Slay the Beast', 'Gather Bones']);
    expect(texts(items[0]!)).toContain('Ready to turn in');
    expect(all(items[0]!).some((n) => n.attrs?.['data-kind'] === 'ready' && n.attrs['aria-hidden'] === 'true')).toBe(true);
    expect(texts(items[1]!)).not.toContain('Ready to turn in');
    expect(all(card).some((n) => n.tag === 'ul')).toBe(true);
  });
  it('says No active quests. for an empty list and shows no quests card when not reported', () => {
    const none = skuaView(model(withStatus({ quests: [] })));
    expect(headings(none)).toEqual(['Now', 'Active quests · 0', 'Log']);
    expect(texts(none)).toContain('No active quests.');
    const old = skuaView(model(withStatus({ quests: null })));
    expect(headings(old)).toEqual(['Now', 'Log']);
  });
  it('shows the Reputation card after Active quests and before Log, one row per faction with rank, bar and what is left', () => {
    const view = skuaView(model(withStatus({ quests: [quest(1, 'Q', false)], factions: [faction(20, 'Good', 10, 0, 0), faction(1, 'Nation', 3, 300, 1200)] })));
    expect(headings(view)).toEqual(['Now', 'Active quests · 1', 'Reputation', 'Log']);
    const card = sections(view)[2]!;
    const items = all(card).filter((n) => n.tag === 'li');
    expect(items.map((li) => texts(li))).toEqual([['Nation', 'Rank 3', '900 to next rank'], ['Good', 'Max rank']]);
    const bars = all(card).filter((n) => n.attrs?.role === 'progressbar');
    expect(bars.map((b) => [b.attrs!['aria-label'], b.attrs!['aria-valuenow']])).toEqual([['Nation', '25'], ['Good', '100']]);
    expect(bars.map((b) => b.children[0]!.fraction)).toEqual([0.25, 1]);
    expect(card.className?.split(' ')).toContain('card');
  });
  it('says No factions read yet. for an empty list and shows no Reputation card when not reported', () => {
    const none = skuaView(model(withStatus({ quests: [], factions: [] })));
    expect(headings(none)).toEqual(['Now', 'Active quests · 0', 'Reputation', 'Log']);
    expect(texts(none)).toContain('No factions read yet.');
    expect(headings(skuaView(model(withStatus({ quests: [], factions: null }))))).toEqual(['Now', 'Active quests · 0', 'Log']);
  });
  it('shows the Reputation card even when quests are not reported', () => {
    expect(headings(skuaView(model(withStatus({ quests: null, factions: [faction(1, 'N', 1, 1, 2)] }))))).toEqual(['Now', 'Reputation', 'Log']);
  });
  it('shows faction names as text only', () => {
    const view = skuaView(model(withStatus({ factions: [faction(1, '<img onerror=x>', 1, 1, 2)] })));
    expect(texts(view)).toContain('<img onerror=x>');
    for (const n of all(view)) for (const name of Object.keys(n.attrs ?? {})) expect(name === 'style' || name.startsWith('on')).toBe(false);
  });
  it('builds the filter row: a labelled group of pressed-state buttons, Everything pressed by default', () => {
    const view = skuaView(model(skua()));
    const group = all(view).find((n) => n.attrs?.role === 'group')!;
    expect(group.attrs!['aria-label']).toBe('Filter the log');
    expect(group.className).toBe('filters');
    const logCard = sections(view)[1]!;
    expect(logCard.children[0]!.text).toBe('Log');
    expect(logCard.children[1]).toBe(group);
    const bs = buttons(group);
    expect(bs.map((b) => b.text)).toEqual(['Everything', 'Shop', 'Drops', 'Maps', 'Script', 'Session']);
    expect(bs.map((b) => b.attrs!['data-value'])).toEqual([...LOG_FILTERS]);
    for (const b of bs) {
      expect(b.attrs!['data-action']).toBe('skua-filter');
      expect(b.attrs!.type).toBe('button');
      expect(b.className).toBe('filter-btn');
    }
    expect(bs.map((b) => b.attrs!['aria-pressed'])).toEqual(['true', 'false', 'false', 'false', 'false', 'false']);
  });
  it('presses the chosen filter, narrows only the log and never hides the other cards', () => {
    const s = withStatus({ quests: [quest(1, 'Q', true)] }, { log: [{ at: '2026-11-11T12:00:00Z', kind: 'Sold', text: 'sold it' }, { at: '2026-11-11T12:00:00Z', kind: 'Died', text: 'died' }] });
    const view = skuaView(model(s, 'session'));
    expect(buttons(view).map((b) => b.attrs!['aria-pressed'])).toEqual(['false', 'false', 'false', 'false', 'false', 'true']);
    expect(texts(view)).toContain('died');
    expect(texts(view)).not.toContain('sold it');
    expect(headings(view)).toEqual(['Now', 'Active quests · 1', 'Log']);
    expect(texts(skuaView(model(s, 'drops')))).toContain('Nothing here yet.');
    expect(headings(skuaView(model(s, 'drops')))).toEqual(['Now', 'Active quests · 1', 'Log']);
  });
  it('gives all three cards the card class', () => {
    const view = skuaView(model(withStatus({ quests: [] })));
    const cards = all(view).filter((n) => n.tag === 'section');
    expect(cards).toHaveLength(3);
    for (const c of cards) expect(c.className?.split(' ')).toContain('card');
  });
  it('is stable across redraws: the same data gives the same tree', () => {
    expect(JSON.stringify(skuaView(model(skua())))).toBe(JSON.stringify(skuaView(model(skua()))));
  });
  it('says so in the Now card when connected without a character', () => {
    const view = skuaView(model(withStatus({ loggedIn: false, character: null, server: null, map: null, scriptRunning: false, script: null, runningSince: null, quests: [] })));
    expect(texts(view)).toEqual(expect.arrayContaining(['Not logged in to a character.', 'No script running']));
    expect(texts(view)).not.toContain('Hero');
  });
  it('shows no Now card and no quests card when connected without a status', () => {
    const view = skuaView(model(skua({ status: null })));
    expect(headings(view)).toEqual(['Log']);
  });
  it('shows no empty time line for an unreadable time', () => {
    const s = skua({ log: [{ at: 'not a date', kind: 'x', text: 'odd' }] });
    const view = skuaView(model(s));
    expect(texts(view)).toContain('odd');
    expect(all(view).some((n) => n.className === 'muted')).toBe(false);
  });
  it('shows only the calm explanation when not connected or on a version mismatch', () => {
    for (const s of [null, skua({ connection: 'NotConnected' }), withStatus({ quests: [quest(1, 'Q', true)], factions: [faction(1, 'N', 1, 1, 2)] }, { connection: 'VersionMismatch' })]) {
      const view = skuaView(model(s));
      expect(sections(view)).toHaveLength(1);
      expect(buttons(view)).toHaveLength(0);
      expect(headings(view)).toEqual(['Skua']);
    }
    expect(texts(skuaView(model(null)))).toContain('Skua is not connected.');
    expect(texts(skuaView(model(skua({ connection: 'VersionMismatch' }))))).toContain('Update the Skua plugin and SkuaLink to the same release.');
  });
  it('shows Nothing to show yet. for a connected empty log', () => {
    expect(texts(skuaView(model(skua({ log: [] }))))).toContain('Nothing to show yet.');
  });
  it('uses no style or on* attributes', () => {
    const s = skua({ log: [{ at: '2026-11-11T12:00:00Z', kind: 'x', text: '<b>hi</b>' }] });
    for (const view of [skuaView(model(s)), listView(model(s))]) {
      for (const n of all(view)) for (const name of Object.keys(n.attrs ?? {})) expect(name === 'style' || name.startsWith('on')).toBe(false);
    }
    expect(texts(skuaView(model(s)))).toContain('<b>hi</b>');
    const hostile = withStatus({ character: '<img onerror=x>', quests: [quest(1, '<script>', true)] });
    expect(texts(skuaView(model(hostile)))).toEqual(expect.arrayContaining(['<img onerror=x>', '<script>']));
  });
});
