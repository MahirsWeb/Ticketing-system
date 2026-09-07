import React from 'react';
import clsx from 'clsx';

export function Button({
  variant = 'primary',
  className,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' | 'danger' | 'ghost' | 'pill' }) {
  const base = 'inline-flex items-center justify-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition disabled:opacity-50 disabled:cursor-not-allowed';
  const variants: Record<string, string> = {
    primary: 'bg-blue-700 text-white hover:bg-blue-800',
    secondary: 'bg-white text-slate-700 border border-slate-300 hover:bg-slate-50',
    danger: 'bg-red-600 text-white hover:bg-red-700',
    ghost: 'bg-transparent text-slate-600 hover:bg-slate-100',
    pill: 'rounded-full bg-[#1a2b4c] text-white py-3 font-semibold shadow-md hover:bg-[#24365c]',
  };
  return <button className={clsx(base, variants[variant], className)} {...props} />;
}

export function Input({ className, ...props }: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={clsx(
        'w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600',
        className
      )}
      {...props}
    />
  );
}

export function IconInput({
  icon,
  className,
  ...props
}: React.InputHTMLAttributes<HTMLInputElement> & { icon: React.ReactNode }) {
  return (
    <div className="relative">
      <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400">{icon}</span>
      <input
        className={clsx(
          'w-full rounded-xl border border-slate-200 bg-white py-3 pl-11 pr-3.5 text-sm shadow-sm transition',
          'focus:border-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-600/20',
          className
        )}
        {...props}
      />
    </div>
  );
}

export function Select({ className, ...props }: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      className={clsx(
        'w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600',
        className
      )}
      {...props}
    />
  );
}

export function Textarea({ className, ...props }: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={clsx(
        'w-full rounded-md border border-slate-300 px-3 py-2 text-sm focus:border-blue-600 focus:outline-none focus:ring-1 focus:ring-blue-600',
        className
      )}
      {...props}
    />
  );
}

export function Label({ children }: { children: React.ReactNode }) {
  return <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-500">{children}</label>;
}

export function Card({ className, children, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={clsx('rounded-lg border border-slate-200 bg-white shadow-sm', className)} {...props}>
      {children}
    </div>
  );
}

export function ErrorText({ children }: { children?: React.ReactNode }) {
  if (!children) return null;
  return <p className="mt-2 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{children}</p>;
}

export function SuccessText({ children }: { children?: React.ReactNode }) {
  if (!children) return null;
  return <p className="mt-2 rounded-md bg-green-50 px-3 py-2 text-sm text-green-700">{children}</p>;
}

const STATUS_STYLES: Record<string, string> = {
  New: 'bg-slate-100 text-slate-700',
  Open: 'bg-amber-100 text-amber-800',
  InProgress: 'bg-blue-100 text-blue-800',
  Resolved: 'bg-teal-100 text-teal-800',
  Closed: 'bg-green-100 text-green-800',
};

export function StatusBadge({ status }: { status: string }) {
  return (
    <span className={clsx('rounded-full px-2.5 py-0.5 text-xs font-semibold', STATUS_STYLES[status] ?? 'bg-slate-100 text-slate-700')}>
      {status}
    </span>
  );
}

const PRIORITY_STYLES: Record<string, string> = {
  Emergency: 'bg-red-100 text-red-700',
  High: 'bg-orange-100 text-orange-700',
  Medium: 'bg-amber-100 text-amber-700',
  Low: 'bg-slate-100 text-slate-600',
};

export function PriorityBadge({ priority }: { priority: string | null }) {
  if (!priority) return <span className="text-xs text-slate-300">—</span>;
  return (
    <span className={clsx('rounded-full px-2.5 py-0.5 text-xs font-semibold', PRIORITY_STYLES[priority] ?? 'bg-slate-100 text-slate-600')}>
      {priority}
    </span>
  );
}

export function StatusPill({ active, activeLabel = 'Active', inactiveLabel = 'Inactive' }: { active: boolean; activeLabel?: string; inactiveLabel?: string }) {
  return (
    <span
      className={clsx(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        active ? 'bg-green-100 text-green-700' : 'bg-slate-200 text-slate-500'
      )}
    >
      <span className={clsx('h-1.5 w-1.5 rounded-full', active ? 'bg-green-500' : 'bg-slate-400')} />
      {active ? activeLabel : inactiveLabel}
    </span>
  );
}

export function Spinner({ className }: { className?: string }) {
  return (
    <svg className={clsx('animate-spin', className)} viewBox="0 0 24 24" fill="none">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
  );
}
