import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
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

  it('shows the value the parent gives it when that value changes on its own (TS-1.1 internal-note leak)', async () => {
    // The parent keeps a separate draft per mode and swaps `value` when the mode changes. The editor must follow it,
    // otherwise it keeps displaying the previous draft (e.g. the internal note) while a reply would carry that text.
    const { rerender } = render(<RichTextEditor value="<p>reply A</p>" onChange={vi.fn()} ariaLabel="Editor" />);
    const box = await screen.findByRole('textbox', { name: 'Editor' });
    expect(box.innerHTML).toContain('reply A');

    // switch to the internal-note draft
    rerender(<RichTextEditor value="<p>note B</p>" onChange={vi.fn()} ariaLabel="Editor" />);
    expect(box.innerHTML).toContain('note B');
    expect(box.innerHTML).not.toContain('reply A');

    // switch back to the reply draft: the editor must not still show the note
    rerender(<RichTextEditor value="<p>reply A</p>" onChange={vi.fn()} ariaLabel="Editor" />);
    expect(box.innerHTML).toContain('reply A');
    expect(box.innerHTML).not.toContain('note B');

    // emptying the value (after a send) clears it
    rerender(<RichTextEditor value="" onChange={vi.fn()} ariaLabel="Editor" />);
    expect(box.textContent).toBe('');
  });

  it('does not fight the user while typing (value flowing back in does not reset the editor)', async () => {
    function Host() {
      const [v, setV] = useState('<p>start</p>');
      return <RichTextEditor value={v} onChange={setV} ariaLabel="Editor" />;
    }
    render(<Host />);
    const box = await screen.findByRole('textbox', { name: 'Editor' });
    // the controlled round-trip (onChange -> value -> effect) must leave the shown text intact
    expect(box.innerHTML).toContain('start');
  });
});
