import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// A stand-in for the SignalR client that records how the hub was built and lets the test decide whether starting works.
const fake = vi.hoisted(() => {
  const state = {
    reconnectOptions: undefined as { nextRetryDelayInMilliseconds: (context: { previousRetryCount: number }) => number | null } | undefined,
    start: undefined as unknown as ReturnType<typeof import('vitest').vi.fn>,
    stop: undefined as unknown as ReturnType<typeof import('vitest').vi.fn>
  };
  return state;
});

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

  it('stops reconnecting and signs the person out once the token expires mid-session (TS-1.5)', async () => {
    fake.start.mockResolvedValue(undefined);
    const { useAuthStore } = await import('../store/authStore');
    useAuthStore.setState({ accessToken: jwt(3600), sessionExpired: false }); // valid when the page connects
    const { getNotificationsHub } = await import('./notificationsHub');
    getNotificationsHub();

    // the token now lapses while the socket is up; the next reconnect must stop and sign out, not loop on a dead token
    useAuthStore.setState({ accessToken: jwt(-10) });
    const next = fake.reconnectOptions!.nextRetryDelayInMilliseconds;
    expect(next({ previousRetryCount: 0 })).toBeNull(); // told to stop, not keep retrying
    expect(useAuthStore.getState().accessToken).toBeNull(); // signed out
    expect(useAuthStore.getState().sessionExpired).toBe(true); // the "session expired" screen explains why
  });

  it('does not even try the first connection with an already-expired token, and signs out (TS-1.5)', async () => {
    fake.start.mockResolvedValue(undefined);
    const { useAuthStore } = await import('../store/authStore');
    useAuthStore.setState({ accessToken: jwt(-10), sessionExpired: false });
    const { getNotificationsHub } = await import('./notificationsHub');
    getNotificationsHub();
    await vi.advanceTimersByTimeAsync(0);

    expect(fake.start).not.toHaveBeenCalled(); // no point connecting with a dead token
    expect(useAuthStore.getState().accessToken).toBeNull();
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
