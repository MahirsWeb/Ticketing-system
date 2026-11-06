import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import AiAssistantPage from './AiAssistantPage';
import { renderWithQuery } from '../../test/renderWithQuery';

describe('the AI assistant page', () => {
  it('names the field where the problem is described (a placeholder is not a name)', () => {
    renderWithQuery(
      <MemoryRouter>
        <AiAssistantPage />
      </MemoryRouter>
    );

    const field = screen.getByRole('textbox', { name: 'Describe the problem' });

    expect(field).toHaveAttribute('placeholder', expect.stringContaining('keywords'));
  });

  it('has a title for the page and the panel open from the start', () => {
    renderWithQuery(
      <MemoryRouter>
        <AiAssistantPage />
      </MemoryRouter>
    );

    expect(screen.getByRole('heading', { level: 1, name: 'AI Assistant' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Describe the problem' })).toBeVisible();
  });
});
