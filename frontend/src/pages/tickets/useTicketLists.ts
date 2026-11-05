import { useEffect, useRef } from 'react';
import { useDepartments, useHelpTopics, useSlaPlans, useTicketCategories } from '../../api/lookupQueries';

const NONE: never[] = [];

/**
 * The lists a ticket is opened with (help topics, branches, SLA plans, categories), shared with the other screens: fetched once, not on every visit.
 * The first entry of each of the three that need a choice is chosen for the person, once, when the list arrives: whatever they choose afterwards
 * stays. An administrator also gets the first branch, unless one was chosen already (by choosing who gets the ticket, for instance).
 * `failed` is true when any of the lists could not be had, so that an empty list is not taken for the whole truth.
 */
export function useTicketLists({
  isAdmin,
  departmentId,
  onTopic,
  onSla,
  onDepartment,
}: {
  isAdmin: boolean;
  departmentId: string;
  onTopic: (id: string) => void;
  onSla: (id: string) => void;
  onDepartment: (id: string) => void;
}) {
  const helpTopics = useHelpTopics();
  const departments = useDepartments();
  const slaPlans = useSlaPlans();
  const categories = useTicketCategories();

  const defaulted = useRef({ topic: false, sla: false, department: false });
  const firstTopic = helpTopics.data?.[0]?.id;
  const firstSla = slaPlans.data?.[0]?.id;
  const firstDepartment = departments.data?.[0]?.id;

  useEffect(() => {
    if (!defaulted.current.topic && firstTopic) {
      defaulted.current.topic = true;
      onTopic(firstTopic);
    }
  }, [firstTopic, onTopic]);
  useEffect(() => {
    if (!defaulted.current.sla && firstSla) {
      defaulted.current.sla = true;
      onSla(firstSla);
    }
  }, [firstSla, onSla]);
  useEffect(() => {
    if (!defaulted.current.department && isAdmin && firstDepartment) {
      defaulted.current.department = true;
      if (!departmentId) onDepartment(firstDepartment);
    }
  }, [firstDepartment, isAdmin, departmentId, onDepartment]);

  return {
    helpTopics: helpTopics.data ?? NONE,
    departments: departments.data ?? NONE,
    slaPlans: slaPlans.data ?? NONE,
    categories: categories.data ?? NONE,
    failed: [helpTopics, departments, slaPlans, categories].some((q) => q.isError),
  };
}
