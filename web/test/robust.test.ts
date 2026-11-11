import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  afterStreamCheck, characterHref, focusKey, isUsableState, keepState, pairFailureText, pairingStep, parseNotice, parseRoute, parseState,
  KEPT_KEY, reconnectDelay, recallKept, streamErrorStep, takePairingToken, wellFormed,
} from '../src/state';
import type { RemoteState } from '../src/types';
import { drawFailedView, h, pairNeededView, type VNode } from '../src/views';

const character = {
  name: 'Alpha', level: 60, live: false, savedAtUtc: '2026-11-09T11:00:00Z', lastLiveUpdateUtc: null, liveUpdates: 0,
  figures: { gold: 1, exp: 1, expToLevel: 2, ac: 1, classPoints: 1, daysPlayed: 1 }, lastKill: null,
};
const snapshot = { contract: 1, capture: { state: 'Listening.', listening: true }, selected: null, characters: [character], factions: [], hardFarms: null };
const notice = { id: 'n1', kind: 'RankUp', character: 'Alpha', title: 'Rank up', body: 'Yokai reached rank 8.', atUtc: '2026-11-09T11:58:00Z' };
const json = (value: unknown) => JSON.stringify(value);
const asState = (snap: unknown) => ({ snapshot: snap, notices: [] }) as unknown as RemoteState;

function texts(node: VNode): string[] {
  return [node.text, ...node.children.flatMap(texts)].filter((t): t is string => t !== undefined);
}

describe('isUsableState', () => {
  it('accepts no snapshot and a complete snapshot', () => {
    expect(isUsableState(asState(null))).toBe(true);
    expect(isUsableState(asState(snapshot))).toBe(true);
    expect(isUsableState(asState({ ...snapshot, hardFarms: { owned: 1, total: 2, missing: ['x'] } }))).toBe(true);
  });
  it.each([
    ['another contract', { ...snapshot, contract: 2 }],
    ['no capture', { ...snapshot, capture: undefined }],
    ['characters not a list', { ...snapshot, characters: {} }],
    ['factions missing', { ...snapshot, factions: undefined }],
    ['a character without figures', { ...snapshot, characters: [{ ...character, figures: null }] }],
    ['a character that is not an object', { ...snapshot, characters: [null] }],
    ['hard farms without a missing list', { ...snapshot, hardFarms: { owned: 1, total: 2 } }],
    ['a snapshot that is a list', []],
  ])('rejects %s', (_name, snap) => expect(isUsableState(asState(snap))).toBe(false));
});

describe('parseState with bad shapes', () => {
  it('rejects a state whose snapshot is unusable', () => {
    expect(parseState(json({ snapshot: { ...snapshot, characters: [{ name: 'x' }] } }))).toBeNull();
    expect(parseState(json({ snapshot: { contract: 1 } }))).toBeNull();
  });
  it('keeps a good state and drops malformed notices', () => {
    const parsed = parseState(json({ snapshot, notices: [notice, { id: 1 }, null, { ...notice, id: 'n2', atUtc: 5 }] }));
    expect(parsed?.snapshot?.characters).toHaveLength(1);
    expect(parsed?.notices.map((n) => n.id)).toEqual(['n1']);
  });
});

describe('parseNotice', () => {
  it('accepts a complete notice', () => expect(parseNotice(json(notice))?.id).toBe('n1'));
  it.each([
    ['not json', '{'],
    ['null', 'null'],
    ['a list', '[]'],
    ['no id', json({ ...notice, id: undefined })],
    ['a numeric title', json({ ...notice, title: 3 })],
    ['no body', json({ ...notice, body: undefined })],
    ['no time', json({ ...notice, atUtc: undefined })],
  ])('rejects %s', (_name, text) => expect(parseNotice(text)).toBeNull());
});

describe('characterHref with a lone surrogate', () => {
  it('does not throw and still gives a character route', () => {
    const href = characterHref('Bad\uD800Name');
    expect(href).toBe('#/c/Bad%EF%BF%BDName');
    expect(parseRoute(href)).toEqual({ view: 'character', name: 'Bad\uFFFDName' });
  });
});

describe('wellFormed', () => {
  it.each([
    ['plain text', 'Alpha', 'Alpha'],
    ['a whole pair', 'A\uD83D\uDE00B', 'A\uD83D\uDE00B'],
    ['a lone high surrogate', 'A\uD800B', 'A\uFFFDB'],
    ['a lone low surrogate', 'A\uDC00B', 'A\uFFFDB'],
    ['a pair in the wrong order', '\uDC00\uD800', '\uFFFD\uFFFD'],
    ['a high surrogate at the end', 'A\uD800', 'A\uFFFD'],
    ['a low surrogate at the start', '\uDE00A', '\uFFFDA'],
    ['two high surrogates before a low one', '\uD800\uD83D\uDE00', '\uFFFD\uD83D\uDE00'],
    ['nothing', '', ''],
  ])('%s', (_name, input, output) => expect(wellFormed(input)).toBe(output));
});

// Safari before 16.4 cannot even parse a regex lookbehind: one in any shipped file stops the whole script from loading there.
describe('no regex lookbehind', () => {
  const src = fileURLToPath(new URL('../src', import.meta.url));
  const walk = (dir: string): string[] => readdirSync(dir).flatMap((n) => {
    const path = join(dir, n);
    return statSync(path).isDirectory() ? walk(path) : /\.(ts|js)$/.test(path) ? [path] : [];
  });
  it.each(walk(src).map((f) => [f.slice(src.length + 1), f]))('%s has no (?< group', (_name, file) => {
    expect(readFileSync(file as string, 'utf8')).not.toContain('(?<');
  });
});

describe('the kept state', () => {
  it('is stored under its own versioned key', () => expect(KEPT_KEY).toBe('qkp-last-state-v1'));
  const NOW = Date.parse('2026-11-09T12:00:00Z');
  const state = asState(snapshot);
  it('comes back with the time it was received', () => {
    expect(recallKept(keepState(state, NOW - 60_000), NOW)).toEqual({ state, receivedAtMs: NOW - 60_000 });
  });
  it('comes back without a time when the stored time is missing, broken or in the future', () => {
    for (const receivedAtMs of [undefined, null, 'x', -1, 0, Number.MAX_VALUE, NOW + 3600_000]) {
      expect(recallKept(json({ receivedAtMs, state }), NOW)).toEqual({ state, receivedAtMs: null });
    }
  });
  it.each([
    ['not json', '{'],
    ['null', 'null'],
    ['a list', '[]'],
    ['no state', json({ receivedAtMs: NOW })],
    ['a state that is only text', json({ receivedAtMs: NOW, state: 'x' })],
    ['an unusable snapshot', json({ receivedAtMs: NOW, state: { snapshot: { contract: 2 }, notices: [] } })],
  ])('is refused when it is %s', (_name, text) => expect(recallKept(text, NOW)).toBeNull());
});

describe('a failing event stream', () => {
  it('is left to the browser while it retries by itself', () => expect(streamErrorStep(false)).toBe('wait'));
  it('is checked against the state once the browser gave up (any non-200 answer, such as the 503 for a ninth stream)', () => {
    expect(streamErrorStep(true)).toBe('check');
  });
  it('ends in "not paired" only when the PC says so (401); anything else reconnects with backoff', () => {
    expect(afterStreamCheck('unpaired')).toBe('unpaired');
    expect(afterStreamCheck('paired')).toBe('reconnect');
    expect(afterStreamCheck('failed')).toBe('reconnect');
  });
});

describe('takePairingToken', () => {
  it('takes the token and gives the hash to leave behind', () => {
    expect(takePairingToken('#t=abc_DEF-1')).toEqual({ token: 'abc_DEF-1', cleanHash: '' });
  });
  it.each(['', '#', '#/', '#/c/Alpha', '#tx=1'])('ignores %s', (hash) => expect(takePairingToken(hash)).toBeNull());
});

describe('pairFailureText', () => {
  it('explains each outcome that did not pair', () => {
    expect(pairFailureText('locked')).toBe('Too many attempts. Wait a minute and open the pairing link again.');
    expect(pairFailureText('invalid')).toBe('The pairing link was not accepted. Create a new one in Questkeeper on your PC.');
    expect(pairFailureText('failed')).toBe('The pairing link was not accepted. Create a new one in Questkeeper on your PC.');
    expect(pairFailureText('paired')).toBeNull();
  });
});

describe('pairingStep', () => {
  it('spends the pairing token only when the PC does not know this browser', () => {
    expect(pairingStep('unpaired')).toBe('pair');
    expect(pairingStep('paired')).toBe('continue');
    expect(pairingStep('failed')).toBe('continue');
  });
});

describe('reconnectDelay', () => {
  it('backs off 2, 4, 8 then 15 seconds at most', () => {
    expect([0, 1, 2, 3, 4, 10].map(reconnectDelay)).toEqual([2000, 4000, 8000, 15000, 15000, 15000]);
  });
});

describe('focusKey', () => {
  it('keys buttons by action, links by href and the details summary by itself', () => {
    expect(focusKey({ tag: 'BUTTON', action: 'toggle-all', href: null })).toBe('action:toggle-all');
    expect(focusKey({ tag: 'A', action: null, href: '#/c/Alpha' })).toBe('href:#/c/Alpha');
    expect(focusKey({ tag: 'SUMMARY', action: null, href: null })).toBe('summary');
  });
  it('keys a text field by its id, so focus stays in the pairing link field after a redraw', () => {
    expect(focusKey({ tag: 'INPUT', action: null, href: null, id: 'pair-link' })).toBe('id:pair-link');
    expect(focusKey({ tag: 'INPUT', action: null, href: null, id: '' })).toBeNull();
    expect(focusKey({ tag: 'INPUT', action: null, href: null })).toBeNull();
  });
  it('has no key for anything else', () => {
    expect(focusKey({ tag: 'DIV', action: null, href: null })).toBeNull();
    expect(focusKey({ tag: 'A', action: null, href: null })).toBeNull();
  });
});

describe('fallback views', () => {
  it('says when the data could not be shown', () => {
    expect(texts(drawFailedView())).toContain('Something went wrong showing this data');
  });
  it('adds the reason a pairing link failed', () => {
    const t = texts(pairNeededView('Too many attempts. Wait a minute and open the pairing link again.'));
    expect(t).toContain('Too many attempts. Wait a minute and open the pairing link again.');
    expect(t.some((x) => x.startsWith('On your PC, open Questkeeper'))).toBe(true);
    expect(texts(pairNeededView())).not.toContain('Too many attempts. Wait a minute and open the pairing link again.');
  });
  it('adds the install hint to the "Not paired" card when there is one', () => {
    expect(texts(pairNeededView(null, 'Install: tap Share, then Add to Home Screen.'))).toContain('Install: tap Share, then Add to Home Screen.');
  });
  it('h still drops falsy children', () => expect(h('p', {}, null).children).toHaveLength(0));
});
