import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { notificationsApi, type NotificationDto } from '../api/notifications';
import { notificationKeys, useNotifications } from '../api/notificationQueries';
import { getNotificationsHub } from '../lib/notificationsHub';
import { formatDistanceToNow } from 'date-fns';

const EVENTS = ['newTicket', 'ticketOpened', 'ticketAssigned', 'ticketClosed', 'newResponse', 'userActivatedViaInvite', 'ticketTransferred', 'ticketOverdue', 'taskAssigned', 'ticketUnassigned'];

export function NotificationBell() {
  const queryClient = useQueryClient();
  const list = useNotifications();
  const items = list.data ?? [];
  const [open, setOpen] = useState(false);
  const boxRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const navigate = useNavigate();

  useEffect(() => {
    const hub = getNotificationsHub();
    // Asking again cancels a request that is still on its way, so the answer that is shown is the latest one asked for.
    const refresh = () => void queryClient.invalidateQueries({ queryKey: notificationKeys.list });
    EVENTS.forEach((e) => hub.on(e, refresh));
    // Whatever was pushed while the connection was down was missed: ask again once it is back. (A connection cannot be told to forget this; once the bell is gone
    // nothing is looking at the list, and asking for it again to be fetched is then a no-op.)
    hub.onreconnected(refresh);
    return () => {
      EVENTS.forEach((e) => hub.off(e, refresh));
    };
  }, [queryClient]);

  // The list closes when something outside it is pressed or gets the focus, and with Escape (which gives the focus back to the button): the keyboard is not a mouse.
  useEffect(() => {
    if (!open) return;
    function onClickOutside(e: MouseEvent) {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    }
    function onFocusOutside(e: FocusEvent) {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    }
    function onEscape(e: KeyboardEvent) {
      if (e.key !== 'Escape') return;
      setOpen(false);
      buttonRef.current?.focus();
    }
    document.addEventListener('mousedown', onClickOutside);
    document.addEventListener('focusin', onFocusOutside);
    document.addEventListener('keydown', onEscape);
    return () => {
      document.removeEventListener('mousedown', onClickOutside);
      document.removeEventListener('focusin', onFocusOutside);
      document.removeEventListener('keydown', onEscape);
    };
  }, [open]);

  const unreadCount = items.filter((i) => !i.isRead).length;
  const setRead = (id: string, isRead: boolean) =>
    queryClient.setQueryData<NotificationDto[]>(notificationKeys.list, (rows) => rows?.map((i) => (i.id === id ? { ...i, isRead } : i)));

  async function handleClick(n: NotificationDto) {
    // Opening what the notification is about does not wait for, or depend on, marking it read: if that fails (the network, a
    // server error) the click used to do nothing at all.
    setOpen(false);
    if (n.ticketId) navigate(`/tickets/${n.ticketId}`);
    if (n.isRead) return;
    setRead(n.id, true);
    try {
      await notificationsApi.markRead(n.id);
      // An answer that was asked for before this one was marked may still be on its way, and says "unread": ask again, which also cancels it.
      void queryClient.invalidateQueries({ queryKey: notificationKeys.list });
    } catch {
      // It was not marked after all: show it as unread again rather than pretend.
      setRead(n.id, false);
    }
  }

  return (
    <div className="relative" ref={boxRef}>
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-label={unreadCount > 0 ? `Notifications, ${unreadCount} unread` : 'Notifications'}
        aria-expanded={open}
        aria-haspopup="true"
        className="relative rounded-full p-2 text-slate-600 hover:bg-slate-100"
        title="Notifications"
      >
        <span aria-hidden>🔔</span>
        {unreadCount > 0 && (
          <span aria-hidden className="absolute -right-0.5 -top-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-red-600 text-[10px] font-bold text-white">
            {unreadCount > 9 ? '9+' : unreadCount}
          </span>
        )}
      </button>
      {open && (
        <div role="group" aria-label="Notifications" className="absolute right-0 z-50 mt-2 w-80 rounded-md border border-slate-200 bg-white shadow-lg">
          <div className="border-b border-slate-100 px-3 py-2 text-xs font-semibold text-slate-600">Notifications</div>
          <div className="max-h-96 overflow-y-auto">
            {list.isError && (
              <div className="border-b border-slate-100 px-3 py-3 text-sm text-red-700">
                <p>The notifications could not be loaded.</p>
                <button type="button" onClick={() => void list.refetch()} className="mt-1 text-xs font-medium text-blue-700 hover:underline">
                  Try again
                </button>
              </div>
            )}
            {list.isPending && <p className="px-3 py-4 text-sm text-slate-600">Loading notifications…</p>}
            {list.isSuccess && items.length === 0 && <p className="px-3 py-4 text-sm text-slate-600">No notifications yet.</p>}
            {items.map((n) => (
              <button
                key={n.id}
                onClick={() => handleClick(n)}
                className={`block w-full border-b border-slate-50 px-3 py-2 text-left text-sm hover:bg-slate-50 ${
                  n.isRead ? 'text-slate-600' : 'font-medium text-slate-800'
                }`}
              >
                <div>{n.message}</div>
                <div className="mt-0.5 text-xs text-slate-600">{formatDistanceToNow(new Date(n.createdAt), { addSuffix: true })}</div>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
