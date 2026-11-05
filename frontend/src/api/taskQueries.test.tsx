import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithQuery } from '../test/renderWithQuery';
import { useTask, useTaskList } from './taskQueries';
import { taskKeys } from './taskKeys';
import { workTasksApi, type WorkTaskListParams } from './workTasks';
import { queryClient } from './queryClient';
import { apiClient } from './client';
import type { WorkTaskDetailDto } from '../types';

vi.mock('./client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), patch: vi.fn() } }));

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const asked = (url: string) => get.mock.calls.filter((c) => c[0] === url).length;

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const detail = (id: string, status: WorkTaskDetailDto['status'], extra: Partial<WorkTaskDetailDto> = {}): WorkTaskDetailDto => ({
  id, title: `Task ${id}`, description: `About ${id}`, status, departmentId: 'd1', departmentName: 'Sarajevo',
  createdByUserId: 'u2', createdByName: 'Bego', assignedByUserId: 'u2', assignedByName: 'Bego', assignedToUserId: 'u1', assignedToName: 'Amra',
  startedAtUtc: null, endedAtUtc: null, createdAt: '2026-06-01T08:00:00Z', history: [], sessions: [], ...extra,
});
const row = (id: string, status: WorkTaskDetailDto['status']) => {
  const { description: _d, history: _h, sessions: _s, ...item } = detail(id, status);
  return item;
};

function List({ params = {}, tag = 'list' }: { params?: WorkTaskListParams; tag?: string }) {
  const { data, isError } = useTaskList(params);
  return <p data-testid={tag}>{isError ? 'failed' : data ? data.map((t) => `${t.id}:${t.status}:${t.assignedToName}`).join(',') : 'loading'}</p>;
}
function One({ id }: { id: string | null }) {
  const { data, isError } = useTask(id);
  return <p data-testid="one">{isError ? 'failed' : data ? `${data.title}:${data.status}` : 'loading'}</p>;
}
function Switch() {
  const [id, setId] = useState('t1');
  return (
    <>
      <button onClick={() => setId('t2')}>open the other task</button>
      <One id={id} />
    </>
  );
}

describe('the task data the screens share (T-17)', () => {
  beforeEach(() => {
    queryClient.clear();
    get.mockReset();
    post.mockReset();
    vi.mocked(apiClient.patch).mockReset();
    get.mockImplementation((async (url: string) => {
      if (url === '/api/work-tasks') return { data: [row('t1', 'Pending')] };
      return { data: detail('t1', 'Pending') };
    }) as never);
  });
  afterEach(() => queryClient.clear());

  it('asks the same list once however many places show it, and a different list on its own', async () => {
    renderWithQuery(
      <>
        <List tag="a" />
        <List tag="b" />
        <List tag="mine" params={{ assignedToUserId: 'u1' }} />
      </>,
      queryClient
    );

    await screen.findAllByText('t1:Pending:Amra');
    expect(asked('/api/work-tasks')).toBe(2);
    expect(get.mock.calls.filter((c) => c[0] === '/api/work-tasks').map((c) => (c[1] as { params: object }).params)).toEqual(
      expect.arrayContaining([{}, { assignedToUserId: 'u1' }])
    );
  });

  it('forgets a list when it is left: coming back to it asks again and shows it as loading first', async () => {
    const first = renderWithQuery(<List />, queryClient);
    await screen.findByText('t1:Pending:Amra');
    first.unmount();
    await waitFor(() => expect(queryClient.getQueryData(taskKeys.list({}))).toBeUndefined());

    renderWithQuery(<List />, queryClient);

    expect(screen.getByTestId('list')).toHaveTextContent('loading');
    await screen.findByText('t1:Pending:Amra');
    expect(asked('/api/work-tasks')).toBe(2);
  });

  it('forgets a task when it is closed, so that another one is never shown with what the first had', async () => {
    const first = renderWithQuery(<One id="t1" />, queryClient);
    await screen.findByText('Task t1:Pending');
    first.unmount();

    await waitFor(() => expect(queryClient.getQueryData(taskKeys.detail('t1'))).toBeUndefined());
  });

  it('asks for no task while none is open', () => {
    renderWithQuery(<One id={null} />, queryClient);

    expect(asked('/api/work-tasks/')).toBe(0);
    expect(get).not.toHaveBeenCalled();
  });

  it('keeps each task as an entry of its own, so a slow answer for the one that was open before is never shown for the one that is open now', async () => {
    const slow = deferred<{ data: WorkTaskDetailDto }>();
    get.mockImplementation((async (url: string) => (url === '/api/work-tasks/t1' ? slow.promise : { data: detail('t2', 'Done') })) as never);
    renderWithQuery(<Switch />, queryClient);

    await userEvent.click(screen.getByRole('button', { name: 'open the other task' }));
    await screen.findByText('Task t2:Done');
    await act(async () => slow.resolve({ data: detail('t1', 'Pending') }));

    expect(screen.getByTestId('one')).toHaveTextContent('Task t2:Done');
  });

  describe('what is done to a task', () => {
    it('replaces the task that is open with the one the server sent back, without asking for it again', async () => {
      post.mockResolvedValue({ data: detail('t1', 'InProgress', { sessions: [{ id: 's', userId: 'u1', userName: 'Amra', startedAtUtc: '2026-06-10T09:00:00Z', endedAtUtc: null }] }) } as never);
      renderWithQuery(<One id="t1" />, queryClient);
      await screen.findByText('Task t1:Pending');

      await act(async () => {
        await workTasksApi.start('t1');
      });

      await waitFor(() => expect(screen.getByTestId('one')).toHaveTextContent('Task t1:InProgress'));
      expect(asked('/api/work-tasks/t1')).toBe(1);
    });

    it('changes the rows of the lists on screen at once, and asks the lists again', async () => {
      const second = deferred<{ data: unknown }>();
      renderWithQuery(<List />, queryClient);
      await screen.findByText('t1:Pending:Amra');
      get.mockImplementation((() => second.promise) as never);
      post.mockResolvedValue({ data: detail('t1', 'InProgress', { assignedToUserId: 'u9', assignedToName: 'Dino' }) } as never);

      await act(async () => {
        await workTasksApi.reassign('t1', 'u9');
      });

      // The server has not answered the second request yet, and the row is already what the change made it.
      await waitFor(() => expect(screen.getByTestId('list')).toHaveTextContent('t1:InProgress:Dino'));
      expect(asked('/api/work-tasks')).toBe(2);
      await act(async () => second.resolve({ data: [row('t1', 'InProgress')] }));
    });

    it('leaves the rows of other tasks as they are', async () => {
      get.mockImplementation((async () => ({ data: [row('t1', 'Pending'), row('t2', 'Done')] })) as never);
      renderWithQuery(<List />, queryClient);
      await screen.findByText('t1:Pending:Amra,t2:Done:Amra');
      get.mockImplementation((() => new Promise(() => {})) as never);
      post.mockResolvedValue({ data: detail('t1', 'InProgress') } as never);

      await act(async () => {
        await workTasksApi.start('t1');
      });

      await waitFor(() => expect(screen.getByTestId('list')).toHaveTextContent('t1:InProgress:Amra,t2:Done:Amra'));
    });

    it('puts nothing into memory for a task that nobody has open', async () => {
      post.mockResolvedValue({ data: detail('t9', 'InProgress') } as never);

      await workTasksApi.start('t9');

      expect(queryClient.getQueryData(taskKeys.detail('t9'))).toBeUndefined();
    });

    it.each([
      ['reassign', () => workTasksApi.reassign('t1', 'u9'), 'post', '/api/work-tasks/t1/reassign'],
      ['start', () => workTasksApi.start('t1'), 'post', '/api/work-tasks/t1/start'],
      ['stop', () => workTasksApi.stop('t1'), 'post', '/api/work-tasks/t1/stop'],
      ['time', () => workTasksApi.setTime('t1', null, null), 'patch', '/api/work-tasks/t1/time'],
    ])('%s goes to the right address and refreshes the lists', async (_name, call, method, url) => {
      renderWithQuery(<List />, queryClient);
      await screen.findByText('t1:Pending:Amra');
      const sender = method === 'post' ? post : vi.mocked(apiClient.patch);
      sender.mockResolvedValue({ data: detail('t1', 'Pending') } as never);

      await act(async () => {
        await call();
      });

      expect(sender.mock.calls.map((c) => c[0])).toEqual([url]);
      await waitFor(() => expect(asked('/api/work-tasks')).toBe(2));
    });

    it('does not touch what is shown when the server refuses', async () => {
      renderWithQuery(<List />, queryClient);
      await screen.findByText('t1:Pending:Amra');
      post.mockRejectedValue(new Error('refused'));

      await act(async () => {
        await workTasksApi.start('t1').catch(() => undefined);
      });

      expect(screen.getByTestId('list')).toHaveTextContent('t1:Pending:Amra');
      expect(asked('/api/work-tasks')).toBe(1);
    });

    it('asks the lists again when a task is made, and puts nothing else into memory', async () => {
      renderWithQuery(<List />, queryClient);
      await screen.findByText('t1:Pending:Amra');
      post.mockResolvedValue({ data: detail('t5', 'Pending') } as never);

      await act(async () => {
        await workTasksApi.create('Napravi plan', 'Opis', 'u2');
      });

      await waitFor(() => expect(asked('/api/work-tasks')).toBe(2));
      expect(post.mock.calls).toEqual([['/api/work-tasks', { title: 'Napravi plan', description: 'Opis', assignedToUserId: 'u2' }]]);
      expect(queryClient.getQueryData(taskKeys.detail('t5'))).toBeUndefined();
    });
  });
});
