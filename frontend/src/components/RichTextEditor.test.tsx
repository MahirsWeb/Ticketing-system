import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { RichTextEditor } from './RichTextEditor';

describe('the rich text editor', () => {
  it('names every control, so a screen reader does not announce a bare list box or a bare file chooser (TS-19.1)', async () => {
    const { container } = render(<RichTextEditor value="" onChange={vi.fn()} onFilesSelected={vi.fn()} ariaLabel="Reply" />);
    await screen.findByRole('textbox', { name: 'Reply' });

    const unnamed = [...container.querySelectorAll('input, select, textarea')].filter(
      (e) => !(e as HTMLInputElement).labels?.length && !e.getAttribute('aria-label') && !e.getAttribute('aria-labelledby')
    );
    expect(unnamed.map((e) => e.outerHTML.slice(0, 60))).toEqual([]);
  });
});
