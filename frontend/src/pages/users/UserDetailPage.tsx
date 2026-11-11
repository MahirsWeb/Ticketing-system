import { Link, useParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from '../../store/authStore';
import { userKeys, useUser } from '../../api/userQueries';
import { ErrorText, Spinner } from '../../components/ui';
import { UserProfileCard } from './UserProfileCard';
import { UserTicketsTable } from './UserTicketsTable';
import { useLanguage } from '../../i18n/LanguageContext';
import type { TranslationKey } from '../../i18n/translations';

/**
 * One person: who they are and, for an administrator, what can be done to their account, and the tickets they submitted or have. Another person
 * in the address is another entry, so the page is drawn from nothing while it is on its way: what was typed or asked about the previous one is not carried over.
 */
export default function UserDetailPage() {
  const { t } = useLanguage();
  const { id } = useParams<{ id: string }>();
  const currentUser = useAuthStore((s) => s.user);
  const queryClient = useQueryClient();
  const query = useUser(id);
  const target = query.data;

  if (!target) {
    if (query.isError) {
      const notFound = (query.error as { response?: { status?: number } })?.response?.status === 404;
      return notFound ? <p>{t('userdetail_notFound')}</p> : <ErrorText>{t('userdetail_loadFail')}</ErrorText>;
    }
    return (
      <div className="flex justify-center py-16">
        <Spinner className="h-8 w-8 text-blue-700" />
      </div>
    );
  }

  /** The person is asked for again, and the tickets too (their names, and which of them are listed, can change); what is on the screen stays until the new answer is there. */
  function refresh() {
    void queryClient.invalidateQueries({ queryKey: userKeys.detail(target!.id) });
    void queryClient.invalidateQueries({ queryKey: ['users', 'tickets'] });
  }

  return (
    <div className="space-y-6">
      <div>
        <Link to="/users" className="text-xs font-medium text-blue-700 hover:underline">
          {t('userdetail_back')}
        </Link>
        <div className="mt-1 flex flex-wrap items-center gap-3">
          <h1 className="text-xl font-bold text-slate-900">
            {target.firstName} {target.lastName}
          </h1>
          <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-semibold text-slate-600">{t(`role_${target.role}` as TranslationKey)}</span>
        </div>
      </div>

      <UserProfileCard user={target} viewerId={currentUser?.id} viewerIsAdmin={currentUser?.role === 'Admin'} onChanged={refresh} />
      <UserTicketsTable user={target} />
    </div>
  );
}
