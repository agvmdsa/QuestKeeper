import { watchChromeHeight } from './chrome';
import { applyTheme, loadTheme, saveTheme, parseTheme, type ThemeChoice } from './theme';
import './styles/tokens.css';
import './styles/app.css';
import { getState, openEvents, pair, probeOf, type PairOutcome } from './api';
import {
  afterStreamCheck, deviceName, focusKey, iosInstallHint, keepState, KEPT_KEY, linkBanner, pairFailureText, pairingStep, parseNotice, parsePairingInput, parseRoute,
  parseState,
  recallKept, reconnectDelay, streamErrorStep, takePairingToken, UNREACHABLE_AFTER_MS, type PairProbe,
} from './state';
import { parseLogFilter, type LogFilter } from './skua';
import { detectLocale, setLocale, t } from './i18n';
import type { Notice, RemoteState } from './types';
import { characterView, drawFailedView, listView, mount, pairNeededView, skuaView, withFooter, type Model, type VNode } from './views';

// First thing: CSP forbids an inline script, so the page may flash the system theme until this runs.
function storage(): Storage | null {
  try {
    return localStorage;
  } catch {
    return null;
  }
}
let theme: ThemeChoice = loadTheme(storage());
applyTheme(document.documentElement, theme);

watchChromeHeight(document.documentElement, document.querySelector<HTMLElement>('.chrome'));

setLocale(detectLocale(navigator.languages?.length ? navigator.languages : [navigator.language ?? 'en']));

const app = document.getElementById('app')!;
const banner = document.getElementById('banner')!;
// What an earlier page on this address may have left: removed, never read.
const OLD_KEYS = ['qkr-last-state', 'qkr-last-state-v1'];
const standalone = (navigator as Navigator & { standalone?: boolean }).standalone === true || matchMedia('(display-mode: standalone)').matches;
// iPadOS 13 and later report a Mac; the touch points tell an iPad apart.
const installHint = iosInstallHint(navigator.userAgent, standalone, navigator.maxTouchPoints ?? 0);

let state: RemoteState | null = null;
let notices: Notice[] = [];
let lastUpdateMs: number | null = null;
let sseOpen = false;
// When the stream went down (the page starts without one) and whether the last state request failed: they decide "Can't reach Questkeeper".
let downSinceMs: number | null = Date.now();
let requestFailed = false;
let unreachableTimer: ReturnType<typeof setTimeout> | undefined;
let unpaired = false;
let pairReason: string | null = null;
let showAll = false;
let hfOpen = false;
// The Skua log filter lives in memory only: a reload starts at Everything.
let skuaFilter: LogFilter = 'all';
let source: EventSource | null = null;
let attempts = 0;
let retryTimer: ReturnType<typeof setTimeout> | undefined;
let lastBanner = '';
let lastTree = '';

// Storage can be switched off or full: the page works without it.
function remember(kept: RemoteState, receivedAtMs: number): void {
  try {
    localStorage.setItem(KEPT_KEY, keepState(kept, receivedAtMs));
  } catch {
    // Not kept; the next load waits for the PC instead.
  }
}

function forget(): void {
  try {
    localStorage.removeItem(KEPT_KEY);
  } catch {
    // Nothing to remove.
  }
}

function recall(): ReturnType<typeof recallKept> {
  try {
    for (const key of OLD_KEYS) localStorage.removeItem(key);
    const text = localStorage.getItem(KEPT_KEY);
    if (text === null) return null;
    const kept = recallKept(text, Date.now());
    if (!kept) localStorage.removeItem(KEPT_KEY);
    return kept;
  } catch {
    return null;
  }
}

/** Redraws just after the stream has been down for 10 s, so the banner changes without waiting for the 15 s tick or new data. */
function armUnreachableTimer(): void {
  clearTimeout(unreachableTimer);
  if (sseOpen || downSinceMs === null) return;
  const wait = downSinceMs + UNREACHABLE_AFTER_MS + 1 - Date.now();
  if (wait > 0) unreachableTimer = setTimeout(draw, wait);
}

function markDown(): void {
  sseOpen = false;
  downSinceMs ??= Date.now();
  armUnreachableTimer();
}

function drawBanner(): void {
  const note = unpaired ? null : linkBanner(sseOpen, lastUpdateMs, Date.now(), state !== null, { downSinceMs, requestFailed });
  const key = note ? `${note.tone}|${note.text}` : '';
  if (key === lastBanner) return;
  lastBanner = key;
  banner.hidden = !note;
  if (note) {
    banner.textContent = note.text;
    banner.dataset.tone = note.tone;
  } else {
    banner.textContent = '';
  }
}

function tree(): VNode {
  const model: Model = { state: state && { ...state, notices }, nowMs: Date.now(), showAll, hfOpen, installHint, theme, skuaFilter };
  if (unpaired) return withFooter(pairNeededView(pairReason, installHint), model);
  const route = parseRoute(location.hash);
  if (route.view === 'skua') return withFooter(skuaView(model), model);
  return withFooter(route.view === 'character' ? characterView(model, route.name) : listView(model), model);
}

function keyOf(el: HTMLElement): string | null {
  return focusKey({ tag: el.tagName, action: el.getAttribute('data-action'), href: el.getAttribute('href'), id: el.id, value: el.getAttribute('data-value') });
}

function focusedKey(): string | null {
  const el = document.activeElement;
  if (!(el instanceof HTMLElement) || !app.contains(el)) return null;
  return keyOf(el);
}

function restoreFocus(key: string | null): void {
  if (!key) return;
  for (const el of app.querySelectorAll<HTMLElement>('a, button, summary, input')) {
    if (keyOf(el) === key) {
      el.focus({ preventScroll: true });
      return;
    }
  }
}

/** Redraws only when what is shown changed, keeps keyboard focus, and never leaves a blank page. Returns false when the data could not be shown. */
function draw(): boolean {
  let ok = true;
  let next: VNode;
  try {
    drawBanner();
    next = tree();
  } catch {
    ok = false;
    next = drawFailedView();
  }
  const signature = JSON.stringify(next);
  if (signature === lastTree) return ok;
  const key = focusedKey();
  try {
    app.replaceChildren(mount(next));
    lastTree = signature;
  } catch {
    ok = false;
    app.replaceChildren(mount(drawFailedView()));
    lastTree = '';
  }
  restoreFocus(key);
  return ok;
}

function setState(text: string): void {
  const parsed = parseState(text);
  if (!parsed) return;
  state = parsed;
  notices = parsed.notices;
  lastUpdateMs = Date.now();
  requestFailed = false;
  // Kept only once it has been drawn, so data that breaks the page is never stored and replayed on the next load.
  if (draw()) remember(parsed, lastUpdateMs);
}

function addNotice(text: string): void {
  const notice = parseNotice(text);
  if (!notice) return;
  if (!notices.some((n) => n.id === notice.id)) notices = [notice, ...notices].slice(0, 50);
  draw();
}

function closeStream(): void {
  clearTimeout(retryTimer);
  source?.close();
  source = null;
  markDown();
}

/** A device the PC no longer knows keeps nothing: names and gold are dropped from memory and storage. */
function becomeUnpaired(): void {
  unpaired = true;
  state = null;
  notices = [];
  lastUpdateMs = null;
  forget();
  closeStream();
  draw();
}

function scheduleReconnect(): void {
  clearTimeout(retryTimer);
  retryTimer = setTimeout(() => {
    if (!source && !unpaired) openStream();
  }, reconnectDelay(attempts));
  attempts += 1;
}

function openStream(): void {
  source = openEvents({
    open: () => {
      sseOpen = true;
      downSinceMs = null;
      requestFailed = false;
      clearTimeout(unreachableTimer);
      unpaired = false;
      attempts = 0;
      draw();
    },
    error: (closed) => {
      markDown();
      draw();
      if (streamErrorStep(closed) === 'wait') return; // EventSource retries by itself
      source?.close();
      source = null;
      let probe: PairProbe = 'failed';
      getState()
        .then((text) => {
          probe = 'paired';
          setState(text);
        })
        .catch((error: unknown) => {
          probe = probeOf(error);
          if (probe !== 'unpaired') {
            requestFailed = true;
            draw();
          }
        })
        .finally(() => {
          if (afterStreamCheck(probe) === 'unpaired') becomeUnpaired();
          else if (!unpaired && !source) scheduleReconnect();
        });
    },
    state: setState,
    notice: addNotice,
  });
}

async function connect(): Promise<void> {
  try {
    const text = await getState();
    unpaired = false;
    pairReason = null;
    setState(text);
  } catch (error) {
    if (probeOf(error) === 'unpaired') {
      becomeUnpaired();
      return;
    }
    requestFailed = true;
    draw();
  }
  if (!source) openStream();
}

/** Spends a one-time token unless this browser is already paired, then connects. Shared by a `#t=` link and a pasted link. */
async function pairWith(token: string): Promise<void> {
  // A browser that is already paired keeps its pairing: the token is not spent and no second device entry is made.
  let probe: PairProbe;
  try {
    await getState();
    probe = 'paired';
  } catch (error) {
    probe = probeOf(error);
  }
  if (pairingStep(probe) === 'pair') {
    let outcome: PairOutcome = 'failed';
    try {
      outcome = await pair(token, deviceName(navigator.userAgent));
    } catch {
      outcome = 'failed';
    }
    pairReason = pairFailureText(outcome);
    if (outcome === 'paired') unpaired = false;
    draw();
  }
  await connect();
}

/** Pairs from a `#t=` link, at load or when the link is opened in a tab that already shows the page. */
async function tryPairFromHash(): Promise<boolean> {
  const taken = takePairingToken(location.hash);
  if (!taken) return false;
  // The token is single-use: take it out of the address bar and history before anything can fail.
  history.replaceState(null, '', location.pathname + location.search + taken.cleanHash);
  await pairWith(taken.token);
  return true;
}

let pasting = false;

/**
 * Pairs from the link pasted in the Not paired card: a Home Screen app has storage of its own and a scanned code opens the browser,
 * so pasting is the only way in for it. The text is read once and cleared; it is never logged or put in the address bar.
 */
function pairFromPaste(): void {
  const input = document.getElementById('pair-link');
  if (!(input instanceof HTMLInputElement) || pasting) return;
  const token = parsePairingInput(input.value);
  input.value = '';
  if (!token) {
    pairReason = t('pair.pasteRejected');
    draw();
    return;
  }
  pasting = true;
  pairWith(token)
    .catch(() => undefined)
    .finally(() => {
      pasting = false;
    });
}

async function start(): Promise<void> {
  setInterval(draw, 15_000);
  const kept = recall();
  if (kept) {
    state = kept.state;
    notices = kept.state.notices;
    lastUpdateMs = kept.receivedAtMs;
    draw();
  }
  armUnreachableTimer();
  try {
    if (!(await tryPairFromHash())) await connect();
  } catch {
    // connect and tryPairFromHash handle their own failures; nothing is left to report.
  }
}

app.addEventListener('click', (event) => {
  const themeButton = (event.target as HTMLElement).closest('[data-action="theme"]');
  if (themeButton) {
    theme = parseTheme(themeButton.getAttribute('data-value'));
    saveTheme(storage(), theme);
    applyTheme(document.documentElement, theme);
    draw();
    return;
  }
  const filterButton = (event.target as HTMLElement).closest('[data-action="skua-filter"]');
  if (filterButton) {
    skuaFilter = parseLogFilter(filterButton.getAttribute('data-value'));
    draw();
    return;
  }
  if ((event.target as HTMLElement).closest('[data-action="pair-paste"]')) {
    pairFromPaste();
    return;
  }
  if ((event.target as HTMLElement).closest('[data-action="toggle-all"]')) {
    showAll = !showAll;
    hfOpen = true;
    draw();
  }
});
app.addEventListener('keydown', (event) => {
  if (event.key === 'Enter' && (event.target as HTMLElement).id === 'pair-link') {
    event.preventDefault();
    pairFromPaste();
  }
});
app.addEventListener('toggle', (event) => {
  if ((event.target as HTMLElement).tagName === 'DETAILS') hfOpen = (event.target as HTMLDetailsElement).open;
}, true);
window.addEventListener('hashchange', () => {
  if (takePairingToken(location.hash)) {
    tryPairFromHash().catch(() => undefined);
    return;
  }
  showAll = false;
  hfOpen = false;
  draw();
});

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => undefined);
}
draw();
void start();
