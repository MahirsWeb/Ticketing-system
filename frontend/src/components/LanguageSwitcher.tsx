import clsx from 'clsx';
import { useLanguage } from '../i18n/LanguageContext';

export function LanguageSwitcher({ dark = false }: { dark?: boolean }) {
  const { language, setLanguage } = useLanguage();

  const base = 'rounded px-1.5 py-0.5 text-xs font-semibold transition';
  const activeClass = dark ? 'bg-white/20 text-white' : 'bg-slate-200 text-slate-900';
  const inactiveClass = dark ? 'text-slate-300 hover:text-white' : 'text-slate-400 hover:text-slate-700';

  return (
    <div className="flex items-center gap-0.5" role="group" aria-label="Language">
      <button
        type="button"
        onClick={() => setLanguage('en')}
        className={clsx(base, language === 'en' ? activeClass : inactiveClass)}
      >
        EN
      </button>
      <span className={dark ? 'text-slate-500' : 'text-slate-300'}>|</span>
      <button
        type="button"
        onClick={() => setLanguage('bs')}
        className={clsx(base, language === 'bs' ? activeClass : inactiveClass)}
      >
        BS
      </button>
    </div>
  );
}
