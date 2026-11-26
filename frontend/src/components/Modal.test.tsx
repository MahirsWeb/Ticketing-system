import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Modal } from './Modal';
import { ConfirmDialog } from './ConfirmDialog';

describe('Modal', () => {
  it('is a named dialog', () => {
    render(
      <Modal title="Delete this?" onClose={() => {}}>
        <button>Ok</button>
      </Modal>
    );

    const dialog = screen.getByRole('dialog', { name: 'Delete this?' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
  });

  it('puts focus inside when it opens', () => {
    render(
      <Modal title="T" onClose={() => {}}>
        <button>First</button>
        <button>Second</button>
      </Modal>
    );

    expect(screen.getByRole('button', { name: 'First' })).toHaveFocus();
  });

  it('closes on Escape', async () => {
    const onClose = vi.fn();
    render(
      <Modal title="T" onClose={onClose}>
        <button>Ok</button>
      </Modal>
    );

    await userEvent.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('keeps Tab inside, in both directions', async () => {
    render(
      <>
        <button>Outside</button>
        <Modal title="T" onClose={() => {}}>
          <button>First</button>
          <button>Last</button>
        </Modal>
      </>
    );
    const first = screen.getByRole('button', { name: 'First' });
    const last = screen.getByRole('button', { name: 'Last' });

    last.focus();
    await userEvent.tab();
    expect(first).toHaveFocus();

    await userEvent.tab({ shift: true });
    expect(last).toHaveFocus();
  });

  it('gives focus back to what had it before', async () => {
    function Page() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button onClick={() => setOpen(true)}>Open</button>
          {open && <ConfirmDialog title="Sure?" message="Really?" onConfirm={() => setOpen(false)} onCancel={() => setOpen(false)} />}
        </>
      );
    }
    render(<Page />);
    const trigger = screen.getByRole('button', { name: 'Open' });

    await userEvent.click(trigger);
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
});

describe('Modal, one inside another', () => {
  it('lets only the one on top hear Escape: a question asked from inside a dialog closes alone', async () => {
    const under = vi.fn();
    const top = vi.fn();
    render(
      <>
        <Modal title="Below" onClose={under}>
          <button>Below ok</button>
        </Modal>
        <Modal title="On top" onClose={top}>
          <button>Top ok</button>
        </Modal>
      </>
    );

    await userEvent.keyboard('{Escape}');

    expect(top).toHaveBeenCalledTimes(1);
    expect(under).not.toHaveBeenCalled();
  });

  it('is heard again by the one below when the one on top is gone', async () => {
    const under = vi.fn();
    function Both() {
      const [showTop, setShowTop] = useState(true);
      return (
        <>
          <Modal title="Below" onClose={under}>
            <button>Below ok</button>
          </Modal>
          {showTop && (
            <Modal title="On top" onClose={() => setShowTop(false)}>
              <button>Top ok</button>
            </Modal>
          )}
        </>
      );
    }
    render(<Both />);

    await userEvent.keyboard('{Escape}'); // closes the one on top
    expect(screen.queryByRole('dialog', { name: 'On top' })).not.toBeInTheDocument();
    await userEvent.keyboard('{Escape}');

    expect(under).toHaveBeenCalledTimes(1);
  });
});

describe('Modal that is longer than the screen', () => {
  it('scrolls inside itself when it is asked to, and does not otherwise', () => {
    const { rerender } = render(
      <Modal title="Form" onClose={() => {}} scroll>
        <p>long</p>
      </Modal>
    );
    expect(screen.getByRole('dialog').parentElement?.className).toContain('overflow-y-auto');

    rerender(
      <Modal title="Form" onClose={() => {}}>
        <p>short</p>
      </Modal>
    );
    expect(screen.getByRole('dialog').parentElement?.className).not.toContain('overflow-y-auto');
  });

  it('shows what it is given at the end of the title row, which is a way to close it that the keyboard also reaches', async () => {
    const onClose = vi.fn();
    render(
      <Modal title="Form" onClose={onClose} headerAction={<button onClick={onClose}>Close it</button>}>
        <p>body</p>
      </Modal>
    );

    await userEvent.click(screen.getByRole('button', { name: 'Close it' }));

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('dialog', { name: 'Form' })).toBeInTheDocument();
  });
});

describe('ConfirmDialog', () => {
  it('confirms and cancels', async () => {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    render(<ConfirmDialog title="Delete?" message="This cannot be undone." confirmLabel="Delete" danger onConfirm={onConfirm} onCancel={onCancel} />);

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
