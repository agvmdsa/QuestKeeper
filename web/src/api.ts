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
