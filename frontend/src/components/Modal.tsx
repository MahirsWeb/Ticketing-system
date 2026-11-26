import { useEffect, useId, useRef, type ReactNode } from 'react';
import { Card } from './ui';

/** The dialogs that are open, the last one on top. A dialog asked from inside another ("discard what you typed?") is the only one that hears Esc and Tab. */
const openDialogs: symbol[] = [];

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/// A modal dialog that behaves like one for keyboard and screen reader users: it is announced as a dialog with a name,
/// Esc closes it, focus moves into it when it opens, Tab stays inside it, and focus returns to what had it before.
export function Modal({
  title,
  onClose,
  children,
  maxWidth = 'max-w-sm',
  scroll = false,
  headerAction,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  maxWidth?: string;
  /** A dialog longer than the screen (a form) scrolls inside itself instead of running off it. */
  scroll?: boolean;
  /** Something at the end of the title row: a button that closes the dialog the way Esc does. */
  headerAction?: ReactNode;
}) {
  const titleId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const panel = panelRef.current;
    const me = Symbol('dialog');
    openDialogs.push(me);
    // First thing to work with: the first control, or the dialog itself when it has none.
    (panel?.querySelector<HTMLElement>(FOCUSABLE) ?? panel)?.focus();

    function onKeyDown(e: KeyboardEvent) {
      if (openDialogs[openDialogs.length - 1] !== me) return; // another dialog is on top of this one
      if (e.key === 'Escape') {
        e.stopPropagation();
        onCloseRef.current();
        return;
      }
      if (e.key !== 'Tab' || !panel) return;

      const focusable = Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE));
      if (focusable.length === 0) {
        e.preventDefault();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (e.shiftKey && (document.activeElement === first || document.activeElement === panel)) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }

    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      openDialogs.splice(openDialogs.indexOf(me), 1);
      previouslyFocused?.focus?.();
    };
  }, []);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-4">
      <Card className={`w-full ${maxWidth} p-6 ${scroll ? 'max-h-full overflow-y-auto' : ''}`}>
        <div ref={panelRef} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} className="outline-none">
          <div className="mb-2 flex items-start justify-between gap-3">
            <h2 id={titleId} className="text-lg font-bold text-slate-900">
              {title}
            </h2>
            {headerAction}
          </div>
          {children}
        </div>
      </Card>
    </div>
  );
}
