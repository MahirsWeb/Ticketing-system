import { useQuery } from '@tanstack/react-query';
import { emailIntegrationApi } from './emailIntegration';

/** What a marked e-mail gives a new ticket (the sender, the subject, the text). Asked for each time the page is opened for an e-mail, and for that e-mail only. */
export function useEmailPrefill(messageId: string | null) {
  return useQuery({
    queryKey: ['email', 'prefill', messageId ?? ''],
    queryFn: () => emailIntegrationApi.getPrefill(messageId!),
    enabled: !!messageId,
    gcTime: 0,
  });
}
