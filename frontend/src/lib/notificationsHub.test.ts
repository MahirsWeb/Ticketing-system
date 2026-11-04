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

  it('never gives up reconnecting, however long the server has been away (TS-18.5)', async () => {
    fake.start.mockResolvedValue(undefined);
    const { getNotificationsHub } = await import('./notificationsHub');
    getNotificationsHub();

    const next = fake.reconnectOptions!.nextRetryDelayInMilliseconds;
    for (const attempts of [0, 1, 5, 10, 100, 10_000]) {
      const delay = next({ previousRetryCount: attempts });
      expect(delay).not.toBeNull(); // null is how SignalR is told to stop
      expect(delay).toBeLessThanOrEqual(36_000);
    }
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
