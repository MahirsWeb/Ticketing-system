import { beforeEach, describe, expect, it, vi } from 'vitest';

// Mock axios so the refresh call hits a controllable `post`, and `axios.create` returns an instance whose interceptor
// registrations are recorded, so the response interceptor can be driven directly.
const post = vi.hoisted(() => vi.fn());
const instance = vi.hoisted(() =>
  Object.assign(vi.fn(), {
    interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } },
  })
);
vi.mock('axios', () => ({ default: { create: () => instance, post }, AxiosError: class {} }));

const refused = { response: { status: 401 } };
const status = (code: number) => ({ response: { status: code } });

async function load() {
  const client = await import('./client');
  client.refreshRetry.sleep = vi.fn().mockResolvedValue(undefined); // do not really wait between tries
  const { useAuthStore } = await import('../store/authStore');
  return { ...client, useAuthStore };
}

describe('refreshAccessToken', () => {
  beforeEach(() => {
    vi.resetModules();
    post.mockReset();
    instance.mockReset();
    instance.interceptors.response.use.mockReset();
  });

  it('trades the cookie for a session, stores it, and shares one in-flight refresh between concurrent callers', async () => {
    post.mockResolvedValue({ data: { accessToken: 'fresh', user: { id: 'u1', role: 'Employee' } } });
    const { refreshAccessToken, useAuthStore } = await load();

    const [a, b] = await Promise.all([refreshAccessToken(), refreshAccessToken()]);

    expect(a).toBe('fresh');
    expect(b).toBe('fresh');
    expect(post).toHaveBeenCalledTimes(1); // the cookie rotates on use, so two parallel refreshes must be one
    expect(useAuthStore.getState().accessToken).toBe('fresh');
    expect(useAuthStore.getState().user?.id).toBe('u1');
  });

  it('returns null and changes nothing when the server refuses the cookie (401: the session is really over)', async () => {
    post.mockRejectedValue(refused);
    const { refreshAccessToken, useAuthStore } = await load();
    useAuthStore.setState({ accessToken: null, user: null });

    expect(await refreshAccessToken()).toBeNull();
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(post).toHaveBeenCalledTimes(1); // a refusal is final, not retried
  });

  it('starts a new refresh the next time, once the first has settled', async () => {
    post.mockResolvedValue({ data: { accessToken: 't1', user: { id: 'u1' } } });
    const { refreshAccessToken } = await load();

    await refreshAccessToken();
    await refreshAccessToken();

    expect(post).toHaveBeenCalledTimes(2); // not deduped once the previous one finished
  });

  it('tries again after a rate limit (429) instead of treating it as an expired session', async () => {
    post.mockRejectedValueOnce(status(429)).mockResolvedValueOnce({ data: { accessToken: 'later', user: { id: 'u1' } } });
    const { refreshAccessToken, refreshRetry } = await load();

    expect(await refreshAccessToken()).toBe('later');
    expect(post).toHaveBeenCalledTimes(2);
    expect(refreshRetry.sleep).toHaveBeenCalledTimes(1);
  });

  it.each([
    ['a server error (503)', status(503)],
    ['no network', new Error('Network Error')],
  ])('keeps trying through %s and then says it is unavailable, without signing anybody out', async (_name, failure) => {
    post.mockRejectedValue(failure);
    const { refreshAccessToken, RefreshUnavailableError, refreshRetry, useAuthStore } = await load();
    useAuthStore.setState({ accessToken: 'still-here', user: { id: 'u1' } as never });

    await expect(refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    expect(post).toHaveBeenCalledTimes(refreshRetry.delaysMs.length + 1);
    expect(useAuthStore.getState().accessToken).toBe('still-here');
  });

  it('does not retry a refusal that waiting will not change, and does not call the session over for it', async () => {
    post.mockRejectedValue(status(418));
    const { refreshAccessToken, RefreshUnavailableError } = await load();

    await expect(refreshAccessToken()).rejects.toBeInstanceOf(RefreshUnavailableError);
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('counts a 403 (the server will take no renewals from this page, for example a wrong list of allowed origins) as the end of the session, not as a person stuck for ever', async () => {
    post.mockRejectedValue(status(403));
    const { refreshAccessToken } = await load();

    expect(await refreshAccessToken()).toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('gives up on a renewal that hangs, so that it cannot hold every tab and the sign-out button for ever', async () => {
    post.mockResolvedValue({ data: { accessToken: 'fresh', user: { id: 'u1' } } });
    const { refreshAccessToken } = await load();

    await refreshAccessToken();

    expect((post.mock.calls[0][2] as { timeout: number }).timeout).toBeGreaterThan(0);
    expect((post.mock.calls[0][2] as { timeout: number }).timeout).toBeLessThanOrEqual(30_000);
  });

  it('throws away the answer of a renewal that was under way when the person signed out, so that it cannot bring the session back', async () => {
    let answer!: (v: unknown) => void;
    post.mockReturnValue(new Promise((resolve) => (answer = resolve)));
    const { refreshAccessToken, useAuthStore } = await load();
    useAuthStore.setState({ accessToken: 'in-use', user: { id: 'u1' } as never });

    const renewing = refreshAccessToken();
    useAuthStore.getState().signOut(); // the person pressed "Sign out" while the renewal was on its way
    answer({ data: { accessToken: 'late', user: { id: 'u1' } } });

    expect(await renewing).toBeNull();
    expect(useAuthStore.getState().accessToken).toBeNull();
  });

  it('takes its turn on the lock that all the tabs of the site share', async () => {
    const request = vi.fn((_name: string, work: () => Promise<unknown>) => work());
    Object.defineProperty(navigator, 'locks', { value: { request }, configurable: true });
    try {
      post.mockResolvedValue({ data: { accessToken: 'fresh', user: { id: 'u1' } } });
      const { refreshAccessToken } = await load();

      await refreshAccessToken();

      expect(request).toHaveBeenCalledTimes(1);
      expect(request.mock.calls[0][0]).toBe('tst-refresh');
    } finally {
      Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
    }
  });
});

const handler = () => instance.interceptors.response.use.mock.calls[0][1] as (e: unknown) => Promise<unknown>;

describe('the response interceptor', () => {
  beforeEach(() => {
    vi.resetModules();
    post.mockReset();
    instance.mockReset();
    instance.interceptors.response.use.mockReset();
  });

  const rejected401 = (headers: Record<string, string>, url = '/api/tickets') => ({
    response: { status: 401 },
    config: { url, headers },
  });

  it('does not renew anything for a request that carried no session (nothing to renew, and nobody to hand a session back to)', async () => {
    const { useAuthStore } = await load();
    useAuthStore.setState({ accessToken: null, user: null });

    await expect(handler()(rejected401({}))).rejects.toBeDefined();
    expect(post).not.toHaveBeenCalled();
  });

  it('signs the person out only when the server refuses the renewal', async () => {
    post.mockRejectedValue(refused);
    const { useAuthStore } = await load();
    useAuthStore.setState({ accessToken: 'old', user: { id: 'u1' } as never });

    await expect(handler()(rejected401({ Authorization: 'Bearer old' }))).rejects.toBeDefined();
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(useAuthStore.getState().sessionExpired).toBe(true);
  });

  it('leaves the person signed in when the renewal could not be asked (the request just fails)', async () => {
    post.mockRejectedValue(status(429));
    const { useAuthStore } = await load();
    useAuthStore.setState({ accessToken: 'old', user: { id: 'u1' } as never });

    await expect(handler()(rejected401({ Authorization: 'Bearer old' }))).rejects.toBeDefined();
    expect(useAuthStore.getState().accessToken).toBe('old');
    expect(useAuthStore.getState().user?.id).toBe('u1');
  });

  it('repeats the request with the renewed token', async () => {
    post.mockResolvedValue({ data: { accessToken: 'new', user: { id: 'u1' } } });
    instance.mockResolvedValue({ data: 'ok' });
    await load();

    const result = await handler()(rejected401({ Authorization: 'Bearer old' }));

    expect(result).toEqual({ data: 'ok' });
    expect((instance.mock.calls[0][0] as { headers: Record<string, string> }).headers.Authorization).toBe('Bearer new');
  });

  describe('the words of the server', () => {
    const success = () => instance.interceptors.response.use.mock.calls[0][0] as (r: { data: unknown }) => { data: { message?: string } };

    it('are put into the language of the screen when it is Bosnian, in a refusal as well as in a confirmation', async () => {
      localStorage.setItem('tst-language', 'bs');
      await load();

      const refusal = { response: { status: 400, data: { message: 'Company not found.' } }, config: { url: '/api/companies/1', headers: {} } };
      await expect(handler()(refusal)).rejects.toBe(refusal);
      expect(refusal.response.data.message).toBe('Firma nije pronađena.');

      expect(success()({ data: { message: 'Phone number saved.' } }).data.message).toBe('Broj telefona je sačuvan.');
      localStorage.removeItem('tst-language');
    });

    it('are left as they came when the language is English, or when nobody has translated them', async () => {
      await load();
      const refusal = { response: { status: 400, data: { message: 'Company not found.' } }, config: { url: '/api/companies/1', headers: {} } };
      await expect(handler()(refusal)).rejects.toBeDefined();
      expect(refusal.response.data.message).toBe('Company not found.');

      localStorage.setItem('tst-language', 'bs');
      const unknown = { response: { status: 400, data: { message: 'A sentence nobody translated.' } }, config: { url: '/x', headers: {} } };
      await expect(handler()(unknown)).rejects.toBeDefined();
      expect(unknown.response.data.message).toBe('A sentence nobody translated.');
      localStorage.removeItem('tst-language');
    });
  });
});
