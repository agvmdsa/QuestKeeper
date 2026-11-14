import { currentDockItem, DESKTOP_QUERY, DOCK_ITEMS, dockMotion, dockVisible, MENU_ICON, menuStep, REDUCED_MOTION_QUERY, type DockId, type MenuEvent } from './dock';
import { t } from './i18n';
import { parseRoute } from './state';
import type { ThemeChoice } from './theme';
import { h, mount, themeFooter, type VNode } from './views';
import dockCss from './styles/dock.css?inline';

// The floating dock's view and behaviour: a chunk of its own, imported by main.ts right after the first draw, so the page's first
// load stays within its budget. The pure rules live in dock.ts; the desktop magnification in dockMagnify.ts (loaded only there).
const icon = (d: string): VNode => h('svg', { className: 'dock-icon', attrs: { viewBox: '0 0 24 24', 'aria-hidden': 'true', focusable: 'false' } }, h('path', { attrs: { d } }));

/**
 * The floating dock: links to the two pages, a Settings button, and the button that opens them on a phone. With a theme, the
 * Settings panel (the Appearance choice) is open above the dock; Settings opens and closes it like a disclosure.
 */
export function dockView(current: DockId | null, open: boolean, settings: ThemeChoice | null = null): VNode {
  return h(
    'nav',
    { className: 'dock', attrs: { 'aria-label': t('dock.label'), 'data-open': String(open) } },
    settings ? h('div', { className: 'card', attrs: { id: 'dock-settings' } }, themeFooter({ theme: settings }, 'div')) : null,
    h(
      'ul',
      { className: 'dock-items', attrs: { id: 'dock-items' } },
      ...DOCK_ITEMS.map((item) => {
        const inner = [icon(item.icon), h('span', { className: 'dock-label', text: t(item.label) })];
        if (item.href === null) {
          return h('li', {}, h('button', { className: 'dock-btn', attrs: { type: 'button', 'data-action': 'settings', 'aria-expanded': String(Boolean(settings)) } }, ...inner));
        }
        return h('li', {}, h('a', { className: 'dock-btn', attrs: item.id === current ? { href: item.href, 'aria-current': 'page' } : { href: item.href } }, ...inner));
      }),
    ),
    h(
      'button',
      {
        className: 'dock-toggle',
        attrs: { type: 'button', 'data-action': 'dock-toggle', 'aria-expanded': String(open), 'aria-controls': 'dock-items', 'aria-label': t('dock.menu') },
      },
      icon(MENU_ICON),
    ),
  );
}

/**
 * Adds the dock's rules (minified by the build) to the end of the page's own stylesheet, wrapped in one always-true media block so a
 * single insertRule takes them all; inside it a rule a browser does not understand is dropped on its own. CSSOM, not a style
 * element: the strict CSP allows no inline style.
 */
export function addRules(sheet: Pick<CSSStyleSheet, 'insertRule' | 'cssRules'> | undefined, css: string): void {
  try {
    sheet?.insertRule('@media (min-width:0){' + css + '}', sheet.cssRules.length);
  } catch {
    // No stylesheet to add to: the dock shows unstyled but works.
  }
}

export interface DockContext {
  host: HTMLElement;
  unpaired: () => boolean;
  theme: () => ThemeChoice;
  /** Applies a theme button's choice; false when the click was not on one. */
  chooseTheme: (event: Event) => boolean;
  focusedKey: (root: HTMLElement) => string | null;
  restoreFocus: (key: string | null, root: HTMLElement) => boolean;
}

/** Starts the dock in its host. draw() redraws when the page, the menu, the Settings panel or the theme changed; close() shuts the menu and the panel. */
export function startDock(ctx: DockContext): { draw: () => void; close: () => void } {
  const { host } = ctx;
  const sheets = document.styleSheets;
  addRules(sheets[sheets.length - 1], dockCss);
  let menuOpen = false;
  let settingsOpen = false;
  let last = '';
  const desktop = matchMedia(DESKTOP_QUERY);
  const reduced = matchMedia(REDUCED_MOTION_QUERY);
  let refreshMagnify: (() => void) | null = null;

  function draw(): void {
    const route = parseRoute(location.hash);
    const visible = dockVisible(ctx.unpaired(), route);
    if (!visible) menuOpen = settingsOpen = false;
    host.hidden = !visible;
    const view = dockView(currentDockItem(route), menuOpen, settingsOpen ? ctx.theme() : null);
    const signature = JSON.stringify(view);
    if (signature === last) return;
    last = signature;
    const key = ctx.focusedKey(host);
    host.replaceChildren(mount(view));
    ctx.restoreFocus(key, host);
    refreshMagnify?.();
  }

  /**
   * One step of the phone menu or of the Settings panel (the same rules: its button opens and closes it; a choice, Escape and a
   * tap outside close it), then focus goes where the step says. Back from the panel it goes to Settings, or to the menu button on a phone.
   */
  function step(event: MenuEvent, panel = false): void {
    const next = menuStep(panel ? settingsOpen : menuOpen, event);
    if (panel) settingsOpen = next.open;
    else menuOpen = next.open;
    draw();
    // The phone menu exists only where its button shows; on the desktop only the panel moves focus.
    if (!next.focus || (!panel && desktop.matches)) return;
    const target = next.focus === 'first' ? (panel ? '[aria-pressed="true"]' : '.dock-btn') : desktop.matches ? '[data-action="settings"]' : '.dock-toggle';
    host.querySelector<HTMLElement>(target)?.focus();
  }

  host.addEventListener('click', (event) => {
    const target = event.target as HTMLElement;
    if (ctx.chooseTheme(event)) return;
    if (target.closest('[data-action="dock-toggle"]')) {
      step('toggle');
    } else if (target.closest('[data-action="settings"]')) {
      // From the phone menu: the menu closes and the panel opens in its place.
      if (menuOpen) menuOpen = settingsOpen = false;
      step('toggle', true);
    } else if (target.closest('a.dock-btn')) {
      // A page link: the hash change draws the page; the menu closes and gives focus back to its button.
      settingsOpen = false;
      step('select');
    }
  });
  document.addEventListener('pointerdown', (event) => {
    if (host.contains(event.target as Node)) return;
    if (menuOpen) step('outside');
    if (settingsOpen) step('outside', true);
  });
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Escape') return;
    if (settingsOpen) step('escape', true);
    else if (menuOpen) step('escape');
  });

  // The magnification: loaded only for a wide screen with a hovering pointer and full motion.
  function loadMagnify(): void {
    if (refreshMagnify || !desktop.matches || dockMotion(reduced.matches) === 'still') return;
    refreshMagnify = () => undefined;
    import('./dockMagnify')
      .then((m) => {
        refreshMagnify = m.startMagnify(host, desktop, reduced);
      })
      .catch(() => {
        refreshMagnify = null; // offline with nothing cached: the dock works without growing; the next change tries again
      });
  }
  desktop.addEventListener?.('change', loadMagnify);
  reduced.addEventListener?.('change', loadMagnify);
  loadMagnify();

  return {
    draw,
    close: () => {
      menuOpen = settingsOpen = false;
      draw();
    },
  };
}
