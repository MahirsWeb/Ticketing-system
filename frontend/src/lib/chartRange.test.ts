import { describe, expect, it } from 'vitest';
import { zoomRange } from './chartRange';

// NOVO-FE-07
describe('zoomRange', () => {
  it('runs from the start of the first day to the end of the last, in the local day', () => {
    const range = zoomRange('2026-10-10', '2026-10-16')!;

    expect(range.from).toEqual(new Date(2026, 9, 10, 0, 0, 0, 0));
    expect(range.to).toEqual(new Date(2026, 9, 16, 23, 59, 59, 999));
  });

  it('takes whole days even when a label carries a time of day', () => {
    const range = zoomRange('2026-10-10T15:30:00', '2026-10-16T08:00:00')!;

    expect(range.from).toEqual(new Date(2026, 9, 10, 0, 0, 0, 0));
    expect(range.to).toEqual(new Date(2026, 9, 16, 23, 59, 59, 999));
  });

  it('does not mind which end was dragged first', () => {
    expect(zoomRange('2026-10-16', '2026-10-10')).toEqual(zoomRange('2026-10-10', '2026-10-16'));
  });

  it('is nothing for a click on a single day and for a label that is not a day', () => {
    expect(zoomRange('2026-10-10', '2026-10-10')).toBeNull();
    expect(zoomRange('2026-10-10', 'not a day')).toBeNull();
  });
});
