import { Button } from './ui';
import { useLanguage } from '../i18n/LanguageContext';

/**
 * Previous and next. Shown when there is more than one page, and also when the address is for a page after the last (a bookmark of a list that
 * has since got shorter): without it there would be an empty list and nothing to press. "Previous" from there goes to the last page, not to the
 * one before the one that does not exist.
 */
export function Pager({ page, totalPages, onPage }: { page: number; totalPages: number; onPage: (page: number) => void }) {
  const { t } = useLanguage();
  if (totalPages <= 1 && page <= 1) return null;
  return (
    <nav aria-label={t('pager_aria')} className="mt-4 flex items-center justify-center gap-2">
      <Button variant="secondary" disabled={page <= 1} onClick={() => onPage(Math.min(page - 1, totalPages))}>
        {t('pager_previous')}
      </Button>
      <span className="text-sm text-slate-600">
        {t('pager_pageOf', { page, total: totalPages })}
      </span>
      <Button variant="secondary" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>
        {t('pager_next')}
      </Button>
    </nav>
  );
}
