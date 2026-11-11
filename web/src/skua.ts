import { relativeTime } from './state';
import type { SkuaConnection, SkuaLogEntry, SkuaState, SkuaStatus } from './types';

export const SKUA_TITLE = 'Skua';
export const SKUA_NOT_CONNECTED = 'Skua is not connected.';
export const SKUA_CONNECTED = 'Skua connected.';
export const SKUA_MISMATCH = 'Update the Skua plugin and Questkeeper to the same release.';
export const SKUA_NOTHING_YET = 'Nothing to show yet.';
export const SKUA_NO_SCRIPT = 'No script running';
export const SKUA_NOT_LOGGED_IN = 'Not logged in';

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
  if (!Number.isFinite(ms) || ms < MINUTE_MS) return 'just now';
  const minutes = Math.floor(ms / MINUTE_MS);
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return minutes % 60 === 0 ? `${hours} h` : `${hours} h ${minutes % 60} min`;
  const days = Math.floor(hours / 24);
  return hours % 24 === 0 ? `${days} d` : `${days} d ${hours % 24} h`;
}

/** The title and lines of the Skua view, or null while Skua is not connected. */
export function skuaHeader(s: SkuaState, nowMs: number): { title: string; lines: string[] } | null {
  if (s.connection !== 'Connected') return null;
  const st = s.status;
  const lines: string[] = [];
  if (st) {
    if (!st.loggedIn) {
      lines.push(SKUA_NOT_LOGGED_IN);
    } else {
      const where = [st.character, st.server, st.map].filter((p): p is string => Boolean(p)).join(' · ');
      if (where) lines.push(where);
    }
    const since = st.runningSince ? Date.parse(st.runningSince) : Number.NaN;
    if (st.scriptRunning) {
      const name = st.script || 'a script';
      lines.push(Number.isNaN(since) ? `Running ${name}` : `Running ${name} for ${formatDuration(nowMs - since)}`);
    } else {
      lines.push(SKUA_NO_SCRIPT);
    }
  }
  return { title: SKUA_TITLE, lines };
}

export function skuaLogRows(s: SkuaState, nowMs: number): { when: string; text: string }[] {
  return s.log.map((e) => ({ when: relativeTime(e.at, nowMs), text: e.text }));
}

/** What the Skua view says instead of a log: empty when there are entries to show. */
export function skuaEmptyText(s: SkuaState | null): string {
  if (!s || s.connection === 'NotConnected') return SKUA_NOT_CONNECTED;
  if (s.connection === 'VersionMismatch') return SKUA_MISMATCH;
  return s.log.length === 0 ? SKUA_NOTHING_YET : '';
}

/** The one sentence on the list-view card. */
export function skuaSummary(s: SkuaState): string {
  if (s.connection === 'Connected') return SKUA_CONNECTED;
  return s.connection === 'VersionMismatch' ? SKUA_MISMATCH : SKUA_NOT_CONNECTED;
}
