import { beforeEach, describe, expect, it, vi } from 'vitest';

// Mock axios so the refresh call hits a controllable `post`, and `axios.create` returns a harmless instance whose
// interceptor registration is a no-op (the interceptor itself is exercised end-to-end by the backend's RefreshTokenTests).
const post = vi.hoisted(() => vi.fn());
vi.mock('axios', () => {
  const instance = Object.assign(vi.fn(), {
    interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } },
  });
  return { default: { create: () => instance, post }, AxiosError: class {} };
});

describe('refreshAccessToken', () => {
  beforeEach(() => {
    vi.resetModules();
    post.mockReset();
  });

  it('trades the cookie for a session, stores it, and shares one in-flight refresh between concurrent callers', async () => {
    post.mockResolvedValue({ data: { accessToken: 'fresh', user: { id: 'u1', role: 'Employee' } } });
    const { refreshAccessToken } = await import('./client');
    const { useAuthStore } = await import('../store/authStore');

    const [a, b] = await Promise.all([refreshAccessToken(), refreshAccessToken()]);

    expect(a).toBe('fresh');
    expect(b).toBe('fresh');
    expect(post).toHaveBeenCalledTimes(1); // the cookie rotates on use, so two parallel refreshes must be one
    expect(useAuthStore.getState().accessToken).toBe('fresh');
    expect(useAuthStore.getState().user?.id).toBe('u1');
  });

  it('returns null and changes nothing when the refresh is refused (the session is really over)', async () => {
    post.mockRejectedValue(new Error('401'));
    const { refreshAccessToken } = await import('./client');
    const { useAuthStore } = await import('../store/authStore');
    useAuthStore.setState({ accessToken: null, user: null });

    expect(await refreshAccessToken()).toBeNull();
    expect(useAuthStore.getState().accessToken).toBeNull();
  });

  it('starts a new refresh the next time, once the first has settled', async () => {
    post.mockResolvedValue({ data: { accessToken: 't1', user: { id: 'u1' } } });
    const { refreshAccessToken } = await import('./client');

    await refreshAccessToken();
    await refreshAccessToken();

    expect(post).toHaveBeenCalledTimes(2); // not deduped once the previous one finished
  });
});
