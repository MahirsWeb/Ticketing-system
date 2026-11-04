import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ClientAiHelpPanel } from './ClientAiHelpPanel';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import { LanguageProvider } from '../../i18n/LanguageContext';

vi.mock('../../api/knowledgeBase', () => ({ knowledgeBaseApi: { askClient: vi.fn() } }));

const ask = vi.mocked(knowledgeBaseApi.askClient);

async function askSomething(aiAvailable: boolean) {
  ask.mockResolvedValue({ answer: 'See the article.', sources: [], similarTicketCount: 0, totalEligibleTicketCount: 0, similarPercentage: 0, aiAvailable });
  render(
    <LanguageProvider>
      <ClientAiHelpPanel />
    </LanguageProvider>
  );
  await userEvent.click(screen.getByRole('button', { name: /Ask the AI assistant/ }));
  await userEvent.type(screen.getByRole('textbox'), 'How do I reset my password?');
  await userEvent.keyboard('{Enter}');
}

describe('the client AI help panel', () => {
  beforeEach(() => ask.mockReset());

  it('says clearly when AI is switched off on this installation (TS-DEP-03.3)', async () => {
    await askSomething(false);

    expect(await screen.findByRole('status')).toHaveTextContent('AI is switched off or not configured');
  });

  it('says nothing about it when AI answered', async () => {
    await askSomething(true);

    await screen.findByText('See the article.');
    expect(screen.queryByText(/switched off/)).not.toBeInTheDocument();
  });
});
