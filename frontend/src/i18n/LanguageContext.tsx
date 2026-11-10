import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { translations, type Language, type TranslationKey } from './translations';

const STORAGE_KEY = 'tst-language';

function readStoredLanguage(): Language {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'en' || stored === 'bs' ? stored : 'en';
}

/** Simple `{name}` placeholders are filled from `params`, e.g. t('x_count', { count: 5 }). No pluralisation: phrases are
 *  written so the count can sit in front without agreement ("Tiketa: 5"), which avoids Bosnian's three-way plural rules. */
type Translate = (key: TranslationKey, params?: Record<string, string | number>) => string;

interface LanguageContextValue {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: Translate;
}

function fill(text: string, params?: Record<string, string | number>): string {
  if (!params) return text;
  let out = text;
  for (const [k, v] of Object.entries(params)) out = out.split(`{${k}}`).join(String(v));
  return out;
}

const LanguageContext = createContext<LanguageContextValue | null>(null);

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(readStoredLanguage);

  function setLanguage(lang: Language) {
    localStorage.setItem(STORAGE_KEY, lang);
    setLanguageState(lang);
  }

  const value = useMemo<LanguageContextValue>(
    () => ({
      language,
      setLanguage,
      t: (key, params) => fill(translations[language][key] ?? translations.en[key] ?? key, params),
    }),
    [language]
  );

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

/// Used when a component asks for the language outside any provider. The real app is always wrapped (main.tsx), so this only
/// ever applies to a test that renders a piece in isolation: it gets the English text, which is what the keys default to, so a
/// test does not have to wrap every subtree it renders just because the component now reads a translation.
const defaultContext: LanguageContextValue = {
  language: 'en',
  setLanguage: () => {},
  t: (key, params) => fill(translations.en[key] ?? key, params),
};

export function useLanguage() {
  return useContext(LanguageContext) ?? defaultContext;
}
