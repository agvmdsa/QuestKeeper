import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { currentDockItem, DESKTOP_QUERY, DOCK_ITEMS, dockMotion, dockPlan, dockStep, dockVisible, focusLeaves, menuStep, type DockEvent, type DockId, type DockState } from '../src/dock';
import { approach, DOCK_BASE, DOCK_MAX, DOCK_RANGE, magnify, magnifyFrame, restCentres } from '../src/dockMagnify';
import { en } from '../src/i18n/en';
import { parseRoute } from '../src/state';
import { addRules, dockView, setOpen } from '../src/dockUi';
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

describe('dock state', () => {
  const closed: DockState = { menu: false, settings: false };
  const menu: DockState = { menu: true, settings: false };
  const panel: DockState = { menu: false, settings: true };
  const step = (s: DockState, e: DockEvent, desktop = false) => dockStep(s, e, desktop);
  it('opens the phone menu with focus on the first item and closes it back onto its button', () => {
    expect(step(closed, 'menu')).toEqual({ state: menu, focus: 'first' });
    expect(step(menu, 'menu')).toEqual({ state: closed, focus: 'toggle' });
  });
  it('opening the menu shuts the Settings panel (both float in the same place on a phone)', () => expect(step(panel, 'menu').state).toEqual(menu));
  it('opens Settings in place of the menu, focus on the pressed theme', () => {
    expect(step(menu, 'settings')).toEqual({ state: panel, focus: 'theme' });
    expect(step(closed, 'settings', true)).toEqual({ state: panel, focus: 'theme' });
  });
  it('closes Settings back onto the Settings button on a desktop and onto the menu button on a phone', () => {
    expect(step(panel, 'settings', true)).toEqual({ state: closed, focus: 'settings' });
    expect(step(panel, 'settings')).toEqual({ state: closed, focus: 'toggle' });
  });
  it('closes Settings with Escape, back onto its opener', () => {
    expect(step(panel, 'escape', true)).toEqual({ state: closed, focus: 'settings' });
    expect(step(panel, 'escape')).toEqual({ state: closed, focus: 'toggle' });
  });
  it('closes the menu with Escape and a choice, back onto its button', () => {
    expect(step(menu, 'escape')).toEqual({ state: closed, focus: 'toggle' });
    expect(step(menu, 'select')).toEqual({ state: closed, focus: 'toggle' });
  });
  it('leaves focus alone when nothing was open, and on a desktop for the menu (it has no button there)', () => {
    expect(step(closed, 'escape')).toEqual({ state: closed, focus: null });
    expect(step(closed, 'select')).toEqual({ state: closed, focus: null });
    expect(step(closed, 'menu', true).focus).toBeNull();
  });
  it.each(['outside', 'mode'] as const)('closes everything without moving focus on %s', (e) => {
    expect(step(menu, e)).toEqual({ state: closed, focus: null });
    expect(step(panel, e)).toEqual({ state: closed, focus: null });
  });
  it('closes only the part that focus left', () => {
    expect(step(menu, 'menu-left')).toEqual({ state: closed, focus: null });
    expect(step(panel, 'panel-left')).toEqual({ state: closed, focus: null });
    expect(step(panel, 'menu-left').state).toEqual(panel);
    expect(step(menu, 'panel-left').state).toEqual(menu);
  });
});

describe('focus leaving', () => {
  const box = (...inside: object[]) => ({ contains: (n: object) => inside.includes(n) });
  const a = {};
  const b = {};
  const out = {};
  it('counts as leaving only when focus goes to an element outside every area', () => {
    expect(focusLeaves([box(a), box(b)], out as Node)).toBe(true);
    expect(focusLeaves([box(a), box(b)], b as Node)).toBe(false);
    expect(focusLeaves([null, box(a)], a as Node)).toBe(false);
  });
  it('ignores focus going nowhere (the window losing focus, a tap on the page)', () => expect(focusLeaves([box(a)], null)).toBe(false));
});

describe('redrawing the dock', () => {
  it('mounts the first time and when anything but the menu changed', () => {
    expect(dockPlan(null, { sig: 'a', open: false })).toBe('mount');
    expect(dockPlan({ sig: 'a', open: false }, { sig: 'b', open: false })).toBe('mount');
    expect(dockPlan({ sig: 'a', open: true }, { sig: 'b', open: false })).toBe('mount');
  });
  it('only flips the open state on the nodes already shown when just the menu changed, so its animation runs', () => {
    expect(dockPlan({ sig: 'a', open: false }, { sig: 'a', open: true })).toBe('open');
    expect(dockPlan({ sig: 'a', open: true }, { sig: 'a', open: false })).toBe('open');
    expect(dockPlan({ sig: 'a', open: true }, { sig: 'a', open: true })).toBe('none');
  });
  it('flips exactly the attributes the view would draw for the open state', () => {
    const attrs: Record<string, Record<string, string>> = { '.dock': {}, '.dock-toggle': {} };
    const host = { querySelector: (sel: string) => (attrs[sel] ? { setAttribute: (k: string, v: string) => { attrs[sel]![k] = v; } } : null) };
    for (const open of [true, false]) {
      setOpen(host, open);
      const v = dockView('characters', open);
      const toggle = all(v).find((n) => n.className === 'dock-toggle')!;
      expect(attrs['.dock']!['data-open']).toBe(v.attrs!['data-open']);
      expect(attrs['.dock-toggle']!['aria-expanded']).toBe(toggle.attrs!['aria-expanded']);
    }
  });
});

describe('accessible names of the dock items', () => {
  it('names every page link and the Settings button from the catalog, whether or not the label is shown', () => {
    const v = dockView('characters', false);
    const items = all(v).filter((n) => n.className === 'dock-btn');
    expect(items.map((n) => n.attrs!['aria-label'])).toEqual(DOCK_ITEMS.map((i) => en[i.label]));
    for (const n of items) expect(n.attrs!['aria-label']!.trim()).not.toBe('');
  });
});

describe('magnification frames', () => {
  it('places the buttons at rest around the bar centre, one size and gap apart', () => {
    expect(restCentres(500, 3, 40, 12)).toEqual([448, 500, 552]);
    expect(restCentres(100, 1, 40, 12)).toEqual([100]);
  });
  it('sizes from the rest positions, so a growing button never moves the target of its neighbours', () => {
    const centres = restCentres(500, 3, 40, 12);
    let sizes = [40, 40, 40];
    const seen: number[][] = [];
    for (let i = 0; i < 80; i += 1) {
      const f = magnifyFrame(sizes, centres, 500, true);
      sizes = f.sizes;
      seen.push(sizes);
      if (!f.moving) break;
    }
    expect(sizes).toEqual([magnify(52), 80, magnify(52)]);
    // Never back and forth: each button only grows on the way there.
    for (let i = 1; i < seen.length; i += 1) for (let b = 0; b < 3; b += 1) expect(seen[i]![b]!).toBeGreaterThanOrEqual(seen[i - 1]![b]!);
  });
  it('goes straight back to rest and stops when the motion is off (reduced motion turned on)', () => {
    expect(magnifyFrame([80, 70, 50], [448, 500, 552], 500, false)).toEqual({ sizes: [40, 40, 40], moving: false });
  });
});

describe('room for the dock', () => {
  const css = readFileSync(new URL('../src/styles/dock.css', import.meta.url), 'utf8');
  const desktop = css.match(/@media \(hover: hover\) and \(pointer: fine\) and \(min-width: 48rem\)\s*\{([\s\S]*?)\n\}/)![1]!;
  const rem = (block: string, sel: string, prop: string) => Number(block.match(new RegExp(`(?:^|\\n)\\s*${sel}\\s*\\{[^}]*${prop}:\\s*calc\\(([\\d.]+)rem`))![1]) * 16;
  it('reserves at least the height of the fully magnified bar on a desktop, so the last card is never covered', () => {
    // The bar: its bottom offset, its padding, its 1 px edges and the biggest button.
    const bar = rem(desktop, '\\.dock-items', 'bottom') + 2 * 0.5 * 16 + 2 + DOCK_MAX;
    expect(rem(desktop, '\\.dock-host', 'height')).toBeGreaterThanOrEqual(bar);
  });
  it('reserves the phone button and its margin on a phone', () => expect(rem(css, '\\.dock-host', 'height')).toBeGreaterThanOrEqual(44 + 16));
  it('places the labels with explicit offsets, not by their static position', () => {
    expect(css).toMatch(/(?:^|\n)\.dock-label\s*\{[^}]*top:/);
    expect(desktop).toMatch(/\.dock-label\s*\{[^}]*left:\s*50%/);
  });
});
