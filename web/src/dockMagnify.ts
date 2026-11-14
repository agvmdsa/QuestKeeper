// The desktop dock's magnification, loaded only on a wide screen with a hovering pointer: each button grows with the pointer's
// distance (40 px far away, 80 px right under it), smoothed frame by frame so it feels like a spring.

export const DOCK_BASE = 40;
export const DOCK_MAX = 80;
/** How far from the pointer, in px, a button still grows. */
export const DOCK_RANGE = 150;

/** The size of a button whose centre is `distance` px from the pointer; no pointer (infinite or broken) is far away. */
export function magnify(distance: number, base = DOCK_BASE, max = DOCK_MAX, range = DOCK_RANGE): number {
  const d = Math.abs(distance);
  if (!(d < range)) return base;
  return max - ((max - base) * d) / range;
}

/** One frame of smoothing: a share of the way to the target, snapping when close. */
export function approach(current: number, target: number, share = 0.25): number {
  const next = current + (target - current) * share;
  return Math.abs(target - next) < 0.1 ? target : next;
}

/**
 * The centres of `count` buttons at rest: the bar is centred on the screen, so its centre stays put while buttons grow, and at rest
 * the buttons sit one size and one gap apart around it. Distances are measured from these, never from the grown buttons, so a
 * growing button does not move its neighbours' targets (which made the sizes jitter).
 */
export function restCentres(centre: number, count: number, size = DOCK_BASE, gap = 12): number[] {
  return Array.from({ length: count }, (_, i) => centre + (i - (count - 1) / 2) * (size + gap));
}

/** One frame: each size a step closer to its target; with the motion off, every button back at rest at once. */
export function magnifyFrame(sizes: readonly number[], centres: readonly number[], pointerX: number, on: boolean): { sizes: number[]; moving: boolean } {
  if (!on) return { sizes: centres.map(() => DOCK_BASE), moving: false };
  let moving = false;
  const next = centres.map((c, i) => {
    const target = magnify(pointerX - c);
    const size = approach(sizes[i] ?? DOCK_BASE, target);
    if (size !== target) moving = true;
    return size;
  });
  return { sizes: next, moving };
}

/**
 * Follows the mouse over the dock and writes each button's size as --dock-size. Does nothing while the desktop layout is off or
 * motion is reduced, and puts the buttons back at rest the moment either changes. Returns a function to call after the dock was
 * drawn anew, so the new buttons take their sizes at once.
 */
export function startMagnify(host: HTMLElement, desktop: MediaQueryList, reduced: MediaQueryList): () => void {
  let pointerX = Number.POSITIVE_INFINITY;
  let frame = 0;
  // By position, not by element: the dock is drawn anew when the page changes.
  let sizes: number[] = [];

  const schedule = () => {
    if (!frame) frame = requestAnimationFrame(step);
  };
  function step(): void {
    frame = 0;
    const bar = host.querySelector<HTMLElement>('.dock-items');
    const buttons = host.querySelectorAll<HTMLElement>('.dock-btn');
    if (!bar) return;
    const box = bar.getBoundingClientRect();
    const gap = parseFloat(getComputedStyle(bar).columnGap) || 12;
    const f = magnifyFrame(sizes, restCentres(box.left + box.width / 2, buttons.length, DOCK_BASE, gap), pointerX, desktop.matches && !reduced.matches);
    sizes = f.sizes;
    buttons.forEach((button, i) => button.style.setProperty('--dock-size', String(sizes[i])));
    if (f.moving) schedule();
  }
  host.addEventListener('pointermove', (event) => {
    pointerX = event.clientX;
    schedule();
  });
  host.addEventListener('pointerleave', () => {
    pointerX = Number.POSITIVE_INFINITY;
    schedule();
  });
  desktop.addEventListener?.('change', schedule);
  reduced.addEventListener?.('change', schedule);
  return schedule;
}
