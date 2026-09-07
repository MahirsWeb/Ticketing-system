import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { notificationsApi, type NotificationDto } from '../api/notifications';
import { getNotificationsHub } from '../lib/notificationsHub';
import { formatDistanceToNow } from 'date-fns';

export function NotificationBell() {
  const [items, setItems] = useState<NotificationDto[]>([]);
  const [open, setOpen] = useState(false);
  const boxRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  useEffect(() => {
    notificationsApi.list().then(setItems).catch(() => {});

    const hub = getNotificationsHub();
    const refresh = () => notificationsApi.list().then(setItems).catch(() => {});
    const events = ['newTicket', 'ticketOpened', 'ticketAssigned', 'ticketClosed', 'newResponse', 'userActivatedViaInvite', 'ticketTransferred', 'ticketOverdue', 'taskAssigned'];
    events.forEach((e) => hub.on(e, refresh));
    return () => {
      events.forEach((e) => hub.off(e, refresh));
    };
  }, []);

  useEffect(() => {
    function onClickOutside(e: MouseEvent) {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', onClickOutside);
    return () => document.removeEventListener('mousedown', onClickOutside);
  }, []);

  const unreadCount = items.filter((i) => !i.isRead).length;

  async function handleClick(n: NotificationDto) {
    await notificationsApi.markRead(n.id);
    setItems((prev) => prev.map((i) => (i.id === n.id ? { ...i, isRead: true } : i)));
    setOpen(false);
    if (n.ticketId) navigate(`/tickets/${n.ticketId}`);
  }

  return (
    <div className="relative" ref={boxRef}>
      <button
        onClick={() => setOpen((o) => !o)}
        className="relative rounded-full p-2 text-slate-600 hover:bg-slate-100"
        title="Notifications"
      >
        🔔
        {unreadCount > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 w-4 items-center justify-center rounded-full bg-red-600 text-[10px] font-bold text-white">
            {unreadCount > 9 ? '9+' : unreadCount}
          </span>
        )}
      </button>
      {open && (
        <div className="absolute right-0 z-50 mt-2 w-80 rounded-md border border-slate-200 bg-white shadow-lg">
          <div className="border-b border-slate-100 px-3 py-2 text-xs font-semibold text-slate-500">Notifications</div>
          <div className="max-h-96 overflow-y-auto">
            {items.length === 0 && <p className="px-3 py-4 text-sm text-slate-400">No notifications yet.</p>}
            {items.map((n) => (
              <button
                key={n.id}
                onClick={() => handleClick(n)}
                className={`block w-full border-b border-slate-50 px-3 py-2 text-left text-sm hover:bg-slate-50 ${
                  n.isRead ? 'text-slate-500' : 'font-medium text-slate-800'
                }`}
              >
                <div>{n.message}</div>
                <div className="mt-0.5 text-xs text-slate-400">{formatDistanceToNow(new Date(n.createdAt), { addSuffix: true })}</div>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
