import { vi } from 'vitest';
import { apiClient } from '../api/client';

/**
 * What the stand-in servers of the tests have in common: they answer at the level of HTTP (what `apiClient` is asked), keep a record of
 * what was asked, can be told to refuse the next request that matches, and can be told to hold an answer back. The test file must mock
 * `../api/client` for this to take effect (`get`, `post`, `put`, `patch` and `delete`, as many as the page uses).
 */

export interface RequestRecord {
  method: string;
  url: string;
  body?: unknown;
  params?: Record<string, unknown>;
  signal?: AbortSignal;
}

/** The server says no, as axios hands it over: an error with a `response`. */
export function refuse(status: number, data: Record<string, unknown> = {}): never {
  throw Object.assign(new Error(`The server said ${status}`), { response: { status, data } });
}

type Handler = (method: string, url: string, body: any, params: any) => unknown;

export function installFakeHttp(handle: Handler) {
  const requests: RequestRecord[] = [];
  const failures: { method: string; pattern: RegExp; status: number; data: Record<string, unknown>; times: number }[] = [];
  const holds: { method: string; pattern: RegExp; gate: Promise<void> }[] = [];

  /** What the server said, as axios hands it over: a copy, so that nothing the page does can change what the server keeps. */
  async function answer(method: string, url: string, body: unknown, config: { params?: Record<string, unknown>; signal?: AbortSignal } | undefined) {
    requests.push({ method, url, body, params: config?.params, signal: config?.signal });
    // The server works out its answer when the request arrives, and a held answer is delivered late, as it was: a slow answer is an old one.
    const hold = holds.find((h) => h.method === method && h.pattern.test(url));
    if (hold) holds.splice(holds.indexOf(hold), 1);
    let data: unknown;
    let refusal: unknown = null;
    try {
      const failure = failures.find((f) => f.method === method && f.pattern.test(url) && f.times !== 0);
      if (failure) {
        if (failure.times > 0) failure.times--;
        refuse(failure.status, failure.data);
      }
      data = handle(method, url, body, config?.params);
    } catch (error) {
      refusal = error;
    }
    if (hold) await hold.gate;
    // A request that was cancelled while it waited is not answered: axios rejects it, and the page must not take that for a failure.
    if (config?.signal?.aborted) throw Object.assign(new Error('canceled'), { code: 'ERR_CANCELED', name: 'CanceledError' });
    if (refusal) throw refusal;
    return { data: data === undefined || data === null ? data : JSON.parse(JSON.stringify(data)) };
  }

  const clientOf = apiClient as unknown as Record<string, unknown>;
  const asked = (method: string) => (url: string, second?: unknown, third?: unknown) =>
    // get and delete take (url, config); post, put and patch take (url, body, config)
    method === 'GET' || method === 'DELETE'
      ? answer(method, url, undefined, second as never)
      : answer(method, url, second, third as never);
  for (const method of ['get', 'post', 'put', 'patch', 'delete']) {
    const target = clientOf[method];
    if (target && vi.isMockFunction(target)) target.mockReset().mockImplementation(asked(method.toUpperCase()) as never);
  }

  return {
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
      holds.push({ method, pattern, gate });
      return { release };
    },
  };
}
