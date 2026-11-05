import { installFakeHttp, refuse } from './fakeHttp';

/**
 * A stand-in for the part of the server that the accounts page talks to, kept in memory and answering at the level of HTTP. It has the
 * rules the page has to live with: an e-mail address can only belong to one account, a client needs a company, an employee a branch, and
 * the list is a page at a time, with the totals of clients and staff that do not depend on what is searched for.
 *
 * The test file must mock `../api/client` for this to take effect.
 */

export type FakeRole = 'Client' | 'Employee' | 'Admin';

export interface FakeAccount {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  role: FakeRole;
  companyId: string | null;
  companyName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  subBranchId: string | null;
  subBranchName: string | null;
  isActive: boolean;
}

export interface UsersState {
  accounts: FakeAccount[];
  companies: { id: string; name: string; isActive: boolean }[];
  departments: { id: string; name: string; email: string; isActive: boolean }[];
  subBranches: { id: string; departmentId: string; name: string; isActive: boolean }[];
}

export function account(id: string, first: string, last: string, role: FakeRole, extra: Partial<FakeAccount> = {}): FakeAccount {
  return {
    id, firstName: first, lastName: last, email: `${first}.${last}@firma.test`.toLowerCase(), role,
    companyId: null, companyName: null, departmentId: null, departmentName: null, subBranchId: null, subBranchName: null, isActive: true, ...extra,
  };
}

export const client = (id: string, first: string, last: string, company: [string, string] = ['c1', 'Acme'], extra: Partial<FakeAccount> = {}) =>
  account(id, first, last, 'Client', { companyId: company[0], companyName: company[1], ...extra });

export function newUsersState(): UsersState {
  return {
    accounts: [
      client('k1', 'Mia', 'Marić'),
      client('k2', 'Nedim', 'Nuhić', ['c2', 'Beta d.o.o.']),
      client('k3', 'Olja', 'Ostojić', ['c1', 'Acme'], { isActive: false }),
      account('e1', 'Amra', 'Ahmetović', 'Employee', { departmentId: 'd1', departmentName: 'Sarajevo' }),
      account('a1', 'Admin', 'Tester', 'Admin'),
    ],
    companies: [
      { id: 'c1', name: 'Acme', isActive: true },
      { id: 'c2', name: 'Beta d.o.o.', isActive: true },
    ],
    departments: [
      { id: 'd1', name: 'Sarajevo', email: 'sarajevo@firma.test', isActive: true },
      { id: 'd2', name: 'Mostar', email: 'mostar@firma.test', isActive: true },
    ],
    subBranches: [{ id: 's1', departmentId: 'd2', name: 'Centar', isActive: true }],
  };
}

/** Many clients, for a list of several pages. */
export function manyClients(count: number): FakeAccount[] {
  return Array.from({ length: count }, (_, i) => client(`m${i + 1}`, `Klijent${String(i + 1).padStart(3, '0')}`, 'Test'));
}

const asListItem = (a: FakeAccount) => ({
  ...a, phoneNumber: null, emailConfirmed: true, createdAtUtc: '2026-01-05T10:00:00Z', profilePictureUrl: null, lastLoginAtUtc: null,
});

export function installUsersBackend(state: UsersState = newUsersState()) {
  let next = 100;

  function created(a: FakeAccount) {
    return { userId: a.id, email: a.email, temporaryPassword: `Tmp#${next}`, temporaryPasswordExpiresAtUtc: new Date(Date.now() + 15 * 60 * 1000).toISOString() };
  }
  function addAccount(body: any, role: FakeRole, extra: Partial<FakeAccount>) {
    if (state.accounts.some((a) => a.email.toLowerCase() === String(body.email).toLowerCase())) refuse(400, { message: 'An account with that email already exists.' });
    const a = account(`n${next++}`, body.firstName, body.lastName, role, { email: body.email, ...extra });
    state.accounts.push(a);
    return created(a);
  }

  function handle(method: string, url: string, body: any, params: any): unknown {
    if (method === 'GET' && url === '/api/users/paged') {
      const staff = params.group === 'staff';
      const inGroup = (a: FakeAccount) => (staff ? a.role !== 'Client' : a.role === 'Client');
      const text = String(params.search ?? '').toLowerCase();
      const matches = state.accounts.filter(
        (a) => inGroup(a) && (!text || `${a.firstName} ${a.lastName}`.toLowerCase().includes(text) || (a.companyName ?? '').toLowerCase().includes(text) || a.email.toLowerCase() === text)
      );
      const size = params.pageSize ?? 25;
      return {
        items: matches.slice((params.page - 1) * size, params.page * size).map(asListItem),
        page: params.page, pageSize: size, totalCount: matches.length,
        clientCount: state.accounts.filter((a) => a.role === 'Client').length,
        staffCount: state.accounts.filter((a) => a.role !== 'Client').length,
      };
    }
    if (method === 'POST' && url === '/api/users/clients') {
      const company = state.companies.find((c) => c.id === body.companyId) ?? refuse(400, { message: 'That company does not exist.' });
      return addAccount(body, 'Client', { companyId: company.id, companyName: company.name });
    }
    if (method === 'POST' && url === '/api/users/employees') {
      if (body.role === 'Employee') {
        const department = state.departments.find((d) => d.id === body.departmentId) ?? refuse(400, { message: 'An employee needs a branch.' });
        const sub = state.subBranches.find((s) => s.id === body.subBranchId);
        return addAccount(body, 'Employee', { departmentId: department.id, departmentName: department.name, subBranchId: sub?.id ?? null, subBranchName: sub?.name ?? null });
      }
      return addAccount(body, 'Admin', {});
    }
    if (method === 'GET' && url === '/api/companies') return state.companies.filter((c) => c.isActive);
    if (method === 'GET' && url === '/api/departments') return state.departments.filter((d) => d.isActive);
    const sub = url.match(/^\/api\/departments\/([^/]+)\/sub-branches$/);
    if (method === 'GET' && sub) return state.subBranches.filter((s) => s.departmentId === sub[1] && s.isActive);
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  }

  return { state, ...installFakeHttp(handle) };
}
