import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { titleKeyFor } from '../lib/pageTitle';
import { useLanguage } from '../i18n/LanguageContext';

/// Keeps the browser tab's title and the page's language in step with where the person is and what language they picked
/// (WCAG 2.4.2 page titled, 3.1.1 language of the page). Renders nothing.
export function DocumentMeta() {
  const { pathname } = useLocation();
  const { t, language } = useLanguage();

  useEffect(() => {
    document.title = `${t(titleKeyFor(pathname))} · ${t('appName')}`;
  }, [pathname, t]);

  useEffect(() => {
    document.documentElement.lang = language;
  }, [language]);

  return null;
}
