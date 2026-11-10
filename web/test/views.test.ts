import { describe, expect, it } from 'vitest';
import { characterView, h, listView, pairNeededView, type Model, type VNode } from '../src/views';
import type { Character, RemoteState } from '../src/types';

const NOW = Date.parse('2026-11-09T12:00:00Z');
const iso = (secondsAgo: number) => new Date(NOW - secondsAgo * 1000).toISOString();

const alpha: Character = {
  name: 'Alpha <b>Hero</b>', level: 82, live: true, savedAtUtc: iso(60), lastLiveUpdateUtc: iso(5), liveUpdates: 3,
  figures: { gold: 93650135, exp: 1609986, expToLevel: 5590000, ac: 200, classPoints: 302500, daysPlayed: 8 }, lastKill: { gold: 625, exp: 1250 },
};
const beta: Character = { ...alpha, name: 'Beta', live: false, lastLiveUpdateUtc: null, lastKill: null };

function state(over: Partial<RemoteState['snapshot'] & object> = {}): RemoteState {
  return {
    snapshot: {
      contract: 1, capture: { state: 'Listening.', listening: true }, selected: alpha.name, characters: [alpha, beta],
      factions: [
        { name: 'Yokai', rank: 8, rep: 1, toNext: 9900, fraction: 0.86, max: false },
        { name: 'Good', rank: 10, rep: 2, toNext: 0, fraction: 1, max: true },
      ],
      hardFarms: { owned: 1, total: 40, missing: Array.from({ length: 30 }, (_, i) => `Item ${i}`) }, ...over,
    },
    notices: [{ id: 'n1', kind: 'RankUp', character: alpha.name, title: 'Rank up', body: 'Yokai reached rank 8.', atUtc: iso(120) }],
  };
}

const model = (s: RemoteState | null, over: Partial<Model> = {}): Model => ({ state: s, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, ...over });

function all(node: VNode): VNode[] {
  return [node, ...node.children.flatMap(all)];
}
const texts = (node: VNode) => all(node).map((n) => n.text).filter((t): t is string => t !== undefined);
const byClass = (node: VNode, cls: string) => all(node).filter((n) => n.className === cls);

describe('list view', () => {
  it('has one link row per character, with the name as text and an encoded href', () => {
    const rows = byClass(listView(model(state())), 'row');
    expect(rows).toHaveLength(2);
    expect(rows[0]!.attrs!.href).toBe('#/c/Alpha%20%3Cb%3EHero%3C%2Fb%3E');
    expect(texts(rows[0]!)).toContain('Alpha <b>Hero</b>');
  });
  it('shows live status as a word, not only a dot', () => {
    expect(texts(listView(model(state())))).toContain('Live - activity just now');
  });
  it('says what to do when no character has logged in', () => {
    expect(texts(listView(model(null)))).toContain('No characters yet. Log a character in while Questkeeper is listening.');
  });
  it('lists recent notices with their age', () => {
    const t = texts(listView(model(state())));
    expect(t).toContain('Rank up');
    expect(t).toContain('Yokai reached rank 8.');
    expect(t.some((x) => x.endsWith('2 min ago'))).toBe(true);
  });
  it('says nothing has happened when there are no notices', () => {
    expect(texts(listView(model({ ...state(), notices: [] })))).toContain('Nothing has happened yet.');
  });
  it('ends with the install hint when there is one, and only then', () => {
    const hint = 'Install: tap Share, then Add to Home Screen.';
    const view = listView(model(state(), { installHint: hint }));
    const last = view.children[view.children.length - 1]!;
    expect(last).toMatchObject({ className: 'note', text: hint });
    expect(texts(listView(model(state())))).not.toContain(hint);
    expect(texts(listView(model(null, { installHint: hint })))).toContain(hint);
  });
});

describe('character view', () => {
  it('shows the figures with grouped digits and an experience bar', () => {
    const view = characterView(model(state()), alpha.name);
    const t = texts(view);
    expect(t).toContain('93,650,135');
    expect(t).toContain('1,609,986 / 5,590,000');
    expect(t).toContain('625 gold, 1,250 exp');
    const bar = byClass(view, 'bar')[0]!;
    expect(bar.attrs!.role).toBe('progressbar');
    expect(bar.attrs!['aria-valuenow']).toBe('29');
  });
  it('shows factions and Hard Farms only for the selected character, and says why otherwise', () => {
    const selected = texts(characterView(model(state()), alpha.name));
    expect(selected).toContain('Yokai');
    expect(selected).toContain('Max rank');
    expect(selected).toContain('1 of 40 owned');
    const other = texts(characterView(model(state()), 'Beta'));
    expect(other).not.toContain('Yokai');
    expect(other.some((x) => x.startsWith('Factions and Hard Farms are shown for the character selected in Questkeeper'))).toBe(true);
  });
  it('keeps the missing list collapsed and offers the rest on request', () => {
    const view = characterView(model(state()), alpha.name);
    expect(byClass(view, 'missing')[0]!.children).toHaveLength(20);
    expect(all(view).some((n) => n.attrs?.['data-action'] === 'toggle-all' && n.text === 'Show 10 more')).toBe(true);
    const full = characterView(model(state(), { showAll: true }), alpha.name);
    expect(byClass(full, 'missing')[0]!.children).toHaveLength(30);
    expect(all(full).some((n) => n.attrs?.['data-action'] === 'toggle-all' && n.text === 'Show fewer')).toBe(true);
  });
  it('says so when Hard Farms are not loaded', () => {
    expect(texts(characterView(model(state({ hardFarms: null })), alpha.name))).toContain('Hard Farms are not loaded yet. Import the list in Questkeeper.');
  });
  it('credits the AQW Wiki and its licence in a muted line of the Hard Farms card', () => {
    const credit = all(characterView(model(state()), alpha.name)).find((n) => n.text === 'The Hard Farms list comes from the AQW Wiki (CC BY-SA 3.0).');
    expect(credit?.className).toBe('muted');
  });
  it('says so when the character is not logged in', () => {
    expect(texts(characterView(model(state()), 'Nobody'))).toContain('This character is not logged in right now.');
  });
});

describe('pairing view', () => {
  it('tells the player how to pair', () => {
    expect(texts(pairNeededView())).toContain('On your PC, open Questkeeper, then Settings, Phone access, "Pair a phone...", and scan the code or open the link here.');
  });
});

describe('h', () => {
  it('drops falsy children', () => {
    expect(h('div', {}, null, false, h('p')).children).toHaveLength(1);
  });
});
