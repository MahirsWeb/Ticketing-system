import { lookupsApi } from '../../api/lookups';
import { useTicketCategoriesAdmin } from '../../api/adminLookupQueries';
import { SimpleLookupTab } from './SimpleLookupTab';
import { SlaPlansTab } from './SlaPlansTab';
import { LIMITS } from './settingsHelpers';
import { useLanguage } from '../../i18n/LanguageContext';

/** Two lists on one tab: the internal categories of tickets and the SLA plans. */
export function CategoriesAndSlaTab() {
  const { t } = useLanguage();
  const categories = useTicketCategoriesAdmin();
  return (
    <div className="space-y-6">
      <div>
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">{t('cats_title')}</h2>
        <SimpleLookupTab
          itemNoun={t('noun_category')}
          pluralNoun={t('noun_categories')}
          maxLength={LIMITS.category}
          list={categories}
          onCreate={lookupsApi.createTicketCategory}
          onUpdate={lookupsApi.updateTicketCategory}
          onDelete={lookupsApi.deleteTicketCategory}
          deleteWarning={t('lookup_deleteWarning', { noun: t('noun_category') })}
          deactivateWarning={t('lookup_deactivateWarning', { noun: t('noun_category') })}
        />
      </div>

      <div className="border-t border-slate-200 pt-6">
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">{t('sla_title')}</h2>
        <SlaPlansTab />
      </div>
    </div>
  );
}
