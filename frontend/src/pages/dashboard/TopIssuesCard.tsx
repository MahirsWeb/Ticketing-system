import type { ReactNode } from 'react';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card } from '../../components/ui';
import type { TopIssueDto } from '../../types';

/** What causes the most tickets in the period, by help topic. Whatever is passed as children (the AI analysis) is shown under the chart. */
export function TopIssuesCard({ issues, children }: { issues: TopIssueDto[]; children?: ReactNode }) {
  return (
    <Card className="p-5">
      <h2 className="mb-1 text-sm font-bold uppercase tracking-wide text-slate-500">Top issues (by help topic)</h2>
      <p className="mb-4 text-xs text-slate-500">What's causing the most tickets in this period.</p>
      {issues.length === 0 ? (
        <p className="text-sm text-slate-500">No routed tickets in this period yet.</p>
      ) : (
        <ResponsiveContainer width="100%" height={Math.max(120, issues.length * 34)}>
          <BarChart data={issues} layout="vertical" margin={{ left: 24 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
            <XAxis type="number" allowDecimals={false} tick={{ fontSize: 12 }} />
            <YAxis type="category" dataKey="helpTopicName" width={160} tick={{ fontSize: 12 }} />
            <Tooltip />
            <Bar dataKey="count" fill="#2f5ea8" radius={[0, 4, 4, 0]} />
          </BarChart>
        </ResponsiveContainer>
      )}
      {children}
    </Card>
  );
}
