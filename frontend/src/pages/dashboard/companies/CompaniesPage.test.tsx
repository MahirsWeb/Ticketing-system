import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import CompaniesPage from './CompaniesPage';
import { companyReportsApi } from '../../../api/companyReports';
import { renderWithQuery } from '../../../test/renderWithQuery';

vi.mock('../../../api/companyReports', () => ({ companyReportsApi: { list: vi.fn(), clients: vi.fn(), exportXlsx: vi.fn() } }));
vi.mock('../../../components/DateRangePicker', () => ({ DateRangePicker: () => <span>the period</span> }));

const row = (id: string, name: string, total: number, previousTotal: number) => ({
  companyId: id, name, total, new: 0, open: 1, closed: total, avgResolutionHours: 10, slaComplianceRate: 75, previousTotal, sharePercent: 50,
});
const FROM = '2026-10-01T00:00:00.000Z';
const TO = '2026-10-31T00:00:00.000Z';

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(companyReportsApi.list).mockImplementation(async (p) => {
    if (p.sort === 'growth' && p.descending) return { items: [row('g1', 'Raste d.o.o.', 9, 3), row('g2', 'Ista d.o.o.', 4, 4)], page: 1, pageSize: 5, totalCount: 3, totalTickets: 16 };
    if (p.sort === 'growth') return { items: [row('q1', 'Utihnula d.o.o.', 0, 6), row('q2', 'Pada d.o.o.', 1, 5)], page: 1, pageSize: 5, totalCount: 3, totalTickets: 16 };
    return { items: [row('c1', 'Alfa', 8, 4), row('c2', 'Beta', 2, 2)], page: p.page ?? 1, pageSize: 25, totalCount: 60, totalTickets: 10 };
  });
  vi.mocked(companyReportsApi.clients).mockResolvedValue([{ clientId: 'k1', name: 'Klijent Prvi', total: 6, open: 1, closed: 5 }]);
});

function renderPage() {
  return renderWithQuery(
    <MemoryRouter initialEntries={[`/dashboard/companies?from=${encodeURIComponent(FROM)}&to=${encodeURIComponent(TO)}`]}>
      <CompaniesPage />
    </MemoryRouter>
  );
}

const lastTableCall = () => vi.mocked(companyReportsApi.list).mock.calls.map((c) => c[0]).filter((p) => p.pageSize === 25).at(-1)!;

describe('"See more": every company in a table', () => {
  it('uses the period it was opened with and shows every company with the figures of the period', async () => {
    renderPage();

    expect(await screen.findByRole('button', { name: /Alfa/ }, { timeout: 5000 })).toBeInTheDocument();
    expect(lastTableCall()).toMatchObject({ from: FROM, to: TO, sort: 'total', descending: true, page: 1, pageSize: 25 });
    expect(screen.getByText('Companies: 60. Tickets created in the period, all companies: 10.')).toBeInTheDocument();
    expect(screen.getByText('Counts the tickets created in the period.')).toBeInTheDocument();
  });

  it('sorts by a column, and again on the same column the other way round; the first press on the name is A to Z', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('button', { name: /Alfa/ });

    await user.click(screen.getByRole('button', { name: /^Created/ })); // already the order: the same column turns round
    await waitFor(() => expect(lastTableCall()).toMatchObject({ sort: 'total', descending: false }));

    await user.click(screen.getByRole('button', { name: 'Company' }));
    await waitFor(() => expect(lastTableCall()).toMatchObject({ sort: 'name', descending: false }));
    expect(screen.getByRole('columnheader', { name: /Company/ })).toHaveAttribute('aria-sort', 'ascending');
  });

  it('searches by name a moment after typing stops, from the first page', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('button', { name: /Alfa/ });
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(lastTableCall().page).toBe(2));

    await user.type(screen.getByRole('textbox', { name: 'Search companies' }), 'alf');

    await waitFor(() => expect(lastTableCall()).toMatchObject({ search: 'alf', page: 1 }));
  });

  it('turns the pages', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('button', { name: /Alfa/ });

    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument(); // 60 companies, 25 a page
    await user.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => expect(lastTableCall().page).toBe(2));
  });

  it('opens a row to the people of the company, and closes it again', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Alfa/ }));

    expect(await screen.findByText('Klijent Prvi')).toBeInTheDocument();
    expect(companyReportsApi.clients).toHaveBeenCalledWith(expect.objectContaining({ companyId: 'c1', from: FROM, to: TO }));
    await user.click(screen.getByRole('button', { name: /Alfa/ }));
    expect(screen.queryByText('Klijent Prvi')).not.toBeInTheDocument();
  });

  it('lists the companies that grew and the ones that fell, and marks the ones that have gone quiet; a company that did not change is in neither', async () => {
    renderPage();

    expect(await screen.findByText('Raste d.o.o.')).toBeInTheDocument();
    expect(screen.getByText('now 9, before 3')).toBeInTheDocument();
    expect(screen.queryByText('Ista d.o.o.')).not.toBeInTheDocument();
    expect(await screen.findByText('Utihnula d.o.o.')).toBeInTheDocument();
    expect(screen.getByText('quiet')).toBeInTheDocument(); // none at all now
    expect(screen.getByText('Pada d.o.o.')).toBeInTheDocument();
  });

  it('downloads the workbook for the period', async () => {
    const user = userEvent.setup();
    vi.mocked(companyReportsApi.exportXlsx).mockResolvedValue(undefined);
    renderPage();
    await screen.findByRole('button', { name: /Alfa/ });

    await user.click(screen.getByRole('button', { name: /Export/ }));

    await waitFor(() => expect(companyReportsApi.exportXlsx).toHaveBeenCalledWith({ from: FROM, to: TO }));
  });
});
