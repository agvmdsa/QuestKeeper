import type { PairOutcome } from './api';
import type { Character, Figures, HardFarms, Notice, RemoteState } from './types';

export interface Banner {
  tone: 'warn';
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

export type Route = { view: 'list' } | { view: 'character'; name: string } | { view: 'pair'; token: string };

/** The localStorage key of the last state shown; a new shape gets a new version. */
export const KEPT_KEY = 'qkp-last-state-v1';

const UNITS: [number, string][] = [[1e9, 'B'], [1e6, 'M'], [1e3, 'K']];
const HARD_FARMS_SHOWN = 20;
const LIVE_WINDOW_MS = 120_000;
const CLOCK_SKEW_MS = 60_000;
const RECONNECT_MS = [2000, 4000, 8000, 15000];
const PAIR_REJECTED = 'The pairing link was not accepted. Create a new one in Questkeeper on your PC.';
const PAIR_FAILURE: Record<PairOutcome, string | null> = {
  paired: null,
  locked: 'Too many attempts. Wait a minute and open the pairing link again.',
  invalid: PAIR_REJECTED,
  failed: PAIR_REJECTED,
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

/** A pairing link puts a one-time token in the hash; the caller removes it from the URL before using it. */
export function takePairingToken(hash: string): { token: string; cleanHash: string } | null {
  return hash.startsWith('#t=') ? { token: hash.slice(3), cleanHash: '' } : null;
}

export function pairFailureText(outcome: PairOutcome): string | null {
  return PAIR_FAILURE[outcome];
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
}

/** Names a focused control so the same control can get focus back after a redraw. */
export function focusKey(t: FocusTarget): string | null {
  if (t.action) return `action:${t.action}`;
  const tag = t.tag.toLowerCase();
  if (tag === 'a' && t.href) return `href:${t.href}`;
  if (tag === 'summary') return 'summary';
  return null;
}

function toState(value: unknown): RemoteState | null {
  if (!isObject(value) || !('snapshot' in value)) return null;
  const notices: unknown[] = Array.isArray(value.notices) ? value.notices : [];
  const state = { snapshot: value.snapshot ?? null, notices: notices.filter(isNotice) } as RemoteState;
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
  return String(Math.trunc(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
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
  if (seconds < 45) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${Math.max(1, minutes)} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  return `${Math.floor(hours / 24)} d ago`;
}

/** Nothing while the event stream is open; otherwise the shown data is old, and the banner says how old when the time is known. */
export function linkBanner(sseOpen: boolean, lastUpdateMs: number | null, nowMs: number): Banner | null {
  if (sseOpen) return null;
  const age = lastUpdateMs === null || !Number.isFinite(lastUpdateMs) ? '' : relativeTime(new Date(lastUpdateMs).toISOString(), nowMs);
  return { tone: 'warn', text: age ? `Old data - last update ${age}. Reconnecting...` : 'Old data - reconnecting...' };
}

export function characterRow(c: Character, nowMs: number): Row {
  const last = c.lastLiveUpdateUtc ? Date.parse(c.lastLiveUpdateUtc) : Number.NaN;
  let kind: Row['kind'] = 'saved';
  let status = `Saved ${relativeTime(c.savedAtUtc, nowMs)}`;
  if (c.live && !Number.isNaN(last)) {
    const fresh = nowMs - last <= LIVE_WINDOW_MS;
    const when = relativeTime(c.lastLiveUpdateUtc!, nowMs);
    kind = fresh ? 'live' : 'idle';
    status = fresh ? `Live - activity ${when}` : `Idle - last activity ${when}`;
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

export function noticeLine(n: Notice, nowMs: number): { title: string; body: string; when: string } {
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

/** iPhone and iPad Safari never offer to install a page, so the page says how; not once it runs from the Home Screen. */
export function iosInstallHint(userAgent: string, standalone: boolean): string | null {
  return IOS.test(userAgent) && !standalone ? 'Install: tap Share, then Add to Home Screen.' : null;
}

export function deviceName(userAgent: string): string {
  if (/iPhone/.test(userAgent)) return 'iPhone';
  if (/iPad/.test(userAgent)) return 'iPad';
  if (/Android/.test(userAgent)) return 'Android phone';
  return 'Browser';
}
