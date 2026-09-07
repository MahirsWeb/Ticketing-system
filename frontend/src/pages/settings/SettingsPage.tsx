import { useEffect, useMemo, useState } from 'react';
import clsx from 'clsx';
import { lookupsApi } from '../../api/lookups';
import { settingsApi } from '../../api/settings';
import { knowledgeBaseApi } from '../../api/knowledgeBase';
import type { DepartmentItemFull, KnowledgeBaseDocumentDto, LookupItemFull, SlaPlanItemFull, SubBranchItemFull } from '../../types';
import { Button, Card, ErrorText, Input, Label, StatusPill, SuccessText } from '../../components/ui';
import { ConfirmDialog } from '../../components/ConfirmDialog';

const NAME_MAX = 25;
const EMAIL_MAX = 50;
const HELP_TOPIC_MAX = 75;
const CATEGORY_MAX = 70;
const SLA_NAME_MAX = 70;

type TabKey = 'branches' | 'helpTopics' | 'categoriesAndSla' | 'notifications';

const TABS: { key: TabKey; label: string; icon: string }[] = [
  { key: 'branches', label: 'Branches', icon: '🏢' },
  { key: 'helpTopics', label: 'Help Topics', icon: '🏷️' },
  { key: 'categoriesAndSla', label: 'Categories & SLA', icon: '🗂️' },
  { key: 'notifications', label: 'Notifications', icon: '⏰' },
];

function Panel({ children, className }: { children: React.ReactNode; className?: string }) {
  return <Card className={clsx('p-5', className)}>{children}</Card>;
}

function ListRow({ children }: { children: React.ReactNode }) {
  return <div className="-mx-2 flex items-center justify-between gap-3 rounded-lg px-2 py-2.5 transition hover:bg-slate-50">{children}</div>;
}

function SubBranchesInline({ departmentId }: { departmentId: string }) {
  const [items, setItems] = useState<SubBranchItemFull[]>([]);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [confirmDeactivate, setConfirmDeactivate] = useState<SubBranchItemFull | null>(null);

  function refresh() {
    lookupsApi.subBranchesAdmin(departmentId).then((items) => {
      setItems(items);
      setLoading(false);
    });
  }

  useEffect(refresh, [departmentId]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return;
    try {
      await lookupsApi.createSubBranch(departmentId, name.trim());
      setName('');
      refresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not add sub-branch.');
    }
  }

  async function handleToggleActive(item: SubBranchItemFull) {
    if (item.isActive) {
      setConfirmDeactivate(item);
      return;
    }
    await lookupsApi.updateSubBranch(item.id, item.name, true);
    refresh();
  }

  async function handleConfirmDeactivate() {
    if (!confirmDeactivate) return;
    await lookupsApi.updateSubBranch(confirmDeactivate.id, confirmDeactivate.name, false);
    setConfirmDeactivate(null);
    refresh();
  }

  if (loading) return <p className="py-2 text-xs text-slate-400">Loading sub-branches…</p>;

  return (
    <div className="mt-1 mb-2 rounded-lg border border-slate-100 bg-slate-50/60 p-3">
      <p className="mb-2 text-xs text-slate-500">
        Optional finer units within this branch, sharing its email but tracked separately for statistics and
        ticket assignment. If any exist, staff and tickets in this branch must pick one.
      </p>
      <form onSubmit={handleCreate} className="mb-2 flex gap-2">
        <Input
          placeholder="Sub-branch name…"
          value={name}
          maxLength={NAME_MAX}
          onChange={(e) => {
            setName(e.target.value);
            setError(null);
          }}
          className="max-w-xs text-xs"
        />
        <Button type="submit" variant="secondary" className="text-xs" disabled={!name.trim()}>
          Add
        </Button>
      </form>
      <ErrorText>{error}</ErrorText>
      <div className="divide-y divide-slate-100">
        {items.map((item) => (
          <ListRow key={item.id}>
            <span className="text-sm text-slate-700">{item.name}</span>
            <div className="flex items-center gap-2">
              <StatusPill active={item.isActive} />
              <button className="text-xs text-slate-500 hover:underline" onClick={() => handleToggleActive(item)}>
                {item.isActive ? 'Deactivate' : 'Reactivate'}
              </button>
            </div>
          </ListRow>
        ))}
        {items.length === 0 && <p className="py-2 text-xs text-slate-400">No sub-branches — this branch is used as a single unit.</p>}
      </div>

      {confirmDeactivate && (
        <ConfirmDialog
          title="Deactivate this sub-branch?"
          message={`Tickets and staff already assigned to "${confirmDeactivate.name}" keep that assignment and are completely unaffected — this does not change any existing tickets or statistics. It just removes "${confirmDeactivate.name}" from the picker when opening a new ticket or assigning staff. If this is the branch's only active sub-branch, the branch will behave as if it has none until you reactivate it.`}
          confirmLabel="Deactivate"
          danger
          onConfirm={handleConfirmDeactivate}
          onCancel={() => setConfirmDeactivate(null)}
        />
      )}
    </div>
  );
}

function BranchesTab({ items, onRefresh }: { items: DepartmentItemFull[]; onRefresh: () => void }) {
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
  const [editingEmail, setEditingEmail] = useState('');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<DepartmentItemFull | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<DepartmentItemFull | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => i.name.toLowerCase().includes(q) || i.email.toLowerCase().includes(q));
  }, [items, search]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim() || !email.trim()) {
      setError('Both branch name and email are required.');
      return;
    }
    try {
      await lookupsApi.createDepartment(name.trim(), email.trim());
      setName('');
      setEmail('');
      onRefresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not add branch.');
    }
  }

  async function handleToggleActive(item: DepartmentItemFull) {
    if (item.isActive) {
      setConfirmDeactivate(item);
      return;
    }
    await lookupsApi.updateDepartment(item.id, item.name, item.email, true);
    onRefresh();
  }

  async function handleConfirmDeactivate() {
    if (!confirmDeactivate) return;
    await lookupsApi.updateDepartment(confirmDeactivate.id, confirmDeactivate.name, confirmDeactivate.email, false);
    setConfirmDeactivate(null);
    onRefresh();
  }

  async function handleSaveEdit(item: DepartmentItemFull) {
    if (!editingName.trim() || !editingEmail.trim()) return;
    try {
      await lookupsApi.updateDepartment(item.id, editingName.trim(), editingEmail.trim(), item.isActive);
      setEditingId(null);
      onRefresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not update branch.');
    }
  }

  async function handleConfirmDelete() {
    if (!confirmDelete) return;
    setDeleteError(null);
    try {
      await lookupsApi.deleteDepartment(confirmDelete.id);
      setConfirmDelete(null);
      onRefresh();
    } catch (err: any) {
      setDeleteError(err?.response?.data?.message ?? 'Could not delete this branch.');
    }
  }

  const canAdd = !!name.trim() && !!email.trim();

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[300px_1fr]">
      <Panel className="h-fit">
        <h2 className="text-sm font-bold text-slate-800">Add new branch</h2>
        <p className="mt-0.5 mb-4 text-xs text-slate-400">Every branch needs a dedicated email address.</p>
        <form onSubmit={handleCreate} className="space-y-3">
          <div>
            <Label>Branch name</Label>
            <Input
              placeholder="e.g. Servis"
              value={name}
              maxLength={NAME_MAX}
              onChange={(e) => {
                setName(e.target.value);
                setError(null);
              }}
            />
          </div>
          <div>
            <Label>Branch email</Label>
            <Input
              type="email"
              placeholder="servis@firma.ba"
              value={email}
              maxLength={EMAIL_MAX}
              onChange={(e) => {
                setEmail(e.target.value);
                setError(null);
              }}
            />
          </div>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" className="w-full" disabled={!canAdd}>
            + Add Branch
          </Button>
        </form>
      </Panel>

      <Panel>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-slate-800">Existing branches ({items.length})</h2>
          <Input placeholder="Search branches…" value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        </div>
        <div className="divide-y divide-slate-100">
          {filtered.map((item) => (
            <div key={item.id}>
              <ListRow>
                {editingId === item.id ? (
                  <div className="flex flex-1 gap-2">
                    <Input value={editingName} maxLength={NAME_MAX} onChange={(e) => setEditingName(e.target.value)} className="max-w-xs" />
                    <Input type="email" value={editingEmail} maxLength={EMAIL_MAX} onChange={(e) => setEditingEmail(e.target.value)} className="max-w-xs" />
                  </div>
                ) : (
                  <span className="text-sm text-slate-800">
                    {item.name} <span className="text-xs text-slate-400">— {item.email}</span>
                  </span>
                )}
                <div className="flex shrink-0 items-center gap-3 text-xs">
                  {editingId === item.id ? (
                    <>
                      <button className="font-medium text-blue-700 hover:underline" onClick={() => handleSaveEdit(item)}>
                        Save
                      </button>
                      <button className="text-slate-500 hover:underline" onClick={() => setEditingId(null)}>
                        Cancel
                      </button>
                    </>
                  ) : (
                    <>
                      <StatusPill active={item.isActive} />
                      <button className="text-slate-500 hover:underline" onClick={() => setExpandedId(expandedId === item.id ? null : item.id)}>
                        {expandedId === item.id ? 'Hide sub-branches' : 'Sub-branches'}
                      </button>
                      <button
                        className="font-medium text-blue-700 hover:underline"
                        onClick={() => {
                          setEditingId(item.id);
                          setEditingName(item.name);
                          setEditingEmail(item.email);
                        }}
                      >
                        Edit
                      </button>
                      <button className="text-slate-500 hover:underline" onClick={() => handleToggleActive(item)}>
                        {item.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                      <button className="font-medium text-red-600 hover:underline" onClick={() => setConfirmDelete(item)}>
                        Delete
                      </button>
                    </>
                  )}
                </div>
              </ListRow>
              {expandedId === item.id && <SubBranchesInline departmentId={item.id} />}
            </div>
          ))}
          {filtered.length === 0 && <p className="py-8 text-center text-sm text-slate-400">No branches found.</p>}
        </div>
      </Panel>

      {confirmDelete && (
        <ConfirmDialog
          title="Delete this branch?"
          message={`"${confirmDelete.name}" and ALL of its sub-branches will be permanently deleted. Tickets and staff currently assigned to it become unassigned from a branch (not deleted themselves), and those tickets lose their branch name in past reports/statistics going forward since the branch record itself is gone. This cannot be undone.${deleteError ? `\n\n${deleteError}` : ''}`}
          confirmLabel="Delete"
          danger
          onConfirm={handleConfirmDelete}
          onCancel={() => {
            setConfirmDelete(null);
            setDeleteError(null);
          }}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title="Deactivate this branch?"
          message={`Existing tickets, staff and statistics for "${confirmDeactivate.name}" are completely unaffected — nothing is changed or deleted. This only removes it from the picker when opening a new ticket or assigning new staff. Staff already in this branch can still work and open tickets in it as normal; its sub-branches are not affected either.`}
          confirmLabel="Deactivate"
          danger
          onConfirm={handleConfirmDeactivate}
          onCancel={() => setConfirmDeactivate(null)}
        />
      )}
    </div>
  );
}

function SimpleLookupTab({
  itemNoun,
  pluralNoun,
  maxLength,
  items,
  onRefresh,
  onCreate,
  onUpdate,
  onDelete,
  deleteWarning,
  deactivateWarning,
}: {
  itemNoun: string;
  pluralNoun?: string;
  maxLength: number;
  items: LookupItemFull[];
  onRefresh: () => void;
  onCreate: (name: string) => Promise<unknown>;
  onUpdate: (id: string, name: string, isActive: boolean) => Promise<unknown>;
  onDelete: (id: string) => Promise<unknown>;
  deleteWarning: string;
  deactivateWarning: string;
}) {
  const plural = pluralNoun ?? `${itemNoun}s`;
  const [name, setName] = useState('');
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState('');
  const [confirmDelete, setConfirmDelete] = useState<LookupItemFull | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<LookupItemFull | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => i.name.toLowerCase().includes(q));
  }, [items, search]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return;
    try {
      await onCreate(name.trim());
      setName('');
      onRefresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not add item.');
    }
  }

  async function handleToggleActive(item: LookupItemFull) {
    if (item.isActive) {
      setConfirmDeactivate(item);
      return;
    }
    await onUpdate(item.id, item.name, true);
    onRefresh();
  }

  async function handleConfirmDeactivate() {
    if (!confirmDeactivate) return;
    await onUpdate(confirmDeactivate.id, confirmDeactivate.name, false);
    setConfirmDeactivate(null);
    onRefresh();
  }

  async function handleSaveEdit(item: LookupItemFull) {
    if (!editingName.trim()) return;
    await onUpdate(item.id, editingName.trim(), item.isActive);
    setEditingId(null);
    onRefresh();
  }

  async function handleConfirmDelete() {
    if (!confirmDelete) return;
    setDeleteError(null);
    try {
      await onDelete(confirmDelete.id);
      setConfirmDelete(null);
      onRefresh();
    } catch (err: any) {
      setDeleteError(err?.response?.data?.message ?? 'Could not delete this item.');
    }
  }

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[300px_1fr]">
      <Panel className="h-fit">
        <h2 className="text-sm font-bold text-slate-800">Add new {itemNoun}</h2>
        <form onSubmit={handleCreate} className="mt-4 space-y-3">
          <div>
            <Label>Name</Label>
            <Input
              placeholder={`Enter ${itemNoun} name…`}
              value={name}
              maxLength={maxLength}
              onChange={(e) => {
                setName(e.target.value);
                setError(null);
              }}
            />
          </div>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" className="w-full" disabled={!name.trim()}>
            + Add {itemNoun}
          </Button>
        </form>
      </Panel>

      <Panel>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-slate-800">
            Existing {plural} ({items.length})
          </h2>
          <Input placeholder={`Search ${plural}…`} value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        </div>
        <div className="divide-y divide-slate-100">
          {filtered.map((item) => (
            <ListRow key={item.id}>
              {editingId === item.id ? (
                <Input value={editingName} maxLength={maxLength} onChange={(e) => setEditingName(e.target.value)} className="max-w-xs" />
              ) : (
                <span className="text-sm text-slate-800">{item.name}</span>
              )}
              <div className="flex items-center gap-3 text-xs">
                {editingId === item.id ? (
                  <>
                    <button className="font-medium text-blue-700 hover:underline" onClick={() => handleSaveEdit(item)}>
                      Save
                    </button>
                    <button className="text-slate-500 hover:underline" onClick={() => setEditingId(null)}>
                      Cancel
                    </button>
                  </>
                ) : (
                  <>
                    <StatusPill active={item.isActive} />
                    <button
                      className="font-medium text-blue-700 hover:underline"
                      onClick={() => {
                        setEditingId(item.id);
                        setEditingName(item.name);
                      }}
                    >
                      Rename
                    </button>
                    <button className="text-slate-500 hover:underline" onClick={() => handleToggleActive(item)}>
                      {item.isActive ? 'Deactivate' : 'Reactivate'}
                    </button>
                    <button className="font-medium text-red-600 hover:underline" onClick={() => setConfirmDelete(item)}>
                      Delete
                    </button>
                  </>
                )}
              </div>
            </ListRow>
          ))}
          {filtered.length === 0 && <p className="py-8 text-center text-sm text-slate-400">No {plural} found.</p>}
        </div>
      </Panel>

      {confirmDelete && (
        <ConfirmDialog
          title={`Delete "${confirmDelete.name}"?`}
          message={`${deleteWarning}${deleteError ? `\n\n${deleteError}` : ''}`}
          confirmLabel="Delete"
          danger
          onConfirm={handleConfirmDelete}
          onCancel={() => {
            setConfirmDelete(null);
            setDeleteError(null);
          }}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title={`Deactivate "${confirmDeactivate.name}"?`}
          message={deactivateWarning}
          confirmLabel="Deactivate"
          danger
          onConfirm={handleConfirmDeactivate}
          onCancel={() => setConfirmDeactivate(null)}
        />
      )}
    </div>
  );
}

function SlaPlansTab({ items, onRefresh }: { items: SlaPlanItemFull[]; onRefresh: () => void }) {
  const DEFAULT_HOURS = 72;
  const [name, setName] = useState('');
  const [resolutionHours, setResolutionHours] = useState(DEFAULT_HOURS);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<SlaPlanItemFull | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<SlaPlanItemFull | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => i.name.toLowerCase().includes(q));
  }, [items, search]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) {
      setError('SLA plan name is required.');
      return;
    }
    try {
      await lookupsApi.createSlaPlan(name.trim(), resolutionHours);
      setName('');
      setResolutionHours(DEFAULT_HOURS);
      onRefresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not add SLA plan.');
    }
  }

  async function handleToggleActive(item: SlaPlanItemFull) {
    if (item.isActive) {
      setConfirmDeactivate(item);
      return;
    }
    await lookupsApi.updateSlaPlan(item.id, item.name, item.resolutionTimeHours, true);
    onRefresh();
  }

  async function handleConfirmDeactivate() {
    if (!confirmDeactivate) return;
    await lookupsApi.updateSlaPlan(confirmDeactivate.id, confirmDeactivate.name, confirmDeactivate.resolutionTimeHours, false);
    setConfirmDeactivate(null);
    onRefresh();
  }

  async function handleConfirmDelete() {
    if (!confirmDelete) return;
    setDeleteError(null);
    try {
      await lookupsApi.deleteSlaPlan(confirmDelete.id);
      setConfirmDelete(null);
      onRefresh();
    } catch (err: any) {
      setDeleteError(err?.response?.data?.message ?? 'Could not delete this SLA plan.');
    }
  }

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[300px_1fr]">
      <Panel className="h-fit">
        <h2 className="text-sm font-bold text-slate-800">Add new SLA plan</h2>
        <form onSubmit={handleCreate} className="mt-4 space-y-3">
          <div>
            <Label>Name</Label>
            <Input
              placeholder="e.g. Standard"
              value={name}
              maxLength={SLA_NAME_MAX}
              onChange={(e) => {
                setName(e.target.value);
                setError(null);
              }}
            />
          </div>
          <div>
            <Label>Resolution time (hours)</Label>
            <Input
              type="number"
              min={1}
              value={resolutionHours}
              onChange={(e) => {
                setResolutionHours(Number(e.target.value));
                setError(null);
              }}
            />
          </div>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" className="w-full" disabled={!name.trim()}>
            + Add SLA Plan
          </Button>
        </form>
      </Panel>

      <Panel>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-sm font-bold text-slate-800">Existing SLA plans ({items.length})</h2>
          <Input placeholder="Search SLA plans…" value={search} onChange={(e) => setSearch(e.target.value)} className="max-w-xs" />
        </div>
        <div className="divide-y divide-slate-100">
          {filtered.map((item) => (
            <ListRow key={item.id}>
              <span className="text-sm text-slate-800">
                {item.name} <span className="text-xs text-slate-400">— {item.resolutionTimeHours}h resolution</span>
              </span>
              <div className="flex items-center gap-3 text-xs">
                <StatusPill active={item.isActive} />
                <button className="text-slate-500 hover:underline" onClick={() => handleToggleActive(item)}>
                  {item.isActive ? 'Deactivate' : 'Reactivate'}
                </button>
                <button className="font-medium text-red-600 hover:underline" onClick={() => setConfirmDelete(item)}>
                  Delete
                </button>
              </div>
            </ListRow>
          ))}
          {filtered.length === 0 && <p className="py-8 text-center text-sm text-slate-400">No SLA plans found.</p>}
        </div>
      </Panel>

      {confirmDelete && (
        <ConfirmDialog
          title={`Delete "${confirmDelete.name}"?`}
          message={`This SLA plan will be permanently deleted. Any tickets currently using it simply lose their SLA plan reference — nothing else is affected.${deleteError ? `\n\n${deleteError}` : ''}`}
          confirmLabel="Delete"
          danger
          onConfirm={handleConfirmDelete}
          onCancel={() => {
            setConfirmDelete(null);
            setDeleteError(null);
          }}
        />
      )}
      {confirmDeactivate && (
        <ConfirmDialog
          title={`Deactivate "${confirmDeactivate.name}"?`}
          message={`Tickets currently using "${confirmDeactivate.name}" keep it and are completely unaffected — nothing changes for existing tickets or statistics. This only removes it from the picker when opening a new ticket.`}
          confirmLabel="Deactivate"
          danger
          onConfirm={handleConfirmDeactivate}
          onCancel={() => setConfirmDeactivate(null)}
        />
      )}
    </div>
  );
}

function CategoriesAndSlaTab({
  categories,
  refreshCategories,
  slaPlans,
  refreshSlaPlans,
}: {
  categories: LookupItemFull[];
  refreshCategories: () => void;
  slaPlans: SlaPlanItemFull[];
  refreshSlaPlans: () => void;
}) {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-500">Categories</h2>
        <SimpleLookupTab
          itemNoun="category"
          pluralNoun="categories"
          maxLength={CATEGORY_MAX}
          items={categories}
          onRefresh={refreshCategories}
          onCreate={lookupsApi.createTicketCategory}
          onUpdate={lookupsApi.updateTicketCategory}
          onDelete={lookupsApi.deleteTicketCategory}
          deleteWarning="This category will be permanently deleted. Any tickets currently using it simply lose their category reference — nothing else is affected."
          deactivateWarning="Tickets currently using this category keep it and are completely unaffected — nothing changes for existing tickets or statistics. This only removes it from the picker when opening a new ticket."
        />
      </div>

      <div className="border-t border-slate-200 pt-6">
        <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-500">SLA Plans</h2>
        <SlaPlansTab items={slaPlans} onRefresh={refreshSlaPlans} />
      </div>
    </div>
  );
}

function NotificationsTab() {
  const [notifyOnSlaBreach, setNotifyOnSlaBreach] = useState(true);
  const [manualOverdueDays, setManualOverdueDays] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    settingsApi
      .getOverdueNotifications()
      .then((s) => {
        setNotifyOnSlaBreach(s.notifyOnSlaBreach);
        setManualOverdueDays(s.manualOverdueDays?.toString() ?? '');
      })
      .finally(() => setLoading(false));
  }, []);

  async function handleSave() {
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      await settingsApi.updateOverdueNotifications(notifyOnSlaBreach, manualOverdueDays.trim() ? Number(manualOverdueDays) : null);
      setSaved(true);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not save settings.');
    } finally {
      setSaving(false);
    }
  }

  if (loading) return null;

  return (
    <div className="space-y-6">
      <Panel className="max-w-lg">
        <h2 className="text-sm font-bold text-slate-800">Overdue ticket notifications</h2>
        <p className="mt-0.5 mb-4 text-xs text-slate-400">
          Emails every assignee once a ticket first becomes overdue. Each ticket is only notified once.
        </p>
        <label className="mb-3 flex items-center gap-2 text-sm text-slate-700">
          <input type="checkbox" checked={notifyOnSlaBreach} onChange={(e) => setNotifyOnSlaBreach(e.target.checked)} />
          Notify when a ticket passes its SLA due date
        </label>
        <Label>Also notify after this many days open (optional)</Label>
        <Input
          type="number"
          min={1}
          max={365}
          placeholder="e.g. 3"
          value={manualOverdueDays}
          onChange={(e) => setManualOverdueDays(e.target.value)}
          className="max-w-[8rem]"
        />
        <div className="mt-4">
          <ErrorText>{error}</ErrorText>
          <SuccessText>{saved ? 'Saved.' : null}</SuccessText>
          <Button onClick={handleSave} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </Panel>

      <KnowledgeBaseMaintenancePanel />
      <KnowledgeBaseDocumentsPanel />
    </div>
  );
}

function formatBytesAsChunks(chunkCount: number) {
  return `${chunkCount} ${chunkCount === 1 ? 'chunk' : 'chunks'}`;
}

function KnowledgeBaseDocumentsPanel() {
  const [documents, setDocuments] = useState<KnowledgeBaseDocumentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<KnowledgeBaseDocumentDto | null>(null);

  function refresh() {
    knowledgeBaseApi.listDocuments().then(setDocuments).finally(() => setLoading(false));
  }

  useEffect(refresh, []);

  async function handleFilesSelected(e: React.ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? []);
    e.target.value = '';
    if (files.length === 0) return;
    setUploading(true);
    setError(null);
    try {
      await knowledgeBaseApi.uploadDocuments(files);
      refresh();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not upload one or more of the selected files.');
    } finally {
      setUploading(false);
    }
  }

  async function handleDeleteConfirmed() {
    if (!confirmDelete) return;
    const target = confirmDelete;
    setConfirmDelete(null);
    await knowledgeBaseApi.deleteDocument(target.id);
    refresh();
  }

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">Knowledge base documentation</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-400">
        Upload manuals or instructions (Word .docx, Excel .xls/.xlsx, PDF) so the AI assistant can answer
        questions using official documentation, not just past tickets. Legacy binary .doc isn't supported —
        re-save as .docx first.
      </p>
      <ErrorText>{error}</ErrorText>
      <label>
        <span className="sr-only">Upload documentation files</span>
        <input
          type="file"
          multiple
          accept=".docx,.xls,.xlsx,.pdf"
          onChange={handleFilesSelected}
          disabled={uploading}
          className="block w-full text-sm text-slate-600 file:mr-3 file:rounded-md file:border-0 file:bg-blue-700 file:px-3 file:py-2 file:text-sm file:font-medium file:text-white hover:file:bg-blue-800"
        />
      </label>
      {uploading && <p className="mt-2 text-xs text-slate-400">Uploading and indexing — this can take a moment per file…</p>}

      {!loading && documents.length > 0 && (
        <ul className="mt-4 space-y-1">
          {documents.map((d) => (
            <ListRow key={d.id}>
              <div className="min-w-0">
                <div className="truncate text-sm font-medium text-slate-800">{d.title}</div>
                <div className="text-xs text-slate-400">
                  {d.sourceFileName} · {formatBytesAsChunks(d.chunkCount)}
                </div>
              </div>
              <button className="shrink-0 text-xs text-red-600 hover:underline" onClick={() => setConfirmDelete(d)}>
                Delete
              </button>
            </ListRow>
          ))}
        </ul>
      )}
      {!loading && documents.length === 0 && <p className="mt-4 text-sm text-slate-400">No documentation uploaded yet.</p>}

      {confirmDelete && (
        <ConfirmDialog
          title="Remove this document?"
          message={`"${confirmDelete.title}" and its indexed content will be permanently removed from the knowledge base. The AI assistant will no longer be able to reference it.`}
          confirmLabel="Delete"
          onConfirm={handleDeleteConfirmed}
          onCancel={() => setConfirmDelete(null)}
        />
      )}
    </Panel>
  );
}

function KnowledgeBaseMaintenancePanel() {
  const [reindexing, setReindexing] = useState(false);
  const [result, setResult] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleReindex() {
    setReindexing(true);
    setError(null);
    setResult(null);
    try {
      const res = await knowledgeBaseApi.reindexAll();
      setResult(res.indexed);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not reindex the knowledge base.');
    } finally {
      setReindexing(false);
    }
  }

  return (
    <Panel className="max-w-lg">
      <h2 className="text-sm font-bold text-slate-800">Knowledge base</h2>
      <p className="mt-0.5 mb-4 text-xs text-slate-400">
        AI search only picks up a ticket once it's been indexed — normally that happens automatically the moment a
        ticket is closed through the app. Tickets inserted directly (e.g. bulk-imported demo data) skip that step, so
        they won't show up in AI search or chat until you reindex. This rebuilds the knowledge base entry for every
        ticket in the system — safe to run any time.
      </p>
      <ErrorText>{error}</ErrorText>
      {result !== null && <SuccessText>{`Reindexed ${result} tickets.`}</SuccessText>}
      <Button onClick={handleReindex} disabled={reindexing}>
        {reindexing ? 'Reindexing…' : 'Reindex all tickets'}
      </Button>
    </Panel>
  );
}

export default function SettingsPage() {
  const [tab, setTab] = useState<TabKey>('branches');
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [helpTopics, setHelpTopics] = useState<LookupItemFull[]>([]);
  const [categories, setCategories] = useState<LookupItemFull[]>([]);
  const [slaPlans, setSlaPlans] = useState<SlaPlanItemFull[]>([]);

  function refreshDepartments() {
    lookupsApi.departmentsAdmin().then(setDepartments);
  }
  function refreshHelpTopics() {
    lookupsApi.helpTopicsAdmin().then(setHelpTopics);
  }
  function refreshCategories() {
    lookupsApi.ticketCategoriesAdmin().then(setCategories);
  }
  function refreshSlaPlans() {
    lookupsApi.slaPlansAdmin().then(setSlaPlans);
  }

  useEffect(() => {
    refreshDepartments();
    refreshHelpTopics();
    refreshCategories();
    refreshSlaPlans();
  }, []);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900">Settings</h1>
        <p className="mt-1 text-sm text-slate-500">
          Manage the dropdown values agents choose from when opening a ticket. Only Admins can change these.
        </p>
      </div>

      <div className="flex gap-1 border-b border-slate-200">
        {TABS.map((t) => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            className={clsx(
              'flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm font-semibold transition',
              tab === t.key ? 'border-blue-700 text-blue-700' : 'border-transparent text-slate-500 hover:text-slate-700'
            )}
          >
            <span>{t.icon}</span>
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'branches' && <BranchesTab items={departments} onRefresh={refreshDepartments} />}
      {tab === 'helpTopics' && (
        <SimpleLookupTab
          itemNoun="help topic"
          maxLength={HELP_TOPIC_MAX}
          items={helpTopics}
          onRefresh={refreshHelpTopics}
          onCreate={lookupsApi.createHelpTopic}
          onUpdate={lookupsApi.updateHelpTopic}
          onDelete={lookupsApi.deleteHelpTopic}
          deleteWarning="This help topic will be permanently deleted. Any tickets currently using it simply lose their help topic reference — nothing else is affected."
          deactivateWarning="Tickets currently using this help topic keep it and are completely unaffected — nothing changes for existing tickets or statistics. This only removes it from the picker when opening a new ticket."
        />
      )}
      {tab === 'categoriesAndSla' && (
        <CategoriesAndSlaTab
          categories={categories}
          refreshCategories={refreshCategories}
          slaPlans={slaPlans}
          refreshSlaPlans={refreshSlaPlans}
        />
      )}
      {tab === 'notifications' && <NotificationsTab />}
    </div>
  );
}
