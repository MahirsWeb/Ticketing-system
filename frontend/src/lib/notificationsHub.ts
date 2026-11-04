import * as signalR from '@microsoft/signalr';
import { API_BASE_URL } from '../api/client';
import { useAuthStore } from '../store/authStore';
import { reconnectDelayMs } from './reconnectPolicy';

let connection: signalR.HubConnection | null = null;

export function getNotificationsHub(): signalR.HubConnection {
  if (connection) return connection;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/notifications`, {
      accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
    })
    // Never gives up (the built-in policy stops after about 42 seconds and leaves the page without live updates until it is reloaded).
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
