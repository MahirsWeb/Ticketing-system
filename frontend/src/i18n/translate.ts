import { translations, type Language, type TranslationKey } from './translations';

export const LANGUAGE_STORAGE_KEY = 'tst-language';

/** Simple `{name}` placeholders are filled from `params`, e.g. t('x_count', { count: 5 }). No pluralisation: phrases are
 *  written so the count can sit in front without agreement ("Tiketa: 5"), which avoids Bosnian's three-way plural rules. */
export type Translate = (key: TranslationKey, params?: Record<string, string | number>) => string;

function fill(text: string, params?: Record<string, string | number>): string {
  if (!params) return text;
  let out = text;
  for (const [k, v] of Object.entries(params)) out = out.split(`{${k}}`).join(String(v));
  return out;
}

/** The translator of one language. A key missing from it falls back to the English text, and a key missing from that to the key itself. */
export function translatorFor(language: Language): Translate {
  return (key, params) => fill(translations[language][key] ?? translations.en[key] ?? key, params);
}

/** What a component gets outside any provider (a test that renders a piece in isolation), and what code without React uses for English. */
export const englishTranslate: Translate = translatorFor('en');

/** The language the person chose, for code that has no React context (the answer interceptor of the API client). */
export function storedLanguage(): Language {
  try {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    return stored === 'en' || stored === 'bs' ? stored : 'en';
  } catch {
    return 'en';
  }
}
