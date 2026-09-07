import * as signalR from '@microsoft/signalr';
import { API_BASE_URL } from '../api/client';
import { useAuthStore } from '../store/authStore';

let connection: signalR.HubConnection | null = null;

export function getNotificationsHub(): signalR.HubConnection {
  if (connection) return connection;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/notifications`, {
      accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
    })
    .withAutomaticReconnect()
    .build();

  connection.start().catch((err) => console.error('SignalR connection failed:', err));
  return connection;
}

export function stopNotificationsHub() {
  connection?.stop();
  connection = null;
}
