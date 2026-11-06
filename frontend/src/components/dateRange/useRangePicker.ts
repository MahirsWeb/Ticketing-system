import { useEffect, useRef, useState } from 'react';
import { isAfter, isBefore, startOfMonth } from 'date-fns';
import { atEndOfDay, atStartOfDay } from './rangeModel';

export type PickerView = 'days' | 'months' | 'years';

/**
 * What the picker does, apart from how it looks: a range is chosen by two days (a start, and then an end that is not before it), or by a
 * preset, and is handed over when it is complete. A picker that is left with only a start chosen takes today for the end; one that is
 * left with nothing chosen changes nothing; Escape leaves without taking anything.
 */
export function useRangePicker({ from, to, onChange }: { from: Date | null; to: Date | null; onChange: (from: Date | null, to: Date | null) => void }) {
  const [open, setOpen] = useState(false);
  const [view, setView] = useState<PickerView>('days');
  const [viewMonth, setViewMonth] = useState(() => startOfMonth(from ?? new Date()));
  // What is being chosen, by whole days. It starts from the range that is set each time the picker is opened.
  const [pendingStart, setPendingStart] = useState<Date | null>(null);
  const [pendingEnd, setPendingEnd] = useState<Date | null>(null);
  // A first day was chosen here, and its end was not: only then is there something to take when the picker is left.
  const [choosing, setChoosing] = useState(false);
  const [hoverDay, setHoverDay] = useState<Date | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  // Whether it is open, known at once: a click that leaves and the move of the focus that follows it are two events of one moment.
  const isOpen = useRef(false);

  function openPicker() {
    isOpen.current = true;
    setPendingStart(from ? atStartOfDay(from) : null);
    setPendingEnd(to ? atStartOfDay(to) : null);
    setViewMonth(startOfMonth(from ?? new Date()));
    setView('days');
    setChoosing(false);
    setHoverDay(null);
    setOpen(true);
  }

  function close() {
    isOpen.current = false;
    setOpen(false);
  }

  function leave() {
    if (!isOpen.current) return;
    if (choosing && pendingStart) onChange(atStartOfDay(pendingStart), atEndOfDay(new Date()));
    close();
  }

  function cancel() {
    if (!isOpen.current) return;
    close();
    triggerRef.current?.focus();
  }

  // The listeners are put up once for each time the picker is open, and call what the last render made, which knows what was chosen.
  const latest = useRef({ leave, cancel });
  useEffect(() => {
    latest.current = { leave, cancel };
  });
  useEffect(() => {
    if (!open) return;
    function outside(e: Event) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) latest.current.leave();
    }
    function escape(e: KeyboardEvent) {
      if (e.key === 'Escape') latest.current.cancel();
    }
    document.addEventListener('mousedown', outside);
    document.addEventListener('focusin', outside);
    document.addEventListener('keydown', escape);
    return () => {
      document.removeEventListener('mousedown', outside);
      document.removeEventListener('focusin', outside);
      document.removeEventListener('keydown', escape);
    };
  }, [open]);

  function pickDay(day: Date) {
    if (!pendingStart || pendingEnd || isBefore(day, pendingStart)) {
      setPendingStart(day);
      setPendingEnd(null);
      setChoosing(true);
      return;
    }
    onChange(atStartOfDay(pendingStart), atEndOfDay(day));
    close();
  }

  function applyPreset(range: [Date, Date]) {
    onChange(atStartOfDay(range[0]), atEndOfDay(range[1]));
    close();
  }

  function clear() {
    onChange(null, null);
    close();
  }

  return {
    open,
    view,
    viewMonth,
    rootRef,
    triggerRef,
    /** The range as it is drawn: the start, and the end, which while only a start is chosen is the day that the pointer or the focus is on. */
    rangeStart: pendingStart,
    rangeEnd: pendingEnd ?? (pendingStart && hoverDay && isAfter(hoverDay, pendingStart) ? hoverDay : null),
    setView,
    setViewMonth,
    setHoverDay,
    toggle: () => (open ? leave() : openPicker()),
    pickDay,
    applyPreset,
    clear,
  };
}
