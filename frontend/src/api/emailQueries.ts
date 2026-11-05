import { useQuery } from '@tanstack/react-query';
import { emailIntegrationApi } from './emailIntegration';
import { emailKeys } from './emailKeys';

export { emailKeys };

/** How many of the latest messages of the mailbox are asked for. */
export const INBOX_SIZE = 30;

/**
 * Whether the mailbox of the branch is connected, and which one. Asked for each time the page is opened and forgotten when it is left: another
 * person may have connected or disconnected it in the meantime. Nothing is asked for where the answer is of no use (somebody who is in no branch).
 */
export function useEmailStatus(options: { enabled?: boolean } = {}) {
  return useQuery({ queryKey: emailKeys.status, queryFn: emailIntegrationApi.getStatus, enabled: options.enabled ?? true, gcTime: 0 });
}

/** The latest messages of the mailbox. Asked again by `refetch`, and by nothing else: what a mailbox holds changes whenever mail arrives. */
export function useInbox() {
  return useQuery({ queryKey: emailKeys.inbox, queryFn: () => emailIntegrationApi.listMessages(INBOX_SIZE), gcTime: 0 });
}

/** What a marked e-mail gives a new ticket (the sender, the subject, the text). Asked for each time the page is opened for an e-mail, and for that e-mail only. */
export function useEmailPrefill(messageId: string | null) {
  return useQuery({
    queryKey: emailKeys.prefill(messageId ?? ''),
    queryFn: () => emailIntegrationApi.getPrefill(messageId!),
    enabled: !!messageId,
    gcTime: 0,
  });
}
