import { describe, expect, it, vi } from 'vitest';
import { finishNewTicket } from './finishNewTicket';

const file = (name: string) => new File(['x'], name);

// NOVO-FE-03: a second attempt after the e-mail step failed attached every file to the ticket again.
describe('finishNewTicket', () => {
  it('sends the files once and the e-mail step again when only the e-mail step failed', async () => {
    const upload = vi.fn().mockResolvedValue(undefined);
    const complete = vi.fn().mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(undefined);
    const files = [file('a.pdf'), file('b.pdf')];
    const uploaded = new Set<File>();

    await expect(finishNewTicket('t1', files, 'mail-1', uploaded, { upload, complete })).rejects.toThrow('network');
    await finishNewTicket('t1', files, 'mail-1', uploaded, { upload, complete });

    expect(upload).toHaveBeenCalledTimes(1);
    expect(upload).toHaveBeenCalledWith('t1', files);
    expect(complete).toHaveBeenCalledTimes(2);
    expect(complete).toHaveBeenLastCalledWith('mail-1', 't1');
  });

  it('sends only what has not gone up when the upload itself failed part of the way', async () => {
    const upload = vi.fn().mockRejectedValueOnce(new Error('too big')).mockResolvedValue(undefined);
    const files = [file('a.pdf')];
    const uploaded = new Set<File>();

    await expect(finishNewTicket('t1', files, null, uploaded, { upload, complete: vi.fn() })).rejects.toThrow('too big');
    const more = file('c.pdf');
    await finishNewTicket('t1', [...files, more], null, uploaded, { upload, complete: vi.fn() });

    expect(upload).toHaveBeenCalledTimes(2);
    expect(upload).toHaveBeenLastCalledWith('t1', [...files, more]); // nothing had gone up, so both go now
    await finishNewTicket('t1', [...files, more, file('d.pdf')], null, uploaded, { upload, complete: vi.fn() });
    expect(upload.mock.calls[2][1].map((f: File) => f.name)).toEqual(['d.pdf']); // and now only the new one
  });

  it('does nothing for a ticket with no files and no e-mail', async () => {
    const upload = vi.fn();
    const complete = vi.fn();

    await finishNewTicket('t1', [], null, new Set(), { upload, complete });

    expect(upload).not.toHaveBeenCalled();
    expect(complete).not.toHaveBeenCalled();
  });
});
