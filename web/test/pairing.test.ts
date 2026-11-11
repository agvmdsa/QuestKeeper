import { afterEach, describe, expect, it, vi } from 'vitest';
import { getState, probeOf, STATE_TIMEOUT_MS, timeoutSignal, UnpairedError } from '../src/api';
import { PASTE_REJECTED, parsePairingInput } from '../src/state';
import { pairNeededView, type VNode } from '../src/views';

const TOKEN = 'Ab3_-9xYz0Ab3_-9xYz0Ab3_-9xYz0Ab3_-9xYz0Abc';

function all(node: VNode): VNode[] {
  return [node, ...node.children.flatMap(all)];
}

describe('parsePairingInput', () => {
  it('has a 43-character test token like the PC makes', () => expect(TOKEN).toHaveLength(43));
  it.each([
    ['a full link', `https://my-pc.tail1234.ts.net/#t=${TOKEN}`],
    ['a full link with capitals in the name', `https://My-PC.tail1234.ts.net/#t=${TOKEN}`],
    ['the hash only', `#t=${TOKEN}`],
    ['the bare token', TOKEN],
    ['spaces and a line break around it', `  https://my-pc.tail1234.ts.net/#t=${TOKEN}\n`],
  ])('takes the token from %s', (_name, text) => expect(parsePairingInput(text)).toBe(TOKEN));

  it.each([
    ['empty', ''],
    ['blank', '   '],
    ['a plain http link', `http://my-pc.tail1234.ts.net/#t=${TOKEN}`],
    ['a javascript link', `javascript:alert(1)//#t=${TOKEN}`],
    ['a data link', `data:text/html,x#t=${TOKEN}`],
    ['a link with a user name', `https://me:pw@my-pc.tail1234.ts.net/#t=${TOKEN}`],
    ['a link with a path', `https://my-pc.tail1234.ts.net/evil#t=${TOKEN}`],
    ['a link with a query', `https://my-pc.tail1234.ts.net/?x=1#t=${TOKEN}`],
    ['a link with a port', `https://my-pc.tail1234.ts.net:8443/#t=${TOKEN}`],
    ['a link without a token', 'https://my-pc.tail1234.ts.net/'],
    ['a link with another hash', `https://my-pc.tail1234.ts.net/#/c/${TOKEN}`],
    ['a short token', TOKEN.slice(1)],
    ['a long token', `${TOKEN}A`],
    ['a token with base64 padding', `${TOKEN.slice(0, 42)}=`],
    ['a token with a slash', `${TOKEN.slice(0, 42)}/`],
    ['markup', `<img src=x>${TOKEN}`],
    ['a token with something after it', `#t=${TOKEN}&next=1`],
    ['two tokens', `#t=${TOKEN} #t=${TOKEN}`],
    ['a space inside', `#t=${TOKEN.slice(0, 20)} ${TOKEN.slice(20)}`],
    ['full-width letters', TOKEN.replace('A', 'Ａ')],
    ['a lone surrogate', `${TOKEN.slice(0, 42)}\uD800`],
    ['a zero-width space', `${TOKEN.slice(0, 42)}​`],
    ['a huge paste', `#t=${TOKEN}`.padEnd(100_000, 'A')],
  ])('refuses %s', (_name, text) => expect(parsePairingInput(text)).toBeNull());

  it('says what to do with a paste it refused', () =>
    expect(PASTE_REJECTED).toBe('That is not a pairing link. Copy the whole link from Questkeeper on your PC and paste it here.'));
});

describe('probeOf', () => {
  it('only a 401 means not paired; a timeout or any other failure is a failed probe', () => {
    expect(probeOf(new UnpairedError())).toBe('unpaired');
    expect(probeOf(new DOMException('The operation timed out.', 'TimeoutError'))).toBe('failed');
    expect(probeOf(new DOMException('Aborted', 'AbortError'))).toBe('failed');
    expect(probeOf(new TypeError('Failed to fetch'))).toBe('failed');
    expect(probeOf(undefined)).toBe('failed');
  });
});

describe('getState', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('gives up after a bounded time so a stalled path cannot freeze the page', async () => {
    expect(STATE_TIMEOUT_MS).toBe(10_000);
    let seen: AbortSignal | undefined;
    vi.stubGlobal('fetch', (_path: string, init: RequestInit) => {
      seen = init.signal ?? undefined;
      return new Promise((_resolve, reject) => init.signal?.addEventListener('abort', () => reject(init.signal!.reason)));
    });
    const outcome = await getState(20).then(() => 'resolved', (error: unknown) => probeOf(error));
    expect(seen).toBeInstanceOf(AbortSignal);
    expect(outcome).toBe('failed');
  });

  it('still reports a 401 as not paired', async () => {
    vi.stubGlobal('fetch', async () => new Response('{"error":"not paired"}', { status: 401 }));
    await expect(getState()).rejects.toBeInstanceOf(UnpairedError);
  });
});

describe('timeoutSignal', () => {
  afterEach(() => vi.useRealTimers());

  it('uses AbortSignal.timeout where the browser has it', () => {
    const t = timeoutSignal(1000, true);
    expect(t.signal).toBeInstanceOf(AbortSignal);
    expect(t.signal.aborted).toBe(false);
    t.done();
  });
  it('falls back to a timer for Safari before 16, aborting with a TimeoutError', async () => {
    vi.useFakeTimers();
    const t = timeoutSignal(1000, false);
    await vi.advanceTimersByTimeAsync(999);
    expect(t.signal.aborted).toBe(false);
    await vi.advanceTimersByTimeAsync(1);
    expect(t.signal.aborted).toBe(true);
    expect((t.signal.reason as DOMException).name).toBe('TimeoutError');
    expect(probeOf(t.signal.reason)).toBe('failed');
  });
  it('clears the fallback timer once the request finished', async () => {
    vi.useFakeTimers();
    const t = timeoutSignal(1000, false);
    t.done();
    expect(vi.getTimerCount()).toBe(0);
    await vi.advanceTimersByTimeAsync(5000);
    expect(t.signal.aborted).toBe(false);
  });
});

describe('the paste field of the Not paired card', () => {
  const view = pairNeededView();
  const input = all(view).find((n) => n.tag === 'input');
  const label = all(view).find((n) => n.tag === 'label');
  const button = all(view).find((n) => n.attrs?.['data-action'] === 'pair-paste');

  it('is a plain labelled text field that the keyboard does not change', () => {
    expect(input?.attrs).toEqual({ id: 'pair-link', type: 'text', autocomplete: 'off', autocapitalize: 'off', spellcheck: 'false', enterkeyhint: 'go' });
    expect(label?.attrs).toEqual({ for: 'pair-link' });
    expect(label?.text).toBe('Pairing link');
  });
  it('has a Pair button that is not a form submit', () => {
    expect(button).toMatchObject({ tag: 'button', text: 'Pair', attrs: { type: 'button', 'data-action': 'pair-paste' } });
    expect(all(view).some((n) => n.tag === 'form')).toBe(false);
  });
  it('announces why a paste or a link was refused', () => {
    const reason = all(pairNeededView(PASTE_REJECTED)).find((n) => n.text === PASTE_REJECTED);
    expect(reason?.attrs).toEqual({ role: 'status' });
  });
  it('explains why an app on the Home Screen needs it', () => {
    expect(all(view).map((n) => n.text)).toContain(
      'Using Questkeeper from your Home Screen? The code opens the browser instead. Copy the pairing link (Copy link in Questkeeper on your PC) and paste it here.',
    );
  });
});
