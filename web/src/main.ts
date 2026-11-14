import { watchChromeHeight } from './chrome';
import { applyTheme, loadTheme, saveTheme, parseTheme, type ThemeChoice } from './theme';
import './styles/tokens.css';
import './styles/app.css';
import { getState, openEvents, pair, probeOf, skuaApi, type PairOutcome } from './api';
import type { Control } from './control';
import {
  afterStreamCheck, deviceName, focusKey, iosInstallHint, keepState, KEPT_KEY, linkAfterFreshData, linkBanner, pairFailureText, pairingStep, parseNotice, parsePairingInput, parseRoute,
  parseState,
  recallKept, reconnectDelay, streamErrorStep, takePairingToken, UNREACHABLE_AFTER_MS, type PairProbe,
} from './state';
import { followSelection, loadInstance, neighbourId, parseLogFilter, saveInstance, type LogFilter } from './skua';
import { detectLocale, setLocale, t } from './i18n';
import type { Notice, RemoteState, SkuaInstance } from './types';
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
// When the stream went down (the page starts without one) and whether the last state request failed: they decide "Can't reach SkuaLink".
let downSinceMs: number | null = Date.now();
let requestFailed = false;
let unreachableTimer: ReturnType<typeof setTimeout> | undefined;
let unpaired = false;
let pairReason: string | null = null;
let showAll = false;
let hfOpen = false;
// The Skua log filter lives in memory only: a reload starts at Everything.
let skuaFilter: LogFilter = 'all';
// The Skua last chosen, by its label; remembered across visits.
let skuaInstance: string | null = loadInstance(storage());
// The Skua chosen in this session, by id: it stays chosen when its label changes (Skua N to the character's name).
let skuaInstanceId: string | null = null;
// Starting and stopping Skua scripts: loaded with the Skua tab (a chunk of its own); every change redraws the page and the open sheet.
type ControlUi = typeof import('./controlViews');
let ui: ControlUi | null = null;
let control: Control | null = null;
let loadingControl = false;

function loadControl(): void {
  if (loadingControl) return;
  loadingControl = true;
  import('./controlViews')
    .then((loaded) => {
      ui = loaded;
      control = new loaded.Control(skuaApi, () => {
        draw();
        drawSheet();
      }, () => becomeUnpaired());
      syncSelection();
      draw();
    })
    .catch(() => {
      // Offline with nothing cached: the Skua tab shows no Control card; the next visit to the tab tries again.
      loadingControl = false;
    });
}
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
  const note = unpaired ? null : linkBanner(sseOpen, lastUpdateMs, Date.now(), state !== null, { downSinceMs, requestFailed }) ?? (ui && control ? ui.controlBanner(Date.now(), control.noAnswerUntil) : null);
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
  const nowMs = Date.now();
  const shown = ui && control ? { draw: ui.controlCard, control } : null;
  const controlCard = shown ? (i: SkuaInstance) => shown.draw(i, shown.control, nowMs) : undefined;
  const model: Model = { state: state && { ...state, notices }, nowMs, showAll, hfOpen, installHint, theme, skuaFilter, skuaInstance, skuaInstanceId, controlCard };
  if (unpaired) return withFooter(pairNeededView(pairReason, installHint), model);
  const route = parseRoute(location.hash);
  if (route.view === 'skua') {
    loadControl();
    return withFooter(skuaView(model), model);
  }
  return withFooter(route.view === 'character' ? characterView(model, route.name) : listView(model), model);
}

function keyOf(el: HTMLElement): string | null {
  return focusKey({ tag: el.tagName, action: el.getAttribute('data-action'), href: el.getAttribute('href'), id: el.id, value: el.getAttribute('data-value') });
}

function focusedKey(root: HTMLElement = app): string | null {
  const el = document.activeElement;
  if (!(el instanceof HTMLElement) || !root.contains(el)) return null;
  return keyOf(el);
}

function restoreFocus(key: string | null, root: HTMLElement = app): boolean {
  if (!key) return false;
  for (const el of root.querySelectorAll<HTMLElement>('a, button, summary, input, select')) {
    if (keyOf(el) === key) {
      el.focus({ preventScroll: true });
      return true;
    }
  }
  return false;
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
  const chipScroll = app.querySelector('.chips')?.scrollLeft ?? 0;
  try {
    app.replaceChildren(mount(next));
    lastTree = signature;
  } catch {
    ok = false;
    app.replaceChildren(mount(drawFailedView()));
    lastTree = '';
  }
  restoreFocus(key);
  keepChipVisible(chipScroll);
  return ok;
}

let shownChip: string | null = null;

/** A redraw makes a new chip row: it gets its scroll position back, and a newly chosen chip is scrolled into view. */
function keepChipVisible(scrollLeft: number): void {
  const row = app.querySelector<HTMLElement>('.chips');
  const chip = row?.querySelector<HTMLElement>('[aria-selected="true"]');
  if (!row || !chip) {
    shownChip = null;
    return;
  }
  row.scrollLeft = scrollLeft;
  markOverflow();
  const value = chip.getAttribute('data-value');
  if (value !== shownChip && typeof chip.scrollIntoView === 'function') chip.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  shownChip = value;
}

/** The right fade of the chip row shows only while there are chips hidden on the right. */
function markOverflow(): void {
  const row = app.querySelector<HTMLElement>('.chips');
  if (!row) return;
  const more = row.scrollWidth - row.clientWidth - row.scrollLeft > 1;
  row.parentElement?.setAttribute('data-overflow', String(more));
}

/** The Skua the tab shows; the selection follows the chosen instance and remembers its current label. */
function syncSelection(): void {
  const list = state?.skuaInstances;
  if (!list) return;
  const next = followSelection(list, { label: skuaInstance, id: skuaInstanceId });
  skuaInstanceId = next.id;
  if (next.label !== null && next.label !== skuaInstance) {
    skuaInstance = next.label;
    saveInstance(storage(), next.label);
  }
  control?.track(next.instance);
}

const currentInstance = () => followSelection(state?.skuaInstances ?? [], { label: skuaInstance, id: skuaInstanceId }).instance;

function setState(text: string): void {
  const parsed = parseState(text);
  if (!parsed) return;
  state = parsed;
  syncSelection();
  notices = parsed.notices;
  lastUpdateMs = Date.now();
  ({ downSinceMs, requestFailed } = linkAfterFreshData(sseOpen, { downSinceMs, requestFailed }, lastUpdateMs));
  armUnreachableTimer();
  // Kept only once it has been drawn, so data that breaks the page is never stored and replayed on the next load.
  if (draw()) remember(parsed, lastUpdateMs);
  drawSheet();
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
  if (control?.sheet) control.close();
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
  const instanceButton = (event.target as HTMLElement).closest('[data-action="skua-instance"]');
  if (instanceButton) {
    chooseInstance(instanceButton.getAttribute('data-value'));
    return;
  }
  const controlButton = (event.target as HTMLElement).closest<HTMLElement>('[data-action="control-start"], [data-action="control-stop"]');
  const chosen = currentInstance();
  if (controlButton && chosen && control) {
    opener = keyOf(controlButton);
    if (controlButton.getAttribute('data-action') === 'control-stop') control.openStop(chosen);
    else control.openStart(chosen);
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
app.addEventListener('change', (event) => {
  const box = event.target as HTMLInputElement;
  const chosen = currentInstance();
  if (box.getAttribute('data-action') === 'ask-options' && chosen && control) control.setAsk(chosen.instanceId, box.getAttribute('data-value') ?? '', box.checked);
});
app.addEventListener('scroll', (event) => {
  if ((event.target as HTMLElement).classList?.contains('chips')) markOverflow();
}, true);
window.addEventListener('resize', markOverflow);

/** Chooses a Skua by its instance id and remembers its label. */
function chooseInstance(id: string | null): void {
  const chosen = state?.skuaInstances?.find((i) => i.instanceId === id);
  if (!chosen) return;
  skuaInstanceId = chosen.instanceId;
  skuaInstance = chosen.label;
  saveInstance(storage(), chosen.label);
  control?.track(chosen);
  draw();
}

const TAB_KEYS = ['ArrowRight', 'ArrowLeft', 'Home', 'End'];

app.addEventListener('keydown', (event) => {
  const tab = (event.target as HTMLElement).closest<HTMLElement>('[role="tab"]');
  if (tab && TAB_KEYS.includes(event.key)) {
    event.preventDefault();
    const list = state?.skuaInstances ?? [];
    const at = currentInstance()?.instanceId ?? '';
    const id = event.key === 'Home' ? list[0]?.instanceId : event.key === 'End' ? list[list.length - 1]?.instanceId : neighbourId(list, at, event.key === 'ArrowRight' ? 1 : -1);
    chooseInstance(id ?? null);
    app.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')?.focus();
    return;
  }
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
  if (control?.sheet) control.close();
  draw();
});

// The sheets (stop confirmation, script list, options) live in one native dialog: modal, so focus stays inside and Escape closes it.
const sheet = document.createElement('dialog');
sheet.className = 'sheet';
sheet.setAttribute('aria-labelledby', 'sheet-title');
document.body.append(sheet);
let lastSheet = '';
let lastKind = '';
// False where the browser has no modal dialog: the sheet is then only shown, and Escape is handled here.
let modal = false;
// The control that opened the sheet; it gets focus back when the sheet closes.
let opener: string | null = null;

function drawSheet(): void {
  const live = state?.skuaInstances?.find((i) => i.instanceId === control?.sheet?.instanceId);
  const view = ui && control ? ui.sheetView(control, live) : null;
  if (!view) {
    if (!lastSheet) return;
    lastSheet = '';
    lastKind = '';
    if (sheet.open) sheet.close();
    sheet.removeAttribute('open');
    sheet.replaceChildren();
    if (!restoreFocus(opener)) app.querySelector<HTMLElement>('#control-title')?.focus();
    opener = null;
    return;
  }
  const signature = JSON.stringify(view);
  if (signature === lastSheet) return;
  const fresh = control?.sheet?.kind !== lastKind;
  const opening = !lastSheet;
  lastSheet = signature;
  lastKind = control?.sheet?.kind ?? '';
  const active = document.activeElement;
  // A field being typed in is kept as it is (caret, composition), not drawn anew.
  const kept = !fresh && (active instanceof HTMLInputElement || active instanceof HTMLSelectElement) && sheet.contains(active) && active.id ? active : null;
  const key = fresh ? null : focusedKey(sheet);
  sheet.replaceChildren(mount(view));
  if (kept) document.getElementById(kept.id)?.replaceWith(kept);
  if (opening) {
    try {
      sheet.showModal();
      modal = true;
    } catch {
      sheet.setAttribute('open', '');
      modal = false;
    }
  }
  if (fresh) {
    sheet.scrollTop = 0;
    sheet.querySelector<HTMLElement>('[autofocus]')?.focus();
  }
  else if (kept) kept.focus({ preventScroll: true });
  else restoreFocus(key, sheet);
}

sheet.addEventListener('cancel', (event) => {
  event.preventDefault();
  control?.close();
});
// A browser may close the dialog by itself (a second Escape); the sheet follows.
sheet.addEventListener('close', () => {
  if (control?.sheet) control?.close();
});
sheet.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && !modal) control?.close();
});
sheet.addEventListener('click', (event) => {
  const target = event.target as HTMLElement;
  if (target === sheet) {
    control?.close(); // a tap on the backdrop
    return;
  }
  const button = target.closest<HTMLElement>('[data-action]');
  const action = button?.getAttribute('data-action');
  if (action === 'sheet-close') control?.close();
  else if (action === 'script-pick') control?.pick(button!.getAttribute('data-value') ?? '');
  else if (action === 'stop-confirm') control?.confirmStop();
  else if (action === 'options-start') control?.submitOptions();
});
function onSheetField(event: Event): void {
  const field = event.target as HTMLInputElement | HTMLSelectElement;
  const action = field.getAttribute('data-action');
  if (action === 'script-search') control?.setQuery(field.value);
  else if (action === 'opt') control?.setValue(Number(field.getAttribute('data-value')), field instanceof HTMLInputElement && field.type === 'checkbox' ? String(field.checked) : field.value);
  else if (action === 'dont-ask' && field instanceof HTMLInputElement) control?.setDontAsk(field.checked);
}
// Both: an older browser may not send input for a switch or a choice; the second call changes nothing.
sheet.addEventListener('input', onSheetField);
sheet.addEventListener('change', onSheetField);

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => undefined);
}
draw();
void start();
