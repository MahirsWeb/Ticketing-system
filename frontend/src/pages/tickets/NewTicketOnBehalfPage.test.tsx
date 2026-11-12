import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import NewTicketOnBehalfPage from './NewTicketOnBehalfPage';
import { lookupsApi } from '../../api/lookups';
import { usersApi } from '../../api/users';
import { ticketsApi } from '../../api/tickets';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { UserSummary } from '../../types';

vi.mock('../../api/lookups', () => ({
  lookupsApi: { helpTopics: vi.fn(), departments: vi.fn(), slaPlans: vi.fn(), ticketCategories: vi.fn(), subBranches: vi.fn() },
}));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn(), lookupByEmail: vi.fn() } }));
vi.mock('../../api/tickets', () => ({ ticketsApi: { createOnBehalf: vi.fn(), uploadAttachments: vi.fn() } }));
vi.mock('../../api/emailIntegration', () => ({ emailIntegrationApi: { getPrefill: vi.fn(), completeTicket: vi.fn() } }));

// The rich text editor is stood in for by a text area and a file field that do what the real one tells the page.
vi.mock('../../components/RichTextEditor', () => ({
  RichTextEditor: ({ ariaLabel, value, onChange, onFilesSelected }: { ariaLabel: string; value: string; onChange: (v: string) => void; onFilesSelected: (f: File[]) => void }) => (
    <div>
      <textarea aria-label={ariaLabel} value={value} onChange={(e) => onChange(e.target.value)} />
      <input type="file" aria-label="attach files" multiple onChange={(e) => onFilesSelected(Array.from(e.target.files ?? []))} />
    </div>
  ),
}));

const admin = { id: 'admin', firstName: 'Ad', lastName: 'Min', role: 'Admin' } as UserSummary;
const staff = { id: 'staff', firstName: 'Dino', lastName: 'Dinić', role: 'Employee', departmentId: 'a', departmentName: 'Branch A' } as UserSummary;

const klijent = { id: 'c1', firstName: 'Klijent', lastName: 'Klijentić', email: 'k@x.test', phoneNumber: '+38761000001', companyId: 'co', companyName: 'Acme' };

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const created = (id = 'new1', number = '100500') => ({ id, ticketNumber: number }) as never;

function GoToSecondEmail() {
  const navigate = useNavigate();
  return <button onClick={() => navigate('/tickets/new-on-behalf?fromEmail=m2')}>go to the second e-mail</button>;
}

function renderPage(address = '/tickets/new-on-behalf') {
  return renderWithQuery(
    <MemoryRouter initialEntries={[address]}>
      <GoToSecondEmail />
      <Routes>
        <Route path="/tickets/new-on-behalf" element={<NewTicketOnBehalfPage />} />
        <Route path="/tickets/:id" element={<p>the ticket page</p>} />
      </Routes>
    </MemoryRouter>
  );
}

/** Finds the client, which brings the form. */
async function findClient(email = 'k@x.test') {
  await userEvent.type(screen.getByPlaceholderText('client@company.com'), email);
  await userEvent.click(screen.getByRole('button', { name: 'Find client' }));
}

async function open() {
  vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);
  renderPage();
  await findClient();
  await screen.findByLabelText('Title *');
}

/** Fills in what has to be filled in by hand, and chooses an assignee (which brings the branch with it for an administrator). */
async function fillIn(user: ReturnType<typeof userEvent.setup>, assignee = 'e2') {
  await user.type(screen.getByLabelText('Title *'), 'Printer ne radi');
  await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');
  await screen.findByRole('option', { name: 'Amra Amić' });
  await user.selectOptions(screen.getByLabelText('Assign to'), assignee);
}

describe('opening a ticket on behalf of a client', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: admin });
    vi.mocked(lookupsApi.helpTopics).mockReset().mockResolvedValue([{ id: 'topic', name: 'Topic' }, { id: 'topic2', name: 'Network' }] as never);
    vi.mocked(lookupsApi.departments).mockReset().mockResolvedValue([
      { id: 'a', name: 'Branch A', email: 'a@x.test', isActive: true },
      { id: 'b', name: 'Branch B', email: 'b@x.test', isActive: true },
    ] as never);
    vi.mocked(lookupsApi.slaPlans).mockReset().mockResolvedValue([{ id: 'sla', name: 'Standard', resolutionTimeHours: 72 }, { id: 'sla2', name: 'Urgent', resolutionTimeHours: 8 }] as never);
    vi.mocked(lookupsApi.ticketCategories).mockReset().mockResolvedValue([{ id: 'cat', name: 'Hardware' }] as never);
    vi.mocked(lookupsApi.subBranches).mockReset().mockImplementation(
      async (departmentId: string) => (departmentId === 'b' ? [{ id: 'sb', departmentId: 'b', name: 'Sub of B', isActive: true }] : []) as never
    );
    vi.mocked(usersApi.list).mockReset().mockResolvedValue([
      { id: 'e1', firstName: 'Berina', lastName: 'Berić', role: 'Employee', departmentId: 'b', subBranchId: 'sb' },
      { id: 'e2', firstName: 'Amra', lastName: 'Amić', role: 'Employee', departmentId: 'a', subBranchId: null },
      { id: 'e3', firstName: 'Dino', lastName: 'Dinić', role: 'Employee', departmentId: 'a', subBranchId: null },
    ] as never);
    vi.mocked(usersApi.lookupByEmail).mockReset();
    vi.mocked(ticketsApi.createOnBehalf).mockReset().mockResolvedValue(created());
    vi.mocked(ticketsApi.uploadAttachments).mockReset().mockResolvedValue([]);
    vi.mocked(emailIntegrationApi.getPrefill).mockReset();
    vi.mocked(emailIntegrationApi.completeTicket).mockReset().mockResolvedValue(undefined);
  });

  describe('finding the client', () => {
    it('shows who was found, and the form only after somebody was', async () => {
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);
      renderPage();
      expect(screen.queryByLabelText('Title *')).not.toBeInTheDocument();

      await findClient();

      expect(await screen.findByText('Klijent Klijentić')).toBeInTheDocument();
      expect(screen.getByText('Acme')).toBeInTheDocument();
      expect(screen.getByText('+38761000001')).toBeInTheDocument();
      expect(vi.mocked(usersApi.lookupByEmail).mock.calls).toEqual([['k@x.test']]);
      expect(await screen.findByLabelText('Title *')).toBeInTheDocument();
    });

    it('says plainly when there is no company or phone number on file', async () => {
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue({ ...klijent, companyName: null, phoneNumber: null } as never);
      renderPage();

      await findClient();

      expect(await screen.findByText('No company on file')).toBeInTheDocument();
      expect(screen.getByText('No phone number on file')).toBeInTheDocument();
    });

    it('says what the server said when nobody was found, and plainly when it said nothing, and takes a client that was found away', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.lookupByEmail).mockResolvedValueOnce(klijent as never).mockRejectedValueOnce({ response: { data: { message: 'That account is not a client.' } } }).mockRejectedValueOnce(new Error('network'));
      renderPage();
      await findClient();
      await screen.findByLabelText('Title *');

      await user.clear(screen.getByPlaceholderText('client@company.com'));
      await user.type(screen.getByPlaceholderText('client@company.com'), 'x@x.test');
      await user.click(screen.getByRole('button', { name: 'Find client' }));
      expect(await screen.findByText('That account is not a client.')).toBeInTheDocument();
      expect(screen.queryByLabelText('Title *')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Find client' }));
      expect(await screen.findByText('No client found with that email.')).toBeInTheDocument();
    });

    it('cannot be asked twice at once', async () => {
      const pending = deferred<never>();
      vi.mocked(usersApi.lookupByEmail).mockReturnValue(pending.promise);
      renderPage();

      await findClient();

      expect(screen.getByRole('button', { name: 'Looking…' })).toBeDisabled();
      await act(async () => pending.resolve(klijent as never));
      expect(await screen.findByRole('button', { name: 'Find client' })).toBeEnabled();
    });
  });

  describe('starting from an e-mail', () => {
    const prefill = { messageId: 'm1', clientEmail: 'k@x.test', subject: 'Printer ne radi', bodyHtml: '<p>Od klijenta</p>', receivedAtUtc: '2026-11-05T08:00:00Z', hasAttachments: true };

    it('takes the title, the description, the source and the client from the e-mail, and says so', async () => {
      vi.mocked(emailIntegrationApi.getPrefill).mockResolvedValue(prefill);
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);

      renderPage('/tickets/new-on-behalf?fromEmail=m1');

      expect(await screen.findByDisplayValue('Printer ne radi')).toBeInTheDocument();
      expect(screen.getByRole('textbox', { name: 'Description' })).toHaveValue('<p>Od klijenta</p>');
      expect(screen.getByLabelText('Ticket source')).toHaveValue('Email');
      expect(screen.getByDisplayValue('k@x.test')).toBeInTheDocument();
      expect(await screen.findByText('Klijent Klijentić')).toBeInTheDocument();
      expect(screen.getByText(/pulled from the marked email/)).toBeInTheDocument();
      expect(screen.getByText(/Its attachments will be added to the ticket automatically/)).toBeInTheDocument();
      expect(vi.mocked(emailIntegrationApi.getPrefill).mock.calls).toEqual([['m1']]);
      // asked once, with the means of cancelling it when the page is left
      expect(vi.mocked(usersApi.lookupByEmail).mock.calls).toEqual([['k@x.test', expect.any(AbortSignal)]]);
    });

    it('does not promise attachments when the e-mail has none', async () => {
      vi.mocked(emailIntegrationApi.getPrefill).mockResolvedValue({ ...prefill, hasAttachments: false });
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);

      renderPage('/tickets/new-on-behalf?fromEmail=m1');

      await screen.findByText(/pulled from the marked email/);
      expect(screen.queryByText(/attachments will be added/)).not.toBeInTheDocument();
    });

    it('says that the client has to be searched for by hand when no account was found for the sender', async () => {
      vi.mocked(emailIntegrationApi.getPrefill).mockResolvedValue(prefill);
      vi.mocked(usersApi.lookupByEmail).mockRejectedValue(new Error('not found'));

      renderPage('/tickets/new-on-behalf?fromEmail=m1');

      expect(await screen.findByText('Could not automatically find a client account for this email — search manually below.')).toBeInTheDocument();
      expect(screen.queryByLabelText('Title *')).not.toBeInTheDocument();
    });
  });

  describe('what is chosen at the start', () => {
    it('takes the first entry of each list as the starting choice, once the lists have arrived', async () => {
      await open();

      expect(await screen.findByDisplayValue('Branch A')).toBeInTheDocument(); // the admin's first branch
      expect(screen.getByDisplayValue('Topic')).toBeInTheDocument();
      expect(screen.getByDisplayValue(/Standard/)).toBeInTheDocument(); // the option also shows the hours
      expect(screen.getByRole('option', { name: 'Standard (72h resolution)' })).toBeInTheDocument();
      expect(screen.getByLabelText('Ticket source')).toHaveValue('Phone');
      expect(screen.getByLabelText('Priority')).toHaveValue('Medium');
      expect(screen.getByLabelText('Category (internal, staff only)')).toHaveValue('');
    });

    it('keeps what the person chose when the lists arrive again', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Topic');

      await user.selectOptions(screen.getByLabelText('Help topic'), 'topic2');

      expect(screen.getByLabelText('Help topic')).toHaveValue('topic2');
    });

    it('gives staff who are not administrators their own branch, which they cannot change, and only their colleagues to assign to', async () => {
      useAuthStore.setState({ accessToken: 't', user: staff });
      await open();

      expect(await screen.findByText('Branch A', { selector: 'div' })).toBeInTheDocument();
      expect(screen.queryByLabelText('Branch')).not.toBeInTheDocument(); // not a choice for them
      const assign = screen.getByLabelText('Assign to');
      await waitFor(() => expect(within(assign).getByRole('option', { name: 'Amra Amić' })).toBeInTheDocument());
      expect(within(assign).queryByRole('option', { name: 'Berina Berić' })).not.toBeInTheDocument();
    });

    it('does not move staff to another branch when they choose somebody to assign to', async () => {
      const user = userEvent.setup();
      useAuthStore.setState({ accessToken: 't', user: staff });
      await open();
      await screen.findByRole('option', { name: 'Amra Amić' });

      await user.selectOptions(screen.getByLabelText('Assign to'), 'e3');

      expect(screen.getByLabelText('Assign to')).toHaveValue('e3');
      expect(screen.getByText('Branch A', { selector: 'div' })).toBeInTheDocument();
    });

    it('says that there is no branch when a member of the staff has none', async () => {
      useAuthStore.setState({ accessToken: 't', user: { ...staff, departmentId: null, departmentName: null } as never });

      await open();

      expect(await screen.findByText('No branch assigned')).toBeInTheDocument();
    });
  });

  describe('the branch, the sub-branch and who gets the ticket', () => {
    it("carries the page to the assignee's branch and sub-branch, and drops the sub-branch for a branch that has none", async () => {
      await open();
      await screen.findByDisplayValue('Branch A');
      await screen.findByRole('option', { name: 'Berina Berić' });

      await userEvent.selectOptions(screen.getByLabelText('Assign to'), 'e1');

      expect(await screen.findByDisplayValue('Branch B')).toBeInTheDocument();
      expect(await screen.findByLabelText('Sub-branch')).toHaveValue('sb');

      await userEvent.selectOptions(screen.getByLabelText('Assign to'), 'e2');

      expect(await screen.findByDisplayValue('Branch A')).toBeInTheDocument();
      expect(screen.queryByLabelText('Sub-branch')).not.toBeInTheDocument();
    });

    it('keeps the assignee when the branch that is chosen is the branch they are in, and lets go of them when it is another', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      await screen.findByRole('option', { name: 'Amra Amić' });
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e2');

      await user.selectOptions(screen.getByLabelText('Branch'), 'a');
      expect(screen.getByLabelText('Assign to')).toHaveValue('e2');

      await user.selectOptions(screen.getByLabelText('Branch'), 'b');
      expect(screen.getByLabelText('Assign to')).toHaveValue('');
    });

    it('shows the sub-branches of the branch that is chosen, and none for a branch that has none', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      expect(screen.queryByLabelText('Sub-branch')).not.toBeInTheDocument();

      await user.selectOptions(screen.getByLabelText('Branch'), 'b');

      expect(await screen.findByLabelText('Sub-branch')).toHaveValue('');
      expect(within(screen.getByLabelText('Sub-branch')).getByRole('option', { name: 'Sub of B' })).toBeInTheDocument();
    });

    it('lists the people to assign to under the name of their branch', async () => {
      await open();
      const assign = await screen.findByLabelText('Assign to');
      await waitFor(() => expect(within(assign).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Branch A', 'Branch B']));
    });
  });

  describe('sending it', () => {
    it('wants a title and a description', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');

      fireEvent.submit(screen.getByRole('button', { name: 'Create & open ticket' }).closest('form')!);

      expect(await screen.findByText('Title and description are required.')).toBeInTheDocument();
      await user.type(screen.getByLabelText('Title *'), '   ');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p> </p>');
      fireEvent.submit(screen.getByRole('button', { name: 'Create & open ticket' }).closest('form')!);
      expect(await screen.findByText('Title and description are required.')).toBeInTheDocument();
      expect(ticketsApi.createOnBehalf).not.toHaveBeenCalled();
    });

    it('wants a description even when there is a title, and a title even when there is a description', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      const form = () => screen.getByRole('button', { name: 'Create & open ticket' }).closest('form')!;

      await user.type(screen.getByLabelText('Title *'), 'Printer ne radi');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p> </p>');
      fireEvent.submit(form());
      expect(await screen.findByText('Title and description are required.')).toBeInTheDocument();

      await user.clear(screen.getByLabelText('Title *'));
      await user.clear(screen.getByRole('textbox', { name: 'Description' }));
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');
      fireEvent.submit(form());
      expect(screen.getByText('Title and description are required.')).toBeInTheDocument();
      expect(ticketsApi.createOnBehalf).not.toHaveBeenCalled();
    });

    it('starts staff in their own branch, so that they can send the ticket without choosing one', async () => {
      const user = userEvent.setup();
      useAuthStore.setState({ accessToken: 't', user: staff });
      await open();
      await user.type(await screen.findByLabelText('Title *'), 'Printer ne radi');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');
      await screen.findByRole('option', { name: 'Amra Amić' });
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e2');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      await screen.findByText('the ticket page');
      expect(vi.mocked(ticketsApi.createOnBehalf).mock.calls[0][0]).toMatchObject({ departmentId: 'a', assignedToUserId: 'e2' });
    });

    it('wants everybody to be chosen, except for the due date', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      await user.type(screen.getByLabelText('Title *'), 'Printer ne radi');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText('All ticket fields except due date are required.')).toBeInTheDocument();
      expect(ticketsApi.createOnBehalf).not.toHaveBeenCalled();
    });

    it('wants a sub-branch when the branch has some', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      await user.type(screen.getByLabelText('Title *'), 'Printer ne radi');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');
      await user.selectOptions(screen.getByLabelText('Branch'), 'b');
      await screen.findByLabelText('Sub-branch');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e1');
      await user.selectOptions(screen.getByLabelText('Sub-branch'), '');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText('Please select a sub-branch.')).toBeInTheDocument();
      expect(ticketsApi.createOnBehalf).not.toHaveBeenCalled();
    });

    it('creates the ticket with everything that was chosen, and opens it', async () => {
      const user = userEvent.setup();
      await open();
      await fillIn(user);
      await user.selectOptions(screen.getByLabelText('Ticket source'), 'Email');
      await user.selectOptions(screen.getByLabelText('Help topic'), 'topic2');
      await user.selectOptions(screen.getByLabelText(/SLA plan/), 'sla2');
      await user.selectOptions(screen.getByLabelText('Priority'), 'High');
      await user.selectOptions(screen.getByLabelText('Category (internal, staff only)'), 'cat');
      fireEvent.change(screen.getByLabelText('Due date (optional)'), { target: { value: '2026-12-24' } });

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
      const [payload, key] = vi.mocked(ticketsApi.createOnBehalf).mock.calls[0];
      expect(payload).toEqual({
        clientEmail: 'k@x.test',
        title: 'Printer ne radi',
        description: '<p>Ne stampa</p>',
        source: 'Email',
        helpTopicId: 'topic2',
        departmentId: 'a',
        subBranchId: undefined,
        slaPlanId: 'sla2',
        priority: 'High',
        dueDateUtc: new Date('2026-12-24T23:59:59').toISOString(),
        assignedToUserId: 'e2',
        categoryId: 'cat',
      });
      expect(typeof key).toBe('string');
      expect(key!.length).toBeGreaterThan(8);
    });

    it('sends no due date and no category when none were chosen, and the sub-branch when there is one', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      await user.type(screen.getByLabelText('Title *'), 'Printer ne radi');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');
      await screen.findByRole('option', { name: 'Berina Berić' });
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e1');
      await screen.findByLabelText('Sub-branch');
      await waitFor(() => expect(screen.getByLabelText('Sub-branch')).toHaveValue('sb'));

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      await screen.findByText('the ticket page');
      expect(vi.mocked(ticketsApi.createOnBehalf).mock.calls[0][0]).toMatchObject({ departmentId: 'b', subBranchId: 'sb', dueDateUtc: null, categoryId: undefined, assignedToUserId: 'e1' });
    });

    it('says what the server said when it refuses, and plainly when it said nothing, and can be tried again', async () => {
      const user = userEvent.setup();
      vi.mocked(ticketsApi.createOnBehalf).mockRejectedValueOnce({ response: { data: { message: 'That branch has no mailbox.' } } }).mockRejectedValueOnce(new Error('network'));
      await open();
      await fillIn(user);

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
      expect(await screen.findByText('That branch has no mailbox.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Create & open ticket' })).toBeEnabled();

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));
      expect(await screen.findByText('Could not create the ticket.')).toBeInTheDocument();
      expect(screen.queryByText('the ticket page')).not.toBeInTheDocument();
    });

    it('cannot be sent twice at once', async () => {
      const user = userEvent.setup();
      const pending = deferred<never>();
      vi.mocked(ticketsApi.createOnBehalf).mockReturnValue(pending.promise);
      await open();
      await fillIn(user);

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(screen.getByRole('button', { name: 'Creating…' })).toBeDisabled();
      await act(async () => pending.resolve(created()));
      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
    });
  });

  describe('the files', () => {
    const attach = (...names: string[]) => userEvent.upload(screen.getByLabelText('attach files'), names.map((n) => new File(['x'], n)));

    it('are listed as they are added, can be taken away, and go up with the ticket', async () => {
      const user = userEvent.setup();
      await open();
      await fillIn(user);
      await attach('a.pdf', 'b.png');
      expect(screen.getByText('a.pdf')).toBeInTheDocument();
      await user.click(within(screen.getByText('a.pdf').closest('li')!).getByRole('button', { name: '×' }));
      expect(screen.queryByText('a.pdf')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
      const [ticketId, sent] = vi.mocked(ticketsApi.uploadAttachments).mock.calls[0];
      expect(ticketId).toBe('new1');
      expect(sent.map((f) => f.name)).toEqual(['b.png']);
    });

    it('add up when they are added in more than one go', async () => {
      const user = userEvent.setup();
      await open();
      await fillIn(user);

      await attach('a.pdf');
      await attach('b.png');

      expect(screen.getByText('a.pdf')).toBeInTheDocument();
      expect(screen.getByText('b.png')).toBeInTheDocument();
    });

    it('do not make a second ticket when they could not be added: the same key, only the part that failed again', async () => {
      const user = userEvent.setup();
      vi.mocked(ticketsApi.uploadAttachments).mockRejectedValueOnce({ response: { data: { message: 'The file is too large.' } } });
      await open();
      await fillIn(user);
      await attach('big.pdf');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText(/Ticket #100500 was created, but its files could not be added \(The file is too large\.\)/)).toBeInTheDocument();
      expect(screen.getByText(/the ticket will not be created twice/)).toBeInTheDocument();
      expect(screen.queryByText('the ticket page')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
      const [first, second] = vi.mocked(ticketsApi.createOnBehalf).mock.calls;
      expect(second[1]).toBe(first[1]); // the same key, so the server returns the ticket it made
      expect(ticketsApi.uploadAttachments).toHaveBeenCalledTimes(2);
    });

    it('say plainly that the upload failed when the server gave no reason', async () => {
      const user = userEvent.setup();
      vi.mocked(ticketsApi.uploadAttachments).mockRejectedValueOnce(new Error('network'));
      await open();
      await fillIn(user);
      await attach('a.pdf');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText(/its files could not be added \(the upload failed\)/)).toBeInTheDocument();
    });

    it('sends the e-mail with the ticket, in ONE request, and does not ask for the e-mail to be marked in a second one (TS-31.1)', async () => {
      const user = userEvent.setup();
      vi.mocked(emailIntegrationApi.getPrefill).mockResolvedValue({ messageId: 'm1', clientEmail: 'k@x.test', subject: 'Printer ne radi', bodyHtml: '<p>Od klijenta</p>', receivedAtUtc: '2026-11-05T08:00:00Z', hasAttachments: false });
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);
      renderPage('/tickets/new-on-behalf?fromEmail=m1');
      await screen.findByLabelText('Title *');
      await screen.findByRole('option', { name: 'Amra Amić' });
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e2');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText('the ticket page')).toBeInTheDocument();
      expect(vi.mocked(ticketsApi.createOnBehalf).mock.calls[0][0]).toMatchObject({ emailMessageId: 'm1' });
      expect(emailIntegrationApi.completeTicket).not.toHaveBeenCalled(); // the server saved the ticket, the e-mail's files and the mark as one
    });

    it('says why, and makes no ticket page, when the server could not take the ticket from the e-mail', async () => {
      const user = userEvent.setup();
      vi.mocked(ticketsApi.createOnBehalf).mockRejectedValueOnce({ response: { data: { message: 'The mailbox could not be reached right now.' } } });
      vi.mocked(emailIntegrationApi.getPrefill).mockResolvedValue({ messageId: 'm1', clientEmail: 'k@x.test', subject: 'Printer ne radi', bodyHtml: '<p>Od klijenta</p>', receivedAtUtc: '2026-11-05T08:00:00Z', hasAttachments: false });
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);
      renderPage('/tickets/new-on-behalf?fromEmail=m1');
      await screen.findByLabelText('Title *');
      await screen.findByRole('option', { name: 'Amra Amić' });
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e2');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      expect(await screen.findByText(/mailbox could not be reached/)).toBeInTheDocument();
      expect(screen.queryByText('the ticket page')).not.toBeInTheDocument();
    });
  });

  // What the page used to leave unsaid.
  describe('when something cannot be had', () => {
    const prefill = { messageId: 'm1', clientEmail: 'k@x.test', subject: 'Printer ne radi', bodyHtml: '<p>Od klijenta</p>', receivedAtUtc: '2026-11-05T08:00:00Z', hasAttachments: false };

    it('says that the staff could not be loaded, instead of offering nobody to assign to without a word', async () => {
      vi.mocked(usersApi.list).mockRejectedValue(new Error('network'));

      await open();

      expect(await screen.findByText(/The staff could not be loaded/)).toBeInTheDocument();
    });

    it('says that the e-mail could not be loaded, and still lets the client be searched for by hand', async () => {
      vi.mocked(emailIntegrationApi.getPrefill).mockRejectedValue(new Error('network'));

      renderPage('/tickets/new-on-behalf?fromEmail=m1');

      expect(await screen.findByText(/The e-mail could not be loaded/)).toBeInTheDocument();
      expect(screen.getByPlaceholderText('client@company.com')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Find client' })).toBeEnabled();
    });

    it('says that something is on its way while the e-mail is being loaded, and not that nothing was pulled from it', async () => {
      vi.mocked(emailIntegrationApi.getPrefill).mockReturnValue(new Promise(() => {}));

      renderPage('/tickets/new-on-behalf?fromEmail=m1');

      expect(await screen.findByText(/Loading the e-mail/)).toBeInTheDocument();
      expect(screen.queryByText(/pulled from the marked email/)).not.toBeInTheDocument();
    });

    it('does not mark the e-mail as turned into a ticket, and does not ask for one, when the page was not opened from an e-mail', async () => {
      const user = userEvent.setup();
      await open();
      await fillIn(user);

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      await screen.findByText('the ticket page');
      expect(emailIntegrationApi.completeTicket).not.toHaveBeenCalled();
      expect(emailIntegrationApi.getPrefill).not.toHaveBeenCalled();
    });

    it('takes the e-mail the page is opened for now, and not the one it was opened for before', async () => {
      const user = userEvent.setup();
      vi.mocked(emailIntegrationApi.getPrefill).mockImplementation(async (id) => ({ ...prefill, messageId: id, subject: id === 'm1' ? 'Prvi' : 'Drugi' }));
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);
      renderPage('/tickets/new-on-behalf?fromEmail=m1');
      expect(await screen.findByDisplayValue('Prvi')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'go to the second e-mail' }));

      expect(await screen.findByDisplayValue('Drugi')).toBeInTheDocument();
      expect(screen.queryByDisplayValue('Prvi')).not.toBeInTheDocument();
    });

    it('says that some of the lists could not be loaded when the categories could not', async () => {
      vi.mocked(lookupsApi.ticketCategories).mockRejectedValue(new Error('network'));

      await open();

      expect(await screen.findByText(/Some of the lists could not be loaded/)).toBeInTheDocument();
    });

    it('says that some of the lists could not be loaded, instead of showing an empty list as if it were the whole truth', async () => {
      vi.mocked(lookupsApi.helpTopics).mockRejectedValue(new Error('network'));

      await open();

      expect(await screen.findByText(/Some of the lists could not be loaded/)).toBeInTheDocument();
    });

    it('sends the title without the spaces around it', async () => {
      const user = userEvent.setup();
      await open();
      await screen.findByDisplayValue('Branch A');
      await user.type(screen.getByLabelText('Title *'), '   Printer ne radi  ');
      await user.type(screen.getByRole('textbox', { name: 'Description' }), '<p>Ne stampa</p>');
      await screen.findByRole('option', { name: 'Amra Amić' });
      await user.selectOptions(screen.getByLabelText('Assign to'), 'e2');

      await user.click(screen.getByRole('button', { name: 'Create & open ticket' }));

      await screen.findByText('the ticket page');
      expect(vi.mocked(ticketsApi.createOnBehalf).mock.calls[0][0].title).toBe('Printer ne radi');
    });

    it('does not take a prefill for the e-mail that was opened before this one when the page is opened for another e-mail', async () => {
      const first = deferred<typeof prefill>();
      vi.mocked(emailIntegrationApi.getPrefill).mockImplementation((id) => (id === 'm1' ? first.promise : Promise.resolve({ ...prefill, messageId: 'm2', subject: 'Skener ne skenira' })));
      vi.mocked(usersApi.lookupByEmail).mockResolvedValue(klijent as never);
      const view = renderPage('/tickets/new-on-behalf?fromEmail=m1');
      view.unmount();

      renderPage('/tickets/new-on-behalf?fromEmail=m2');
      expect(await screen.findByDisplayValue('Skener ne skenira')).toBeInTheDocument();
      await act(async () => first.resolve(prefill));

      expect(screen.getByDisplayValue('Skener ne skenira')).toBeInTheDocument();
      expect(screen.queryByDisplayValue('Printer ne radi')).not.toBeInTheDocument();
    });
  });
});
