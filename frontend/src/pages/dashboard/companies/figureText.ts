/** A figure that does not exist (no closed ticket to take an average of, no due date to measure the SLA by) is a dash, never a zero. */
export function hoursText(hours: number, closed: number): string {
  return closed > 0 ? `${hours} h` : '—';
}

export function percentText(value: number | null): string {
  return value == null ? '—' : `${value}%`;
}
