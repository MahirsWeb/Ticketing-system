import { describe, expect, it } from 'vitest';
import { countByStatus, sortTasks } from './taskSorting';
import type { WorkTaskListItem } from '../../types';

function task(id: string, extra: Partial<WorkTaskListItem> = {}): WorkTaskListItem {
  return {
    id, title: id, status: 'Pending', departmentId: 'd', departmentName: 'Branch', createdByUserId: 'u', createdByName: 'U', assignedByUserId: 'u',
    assignedByName: 'U', assignedToUserId: 'u', assignedToName: 'U', startedAtUtc: null, endedAtUtc: null, createdAt: '2026-06-01T08:00:00Z', ...extra,
  };
}
const ids = (tasks: WorkTaskListItem[]) => tasks.map((t) => t.id);

describe('sorting tasks', () => {
  it('leaves the tasks as they are when no column is chosen, or one that is not a column', () => {
    const tasks = [task('b'), task('a')];

    expect(sortTasks(tasks, null, 'asc')).toBe(tasks);
    expect(sortTasks(tasks, 'nothing', 'desc')).toBe(tasks);
  });

  it('does not change the list it was given', () => {
    const tasks = [task('b', { title: 'B' }), task('a', { title: 'A' })];

    sortTasks(tasks, 'title', 'asc');

    expect(ids(tasks)).toEqual(['b', 'a']);
  });

  it('puts the names in order, and the other way round when descending', () => {
    const tasks = [task('1', { title: 'Beta' }), task('2', { title: 'Gamma' }), task('3', { title: 'Alpha' })];

    expect(ids(sortTasks(tasks, 'title', 'asc'))).toEqual(['3', '1', '2']);
    expect(ids(sortTasks(tasks, 'title', 'desc'))).toEqual(['2', '1', '3']);
  });

  it('does not tell a capital letter from a small one: names that differ only by it stay as they were', () => {
    const tasks = [task('1', { title: 'Banana' }), task('2', { title: 'Apple' }), task('3', { title: 'apple' })];

    expect(ids(sortTasks(tasks, 'title', 'asc'))).toEqual(['2', '3', '1']);
  });

  it('does tell a letter from the same letter with an accent, and places the accented one after it and before the next', () => {
    const tasks = [task('1', { assignedToName: 'Zora' }), task('2', { assignedToName: 'Šime' }), task('3', { assignedToName: 'Sime' }), task('4', { assignedToName: 'Tara' })];

    expect(ids(sortTasks(tasks, 'assignedTo', 'asc'))).toEqual(['3', '2', '4', '1']);
  });

  it('sorts by who gave the task and by the branch', () => {
    const tasks = [task('1', { assignedByName: 'Cana', departmentName: 'Mostar' }), task('2', { assignedByName: 'Amra', departmentName: 'Tuzla' }), task('3', { assignedByName: 'Bego', departmentName: 'Bihać' })];

    expect(ids(sortTasks(tasks, 'assignedBy', 'asc'))).toEqual(['2', '3', '1']);
    expect(ids(sortTasks(tasks, 'branch', 'asc'))).toEqual(['3', '1', '2']);
  });

  it('puts times in order as times, and the ones with no time before all the others when ascending and after them when descending', () => {
    const tasks = [
      task('late', { startedAtUtc: '2026-06-10T12:00:00Z' }),
      task('none'),
      task('early', { startedAtUtc: '2026-06-08T09:00:00Z' }),
      task('fraction', { startedAtUtc: '2026-06-10T11:59:59.5Z' }),
    ];

    expect(ids(sortTasks(tasks, 'started', 'asc'))).toEqual(['none', 'early', 'fraction', 'late']);
    expect(ids(sortTasks(tasks, 'started', 'desc'))).toEqual(['late', 'fraction', 'early', 'none']);
  });

  it('sorts by when work ended separately from when it started', () => {
    const tasks = [
      task('a', { startedAtUtc: '2026-06-01T09:00:00Z', endedAtUtc: '2026-06-09T09:00:00Z' }),
      task('b', { startedAtUtc: '2026-06-05T09:00:00Z', endedAtUtc: '2026-06-06T09:00:00Z' }),
    ];

    expect(ids(sortTasks(tasks, 'started', 'asc'))).toEqual(['a', 'b']);
    expect(ids(sortTasks(tasks, 'ended', 'asc'))).toEqual(['b', 'a']);
  });
});

describe('counting tasks', () => {
  it('counts each status, and a status with no tasks as nought', () => {
    expect(countByStatus([task('1'), task('2', { status: 'Done' }), task('3', { status: 'Done' })])).toEqual({ Pending: 1, InProgress: 0, Done: 2 });
    expect(countByStatus([])).toEqual({ Pending: 0, InProgress: 0, Done: 0 });
  });
});
