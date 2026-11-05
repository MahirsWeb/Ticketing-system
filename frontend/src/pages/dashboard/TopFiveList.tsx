import { useState } from 'react';

/** The first five of a list, and the rest on request. */
export function TopFiveList<T>({
  items,
  keyOf,
  render,
}: {
  items: T[];
  keyOf: (item: T, index: number) => string;
  render: (item: T, index: number) => React.ReactNode;
}) {
  const [showAll, setShowAll] = useState(false);
  const visible = showAll ? items : items.slice(0, 5);
  return (
    <>
      <ol className="space-y-2">
        {visible.map((item, i) => (
          <li key={keyOf(item, i)}>{render(item, i)}</li>
        ))}
      </ol>
      {items.length > 5 && (
        <button
          className="mt-3 text-xs font-medium text-blue-700 hover:underline"
          onClick={() => setShowAll((s) => !s)}
        >
          {showAll ? 'Show less' : `Show more (${items.length - 5} more)`}
        </button>
      )}
    </>
  );
}
