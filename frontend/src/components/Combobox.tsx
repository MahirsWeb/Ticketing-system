import clsx from 'clsx';
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Input } from './ui';

export interface ComboboxOption {
  id: string;
  label: string;
  /** A second line under the label (an e-mail under a name: two people can have the same name). It is searched as well. */
  secondary?: string;
}

/** The most options drawn at once. A longer list is narrowed by typing; drawing thousands of rows is what makes a list slow. */
const MOST_SHOWN = 200;

/** Lower case, without accents, with "đ" as "d": so "cesto" finds "Često" and "dordje" finds "Đorđe". */
function plain(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'd')
    .toLowerCase()
    .trim();
}

/**
 * A search box with a list under it, in which only an item that exists can be chosen. What is typed narrows the list; it never becomes the value by
 * itself: a text that is not (exactly) one of the items is dropped when the person leaves the box, so no made-up name can get through. Emptying the box
 * clears the choice. Put it in a `Field` with a `Label` (it takes the label's id), or give it an `aria-label`.
 *
 * All words it shows come from the caller, so that they are translated: `noResultsText` for an empty list.
 */
export function Combobox({
  options,
  value,
  onChange,
  disabled = false,
  busy = false,
  placeholder,
  noResultsText,
  className,
  'aria-label': ariaLabel,
}: {
  options: ComboboxOption[];
  /** The id of the chosen item, or null. */
  value: string | null;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  /** The items are still being fetched. */
  busy?: boolean;
  placeholder?: string;
  noResultsText: string;
  className?: string;
  'aria-label'?: string;
}) {
  const listId = useId();
  const selected = useMemo(() => options.find((o) => o.id === value) ?? null, [options, value]);
  const [text, setText] = useState(selected?.label ?? '');
  const [query, setQuery] = useState(''); // what was typed: the list is narrowed by it, the box shows `text`
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const ownChange = useRef(false);
  // Where the list is drawn: it is drawn on the page, over everything (a dialog that scrolls would cut it off), next to the box.
  const [place, setPlace] = useState<{ left: number; width: number; top?: number; bottom?: number; maxHeight: number } | null>(null);

  // The choice changed from outside (a form that was reset, a company that changed): the box follows it. A change this component made itself is not
  // followed, or the text that is being typed would be wiped the moment the choice was cleared.
  useEffect(() => {
    if (ownChange.current) {
      ownChange.current = false;
      return;
    }
    setText(selected?.label ?? '');
    setQuery('');
  }, [selected?.id, selected?.label]);

  useLayoutEffect(() => {
    if (!open) return;
    function measure() {
      const box = wrapperRef.current?.getBoundingClientRect();
      if (!box) return;
      const below = window.innerHeight - box.bottom - 12;
      const above = box.top - 12;
      // Under the box, unless there is little room there and more above it.
      if (below < 160 && above > below) setPlace({ left: box.left, width: box.width, bottom: window.innerHeight - box.top + 4, maxHeight: Math.min(240, above) });
      else setPlace({ left: box.left, width: box.width, top: box.bottom + 4, maxHeight: Math.max(120, Math.min(240, below)) });
    }
    measure();
    window.addEventListener('resize', measure);
    window.addEventListener('scroll', measure, true); // a scroll of anything the box is in
    return () => {
      window.removeEventListener('resize', measure);
      window.removeEventListener('scroll', measure, true);
    };
  }, [open]);

  const shown = useMemo(() => {
    const needle = plain(query);
    const found = needle ? options.filter((o) => plain(`${o.label} ${o.secondary ?? ''}`).includes(needle)) : options;
    return found.slice(0, MOST_SHOWN);
  }, [options, query]);

  function choose(option: ComboboxOption) {
    setText(option.label);
    setQuery('');
    setOpen(false);
    if (option.id !== value) {
      ownChange.current = false;
      onChange(option.id);
    }
  }

  function clear() {
    if (value !== null) {
      ownChange.current = true;
      onChange(null);
    }
  }

  function settle() {
    // Leaving the box: the text that is exactly one item is that item; anything else is not a choice, and the box goes back to what is chosen.
    const exact = query ? options.filter((o) => plain(o.label) === plain(query)) : [];
    if (exact.length === 1) {
      choose(exact[0]);
      return;
    }
    setOpen(false);
    setQuery('');
    setText(selected?.label ?? '');
  }

  function onInput(next: string) {
    setText(next);
    setQuery(next);
    setOpen(true);
    setActive(0);
    if (next.trim() === '') clear(); // an empty box is no choice
    else if (value !== null) clear(); // a text that is being changed is no longer the item that was chosen
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setOpen(true);
      setActive((i) => Math.min(i + 1, Math.max(shown.length - 1, 0)));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setOpen(true);
      setActive((i) => Math.max(i - 1, 0));
    } else if (e.key === 'Enter') {
      if (open) {
        e.preventDefault(); // a form around it is not sent by the Enter that picks an item
        const option = shown[active];
        if (option) choose(option);
      }
    } else if (e.key === 'Escape') {
      if (open) {
        // Closes the list only; a dialog around it must not close with it.
        e.stopPropagation();
        setOpen(false);
        setQuery('');
        setText(selected?.label ?? '');
      }
    }
  }

  const activeId = open && shown[active] ? `${listId}-${active}` : undefined;

  return (
    <div
      ref={wrapperRef}
      className="relative"
      onBlur={(e) => {
        if (!wrapperRef.current?.contains(e.relatedTarget as Node | null)) settle();
      }}
    >
      <Input
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={activeId}
        aria-label={ariaLabel}
        aria-busy={busy || undefined}
        autoComplete="off"
        disabled={disabled}
        placeholder={placeholder}
        className={className}
        value={text}
        onChange={(e) => onInput(e.target.value)}
        onFocus={() => setOpen(true)}
        onClick={() => setOpen(true)}
        onKeyDown={onKeyDown}
      />
      {open && !disabled && place &&
        createPortal(
        <div
          id={listId}
          role="listbox"
          tabIndex={-1}
          // the box keeps the focus when the list is pressed (its scroll bar included): leaving the box would settle the text before an item is chosen
          onMouseDown={(e) => e.preventDefault()}
          style={{ position: 'fixed', left: place.left, width: place.width, top: place.top, bottom: place.bottom, maxHeight: place.maxHeight }}
          className="z-[60] overflow-auto rounded-md border border-slate-300 bg-white py-1 shadow-lg"
        >
          {shown.length === 0 ? (
            <div role="presentation" className="px-3 py-2 text-sm text-slate-600">
              {noResultsText}
            </div>
          ) : (
            shown.map((o, i) => (
              <div
                key={o.id}
                id={`${listId}-${i}`}
                role="option"
                tabIndex={-1}
                aria-selected={o.id === value}
                // mousedown, not click: the box must keep the focus, or leaving it would settle the text before the item is chosen
                onMouseDown={(e) => {
                  e.preventDefault();
                  choose(o);
                }}
                onMouseMove={() => setActive(i)}
                className={clsx('cursor-pointer px-3 py-2 text-sm', i === active ? 'bg-blue-50 text-slate-900' : 'text-slate-800')}
              >
                <div className={clsx(o.id === value && 'font-semibold')}>{o.label}</div>
                {o.secondary && <div className="text-xs text-slate-600">{o.secondary}</div>}
              </div>
            ))
          )}
        </div>,
        document.body
      )}
    </div>
  );
}
