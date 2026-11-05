import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import EmailCallbackPage from './EmailCallbackPage';
import { installEmailBackend, newEmailState, type EmailState } from '../../test/fakeEmailApi';

vi.mock('../../api/client', () => ({ apiClient: { get: vi.fn(), post: vi.fn() } }));

let server: ReturnType<typeof installEmailBackend>;

function Where() {
  const location = useLocation();
  return <div data-testid="where">{location.pathname}</div>;
}
function Go({ to }: { to: string }) {
  const navigate = useNavigate();
  return <button onClick={() => navigate(to)}>go to {to}</button>;
}

const disconnected = (): EmailState => ({ ...newEmailState(), connected: false, connectedEmail: null });

function renderPage(
  search: string,
  { strict = false, state = disconnected(), before }: { strict?: boolean; state?: EmailState; before?: (server: ReturnType<typeof installEmailBackend>) => void } = {}
) {
  server = installEmailBackend(state);
  before?.(server);
  const page = (
    <MemoryRouter initialEntries={[`/emails/callback${search}`]}>
      <Where />
      <Go to="/somewhere/else" />
      <Routes>
        <Route path="/emails/callback" element={<EmailCallbackPage />} />
        <Route path="/emails" element={<p>the emails page</p>} />
        <Route path="*" element={<p>some other page</p>} />
      </Routes>
    </MemoryRouter>
  );
  return render(strict ? <StrictMode>{page}</StrictMode> : page);
}

const connectRequests = () => server.sent('POST', /email-integration\/connect$/);
const where = () => screen.getByTestId('where').textContent;
const spent = () => [sessionStorage.getItem('ms_oauth_state'), sessionStorage.getItem('ms_oauth_verifier')];

describe('coming back from Microsoft', () => {
  beforeEach(() => {
    sessionStorage.clear();
    sessionStorage.setItem('ms_oauth_state', 'the-state');
    sessionStorage.setItem('ms_oauth_verifier', 'the-verifier');
  });
  afterEach(() => vi.useRealTimers());

  describe('when everything is in order', () => {
    it('hands the code to the server with the secret that was kept, says which mailbox is connected, and spends the one-time values', async () => {
      renderPage('?code=the-code&state=the-state');

      expect(await screen.findByText('Connected: sarajevo@firma.test')).toBeInTheDocument();
      expect(connectRequests().map((r) => r.body)).toEqual([{ code: 'the-code', redirectUri: `${window.location.origin}/emails/callback`, codeVerifier: 'the-verifier' }]);
      expect(spent()).toEqual([null, null]);
      expect(screen.queryByRole('link', { name: 'Back to Emails' })).not.toBeInTheDocument();
    });

    it('goes on to the emails after a moment, so that the person has time to read that it worked', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      renderPage('?code=the-code&state=the-state');
      await screen.findByText('Connected: sarajevo@firma.test');
      expect(where()).toBe('/emails/callback');

      await act(async () => {
        await vi.advanceTimersByTimeAsync(1400);
      });
      expect(where()).toBe('/emails/callback');
      await act(async () => {
        await vi.advanceTimersByTimeAsync(200);
      });

      expect(where()).toBe('/emails');
    });

    it('sends no secret when none was kept', async () => {
      sessionStorage.removeItem('ms_oauth_verifier');
      renderPage('?code=the-code&state=the-state');

      await screen.findByText('Connected: sarajevo@firma.test');
      expect(connectRequests()[0].body).toEqual({ code: 'the-code', redirectUri: `${window.location.origin}/emails/callback` });
    });

    it('says that it is connecting while the server is being told, and has no link back yet', async () => {
      const held = { release: () => {} };
      renderPage('?code=the-code&state=the-state', { before: (s) => Object.assign(held, s.hold('POST', /email-integration\/connect$/)) });

      expect(screen.getByRole('heading', { name: 'Connecting Outlook…' })).toBeInTheDocument();
      expect(screen.queryByText(/Connected:/)).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Back to Emails' })).not.toBeInTheDocument();
      held.release();
      await screen.findByText(/Connected:/);
    });
  });

  describe('when it must not go on', () => {
    it.each([
      ['Microsoft says why in words', '?error=access_denied&error_description=The user declined', 'The user declined'],
      ['Microsoft only gives a code for the error', '?error=access_denied', 'access_denied'],
    ])('says what Microsoft said when %s, and asks nothing of the server', async (_name, search, shown) => {
      renderPage(search);

      expect(await screen.findByText(shown)).toBeInTheDocument();
      expect(connectRequests()).toHaveLength(0);
      expect(screen.getByRole('link', { name: 'Back to Emails' })).toHaveAttribute('href', '/emails');
      expect(spent()).toEqual([null, null]);
    });

    it('says that there was no code when none came back', async () => {
      renderPage('?state=the-state');

      expect(await screen.findByText('No authorization code was returned by Microsoft.')).toBeInTheDocument();
      expect(connectRequests()).toHaveLength(0);
    });

    it('fails when the state that came back is not the one that was sent, and spends what was kept', async () => {
      renderPage('?code=the-code&state=another-state');

      expect(await screen.findByText('Security check failed (state mismatch). Please try connecting again.')).toBeInTheDocument();
      expect(connectRequests()).toHaveLength(0);
      expect(spent()).toEqual([null, null]);
    });

    it('fails when nothing was kept to compare with (the page opened directly, or from a link that somebody else made)', async () => {
      sessionStorage.clear();
      renderPage('?code=the-code&state=anything');

      expect(await screen.findByText('Security check failed (state mismatch). Please try connecting again.')).toBeInTheDocument();
      expect(connectRequests()).toHaveLength(0);
    });

    it('fails when there is neither a state that came back nor one that was kept, which a careless comparison would take for the same', async () => {
      sessionStorage.clear();
      renderPage('?code=the-code');

      expect(await screen.findByText(/Security check failed/)).toBeInTheDocument();
      expect(connectRequests()).toHaveLength(0);
    });

    it('fails when the page was opened with no state at all', async () => {
      renderPage('?code=the-code');

      expect(await screen.findByText(/Security check failed/)).toBeInTheDocument();
      expect(connectRequests()).toHaveLength(0);
    });

    it('says what the server said when it refuses the code, and offers the way back', async () => {
      renderPage('?code=the-code&state=the-state', { before: (s) => s.refuse('POST', /email-integration\/connect$/, 400, { message: 'The code has expired.' }) });

      expect(await screen.findByText('The code has expired.')).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Back to Emails' })).toHaveAttribute('href', '/emails');
    });

    it('says plainly that it did not work when the server said nothing', async () => {
      renderPage('?code=the-code&state=the-state', { before: (s) => s.refuse('POST', /email-integration\/connect$/, 500, {}) });

      expect(await screen.findByText('Could not connect your mailbox.')).toBeInTheDocument();
    });
  });

  describe('what it used to get wrong', () => {
    it('asks the server once and ends up connected, without a moment of "Security check failed", where the effect is run twice (the application is started in strict mode)', async () => {
      const seen: string[] = [];
      const watch = new MutationObserver(() => {
        if (document.body.textContent?.includes('Security check failed')) seen.push('failed');
      });
      watch.observe(document.body, { childList: true, subtree: true, characterData: true });
      renderPage('?code=the-code&state=the-state', { strict: true });

      expect(await screen.findByText('Connected: sarajevo@firma.test')).toBeInTheDocument();
      watch.disconnect();
      expect(connectRequests()).toHaveLength(1);
      expect(seen).toEqual([]);
    });

    it('does not take the person away from the page they went to while it waited, when the moment is over', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderPage('?code=the-code&state=the-state');
      await screen.findByText('Connected: sarajevo@firma.test');

      await user.click(screen.getByRole('button', { name: 'go to /somewhere/else' }));
      await act(async () => {
        await vi.advanceTimersByTimeAsync(3000);
      });

      expect(where()).toBe('/somewhere/else');
    });

    it('says what it is waiting for to a screen reader, and says an error as an alert and the success as a status', async () => {
      const first = renderPage('?code=the-code&state=the-state');
      expect(screen.getByRole('status', { name: 'Connecting the mailbox' })).toBeInTheDocument();
      await screen.findByText('Connected: sarajevo@firma.test');
      expect(screen.getByRole('status')).toHaveTextContent('Connected: sarajevo@firma.test');
      first.unmount();

      renderPage('?state=the-state');
      expect(await screen.findByRole('alert')).toHaveTextContent('No authorization code was returned by Microsoft.');
    });

  });
});
