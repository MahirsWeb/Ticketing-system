import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import CompanyDetailPage from './CompanyDetailPage';
import { queryClient } from '../../api/queryClient';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installCompaniesBackend, newCompaniesState, ticketOf, type CompaniesState } from '../../test/fakeCompaniesApi';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

let server: ReturnType<typeof installCompaniesBackend>;
const originalDefaults = queryClient.getDefaultOptions();

function Go({ to, children }: { to: string; children: string }) {
  const navigate = useNavigate();
  return <button onClick={() => navigate(to)}>{children}</button>;
}

function renderPage(id = 'c1', state: CompaniesState = newCompaniesState(), before?: (server: ReturnType<typeof installCompaniesBackend>) => void) {
  server = installCompaniesBackend(state);
  before?.(server);
  return renderWithQuery(
    <MemoryRouter initialEntries={[`/companies/${id}`]}>
      <Go to="/companies/c2">go to Beta</Go>
      <Routes>
        <Route path="/companies/:id" element={<CompanyDetailPage />} />
      </Routes>
    </MemoryRouter>,
    queryClient
  );
}

const usersTab = () => screen.getByText(/^Users \(/);
const ticketsTab = () => screen.getByText('Tickets');
const bodyRows = () => within(screen.getAllByRole('rowgroup')[1]).getAllByRole('row');
const cellsOf = (row: HTMLElement) => within(row).getAllByRole('cell').map((c) => c.textContent);
const ticketRequests = () => server.sent('GET', /^\/api\/tickets$/);
const lastTicketParams = () => ticketRequests().at(-1)!.params as Record<string, unknown>;
/** The word beside the name of the company that says whether it is active. */
const pill = () => within(screen.getByRole('heading', { level: 1 }).parentElement!).getByText(/^(Active|Inactive)$/);
const editToggle = () => screen.getByRole('button', { name: /Edit company details|Close/ });

async function openEditor(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /Edit company details/ }));
}

describe('the company page', () => {
  beforeEach(() => {
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
  });
  afterAll(() => queryClient.setDefaultOptions(originalDefaults));

  describe('the company', () => {
    it('shows the name, whether it is active, the address and the contact, and the way back to all companies', async () => {
      renderPage();

      expect(await screen.findByRole('heading', { name: 'Acme d.o.o.' })).toBeInTheDocument();
      expect(pill()).toHaveTextContent('Active');
      expect(screen.getByText('Zmaja od Bosne 1 · 033 123 456')).toBeInTheDocument();
      expect(screen.getByRole('link', { name: '← All companies' })).toHaveAttribute('href', '/users?tab=companies');
      expect(server.sent('GET', /\/api\/companies\/c1$/)).toHaveLength(1);
      expect(server.sent('GET', /^\/api\/users$/)[0].params).toEqual({ role: 'Client', companyId: 'c1' });
    });

    it('shows no line for a company with neither address nor contact, and says when it is not active', async () => {
      renderPage('c3');

      expect(await screen.findByRole('heading', { name: 'Stara firma' })).toBeInTheDocument();
      expect(pill()).toHaveTextContent('Inactive');
      expect(screen.queryByText(/·/)).not.toBeInTheDocument();
      expect(screen.getByRole('heading', { level: 1 }).parentElement!.parentElement!.querySelector('p')).toBeNull();
    });

    it('says what the server said when the company could not be loaded, or plainly when it said nothing', async () => {
      const missing = renderPage('c9');
      expect(await screen.findByText('Company not found.')).toBeInTheDocument();
      missing.unmount();

      renderPage('c1', newCompaniesState(), (s) => s.refuse('GET', /companies\/c1$/, 500, {}));
      expect(await screen.findByText('Could not load this company.')).toBeInTheDocument();
    });
  });

  describe('the users', () => {
    it('are counted in the tab and shown with their e-mail, phone, when they were made, and whether they are active', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(usersTab()).toHaveTextContent('Users (3)');
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Name', 'Email', 'Phone', 'Created', 'Status']);
      expect(cellsOf(bodyRows()[0])).toEqual(['Mia Marić', 'mia.marić@firma.test', '061 111 222', '10.03.2026', 'Active']);
      expect(cellsOf(bodyRows()[1])).toEqual(['Nedim Nuhić', 'nedim.nuhić@firma.test', '—', '10.03.2026', 'Active']);
      expect(cellsOf(bodyRows()[2])[4]).toBe('Inactive');
    });

    it('are searched by name or e-mail, ignoring capital letters and the spaces around what was typed', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      const box = screen.getByPlaceholderText('Search by name or email…');

      await user.type(box, '  NEDIM ');
      expect(bodyRows().map((r) => cellsOf(r)[0])).toEqual(['Nedim Nuhić']);

      await user.clear(box);
      await user.type(box, 'olja.ostoj');
      expect(bodyRows().map((r) => cellsOf(r)[0])).toEqual(['Olja Ostojić']);

      await user.clear(box);
      await user.type(box, 'olja ostojić');
      expect(bodyRows().map((r) => cellsOf(r)[0])).toEqual(['Olja Ostojić']);

      await user.clear(box);
      await user.type(box, 'zzz');
      expect(screen.getByText('No matching users.')).toBeInTheDocument();
    });

    it('have a way to add one to this company', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getByRole('link', { name: '+ Add user' })).toHaveAttribute('href', '/users?companyId=c1');
    });
  });

  describe('the tickets', () => {
    it('are asked for by company, a hundred at most, when the tab is chosen, and not before', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      expect(ticketRequests()).toHaveLength(0);

      await user.click(ticketsTab());

      await screen.findByText('Printer ne radi');
      expect(lastTicketParams()).toEqual({ companyId: 'c1', clientId: undefined, pageSize: 100 });
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['#', 'Title', 'Status', 'Priority', 'Client', 'Assigned to', 'Created']);
      expect(cellsOf(bodyRows()[0])).toEqual(['#100001', 'Printer ne radi', 'InProgress', 'High', 'Mia Marić', 'Amra Ahmetović', '10.03.2026 09:30']);
      expect(cellsOf(bodyRows()[1])[5]).toBe('—');
      expect(screen.getByRole('link', { name: '#100001' })).toHaveAttribute('href', '/tickets/t1');
      expect(bodyRows()).toHaveLength(2);
    });

    it('say so when the company has none', async () => {
      const user = userEvent.setup();
      renderPage('c2', { ...newCompaniesState(), tickets: [] });
      await screen.findByText('Pero Perić');

      await user.click(ticketsTab());

      expect(await screen.findByText('No tickets found.')).toBeInTheDocument();
    });

    it('can be narrowed to the tickets of one client, and back to all of them', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await user.click(ticketsTab());
      await screen.findByText('Printer ne radi');
      const select = screen.getByRole('combobox', { name: 'Client' });
      expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual([
        'All clients', 'Mia Marić (mia.marić@firma.test)', 'Nedim Nuhić (nedim.nuhić@firma.test)', 'Olja Ostojić (olja.ostojić@firma.test)',
      ]);

      await user.selectOptions(select, 'k2');
      await waitFor(() => expect(lastTicketParams().clientId).toBe('k2'));
      await waitFor(() => expect(bodyRows().map((r) => cellsOf(r)[1])).toEqual(['Monitor treperi']));

      await user.selectOptions(select, '');
      await waitFor(() => expect(bodyRows()).toHaveLength(2));
      expect(lastTicketParams().clientId).toBeUndefined();
    });
  });

  describe('editing the company', () => {
    it('opens and closes, with what the company has in the fields', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      expect(editToggle()).toHaveTextContent('Edit company details');

      await user.click(editToggle());

      expect(editToggle()).toHaveTextContent('Close');
      expect(screen.getByLabelText('Name')).toHaveValue('Acme d.o.o.');
      expect(screen.getByLabelText('Address')).toHaveValue('Zmaja od Bosne 1');
      expect(screen.getByLabelText('Contact info')).toHaveValue('033 123 456');
      expect(screen.getByLabelText('Active')).toBeChecked();
      await user.click(editToggle());
      expect(editToggle()).toHaveTextContent('Edit company details');
    });

    it('limits the name, the address and the contact to what the server accepts', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getByLabelText('Name')).toHaveAttribute('maxlength', '120');
      expect(screen.getByLabelText('Address')).toHaveAttribute('maxlength', '100');
      expect(screen.getByLabelText('Contact info')).toHaveAttribute('maxlength', '120');
    });

    it('saves what was changed, shows it in the heading, and closes', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);

      await user.clear(screen.getByLabelText('Name'));
      await user.type(screen.getByLabelText('Name'), 'Acme novi');
      await user.clear(screen.getByLabelText('Address'));
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByRole('heading', { name: 'Acme novi' })).toBeInTheDocument();
      expect(server.sent('PUT', /companies\/c1$/).map((r) => r.body)).toEqual([{ name: 'Acme novi', contactInfo: '033 123 456', isActive: true, aiIndexingEnabled: true }]);
      expect(screen.getByText('033 123 456')).toBeInTheDocument();
      expect(editToggle()).toHaveTextContent('Edit company details');
    });

    it('cannot be saved without a name', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);

      await user.clear(screen.getByLabelText('Name'));
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
      await user.type(screen.getByLabelText('Name'), '   ');
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    });

    it('goes back to what the company has when it is cancelled, and closes', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.type(screen.getByLabelText('Name'), ' changed');
      await user.type(screen.getByLabelText('Address'), ' changed');
      await user.type(screen.getByLabelText('Contact info'), ' changed');
      await user.click(screen.getByLabelText('Active'));

      await user.click(screen.getByRole('button', { name: 'Cancel' }));
      await openEditor(user);

      expect(screen.getByLabelText('Name')).toHaveValue('Acme d.o.o.');
      expect(screen.getByLabelText('Address')).toHaveValue('Zmaja od Bosne 1');
      expect(screen.getByLabelText('Contact info')).toHaveValue('033 123 456');
      expect(screen.getByLabelText('Active')).toBeChecked();
      expect(server.sent('PUT', /companies/)).toHaveLength(0);
    });

    it('asks before switching a company off, and says that nothing of the clients or the tickets is touched', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.click(screen.getByLabelText('Active'));

      await user.click(screen.getByRole('button', { name: 'Save' }));

      const dialog = await screen.findByRole('dialog', { name: 'Deactivate this company?' });
      expect(within(dialog).getByText(/Existing clients and tickets for "Acme d.o.o." are completely unaffected/)).toBeInTheDocument();
      expect(server.sent('PUT', /companies/)).toHaveLength(0);

      await user.click(within(dialog).getByRole('button', { name: 'Deactivate' }));
      await waitFor(() => expect(pill()).toHaveTextContent('Inactive'));
      expect(server.sent('PUT', /companies\/c1$/)[0].body).toMatchObject({ isActive: false });
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('does not switch it off when the question is answered with Cancel, and keeps the editing open', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.click(screen.getByLabelText('Active'));
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(server.sent('PUT', /companies/)).toHaveLength(0);
      expect(editToggle()).toHaveTextContent('Close');
    });

    it('asks before switching a company on again too, with other words', async () => {
      const user = userEvent.setup();
      renderPage('c3');
      await screen.findByRole('heading', { name: 'Stara firma' });
      await openEditor(user);
      await user.click(screen.getByLabelText('Active'));

      await user.click(screen.getByRole('button', { name: 'Save' }));

      const dialog = await screen.findByRole('dialog', { name: 'Reactivate this company?' });
      expect(within(dialog).getByText(/will become active again/)).toBeInTheDocument();
      await user.click(within(dialog).getByRole('button', { name: 'Reactivate' }));
      await waitFor(() => expect(pill()).toHaveTextContent('Active'));
    });

    it('says what the server said when it refuses, or plainly when it said nothing, and keeps what was written', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.clear(screen.getByLabelText('Name'));
      await user.type(screen.getByLabelText('Name'), 'Beta');

      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('A company with that name already exists.')).toBeInTheDocument();

      server.refuse('PUT', /companies\/c1$/, 500, {});
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('Could not save changes.')).toBeInTheDocument();
      expect(screen.getByLabelText('Name')).toHaveValue('Beta');
      expect(editToggle()).toHaveTextContent('Close');
    });

    it('cannot be saved twice while it is being saved', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.type(screen.getByLabelText('Name'), '!');
      const hold = server.hold('PUT', /companies\/c1$/);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
      hold.release();
      await screen.findByRole('heading', { name: 'Acme d.o.o.!' });
      expect(server.sent('PUT', /companies/)).toHaveLength(1);
    });
  });

  describe('closing the editing, and the way it is told to a screen reader', () => {
    it('goes back to what the company has when it is closed with the button that opened it, not only with Cancel', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.type(screen.getByLabelText('Name'), ' changed');

      await user.click(editToggle());
      await openEditor(user);

      expect(screen.getByLabelText('Name')).toHaveValue('Acme d.o.o.');
    });

    it('does not keep the reason that saving was refused once the editing is cancelled', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.clear(screen.getByLabelText('Name'));
      await user.type(screen.getByLabelText('Name'), 'Beta');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await screen.findByText('A company with that name already exists.');

      await user.click(screen.getByRole('button', { name: 'Cancel' }));
      await openEditor(user);

      expect(screen.queryByText('A company with that name already exists.')).not.toBeInTheDocument();
    });

    it('does not keep the reason that saving was refused while it is tried again', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.clear(screen.getByLabelText('Name'));
      await user.type(screen.getByLabelText('Name'), 'Beta');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await screen.findByText('A company with that name already exists.');
      const hold = server.hold('PUT', /companies\/c1$/);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(screen.queryByText('A company with that name already exists.')).not.toBeInTheDocument();
      hold.release();
      await screen.findByText('A company with that name already exists.');
    });

    it('says to a screen reader that the editing is folded away, and the button says which part of the page it opens', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      const name = () => screen.getByLabelText('Name');
      expect(name().closest('[aria-hidden="true"]')).not.toBeNull();
      const panelId = editToggle().getAttribute('aria-controls')!;
      expect(document.getElementById(panelId)).toContainElement(name());

      await user.click(editToggle());

      expect(name().closest('[aria-hidden="true"]')).toBeNull();
    });

    it('names the row of tabs', async () => {
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getByRole('tablist', { name: 'Company' })).toBeInTheDocument();
    });
  });

  describe('what the tables say when they are empty', () => {
    it('says that the users are on their way, and not that there are none, until they have come', async () => {
      const held = { release: () => {} };
      renderPage('c1', newCompaniesState(), (s) => Object.assign(held, s.hold('GET', /^\/api\/users$/)));
      await screen.findByRole('heading', { name: 'Acme d.o.o.' });

      expect(screen.getByText('Loading the users…')).toBeInTheDocument();
      expect(screen.queryByText('This company has no users yet.')).not.toBeInTheDocument();
      expect(screen.queryByText('No matching users.')).not.toBeInTheDocument();
      held.release();
      await screen.findByText('Mia Marić');
      expect(screen.queryByText('Loading the users…')).not.toBeInTheDocument();
    });

    it('says it across the whole width of the table, for the users and for the tickets', async () => {
      const user = userEvent.setup();
      renderPage('c1', { ...newCompaniesState(), clients: [], tickets: [] });

      expect((await screen.findByText('This company has no users yet.')).closest('td')).toHaveAttribute('colspan', '5');
      await user.click(ticketsTab());
      expect((await screen.findByText('No tickets found.')).closest('td')).toHaveAttribute('colspan', '7');
    });
  });

  describe('what it used to get wrong', () => {
    it('closes the question and shows why when switching the company off is refused, instead of leaving the question over the reason', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.click(screen.getByLabelText('Active'));
      await user.click(screen.getByRole('button', { name: 'Save' }));
      server.refuse('PUT', /companies\/c1$/, 400, { message: 'A company with open tickets cannot be switched off.' });

      await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Deactivate' }));

      expect(await screen.findByText('A company with open tickets cannot be switched off.')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(editToggle()).toHaveTextContent('Close');
    });

    it('says so when the tickets could not be loaded, instead of "No tickets found."', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      server.refuse('GET', /^\/api\/tickets$/, 500, {}, -1);

      await user.click(ticketsTab());

      expect(await screen.findByText('The tickets could not be loaded. Try again in a moment.')).toBeInTheDocument();
      expect(screen.queryByText('No tickets found.')).not.toBeInTheDocument();
    });

    it('shows the tickets of the client that was chosen last even when the answer for the one before arrives last', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      const early = server.hold('GET', /^\/api\/tickets$/);
      await user.click(ticketsTab());

      await user.selectOptions(screen.getByRole('combobox'), 'k2');
      await waitFor(() => expect(bodyRows().map((r) => cellsOf(r)[1])).toEqual(['Monitor treperi']));
      early.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });

      expect(bodyRows().map((r) => cellsOf(r)[1])).toEqual(['Monitor treperi']);
    });

    it('says that the tickets are on their way, and not that there are none', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      const held = server.hold('GET', /^\/api\/tickets$/);

      await user.click(ticketsTab());

      expect(screen.getByRole('status', { name: 'Loading the tickets' })).toBeInTheDocument();
      expect(screen.queryByText('No tickets found.')).not.toBeInTheDocument();
      held.release();
      await screen.findByText('Printer ne radi');
    });

    it('shows the company, and says why the users are missing, when only the users could not be loaded', async () => {
      renderPage('c1', newCompaniesState(), (s) => s.refuse('GET', /^\/api\/users$/, 500, {}, -1));

      expect(await screen.findByRole('heading', { name: 'Acme d.o.o.' })).toBeInTheDocument();
      expect(await screen.findByText('The users could not be loaded. Try again in a moment.')).toBeInTheDocument();
      expect(screen.queryByText('No matching users.')).not.toBeInTheDocument();
      expect(usersTab().textContent).toBe('Users (…)');
    });

    it('starts again from the users, with nothing searched for and the editing closed, when another company is opened', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await user.type(screen.getByPlaceholderText('Search by name or email…'), 'mia');
      await openEditor(user);
      await user.click(ticketsTab());
      await screen.findByText('Printer ne radi');
      await user.selectOptions(screen.getByRole('combobox'), 'k1');

      await user.click(screen.getByRole('button', { name: 'go to Beta' }));

      expect(await screen.findByRole('heading', { name: 'Beta' })).toBeInTheDocument();
      expect(await screen.findByText('Pero Perić')).toBeInTheDocument();
      expect(usersTab()).toHaveTextContent('Users (1)');
      expect(screen.getByPlaceholderText('Search by name or email…')).toHaveValue('');
      expect(editToggle()).toHaveTextContent('Edit company details');
      expect(screen.getByLabelText('Name')).toHaveValue('Beta');
      await user.click(ticketsTab());
      await screen.findByText('Tuđa firma');
      expect(lastTicketParams()).toEqual({ companyId: 'c2', clientId: undefined, pageSize: 100 });
    });

    it('shows what was saved, without the spaces, when the editing is opened again', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.clear(screen.getByLabelText('Name'));
      await user.type(screen.getByLabelText('Name'), '  Acme novi  ');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await screen.findByRole('heading', { name: 'Acme novi' });

      await openEditor(user);

      expect(screen.getByLabelText('Name')).toHaveValue('Acme novi');
    });

    it('sends the address and the contact without the spaces around them, and nothing for ones of spaces', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      await user.clear(screen.getByLabelText('Address'));
      await user.type(screen.getByLabelText('Address'), '   ');
      await user.clear(screen.getByLabelText('Contact info'));
      await user.type(screen.getByLabelText('Contact info'), '  033 999  ');

      await user.click(screen.getByRole('button', { name: 'Save' }));

      await screen.findByText('033 999');
      expect(server.sent('PUT', /companies\/c1$/)[0].body).toEqual({ name: 'Acme d.o.o.', contactInfo: '033 999', isActive: true, aiIndexingEnabled: true });
    });

    it('sends that the company does not want its tickets sent to the AI provider when that box is cleared', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      await openEditor(user);
      const box = screen.getByRole('checkbox', { name: /sent to the AI provider/i });
      expect(box).toBeChecked();
      await user.click(box);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      await screen.findByText('Acme d.o.o.');
      expect(server.sent('PUT', /companies\/c1$/)[0].body).toMatchObject({ aiIndexingEnabled: false });
    });

    it('keeps the editing out of reach of the keyboard and of a screen reader while it is closed', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');
      const panel = () => screen.getByLabelText('Name').closest('[inert]');
      expect(panel()).not.toBeNull();
      expect(editToggle()).toHaveAttribute('aria-expanded', 'false');

      await user.click(editToggle());

      expect(panel()).toBeNull();
      expect(editToggle()).toHaveAttribute('aria-expanded', 'true');
    });

    it('says that a company has no users, and not that none match', async () => {
      renderPage('c1', { ...newCompaniesState(), clients: [] });

      expect(await screen.findByText('This company has no users yet.')).toBeInTheDocument();
      expect(screen.queryByText('No matching users.')).not.toBeInTheDocument();
    });

    it('still says that none match when there are users and nothing matches', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');

      await user.type(screen.getByPlaceholderText('Search by name or email…'), 'zzz');

      expect(screen.getByText('No matching users.')).toBeInTheDocument();
      expect(screen.queryByText('This company has no users yet.')).not.toBeInTheDocument();
    });

    it('says how many tickets there are when only the first hundred are shown', async () => {
      const user = userEvent.setup();
      const many = Array.from({ length: 130 }, (_, i) => ticketOf(`m${i}`, String(200000 + i), `Zahtjev ${i}`));
      renderPage('c1', { ...newCompaniesState(), tickets: many });
      await screen.findByText('Mia Marić');

      await user.click(ticketsTab());

      expect(await screen.findByText('Showing the first 100 of 130 tickets.')).toBeInTheDocument();
      expect(bodyRows()).toHaveLength(100);
    });

    it('does not say that when all the tickets are shown', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');

      await user.click(ticketsTab());
      await screen.findByText('Printer ne radi');

      expect(screen.queryByText(/Showing the first/)).not.toBeInTheDocument();
    });

    it('has the users and the tickets as tabs, with the one that is chosen said to be chosen', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Mia Marić');

      expect(screen.getAllByRole('tab').map((t) => [t.textContent, t.getAttribute('aria-selected')])).toEqual([['Users (3)', 'true'], ['Tickets', 'false']]);

      await user.click(screen.getByRole('tab', { name: 'Tickets' }));
      expect(screen.getByRole('tab', { name: 'Tickets' })).toHaveAttribute('aria-selected', 'true');
    });

    it('says that the company is on its way, with the spinner named', async () => {
      const held = { release: () => {} };
      renderPage('c1', newCompaniesState(), (s) => Object.assign(held, s.hold('GET', /companies\/c1$/)));

      expect(screen.getByRole('status', { name: 'Loading the company' })).toBeInTheDocument();
      held.release();
      await screen.findByRole('heading', { name: 'Acme d.o.o.' });
    });
  });
});
