import * as signalR from '@microsoft/signalr';
import { API_BASE_URL, refreshAccessToken } from '../api/client';
import { useAuthStore } from '../store/authStore';
import { reconnectDelayMs } from './reconnectPolicy';

let connection: signalR.HubConnection | null = null;

/// True when a token is present but its lifetime has passed. A null token is NOT treated as expired here. We only read `exp`;
/// a token we cannot parse is left alone.
export function accessTokenExpired(token: string | null): boolean {
  if (!token) return false;
  try {
    const payload = token.split('.')[1];
    const claims = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
    return typeof claims.exp === 'number' && claims.exp * 1000 <= Date.now();
  } catch {
    return false;
  }
}

/// The token the socket (re)connects with. The access token is short-lived by design, so when it has expired this trades the
/// httpOnly refresh cookie for a fresh one — the connection stays live across token expiry without a reload. If the refresh
/// fails (the session is really over) the person is signed out, which stops the hub and shows the "session expired" screen.
export async function hubAccessToken(): Promise<string> {
  let token = useAuthStore.getState().accessToken;
  if (accessTokenExpired(token)) {
    token = await refreshAccessToken();
    if (!token) {
      useAuthStore.getState().logout();
      return '';
    }
  }
  return token ?? '';
}

export function getNotificationsHub(): signalR.HubConnection {
  if (connection) return connection;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/notifications`, {
      accessTokenFactory: hubAccessToken,
    })
    // Never gives up (the built-in policy stops after about 42 seconds and leaves the page without live updates until it is
    // reloaded). A truly dead session stops it a different way: hubAccessToken signs the person out, and the subscription below
    // then drops the socket.
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => reconnectDelayMs(context.previousRetryCount) })
    .build();

  void startUntilConnected(connection);
  return connection;
}

/// The first connection can fail too (the server was down when the page opened), and automatic reconnect does not cover that:
/// keep trying, with the same growing wait, until it works or the hub is stopped.
async function startUntilConnected(hub: signalR.HubConnection) {
  for (let attempt = 0; connection === hub; attempt++) {
    try {
      await hub.start();
      return;
    } catch (err) {
      console.error('SignalR connection failed:', err);
      await new Promise((resolve) => setTimeout(resolve, reconnectDelayMs(attempt + 1)));
    }
  }
}

export function stopNotificationsHub() {
  connection?.stop();
  connection = null;
}

// Signing out (or a 401 that logs the user out) must also drop the socket: otherwise it stays in the previous
// user's `user:` and `role:` groups and keeps receiving their pushes until something happens to reconnect it.
useAuthStore.subscribe((state, previous) => {
  if (previous.accessToken && !state.accessToken) stopNotificationsHub();
});
