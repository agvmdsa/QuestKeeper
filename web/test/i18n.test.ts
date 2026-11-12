import { readdirSync, readFileSync } from 'node:fs';
import { afterEach, describe, expect, expectTypeOf, it } from 'vitest';
import { en } from '../src/i18n/en';
import { detectLocale, formatNumberLocale, getLocale, LOCALES, plural, pluralFrom, setLocale, t, type BaseKey, type BaseOf, type MessageKey } from '../src/i18n';

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
  it('writes {n} with the thousands separators of the language', () => {
    expect(pluralFrom(catalog, 'en', 'item', 1234)).toBe('1,234 items');
    expect(pluralFrom(catalog, 'en', 'item', 1.5)).toBe('1.5 items');
  });
  it('keeps the base keys that have both forms and drops the rest (checked by tsc, which also compiles the tests)', () => {
    type Keys = 'a.one' | 'a.other' | 'b.one' | 'c.other' | 'd';
    expectTypeOf<BaseOf<Keys>>().toEqualTypeOf<'a'>();
    expectTypeOf<BaseOf<'x.one'>>().toEqualTypeOf<never>();
    expectTypeOf<BaseKey>().toEqualTypeOf<BaseOf<MessageKey>>();
    expectTypeOf(plural).parameter(0).toEqualTypeOf<BaseKey>();
    const takes = (_base: BaseOf<Keys>): void => undefined;
    takes('a');
    // @ts-expect-error b has no .other form
    takes('b');
    // @ts-expect-error an unknown base
    takes('zzz');
    expect(true).toBe(true);
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

const placeholders = (text: string): Set<string> => new Set([...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1]!));
const sameSet = (a: Set<string>, b: Set<string>): boolean => a.size === b.size && [...a].every((x) => b.has(x));

/** A translation must have every key of the English catalog with the same placeholders, and nothing else. */
function assertParity(base: Record<string, string>, other: Record<string, string>): void {
  const problems: string[] = [];
  for (const key of Object.keys(base)) {
    if (!(key in other)) problems.push(`missing ${key}`);
    else if (!sameSet(placeholders(base[key]!), placeholders(other[key]!))) problems.push(`placeholders differ in ${key}`);
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
  it('fails on a missing placeholder name, passes when only the order or repeats differ', () => {
    expect(() => assertParity({ k: '{a} {b}' }, { k: '{a}' })).toThrow('placeholders differ in k');
    expect(() => assertParity({ k: '{a} {b}' }, { k: '{b} {a} {a}' })).not.toThrow();
  });
  for (const [name, catalog] of Object.entries(LOCALES)) {
    if (name === 'en') continue;
    it(`the ${name} catalog matches English`, () => assertParity(en, catalog));
  }
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
      const afterText = TEXT_SLOT.test(source.slice(Math.max(0, i - 80), i));
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
  'card now-card', // CSS class names
  'Browser', // deviceName() fallback: the name stored on the PC for this phone, not page text
  'iPhone', // deviceName(): a device name stored on the PC
  'iPad', // deviceName(): a device name stored on the PC
  'Android phone', // deviceName(): a device name stored on the PC
  'NotConnected', // Skua connection enum value of the protocol
  'Connected', // Skua connection enum value of the protocol
  'VersionMismatch', // Skua connection enum value of the protocol
  'action:${t.action}', // focusKey(): an internal id used to restore keyboard focus, never shown
  'action:${t.action}:${t.value}', // focusKey(): an internal id used to restore keyboard focus, never shown
  'theme.${choice}', // a catalog key (theme.system, theme.light, theme.dark), resolved through t()
  'id:${t.id}', // focusKey(): an internal id used to restore keyboard focus, never shown
  'href:${t.href}', // focusKey(): an internal id used to restore keyboard focus, never shown
  '${height}px', // a CSS length for --chrome-h, not text
  'Enter', // KeyboardEvent.key value, not text
]);

/** Source files the guard skips; every other .ts file under src/ is scanned, so a new module cannot escape. */
const EXCLUDED = new Set([
  'api.ts', // network code: URLs, header names and error messages that are never shown
  'types.ts', // type declarations only: protocol enum values such as RankUp, never shown
  'i18n/en.ts', // the catalog itself
  'i18n/index.ts', // the i18n module: locale codes, Intl options and key plumbing
]);

/** A property or assignment that puts text in front of the user. */
const TEXT_SLOT = /(?:\b(?:text|title|alt|placeholder)|aria-label)['"]?\s*:\s*$|\btextContent\s*=\s*$/;

/** Why a literal counts as page text, or null. */
function rawTextReason(lit: Literal): string | null {
  const bare = lit.template ? lit.value.replace(/\$\{[^}]*\}/g, '') : lit.value;
  if (ALLOWED.has(lit.value)) return null;
  if (lit.afterText && /[A-Za-z]/.test(bare)) return 'text slot';
  if (/^[A-Z][a-z]/.test(bare)) return 'starts like a sentence';
  if (/[A-Za-z]{2,} [A-Za-z]{2,}/.test(bare)) return 'two words';
  if (lit.template && /[A-Za-z]{2,}/.test(bare)) return 'words in a template';
  return null;
}

function rawTexts(source: string): string[] {
  return literals(source).flatMap((l) => {
    const why = rawTextReason(l);
    return why ? [`${why}: ${l.value}`] : [];
  });
}

function sourceFiles(dir: URL, prefix = ''): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
    e.isDirectory() ? sourceFiles(new URL(`${e.name}/`, dir), `${prefix}${e.name}/`) : e.name.endsWith('.ts') ? [`${prefix}${e.name}`] : [],
  );
}

describe('no raw text in the page code', () => {
  const srcDir = new URL('../src/', import.meta.url);
  const files = sourceFiles(srcDir);
  it('scans the page modules', () => {
    for (const f of ['views.ts', 'state.ts', 'skua.ts', 'main.ts']) expect(files).toContain(f);
    for (const f of EXCLUDED) expect(files).toContain(f);
  });
  for (const file of files.filter((f) => !EXCLUDED.has(f))) {
    it(`${file} takes every user text from the catalog`, () => {
      expect(rawTexts(readFileSync(new URL(file, srcDir), 'utf8'))).toEqual([]);
    });
  }
  it('the guard catches a raw literal', () => {
    expect(rawTexts("h('p', { text: 'Hello there' })")).toHaveLength(1);
    expect(rawTexts("h('p', { text: `${n} gold` })")).toHaveLength(1);
    expect(rawTexts("empty('Nothing here')")).toHaveLength(1);
    expect(rawTexts("// 'Hello there' in a comment\nh('p', { text: t('common.back') })")).toEqual([]);
  });
  it.each([
    ["{ title: 'abc' }"],
    ["{ 'aria-label': 'abc' }"],
    ["{ placeholder: 'abc' }"],
    ["{ alt: 'abc' }"],
    ["el.textContent = 'abc'"],
    ["{ text:\n        'abc' }"],
    ["el.textContent =\n      'abc';"],
    ["{ text:   `abc` }"],
  ])('the guard treats %s as text', (code) => expect(rawTexts(code)).toHaveLength(1));
  it('the guard leaves ordinary literals and catalog calls alone', () => {
    expect(rawTexts("{ title: t('x'), href: '#/skua', className: 'card' }")).toEqual([]);
  });
});
