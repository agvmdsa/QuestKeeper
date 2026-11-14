import { characterHref, characterRow, characterRows, expFraction, formatNumber, hardFarmsView, noticeLine } from './state';
import { plural, t } from './i18n';
import { effectiveFactions, factionRows, instanceState, LOG_FILTERS, LOG_FILTER_LABELS, questRows, selectInstance, skuaEmptyText, skuaLogRows, skuaNow, skuaSummary, type LogFilter } from './skua';
import { THEMES, type ThemeChoice } from './theme';
import type { Faction, RemoteState, SkuaInstance, SkuaState } from './types';

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
  /** The stored appearance choice; System when absent. */
  theme?: ThemeChoice;
  /** The selected Skua log filter; Everything when absent. */
  skuaFilter?: LogFilter;
  /** The label (character name) of the Skua last chosen, or null; the first connected one when absent or gone. */
  skuaInstance?: string | null;
  /** The instance chosen in this session; it wins over the label. */
  skuaInstanceId?: string | null;
  /** Draws the Control card of an instance (loaded with the Skua tab); without it the tab shows no Control card. */
  controlCard?: (i: SkuaInstance) => VNode | null;
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

const back = (): VNode => h('a', { className: 'back', text: t('common.back'), attrs: { href: '#/' } });

const empty = (text: string): VNode => h('p', { className: 'empty', text });

export function bar(label: string, fraction: number): VNode {
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
      h('h2', { text: t('list.title') }),
      rows.length === 0
        ? empty(t('list.empty'))
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
                  h('span', { className: 'title' }, h('span', { className: 'name', text: r.name }), h('span', { className: 'level', text: t('list.level', { level: r.level }) })),
                  h('span', { className: 'gold', text: t('list.gold', { gold: r.gold }) }),
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
          h('h2', { text: t('skua.title') }),
          h('a', { className: 'row', attrs: { href: '#/skua' } }, h('span', { className: 'name', text: skuaSummary(model.state.skua) })),
        )
      : null,
    h(
      'section',
      { className: 'card' },
      h('h2', { text: t('list.notices.title') }),
      notices.length === 0
        ? empty(t('list.notices.empty'))
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
                h('p', { className: 'muted', text: n.character ? t('list.notices.when', { character: n.character, when: line.when }) : line.when }),
              );
            }),
          ),
    ),
    model.state?.snapshot ? h('p', { className: 'note', text: t('list.capture', { state: model.state.snapshot.capture.state }) }) : null,
    model.installHint ? h('p', { className: 'note', text: model.installHint }) : null,
  );
}

function nowCard(skua: SkuaState, nowMs: number): VNode | null {
  const now = skuaNow(skua, nowMs);
  if (!now) return null;
  return h(
    'section',
    { className: 'card now-card' },
    h('h2', { text: t('skua.now.title') }),
    now.loggedIn ? (now.character ? h('p', { className: 'now-name', text: now.character }) : null) : h('p', { className: 'skua-line', text: t('skua.notLoggedIn') }),
    now.where ? h('p', { className: 'skua-line', text: now.where }) : null,
    status(now.script.running ? 'live' : 'idle', now.script.text),
    now.script.name ? h('p', { className: 'skua-line', text: now.script.name }) : null,
  );
}

function questsCard(skua: SkuaState): VNode | null {
  const quests = questRows(skua);
  if (!quests) return null;
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: plural('skua.quests.title', quests.length) }),
    quests.length === 0
      ? empty(t('skua.quests.none'))
      : h('ul', { className: 'quests' }, ...quests.map((q) => h('li', {}, h('p', { className: 'name', text: q.name }), q.ready ? status('ready', t('skua.quests.ready')) : null))),
  );
}

/** The live reputation from the Skua plugin; hidden when the plugin does not report it. */
function reputationCard(skua: SkuaState): VNode | null {
  const rows = factionRows(skua);
  if (!rows) return null;
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('skua.reputation.title') }),
    rows.length === 0 ? empty(t('skua.reputation.none')) : h('ul', { className: 'list' }, ...rows.map(factionRow)),
  );
}

function filterRow(selected: LogFilter): VNode {
  return h(
    'div',
    { className: 'filters', attrs: { role: 'group', 'aria-label': t('skua.filter.group') } },
    ...LOG_FILTERS.map((filter) =>
      h('button', {
        className: 'filter-btn',
        text: t(LOG_FILTER_LABELS[filter]),
        attrs: { type: 'button', 'data-action': 'skua-filter', 'data-value': filter, 'aria-pressed': String(filter === selected) },
      }),
    ),
  );
}

function logCard(skua: SkuaState, model: Model): VNode {
  const filter = model.skuaFilter ?? 'all';
  const rows = skuaLogRows(skua, model.nowMs, filter);
  const emptyText = skuaEmptyText(skua, filter);
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('skua.log.title') }),
    filterRow(filter),
    emptyText
      ? empty(emptyText)
      : h('ul', { className: 'skua-log' }, ...rows.map((r) => h('li', {}, h('p', { text: r.text }), r.when ? h('p', { className: 'muted', text: r.when }) : null))),
  );
}

const MANY_INSTANCES = 5;

function instanceLabel(i: SkuaInstance, selected: boolean, role: 'chip' | 'list'): VNode {
  const st = instanceState(i);
  const common = { 'data-action': 'skua-instance', 'data-value': i.instanceId, type: 'button' };
  const inner = [
    h('span', { className: 'chip-name', text: i.label }),
    h('span', { className: 'chip-state' }, h('span', { className: 'dot', attrs: { 'data-kind': st.tone === 'run' ? 'live' : st.tone === 'warn' ? 'idle' : 'saved', 'aria-hidden': 'true' } }), h('span', { text: st.text })),
  ];
  const off = i.disconnected || i.connection === 'NotConnected';
  if (role === 'list') {
    return h('li', {}, h('button', { className: off ? ['inst-btn', 'off'].join(' ') : 'inst-btn', attrs: { ...common, 'aria-pressed': String(selected) } }, ...inner));
  }
  return h('button', { className: off ? ['chip', 'off'].join(' ') : 'chip', attrs: { ...common, role: 'tab', 'aria-selected': String(selected), tabindex: selected ? '0' : '-1' } }, ...inner);
}

/** Which Skua the tab shows: nothing for one, a chip row for two to five, a list of buttons above that. */
function instanceSelector(instances: SkuaInstance[], selected: SkuaInstance): VNode | null {
  if (instances.length < 2) return null;
  if (instances.length > MANY_INSTANCES) {
    return h('ul', { className: 'inst-list', attrs: { 'aria-label': t('skua.instances.label') } }, ...instances.map((i) => instanceLabel(i, i === selected, 'list')));
  }
  return h(
    'div',
    { className: 'chip-wrap' },
    h('div', { className: 'chips', attrs: { role: 'tablist', 'aria-label': t('skua.instances.label') } }, ...instances.map((i) => instanceLabel(i, i === selected, 'chip'))),
  );
}

export function skuaView(model: Model): VNode {
  const instances = model.state?.skuaInstances ?? [];
  const chosen = selectInstance(instances, model.skuaInstance ?? null, model.skuaInstanceId ?? null);
  // An older app sends no list: the single Skua is shown as before, with no selector.
  const skua: SkuaState | null = chosen ?? model.state?.skua ?? null;
  const selector = chosen ? instanceSelector(instances, chosen) : null;
  // A disconnected instance keeps its last status, which must not be shown as live.
  const live = skua && chosen?.disconnected ? { ...skua, connection: 'NotConnected' as const } : skua;
  if (!live || live.connection !== 'Connected') {
    return h('div', {}, back(), selector, h('section', { className: 'card' }, h('h2', { text: t('skua.title') }), empty(skuaEmptyText(live))));
  }
  const control = chosen && model.controlCard ? model.controlCard(chosen) : null;
  return h('div', {}, back(), selector, nowCard(live, model.nowMs), control, questsCard(live), reputationCard(live), logCard(live, model));
}

/** One faction: from the capture (Faction) or from Skua (FactionRow), which carry the same fields. */
function factionRow(f: Pick<Faction, 'name' | 'rank' | 'max' | 'fraction' | 'toNext'>): VNode {
  return h(
    'li',
    {},
    h('p', { className: 'faction' }, h('span', { className: 'name', text: f.name }), h('span', { className: 'muted', text: f.max ? t('factions.maxRank') : t('factions.rank', { rank: f.rank }) })),
    bar(f.name, f.fraction),
    f.max ? null : h('p', { className: 'muted', text: t('factions.toNext', { n: formatNumber(f.toNext) }) }),
  );
}

export function characterView(model: Model, name: string): VNode {
  const snapshot = model.state?.snapshot;
  const character = snapshot?.characters.find((c) => c.name === name);
  if (!snapshot || !character) {
    return h('div', {}, back(), h('section', { className: 'card' }, empty(t('character.notLoggedIn'))));
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
      ...fact(t('character.level'), String(character.level)),
      ...fact(t('character.gold'), formatNumber(f.gold)),
      ...fact(t('character.exp'), t('character.expOf', { exp: formatNumber(f.exp), toLevel: formatNumber(f.expToLevel) })),
      ...fact(t('character.ac'), String(f.ac)),
      ...fact(t('character.classPoints'), formatNumber(f.classPoints)),
      ...fact(t('character.daysPlayed'), String(f.daysPlayed)),
      ...fact(t('character.lastKill'), kill ? t('character.lastKill.value', { gold: formatNumber(kill.gold), exp: formatNumber(kill.exp) }) : t('character.lastKill.none')),
    ),
    bar(t('character.expBar'), expFraction(f)),
  );

  if (snapshot.selected !== name) {
    const note = snapshot.selected
      ? t('character.note.selectedElsewhere', { selected: snapshot.selected })
      : t('character.note.selectHere');
    return h('div', {}, back(), figures, h('p', { className: 'note', text: note }));
  }

  const reputation = effectiveFactions(model.state!);
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
      h('h2', { text: t('factions.title') }),
      reputation.source === 'skua' ? h('p', { className: 'muted', text: t('factions.fromSkua') }) : null,
      reputation.factions.length === 0 ? empty(t('factions.empty')) : h('ul', { className: 'list' }, ...reputation.factions.map(factionRow)),
    ),
    h(
      'section',
      { className: 'card' },
      h('h2', { text: t('hardFarms.title') }),
      hf === null
        ? empty(t('hardFarms.notLoaded'))
        : h(
            'div',
            {},
            h('p', { text: t('hardFarms.owned', { owned: hf.owned, total: hf.total }) }),
            bar(t('hardFarms.bar'), hf.fraction),
            hf.shown.length + hf.hidden === 0
              ? null
              : h(
                  'details',
                  { attrs: model.hfOpen ? { open: '' } : {} },
                  h('summary', { text: t('hardFarms.missing', { count: missingCount }) }),
                  h('ul', { className: 'missing' }, ...hf.shown.map((item) => h('li', { text: item }))),
                  hf.hidden > 0 ? h('button', { text: t('hardFarms.showMore', { count: hf.hidden }), attrs: { type: 'button', 'data-action': 'toggle-all' } }) : null,
                  model.showAll && missingCount > 20 ? h('button', { text: t('hardFarms.showFewer'), attrs: { type: 'button', 'data-action': 'toggle-all' } }) : null,
                ),
            h('p', { className: 'muted', text: t('hardFarms.source') }),
          ),
    ),
  );
}

export function pairNeededView(reason: string | null = null, installHint: string | null = null): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('pair.title') }),
    reason ? h('p', { className: 'reason', text: reason, attrs: { role: 'status' } }) : null,
    h('p', {
      text: t('pair.steps'),
    }),
    h(
      'div',
      { className: 'paste' },
      h('p', {
        className: 'muted',
        text: t('pair.pasteHelp'),
      }),
      h('label', { text: t('pair.linkLabel'), attrs: { for: 'pair-link' } }),
      h('input', { attrs: { id: 'pair-link', type: 'text', autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'go' } }),
      h('button', { text: t('pair.button'), attrs: { type: 'button', 'data-action': 'pair-paste' } }),
    ),
    installHint ? h('p', { className: 'muted hint', text: installHint }) : null,
  );
}

export function drawFailedView(): VNode {
  return h(
    'section',
    { className: 'card' },
    h('h2', { text: t('failed.title') }),
    h('p', { className: 'muted', text: t('failed.body') }),
  );
}

/** The Appearance control: three toggle buttons, exactly one pressed. */
export function themeFooter(model: Pick<Model, 'theme'>): VNode {
  const current = model.theme ?? 'system';
  return h(
    'footer',
    { className: 'appearance', attrs: { role: 'group', 'aria-labelledby': 'appearance-label' } },
    h('p', { className: 'muted', text: t('theme.group'), attrs: { id: 'appearance-label' } }),
    h(
      'div',
      { className: 'appearance-row' },
      ...THEMES.map((choice) =>
        h('button', {
          className: 'theme-btn',
          text: t(`theme.${choice}`),
          attrs: { type: 'button', 'data-action': 'theme', 'data-value': choice, 'aria-pressed': String(choice === current) },
        }),
      ),
    ),
  );
}

/** Puts the Appearance footer under a view, inside the same column and grid. */
export function withFooter(view: VNode, model: Pick<Model, 'theme'>): VNode {
  return h('div', {}, ...(view.tag === 'div' ? view.children : [view]), themeFooter(model));
}
