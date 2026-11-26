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
  companyReportsApi: { top: vi.fn(), list: vi.fn(), summary: vi.fn(), clients: vi.fn(), topics: vi.fn(), categories: vi.fn(), branches: vi.fn(), closedBy: vi.fn(), timeSeries: vi.fn(), exportXlsx: vi.fn() },
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
  it('with nothing chosen shows the ten top companies, who solves, topics and categories, each saying which tickets it counts', async () => {
    renderTab();

    expect(await screen.findByText('Companies with the most tickets')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Alfa' })).toBeInTheDocument();
    expect(screen.getByText('8 tickets · 80%')).toBeInTheDocument();
    expect(screen.getByText('Together: 10 of 10 tickets of the period. Companies with tickets: 2.')).toBeInTheDocument();
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
    expect(await screen.findByText('By branch')).toBeInTheDocument();
    expect(screen.getByText('Mreža')).toBeInTheDocument();
    expect(screen.getByText('No sub-branch')).toBeInTheDocument();
    expect(vi.mocked(companyReportsApi.summary).mock.calls.at(-1)?.[0]).toMatchObject({ companyId: 'c1' });
    expect(vi.mocked(companyReportsApi.summary).mock.calls.at(-1)?.[0].clientId).toBeUndefined();
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

