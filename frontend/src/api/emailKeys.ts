/** Names of the cached mailbox data; see emailQueries.ts. */
export const emailKeys = {
  all: ['email'] as const,
  status: ['email', 'status'] as const,
  inbox: ['email', 'inbox'] as const,
  prefill: (messageId: string) => ['email', 'prefill', messageId] as const,
};
