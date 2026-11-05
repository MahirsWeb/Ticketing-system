import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import TasksPage from './TasksPage';
import { queryClient } from '../../api/queryClient';
import { useAuthStore } from '../../store/authStore';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installTasksBackend, makeTask, newTasksState, person, type TasksState } from '../../test/fakeTasksApi';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), patch: vi.fn() } }));

const at = (day: number, hour: number, minute = 0) => new Date(2026, 5, day, hour, minute).toISOString();
const asEmployee = (extra: Record<string, unknown> = {}) =>
  useAuthStore.setState({ accessToken: 't', user: { id: 'u1', role: 'Employee', firstName: 'Amra', lastName: 'Ahmetović', departmentId: 'd1', ...extra } as never });
const asAdmin = () =>
  useAuthStore.setState({ accessToken: 't', user: { id: 'a1', role: 'Admin', firstName: 'Admin', lastName: 'Tester', departmentId: null } as never });

/** Pending: two; in progress: one; done: one. */
function seed(): TasksState {
  const state = newTasksState();
  state.tasks.push(
    makeTask('t1', 'Zebra plan', { to: 'u1', toName: 'Amra Ahmetović', by: 'u2', byName: 'Bego Begić' }),
    makeTask('t2', 'alpha report', { to: 'u2', toName: 'Bego Begić', by: 'u1', byName: 'Amra Ahmetović' }),
    makeTask('t3', 'Middle job', { to: 'u2', toName: 'Bego Begić', by: 'u1', byName: 'Amra Ahmetović', sessions: [[at(10, 9, 30), null]] }),
    makeTask('t4', 'Done one', { to: 'u1', toName: 'Amra Ahmetović', by: 'u2', byName: 'Bego Begić', sessions: [[at(9, 9), at(9, 10, 15)]] })
  );
  return state;
}

let server: ReturnType<typeof installTasksBackend>;
const originalDefaults = queryClient.getDefaultOptions();

function renderPage(state: TasksState = seed(), before?: (server: ReturnType<typeof installTasksBackend>) => void) {
  server = installTasksBackend(state);
  before?.(server);
  return renderWithQuery(<TasksPage />, queryClient);
}

const newUser = () => userEvent.setup();
const tab = (name: RegExp) => screen.getByText(name);
const PENDING = /^Opened \(Pending\)/;
const IN_PROGRESS = /^In Progress/;
const DONE = /^Done \(/;
const bodyRows = () => within(screen.getAllByRole('rowgroup')[1]).getAllByRole('row');
const cellsOf = (row: HTMLElement) => within(row).getAllByRole('cell').map((c) => c.textContent);
const titles = () => bodyRows().map((r) => cellsOf(r)[0]);
const listRequests = () => server.sent('GET', /^\/api\/work-tasks$/);
const lastListParams = () => listRequests().at(-1)!.params as Record<string, unknown>;
const heading = (name: string) => screen.getByRole('columnheader', { name: new RegExp(`^${name}`) });

describe('the tasks page', () => {
  beforeEach(() => {
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
    asEmployee();
  });
  afterAll(() => queryClient.setDefaultOptions(originalDefaults));

  describe('what each kind of person sees', () => {
    it('shows an employee the form for a new task, "My tasks only", and the columns of a task without the branch', async () => {
      renderPage();
      await screen.findByText('Zebra plan');

      expect(screen.getByRole('heading', { name: 'Tasks' })).toBeInTheDocument();
      expect(screen.getByText(/Quick, informal work items/)).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'New task' })).toBeInTheDocument();
      expect(screen.getByLabelText('My tasks only')).toBeInTheDocument();
      expect(screen.queryByRole('combobox', { name: /branch/i })).not.toBeInTheDocument();
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Title', 'Status', 'Assigned to', 'Assigned by', 'Started', 'Ended']);
      expect(server.sent('GET', /^\/api\/departments$/)).toHaveLength(0);
    });

    it('shows an administrator the form too, the branches instead of "My tasks only", and the branch of each task', async () => {
      asAdmin();
      renderPage();
      await screen.findByText('Zebra plan');

      expect(screen.getByText(/You can create and assign tasks into any branch/)).toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'New task' })).toBeInTheDocument();
      expect(screen.queryByLabelText('My tasks only')).not.toBeInTheDocument();
      expect(await screen.findByRole('option', { name: 'Mostar' })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: 'All branches' })).toBeInTheDocument();
      expect(screen.getAllByRole('columnheader').map((h) => h.textContent)).toEqual(['Title', 'Status', 'Assigned to', 'Assigned by', 'Branch', 'Started', 'Ended']);
      expect(cellsOf(bodyRows()[0])[4]).toBe('Sarajevo');
    });
  });

  describe('the list', () => {
    it('asks for every task when nothing is chosen, and shows the pending ones first', async () => {
      renderPage();
      await screen.findByText('Zebra plan');

      expect(lastListParams().departmentId).toBeUndefined();
      expect(lastListParams().assignedToUserId).toBeUndefined();
      expect(titles()).toEqual(['Zebra plan', 'alpha report']);
    });

    it('asks only for the tasks of the person who is signed in when "My tasks only" is chosen, and for all of them again when it is not', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');

      await user.click(screen.getByLabelText('My tasks only'));
      await waitFor(() => expect(lastListParams().assignedToUserId).toBe('u1'));
      await waitFor(() => expect(titles()).toEqual(['Zebra plan']));

      await user.click(screen.getByLabelText('My tasks only'));
      await waitFor(() => expect(lastListParams().assignedToUserId).toBeUndefined());
      await waitFor(() => expect(titles()).toEqual(['Zebra plan', 'alpha report']));
    });

    it('lets an administrator narrow the tasks to a branch, and back to all of them', async () => {
      const user = newUser();
      asAdmin();
      const state = seed();
      state.tasks.push(makeTask('t5', 'Mostar job', { to: 'u3', toName: 'Dino Dinić', department: ['d2', 'Mostar'] }));
      renderPage(state);
      await screen.findByText('Mostar job');
      await screen.findByRole('option', { name: 'Mostar' });
      const select = screen.getByRole('option', { name: 'All branches' }).closest('select')!;

      await user.selectOptions(select, 'd2');
      await waitFor(() => expect(lastListParams().departmentId).toBe('d2'));
      await waitFor(() => expect(titles()).toEqual(['Mostar job']));

      await user.selectOptions(select, '');
      await waitFor(() => expect(titles()).toHaveLength(3));
      expect(lastListParams().departmentId).toBeUndefined();
    });

    it('counts the tasks of each status in the tab, and shows the ones of the status that is chosen', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');

      expect(tab(PENDING)).toHaveTextContent('Opened (Pending) (2)');
      expect(tab(IN_PROGRESS)).toHaveTextContent('In Progress (1)');
      expect(tab(DONE)).toHaveTextContent('Done (1)');

      await user.click(tab(IN_PROGRESS));
      expect(titles()).toEqual(['Middle job']);
      await user.click(tab(DONE));
      expect(titles()).toEqual(['Done one']);
    });

    it('says it across the whole width of the table, which is wider for an administrator, who sees the branch too', async () => {
      const user = newUser();
      const { unmount } = renderPage(newTasksState());
      await user.click(await screen.findByText(DONE));
      expect(screen.getByText('No tasks in this status yet.').closest('td')).toHaveAttribute('colspan', '6');
      unmount();

      asAdmin();
      renderPage(newTasksState());
      await user.click(await screen.findByText(DONE));
      expect(screen.getByText('No tasks in this status yet.').closest('td')).toHaveAttribute('colspan', '7');
    });

    it('says so when there are no tasks of the status that is chosen', async () => {
      const user = newUser();
      const state = newTasksState();
      state.tasks.push(makeTask('t1', 'Zebra plan'));
      renderPage(state);
      await screen.findByText('Zebra plan');

      await user.click(tab(DONE));

      expect(screen.getByText('No tasks in this status yet.')).toBeInTheDocument();
    });

    it('shows the status, who has the task, who gave it, and when work on it started and ended', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      expect(cellsOf(bodyRows()[0])).toEqual(['Zebra plan', 'Pending', 'Amra Ahmetović', 'Bego Begić', '—', '—']);

      await user.click(tab(IN_PROGRESS));
      expect(cellsOf(bodyRows()[0])).toEqual(['Middle job', 'InProgress', 'Bego Begić', 'Amra Ahmetović', '10.06 09:30', '—']);

      await user.click(tab(DONE));
      expect(cellsOf(bodyRows()[0])).toEqual(['Done one', 'Done', 'Amra Ahmetović', 'Bego Begić', '09.06 09:00', '09.06 10:15']);
    });
  });

  describe('sorting', () => {
    it('sorts by title, ignoring capital letters, ascending first, and the other way round when chosen again', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      expect(heading('Title')).toHaveAttribute('aria-sort', 'none');

      await user.click(within(heading('Title')).getByRole('button'));
      expect(titles()).toEqual(['alpha report', 'Zebra plan']);
      expect(heading('Title')).toHaveAttribute('aria-sort', 'ascending');
      expect(heading('Title')).toHaveTextContent('Title▲');

      await user.click(within(heading('Title')).getByRole('button'));
      expect(titles()).toEqual(['Zebra plan', 'alpha report']);
      expect(heading('Title')).toHaveAttribute('aria-sort', 'descending');
      expect(heading('Title')).toHaveTextContent('Title▼');

      await user.click(within(heading('Title')).getByRole('button'));
      expect(heading('Title')).toHaveAttribute('aria-sort', 'ascending');
    });

    it('starts again from ascending when another column is chosen, and leaves the first one unsorted', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await user.click(within(heading('Title')).getByRole('button'));
      await user.click(within(heading('Title')).getByRole('button'));

      await user.click(within(heading('Assigned to')).getByRole('button'));

      expect(heading('Assigned to')).toHaveAttribute('aria-sort', 'ascending');
      expect(heading('Title')).toHaveAttribute('aria-sort', 'none');
      expect(titles()).toEqual(['Zebra plan', 'alpha report']); // Amra, then Bego
    });

    it('sorts by who gave the task', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');

      await user.click(within(heading('Assigned by')).getByRole('button'));

      expect(titles()).toEqual(['alpha report', 'Zebra plan']); // Amra gave alpha, Bego gave Zebra
    });

    it('sorts by branch, for an administrator', async () => {
      const user = newUser();
      asAdmin();
      const state = seed();
      state.tasks.push(makeTask('t5', 'Mostar job', { to: 'u3', toName: 'Dino Dinić', department: ['d2', 'Mostar'] }));
      renderPage(state);
      await screen.findByText('Mostar job');

      await user.click(within(heading('Branch')).getByRole('button'));

      expect(titles()).toEqual(['Mostar job', 'Zebra plan', 'alpha report']);
    });

    it('sorts by when work started and ended', async () => {
      const user = newUser();
      const state = newTasksState();
      state.tasks.push(
        makeTask('t1', 'Late', { sessions: [[at(12, 9), at(12, 10)]] }),
        makeTask('t2', 'Early', { sessions: [[at(8, 9), at(15, 10)]] }),
        makeTask('t3', 'Middle', { sessions: [[at(10, 9), at(11, 10)]] })
      );
      renderPage(state);
      await user.click(await screen.findByText(DONE));

      await user.click(within(heading('Started')).getByRole('button'));
      expect(titles()).toEqual(['Early', 'Middle', 'Late']);

      await user.click(within(heading('Ended')).getByRole('button'));
      expect(titles()).toEqual(['Middle', 'Late', 'Early']);
      await user.click(within(heading('Ended')).getByRole('button'));
      expect(titles()).toEqual(['Early', 'Late', 'Middle']);
    });
  });

  describe('a new task', () => {
    it('is given to a colleague of the branch of the person who signed in, with themselves among them, grouped by branch', async () => {
      renderPage();
      await screen.findByText('Zebra plan');

      const select = screen.getByLabelText('Assign to');
      await within(select).findByRole('group', { name: 'Sarajevo' });
      expect(server.sent('GET', /^\/api\/users$/)[0].params).toEqual({ role: 'Employee', departmentId: 'd1' });
      expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Choose a colleague (or yourself)…', 'Amra Ahmetović — me', 'Bego Begić']);
      expect(within(select).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Sarajevo']);
    });

    it('is given to a colleague of any branch when an administrator makes it, in the order of the branch names', async () => {
      asAdmin();
      renderPage();
      await screen.findByText('Zebra plan');

      const select = screen.getByLabelText('Assign to');
      await within(select).findByRole('group', { name: 'Mostar' });
      expect(server.sent('GET', /^\/api\/users$/)[0].params).toEqual({ role: 'Employee', departmentId: undefined });
      expect(within(select).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Mostar', 'Sarajevo']);
      expect(within(select).getAllByRole('option')[0]).toHaveTextContent('Choose a colleague…');
    });

    it('does not ask for colleagues when the employee is in no branch', async () => {
      asEmployee({ departmentId: null });
      renderPage();
      await screen.findByText('Zebra plan');

      expect(server.sent('GET', /^\/api\/users$/)).toHaveLength(0);
    });

    it('wants a title, a description and somebody to give it to', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');

      await user.click(screen.getByRole('button', { name: 'Create task' }));
      expect(screen.getByText('Title, description and assignee are all required.')).toBeInTheDocument();

      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), '   ');
      await user.click(screen.getByRole('button', { name: 'Create task' }));
      expect(screen.getByText('Title, description and assignee are all required.')).toBeInTheDocument();
      expect(server.sent('POST', /work-tasks$/)).toHaveLength(0);
    });

    it.each([
      ['no title', { title: '', description: 'Za iduću sedmicu', assignee: 'u2' }],
      ['a title of nothing but spaces', { title: '   ', description: 'Za iduću sedmicu', assignee: 'u2' }],
      ['no description', { title: 'Napravi plan', description: '', assignee: 'u2' }],
      ['a description of nothing but spaces', { title: 'Napravi plan', description: '   ', assignee: 'u2' }],
      ['nobody to give it to', { title: 'Napravi plan', description: 'Za iduću sedmicu', assignee: '' }],
    ])('is not sent with %s, even when everything else is in order', async (_name, written) => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });
      if (written.title) await user.type(screen.getByLabelText('Title'), written.title);
      if (written.description) await user.type(screen.getByLabelText('Description'), written.description);
      if (written.assignee) await user.selectOptions(screen.getByLabelText('Assign to'), written.assignee);

      await user.click(screen.getByRole('button', { name: 'Create task' }));

      expect(screen.getByText('Title, description and assignee are all required.')).toBeInTheDocument();
      expect(server.sent('POST', /work-tasks$/)).toHaveLength(0);
    });

    it('names the group of colleagues who are in no branch, and puts it among the others in the order of the names', async () => {
      asAdmin();
      const state = newTasksState();
      state.people.push(person('u4', 'Zed', 'Zedić', null, null));
      renderPage(state);
      await screen.findByText('No tasks in this status yet.');

      const select = screen.getByLabelText('Assign to');
      await within(select).findByRole('group', { name: 'No branch' });
      expect(within(select).getAllByRole('group').map((g) => g.getAttribute('label'))).toEqual(['Mostar', 'No branch', 'Sarajevo']);
    });

    it('is created with what was written and who was chosen, the form is emptied, and the task is in the list', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });

      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), 'Za iduću sedmicu');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');
      await user.click(screen.getByRole('button', { name: 'Create task' }));

      expect(await screen.findByText('Napravi plan')).toBeInTheDocument();
      expect(server.sent('POST', /work-tasks$/).map((r) => r.body)).toEqual([{ title: 'Napravi plan', description: 'Za iduću sedmicu', assignedToUserId: 'u2' }]);
      expect(screen.getByLabelText('Title')).toHaveValue('');
      expect(screen.getByLabelText('Description')).toHaveValue('');
      expect(screen.getByLabelText('Assign to')).toHaveValue('');
      expect(titles()).toContain('Napravi plan');
    });

    it('limits the title to 150 characters', async () => {
      renderPage();
      await screen.findByText('Zebra plan');

      expect(screen.getByLabelText('Title')).toHaveAttribute('maxlength', '150');
    });

    it('says what the server said when it refuses, or plainly when it said nothing, and keeps what was written', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });
      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), 'Za iduću sedmicu');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');

      server.refuse('POST', /work-tasks$/, 400, { message: 'That person is on leave.' });
      await user.click(screen.getByRole('button', { name: 'Create task' }));
      expect(await screen.findByText('That person is on leave.')).toBeInTheDocument();

      server.refuse('POST', /work-tasks$/, 500, {});
      await user.click(screen.getByRole('button', { name: 'Create task' }));
      expect(await screen.findByText('Could not create the task.')).toBeInTheDocument();
      expect(screen.getByLabelText('Title')).toHaveValue('Napravi plan');
      expect(screen.getByLabelText('Description')).toHaveValue('Za iduću sedmicu');
    });

    it('cannot be sent twice while it is being made', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });
      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), 'Za iduću sedmicu');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');
      const hold = server.hold('POST', /work-tasks$/);

      await user.click(screen.getByRole('button', { name: 'Create task' }));

      expect(screen.getByRole('button', { name: 'Creating…' })).toBeDisabled();
      hold.release();
      await screen.findByRole('button', { name: 'Create task' });
      expect(server.sent('POST', /work-tasks$/)).toHaveLength(1);
    });
  });

  describe('opening a task', () => {
    it('shows what the task is and who had it, and closes again when the same task is chosen', async () => {
      const user = newUser();
      renderPage();
      const title = await screen.findByRole('button', { name: 'Zebra plan' });
      expect(title).toHaveAttribute('aria-expanded', 'false');

      await user.click(title);
      expect(await screen.findByText('Opis zadatka Zebra plan')).toBeInTheDocument();
      expect(title).toHaveAttribute('aria-expanded', 'true');
      expect(screen.getByText('Assignment history')).toBeInTheDocument();

      await user.click(title);
      expect(screen.queryByText('Opis zadatka Zebra plan')).not.toBeInTheDocument();
      expect(title).toHaveAttribute('aria-expanded', 'false');
    });

    it('opens when the row is clicked too, and not for a click inside what is shown', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');

      await user.click(within(bodyRows()[0]).getAllByRole('cell')[3]);
      expect(await screen.findByText('Opis zadatka Zebra plan')).toBeInTheDocument();

      await user.click(screen.getByText('Opis zadatka Zebra plan'));
      expect(screen.getByText('Opis zadatka Zebra plan')).toBeInTheDocument();
    });

    it('shows what is under the row across the whole width of the table too', async () => {
      const user = newUser();
      const { unmount } = renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      expect((await screen.findByText('Opis zadatka Zebra plan')).closest('td')).toHaveAttribute('colspan', '6');
      unmount();

      asAdmin();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      expect((await screen.findByText('Opis zadatka Zebra plan')).closest('td')).toHaveAttribute('colspan', '7');
    });

    it('says that the task is on its way while it is, and then shows it', async () => {
      const user = newUser();
      renderPage();
      const zebra = await screen.findByRole('button', { name: 'Zebra plan' });
      const hold = server.hold('GET', /work-tasks\/t1$/);

      await user.click(zebra);
      expect(screen.getByText('Loading the task…')).toBeInTheDocument();
      expect(screen.queryByText('Opis zadatka Zebra plan')).not.toBeInTheDocument();

      hold.release();
      expect(await screen.findByText('Opis zadatka Zebra plan')).toBeInTheDocument();
      expect(screen.queryByText('Loading the task…')).not.toBeInTheDocument();
    });

    it('shows one task at a time: choosing another closes the first', async () => {
      const user = newUser();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      await screen.findByText('Opis zadatka Zebra plan');

      await user.click(screen.getByRole('button', { name: 'alpha report' }));

      expect(await screen.findByText('Opis zadatka alpha report')).toBeInTheDocument();
      expect(screen.queryByText('Opis zadatka Zebra plan')).not.toBeInTheDocument();
    });

    it('says why when the task cannot be shown, with the words of the server when it had some', async () => {
      const user = newUser();
      renderPage();
      const zebra = await screen.findByRole('button', { name: 'Zebra plan' });

      server.refuse('GET', /work-tasks\/t1$/, 404, { message: 'This task was deleted.' });
      await user.click(zebra);
      expect(await screen.findByText('This task was deleted.')).toBeInTheDocument();

      await user.click(zebra);
      server.refuse('GET', /work-tasks\/t1$/, 500, {});
      await user.click(zebra);
      expect(await screen.findByText('Could not load this task.')).toBeInTheDocument();
    });

    it('lets an employee reassign the task and time it, but an administrator only read it', async () => {
      const user = newUser();
      const { unmount } = renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      expect(await screen.findByText('Reassign to')).toBeInTheDocument();
      expect(screen.getByText('Live timer')).toBeInTheDocument();
      unmount();

      asAdmin();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      expect(await screen.findByText('Opis zadatka Zebra plan')).toBeInTheDocument();
      expect(screen.queryByText('Reassign to')).not.toBeInTheDocument();
      expect(screen.queryByText('Live timer')).not.toBeInTheDocument();
    });

    it('shows what the panel changed, and counts the tasks again, when the timer is started', async () => {
      const user = newUser();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      await user.click(await screen.findByRole('button', { name: 'Start' }));

      await waitFor(() => expect(tab(IN_PROGRESS)).toHaveTextContent('In Progress (2)'));
      expect(tab(PENDING)).toHaveTextContent('Opened (Pending) (1)');
      expect(server.sent('POST', /work-tasks\/t1\/start$/)).toHaveLength(1);
      expect(listRequests().length).toBeGreaterThan(1);
    });
  });

  describe('what it used to get wrong', () => {
    it('says so when the tasks could not be loaded, instead of "No tasks in this status yet." and a count of nought', async () => {
      renderPage(seed(), (s) => s.refuse('GET', /^\/api\/work-tasks$/, 500, {}, -1));

      expect(await screen.findByText('The tasks could not be loaded. Try again in a moment.')).toBeInTheDocument();
      expect(screen.queryByText('No tasks in this status yet.')).not.toBeInTheDocument();
      expect(tab(PENDING).textContent).toBe('Opened (Pending)');
    });

    it('says that the tasks are on their way, and counts nothing, until they have come', async () => {
      const holding = { release: () => {} };
      renderPage(seed(), (s) => Object.assign(holding, s.hold('GET', /^\/api\/work-tasks$/)));

      expect(await screen.findByText('Loading tasks…')).toBeInTheDocument();
      expect(screen.queryByText('No tasks in this status yet.')).not.toBeInTheDocument();
      expect(tab(PENDING).textContent).toBe('Opened (Pending)');

      holding.release();
      await screen.findByText('Zebra plan');
      expect(tab(PENDING)).toHaveTextContent('Opened (Pending) (2)');
      expect(screen.queryByText('Loading tasks…')).not.toBeInTheDocument();
    });

    it('shows the answer for the branch that is chosen even when the answer for the one before arrives last', async () => {
      const user = newUser();
      asAdmin();
      const state = seed();
      state.tasks.push(makeTask('t5', 'Mostar job', { to: 'u3', toName: 'Dino Dinić', department: ['d2', 'Mostar'] }));
      let all!: { release: () => void };
      renderPage(state, (s) => (all = s.hold('GET', /^\/api\/work-tasks$/)));
      await screen.findByRole('option', { name: 'Mostar' });

      await user.selectOptions(screen.getByRole('option', { name: 'All branches' }).closest('select')!, 'd2');
      await waitFor(() => expect(titles()).toEqual(['Mostar job']));
      all.release();
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(titles()).toEqual(['Mostar job']);
    });

    it('shows the task that was chosen last under its row even when the answer for the one before arrives last', async () => {
      const user = newUser();
      renderPage();
      const zebra = await screen.findByRole('button', { name: 'Zebra plan' });
      const first = server.hold('GET', /work-tasks\/t1$/);
      await user.click(zebra);

      await user.click(screen.getByRole('button', { name: 'alpha report' }));
      await screen.findByText('Opis zadatka alpha report');
      first.release();
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(screen.getByText('Opis zadatka alpha report')).toBeInTheDocument();
      expect(screen.queryByText('Opis zadatka Zebra plan')).not.toBeInTheDocument();
    });

    it('sends the title and the description without the spaces around them', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });

      await user.type(screen.getByLabelText('Title'), '  Napravi plan ');
      await user.type(screen.getByLabelText('Description'), ' Za iduću sedmicu  ');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');
      await user.click(screen.getByRole('button', { name: 'Create task' }));

      await screen.findByText('Napravi plan');
      expect(server.sent('POST', /work-tasks$/).map((r) => r.body)).toEqual([{ title: 'Napravi plan', description: 'Za iduću sedmicu', assignedToUserId: 'u2' }]);
    });

    it('says that the task was made, and takes it back when something new is written', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });
      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), 'Za iduću sedmicu');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');

      await user.click(screen.getByRole('button', { name: 'Create task' }));
      expect(await screen.findByText('Task created.')).toBeInTheDocument();

      await user.type(screen.getByLabelText('Title'), 'N');
      expect(screen.queryByText('Task created.')).not.toBeInTheDocument();
    });

    it('takes back "Task created." when the form is sent again, and says what is wrong with it', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });
      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), 'Za iduću sedmicu');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');
      await user.click(screen.getByRole('button', { name: 'Create task' }));
      await screen.findByText('Task created.');

      await user.click(screen.getByRole('button', { name: 'Create task' }));

      expect(screen.getByText('Title, description and assignee are all required.')).toBeInTheDocument();
      expect(screen.queryByText('Task created.')).not.toBeInTheDocument();
    });

    it('does not say that a task was made when it was not', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');
      await screen.findByRole('option', { name: 'Bego Begić' });
      await user.type(screen.getByLabelText('Title'), 'Napravi plan');
      await user.type(screen.getByLabelText('Description'), 'Za iduću sedmicu');
      await user.selectOptions(screen.getByLabelText('Assign to'), 'u2');
      server.refuse('POST', /work-tasks$/, 400, { message: 'No.' });

      await user.click(screen.getByRole('button', { name: 'Create task' }));

      expect(await screen.findByText('No.')).toBeInTheDocument();
      expect(screen.queryByText('Task created.')).not.toBeInTheDocument();
    });

    it('puts names with accented letters where they belong in the alphabet, not after Z', async () => {
      const user = newUser();
      const state = newTasksState();
      state.tasks.push(
        makeTask('t1', 'One', { toName: 'Zora Zorić' }),
        makeTask('t2', 'Two', { toName: 'Šime Šimić' }),
        makeTask('t3', 'Three', { toName: 'Amra Ahmetović' })
      );
      renderPage(state);
      await screen.findByText('One');

      await user.click(within(heading('Assigned to')).getByRole('button'));

      expect(titles()).toEqual(['Three', 'Two', 'One']);
    });

    it('puts the times in order when they are the same second and differ in the fraction of it', async () => {
      const user = newUser();
      const state = newTasksState();
      state.tasks.push(
        makeTask('t1', 'Whole second', { sessions: [['2026-06-10T09:00:00Z', '2026-06-10T10:00:00Z']] }),
        makeTask('t2', 'Half a second later', { sessions: [['2026-06-10T09:00:00.5Z', '2026-06-10T10:00:00Z']] }),
        makeTask('t3', 'Half a second earlier', { sessions: [['2026-06-10T08:59:59.5Z', '2026-06-10T10:00:00Z']] })
      );
      renderPage(state);
      await user.click(await screen.findByText(DONE));

      await user.click(within(heading('Started')).getByRole('button'));

      expect(titles()).toEqual(['Half a second earlier', 'Whole second', 'Half a second later']);
    });

    it('says so when the colleagues could not be loaded, or when the employee is in no branch and so has none', async () => {
      const first = renderPage(seed(), (s) => s.refuse('GET', /^\/api\/users$/, 500, {}, -1));
      expect(await screen.findByText('The list of colleagues could not be loaded.')).toBeInTheDocument();
      first.unmount();

      asEmployee({ departmentId: null });
      renderPage();
      expect(await screen.findByText('You are not in a branch, so there are no colleagues to choose from.')).toBeInTheDocument();
    });

    it('does not say that when there are colleagues', async () => {
      renderPage();
      await screen.findByRole('option', { name: 'Bego Begić' });

      expect(screen.queryByText(/could not be loaded/)).not.toBeInTheDocument();
      expect(screen.queryByText(/You are not in a branch/)).not.toBeInTheDocument();
    });

    it('says so when the branches could not be loaded, since the filter would otherwise offer nothing and give no reason', async () => {
      asAdmin();
      renderPage(seed(), (s) => s.refuse('GET', /^\/api\/departments$/, 500, {}, -1));

      expect(await screen.findByText('The list of branches could not be loaded.')).toBeInTheDocument();
    });

    it('names the branch filter, so that a screen reader can tell what it is for', async () => {
      asAdmin();
      renderPage();
      await screen.findByText('Zebra plan');

      expect(screen.getByRole('combobox', { name: 'Branch' })).toBeInTheDocument();
    });

    it('has the statuses as tabs, with the one that is chosen said to be chosen', async () => {
      const user = newUser();
      renderPage();
      await screen.findByText('Zebra plan');

      const tabs = screen.getAllByRole('tab');
      expect(tabs.map((x) => x.textContent)).toEqual(['Opened (Pending) (2)', 'In Progress (1)', 'Done (1)']);
      expect(tabs.map((x) => x.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);

      await user.click(screen.getByRole('tab', { name: /^Done/ }));
      expect(screen.getAllByRole('tab').map((x) => x.getAttribute('aria-selected'))).toEqual(['false', 'false', 'true']);
    });

    it('follows the task to its new status when what is done to it changes the status, so that it does not vanish from under the hand that did it', async () => {
      const user = newUser();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));

      await user.click(await screen.findByRole('button', { name: 'Start' }));

      expect(await screen.findByRole('button', { name: 'Stop' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /^In Progress/ })).toHaveAttribute('aria-selected', 'true');
      expect(titles()).toContain('Zebra plan');
    });

    it('stays on the status that is chosen when what is done to a task does not change it', async () => {
      const user = newUser();
      renderPage();
      await user.click(await screen.findByRole('button', { name: 'Zebra plan' }));
      await screen.findByRole('button', { name: 'Start' });
      const reassign = within(screen.getByText('Reassign to').parentElement!).getByRole('combobox');
      await within(reassign).findByRole('option', { name: 'Bego Begić' });
      await user.selectOptions(reassign, 'u2');

      await user.click(screen.getByRole('button', { name: 'Reassign' }));

      await waitFor(() => expect(server.sent('POST', /reassign$/)).toHaveLength(1));
      await waitFor(() => expect(screen.getByText('Assignment history').parentElement).toHaveTextContent('Amra Ahmetović assigned to Bego Begić'));
      expect(titles()).toContain('Zebra plan');
      expect(tab(PENDING)).toHaveTextContent('Opened (Pending) (2)');
    });
  });
});
