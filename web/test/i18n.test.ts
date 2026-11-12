import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it } from 'vitest';
import { en } from '../src/i18n/en';
import { detectLocale, formatNumberLocale, getLocale, LOCALES, plural, pluralFrom, setLocale, t, type MessageKey } from '../src/i18n';

afterEach(() => setLocale('en'));

describe('t', () => {
  it('returns the English text', () => expect(t('common.back')).toBe('Back to characters'));
  it('fills placeholders, repeated ones too', () => {
    expect(t('list.level', { level: 82 })).toBe('Lv 82');
    expect(t('hardFarms.owned', { owned: 1, total: 40 })).toBe('1 of 40 owned');
    expect(t('duration.hoursMinutes', { h: 1, m: 5 })).toBe('1 h 5 min');
  });
  it('leaves a missing parameter visible and does not throw', () => expect(t('list.level')).toBe('Lv {level}'));
  it('does not expand a placeholder inside a parameter', () => expect(t('list.level', { level: '{level}' })).toBe('Lv {level}'));
  it('falls back without throwing for a key that is not in the catalog', () => {
    expect(t('no.such.key' as MessageKey)).toBe('');
    expect(t('constructor' as MessageKey)).toBe('');
  });
});

describe('plural', () => {
  const catalog = { 'item.one': '{n} item', 'item.other': '{n} items' };
  it('picks one for 1 and other for 0, 2 and 1.5 in English', () => {
    expect(pluralFrom(catalog, 'en', 'item', 1)).toBe('1 item');
    for (const n of [0, 2, 1.5]) expect(pluralFrom(catalog, 'en', 'item', n)).toBe(`${n} items`);
  });
  it('does not throw for an unknown language tag', () => expect(pluralFrom(catalog, 'not a tag!', 'item', 1)).toBe('1 items'));
  it('is typed on keys that have both forms (none in the catalog yet)', () => {
    const none: Parameters<typeof plural>[0][] = [];
    expect(none).toEqual([]);
  });
});

describe('locale', () => {
  it('detects the first supported base language, else English', () => {
    expect(detectLocale(['pt-BR', 'en'])).toBe('en');
    expect(detectLocale([])).toBe('en');
    expect(detectLocale(['en-GB'])).toBe('en');
    expect(detectLocale(['__proto__', 'EN_us'])).toBe('en');
  });
  it('has a single table of locales', () => expect(Object.keys(LOCALES)).toEqual(['en']));
  it('setLocale updates <html lang> when there is a document', () => {
    const root = { lang: '' };
    (globalThis as { document?: unknown }).document = { documentElement: root };
    try {
      setLocale('en');
      expect(root.lang).toBe('en');
      expect(getLocale()).toBe('en');
    } finally {
      delete (globalThis as { document?: unknown }).document;
    }
  });
  it('setLocale works without a document', () => expect(() => setLocale('en')).not.toThrow());
});

describe('formatNumberLocale', () => {
  it.each([[0, '0'], [-0, '0'], [999, '999'], [1234, '1,234'], [-1234567.9, '-1,234,567'], [93643759, '93,643,759'], [Number.NaN, 'NaN'], [Infinity, 'Infinity'], [1e21, '1e+21']])(
    '%d', (n, text) => expect(formatNumberLocale(n)).toBe(text),
  );
});

const placeholders = (text: string): string[] => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]!).sort();

/** A translation must have every key of the English catalog with the same placeholders, and nothing else. */
function assertParity(base: Record<string, string>, other: Record<string, string>): void {
  const problems: string[] = [];
  for (const key of Object.keys(base)) {
    if (!(key in other)) problems.push(`missing ${key}`);
    else if (placeholders(base[key]!).join() !== placeholders(other[key]!).join()) problems.push(`placeholders differ in ${key}`);
  }
  for (const key of Object.keys(other)) if (!(key in base)) problems.push(`extra ${key}`);
  if (problems.length > 0) throw new Error(problems.join('; '));
}

describe('catalog parity', () => {
  const pt = Object.fromEntries(Object.entries(en).map(([k, v]) => [k, v])) as Record<string, string>;
  it('accepts a complete copy', () => expect(() => assertParity(en, pt)).not.toThrow());
  it('fails on a missing key', () => {
    const { 'common.back': _gone, ...rest } = pt;
    expect(() => assertParity(en, rest)).toThrow('missing common.back');
  });
  it('fails on a changed placeholder', () => expect(() => assertParity(en, { ...pt, 'list.level': 'Nv {nivel}' })).toThrow('placeholders differ in list.level'));
  it('fails on an extra key', () => expect(() => assertParity(en, { ...pt, 'x.y': 'z' })).toThrow('extra x.y'));
  it('every plural base has both forms', () => {
    const keys = Object.keys(en);
    for (const k of keys.filter((x) => x.endsWith('.one'))) expect(keys).toContain(`${k.slice(0, -4)}.other`);
    for (const k of keys.filter((x) => x.endsWith('.other'))) expect(keys).toContain(`${k.slice(0, -6)}.one`);
  });
});

interface Literal {
  value: string;
  template: boolean;
  afterText: boolean;
}

/** The string and template literals of a source file, comments skipped. A small scanner: the sources hold no regex literal with a quote in it. */
function literals(source: string): Literal[] {
  const out: Literal[] = [];
  let i = 0;
  while (i < source.length) {
    const c = source[i]!;
    if (c === '/' && source[i + 1] === '/') {
      while (i < source.length && source[i] !== '\n') i += 1;
    } else if (c === '/' && source[i + 1] === '*') {
      i = source.indexOf('*/', i + 2) + 2;
      if (i < 2) break;
    } else if (c === "'" || c === '"' || c === '`') {
      const afterText = /\btext:\s*$/.test(source.slice(Math.max(0, i - 12), i));
      let value = '';
      i += 1;
      while (i < source.length && source[i] !== c) {
        if (source[i] === '\\') {
          value += source[i + 1] ?? '';
          i += 2;
        } else {
          value += source[i];
          i += 1;
        }
      }
      i += 1;
      out.push({ value, template: c === '`', afterText });
    } else {
      i += 1;
    }
  }
  return out;
}

/**
 * Literals that look like words but are not user text. Each is justified.
 */
const ALLOWED = new Set([
  'The attribute ${name} is not allowed.', // a programming error thrown by mount(), caught by draw(); never shown
  'muted hint', // CSS class names
  'Browser', // deviceName() fallback: the name stored on the PC for this phone, not page text
  'iPhone', // deviceName(): a device name stored on the PC
  'iPad', // deviceName(): a device name stored on the PC
  'Android phone', // deviceName(): a device name stored on the PC
  'NotConnected', // Skua connection enum value of the protocol
  'Connected', // Skua connection enum value of the protocol
  'VersionMismatch', // Skua connection enum value of the protocol
  'action:${t.action}', // focusKey(): an internal id used to restore keyboard focus, never shown
  'id:${t.id}', // focusKey(): an internal id used to restore keyboard focus, never shown
  'href:${t.href}', // focusKey(): an internal id used to restore keyboard focus, never shown
  'Enter', // KeyboardEvent.key value, not text
]);

/** Why a literal counts as page text, or null. */
function rawTextReason(lit: Literal): string | null {
  const bare = lit.template ? lit.value.replace(/\$\{[^}]*\}/g, '') : lit.value;
  if (ALLOWED.has(lit.value)) return null;
  if (lit.afterText && /[A-Za-z]/.test(bare)) return 'text property';
  if (/^[A-Z][a-z]/.test(bare)) return 'starts like a sentence';
  if (/[A-Za-z]{2,} [A-Za-z]{2,}/.test(bare)) return 'two words';
  if (lit.template && /[A-Za-z]{2,}/.test(bare)) return 'words in a template';
  return null;
}

const SOURCES = ['views.ts', 'state.ts', 'skua.ts', 'main.ts'];

function rawTexts(source: string): string[] {
  return literals(source).flatMap((l) => {
    const why = rawTextReason(l);
    return why ? [`${why}: ${l.value}`] : [];
  });
}

describe('no raw text in the page code', () => {
  for (const file of SOURCES) {
    it(`${file} takes every user text from the catalog`, () => {
      expect(rawTexts(readFileSync(new URL(`../src/${file}`, import.meta.url), 'utf8'))).toEqual([]);
    });
  }
  it('the guard catches a raw literal', () => {
    expect(rawTexts("h('p', { text: 'Hello there' })")).toHaveLength(1);
    expect(rawTexts("h('p', { text: `${n} gold` })")).toHaveLength(1);
    expect(rawTexts("empty('Nothing here')")).toHaveLength(1);
    expect(rawTexts("// 'Hello there' in a comment\nh('p', { text: t('common.back') })")).toEqual([]);
  });
});
