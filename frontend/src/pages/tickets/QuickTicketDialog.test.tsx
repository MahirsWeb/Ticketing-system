import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QuickTicketDialog } from './QuickTicketDialog';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { UserSummary } from '../../types';

vi.mock('../../api/lookups', () => ({
  lookupsApi: { companies: vi.fn(), helpTopics: vi.fn(), departments: vi.fn(), slaPlans: vi.fn(), ticketCategories: vi.fn(), subBranches: vi.fn() },
}));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn(), lookupByEmail: vi.fn(), companyClientChoices: vi.fn() } }));
vi.mock('../../api/tickets', () => ({ ticketsApi: { createOnBehalf: vi.fn(), uploadAttachments: vi.fn() } }));
vi.mock('../../api/emailIntegration', () => ({ emailIntegrationApi: { getPrefill: vi.fn(), completeTicket: vi.fn() } }));

// The rich text editor is stood in for by a text area and a file field that do what the real one tells the form.
vi.mock('../../components/RichTextEditor', () => ({
  RichTextEditor: ({ ariaLabel, value, onChange, onFilesSelected }: { ariaLabel: string; value: string; onChange: (v: string) => void; onFilesSelected: (f: File[]) => void }) => (
    <div>
      <textarea aria-label={ariaLabel} value={value} onChange={(e) => onChange(e.target.value)} />
      <input type="file" aria-label="attach files" multiple onChange={(e) => onFilesSelected(Array.from(e.target.files ?? []))} />
    </div>
  ),
}));

const admin = { id: 'admin', firstName: 'Ad', lastName: 'Min', role: 'Admin' } as UserSummary;

function renderDialog(onClose = vi.fn()) {
  const view = renderWithQuery(
    <MemoryRouter initialEntries={['/tasks']}>
      <Routes>
        <Route path="/tasks" element={<QuickTicketDialog onClose={onClose} />} />
        <Route path="/tickets/:id" element={<p>the ticket page</p>} />
      </Routes>
    </MemoryRouter>
  );
  return { ...view, onClose };
}

async function chooseClient(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('combobox', { name: 'Company' }));
  await user.click(await screen.findByRole('option', { name: 'Acme' }));
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Company employee' })).toBeEnabled());
  await user.click(screen.getByRole('combobox', { name: 'Company employee' }));
  await user.click(await screen.findByRole('option', { name: /^Klijent Klijentić/ }));
  await screen.findByLabelText('Title *');
}

async function fillIn(user: ReturnType<typeof userEvent.setup>, title = 'Printer ne radi') {
  await user.type(screen.getByLabelText('Title *'), title);
  await user.type(screen.getByRole('textbox', { name: 'Description' }), 'Ne stampa');
  await screen.findByRole('option', { name: 'Amra Amić' });
  await user.selectOptions(screen.getByLabelText('Assign to'), 'e2');
}

// A ticket opened for a client from any page, in a dialog, so that the person who works on something else does not leave it.
describe('the dialog for a new ticket', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: admin });
    vi.mocked(lookupsApi.companies).mockReset().mockResolvedValue([{ id: 'co', name: 'Acme', isActive: true }] as never);
    vi.mocked(usersApi.companyClientChoices).mockReset().mockResolvedValue([{ id: 'c1', name: 'Klijent Klijentić', email: 'k@x.test' }]);
    vi.mocked(lookupsApi.helpTopics).mockReset().mockResolvedValue([{ id: 'topic', name: 'Topic' }] as never);
    vi.mocked(lookupsApi.departments).mockReset().mockResolvedValue([{ id: 'a', name: 'Branch A', email: 'a@x.test', isActive: true }] as never);
    vi.mocked(lookupsApi.slaPlans).mockReset().mockResolvedValue([{ id: 'sla', name: 'Standard', resolutionTimeHours: 72 }] as never);
    vi.mocked(lookupsApi.ticketCategories).mockReset().mockResolvedValue([] as never);
    vi.mocked(lookupsApi.subBranches).mockReset().mockResolvedValue([] as never);
    vi.mocked(usersApi.list).mockReset().mockResolvedValue([{ id: 'e2', firstName: 'Amra', lastName: 'Amić', role: 'Employee', departmentId: 'a', subBranchId: null }] as never);
    vi.mocked(ticketsApi.createOnBehalf).mockReset().mockResolvedValue({ id: 'new1', ticketNumber: '100500' } as never);
    vi.mocked(ticketsApi.uploadAttachments).mockReset().mockResolvedValue([]);
  });

  it('is a named dialog that starts from the two boxes and nothing else, and closes at once when nothing was typed', async () => {
    const { onClose } = renderDialog();
    const user = userEvent.setup();

    expect(screen.getByRole('dialog', { name: 'New ticket' })).toBeInTheDocument();
    expect(await screen.findByRole('combobox', { name: 'Company' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Title *')).not.toBeInTheDocument();

    await user.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Discard what you entered?')).not.toBeInTheDocument();
  });

  it('asks before it throws away what was typed, on Escape and on the button, and keeps it when the person says no', async () => {
    const { onClose } = renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await user.type(screen.getByLabelText('Title *'), 'Printer ne radi');

    await user.keyboard('{Escape}');
    expect(await screen.findByText('Discard what you entered?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();

    await user.keyboard('{Escape}'); // Escape in the question answers the question: no, keep it
    expect(screen.queryByText('Discard what you entered?')).not.toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Title *')).toHaveValue('Printer ne radi');

    await user.click(screen.getByRole('button', { name: 'Close' }));
    expect(await screen.findByText('Discard what you entered?')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Discard' }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('counts a file that was added as something that would be lost', async () => {
    const { onClose } = renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await user.upload(screen.getByLabelText('attach files'), new File(['x'], 'log.txt', { type: 'text/plain' }));

    await user.click(screen.getByRole('button', { name: 'Close' }));

    expect(await screen.findByText('Discard what you entered?')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('makes the ticket for the person that was chosen, and the person stays where they were: no page change, a message, a link', async () => {
    const { onClose } = renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await fillIn(user);

    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

    expect(await screen.findByText('Ticket #100500 was created.')).toBeInTheDocument();
    expect(vi.mocked(ticketsApi.createOnBehalf).mock.calls[0][0]).toMatchObject({ clientId: 'c1', title: 'Printer ne radi', assignedToUserId: 'e2' });
    expect(screen.queryByText('the ticket page')).not.toBeInTheDocument(); // not taken to the ticket
    expect(screen.getByRole('link', { name: 'Open ticket #100500' })).toHaveAttribute('href', '/tickets/new1');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('does not ask "discard?" any more once the ticket is made', async () => {
    const { onClose } = renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
    await screen.findByText('Ticket #100500 was created.');

    await user.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Discard what you entered?')).not.toBeInTheDocument();
  });

  it('closes by itself when the link to the ticket is followed, and the link goes to that ticket', async () => {
    const { onClose } = renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

    await user.click(await screen.findByRole('link', { name: 'Open ticket #100500' }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(await screen.findByText('the ticket page')).toBeInTheDocument();
  });

  it('begins from nothing for the next ticket, with a new key: with the old one the second ticket would quietly be the first again', async () => {
    renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await fillIn(user, 'Prvi');
    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
    await screen.findByText('Ticket #100500 was created.');

    await user.click(screen.getByRole('button', { name: 'Create another' }));

    expect(screen.getByRole('combobox', { name: 'Company' })).toHaveValue('');
    expect(screen.queryByLabelText('Title *')).not.toBeInTheDocument();
    await chooseClient(user);
    expect(screen.getByLabelText('Title *')).toHaveValue('');
    await fillIn(user, 'Drugi');
    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
    await screen.findByText('Ticket #100500 was created.');

    const keys = vi.mocked(ticketsApi.createOnBehalf).mock.calls.map((c) => c[1]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });

  it('has a new key each time it is opened', async () => {
    const user = userEvent.setup();
    const first = renderDialog();
    await chooseClient(user);
    await fillIn(user, 'Prvi');
    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
    await screen.findByText('Ticket #100500 was created.');
    first.unmount();

    renderDialog();
    await chooseClient(user);
    await fillIn(user, 'Drugi');
    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
    await screen.findByText('Ticket #100500 was created.');

    const [a, b] = vi.mocked(ticketsApi.createOnBehalf).mock.calls.map((c) => c[1]);
    expect(a).not.toBe(b);
  });

  it('keeps the form, with what was typed, when the server refuses, so that nothing has to be typed again', async () => {
    vi.mocked(ticketsApi.createOnBehalf).mockRejectedValueOnce({ response: { data: { message: 'This client\'s company is not active.' } } });
    const { onClose } = renderDialog();
    const user = userEvent.setup();
    await chooseClient(user);
    await fillIn(user);

    await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

    expect(await screen.findByText("This client's company is not active.")).toBeInTheDocument();
    expect(screen.getByLabelText('Title *')).toHaveValue('Printer ne radi');
    expect(onClose).not.toHaveBeenCalled();
  });
});
