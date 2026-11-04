import { useEffect, useState } from 'react';

/// The value as it was `delayMs` ago, once it has stopped changing: typing "Izv" gives one change, not three. Used so a search box
/// asks the server once for what the person finished typing instead of once per keystroke.
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return settled;
}
