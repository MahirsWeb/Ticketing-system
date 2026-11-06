import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient } from '@tanstack/react-query';
import { KnowledgeBaseDocumentsPanel } from './KnowledgeBaseDocumentsPanel';
import { KnowledgeBaseMaintenancePanel } from './KnowledgeBaseMaintenancePanel';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installFakeHttp, refuse } from '../../test/fakeHttp';
import type { ReindexStatus } from '../../api/knowledgeBase';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn(), delete: vi.fn() } }));

const STATUS_URL = /^\/api\/knowledge-base\/reindex-status$/;
const START_ANY = /^\/api\/knowledge-base\/(reindex-all|documents\/[^/]+\/reindex)$/;

const status = (extra: Partial<ReindexStatus> = {}): ReindexStatus => ({
  state: 'Idle', total: 0, processed: 0, embedded: 0, failed: 0, startedAtUtc: null, finishedAtUtc: null, message: null, ...extra,
});
const running = (processed = 3, total = 10, message = 'Embedding tickets.') => status({ state: 'Running', processed, total, message });

let server: ReturnType<typeof setup>;

/** The server as far as the two panels know it: one background run at a time, whoever started it, and the documents that were uploaded. */
function setup(initial: ReindexStatus, chunkCount = 12) {
  const state = { reindex: initial, chunkCount };
  const http = installFakeHttp((method, url) => {
    if (method === 'GET' && url === '/api/knowledge-base/reindex-status') return state.reindex;
    if (method === 'GET' && url === '/api/knowledge-base/documents') {
      return [{ id: 'k1', title: 'Manual', sourceFileName: 'manual.docx', fileUrl: null, createdAt: '2026-10-01T08:00:00Z', chunkCount: state.chunkCount }];
    }
    if (method === 'POST' && START_ANY.test(url)) {
      if (state.reindex.state === 'Running') refuse(409, { message: 'A re-index is already running.', status: state.reindex });
      state.reindex = running(0, 10, 'Started.');
      return state.reindex;
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  });
  return { state, ...http };
}

function renderPanels(initial: ReindexStatus = status(), before?: (s: ReturnType<typeof setup>) => void, client?: QueryClient) {
  server = setup(initial);
  before?.(server);
  return renderWithQuery(
    <>
      <KnowledgeBaseMaintenancePanel />
      <KnowledgeBaseDocumentsPanel />
    </>,
    client
  );
}

const asked = () => server.sent('GET', STATUS_URL);
const startButton = () => screen.getByRole('button', { name: /^(Reindex missing tickets|Reindexing…)$/ });
const reindexOf = async () => within((await screen.findByText('Manual')).closest('div')!.parentElement!.parentElement!).getByRole('button', { name: 'Re-index' });
const advance = (ms: number) => act(async () => { vi.advanceTimersByTime(ms); });
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });
const slowly = () => vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setInterval', 'clearInterval'] });

afterEach(() => vi.useRealTimers());

describe('the two panels that look after the knowledge base', () => {
  describe('following a run', () => {
    it('asks again every few seconds while a run is going, and stops asking when it is over', async () => {
      slowly();
      renderPanels(running());
      expect(await screen.findByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      expect(asked()).toHaveLength(1);

      server.state.reindex = status({ state: 'Completed', total: 10, processed: 10, embedded: 7, message: 'Finished.' });
      await advance(3000);
      expect(await screen.findByText(/Finished\./)).toBeInTheDocument();
      expect(startButton()).toBeEnabled();
      const asking = asked().length;

      await advance(12000);

      expect(asked()).toHaveLength(asking);
    });

    it('does not pile up questions while the server is slow to answer the last one', async () => {
      slowly();
      renderPanels(running());
      await screen.findByRole('button', { name: 'Reindexing…' });
      server.hold('GET', STATUS_URL);

      await advance(3000);
      await advance(3000);
      await advance(3000);

      expect(asked()).toHaveLength(2);
    });

    it('does not let an answer to an old question undo the start of a run', async () => {
      const user = userEvent.setup();
      let late!: { release: () => void };
      renderPanels(status(), (s) => { late = s.hold('GET', STATUS_URL); });
      await waitFor(() => expect(asked()).toHaveLength(1));

      await user.click(startButton());
      expect(await screen.findByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      late.release();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });

      expect(screen.getByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      expect(screen.getByText(/0 \/ 10/)).toBeInTheDocument();
    });

    it('says so when it cannot find out how far the run is, and goes on asking until it can', async () => {
      slowly();
      renderPanels(running());
      await screen.findByRole('button', { name: 'Reindexing…' });
      server.refuse('GET', STATUS_URL, 500);

      await advance(3000);

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not be checked/);
      expect(screen.getByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      expect(screen.getByText(/3 \/ 10/)).toBeInTheDocument();

      server.state.reindex = status({ state: 'Completed', total: 10, processed: 10, embedded: 4, message: 'Finished.' });
      await advance(3000);

      expect(await screen.findByText(/Finished\./)).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('stops asking, and cancels a question that is still on its way, when the panels are left', async () => {
      slowly();
      const view = renderPanels(running());
      await screen.findByRole('button', { name: 'Reindexing…' });
      server.hold('GET', STATUS_URL);
      await advance(3000);
      await waitFor(() => expect(asked()).toHaveLength(2));
      const signal = asked()[1].signal;

      view.unmount();
      await advance(12000);

      expect(signal?.aborted).toBe(true);
      expect(asked()).toHaveLength(2);
    });
  });

  describe('when the state of the run is not known', () => {
    it('says so, with a way to try again, and then shows what the server says', async () => {
      const user = userEvent.setup();
      renderPanels(running(), (s) => s.refuse('GET', STATUS_URL, 500));

      expect(await screen.findByRole('alert')).toHaveTextContent('The state of the reindex could not be loaded.');
      await user.click(screen.getByRole('button', { name: 'Try again' }));

      expect(await screen.findByRole('button', { name: 'Reindexing…' })).toBeDisabled();
      expect(screen.queryByText('The state of the reindex could not be loaded.')).not.toBeInTheDocument();
      expect(screen.getByText(/3 \/ 10/)).toBeInTheDocument();
    });

    it('does not keep what it was told, to show it from memory the next time (even where a client would keep what it fetched)', async () => {
      const view = renderPanels(running(), undefined, new QueryClient({ defaultOptions: { queries: { retry: false } } }));
      await screen.findByRole('button', { name: 'Reindexing…' });

      view.unmount();
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });

      expect(view.client.getQueryCache().getAll()).toHaveLength(0);
    });
  });

  describe('the two panels know of one another, because there is one run at a time', () => {
    it('does not offer to re-index a document while all the tickets are being re-indexed', async () => {
      const user = userEvent.setup();
      renderPanels();
      await screen.findByText('Manual');

      await user.click(startButton());

      await waitFor(async () => expect(await reindexOf()).toBeDisabled());
      expect(await reindexOf()).toHaveAttribute('title', expect.stringMatching(/already running/));
    });

    it('does not offer to start the re-index of the tickets while a document is being re-indexed', async () => {
      const user = userEvent.setup();
      renderPanels();

      await user.click(await reindexOf());

      expect(await screen.findByRole('button', { name: 'Reindexing…' })).toBeDisabled();
    });

    it('does not offer a document re-index while a run that was going when the page was opened is on', async () => {
      renderPanels(running());

      await screen.findByRole('button', { name: 'Reindexing…' });

      expect(await reindexOf()).toBeDisabled();
    });

    it('shows how a re-index of a document went, and reloads the documents when it is over (the chunks may have changed)', async () => {
      slowly();
      const user = userEvent.setup({ advanceTimers: (ms) => vi.advanceTimersByTime(ms) });
      renderPanels();
      expect(await screen.findByText('manual.docx · 12 chunks')).toBeInTheDocument();
      await user.click(await reindexOf());
      expect(await screen.findByText(/0 \/ 10 · Started\./)).toBeInTheDocument();

      server.state.chunkCount = 15;
      server.state.reindex = status({ state: 'Completed', total: 10, processed: 10, embedded: 10, message: 'Rebuilt.' });
      await advance(3000);

      expect(await screen.findByText('manual.docx · 15 chunks')).toBeInTheDocument();
      expect(screen.getByText('Rebuilt.')).toBeInTheDocument();
      expect(await reindexOf()).toBeEnabled();
    });

    it('does not show the progress of a run under the documents when it was not started there', async () => {
      renderPanels(running(4, 20, 'Working on it.'));

      await screen.findByRole('button', { name: 'Reindexing…' });

      expect(screen.getAllByText(/4 \/ 20/)).toHaveLength(1);
    });

    it('says what the server said when it refuses a re-index without telling how far the other run is', async () => {
      const user = userEvent.setup();
      renderPanels(status(), (s) => s.refuse('POST', START_ANY, 409, { message: 'A re-index is already running.' }));

      await user.click(await reindexOf());

      expect(await screen.findByText('A re-index is already running.')).toBeInTheDocument();
    });

    it('says so under the documents too when it cannot find out how far a re-index of a document is', async () => {
      slowly();
      const user = userEvent.setup({ advanceTimers: (ms) => vi.advanceTimersByTime(ms) });
      renderPanels();
      await user.click(await reindexOf());
      await screen.findByText(/0 \/ 10 · Started\./);
      server.refuse('GET', STATUS_URL, 500);

      await advance(3000);

      expect(await screen.findAllByRole('alert')).toHaveLength(2);
    });
  });

  describe('what the panels say about how a run went', () => {
    it('says that a run is going, and how many were embedded only when it is over', async () => {
      renderPanels(status({ state: 'Running', total: 10, processed: 3, embedded: 5, message: 'Embedding tickets.' }));

      const line = await screen.findByText(/Embedding tickets\./);

      expect(line).toHaveTextContent('3 / 10 · Running… Embedding tickets.');
      expect(line).not.toHaveTextContent('embedded');
    });

    it('says nothing of a run that has not been made, even when the server has words for it', async () => {
      renderPanels(status({ state: 'Idle', message: 'Nothing has been run yet.' }));
      await screen.findByRole('button', { name: 'Reindex missing tickets' });
      await settle();

      expect(screen.queryByText(/Nothing has been run yet\./)).not.toBeInTheDocument();
    });

    it('does not say how far a run got when it never counted anything', async () => {
      renderPanels(status({ state: 'Failed', total: 0, message: 'The AI service is down.' }));

      const line = await screen.findByText(/The AI service is down\./);

      expect(line.textContent?.trim()).toBe('The AI service is down.');
    });

    it('says a run that went well in green', async () => {
      renderPanels(status({ state: 'Completed', total: 8, processed: 8, embedded: 6, message: 'Done.' }));

      expect((await screen.findByText(/Done\./)).className).toContain('text-green-700');
    });

    it.each([
      { how: 'went well', state: 'Completed' as const, colour: 'text-green-700' },
      { how: 'failed', state: 'Failed' as const, colour: 'text-red-700' },
    ])('says under the documents, in its own colour, how a re-index of a document that $how', async ({ state, colour }) => {
      slowly();
      const user = userEvent.setup({ advanceTimers: (ms) => vi.advanceTimersByTime(ms) });
      renderPanels();
      await user.click(await reindexOf());
      await screen.findByText(/0 \/ 10 · Started\./);

      server.state.reindex = status({ state, total: 10, processed: 10, message: 'It is over.' });
      await advance(3000);

      expect((await screen.findByText('It is over.')).className).toContain(colour);
    });

    it('does not take the run of somebody else for its own when its own start was refused', async () => {
      const user = userEvent.setup();
      renderPanels(status({ state: 'Completed', total: 8, processed: 8, embedded: 6, message: 'Done.' }), (s) => s.refuse('POST', START_ANY, 500));
      await screen.findByText(/Done\./);

      await user.click(await reindexOf());

      expect(await screen.findByText('Could not start the re-index.')).toBeInTheDocument();
      expect(screen.getAllByText(/Done\./)).toHaveLength(1);
    });
  });

  describe('starting again after a refusal', () => {
    it('stops saying why the last start was refused as soon as the tickets are asked to be re-indexed again', async () => {
      const user = userEvent.setup();
      renderPanels(status(), (s) => s.refuse('POST', START_ANY, 400, { message: 'The AI service is not set up.' }));
      await user.click(startButton());
      expect(await screen.findByText('The AI service is not set up.')).toBeInTheDocument();
      const slow = server.hold('POST', START_ANY);

      await user.click(startButton());

      expect(screen.queryByText('The AI service is not set up.')).not.toBeInTheDocument();
      slow.release();
    });

    it('stops saying why the last start was refused as soon as a document is asked to be re-indexed again', async () => {
      const user = userEvent.setup();
      renderPanels(status(), (s) => s.refuse('POST', START_ANY, 400, { message: 'The AI service is not set up.' }));
      await user.click(await reindexOf());
      expect(await screen.findByText('The AI service is not set up.')).toBeInTheDocument();
      const slow = server.hold('POST', START_ANY);

      await user.click(await reindexOf());

      expect(screen.queryByText('The AI service is not set up.')).not.toBeInTheDocument();
      slow.release();
    });

    it('does not leave what the server said in memory when the panels were left before it answered', async () => {
      const user = userEvent.setup();
      let late!: { release: () => void };
      const view = renderPanels(status(), (s) => { late = s.hold('POST', START_ANY); }, new QueryClient({ defaultOptions: { queries: { retry: false } } }));
      await user.click(startButton());

      view.unmount();
      await settle(); // what nobody looks at any more is forgotten
      late.release();
      await settle();

      expect(view.client.getQueryCache().getAll()).toHaveLength(0);
    });
  });

  describe('the list of documents', () => {
    it('is asked for once, and not again until a run has ended', async () => {
      renderPanels(status());
      await screen.findByText('manual.docx · 12 chunks');
      await settle();

      expect(server.sent('GET', /^\/api\/knowledge-base\/documents$/)).toHaveLength(1);
    });
  });
});
