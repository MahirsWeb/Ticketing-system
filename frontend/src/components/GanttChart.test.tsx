import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { GanttChart } from './GanttChart';
import type { GanttEntryDto } from '../types';

const day = new Date(2026, 9, 14, 12, 0, 0);
const at = (hour: number, minute = 0) => new Date(2026, 9, 14, hour, minute).toISOString();

const entries: GanttEntryDto[] = [
  { type: 'Ticket', id: 't1', title: '#100001 — Printer', subLabel: null, startedAtUtc: at(9), endedAtUtc: at(10, 30), status: 'InProgress' },
  { type: 'WorkTask', id: 'k1', title: 'Backup check', subLabel: null, startedAtUtc: at(11), endedAtUtc: at(12), status: 'Done' },
];

describe('GanttChart', () => {
  it('describes every block in words, so colour is not the only information', () => {
    render(<GanttChart entries={entries} date={day} />);

    expect(screen.getByRole('button', { name: '#100001 — Printer, InProgress, 09:00 to 10:30' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Backup check, Done, 11:00 to 12:00' })).toBeInTheDocument();
  });

  it('puts the ticket work and the task work in separately named groups', () => {
    render(<GanttChart entries={entries} date={day} />);

    expect(screen.getByRole('group', { name: 'Ticket work' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Task work' })).toBeInTheDocument();
  });

  it('explains the colours with text', () => {
    render(<GanttChart entries={entries} date={day} />);

    const legend = screen.getByLabelText('Colour legend');
    expect(legend).toHaveTextContent('Ticket: InProgress');
    expect(legend).toHaveTextContent('Task: Done');
  });

  it('says so when a block has no end yet', () => {
    const running: GanttEntryDto[] = [{ ...entries[0], endedAtUtc: null }];
    render(<GanttChart entries={running} date={day} />);

    expect(screen.getByRole('button', { name: /09:00 to ongoing/ })).toBeInTheDocument();
  });

  it('says so when there is nothing to show', () => {
    render(<GanttChart entries={[]} date={day} />);

    expect(screen.getByText('No ticket work logged this day')).toBeInTheDocument();
  });
});
