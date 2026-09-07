import { apiClient } from './client';
import type { KnowledgeBaseDocumentDto, KnowledgeBaseSearchResultDto, SimilarTicketsResponseDto } from '../types';

export interface KnowledgeBaseAskResponse {
  answer: string;
  sources: KnowledgeBaseSearchResultDto[];
  similarTicketCount: number;
  totalEligibleTicketCount: number;
  similarPercentage: number;
}

export const knowledgeBaseApi = {
  search: (query: string) =>
    apiClient
      .get<KnowledgeBaseSearchResultDto[]>('/api/knowledge-base/search', { params: { query } })
      .then((r) => r.data),

  // signal lets the caller cancel an in-flight request (e.g. "Stop" button) via AbortController.
  ask: (question: string, signal?: AbortSignal) =>
    apiClient.post<KnowledgeBaseAskResponse>('/api/knowledge-base/ask', { question }, { signal }).then((r) => r.data),

  askClient: (question: string, signal?: AbortSignal) =>
    apiClient.post<KnowledgeBaseAskResponse>('/api/knowledge-base/ask-client', { question }, { signal }).then((r) => r.data),

  similarTickets: (query: string) =>
    apiClient
      .get<SimilarTicketsResponseDto>('/api/knowledge-base/similar-tickets', { params: { query } })
      .then((r) => r.data),

  reindexAll: () => apiClient.post<{ indexed: number }>('/api/knowledge-base/reindex-all').then((r) => r.data),

  listDocuments: () =>
    apiClient.get<KnowledgeBaseDocumentDto[]>('/api/knowledge-base/documents').then((r) => r.data),

  uploadDocuments: (files: File[]) => {
    const form = new FormData();
    files.forEach((f) => form.append('files', f));
    return apiClient
      .post<KnowledgeBaseDocumentDto[]>('/api/knowledge-base/documents', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      })
      .then((r) => r.data);
  },

  deleteDocument: (id: string) => apiClient.delete(`/api/knowledge-base/documents/${id}`).then((r) => r.data),
};
