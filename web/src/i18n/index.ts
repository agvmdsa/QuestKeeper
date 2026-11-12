import { en } from './en';

export type MessageKey = keyof typeof en;
type Params = Record<string, string | number>;
type Catalog = Readonly<Record<string, string>>;

/** Keys that have both a `.one` and a `.other` sibling, without the suffix: the ones plural() takes. */
type Strip<K, S extends string> = K extends `${infer B}${S}` ? B : never;
/** The bases of a key union that have both a `.one` and an `.other` form. */
export type BaseOf<K extends string> = Extract<Strip<K, '.one'>, Strip<K, '.other'>>;
export type BaseKey = BaseOf<MessageKey>;

/** The supported locales. Adding one: a catalog file, an import and one entry here. */
export const LOCALES = { en } satisfies Record<string, Catalog>;
export type Locale = keyof typeof LOCALES;

let locale: Locale = 'en';

const isLocale = (v: string): v is Locale => Object.hasOwn(LOCALES, v);

export const getLocale = (): Locale => locale;

/** Switches the language of everything the page writes from now on; `<html lang>` follows when there is a document. */
export function setLocale(next: Locale): void {
  locale = isLocale(next) ? next : 'en';
  if (typeof document !== 'undefined') document.documentElement.lang = locale;
}

/** The first supported base language of the browser's list ("pt-BR" counts as "pt"), else English. */
export function detectLocale(languages: readonly string[]): Locale {
  for (const language of languages) {
    const base = String(language).toLowerCase().split(/[-_]/)[0] ?? '';
    if (isLocale(base)) return base;
  }
  return 'en';
}

function lookup(catalog: Catalog, key: string): string {
  const text = Object.hasOwn(catalog, key) ? catalog[key] : undefined;
  if (text !== undefined) return text;
  return Object.hasOwn(en, key) ? (en as Catalog)[key]! : '';
}

function fill(text: string, params?: Params): string {
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => (Object.hasOwn(params, name) ? String(params[name]) : whole));
}

/** The text for a key in the current language, with {name} placeholders filled. Never throws; a key the language lacks reads in English. */
export function t(key: MessageKey, params?: Params): string {
  return fill(lookup(LOCALES[locale], key), params);
}

/** Chooses `base.one` or `base.other` by the plural rules of `loc` (the current language by default). */
export function pluralFrom(catalog: Catalog, loc: string, base: string, n: number, params?: Params): string {
  let category = 'other';
  try {
    category = new Intl.PluralRules(loc).select(n);
  } catch {
    // An unknown language falls back to the generic form.
  }
  const key = category === 'one' ? `${base}.one` : `${base}.other`;
  return fill(lookup(catalog, key), { n: numberText(n, loc), ...params });
}

export function plural(base: BaseKey, n: number, params?: Params): string {
  return pluralFrom(LOCALES[locale], locale, base, n, params);
}

const formats = new Map<string, Intl.NumberFormat>();

/** A number written for a language ("1,234" or "1.5" in English); the plain digits when the language is unknown. */
function numberText(n: number, loc: string): string {
  try {
    let format = formats.get(loc);
    if (!format) {
      format = new Intl.NumberFormat(loc);
      formats.set(loc, format);
    }
    return format.format(n);
  } catch {
    return String(n);
  }
}

/** A whole number with thousands separators of the current language ("1,234" in English). */
export function formatNumberLocale(n: number): string {
  const whole = Math.trunc(n) + 0; // + 0 turns -0 into 0
  // Beyond the safe range, or not a number, the plain digits stay as they always were.
  if (!Number.isSafeInteger(whole)) return String(whole).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return numberText(whole, locale);
}
