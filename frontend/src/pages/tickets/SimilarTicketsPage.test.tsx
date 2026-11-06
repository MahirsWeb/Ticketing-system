import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient } from '@tanstack/react-query';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import SimilarTicketsPage from './SimilarTicketsPage';
import { renderWithQuery } from '../../test/renderWithQuery';
import { installFakeHttp, refuse } from '../../test/fakeHttp';
import type { KnowledgeBaseSearchResultDto, SimilarTicketsResponseDto } from '../../types';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn() } }));

const REPORT_URL = /^\/api\/knowledge-base\/similar-tickets$/;

const found = (n: number, extra: Partial<KnowledgeBaseSearchResultDto> = {}): KnowledgeBaseSearchResultDto => ({
  sourceType: 'Ticket',
  ticketId: `t${n}`,
  ticketNumber: `10000${n}`,
  status: 'Closed',
  closedAtUtc: '2026-03-14T10:00:00Z',
  resolutionSummary: null,
  documentId: null,
  documentFileUrl: null,
  title: `Printer jams ${n}`,
  matchScore: 90 - n,
  ...extra,
});

const report = (tickets: KnowledgeBaseSearchResultDto[], total = 40): SimilarTicketsResponseDto => ({
  similarTicketCount: tickets.length,
  totalEligibleTicketCount: total,
  similarPercentage: Math.round((tickets.length / total) * 100),
  tickets,
});

let server: ReturnType<typeof setup>;

/** The server as far as the page knows it: the report for each text that can be asked about. */
function setup(reports: Record<string, SimilarTicketsResponseDto>) {
  return installFakeHttp((method, url, _body, params) => {
    if (method === 'GET' && url === '/api/knowledge-base/similar-tickets') {
      return reports[params?.query as string] ?? refuse(404, { message: `The stand-in server has no report for "${params?.query}".` });
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  });
}

const address = (query: string) => `/knowledge-base/similar?query=${encodeURIComponent(query)}`;

function renderPage(
  reports: Record<string, SimilarTicketsResponseDto>,
  entries: string[] = [address('printer jams')],
  before?: (s: ReturnType<typeof setup>) => void,
  client?: QueryClient
) {
  server = setup(reports);
  before?.(server);
  return renderWithQuery(
    <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
      <Link to={address('scanner')}>other text</Link>
      <Routes>
        <Route path="/knowledge-base/similar" element={<SimilarTicketsPage />} />
        <Route path="/ai-assistant" element={<p>the assistant</p>} />
        <Route path="/earlier" element={<p>the earlier page</p>} />
      </Routes>
    </MemoryRouter>,
    client
  );
}

const asked = () => server.sent('GET', REPORT_URL);
const figure = (label: string) => screen.getByText(label).previousElementSibling as HTMLElement;
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });

afterEach(() => vi.restoreAllMocks());

describe('the similar problems report', () => {
  describe('what it shows', () => {
    it('asks for the report of the text in the address and says which text it is about', async () => {
      renderPage({ 'printer jams': report([found(1)]) });

      expect(await screen.findByText('#100001')).toBeInTheDocument();

      expect(asked()).toHaveLength(1);
      expect(asked()[0].params).toEqual({ query: 'printer jams' });
      expect(screen.getByRole('heading', { name: 'Similar problems report' })).toBeInTheDocument();
      expect(screen.getByText('“printer jams”')).toBeInTheDocument();
    });

    it('asks about the text exactly as it is in the address, spaces included', async () => {
      renderPage({ ' printer jams ': report([found(1)]) }, [address(' printer jams ')]);

      expect(await screen.findByText('#100001')).toBeInTheDocument();

      expect(asked()[0].params).toEqual({ query: ' printer jams ' });
    });

    it('says the knowledge base is being analyzed until the answer is there', async () => {
      renderPage({ 'printer jams': report([found(1)]) }, undefined, (s) => s.hold('GET', REPORT_URL));

      expect(screen.getByText(/Analyzing the knowledge base/)).toBeInTheDocument();
      expect(screen.getByRole('status')).toHaveTextContent('Analyzing the knowledge base');
      expect(screen.queryByText('Similar tickets')).not.toBeInTheDocument();
    });

    it('shows how many tickets are similar, how many there are, and what share that is', async () => {
      renderPage({ 'printer jams': report([found(1), found(2), found(3)], 12) });

      await screen.findByText('#100001');

      expect(figure('Similar tickets')).toHaveTextContent('3');
      expect(figure('Total documented tickets')).toHaveTextContent('12');
      expect(figure('Share of the whole base')).toHaveTextContent('25%');
    });

    it('lists the matching tickets with number, title, status, closing date and match', async () => {
      renderPage({ 'printer jams': report([found(1), found(2, { status: 'Resolved', closedAtUtc: null, matchScore: 71 })]) });

      const first = (await screen.findByText('#100001')).closest('tr')!;
      const second = screen.getByText('#100002').closest('tr')!;

      expect(within(first).getByText('Printer jams 1')).toBeInTheDocument();
      expect(within(first).getByText('Closed')).toBeInTheDocument();
      expect(within(first).getByText(new Date('2026-03-14T10:00:00Z').toLocaleDateString())).toBeInTheDocument();
      expect(within(first).getByText('89%')).toBeInTheDocument();
      expect(within(second).getByText('Resolved')).toBeInTheDocument();
      expect(within(second).getByText('—')).toBeInTheDocument();
      expect(within(second).getByText('71%')).toBeInTheDocument();
    });

    it('leaves the status cell empty for a ticket that has none', async () => {
      renderPage({ 'printer jams': report([found(1, { status: null })]) });

      const row = (await screen.findByText('#100001')).closest('tr')!;

      const status = row.querySelectorAll('td')[2];
      expect(status).toBeEmptyDOMElement();
    });

    it('says so when no ticket is similar, and still shows the figures', async () => {
      renderPage({ 'printer jams': report([], 9) });

      expect(await screen.findByText('No similar tickets found.')).toBeInTheDocument();

      expect(figure('Total documented tickets')).toHaveTextContent('9');
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
    });

    it('keeps showing the report while it is asked for again in the background, without the spinner over it', async () => {
      const view = renderPage({ 'printer jams': report([found(1)]) });
      await screen.findByText('#100001');
      server.hold('GET', REPORT_URL);

      await act(async () => { void view.client.refetchQueries(); });

      await waitFor(() => expect(asked()).toHaveLength(2));
      expect(screen.getByText('#100001')).toBeInTheDocument();
      expect(screen.queryByText(/Analyzing the knowledge base/)).not.toBeInTheDocument();
    });

    it('does not show the figures of the previous text while the report of the next one is on its way', async () => {
      const user = userEvent.setup();
      renderPage({ 'printer jams': report([found(1)]), scanner: report([found(7)]) });
      await screen.findByText('#100001');
      server.hold('GET', REPORT_URL);

      await user.click(screen.getByRole('link', { name: 'other text' }));

      expect(screen.getByText(/Analyzing the knowledge base/)).toBeInTheDocument();
      expect(screen.queryByText('#100001')).not.toBeInTheDocument();
    });

    it('asks again when the text in the address changes', async () => {
      const user = userEvent.setup();
      renderPage({ 'printer jams': report([found(1)]), scanner: report([found(7)]) });
      await screen.findByText('#100001');

      await user.click(screen.getByRole('link', { name: 'other text' }));

      expect(await screen.findByText('#100007')).toBeInTheDocument();
      expect(screen.queryByText('#100001')).not.toBeInTheDocument();
      expect(screen.getByText('“scanner”')).toBeInTheDocument();
      expect(asked().map((r) => r.params?.query)).toEqual(['printer jams', 'scanner']);
    });

    it('asks nothing and shows nothing of a report when there is no text', async () => {
      renderPage({}, ['/knowledge-base/similar']);
      await settle();

      expect(asked()).toHaveLength(0);
      expect(screen.queryByText('Similar tickets')).not.toBeInTheDocument();
    });
  });

  describe('opening a ticket', () => {
    it('opens the ticket in a new tab when its row is clicked', async () => {
      const user = userEvent.setup();
      const open = vi.spyOn(window, 'open').mockImplementation(() => null);
      renderPage({ 'printer jams': report([found(1), found(2)]) });

      await user.click(await screen.findByText('Printer jams 2'));

      expect(open).toHaveBeenCalledTimes(1);
      expect(open.mock.calls[0].slice(0, 2)).toEqual(['/tickets/t2', '_blank']);
    });

    it('opens it without handing the new tab this one (noopener)', async () => {
      const user = userEvent.setup();
      const open = vi.spyOn(window, 'open').mockImplementation(() => null);
      renderPage({ 'printer jams': report([found(1)]) });

      await user.click(await screen.findByText('Printer jams 1'));

      expect(String(open.mock.calls[0][2])).toContain('noopener');
    });

    it('has the number of the ticket as a link that the keyboard reaches and that opens a new tab', async () => {
      const user = userEvent.setup();
      const open = vi.spyOn(window, 'open').mockImplementation(() => null);
      renderPage({ 'printer jams': report([found(1)]) });

      const link = await screen.findByRole('link', { name: '#100001' });

      expect(link).toHaveAttribute('href', '/tickets/t1');
      expect(link).toHaveAttribute('target', '_blank');
      expect(link.getAttribute('rel')).toContain('noopener');
      link.focus();
      expect(link).toHaveFocus();
      // following the link must not open a second tab through the row it is in
      await user.click(link);
      expect(open).not.toHaveBeenCalled();
    });
  });

  describe('going back', () => {
    it('goes back to where it was opened from', async () => {
      const user = userEvent.setup();
      renderPage({ 'printer jams': report([found(1)]) }, ['/earlier', address('printer jams')]);
      await screen.findByText('#100001');

      await user.click(screen.getByRole('button', { name: /Back/ }));

      expect(await screen.findByText('the earlier page')).toBeInTheDocument();
    });

    it('goes to the assistant when it was opened directly and there is nothing to go back to', async () => {
      const user = userEvent.setup();
      renderPage({ 'printer jams': report([found(1)]) });
      await screen.findByText('#100001');

      await user.click(screen.getByRole('button', { name: /Back/ }));

      expect(await screen.findByText('the assistant')).toBeInTheDocument();
    });
  });

  describe('when it does not work out', () => {
    it('says why the report could not be made, with the words of the server, instead of waiting for ever', async () => {
      renderPage({}, undefined, (s) => s.refuse('GET', REPORT_URL, 400, { message: 'The text is too long (at most 2000 characters).' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('The text is too long (at most 2000 characters).');
      expect(screen.queryByText(/Analyzing the knowledge base/)).not.toBeInTheDocument();
    });

    it('says it in its own words when the server said nothing', async () => {
      renderPage({}, undefined, (s) => s.refuse('GET', REPORT_URL, 500, {}, -1));

      expect(await screen.findByRole('alert')).toHaveTextContent('The report could not be made.');
    });

    it('asks again when "Try again" is pressed, and then shows the report', async () => {
      const user = userEvent.setup();
      renderPage({ 'printer jams': report([found(1)]) }, undefined, (s) => s.refuse('GET', REPORT_URL, 429, { message: 'Too many requests.' }));
      await screen.findByRole('alert');

      await user.click(screen.getByRole('button', { name: 'Try again' }));

      expect(await screen.findByText('#100001')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      expect(asked()).toHaveLength(2);
    });

    it('does not show a failure of one text under the next one', async () => {
      const user = userEvent.setup();
      renderPage({ scanner: report([found(7)]) }, undefined, (s) => s.refuse('GET', REPORT_URL, 400, { message: 'Nope.' }));
      await screen.findByRole('alert');

      await user.click(screen.getByRole('link', { name: 'other text' }));

      expect(await screen.findByText('#100007')).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });

    it('says that there is nothing to look for when no text was given, and asks nothing', async () => {
      renderPage({}, ['/knowledge-base/similar']);

      expect(await screen.findByText(/no problem was described/i)).toBeInTheDocument();
      expect(screen.queryByText(/Analyzing the knowledge base/)).not.toBeInTheDocument();
      expect(asked()).toHaveLength(0);
    });

    it('takes a text of nothing but spaces for no text', async () => {
      renderPage({}, [address('   ')]);

      expect(await screen.findByText(/no problem was described/i)).toBeInTheDocument();
      expect(asked()).toHaveLength(0);
    });
  });

  describe('answers that come late', () => {
    it('shows the report of the text asked for last, even when the one asked for first answers after it', async () => {
      const user = userEvent.setup();
      let late!: { release: () => void };
      renderPage({ 'printer jams': report([found(1)]), scanner: report([found(7)]) }, undefined, (s) => {
        late = s.hold('GET', REPORT_URL);
      });
      await waitFor(() => expect(asked()).toHaveLength(1));
      await user.click(screen.getByRole('link', { name: 'other text' }));
      expect(await screen.findByText('#100007')).toBeInTheDocument();

      late.release();
      await settle();

      expect(screen.getByText('#100007')).toBeInTheDocument();
      expect(screen.queryByText('#100001')).not.toBeInTheDocument();
      expect(screen.getByText('“scanner”')).toBeInTheDocument();
    });
  });

  describe('leaving', () => {
    it('cancels the report that is still being made when the page is left', async () => {
      const view = renderPage({ 'printer jams': report([found(1)]) }, undefined, (s) => s.hold('GET', REPORT_URL));
      await waitFor(() => expect(asked()).toHaveLength(1));
      const signal = asked()[0].signal;

      view.unmount();

      expect(signal?.aborted).toBe(true);
    });

    it('does not keep the report to show it from memory the next time (even where a client would keep what it fetched)', async () => {
      const view = renderPage({ 'printer jams': report([found(1)]) }, undefined, undefined, new QueryClient({ defaultOptions: { queries: { retry: false } } }));
      await screen.findByText('#100001');

      view.unmount();
      await settle();

      expect(view.client.getQueryCache().getAll()).toHaveLength(0);
    });
  });
});
