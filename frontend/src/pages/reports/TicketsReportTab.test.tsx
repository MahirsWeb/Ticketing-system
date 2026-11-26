import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import ReportsPage from './ReportsPage';
import { ticketInsightsApi } from '../../api/ticketInsights';
import { lookupsApi } from '../../api/lookups';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { AttentionDto, TicketBriefDto, UserSummary } from '../../types';

vi.mock('../../api/ticketInsights', () => ({ ticketInsightsApi: { attention: vi.fn(), resolution: vi.fn(), firstResponse: vi.fn(), transfers: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { departments: vi.fn() } }));
vi.mock('../../components/DateRangePicker', () => ({ DateRangePicker: () => <span>the period</span> }));

const admin = { id: 'a1', firstName: 'Ad', lastName: 'Min', role: 'Admin', phoneNumberPrompted: true } as UserSummary;
const employee = { id: 'e1', firstName: 'Em', lastName: 'Ployee', role: 'Employee', departmentId: 'd1', phoneNumberPrompted: true } as UserSummary;

const brief = (id: string, number: string, title: string, over: Partial<TicketBriefDto> = {}): TicketBriefDto => ({
  id, ticketNumber: number, title, status: 'Open', createdAt: '2026-10-01T08:00:00Z', dueDateUtc: null, lastActivityUtc: null, ageDays: 30, ...over,
});

const attention: AttentionDto = {
  asOfUtc: '2026-11-20T12:00:00Z',
  activeTotal: 8,
  ages: [
    { key: 'upTo2Days', count: 1, oldest: brief('t1', 'TKT-1', 'Mlad', { ageDays: 1 }) },
    { key: 'upTo7Days', count: 2, oldest: brief('t2', 'TKT-2', 'Sedmica', { ageDays: 6 }) },
    { key: 'upTo30Days', count: 3, oldest: brief('t3', 'TKT-3', 'Mjesec', { ageDays: 25 }) },
    { key: 'older', count: 2, oldest: brief('t4', 'TKT-4', 'Davno', { ageDays: 90 }) },
  ],
  inactiveDays: 3,
  inactive: { total: 30, items: [brief('t5', 'TKT-5', 'Stoji', { lastActivityUtc: '2026-11-10T12:00:00Z' }), brief('t6', 'TKT-6', 'Stoji i ovaj', { lastActivityUtc: '2026-11-15T12:00:00Z' })] },
  unassigned: { total: 1, items: [brief('t7', 'TKT-7', 'Niko ga nema', { status: 'New', ageDays: 2 })] },
  dueSoon: { total: 0, items: [] },
  overdue: { total: 1, items: [brief('t8', 'TKT-8', 'Kasni', { dueDateUtc: '2026-11-18T12:00:00Z' })] },
};

beforeEach(() => {
  vi.resetAllMocks();
  useAuthStore.setState({ accessToken: 't', user: admin });
  vi.mocked(lookupsApi.departments).mockResolvedValue([{ id: 'd1', name: 'IT', isActive: true }, { id: 'd2', name: 'Prodaja', isActive: true }] as never);
  vi.mocked(ticketInsightsApi.attention).mockResolvedValue(attention);
  vi.mocked(ticketInsightsApi.resolution).mockResolvedValue({ count: 5, averageHours: 20.8, medianHours: 1, p90Hours: 60.4 });
  vi.mocked(ticketInsightsApi.firstResponse).mockResolvedValue({ tickets: 10, answered: 8, unanswered: 2, averageHours: 3.2, medianHours: 2.5, p90Hours: 7.1 });
  vi.mocked(ticketInsightsApi.transfers).mockResolvedValue({
    total: 3,
    edges: [
      { fromId: 'd1', fromName: 'IT', toId: 'd2', toName: 'Prodaja', count: 2 },
      { fromId: null, fromName: null, toId: 'd2', toName: 'Prodaja', count: 1 },
    ],
    items: [{ ticketId: 't9', ticketNumber: 'TKT-9', title: 'Prebačen', atUtc: '2026-11-12T09:30:00Z', fromId: 'd1', fromName: 'IT', toId: 'd2', toName: 'Prodaja' }],
  });
});

function renderPage() {
  return renderWithQuery(
    <MemoryRouter>
      <ReportsPage />
    </MemoryRouter>
  );
}

describe('the reports page: the Tickets tab', () => {
  it('shows what needs attention now, with a link to each ticket', async () => {
    renderPage();

    expect(await screen.findByText('Active tickets by age')).toBeInTheDocument();
    expect(screen.getByText('Active now: 8')).toBeInTheDocument();
    const oldest = screen.getByRole('link', { name: /TKT-4 Davno/ });
    expect(oldest).toHaveAttribute('href', '/tickets/t4');
    expect(screen.getByText('Oldest, 90 days:')).toBeInTheDocument();

    const inactive = screen.getByRole('heading', { name: 'Without activity' }).closest('div')!.parentElement!;
    expect(within(inactive).getByText('30')).toBeInTheDocument();
    expect(within(inactive).getByRole('link', { name: /TKT-5 Stoji/ })).toHaveAttribute('href', '/tickets/t5');
    expect(within(inactive).getByText('idle 10 days')).toBeInTheDocument();
    expect(within(inactive).getByText('Showing 2 of 30')).toBeInTheDocument(); // the list is shorter than the count and says so

    expect(screen.getByRole('link', { name: /TKT-7 Niko ga nema/ })).toBeInTheDocument();
    expect(screen.getByText('No ticket is due in the next 24 hours.')).toBeInTheDocument(); // an empty list says so
    expect(screen.getByRole('link', { name: /TKT-8 Kasni/ })).toBeInTheDocument();
  });

  it('shows the average beside the median and the 90th percentile, and says how many tickets it is from', async () => {
    renderPage();

    expect(await screen.findByText('20.8 h')).toBeInTheDocument();
    expect(screen.getByText('1 h')).toBeInTheDocument();
    expect(screen.getByText('60.4 h')).toBeInTheDocument();
    expect(screen.getByText('From 5 tickets')).toBeInTheDocument();
    expect(screen.getByText(/found by linear interpolation/)).toBeInTheDocument(); // the definition is on the screen
  });

  it('counts the tickets nobody has answered apart from the hours', async () => {
    renderPage();

    expect(await screen.findByText('8 of 10 tickets answered; 2 without a reply')).toBeInTheDocument();
    expect(screen.getByText('2.5 h')).toBeInTheDocument();
  });

  it('shows who has no reply at all as a sentence and not as zero hours', async () => {
    vi.mocked(ticketInsightsApi.firstResponse).mockResolvedValue({ tickets: 4, answered: 0, unanswered: 4, averageHours: 0, medianHours: 0, p90Hours: 0 });
    renderPage();

    expect(await screen.findByText('None of these tickets has had a reply yet.')).toBeInTheDocument();
  });

  it('shows the transfers from a branch to a branch, with an unrouted ticket as such', async () => {
    renderPage();

    expect(await screen.findByText('IT → Prodaja')).toBeInTheDocument();
    expect(screen.getByText('2 times')).toBeInTheDocument();
    expect(screen.getByText('(unrouted) → Prodaja')).toBeInTheDocument();
    expect(screen.getByText('Transfers: 3')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /TKT-9 Prebačen/ })).toHaveAttribute('href', '/tickets/t9');
  });

  it('says so when there is nothing in a period', async () => {
    vi.mocked(ticketInsightsApi.resolution).mockResolvedValue({ count: 0, averageHours: 0, medianHours: 0, p90Hours: 0 });
    vi.mocked(ticketInsightsApi.transfers).mockResolvedValue({ total: 0, edges: [], items: [] });
    vi.mocked(ticketInsightsApi.attention).mockResolvedValue({ ...attention, activeTotal: 0, ages: [], inactive: { total: 0, items: [] }, unassigned: { total: 0, items: [] }, overdue: { total: 0, items: [] } });
    renderPage();

    expect(await screen.findByText('No ticket was closed in this period.')).toBeInTheDocument();
    expect(screen.getByText('No ticket was transferred in this period.')).toBeInTheDocument();
    expect(screen.getByText('There are no active tickets.')).toBeInTheDocument();
    expect(screen.getByText('No ticket has stood still that long.')).toBeInTheDocument();
  });

  it('asks again with the days chosen, and the note says which days', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Active tickets by age');
    expect(vi.mocked(ticketInsightsApi.attention)).toHaveBeenLastCalledWith({ departmentId: undefined, inactiveDays: 3 });

    await user.selectOptions(screen.getByLabelText('Days without activity'), '7');

    await vi.waitFor(() => expect(vi.mocked(ticketInsightsApi.attention)).toHaveBeenLastCalledWith({ departmentId: undefined, inactiveDays: 7 }));
  });

  it('lets Admin narrow every figure to one branch', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Active tickets by age');

    await user.selectOptions(await screen.findByRole('combobox', { name: 'Branch' }), 'd2');

    await vi.waitFor(() => {
      expect(vi.mocked(ticketInsightsApi.attention)).toHaveBeenLastCalledWith({ departmentId: 'd2', inactiveDays: 3 });
      expect(vi.mocked(ticketInsightsApi.resolution)).toHaveBeenLastCalledWith(expect.objectContaining({ departmentId: 'd2' }));
      expect(vi.mocked(ticketInsightsApi.firstResponse)).toHaveBeenLastCalledWith(expect.objectContaining({ departmentId: 'd2' }));
      expect(vi.mocked(ticketInsightsApi.transfers)).toHaveBeenLastCalledWith(expect.objectContaining({ departmentId: 'd2' }));
    });
  });

  it('gives staff no branch choice, only their own, and never asks for another', async () => {
    useAuthStore.setState({ accessToken: 't', user: employee });
    renderPage();

    expect(await screen.findByText('The figures of your own branch.')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Branch' })).not.toBeInTheDocument();
    expect(vi.mocked(lookupsApi.departments)).not.toHaveBeenCalled();
    expect(vi.mocked(ticketInsightsApi.attention)).toHaveBeenCalledWith({ departmentId: undefined, inactiveDays: 3 });
  });

  it('says that a part of the figures could not be loaded instead of showing a half page as a whole one', async () => {
    vi.mocked(ticketInsightsApi.transfers).mockRejectedValue(new Error('boom'));
    renderPage();

    expect(await screen.findByText(/Some of the figures could not be loaded/)).toBeInTheDocument();
    expect(screen.getByText('Active tickets by age')).toBeInTheDocument(); // the rest is still there
  });
});
