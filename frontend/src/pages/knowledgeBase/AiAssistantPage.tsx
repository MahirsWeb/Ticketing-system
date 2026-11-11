import { KnowledgeBaseSearchPanel } from '../tickets/KnowledgeBaseSearchPanel';
import { useLanguage } from '../../i18n/LanguageContext';

export default function AiAssistantPage() {
  const { t } = useLanguage();
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">{t('nav_aiAssistant')}</h1>
        <p className="mt-1 text-sm text-slate-600">{t('kb_assistantIntro')}</p>
      </div>
      <KnowledgeBaseSearchPanel alwaysOpen />
    </div>
  );
}
