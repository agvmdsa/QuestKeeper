import { t } from './i18n';
import { relativeTime } from './state';
import type { SkuaConnection, SkuaLogEntry, SkuaState, SkuaStatus } from './types';

const MAX_TEXT = 300;
const MAX_ENTRIES = 100;
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
  };
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
    log.push({ at: e.at, kind: typeof e.kind === 'string' ? e.kind : '', text: e.text.slice(0, MAX_TEXT) });
  }
  return { connection: value.connection as SkuaConnection, hello: parseHello(value.hello), status: parseStatus(value.status), log };
}

/** "just now", "3 min", "1 h 5 min", "2 d 3 h". */
export function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms < MINUTE_MS) return t('duration.now');
  const minutes = Math.floor(ms / MINUTE_MS);
  if (minutes < 60) return t('duration.minutes', { n: minutes });
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return minutes % 60 === 0 ? t('duration.hours', { n: hours }) : t('duration.hoursMinutes', { h: hours, m: minutes % 60 });
  const days = Math.floor(hours / 24);
  return hours % 24 === 0 ? t('duration.days', { n: days }) : t('duration.daysHours', { d: days, h: hours % 24 });
}

/** The title and lines of the Skua view, or null while Skua is not connected. */
export function skuaHeader(s: SkuaState, nowMs: number): { title: string; lines: string[] } | null {
  if (s.connection !== 'Connected') return null;
  const st = s.status;
  const lines: string[] = [];
  if (st) {
    if (!st.loggedIn) {
      lines.push(t('skua.notLoggedIn'));
    } else {
      const where = [st.character, st.server, st.map].filter((p): p is string => Boolean(p)).join(' · ');
      if (where) lines.push(where);
    }
    const since = st.runningSince ? Date.parse(st.runningSince) : Number.NaN;
    if (st.scriptRunning) {
      const name = st.script || t('skua.defaultScript');
      lines.push(Number.isNaN(since) ? t('skua.running', { name }) : t('skua.runningFor', { name, duration: formatDuration(nowMs - since) }));
    } else {
      lines.push(t('skua.noScript'));
    }
  }
  return { title: t('skua.title'), lines };
}

export function skuaLogRows(s: SkuaState, nowMs: number): { when: string; text: string }[] {
  return s.log.map((e) => ({ when: relativeTime(e.at, nowMs), text: e.text }));
}

/** What the Skua view says instead of a log: empty when there are entries to show. */
export function skuaEmptyText(s: SkuaState | null): string {
  if (!s || s.connection === 'NotConnected') return t('skua.notConnected');
  if (s.connection === 'VersionMismatch') return t('skua.mismatch');
  return s.log.length === 0 ? t('skua.nothingYet') : '';
}

/** The one sentence on the list-view card. */
export function skuaSummary(s: SkuaState): string {
  if (s.connection === 'Connected') return t('skua.connected');
  return s.connection === 'VersionMismatch' ? t('skua.mismatch') : t('skua.notConnected');
}
