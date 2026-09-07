import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ticketsApi } from '../../api/tickets';
import { lookupsApi } from '../../api/lookups';
import type { DepartmentItemFull } from '../../types';
import { Button, Card, ErrorText, Input, Label, Select } from '../../components/ui';
import { RichTextEditor } from '../../components/RichTextEditor';

export default function NewTicketPage() {
  const navigate = useNavigate();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [departments, setDepartments] = useState<DepartmentItemFull[]>([]);
  const [files, setFiles] = useState<File[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    lookupsApi.departments().then(setDepartments);
  }, []);

  function addFiles(newFiles: File[]) {
    setFiles((prev) => [...prev, ...newFiles]);
  }

  function removeFile(index: number) {
    setFiles((prev) => prev.filter((_, i) => i !== index));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!title.trim() || !description.trim() || description === '<p></p>') {
      setError('Title and description are required.');
      return;
    }
    if (!departmentId) {
      setError('Please select a branch to send this ticket to.');
      return;
    }
    setLoading(true);
    try {
      const ticket = await ticketsApi.create(title, description, departmentId);
      if (files.length > 0) {
        await ticketsApi.uploadAttachments(ticket.id, files);
      }
      navigate(`/tickets/${ticket.id}`);
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'Could not submit your ticket.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="mb-4 text-xl font-bold text-slate-900">Submit a ticket</h1>
      <Card className="p-6">
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <Label>Title *</Label>
            <Input required value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Short summary of the issue" />
          </div>
          <div>
            <Label>Branch *</Label>
            <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">Select a branch…</option>
              {departments.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name} ({d.email})
                </option>
              ))}
            </Select>
          </div>
          <div>
            <Label>Description *</Label>
            <RichTextEditor
              value={description}
              onChange={setDescription}
              onFilesSelected={addFiles}
              placeholder="Describe the issue in detail… (you can also paste or attach a screenshot)"
            />
            {files.length > 0 && (
              <ul className="mt-2 flex flex-wrap gap-2">
                {files.map((f, i) => (
                  <li key={`${f.name}-${i}`} className="flex items-center gap-1 rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-600">
                    📎 {f.name}
                    <button type="button" onClick={() => removeFile(i)} className="ml-1 font-bold text-slate-400 hover:text-red-600">
                      ×
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
          <ErrorText>{error}</ErrorText>
          <Button type="submit" disabled={loading}>
            {loading ? 'Submitting…' : 'Submit ticket'}
          </Button>
        </form>
      </Card>
    </div>
  );
}
