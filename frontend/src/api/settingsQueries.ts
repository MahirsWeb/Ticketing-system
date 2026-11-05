import { useQuery } from '@tanstack/react-query';
import { settingsApi } from './settings';
import { knowledgeBaseApi } from './knowledgeBase';

export const settingsKeys = {
  overdue: ['settings', 'overdue-notifications'] as const,
  documents: ['knowledge-base', 'documents'] as const,
};

/** The settings are asked for each time the page is opened; a form is only drawn from settings that were really received. */
export function useOverdueSettings() {
  return useQuery({ queryKey: settingsKeys.overdue, queryFn: settingsApi.getOverdueNotifications, gcTime: 0 });
}

/** The documentation uploaded for the AI assistant. Whoever uploads or removes one asks for the list again. */
export function useKnowledgeBaseDocuments() {
  return useQuery({ queryKey: settingsKeys.documents, queryFn: knowledgeBaseApi.listDocuments, gcTime: 0 });
}
