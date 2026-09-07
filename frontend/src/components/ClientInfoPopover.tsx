import { useState } from 'react';

function initialsOf(name: string) {
  const parts = name.trim().split(/\s+/);
  return parts.slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('') || '?';
}

// Deterministic color per name so the same client always gets the same avatar color.
function colorOf(name: string) {
  const palette = ['#2f5ea8', '#15803d', '#b45309', '#7c3aed', '#be185d', '#0f766e'];
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.charCodeAt(0)) % palette.length;
  return palette[hash];
}

export function ClientInfoPopover({
  name,
  email,
  phoneNumber,
  companyName,
}: {
  name: string;
  email?: string | null;
  phoneNumber?: string | null;
  companyName: string;
}) {
  const [open, setOpen] = useState(false);

  return (
    <span className="relative inline-block">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="font-medium text-blue-700 hover:underline"
      >
        {name}
      </button>
      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} />
          <div className="absolute left-0 top-full z-20 mt-2 w-64 rounded-lg border border-slate-200 bg-white p-4 shadow-lg">
            <div className="flex items-center gap-3">
              <span
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-sm font-bold text-white"
                style={{ backgroundColor: colorOf(name) }}
              >
                {initialsOf(name)}
              </span>
              <div className="min-w-0">
                <div className="truncate text-sm font-semibold text-slate-800">{name}</div>
                <div className="truncate text-xs text-slate-500">{companyName}</div>
              </div>
            </div>
            <dl className="mt-3 space-y-1.5 text-xs">
              <div className="flex justify-between gap-2">
                <dt className="text-slate-400">Email</dt>
                <dd className="truncate text-slate-700">{email ?? '—'}</dd>
              </div>
              <div className="flex justify-between gap-2">
                <dt className="text-slate-400">Phone</dt>
                <dd className="truncate text-slate-700">{phoneNumber ?? '—'}</dd>
              </div>
            </dl>
          </div>
        </>
      )}
    </span>
  );
}
