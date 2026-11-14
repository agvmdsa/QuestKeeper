import type { MessageKey } from './i18n';
import type { Route } from './state';

// The floating dock: the page's navigation. A bar of round icon buttons at the bottom on a wide screen with a hovering pointer
// (the buttons grow near the pointer, in a chunk of its own: dockMagnify.ts), one round button at the bottom right on a phone
// that opens the items upward. Which layout shows is decided by CSS with the same media query as DESKTOP_QUERY.

export type DockId = 'characters' | 'skua' | 'settings';

export interface DockItem {
  id: DockId;
  label: MessageKey;
  /** The hash route of a page; null for Settings, which opens a sheet. */
  href: string | null;
  /** One stroke path on a 24 by 24 grid, drawn in currentColor. */
  icon: string;
}

export const DOCK_ITEMS: readonly DockItem[] = [
  // A person: head and shoulders.
  { id: 'characters', label: 'list.title', href: '#/', icon: 'M20 21a8 8 0 0 0-16 0M8 7a4 4 0 1 0 8 0a4 4 0 1 0-8 0' },
  // A bot: head, antenna, ears and eyes.
  { id: 'skua', label: 'skua.title', href: '#/skua', icon: 'M12 8V4H8M4 8h16v12H4zM2 14h2M20 14h2M9 13v2M15 13v2' },
  // Two sliders.
  { id: 'settings', label: 'dock.settings', href: null, icon: 'M20 7h-9M14 17H5M14 17a3 3 0 1 0 6 0a3 3 0 1 0-6 0M4 7a3 3 0 1 0 6 0a3 3 0 1 0-6 0' },
];

/** The menu button of the phone layout: three lines (its state is in aria-expanded and the open items above it). */
export const MENU_ICON = 'M4 6h16M4 12h16M4 18h16';

/** The desktop dock: a wide screen with a hovering, precise pointer. Anything else gets the phone layout. Kept in step with dock.css. */
export const DESKTOP_QUERY = '(hover: hover) and (pointer: fine) and (min-width: 48rem)';
export const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

/** The dock item of a route: a character screen belongs to Characters; the pairing link to none. */
export function currentDockItem(route: Route): DockId | null {
  if (route.view === 'pair') return null;
  return route.view === 'skua' ? 'skua' : 'characters';
}

/** No dock on the Not paired screen, nor while a pairing link is being used. */
export const dockVisible = (unpaired: boolean, route: Route): boolean => !unpaired && route.view !== 'pair';

export type MenuEvent = 'toggle' | 'select' | 'outside' | 'escape';

export interface MenuStep {
  open: boolean;
  /** Where keyboard focus goes: into the menu, back to its button, or left where it is. */
  focus: 'first' | 'toggle' | null;
}

/** The phone menu: the button opens and closes it; a choice, Escape and a tap outside close it. */
export function menuStep(open: boolean, event: MenuEvent): MenuStep {
  if (event === 'toggle') return open ? { open: false, focus: 'toggle' } : { open: true, focus: 'first' };
  if (!open) return { open: false, focus: null };
  return { open: false, focus: event === 'outside' ? null : 'toggle' };
}

/** The desktop buttons grow with the pointer only for people who have not asked for less motion. */
export const dockMotion = (reducedMotion: boolean): 'magnify' | 'still' => (reducedMotion ? 'still' : 'magnify');
