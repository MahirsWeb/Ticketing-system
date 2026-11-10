import * as signalR from '@microsoft/signalr';
import { API_BASE_URL } from '../api/client';
import { useAuthStore } from '../store/authStore';
import { reconnectDelayMs } from './reconnectPolicy';

let connection: signalR.HubConnection | null = null;

/// True when a token is present but its lifetime has passed. A null token is NOT treated as expired here: that case is handled
/// by the 401 interceptor and the sign-out subscription below. We only read `exp`; a token we cannot parse is left alone.
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

/// When the access token has expired, the socket must not keep reconnecting with it for ever: without a refresh token there is
/// nothing to connect with, and the person should be signed out (which the "session expired" screen explains) instead of sitting
/// on a page whose live updates silently never come back. Returns true if it took that action.
function signOutIfTokenExpired(): boolean {
  if (accessTokenExpired(useAuthStore.getState().accessToken)) {
    useAuthStore.getState().logout(); // clears the token -> the subscription below stops the hub -> the app shows "session expired"
    return true;
  }
  return false;
}

export function getNotificationsHub(): signalR.HubConnection {
  if (connection) return connection;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/notifications`, {
      accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
    })
    // Never gives up while the session is valid (the built-in policy stops after about 42 seconds and leaves the page without
    // live updates until it is reloaded). The one thing that does stop it is an expired token: then the person is signed out.
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (context) => (signOutIfTokenExpired() ? null : reconnectDelayMs(context.previousRetryCount)),
    })
    .build();

  void startUntilConnected(connection);
  return connection;
}

/// The first connection can fail too (the server was down when the page opened), and automatic reconnect does not cover that:
/// keep trying, with the same growing wait, until it works or the hub is stopped.
async function startUntilConnected(hub: signalR.HubConnection) {
  for (let attempt = 0; connection === hub; attempt++) {
    if (signOutIfTokenExpired()) return; // no point retrying the first connection with a token that has already expired
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
