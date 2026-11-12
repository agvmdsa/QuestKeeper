/** Publishes the height of the sticky header as --chrome-h so scroll-padding-top follows the banner. Never throws. */
export function publishChromeHeight(
  root: Pick<HTMLElement, 'style'> | null | undefined,
  chrome: Pick<HTMLElement, 'getBoundingClientRect'> | null | undefined,
): void {
  try {
    if (!root || !chrome) return;
    const height = chrome.getBoundingClientRect().height;
    if (Number.isFinite(height) && height > 0) root.style.setProperty('--chrome-h', `${height}px`);
  } catch {
    // The CSS fallback (the strip alone) applies.
  }
}

/** Measures now and whenever the header changes size; does nothing without ResizeObserver. */
export function watchChromeHeight(root: HTMLElement, chrome: HTMLElement | null, observer: typeof ResizeObserver | undefined = globalThis.ResizeObserver): void {
  try {
    publishChromeHeight(root, chrome);
    if (chrome && typeof observer !== 'undefined') new observer(() => publishChromeHeight(root, chrome)).observe(chrome);
  } catch {
    // Not fatal: the page works with the fallback padding.
  }
}
