import { installFakeHttp, refuse } from './fakeHttp';

/**
 * A stand-in for the part of the server that the company pages talk to, kept in memory and answering at the level of HTTP. It has the rules
 * the pages have to live with: a company name can only be used once, a company can be switched off (and then only the administrator's list has
 * it), and a list of tickets is cut to the page size with the total that there is.
 *
 * The test file must mock `../api/client` for this to take effect (`get`, `post` and `put`).
 */

export interface FakeCompany {
  id: string;
  name: string;
  address: string | null;
  contactInfo: string | null;
  isActive: boolean;
}

export interface FakeClient {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string | null;
  createdAtUtc: string;
  isActive: boolean;
  companyId: string;
}

export interface FakeTicket {
  id: string;
  ticketNumber: string;
  title: string;
  status: string;
  priority: string | null;
  companyId: string;
  clientId: string;
  clientName: string;
  assignedToName: string | null;
  createdAt: string;
}

export interface CompaniesState {
  companies: FakeCompany[];
  clients: FakeClient[];
  tickets: FakeTicket[];
}

export const company = (id: string, name: string, extra: Partial<FakeCompany> = {}): FakeCompany => ({
  id, name, address: null, contactInfo: null, isActive: true, ...extra,
});

export const clientOf = (id: string, first: string, last: string, extra: Partial<FakeClient> = {}): FakeClient => ({
  id, firstName: first, lastName: last, email: `${first}.${last}@firma.test`.toLowerCase(), phoneNumber: null,
  createdAtUtc: '2026-03-10T12:00:00.000Z', isActive: true, companyId: 'c1', ...extra,
});

export const ticketOf = (id: string, number: string, title: string, extra: Partial<FakeTicket> = {}): FakeTicket => ({
  id, ticketNumber: number, title, status: 'Open', priority: 'Medium', companyId: 'c1', clientId: 'k1', clientName: 'Mia Marić',
  assignedToName: null, createdAt: new Date(2026, 2, 10, 9, 30).toISOString(), ...extra,
});

export function newCompaniesState(): CompaniesState {
  return {
    companies: [
      company('c1', 'Acme d.o.o.', { address: 'Zmaja od Bosne 1', contactInfo: '033 123 456' }),
      company('c2', 'Beta'),
      company('c3', 'Stara firma', { isActive: false }),
    ],
    clients: [
      clientOf('k1', 'Mia', 'Marić', { phoneNumber: '061 111 222' }),
      clientOf('k2', 'Nedim', 'Nuhić'),
      clientOf('k3', 'Olja', 'Ostojić', { isActive: false }),
      clientOf('k4', 'Pero', 'Perić', { companyId: 'c2' }),
    ],
    tickets: [
      ticketOf('t1', '100001', 'Printer ne radi', { assignedToName: 'Amra Ahmetović', priority: 'High', status: 'InProgress' }),
      ticketOf('t2', '100002', 'Monitor treperi', { clientId: 'k2', clientName: 'Nedim Nuhić' }),
      ticketOf('t3', '100003', 'Tuđa firma', { companyId: 'c2', clientId: 'k4', clientName: 'Pero Perić' }),
    ],
  };
}

export function installCompaniesBackend(state: CompaniesState = newCompaniesState()) {
  let next = 100;

  const find = (id: string) => state.companies.find((c) => c.id === id) ?? refuse(404, { message: 'Company not found.' });
  const taken = (name: string, except?: string) => state.companies.some((c) => c.id !== except && c.name.toLowerCase() === String(name).toLowerCase());

  function handle(method: string, url: string, body: any, params: any): unknown {
    if (method === 'GET' && url === '/api/companies') return state.companies.filter((c) => params?.includeInactive || c.isActive);
    if (method === 'POST' && url === '/api/companies') {
      if (taken(body.name)) refuse(400, { message: 'A company with that name already exists.' });
      const created = company(`n${next++}`, body.name, { address: body.address ?? null, contactInfo: body.contactInfo ?? null });
      state.companies.push(created);
      return { id: created.id, name: created.name };
    }
    const one = url.match(/^\/api\/companies\/([^/]+)$/);
    if (one) {
      const c = find(one[1]);
      if (method === 'GET') return c;
      if (method === 'PUT') {
        if (taken(body.name, c.id)) refuse(400, { message: 'A company with that name already exists.' });
        Object.assign(c, { name: body.name, address: body.address ?? null, contactInfo: body.contactInfo ?? null, isActive: body.isActive });
        return c;
      }
    }
    if (method === 'GET' && url === '/api/users') {
      return state.clients.filter((c) => (!params?.companyId || c.companyId === params.companyId)).map((c) => ({
        ...c, role: 'Client', companyName: state.companies.find((x) => x.id === c.companyId)?.name ?? null, emailConfirmed: true,
        departmentId: null, departmentName: null, subBranchId: null, subBranchName: null, profilePictureUrl: null, lastLoginAtUtc: null,
      }));
    }
    if (method === 'GET' && url === '/api/tickets') {
      const matching = state.tickets.filter((t) => (!params?.companyId || t.companyId === params.companyId) && (!params?.clientId || t.clientId === params.clientId));
      const size = params?.pageSize ?? 20;
      return { items: matching.slice(0, size), totalCount: matching.length, page: 1, pageSize: size };
    }
    return refuse(404, { message: `The stand-in server has no ${method} ${url}.` });
  }

  return { state, ...installFakeHttp(handle) };
}
