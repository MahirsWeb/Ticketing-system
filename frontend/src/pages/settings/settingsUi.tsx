import clsx from 'clsx';
import { Card, ErrorText } from '../../components/ui';
import { useLanguage } from '../../i18n/LanguageContext';

export function Panel({ children, className }: { children: React.ReactNode; className?: string }) {
  return <Card className={clsx('p-5', className)}>{children}</Card>;
}

export function ListRow({ children }: { children: React.ReactNode }) {
  return <div className="-mx-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 rounded-lg px-2 py-2.5 transition hover:bg-slate-50">{children}</div>;
}

/** In the place of a list that is on its way or could not be had; nothing when the list is there. */
export function ListStatus({ loading, failed, plural }: { loading: boolean; failed: boolean; plural: string }) {
  const { t } = useLanguage();
  if (loading) return <p className="py-8 text-center text-sm text-slate-600">{t('settings_loadingList', { plural })}</p>;
  if (failed) return <ErrorText>{t('settings_listLoadFail', { plural })}</ErrorText>;
  return null;
}
