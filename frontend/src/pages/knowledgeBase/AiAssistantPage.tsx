import { KnowledgeBaseSearchPanel } from '../tickets/KnowledgeBaseSearchPanel';

export default function AiAssistantPage() {
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">AI Assistant</h1>
        <p className="mt-1 text-sm text-slate-500">
          Ask a general question about how the system/product works, or describe a client's problem — the AI
          searches past resolved tickets and uploaded documentation for the answer.
        </p>
      </div>
      <KnowledgeBaseSearchPanel alwaysOpen />
    </div>
  );
}
