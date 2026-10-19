import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ErrorBoundary } from './ErrorBoundary';
import { LanguageProvider } from '../i18n/LanguageContext';

let shouldThrow = true;
function Fragile() {
  if (shouldThrow) throw new Error('boom');
  return <p>all good</p>;
}

describe('ErrorBoundary', () => {
  beforeEach(() => {
    shouldThrow = true;
    // React logs a caught render error; that noise is expected here
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });
  afterEach(() => vi.restoreAllMocks());

  it('shows a message instead of a blank page, and the app can recover', async () => {
    render(
      <LanguageProvider>
        <ErrorBoundary>
          <Fragile />
        </ErrorBoundary>
      </LanguageProvider>
    );

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong');

    shouldThrow = false;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(screen.getByText('all good')).toBeInTheDocument();
  });

  it('shows nothing special while everything works', () => {
    shouldThrow = false;
    render(
      <LanguageProvider>
        <ErrorBoundary>
          <Fragile />
        </ErrorBoundary>
      </LanguageProvider>
    );

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
