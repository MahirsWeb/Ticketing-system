import { vi } from 'vitest';
import { apiClient } from '../api/client';

/**
 * A stand-in for the part of the server that the settings page talks to, kept in memory and answering at the level of HTTP (what
 * `apiClient` is asked), so that a test says what a person does and what the server answers, and does not depend on how the page
 * loads or refreshes its lists. The rules a real server has and the page has to live with are here too: a name may not be used twice,
 * and something tickets already use cannot be deleted.
 *
 * The test file must mock `../api/client` for this to take effect.
 */

type Lookup = { id: string; name: string; isActive: boolean };
type Department = Lookup & { email: string };
type SubBranch = Lookup & { departmentId: string };
type SlaPlan = Lookup & { resolutionTimeHours: number };
type Document = { id: string; title: string; sourceFileName: string; fileUrl: string | null; createdAt: string; chunkCount: number };
type Reindex = { state: 'Idle' | 'Running' | 'Completed' | 'Failed'; total: number; processed: number; embedded: number; failed: number; startedAtUtc: string | null; finishedAtUtc: string | null; message: string | null };

export interface AdminState {
  departments: Department[];
  subBranches: SubBranch[];
  helpTopics: Lookup[];
  categories: Lookup[];
  slaPlans: SlaPlan[];
  overdue: { notifyOnSlaBreach: boolean; manualOverdueDays: number | null };
  documents: Document[];
  reindex: Reindex;
  /** Ids that tickets already use: they cannot be deleted. */
  inUse: Set<string>;
}

export function newAdminState(): AdminState {
  return {
    departments: [
      { id: 'd1', name: 'Sarajevo', email: 'sarajevo@firma.test', isActive: true },
      { id: 'd2', name: 'Mostar', email: 'mostar@firma.test', isActive: false },
    ],
    subBranches: [{ id: 's1', departmentId: 'd1', name: 'Centar', isActive: true }],
    helpTopics: [
      { id: 'h1', name: 'Printer', isActive: true },
      { id: 'h2', name: 'Network', isActive: false },
    ],
    categories: [{ id: 'c1', name: 'Hardware', isActive: true }],
    slaPlans: [{ id: 'p1', name: 'Standard', resolutionTimeHours: 72, isActive: true }],
    overdue: { notifyOnSlaBreach: true, manualOverdueDays: 3 },
    documents: [{ id: 'k1', title: 'Manual', sourceFileName: 'manual.docx', fileUrl: null, createdAt: '2026-10-01T08:00:00Z', chunkCount: 12 }],
    reindex: { state: 'Idle', total: 0, processed: 0, embedded: 0, failed: 0, startedAtUtc: null, finishedAtUtc: null, message: null },
    inUse: new Set(['h1']),
  };
}

export interface RequestRecord {
  method: string;
  url: string;
  body?: unknown;
  params?: Record<string, unknown>;
}

export function installAdminBackend(state: AdminState = newAdminState()) {
  const requests: RequestRecord[] = [];
  const failures: { method: string; pattern: RegExp; status: number; data: Record<string, unknown>; times: number }[] = [];
  let next = 100;

  const refuse = (status: number, data: Record<string, unknown>): never => {
    throw Object.assign(new Error(`The server said ${status}`), { response: { status, data } });
  };

  function lookupRoutes<T extends Lookup>(method: string, url: string, prefix: string, list: T[], noun: string, body: any, params: any, make: (b: any) => Omit<T, 'id'>, patch: (item: T, b: any) => void) {
    if (url === prefix) {
      if (method === 'GET') return params?.includeInactive ? list : list.filter((i) => i.isActive);
      if (method === 'POST') {
        if (list.some((i) => i.name.toLowerCase() === String(body.name).toLowerCase())) refuse(400, { message: `A ${noun} with that name already exists.` });
        const item = { id: `n${next++}`, ...make(body) } as T;
        list.push(item);
        return item;
      }
    }
    const m = url.startsWith(prefix + '/') ? url.slice(prefix.length + 1) : null;
    if (m && !m.includes('/')) {
      const item = list.find((i) => i.id === m);
      if (!item) refuse(404, { message: `No such ${noun}.` });
      if (method === 'PUT') {
        if (list.some((i) => i.id !== m && i.name.toLowerCase() === String(body.name).toLowerCase())) refuse(400, { message: `A ${noun} with that name already exists.` });
        patch(item!, body);
        return item;
      }
      if (method === 'DELETE') {
        if (state.inUse.has(m)) refuse(400, { message: `This ${noun} is used by tickets and cannot be deleted. Deactivate it instead.` });
        list.splice(list.indexOf(item!), 1);
        return null;
      }
    }
    return undefined;
  }

  function handle(method: string, url: string, body: any, config: any): unknown {
    requests.push({ method, url, body, params: config?.params });
    const failure = failures.find((f) => f.method === method && f.pattern.test(url) && f.times !== 0);
    if (failure) {
      if (failure.times > 0) failure.times--;
      refuse(failure.status, failure.data);
    }
    const params = config?.params;

    const results = [
      lookupRoutes(method, url, '/api/departments', state.departments, 'branch', body, params, (b) => ({ name: b.name, email: b.email, isActive: true }), (i, b) => Object.assign(i, { name: b.name, email: b.email, isActive: b.isActive })),
      lookupRoutes(method, url, '/api/help-topics', state.helpTopics, 'help topic', body, params, (b) => ({ name: b.name, isActive: true }), (i, b) => Object.assign(i, { name: b.name, isActive: b.isActive })),
      lookupRoutes(method, url, '/api/ticket-categories', state.categories, 'category', body, params, (b) => ({ name: b.name, isActive: true }), (i, b) => Object.assign(i, { name: b.name, isActive: b.isActive })),
      lookupRoutes(method, url, '/api/sla-plans', state.slaPlans, 'SLA plan', body, params, (b) => ({ name: b.name, resolutionTimeHours: b.resolutionTimeHours, isActive: true }), (i, b) => Object.assign(i, { name: b.name, resolutionTimeHours: b.resolutionTimeHours, isActive: b.isActive })),
    ];
    const found = results.find((r) => r !== undefined);
    if (found !== undefined) return found;

    let m: RegExpMatchArray | null;
    if ((m = url.match(/^\/api\/departments\/([^/]+)\/sub-branches$/))) {
      const mine = state.subBranches.filter((s) => s.departmentId === m![1]);
      if (method === 'GET') return params?.includeInactive ? mine : mine.filter((s) => s.isActive);
      if (method === 'POST') {
        if (mine.some((s) => s.name.toLowerCase() === String(body.name).toLowerCase())) refuse(400, { message: 'A sub-branch with that name already exists.' });
        const item = { id: `n${next++}`, departmentId: m[1], name: body.name, isActive: true };
        state.subBranches.push(item);
        return item;
      }
    }
    if ((m = url.match(/^\/api\/sub-branches\/([^/]+)$/)) && method === 'PUT') {
      const item = state.subBranches.find((s) => s.id === m![1]) ?? refuse(404, { message: 'No such sub-branch.' });
      Object.assign(item, { name: body.name, isActive: body.isActive });
      return item;
    }
    if (url === '/api/settings/overdue-notifications') {
      if (method === 'GET') return state.overdue;
      if (method === 'PUT') {
        state.overdue = { notifyOnSlaBreach: body.notifyOnSlaBreach, manualOverdueDays: body.manualOverdueDays };
        return state.overdue;
      }
    }
    if (url === '/api/knowledge-base/documents') {
      if (method === 'GET') return state.documents;
      if (method === 'POST') {
        const files = (body as FormData).getAll('files') as File[];
        const created = files.map((f) => ({ id: `k${next++}`, title: f.name.replace(/\.[^.]+$/, ''), sourceFileName: f.name, fileUrl: null, createdAt: '2026-11-05T08:00:00Z', chunkCount: 1 }));
        state.documents.push(...created);
        return created;
      }
    }
    if ((m = url.match(/^\/api\/knowledge-base\/documents\/([^/]+)$/)) && method === 'DELETE') {
      state.documents = state.documents.filter((d) => d.id !== m![1]);
      return null;
    }
    if (url === '/api/knowledge-base/reindex-status' && method === 'GET') return state.reindex;
    if ((url === '/api/knowledge-base/reindex-all' || /^\/api\/knowledge-base\/documents\/[^/]+\/reindex$/.test(url)) && method === 'POST') {
      if (state.reindex.state === 'Running') refuse(409, { message: 'A re-index is already running.', status: state.reindex });
      state.reindex = { ...state.reindex, state: 'Running', total: 10, processed: 0, message: 'Started.' };
      return state.reindex;
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}` });
  }

  // Every answer is a copy, as every answer off the wire is: a page must not be able to see a change in what it was given before.
  const answer = (value: unknown) => ({ data: value === undefined || value === null ? value : JSON.parse(JSON.stringify(value)) });
  vi.mocked(apiClient.get).mockReset().mockImplementation((async (url: string, config?: unknown) => answer(handle('GET', url, undefined, config))) as never);
  vi.mocked(apiClient.post).mockReset().mockImplementation((async (url: string, body?: unknown, config?: unknown) => answer(handle('POST', url, body, config))) as never);
  vi.mocked(apiClient.put).mockReset().mockImplementation((async (url: string, body?: unknown, config?: unknown) => answer(handle('PUT', url, body, config))) as never);
  vi.mocked(apiClient.delete).mockReset().mockImplementation((async (url: string, config?: unknown) => answer(handle('DELETE', url, undefined, config))) as never);

  return {
    state,
    requests,
    /** Requests of one kind, e.g. `sent('PUT', /sla-plans/)`. */
    sent: (method: string, pattern: RegExp) => requests.filter((r) => r.method === method && pattern.test(r.url)),
    /** The next requests that match are answered with a refusal (`times` of them; every one when negative). */
    refuse: (method: string, pattern: RegExp, status = 400, data: Record<string, unknown> = {}, times = 1) => {
      failures.push({ method, pattern, status, data, times });
    },
    /** Stops refusing. */
    forgive: () => {
      failures.length = 0;
    },
  };
}
