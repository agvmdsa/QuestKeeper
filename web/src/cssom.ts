// Rules of a chunk loaded later (the dock, the Skua control), added to the page's own stylesheet when the chunk arrives, so the
// first load stays within its budget.

/**
 * Adds a lazily loaded chunk's rules (minified by the build) to the end of the page's own stylesheet, wrapped in one always-true media block so a
 * single insertRule takes them all; inside it a rule a browser does not understand is dropped on its own. CSSOM, not a style
 * element: the strict CSP allows no inline style.
 */
export function addRules(sheet: Pick<CSSStyleSheet, 'insertRule' | 'cssRules'> | undefined, css: string): void {
  try {
    sheet?.insertRule('@media (min-width:0){' + css + '}', sheet.cssRules.length);
  } catch {
    // No stylesheet to add to: the chunk's part of the page shows unstyled but works.
  }
}

/** The page's stylesheet (the last one), where a chunk's rules go. */
export function pageSheet(): CSSStyleSheet | undefined {
  const sheets = document.styleSheets;
  return sheets[sheets.length - 1];
}
