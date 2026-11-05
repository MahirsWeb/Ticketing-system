import { vi } from 'vitest';
import { apiClient } from '../api/client';

/**
 * A stand-in for the part of the server that the tasks page talks to, kept in memory and answering at the level of HTTP (what `apiClient`
 * is asked), so that a test says what a person does and what the server answers, and does not depend on how the page loads or refreshes
 * its lists. The rules the real server has, and the page has to live with, are here too: a task's status comes from its work sessions (none:
 * pending, one running: in progress, all ended: done), and only the person who has a task can start its timer.
 *
 * The test file must mock `../api/client` for this to take effect (`get`, `post` and `patch`).
 */

export interface FakePerson {
  id: string;
  firstName: string;
  lastName: string;
  role: 'Employee';
  departmentId: string | null;
  departmentName: string | null;
}

export interface FakeSession {
  id: string;
  userId: string;
  userName: string;
  startedAtUtc: string;
  endedAtUtc: string | null;
}

export interface FakeTask {
  id: string;
  title: string;
  description: string;
  departmentId: string;
  departmentName: string;
  createdByUserId: string;
  createdByName: string;
  assignedByUserId: string;
  assignedByName: string;
  assignedToUserId: string;
  assignedToName: string;
  createdAt: string;
  history: { assignedByUserId: string; assignedByName: string; assignedToUserId: string; assignedToName: string; assignedAtUtc: string }[];
  sessions: FakeSession[];
}

export interface TasksState {
  tasks: FakeTask[];
  people: FakePerson[];
  departments: { id: string; name: string; email: string; isActive: boolean }[];
  /** The person who is signed in: the one who creates, and who the server takes for the one who assigns. */
  me: { id: string; name: string };
}

export const person = (id: string, first: string, last: string, departmentId: string | null, departmentName: string | null): FakePerson => ({
  id, firstName: first, lastName: last, role: 'Employee', departmentId, departmentName,
});

export function newTasksState(): TasksState {
  return {
    tasks: [],
    people: [
      person('u1', 'Amra', 'Ahmetović', 'd1', 'Sarajevo'),
      person('u2', 'Bego', 'Begić', 'd1', 'Sarajevo'),
      person('u3', 'Dino', 'Dinić', 'd2', 'Mostar'),
    ],
    departments: [
      { id: 'd1', name: 'Sarajevo', email: 's@firma.test', isActive: true },
      { id: 'd2', name: 'Mostar', email: 'm@firma.test', isActive: true },
    ],
    me: { id: 'u1', name: 'Amra Ahmetović' },
  };
}

/** A task as the server keeps it: `by` is who handed it out, `to` who has it; `sessions` say what has been done on it. */
export function makeTask(
  id: string,
  title: string,
  options: { to?: string; toName?: string; by?: string; byName?: string; department?: [string, string]; sessions?: [string, string | null][]; description?: string } = {}
): FakeTask {
  const to = options.to ?? 'u1';
  const toName = options.toName ?? 'Amra Ahmetović';
  const by = options.by ?? 'u2';
  const byName = options.byName ?? 'Bego Begić';
  const [departmentId, departmentName] = options.department ?? ['d1', 'Sarajevo'];
  return {
    id, title, description: options.description ?? `Opis zadatka ${title}`, departmentId, departmentName,
    createdByUserId: by, createdByName: byName, assignedByUserId: by, assignedByName: byName, assignedToUserId: to, assignedToName: toName,
    createdAt: new Date(2026, 5, 1, 8).toISOString(),
    history: [{ assignedByUserId: by, assignedByName: byName, assignedToUserId: to, assignedToName: toName, assignedAtUtc: new Date(2026, 5, 1, 8).toISOString() }],
    sessions: (options.sessions ?? []).map(([start, end], i) => ({ id: `${id}-s${i}`, userId: to, userName: toName, startedAtUtc: start, endedAtUtc: end })),
  };
}

const statusOf = (t: FakeTask) => (t.sessions.length === 0 ? 'Pending' : t.sessions.some((s) => s.endedAtUtc === null) ? 'InProgress' : 'Done');
const firstStart = (t: FakeTask) => t.sessions[0]?.startedAtUtc ?? null;
const lastEnd = (t: FakeTask) => (t.sessions.length === 0 || t.sessions.some((s) => s.endedAtUtc === null) ? null : t.sessions[t.sessions.length - 1].endedAtUtc);
const asListItem = (t: FakeTask) => {
  const { description: _d, history: _h, sessions: _s, ...rest } = t;
  return { ...rest, status: statusOf(t), startedAtUtc: firstStart(t), endedAtUtc: lastEnd(t) };
};
const asDetail = (t: FakeTask) => ({ ...t, status: statusOf(t), startedAtUtc: firstStart(t), endedAtUtc: lastEnd(t) });

export interface RequestRecord {
  method: string;
  url: string;
  body?: unknown;
  params?: Record<string, unknown>;
}

export function installTasksBackend(state: TasksState = newTasksState()) {
  const requests: RequestRecord[] = [];
  const failures: { method: string; pattern: RegExp; status: number; data: Record<string, unknown>; times: number }[] = [];
  const holds: { method: string; pattern: RegExp; release: () => void; gate: Promise<void> }[] = [];
  let next = 100;
  let tick = 0;

  const refuse = (status: number, data: Record<string, unknown>): never => {
    throw Object.assign(new Error(`The server said ${status}`), { response: { status, data } });
  };
  const find = (id: string) => state.tasks.find((t) => t.id === id) ?? refuse(404, { message: 'Task not found.' });
  const now = () => new Date(2026, 5, 20, 10, 0, tick++).toISOString();

  function handle(method: string, url: string, body: any, config: any): unknown {
    const params = config?.params;
    const failure = failures.find((f) => f.method === method && f.pattern.test(url) && f.times !== 0);
    if (failure) {
      if (failure.times > 0) failure.times--;
      refuse(failure.status, failure.data);
    }

    if (method === 'GET' && url === '/api/departments') return state.departments.filter((d) => d.isActive);
    if (method === 'GET' && url === '/api/users') {
      return state.people.filter((p) => (!params?.role || p.role === params.role) && (!params?.departmentId || p.departmentId === params.departmentId));
    }
    if (method === 'GET' && url === '/api/work-tasks') {
      return state.tasks
        .filter((t) => (!params?.departmentId || t.departmentId === params.departmentId) && (!params?.assignedToUserId || t.assignedToUserId === params.assignedToUserId) && (!params?.status || statusOf(t) === params.status))
        .map(asListItem);
    }
    if (method === 'POST' && url === '/api/work-tasks') {
      const assignee = state.people.find((p) => p.id === body.assignedToUserId) ?? refuse(400, { message: 'That person cannot be given a task.' });
      if (!String(body.title).trim()) refuse(400, { message: 'A task needs a title.' });
      const task = makeTask(`n${next++}`, body.title, {
        to: assignee.id, toName: `${assignee.firstName} ${assignee.lastName}`, by: state.me.id, byName: state.me.name,
        department: [assignee.departmentId ?? 'd1', assignee.departmentName ?? 'Sarajevo'], description: body.description,
      });
      state.tasks.push(task);
      return asDetail(task);
    }
    const m = url.match(/^\/api\/work-tasks\/([^/]+)(?:\/(reassign|time|start|stop))?$/);
    if (m) {
      const task = find(m[1]);
      const action = m[2];
      if (method === 'GET' && !action) return asDetail(task);
      if (method === 'POST' && action === 'reassign') {
        const to = state.people.find((p) => p.id === body.assignedToUserId) ?? refuse(400, { message: 'That person cannot be given a task.' });
        task.assignedToUserId = to.id;
        task.assignedToName = `${to.firstName} ${to.lastName}`;
        task.assignedByUserId = state.me.id;
        task.assignedByName = state.me.name;
        task.history.push({ assignedByUserId: state.me.id, assignedByName: state.me.name, assignedToUserId: to.id, assignedToName: task.assignedToName, assignedAtUtc: now() });
        return asDetail(task);
      }
      if (method === 'POST' && action === 'start') {
        if (task.assignedToUserId !== state.me.id) refuse(403, { message: 'Only the person who has the task can start its timer.' });
        if (task.sessions.some((s) => s.endedAtUtc === null)) refuse(400, { message: 'The timer is already running.' });
        task.sessions.push({ id: `${task.id}-s${task.sessions.length}`, userId: state.me.id, userName: state.me.name, startedAtUtc: now(), endedAtUtc: null });
        return asDetail(task);
      }
      if (method === 'POST' && action === 'stop') {
        const running = task.sessions.find((s) => s.endedAtUtc === null) ?? refuse(400, { message: 'The timer is not running.' });
        running.endedAtUtc = now();
        return asDetail(task);
      }
      if (method === 'PATCH' && action === 'time') {
        const latest = task.sessions[task.sessions.length - 1] ?? refuse(400, { message: 'No work has been logged on this task yet.' });
        if (body.endedAtUtc && body.startedAtUtc && body.endedAtUtc < body.startedAtUtc) refuse(400, { message: 'The end cannot be before the start.' });
        latest.startedAtUtc = body.startedAtUtc ?? latest.startedAtUtc;
        latest.endedAtUtc = body.endedAtUtc;
        return asDetail(task);
      }
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  }

  /** What the server said, as axios hands it over: a copy, so that nothing the page does can change what the server keeps. */
  async function answer(method: string, url: string, body: unknown, config: unknown) {
    requests.push({ method, url, body, params: (config as { params?: Record<string, unknown> } | undefined)?.params });
    // The server works out its answer when the request arrives, and a held answer is delivered late, as it was: a slow answer is an old one.
    const hold = holds.find((h) => h.method === method && h.pattern.test(url));
    if (hold) holds.splice(holds.indexOf(hold), 1);
    let data: unknown;
    let refusal: unknown = null;
    try {
      data = handle(method, url, body, config);
    } catch (error) {
      refusal = error;
    }
    if (hold) await hold.gate;
    if (refusal) throw refusal;
    return { data: data === undefined || data === null ? data : JSON.parse(JSON.stringify(data)) };
  }

  vi.mocked(apiClient.get).mockReset().mockImplementation(((url: string, config?: unknown) => answer('GET', url, undefined, config)) as never);
  vi.mocked(apiClient.post).mockReset().mockImplementation(((url: string, body?: unknown, config?: unknown) => answer('POST', url, body, config)) as never);
  vi.mocked(apiClient.patch).mockReset().mockImplementation(((url: string, body?: unknown, config?: unknown) => answer('PATCH', url, body, config)) as never);

  return {
    state,
    requests,
    /** Requests of one kind, e.g. `sent('GET', /work-tasks$/)`. */
    sent: (method: string, pattern: RegExp) => requests.filter((r) => r.method === method && pattern.test(r.url)),
    /** The next requests that match are answered with a refusal (`times` of them; every one when negative). */
    refuse: (method: string, pattern: RegExp, status = 400, data: Record<string, unknown> = {}, times = 1) => {
      failures.push({ method, pattern, status, data, times });
    },
    /** The next request that matches is not answered until `release()` is called; the answer is what the server said when the request arrived. */
    hold: (method: string, pattern: RegExp) => {
      let release!: () => void;
      const gate = new Promise<void>((resolve) => (release = resolve));
      holds.push({ method, pattern, release, gate });
      return { release };
    },
  };
}
