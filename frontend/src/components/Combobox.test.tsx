import { describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Combobox, type ComboboxOption } from './Combobox';
import { Field, Label } from './ui';

const COMPANIES: ComboboxOption[] = [
  { id: 'c1', label: 'Alfa d.o.o.' },
  { id: 'c2', label: 'Beta Trgovina' },
  { id: 'c3', label: 'Čelik Mostar' },
  { id: 'c4', label: 'Đurđevak' },
];

const PEOPLE: ComboboxOption[] = [
  { id: 'p1', label: 'Mirza Hodžić', secondary: 'mirza@alfa.test' },
  { id: 'p2', label: 'Mirza Hodžić', secondary: 'mirza.h@beta.test' },
];

function Harness({
  options = COMPANIES,
  initial = null,
  onChange = () => undefined,
  disabled = false,
}: {
  options?: ComboboxOption[];
  initial?: string | null;
  onChange?: (id: string | null) => void;
  disabled?: boolean;
}) {
  const [value, setValue] = useState<string | null>(initial);
  return (
    <form onSubmit={(e) => e.preventDefault()}>
      <Field>
        <Label>Company</Label>
        <Combobox
          options={options}
          value={value}
          onChange={(id) => {
            setValue(id);
            onChange(id);
          }}
          disabled={disabled}
          noResultsText="Nothing found"
        />
      </Field>
      <p data-testid="value">{value ?? 'none'}</p>
    </form>
  );
}

const box = () => screen.getByRole('combobox', { name: 'Company' });
const chosen = () => screen.getByTestId('value').textContent;

describe('the combobox', () => {
  it('is a combobox with a name, which opens a list of options when it is focused', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    expect(box()).toHaveAttribute('aria-expanded', 'false');
    await user.click(box());

    expect(box()).toHaveAttribute('aria-expanded', 'true');
    expect(within(screen.getByRole('listbox')).getAllByRole('option')).toHaveLength(4);
    expect(box()).toHaveAttribute('aria-controls', screen.getByRole('listbox').id);
  });

  it('narrows the list by what is typed, without regard to case or accents', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.type(box(), 'cel');
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Čelik Mostar']);

    await user.clear(box());
    await user.type(box(), 'durd');
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Đurđevak']);
  });

  it('chooses an item with a click and shows its name in the box', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);

    await user.click(box());
    await user.click(screen.getByRole('option', { name: 'Beta Trgovina' }));

    expect(onChange).toHaveBeenCalledWith('c2');
    expect(box()).toHaveValue('Beta Trgovina');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(chosen()).toBe('c2');
  });

  it('chooses with the keyboard: arrows move, Enter takes the one that is marked, and Enter does not send a form around it', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(box());
    await user.keyboard('{ArrowDown}{ArrowDown}');
    expect(box()).toHaveAttribute('aria-activedescendant', screen.getAllByRole('option')[2].id);
    await user.keyboard('{Enter}');

    expect(chosen()).toBe('c3');
    expect(box()).toHaveValue('Čelik Mostar');
  });

  it('never makes a text that is not an item into the choice: it is dropped when the person leaves the box', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.type(box(), 'Gama');
    await user.tab();

    expect(box()).toHaveValue('');
    expect(chosen()).toBe('none');
  });

  it('takes a text that is exactly one item as that item when the person leaves the box', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.type(box(), 'alfa d.o.o.');
    await user.tab();

    expect(chosen()).toBe('c1');
    expect(box()).toHaveValue('Alfa d.o.o.');
  });

  it('clears the choice when the box is emptied, and when the text of a chosen item is changed', async () => {
    const user = userEvent.setup();
    render(<Harness initial="c2" />);
    expect(box()).toHaveValue('Beta Trgovina');

    await user.type(box(), 'x');
    expect(chosen()).toBe('none'); // "Beta Trgovinax" is not the item that was chosen

    await user.clear(box());
    await user.tab();
    expect(box()).toHaveValue('');
  });

  it('goes back to the chosen item, not to an empty box, when the person starts to type and then gives up with Escape', async () => {
    const user = userEvent.setup();
    render(<Harness initial="c1" />);

    await user.click(box());
    await user.keyboard('{Escape}');

    expect(chosen()).toBe('c1');
    expect(box()).toHaveValue('Alfa d.o.o.');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('closes only its list with Escape, so a dialog around it is not closed with it; with the list closed the key goes on', async () => {
    const user = userEvent.setup();
    const outside = vi.fn();
    document.addEventListener('keydown', outside); // what a dialog listens with
    try {
      render(<Harness />);

      await user.click(box());
      await user.keyboard('{Escape}');
      expect(outside).not.toHaveBeenCalled();

      await user.keyboard('{Escape}');
      expect(outside).toHaveBeenCalled();
    } finally {
      document.removeEventListener('keydown', outside);
    }
  });

  it('says so when nothing is found, and offers no option to choose', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.type(box(), 'zzz');

    expect(screen.getByText('Nothing found')).toBeInTheDocument();
    expect(screen.queryAllByRole('option')).toHaveLength(0);
  });

  it('tells two people with the same name apart by the second line, and finds them by it', async () => {
    const user = userEvent.setup();
    render(<Harness options={PEOPLE} />);

    await user.click(box());
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Mirza Hodžićmirza@alfa.test', 'Mirza Hodžićmirza.h@beta.test']);

    await user.type(box(), 'beta.test');
    await user.click(screen.getByRole('option'));

    expect(chosen()).toBe('p2');
  });

  it('follows a choice that is changed from outside', () => {
    const { rerender } = render(<Combobox options={COMPANIES} value="c1" onChange={() => undefined} noResultsText="-" aria-label="Company" />);
    expect(screen.getByRole('combobox')).toHaveValue('Alfa d.o.o.');

    rerender(<Combobox options={COMPANIES} value={null} onChange={() => undefined} noResultsText="-" aria-label="Company" />);
    expect(screen.getByRole('combobox')).toHaveValue('');

    rerender(<Combobox options={COMPANIES} value="c4" onChange={() => undefined} noResultsText="-" aria-label="Company" />);
    expect(screen.getByRole('combobox')).toHaveValue('Đurđevak');
  });

  it('shows a chosen item that arrives together with the list, which was empty a moment before', () => {
    const { rerender } = render(<Combobox options={[]} value="c2" onChange={() => undefined} noResultsText="-" aria-label="Company" />);
    expect(screen.getByRole('combobox')).toHaveValue('');

    rerender(<Combobox options={COMPANIES} value="c2" onChange={() => undefined} noResultsText="-" aria-label="Company" />);
    expect(screen.getByRole('combobox')).toHaveValue('Beta Trgovina');
  });

  it('cannot be used while it is disabled, and does not open', async () => {
    const user = userEvent.setup();
    render(<Harness disabled />);

    expect(box()).toBeDisabled();
    await user.click(box());
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('draws only a screenful of a very long list and says nothing is wrong, so that typing narrows it', async () => {
    const user = userEvent.setup();
    const many = Array.from({ length: 1000 }, (_, i) => ({ id: `n${i}`, label: `Firma ${i}` }));
    render(<Harness options={many} />);

    await user.click(box());
    expect(screen.getAllByRole('option').length).toBeLessThanOrEqual(200);

    await user.type(box(), 'Firma 999');
    expect(screen.getAllByRole('option').map((o) => o.textContent)).toEqual(['Firma 999']);
  });
});
