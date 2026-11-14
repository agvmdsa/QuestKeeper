import { STATE_TIMEOUT_MS, type PairOutcome } from './api';
import { formatNumberLocale, t, type MessageKey } from './i18n';
import { parseSkua, parseSkuaInstances } from './skua';
import type { Character, Figures, HardFarms, Notice, NoticeKind, RemoteState } from './types';

export interface Banner {
  tone: 'warn' | 'neutral';
  text: string;
}

export interface Row {
  name: string;
  level: number;
  kind: 'live' | 'idle' | 'saved';
  status: string;
  gold: string;
}

export interface HardFarmsView {
  owned: number;
  total: number;
  fraction: number;
  shown: string[];
  hidden: number;
}

export type Route = { view: 'list' } | { view: 'skua' } | { view: 'character'; name: string } | { view: 'pair'; token: string };

/** The localStorage key of the last state shown; a new shape gets a new version. */
export const KEPT_KEY = 'qkp-last-state-v1';

const UNITS: [number, string][] = [[1e9, 'B'], [1e6, 'M'], [1e3, 'K']];
const HARD_FARMS_SHOWN = 20;
const LIVE_WINDOW_MS = 120_000;
const CLOCK_SKEW_MS = 60_000;
const RECONNECT_MS = [2000, 4000, 8000, 15000];
const PAIR_FAILURE: Record<PairOutcome, MessageKey | null> = {
  paired: null,
  locked: 'pair.locked',
  invalid: 'pair.rejected',
  failed: 'pair.rejected',
};

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

/** True when the snapshot has every part the views read: an unknown or broken shape is refused before it is drawn or stored. */
export function isUsableState(s: RemoteState): boolean {
  const snap: unknown = s.snapshot;
  if (snap === null) return true;
  if (!isObject(snap) || snap.contract !== 1) return false;
  if (!isObject(snap.capture) || typeof snap.capture.state !== 'string') return false;
  if (!Array.isArray(snap.characters) || !Array.isArray(snap.factions)) return false;
  if (!snap.characters.every((c: unknown) => isObject(c) && typeof c.name === 'string' && isObject(c.figures))) return false;
  if (!snap.factions.every((f: unknown) => isObject(f) && typeof f.name === 'string')) return false;
  const hf = snap.hardFarms;
  return hf === null || hf === undefined || (isObject(hf) && Array.isArray(hf.missing));
}

export function isNotice(v: unknown): v is Notice {
  return isObject(v) && typeof v.id === 'string' && typeof v.title === 'string' && typeof v.body === 'string' && typeof v.atUtc === 'string'
    && (v.character === undefined || v.character === null || typeof v.character === 'string');
}

export function parseNotice(text: string): Notice | null {
  try {
    const value: unknown = JSON.parse(text);
    return isNotice(value) ? value : null;
  } catch {
    return null;
  }
}

/** The one-time token the PC makes: 32 random bytes in URL-safe base64 without padding. */
const TOKEN = /^[A-Za-z0-9_-]{43}$/;
const MAX_PASTE = 512;

/**
 * The token from a pasted pairing link: the whole link (https, a bare name, no path, query, port or user), its "#t=" part, or the
 * token alone. Anything else is null. The text is only read here; it is never logged or put in the address bar.
 */
export function parsePairingInput(text: string): string | null {
  if (text.length > MAX_PASTE) return null;
  const value = text.trim();
  if (TOKEN.test(value)) return value;
  if (value.startsWith('#t=')) return TOKEN.test(value.slice(3)) ? value.slice(3) : null;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  if (url.protocol !== 'https:' || url.username || url.password || url.port || url.pathname !== '/' || url.search) return null;
  if (!url.hash.startsWith('#t=')) return null;
  const token = url.hash.slice(3);
  return TOKEN.test(token) ? token : null;
}

/** A pairing link puts a one-time token in the hash; the caller removes it from the URL before using it. */
export function takePairingToken(hash: string): { token: string; cleanHash: string } | null {
  return hash.startsWith('#t=') ? { token: hash.slice(3), cleanHash: '' } : null;
}

export function pairFailureText(outcome: PairOutcome): string | null {
  const key = PAIR_FAILURE[outcome];
  return key === null ? null : t(key);
}

/** What asking for the state said before a pairing link is used: known (200), unknown (401) or no answer. */
export type PairProbe = 'paired' | 'unpaired' | 'failed';

/** A pairing link spends its one-time token only when the PC does not know this browser; otherwise the page carries on. */
export function pairingStep(probe: PairProbe): 'pair' | 'continue' {
  return probe === 'unpaired' ? 'pair' : 'continue';
}

export function reconnectDelay(attempt: number): number {
  return RECONNECT_MS[Math.min(attempt, RECONNECT_MS.length - 1)]!;
}

/** Loads a chunk, trying again after 0.5 s and 1 s; rejects with the last error after the third failure. */
export async function retryImport<T>(load: () => Promise<T>, tries = 3): Promise<T> {
  for (let i = 1; ; i += 1) {
    try {
      return await load();
    } catch (error) {
      if (i >= tries) throw error;
      await new Promise((done) => setTimeout(done, 250 * 2 ** i));
    }
  }
}

/**
 * What to do when the event stream reports an error. While the browser still retries by itself (a dropped connection) the page waits.
 * Once it gave up (any answer other than 200, such as 401 or the 503 the PC gives a ninth stream) the page asks /api/state.
 */
export function streamErrorStep(closed: boolean): 'wait' | 'check' {
  return closed ? 'check' : 'wait';
}

/** After that check only a 401 means this browser is no longer paired; any other outcome reconnects after reconnectDelay. */
export function afterStreamCheck(probe: PairProbe): 'unpaired' | 'reconnect' {
  return probe === 'unpaired' ? 'unpaired' : 'reconnect';
}

export interface FocusTarget {
  tag: string;
  action: string | null;
  href: string | null;
  id?: string | null;
  /** `data-value`, for controls that share one action (the theme buttons). */
  value?: string | null;
}

/** Names a focused control so the same control can get focus back after a redraw. */
export function focusKey(t: FocusTarget): string | null {
  if (t.action) return t.value ? `action:${t.action}:${t.value}` : `action:${t.action}`;
  if (t.id) return `id:${t.id}`;
  const tag = t.tag.toLowerCase();
  if (tag === 'a' && t.href) return `href:${t.href}`;
  if (tag === 'summary') return 'summary';
  return null;
}

function toState(value: unknown): RemoteState | null {
  if (!isObject(value) || !('snapshot' in value)) return null;
  const notices: unknown[] = Array.isArray(value.notices) ? value.notices : [];
  const state = { snapshot: value.snapshot ?? null, notices: notices.filter(isNotice), skua: parseSkua(value.skua), skuaInstances: parseSkuaInstances(value.skuaInstances) } as RemoteState;
  return isUsableState(state) ? state : null;
}

export function parseState(text: string): RemoteState | null {
  try {
    return toState(JSON.parse(text));
  } catch {
    return null;
  }
}

/** What is stored: the state and when it was received, so "Old data" can say how old it is after a reload. */
export function keepState(state: RemoteState, receivedAtMs: number): string {
  return JSON.stringify({ receivedAtMs, state });
}

/** Reads what keepState stored. A broken state is refused; a missing, broken or future time is dropped and the state kept. */
export function recallKept(text: string, nowMs: number): { state: RemoteState; receivedAtMs: number | null } | null {
  try {
    const value: unknown = JSON.parse(text);
    if (!isObject(value)) return null;
    const state = toState(value.state);
    if (!state) return null;
    const at = value.receivedAtMs;
    const valid = typeof at === 'number' && Number.isFinite(at) && at > 0 && at <= nowMs + CLOCK_SKEW_MS;
    return { state, receivedAtMs: valid ? at : null };
  } catch {
    return null;
  }
}

export function formatNumber(n: number): string {
  return formatNumberLocale(n);
}

export function formatCompact(n: number): string {
  for (let i = 0; i < UNITS.length; i += 1) {
    const [size, suffix] = UNITS[i]!;
    if (n < size) continue;
    const rounded = Math.round((n / size) * 10) / 10;
    // 999,999 rounds to 1000K: carry it to the unit above ("1M").
    if (rounded >= 1000 && i > 0) {
      const [bigger, biggerSuffix] = UNITS[i - 1]!;
      return `${Math.round((n / bigger) * 10) / 10}${biggerSuffix}`;
    }
    return `${rounded}${suffix}`;
  }
  return String(n);
}

export function relativeTime(iso: string, nowMs: number): string {
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return '';
  const seconds = (nowMs - then) / 1000;
  if (seconds < 45) return t('time.now');
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return t('time.minutesAgo', { n: Math.max(1, minutes) });
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return t('time.hoursAgo', { n: hours });
  return t('time.daysAgo', { n: Math.floor(hours / 24) });
}

/** How long the stream may stay down before the banner says SkuaLink cannot be reached (the same as the state request's timeout). */
export const UNREACHABLE_AFTER_MS = STATE_TIMEOUT_MS;

/** What the page knows about the link: when the stream went down (null if unknown) and whether the last state request failed or timed out. */
export interface LinkFacts {
  downSinceMs: number | null;
  requestFailed: boolean;
}

/** Fresh data proves the PC is reachable: the failure is forgotten and, with the stream closed, the down clock restarts from now. */
export function linkAfterFreshData(sseOpen: boolean, link: LinkFacts, nowMs: number): LinkFacts {
  return { downSinceMs: sseOpen ? link.downSinceMs : nowMs, requestFailed: false };
}

/**
 * Nothing while the event stream is open. Once the last state request failed or the stream has been down for more than 10 s, the
 * calm "Can't reach SkuaLink" warning, with the age of the shown data when it is known. Before that, a first visit with no data
 * shows "Connecting..."; otherwise the shown data is old, and the banner says how old when the time is known.
 */
export function linkBanner(sseOpen: boolean, lastUpdateMs: number | null, nowMs: number, hasData = true, link?: LinkFacts): Banner | null {
  if (sseOpen) return null;
  const age = lastUpdateMs === null || !Number.isFinite(lastUpdateMs) ? '' : relativeTime(new Date(lastUpdateMs).toISOString(), nowMs);
  const unreachable = !!link && (link.requestFailed || (link.downSinceMs !== null && nowMs - link.downSinceMs > UNREACHABLE_AFTER_MS));
  if (unreachable) return { tone: 'warn', text: age ? t('banner.unreachableAge', { age }) : t('banner.unreachable') };
  if (!hasData && lastUpdateMs === null) return { tone: 'neutral', text: t('banner.connecting') };
  return { tone: 'warn', text: age ? t('banner.oldWithAge', { age }) : t('banner.old') };
}

export function characterRow(c: Character, nowMs: number): Row {
  const last = c.lastLiveUpdateUtc ? Date.parse(c.lastLiveUpdateUtc) : Number.NaN;
  let kind: Row['kind'] = 'saved';
  let status = t('status.saved', { when: relativeTime(c.savedAtUtc, nowMs) });
  if (c.live && !Number.isNaN(last)) {
    const fresh = nowMs - last <= LIVE_WINDOW_MS;
    const when = relativeTime(c.lastLiveUpdateUtc!, nowMs);
    kind = fresh ? 'live' : 'idle';
    status = fresh ? t('status.live', { when }) : t('status.idle', { when });
  }
  return { name: c.name, level: c.level, kind, status, gold: formatCompact(c.figures.gold) };
}

export function characterRows(state: RemoteState | null, nowMs: number): Row[] {
  return (state?.snapshot?.characters ?? []).map((c) => characterRow(c, nowMs));
}

export function expFraction(f: Figures): number {
  if (f.expToLevel <= 0) return 0;
  return Math.min(1, Math.max(0, f.exp / f.expToLevel));
}

export function hardFarmsView(hf: HardFarms | null, showAll: boolean): HardFarmsView | null {
  if (!hf) return null;
  const shown = showAll ? hf.missing : hf.missing.slice(0, HARD_FARMS_SHOWN);
  return { owned: hf.owned, total: hf.total, fraction: hf.total > 0 ? hf.owned / hf.total : 0, shown, hidden: hf.missing.length - shown.length };
}

/** The Skua notices are worded by the page, with the label of the Skua they are about; the PC's own text is the fallback. */
const SKUA_NOTICES: Partial<Record<NoticeKind, readonly [title: MessageKey, body: MessageKey]>> = {
  SkuaDisconnected: ['notice.skuaDisconnected.title', 'notice.skuaDisconnected.body'],
  SkuaReconnected: ['notice.skuaReconnected.title', 'notice.skuaReconnected.body'],
  SkuaOptionsWaiting: ['notice.skuaOptionsWaiting.title', 'notice.skuaOptionsWaiting.body'],
};

export function noticeLine(n: Notice, nowMs: number): { title: string; body: string; when: string } {
  const own = Object.hasOwn(SKUA_NOTICES, n.kind) && n.character ? SKUA_NOTICES[n.kind] : undefined;
  if (own) return { title: t(own[0]), body: t(own[1], { label: n.character! }), when: relativeTime(n.atUtc, nowMs) };
  return { title: n.title, body: n.body, when: relativeTime(n.atUtc, nowMs) };
}

const isHigh = (code: number) => code >= 0xd800 && code <= 0xdbff;
const isLow = (code: number) => code >= 0xdc00 && code <= 0xdfff;

/** Replaces every lone surrogate with U+FFFD. A loop, not a regex: the lookbehind such a regex needs breaks Safari before 16.4. */
export function wellFormed(text: string): string {
  let out = '';
  for (let i = 0; i < text.length; i += 1) {
    const code = text.charCodeAt(i);
    if (isHigh(code) && isLow(text.charCodeAt(i + 1))) {
      out += text.slice(i, i + 2);
      i += 1;
    } else {
      out += isHigh(code) || isLow(code) ? '\uFFFD' : text[i];
    }
  }
  return out;
}

export function characterHref(name: string): string {
  try {
    return `#/c/${encodeURIComponent(name)}`;
  } catch {
    // A lone surrogate makes encodeURIComponent throw; show it as U+FFFD instead of failing the whole list.
    return `#/c/${encodeURIComponent(wellFormed(name))}`;
  }
}

export function parseRoute(hash: string): Route {
  if (hash.startsWith('#t=')) return { view: 'pair', token: hash.slice(3) };
  if (hash === '#/skua') return { view: 'skua' };
  if (hash.startsWith('#/c/')) {
    try {
      return { view: 'character', name: decodeURIComponent(hash.slice(4)) };
    } catch {
      return { view: 'list' };
    }
  }
  return { view: 'list' };
}

const IOS = /iPhone|iPad/;

/**
 * iPhone and iPad Safari never offer to install a page, so the page says how; not once it runs from the Home Screen. An iPad on
 * iPadOS 13 or later reports itself as a Mac: a "Macintosh" with a touch screen (more than one touch point) counts as an iPad.
 */
export function iosInstallHint(userAgent: string, standalone: boolean, maxTouchPoints = 0): string | null {
  const ios = IOS.test(userAgent) || (/Macintosh/.test(userAgent) && maxTouchPoints > 1);
  return ios && !standalone ? t('install.ios') : null;
}

export function deviceName(userAgent: string): string {
  if (/iPhone/.test(userAgent)) return 'iPhone';
  if (/iPad/.test(userAgent)) return 'iPad';
  if (/Android/.test(userAgent)) return 'Android phone';
  return 'Browser';
}
