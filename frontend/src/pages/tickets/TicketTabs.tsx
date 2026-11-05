import { TABS, type TicketTab } from './ticketListAddress';

/** The groups the list can be narrowed to. `active` is null when the address asks for a status that is none of them. */
export function TicketTabs({ active, onSelect }: { active: TicketTab | null; onSelect: (tab: TicketTab) => void }) {
  return (
    <div className="mb-4 flex gap-2 overflow-x-auto border-b border-slate-200" role="tablist" aria-label="Ticket groups">
      {TABS.map((t) => (
        <button
          key={t.key}
          role="tab"
          aria-selected={active === t.key}
          onClick={() => onSelect(t.key)}
          className={`whitespace-nowrap px-3 py-2 text-sm font-medium ${active === t.key ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500'}`}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}
