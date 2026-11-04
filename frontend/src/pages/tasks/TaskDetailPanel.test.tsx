import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TaskDetailPanel } from './TaskDetailPanel';
import { workTasksApi } from '../../api/workTasks';
import { usersApi } from '../../api/users';
import type { WorkTaskDetailDto } from '../../types';

vi.mock('../../api/workTasks', () => ({ workTasksApi: { setTime: vi.fn(), start: vi.fn(), stop: vi.fn(), reassign: vi.fn() } }));
vi.mock('../../api/users', () => ({ usersApi: { list: vi.fn() } }));

const local = (hour: number, minute = 0) => new Date(2026, 5, 10, hour, minute);

function task(sessions: { start: Date; end: Date | null }[]): WorkTaskDetailDto {
  return {
    id: 'task-1', title: 'Check VPN', description: 'd', status: 'Done', departmentId: 'dep', departmentName: 'Branch',
    createdByUserId: 'u1', createdByName: 'A', assignedByUserId: 'u1', assignedByName: 'A', assignedToUserId: 'u1', assignedToName: 'A',
    startedAtUtc: sessions[0]?.start.toISOString() ?? null,
    endedAtUtc: sessions.some((s) => s.end === null) ? null : (sessions.at(-1)?.end?.toISOString() ?? null),
    createdAt: local(8).toISOString(), history: [],
    sessions: sessions.map((s, i) => ({ id: `s${i}`, userId: 'u1', userName: 'Amra Ahmetović', startedAtUtc: s.start.toISOString(), endedAtUtc: s.end?.toISOString() ?? null })),
  };
}

function renderPanel(t: WorkTaskDetailDto) {
  return render(<TaskDetailPanel task={t} currentUserId="u1" canManage onChanged={() => {}} />);
}

describe('the task panel', () => {
  beforeEach(() => {
    vi.mocked(workTasksApi.setTime).mockReset();
    vi.mocked(usersApi.list).mockResolvedValue([]);
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
});
