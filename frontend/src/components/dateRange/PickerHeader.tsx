import type { ReactNode } from 'react';

function Arrow({ label, onClick, symbol }: { label: string; onClick: () => void; symbol: string }) {
  return (
    <button type="button" onClick={onClick} aria-label={label} className="rounded p-1 text-slate-600 hover:bg-slate-100 hover:text-slate-700">
      <span aria-hidden>{symbol}</span>
    </button>
  );
}

/** The row above a view: an arrow to go back, what is shown (the caller says what it is), and an arrow to go on. */
export function PickerHeader({
  previous,
  next,
  children,
}: {
  previous: { label: string; onClick: () => void };
  next: { label: string; onClick: () => void };
  children: ReactNode;
}) {
  return (
    <div className="mb-2 flex items-center justify-between">
      <Arrow label={previous.label} onClick={previous.onClick} symbol="‹" />
      {children}
      <Arrow label={next.label} onClick={next.onClick} symbol="›" />
    </div>
  );
}

export const HEADING_BUTTON = 'rounded px-2 py-0.5 text-sm font-semibold text-slate-700 hover:bg-slate-100';
