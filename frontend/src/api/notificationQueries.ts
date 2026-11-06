import { useQuery } from '@tanstack/react-query';
import { notificationsApi } from './notifications';

/** Names of the cached notifications. */
export const notificationKeys = {
  all: ['notifications'] as const,
  list: ['notifications', 'list'] as const,
};

/** How many of the latest notifications the bell lists. */
export const NOTIFICATIONS_TAKE = 20;

/**
 * The latest notifications of the person who is signed in. They are asked for again when something is pushed (see the bell) and by
 * `invalidateQueries`, which also cancels an earlier request still on its way, so the answer that is shown is always the latest one asked for,
 * whatever order the answers come in. Forgotten when the person signs out (the shared client is cleared).
 */
export function useNotifications() {
  return useQuery({ queryKey: notificationKeys.list, queryFn: () => notificationsApi.list(NOTIFICATIONS_TAKE) });
}
