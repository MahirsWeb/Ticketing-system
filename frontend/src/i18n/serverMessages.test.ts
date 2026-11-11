import { describe, expect, it } from 'vitest';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { hasEventTranslation, hasServerTranslation, translateEventHtml, translateServerMessage } from './serverMessages';

// The server's source, if it is next to the frontend (it always is in this repository; a copy of the frontend alone skips the check).
const BACKEND = resolve(__dirname, '../../../backend/src/TicketSystemTech.Api');

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (name === 'obj' || name === 'bin' || name === 'Migrations') return [];
    return statSync(path).isDirectory() ? sources(path) : path.endsWith('.cs') ? [path] : [];
  });
}

// A C# string literal, plain or interpolated: group 1 is "$" for an interpolated one, group 2 is what is between the quotes.
const LITERAL = String.raw`(\$?)"((?:[^"\\\n]|\\.)*)"`;

/** The sentences the server puts in its answers, as it writes them: a literal given to a refusal, to "message =", or to the exception middleware. */
function serverSentences(): string[] {
  const found = new Set<string>();
  const refusals = new RegExp(String.raw`\.(?:Invalid|Conflict|NotFound|Forbidden)\(\s*${LITERAL}`, 'g');
  const messages = new RegExp(String.raw`message = ${LITERAL}`, 'g');
  const apiErrors = new RegExp(String.raw`ApiError\(\s*${LITERAL}`, 'g');
  const constants = new RegExp(String.raw`const string (?:InvalidCredentials|TempPasswordRejected|RefreshRejected|NameTakenMessage|NoBranch)\s*=\s*${LITERAL}`, 'g');
  const returned = new RegExp(String.raw`return [^;"]*${LITERAL}[^;]*;\s*$`, 'gm');

  for (const file of sources(BACKEND)) {
    const text = readFileSync(file, 'utf8');
    const patterns = [refusals, messages, apiErrors, constants];
    if (/BranchRules/.test(file)) patterns.push(returned);
    for (const pattern of patterns) {
      for (const match of text.matchAll(pattern)) {
        let sentence = match[2].replace(/\\"/g, '"');
        if (match[1] === '$') sentence = sentence.replace(/\{[^}]*\}/g, '3');
        if (/^[A-Z]/.test(sentence) && /[ .]/.test(sentence) && sentence.length > 8) found.add(sentence);
      }
    }
  }
  return [...found];
}

describe('the words of the server', () => {
  it.skipIf(!existsSync(BACKEND))('every sentence the server can answer with has a Bosnian rendering', () => {
    const missing = serverSentences().filter((sentence) => !hasServerTranslation(sentence));
    expect(missing).toEqual([]);
  });

  it.skipIf(!existsSync(BACKEND))('really finds the sentences (the check above is not passing because it found nothing)', () => {
    expect(serverSentences().length).toBeGreaterThan(60);
  });

  it('is left alone in English', () => {
    expect(translateServerMessage('Company not found.', 'en')).toBe('Company not found.');
  });

  it('turns a known message into Bosnian, and a message nobody has translated stays as it came', () => {
    expect(translateServerMessage('Company not found.', 'bs')).toBe('Firma nije pronađena.');
    expect(translateServerMessage('A sentence that no one wrote a translation for.', 'bs')).toBe('A sentence that no one wrote a translation for.');
  });

  it('fills in the numbers and names of the messages that have them', () => {
    expect(translateServerMessage("File type '.exe' is not allowed.", 'bs')).toBe("Vrsta fajla '.exe' nije dozvoljena.");
    expect(translateServerMessage('This branch still owns 4 ticket(s). Transfer them to another branch first.', 'bs')).toContain('4');
    expect(translateServerMessage('A Closed ticket cannot be moved to Open.', 'bs')).toContain('zatvoreni');
  });

  it('knows the answer to a wrong password, which is the one that people see most', () => {
    expect(translateServerMessage('Invalid email or password. After several failed attempts an account is locked for a few minutes.', 'bs')).toMatch(/^Pogrešan email ili lozinka/);
  });
});

// ---- the sentences that reach the screen by another road than a refusal: a variable, an exception, a status line, a line stored in the database

const SERVER = resolve(__dirname, '../../../backend/src');

/** Files whose sentences are shown to a person (rules for work sessions, the Microsoft 365 connection, the state of a reindex, the answers of the AI). */
const FILES_WITH_SHOWN_SENTENCES = [
  'TicketSystemTech.Application/WorkTime/WorkSessionRules.cs',
  'TicketSystemTech.Infrastructure/Services/GraphEmailIntegrationService.cs',
  'TicketSystemTech.Api/BackgroundJobs/KnowledgeBaseReindexService.cs',
  'TicketSystemTech.Api/KnowledgeBase/KnowledgeBaseSearch.cs',
  'TicketSystemTech.Api/KnowledgeBase/KnowledgeBaseDocumentIndexer.cs',
  'TicketSystemTech.Api/Reports/ReportService.cs',
  'TicketSystemTech.Api/Mailbox/MailboxService.cs',
];

function shownSentences(): string[] {
  const found = new Set<string>();
  const literal = new RegExp(LITERAL, 'g');
  for (const file of FILES_WITH_SHOWN_SENTENCES) {
    const path = join(SERVER, file);
    if (!existsSync(path)) continue;
    for (const line of readFileSync(path, 'utf8').split('\n')) {
      if (/Log(ger)?\.|_logger|\.Log[A-Z]\w+\(|nameof|ILogger|^\s*\/\//.test(line)) continue; // what goes to the log is for the operator, not for the screen
      for (const match of line.matchAll(literal)) {
        let sentence = match[2].replace(/\\"/g, '"');
        if (match[1] === '$') sentence = sentence.replace(/\{[^}]*\}/g, '3');
        const words = sentence.trim().split(/\s+/).length;
        // A sentence: capital letter, several words, ends like one; not a prompt for the model (those are in KnowledgeBaseSearch/ReportService and are long).
        if (/^[A-Z]/.test(sentence) && words >= 4 && /[.…]\s?$/.test(sentence) && sentence.length < 260 && !/^You (are|may)|^Based ONLY|^Tickets that might|^Which problems/.test(sentence)) found.add(sentence);
      }
    }
  }
  return [...found];
}

describe('the other sentences of the server', () => {
  it.skipIf(!existsSync(SERVER))('(work session rules, Microsoft 365, reindex, AI answers) all have a Bosnian rendering', () => {
    const missing = shownSentences().filter((sentence) => !hasServerTranslation(sentence));
    expect(missing).toEqual([]);
  });

  it.skipIf(!existsSync(SERVER))('are really found', () => {
    expect(shownSentences().length).toBeGreaterThan(25);
  });
});

describe('what the server writes into the feed of a ticket and into the list of notifications', () => {
  const EVENTS = [
    '<p>Ana Anić added as an assignee by Bojan B.</p>',
    '<p>Ticket opened by Bojan B and assigned to Ana Anić.</p>',
    '<p>Ticket transferred from <b>Podrška Sarajevo</b> to <b>Podrška Mostar</b> by Bojan B.</p>',
    '<p>Ticket transferred from <b>(unrouted)</b> to <b>Mostar</b> by Bojan B.</p>',
    '<p>Ticket closed by Bojan B.</p>',
    '<p>Ticket reopened by Bojan B.</p>',
    '<p>Status changed from <b>Open</b> to <b>InProgress</b> by Bojan B.</p>',
    '<p>Ana Anić was unassigned from this ticket (account deactivated).</p>',
    '<p>Ana Anić was unassigned from this ticket (moved to a different role or branch); it currently has no assignee.</p>',
  ];
  const LINES = [
    'New ticket #100123: Printer ne radi',
    'Ticket #100123 assigned to you',
    'Your ticket #100123 has been opened',
    'Ticket #100123 transferred to Mostar',
    'Ticket #100123 has been resolved',
    'New reply on ticket #100123',
    'Ticket #100123 is overdue',
    'Ana Anić assigned you a task: "Provjeri ugovor"',
    'Ivana Gavrić successfully logged in via invite — role: Client.',
  ];

  it('is turned into Bosnian, and the names inside stay as they are', () => {
    for (const html of EVENTS) expect(hasEventTranslation(html), html).toBe(true);
    for (const line of LINES) expect(hasServerTranslation(line), line).toBe(true);
    expect(translateEventHtml('<p>Ticket closed by Bojan B.</p>', 'bs')).toBe('<p>Tiket je zatvoren (Bojan B).</p>');
    expect(translateEventHtml('<p>Ticket closed by Bojan B.</p>', 'en')).toBe('<p>Ticket closed by Bojan B.</p>');
    expect(translateServerMessage('Ivana Gavrić successfully logged in via invite — role: Client.', 'bs')).toBe('Pozivnica prihvaćena: Ivana Gavrić — uloga: Klijent.');
  });

  it.skipIf(!existsSync(SERVER))('is still worded the way the server writes it (so that a change of wording here is noticed)', () => {
    const read = (file: string) => readFileSync(join(SERVER, file), 'utf8');
    const lifecycle = read('TicketSystemTech.Api/Tickets/TicketLifecycleService.cs') + read('TicketSystemTech.Api/Tickets/TicketCreationService.cs') + read('TicketSystemTech.Api/Tickets/TicketAssigneeService.cs') + read('TicketSystemTech.Infrastructure/Services/TicketAssignmentService.cs');
    for (const wording of ['added as an assignee by', 'Ticket opened by', 'Ticket transferred from <b>', 'Ticket closed by', 'Ticket reopened by', 'Status changed from <b>', 'was unassigned from this ticket', 'it currently has no assignee']) {
      expect(lifecycle, wording).toContain(wording);
    }
    const notifier = read('TicketSystemTech.Api/Notifications/Notifier.cs') + read('TicketSystemTech.Api/Auth/AuthService.cs');
    for (const wording of ['New ticket #{e.Ticket.TicketNumber}: {e.Ticket.Title}', 'assigned to you', 'has been opened', 'transferred to {e.ToBranch}', 'has been resolved', 'New reply on ticket #', 'is overdue', 'assigned you a task', 'successfully logged in via invite — role:']) {
      expect(notifier, wording).toContain(wording);
    }
  });

  it('is shown in Bosnian where it is shown (an interceptor cannot reach the lines that are stored in the database)', () => {
    const feed = readFileSync(resolve(__dirname, '../pages/tickets/TicketActivityFeed.tsx'), 'utf8');
    const bell = readFileSync(resolve(__dirname, '../components/NotificationBell.tsx'), 'utf8');
    expect(feed).toContain('translateEventHtml(');
    expect(bell).toContain('translateServerMessage(');
  });
});
