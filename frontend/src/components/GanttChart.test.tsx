import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { GanttChart } from './GanttChart';
import type { GanttEntryDto } from '../types';

const day = new Date(2026, 9, 14, 12, 0, 0);
const at = (hour: number, minute = 0) => new Date(2026, 9, 14, hour, minute).toISOString();

const entries: GanttEntryDto[] = [
  { type: 'Ticket', id: 't1', title: '#100001 — Printer', subLabel: null, startedAtUtc: at(9), endedAtUtc: at(10, 30), status: 'InProgress', isLogged: true },
  { type: 'WorkTask', id: 'k1', title: 'Backup check', subLabel: null, startedAtUtc: at(11), endedAtUtc: at(12), status: 'Done', isLogged: true },
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

  it('tells the open time of a ticket nobody logged work on from real work, in words and in the legend', () => {
    const elapsed: GanttEntryDto[] = [{ ...entries[0], id: 't2', title: '#100002 — Unlogged', isLogged: false }];
    render(<GanttChart entries={[...entries, ...elapsed]} date={day} />);

    expect(screen.getByRole('button', { name: '#100002 — Unlogged, InProgress, 09:00 to 10:30, open time, no work logged' })).toBeInTheDocument();
    // the logged ones carry no such remark
    expect(screen.getByRole('button', { name: '#100001 — Printer, InProgress, 09:00 to 10:30' })).toBeInTheDocument();
    expect(screen.getByLabelText('Colour legend')).toHaveTextContent('Open time, no work logged');
  });

  it('draws elapsed time differently from logged work', () => {
    const elapsed: GanttEntryDto[] = [{ ...entries[0], id: 't2', title: '#100002 — Unlogged', isLogged: false }];
    render(<GanttChart entries={[entries[0], ...elapsed]} date={day} />);

    const logged = screen.getByRole('button', { name: /#100001/ });
    const stand = screen.getByRole('button', { name: /#100002/ });
    expect(logged.style.backgroundImage).not.toContain('repeating-linear-gradient');
    expect(stand.style.backgroundImage).toContain('repeating-linear-gradient');
  });

  it('says so when there is nothing to show', () => {
    render(<GanttChart entries={[]} date={day} />);

    expect(screen.getByText('No ticket work logged this day')).toBeInTheDocument();
  });
});
