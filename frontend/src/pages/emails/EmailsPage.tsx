import { useEmailStatus } from '../../api/emailQueries';
import { Button, Card, ErrorText, Spinner } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { ConnectMailbox } from './ConnectMailbox';
import { InboxView } from './InboxView';
import { useLanguage } from '../../i18n/LanguageContext';

function Notice({ children }: { children: React.ReactNode }) {
  const { t } = useLanguage();
  return (
    <div className="mx-auto max-w-lg">
      <h1 className="mb-2 text-xl font-bold text-slate-900">{t('nav_emails')}</h1>
      <Card className="p-6 text-center">{children}</Card>
    </div>
  );
}

/** The mailbox of the branch: nothing to read before the person is in a branch and the mailbox is connected. */
export default function EmailsPage() {
  const { t } = useLanguage();
  const user = useAuthStore((s) => s.user);
  const hasBranch = !!user?.departmentId;
  const status = useEmailStatus({ enabled: hasBranch });

  if (!hasBranch) {
    return (
      <Notice>
        <p className="text-sm text-slate-600">{t('email_noBranch')}</p>
      </Notice>
    );
  }
  if (status.isPending) {
    return (
      <div role="status" aria-label={t('email_loadingAria')} className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }
  // Not knowing is not the same as not being connected: offering to connect again would be offering the wrong thing.
  if (status.isError) {
    return (
      <Notice>
        <ErrorText>{t('email_statusFail')}</ErrorText>
        <Button className="mt-4" onClick={() => void status.refetch()}>
          Try again
        </Button>
      </Notice>
    );
  }
  if (!status.data.connected) return <ConnectMailbox branchName={user?.departmentName ?? null} />;
  return <InboxView connectedEmail={status.data.connectedEmail} />;
}
