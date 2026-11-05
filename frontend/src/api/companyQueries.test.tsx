import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import { renderWithQuery } from '../test/renderWithQuery';
import { useCompaniesAdmin, useCompany } from './adminLookupQueries';
import { useCompanyClients } from './userQueries';
import { lookupKeys } from './lookupKeys';
import { userKeys } from './userKeys';
import { lookupsApi } from './lookups';
import { queryClient } from './queryClient';
import { apiClient } from './client';
import type { CompanyItemFull } from '../types';

vi.mock('./client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const put = vi.mocked(apiClient.put);
const asked = (url: string) => get.mock.calls.filter((c) => c[0] === url).length;

const firm = (id: string, name: string): CompanyItemFull => ({ id, name, address: null, contactInfo: null, isActive: true });

function Company({ id, tag = 'company' }: { id: string; tag?: string }) {
  const q = useCompany(id);
  return <p data-testid={tag}>{q.isError ? 'failed' : q.data ? q.data.name : 'loading'}</p>;
}
function Companies() {
  const q = useCompaniesAdmin();
  return <p data-testid="companies">{q.data ? q.data.map((c) => c.name).join(',') : 'loading'}</p>;
}
function Clients({ id, tag = 'clients' }: { id: string; tag?: string }) {
  const q = useCompanyClients(id);
  return <p data-testid={tag}>{q.data ? q.data.map((c) => c.id).join(',') : 'loading'}</p>;
}

describe('the company data the screens share (T-17)', () => {
  beforeEach(() => {
    queryClient.clear();
    get.mockReset();
    put.mockReset();
    get.mockImplementation((async (url: string, config?: unknown) => {
      if (url === '/api/companies') return { data: [firm('c1', 'Acme'), firm('c2', 'Beta')] };
      if (url === '/api/users') return { data: [{ id: `k-of-${(config as { params: { companyId: string } }).params.companyId}` }] };
      return { data: firm(url.split('/').pop()!, `Firma ${url.split('/').pop()}`) };
    }) as never);
  });
  afterEach(() => queryClient.clear());

  it('asks for one company once however many places show it, and for another company on its own', async () => {
    renderWithQuery(
      <>
        <Company id="c1" tag="a" />
        <Company id="c1" tag="b" />
        <Company id="c2" tag="other" />
      </>,
      queryClient
    );

    await waitFor(() => expect(screen.getByTestId('other')).toHaveTextContent('Firma c2'));
    expect(asked('/api/companies/c1')).toBe(1);
    expect(asked('/api/companies/c2')).toBe(1);
  });

  it('forgets a company, and the list of companies, and the clients of a company, when they are left', async () => {
    const view = renderWithQuery(
      <>
        <Company id="c1" />
        <Companies />
        <Clients id="c1" />
      </>,
      queryClient
    );
    await screen.findByText('Firma c1');
    await screen.findByText('Acme,Beta');
    await screen.findByText('k-of-c1');

    view.unmount();

    await waitFor(() => expect(queryClient.getQueryData(lookupKeys.company('c1'))).toBeUndefined());
    expect(queryClient.getQueryData(lookupKeys.companiesAdmin)).toBeUndefined();
    expect(queryClient.getQueryData(userKeys.companyClients('c1'))).toBeUndefined();
  });

  it('asks the administrator\'s list for the companies that are switched off too, and the clients of one company only', async () => {
    renderWithQuery(
      <>
        <Companies />
        <Clients id="c7" />
      </>,
      queryClient
    );

    await screen.findByText('Acme,Beta');
    await screen.findByText('k-of-c7');
    expect(get.mock.calls.find((c) => c[0] === '/api/companies')![1]).toEqual({ params: { includeInactive: true } });
    expect(get.mock.calls.find((c) => c[0] === '/api/users')![1]).toEqual({ params: { role: 'Client', companyId: 'c7' } });
  });

  it('keeps the clients of each company as an entry of its own', async () => {
    renderWithQuery(
      <>
        <Clients id="c1" tag="first" />
        <Clients id="c2" tag="second" />
      </>,
      queryClient
    );

    await waitFor(() => expect(screen.getByTestId('second')).toHaveTextContent('k-of-c2'));
    expect(screen.getByTestId('first')).toHaveTextContent('k-of-c1');
    expect(get.mock.calls.filter((c) => c[0] === '/api/users')).toHaveLength(2);
  });

  describe('changing a company', () => {
    it('puts what the server sent back into the company that is open at once, and asks the lists again', async () => {
      renderWithQuery(
        <>
          <Company id="c1" />
          <Companies />
        </>,
        queryClient
      );
      await screen.findByText('Acme,Beta');
      await screen.findByText('Firma c1');
      const slow = new Promise(() => {});
      get.mockImplementation((() => slow) as never);
      put.mockResolvedValue({ data: firm('c1', 'Acme novi') } as never);

      await act(async () => {
        await lookupsApi.updateCompany('c1', 'Acme novi', undefined, undefined, true);
      });

      // The server has not answered the questions that were asked again, and the company is already what the change made it.
      await waitFor(() => expect(screen.getByTestId('company')).toHaveTextContent('Acme novi'));
      expect(asked('/api/companies')).toBe(2);
    });

    it('puts nothing into memory for a company that nobody has open', async () => {
      put.mockResolvedValue({ data: firm('c9', 'Deveta') } as never);

      await lookupsApi.updateCompany('c9', 'Deveta', undefined, undefined, true);

      expect(queryClient.getQueryData(lookupKeys.company('c9'))).toBeUndefined();
    });

    it('sends what it was given to the address of the company', async () => {
      put.mockResolvedValue({ data: firm('c1', 'Acme') } as never);

      await lookupsApi.updateCompany('c1', 'Acme', 'Titova 5', undefined, false);

      expect(put.mock.calls).toEqual([['/api/companies/c1', { name: 'Acme', address: 'Titova 5', contactInfo: undefined, isActive: false }]]);
    });

    it('does not touch what is shown when the server refuses', async () => {
      renderWithQuery(<Company id="c1" />, queryClient);
      await screen.findByText('Firma c1');
      put.mockRejectedValue(new Error('refused'));

      await act(async () => {
        await lookupsApi.updateCompany('c1', 'Acme novi', undefined, undefined, true).catch(() => undefined);
      });

      expect(screen.getByTestId('company')).toHaveTextContent('Firma c1');
      expect(asked('/api/companies/c1')).toBe(1);
    });
  });
});
