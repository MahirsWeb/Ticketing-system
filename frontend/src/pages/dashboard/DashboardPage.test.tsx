import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { endOfDay, parseISO, startOfDay, subDays } from 'date-fns';
import { QueryClient } from '@tanstack/react-query';
import DashboardPage from './DashboardPage';
import { reportsApi } from '../../api/reports';
import { usersApi } from '../../api/users';
import { workTasksApi } from '../../api/workTasks';
import { lookupsApi } from '../../api/lookups';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';

vi.mock('../../api/reports', () => ({
  reportsApi: { summary: vi.fn(), timeSeries: vi.fn(), leaderboard: vi.fn(), topIssues: vi.fn(), byBranch: vi.fn(), periodComparison: vi.fn(), aiInsights: vi.fn(), exportXlsx: vi.fn() },
}));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn() } }));
vi.mock('../../api/workTasks', () => ({ workTasksApi: { gantt: vi.fn(), ganttBranch: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { departments: vi.fn() } }));

// The charts are stood in for by text that says what they were given, and the line chart by three buttons that do what the mouse does to it.
vi.mock('recharts', () => {
  const nothing = () => null;
  return {
    ResponsiveContainer: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
    LineChart: ({ data, onMouseDown, onMouseMove, onMouseUp }: { data: { date: string }[]; onMouseDown: (e: unknown) => void; onMouseMove: (e: unknown) => void; onMouseUp: () => void }) => (
      <div>
        <p data-testid="points">{data.map((p) => p.date.slice(0, 10)).join(',')}</p>
        <button onClick={() => onMouseDown({ activeLabel: '2026-10-10' })}>press on the 10th</button>
        <button onClick={() => onMouseMove({ activeLabel: '2026-10-12' })}>drag to the 12th</button>
        <button onClick={() => onMouseUp()}>let go</button>
      </div>
    ),
    BarChart: ({ data, layout }: { data: { label?: string; helpTopicName?: string; value?: number; count?: number }[]; layout?: string }) => (
      <p data-testid={layout === 'vertical' ? 'issues-chart' : 'period-chart'}>{data.map((d) => `${d.label ?? d.helpTopicName}:${d.value ?? d.count}`).join(',')}</p>
    ),
    Line: nothing, Bar: nothing, Cell: nothing, XAxis: nothing, YAxis: nothing, CartesianGrid: nothing, Tooltip: nothing, Legend: nothing, ReferenceArea: nothing,
  };
});
vi.mock('../../components/GanttChart', () => ({
  GanttChart: ({ entries }: { entries: { title: string }[] }) => <p data-testid="gantt">{entries.map((e) => e.title).join(',') || 'nothing'}</p>,
}));
vi.mock('../../components/DateRangePicker', () => ({
  DateRangePicker: ({ from, to, onChange }: { from: Date; to: Date; onChange: (f: Date, t: Date) => void }) => (
    <div>
      <span data-testid="range">{`${from.toISOString()}|${to.toISOString()}`}</span>
      <button onClick={() => onChange(new Date('2026-11-01T00:00:00Z'), new Date('2026-11-05T00:00:00Z'))}>pick a short period</button>
      <button onClick={() => onChange(new Date('2026-09-01T00:00:00Z'), new Date('2026-10-15T00:00:00Z'))}>pick a long period</button>
      <button onClick={() => onChange(new Date('2026-10-08T00:00:00Z'), new Date('2026-11-05T00:00:00Z'))}>pick exactly 28 days</button>
      <button onClick={() => onChange(new Date('2026-10-09T00:00:00Z'), new Date('2026-11-05T00:00:00Z'))}>pick 27 days</button>
      <button onClick={() => (onChange as (f: Date | null, t: Date | null) => void)(null, null)}>clear the period</button>
    </div>
  ),
}));

const NOW = new Date('2026-11-05T12:00:00Z');
// 30 calendar days back in the zone of the person looking (a clock change in between does not make it 30 x 24 hours).
const THIRTY_DAYS_AGO = subDays(NOW, 30);

const admin = { id: 'admin1', role: 'Admin', firstName: 'Admin', lastName: 'Tester', departmentId: null, departmentName: null } as never;
const staff = { id: 'e1', role: 'Employee', firstName: 'Amra', lastName: 'Agent', departmentId: 'd1', departmentName: 'Sarajevo' } as never;

const summary = (totalNew: number) => ({ totalNew, totalOpen: 7, totalClosed: 11, avgResolutionHours: 5.5, slaComplianceRate: 92 }) as never;
const branches = [{ id: 'd1', name: 'Sarajevo' }, { id: 'd2', name: 'Mostar' }] as never;
const employees = [
  { id: 'e1', firstName: 'Amra', lastName: 'Agent', role: 'Employee', departmentId: 'd1', departmentName: 'Sarajevo' },
  { id: 'e2', firstName: 'Dino', lastName: 'Dinić', role: 'Employee', departmentId: 'd2', departmentName: 'Mostar' },
] as never;

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const asked = (fn: unknown) => vi.mocked(fn as (...a: unknown[]) => unknown).mock.calls;

describe('the dashboard', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now: NOW, toFake: ['Date'] });
    for (const fn of Object.values(reportsApi)) vi.mocked(fn).mockReset();
    vi.mocked(usersApi.list).mockReset().mockResolvedValue(employees);
    vi.mocked(workTasksApi.gantt).mockReset().mockResolvedValue({ entries: [{ title: 'Own ticket' }] } as never);
    vi.mocked(workTasksApi.ganttBranch).mockReset().mockResolvedValue({ users: [{ userId: 'e1', userName: 'Amra Agent', entries: [{ title: 'Branch ticket' }] }] } as never);
    vi.mocked(lookupsApi.departments).mockReset().mockResolvedValue(branches);
    vi.mocked(reportsApi.summary).mockResolvedValue(summary(31));
    vi.mocked(reportsApi.timeSeries).mockResolvedValue([
      { date: '2026-11-06T12:00:00Z', opened: 1, closed: 0 }, // a Friday
      { date: '2026-11-07T12:00:00Z', opened: 2, closed: 1 }, // a Saturday
      { date: '2026-11-08T12:00:00Z', opened: 0, closed: 0 }, // a Sunday
      { date: '2026-11-09T12:00:00Z', opened: 3, closed: 2 }, // a Monday
    ] as never);
    vi.mocked(reportsApi.leaderboard).mockResolvedValue(
      Array.from({ length: 7 }, (_, i) => ({ userId: `u${i}`, name: `Agent ${i}`, closedCount: 20 - i })) as never
    );
    vi.mocked(reportsApi.topIssues).mockResolvedValue([{ helpTopicName: 'Printer', count: 4 }] as never);
    vi.mocked(reportsApi.byBranch).mockResolvedValue([{ departmentId: 'd1', departmentName: 'Sarajevo', totalNew: 1, totalOpen: 2, totalClosed: 3 }] as never);
    vi.mocked(reportsApi.periodComparison).mockResolvedValue([{ label: 'Oct', count: 10 }, { label: 'Nov', count: 30 }, { label: 'Dec', count: 20 }] as never);
    useAuthStore.setState({ accessToken: 't', user: admin });
  });
  afterEach(() => vi.useRealTimers());

  const renderPage = (client?: QueryClient) => renderWithQuery(<DashboardPage />, client);
  // The test client forgets everything at once; this one keeps what the application's own client would, so that what the page asks to be forgotten is what is tested.
  const clientThatKeeps = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const settled = async () => {
    await waitFor(() => expect(reportsApi.summary).toHaveBeenCalled());
    await screen.findByText('Printer:4');
  };

  it('asks once for every figure, for the last 30 days, and shows them', async () => {
    renderPage();
    await settled();

    const params = { from: THIRTY_DAYS_AGO.toISOString(), to: NOW.toISOString(), departmentId: undefined, agentId: undefined };
    expect(asked(reportsApi.summary)).toEqual([[params]]);
    expect(asked(reportsApi.timeSeries)).toEqual([[params]]);
    expect(asked(reportsApi.leaderboard)).toEqual([[params]]);
    expect(asked(reportsApi.topIssues)).toEqual([[{ ...params, limit: 15 }]]);
    expect(asked(reportsApi.byBranch)).toEqual([[{ from: params.from, to: params.to }]]);
    expect(asked(reportsApi.periodComparison)).toEqual([[{ ...params, groupBy: 'month' }]]);
    expect(await screen.findByText('31')).toBeInTheDocument(); // new
    expect(screen.getByText('92%')).toBeInTheDocument();
    expect(screen.getByText('5.5')).toBeInTheDocument();
    expect(within(screen.getByText('By branch').parentElement!).getByText('Sarajevo')).toBeInTheDocument(); // the branch breakdown
    expect(screen.getByTestId('period-chart')).toHaveTextContent('Oct:10,Nov:30,Dec:20');
    expect(screen.getByText(/Busiest: Nov/)).toBeInTheDocument();
    expect(screen.getByText(/Quietest: Oct/)).toBeInTheDocument();
  });

  it('shows staff their own branch: no breakdown by branch is asked for and the people to choose from are those of their branch', async () => {
    useAuthStore.setState({ accessToken: 't', user: staff });

    renderPage();
    await settled();

    expect(reportsApi.byBranch).not.toHaveBeenCalled();
    expect(screen.queryByText('By branch')).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('All branches')).not.toBeInTheDocument();
    expect(asked(usersApi.list)).toEqual([[{ role: 'Employee', departmentId: 'd1' }]]);
    expect(lookupsApi.departments).not.toHaveBeenCalled();
    expect(screen.getByText(/Showing data for your branch: Sarajevo/)).toBeInTheDocument();
  });

  it('asks for the whole staff, not one branch, when the person is an administrator', async () => {
    renderPage();
    await settled();

    expect(asked(usersApi.list)).toEqual([[{ role: 'Employee', departmentId: undefined }]]);
  });

  it('asks again for the selected branch and forgets the selected employee', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    await user.selectOptions(await screen.findByDisplayValue('All employees'), 'e2');
    await waitFor(() => expect(asked(reportsApi.summary).at(-1)![0]).toMatchObject({ agentId: 'e2' }));

    await user.selectOptions(screen.getByDisplayValue('All branches'), 'd1');

    await waitFor(() => expect(asked(reportsApi.summary).at(-1)![0]).toMatchObject({ departmentId: 'd1', agentId: undefined }));
    expect(asked(reportsApi.leaderboard).at(-1)![0]).toMatchObject({ departmentId: 'd1', agentId: undefined });
    expect(asked(reportsApi.topIssues).at(-1)![0]).toMatchObject({ departmentId: 'd1', limit: 15 });
    expect(asked(reportsApi.periodComparison).at(-1)![0]).toMatchObject({ departmentId: 'd1', groupBy: 'month' });
    expect(screen.getByDisplayValue('All employees')).toBeInTheDocument();
  });

  it('shows the answer to the latest selection even when the answer to an earlier one arrives last', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    const first = deferred<never>();
    const second = deferred<never>();
    vi.mocked(reportsApi.summary).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);

    await user.selectOptions(screen.getByDisplayValue('All branches'), 'd1');
    await user.selectOptions(screen.getByDisplayValue('Sarajevo'), 'd2');
    await act(async () => second.resolve(summary(22)));
    await act(async () => first.resolve(summary(13)));

    expect(await screen.findByText('22')).toBeInTheDocument();
    expect(screen.queryByText('13', { selector: 'div' })).not.toBeInTheDocument();
  });

  it('shows a share of 0% as 0% and a share that does not exist as a dash', async () => {
    vi.mocked(reportsApi.summary).mockResolvedValue({ totalNew: 3, totalOpen: 7, totalClosed: 11, avgResolutionHours: 5.5, slaComplianceRate: 0 } as never);
    const first = renderPage();
    await settled();
    expect(await screen.findByText('0%')).toBeInTheDocument();
    first.unmount();

    vi.mocked(reportsApi.summary).mockResolvedValue({ totalNew: 3, totalOpen: 7, totalClosed: 11, avgResolutionHours: 5.5, slaComplianceRate: null } as never);
    renderPage();

    expect(await screen.findByText('11')).toBeInTheDocument();
    const card = screen.getByText('SLA compliance').parentElement!.parentElement!;
    expect(within(card).getByText('—')).toBeInTheDocument();
    expect(within(card).queryByText('0%')).not.toBeInTheDocument();
  });

  it('numbers the top agents', async () => {
    renderPage();
    await settled();

    expect(await screen.findByText('#1')).toBeInTheDocument();
    expect(screen.getByText('#5')).toBeInTheDocument();
  });

  it('says there are no routed tickets, and draws no chart, when there are no issues', async () => {
    vi.mocked(reportsApi.topIssues).mockResolvedValue([]);

    renderPage();
    await waitFor(() => expect(reportsApi.topIssues).toHaveBeenCalled());

    expect((await screen.findAllByText('No routed tickets in this period yet.')).length).toBeGreaterThan(0);
    expect(screen.queryByTestId('issues-chart')).not.toBeInTheDocument();
  });

  it('lists the people under the name of their branch, and only those of the chosen branch once one is chosen', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    const select = await screen.findByDisplayValue('All employees');
    await waitFor(() => expect(within(select).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Mostar', 'Sarajevo']));
    expect(within(select).getByRole('option', { name: 'Dino Dinić (Employee)' })).toBeInTheDocument();

    await user.selectOptions(screen.getByDisplayValue('All branches'), 'd1');

    const narrowed = screen.getByDisplayValue('All employees');
    expect(within(narrowed).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Sarajevo']);
    expect(within(narrowed).queryByRole('option', { name: /Dino/ })).not.toBeInTheDocument();
    expect(within(narrowed).getByRole('option', { name: 'Amra Agent (Employee)' })).toBeInTheDocument();
  });

  it('keeps showing the figures of the previous selection while those of the new one are on their way', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    expect(await screen.findByText('31')).toBeInTheDocument();
    const pending = deferred<never>();
    vi.mocked(reportsApi.summary).mockReturnValueOnce(pending.promise);
    vi.mocked(reportsApi.leaderboard).mockReturnValueOnce(new Promise<never>(() => {}));

    await user.selectOptions(screen.getByDisplayValue('All branches'), 'd1');

    await waitFor(() => expect(asked(reportsApi.summary)).toHaveLength(2));
    expect(screen.getByText('31')).toBeInTheDocument(); // not "—", and not "no closed tickets in this period yet"
    expect(screen.queryByText('No closed tickets in this period yet.')).not.toBeInTheDocument();
    await act(async () => pending.resolve(summary(44)));
    expect(await screen.findByText('44')).toBeInTheDocument();
  });

  it('does not show figures from memory when the page is left and opened again', async () => {
    const client = clientThatKeeps();
    const first = renderPage(client);
    await settled();
    expect(await screen.findByText('31')).toBeInTheDocument();
    first.unmount();
    await new Promise((r) => setTimeout(r, 10));
    vi.mocked(reportsApi.summary).mockReturnValueOnce(new Promise<never>(() => {}));

    renderPage(client);

    await waitFor(() => expect(asked(reportsApi.summary)).toHaveLength(2));
    expect(screen.queryByText('31')).not.toBeInTheDocument();
  });

  it('does not ask again for the breakdown by branch when only the branch or the employee changes, because it depends only on the period', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();

    await user.selectOptions(screen.getByDisplayValue('All branches'), 'd1');
    await waitFor(() => expect(asked(reportsApi.summary)).toHaveLength(2));
    await user.selectOptions(screen.getByDisplayValue('All employees'), 'e1');
    await waitFor(() => expect(asked(reportsApi.summary)).toHaveLength(3));

    expect(asked(reportsApi.byBranch)).toHaveLength(1);

    await user.click(screen.getByRole('button', { name: 'pick a long period' }));
    await waitFor(() => expect(asked(reportsApi.byBranch)).toHaveLength(2));
  });

  it.each(['summary', 'timeSeries', 'leaderboard', 'topIssues', 'byBranch', 'periodComparison'] as const)('says so when the %s cannot be loaded', async (figure) => {
    vi.mocked(reportsApi[figure]).mockRejectedValue(new Error('network'));

    renderPage();

    expect(await screen.findByText(/Some of the figures could not be loaded for this selection/)).toBeInTheDocument();
  });

  it('says so when some of the figures cannot be loaded, and still shows the others', async () => {
    vi.mocked(reportsApi.leaderboard).mockRejectedValue(new Error('network'));

    renderPage();

    expect(await screen.findByText(/Some of the figures could not be loaded for this selection/)).toBeInTheDocument();
    expect(await screen.findByText('31')).toBeInTheDocument();
    expect(screen.getByText('Printer:4')).toBeInTheDocument();
  });

  it('takes the message about missing figures away when the next selection loads', async () => {
    const user = userEvent.setup();
    vi.mocked(reportsApi.leaderboard).mockRejectedValueOnce(new Error('network'));
    renderPage();
    expect(await screen.findByText(/Some of the figures could not be loaded for this selection/)).toBeInTheDocument();

    await user.selectOptions(await screen.findByDisplayValue('All branches'), 'd1');

    await waitFor(() => expect(screen.queryByText(/Some of the figures could not be loaded for this selection/)).not.toBeInTheDocument());
  });

  it('starts with the last 30 days selected, and goes back to them when the picker is cleared', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    const thirtyDays = `${THIRTY_DAYS_AGO.toISOString()}|${NOW.toISOString()}`;
    expect(screen.getByTestId('range')).toHaveTextContent(thirtyDays);
    await user.click(screen.getByRole('button', { name: 'pick a short period' }));
    expect(screen.getByTestId('range')).not.toHaveTextContent(thirtyDays);

    await user.click(screen.getByRole('button', { name: 'clear the period' }));

    expect(screen.getByTestId('range')).toHaveTextContent(thirtyDays);
  });

  it('asks only for the period comparison again when the grouping changes', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();

    await user.selectOptions(screen.getByDisplayValue('By month'), 'week');

    await waitFor(() => expect(asked(reportsApi.periodComparison)).toHaveLength(2));
    expect(asked(reportsApi.periodComparison).at(-1)![0]).toMatchObject({ groupBy: 'week' });
    expect(asked(reportsApi.summary)).toHaveLength(1);
    expect(asked(reportsApi.timeSeries)).toHaveLength(1);
    expect(asked(reportsApi.topIssues)).toHaveLength(1);
  });

  it('shows the period comparison as shares of the whole when asked to', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();

    await user.click(screen.getByRole('button', { name: '%' }));

    expect(screen.getByTestId('period-chart')).toHaveTextContent('Oct:16.7,Nov:50,Dec:33.3');
  });

  it('switches between counts and shares of the whole, in both directions', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();

    await user.click(screen.getByRole('button', { name: '%' }));
    expect(screen.getByTestId('period-chart')).toHaveTextContent('Oct:16.7,Nov:50,Dec:33.3');
    await user.click(screen.getByRole('button', { name: 'Count' }));

    expect(screen.getByTestId('period-chart')).toHaveTextContent('Oct:10,Nov:30,Dec:20');
  });

  it('shows the busiest and the quietest period only when there are some', async () => {
    vi.mocked(reportsApi.periodComparison).mockResolvedValue([]);

    renderPage();
    await settled();

    expect(screen.queryByText(/Busiest/)).not.toBeInTheDocument();
  });

  it('can leave the weekends out of the chart of tickets over time', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    expect(await screen.findByTestId('points')).toHaveTextContent('2026-11-06,2026-11-07,2026-11-08,2026-11-09');

    await user.click(screen.getByRole('checkbox', { name: /Hide weekends/ }));

    expect(screen.getByTestId('points')).toHaveTextContent('2026-11-06,2026-11-09');
  });

  it('zooms into the days that were dragged across, goes back with "Reset zoom", and one step back with a right click', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    const zoomedFrom = startOfDay(parseISO('2026-10-10')).toISOString();
    const zoomedTo = endOfDay(parseISO('2026-10-12')).toISOString();

    await user.click(await screen.findByRole('button', { name: 'press on the 10th' }));
    await user.click(screen.getByRole('button', { name: 'drag to the 12th' }));
    await user.click(screen.getByRole('button', { name: 'let go' }));

    await waitFor(() => expect(asked(reportsApi.summary).at(-1)![0]).toMatchObject({ from: zoomedFrom, to: zoomedTo }));
    expect(screen.getByTestId('range')).toHaveTextContent(`${zoomedFrom}|${zoomedTo}`);

    await user.click(screen.getByRole('button', { name: 'Reset zoom' }));

    await waitFor(() => expect(asked(reportsApi.summary).at(-1)![0]).toMatchObject({ from: THIRTY_DAYS_AGO.toISOString(), to: NOW.toISOString() }));
    expect(screen.queryByRole('button', { name: 'Reset zoom' })).not.toBeInTheDocument();

    // and a right click takes back one step, whatever the chart under the pointer
    await user.click(screen.getByRole('button', { name: 'press on the 10th' }));
    await user.click(screen.getByRole('button', { name: 'drag to the 12th' }));
    await user.click(screen.getByRole('button', { name: 'let go' }));
    await screen.findByRole('button', { name: 'Reset zoom' });
    fireEvent.contextMenu(screen.getByTestId('points').parentElement!.parentElement!);
    await waitFor(() => expect(screen.getByTestId('range')).toHaveTextContent(`${THIRTY_DAYS_AGO.toISOString()}|${NOW.toISOString()}`));
  });

  it('goes back to the very first period with "Reset zoom", however many times it was zoomed in', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    for (let step = 0; step < 2; step++) {
      await user.click(await screen.findByRole('button', { name: 'press on the 10th' }));
      await user.click(screen.getByRole('button', { name: 'drag to the 12th' }));
      await user.click(screen.getByRole('button', { name: 'let go' }));
    }

    await user.click(await screen.findByRole('button', { name: 'Reset zoom' }));

    await waitFor(() => expect(screen.getByTestId('range')).toHaveTextContent(`${THIRTY_DAYS_AGO.toISOString()}|${NOW.toISOString()}`));
    expect(screen.queryByRole('button', { name: 'Reset zoom' })).not.toBeInTheDocument();
  });

  it('takes one step back with a right click, keeps the browser menu from opening, and has nothing left to reset afterwards', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    await user.click(await screen.findByRole('button', { name: 'press on the 10th' }));
    await user.click(screen.getByRole('button', { name: 'drag to the 12th' }));
    await user.click(screen.getByRole('button', { name: 'let go' }));
    await screen.findByRole('button', { name: 'Reset zoom' });

    const notCancelled = fireEvent.contextMenu(screen.getByTestId('points'));

    expect(notCancelled).toBe(false); // the page cancelled the browser's own menu
    await waitFor(() => expect(screen.getByTestId('range')).toHaveTextContent(`${THIRTY_DAYS_AGO.toISOString()}|${NOW.toISOString()}`));
    expect(screen.queryByRole('button', { name: 'Reset zoom' })).not.toBeInTheDocument();
  });

  it('keeps the browser menu closed over the chart even when there is nothing to zoom out of', async () => {
    renderPage();
    await settled();

    const notCancelled = fireEvent.contextMenu(await screen.findByTestId('points'));

    expect(notCancelled).toBe(false);
    expect(screen.getByTestId('range')).toHaveTextContent(`${THIRTY_DAYS_AGO.toISOString()}|${NOW.toISOString()}`);
  });

  it('does not zoom to the end of an earlier drag when the mouse is pressed and released on one day', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    await user.click(await screen.findByRole('button', { name: 'press on the 10th' }));
    await user.click(screen.getByRole('button', { name: 'drag to the 12th' }));
    await user.click(screen.getByRole('button', { name: 'let go' }));
    await user.click(await screen.findByRole('button', { name: 'Reset zoom' }));
    await waitFor(() => expect(asked(reportsApi.summary).at(-1)![0]).toMatchObject({ from: THIRTY_DAYS_AGO.toISOString() }));
    const before = asked(reportsApi.summary).length;

    await user.click(screen.getByRole('button', { name: 'press on the 10th' }));
    await user.click(screen.getByRole('button', { name: 'let go' }));

    expect(screen.queryByRole('button', { name: 'Reset zoom' })).not.toBeInTheDocument();
    expect(asked(reportsApi.summary)).toHaveLength(before);
  });

  it('does not zoom when the mouse is released on the day it was pressed on', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();

    await user.click(await screen.findByRole('button', { name: 'press on the 10th' }));
    await user.click(screen.getByRole('button', { name: 'let go' }));

    expect(screen.queryByRole('button', { name: 'Reset zoom' })).not.toBeInTheDocument();
    expect(asked(reportsApi.summary)).toHaveLength(1);
  });

  describe('the AI insights', () => {
    it('are offered for a period of at least 28 days and not for a shorter one', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();
      expect(screen.getByRole('button', { name: /Generate AI insights/ })).toBeEnabled();

      await user.click(screen.getByRole('button', { name: 'pick a short period' }));

      expect(screen.getByRole('button', { name: /Generate AI insights/ })).toBeDisabled();
      expect(screen.getByText(/at least 28 days/)).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'pick a long period' }));

      expect(screen.getByRole('button', { name: /Generate AI insights/ })).toBeEnabled();
    });

    it('are offered from exactly 28 days, and not for 27', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: 'pick 27 days' }));
      expect(screen.getByRole('button', { name: /Generate AI insights/ })).toBeDisabled();

      await user.click(screen.getByRole('button', { name: 'pick exactly 28 days' }));
      expect(screen.getByRole('button', { name: /Generate AI insights/ })).toBeEnabled();
    });

    it('are asked for the selected period and shown, and a new selection clears them', async () => {
      const user = userEvent.setup();
      vi.mocked(reportsApi.aiInsights).mockResolvedValue({ summary: 'Most tickets are about printers.' });
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: /Generate AI insights/ }));

      expect(await screen.findByText('Most tickets are about printers.')).toBeInTheDocument();
      expect(asked(reportsApi.aiInsights)).toEqual([[{ from: THIRTY_DAYS_AGO.toISOString(), to: NOW.toISOString(), departmentId: undefined, agentId: undefined }]]);

      await user.click(screen.getByRole('button', { name: 'pick a long period' }));

      await waitFor(() => expect(screen.queryByText('Most tickets are about printers.')).not.toBeInTheDocument());
    });

    it('say why they could not be made, in the server\'s words, or plainly when it said nothing', async () => {
      const user = userEvent.setup();
      vi.mocked(reportsApi.aiInsights).mockRejectedValueOnce({ response: { data: { message: 'The AI service is not set up.' } } }).mockRejectedValueOnce(new Error('network'));
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: /Generate AI insights/ }));
      expect(await screen.findByText('The AI service is not set up.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /Generate AI insights/ }));
      expect(await screen.findByText('Could not generate AI insights.')).toBeInTheDocument();
    });

    it('are cleared by a new choice of the branch alone', async () => {
      const user = userEvent.setup();
      vi.mocked(reportsApi.aiInsights).mockResolvedValue({ summary: 'Most tickets are about printers.' });
      renderPage();
      await settled();
      await user.click(screen.getByRole('button', { name: /Generate AI insights/ }));
      expect(await screen.findByText('Most tickets are about printers.')).toBeInTheDocument();

      await user.selectOptions(screen.getByDisplayValue('All branches'), 'd1');

      await waitFor(() => expect(screen.queryByText('Most tickets are about printers.')).not.toBeInTheDocument());
    });

    it('are cleared by a new choice of the employee alone', async () => {
      const user = userEvent.setup();
      vi.mocked(reportsApi.aiInsights).mockResolvedValue({ summary: 'Most tickets are about printers.' });
      renderPage();
      await settled();
      await user.click(screen.getByRole('button', { name: /Generate AI insights/ }));
      expect(await screen.findByText('Most tickets are about printers.')).toBeInTheDocument();

      await user.selectOptions(await screen.findByDisplayValue('All employees'), 'e1');

      await waitFor(() => expect(screen.queryByText('Most tickets are about printers.')).not.toBeInTheDocument());
    });

    it('cannot be asked for twice at once', async () => {
      const user = userEvent.setup();
      const pending = deferred<{ summary: string }>();
      vi.mocked(reportsApi.aiInsights).mockReturnValue(pending.promise);
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: /Generate AI insights/ }));

      expect(screen.getByRole('button', { name: /Analyzing/ })).toBeDisabled();
      await act(async () => pending.resolve({ summary: 'Done.' }));
      expect(await screen.findByText('Done.')).toBeInTheDocument();
    });
  });

  it('downloads the export for the selected period and branch, and cannot be started twice at once', async () => {
    const user = userEvent.setup();
    const pending = deferred<void>();
    vi.mocked(reportsApi.exportXlsx).mockReturnValue(pending.promise);
    renderPage();
    await settled();
    await user.selectOptions(screen.getByDisplayValue('All branches'), 'd2');

    await user.click(screen.getByRole('button', { name: /Export XLSX/ }));

    expect(asked(reportsApi.exportXlsx)).toEqual([[{ from: THIRTY_DAYS_AGO.toISOString(), to: NOW.toISOString(), departmentId: 'd2', agentId: undefined }]]);
    expect(screen.getByRole('button', { name: /Exporting/ })).toBeDisabled();
    await act(async () => pending.resolve());
    expect(await screen.findByRole('button', { name: /Export XLSX/ })).toBeEnabled();
  });

  it('shows five of the top agents and the rest on request', async () => {
    const user = userEvent.setup();
    renderPage();
    await settled();
    const card = (await screen.findByText('Top agents (closed tickets)')).parentElement!;

    expect(within(card).getAllByText(/closed$/)).toHaveLength(5);
    await user.click(within(card).getByRole('button', { name: 'Show more (2 more)' }));
    expect(within(card).getAllByText(/closed$/)).toHaveLength(7);
    await user.click(within(card).getByRole('button', { name: 'Show less' }));
    expect(within(card).getAllByText(/closed$/)).toHaveLength(5);
  });

  describe('the daily timeline', () => {
    it('shows staff their own day at once', async () => {
      useAuthStore.setState({ accessToken: 't', user: staff });

      renderPage();

      expect(await screen.findByText('Own ticket', { selector: '[data-testid="gantt"]' })).toBeInTheDocument();
      expect(asked(workTasksApi.gantt)).toEqual([['e1', '2026-11-05']]);
    });

    it('asks an administrator to choose a person first, and then shows that person\'s day', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();
      expect(screen.getByText('Choose a person above to see their timeline.')).toBeInTheDocument();
      expect(workTasksApi.gantt).not.toHaveBeenCalled();

      await user.selectOptions(screen.getByDisplayValue('Select a person…'), 'e2');

      expect(await screen.findByTestId('gantt')).toHaveTextContent('Own ticket');
      expect(asked(workTasksApi.gantt)).toEqual([['e2', '2026-11-05']]);
    });

    it('does not let staff switch to the whole branch', async () => {
      useAuthStore.setState({ accessToken: 't', user: staff });

      renderPage();
      await screen.findByTestId('gantt');

      expect(screen.queryByRole('button', { name: 'Whole branch' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'One person' })).not.toBeInTheDocument();
    });

    it('says it is loading while the day is on its way', async () => {
      useAuthStore.setState({ accessToken: 't', user: staff });
      vi.mocked(workTasksApi.gantt).mockReturnValue(new Promise<never>(() => {}));

      renderPage();

      expect(await screen.findByText('Loading…')).toBeInTheDocument();
      expect(screen.queryByTestId('gantt')).not.toBeInTheDocument();
    });

    it('does not show a day from memory when the page is left and opened again', async () => {
      useAuthStore.setState({ accessToken: 't', user: staff });
      const client = clientThatKeeps();
      const first = renderPage(client);
      expect(await screen.findByText('Own ticket')).toBeInTheDocument();
      first.unmount();
      await new Promise((r) => setTimeout(r, 10));
      vi.mocked(workTasksApi.gantt).mockReturnValue(new Promise<never>(() => {}));

      renderPage(client);

      expect(await screen.findByText('Loading…')).toBeInTheDocument();
      expect(screen.queryByText('Own ticket')).not.toBeInTheDocument();
    });

    it('shows everybody in a branch when an administrator chooses "Whole branch"', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: 'Whole branch' }));
      expect(screen.getByText("Choose a branch above to see everyone's timeline.")).toBeInTheDocument();
      await user.selectOptions(screen.getByDisplayValue('Select a branch…'), 'd1');

      expect(await screen.findByText('Amra Agent', { selector: 'div' })).toBeInTheDocument();
      expect(screen.getByTestId('gantt')).toHaveTextContent('Branch ticket');
      expect(asked(workTasksApi.ganttBranch)).toEqual([['d1', '2026-11-05']]);
    });

    it('says so when a branch has nobody to show', async () => {
      const user = userEvent.setup();
      vi.mocked(workTasksApi.ganttBranch).mockResolvedValue({ users: [] } as never);
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: 'Whole branch' }));
      await user.selectOptions(screen.getByDisplayValue('Select a branch…'), 'd1');

      expect(await screen.findByText('No active employees in this branch.')).toBeInTheDocument();
    });

    it('says why the timeline could not be loaded, in the server\'s words', async () => {
      const user = userEvent.setup();
      vi.mocked(workTasksApi.gantt).mockRejectedValue({ response: { data: { message: 'No access to this person.' } } });
      renderPage();
      await settled();

      await user.selectOptions(screen.getByDisplayValue('Select a person…'), 'e2');

      expect(await screen.findByText('No access to this person.')).toBeInTheDocument();
    });

    it('says why the timeline of a branch could not be loaded, and asks for the day that is chosen', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();
      await user.click(screen.getByRole('button', { name: 'Whole branch' }));
      await user.selectOptions(screen.getByDisplayValue('Select a branch…'), 'd1');
      await screen.findByTestId('gantt');

      fireEvent.change(document.querySelector('input[type="date"]')!, { target: { value: '2026-10-20' } });
      await waitFor(() => expect(asked(workTasksApi.ganttBranch).at(-1)).toEqual(['d1', '2026-10-20']));

      vi.mocked(workTasksApi.ganttBranch).mockRejectedValue({ response: { data: { message: 'No access to this branch.' } } });
      await user.click(screen.getByRole('button', { name: 'Today' }));

      expect(await screen.findByText('No access to this branch.')).toBeInTheDocument();
    });

    it('asks for another day when the date is changed, and for today with "Today"', async () => {
      const user = userEvent.setup();
      useAuthStore.setState({ accessToken: 't', user: staff });
      renderPage();
      await screen.findByTestId('gantt');

      fireEvent.change(document.querySelector('input[type="date"]')!, { target: { value: '2026-10-20' } });
      await waitFor(() => expect(asked(workTasksApi.gantt).at(-1)).toEqual(['e1', '2026-10-20']));

      await user.click(screen.getByRole('button', { name: 'Today' }));
      await waitFor(() => expect(asked(workTasksApi.gantt).at(-1)).toEqual(['e1', '2026-11-05']));
    });

    it('shows only the answer for the person who is selected now, however late an earlier one arrives', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();
      const first = deferred<never>();
      const second = deferred<never>();
      vi.mocked(workTasksApi.gantt).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);

      await user.selectOptions(screen.getByDisplayValue('Select a person…'), 'e1');
      await user.selectOptions(screen.getByDisplayValue('Amra Agent (Employee)'), 'e2');
      await act(async () => second.resolve({ entries: [{ title: 'Dino ticket' }] } as never));
      await act(async () => first.resolve({ entries: [{ title: 'Amra ticket' }] } as never));

      expect(await screen.findByTestId('gantt')).toHaveTextContent('Dino ticket');
    });
  });

  // A control that shows only its chosen option tells a screen reader nothing about what it is for.
  describe('the names of its controls', () => {
    it('names every list and field of the filter bar, the timeline and the comparison', async () => {
      renderPage();
      await settled();

      expect(screen.getByRole('combobox', { name: 'Branch' })).toHaveDisplayValue('All branches');
      expect(screen.getByRole('combobox', { name: 'Employee' })).toHaveDisplayValue('All employees');
      expect(screen.getByRole('combobox', { name: 'Person' })).toHaveDisplayValue('Select a person…');
      expect(screen.getByLabelText('Day')).toHaveAttribute('type', 'date');
      expect(screen.getByRole('combobox', { name: 'Compare by' })).toHaveDisplayValue('By month');
    });

    it('names the branch list of the timeline too, when the whole branch is shown', async () => {
      const user = userEvent.setup();
      renderPage();
      await settled();

      await user.click(screen.getByRole('button', { name: 'Whole branch' }));

      const lists = screen.getAllByRole('combobox', { name: 'Branch' });
      expect(lists).toHaveLength(2);
      expect(lists[1]).toHaveDisplayValue('Select a branch…');
      expect(screen.queryByRole('combobox', { name: 'Person' })).not.toBeInTheDocument();
    });

    it('leaves no list or field of the page without a name', async () => {
      renderPage();
      await settled();

      const unnamed = screen
        .getAllByRole('combobox')
        .filter((c) => !c.getAttribute('aria-label'))
        .concat(Array.from(document.querySelectorAll<HTMLElement>('input[type="date"]')).filter((c) => !c.getAttribute('aria-label')));

      expect(unnamed).toEqual([]);
    });
  });
});
