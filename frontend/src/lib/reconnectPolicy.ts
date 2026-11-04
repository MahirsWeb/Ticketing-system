/// How long to wait before the next attempt to reconnect, after `failedAttempts` attempts in a row have failed. It grows from
/// immediately to half a minute and then stays there, and it never gives up: SignalR's built-in policy stops after about 42
/// seconds, so a laptop that slept or a server that restarted left the page without live updates until it was reloaded.
const STEPS_MS = [0, 2000, 5000, 10000, 20000, 30000];

export function reconnectDelayMs(failedAttempts: number, random: () => number = Math.random): number {
  const base = STEPS_MS[Math.min(Math.max(failedAttempts, 0), STEPS_MS.length - 1)];
  // A little jitter, so that many pages that lost the server together do not all come back in the same second.
  return base === 0 ? 0 : Math.round(base * (0.8 + random() * 0.4));
}
