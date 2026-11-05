import { useState } from 'react';
import type { CreatedUserResponse } from '../../types';
import { AccountsList } from './AccountsList';
import { CreateAccountForm } from './CreateAccountForm';
import { TempPasswordModal } from './TempPasswordModal';

/** The accounts: the form for a new one, the list, and the dialog that shows the temporary password of the one that was just made. */
export function AccountsSection({ isAdmin, preselectedCompanyId }: { isAdmin: boolean; preselectedCompanyId: string | null }) {
  const [created, setCreated] = useState<CreatedUserResponse | null>(null);
  return (
    <>
      <CreateAccountForm isAdmin={isAdmin} preselectedCompanyId={preselectedCompanyId} onCreated={setCreated} />
      <AccountsList isAdmin={isAdmin} />
      {created && <TempPasswordModal result={created} onClose={() => setCreated(null)} />}
    </>
  );
}
