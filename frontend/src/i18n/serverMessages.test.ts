import { describe, expect, it } from 'vitest';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { hasServerTranslation, translateServerMessage } from './serverMessages';

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
