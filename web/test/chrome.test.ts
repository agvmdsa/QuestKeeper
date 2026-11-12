import { describe, expect, it } from 'vitest';
import { publishChromeHeight, watchChromeHeight } from '../src/chrome';

const root = () => {
  const props = new Map<string, string>();
  return { props, style: { setProperty: (k: string, v: string) => void props.set(k, v) } as unknown as CSSStyleDeclaration };
};
const el = (height: number) => ({ getBoundingClientRect: () => ({ height }) as DOMRect });

describe('publishChromeHeight', () => {
  it('sets --chrome-h to the measured px', () => {
    const r = root();
    publishChromeHeight(r, el(96.5));
    expect(r.props.get('--chrome-h')).toBe('96.5px');
  });
  it.each([NaN, 0, -5, Infinity])('ignores a measure of %s', (h) => {
    const r = root();
    publishChromeHeight(r, el(h));
    expect(r.props.size).toBe(0);
  });
  it('does not throw without an element or when measuring throws', () => {
    expect(() => publishChromeHeight(root(), null)).not.toThrow();
    expect(() => publishChromeHeight(null, el(10))).not.toThrow();
    expect(() => publishChromeHeight(root(), { getBoundingClientRect: () => { throw new Error('x'); } })).not.toThrow();
  });
});

describe('watchChromeHeight', () => {
  it('does not throw without ResizeObserver or an element', () => {
    const r = root() as unknown as HTMLElement;
    expect(() => watchChromeHeight(r, el(50) as unknown as HTMLElement, undefined)).not.toThrow();
    expect(() => watchChromeHeight(r, null)).not.toThrow();
  });
  it('re-measures when the observer fires', () => {
    const r = root();
    let height = 50;
    let fire = () => {};
    class FakeObserver { constructor(cb: () => void) { fire = cb; } observe() {} }
    const chrome = { getBoundingClientRect: () => ({ height }) as DOMRect };
    watchChromeHeight(r as unknown as HTMLElement, chrome as unknown as HTMLElement, FakeObserver as unknown as typeof ResizeObserver);
    expect(r.props.get('--chrome-h')).toBe('50px');
    height = 100;
    fire();
    expect(r.props.get('--chrome-h')).toBe('100px');
  });
});
