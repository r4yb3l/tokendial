import en from './en.json';
import enGB from './en-GB.json';
import es from './es.json';
import fr from './fr.json';
import de from './de.json';
import ar from './ar.json';

export type Locale = 'en' | 'en-GB' | 'es' | 'fr' | 'de' | 'ar';

export const locales: Locale[] = ['en', 'en-GB', 'es', 'fr', 'de', 'ar'];
export const defaultLocale: Locale = 'en';

export const localeNames: Record<Locale, string> = {
  en: 'English',
  'en-GB': 'English (UK)',
  es: 'Español',
  fr: 'Français',
  de: 'Deutsch',
  ar: 'العربية'
};

type Dictionary = Record<string, string>;

const dictionaries: Record<Locale, Dictionary> = {
  en,
  'en-GB': { ...en, ...enGB },
  es,
  fr,
  de,
  ar
};

export function isLocale(value: string | undefined): value is Locale {
  return locales.includes(value as Locale);
}

export function dir(locale: Locale): 'ltr' | 'rtl' {
  return locale === 'ar' ? 'rtl' : 'ltr';
}

/** The BCP 47 tag for the html lang attribute. */
export function lang(locale: Locale): string {
  return locale;
}

export function pathFor(locale: Locale, path = ''): string {
  const suffix = path ? `/${path}` : '';
  return locale === defaultLocale ? `/${path}` : `/${locale}${suffix}`;
}

export function useTranslations(locale: Locale) {
  const table = dictionaries[locale];
  return (key: string, vars: Record<string, string | number> = {}): string => {
    let text = table[key] ?? en[key as keyof typeof en] ?? key;
    for (const [name, value] of Object.entries(vars)) text = text.replaceAll(`{${name}}`, String(value));
    return text;
  };
}

/** Every translated key that starts with a prefix, ordered by its numeric suffix: "alerts.1.title", "alerts.2.title"… */
export function list(locale: Locale, prefix: string): Record<string, string>[] {
  const table = { ...en, ...dictionaries[locale] };
  const rows = new Map<number, Record<string, string>>();
  for (const [key, value] of Object.entries(table)) {
    if (!key.startsWith(prefix + '.')) continue;
    const [index, field] = key.slice(prefix.length + 1).split('.');
    const n = Number(index);
    if (!Number.isInteger(n) || !field) continue;
    if (!rows.has(n)) rows.set(n, {});
    rows.get(n)![field] = value;
  }
  return [...rows.entries()].sort((a, b) => a[0] - b[0]).map(([, row]) => row);
}
