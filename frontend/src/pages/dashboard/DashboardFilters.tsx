import { useState } from 'react';
import { reportsApi } from '../../api/reports';
import { Button, Select } from '../../components/ui';
import { DateRangePicker } from '../../components/DateRangePicker';
import { PersonOptions } from './PersonOptions';
import type { EmployeeGroups } from './employeeGroups';

function ExportButton({ from, to, departmentId, agentId }: { from: Date; to: Date; departmentId?: string; agentId?: string }) {
  const [exporting, setExporting] = useState(false);

  async function download() {
    setExporting(true);
    try {
      await reportsApi.exportXlsx({ from: from.toISOString(), to: to.toISOString(), departmentId, agentId });
    } finally {
      setExporting(false);
    }
  }

  return (
    <Button variant="secondary" onClick={download} disabled={exporting}>
      {exporting ? 'Exporting…' : <><span aria-hidden>⬇</span> Export XLSX</>}
    </Button>
  );
}

/** The bar at the top: whose figures these are (an administrator chooses a branch and an employee, staff see their own branch), for which period, and the export. */
export function DashboardFilters({
  isAdmin,
  ownBranchName,
  departments,
  people,
  departmentId,
  agentId,
  from,
  to,
  onBranchChange,
  onAgentChange,
  onPeriodChange,
}: {
  isAdmin: boolean;
  ownBranchName: string | null | undefined;
  departments: { id: string; name: string }[];
  people: EmployeeGroups;
  departmentId: string;
  agentId: string;
  from: Date;
  to: Date;
  onBranchChange: (departmentId: string) => void;
  onAgentChange: (agentId: string) => void;
  onPeriodChange: (from: Date | null, to: Date | null) => void;
}) {
  return (
    <div className="sticky top-0 z-20 -mx-4 flex flex-wrap items-center justify-between gap-3 border-b border-slate-300 bg-slate-200 px-4 py-3">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Dashboard</h1>
        {!isAdmin && ownBranchName && <p className="text-xs text-slate-500">Showing data for your branch: {ownBranchName}</p>}
        {isAdmin && <p className="text-xs text-slate-500">Global statistics across all branches</p>}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        {isAdmin && (
          <div className="w-48">
            <Select value={departmentId} onChange={(e) => onBranchChange(e.target.value)}>
              <option value="">All branches</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          </div>
        )}
        {isAdmin && (
          <div className="w-56">
            <Select value={agentId} onChange={(e) => onAgentChange(e.target.value)}>
              <option value="">All employees</option>
              <PersonOptions groups={people} />
            </Select>
          </div>
        )}
        <DateRangePicker from={from} to={to} onChange={onPeriodChange} />
        <ExportButton from={from} to={to} departmentId={departmentId || undefined} agentId={agentId || undefined} />
      </div>
    </div>
  );
}
