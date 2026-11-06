import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DateRangePicker } from './DateRangePicker';

/** Today is Friday, 20 November 2026, at noon: the days 7 to 25 then appear once in the grid of November, so a day is found by its number. */
const TODAY = new Date(2026, 10, 20, 12, 0, 0);
const startOf = (y: number, m: number, d: number) => new Date(y, m, d, 0, 0, 0, 0);
const endOf = (y: number, m: number, d: number) => new Date(y, m, d, 23, 59, 59, 999);

const onChange = vi.fn();
let view: ReturnType<typeof render>;

function setup(props: Partial<React.ComponentProps<typeof DateRangePicker>> = {}) {
  view = render(
    <>
      <DateRangePicker from={null} to={null} onChange={onChange} {...props} />
      <button type="button">somewhere else</button>
    </>
  );
  return userEvent.setup();
}

/** The button that shows the range and opens the picker: the first button there is. */
const trigger = () => view.container.querySelector('button') as HTMLButtonElement;
const day = (n: number) => screen.getByRole('button', { name: new RegExp(`^${n}( |$)`) });
const heading = () => screen.getByRole('button', { name: /^[A-Z][a-z]+ \d{4}$/ });
const isOpen = () => screen.queryByRole('button', { name: 'Previous month' }) !== null;
const elsewhere = () => screen.getByRole('button', { name: 'somewhere else' });

beforeEach(() => {
  onChange.mockReset();
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(TODAY);
});
afterEach(() => vi.useRealTimers());

describe('the date range picker', () => {
  describe('what it shows', () => {
    it('names every arrow, so none of them is announced as just a symbol (TS-46.5)', async () => {
      const user = setup();
      await user.click(trigger());

      expect(screen.getByRole('button', { name: 'Previous month' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Next month' })).toBeInTheDocument();

      await user.click(heading()); // the month heading opens the months
      expect(screen.getByRole('button', { name: 'Previous year' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Next year' })).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /^\d{4}$/ })); // the year heading opens the years
      expect(screen.getByRole('button', { name: 'Previous years' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Next years' })).toBeInTheDocument();
    });

    it('shows days of the neighbouring months in a colour that can be read', async () => {
      const user = setup();
      await user.click(trigger());

      const outside = document.querySelectorAll('button.text-slate-600');
      expect(document.querySelector('.text-slate-300')).toBeNull();
      expect(outside.length).toBeGreaterThan(0);
      expect(screen.getByRole('button', { name: '26 October 2026' }).className).toContain('text-slate-600');
      expect(screen.getByRole('button', { name: '1 December 2026' }).className).toContain('text-slate-600');
      expect(day(12).className).not.toContain('text-slate-600');
    });

    it('shows whole weeks, Monday to Sunday, with the days of the months next to the month that fill the first and the last one', async () => {
      const user = setup();
      await user.click(trigger());

      const days = screen.getAllByRole('button', { name: /^\d{1,2} [A-Z][a-z]+ \d{4}$/ });

      expect(days[0]).toHaveAccessibleName('26 October 2026');
      expect(days[days.length - 1]).toHaveAccessibleName('6 December 2026');
      expect(days).toHaveLength(42);
      expect(screen.getAllByText(/^(Mo|Tu|We|Th|Fr|Sa|Su)$/).map((d) => d.textContent)).toEqual(['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su']);
    });

    it.each([
      { what: 'the placeholder when there is no range', props: {}, label: 'All time' },
      { what: 'a placeholder of its own', props: { placeholder: 'Any date' }, label: 'Any date' },
      { what: 'one date when the range is one day', props: { from: new Date(2026, 9, 5, 0, 0), to: new Date(2026, 9, 5, 23, 59) }, label: '05.10.2026' },
      { what: 'both dates', props: { from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) }, label: '05.10.2026 – 12.10.2026' },
      { what: 'today for a range that has no end', props: { from: new Date(2026, 9, 5), to: null }, label: '05.10.2026 – Today' },
      { what: 'dots for a range that has no beginning', props: { from: null, to: new Date(2026, 9, 12) }, label: '… – 12.10.2026' },
    ])('shows $what', ({ props, label }) => {
      setup(props);

      expect(trigger().textContent?.trim()).toBe(label);
    });

    it('opens at the month the range begins in, and at this month when there is no range', async () => {
      const user = setup({ from: new Date(2026, 8, 5), to: new Date(2026, 8, 9) });
      await user.click(trigger());
      expect(heading()).toHaveTextContent('September 2026');
      view.unmount();

      const again = setup();
      await again.click(trigger());
      expect(heading()).toHaveTextContent('November 2026');
    });

    it('opens at the month the range begins in again after it was left at another month', async () => {
      const user = setup({ from: new Date(2026, 8, 5), to: new Date(2026, 8, 9) });
      await user.click(trigger());
      await user.click(screen.getByRole('button', { name: 'Next month' }));
      expect(heading()).toHaveTextContent('October 2026');
      await user.click(trigger());

      await user.click(trigger());

      expect(heading()).toHaveTextContent('September 2026');
    });

    it('offers "All time" only where the range can be cleared (the button itself says it too while there is no range)', async () => {
      const user = setup();
      await user.click(trigger());
      expect(screen.getAllByRole('button', { name: 'All time' })).toHaveLength(1);
      view.unmount();

      const clearable = setup({ clearable: true });
      await clearable.click(trigger());
      expect(screen.getAllByRole('button', { name: 'All time' })).toHaveLength(2);
    });
  });

  describe('choosing days', () => {
    it('takes the first day for the start and the second for the end, and gives the start of one and the end of the other', async () => {
      const user = setup();
      await user.click(trigger());

      await user.click(day(10));
      expect(onChange).not.toHaveBeenCalled();
      await user.click(day(14));

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 10), endOf(2026, 10, 14));
      expect(isOpen()).toBe(false);
    });

    it('takes one day for a range of that day', async () => {
      const user = setup();
      await user.click(trigger());

      await user.click(day(10));
      await user.click(day(10));

      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 10), endOf(2026, 10, 10));
    });

    it('starts again when the second day is before the first', async () => {
      const user = setup();
      await user.click(trigger());

      await user.click(day(14));
      await user.click(day(10));
      expect(onChange).not.toHaveBeenCalled();
      await user.click(day(12));

      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 10), endOf(2026, 10, 12));
    });

    it('starts a new range from the first day chosen after a complete one', async () => {
      const user = setup({ from: new Date(2026, 10, 3), to: new Date(2026, 10, 8) });
      await user.click(trigger());

      await user.click(day(12));
      expect(onChange).not.toHaveBeenCalled();
      await user.click(day(15));

      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 12), endOf(2026, 10, 15));
    });

    it('does not allow a day that has not come yet', async () => {
      const user = setup();
      await user.click(trigger());

      expect(day(20)).toBeEnabled();
      expect(day(21)).toBeDisabled();
      await user.click(day(21));
      expect(onChange).not.toHaveBeenCalled();
    });

    it('marks the ends of the range, the days between them, and shows the range that the pointer is about to make', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));
      await user.hover(day(14));

      expect(day(10).className).toContain('bg-blue-700');
      expect(day(12).className).toContain('bg-blue-50');
      expect(day(14).className).toContain('bg-blue-700');
      expect(day(15).className).not.toContain('bg-blue-');
      expect(day(9).className).not.toContain('bg-blue-');
    });

    it('does not show a range for a day before the first one, and does not take that day for its end', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));
      await user.hover(day(8));

      expect(day(9).className).not.toContain('bg-blue-50');
      expect(day(8).className).not.toContain('bg-blue-700');
    });

    it('shows no new range for the pointer while a complete range is being shown', async () => {
      const user = setup({ from: new Date(2026, 10, 3), to: new Date(2026, 10, 8) });
      await user.click(trigger());

      await user.hover(day(15));

      expect(day(15).className).not.toContain('bg-blue-');
      expect(day(8).className).toContain('bg-blue-700');
      expect(day(12).className).not.toContain('bg-blue-');
    });
  });

  describe('moving about', () => {
    it('moves a month at a time', async () => {
      const user = setup();
      await user.click(trigger());

      await user.click(screen.getByRole('button', { name: 'Previous month' }));
      expect(heading()).toHaveTextContent('October 2026');
      await user.click(screen.getByRole('button', { name: 'Next month' }));
      await user.click(screen.getByRole('button', { name: 'Next month' }));
      expect(heading()).toHaveTextContent('December 2026');
    });

    it('chooses a month from the year, and a year from the block of years', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(heading());

      await user.click(screen.getByRole('button', { name: 'Previous year' }));
      expect(screen.getByRole('button', { name: '2025' })).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Mar' }));
      expect(heading()).toHaveTextContent('March 2025');

      await user.click(heading());
      await user.click(screen.getByRole('button', { name: '2025' }));
      expect(screen.getByText('2016 – 2027')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Previous years' }));
      expect(screen.getByText('2004 – 2015')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Next years' }));
      await user.click(screen.getByRole('button', { name: 'Next years' }));
      expect(screen.getByText('2028 – 2039')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: '2030' }));
      expect(screen.getByRole('button', { name: 'Mar' }).className).toContain('bg-blue-700');
      await user.click(screen.getByRole('button', { name: 'Jun' }));
      expect(heading()).toHaveTextContent('June 2030');
    });

    it('marks the month and the year that are shown', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(heading());
      expect(screen.getByRole('button', { name: 'Nov' }).className).toContain('bg-blue-700');
      expect(screen.getByRole('button', { name: 'Oct' }).className).not.toContain('bg-blue-700');

      await user.click(screen.getByRole('button', { name: '2026' }));
      expect(screen.getByRole('button', { name: '2026' }).className).toContain('bg-blue-700');
      expect(screen.getByRole('button', { name: '2025' }).className).not.toContain('bg-blue-700');
    });
  });

  describe('the presets', () => {
    it.each([
      { preset: 'Today', from: startOf(2026, 10, 20), to: endOf(2026, 10, 20) },
      { preset: 'Yesterday', from: startOf(2026, 10, 19), to: endOf(2026, 10, 19) },
      { preset: 'This week', from: startOf(2026, 10, 16), to: endOf(2026, 10, 20) },
      { preset: 'This month', from: startOf(2026, 10, 1), to: endOf(2026, 10, 20) },
      { preset: 'This year', from: startOf(2026, 0, 1), to: endOf(2026, 10, 20) },
    ])('"$preset" gives the whole of its first day to the end of its last, and closes', async ({ preset, from, to }) => {
      const user = setup();
      await user.click(trigger());

      await user.click(screen.getByRole('button', { name: preset }));

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange).toHaveBeenCalledWith(from, to);
      expect(isOpen()).toBe(false);
    });
  });

  describe('clearing', () => {
    it('has a cross only where it can be cleared and there is something to clear', () => {
      setup({ from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) });
      expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();
      view.unmount();

      setup({ clearable: true });
      expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();
      view.unmount();

      setup({ clearable: true, from: new Date(2026, 9, 5), to: null });
      expect(screen.getByRole('button', { name: 'Clear' })).toBeInTheDocument();
    });

    it('does not put the cross inside the button that opens the picker (a control inside a control cannot be reached by every reader)', () => {
      setup({ clearable: true, from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) });

      expect(within(trigger()).queryByRole('button')).not.toBeInTheDocument();
    });

    it('clears with the cross, without opening the picker', async () => {
      const user = setup({ clearable: true, from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) });

      await user.click(screen.getByRole('button', { name: 'Clear' }));

      expect(onChange).toHaveBeenCalledWith(null, null);
      expect(isOpen()).toBe(false);
    });

    it.each(['{Enter}', ' '])('clears with the cross from the keyboard (%j)', async (key) => {
      const user = setup({ clearable: true, from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) });
      screen.getByRole('button', { name: 'Clear' }).focus();

      await user.keyboard(key);

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange).toHaveBeenCalledWith(null, null);
      expect(isOpen()).toBe(false);
    });

    it('clears with "All time"', async () => {
      const user = setup({ clearable: true, from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) });
      await user.click(trigger());

      await user.click(screen.getByRole('button', { name: 'All time' }));

      expect(onChange).toHaveBeenCalledWith(null, null);
      expect(isOpen()).toBe(false);
    });
  });

  describe('leaving without finishing', () => {
    it('takes a start that was chosen, and today for the end, when the picker is left by a click elsewhere', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await user.click(document.body);

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 10), endOf(2026, 10, 20));
      expect(isOpen()).toBe(false);
    });

    it('takes it also when the button is pressed again', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await user.click(trigger());

      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 10), endOf(2026, 10, 20));
      expect(isOpen()).toBe(false);
    });

    it('changes nothing when nothing was chosen', async () => {
      const user = setup({ from: new Date(2026, 9, 5), to: new Date(2026, 9, 12) });
      await user.click(trigger());

      await user.click(document.body);

      expect(onChange).not.toHaveBeenCalled();
      expect(isOpen()).toBe(false);
    });

    it('asks once, not twice, when the click that leaves is on another button', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await user.click(elsewhere());

      expect(onChange).toHaveBeenCalledTimes(1);
    });

    it('opens at the days, whatever view it was left at', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(heading());
      expect(screen.queryByRole('button', { name: 'Previous month' })).not.toBeInTheDocument();
      await user.click(trigger());

      await user.click(trigger());

      expect(screen.getByRole('button', { name: 'Previous month' })).toBeInTheDocument();
    });

    it('does not take what was chosen before it was left with Escape when it is opened and left again', async () => {
      const user = setup({ from: new Date(2026, 10, 3), to: new Date(2026, 10, 8) });
      await user.click(trigger());
      await user.click(day(12));
      await user.keyboard('{Escape}');
      await user.click(trigger());

      await user.click(document.body);

      expect(onChange).not.toHaveBeenCalled();
    });

    it('asks once even when the click that leaves and the move of the focus that follows it come in one moment', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await act(async () => {
        fireEvent.mouseDown(elsewhere());
        fireEvent.focusIn(elsewhere());
      });

      expect(onChange).toHaveBeenCalledTimes(1);
    });

    it('is not left by a key that is not Escape', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await user.keyboard('a');
      await user.keyboard('{ArrowRight}');

      expect(isOpen()).toBe(true);
      expect(onChange).not.toHaveBeenCalled();
    });

    it('does not close when a click is inside', async () => {
      const user = setup();
      await user.click(trigger());

      await user.click(heading());

      expect(screen.getByRole('button', { name: 'Previous year' })).toBeInTheDocument();
    });
  });

  describe('what was wrong', () => {
    it('does not change a range that has no end only because the picker was opened and left', async () => {
      const user = setup({ from: new Date(2026, 9, 5), to: null });
      await user.click(trigger());

      await user.click(document.body);

      expect(onChange).not.toHaveBeenCalled();
    });

    it('does not change a range that has no end when the button is pressed again either', async () => {
      const user = setup({ from: new Date(2026, 9, 5), to: null });
      await user.click(trigger());

      await user.click(trigger());

      expect(onChange).not.toHaveBeenCalled();
    });

    it('opens, without failing, a range whose end is before its beginning (an address can say so)', async () => {
      const user = setup({ from: new Date(2026, 10, 10), to: new Date(2026, 10, 5) });

      await user.click(trigger());

      expect(isOpen()).toBe(true);
      expect(day(12).className).not.toContain('bg-blue-50');
    });

    it('takes the day of a start that has a time of day as the day it is, when the end is chosen on the same day', async () => {
      const user = setup({ from: new Date(2026, 10, 9, 8, 30), to: null });
      await user.click(trigger());

      await user.click(day(9));

      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 9), endOf(2026, 10, 9));
    });

    it('closes on Escape without taking what was chosen, and gives the focus back to the button', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await user.keyboard('{Escape}');

      expect(isOpen()).toBe(false);
      expect(onChange).not.toHaveBeenCalled();
      expect(trigger()).toHaveFocus();
    });

    it('takes what was chosen when the focus leaves the picker, as it does when a click leaves it', async () => {
      const user = setup();
      await user.click(trigger());
      await user.click(day(10));

      await act(async () => elsewhere().focus());

      expect(onChange).toHaveBeenCalledTimes(1);
      expect(onChange).toHaveBeenCalledWith(startOf(2026, 10, 10), endOf(2026, 10, 20));
      expect(isOpen()).toBe(false);
    });

    it('does not close when the focus moves inside the picker', async () => {
      const user = setup();
      await user.click(trigger());

      await act(async () => day(12).focus());

      expect(isOpen()).toBe(true);
    });

    it('says that the button opens a dialog, and whether it is open', async () => {
      const user = setup();
      expect(trigger()).toHaveAttribute('aria-haspopup', 'dialog');
      expect(trigger()).toHaveAttribute('aria-expanded', 'false');

      await user.click(trigger());

      expect(trigger()).toHaveAttribute('aria-expanded', 'true');
    });

    it('is a dialog with a name', async () => {
      const user = setup();
      await user.click(trigger());

      expect(screen.getByRole('dialog', { name: 'Choose a date range' })).toBeInTheDocument();
    });

    it('names every day with its whole date, and says which ones are the ends of the range', async () => {
      const user = setup({ from: new Date(2026, 10, 3), to: new Date(2026, 10, 8) });
      await user.click(trigger());

      expect(screen.getByRole('button', { name: '3 November 2026' })).toHaveAttribute('aria-pressed', 'true');
      expect(screen.getByRole('button', { name: '8 November 2026' })).toHaveAttribute('aria-pressed', 'true');
      expect(screen.getByRole('button', { name: '5 November 2026' })).toHaveAttribute('aria-pressed', 'false');
      expect(screen.getByRole('button', { name: '1 December 2026' })).toBeInTheDocument();
    });

    it('shows the range that a focused day would make, so the keyboard sees what the pointer sees', async () => {
      const user = setup({ from: new Date(2026, 10, 3), to: null });
      await user.click(trigger());

      await act(async () => day(12).focus());

      expect(day(8).className).toContain('bg-blue-50');
    });

    it('does not show the range that the pointer was about to make before the picker was left', async () => {
      const user = setup({ from: new Date(2026, 10, 3), to: null });
      await user.click(trigger());
      await user.hover(day(12));
      expect(day(8).className).toContain('bg-blue-50');
      await user.click(trigger());

      await user.click(trigger());

      expect(day(8).className).not.toContain('bg-blue-50');
    });
  });
});
