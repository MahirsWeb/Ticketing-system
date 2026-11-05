import { useEffect, useState } from 'react';
import { reportsApi } from '../../api/reports';
import { Button } from '../../components/ui';

export const MIN_AI_INSIGHTS_DAYS = 28;

/**
 * Asks the AI service to say what went wrong most in the selected period. Worth asking only for a longer period (28 days), and the answer
 * belongs to the selection it was made for: choosing another period, branch or employee clears it.
 */
export function AiInsights({ from, to, departmentId, agentId }: { from: Date; to: Date; departmentId?: string; agentId?: string }) {
  const [insight, setInsight] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const params = { from: from.toISOString(), to: to.toISOString(), departmentId, agentId };
  const selectionKey = `${params.from}|${params.to}|${params.departmentId}|${params.agentId}`;
  useEffect(() => {
    setInsight(null);
    setError(null);
  }, [selectionKey]);

  const rangeDays = Math.round((to.getTime() - from.getTime()) / 86400000);
  const canRun = rangeDays >= MIN_AI_INSIGHTS_DAYS;

  async function generate() {
    setLoading(true);
    setError(null);
    try {
      const res = await reportsApi.aiInsights(params);
      setInsight(res.summary);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not generate AI insights.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="mt-5 border-t border-slate-100 pt-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold text-slate-700">AI insights</h3>
          <p className="text-xs text-slate-500">
            {canRun
              ? 'Ask AI to summarize what went wrong most in the selected period and what to do about it.'
              : `Select a period of at least ${MIN_AI_INSIGHTS_DAYS} days to run AI analysis.`}
          </p>
        </div>
        <Button variant="secondary" onClick={generate} disabled={!canRun || loading}>
          {loading ? 'Analyzing…' : '✨ Generate AI insights'}
        </Button>
      </div>
      {error && <p className="mt-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {insight && <p className="mt-3 whitespace-pre-wrap rounded-md bg-blue-50 px-3 py-2 text-sm text-blue-900">{insight}</p>}
    </div>
  );
}
