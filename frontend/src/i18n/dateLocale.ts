import { bs } from 'date-fns/locale/bs';
import type { Locale } from 'date-fns';
import { useLanguage } from './LanguageContext';

/// The date-fns locale for the language of the screen, so that month names and relative times ("5 minutes ago") follow it.
/// English is date-fns' own default, so it is not passed at all.
export function useDateLocale(): Locale | undefined {
  const { language } = useLanguage();
  return language === 'bs' ? bs : undefined;
}
