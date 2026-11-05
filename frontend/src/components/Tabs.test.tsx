import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Tabs } from './Tabs';

const TABS = [
  { key: 'a', label: 'First' },
  { key: 'b', label: 'Second' },
] as const;

describe('the tabs', () => {
  it('are a list of tabs with a name, and the one that is chosen is said to be chosen', () => {
    render(<Tabs label="Kinds" tabs={[...TABS]} active="b" onSelect={() => {}} />);

    expect(screen.getByRole('tablist', { name: 'Kinds' })).toBeInTheDocument();
    expect(screen.getAllByRole('tab').map((t) => [t.textContent, t.getAttribute('aria-selected')])).toEqual([
      ['First', 'false'],
      ['Second', 'true'],
    ]);
  });

  it('say that none is chosen when none is the one that is shown', () => {
    render(<Tabs label="Kinds" tabs={[...TABS]} active={null} onSelect={() => {}} />);

    expect(screen.getAllByRole('tab').map((t) => t.getAttribute('aria-selected'))).toEqual(['false', 'false']);
  });

  it('hand on the key of the tab that was pressed, and are not buttons that send a form', async () => {
    const onSelect = vi.fn();
    const onSubmit = vi.fn((e: React.FormEvent) => e.preventDefault());
    render(
      <form onSubmit={onSubmit}>
        <Tabs label="Kinds" tabs={[...TABS]} active="a" onSelect={onSelect} />
      </form>
    );

    await userEvent.click(screen.getByRole('tab', { name: 'Second' }));

    expect(onSelect).toHaveBeenCalledWith('b');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('leave room around them when asked to', () => {
    render(<Tabs label="Kinds" tabs={[...TABS]} active="a" onSelect={() => {}} className="mb-4" />);

    expect(screen.getByRole('tablist')).toHaveClass('mb-4');
  });
});
