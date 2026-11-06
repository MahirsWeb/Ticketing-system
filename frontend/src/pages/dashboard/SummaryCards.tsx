import { Card } from '../../components/ui';
import type { ReportSummaryDto } from '../../types';

function KpiCard({ label, value, hint, accent }: { label: string; value: string | number; hint: string; accent?: string }) {
  return (
    <Card className="p-4" title={hint}>
      <div className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-slate-600">
        {label}
        <span className="flex h-3.5 w-3.5 cursor-help items-center justify-center rounded-full border border-slate-300 text-[9px] font-bold text-slate-600">
          ?
        </span>
      </div>
      <div className={`mt-1 text-2xl font-bold ${accent ?? 'text-slate-900'}`}>{value}</div>
    </Card>
  );
}

/** The five figures at the top. A dash stands for a figure that has not arrived (or does not exist, like the SLA share when no ticket had a due date). */
export function SummaryCards({ summary }: { summary: ReportSummaryDto | null }) {
  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-5">
      <KpiCard label="New" value={summary?.totalNew ?? '—'} hint="Tickets submitted but not yet opened/routed to a branch." />
      <KpiCard
        label="Open / In progress"
        value={summary?.totalOpen ?? '—'}
        accent="text-amber-700"
        hint="Tickets submitted in this period that are opened, in progress or resolved, but not yet closed."
      />
      <KpiCard label="Closed" value={summary?.totalClosed ?? '—'} accent="text-green-700" hint="Tickets resolved and closed in this period." />
      <KpiCard
        label="Avg. resolution (h)"
        value={summary?.avgResolutionHours ?? '—'}
        hint="Average time between a ticket being opened and being closed, in hours, over the tickets closed in this period."
      />
      <KpiCard
        label="SLA compliance"
        value={summary?.slaComplianceRate != null ? `${summary.slaComplianceRate}%` : '—'}
        accent="text-blue-700"
        hint="Share of tickets closed in this period (that had a due date) which were closed by that date. Shows — when none had one."
      />
    </div>
  );
}
