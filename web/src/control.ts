import type { CallResult, ControlApi } from './api';
import { t, type MessageKey } from './i18n';
import { formatDuration, instanceState, isLive, type InstanceState } from './skua';
import type { Banner } from './state';
import type { SkuaInstance } from './types';

/** A script id as the PC makes it: 16 lowercase hex characters, nothing else. */
const SCRIPT_ID = /^[0-9a-f]{16}$/;
const MAX_SCRIPTS = 500;
const MAX_DEFS = 500;
const MAX_CHOICES = 200;
const MAX_TEXT = 300;
const MAX_QUERY = 100;
/** How long "Skua did not answer" stays in the banner. */
const NO_ANSWER_MS = 30_000;
/** The settle wait of the plugin, for the bar. */
const SETTLE_SECONDS = 10;
const TYPES: readonly string[] = ['bool', 'int', 'double', 'string', 'enum', 'other'];
const INT = /^-?\d+$/;
const DOUBLE = /^-?\d+(\.\d+)?$/;

export type OptionType = 'bool' | 'int' | 'double' | 'string' | 'enum' | 'other';

export interface ScriptItem {
  id: string;
  name: string;
  folder: string;
  /** Show the options step before a start (the default); false once the owner chose "Don't ask again". */
  askOptions: boolean;
}

export interface OptionDef {
  category: string;
  name: string;
  displayName: string;
  description: string;
  type: OptionType;
  default: string;
  choices: string[] | null;
}

export interface OptionValue {
  category: string;
  name: string;
  value: string;
}

export interface OptionsInfo {
  /** null: the definitions are not known yet; empty: the script has no options. */
  defs: OptionDef[] | null;
  values: OptionValue[];
  hasOptions: boolean;
  discovery: boolean;
  askOptions: boolean;
}

export interface StartResult {
  hasOptions: boolean;
  firstRun: boolean;
  defs: OptionDef[] | null;
  values: OptionValue[];
}

/** What a failed call was doing; it picks the words of the error. */
export type Action = 'start' | 'stop' | 'options' | 'list' | 'load' | 'prefs';

const ACTION_TEXT: Record<Action, MessageKey> = {
  start: 'control.error.start',
  stop: 'control.error.stop',
  options: 'control.error.options',
  list: 'control.error.list',
  load: 'control.error.load',
  prefs: 'control.error.prefs',
};

/** Plugin refusals the page can say better; any other text is shown as the plugin wrote it. */
const REASONS: Readonly<Record<string, MessageKey>> = {
  'already running': 'control.reason.running',
  settling: 'control.reason.settling',
  stopping: 'control.reason.settling',
  'unknown script id': 'control.reason.unknownScript',
  'not found': 'control.reason.unknownScript',
};

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const text = (v: unknown): string => (typeof v === 'string' ? cut(v) : '');

function cut(s: string): string {
  if (s.length <= MAX_TEXT) return s;
  const last = s.charCodeAt(MAX_TEXT - 1);
  return s.slice(0, last >= 0xd800 && last <= 0xdbff ? MAX_TEXT - 1 : MAX_TEXT);
}

/** The script list; a broken entry is left out on its own, anything but the shape is null. */
export function parseScripts(v: unknown): { scripts: ScriptItem[]; truncated: boolean } | null {
  if (!isObject(v) || !Array.isArray(v.scripts)) return null;
  const scripts: ScriptItem[] = [];
  for (const s of v.scripts as unknown[]) {
    if (scripts.length >= MAX_SCRIPTS) break;
    if (!isObject(s) || typeof s.id !== 'string' || !SCRIPT_ID.test(s.id) || typeof s.name !== 'string') continue;
    scripts.push({ id: s.id, name: cut(s.name), folder: text(s.folder), askOptions: s.askOptions !== false });
  }
  return { scripts, truncated: v.truncated === true };
}

function parseDefs(v: unknown): OptionDef[] | null {
  if (!Array.isArray(v)) return null;
  const defs: OptionDef[] = [];
  for (const d of v as unknown[]) {
    if (defs.length >= MAX_DEFS) break;
    if (!isObject(d) || !text(d.category) || !text(d.name)) continue;
    const type = typeof d.type === 'string' && TYPES.includes(d.type) ? (d.type as OptionType) : 'other';
    const choices = Array.isArray(d.choices) ? (d.choices as unknown[]).filter((c): c is string => typeof c === 'string').slice(0, MAX_CHOICES).map(cut) : null;
    defs.push({ category: text(d.category), name: text(d.name), displayName: text(d.displayName), description: text(d.description), type, default: text(d.default), choices });
  }
  return defs;
}

function parseValues(v: unknown): OptionValue[] {
  if (!Array.isArray(v)) return [];
  return (v as unknown[])
    .filter((x): x is OptionValue => isObject(x) && typeof x.category === 'string' && typeof x.name === 'string' && typeof x.value === 'string')
    .slice(0, MAX_DEFS)
    .map((x) => ({ category: cut(x.category), name: cut(x.name), value: cut(x.value) }));
}

export function parseOptions(v: unknown): (OptionsInfo & { known: boolean }) | null {
  if (!isObject(v)) return null;
  const defs = parseDefs(v.defs);
  return { defs, values: parseValues(v.values), known: defs !== null, hasOptions: v.hasOptions === true, discovery: v.discovery === true, askOptions: v.askOptions !== false };
}

export function parseStart(v: unknown): StartResult {
  if (!isObject(v)) return { hasOptions: false, firstRun: false, defs: null, values: [] };
  return { hasOptions: v.hasOptions === true, firstRun: v.firstRun === true, defs: parseDefs(v.defs), values: parseValues(v.values) };
}

/** The words for a failed call: by status, with the plugin's own text (as text, never markup) for a refusal. */
export function failureText(r: CallResult, action: Action): string {
  if (r.status === 504) return t('control.error.noAnswer');
  if (r.status === 0) return t('control.error.unreachable');
  const error = isObject(r.data) ? text(r.data.error) : '';
  if (r.status === 409) return error === 'busy' ? t('control.error.busy') : t('control.error.disconnected');
  if (r.status === 404) return t('control.error.gone');
  if (r.status === 400 && error) return t(ACTION_TEXT[action], { reason: Object.hasOwn(REASONS, error) ? t(REASONS[error]!) : error });
  return t('control.error.generic');
}

/** "Skua did not answer" in the page's banner, for a while after a 504. */
export function controlBanner(nowMs: number, until: number): Banner | null {
  return nowMs < until ? { tone: 'warn', text: t('control.error.noAnswer') } : null;
}

/** The scripts whose name holds the search text (ignoring case), grouped by folder in the order they came. */
export function scriptGroups(scripts: readonly ScriptItem[], query: string): { folder: string; scripts: ScriptItem[] }[] {
  const q = query.trim().toLowerCase();
  const groups = new Map<string, ScriptItem[]>();
  for (const s of scripts) {
    if (q && !s.name.toLowerCase().includes(q)) continue;
    const list = groups.get(s.folder) ?? [];
    list.push(s);
    groups.set(s.folder, list);
  }
  return [...groups].map(([folder, list]) => ({ folder, scripts: list }));
}

export interface CardModel {
  tone: InstanceState['tone'];
  state: string;
  /** "for 2 h", while running. */
  since: string | null;
  script: string | null;
  /** The loud line of the waiting state. */
  strong: string | null;
  hint: string | null;
  /** The share of the settle wait already passed, 0 to 1, while settling. */
  settle: number | null;
  /** The one button; null when this Skua cannot be controlled (an older plugin). */
  action: 'start' | 'stop' | null;
  /** Why the button is disabled, or null when it can be used. */
  disabled: string | null;
  pending: boolean;
  waiting: boolean;
  /** The "Ask for options" switch of the running script, when the page knows which script it is. */
  ask: { id: string; checked: boolean } | null;
  error: string | null;
}

/** The Control card of an instance; null when it is not connected or has no status. */
export function controlCardModel(i: SkuaInstance, control: Control, nowMs: number): CardModel | null {
  const st = i.status;
  if (!st || !isLive(i)) return null;
  const pending = control.isPending(i.instanceId);
  const busy = pending ? t('control.pending') : null;
  const plain = instanceState(i);
  const base: CardModel = {
    tone: plain.tone, state: plain.text, since: null, script: null, strong: null, hint: null, settle: null, action: null, disabled: null, pending, waiting: false, ask: null,
    error: control.errorOf(i.instanceId),
  };
  if (st.control === undefined) return { ...base, hint: t('control.oldPlugin') };
  const script = st.script || null;
  if (st.optionsStuck) {
    return { ...base, tone: 'warn', state: t('control.waiting'), script, strong: t('control.waitingStrong'), hint: t('control.waitingHint'), action: 'stop', disabled: busy, waiting: true };
  }
  switch (st.control) {
    case 'awaitingOptions':
      return { ...base, tone: 'warn', state: t('control.applying'), script, hint: t('control.applyingHint'), action: 'stop', disabled: busy };
    case 'running': {
      const since = st.runningSince ? Date.parse(st.runningSince) : Number.NaN;
      const item = control.runningItem(i);
      return {
        ...base, tone: 'run', state: t('skua.state.running'), script, action: 'stop', disabled: busy, hint: t('control.runningHint'),
        since: Number.isNaN(since) ? null : t('control.for', { duration: formatDuration(nowMs - since) }),
        ask: item ? { id: item.id, checked: item.askOptions } : null,
      };
    }
    case 'stopping':
      return { ...base, tone: 'warn', state: t('skua.state.stopping'), script, action: 'start', disabled: t('control.stoppingHint') };
    case 'settling': {
      const left = st.settleSecondsLeft;
      return {
        ...base, tone: 'warn', state: t('skua.state.settlingNoTime'), action: 'start',
        disabled: left === undefined ? t('control.settlingHintNoTime') : t('control.settlingHint', { n: left }),
        settle: left === undefined ? null : Math.min(1, Math.max(0, (SETTLE_SECONDS - left) / SETTLE_SECONDS)),
      };
    }
    default:
      return { ...base, hint: t('control.noScript'), action: 'start', disabled: busy };
  }
}

interface SheetBase {
  instanceId: string;
  /** The label of the Skua the sheet acts on (the character name, or "Skua N"). */
  character: string;
  error: string | null;
}

export type Sheet =
  | (SheetBase & { kind: 'stop'; script: string })
  | (SheetBase & {
      kind: 'scripts';
      loading: boolean;
      scripts: ScriptItem[];
      truncated: boolean;
      query: string;
      /** The script being loaded or started, and what is happening to it. */
      pick: string | null;
      pickText: MessageKey | null;
    })
  | (SheetBase & {
      kind: 'options';
      script: ScriptItem;
      defs: OptionDef[];
      /** The value of each definition, in the same order. */
      values: string[];
      /** False when the definitions are not known yet: the saved values are only shown. */
      editable: boolean;
      firstRun: boolean;
      dontAsk: boolean;
      initialDontAsk: boolean;
    });

/** The value the PC takes: a decimal comma becomes a point (the PC wants 1.5, not 1,5). */
function wireValue(def: OptionDef, value: string): string {
  return def.type === 'double' ? value.replace(',', '.') : value;
}

const labelOf = (d: OptionDef): string => d.displayName || d.name;

/**
 * The Skua control of the page: the open sheet, which instance has a command in flight (one at a time per instance, so a second tap
 * sends nothing), the last error of each instance, and what it knows about the scripts. It calls `changed` whenever any of it changes.
 */
export class Control {
  sheet: Sheet | null = null;
  /** Until when the banner says "Skua did not answer". */
  noAnswerUntil = 0;
  private readonly api: ControlApi;
  private readonly changed: () => void;
  private readonly unpaired: () => void;
  private readonly now: () => number;
  private readonly busy = new Set<string>();
  private readonly errors = new Map<string, string>();
  private readonly lists = new Map<string, ScriptItem[]>();
  private readonly started = new Map<string, ScriptItem>();
  private readonly looked = new Set<string>();

  constructor(api: ControlApi, changed: () => void, unpaired: () => void, now: () => number = Date.now) {
    this.api = api;
    this.changed = changed;
    this.unpaired = unpaired;
    this.now = now;
  }

  isPending(instanceId: string): boolean {
    return this.busy.has(instanceId);
  }

  errorOf(instanceId: string): string | null {
    return this.errors.get(instanceId) ?? null;
  }

  /** The running script of an instance, when the page knows it: the one it started, or the only one of that name in the list. */
  runningItem(i: SkuaInstance): ScriptItem | null {
    const name = i.status?.script;
    if (!name) return null;
    const own = this.started.get(i.instanceId);
    if (own?.name === name) return own;
    const same = (this.lists.get(i.instanceId) ?? []).filter((s) => s.name === name);
    return same.length === 1 ? same[0]! : null;
  }

  /** Looks up the running script once per instance and name, so the card can offer the "Ask for options" switch. */
  track(i: SkuaInstance | null): void {
    const st = i?.status;
    if (!i || !st?.script || !isLive(i) || (st.control !== 'running' && st.control !== 'awaitingOptions') || this.runningItem(i)) return;
    const key = `${i.instanceId}\n${st.script}`;
    if (this.looked.has(key)) return;
    this.looked.add(key);
    void this.api.listScripts(i.instanceId).then((r) => {
      const list = r.status === 200 ? parseScripts(r.data) : null;
      if (!list) return;
      this.lists.set(i.instanceId, list.scripts);
      this.changed();
    });
  }

  openStop(i: SkuaInstance): void {
    if (this.isPending(i.instanceId)) return;
    this.sheet = { kind: 'stop', instanceId: i.instanceId, character: i.label, script: i.status?.script || t('skua.defaultScript'), error: null };
    this.changed();
  }

  openStart(i: SkuaInstance): void {
    if (this.isPending(i.instanceId)) return;
    const sheet: Sheet = { kind: 'scripts', instanceId: i.instanceId, character: i.label, loading: true, scripts: [], truncated: false, query: '', pick: null, pickText: null, error: null };
    this.sheet = sheet;
    this.changed();
    void this.api.listScripts(i.instanceId).then((r) => {
      if (this.sheet !== sheet) return;
      sheet.loading = false;
      const list = r.status === 200 ? parseScripts(r.data) : null;
      if (list) {
        sheet.scripts = list.scripts;
        sheet.truncated = list.truncated;
        this.lists.set(i.instanceId, list.scripts);
      } else {
        sheet.error = this.noteFailure(r, 'list');
      }
      this.changed();
    });
  }

  close(): void {
    this.sheet = null;
    this.changed();
  }

  setQuery(query: string): void {
    if (this.sheet?.kind !== 'scripts') return;
    this.sheet.query = query.slice(0, MAX_QUERY);
    this.changed();
  }

  /** A field of the options step changed; the page already shows it, so nothing is redrawn. */
  setValue(index: number, value: string): void {
    if (this.sheet?.kind === 'options' && this.sheet.editable && index >= 0 && index < this.sheet.values.length) this.sheet.values[index] = value;
  }

  setDontAsk(on: boolean): void {
    if (this.sheet?.kind === 'options') this.sheet.dontAsk = on;
  }

  /** One tap on a script: its options step, or a start at once (asking turned off, no options, or never seen: a discovery start). */
  pick(scriptId: string): void {
    const sheet = this.sheet;
    if (sheet?.kind !== 'scripts' || sheet.pick) return;
    const item = sheet.scripts.find((s) => s.id === scriptId);
    if (!item) return;
    const id = sheet.instanceId;
    this.run(id, async () => {
      sheet.pick = item.id;
      try {
        if (!item.askOptions) return await this.start(id, item, sheet.character);
        sheet.pickText = 'control.loadingOptions';
        const r = await this.api.getOptions(id, item.id);
        const info = r.status === 200 ? parseOptions(r.data) : null;
        if (!info) return this.fail(id, r, 'load');
        if (info.defs?.length) return this.openOptions(id, sheet.character, item, info.defs, info.values, true, false, !info.askOptions);
        if (info.defs || info.values.length === 0) return await this.start(id, item, sheet.character);
        // Saved values without definitions: shown, not editable, until the next start teaches SkuaLink the definitions.
        const shown = info.values.map((v): OptionDef => ({ category: v.category, name: v.name, displayName: v.name, description: '', type: 'other', default: v.value, choices: null }));
        return this.openOptions(id, sheet.character, item, shown, info.values, false, false, !info.askOptions);
      } finally {
        sheet.pick = null;
        sheet.pickText = null;
      }
    });
  }

  /** "Start with these options": the options (when editable), the "Don't ask again" choice when it changed, then the start. */
  submitOptions(): void {
    const sheet = this.sheet;
    if (sheet?.kind !== 'options' || this.isPending(sheet.instanceId)) return;
    const invalid = sheet.editable ? this.invalidField(sheet.defs, sheet.values) : null;
    if (invalid) {
      sheet.error = invalid;
      this.changed();
      return;
    }
    const id = sheet.instanceId;
    this.run(id, async () => {
      if (sheet.editable) {
        const values = sheet.defs.map((d, i) => ({ category: d.category, name: d.name, value: wireValue(d, sheet.values[i] ?? '') }));
        const r = await this.api.setOptions(id, sheet.script.id, values);
        if (r.status !== 200) return this.fail(id, r, 'options');
      }
      if (sheet.dontAsk !== sheet.initialDontAsk) {
        const r = await this.api.setPrefs(id, sheet.script.id, !sheet.dontAsk);
        if (r.status !== 200) return this.fail(id, r, 'prefs');
        this.remember(id, sheet.script.id, !sheet.dontAsk);
        sheet.initialDontAsk = sheet.dontAsk;
      }
      await this.start(id, sheet.script, sheet.character);
    });
  }

  confirmStop(): void {
    const sheet = this.sheet;
    if (sheet?.kind !== 'stop') return;
    const id = sheet.instanceId;
    this.run(id, async () => {
      const r = await this.api.stop(id);
      if (r.status !== 200) return this.fail(id, r, 'stop');
      if (this.sheet === sheet) this.sheet = null;
    });
  }

  /** The "Ask for options when starting this script" switch of the running card. */
  setAsk(instanceId: string, scriptId: string, ask: boolean): void {
    if (!SCRIPT_ID.test(scriptId)) return;
    this.run(instanceId, async () => {
      const r = await this.api.setPrefs(instanceId, scriptId, ask);
      if (r.status !== 200) return this.fail(instanceId, r, 'prefs');
      this.remember(instanceId, scriptId, ask);
    });
  }

  private remember(instanceId: string, scriptId: string, ask: boolean): void {
    for (const s of [...(this.lists.get(instanceId) ?? []), this.started.get(instanceId)]) if (s?.id === scriptId) s.askOptions = ask;
  }

  private invalidField(defs: OptionDef[], values: string[]): string | null {
    for (const [i, d] of defs.entries()) {
      const v = wireValue(d, values[i] ?? '');
      if (d.type === 'int' && !INT.test(v)) return t('control.field.int', { name: labelOf(d) });
      if (d.type === 'double' && !DOUBLE.test(v)) return t('control.field.double', { name: labelOf(d) });
    }
    return null;
  }

  private openOptions(id: string, character: string, script: ScriptItem, defs: OptionDef[], saved: OptionValue[], editable: boolean, firstRun: boolean, dontAsk: boolean): void {
    const values = defs.map((d) => saved.find((v) => v.category === d.category && v.name === d.name)?.value ?? d.default);
    this.sheet = { kind: 'options', instanceId: id, character, script, defs, values, editable, firstRun, dontAsk, initialDontAsk: dontAsk, error: null };
  }

  private async start(id: string, item: ScriptItem, character: string): Promise<void> {
    if (this.sheet?.kind === 'scripts') this.sheet.pickText = 'control.starting';
    const r = await this.api.start(id, item.id);
    if (r.status !== 200) return this.fail(id, r, 'start');
    const result = parseStart(r.data);
    const free = this.sheet === null || this.sheet.instanceId === id;
    if (result.firstRun && result.defs?.length) {
      if (free) this.openOptions(id, character, item, result.defs, result.values, true, true, !item.askOptions);
      return;
    }
    this.started.set(id, item);
    if (free) this.sheet = null;
  }

  /** One command at a time per instance: a call while one is in flight is ignored. */
  private run(id: string, work: () => Promise<void>): void {
    if (this.busy.has(id)) return;
    this.busy.add(id);
    this.errors.delete(id);
    if (this.sheet?.instanceId === id) this.sheet.error = null;
    this.changed();
    work()
      .catch(() => this.fail(id, { status: -1, data: null }, 'start'))
      .finally(() => {
        this.busy.delete(id);
        this.changed();
      });
  }

  /** The side effects of a failure (401 unpairs, 504 lights the banner) and its words; null when the page became unpaired. */
  private noteFailure(r: CallResult, action: Action): string | null {
    if (r.status === 401) {
      this.sheet = null;
      this.unpaired();
      return null;
    }
    if (r.status === 504) this.noAnswerUntil = this.now() + NO_ANSWER_MS;
    return failureText(r, action);
  }

  private fail(id: string, r: CallResult, action: Action): void {
    const words = this.noteFailure(r, action);
    if (words === null) return;
    this.errors.set(id, words);
    if (this.sheet?.instanceId === id) this.sheet.error = words;
  }
}
