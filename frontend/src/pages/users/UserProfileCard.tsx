import { useState, type ReactNode } from 'react';
import { format } from 'date-fns';
import clsx from 'clsx';
import { usersApi } from '../../api/users';
import { Card, StatusPill } from '../../components/ui';
import { UserAvatar } from '../../components/UserAvatar';
import type { UserListItemDto } from '../../types';
import { UserAdminActions } from './UserAdminActions';
import { UserEditForm } from './UserEditForm';

/** Who the person is: the picture, the facts about the account and, for an administrator, the form for changing it and the actions on it. */
export function UserProfileCard({
  user,
  viewerId,
  viewerIsAdmin,
  onChanged,
}: {
  user: UserListItemDto;
  viewerId: string | undefined;
  viewerIsAdmin: boolean;
  onChanged: () => void;
}) {
  const [editing, setEditing] = useState(false);
  // A new form is drawn each time it is opened, so it starts from what the account is and not from what was typed the last time.
  const [editSession, setEditSession] = useState(0);
  const [avatarUploading, setAvatarUploading] = useState(false);
  const [avatarError, setAvatarError] = useState<string | null>(null);

  const canEditAvatar = viewerIsAdmin || viewerId === user.id;
  const orgLabel = user.role === 'Client' ? 'Organization' : 'Branch';
  const orgValue = user.role === 'Client' ? user.companyName ?? '—' : [user.departmentName, user.subBranchName].filter(Boolean).join(' / ') || '—';

  const profileFields: { label: string; value: ReactNode }[] = [
    { label: 'Email', value: user.email },
    ...(user.role === 'Client' && user.phoneNumber ? [{ label: 'Phone', value: user.phoneNumber }] : []),
    { label: orgLabel, value: orgValue },
    { label: 'Status', value: <StatusPill active={user.isActive} /> },
    { label: 'Email verified', value: user.emailConfirmed ? 'Yes' : 'No' },
    { label: 'Created', value: format(new Date(user.createdAtUtc), 'dd.MM.yyyy') },
    { label: 'Last login', value: user.lastLoginAtUtc ? format(new Date(user.lastLoginAtUtc), 'dd.MM.yyyy HH:mm') : 'Never' },
  ];
  const profileColumns: (typeof profileFields)[] = [];
  for (let i = 0; i < profileFields.length; i += 3) profileColumns.push(profileFields.slice(i, i + 3));

  async function handleAvatarUpload(file: File) {
    setAvatarError(null);
    setAvatarUploading(true);
    try {
      await usersApi.uploadProfilePicture(user.id, file);
      onChanged();
    } catch (err: any) {
      setAvatarError(err?.response?.data?.message ?? 'Could not upload the picture.');
    } finally {
      setAvatarUploading(false);
    }
  }

  return (
    <Card className="p-5">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-wrap items-center gap-5">
          <div>
            <UserAvatar url={user.profilePictureUrl} editable={canEditAvatar} uploading={avatarUploading} onUpload={handleAvatarUpload} />
            {avatarError && <p className="mt-1 max-w-[6rem] text-[11px] text-red-600">{avatarError}</p>}
          </div>
          <div className="grid grid-cols-1 gap-x-10 gap-y-3 text-sm sm:grid-cols-2 lg:grid-cols-3">
            {profileColumns.map((col, i) => (
              <div key={i} className="space-y-3">
                {col.map((f) => (
                  <div key={f.label}>
                    <div className="text-xs font-semibold uppercase text-slate-600">{f.label}</div>
                    <div className="text-slate-800">{f.value}</div>
                  </div>
                ))}
              </div>
            ))}
          </div>
        </div>
        {viewerIsAdmin && (
          <button
            type="button"
            onClick={() => {
              if (!editing) setEditSession((n) => n + 1);
              setEditing(!editing);
            }}
            className="flex shrink-0 items-center gap-1.5 rounded-md border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 hover:bg-slate-50"
          >
            ✏️ {editing ? 'Close' : 'Edit account details'}
          </button>
        )}
      </div>

      {/* Folded away while closed: out of reach of the keyboard and of a screen reader, not only invisible. */}
      <div
        className={clsx('grid transition-all duration-200 ease-out', editing ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0')}
        inert={!editing}
        aria-hidden={editing ? undefined : true}
      >
        <div className="overflow-hidden">
          <UserEditForm
            key={editSession}
            user={user}
            onCancel={() => setEditing(false)}
            onSaved={() => {
              setEditing(false);
              onChanged();
            }}
          />
        </div>
      </div>

      {viewerIsAdmin && <UserAdminActions user={user} isSelf={viewerId === user.id} onChanged={onChanged} />}
    </Card>
  );
}
