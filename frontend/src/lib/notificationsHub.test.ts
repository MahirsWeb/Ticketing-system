import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// A stand-in for the SignalR client that records how the hub was built and lets the test decide whether starting works.
const fake = vi.hoisted(() => {
  const state = {
    reconnectOptions: undefined as { nextRetryDelayInMilliseconds: (context: { previousRetryCount: number }) => number | null } | undefined,
    start: undefined as unknown as ReturnType<typeof import('vitest').vi.fn>,
    stop: undefined as unknown as ReturnType<typeof import('vitest').vi.fn>,
    refresh: undefined as unknown as ReturnType<typeof import('vitest').vi.fn>
  };
  return state;
});

// The hub trades an expired access token for a fresh one through api/client; the test controls what that returns.
vi.mock('../api/client', () => ({
  API_BASE_URL: 'http://api.test',
  refreshAccessToken: () => (fake.refresh as unknown as () => Promise<string | null>)(),
}));

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }
    withAutomaticReconnect(options: typeof fake.reconnectOptions) {
      fake.reconnectOptions = options;
      return this;
    }
    build() {
      return { start: fake.start, stop: fake.stop, on: vi.fn(), off: vi.fn(), onreconnected: vi.fn() };
    }
  }
  return { HubConnectionBuilder };
});

describe('the live updates connection', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.useFakeTimers();
    vi.spyOn(console, 'error').mockImplementation(() => {});
    fake.start = vi.fn();
    fake.stop = vi.fn().mockResolvedValue(undefined);
    fake.refresh = vi.fn().mockResolvedValue(null);
    fake.reconnectOptions = undefined;
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  // A JWT whose exp is `secondsFromNow` seconds away (only the payload's exp is read).
  const jwt = (secondsFromNow: number) =>
    `h.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + secondsFromNow }))}.s`;

  it('never gives up reconnecting while the session is valid, however long the server has been away (TS-18.5)', async () => {
    fake.start.mockResolvedValue(undefined);
    const { useAuthStore } = await import('../store/authStore');
    useAuthStore.setState({ accessToken: jwt(3600) }); // a token good for another hour
    const { getNotificationsHub } = await import('./notificationsHub');
    getNotificationsHub();

    const next = fake.reconnectOptions!.nextRetryDelayInMilliseconds;
    for (const attempts of [0, 1, 5, 10, 100, 10_000]) {
      const delay = next({ previousRetryCount: attempts });
      expect(delay).not.toBeNull(); // null is how SignalR is told to stop
      expect(delay).toBeLessThanOrEqual(36_000);
    }
  });

  it('hands the socket the current access token, and a refreshed one once it has expired (TS-1.5)', async () => {
    fake.start.mockResolvedValue(undefined);
    fake.refresh.mockResolvedValue(jwt(3600)); // a refresh succeeds with a fresh token
    const { useAuthStore } = await import('../store/authStore');
    const { getNotificationsHub, hubAccessToken } = await import('./notificationsHub');
    getNotificationsHub();

    useAuthStore.setState({ accessToken: jwt(3600) });
    expect(await hubAccessToken()).toBe(useAuthStore.getState().accessToken); // still valid: used as-is, no refresh
    expect(fake.refresh).not.toHaveBeenCalled();

    useAuthStore.setState({ accessToken: jwt(-10) }); // now expired: the cookie is traded for a new token
    expect(await hubAccessToken()).toBe(jwt(3600));
    expect(fake.refresh).toHaveBeenCalledTimes(1);
  });

  it('signs the person out when the token has expired and the refresh cookie is dead too (TS-1.5)', async () => {
    fake.start.mockResolvedValue(undefined);
    fake.refresh.mockResolvedValue(null); // the refresh fails: the session is really over
    const { useAuthStore } = await import('../store/authStore');
    const { getNotificationsHub, hubAccessToken } = await import('./notificationsHub');
    getNotificationsHub();

    useAuthStore.setState({ accessToken: jwt(-10) });
    expect(await hubAccessToken()).toBe(''); // nothing to connect with
    expect(useAuthStore.getState().accessToken).toBeNull(); // signed out, which stops the hub
    expect(useAuthStore.getState().sessionExpired).toBe(true);
  });

  it('exposes a token-expiry check that treats a missing token as not-expired and ignores an unparseable one', async () => {
    const { accessTokenExpired } = await import('./notificationsHub');
    expect(accessTokenExpired(null)).toBe(false);
    expect(accessTokenExpired('not-a-jwt')).toBe(false);
    expect(accessTokenExpired(jwt(60))).toBe(false);
    expect(accessTokenExpired(jwt(-1))).toBe(true);
  });

  it('keeps trying when the very first connection fails, because the server was down when the page opened', async () => {
    fake.start.mockRejectedValueOnce(new Error('down')).mockRejectedValueOnce(new Error('still down')).mockResolvedValue(undefined);
    const { getNotificationsHub } = await import('./notificationsHub');

    getNotificationsHub();
    await vi.advanceTimersByTimeAsync(60_000);

    expect(fake.start).toHaveBeenCalledTimes(3);
  });

  it('stops trying once the hub was stopped (the person signed out)', async () => {
    fake.start.mockRejectedValue(new Error('down'));
    const { getNotificationsHub, stopNotificationsHub } = await import('./notificationsHub');

    getNotificationsHub();
    await vi.advanceTimersByTimeAsync(0);
    expect(fake.start).toHaveBeenCalledTimes(1);
    stopNotificationsHub();
    await vi.advanceTimersByTimeAsync(120_000);

    expect(fake.start).toHaveBeenCalledTimes(1);
  });

  it('is one connection however often it is asked for', async () => {
    fake.start.mockResolvedValue(undefined);
    const { getNotificationsHub } = await import('./notificationsHub');

    expect(getNotificationsHub()).toBe(getNotificationsHub());
    expect(fake.start).toHaveBeenCalledTimes(1);
  });
});
