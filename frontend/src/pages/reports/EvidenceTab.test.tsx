import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { format, subMonths } from 'date-fns';
import ReportsPage from './ReportsPage';
import { documentsApi } from '../../api/documents';
import { employeeReportsApi } from '../../api/employeeReports';
import { lookupsApi } from '../../api/lookups';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { CompanyMonthlyDto, UserSummary } from '../../types';

vi.mock('../../api/documents', () => ({ documentsApi: { ticketCard: vi.fn(), ticketCardXlsx: vi.fn(), monthly: vi.fn(), monthlyXlsx: vi.fn() } }));
vi.mock('../../api/employeeReports', () => ({ employeeReportsApi: { workload: vi.fn(), timesheet: vi.fn() } }));
vi.mock('../../api/lookups', () => ({ lookupsApi: { companies: vi.fn() } }));

const admin = { id: 'a1', firstName: 'Ad', lastName: 'Min', role: 'Admin', phoneNumberPrompted: true } as UserSummary;
const employee = { id: 'e1', firstName: 'Amra', lastName: 'Hadžić', role: 'Employee', departmentId: 'd1', phoneNumberPrompted: true } as UserSummary;

const report: CompanyMonthlyDto = {
  header: { companyId: 'c1', company: 'Alfa d.o.o.', year: 2026, month: 3, audience: 'internal', includesHours: true, madeAtUtc: '2026-04-02T09:00:00Z', madeBy: 'Ad Min', listScope: 'all' },
  created: 7,
  closed: 3,
  byStatus: [{ status: 'New', count: 1 }, { status: 'Open', count: 4 }, { status: 'InProgress', count: 0 }, { status: 'Resolved', count: 0 }, { status: 'Closed', count: 2 }],
  byTopic: [{ id: 't1', name: 'Mreža', count: 4 }, { id: null, name: '', count: 3 }],
  byCategory: [{ id: 'g1', name: 'Hardver', count: 2 }, { id: null, name: '', count: 5 }],
  resolutionCount: 3,
  averageResolutionHours: 4.3,
  medianResolutionHours: 2,
  slaComplianceRate: 50,
  slaCounted: 2,
  workHours: 2,
  tickets: [
    { id: 'x1', ticketNumber: 'TKT-1', title: 'Prvi', status: 'Closed', createdAt: '2026-03-10T08:00:00Z', closedAtUtc: '2026-03-10T09:00:00Z', resolutionHours: 1 },
    { id: 'x2', ticketNumber: 'TKT-2', title: 'Drugi', status: 'Open', createdAt: '2026-03-11T08:00:00Z', closedAtUtc: null, resolutionHours: null },
  ],
  ticketsTotal: 2,
  ticketsTruncated: false,
};

beforeEach(() => {
  vi.resetAllMocks();
  useAuthStore.setState({ accessToken: 't', user: admin });
  vi.mocked(lookupsApi.companies).mockResolvedValue([{ id: 'c1', name: 'Alfa d.o.o.', isActive: true }, { id: 'c2', name: 'Beta', isActive: true }] as never);
  vi.mocked(documentsApi.monthly).mockResolvedValue(report);
  vi.mocked(documentsApi.monthlyXlsx).mockResolvedValue(undefined);
  vi.mocked(employeeReportsApi.workload).mockResolvedValue({ asOfUtc: '2026-11-20T12:00:00Z', people: [{ userId: 'e1', name: 'Amra Hadžić', branch: 'IT', openTickets: 0, mainTickets: 0, overdueTickets: 0, openTasks: 0 }] });
  vi.mocked(employeeReportsApi.timesheet).mockResolvedValue(undefined);
  window.print = vi.fn();
});

function renderPage() {
  return renderWithQuery(
    <MemoryRouter initialEntries={['/reports?tab=evidence']}>
      <ReportsPage />
    </MemoryRouter>
  );
}

async function chooseCompany(user: ReturnType<typeof userEvent.setup>, name = 'Alfa d.o.o.') {
  await user.click(await screen.findByRole('combobox', { name: 'Company' }));
  await user.click(await screen.findByRole('option', { name }));
}

// The form of the monthly report: the work sheet below it has a month and a download button of its own.
const form = () => screen.getByRole('heading', { name: 'Monthly report of the company' }).closest('div')!.parentElement as HTMLElement;

const previousMonth = () => format(subMonths(new Date(), 1), 'yyyy-MM');

describe('the reports page: the Records tab', () => {
  it('needs a company before a report can be made, and starts from the month before this one', async () => {
    renderPage();

    expect(await screen.findByRole('button', { name: 'Show the report' })).toBeDisabled();
    expect(within(form()).getByRole('button', { name: /Download/ })).toBeDisabled();
    expect(screen.getByText('Choose a company and a month.')).toBeInTheDocument();
    expect((within(form()).getByLabelText('Month') as HTMLInputElement).value).toBe(previousMonth());
    expect(vi.mocked(documentsApi.monthly)).not.toHaveBeenCalled(); // nothing is made before it is asked for
  });

  it('makes the internal report for the company and month chosen, with its figures, distributions and list', async () => {
    const user = userEvent.setup();
    renderPage();
    await chooseCompany(user);
    const month = within(form()).getByLabelText('Month') as HTMLInputElement;
    await user.clear(month);
    await user.type(month, '2026-03');

    await user.click(screen.getByRole('button', { name: 'Show the report' }));

    await waitFor(() => expect(vi.mocked(documentsApi.monthly)).toHaveBeenCalledWith({ companyId: 'c1', year: 2026, month: 3, audience: 'internal', includeHours: undefined }));
    const doc = await screen.findByRole('article', { name: 'Monthly report of the company' });
    expect(within(doc).getByRole('heading', { name: 'Alfa d.o.o.' })).toBeInTheDocument();
    expect(within(doc).getByText('Month: 03.2026 · Internal (archive)')).toBeInTheDocument();
    expect(within(doc).getByText(/Made by Ad Min/)).toBeInTheDocument();
    expect(within(doc).getByText('Created in the month').nextSibling).toHaveTextContent('7');
    expect(within(doc).getByText('Closed in the month').nextSibling).toHaveTextContent('3');
    expect(within(doc).getByText('Average').nextSibling).toHaveTextContent('4.3 h');
    expect(within(doc).getByText('Median').nextSibling).toHaveTextContent('2 h');
    expect(within(doc).getByText('Closed within the due date').nextSibling).toHaveTextContent('50 %');
    expect(within(doc).getByText('Recorded work').nextSibling).toHaveTextContent('2 h');
    expect(within(doc).getByText('By internal category')).toBeInTheDocument();
    expect(within(doc).getAllByText('(none)')).toHaveLength(2); // no topic yet, no category
    expect(within(doc).getByRole('link', { name: 'TKT-1' })).toHaveAttribute('href', '/tickets/x1');
    expect(within(doc).getByText('The list holds the tickets of all branches.')).toBeInTheDocument();
    expect(within(doc).getByText(/Status, topic and category are of the tickets created in the month/)).toBeInTheDocument(); // the definition is on the screen
  });

  it('offers the hours only for the client version and asks for them when ticked; the client document has no category, no links and no name of staff', async () => {
    const user = userEvent.setup();
    vi.mocked(documentsApi.monthly).mockResolvedValue({
      ...report,
      header: { ...report.header, audience: 'client', includesHours: false, madeBy: 'Tix' },
      byCategory: null,
      workHours: null,
    });
    renderPage();
    await chooseCompany(user);
    expect(screen.queryByLabelText('Include the hours of work')).not.toBeInTheDocument(); // the internal version always has them

    await user.click(screen.getByRole('radio', { name: 'For the client' }));
    await user.click(screen.getByRole('button', { name: 'Show the report' }));

    await waitFor(() => expect(vi.mocked(documentsApi.monthly)).toHaveBeenLastCalledWith(expect.objectContaining({ audience: 'client', includeHours: false })));
    const doc = await screen.findByRole('article', { name: 'Monthly report of the company' });
    expect(within(doc).getByText('This version has no internal categories, internal notes or names of staff.')).toBeInTheDocument();
    expect(within(doc).queryByText('By internal category')).not.toBeInTheDocument();
    expect(within(doc).queryByText('Recorded work')).not.toBeInTheDocument();
    expect(within(doc).queryByRole('link', { name: 'TKT-1' })).not.toBeInTheDocument();
    expect(within(doc).getByText(/Made by Tix/)).toBeInTheDocument();

    await user.click(screen.getByLabelText('Include the hours of work'));
    await user.click(screen.getByRole('button', { name: 'Show the report' }));
    await waitFor(() => expect(vi.mocked(documentsApi.monthly)).toHaveBeenLastCalledWith(expect.objectContaining({ audience: 'client', includeHours: true })));
  });

  it('says that an employee gets only the tickets of their own branch in the list', async () => {
    vi.mocked(documentsApi.monthly).mockResolvedValue({ ...report, header: { ...report.header, listScope: 'IT' } });
    const user = userEvent.setup();
    renderPage();
    await chooseCompany(user);

    await user.click(screen.getByRole('button', { name: 'Show the report' }));

    expect(await screen.findByText('The list holds only the tickets of the branch IT; the figures are for all branches.')).toBeInTheDocument();
  });

  it('says that the list is cut when there are more tickets than are shown', async () => {
    vi.mocked(documentsApi.monthly).mockResolvedValue({ ...report, ticketsTotal: 1500, ticketsTruncated: true });
    const user = userEvent.setup();
    renderPage();
    await chooseCompany(user);

    await user.click(screen.getByRole('button', { name: 'Show the report' }));

    expect(await screen.findByRole('note')).toHaveTextContent('The list is cut: the screen shows the first 2 of 1500 tickets. The xlsx has all of them.');
  });

  it('downloads the same choice as an xlsx and prints the document; the form is not in the print', async () => {
    const user = userEvent.setup();
    renderPage();
    await chooseCompany(user, 'Beta');
    await user.click(screen.getByRole('radio', { name: 'For the client' }));
    await user.click(screen.getByLabelText('Include the hours of work'));

    await user.click(within(form()).getByRole('button', { name: /Download/ }));

    await waitFor(() => {
      const [year, month] = previousMonth().split('-').map(Number);
      expect(vi.mocked(documentsApi.monthlyXlsx)).toHaveBeenCalledWith({ companyId: 'c2', year, month, audience: 'client', includeHours: true });
    });
    await user.click(screen.getByRole('button', { name: 'Show the report' }));
    await screen.findByRole('article', { name: 'Monthly report of the company' });
    await user.click(screen.getByRole('button', { name: /Print/ }));
    expect(window.print).toHaveBeenCalled();
    expect(screen.getByRole('radio', { name: 'For the client' }).closest('.print\\:hidden')).not.toBeNull();
  });

  it('says so when the report or the download cannot be made', async () => {
    vi.mocked(documentsApi.monthly).mockRejectedValue(new Error('boom'));
    vi.mocked(documentsApi.monthlyXlsx).mockRejectedValue(new Error('boom'));
    const user = userEvent.setup();
    renderPage();
    await chooseCompany(user);

    await user.click(within(form()).getByRole('button', { name: /Download/ }));
    expect(await screen.findByText('The work sheet could not be made. Try again.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Show the report' }));
    expect(await screen.findByText('The report could not be made. Try again.')).toBeInTheDocument();
  });

  it('gives Admin the work sheet of any person, and the month to choose', async () => {
    const user = userEvent.setup();
    renderPage();
    const sheet = (await screen.findByText('Monthly work sheet')).closest('div')!.parentElement!;
    expect(within(sheet).getByRole('button', { name: /Download/ })).toBeDisabled(); // nobody is chosen

    const select = await screen.findByRole('combobox', { name: 'Person' });
    await waitFor(() => expect(within(select).getAllByRole('option').length).toBeGreaterThan(1));
    await user.selectOptions(select, 'e1');
    const month = within(sheet).getByLabelText('Month') as HTMLInputElement;
    await user.clear(month);
    await user.type(month, '2026-03');
    await user.click(within(sheet).getByRole('button', { name: /Download/ }));

    await waitFor(() => expect(vi.mocked(employeeReportsApi.timesheet)).toHaveBeenCalledWith({ userId: 'e1', year: 2026, month: 3 }));
  });

  it('gives an employee only their own work sheet, with no person to choose', async () => {
    useAuthStore.setState({ accessToken: 't', user: employee });
    const user = userEvent.setup();
    renderPage();

    const sheet = (await screen.findByText('Monthly work sheet')).closest('div')!.parentElement!;
    expect(screen.queryByRole('combobox', { name: 'Person' })).not.toBeInTheDocument();
    await user.click(within(sheet).getByRole('button', { name: /Download/ }));

    await waitFor(() => expect(vi.mocked(employeeReportsApi.timesheet)).toHaveBeenCalledWith(expect.objectContaining({ userId: 'e1' })));
  });
});
