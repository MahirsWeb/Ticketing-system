import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DateRangePicker } from './DateRangePicker';

describe('the date range picker', () => {
  it('names every arrow, so none of them is announced as just a symbol (TS-46.5)', async () => {
    render(<DateRangePicker from={null} to={null} onChange={() => {}} />);
    await userEvent.click(screen.getByRole('button', { name: /All time/ }));

    expect(screen.getByRole('button', { name: 'Previous month' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next month' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^[A-Z][a-z]+ \d{4}$/ })); // the month heading opens the months
    expect(screen.getByRole('button', { name: 'Previous year' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next year' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^\d{4}$/ })); // the year heading opens the years
    expect(screen.getByRole('button', { name: 'Previous years' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Next years' })).toBeInTheDocument();
  });

  it('shows days of the neighbouring months in a colour that can be read', async () => {
    render(<DateRangePicker from={null} to={null} onChange={() => {}} />);
    await userEvent.click(screen.getByRole('button', { name: /All time/ }));

    const outside = document.querySelectorAll('button.text-slate-500');
    expect(document.querySelector('.text-slate-300')).toBeNull();
    expect(outside.length).toBeGreaterThan(0);
  });
});
