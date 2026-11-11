import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import type { Language } from './translations';
import { LANGUAGE_STORAGE_KEY, englishTranslate, storedLanguage, translatorFor, type Translate } from './translate';

interface LanguageContextValue {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: Translate;
}

const LanguageContext = createContext<LanguageContextValue | null>(null);

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [language, setLanguageState] = useState<Language>(storedLanguage);

  function setLanguage(lang: Language) {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, lang);
    setLanguageState(lang);
  }

  const value = useMemo<LanguageContextValue>(() => ({ language, setLanguage, t: translatorFor(language) }), [language]);

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

/// Used when a component asks for the language outside any provider. The real app is always wrapped (main.tsx), so this only
/// ever applies to a test that renders a piece in isolation: it gets the English text, which is what the keys default to, so a
/// test does not have to wrap every subtree it renders just because the component now reads a translation.
const defaultContext: LanguageContextValue = { language: 'en', setLanguage: () => {}, t: englishTranslate };

export function useLanguage() {
  return useContext(LanguageContext) ?? defaultContext;
}
