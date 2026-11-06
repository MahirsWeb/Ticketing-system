import { lookupsApi } from '../../api/lookups';
import { useTicketCategoriesAdmin } from '../../api/adminLookupQueries';
import { SimpleLookupTab } from './SimpleLookupTab';
import { SlaPlansTab } from './SlaPlansTab';
import { LIMITS } from './settingsHelpers';

/** Two lists on one tab: the internal categories of tickets and the SLA plans. */
export function CategoriesAndSlaTab() {
  const categories = useTicketCategoriesAdmin();
  return (
    <div className="space-y-6">
      <div>
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">Categories</h2>
        <SimpleLookupTab
          itemNoun="category"
          pluralNoun="categories"
          maxLength={LIMITS.category}
          list={categories}
          onCreate={lookupsApi.createTicketCategory}
          onUpdate={lookupsApi.updateTicketCategory}
          onDelete={lookupsApi.deleteTicketCategory}
          deleteWarning="This category will be permanently deleted. A category that tickets already use cannot be deleted (deactivate it instead), so nothing in the history of any ticket is lost."
          deactivateWarning="Tickets currently using this category keep it and are completely unaffected — nothing changes for existing tickets or statistics. This only removes it from the picker when opening a new ticket."
        />
      </div>

      <div className="border-t border-slate-200 pt-6">
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-600">SLA Plans</h2>
        <SlaPlansTab />
      </div>
    </div>
  );
}
