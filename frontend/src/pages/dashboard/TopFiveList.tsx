import { useState } from 'react';
import { useLanguage } from '../../i18n/LanguageContext';

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
  const { t } = useLanguage();
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
          {showAll ? t('dash_showLess') : t('dash_showMore', { count: items.length - 5 })}
        </button>
      )}
    </>
  );
}
