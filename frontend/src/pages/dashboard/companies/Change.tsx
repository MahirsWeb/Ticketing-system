import { useLanguage } from '../../../i18n/LanguageContext';

/** The change from the period before to this one, as a short note: "▲ 25 % (before: 4)", "new" when there was nothing before, a dash when there is nothing in either. */
export function Change({ total, previous }: { total: number; previous: number }) {
  const { t } = useLanguage();
  if (previous === 0 && total === 0) return <span className="text-xs text-slate-600">—</span>;
  if (previous === 0) return <span className="text-xs font-medium text-blue-700">{t('co_newActivity')}</span>;
  const pct = Math.round(((total - previous) / previous) * 100);
  const tone = pct > 0 ? 'text-amber-700' : pct < 0 ? 'text-green-700' : 'text-slate-600';
  const arrow = pct > 0 ? '▲' : pct < 0 ? '▼' : '=';
  return (
    <span className={`text-xs font-medium ${tone}`} title={t('co_changeHint', { previous })}>
      {arrow} {Math.abs(pct)}% <span className="font-normal text-slate-600">({t('co_before', { previous })})</span>
    </span>
  );
}
