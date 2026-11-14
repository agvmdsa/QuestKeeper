export class UnpairedError extends Error {}

/** How long the page waits for /api/state before it counts as no answer, so a stalled path cannot hold up the first load or the redraws. */
export const STATE_TIMEOUT_MS = 10_000;

/** What a failed /api/state says: only a 401 means this browser is not paired; a timeout or any other failure is no answer. */
export function probeOf(error: unknown): 'unpaired' | 'failed' {
  return error instanceof UnpairedError ? 'unpaired' : 'failed';
}

const hasNativeTimeout = (): boolean => typeof AbortSignal !== 'undefined' && typeof AbortSignal.timeout === 'function';

/** A signal that aborts with a TimeoutError after `ms`. AbortSignal.timeout arrived in Safari 16; older browsers get a timer, cleared by done(). */
export function timeoutSignal(ms: number, native = hasNativeTimeout()): { signal: AbortSignal; done(): void } {
  if (native) return { signal: AbortSignal.timeout(ms), done: () => undefined };
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(new DOMException('The operation timed out.', 'TimeoutError')), ms);
  return { signal: controller.signal, done: () => clearTimeout(timer) };
}

export type PairOutcome = 'paired' | 'invalid' | 'locked' | 'failed';

export interface EventHandlers {
  open(): void;
  error(closed: boolean): void;
  state(data: string): void;
  notice(data: string): void;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.method && init.method !== 'GET') {
    headers.set('X-Remote', '1');
    headers.set('Content-Type', 'application/json');
  }
  return fetch(path, { ...init, headers, cache: 'no-store', credentials: 'same-origin' });
}

export async function getState(timeoutMs = STATE_TIMEOUT_MS): Promise<string> {
  const timeout = timeoutSignal(timeoutMs);
  try {
    const response = await send('/api/state', { signal: timeout.signal });
    if (response.status === 401) throw new UnpairedError();
    if (!response.ok) throw new Error(`state ${response.status}`);
    return await response.text();
  } finally {
    timeout.done();
  }
}

export async function pair(token: string, name: string): Promise<PairOutcome> {
  const response = await send('/api/pair', { method: 'POST', body: JSON.stringify({ token, name }) });
  if (response.status === 204) return 'paired';
  if (response.status === 401) return 'invalid';
  if (response.status === 429) return 'locked';
  return 'failed';
}

export function openEvents(handlers: EventHandlers): EventSource {
  const source = new EventSource('/api/events');
  source.addEventListener('open', () => handlers.open());
  source.addEventListener('error', () => handlers.error(source.readyState === EventSource.CLOSED));
  source.addEventListener('state', (e) => handlers.state((e as MessageEvent<string>).data));
  source.addEventListener('notice', (e) => handlers.notice((e as MessageEvent<string>).data));
  return source;
}

/** What a Skua call answered: the HTTP status (0 when nothing came back in time) and the parsed JSON body, or null. */
export interface CallResult {
  status: number;
  data: unknown;
}

/** A Skua command may wait for Skua for up to 45 s and the PC allows 60 s; the page waits a little longer than the PC. */
export const SKUA_TIMEOUT_MS = 65_000;

const skuaPath = (instanceId: string, ...rest: string[]): string => ['/api/skua', encodeURIComponent(instanceId), ...rest].join('/');

async function skuaCall(path: string, body?: unknown): Promise<CallResult> {
  const timeout = timeoutSignal(SKUA_TIMEOUT_MS);
  try {
    const init: RequestInit = body === undefined ? { signal: timeout.signal } : { method: 'POST', body: JSON.stringify(body), signal: timeout.signal };
    const response = await send(path, init);
    let data: unknown = null;
    try {
      data = await response.json();
    } catch {
      // An empty or non-JSON body is no data.
    }
    return { status: response.status, data };
  } catch {
    return { status: 0, data: null };
  } finally {
    timeout.done();
  }
}

/** The Skua control routes of the PC; script ids are checked by the caller. */
export const skuaApi = {
  listScripts: (instanceId: string) => skuaCall(skuaPath(instanceId, 'scripts')),
  getOptions: (instanceId: string, scriptId: string) => skuaCall(skuaPath(instanceId, 'scripts', scriptId, 'options')),
  setOptions: (instanceId: string, scriptId: string, values: { category: string; name: string; value: string }[]) =>
    skuaCall(skuaPath(instanceId, 'scripts', scriptId, 'options'), { values }),
  setPrefs: (instanceId: string, scriptId: string, askOptions: boolean) => skuaCall(skuaPath(instanceId, 'scripts', scriptId, 'prefs'), { askOptions }),
  start: (instanceId: string, scriptId: string) => skuaCall(skuaPath(instanceId, 'start'), { id: scriptId }),
  stop: (instanceId: string) => skuaCall(skuaPath(instanceId, 'stop'), {}),
};

export type ControlApi = typeof skuaApi;
