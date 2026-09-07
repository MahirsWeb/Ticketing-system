import type { ReactNode } from 'react';
import { LanguageSwitcher } from '../components/LanguageSwitcher';
import { useLanguage } from '../i18n/LanguageContext';

export function AuthSplitLayout({ children }: { children: ReactNode }) {
  const { t } = useLanguage();
  return (
    <div className="flex min-h-screen bg-white">
      {/* Decorative brand panel */}
      <div className="relative hidden w-1/2 overflow-hidden bg-[#132038] lg:flex lg:flex-col lg:justify-between">
        <div
          className="absolute inset-0"
          style={{ background: 'linear-gradient(135deg, #1a2b4c 0%, #2f5ea8 100%)' }}
        />
        <div className="absolute -left-24 -top-24 h-96 w-96 rounded-full bg-white/10" />
        <div className="absolute -bottom-32 -right-16 h-[28rem] w-[28rem] rounded-full bg-white/10" />
        <div className="absolute bottom-24 left-16 h-40 w-40 rounded-full bg-white/10" />

        <div className="relative z-10 flex items-center justify-between px-14 pt-14">
          <span className="text-lg font-bold tracking-tight text-white">{t('appName')}</span>
          <LanguageSwitcher dark />
        </div>

        <div className="relative z-10 px-14 pb-20">
          <h1 className="mb-4 text-5xl font-bold leading-tight text-white">
            {t('login_heroTitle1')}
            <br />
            {t('login_heroTitle2')}
          </h1>
          <p className="max-w-sm text-sm leading-relaxed text-blue-100">{t('login_heroSubtitle')}</p>
        </div>
      </div>

      {/* Content panel */}
      <div className="relative flex w-full flex-col items-center justify-center bg-slate-50 px-6 py-12 lg:w-1/2">
        <div className="absolute right-6 top-6 lg:hidden">
          <LanguageSwitcher />
        </div>
        {children}
      </div>
    </div>
  );
}
