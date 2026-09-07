import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { translations, type Language, type TranslationKey } from './translations';

const STORAGE_KEY = 'tst-language';

function readStoredLanguage(): Language {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'en' || stored === 'bs' ? stored : 'en';
}

interface LanguageContextValue {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: (key: TranslationKey) => string;
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
      t: (key) => translations[language][key] ?? translations.en[key] ?? key,
    }),
    [language]
  );

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}

export function useLanguage() {
  const ctx = useContext(LanguageContext);
  if (!ctx) throw new Error('useLanguage must be used within a LanguageProvider');
  return ctx;
}
