import { beforeAll, describe, expect, it, vi } from 'vitest';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import { RichTextEditor } from './RichTextEditor';

// jsdom does no layout, and ProseMirror asks for rectangles when it scrolls the cursor into view after typing.
beforeAll(() => {
  const noRects = () => [] as unknown as DOMRectList;
  Element.prototype.getClientRects = noRects;
  Range.prototype.getClientRects = noRects;
  Range.prototype.getBoundingClientRect = () => new DOMRect();
});

describe('the rich text editor', () => {
  it('names every control, so a screen reader does not announce a bare list box or a bare file chooser (TS-19.1)', async () => {
    const { container } = render(<RichTextEditor value="" onChange={vi.fn()} onFilesSelected={vi.fn()} ariaLabel="Reply" />);
    await screen.findByRole('textbox', { name: 'Reply' });

    const unnamed = [...container.querySelectorAll('input, select, textarea')].filter(
      (e) => !(e as HTMLInputElement).labels?.length && !e.getAttribute('aria-label') && !e.getAttribute('aria-labelledby')
    );
    expect(unnamed.map((e) => e.outerHTML.slice(0, 60))).toEqual([]);
  });

  it('names every toolbar button, including the three that show only an arrow (TS-46.5), and says which one is on', async () => {
    const { container } = render(<RichTextEditor value="<p>x</p>" onChange={vi.fn()} ariaLabel="Reply" />);
    await screen.findByRole('textbox', { name: 'Reply' });

    const buttons = [...container.querySelectorAll('button')];
    expect(buttons.length).toBeGreaterThanOrEqual(8);
    expect(buttons.filter((b) => !b.getAttribute('aria-label')).map((b) => b.outerHTML.slice(0, 60))).toEqual([]);
    expect(buttons.filter((b) => b.getAttribute('aria-pressed') === null)).toEqual([]);
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

  it('passes what is typed on to the parent, and the value that comes back does not disturb the editor', async () => {
    const seen: string[] = [];
    function Host() {
      const [v, setV] = useState('<p>start</p>');
      return (
        <RichTextEditor
          value={v}
          onChange={(html) => {
            seen.push(html);
            setV(html);
          }}
          ariaLabel="Editor"
        />
      );
    }
    render(<Host />);
    const box = await screen.findByRole('textbox', { name: 'Editor' });
    expect(box.innerHTML).toContain('start');

    box.focus();
    await userEvent.keyboard('X');

    expect(seen.length).toBeGreaterThan(0); // typing really reached the parent
    expect(seen.at(-1)).toContain('X');
    expect(seen.at(-1)).toContain('start');
    expect(box.textContent).toContain('X'); // and the round trip left what was typed on the screen
  });
});
