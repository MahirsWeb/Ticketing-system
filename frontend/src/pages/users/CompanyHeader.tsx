import { Link } from 'react-router-dom';
import { StatusPill } from '../../components/ui';
import type { CompanyItemFull } from '../../types';
import { EDIT_PANEL_ID } from './CompanyEditPanel';

/** The name of the company, whether it is active, its address and contact, the way back, and the button that opens the editing. */
export function CompanyHeader({ company, editing, onToggleEdit }: { company: CompanyItemFull; editing: boolean; onToggleEdit: () => void }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <Link to="/users?tab=companies" className="text-xs font-medium text-blue-700 hover:underline">
          ← All companies
        </Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-bold text-slate-900">{company.name}</h1>
          <StatusPill active={company.isActive} />
        </div>
        {(company.address || company.contactInfo) && (
          <p className="mt-1 text-sm text-slate-500">{[company.address, company.contactInfo].filter(Boolean).join(' · ')}</p>
        )}
      </div>
      <button
        type="button"
        aria-expanded={editing}
        aria-controls={EDIT_PANEL_ID}
        onClick={onToggleEdit}
        className="flex shrink-0 items-center gap-1.5 rounded-md border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-50"
      >
        ✏️ {editing ? 'Close' : 'Edit company details'}
      </button>
    </div>
  );
}
