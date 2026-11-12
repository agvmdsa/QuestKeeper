import { describe, expect, it } from 'vitest';
import { en } from '../src/i18n/en';
import { applyTheme, loadTheme, parseTheme, saveTheme, THEME_KEY } from '../src/theme';
import { themeFooter, withFooter, h, type VNode } from '../src/views';

const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];

function fakeRoot() {
  const attrs = new Map<string, string>();
  return { attrs, setAttribute: (k: string, v: string) => void attrs.set(k, v), removeAttribute: (k: string) => void attrs.delete(k) };
}
const blocked = {
  getItem: (): string | null => {
    throw new Error('blocked');
  },
  setItem: (): void => {
    throw new Error('blocked');
  },
};
function memory(initial?: string) {
  const data = new Map<string, string>(initial === undefined ? [] : [[THEME_KEY, initial]]);
  return { data, getItem: (k: string) => data.get(k) ?? null, setItem: (k: string, v: string) => void data.set(k, v) };
}

describe('theme', () => {
  it('uses the versioned key', () => expect(THEME_KEY).toBe('qkp-theme-v1'));
  it.each([['light', 'light'], ['dark', 'dark'], ['system', 'system'], [null, 'system'], ['', 'system'], ['Dark', 'system'], ['blue', 'system']] as const)(
    'parseTheme(%j) is %s', (raw, want) => expect(parseTheme(raw)).toBe(want));
  it('applies explicit choices as data-theme and removes it for system', () => {
    const root = fakeRoot();
    applyTheme(root, 'dark');
    expect(root.attrs.get('data-theme')).toBe('dark');
    applyTheme(root, 'light');
    expect(root.attrs.get('data-theme')).toBe('light');
    applyTheme(root, 'system');
    expect(root.attrs.has('data-theme')).toBe(false);
  });
  it('loads and saves through storage', () => {
    const s = memory();
    expect(loadTheme(s)).toBe('system');
    saveTheme(s, 'dark');
    expect(s.data.get(THEME_KEY)).toBe('dark');
    expect(loadTheme(s)).toBe('dark');
    expect(loadTheme(memory('garbage'))).toBe('system');
  });
  it('never throws when storage is blocked or missing', () => {
    expect(loadTheme(blocked)).toBe('system');
    expect(() => saveTheme(blocked, 'dark')).not.toThrow();
    expect(loadTheme(null)).toBe('system');
    expect(() => saveTheme(null, 'light')).not.toThrow();
  });
});

describe('theme footer', () => {
  const buttons = (n: VNode) => all(n).filter((x) => x.tag === 'button');
  it('is a labelled group of three buttons from the catalog', () => {
    const f = themeFooter({ theme: 'light' });
    expect(f.attrs).toMatchObject({ role: 'group', 'aria-label': en['theme.group'] });
    expect(en['theme.group']).toBe('Appearance');
    expect(buttons(f).map((b) => b.text)).toEqual(['System', 'Light', 'Dark']);
    expect(buttons(f).map((b) => b.attrs!['data-value'])).toEqual(['system', 'light', 'dark']);
    for (const b of buttons(f)) expect(b.attrs).toMatchObject({ 'data-action': 'theme', type: 'button' });
  });
  it.each(['system', 'light', 'dark'] as const)('presses exactly the %s button', (choice) => {
    const pressed = buttons(themeFooter({ theme: choice })).filter((b) => b.attrs!['aria-pressed'] === 'true');
    expect(pressed.map((b) => b.attrs!['data-value'])).toEqual([choice]);
  });
  it('defaults to system', () => {
    expect(buttons(themeFooter({})).filter((b) => b.attrs!['aria-pressed'] === 'true')[0]!.attrs!['data-value']).toBe('system');
  });
  it('withFooter keeps the view children in one grid and appends the footer', () => {
    const view = h('div', {}, h('section'), h('section'));
    const out = withFooter(view, {});
    expect(out.tag).toBe('div');
    expect(out.children.map((c) => c.tag)).toEqual(['section', 'section', 'footer']);
    expect(withFooter(h('section'), {}).children.map((c) => c.tag)).toEqual(['section', 'footer']);
  });
});
