import { afterEach, describe, expect, it, vi } from 'vitest';
import { en } from '../src/i18n/en';
import { parseRoute, retryImport } from '../src/state';
import { fallbackNav, type VNode } from '../src/views';

const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];

describe('fallback navigation until the dock is mounted', () => {
  it('is a labelled nav with real links to Characters and Skua, texts from the catalog', () => {
    const v = fallbackNav(parseRoute('#/'), false)!;
    expect(v.tag).toBe('nav');
    expect(v.attrs!['aria-label']).toBe(en['dock.label']);
    const links = all(v).filter((n) => n.tag === 'a');
    expect(links.map((a) => [a.attrs!.href, a.text])).toEqual([['#/', en['list.title']], ['#/skua', en['skua.title']]]);
  });
  it.each([['#/', '#/'], ['#/c/Hero', '#/'], ['#/skua', '#/skua']])('marks the page of %s as current', (hash, href) => {
    const marked = all(fallbackNav(parseRoute(hash), false)!).filter((n) => n.attrs?.['aria-current'] === 'page');
    expect(marked.map((n) => n.attrs!.href)).toEqual([href]);
  });
  it('is absent where the dock would be: the Not paired screen and a pairing link', () => {
    expect(fallbackNav(parseRoute('#/'), true)).toBeNull();
    expect(fallbackNav(parseRoute('#t=abc'), false)).toBeNull();
  });
});

describe('retrying the dock chunk', () => {
  afterEach(() => vi.useRealTimers());
  it('tries three times with a growing wait, then gives up with the last error', async () => {
    vi.useFakeTimers();
    const load = vi.fn(() => Promise.reject(new Error('offline')));
    const done = retryImport(load).catch((e: Error) => e.message);
    await vi.advanceTimersByTimeAsync(0);
    expect(load).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(499);
    expect(load).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(1);
    expect(load).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1000);
    expect(load).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(load).toHaveBeenCalledTimes(3);
    expect(await done).toBe('offline');
  });
  it('stops at the first success', async () => {
    vi.useFakeTimers();
    let n = 0;
    const load = vi.fn(() => (++n < 2 ? Promise.reject(new Error('x')) : Promise.resolve('dock')));
    const done = retryImport(load);
    await vi.advanceTimersByTimeAsync(2000);
    expect(await done).toBe('dock');
    expect(load).toHaveBeenCalledTimes(2);
  });
});
