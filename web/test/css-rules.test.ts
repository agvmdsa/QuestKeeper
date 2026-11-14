import { readdirSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// A tiny CSS reader: enough to see each rule's selector list and declarations, so a merge that breaks a selector list
// (a list ending in a comma that swallows the next rule) or drops declarations is caught by a test, not by the owner's eyes.
interface Rule {
  /** The raw text before the brace. */
  prelude: string;
  selectors: string[];
  decls: Record<string, string>;
  /** The at-rules around it, outermost first. */
  within: string[];
}

function parse(css: string, within: string[] = []): Rule[] {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const rules: Rule[] = [];
  let i = 0;
  while (i < text.length) {
    const open = text.indexOf('{', i);
    if (open < 0) {
      expect(text.slice(i).trim(), 'text after the last rule').toBe('');
      break;
    }
    const prelude = text.slice(i, open).trim();
    // The matching brace, counting nested blocks.
    let depth = 1;
    let j = open + 1;
    for (; j < text.length && depth > 0; j += 1) {
      if (text[j] === '{') depth += 1;
      else if (text[j] === '}') depth -= 1;
    }
    expect(depth, `unclosed block after ${prelude}`).toBe(0);
    const body = text.slice(open + 1, j - 1);
    if (prelude.startsWith('@media') || prelude.startsWith('@supports')) {
      rules.push(...parse(body, [...within, prelude]));
    } else if (!prelude.startsWith('@')) {
      expect(body, `a rule inside the declarations of ${prelude}`).not.toMatch(/[{}]/);
      const decls: Record<string, string> = {};
      for (const part of body.split(';')) {
        const at = part.indexOf(':');
        if (at < 0) {
          expect(part.trim(), `a broken declaration in ${prelude}`).toBe('');
          continue;
        }
        decls[part.slice(0, at).trim()] = part.slice(at + 1).trim();
      }
      rules.push({ prelude, selectors: prelude.split(',').map((s) => s.trim()), decls, within });
    }
    i = j;
  }
  return rules;
}

const styles = new URL('../src/styles/', import.meta.url);
const read = (name: string) => readFileSync(new URL(name, styles), 'utf8');
const app = parse(read('app.css'));
/** Every top-level rule (outside any media query) whose selector list names `selector`. */
const top = (selector: string) => app.filter((r) => r.within.length === 0 && r.selectors.includes(selector));
/** The declarations a selector gets from the top-level rules, later rules winning. */
const declsOf = (selector: string) => Object.assign({}, ...top(selector).map((r) => r.decls)) as Record<string, string>;

describe('stylesheet syntax', () => {
  it.each(readdirSync(styles).filter((f) => f.endsWith('.css')))('%s has no selector list ending in a comma or an empty selector', (file) => {
    const raw = read(file).replace(/\/\*[\s\S]*?\*\//g, '');
    expect(raw).not.toMatch(/,\s*\{/);
    // A list ending in a comma right before another rule's selector, on the line before it.
    expect(raw).not.toMatch(/,\s*\n\s*[^\s{][^{;]*\{/);
    for (const rule of parse(read(file))) {
      for (const s of rule.selectors) expect(s, `an empty selector in "${rule.prelude}"`).not.toBe('');
    }
  });
});

describe('app.css keeps the meaning of its rules', () => {
  it('styles .muted as muted text, not as a row', () => {
    expect(declsOf('.muted')).toMatchObject({ color: 'var(--muted)', 'font-size': '0.9375rem' });
    for (const rule of top('.muted')) {
      expect(rule.decls['min-height'], rule.prelude).toBeUndefined();
      expect(rule.decls.display, rule.prelude).toBeUndefined();
      expect(rule.decls['border-bottom'], rule.prelude).toBeUndefined();
    }
  });
  it('styles .note and .empty as muted text, not as rows', () => {
    expect(declsOf('.note')).toMatchObject({ color: 'var(--muted)', 'font-size': '0.9375rem' });
    expect(declsOf('.empty')).toMatchObject({ color: 'var(--muted)' });
    for (const s of ['.note', '.empty']) for (const rule of top(s)) expect(rule.decls['min-height'], rule.prelude).toBeUndefined();
  });
  it('lays a .row out as a grid, at least 3rem tall, and only rows get that', () => {
    expect(declsOf('.row')).toMatchObject({ display: 'grid', 'grid-template-columns': '1fr auto', 'min-height': '3rem' });
    const rowRule = app.find((r) => r.decls['grid-template-columns'] === '1fr auto' && r.decls['min-height'] === '3rem')!;
    expect(rowRule.selectors).toEqual(['.row']);
  });
  it.each(['.notices li + li', '.quests li + li'])('separates the items of %s with a line, without making them bold', (selector) => {
    expect(declsOf(selector)).toMatchObject({ 'margin-top': '0.75rem', 'padding-top': '0.75rem', 'border-top': '1px solid var(--raised)' });
    expect(declsOf(selector)['font-weight']).toBeUndefined();
  });
  it('makes the notice title bold', () => expect(declsOf('.notice-title')).toMatchObject({ 'font-weight': '600' }));
});
