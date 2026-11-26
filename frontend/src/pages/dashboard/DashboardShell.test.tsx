import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import DashboardShell from './DashboardShell';

// The two tabs are stood in for: what is tested here is which one is shown, not what is in them (DashboardPage.test.tsx and CompaniesTab.test.tsx do that).
vi.mock('./DashboardPage', () => ({ default: () => <p>the overview</p> }));
vi.mock('./companies/CompaniesTab', () => ({ CompaniesTab: () => <p>the companies</p> }));

function Where() {
  return <p data-testid="where">{useLocation().search}</p>;
}

function renderShell(entry = '/') {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <DashboardShell />
      <Where />
    </MemoryRouter>
  );
}

describe('the home page of staff: two tabs', () => {
  it('opens on the overview, which is the page it has always been', () => {
    renderShell();

    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('the overview')).toBeInTheDocument();
    expect(screen.queryByText('the companies')).not.toBeInTheDocument();
  });

  it('switches to the companies and back, and keeps the choice in the address so that a reload and "back" land on the same tab', async () => {
    const user = userEvent.setup();
    renderShell();

    await user.click(screen.getByRole('tab', { name: 'Companies' }));
    expect(await screen.findByText('the companies')).toBeInTheDocument();
    expect(screen.getByTestId('where')).toHaveTextContent('?tab=companies');

    await user.click(screen.getByRole('tab', { name: 'Overview' }));
    expect(screen.getByText('the overview')).toBeInTheDocument();
    expect(screen.getByTestId('where')).toHaveTextContent('');
  });

  it('opens on the companies when the address says so', async () => {
    renderShell('/?tab=companies');

    expect(await screen.findByText('the companies')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Companies' })).toHaveAttribute('aria-selected', 'true');
  });

  it('treats an address with any other tab as the overview', () => {
    renderShell('/?tab=something');

    expect(screen.getByText('the overview')).toBeInTheDocument();
  });
});
