import { useMemo, useState } from 'react';
import { format } from 'date-fns';
import { CartesianGrid, Legend, Line, LineChart, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { zoomRange } from '../../lib/chartRange';
import { Card } from '../../components/ui';
import type { TimeSeriesPointDto } from '../../types';

/**
 * Tickets opened and closed per day. Dragging across the chart narrows the period of the whole dashboard to those days, a right click goes
 * back one step and "Reset zoom" goes back to the period the dashboard had before the first zoom. The period itself belongs to the page:
 * this part only asks for it to be changed.
 */
export function TicketsOverTime({
  series,
  from,
  to,
  onRangeChange,
}: {
  series: TimeSeriesPointDto[] | undefined;
  from: Date;
  to: Date;
  onRangeChange: (from: Date, to: Date) => void;
}) {
  const [hideWeekends, setHideWeekends] = useState(false);
  const [zoomStack, setZoomStack] = useState<{ from: Date; to: Date }[]>([]);
  const [refAreaLeft, setRefAreaLeft] = useState<string | null>(null);
  const [refAreaRight, setRefAreaRight] = useState<string | null>(null);

  const chartData = useMemo(
    () => (hideWeekends ? (series ?? []).filter((p) => ![0, 6].includes(new Date(p.date).getDay())) : (series ?? [])),
    [series, hideWeekends]
  );

  function handleMouseDown(e: any) {
    if (e?.activeLabel) setRefAreaLeft(e.activeLabel);
  }
  function handleMouseMove(e: any) {
    if (refAreaLeft && e?.activeLabel) setRefAreaRight(e.activeLabel);
  }
  function handleMouseUp() {
    if (refAreaLeft && refAreaRight) {
      const range = zoomRange(refAreaLeft, refAreaRight);
      if (range) {
        setZoomStack((s) => [...s, { from, to }]);
        onRangeChange(range.from, range.to);
      }
    }
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }
  function handleContextMenu(e: any) {
    e.preventDefault();
    if (zoomStack.length === 0) return;
    const previous = zoomStack[zoomStack.length - 1];
    onRangeChange(previous.from, previous.to);
    setZoomStack(zoomStack.slice(0, -1));
  }

  return (
    <Card className="p-5">
      <div className="mb-1 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-sm font-bold uppercase tracking-wide text-slate-500">Tickets over time</h2>
        <div className="flex items-center gap-3">
          {zoomStack.length > 0 && (
            <button
              type="button"
              onClick={() => {
                const first = zoomStack[0];
                onRangeChange(first.from, first.to);
                setZoomStack([]);
              }}
              className="text-xs text-slate-500 hover:text-slate-600 hover:underline"
            >
              Reset zoom
            </button>
          )}
          <label className="flex items-center gap-1.5 text-xs text-slate-500">
            <input type="checkbox" checked={hideWeekends} onChange={(e) => setHideWeekends(e.target.checked)} />
            Hide weekends
          </label>
        </div>
      </div>
      <p className="mb-3 text-xs text-slate-500">Drag to zoom into a range · right-click to zoom back out</p>
      <div onContextMenu={handleContextMenu} className="select-none">
        <ResponsiveContainer width="100%" height={280}>
          <LineChart data={chartData} onMouseDown={handleMouseDown} onMouseMove={handleMouseMove} onMouseUp={handleMouseUp}>
            <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
            <XAxis dataKey="date" tick={{ fontSize: 12 }} tickFormatter={(d) => format(new Date(d), 'dd MMM')} allowDataOverflow />
            <YAxis tick={{ fontSize: 12 }} allowDecimals={false} />
            <Tooltip labelFormatter={(d) => (d ? format(new Date(d as string), 'dd MMM yyyy') : '')} />
            <Legend />
            <Line type="monotone" dataKey="opened" name="Opened" stroke="#2f5ea8" strokeWidth={2} dot={false} />
            <Line type="monotone" dataKey="closed" name="Closed" stroke="#15803d" strokeWidth={2} dot={false} />
            {refAreaLeft && refAreaRight && <ReferenceArea x1={refAreaLeft} x2={refAreaRight} strokeOpacity={0.3} fill="#2f5ea8" fillOpacity={0.15} />}
          </LineChart>
        </ResponsiveContainer>
      </div>
    </Card>
  );
}
