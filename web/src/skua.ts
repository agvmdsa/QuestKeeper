import { t, type MessageKey } from './i18n';
import { relativeTime } from './state';
import type { Faction, RemoteState, SkuaConnection, SkuaControl, SkuaFaction, SkuaInstance, SkuaLogEntry, SkuaQuest, SkuaState, SkuaStatus } from './types';

const MAX_TEXT = 300;
const MAX_ENTRIES = 100;
const MAX_QUESTS = 30;
const MAX_QUEST_ID = 2_147_483_647;
const MAX_INT32 = MAX_QUEST_ID;
const MAX_FACTIONS = 60;
const CONNECTIONS: readonly string[] = ['NotConnected', 'Connected', 'VersionMismatch'];
const MINUTE_MS = 60_000;
const MAX_INSTANCES = 8;
const CONTROLS: readonly string[] = ['idle', 'running', 'stopping', 'settling', 'awaitingOptions'];
/** The bound on a stored label; anything longer is not one of ours (labels are cut to MAX_TEXT). */
const MAX_LABEL = 300;
/** The localStorage key of the Skua instance last looked at, by its label (the character name). */
export const SKUA_INSTANCE_KEY = 'sl-skua-instance';

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const str = (v: unknown): string | null => (typeof v === 'string' ? v : null);

function parseStatus(v: unknown): SkuaStatus | null {
  if (!isObject(v)) return null;
  return {
    loggedIn: v.loggedIn === true,
    character: str(v.character),
    server: str(v.server),
    map: str(v.map),
    script: str(v.script),
    scriptRunning: v.scriptRunning === true,
    runningSince: str(v.runningSince),
    quests: parseQuests(v.quests),
    factions: parseFactions(v.factions),
    control: typeof v.control === 'string' && CONTROLS.includes(v.control) ? (v.control as SkuaControl) : undefined,
    settleSecondsLeft: count(v.settleSecondsLeft) ?? undefined,
    optionsStuck: v.optionsStuck === true ? true : undefined,
  };
}

/** Cut to the text cap without leaving half of a surrogate pair at the end. */
function cut(text: string): string {
  if (text.length <= MAX_TEXT) return text;
  const last = text.charCodeAt(MAX_TEXT - 1);
  return text.slice(0, last >= 0xd800 && last <= 0xdbff ? MAX_TEXT - 1 : MAX_TEXT);
}

/** The active quests; anything but an array is "not reported" (null), and a damaged entry is left out on its own. */
function parseQuests(v: unknown): SkuaQuest[] | null {
  if (!Array.isArray(v)) return null;
  const quests: SkuaQuest[] = [];
  for (const q of v as unknown[]) {
    if (quests.length >= MAX_QUESTS) break;
    if (!isObject(q) || typeof q.id !== 'number' || !Number.isInteger(q.id) || q.id < 0 || q.id > MAX_QUEST_ID) continue;
    if (typeof q.name !== 'string' || typeof q.ready !== 'boolean') continue;
    quests.push({ id: q.id, name: cut(q.name), ready: q.ready });
  }
  return quests;
}

/** A whole number from 0 to the int32 maximum, or null. */
const count = (v: unknown): number | null => (typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= MAX_INT32 ? v : null);

/** The factions; anything but an array is "not reported" (null), and a damaged entry is left out on its own. */
function parseFactions(v: unknown): SkuaFaction[] | null {
  if (!Array.isArray(v)) return null;
  const factions: SkuaFaction[] = [];
  for (const f of v as unknown[]) {
    if (factions.length >= MAX_FACTIONS) break;
    if (!isObject(f) || typeof f.name !== 'string') continue;
    const id = count(f.id), rank = count(f.rank), totalRep = count(f.totalRep), rep = count(f.rep), requiredRep = count(f.requiredRep);
    if (id === null || rank === null || totalRep === null || rep === null || requiredRep === null) continue;
    factions.push({ id, name: cut(f.name), rank, totalRep, rep, requiredRep });
  }
  return factions;
}

function parseHello(v: unknown): SkuaState['hello'] {
  if (!isObject(v) || typeof v.pluginVersion !== 'string' || typeof v.skuaVersion !== 'string') return null;
  return { pluginVersion: v.pluginVersion, skuaVersion: v.skuaVersion };
}

/** The Skua part of the state; anything that is not the expected shape is null (the Skua card is then not shown). */
export function parseSkua(value: unknown): SkuaState | null {
  if (!isObject(value) || typeof value.connection !== 'string' || !CONNECTIONS.includes(value.connection) || !Array.isArray(value.log)) return null;
  const log: SkuaLogEntry[] = [];
  for (const e of value.log as unknown[]) {
    if (log.length >= MAX_ENTRIES) break;
    if (!isObject(e) || typeof e.at !== 'string' || typeof e.text !== 'string') continue;
    log.push({ at: e.at, kind: typeof e.kind === 'string' ? e.kind : '', text: cut(e.text) });
  }
  return { connection: value.connection as SkuaConnection, hello: parseHello(value.hello), status: parseStatus(value.status), log };
}

/**
 * The instance list of the state; null, absent or anything but an array is undefined (an older app: the single Skua is shown as before).
 * A damaged entry is left out on its own, and no more than the eight the PC can hold are read.
 */
export function parseSkuaInstances(value: unknown): SkuaInstance[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const out: SkuaInstance[] = [];
  for (const item of value as unknown[]) {
    if (out.length >= MAX_INSTANCES) break;
    if (!isObject(item) || typeof item.instanceId !== 'string' || typeof item.label !== 'string') continue;
    const skua = parseSkua(item);
    if (!skua) continue;
    out.push({ ...skua, instanceId: cut(item.instanceId), label: cut(item.label), disconnected: item.disconnected === true });
  }
  return out;
}

const isLive = (i: SkuaInstance): boolean => i.connection === 'Connected' && !i.disconnected;

/** The instance the Skua tab shows: the remembered one by label, else the first connected, else the first. Null for none. */
export function selectInstance(instances: readonly SkuaInstance[], savedLabel: string | null): SkuaInstance | null {
  return (savedLabel === null ? undefined : instances.find((i) => i.label === savedLabel)) ?? instances.find(isLive) ?? instances[0] ?? null;
}

/** The label next to the given one, in the given direction, wrapping around; for arrow keys on the chips. */
export function neighbourLabel(instances: readonly SkuaInstance[], label: string, step: 1 | -1): string | null {
  if (instances.length === 0) return null;
  const at = instances.findIndex((i) => i.label === label);
  return instances[(Math.max(at, 0) + step + instances.length) % instances.length]!.label;
}

type Store = Pick<Storage, 'getItem' | 'setItem'>;

/** The remembered label; storage can be blocked, and an odd value is not ours. */
export function loadInstance(storage: Store | null | undefined): string | null {
  try {
    const raw = storage?.getItem(SKUA_INSTANCE_KEY) ?? null;
    return raw !== null && raw.length <= MAX_LABEL ? raw : null;
  } catch {
    return null;
  }
}

export function saveInstance(storage: Store | null | undefined, label: string): void {
  try {
    storage?.setItem(SKUA_INSTANCE_KEY, label);
  } catch {
    // Not remembered; the choice still holds until the page closes.
  }
}

export interface InstanceState {
  text: string;
  /** How the dot looks: running (green), busy (orange: stopping, settling, waiting) or quiet. */
  tone: 'run' | 'warn' | 'none';
}

/** What an instance is doing, in words (never colour alone). */
export function instanceState(i: SkuaInstance): InstanceState {
  if (i.disconnected || i.connection === 'NotConnected') return { text: t('skua.state.disconnected'), tone: 'none' };
  if (i.connection === 'VersionMismatch') return { text: t('skua.state.mismatch'), tone: 'warn' };
  const st = i.status;
  if (!st || !st.loggedIn) return { text: t('skua.state.notLoggedIn'), tone: 'none' };
  if (st.control === 'awaitingOptions' || st.optionsStuck) return { text: t('skua.state.waiting'), tone: 'warn' };
  if (st.control === 'stopping') return { text: t('skua.state.stopping'), tone: 'warn' };
  if (st.control === 'settling') {
    return { text: st.settleSecondsLeft === undefined ? t('skua.state.settlingNoTime') : t('skua.state.settling', { n: st.settleSecondsLeft }), tone: 'warn' };
  }
  const running = st.control === undefined ? st.scriptRunning : st.control === 'running';
  return running ? { text: t('skua.state.running'), tone: 'run' } : { text: t('skua.state.idle'), tone: 'none' };
}

export const instanceStateText = (i: SkuaInstance): string => instanceState(i).text;

/** "just now", "3 min", "1 h 5 min", "2 d 3 h". */
export function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms < MINUTE_MS) return t('time.now');
  const minutes = Math.floor(ms / MINUTE_MS);
  if (minutes < 60) return t('duration.minutes', { n: minutes });
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return minutes % 60 === 0 ? t('duration.hours', { n: hours }) : t('duration.hoursMinutes', { h: hours, m: minutes % 60 });
  const days = Math.floor(hours / 24);
  return hours % 24 === 0 ? t('duration.days', { n: days }) : t('duration.daysHours', { d: days, h: hours % 24 });
}

export type LogFilter = 'all' | 'shop' | 'drops' | 'maps' | 'script' | 'session';

/** The filters in the order they are shown. */
export const LOG_FILTERS: readonly LogFilter[] = ['all', 'shop', 'drops', 'maps', 'script', 'session'];

export const LOG_FILTER_LABELS: Record<LogFilter, MessageKey> = {
  all: 'skua.filter.all',
  shop: 'skua.filter.shop',
  drops: 'skua.filter.drops',
  maps: 'skua.filter.maps',
  script: 'skua.filter.script',
  session: 'skua.filter.session',
};

/** Anything that is not a known filter is Everything. */
export function parseLogFilter(raw: string | null): LogFilter {
  return LOG_FILTERS.find((f) => f === raw) ?? 'all';
}

const KIND_FILTERS: Readonly<Record<string, Exclude<LogFilter, 'all'>>> = {
  Bought: 'shop',
  Sold: 'shop',
  Dropped: 'drops',
  MapChanged: 'maps',
  ScriptStarted: 'script',
  ScriptStopped: 'script',
  ScriptError: 'script',
  Died: 'session',
  LoggedOut: 'session',
  Relogin: 'session',
};

/**
 * The one filter a log kind belongs to, or null when it belongs to none: QuestAccepted, QuestTurnedIn (the active quests replace them)
 * and any kind this page does not know are shown only under Everything.
 */
export function filterOf(kind: string): Exclude<LogFilter, 'all'> | null {
  return Object.hasOwn(KIND_FILTERS, kind) ? KIND_FILTERS[kind]! : null;
}

export interface SkuaNow {
  loggedIn: boolean;
  /** The headline: the character's name, when logged in and known. */
  character: string | null;
  /** "Server · Map" with the missing parts left out, or null. */
  where: string | null;
  script: { running: boolean; text: string; name: string | null };
}

/** The Now card: who plays, where, and the script line; null while there is no status to show. */
export function skuaNow(s: SkuaState, nowMs: number): SkuaNow | null {
  const st = s.status;
  if (s.connection !== 'Connected' || !st) return null;
  const since = st.runningSince ? Date.parse(st.runningSince) : Number.NaN;
  const name = st.script || null;
  let text = t('skua.noScript');
  if (st.scriptRunning) text = Number.isNaN(since) ? t('skua.running', { name: name ?? t('skua.defaultScript') }) : t('skua.runningFor', { duration: formatDuration(nowMs - since) });
  const where = [st.server, st.map].filter((p): p is string => Boolean(p)).join(' · ');
  return {
    loggedIn: st.loggedIn,
    character: st.loggedIn && st.character ? st.character : null,
    where: st.loggedIn && where ? where : null,
    script: { running: st.scriptRunning, text, name: st.scriptRunning && !Number.isNaN(since) ? name : null },
  };
}

/** The active quests, ready ones first and the rest in the game's order; null when the plugin does not report quests. */
export function questRows(s: SkuaState): SkuaQuest[] | null {
  const quests = s.connection === 'Connected' ? s.status?.quests : null;
  if (!quests) return null;
  return [...quests.filter((q) => q.ready), ...quests.filter((q) => !q.ready)];
}

export interface FactionRow {
  name: string;
  rank: number;
  /** The maximum rank: rank 10, or a rank step of 0. */
  max: boolean;
  /** The share of the current rank already earned, 0 to 1 (1 at the maximum rank). */
  fraction: number;
  toNext: number;
}

const finite = (n: number): number => (Number.isFinite(n) ? n : 0);

/** The ordering key of the capture-based list (FeedSnapshotBuilder): what is missing for the next rank, the maximum rank last. */
const missing = (r: FactionRow): number => (r.max ? Number.POSITIVE_INFINITY : r.toNext);

/** Ordinal comparison ignoring case, like the app's StringComparer.OrdinalIgnoreCase. */
const byName = (a: string, b: string): number => {
  const x = a.toUpperCase(), y = b.toUpperCase();
  return x < y ? -1 : x > y ? 1 : 0;
};

/**
 * The Reputation card's rows, ordered like the capture-based reputation list: closest to the next rank first, the maximum
 * rank last, ties by name ignoring case. Null when the plugin does not report reputation or Skua is not connected.
 */
export function factionRows(s: SkuaState): FactionRow[] | null {
  return ranked(s)?.map((x) => x.row) ?? null;
}

/** The rows with the faction's total reputation, in the shared order. */
function ranked(s: SkuaState): { row: FactionRow; rep: number }[] | null {
  const factions = s.connection === 'Connected' ? s.status?.factions : null;
  if (!factions) return null;
  return factions
    .map((f) => {
      const max = f.rank >= 10 || f.requiredRep <= 0;
      const fraction = max ? 1 : Math.min(1, Math.max(0, finite(f.rep / f.requiredRep)));
      const toNext = max ? 0 : Math.max(0, finite(f.requiredRep - f.rep));
      const row: FactionRow = { name: f.name, rank: f.rank, max, fraction, toNext };
      return { row, rep: f.totalRep };
    })
    .sort((a, b) => missing(a.row) - missing(b.row) || byName(a.row.name, b.row.name));
}

/**
 * The list the phone's main Reputation card shows. Skua's live list wins only when a connected Skua (any of them) reports factions
 * for the selected character, ignoring letter case. An empty Skua list falls back to the capture list: a logged-in character with
 * no reputation at all is rare, and a transient empty read (Skua answers [] on any read failure) must not blank the page.
 */
export function effectiveFactions(state: RemoteState): { factions: Faction[]; source: 'skua' | 'capture' } {
  const snapshot = state.snapshot;
  const capture = { factions: snapshot?.factions ?? [], source: 'capture' as const };
  if (!snapshot || snapshot.selected === null) return capture;
  const selected = snapshot.selected.toUpperCase();
  // Every connected Skua is a candidate: the selected character may be played in any of them. An older app sends only the single one.
  const candidates: SkuaState[] = state.skuaInstances?.length ? state.skuaInstances.filter((i) => !i.disconnected) : state.skua ? [state.skua] : [];
  for (const skua of candidates) {
    const character = skua.status?.character;
    if (!character || character.toUpperCase() !== selected) continue;
    const rows = ranked(skua);
    if (!rows || rows.length === 0) continue;
    return { factions: rows.map(({ row, rep }) => ({ name: row.name, rank: row.rank, rep, toNext: row.toNext, fraction: row.fraction, max: row.max })), source: 'skua' };
  }
  return capture;
}

export function skuaLogRows(s: SkuaState, nowMs: number, filter: LogFilter = 'all'): { when: string; text: string }[] {
  return s.log.filter((e) => filter === 'all' || filterOf(e.kind) === filter).map((e) => ({ when: relativeTime(e.at, nowMs), text: e.text }));
}

/** What the Skua view says instead of a log: empty when there are entries to show. */
export function skuaEmptyText(s: SkuaState | null, filter: LogFilter = 'all'): string {
  if (!s || s.connection === 'NotConnected') return t('skua.notConnected');
  if (s.connection === 'VersionMismatch') return t('skua.mismatch');
  if (s.log.length === 0) return t('skua.nothingYet');
  return skuaLogRows(s, 0, filter).length === 0 ? t('skua.log.filterEmpty') : '';
}

/** The one sentence on the list-view card. */
export function skuaSummary(s: SkuaState): string {
  if (s.connection === 'Connected') return t('skua.connected');
  return s.connection === 'VersionMismatch' ? t('skua.mismatch') : t('skua.notConnected');
}
