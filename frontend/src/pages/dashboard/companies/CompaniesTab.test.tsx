import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { CompaniesTab } from './CompaniesTab';
import { companyReportsApi } from '../../../api/companyReports';
import { lookupsApi } from '../../../api/lookups';
import { usersApi } from '../../../api/users';
import { renderWithQuery } from '../../../test/renderWithQuery';

vi.mock('../../../api/companyReports', () => ({
  companyReportsApi: { top: vi.fn(), list: vi.fn(), summary: vi.fn(), clients: vi.fn(), topics: vi.fn(), categories: vi.fn(), branches: vi.fn(), card: vi.fn(), branchLoad: vi.fn(), closedBy: vi.fn(), timeSeries: vi.fn(), openAge: vi.fn(), timeSpent: vi.fn(), conversation: vi.fn(), rhythm: vi.fn(), repeatedTopics: vi.fn(), quiet: vi.fn(), exportXlsx: vi.fn() },
}));
vi.mock('../../../api/lookups', () => ({ lookupsApi: { companies: vi.fn() } }));
vi.mock('../../../api/users', () => ({ usersApi: { companyClientChoices: vi.fn() } }));
vi.mock('../../../components/DateRangePicker', () => ({ DateRangePicker: () => <span>the period</span> }));

const row = (id: string, name: string, total: number, previousTotal: number, share: number) => ({
  companyId: id, name, total, new: 1, open: 2, closed: 5, avgResolutionHours: 12.5, slaComplianceRate: 80, previousTotal, sharePercent: share,
});
const companySummary = { total: 8, new: 1, open: 2, closed: 5, avgResolutionHours: 12.5, slaComplianceRate: 80, previousTotal: 4 };
const clientSummary = { total: 2, new: 0, open: 1, closed: 1, avgResolutionHours: 20, slaComplianceRate: null, previousTotal: 0 };

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(lookupsApi.companies).mockResolvedValue([
    { id: 'c1', name: 'Alfa', isActive: true },
    { id: 'c2', name: 'Beta', isActive: true },
  ] as never);
  vi.mocked(usersApi.companyClientChoices).mockResolvedValue([
    { id: 'k1', name: 'Klijent Prvi', email: 'prvi@alfa.test' },
    { id: 'k2', name: 'Klijent Drugi', email: 'drugi@alfa.test' },
  ] as never);
  vi.mocked(companyReportsApi.top).mockResolvedValue({ items: [row('c1', 'Alfa', 8, 4, 80), row('c2', 'Beta', 2, 0, 20)], totalTickets: 10, topTickets: 10, totalCompanies: 2 });
  vi.mocked(companyReportsApi.summary).mockImplementation(async (p) => (p.clientId ? clientSummary : companySummary));
  vi.mocked(companyReportsApi.clients).mockResolvedValue([
    { clientId: 'k1', name: 'Klijent Prvi', total: 6, open: 1, closed: 5 },
    { clientId: 'k2', name: 'Klijent Drugi', total: 2, open: 1, closed: 1 },
  ]);
  vi.mocked(companyReportsApi.topics).mockResolvedValue([
    { id: 't1', name: 'Printer', count: 3, isActive: true },
    { id: null, name: null, count: 1, isActive: true },
    { id: 't2', name: 'Stara tema', count: 1, isActive: false },
  ]);
  vi.mocked(companyReportsApi.categories).mockResolvedValue([{ id: 'g1', name: 'Hardver', count: 5, isActive: true }]);
  vi.mocked(companyReportsApi.branches).mockResolvedValue([{ departmentId: 'd1', name: 'IT', count: 5, noSubBranchCount: 2, subBranches: [{ subBranchId: 's1', name: 'Mreža', count: 3 }] }]);
  vi.mocked(companyReportsApi.card).mockResolvedValue({
    firstTicketAtUtc: '2031-12-20T10:00:00Z', lastTicketAtUtc: '2033-02-01T10:00:00Z', totalTickets: 120, byYear: [{ year: 2032, count: 90 }, { year: 2033, count: 30 }],
    registeredClients: 8, activeClientsInPeriod: 3, activeClientsEver: 6, totalHours: 41.5,
  });
  vi.mocked(companyReportsApi.branchLoad).mockResolvedValue({
    totalTickets: 10, totalHours: 12.25,
    items: [
      { departmentId: 'd1', name: 'IT', tickets: 6, closed: 4, hours: 8.5, sharePercent: 60, subBranches: [
        { subBranchId: 's1', name: 'Mreža', tickets: 4, closed: 3, hours: 6 }, { subBranchId: null, name: '', tickets: 2, closed: 1, hours: 2.5 } ] },
      { departmentId: null, name: '', tickets: 4, closed: 0, hours: 3.75, sharePercent: 40, subBranches: [] },
    ],
  });
  vi.mocked(companyReportsApi.openAge).mockResolvedValue({ total: 5, upTo2Days: 1, upTo7Days: 2, upTo30Days: 1, older: 1, oldestDays: 60, oldestCompany: 'Alfa' });
  vi.mocked(companyReportsApi.timeSpent).mockResolvedValue({ totalHours: 4, sessions: 3, tickets: 2, avgHoursPerTicket: 2, runningSessions: 1 });
  vi.mocked(companyReportsApi.conversation).mockResolvedValue({ tickets: 3, avgMessagesPerTicket: 1.5, pickedUp: 2, avgHoursToPickup: 2.5, notPickedUp: 1 });
  vi.mocked(companyReportsApi.rhythm).mockResolvedValue({ total: 4, byWeekday: [3, 0, 0, 0, 0, 0, 1], byHour: Array.from({ length: 24 }, (_, h) => (h === 9 ? 3 : h === 13 ? 1 : 0)), timeZone: 'Europe/Sarajevo' });
  vi.mocked(companyReportsApi.repeatedTopics).mockResolvedValue({ groups: 2, tickets: 5, items: [{ clientId: 'k1', clientName: 'Klijent Prvi', topicId: 't1', topicName: 'Printer', count: 3 }] });
  vi.mocked(companyReportsApi.quiet).mockResolvedValue({ total: 1, items: [{ companyId: 'c9', name: 'Utihla d.o.o.', previousTotal: 6, lastTicketAtUtc: '2026-10-05T09:00:00Z' }] });
  vi.mocked(companyReportsApi.closedBy).mockResolvedValue({ people: [{ userId: 'u1', name: 'Amra Amić', closedCount: 2, branches: [{ departmentId: 'd1', name: 'IT', count: 2 }] }], branches: [], totalClosed: 2 });
});

function renderTab() {
  return renderWithQuery(
    <MemoryRouter>
      <CompaniesTab />
    </MemoryRouter>
  );
}

async function chooseCompany(user: ReturnType<typeof userEvent.setup>, name = 'Alfa') {
  await user.click(await screen.findByRole('combobox', { name: 'Company' }));
  await user.click(await screen.findByRole('option', { name }));
}

describe('the second tab of the dashboard: companies', () => {
  it('shows the extra statistics, each saying which tickets it counts, with their figures', async () => {
    renderTab();

    expect(await screen.findByText('Open tickets by age')).toBeInTheDocument();
    expect(screen.getByText('Counts the tickets that are not closed now, whatever the period; the age is from when they were created.')).toBeInTheDocument();
    expect(screen.getByText('Open now: 5')).toBeInTheDocument();
    expect(screen.getByText('Oldest: 60 days, Alfa')).toBeInTheDocument();
    expect(await screen.findByText('4 h written down')).toBeInTheDocument();
    expect(screen.getByText('3 work sessions on 2 tickets')).toBeInTheDocument();
    expect(screen.getByText('1 still running (not in the hours)')).toBeInTheDocument();
    expect(await screen.findByText('1.5 replies per ticket')).toBeInTheDocument();
    expect(screen.getByText('Average time to pick-up: 2.5 h')).toBeInTheDocument();
    expect(screen.getByText('2 of 3 tickets picked up; 1 not yet')).toBeInTheDocument();
    expect(await screen.findByText('Reported again and again')).toBeInTheDocument();
    expect(screen.getByText('2 repeated reports, 5 tickets in all')).toBeInTheDocument();
    expect(screen.getByText('Counts the tickets created in the period, by the day and the hour in Europe/Sarajevo.')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /Mon: 3.*Sun: 1/ })).toBeInTheDocument();
  });

  it('lists the companies that went quiet only when no company is chosen', async () => {
    const user = userEvent.setup();
    renderTab();

    expect(await screen.findByText('Utihla d.o.o.')).toBeInTheDocument();
    expect(screen.getByText(/last ticket: 05\.10\.2026/)).toBeInTheDocument();
    expect(screen.getByText('Companies gone quiet: 1')).toBeInTheDocument();
    await chooseCompany(user);
    await screen.findByText('Employees of the company');

    expect(screen.queryByText('Utihla d.o.o.')).not.toBeInTheDocument();
    expect(companyReportsApi.quiet).toHaveBeenCalledTimes(1);
  });

  it('asks the extra statistics for the company and person that are chosen', async () => {
    const user = userEvent.setup();
    renderTab();
    await chooseCompany(user);
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toBeEnabled());
    await user.click(screen.getByRole('combobox', { name: 'Employee of the company' }));
    await user.click(await screen.findByRole('option', { name: /^Klijent Prvi/ }));

    await waitFor(() => expect(vi.mocked(companyReportsApi.repeatedTopics).mock.calls.at(-1)?.[0]).toMatchObject({ companyId: 'c1', clientId: 'k1' }));
    for (const call of [companyReportsApi.openAge, companyReportsApi.timeSpent, companyReportsApi.conversation, companyReportsApi.rhythm]) {
      expect(vi.mocked(call).mock.calls.at(-1)?.[0]).toMatchObject({ companyId: 'c1', clientId: 'k1' });
    }
  });

  it('says that nothing is open or written down instead of showing zeros that look like data', async () => {
    vi.mocked(companyReportsApi.openAge).mockResolvedValue({ total: 0, upTo2Days: 0, upTo7Days: 0, upTo30Days: 0, older: 0, oldestDays: 0, oldestCompany: null });
    vi.mocked(companyReportsApi.timeSpent).mockResolvedValue({ totalHours: 0, sessions: 0, tickets: 0, avgHoursPerTicket: 0, runningSessions: 0 });
    vi.mocked(companyReportsApi.repeatedTopics).mockResolvedValue({ groups: 0, tickets: 0, items: [] });
    vi.mocked(companyReportsApi.quiet).mockResolvedValue({ total: 0, items: [] });
    renderTab();

    expect(await screen.findByText('Nothing is open.')).toBeInTheDocument();
    expect(await screen.findByText('No time written down in this period.')).toBeInTheDocument();
    expect(await screen.findByText('Nobody reported the same topic twice in this period.')).toBeInTheDocument();
    expect(await screen.findByText('No company went quiet.')).toBeInTheDocument();
  });

  it('with nothing chosen shows the ten top companies, who solves, topics and categories, each saying which tickets it counts', async () => {
    renderTab();

    expect(await screen.findByText('Companies with the most tickets')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Alfa' })).toBeInTheDocument();
    expect(screen.getByText('8 tickets · 80%')).toBeInTheDocument();
    expect(screen.getByText('Together: 10 of 10 tickets of the period (100%). Companies with tickets: 2.')).toBeInTheDocument();
    expect(await screen.findByText('Who solves them')).toBeInTheDocument();
    expect(await screen.findByText('By topic')).toBeInTheDocument();
    expect(await screen.findByText('By category')).toBeInTheDocument();
    // two groups of tickets, and each card says which one it counts
    expect(screen.getAllByText('Counts the tickets closed in the period, whenever they were created.')).toHaveLength(1);
    expect(screen.getAllByText('Counts the tickets created in the period.').length).toBeGreaterThanOrEqual(3);
    // the rows add up: tickets that have no topic yet have a row, and a topic that is switched off keeps its name
    expect(screen.getByText('(none yet)')).toBeInTheDocument();
    expect(screen.getByText('Stara tema')).toBeInTheDocument();
    expect(screen.getByText('(inactive)')).toBeInTheDocument();
    // the cards of one company and the branches are for a chosen company
    expect(screen.queryByText('By branch')).not.toBeInTheDocument();
    expect(companyReportsApi.summary).not.toHaveBeenCalled();
    expect(companyReportsApi.clients).not.toHaveBeenCalled();
    expect(screen.getByRole('link', { name: /See more/ })).toHaveAttribute('href', expect.stringContaining('/dashboard/companies?from='));
  });

  it('asks for no company when none is chosen, and for no person without a company', async () => {
    renderTab();
    await screen.findByText('By topic');

    const topicCall = vi.mocked(companyReportsApi.topics).mock.calls[0][0];
    expect(topicCall.companyId).toBeUndefined();
    expect(topicCall.clientId).toBeUndefined();
    expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toBeDisabled();
  });

  it('chooses a company from the list of top companies and shows its figures, its people and its branches', async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click(await screen.findByRole('button', { name: 'Alfa' }));

    expect(await screen.findByText('Created')).toBeInTheDocument();
    expect(await screen.findByText('12.5 h')).toBeInTheDocument();
    expect(screen.getByText('80%')).toBeInTheDocument();
    expect(screen.queryByText('Companies with the most tickets')).not.toBeInTheDocument();
    expect(await screen.findByText('Employees of the company')).toBeInTheDocument();
    expect(await screen.findByText('Load by branch')).toBeInTheDocument();
    expect(screen.getByText('Mreža')).toBeInTheDocument();
    expect(screen.getByText('No sub-branch')).toBeInTheDocument();
    expect(vi.mocked(companyReportsApi.summary).mock.calls.at(-1)?.[0]).toMatchObject({ companyId: 'c1' });
    expect(vi.mocked(companyReportsApi.summary).mock.calls.at(-1)?.[0].clientId).toBeUndefined();
  });

  it('shows the card of the company: first and last ticket, the years, and the clients registered against those that wrote', async () => {
    const user = userEvent.setup();
    renderTab();

    await chooseCompany(user);

    const card = (await screen.findByText('Card of the company')).closest('div')!.parentElement!;
    expect(within(card).getByText('First ticket').nextSibling).toHaveTextContent('20.12.2031');
    expect(within(card).getByText('Last ticket').nextSibling).toHaveTextContent('01.02.2033');
    expect(within(card).getByText('All tickets').nextSibling).toHaveTextContent('120');
    expect(within(card).getByText('All recorded work').nextSibling).toHaveTextContent('41.5 h');
    expect(within(card).getByText('2032').nextSibling).toHaveTextContent('90');
    expect(within(card).getByText('2033').nextSibling).toHaveTextContent('30');
    expect(within(card).getByText('Registered').nextSibling).toHaveTextContent('8');
    expect(within(card).getByText('Wrote in the period').nextSibling).toHaveTextContent('3 / 8');
    expect(within(card).getByText('Ever wrote').nextSibling).toHaveTextContent('6 / 8');
    expect(within(card).getByText(/the period does not change it/)).toBeInTheDocument(); // what the period does is said
    expect(vi.mocked(companyReportsApi.card)).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'c1' }));
  });

  it('says so when the company has no tickets yet', async () => {
    vi.mocked(companyReportsApi.card).mockResolvedValue({ firstTicketAtUtc: null, lastTicketAtUtc: null, totalTickets: 0, byYear: [], registeredClients: 2, activeClientsInPeriod: 0, activeClientsEver: 0, totalHours: 0 });
    const user = userEvent.setup();
    renderTab();

    await chooseCompany(user);

    expect(await screen.findByText('The company has no tickets yet.')).toBeInTheDocument();
  });

  it('shows the branches that work for the company with the closed, the hours of their staff and the share, and the sub-branches under their branch', async () => {
    const user = userEvent.setup();
    renderTab();

    await chooseCompany(user);

    const load = (await screen.findByText('Load by branch')).closest('div')!.parentElement!;
    const rows = within(load).getAllByRole('row').slice(1).map((r) => within(r).getAllByRole('cell').map((c) => c.textContent));
    expect(rows[0]).toEqual(['IT', '6', '4', '8.5', '60 %']);
    expect(rows[1]).toEqual(['Mreža', '4', '3', '6', '—']); // a sub-branch has no share of its own
    expect(rows[2]).toEqual(['No sub-branch', '2', '1', '2.5', '—']);
    expect(rows[3]).toEqual(['Not routed / no branch', '4', '0', '3.75', '40 %']);
    expect(within(load).getByText('In all: 10 tickets created, 12.25 h of work.')).toBeInTheDocument();
    expect(within(load).getByText(/by the branch of the person who worked, not of the ticket/)).toBeInTheDocument();
    expect(vi.mocked(companyReportsApi.branchLoad)).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'c1' }));
  });

  it('does not ask for the card or the load of the branches until a company is chosen, and never for the old branches of a person without one', async () => {
    renderTab();
    await screen.findByText('By topic');

    expect(vi.mocked(companyReportsApi.card)).not.toHaveBeenCalled();
    expect(vi.mocked(companyReportsApi.branchLoad)).not.toHaveBeenCalled();
    expect(vi.mocked(companyReportsApi.branches)).not.toHaveBeenCalled();
    expect(screen.queryByText('Card of the company')).not.toBeInTheDocument();
  });

  it('shows the branches of a person, not of the company, when a person is chosen: the load of the company is its own', async () => {
    const user = userEvent.setup();
    renderTab();
    await chooseCompany(user);
    await screen.findByText('Load by branch');
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toBeEnabled());

    await user.click(screen.getByRole('combobox', { name: 'Employee of the company' }));
    await user.click(await screen.findByRole('option', { name: /^Klijent Prvi/ }));

    expect(await screen.findByText('By branch')).toBeInTheDocument();
    expect(screen.queryByText('Load by branch')).not.toBeInTheDocument();
    expect(screen.queryByText('Card of the company')).not.toBeInTheDocument();
    expect(vi.mocked(companyReportsApi.branches)).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'c1', clientId: 'k1' }));
  });

  it('shows a person of the company next to the company: the tickets against the average of the people, the resolution time and the SLA against the company', async () => {
    const user = userEvent.setup();
    renderTab();
    await chooseCompany(user);
    await screen.findByText('Employees of the company');

    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toBeEnabled());
    await user.click(screen.getByRole('combobox', { name: 'Employee of the company' }));
    await user.click(await screen.findByRole('option', { name: /^Klijent Prvi/ }));

    await waitFor(() => expect(vi.mocked(companyReportsApi.summary).mock.calls.map((c) => c[0])).toContainEqual(expect.objectContaining({ companyId: 'c1', clientId: 'k1' })));
    expect(await screen.findByText('Average of the company: 4 per employee who reported')).toBeInTheDocument(); // 8 tickets, 2 people
    expect(screen.getByText('Company: 12.5 h')).toBeInTheDocument();
    expect(screen.getByText('Company: 80%')).toBeInTheDocument();
    expect(screen.getByText('20 h')).toBeInTheDocument();
  });

  it('lets a person be chosen from the card, but not one whose account is switched off (they are not in the box, so the box would look empty)', async () => {
    const user = userEvent.setup();
    vi.mocked(companyReportsApi.clients).mockResolvedValue([
      { clientId: 'k1', name: 'Klijent Prvi', total: 6, open: 1, closed: 5 },
      { clientId: 'k9', name: 'Bivši Zaposlenik', total: 3, open: 0, closed: 3 },
    ]);
    renderTab();
    await chooseCompany(user);

    expect(await screen.findByText('Bivši Zaposlenik')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Bivši Zaposlenik' })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Klijent Prvi' }));

    await waitFor(() => expect(vi.mocked(companyReportsApi.summary).mock.calls.map((c) => c[0])).toContainEqual(expect.objectContaining({ companyId: 'c1', clientId: 'k1' })));
    expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toHaveValue('Klijent Prvi');
  });

  it('forgets the person when the company is cleared or changed', async () => {
    const user = userEvent.setup();
    renderTab();
    await chooseCompany(user);
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toBeEnabled());
    await user.click(screen.getByRole('combobox', { name: 'Employee of the company' }));
    await user.click(await screen.findByRole('option', { name: /^Klijent Prvi/ }));
    await waitFor(() => expect(vi.mocked(companyReportsApi.topics).mock.calls.at(-1)?.[0].clientId).toBe('k1'));

    await user.clear(screen.getByRole('combobox', { name: 'Company' }));

    await waitFor(() => expect(vi.mocked(companyReportsApi.topics).mock.calls.at(-1)?.[0]).toEqual(expect.objectContaining({ companyId: undefined, clientId: undefined })));
    expect(screen.getByRole('combobox', { name: 'Employee of the company' })).toBeDisabled();
    expect(await screen.findByText('Companies with the most tickets')).toBeInTheDocument();
  });

  it('downloads the workbook for the same choice as the screen', async () => {
    const user = userEvent.setup();
    vi.mocked(companyReportsApi.exportXlsx).mockResolvedValue(undefined);
    renderTab();
    await chooseCompany(user, 'Beta');

    await user.click(await screen.findByRole('button', { name: /Export/ }));

    await waitFor(() => expect(companyReportsApi.exportXlsx).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'c2' })));
  });

  it('says so, in one line, when a figure could not be fetched', async () => {
    vi.mocked(companyReportsApi.topics).mockRejectedValue(new Error('boom'));
    renderTab();

    expect(await screen.findByText(/could not be loaded|Some figures/i)).toBeInTheDocument();
  });

  it('puts a dash, not a zero, where a company has no closed ticket to take an average of', async () => {
    const user = userEvent.setup();
    vi.mocked(companyReportsApi.summary).mockResolvedValue({ total: 3, new: 3, open: 0, closed: 0, avgResolutionHours: 0, slaComplianceRate: null, previousTotal: 0 });
    renderTab();
    await user.click(await screen.findByRole('button', { name: 'Alfa' }));

    const cards = await screen.findAllByText('—');
    expect(cards.length).toBeGreaterThanOrEqual(2);
    expect(within(document.body).queryByText('0 h')).not.toBeInTheDocument();
  });
});

