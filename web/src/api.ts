export class UnpairedError extends Error {}

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

export async function getState(): Promise<string> {
  const response = await send('/api/state');
  if (response.status === 401) throw new UnpairedError();
  if (!response.ok) throw new Error(`state ${response.status}`);
  return response.text();
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
