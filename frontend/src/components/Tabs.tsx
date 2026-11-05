import clsx from 'clsx';

/** A row of tabs. `active` is null when none of them is the one that is shown. `className` is for the space around the row. */
export function Tabs<K extends string>({
  label,
  tabs,
  active,
  onSelect,
  className,
}: {
  label: string;
  tabs: { key: K; label: string }[];
  active: K | null;
  onSelect: (key: K) => void;
  className?: string;
}) {
  return (
    <div className={clsx('flex gap-2 overflow-x-auto border-b border-slate-200', className)} role="tablist" aria-label={label}>
      {tabs.map((t) => (
        <button
          key={t.key}
          type="button"
          role="tab"
          aria-selected={active === t.key}
          onClick={() => onSelect(t.key)}
          className={clsx('whitespace-nowrap px-3 py-2 text-sm font-medium', active === t.key ? 'border-b-2 border-blue-700 text-blue-700' : 'text-slate-500')}
        >
          {t.label}
        </button>
      ))}
    </div>
  );
}
