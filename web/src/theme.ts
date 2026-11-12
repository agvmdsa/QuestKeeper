export type ThemeChoice = 'system' | 'light' | 'dark';

export const THEME_KEY = 'qkp-theme-v1';
export const THEMES: readonly ThemeChoice[] = ['system', 'light', 'dark'];

/** Anything that is not a known choice follows the system. */
export function parseTheme(raw: string | null): ThemeChoice {
  return raw === 'light' || raw === 'dark' ? raw : 'system';
}

/** Explicit choices set `data-theme`; the system choice removes it so the media query decides. */
export function applyTheme(root: Pick<HTMLElement, 'setAttribute' | 'removeAttribute'>, choice: ThemeChoice): void {
  if (choice === 'system') root.removeAttribute('data-theme');
  else root.setAttribute('data-theme', choice);
}

type Store = Pick<Storage, 'getItem' | 'setItem'>;

/** Storage can be blocked (private mode): the page then simply follows the system. */
export function loadTheme(storage: Store | null | undefined): ThemeChoice {
  try {
    return parseTheme(storage?.getItem(THEME_KEY) ?? null);
  } catch {
    return 'system';
  }
}

export function saveTheme(storage: Store | null | undefined, choice: ThemeChoice): void {
  try {
    storage?.setItem(THEME_KEY, choice);
  } catch {
    // Not remembered; the choice still applies until the page closes.
  }
}
