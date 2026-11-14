import { currentDockItem, DESKTOP_QUERY, DOCK_ITEMS, dockMotion, dockPlan, dockStep, dockVisible, focusLeaves, MENU_ICON, REDUCED_MOTION_QUERY, type DockEvent, type DockFocus, type DockId, type DockState } from './dock';
import { t } from './i18n';
import { parseRoute } from './state';
import type { ThemeChoice } from './theme';
import { h, mount, themeFooter, type VNode } from './views';
import dockCss from './styles/dock.css?inline';
import { addRules, pageSheet } from './cssom';

export { addRules };

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
        // The name is given outright: on a desktop the label shows only on hover, and a hidden label names nothing.
        const label = t(item.label);
        const inner = [icon(item.icon), h('span', { className: 'dock-label', text: label })];
        if (item.href === null) {
          return h('li', {}, h('button', { className: 'dock-btn', attrs: { type: 'button', 'data-action': 'settings', 'aria-expanded': String(Boolean(settings)), 'aria-label': label } }, ...inner));
        }
        const attrs: Record<string, string> = { href: item.href, 'aria-label': label };
        if (item.id === current) attrs['aria-current'] = 'page';
        return h('li', {}, h('a', { className: 'dock-btn', attrs }, ...inner));
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

/** Flips the menu's open state on the dock already shown, the same attributes dockView draws, so the items animate from where they are. */
export function setOpen(root: { querySelector(selector: string): { setAttribute(name: string, value: string): void } | null }, open: boolean): void {
  root.querySelector('.dock')?.setAttribute('data-open', String(open));
  root.querySelector('.dock-toggle')?.setAttribute('aria-expanded', String(open));
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

const FOCUS: Record<Exclude<DockFocus, null>, string> = { first: '.dock-btn', theme: '[aria-pressed="true"]', toggle: '.dock-toggle', settings: '[data-action="settings"]' };

/** Starts the dock in its host. draw() redraws when the page, the menu, the Settings panel or the theme changed; close() shuts the menu and the panel. */
export function startDock(ctx: DockContext): { draw: () => void; close: () => void } {
  const { host } = ctx;
  addRules(pageSheet(), dockCss);
  let state: DockState = { menu: false, settings: false };
  let shown: { sig: string; open: boolean } | null = null;
  const desktop = matchMedia(DESKTOP_QUERY);
  const reduced = matchMedia(REDUCED_MOTION_QUERY);
  let refreshMagnify: (() => void) | null = null;

  function draw(): void {
    const route = parseRoute(location.hash);
    const visible = dockVisible(ctx.unpaired(), route);
    if (!visible) state = { menu: false, settings: false };
    host.hidden = !visible;
    const current = currentDockItem(route);
    const theme = state.settings ? ctx.theme() : null;
    // The signature of the view drawn closed: a change of the menu alone only flips its state on the nodes shown.
    const next = { sig: JSON.stringify(dockView(current, false, theme)), open: state.menu };
    const plan = dockPlan(shown, next);
    shown = next;
    if (plan === 'open') setOpen(host, state.menu);
    if (plan !== 'mount') return;
    const key = ctx.focusedKey(host);
    host.replaceChildren(mount(dockView(current, state.menu, theme)));
    ctx.restoreFocus(key, host);
    refreshMagnify?.();
  }

  /** One step of the dock (dock.ts), then focus goes where the step says. */
  function apply(event: DockEvent): void {
    const next = dockStep(state, event, desktop.matches);
    state = next.state;
    draw();
    if (next.focus) host.querySelector<HTMLElement>(FOCUS[next.focus])?.focus();
  }

  host.addEventListener('click', (event) => {
    const target = event.target as HTMLElement;
    if (ctx.chooseTheme(event)) return;
    if (target.closest('[data-action="dock-toggle"]')) apply('menu');
    else if (target.closest('[data-action="settings"]')) apply('settings');
    // A page link: the hash change draws the page; the menu closes and gives focus back to its button.
    else if (target.closest('a.dock-btn')) apply('select');
  });
  // Tabbing out of the menu or the Settings panel closes it. Handled after focus has moved, so a redraw keeps it where it went.
  host.addEventListener('focusout', (event) => {
    const from = event.target as Node;
    const to = event.relatedTarget as Node | null;
    setTimeout(() => {
      const panel = host.querySelector('#dock-settings');
      if (state.settings && panel?.contains(from) && focusLeaves([panel, host.querySelector('[data-action="settings"]')], to)) apply('panel-left');
      if (state.menu && focusLeaves([host], to)) apply('menu-left');
    });
  });
  document.addEventListener('pointerdown', (event) => {
    if (!host.contains(event.target as Node) && (state.menu || state.settings)) apply('outside');
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && (state.menu || state.settings)) apply('escape');
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
  // Switching between the phone and the desktop layout closes what was open: the menu of one is not the menu of the other.
  desktop.addEventListener?.('change', () => {
    apply('mode');
    loadMagnify();
  });
  reduced.addEventListener?.('change', loadMagnify);
  loadMagnify();

  return {
    draw,
    close: () => {
      state = { menu: false, settings: false };
      draw();
    },
  };
}
