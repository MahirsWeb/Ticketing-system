import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TaskDetailPanel } from './TaskDetailPanel';
import { workTasksApi } from '../../api/workTasks';
import { usersApi } from '../../api/users';
import { renderWithQuery } from '../../test/renderWithQuery';
import type { UserListItemDto, WorkTaskDetailDto } from '../../types';

vi.mock('../../api/workTasks', () => ({ workTasksApi: { setTime: vi.fn(), start: vi.fn(), stop: vi.fn(), reassign: vi.fn() } }));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn() } }));

const local = (hour: number, minute = 0) => new Date(2026, 5, 10, hour, minute);
const colleague = (id: string, first: string, last: string) => ({ id, firstName: first, lastName: last, role: 'Employee' }) as UserListItemDto;

function task(sessions: { start: Date; end: Date | null }[], extra: Partial<WorkTaskDetailDto> = {}): WorkTaskDetailDto {
  return {
    id: 'task-1', title: 'Check VPN', description: 'd', status: 'Done', departmentId: 'dep', departmentName: 'Branch',
    createdByUserId: 'u1', createdByName: 'A', assignedByUserId: 'u1', assignedByName: 'A', assignedToUserId: 'u1', assignedToName: 'A',
    startedAtUtc: sessions[0]?.start.toISOString() ?? null,
    endedAtUtc: sessions.some((s) => s.end === null) ? null : (sessions.at(-1)?.end?.toISOString() ?? null),
    createdAt: local(8).toISOString(), history: [],
    sessions: sessions.map((s, i) => ({ id: `s${i}`, userId: 'u1', userName: 'Amra Ahmetović', startedAtUtc: s.start.toISOString(), endedAtUtc: s.end?.toISOString() ?? null })),
    ...extra,
  };
}

function renderPanel(t: WorkTaskDetailDto, props: { currentUserId?: string; canManage?: boolean; onChanged?: (t: WorkTaskDetailDto) => void } = {}) {
  return renderWithQuery(
    <TaskDetailPanel task={t} currentUserId={props.currentUserId ?? 'u1'} canManage={props.canManage ?? true} onChanged={props.onChanged ?? (() => {})} />
  );
}

describe('the task panel', () => {
  beforeEach(() => {
    vi.mocked(workTasksApi.setTime).mockReset();
    vi.mocked(workTasksApi.start).mockReset();
    vi.mocked(workTasksApi.stop).mockReset();
    vi.mocked(workTasksApi.reassign).mockReset();
    vi.mocked(usersApi.list).mockReset().mockResolvedValue([]);
  });

  it('lists every work session of the task, so starting again does not hide the earlier work (TS-33.1)', () => {
    renderPanel(task([{ start: local(9), end: local(10) }, { start: local(13), end: local(14, 30) }]));

    const heading = screen.getByText('Work sessions');
    const list = heading.parentElement!.querySelector('ul')!;
    expect(list.querySelectorAll('li')).toHaveLength(2);
    expect(list).toHaveTextContent('10.06.2026 09:00 → 10:00');
    expect(list).toHaveTextContent('10.06.2026 13:00 → 14:30');
  });

  it('says so when no work was logged and shows a running session as running', () => {
    const { unmount } = renderPanel(task([]));
    expect(screen.getByText('No work has been logged on this task yet.')).toBeInTheDocument();
    unmount();

    renderPanel(task([{ start: local(9), end: null }]));
    expect(screen.getByText(/running/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Stop/ })).toBeInTheDocument();
  });

  it('corrects the LATEST session, not a stretch spanning all of them', async () => {
    vi.mocked(workTasksApi.setTime).mockResolvedValue(task([]));
    renderPanel(task([{ start: local(9), end: local(10) }, { start: local(13), end: local(14) }]));

    const [start, end] = Array.from(document.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]'));
    expect(start.value).toBe('2026-06-10T13:00'); // not 09:00, the start of the first session
    expect(end.value).toBe('2026-06-10T14:00');

    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(workTasksApi.setTime).toHaveBeenCalledWith('task-1', local(13).toISOString(), local(14).toISOString());
  });

  describe('reassigning', () => {
    const colleagues = [colleague('u1', 'Amra', 'Ahmetović'), colleague('u2', 'Bego', 'Begić'), colleague('u3', 'Cana', 'Čaušević')];
    const choose = async (user: ReturnType<typeof userEvent.setup>, name: string) => {
      const select = within(screen.getByText('Reassign to').parentElement!).getByRole('combobox');
      await within(select).findByRole('option', { name });
      await user.selectOptions(select, name);
      return select;
    };

    it('offers the colleagues of the branch of the task, except the one who has it, and asks for them by that branch', async () => {
      vi.mocked(usersApi.list).mockResolvedValue(colleagues);
      renderPanel(task([]));

      const select = within(screen.getByText('Reassign to').parentElement!).getByRole('combobox');
      await within(select).findByRole('option', { name: 'Bego Begić' });

      expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Choose a colleague…', 'Bego Begić', 'Cana Čaušević']);
      expect(usersApi.list).toHaveBeenCalledWith({ role: 'Employee', departmentId: 'dep' });
    });

    it('does not ask for colleagues, or offer reassigning, to somebody who cannot manage the task', async () => {
      renderPanel(task([]), { canManage: false });

      expect(screen.queryByText('Reassign to')).not.toBeInTheDocument();
      expect(usersApi.list).not.toHaveBeenCalled();
    });

    it('hands the task to the colleague that was chosen, passes on the changed task, and lets go of the choice', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.list).mockResolvedValue(colleagues);
      const changed = task([], { assignedToUserId: 'u2' });
      vi.mocked(workTasksApi.reassign).mockResolvedValue(changed);
      const onChanged = vi.fn();
      renderPanel(task([]), { onChanged });
      const select = await choose(user, 'Bego Begić');

      await user.click(screen.getByRole('button', { name: 'Reassign' }));

      await waitFor(() => expect(onChanged).toHaveBeenCalledWith(changed));
      expect(workTasksApi.reassign).toHaveBeenCalledWith('task-1', 'u2');
      await waitFor(() => expect(select).toHaveValue(''));
    });

    it('keeps the colleague that was chosen when the server refuses, and says what it said', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.list).mockResolvedValue(colleagues);
      vi.mocked(workTasksApi.reassign).mockRejectedValue({ response: { data: { message: 'That person is on leave.' } } });
      const onChanged = vi.fn();
      renderPanel(task([]), { onChanged });
      const select = await choose(user, 'Bego Begić');

      await user.click(screen.getByRole('button', { name: 'Reassign' }));

      expect(await screen.findByText('That person is on leave.')).toBeInTheDocument();
      expect(select).toHaveValue('u2');
      expect(onChanged).not.toHaveBeenCalled();
    });

    it('says plainly that it did not work when the server said nothing', async () => {
      const user = userEvent.setup();
      vi.mocked(usersApi.list).mockResolvedValue(colleagues);
      vi.mocked(workTasksApi.reassign).mockRejectedValue(new Error('network'));
      renderPanel(task([]));
      await choose(user, 'Bego Begić');

      await user.click(screen.getByRole('button', { name: 'Reassign' }));

      expect(await screen.findByText('Action failed.')).toBeInTheDocument();
    });

    it('cannot be asked for until somebody is chosen', async () => {
      vi.mocked(usersApi.list).mockResolvedValue(colleagues);
      renderPanel(task([]));

      expect(screen.getByRole('button', { name: 'Reassign' })).toBeDisabled();
    });

    it('says so when the colleagues could not be loaded', async () => {
      vi.mocked(usersApi.list).mockRejectedValue(new Error('network'));
      renderPanel(task([]));

      expect(await screen.findByText('The list of colleagues could not be loaded.')).toBeInTheDocument();
    });

    it('does not say so when they could', async () => {
      vi.mocked(usersApi.list).mockResolvedValue(colleagues);
      renderPanel(task([]));
      await screen.findByRole('option', { name: 'Bego Begić' });

      expect(screen.queryByText(/could not be loaded/)).not.toBeInTheDocument();
    });
  });

  describe('the live timer', () => {
    it('is the assignee\'s: a colleague who manages the task but does not have it sees no timer', () => {
      renderPanel(task([]), { currentUserId: 'u9' });

      expect(screen.queryByText('Live timer')).not.toBeInTheDocument();
    });

    it('starts when it is not running, and passes on the changed task', async () => {
      const user = userEvent.setup();
      const changed = task([{ start: local(9), end: null }]);
      vi.mocked(workTasksApi.start).mockResolvedValue(changed);
      const onChanged = vi.fn();
      renderPanel(task([]), { onChanged });

      await user.click(screen.getByRole('button', { name: 'Start' }));

      await waitFor(() => expect(onChanged).toHaveBeenCalledWith(changed));
      expect(workTasksApi.start).toHaveBeenCalledWith('task-1');
      expect(workTasksApi.stop).not.toHaveBeenCalled();
    });

    it('stops when it is running', async () => {
      const user = userEvent.setup();
      vi.mocked(workTasksApi.stop).mockResolvedValue(task([{ start: local(9), end: local(10) }]));
      renderPanel(task([{ start: local(9), end: null }]));

      await user.click(screen.getByRole('button', { name: 'Stop' }));

      await waitFor(() => expect(workTasksApi.stop).toHaveBeenCalledWith('task-1'));
      expect(workTasksApi.start).not.toHaveBeenCalled();
    });

    it('cannot be pressed again while the first press is being answered', async () => {
      const user = userEvent.setup();
      let answer!: (t: WorkTaskDetailDto) => void;
      vi.mocked(workTasksApi.start).mockReturnValue(new Promise((resolve) => (answer = resolve)));
      renderPanel(task([]));

      await user.click(screen.getByRole('button', { name: 'Start' }));

      expect(screen.getByRole('button', { name: 'Start' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Reassign' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
      answer(task([{ start: local(9), end: null }]));
      await waitFor(() => expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled());
    });

    it('says what the server said when it refuses', async () => {
      const user = userEvent.setup();
      vi.mocked(workTasksApi.start).mockRejectedValue({ response: { data: { message: 'The timer is already running.' } } });
      renderPanel(task([]));

      await user.click(screen.getByRole('button', { name: 'Start' }));

      expect(await screen.findByText('The timer is already running.')).toBeInTheDocument();
    });
  });

  describe('the correction of the time', () => {
    it('sends no time for a field that was emptied', async () => {
      const user = userEvent.setup();
      vi.mocked(workTasksApi.setTime).mockResolvedValue(task([]));
      renderPanel(task([{ start: local(9), end: local(10) }]));
      const [, end] = Array.from(document.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]'));

      await user.clear(end);
      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(workTasksApi.setTime).toHaveBeenCalledWith('task-1', local(9).toISOString(), null);
    });

    it('shows the times of the task when it is replaced by one with other times, as after a correction', async () => {
      const user = userEvent.setup();
      function Swap() {
        const [shown, setShown] = useState(task([{ start: local(9), end: local(10) }]));
        return (
          <>
            <button onClick={() => setShown(task([{ start: local(11), end: local(12) }]))}>replace the task</button>
            <TaskDetailPanel task={shown} currentUserId="u1" canManage onChanged={() => {}} />
          </>
        );
      }
      renderWithQuery(<Swap />);
      const [start, end] = Array.from(document.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]'));
      expect([start.value, end.value]).toEqual(['2026-06-10T09:00', '2026-06-10T10:00']);

      await user.click(screen.getByRole('button', { name: 'replace the task' }));

      expect([start.value, end.value]).toEqual(['2026-06-10T11:00', '2026-06-10T12:00']);
    });
  });
});
