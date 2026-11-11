import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ticketsApi } from '../../api/tickets';
import { emailIntegrationApi, type EmailPrefillDto } from '../../api/emailIntegration';
import { useSubBranches } from '../../api/lookupQueries';
import { useEmployees } from '../../api/userQueries';
import { useAuthStore } from '../../store/authStore';
import { newIdempotencyKey } from '../../lib/idempotency';
import { isBlankHtml } from '../../lib/htmlText';
import { finishNewTicket } from '../../lib/finishNewTicket';
import type { ClientLookupResult, TicketPriority, TicketSource, UserListItemDto } from '../../types';
import { dueDateToUtc } from './ticketOptions';
import { useTicketLists } from './useTicketLists';
import { useLanguage } from '../../i18n/LanguageContext';

const NO_ITEMS: never[] = [];

/**
 * Everything the form for a ticket opened on behalf of a client knows and does: what was typed and chosen, how the branch, the sub-branch and
 * the person who gets the ticket follow one another, and the sending of it. It lives above the form, not in it, so that what was typed is not lost
 * when the search for another client fails and the form goes away for a moment.
 */
export function useOnBehalfTicket({ client, prefill, emailMessageId }: { client: ClientLookupResult | null; prefill: EmailPrefillDto | undefined; emailMessageId: string | null }) {
  const { t } = useLanguage();
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const isAdmin = user?.role === 'Admin';

  const [title, setTitle] = useState(prefill?.subject ?? '');
  const [description, setDescription] = useState(prefill?.bodyHtml ?? '');
  const [files, setFiles] = useState<File[]>([]);
  const [source, setSource] = useState<TicketSource>(prefill ? 'Email' : 'Phone');
  const [priority, setPriority] = useState<TicketPriority>('Medium');
  const [helpTopicId, setHelpTopicId] = useState('');
  // Non-admin staff can only open tickets into their own branch; Admin may pick any branch.
  const [departmentId, setDepartmentId] = useState(isAdmin ? '' : user?.departmentId ?? '');
  const [subBranchId, setSubBranchId] = useState('');
  const [slaPlanId, setSlaPlanId] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [assignedToUserId, setAssignedToUserId] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  // One key per visit to this form: a retry of the same submission gets the existing ticket back, not a duplicate.
  const [idempotencyKey] = useState(newIdempotencyKey);
  // The files that went up already, kept between attempts so that a second attempt does not attach them again.
  const uploadedFiles = useRef(new Set<File>());

  const lists = useTicketLists({ isAdmin, departmentId, onTopic: setHelpTopicId, onSla: setSlaPlanId, onDepartment: setDepartmentId });

  // Full employee roster (not branch-filtered) so picking an assignee can drive the branch selection, not just the other way around.
  const employees = useEmployees();
  const allEmployees = employees.data ?? NO_ITEMS;

  // The sub-branches are asked for per branch, so an answer for a branch that is no longer the chosen one is never shown. When the branch changes,
  // the sub-branch of the person who gets the ticket is chosen too, if they belong to this branch, rather than just clearing it every time.
  const subBranchQuery = useSubBranches(departmentId);
  const subBranches = subBranchQuery.data ?? NO_ITEMS;
  const loadedSubBranches = subBranchQuery.data;
  useEffect(() => {
    if (!departmentId || !loadedSubBranches) {
      setSubBranchId('');
      return;
    }
    const assignee = allEmployees.find((e) => e.id === assignedToUserId);
    if (assignee?.departmentId === departmentId && assignee.subBranchId && loadedSubBranches.some((s) => s.id === assignee.subBranchId)) {
      setSubBranchId(assignee.subBranchId);
    } else {
      setSubBranchId('');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the choice is made when the branch (or its sub-branches) changes; picking an assignee must not redo it
  }, [departmentId, loadedSubBranches]);

  /** Picking an assignee is the primary flow for an administrator: their branch (and sub-branch) is adopted. Staff have a fixed branch. */
  function changeAssignee(userId: string) {
    setAssignedToUserId(userId);
    if (!isAdmin) return;
    const employee = allEmployees.find((e) => e.id === userId);
    if (employee?.departmentId && employee.departmentId !== departmentId) setDepartmentId(employee.departmentId);
  }

  /** Changing the branch lets go of the assignee only if they are not in the new branch: picking the branch they are in keeps them. */
  function changeDepartment(newDepartmentId: string) {
    setDepartmentId(newDepartmentId);
    const assignee = allEmployees.find((e) => e.id === assignedToUserId);
    if (assignee && assignee.departmentId !== newDepartmentId) setAssignedToUserId('');
  }

  // Admin can assign across any branch; non-admin staff only within their own fixed branch.
  const assignableEmployees = isAdmin ? allEmployees : allEmployees.filter((e) => e.departmentId === user?.departmentId);
  const employeesByDepartment = new Map<string, UserListItemDto[]>();
  for (const emp of assignableEmployees) {
    const key = emp.departmentId ?? '';
    if (!employeesByDepartment.has(key)) employeesByDepartment.set(key, []);
    employeesByDepartment.get(key)!.push(emp);
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!client) return;
    if (!title.trim() || isBlankHtml(description)) {
      setError(t('newTicket_titleDescRequired'));
      return;
    }
    if (!helpTopicId || !departmentId || !slaPlanId || !assignedToUserId) {
      setError(t('onbehalf_errAllRequired'));
      return;
    }
    if (subBranches.length > 0 && !subBranchId) {
      setError(t('common_selectSubBranch'));
      return;
    }
    setLoading(true);
    let ticket;
    try {
      ticket = await ticketsApi.createOnBehalf(
        {
          clientEmail: client.email,
          title: title.trim(),
          description,
          source,
          helpTopicId,
          departmentId,
          subBranchId: subBranchId || undefined,
          slaPlanId,
          priority,
          dueDateUtc: dueDateToUtc(dueDate),
          assignedToUserId,
          categoryId: categoryId || undefined,
        },
        idempotencyKey
      );
    } catch (err: any) {
      setError(err?.response?.data?.message ?? t('onbehalf_createFail'));
      setLoading(false);
      return;
    }

    try {
      await finishNewTicket(ticket.id, files, emailMessageId, uploadedFiles.current, {
        upload: ticketsApi.uploadAttachments,
        complete: emailIntegrationApi.completeTicket,
      });
      navigate(`/tickets/${ticket.id}`);
    } catch (err: any) {
      // The ticket exists by now. Saying "could not create" here sent people to try again; the same idempotency key means that
      // sending the form again does not make a second ticket, it only repeats the part that failed.
      setError(t('onbehalf_filesFail', { number: ticket.ticketNumber, reason: err?.response?.data?.message ?? t('onbehalf_uploadFailed') }));
    } finally {
      setLoading(false);
    }
  }

  return {
    isAdmin,
    ownBranchName: user?.departmentName ?? null,
    lists,
    staffFailed: employees.isError,
    employeesByDepartment,
    subBranches,
    values: { title, description, files, source, priority, helpTopicId, departmentId, subBranchId, slaPlanId, dueDate, assignedToUserId, categoryId },
    set: { setTitle, setDescription, setSource, setPriority, setHelpTopicId, setSubBranchId, setSlaPlanId, setDueDate, setCategoryId, changeAssignee, changeDepartment },
    addFiles: (added: File[]) => setFiles((prev) => [...prev, ...added]),
    removeFile: (index: number) => setFiles((prev) => prev.filter((_, i) => i !== index)),
    error,
    loading,
    submit,
  };
}

export type OnBehalfTicket = ReturnType<typeof useOnBehalfTicket>;
