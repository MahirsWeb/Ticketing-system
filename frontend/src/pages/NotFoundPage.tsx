import { Link } from 'react-router-dom';
import { useLanguage } from '../i18n/LanguageContext';

export default function NotFoundPage() {
  const { t } = useLanguage();
  return (
    <div className="mx-auto max-w-md py-16 text-center">
      <p className="text-5xl font-bold text-slate-400">404</p>
      <h1 className="mt-4 text-xl font-bold text-slate-900">{t('notFound_title')}</h1>
      <p className="mt-2 text-sm text-slate-600">{t('notFound_body')}</p>
      <Link to="/" className="mt-6 inline-block rounded-md bg-blue-700 px-4 py-2 text-sm font-medium text-white hover:bg-blue-800">
        {t('notFound_home')}
      </Link>
    </div>
  );
}
