import { QueryClient } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { useAuthStore } from '../store/authStore';

/** A request the server answered with "no" (400-499) will not be answered differently a moment later; a failure to reach it, or a 500, might be. */
function shouldRetry(failureCount: number, error: unknown): boolean {
  if (isAxiosError(error) && error.response && error.response.status < 500) return false;
  return failureCount < 2;
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetry,
        // The screens have their own refresh; a window coming back into focus must not start a wave of requests.
        refetchOnWindowFocus: false,
      },
    },
  });
}

export const queryClient = createQueryClient();

// What was fetched for one person must never be shown to the next one who signs in on the same browser.
useAuthStore.subscribe((state, previous) => {
  if (previous.accessToken && !state.accessToken) queryClient.clear();
});
