import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SettingsPage from './SettingsPage';
import { apiClient } from '../../api/client';
import { queryClient } from '../../api/queryClient';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installAdminBackend, newAdminState } from '../../test/fakeAdminApi';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }));

let server: ReturnType<typeof installAdminBackend>;
const originalDefaults = queryClient.getDefaultOptions();

const renderPage = () => renderWithQuery(<SettingsPage />, queryClient);
const openTab = (name: RegExp) => userEvent.click(screen.getByRole('tab', { name }));
/** The row of a list that has this text in it. */
const rowOf = (what: string | HTMLElement) => (typeof what === 'string' ? screen.getByText(what) : what).closest('div[class*="justify-between"]') as HTMLElement;
const dialog = () => screen.getByRole('dialog');
const refused = (message: string) => ({ message });

describe('the settings page', () => {
  beforeEach(() => {
    queryClient.clear();
    // The application tries a request that failed twice more before it gives up (a few seconds); a test would only wait for that.
    queryClient.setDefaultOptions({ queries: { ...originalDefaults.queries, retry: false } });
    server = installAdminBackend(newAdminState());
  });
  afterEach(() => {
    queryClient.clear();
    queryClient.setDefaultOptions(originalDefaults);
    vi.useRealTimers();
  });

  it('opens on the branches, with the other sections one click away', async () => {
    renderPage();

    expect(await screen.findByText('Existing branches (2)')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Branches/ })).toHaveAttribute('aria-selected', 'true');

    await openTab(/Help Topics/);
    expect(await screen.findByText('Existing help topics (2)')).toBeInTheDocument();
    await openTab(/Categories & SLA/);
    expect(await screen.findByText('Existing categories (1)')).toBeInTheDocument();
    expect(screen.getByText('Existing SLA plans (1)')).toBeInTheDocument();
    await openTab(/Notifications/);
    expect(await screen.findByText('Overdue ticket notifications')).toBeInTheDocument();
  });

  it('asks for what is switched off as well, because this is where it is switched back on, and for each section only when it is opened', async () => {
    renderPage();
    await screen.findByText('Existing branches (2)');
    expect(server.sent('GET', /^\/api\/departments$/).map((r) => r.params)).toContainEqual({ includeInactive: true });
    for (const url of [/help-topics/, /ticket-categories/, /sla-plans/]) expect(server.sent('GET', url)).toHaveLength(0);

    await openTab(/Help Topics/);
    await screen.findByText('Existing help topics (2)');
    expect(server.sent('GET', /^\/api\/help-topics$/).map((r) => r.params)).toContainEqual({ includeInactive: true });
    await openTab(/Categories & SLA/);
    await screen.findByText('Existing SLA plans (1)');

    expect(server.sent('GET', /^\/api\/ticket-categories$/).map((r) => r.params)).toContainEqual({ includeInactive: true });
    expect(server.sent('GET', /^\/api\/sla-plans$/).map((r) => r.params)).toContainEqual({ includeInactive: true });
  });

  describe('branches', () => {
    it('lists them with their e-mail and whether they are active, and finds them by name or e-mail', async () => {
      const user = userEvent.setup();
      renderPage();

      const sarajevo = rowOf(await screen.findByText('Sarajevo'));
      expect(within(sarajevo).getByText(/sarajevo@firma.test/)).toBeInTheDocument();
      expect(within(sarajevo).getByText('Active')).toBeInTheDocument();
      expect(within(rowOf('Mostar')).getByText('Inactive')).toBeInTheDocument();

      await user.type(screen.getByPlaceholderText('Search branches…'), 'mostar@');
      expect(screen.queryByText('Sarajevo')).not.toBeInTheDocument();
      expect(screen.getByText('Mostar')).toBeInTheDocument();

      await user.clear(screen.getByPlaceholderText('Search branches…'));
      await user.type(screen.getByPlaceholderText('Search branches…'), 'nothing like this');
      expect(screen.getByText('No branches found.')).toBeInTheDocument();
    });

    it('adds one only when both the name and the e-mail are filled in, with what was typed trimmed, and clears the form', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');
      const add = screen.getByRole('button', { name: '+ Add Branch' });
      expect(add).toBeDisabled();

      await user.type(screen.getByPlaceholderText('e.g. Servis'), '  Tuzla ');
      expect(add).toBeDisabled();
      await user.type(screen.getByPlaceholderText('servis@firma.ba'), ' tuzla@firma.test  ');
      await user.click(add);

      await screen.findByText('Tuzla');
      expect(server.sent('POST', /^\/api\/departments$/).map((r) => r.body)).toEqual([{ name: 'Tuzla', email: 'tuzla@firma.test' }]);
      expect(screen.getByPlaceholderText('e.g. Servis')).toHaveValue('');
      expect(screen.getByPlaceholderText('servis@firma.ba')).toHaveValue('');
      expect(screen.getByText('Existing branches (3)')).toBeInTheDocument();
    });

    it('says what the server said when it refuses one, keeps what was typed, and takes the message away when typing goes on', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');

      await user.type(screen.getByPlaceholderText('e.g. Servis'), 'Sarajevo');
      await user.type(screen.getByPlaceholderText('servis@firma.ba'), 'x@firma.test');
      await user.click(screen.getByRole('button', { name: '+ Add Branch' }));

      expect(await screen.findByText('A branch with that name already exists.')).toBeInTheDocument();
      expect(screen.getByPlaceholderText('e.g. Servis')).toHaveValue('Sarajevo');

      await user.type(screen.getByPlaceholderText('e.g. Servis'), '2');
      expect(screen.queryByText('A branch with that name already exists.')).not.toBeInTheDocument();
    });

    it('says plainly that it could not add one when the server gave no reason', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');
      server.refuse('POST', /^\/api\/departments$/, 500);

      await user.type(screen.getByPlaceholderText('e.g. Servis'), 'Tuzla');
      await user.type(screen.getByPlaceholderText('servis@firma.ba'), 'tuzla@firma.test');
      await user.click(screen.getByRole('button', { name: '+ Add Branch' }));

      expect(await screen.findByText('Could not add branch.')).toBeInTheDocument();
    });

    it('edits the name and the e-mail of one in place, and leaves it alone on Cancel or when a field is emptied', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Edit' }));
      const [name, email] = within(screen.getByText('Existing branches (2)').closest('div')!.parentElement!).getAllByRole('textbox').slice(-2);
      expect(name).toHaveValue('Sarajevo');
      expect(email).toHaveValue('sarajevo@firma.test');
      await user.clear(name);
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(server.sent('PUT', /departments/)).toHaveLength(0); // an empty name is not sent
      await user.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(screen.getByText('Sarajevo')).toBeInTheDocument();

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Edit' }));
      const fields = within(screen.getByText('Existing branches (2)').closest('div')!.parentElement!).getAllByRole('textbox').slice(-2);
      await user.clear(fields[0]);
      await user.type(fields[0], 'Sarajevo Centar');
      await user.clear(fields[1]);
      await user.type(fields[1], 'centar@firma.test');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await screen.findByText('Sarajevo Centar');
      expect(server.sent('PUT', /^\/api\/departments\/d1$/).map((r) => r.body)).toEqual([{ name: 'Sarajevo Centar', email: 'centar@firma.test', isActive: true }]);
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    });

    it('says why an edit was refused and stays in the edit', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');
      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Edit' }));
      const fields = within(screen.getByText('Existing branches (2)').closest('div')!.parentElement!).getAllByRole('textbox').slice(-2);
      await user.clear(fields[0]);
      await user.type(fields[0], 'Mostar');

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('A branch with that name already exists.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    });

    it('asks before deactivating one, changes only its status, and reactivates without asking', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Deactivate' }));
      expect(within(dialog()).getByText('Deactivate this branch?')).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(server.sent('PUT', /departments/)).toHaveLength(0);

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Deactivate' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));
      await waitFor(() => expect(within(rowOf('Sarajevo')).getByText('Inactive')).toBeInTheDocument());
      expect(server.sent('PUT', /^\/api\/departments\/d1$/).map((r) => r.body)).toEqual([{ name: 'Sarajevo', email: 'sarajevo@firma.test', isActive: false }]);

      await user.click(within(rowOf('Mostar')).getByRole('button', { name: 'Reactivate' }));
      await waitFor(() => expect(within(rowOf('Mostar')).getByText('Active')).toBeInTheDocument());
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(server.sent('PUT', /^\/api\/departments\/d2$/).map((r) => r.body)).toEqual([{ name: 'Mostar', email: 'mostar@firma.test', isActive: true }]);
    });

    it('asks before deleting one, deletes it, and says what the server said when it refuses', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Delete' }));
      expect(within(dialog()).getByText('Delete this branch?')).toBeInTheDocument();
      server.refuse('DELETE', /^\/api\/departments\/d1$/, 400, refused('Staff still belong to this branch.'));
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      expect(await within(dialog()).findByText(/Staff still belong to this branch\./)).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(screen.getByText('Sarajevo')).toBeInTheDocument();

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Delete' }));
      expect(within(dialog()).queryByText(/Staff still belong to this branch\./)).not.toBeInTheDocument(); // an old refusal is not shown again
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));
      await waitFor(() => expect(screen.queryByText('Sarajevo')).not.toBeInTheDocument());
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByText('Existing branches (1)')).toBeInTheDocument();
    });

    describe('sub-branches', () => {
      it('are shown under their branch on request, with the inactive ones too, and hidden again', async () => {
        const user = userEvent.setup();
        renderPage();
        await screen.findByText('Sarajevo');

        await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Sub-branches' }));

        expect(await screen.findByText('Centar')).toBeInTheDocument();
        expect(server.sent('GET', /^\/api\/departments\/d1\/sub-branches$/).map((r) => r.params)).toEqual([{ includeInactive: true }]);
        await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Hide sub-branches' }));
        expect(screen.queryByText('Centar')).not.toBeInTheDocument();
      });

      it('say so when a branch has none', async () => {
        const user = userEvent.setup();
        renderPage();
        await screen.findByText('Mostar');

        await user.click(within(rowOf('Mostar')).getByRole('button', { name: 'Sub-branches' }));

        expect(await screen.findByText('No sub-branches — this branch is used as a single unit.')).toBeInTheDocument();
      });

      it('can be added, with the name trimmed, and not when the name is empty', async () => {
        const user = userEvent.setup();
        renderPage();
        await screen.findByText('Sarajevo');
        await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Sub-branches' }));
        await screen.findByText('Centar');
        const add = screen.getByRole('button', { name: 'Add' });
        expect(add).toBeDisabled();

        await user.type(screen.getByPlaceholderText('Sub-branch name…'), '  Ilidža ');
        await user.click(add);

        await screen.findByText('Ilidža');
        expect(server.sent('POST', /sub-branches/).map((r) => r.body)).toEqual([{ name: 'Ilidža' }]);
        expect(screen.getByPlaceholderText('Sub-branch name…')).toHaveValue('');
      });

      it('say why one was refused, and plainly when the server gave no reason', async () => {
        const user = userEvent.setup();
        renderPage();
        await screen.findByText('Sarajevo');
        await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Sub-branches' }));
        await screen.findByText('Centar');

        await user.type(screen.getByPlaceholderText('Sub-branch name…'), 'Centar');
        await user.click(screen.getByRole('button', { name: 'Add' }));
        expect(await screen.findByText('A sub-branch with that name already exists.')).toBeInTheDocument();

        server.refuse('POST', /sub-branches/, 500);
        await user.type(screen.getByPlaceholderText('Sub-branch name…'), '2');
        await user.click(screen.getByRole('button', { name: 'Add' }));
        expect(await screen.findByText('Could not add sub-branch.')).toBeInTheDocument();
      });

      it('are deactivated after a question and reactivated without one', async () => {
        const user = userEvent.setup();
        renderPage();
        await screen.findByText('Sarajevo');
        await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Sub-branches' }));
        await screen.findByText('Centar');

        await user.click(within(rowOf('Centar')).getByRole('button', { name: 'Deactivate' }));
        expect(within(dialog()).getByText('Deactivate this sub-branch?')).toBeInTheDocument();
        await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
        expect(server.sent('PUT', /sub-branches/)).toHaveLength(0);
        await user.click(within(rowOf('Centar')).getByRole('button', { name: 'Deactivate' }));
        await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));

        await waitFor(() => expect(within(rowOf('Centar')).getByText('Inactive')).toBeInTheDocument());
        expect(server.sent('PUT', /^\/api\/sub-branches\/s1$/).map((r) => r.body)).toEqual([{ name: 'Centar', isActive: false }]);
        await user.click(within(rowOf('Centar')).getByRole('button', { name: 'Reactivate' }));
        await waitFor(() => expect(within(rowOf('Centar')).getByText('Active')).toBeInTheDocument());
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      });
    });
  });

  describe.each([
    { tab: /Help Topics/, noun: 'help topic', plural: 'help topics', first: 'Printer', inactive: 'Network', url: /help-topics/, inUse: true },
    { tab: /Categories & SLA/, noun: 'category', plural: 'categories', first: 'Hardware', inactive: null, url: /ticket-categories/, inUse: false },
  ])('the list of $plural', ({ tab, noun, plural, first, inactive, url, inUse }) => {
    const search = () => screen.getByPlaceholderText(`Search ${plural}…`);
    const typeName = (text: string) => userEvent.type(screen.getByPlaceholderText(`Enter ${noun} name…`), text);

    it('shows them with their status, counts them, and finds them by name', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);

      const row = rowOf(await screen.findByText(first));
      expect(within(row).getByText('Active')).toBeInTheDocument();
      if (inactive) expect(within(rowOf(inactive)).getByText('Inactive')).toBeInTheDocument();
      await user.type(search(), 'zzz');
      expect(screen.getByText(`No ${plural} found.`)).toBeInTheDocument();
      await user.clear(search());
      await user.type(search(), first.slice(1, 4).toUpperCase());
      expect(screen.getByText(first)).toBeInTheDocument();
      await user.clear(search());
      await user.type(search(), first.toLowerCase());
      expect(screen.getByText(first)).toBeInTheDocument();
    });

    it('adds one with the name trimmed, only when there is a name, and clears the field', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);
      await screen.findByText(first);
      const add = screen.getByRole('button', { name: `+ Add ${noun}` });
      expect(add).toBeDisabled();

      await typeName('  Brand new ');
      await user.click(add);

      await screen.findByText('Brand new');
      expect(server.sent('POST', url).map((r) => r.body)).toEqual([{ name: 'Brand new' }]);
      expect(screen.getByPlaceholderText(`Enter ${noun} name…`)).toHaveValue('');
    });

    it('says why one was refused, and plainly when the server gave no reason', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);
      await screen.findByText(first);

      await typeName(first);
      await user.click(screen.getByRole('button', { name: `+ Add ${noun}` }));
      expect(await screen.findByText(`A ${noun} with that name already exists.`)).toBeInTheDocument();

      server.refuse('POST', url, 500);
      await typeName('2');
      await user.click(screen.getByRole('button', { name: `+ Add ${noun}` }));
      expect(await screen.findByText('Could not add item.')).toBeInTheDocument();
    });

    it('renames one in place and keeps its status, and leaves it alone on Cancel or an empty name', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);
      await user.click(within(rowOf(await screen.findByText(first))).getByRole('button', { name: 'Rename' }));
      await user.clear(screen.getByDisplayValue(first));
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(server.sent('PUT', url)).toHaveLength(0);
      await user.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(screen.getByText(first)).toBeInTheDocument();

      await user.click(within(rowOf(first)).getByRole('button', { name: 'Rename' }));
      const field = screen.getByDisplayValue(first);
      await user.clear(field);
      await user.type(field, 'Renamed');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await screen.findByText('Renamed');
      expect(server.sent('PUT', url).map((r) => r.body)).toEqual([{ name: 'Renamed', isActive: true }]);
    });

    it('asks before deactivating one and reactivates without asking', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);
      await user.click(within(rowOf(await screen.findByText(first))).getByRole('button', { name: 'Deactivate' }));
      expect(within(dialog()).getByText(`Deactivate "${first}"?`)).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(server.sent('PUT', url)).toHaveLength(0);

      await user.click(within(rowOf(first)).getByRole('button', { name: 'Deactivate' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));

      await waitFor(() => expect(within(rowOf(first)).getByText('Inactive')).toBeInTheDocument());
      expect(server.sent('PUT', url).map((r) => r.body)).toEqual([{ name: first, isActive: false }]);
      await user.click(within(rowOf(first)).getByRole('button', { name: 'Reactivate' }));
      await waitFor(() => expect(within(rowOf(first)).getByText('Active')).toBeInTheDocument());
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('asks before deleting one and deletes it', async () => {
      const user = userEvent.setup();
      server.state.inUse.clear();
      renderPage();
      await openTab(tab);
      await user.click(within(rowOf(await screen.findByText(first))).getByRole('button', { name: 'Delete' }));
      expect(within(dialog()).getByText(`Delete "${first}"?`)).toBeInTheDocument();
      expect(within(dialog()).getByText(/will be permanently deleted/)).toBeInTheDocument();

      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      await waitFor(() => expect(screen.queryByText(first)).not.toBeInTheDocument());
      expect(server.sent('DELETE', url)).toHaveLength(1);
    });

    if (inUse) {
      it('says what the server said when it refuses to delete one that tickets use, and does not repeat that refusal on the next try', async () => {
        const user = userEvent.setup();
        renderPage();
        await openTab(tab);
        await user.click(within(rowOf(await screen.findByText(first))).getByRole('button', { name: 'Delete' }));
        await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

        expect(await within(dialog()).findByText(/is used by tickets and cannot be deleted/)).toBeInTheDocument();
        await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
        await user.click(within(rowOf(first)).getByRole('button', { name: 'Delete' }));

        expect(within(dialog()).queryByText(/is used by tickets/)).not.toBeInTheDocument();
        expect(screen.getByText(first)).toBeInTheDocument();
      });
    }
  });

  describe('SLA plans', () => {
    const hours = () => screen.getByLabelText('Resolution time (hours)');

    it('shows them with their resolution time', async () => {
      renderPage();
      await openTab(/Categories & SLA/);

      expect(await screen.findByText(/72h resolution/)).toBeInTheDocument();
    });

    it('adds one with the hours it was given, 72 when nothing else was chosen, and goes back to 72 afterwards', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Categories & SLA/);
      await screen.findByText('Standard');
      expect(hours()).toHaveValue(72);
      const add = screen.getByRole('button', { name: '+ Add SLA Plan' });
      expect(add).toBeDisabled();

      await user.type(screen.getByPlaceholderText('e.g. Standard'), 'Urgent');
      fireEvent.change(hours(), { target: { value: '24' } });
      await user.click(add);
      await screen.findByText('Urgent');
      expect(hours()).toHaveValue(72);
      await user.type(screen.getByPlaceholderText('e.g. Standard'), 'Relaxed');
      await user.click(screen.getByRole('button', { name: '+ Add SLA Plan' }));
      await screen.findByText('Relaxed');

      expect(server.sent('POST', /sla-plans/).map((r) => r.body)).toEqual([
        { name: 'Urgent', resolutionTimeHours: 24 },
        { name: 'Relaxed', resolutionTimeHours: 72 },
      ]);
    });

    it('says why one was refused, and plainly when the server gave no reason', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Categories & SLA/);
      await screen.findByText('Standard');

      await user.type(screen.getByPlaceholderText('e.g. Standard'), 'Standard');
      await user.click(screen.getByRole('button', { name: '+ Add SLA Plan' }));
      expect(await screen.findByText('A SLA plan with that name already exists.')).toBeInTheDocument();
      server.refuse('POST', /sla-plans/, 500);
      await user.type(screen.getByPlaceholderText('e.g. Standard'), '2');
      await user.click(screen.getByRole('button', { name: '+ Add SLA Plan' }));
      expect(await screen.findByText('Could not add SLA plan.')).toBeInTheDocument();
    });

    it('keeps the hours when a plan is deactivated and reactivated, and asks before deactivating', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Categories & SLA/);
      await user.click(within(rowOf(await screen.findByText('Standard'))).getByRole('button', { name: 'Deactivate' }));
      expect(within(dialog()).getByText('Deactivate "Standard"?')).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));

      await waitFor(() => expect(within(rowOf('Standard')).getByText('Inactive')).toBeInTheDocument());
      expect(server.sent('PUT', /^\/api\/sla-plans\/p1$/).map((r) => r.body)).toEqual([{ name: 'Standard', resolutionTimeHours: 72, isActive: false }]);
      await user.click(within(rowOf('Standard')).getByRole('button', { name: 'Reactivate' }));
      await waitFor(() => expect(within(rowOf('Standard')).getByText('Active')).toBeInTheDocument());
      expect(server.sent('PUT', /^\/api\/sla-plans\/p1$/).at(-1)!.body).toEqual({ name: 'Standard', resolutionTimeHours: 72, isActive: true });
    });

    it('asks before deleting one, says what the server said when it refuses, and deletes it otherwise', async () => {
      const user = userEvent.setup();
      server.state.inUse.add('p1');
      renderPage();
      await openTab(/Categories & SLA/);
      await user.click(within(rowOf(await screen.findByText('Standard'))).getByRole('button', { name: 'Delete' }));
      expect(within(dialog()).getByText(/This SLA plan will be permanently deleted/)).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));
      expect(await within(dialog()).findByText(/is used by tickets and cannot be deleted/)).toBeInTheDocument();

      server.state.inUse.delete('p1');
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      await waitFor(() => expect(screen.queryByText('Standard')).not.toBeInTheDocument());
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  describe('notifications about overdue tickets', () => {
    const days = () => screen.getByPlaceholderText('e.g. 3');
    const toggle = () => screen.getByRole('checkbox', { name: /passes its SLA due date/ });

    it('shows what is set now', async () => {
      renderPage();
      await openTab(/Notifications/);

      expect(await screen.findByText('Overdue ticket notifications')).toBeInTheDocument();
      expect(toggle()).toBeChecked();
      expect(days()).toHaveValue(3);
    });

    it('saves what was changed, and says so', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Overdue ticket notifications');

      await user.click(toggle());
      await user.clear(days());
      await user.type(days(), '5');
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Saved.')).toBeInTheDocument();
      expect(server.sent('PUT', /overdue-notifications/).map((r) => r.body)).toEqual([{ notifyOnSlaBreach: false, manualOverdueDays: 5 }]);
    });

    it('saves an empty number of days as none', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Overdue ticket notifications');

      await user.clear(days());
      await user.click(screen.getByRole('button', { name: 'Save' }));

      await screen.findByText('Saved.');
      expect(server.sent('PUT', /overdue-notifications/).map((r) => r.body)).toEqual([{ notifyOnSlaBreach: true, manualOverdueDays: null }]);
    });

    it('says what the server said when saving fails, and plainly when it said nothing, and takes "Saved." away on the next try', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Overdue ticket notifications');
      await user.click(screen.getByRole('button', { name: 'Save' }));
      await screen.findByText('Saved.');

      server.refuse('PUT', /overdue-notifications/, 400, refused('Days must be between 1 and 365.'));
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('Days must be between 1 and 365.')).toBeInTheDocument();
      expect(screen.queryByText('Saved.')).not.toBeInTheDocument();

      server.refuse('PUT', /overdue-notifications/, 500);
      await user.click(screen.getByRole('button', { name: 'Save' }));
      expect(await screen.findByText('Could not save settings.')).toBeInTheDocument();
    });

    it('cannot be saved twice at once', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Overdue ticket notifications');
      let finish!: () => void;
      vi.mocked(apiClient.put).mockImplementationOnce((() => new Promise((resolve) => (finish = () => resolve({ data: server.state.overdue })))) as never);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
      await act(async () => finish());
      expect(await screen.findByText('Saved.')).toBeInTheDocument();
    });
  });

  describe('the knowledge base documentation', () => {
    const upload = () => screen.getByLabelText('Upload documentation files') as HTMLInputElement;

    it('lists what was uploaded, with the file and the number of chunks, and says when nothing was', async () => {
      server.state.documents.push({ id: 'k2', title: 'Guide', sourceFileName: 'guide.pdf', fileUrl: null, createdAt: '2026-10-02T08:00:00Z', chunkCount: 1 });
      const first = renderPage();
      await openTab(/Notifications/);

      expect(await screen.findByText('Manual')).toBeInTheDocument();
      expect(screen.getByText('manual.docx · 12 chunks')).toBeInTheDocument();
      expect(screen.getByText('guide.pdf · 1 chunk')).toBeInTheDocument();
      first.unmount();

      server.state.documents.length = 0;
      queryClient.clear();
      renderPage();
      await openTab(/Notifications/);
      expect(await screen.findByText('No documentation uploaded yet.')).toBeInTheDocument();
    });

    it('uploads the chosen files, shows them, and says what the server said when it refuses', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Manual');

      await user.upload(upload(), [new File(['x'], 'Wiring.docx'), new File(['y'], 'Setup.xlsx')]);

      await screen.findByText('Wiring');
      expect(screen.getByText('Setup')).toBeInTheDocument();
      expect(((server.sent('POST', /knowledge-base\/documents$/)[0].body) as FormData).getAll('files')).toHaveLength(2);
      expect(upload().value).toBe('');

      server.refuse('POST', /knowledge-base\/documents$/, 400, refused('Only .docx, .xls, .xlsx and .pdf are accepted.'));
      await userEvent.setup({ applyAccept: false }).upload(upload(), new File(['z'], 'Notes.txt'));
      expect(await screen.findByText('Only .docx, .xls, .xlsx and .pdf are accepted.')).toBeInTheDocument();

      server.refuse('POST', /knowledge-base\/documents$/, 500);
      await user.upload(upload(), new File(['z'], 'Notes.docx'));
      expect(await screen.findByText('Could not upload one or more of the selected files.')).toBeInTheDocument();
    });

    it('says it is working while the files are being read', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Manual');
      let finish!: () => void;
      vi.mocked(apiClient.post).mockImplementationOnce((() => new Promise((resolve) => (finish = () => resolve({ data: [] })))) as never);

      await user.upload(upload(), new File(['x'], 'Slow.docx'));

      expect(screen.getByText(/Uploading and indexing/)).toBeInTheDocument();
      expect(upload()).toBeDisabled();
      await act(async () => finish());
      await waitFor(() => expect(screen.queryByText(/Uploading and indexing/)).not.toBeInTheDocument());
    });

    it('removes one after asking', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await user.click(within(rowOf(await screen.findByText('Manual'))).getByRole('button', { name: 'Delete' }));
      expect(within(dialog()).getByText('Remove this document?')).toBeInTheDocument();
      await user.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
      expect(server.sent('DELETE', /documents\/k1/)).toHaveLength(0);

      await user.click(within(rowOf('Manual')).getByRole('button', { name: 'Delete' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      await waitFor(() => expect(screen.queryByText('Manual')).not.toBeInTheDocument());
      expect(server.sent('DELETE', /documents\/k1/)).toHaveLength(1);
    });

    it('re-reads one and shows how far it is, and says what the server said when it refuses to start', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await user.click(within(rowOf(await screen.findByText('Manual'))).getByRole('button', { name: 'Re-index' }));

      expect(await screen.findByText(/0 \/ 10 · Started\./)).toBeInTheDocument();
      expect(within(rowOf('Manual')).getByRole('button', { name: 'Re-index' })).toBeDisabled();
      expect(server.sent('POST', /documents\/k1\/reindex/)).toHaveLength(1);
    });

    it('shows the state of a run that was started elsewhere after the page was opened, when another one is asked for', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      const row = rowOf(await screen.findByText('Manual'));
      server.state.reindex = { ...server.state.reindex, state: 'Running', total: 20, processed: 4, message: 'Working on it.' };
      await user.click(within(row).getByRole('button', { name: 'Re-index' }));

      expect(await screen.findByText(/4 \/ 20 · Working on it\./)).toBeInTheDocument();
      expect(within(rowOf('Manual')).getByRole('button', { name: 'Re-index' })).toBeDisabled();
    });

    it('says plainly that it could not start the re-index when the server gave no reason', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Manual');
      server.refuse('POST', /documents\/k1\/reindex/, 500);

      await user.click(within(rowOf('Manual')).getByRole('button', { name: 'Re-index' }));

      expect(await screen.findByText('Could not start the re-index.')).toBeInTheDocument();
    });
  });

  describe('the maintenance of the knowledge base', () => {
    it('starts a re-index of the tickets that lack an entry, and says it is running', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Manual');

      await user.click(screen.getByRole('button', { name: 'Reindex missing tickets' }));

      expect(await screen.findByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      expect(screen.getByText(/0 \/ 10/)).toBeInTheDocument();
      expect(server.sent('POST', /reindex-all/)).toHaveLength(1);
    });

    it('picks up a run that was already going when the page was opened', async () => {
      server.state.reindex = { ...server.state.reindex, state: 'Running', total: 8, processed: 3, message: 'Embedding tickets.' };
      renderPage();
      await openTab(/Notifications/);

      expect(await screen.findByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      expect(screen.getByText(/3 \/ 8/)).toBeInTheDocument();
    });

    it('says how many were embedded when it is done, and in red when it failed', async () => {
      server.state.reindex = { ...server.state.reindex, state: 'Completed', total: 8, processed: 8, embedded: 6, message: 'Done.' };
      const first = renderPage();
      await openTab(/Notifications/);

      expect(await screen.findByText(/Done\./)).toHaveTextContent('(6 embedded)');
      expect(screen.getByRole('button', { name: 'Reindex missing tickets' })).toBeEnabled();
      first.unmount();

      server.state.reindex = { ...server.state.reindex, state: 'Failed', message: 'The AI service is down.', embedded: 0 };
      queryClient.clear();
      renderPage();
      await openTab(/Notifications/);
      expect((await screen.findByText(/The AI service is down\./)).className).toContain('text-red-700');
    });

    it('shows the state of a run that is already going when another one is asked for', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Manual');
      server.state.reindex = { ...server.state.reindex, state: 'Running', total: 9, processed: 2, message: 'Busy.' };
      await user.click(screen.getByRole('button', { name: 'Reindex missing tickets' }));

      expect(await screen.findByText(/2 \/ 9/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Reindexing…' })).toBeDisabled();
    });

    it('says what the server said when it could not start, and plainly when it said nothing', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Notifications/);
      await screen.findByText('Manual');

      server.refuse('POST', /reindex-all/, 400, refused('The AI service is not set up.'));
      await user.click(screen.getByRole('button', { name: 'Reindex missing tickets' }));
      expect(await screen.findByText('The AI service is not set up.')).toBeInTheDocument();

      server.refuse('POST', /reindex-all/, 500);
      await user.click(screen.getByRole('button', { name: 'Reindex missing tickets' }));
      expect(await screen.findByText('Could not start the reindex.')).toBeInTheDocument();
    });

    it('follows a run until it ends', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setInterval', 'clearInterval'] });
      server.state.reindex = { ...server.state.reindex, state: 'Running', total: 10, processed: 1, message: 'Going.' };
      renderPage();
      await openTab(/Notifications/);
      expect(await screen.findByText(/1 \/ 10/)).toBeInTheDocument();
      server.state.reindex = { ...server.state.reindex, state: 'Completed', processed: 10, embedded: 10, message: 'Finished.' };

      await act(async () => {
        vi.advanceTimersByTime(3000);
      });

      expect(await screen.findByText(/Finished\./)).toBeInTheDocument();
    });
  });
  // What the page used to do in silence when the server refused or could not be reached.
  describe('when the server refuses or cannot be reached', () => {
    it('says why renaming a help topic or a category was refused, and stays in the edit', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Help Topics/);
      await user.click(within(rowOf(await screen.findByText('Printer'))).getByRole('button', { name: 'Rename' }));
      const field = screen.getByDisplayValue('Printer');
      await user.clear(field);
      await user.type(field, 'Network');

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('A help topic with that name already exists.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
      expect(screen.getByDisplayValue('Network')).toBeInTheDocument();
    });

    it.each([
      { what: 'a branch', tab: /Branches/, row: 'Sarajevo', url: /^\/api\/departments\/d1$/, fallback: /Could not change this branch\./, confirms: true },
      { what: 'an inactive branch', tab: /Branches/, row: 'Mostar', url: /^\/api\/departments\/d2$/, fallback: /Could not change this branch\./, confirms: false },
      { what: 'a help topic', tab: /Help Topics/, row: 'Printer', url: /^\/api\/help-topics\/h1$/, fallback: /Could not change this help topic\./, confirms: true },
      { what: 'an inactive help topic', tab: /Help Topics/, row: 'Network', url: /^\/api\/help-topics\/h2$/, fallback: /Could not change this help topic\./, confirms: false },
      { what: 'a category', tab: /Categories & SLA/, row: 'Hardware', url: /^\/api\/ticket-categories\/c1$/, fallback: /Could not change this category\./, confirms: true },
      { what: 'an SLA plan', tab: /Categories & SLA/, row: 'Standard', url: /^\/api\/sla-plans\/p1$/, fallback: /Could not change this SLA plan\./, confirms: true },
    ])('says why changing $what was refused, and plainly when the server gave no reason', async ({ tab, row, url, fallback, confirms }) => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);
      await screen.findByText(row);

      server.refuse('PUT', url, 400, refused('Nothing may be changed right now.'));
      await user.click(within(rowOf(row)).getByRole('button', { name: confirms ? 'Deactivate' : 'Reactivate' }));
      if (confirms) await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));
      expect(await screen.findByText('Nothing may be changed right now.')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

      server.refuse('PUT', url, 500);
      await user.click(within(rowOf(row)).getByRole('button', { name: confirms ? 'Deactivate' : 'Reactivate' }));
      if (confirms) await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));
      expect(await screen.findByText(fallback)).toBeInTheDocument();
    });

    it('says why changing a sub-branch was refused', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');
      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Sub-branches' }));
      await screen.findByText('Centar');
      server.refuse('PUT', /sub-branches\/s1/, 500);

      await user.click(within(rowOf('Centar')).getByRole('button', { name: 'Deactivate' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Deactivate' }));

      expect(await screen.findByText(/Could not change this sub-branch\./)).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it.each([
      { what: 'branches', tab: /Branches/, url: /^\/api\/departments$/, empty: 'No branches found.' },
      { what: 'help topics', tab: /Help Topics/, url: /^\/api\/help-topics$/, empty: 'No help topics found.' },
      { what: 'categories', tab: /Categories & SLA/, url: /^\/api\/ticket-categories$/, empty: 'No categories found.' },
      { what: 'SLA plans', tab: /Categories & SLA/, url: /^\/api\/sla-plans$/, empty: 'No SLA plans found.' },
    ])('says that the $what could not be loaded instead of saying there are none', async ({ what, tab, url, empty }) => {
      server.refuse('GET', url, 500, {}, -1);
      renderPage();
      await openTab(tab);

      expect(await screen.findByText(new RegExp(`The ${what} could not be loaded`))).toBeInTheDocument();
      expect(screen.queryByText(empty)).not.toBeInTheDocument();
    });

    it('says that the sub-branches could not be loaded instead of loading for ever', async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText('Sarajevo');
      server.refuse('GET', /sub-branches/, 500, {}, -1);

      await user.click(within(rowOf('Sarajevo')).getByRole('button', { name: 'Sub-branches' }));

      expect(await screen.findByText(/The sub-branches could not be loaded/)).toBeInTheDocument();
      expect(screen.queryByText('Loading sub-branches…')).not.toBeInTheDocument();
    });

    it('says that a list is loading, and not that there is nothing in it, while it is on its way', async () => {
      const original = vi.mocked(apiClient.get).getMockImplementation()!;
      vi.mocked(apiClient.get).mockImplementation(((url: string, config?: unknown) => (url === '/api/departments' ? new Promise(() => {}) : original(url, config as never))) as never);

      renderPage();

      expect(await screen.findByText('Loading branches…')).toBeInTheDocument();
      expect(screen.queryByText('No branches found.')).not.toBeInTheDocument();
    });

    it('does not show the notification settings with made-up values when the real ones could not be loaded, and so cannot overwrite them', async () => {
      server.refuse('GET', /overdue-notifications/, 500, {}, -1);
      renderPage();
      await openTab(/Notifications/);

      expect(await screen.findByText(/The settings could not be loaded/)).toBeInTheDocument();
      expect(screen.queryByRole('checkbox', { name: /passes its SLA due date/ })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
      expect(server.sent('PUT', /overdue-notifications/)).toHaveLength(0);
    });

    it('says that the documentation could not be loaded, and why removing a document failed', async () => {
      const user = userEvent.setup();
      server.refuse('GET', /knowledge-base\/documents$/, 500, {}, 1);
      const first = renderPage();
      await openTab(/Notifications/);
      expect(await screen.findByText(/The documentation could not be loaded/)).toBeInTheDocument();
      first.unmount();

      queryClient.clear();
      renderPage();
      await openTab(/Notifications/);
      await user.click(within(rowOf(await screen.findByText('Manual'))).getByRole('button', { name: 'Delete' }));
      server.refuse('DELETE', /documents\/k1/, 400, refused('The document is being re-indexed.'));
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      expect(await screen.findByText('The document is being re-indexed.')).toBeInTheDocument();
      expect(screen.getByText('Manual')).toBeInTheDocument();

      server.refuse('DELETE', /documents\/k1/, 500);
      await user.click(within(rowOf('Manual')).getByRole('button', { name: 'Delete' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));
      expect(await screen.findByText('Could not remove this document.')).toBeInTheDocument();
    });
  });

  describe('what is left unsaid until it is looked at closely', () => {
    it('takes the message about a refused change away when the next change goes through', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Help Topics/);
      await screen.findByText('Network');
      server.refuse('PUT', /help-topics\/h2/, 400, refused('Nothing may be changed right now.'));
      await user.click(within(rowOf('Network')).getByRole('button', { name: 'Reactivate' }));
      expect(await screen.findByText('Nothing may be changed right now.')).toBeInTheDocument();

      await user.click(within(rowOf('Network')).getByRole('button', { name: 'Reactivate' }));

      await waitFor(() => expect(within(rowOf('Network')).getByText('Active')).toBeInTheDocument());
      expect(screen.queryByText('Nothing may be changed right now.')).not.toBeInTheDocument();
    });

    it('does not show an old refusal to delete while the next try is on its way', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Help Topics/);
      await user.click(within(rowOf(await screen.findByText('Printer'))).getByRole('button', { name: 'Delete' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));
      expect(await within(dialog()).findByText(/is used by tickets and cannot be deleted/)).toBeInTheDocument();
      vi.mocked(apiClient.delete).mockImplementationOnce((() => new Promise(() => {})) as never);

      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      expect(within(dialog()).queryByText(/is used by tickets and cannot be deleted/)).not.toBeInTheDocument();
    });

    it.each([
      { what: 'branch', tab: /Branches/, row: 'Sarajevo', url: /departments\/d1/ },
      { what: 'help topic', tab: /Help Topics/, row: 'Printer', url: /help-topics\/h1/ },
      { what: 'category', tab: /Categories & SLA/, row: 'Hardware', url: /ticket-categories\/c1/ },
      { what: 'SLA plan', tab: /Categories & SLA/, row: 'Standard', url: /sla-plans\/p1/ },
    ])('says plainly that the $what could not be deleted when the server gave no reason', async ({ what, tab, row, url }) => {
      const user = userEvent.setup();
      renderPage();
      await openTab(tab);
      await screen.findByText(row);
      server.refuse('DELETE', url, 500);

      await user.click(within(rowOf(row)).getByRole('button', { name: 'Delete' }));
      await user.click(within(dialog()).getByRole('button', { name: 'Delete' }));

      expect(await within(dialog()).findByText(new RegExp(`Could not delete this ${what}\\.`))).toBeInTheDocument();
    });

    describe('editing a branch', () => {
      const fields = () => within(screen.getByText('Existing branches (2)').closest('div')!.parentElement!).getAllByRole('textbox').slice(-2);

      it('does not save a branch whose e-mail was emptied', async () => {
        const user = userEvent.setup();
        renderPage();
        await user.click(within(rowOf(await screen.findByText('Sarajevo'))).getByRole('button', { name: 'Edit' }));
        await user.clear(fields()[1]);

        await user.click(screen.getByRole('button', { name: 'Save' }));

        expect(server.sent('PUT', /departments/)).toHaveLength(0);
        expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
      });

      it('keeps a branch that is switched off switched off when it is edited', async () => {
        const user = userEvent.setup();
        renderPage();
        await user.click(within(rowOf(await screen.findByText('Mostar'))).getByRole('button', { name: 'Edit' }));
        await user.type(fields()[0], ' Centar');

        await user.click(screen.getByRole('button', { name: 'Save' }));

        await screen.findByText('Mostar Centar');
        expect(server.sent('PUT', /^\/api\/departments\/d2$/).map((r) => r.body)).toEqual([{ name: 'Mostar Centar', email: 'mostar@firma.test', isActive: false }]);
        expect(within(rowOf('Mostar Centar')).getByText('Inactive')).toBeInTheDocument();
      });

      it('says plainly that it could not be changed when the server gave no reason, and takes a refusal away once the edit goes through', async () => {
        const user = userEvent.setup();
        renderPage();
        await user.click(within(rowOf(await screen.findByText('Sarajevo'))).getByRole('button', { name: 'Edit' }));
        server.refuse('PUT', /departments\/d1/, 500);
        await user.click(screen.getByRole('button', { name: 'Save' }));
        expect(await screen.findByText('Could not update branch.')).toBeInTheDocument();

        await user.type(fields()[0], ' Centar');
        await user.click(screen.getByRole('button', { name: 'Save' }));

        await screen.findByText('Sarajevo Centar');
        expect(screen.queryByText('Could not update branch.')).not.toBeInTheDocument();
      });
    });

    describe('renaming a help topic', () => {
      it('keeps one that is switched off switched off', async () => {
        const user = userEvent.setup();
        renderPage();
        await openTab(/Help Topics/);
        await user.click(within(rowOf(await screen.findByText('Network'))).getByRole('button', { name: 'Rename' }));
        const field = screen.getByDisplayValue('Network');
        await user.clear(field);
        await user.type(field, 'LAN');

        await user.click(screen.getByRole('button', { name: 'Save' }));

        await screen.findByText('LAN');
        expect(server.sent('PUT', /help-topics\/h2/).map((r) => r.body)).toEqual([{ name: 'LAN', isActive: false }]);
      });

      it('says plainly that it could not be renamed when the server gave no reason', async () => {
        const user = userEvent.setup();
        renderPage();
        await openTab(/Help Topics/);
        await user.click(within(rowOf(await screen.findByText('Printer'))).getByRole('button', { name: 'Rename' }));
        const field = screen.getByDisplayValue('Printer');
        await user.type(field, ' 2');
        server.refuse('PUT', /help-topics\/h1/, 500);

        await user.click(screen.getByRole('button', { name: 'Save' }));

        expect(await screen.findByText('Could not rename this help topic.')).toBeInTheDocument();
      });
    });

    it('trims the name of an SLA plan', async () => {
      const user = userEvent.setup();
      renderPage();
      await openTab(/Categories & SLA/);
      await screen.findByText('Standard');

      await user.type(screen.getByPlaceholderText('e.g. Standard'), '  Urgent ');
      await user.click(screen.getByRole('button', { name: '+ Add SLA Plan' }));

      await screen.findByText('Urgent');
      expect(server.sent('POST', /sla-plans/).map((r) => r.body)).toEqual([{ name: 'Urgent', resolutionTimeHours: 72 }]);
    });

    it('shows the notification settings as they are when they are switched off and have no number of days', async () => {
      server.state.overdue = { notifyOnSlaBreach: false, manualOverdueDays: null };
      renderPage();
      await openTab(/Notifications/);

      expect(await screen.findByRole('checkbox', { name: /passes its SLA due date/ })).not.toBeChecked();
      expect(screen.getByPlaceholderText('e.g. 3')).toHaveValue(null);
    });

    it('reloads the list of documents when a re-index that was followed has ended, because the number of chunks may have changed', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setInterval', 'clearInterval'] });
      renderPage();
      await openTab(/Notifications/);
      await act(async () => {
        fireEvent.click(within(rowOf(await screen.findByText('Manual'))).getByRole('button', { name: 'Re-index' }));
      });
      expect(await screen.findByText('manual.docx · 12 chunks')).toBeInTheDocument();
      server.state.documents[0].chunkCount = 20;
      server.state.reindex = { ...server.state.reindex, state: 'Completed', processed: 10, message: 'Finished.' };

      await act(async () => {
        vi.advanceTimersByTime(2000);
      });

      expect(await screen.findByText('manual.docx · 20 chunks')).toBeInTheDocument();
    });
  });
});
