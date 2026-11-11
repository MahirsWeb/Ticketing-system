import { useState } from 'react';
import { useCompanies, useDepartments, useSubBranches } from '../../api/lookupQueries';
import { usersApi } from '../../api/users';
import { messageOf } from '../../lib/messageOf';
import type { CreatedUserResponse, UserRole } from '../../types';
import { useLanguage } from '../../i18n/LanguageContext';

/**
 * The form for a new account: what is written and chosen, the lists it chooses from (asked for only when the kind of account needs them),
 * and the sending. A client needs a company, an employee a branch (and a sub-branch when the branch has some), an administrator neither.
 * What is written is sent without the spaces around it, and names of nothing but spaces are not names.
 */
export function useCreateAccount({ preselectedCompanyId, onCreated }: { preselectedCompanyId: string | null; onCreated: (created: CreatedUserResponse) => void }) {
  const { t } = useLanguage();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [accountType, setAccountType] = useState<UserRole>('Client');
  const [companyId, setCompanyId] = useState(preselectedCompanyId ?? '');
  const [departmentId, setDepartmentId] = useState('');
  const [subBranchId, setSubBranchId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const needsCompany = accountType === 'Client';
  const needsBranch = accountType === 'Employee';
  const companies = useCompanies({ enabled: needsCompany });
  const departments = useDepartments({ enabled: needsBranch });
  // Sub-branches are kept per branch by the shared cache, so going back to a branch already seen does not ask again.
  const subBranches = useSubBranches(departmentId, { enabled: needsBranch });
  const subBranchChoices = subBranches.data ?? [];

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const first = firstName.trim();
    const last = lastName.trim();

    if (!first || !last) {
      setError(t('createacc_namesRequired'));
      return;
    }
    if (needsCompany && !companyId) {
      setError(t('createacc_selectCompany'));
      return;
    }
    if (needsBranch && !departmentId) {
      setError(t('createacc_selectBranch'));
      return;
    }
    if (needsBranch && subBranchChoices.length > 0 && !subBranchId) {
      setError(t('common_selectSubBranch'));
      return;
    }

    setLoading(true);
    try {
      const created = needsCompany
        ? await usersApi.createClient(first, last, email.trim(), companyId)
        : await usersApi.createEmployee(first, last, email.trim(), accountType, needsBranch ? departmentId : undefined, needsBranch ? subBranchId || undefined : undefined);
      onCreated(created);
      setFirstName('');
      setLastName('');
      setEmail('');
      setCompanyId('');
      setDepartmentId('');
      setSubBranchId('');
    } catch (err) {
      setError(messageOf(err, t('createacc_createFail')));
    } finally {
      setLoading(false);
    }
  }

  return {
    values: { firstName, lastName, email, accountType, companyId, departmentId, subBranchId },
    set: {
      firstName: setFirstName,
      lastName: setLastName,
      email: setEmail,
      accountType: setAccountType,
      companyId: setCompanyId,
      department: (id: string) => {
        setDepartmentId(id);
        setSubBranchId('');
      },
      subBranchId: setSubBranchId,
    },
    needsCompany,
    needsBranch,
    companies,
    departments,
    subBranches,
    subBranchChoices,
    error,
    loading,
    submit,
  };
}
