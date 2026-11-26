import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import ReportsPage from './ReportsPage';
import { employeeReportsApi } from '../../api/employeeReports';
import { ticketInsightsApi } from '../../api/ticketInsights';
import { lookupsApi } from '../../api/lookups';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { UserSummary } from '../../types';

vi.mock('../../api/employeeReports', () => ({ employeeReportsApi: { workload: vi.fn(), hours: vi.fn(), hoursOf: vi.fn(), specialization: vi.fn(), dependency: vi.fn(), timesheet: vi.fn() } }));
vi.mock('../../api/ticketInsights', () => ({ ticketInsightsApi: { attention: vi.fn(), resolution: vi.fn(), firstResponse: vi.fn(), transfers: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { departments: vi.fn() } }));
vi.mock('../../components/DateRangePicker', () => ({ DateRangePicker: () => <span>the period</span> }));

const admin = { id: 'a1', firstName: 'Ad', lastName: 'Min', role: 'Admin', phoneNumberPrompted: true } as UserSummary;
const employee = { id: 'e1', firstName: 'Amra', lastName: 'Hadžić', role: 'Employee', departmentId: 'd1', phoneNumberPrompted: true } as UserSummary;

const detail = {
  userId: 'e1',
  name: 'Amra Hadžić',
  ticketHours: 5.5,
  taskHours: 1,
  totalHours: 6.5,
  days: [
    { day: '2026-11-02', ticketHours: 3, taskHours: 1, totalHours: 4 },
    { day: '2026-11-03', ticketHours: 2.5, taskHours: 0, totalHours: 2.5 },
    { day: '2026-11-04', ticketHours: 0, taskHours: 0, totalHours: 0 },
  ],
  weeks: [{ weekStart: '2026-11-02', ticketHours: 5.5, taskHours: 1, totalHours: 6.5 }],
  daysWithoutEntry: ['2026-11-04', '2026-11-05'],
  daysWithoutEntryApply: true,
};

beforeEach(() => {
  vi.resetAllMocks();
  useAuthStore.setState({ accessToken: 't', user: admin });
  vi.mocked(lookupsApi.departments).mockResolvedValue([{ id: 'd1', name: 'IT', isActive: true }, { id: 'd2', name: 'Prodaja', isActive: true }] as never);
  vi.mocked(employeeReportsApi.workload).mockResolvedValue({
    asOfUtc: '2026-11-20T12:00:00Z',
    people: [
      { userId: 'e1', name: 'Amra Hadžić', branch: 'IT', openTickets: 7, mainTickets: 4, overdueTickets: 2, openTasks: 3 },
      { userId: 'e2', name: 'Emir Kovač', branch: 'Prodaja', openTickets: 0, mainTickets: 0, overdueTickets: 0, openTasks: 0 },
    ],
  });
  vi.mocked(employeeReportsApi.hours).mockResolvedValue({
    people: [
      { userId: 'e1', name: 'Amra Hadžić', branch: 'IT', ticketHours: 5.5, taskHours: 1, totalHours: 6.5, daysWithoutEntry: 2 },
      { userId: 'e2', name: 'Emir Kovač', branch: 'Prodaja', ticketHours: 0, taskHours: 0, totalHours: 0, daysWithoutEntry: null },
    ],
  });
  vi.mocked(employeeReportsApi.hoursOf).mockResolvedValue(detail);
  vi.mocked(employeeReportsApi.specialization).mockResolvedValue({
    userId: 'e1',
    name: 'Amra Hadžić',
    closedTotal: 9,
    topics: [{ id: 't1', name: 'Printer', closed: 5, hours: 3.5 }, { id: 't2', name: 'Mreža', closed: 2, hours: 1 }],
    categories: [],
    companies: [{ id: 'c1', name: 'Alfa', closed: 6, hours: 4 }],
  });
  vi.mocked(employeeReportsApi.dependency).mockResolvedValue({
    total: 3,
    minClosed: 5,
    minSharePercent: 50,
    items: [
      { companyId: 'c1', company: 'Alfa', closed: 6, personId: 'e1', person: 'Amra Hadžić', personClosed: 5, sharePercent: 83.3, high: true },
      { companyId: 'c2', company: 'Beta', closed: 8, personId: 'e2', person: 'Emir Kovač', personClosed: 4, sharePercent: 50, high: false },
    ],
  });
  vi.mocked(employeeReportsApi.timesheet).mockResolvedValue(undefined);
});

function renderPage(path = '/reports?tab=employees') {
  return renderWithQuery(
    <MemoryRouter initialEntries={[path]}>
      <ReportsPage />
    </MemoryRouter>
  );
}


// The people to choose from arrive with the load: wait for them before choosing.
async function pick(user: ReturnType<typeof userEvent.setup>, id: string) {
  const select = await screen.findByRole('combobox', { name: 'Person' });
  await waitFor(() => expect(within(select).getAllByRole('option').length).toBeGreaterThan(1));
  await user.selectOptions(select, id);
}

describe('the reports page: the Employees tab', () => {
  it('is opened by the address and by its tab', async () => {
    vi.mocked(ticketInsightsApi.attention).mockResolvedValue({ asOfUtc: '2026-11-20T12:00:00Z', activeTotal: 0, ages: [], inactiveDays: 3, inactive: { total: 0, items: [] }, unassigned: { total: 0, items: [] }, dueSoon: { total: 0, items: [] }, overdue: { total: 0, items: [] } });
    vi.mocked(ticketInsightsApi.resolution).mockResolvedValue({ count: 0, averageHours: 0, medianHours: 0, p90Hours: 0 });
    vi.mocked(ticketInsightsApi.firstResponse).mockResolvedValue({ tickets: 0, answered: 0, unanswered: 0, averageHours: 0, medianHours: 0, p90Hours: 0 });
    vi.mocked(ticketInsightsApi.transfers).mockResolvedValue({ total: 0, edges: [], items: [] });
    const user = userEvent.setup();
    renderPage('/reports');
    expect(await screen.findByRole('heading', { name: 'Ticket reports' })).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Employees' }));

    expect(await screen.findByRole('heading', { name: 'Employee reports' })).toBeInTheDocument();
  });

  it('shows the load of each person with the overdue ones marked, and says that tasks have no due date', async () => {
    renderPage();

    const load = (await screen.findByText('Open tickets and tasks')).closest('div')!.parentElement!;
    const row = within(load).getByRole('row', { name: /Amra Hadžić/ });
    expect(within(row).getAllByRole('cell').map((c) => c.textContent)).toEqual(['Amra HadžićIT', '7', '4', '2', '3']);
    expect(within(load).getByText(/Tasks have no due date, so "overdue" is only about tickets/)).toBeInTheDocument();
  });

  it('shows the hours of everybody and what is not counted, with no person chosen yet', async () => {
    renderPage();

    const hours = (await screen.findByText('Recorded hours')).closest('div')!.parentElement!;
    const row = within(hours).getByRole('row', { name: /Amra Hadžić/ });
    expect(within(row).getAllByRole('cell').map((c) => c.textContent)).toEqual(['Amra Hadžić', '5.5', '1', '6.5', '2']);
    expect(within(within(hours).getByRole('row', { name: /Emir Kovač/ })).getAllByRole('cell')[4]).toHaveTextContent('—'); // days without entry are not counted for him
    expect(within(hours).getByText(/there is no calendar of holidays/)).toBeInTheDocument();
    expect(screen.getByText(/Choose a person to see the details/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download/ })).toBeDisabled();
    expect(vi.mocked(employeeReportsApi.hoursOf)).not.toHaveBeenCalled(); // nobody is asked for before one is chosen
    expect(vi.mocked(employeeReportsApi.specialization)).not.toHaveBeenCalled();
  });

  it('opens the details of a person from the table: hours by week and day, the days without entry, the top three and the worksheet', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Amra Hadžić' }));

    expect(await screen.findByText('Hours of Amra Hadžić')).toBeInTheDocument();
    expect(vi.mocked(employeeReportsApi.hoursOf)).toHaveBeenCalledWith('e1', expect.objectContaining({ from: expect.any(String) }));
    expect(screen.getByText('6.5 h in all: 5.5 h on tickets, 1 h on tasks.')).toBeInTheDocument();
    expect(screen.getByText('Week of 02.11.2026')).toBeInTheDocument();
    expect(screen.getByText('03.11.2026')).toBeInTheDocument();
    expect(screen.queryByText('04.11.2026', { selector: 'td' })).not.toBeInTheDocument(); // a day with no hours is not in the list of days
    expect(screen.getByText('Working days with nothing recorded: 2')).toBeInTheDocument();
    expect(screen.getByText('04.11.2026, 05.11.2026')).toBeInTheDocument();

    expect(await screen.findByText('Specialization of Amra Hadžić')).toBeInTheDocument();
    expect(screen.getByText('1. Printer')).toBeInTheDocument();
    expect(screen.getByText('5 closed · 3.5 h')).toBeInTheDocument();
    expect(screen.getByText('Tickets closed in the period: 9')).toBeInTheDocument();
    expect(screen.getAllByText('Nothing in this period.')).toHaveLength(1); // the categories

    await user.click(screen.getByRole('button', { name: /Download/ }));
    await waitFor(() => expect(vi.mocked(employeeReportsApi.timesheet)).toHaveBeenCalledWith({ userId: 'e1', year: new Date().getFullYear(), month: new Date().getMonth() + 1 }));
  });

  it('downloads the month that was chosen', async () => {
    const user = userEvent.setup();
    renderPage();
    await pick(user, 'e2');

    const month = screen.getByLabelText('Month') as HTMLInputElement;
    await user.clear(month);
    await user.type(month, '2026-03');
    await user.click(screen.getByRole('button', { name: /Download/ }));

    await waitFor(() => expect(vi.mocked(employeeReportsApi.timesheet)).toHaveBeenCalledWith({ userId: 'e2', year: 2026, month: 3 }));
  });

  it('says so when the worksheet could not be made', async () => {
    vi.mocked(employeeReportsApi.timesheet).mockRejectedValue(new Error('boom'));
    const user = userEvent.setup();
    renderPage();
    await pick(user, 'e1');

    await user.click(screen.getByRole('button', { name: /Download/ }));

    expect(await screen.findByText('The work sheet could not be made. Try again.')).toBeInTheDocument();
  });

  it('shows Admin the companies that depend on one person with the share and the high mark, and the rule in the note', async () => {
    renderPage();

    const card = (await screen.findByText('Companies that depend on one person')).closest('div')!.parentElement!;
    expect(within(card).getByText(/at least 5 tickets closed in the last 12 months where one person closed 50 % or more/)).toBeInTheDocument();
    const alfa = within(card).getByText('Alfa').closest('li')!;
    expect(within(alfa).getByText('Amra Hadžić: 5 of 6 tickets')).toBeInTheDocument();
    expect(within(alfa).getByText('83.3 %')).toBeInTheDocument();
    expect(within(alfa).getByText('high')).toBeInTheDocument();
    expect(within(within(card).getByText('Beta').closest('li')!).queryByText('high')).not.toBeInTheDocument();
  });

  it('narrows the load and the hours to a branch and forgets the person chosen', async () => {
    const user = userEvent.setup();
    renderPage();
    await pick(user, 'e1');
    await screen.findByText('Hours of Amra Hadžić');

    await user.selectOptions(screen.getByRole('combobox', { name: 'Branch' }), 'd2');

    await waitFor(() => {
      expect(vi.mocked(employeeReportsApi.workload)).toHaveBeenLastCalledWith({ departmentId: 'd2' });
      expect(vi.mocked(employeeReportsApi.hours)).toHaveBeenLastCalledWith(expect.objectContaining({ departmentId: 'd2' }));
    });
    expect(screen.queryByText('Hours of Amra Hadžić')).not.toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Person' })).toHaveValue('');
  });

  it('gives an employee only themself: no branch or person to choose, their details at once, and no question about the dependence', async () => {
    useAuthStore.setState({ accessToken: 't', user: employee });
    renderPage();

    expect(await screen.findByText('Your own figures.')).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Person' })).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Branch' })).not.toBeInTheDocument();
    expect(await screen.findByText('Hours of Amra Hadžić')).toBeInTheDocument();
    expect(vi.mocked(employeeReportsApi.hoursOf)).toHaveBeenCalledWith('e1', expect.anything());
    expect(vi.mocked(employeeReportsApi.specialization)).toHaveBeenCalledWith(expect.objectContaining({ userId: 'e1' }));
    expect(screen.queryByText('Companies that depend on one person')).not.toBeInTheDocument();
    expect(vi.mocked(employeeReportsApi.dependency)).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Emir Kovač' })).not.toBeInTheDocument(); // the names in the table are not links for an employee
    expect(vi.mocked(lookupsApi.departments)).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /Download/ })).toBeEnabled();
  });

  it('says that days without entry do not apply to somebody who is not an active employee', async () => {
    vi.mocked(employeeReportsApi.hoursOf).mockResolvedValue({ ...detail, daysWithoutEntry: [], daysWithoutEntryApply: false });
    const user = userEvent.setup();
    renderPage();

    await pick(user, 'e1');

    expect(await screen.findByText('Days without entry are counted only for active employees.')).toBeInTheDocument();
  });

  it('says so when nothing was recorded, and when nobody depends on one person', async () => {
    vi.mocked(employeeReportsApi.hoursOf).mockResolvedValue({ ...detail, days: [], weeks: [], ticketHours: 0, taskHours: 0, totalHours: 0, daysWithoutEntry: [] });
    vi.mocked(employeeReportsApi.dependency).mockResolvedValue({ total: 0, minClosed: 5, minSharePercent: 50, items: [] });
    const user = userEvent.setup();
    renderPage();

    await pick(user, 'e1');

    expect(await screen.findByText('No hours were recorded in this period.')).toBeInTheDocument();
    expect(screen.getByText('There is no working day without an entry.')).toBeInTheDocument();
    expect(await screen.findByText('No company depends on one person.')).toBeInTheDocument();
  });

  it('says that a part of the figures could not be loaded and shows the rest', async () => {
    vi.mocked(employeeReportsApi.dependency).mockRejectedValue(new Error('boom'));
    renderPage();

    expect(await screen.findByText(/Some of the figures could not be loaded/)).toBeInTheDocument();
    expect(screen.getByText('Open tickets and tasks')).toBeInTheDocument();
  });
});
