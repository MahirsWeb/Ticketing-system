import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import EmailsPage from './EmailsPage';
import { emailIntegrationApi } from '../../api/emailIntegration';
import { createPkce } from '../../lib/oauth';
import { useAuthStore } from '../../store/authStore';
import type { UserSummary } from '../../types';

vi.mock('../../api/emailIntegration', () => ({
  emailIntegrationApi: { getStatus: vi.fn(), getConfig: vi.fn(), listMessages: vi.fn() },
}));
vi.mock('../../lib/oauth', async (importOriginal) => ({ ...(await importOriginal<typeof import('../../lib/oauth')>()), createPkce: vi.fn() }));

const employee = { id: 'u1', firstName: 'Amra', lastName: 'H', role: 'Employee', departmentId: 'branch-1' } as UserSummary;

function renderPage() {
  return render(
    <MemoryRouter>
      <EmailsPage />
    </MemoryRouter>
  );
}

const connectButton = () => screen.findByRole('button', { name: /Connect branch mailbox/ });

// NOVO-FE-08 / NOVO-FE-09: starting the connection to Microsoft.
describe('connecting the branch mailbox', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 't', user: employee });
    vi.mocked(emailIntegrationApi.getStatus).mockReset().mockResolvedValue({ connected: false, connectedEmail: null });
    vi.mocked(emailIntegrationApi.getConfig).mockReset().mockResolvedValue({ clientId: 'client-id' });
    vi.mocked(createPkce).mockReset();
  });

  it('is refused, with a reason, where the browser cannot make the PKCE secret (a page that is not https)', async () => {
    vi.mocked(createPkce).mockResolvedValue(null);
    renderPage();

    await userEvent.click(await connectButton());

    expect(await screen.findByText(/secure connection \(https\)/)).toBeInTheDocument();
    expect(await connectButton()).toBeEnabled(); // and the button is usable again
  });

  it('puts the button back and says so when Microsoft\'s settings cannot be fetched', async () => {
    vi.mocked(emailIntegrationApi.getConfig).mockRejectedValue(new Error('network'));
    renderPage();

    await userEvent.click(await connectButton());

    expect(await screen.findByText(/could not be started/)).toBeInTheDocument();
    expect(await connectButton()).toBeEnabled();
  });
});
