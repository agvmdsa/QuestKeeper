import { t, type MessageKey } from './i18n';
import { relativeTime } from './state';
import type { SkuaConnection, SkuaLogEntry, SkuaQuest, SkuaState, SkuaStatus } from './types';

const MAX_TEXT = 300;
const MAX_ENTRIES = 100;
const MAX_QUESTS = 30;
const MAX_QUEST_ID = 2_147_483_647;
const CONNECTIONS: readonly string[] = ['NotConnected', 'Connected', 'VersionMismatch'];
const MINUTE_MS = 60_000;

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
