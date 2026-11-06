import type { TicketWorkSessionDto } from '../../../types';

const pad = (n: number) => String(n).padStart(2, '0');

export function toDatePart(date: Date | null) {
  if (!date) return '';
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function toTimePart(date: Date | null) {
  if (!date) return '';
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** A date and a time, both in the local time of the person at the keyboard, as one moment; null when either is missing or is not one. */
export function combine(datePart: string, timePart: string): Date | null {
  if (!datePart || !timePart) return null;
  const d = new Date(`${datePart}T${timePart}:00`);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function formatDuration(milliseconds: number) {
  const minutes = Math.max(0, Math.round(milliseconds / 60000));
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}h ${m}min` : `${m}min`;
}

/** How long a session lasted; a session that is still running lasts until `now`. */
export function durationOf(session: TicketWorkSessionDto, now: number) {
  const end = session.endedAtUtc ? new Date(session.endedAtUtc).getTime() : now;
  return end - new Date(session.startedAtUtc).getTime();
}

export const TIME_MESSAGE = 'Enter the time as hours and minutes, for example 09:30.';

/**
 * What a person typed as a time of day, as "HH:MM": hours only ("9"), hours and minutes with a colon ("9:30") or without ("930", "0930").
 * An empty field is "" (nothing); anything else, and a time that does not exist ("25:00", "12:75"), is null. Never a different time from the one typed.
 */
export function parseTime(raw: string): string | null {
  const text = raw.trim();
  if (text === '') return '';
  let match = text.match(/^(\d{1,2}):(\d{2})$/);
  let hours: number;
  let minutes: number;
  if (match) {
    [hours, minutes] = [Number(match[1]), Number(match[2])];
  } else if ((match = text.match(/^(\d{1,2}):?$/))) {
    [hours, minutes] = [Number(match[1]), 0];
  } else if ((match = text.match(/^(\d{1,2})(\d{2})$/))) {
    [hours, minutes] = [Number(match[1]), Number(match[2])];
  } else {
    return null;
  }
  return hours > 23 || minutes > 59 ? null : `${pad(hours)}:${pad(minutes)}`;
}
