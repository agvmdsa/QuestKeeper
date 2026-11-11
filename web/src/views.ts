import { characterHref, characterRow, characterRows, expFraction, formatNumber, hardFarmsView, noticeLine } from './state';
import { skuaEmptyText, skuaHeader, skuaLogRows, skuaSummary, SKUA_TITLE } from './skua';
import type { Faction, RemoteState } from './types';

export interface VNode {
  tag: string;
  className?: string;
  text?: string;
  attrs?: Record<string, string>;
  fraction?: number;
  children: VNode[];
}

export interface Model {
  state: RemoteState | null;
  nowMs: number;
  showAll: boolean;
  hfOpen: boolean;
  /** How to install the page on an iPhone or iPad, or null. */
  installHint: string | null;
}

type Child = VNode | null | false;

export function h(tag: string, props: Omit<Partial<VNode>, 'tag' | 'children'> = {}, ...children: Child[]): VNode {
  return { tag, ...props, children: children.filter((c): c is VNode => Boolean(c)) };
}

export function mount(node: VNode): HTMLElement {
  const el = document.createElement(node.tag);
  if (node.className) el.className = node.className;
  if (node.text !== undefined) el.textContent = node.text;
  for (const [name, value] of Object.entries(node.attrs ?? {})) {
    if (name === 'style' || name.startsWith('on')) throw new Error(`The attribute ${name} is not allowed.`);
    el.setAttribute(name, value);
  }
  if (node.fraction !== undefined) el.style.setProperty('--fraction', String(node.fraction));
  for (const child of node.children) el.append(mount(child));
  return el;
}

const back = (): VNode => h('a', { className: 'back', text: 'Back to characters', attrs: { href: '#/' } });

const empty = (text: string): VNode => h('p', { className: 'empty', text });

function bar(label: string, fraction: number): VNode {
  return h(
    'div',
    {
      className: 'bar',
      attrs: { role: 'progressbar', 'aria-label': label, 'aria-valuemin': '0', 'aria-valuemax': '100', 'aria-valuenow': String(Math.round(fraction * 100)) },
    },
    h('div', { className: 'bar-fill', fraction }),
  );
}

function status(kind: string, text: string): VNode {
  return h('p', { className: 'status' }, h('span', { className: 'dot', attrs: { 'data-kind': kind, 'aria-hidden': 'true' } }), h('span', { text }));
}

function fact(label: string, value: string): VNode[] {
  return [h('dt', { text: label }), h('dd', { text: value })];
}

export function listView(model: Model): VNode {
  const rows = characterRows(model.state, model.nowMs);
  const notices = model.state?.notices ?? [];
  return h(
    'div',
    {},
    h(
      'section',
      { className: 'card' },
      h('h2', { text: 'Characters' }),
      rows.length === 0
        ? empty('No characters yet. Log a character in while Questkeeper is listening.')
        : h(
            'ul',
            { className: 'list' },
            ...rows.map((r) =>
              h(
                'li',
                {},
                h(
                  'a',
                  { className: 'row', attrs: { href: characterHref(r.name) } },
                  h('span', { className: 'title' }, h('span', { className: 'name', text: r.name }), h('span', { className: 'level', text: `Lv ${r.level}` })),
                  h('span', { className: 'gold', text: `${r.gold} gold` }),
                  h('span', { className: 'row-status' }, status(r.kind, r.status)),
                ),
              ),
            ),
          ),
    ),
    model.state?.skua
      ? h(
          'section',
          { className: 'card' },
          h('h2', { text: SKUA_TITLE }),
          h('a', { className: 'row', attrs: { href: '#/skua' } }, h('span', { className: 'name', text: skuaSummary(model.state.skua) })),
        )
      : null,
    h(
      'section',
      { className: 'card' },
      h('h2', { text: 'Recent notices' }),
      notices.length === 0
        ? empty('Nothing has happened yet.')
        : h(
            'ul',
            { className: 'notices' },
            ...notices.slice(0, 10).map((n) => {
              const line = noticeLine(n, model.nowMs);
              return h(
                'li',
                {},
                h('p', { className: 'notice-title', text: line.title }),
                h('p', { text: line.body }),
                h('p', { className: 'muted', text: n.character ? `${n.character} - ${line.when}` : line.when }),
              );
            }),
          ),
    ),
    model.state?.snapshot ? h('p', { className: 'note', text: `Questkeeper: ${model.state.snapshot.capture.state}` }) : null,
    model.installHint ? h('p', { className: 'note', text: model.installHint }) : null,
  );
}

export function skuaView(model: Model): VNode {
  const skua = model.state?.skua ?? null;
  const header = skua ? skuaHeader(skua, model.nowMs) : null;
  const rows = skua && skua.connection === 'Connected' ? skuaLogRows(skua, model.nowMs) : [];
  const emptyText = skuaEmptyText(skua);
  return h(
    'div',
    {},
    back(),
    h('section', { className: 'card' }, h('h2', { text: header?.title ?? SKUA_TITLE }), ...(header?.lines ?? []).map((line) => h('p', { className: 'skua-line', text: line }))),
    h(
      'section',
      { className: 'card' },
      h('h2', { text: 'Log' }),
      emptyText
        ? empty(emptyText)
        : h('ul', { className: 'skua-log' }, ...rows.map((r) => h('li', {}, h('p', { text: r.text }), r.when ? h('p', { className: 'muted', text: r.when }) : null))),
    ),
  );
}

function factionRow(f: Faction): VNode {
  return h(
    'li',
    {},
    h('p', { className: 'faction' }, h('span', { className: 'name', text: f.name }), h('span', { className: 'muted', text: f.max ? 'Max rank' : `Rank ${f.rank}` })),
    bar(f.name, f.fraction),
    f.max ? null : h('p', { className: 'muted', text: `${formatNumber(f.toNext)} to next rank` }),
  );
}

export function characterView(model: Model, name: string): VNode {
  const snapshot = model.state?.snapshot;
  const character = snapshot?.characters.find((c) => c.name === name);
  if (!snapshot || !character) {
    return h('div', {}, back(), h('section', { className: 'card' }, empty('This character is not logged in right now.')));
  }

  const row = characterRow(character, model.nowMs);
  const f = character.figures;
  const kill = character.lastKill;
  const figures = h(
    'section',
    { className: 'card' },
    h('h2', { text: character.name }),
    status(row.kind, row.status),
    h(
      'dl',
      { className: 'figures' },
      ...fact('Level', String(character.level)),
      ...fact('Gold', formatNumber(f.gold)),
      ...fact('Experience', `${formatNumber(f.exp)} / ${formatNumber(f.expToLevel)}`),
      ...fact('Armor class', String(f.ac)),
      ...fact('Class points', formatNumber(f.classPoints)),
      ...fact('Days played', String(f.daysPlayed)),
      ...fact('Last kill', kill ? `${formatNumber(kill.gold)} gold, ${formatNumber(kill.exp)} exp` : 'None seen'),
    ),
    bar('Experience to next level', expFraction(f)),
  );

  if (snapshot.selected !== name) {
    const note = snapshot.selected
      ? `Factions and Hard Farms are shown for the character selected in Questkeeper (${snapshot.selected}).`
      : 'Select this character in Questkeeper to see its factions and Hard Farms.';
    return h('div', {}, back(), figures, h('p', { className: 'note', text: note }));
  }

  const hf = hardFarmsView(snapshot.hardFarms, model.showAll);
  const missingCount = hf ? hf.shown.length + hf.hidden : 0;
  return h(
    'div',
    {},
    back(),
    figures,
    h(
      'section',
      { className: 'card' },
      h('h2', { text: 'Factions' }),
      snapshot.factions.length === 0 ? empty('No factions read yet.') : h('ul', { className: 'list' }, ...snapshot.factions.map(factionRow)),
    ),
    h(
      'section',
      { className: 'card' },
      h('h2', { text: 'Hard Farms' }),
      hf === null
        ? empty('Hard Farms are not loaded yet. Import the list in Questkeeper.')
        : h(
            'div',
            {},
            h('p', { text: `${hf.owned} of ${hf.total} owned` }),
            bar('Hard Farms owned', hf.fraction),
            hf.shown.length + hf.hidden === 0
              ? null
              : h(
                  'details',
                  { attrs: model.hfOpen ? { open: '' } : {} },
                  h('summary', { text: `Missing (${missingCount})` }),
                  h('ul', { className: 'missing' }, ...hf.shown.map((item) => h('li', { text: item }))),
                  hf.hidden > 0 ? h('button', { text: `Show ${hf.hidden} more`, attrs: { type: 'button', 'data-action': 'toggle-all' } }) : null,
                  model.showAll && missingCount > 20 ? h('button', { text: 'Show fewer', attrs: { type: 'button', 'data-action': 'toggle-all' } }) : null,
                ),
            h('p', { className: 'muted', text: 'The Hard Farms list comes from the AQW Wiki (CC BY-SA 3.0).' }),
          ),
    ),
  );
}

export function pairNeededView(reason: string | null = null, installHint: string | null = null): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: 'Not paired' }),
    reason ? h('p', { className: 'reason', text: reason, attrs: { role: 'status' } }) : null,
    h('p', {
      text: 'On your PC, open Questkeeper, then Settings, "Watch my characters from my phone", "Pair a phone...", and scan the code or open the link here.',
    }),
    h(
      'div',
      { className: 'paste' },
      h('p', {
        className: 'muted',
        text: 'Using Questkeeper from your Home Screen? The code opens the browser instead. Copy the pairing link (Copy link in Questkeeper on your PC) and paste it here.',
      }),
      h('label', { text: 'Pairing link', attrs: { for: 'pair-link' } }),
      h('input', { attrs: { id: 'pair-link', type: 'text', autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'go' } }),
      h('button', { text: 'Pair', attrs: { type: 'button', 'data-action': 'pair-paste' } }),
    ),
    installHint ? h('p', { className: 'muted hint', text: installHint }) : null,
  );
}

export function drawFailedView(): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: 'Something went wrong showing this data' }),
    h('p', { className: 'muted', text: 'The page will try again when new data arrives.' }),
  );
}
