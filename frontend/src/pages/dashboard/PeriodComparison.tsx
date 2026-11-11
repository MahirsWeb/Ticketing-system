import { useState } from 'react';
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, Select } from '../../components/ui';
import type { PeriodComparisonPointDto } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';

export type GroupBy = 'week' | 'month' | 'quarter' | 'year';

const GROUP_BY_OPTIONS: { value: GroupBy; labelKey: TranslationKey }[] = [
  { value: 'week', labelKey: 'dash_byWeek' },
  { value: 'month', labelKey: 'dash_byMonth' },
  { value: 'quarter', labelKey: 'dash_byQuarter' },
  { value: 'year', labelKey: 'dash_byYear' },
];

/** Tickets per week, month, quarter or year, as numbers or as shares of the whole, with the busiest period in red and the quietest in green. */
export function PeriodComparison({
  points,
  groupBy,
  onGroupByChange,
}: {
  points: PeriodComparisonPointDto[];
  groupBy: GroupBy;
  onGroupByChange: (groupBy: GroupBy) => void;
}) {
  const { t } = useLanguage();
  const [mode, setMode] = useState<'count' | 'percent'>('count');

  const total = points.reduce((sum, p) => sum + p.count, 0);
  const chartData = points.map((p) => ({
    ...p,
    value: mode === 'percent' ? (total > 0 ? Math.round((p.count / total) * 1000) / 10 : 0) : p.count,
  }));
  const busiest = points.length > 0 ? points.reduce((a, b) => (b.count > a.count ? b : a)) : null;
  const quietest = points.length > 0 ? points.reduce((a, b) => (b.count < a.count ? b : a)) : null;

  return (
    <Card className="p-5">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-sm font-bold uppercase tracking-wide text-slate-600">{t('dash_periodComparison')}</h2>
        <div className="flex items-center gap-2">
          <div className="w-36">
            <Select aria-label={t('dash_compareByAria')} value={groupBy} onChange={(e) => onGroupByChange(e.target.value as GroupBy)}>
              {GROUP_BY_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {t(o.labelKey)}
                </option>
              ))}
            </Select>
          </div>
          <div className="flex rounded-md border border-slate-300 text-xs">
            <button
              onClick={() => setMode('count')}
              className={`px-3 py-1.5 rounded-l-md ${mode === 'count' ? 'bg-blue-700 text-white' : 'text-slate-600'}`}
            >
              {t('dash_countMode')}
            </button>
            <button
              onClick={() => setMode('percent')}
              className={`px-3 py-1.5 rounded-r-md ${mode === 'percent' ? 'bg-blue-700 text-white' : 'text-slate-600'}`}
            >
              %
            </button>
          </div>
        </div>
      </div>
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={chartData}>
          <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
          <XAxis dataKey="label" tick={{ fontSize: 11 }} />
          <YAxis tick={{ fontSize: 12 }} unit={mode === 'percent' ? '%' : ''} allowDecimals={mode === 'percent'} />
          <Tooltip formatter={(v: any) => (mode === 'percent' ? `${v}%` : v)} />
          <Bar dataKey="value" radius={[4, 4, 0, 0]}>
            {chartData.map((p) => (
              <Cell key={p.label} fill={busiest && p.label === busiest.label ? '#dc2626' : quietest && p.label === quietest.label ? '#15803d' : '#2f5ea8'} />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
      {busiest && quietest && (
        <p className="mt-3 text-xs text-slate-600">
          <span className="font-semibold text-red-600">{t('dash_busiest', { label: busiest.label })}</span> {t('dash_countTickets', { count: busiest.count })} ·{' '}
          <span className="font-semibold text-green-700">{t('dash_quietest', { label: quietest.label })}</span> {t('dash_countTickets', { count: quietest.count })}
        </p>
      )}
    </Card>
  );
}
