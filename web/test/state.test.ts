import { describe, expect, it } from 'vitest';
import {
  characterHref, characterRow, characterRows, deviceName, expFraction, formatCompact, formatNumber,
  hardFarmsView, iosInstallHint, linkBanner, noticeLine, parseRoute, parseState, relativeTime,
} from '../src/state';
import type { Character, RemoteState } from '../src/types';

const NOW = Date.parse('2026-11-09T12:00:00Z');
const ago = (seconds: number) => new Date(NOW - seconds * 1000).toISOString();

function hero(over: Partial<Character> = {}): Character {
  return {
    name: 'Alpha', level: 60, live: false, savedAtUtc: ago(3 * 3600), lastLiveUpdateUtc: null, liveUpdates: 0,
    figures: { gold: 93643759, exp: 1609986, expToLevel: 5590000, ac: 200, classPoints: 9, daysPlayed: 8 }, lastKill: null, ...over,
  };
}

function stateWith(characters: Character[]): RemoteState {
  return {
    snapshot: { contract: 1, capture: { state: 'x', listening: true }, selected: null, characters, factions: [], hardFarms: null },
    notices: [],
  };
}

describe('parseState', () => {
  it('parses a state and defaults missing notices', () => {
    expect(parseState('{"snapshot":null}')?.notices).toEqual([]);
  });
  it('no longer needs the link of the Remote', () => expect(parseState('{"snapshot":null,"notices":[]}')).toEqual({ snapshot: null, notices: [], skua: null }));
  it.each(['{', '[]', 'null', '"text"', '{}', '{"notices":[]}'])('rejects %s', (text) => expect(parseState(text)).toBeNull());
});

describe('numbers', () => {
  it.each([[0, '0'], [999, '999'], [1234567, '1,234,567'], [93643759, '93,643,759']])('formatNumber(%d)', (n, s) => expect(formatNumber(n)).toBe(s));
  it.each([
    [999, '999'], [1250, '1.3K'], [1000000, '1M'], [93643759, '93.6M'], [2100000000, '2.1B'], [999999, '1M'], [999499, '999.5K'],
  ])('formatCompact(%d)', (n, s) => expect(formatCompact(n)).toBe(s));
  it('expFraction', () => {
    expect(expFraction({ ...hero().figures, exp: 1609986, expToLevel: 5590000 })).toBeCloseTo(0.288, 3);
    expect(expFraction({ ...hero().figures, exp: 5, expToLevel: 0 })).toBe(0);
    expect(expFraction({ ...hero().figures, exp: 9, expToLevel: 3 })).toBe(1);
  });
});

describe('relativeTime', () => {
  it.each([
    [10, 'just now'], [44, 'just now'], [45, '1 min ago'], [150, '2 min ago'], [3599, '59 min ago'],
    [3600, '1 h ago'], [86399, '23 h ago'], [86400, '1 d ago'], [3 * 86400, '3 d ago'], [-30, 'just now'],
  ])('%d seconds ago', (s, text) => expect(relativeTime(ago(s), NOW)).toBe(text));
  it('gives nothing for an unparsable date', () => expect(relativeTime('nope', NOW)).toBe(''));
});

describe('linkBanner', () => {
  it('says nothing while the stream is open', () => {
    expect(linkBanner(true, NOW - 180_000, NOW)).toBeNull();
    expect(linkBanner(true, null, NOW)).toBeNull();
  });
  it('says the data is old, and how old, when the stream is closed', () => {
    expect(linkBanner(false, NOW - 180_000, NOW)).toEqual({ tone: 'warn', text: 'Old data - last update 3 min ago. Reconnecting...' });
    expect(linkBanner(false, NOW - 2 * 3600_000, NOW)).toEqual({ tone: 'warn', text: 'Old data - last update 2 h ago. Reconnecting...' });
  });
  it('leaves the age out when no update time is known', () => {
    expect(linkBanner(false, null, NOW)).toEqual({ tone: 'warn', text: 'Old data - reconnecting...' });
    expect(linkBanner(false, Number.NaN, NOW)).toEqual({ tone: 'warn', text: 'Old data - reconnecting...' });
    expect(linkBanner(false, null, NOW, true)).toEqual({ tone: 'warn', text: 'Old data - reconnecting...' });
  });
  it('only says "Connecting..." in a calm tone on a first visit, when no data was ever shown', () => {
    expect(linkBanner(false, null, NOW, false)).toEqual({ tone: 'neutral', text: 'Connecting...' });
    expect(linkBanner(true, null, NOW, false)).toBeNull();
  });
});

describe('iosInstallHint', () => {
  const HINT = 'Install: tap Share, then Add to Home Screen.';
  const IPHONE = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1';
  const IPAD = 'Mozilla/5.0 (iPad; CPU OS 16_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1';
  it('tells iPhone and iPad users how to install the page', () => {
    expect(iosInstallHint(IPHONE, false)).toBe(HINT);
    expect(iosInstallHint(IPAD, false)).toBe(HINT);
  });
  it('counts an iPad that reports itself as a Mac (iPadOS 13 and later) by its touch screen', () => {
    const MAC = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15';
    expect(iosInstallHint(MAC, false, 5)).toBe(HINT);
    expect(iosInstallHint(MAC, true, 5)).toBeNull();
    expect(iosInstallHint(MAC, false, 1)).toBeNull();
    expect(iosInstallHint(MAC, false, 0)).toBeNull();
    expect(iosInstallHint(MAC, false)).toBeNull();
  });
  it('says nothing once the page runs from the Home Screen', () => {
    expect(iosInstallHint(IPHONE, true)).toBeNull();
    expect(iosInstallHint(IPAD, true)).toBeNull();
  });
  it.each([
    ['Android', 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Mobile Safari/537.36'],
    ['Windows', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36'],
    ['macOS', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15'],
    ['empty', ''],
  ])('says nothing on %s', (_name, ua) => expect(iosInstallHint(ua, false)).toBeNull());
});

describe('character rows', () => {
  it('a live character with recent activity is live', () => {
    const row = characterRow(hero({ live: true, lastLiveUpdateUtc: ago(5) }), NOW);
    expect(row).toMatchObject({ kind: 'live', status: 'Live - activity just now', gold: '93.6M' });
  });
  it('a live character quiet for two minutes or more is idle', () => {
    expect(characterRow(hero({ live: true, lastLiveUpdateUtc: ago(12 * 60) }), NOW)).toMatchObject({ kind: 'idle', status: 'Idle - last activity 12 min ago' });
  });
  it('a character that is not live is saved', () => {
    expect(characterRow(hero(), NOW)).toMatchObject({ kind: 'saved', status: 'Saved 3 h ago' });
  });
  it('keeps the snapshot order and handles no state', () => {
    expect(characterRows(stateWith([hero({ name: 'B' }), hero({ name: 'A' })]), NOW).map((r) => r.name)).toEqual(['B', 'A']);
    expect(characterRows(null, NOW)).toEqual([]);
  });
});

describe('hardFarmsView', () => {
  const missing = Array.from({ length: 30 }, (_, i) => `Item ${i}`);
  it('is null without data', () => expect(hardFarmsView(null, false)).toBeNull());
  it('shows the first 20 and counts the rest', () => {
    const v = hardFarmsView({ owned: 10, total: 40, missing }, false)!;
    expect(v.shown).toHaveLength(20);
    expect(v.hidden).toBe(10);
    expect(v.fraction).toBe(0.25);
  });
  it('shows all on request and survives a zero total', () => {
    expect(hardFarmsView({ owned: 0, total: 40, missing }, true)).toMatchObject({ hidden: 0 });
    expect(hardFarmsView({ owned: 0, total: 0, missing: [] }, false)!.fraction).toBe(0);
  });
});

describe('notices, routes and device names', () => {
  it('noticeLine', () => {
    expect(noticeLine({ id: '1', kind: 'RankUp', character: 'A', title: 'Rank up', body: 'Yokai reached rank 9.', atUtc: ago(120) }, NOW))
      .toEqual({ title: 'Rank up', body: 'Yokai reached rank 9.', when: '2 min ago' });
  });
  it('routes', () => {
    expect(parseRoute('')).toEqual({ view: 'list' });
    expect(parseRoute('#/')).toEqual({ view: 'list' });
    expect(parseRoute('#/c/Alpha%20Hero')).toEqual({ view: 'character', name: 'Alpha Hero' });
    expect(parseRoute('#/c/%E0%A4%A')).toEqual({ view: 'list' });
    expect(parseRoute('#t=abc_DEF-1')).toEqual({ view: 'pair', token: 'abc_DEF-1' });
    expect(characterHref('Alpha Hero')).toBe('#/c/Alpha%20Hero');
  });
  it.each([
    ['Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)', 'iPhone'], ['Mozilla/5.0 (iPad; CPU OS 17_0)', 'iPad'],
    ['Mozilla/5.0 (Linux; Android 14; Pixel 7)', 'Android phone'], ['Mozilla/5.0 (Windows NT 10.0)', 'Browser'], ['', 'Browser'],
  ])('deviceName(%s)', (ua, name) => expect(deviceName(ua)).toBe(name));
});
