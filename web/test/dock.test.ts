import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { currentDockItem, DESKTOP_QUERY, DOCK_ITEMS, dockMotion, dockVisible, menuStep, type DockId } from '../src/dock';
import { approach, DOCK_BASE, DOCK_MAX, DOCK_RANGE, magnify } from '../src/dockMagnify';
import { en } from '../src/i18n/en';
import { parseRoute } from '../src/state';
import { addRules, dockView } from '../src/dockUi';
import type { VNode } from '../src/views';

const all = (n: VNode): VNode[] => [n, ...n.children.flatMap(all)];
const texts = (n: VNode) => all(n).map((x) => x.text).filter((t): t is string => t !== undefined);
const links = (n: VNode) => all(n).filter((x) => x.tag === 'a');

describe('dock items', () => {
  it('are Characters, Skua and Settings, in that order, with catalog labels', () => {
    expect(DOCK_ITEMS.map((i) => i.id)).toEqual(['characters', 'skua', 'settings']);
    expect(DOCK_ITEMS.map((i) => en[i.label])).toEqual(['Characters', 'Skua', 'Settings']);
  });
  it('link the two pages by their hash routes; Settings is a button, not a page', () => {
    expect(DOCK_ITEMS.map((i) => i.href)).toEqual(['#/', '#/skua', null]);
  });
  it('each carry one stroke icon path', () => {
    for (const i of DOCK_ITEMS) expect(i.icon).toMatch(/^M[\d .,a-zA-Z-]+$/);
  });
});

describe('current item', () => {
  it.each([
    ['', 'characters'],
    ['#/', 'characters'],
    ['#/c/Hero', 'characters'],
    ['#/skua', 'skua'],
    ['#/nowhere', 'characters'],
    ['#t=token', null],
  ] as const)('for %j is %s', (hash, want) => expect(currentDockItem(parseRoute(hash))).toBe(want));
});

describe('visibility', () => {
  it('is hidden on the Not paired screen and while a pairing link is open', () => {
    expect(dockVisible(true, parseRoute('#/'))).toBe(false);
    expect(dockVisible(false, parseRoute('#t=abc'))).toBe(false);
    expect(dockVisible(false, parseRoute('#/skua'))).toBe(true);
    expect(dockVisible(false, parseRoute('#/c/Hero'))).toBe(true);
  });
});

describe('mobile menu', () => {
  it('opens on the toggle and moves focus to the first item', () => expect(menuStep(false, 'toggle')).toEqual({ open: true, focus: 'first' }));
  it('closes on the toggle and keeps focus on it', () => expect(menuStep(true, 'toggle')).toEqual({ open: false, focus: 'toggle' }));
  it('closes on a choice and gives focus back to the toggle', () => expect(menuStep(true, 'select')).toEqual({ open: false, focus: 'toggle' }));
  it('closes on Escape and gives focus back to the toggle', () => expect(menuStep(true, 'escape')).toEqual({ open: false, focus: 'toggle' }));
  it('closes on a tap outside without taking focus from where the tap went', () => expect(menuStep(true, 'outside')).toEqual({ open: false, focus: null }));
  it.each(['select', 'escape', 'outside'] as const)('ignores %s while closed', (event) => expect(menuStep(false, event)).toEqual({ open: false, focus: null }));
});

describe('motion', () => {
  it('magnifies only without a reduced-motion request', () => {
    expect(dockMotion(false)).toBe('magnify');
    expect(dockMotion(true)).toBe('still');
  });
  it('asks for a wide screen with a hovering, precise pointer for the desktop dock', () => {
    expect(DESKTOP_QUERY).toContain('(hover: hover)');
    expect(DESKTOP_QUERY).toContain('(pointer: fine)');
    expect(DESKTOP_QUERY).toMatch(/min-width/);
  });
});

describe('distance to size', () => {
  it('is 40 px far away and 80 px under the pointer', () => {
    expect([DOCK_BASE, DOCK_MAX, DOCK_RANGE]).toEqual([40, 80, 150]);
    expect(magnify(0)).toBe(80);
    expect(magnify(150)).toBe(40);
    expect(magnify(-150)).toBe(40);
    expect(magnify(1000)).toBe(40);
  });
  it('falls off evenly on both sides', () => {
    expect(magnify(75)).toBe(60);
    expect(magnify(-75)).toBe(60);
    expect(magnify(30)).toBeCloseTo(72);
  });
  it('treats no pointer (an infinite or broken distance) as far away', () => {
    expect(magnify(Number.POSITIVE_INFINITY)).toBe(40);
    expect(magnify(Number.NEGATIVE_INFINITY)).toBe(40);
    expect(magnify(Number.NaN)).toBe(40);
  });
  it('takes other sizes for the icon (20 to 40)', () => {
    expect(magnify(0, 20, 40)).toBe(40);
    expect(magnify(75, 20, 40)).toBe(30);
  });
});

describe('smoothing', () => {
  it('moves part of the way each frame and settles on the target', () => {
    expect(approach(40, 80)).toBeGreaterThan(40);
    expect(approach(40, 80)).toBeLessThan(80);
    let v = 40;
    for (let i = 0; i < 60; i += 1) v = approach(v, 80);
    expect(v).toBe(80);
  });
  it('snaps when close and never overshoots', () => {
    expect(approach(79.95, 80)).toBe(80);
    expect(approach(80, 40)).toBeGreaterThanOrEqual(40);
  });
});

describe('dock view', () => {
  const view = (current: DockId | null, open = false) => dockView(current, open);
  it('is a labelled nav', () => {
    const v = view('characters');
    expect(v.tag).toBe('nav');
    expect(v.attrs!['aria-label']).toBe('Pages');
  });
  it('has real links for the pages and a button for Settings', () => {
    const v = view('characters');
    expect(links(v).map((a) => a.attrs!.href)).toEqual(['#/', '#/skua']);
    const settings = all(v).find((n) => n.attrs?.['data-action'] === 'settings')!;
    expect(settings.tag).toBe('button');
    expect(settings.attrs).toMatchObject({ type: 'button', 'aria-expanded': 'false' });
  });
  it.each([['characters', '#/'], ['skua', '#/skua']] as const)('marks %s as the current page, and only it', (current, href) => {
    const marked = all(view(current)).filter((n) => n.attrs?.['aria-current'] === 'page');
    expect(marked.map((n) => n.attrs!.href)).toEqual([href]);
  });
  it('marks nothing on a page that is not in the dock', () => expect(all(view(null)).some((n) => n.attrs?.['aria-current'])).toBe(false));
  it('names every item with its catalog label as text, the icon hidden from screen readers', () => {
    const v = view('skua');
    expect(texts(v)).toEqual(expect.arrayContaining(['Characters', 'Skua', 'Settings']));
    const icons = all(v).filter((n) => n.tag === 'svg');
    expect(icons.length).toBe(4);
    for (const i of icons) expect(i.attrs).toMatchObject({ 'aria-hidden': 'true', focusable: 'false', viewBox: '0 0 24 24' });
  });
  it('has a menu toggle with one name that says whether the menu is open through aria-expanded', () => {
    const closed = all(view('characters')).find((n) => n.attrs?.['data-action'] === 'dock-toggle')!;
    expect(closed.tag).toBe('button');
    expect(closed.attrs).toMatchObject({ type: 'button', 'aria-expanded': 'false', 'aria-controls': 'dock-items', 'aria-label': 'Menu' });
    const open = all(view('characters', true)).find((n) => n.attrs?.['data-action'] === 'dock-toggle')!;
    expect(open.attrs).toMatchObject({ 'aria-expanded': 'true', 'aria-label': 'Menu' });
    expect(view('characters', true).attrs!['data-open']).toBe('true');
    expect(all(view('characters')).find((n) => n.attrs?.id === 'dock-items')).toBeTruthy();
  });
  it('uses no inline style or event attribute', () => {
    for (const n of all(view('skua', true))) for (const k of Object.keys(n.attrs ?? {})) expect(k === 'style' || k.startsWith('on')).toBe(false);
  });
});

describe('settings panel', () => {
  const panel = (v: VNode) => all(v).find((n) => n.attrs?.id === 'dock-settings');
  it('is closed without a theme', () => expect(panel(dockView('characters', false))).toBeUndefined());
  it('opens above the dock as a card with the labelled Appearance group, the current theme pressed', () => {
    const v = dockView('characters', false, 'dark');
    const p = panel(v)!;
    expect(p.className).toBe('card'); // text on the paper surface: covered by the token contrast tests
    const group = all(p).find((n) => n.attrs?.role === 'group')!;
    expect(group.attrs!['aria-labelledby']).toBe('appearance-label');
    expect(texts(p)[0]).toBe('Appearance');
    const themes = all(p).filter((n) => n.attrs?.['data-action'] === 'theme');
    expect(themes.map((b) => b.text)).toEqual(['System', 'Light', 'Dark']);
    expect(themes.filter((b) => b.attrs!['aria-pressed'] === 'true').map((b) => b.attrs!['data-value'])).toEqual(['dark']);
    expect(all(v).find((n) => n.attrs?.['data-action'] === 'settings')!.attrs!['aria-expanded']).toBe('true');
  });
  it('comes before the items, so it sits above them in reading order too', () => expect(dockView('skua', false, 'system').children[0]!.attrs!.id).toBe('dock-settings'));
});

describe('dock styles', () => {
  const css = readFileSync(new URL('../src/styles/dock.css', import.meta.url), 'utf8');
  const app = readFileSync(new URL('../src/styles/app.css', import.meta.url), 'utf8');
  // A rule by one of its selectors, alone or in a list.
  const rule = (sel: string) => css.match(new RegExp(`(?:^|\\n|,)\\s*${sel}\\s*(?:,[^{]*)?\\{([^}]*)\\}`))?.[1] ?? '';
  it('floats at the bottom with sticky positioning, above the home indicator', () => {
    const host = rule('\\.dock-host');
    expect(host).toMatch(/position:\s*sticky/);
    expect(host).toMatch(/bottom:\s*0/);
    expect(host).toMatch(/env\(safe-area-inset-bottom\)/);
    expect(host).toMatch(/pointer-events:\s*none/);
  });
  it('keeps a focused element from scrolling under the dock', () => expect(app).toMatch(/(^|\n)html\s*\{[^}]*scroll-padding-bottom/));
  it('makes the body at least as tall as the screen, so the dock sits at the bottom of a short page', () => {
    expect(app).toMatch(/(^|\n)body\s*\{[^}]*flex-direction:\s*column[^}]*min-height:\s*100dvh/);
    expect(app).toMatch(/(^|\n)main\s*\{[^}]*flex:\s*1 0 auto/);
  });
  it('never fades text (no opacity) and never uses position: fixed', () => {
    expect(css).not.toMatch(/opacity/);
    expect(css).not.toMatch(/position:\s*fixed/);
  });
  it('takes every colour from the tokens', () => expect(css).not.toMatch(/#[0-9a-fA-F]{3,6}\b|rgb\(/));
  it('gives the phone buttons a 44 px round target', () => {
    for (const sel of ['\\.dock-toggle', '\\.dock-btn']) {
      const r = rule(sel);
      expect(r, sel).toMatch(/2\.75rem/);
      expect(r, sel).toMatch(/border-radius:\s*50%/);
    }
  });
  it('grows the desktop buttons from 40 px with the pointer', () => expect(css).toMatch(/calc\(var\(--dock-size,\s*40\)\s*\*\s*1px\)/));
  it('turns the menu animation off for people who ask', () => {
    const reduced = [...css.matchAll(/@media \(prefers-reduced-motion: reduce\)\s*\{([\s\S]*?)\n\}/g)].map((m) => m[1]).join('\n');
    expect(reduced).toMatch(/\.dock/);
  });
  it('sets no display on the dock host, so its hidden attribute hides it', () => expect(rule('\\.dock-host')).not.toMatch(/display:/));
});

describe('contrast of the dock', () => {
  const tokens = readFileSync(new URL('../src/styles/tokens.css', import.meta.url), 'utf8');
  const css = readFileSync(new URL('../src/styles/dock.css', import.meta.url), 'utf8');
  const block = (t: string) => Object.fromEntries([...t.matchAll(/--([a-z-]+):\s*(#[0-9A-Fa-f]{6})\s*;/g)].map((m) => [m[1]!, m[2]!]));
  const body = (re: RegExp) => tokens.match(re)![1]!;
  const light = block(body(/^:root\s*\{([^}]*)\}/m));
  const sysDark = { ...light, ...block(body(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme='light'\]\)\s*\{([^}]*)\}/)) };
  const forcedDark = { ...light, ...block(body(/^:root\[data-theme='dark'\]\s*\{([^}]*)\}/m)) };
  const lum = (hex: string) => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)) as [number, number, number];
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const ratio = (a: string, b: string) => {
    const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x) as [number, number];
    return (hi + 0.05) / (lo + 0.05);
  };
  // Top-level rules and the desktop rules inside their media query (indented).
  const decl = (sel: string, prop: string) => css.match(new RegExp(`(?:^|\\n|,)\\s*${sel}\\s*(?:,[^{]*)?\\{[^}]*(?:^|[;{\\s])${prop}:\\s*(?:1px solid )?var\\(--([a-z-]+)\\)`))?.[1];
  // [label, selector, minimum ratio]: text and icons (currentColor) on their own background.
  const pairs: [string, string, number][] = [
    ['dock button', '\\.dock-btn', 7],
    ['current dock item', '\\.dock-btn\\[aria-current="page"\\]', 7],
    ['menu toggle', '\\.dock-toggle', 7],
    ['label', '\\.dock-label', 7],
  ];
  describe.each([['light', light], ['system dark', sysDark], ['forced dark', forcedDark], ['forced light on a dark system', light]] as const)('%s', (_n, t) => {
    it.each(pairs)('%s reads at its ratio', (_label, sel, min) => {
      const fg = decl(sel, 'color');
      const bg = decl(sel, 'background');
      expect(fg, `${sel} color`).toBeTruthy();
      expect(bg, `${sel} background`).toBeTruthy();
      expect(ratio(t[fg!]!, t[bg!]!)).toBeGreaterThanOrEqual(min);
    });
    it('the bar edge stands out from the page and the cards (3:1)', () => {
      const edge = decl('\\.dock-items', 'border')!;
      expect(edge).toBeTruthy();
      for (const s of ['bg', 'surface', 'raised']) expect(ratio(t[edge]!, t[s]!)).toBeGreaterThanOrEqual(3);
    });
    it('the button edge stands out from the bar (3:1)', () => {
      const edge = decl('\\.dock-btn', 'border')!;
      const bar = decl('\\.dock-items', 'background')!;
      expect(ratio(t[edge]!, t[bar]!)).toBeGreaterThanOrEqual(3);
    });
  });
});

describe('adding the dock rules', () => {
  function sheet(refuse = false) {
    const rules: string[] = [];
    return {
      rules,
      cssRules: { get length() { return rules.length; } } as unknown as CSSRuleList,
      insertRule(rule: string, at: number) {
        if (refuse) throw new Error('syntax');
        rules.splice(at, 0, rule);
        return at;
      },
    };
  }
  it('adds all the rules at the end of the sheet as one media block', () => {
    const s = sheet();
    s.rules.push('.page{}');
    addRules(s, '.a{color:red}@media (min-width:1px){.b{x:1}}');
    expect(s.rules).toEqual(['.page{}', '@media (min-width:0){.a{color:red}@media (min-width:1px){.b{x:1}}}']);
  });
  it('never throws, without a stylesheet or when the browser refuses', () => {
    expect(() => addRules(undefined, '.a{x:1}')).not.toThrow();
    expect(() => addRules(sheet(true), '.a{x:1}')).not.toThrow();
  });
});
