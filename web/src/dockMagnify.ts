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
 * Follows the mouse over the dock and writes each button's size as --dock-size. Does nothing while the desktop layout is off or
 * motion is reduced. Returns a function to call after the dock was drawn anew, so the new buttons take their sizes at once.
 */
export function startMagnify(host: HTMLElement, desktop: MediaQueryList, reduced: MediaQueryList): () => void {
  let pointerX = Number.POSITIVE_INFINITY;
  let frame = 0;
  // By position, not by element: the dock is drawn anew when the page changes.
  const sizes: number[] = [];

  const schedule = () => {
    if (!frame) frame = requestAnimationFrame(step);
  };
  function step(): void {
    frame = 0;
    const on = desktop.matches && !reduced.matches;
    let moving = false;
    host.querySelectorAll<HTMLElement>('.dock-btn').forEach((button, i) => {
      const box = button.getBoundingClientRect();
      const target = on ? magnify(pointerX - (box.left + box.width / 2)) : DOCK_BASE;
      const size = on ? approach(sizes[i] ?? DOCK_BASE, target) : DOCK_BASE;
      sizes[i] = size;
      if (size !== target) moving = true;
      button.style.setProperty('--dock-size', String(size));
    });
    if (moving) schedule();
  }
  host.addEventListener('pointermove', (event) => {
    pointerX = event.clientX;
    schedule();
  });
  host.addEventListener('pointerleave', () => {
    pointerX = Number.POSITIVE_INFINITY;
    schedule();
  });
  return schedule;
}
