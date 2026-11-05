import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import CompaniesPage from './CompaniesPage';
import { queryClient } from '../../api/queryClient';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installCompaniesBackend, newCompaniesState, type CompaniesState } from '../../test/fakeCompaniesApi';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

let server: ReturnType<typeof installCompaniesBackend>;
const originalDefaults = queryClient.getDefaultOptions();

function renderPage(state: CompaniesState = newCompaniesState(), before?: (server: ReturnType<typeof installCompaniesBackend>) => void) {
  server = installCompaniesBackend(state);
  before?.(server);
  return renderWithQuery(
    <MemoryRouter>
      <CompaniesPage />
    </MemoryRouter>,
    queryClient
  );
}

const listed = () => screen.getAllByRole('link').map((a) => a.textContent);

describe('the companies page', () => {
  beforeEach(() => {
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
  });
  afterAll(() => queryClient.setDefaultOptions(originalDefaults));

  describe('the list', () => {
    it('shows every company, switched off ones too, each as a link to its page, with its address and contact and whether it is active', async () => {
      renderPage();
      await screen.findByText('Acme d.o.o.');

      expect(screen.getByRole('heading', { name: 'Client companies' })).toBeInTheDocument();
      expect(server.sent('GET', /\/api\/companies$/)[0].params).toEqual({ includeInactive: true });
      const links = screen.getAllByRole('link');
      expect(links.map((a) => a.getAttribute('href'))).toEqual(['/companies/c1', '/companies/c2', '/companies/c3']);
      expect(within(links[0]).getByText('Zmaja od Bosne 1 · 033 123 456')).toBeInTheDocument();
      expect(within(links[0]).getByText('Active')).toBeInTheDocument();
      expect(within(links[2]).getByText('Inactive')).toBeInTheDocument();
      expect(within(links[1]).queryByText(/·/)).not.toBeInTheDocument();
    });

    it('says so when there are no companies', async () => {
      renderPage({ ...newCompaniesState(), companies: [] });

      expect(await screen.findByText('No companies yet.')).toBeInTheDocument();
    });
  });

  describe('adding one', () => {
    it('sends what was written, empties the form, and the company is in the list', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Acme d.o.o.');

      await user.type(screen.getByLabelText('Name'), 'Gama');
      await user.type(screen.getByLabelText('Address (optional)'), 'Titova 5');
      await user.type(screen.getByLabelText('Contact info (optional)'), 'gama@firma.test');
      await user.click(screen.getByRole('button', { name: 'Add company' }));

      expect(await screen.findByText('Gama')).toBeInTheDocument();
      expect(server.sent('POST', /\/api\/companies$/).map((r) => r.body)).toEqual([{ name: 'Gama', address: 'Titova 5', contactInfo: 'gama@firma.test' }]);
      expect(screen.getByLabelText('Name')).toHaveValue('');
      expect(screen.getByLabelText('Address (optional)')).toHaveValue('');
      expect(screen.getByLabelText('Contact info (optional)')).toHaveValue('');
      expect(listed().some((text) => text?.startsWith('Gama'))).toBe(true);
    });

    it('sends no address or contact when they were left empty', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Acme d.o.o.');

      await user.type(screen.getByLabelText('Name'), 'Gama');
      await user.click(screen.getByRole('button', { name: 'Add company' }));

      await screen.findByText('Gama');
      expect(server.sent('POST', /\/api\/companies$/)[0].body).toEqual({ name: 'Gama' });
    });

    it('wants a name, and a name of nothing but spaces is not one', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Acme d.o.o.');

      await user.type(screen.getByLabelText('Name'), '   ');
      await user.click(screen.getByRole('button', { name: 'Add company' }));

      expect(screen.getByText('Company name is required.')).toBeInTheDocument();
      expect(server.sent('POST', /\/api\/companies$/)).toHaveLength(0);
    });

    it('limits the name, the address and the contact to what the server accepts', async () => {
      renderPage();
      await screen.findByText('Acme d.o.o.');

      expect(screen.getByLabelText('Name')).toHaveAttribute('maxlength', '120');
      expect(screen.getByLabelText('Address (optional)')).toHaveAttribute('maxlength', '100');
      expect(screen.getByLabelText('Contact info (optional)')).toHaveAttribute('maxlength', '120');
    });

    it('says what the server said when it refuses, or plainly when it said nothing, and keeps what was written', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Acme d.o.o.');
      await user.type(screen.getByLabelText('Name'), 'Beta');

      await user.click(screen.getByRole('button', { name: 'Add company' }));
      expect(await screen.findByText('A company with that name already exists.')).toBeInTheDocument();

      server.refuse('POST', /\/api\/companies$/, 500, {});
      await user.click(screen.getByRole('button', { name: 'Add company' }));
      expect(await screen.findByText('Could not create the company.')).toBeInTheDocument();
      expect(screen.getByLabelText('Name')).toHaveValue('Beta');
    });

    it('cannot be sent twice while it is being made', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Acme d.o.o.');
      await user.type(screen.getByLabelText('Name'), 'Gama');
      const hold = server.hold('POST', /\/api\/companies$/);

      await user.click(screen.getByRole('button', { name: 'Add company' }));

      expect(screen.getByRole('button', { name: 'Adding…' })).toBeDisabled();
      hold.release();
      await screen.findByRole('button', { name: 'Add company' });
      expect(server.sent('POST', /\/api\/companies$/)).toHaveLength(1);
    });
  });

  describe('what it used to get wrong', () => {
    it('says that the companies are on their way, and not that there are none, until they have come', async () => {
      const holding = { release: () => {} };
      renderPage(newCompaniesState(), (s) => Object.assign(holding, s.hold('GET', /\/api\/companies$/)));

      expect(await screen.findByText('Loading companies…')).toBeInTheDocument();
      expect(screen.queryByText('No companies yet.')).not.toBeInTheDocument();

      holding.release();
      expect(await screen.findByText('Acme d.o.o.')).toBeInTheDocument();
      expect(screen.queryByText('Loading companies…')).not.toBeInTheDocument();
    });

    it('says so when the companies could not be loaded, instead of "No companies yet."', async () => {
      renderPage(newCompaniesState(), (s) => s.refuse('GET', /\/api\/companies$/, 500, {}, -1));

      expect(await screen.findByText('The companies could not be loaded. Try again in a moment.')).toBeInTheDocument();
      expect(screen.queryByText('No companies yet.')).not.toBeInTheDocument();
      expect(screen.queryByText('Loading companies…')).not.toBeInTheDocument();
    });

    it('sends the name, the address and the contact without the spaces around them, and nothing for an address of spaces', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Acme d.o.o.');

      await user.type(screen.getByLabelText('Name'), '  Gama ');
      await user.type(screen.getByLabelText('Address (optional)'), '   ');
      await user.type(screen.getByLabelText('Contact info (optional)'), ' gama@firma.test ');
      await user.click(screen.getByRole('button', { name: 'Add company' }));

      await screen.findByText('Gama');
      expect(server.sent('POST', /\/api\/companies$/)[0].body).toEqual({ name: 'Gama', contactInfo: 'gama@firma.test' });
    });
  });
});
