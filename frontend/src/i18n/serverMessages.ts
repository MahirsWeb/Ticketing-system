import type { Language } from './translations';

/// The server answers in English, whatever the language of the screen. Rather than make every screen know that, the words the server is known
/// to use are translated in one place, as the answer arrives (see the response interceptor in api/client.ts). A message that is not listed here
/// is shown as it came: English, which is still understandable, and never a blank. A test (serverMessages.test.ts) reads the messages out of the
/// server's source and fails when one of them has no translation, so a new message cannot be forgotten.

const BS: Record<string, string> = {
  // ---- signing in and passwords
  'Invalid email or password. After several failed attempts an account is locked for a few minutes.':
    'Pogrešan email ili lozinka. Nakon nekoliko neuspjelih pokušaja nalog se zaključava na nekoliko minuta.',
  'Too many sign-in attempts for this account. Try again in a minute.': 'Previše pokušaja prijave za ovaj nalog. Pokušajte ponovo za minut.',
  'Your temporary password has expired. Ask an admin or consultant to generate a new one.': 'Vaša privremena lozinka je istekla. Zamolite administratora ili konsultanta da napravi novu.',
  'The temporary password is not valid or has expired. Ask an admin or consultant to generate a new one.': 'Privremena lozinka nije ispravna ili je istekla. Zamolite administratora ili konsultanta da napravi novu.',
  'Please verify your email address before logging in.': 'Potvrdite svoju email adresu prije prijave.',
  'The session is no longer valid. Please sign in again.': 'Sesija više nije važeća. Prijavite se ponovo.',
  'Your session is no longer valid — please log in again.': 'Vaša sesija više nije važeća — prijavite se ponovo.',
  'Request from an unexpected origin.': 'Zahtjev s neočekivanog izvora.',
  'New password and confirmation do not match.': 'Nova lozinka i potvrda se ne podudaraju.',
  'Password set. Please check your email to verify your account before logging in.': 'Lozinka je postavljena. Provjerite email da potvrdite nalog prije prijave.',
  'Password set successfully. You can now log in.': 'Lozinka je uspješno postavljena. Sada se možete prijaviti.',
  'Password reset successfully. You can now log in.': 'Lozinka je uspješno resetovana. Sada se možete prijaviti.',
  'If that email exists in our system, a reset link has been sent.': 'Ako ta email adresa postoji u našem sistemu, poslan je link za resetovanje.',
  'Email verified. You can now log in.': 'Email je potvrđen. Sada se možete prijaviti.',
  'Invalid verification link.': 'Neispravan link za potvrdu.',
  'This verification link is invalid or has expired.': 'Ovaj link za potvrdu nije ispravan ili je istekao.',
  'Invalid or expired reset link.': 'Neispravan ili istekao link za resetovanje.',
  'Could not reset password.': 'Nije bilo moguće resetovati lozinku.',
  'Phone number saved.': 'Broj telefona je sačuvan.',
  // Identity's own password rules
  'Passwords must be at least 10 characters.': 'Lozinka mora imati najmanje 10 znakova.',
  "Passwords must have at least one lowercase ('a'-'z').": "Lozinka mora imati najmanje jedno malo slovo ('a'-'z').",
  "Passwords must have at least one digit ('0'-'9').": "Lozinka mora imati najmanje jednu cifru ('0'-'9').",
  "Passwords must have at least one uppercase ('A'-'Z').": "Lozinka mora imati najmanje jedno veliko slovo ('A'-'Z').",
  'Passwords must have at least one non alphanumeric character.': 'Lozinka mora imati najmanje jedan poseban znak.',
  'Incorrect password.': 'Pogrešna lozinka.',

  // ---- accounts
  'A user with this email already exists.': 'Korisnik s ovom email adresom već postoji.',
  'Email is required.': 'Email je obavezan.',
  'You cannot deactivate your own account.': 'Ne možete deaktivirati vlastiti nalog.',
  'You cannot change your own account type.': 'Ne možete promijeniti vrstu vlastitog naloga.',
  'You cannot anonymize your own account.': 'Ne možete anonimizirati vlastiti nalog.',
  'Use POST /api/users/clients to create client accounts.': 'Za pravljenje klijentskih naloga koristite POST /api/users/clients.',
  'A company must be selected for Client accounts.': 'Za klijentske naloge mora se izabrati firma.',
  'A branch must be selected for Employee accounts.': 'Za naloge zaposlenika mora se izabrati poslovnica.',
  'Company not found.': 'Firma nije pronađena.',
  'Branch not found.': 'Poslovnica nije pronađena.',
  'Your account is not linked to a company.': 'Vaš nalog nije povezan s firmom.',
  'This client is not linked to a company.': 'Ovaj klijent nije povezan s firmom.',
  'No client account found with that email.': 'Nije pronađen klijentski nalog s tom email adresom.',
  'A company with that name already exists.': 'Firma s tim nazivom već postoji.',
  'This branch has no sub-branches.': 'Ova poslovnica nema podposlovnica.',
  'A sub-branch must be selected for this branch.': 'Za ovu poslovnicu mora se izabrati podposlovnica.',
  'Sub-branch not found for this branch.': 'Podposlovnica nije pronađena za ovu poslovnicu.',

  // ---- branches
  'A branch with this email already exists.': 'Poslovnica s ovom email adresom već postoji.',
  'This branch already has a sub-branch with that name.': 'Ova poslovnica već ima podposlovnicu s tim nazivom.',
  'This branch still has a connected mailbox. Disconnect it first.': 'Ova poslovnica još ima povezano poštansko sanduče. Prvo ga odspojite.',
  'Destination branch not found.': 'Odredišna poslovnica nije pronađena.',
  'Please select a valid branch.': 'Izaberite ispravnu poslovnicu.',

  // ---- tickets
  'Please describe the problem.': 'Opišite problem.',
  'The message is empty.': 'Poruka je prazna.',
  'System events are recorded automatically and cannot be posted directly.': 'Sistemski događaji se bilježe automatski i ne mogu se slati izravno.',
  'This ticket is closed. Reopen it to add a message.': 'Ovaj tiket je zatvoren. Ponovo ga otvorite da dodate poruku.',
  'This exact message was just sent. If you meant to send it again, wait a few seconds.': 'Ista poruka je upravo poslana. Ako ste je htjeli poslati ponovo, pričekajte nekoliko sekundi.',
  'Message does not belong to this ticket.': 'Poruka ne pripada ovom tiketu.',
  'Ticket is already closed.': 'Tiket je već zatvoren.',
  'Ticket must be opened before it can be closed.': 'Tiket mora biti otvoren prije nego što se zatvori.',
  'Ticket is already in this branch.': 'Tiket je već u ovoj poslovnici.',
  'Open the ticket before transferring it to a branch.': 'Otvorite tiket prije prebacivanja u poslovnicu.',
  'A closed ticket cannot be transferred. Reopen it first.': 'Zatvoren tiket se ne može prebaciti. Prvo ga ponovo otvorite.',
  'Only new tickets can be opened.': 'Mogu se otvoriti samo novi tiketi.',
  'Only closed tickets can be reopened.': 'Mogu se ponovo otvoriti samo zatvoreni tiketi.',
  'A resolution summary is required to close a ticket.': 'Za zatvaranje tiketa obavezan je sažetak rješenja.',
  'You can only open tickets into your own branch.': 'Tikete možete otvarati samo u vlastitu poslovnicu.',
  'This person is already assigned to the ticket.': 'Ova osoba je već zadužena za tiket.',
  'The ticket must be open to change its assignees.': 'Tiket mora biti otvoren da bi se mijenjali zaduženi.',
  'Somebody else changed the assignees of this ticket a moment ago. Reload it and try again.': 'Neko drugi je maloprije promijenio zadužene na ovom tiketu. Ponovo ga učitajte i pokušajte opet.',
  'This e-mail already became another ticket.': 'Ovaj email je već postao drugi tiket.',
  'Assign the ticket to somebody before logging work time on it.': 'Dodijelite tiket nekome prije nego što bilježite radno vrijeme na njemu.',
  'A work session needs a start time.': 'Radna sesija treba vrijeme početka.',
  'No files were provided.': 'Nijedan fajl nije priložen.',
  'No file was provided.': 'Fajl nije priložen.',
  'Image exceeds the 5 MB limit.': 'Slika prelazi ograničenje od 5 MB.',
  'None of the files could be indexed (unsupported type, unreadable, or too large).': 'Nijedan fajl nije bilo moguće indeksirati (nepodržana vrsta, nečitljiv ili prevelik).',

  // ---- tasks
  'Assignee must be a support agent or consultant.': 'Zaduženi mora biti agent podrške ili konsultant.',
  "That colleague's account is inactive, so a task cannot be assigned to them.": 'Nalog tog kolege je neaktivan, pa mu se zadatak ne može dodijeliti.',
  'Assignee is not linked to a branch.': 'Zaduženi nije povezan s poslovnicom.',
  'You can only assign tasks to colleagues in your own branch.': 'Zadatke možete dodjeljivati samo kolegama iz vlastite poslovnice.',
  'You can only reassign tasks to colleagues in the same branch.': 'Zadatke možete prosljeđivati samo kolegama iz iste poslovnice.',
  'Timer is already running.': 'Mjerač vremena već radi.',
  'Timer is not currently running.': 'Mjerač vremena trenutno ne radi.',

  // ---- email integration, AI, reports
  'This branch already has a mailbox connected. Disconnect it first if you want to connect a different one.': 'Ova poslovnica već ima povezano poštansko sanduče. Prvo ga odspojite ako želite povezati drugo.',
  'You are not assigned to a branch yet. Ask an admin to assign you to one.': 'Još niste dodijeljeni nijednoj poslovnici. Zamolite administratora da vas dodijeli.',
  'No mailbox is connected for your branch.': 'Za vašu poslovnicu nije povezano poštansko sanduče.',
  'No mailbox is connected for your branch. Connect one in the email settings first.': 'Za vašu poslovnicu nije povezano poštansko sanduče. Prvo ga povežite u postavkama emaila.',
  'A reindex is already running.': 'Ponovno indeksiranje je već u toku.',
  'Select a period of at least one month for AI analysis.': 'Izaberite period od najmanje mjesec dana za AI analizu.',
  'The start date must not be after the end date.': 'Početni datum ne smije biti poslije krajnjeg.',

  // ---- everything else
  'This was changed by someone else while you were working on it. Reload the page and try again.': 'Neko drugi je ovo promijenio dok ste radili na tome. Ponovo učitajte stranicu i pokušajte opet.',
  'This was already done by someone else (or by an earlier identical request). Reload the page and check.': 'Neko drugi je ovo već uradio (ili raniji identičan zahtjev). Ponovo učitajte stranicu i provjerite.',
  'Something went wrong on our side.': 'Nešto je pošlo po zlu s naše strane.',
  'The request is not valid.': 'Zahtjev nije ispravan.',
  'Invalid value.': 'Neispravna vrijednost.',

  // ---- work sessions
  'The end of a work session must be after its start.': 'Kraj radne sesije mora biti poslije njenog početka.',
  'A work session cannot start in the future.': 'Radna sesija ne može početi u budućnosti.',
  'A work session cannot end in the future.': 'Radna sesija ne može završiti u budućnosti.',
  'A work session is still running on another ticket or task. End it first.': 'Radna sesija još traje na drugom tiketu ili zadatku. Prvo je završite.',
  'This overlaps your work on another ticket or task.': 'Ovo se preklapa s vašim radom na drugom tiketu ili zadatku.',

  // ---- Microsoft 365
  'The saved mailbox connection can no longer be read. Disconnect the mailbox and connect it again.': 'Sačuvana veza s poštanskim sandučetom više se ne može pročitati. Odspojite sanduče i povežite ga ponovo.',
  'Microsoft did not accept the sign-in (it may have expired or already been used). Please start connecting the mailbox again.': 'Microsoft nije prihvatio prijavu (možda je istekla ili je već iskorištena). Ponovo započnite povezivanje sandučeta.',
  'Microsoft 365 could not be reached right now. Please try again in a minute.': 'Microsoft 365 trenutno nije dostupan. Pokušajte ponovo za minut.',
  'Microsoft 365 is busy right now. Please try again in a minute.': 'Microsoft 365 je trenutno zauzet. Pokušajte ponovo za minut.',
  'The mailbox connection has expired or was revoked. Disconnect the mailbox and connect it again.': 'Veza sa sandučetom je istekla ili je opozvana. Odspojite sanduče i povežite ga ponovo.',
  'Microsoft 365 did not accept the request. If this keeps happening, reconnect the mailbox.': 'Microsoft 365 nije prihvatio zahtjev. Ako se ovo ponavlja, ponovo povežite sanduče.',

  // ---- the knowledge base and the AI
  'Looking for tickets that need an embedding…': 'Traže se tiketi kojima treba indeksiranje…',
  'Reading the stored document…': 'Čitanje sačuvanog dokumenta…',
  'Embedding the document…': 'Indeksiranje dokumenta…',
  'Embedding tickets…': 'Indeksiranje tiketa…',
  'Stopped because the application is shutting down. Run it again to continue.': 'Zaustavljeno jer se aplikacija gasi. Pokrenite ponovo da nastavite.',
  'Check the AI settings (Ai:Provider, Ai:ApiKey / Ai:Endpoint, Ai:Enabled) and that the quota is not exhausted, then run the reindex again — it continues where it stopped.': 'Provjerite AI postavke (Ai:Provider, Ai:ApiKey / Ai:Endpoint, Ai:Enabled) i da kvota nije iscrpljena, pa ponovo pokrenite indeksiranje — nastavlja gdje je stalo.',
  'No text could be extracted from the stored file.': 'Iz sačuvanog fajla nije bilo moguće izvući tekst.',
  'Nothing relevant was found in the knowledge base yet for this question.': 'U bazi znanja još nema ničeg relevantnog za ovo pitanje.',
  'The AI did not answer this time — showing the closest matching sources instead.': 'AI ovaj put nije odgovorio — prikazani su najbliži izvori.',
  'AI is switched off or not configured on this installation — showing the closest matching sources instead.': 'AI je isključen ili nije podešen na ovoj instalaciji — prikazani su najbliži izvori.',
  'No tickets found in this period to analyze.': 'U ovom periodu nema tiketa za analizu.',
  'The AI did not answer this time. Please try again.': 'AI ovaj put nije odgovorio. Pokušajte ponovo.',
  'AI is switched off or not configured on this installation.': 'AI je isključen ili nije podešen na ovoj instalaciji.',
  'Žao nam je, nemamo dovoljno informacija u sistemu za vaš problem. Molimo kontaktirajte vašeg administratora.': 'Žao nam je, nemamo dovoljno informacija u sistemu za vaš problem. Molimo kontaktirajte vašeg administratora.',

  // ---- accounts and mail
  'The database check failed.': 'Provjera baze nije uspjela.',
};

const ROLE_BS: Record<string, string> = { Client: 'Klijent', Employee: 'Zaposlenik', Admin: 'Administrator' };
const STATUS_BS: Record<string, string> = { New: 'novi', Open: 'otvoreni', InProgress: 'u toku', Resolved: 'riješeni', Closed: 'zatvoreni' };
const status = (s: string) => STATUS_BS[s] ?? s;

const PATTERNS: [RegExp, (m: RegExpMatchArray) => string][] = [
  [/^File type '(.+)' is not allowed\.$/, (m) => `Vrsta fajla '${m[1]}' nije dozvoljena.`],
  [/^File '(.+)' exceeds the 10 MB limit\.$/, (m) => `Fajl '${m[1]}' prelazi ograničenje od 10 MB.`],
  [/^This branch still owns (\d+) ticket\(s\)\. Transfer them to another branch first\.$/, (m) => `Ova poslovnica još ima tiketa: ${m[1]}. Prvo ih prebacite u drugu poslovnicu.`],
  [/^This branch still has (\d+) task\(s\) assigned to it\. Reassign or complete them first\.$/, (m) => `Ovoj poslovnici je još dodijeljeno zadataka: ${m[1]}. Prvo ih proslijedite ili završite.`],
  [/^This branch still has (\d+) sub-branch\(es\)\. Remove them first\.$/, (m) => `Ova poslovnica još ima podposlovnica: ${m[1]}. Prvo ih uklonite.`],
  [/^This branch still has (\d+) staff member\(s\)\. Move or remove them first\.$/, (m) => `Ova poslovnica još ima osoblja: ${m[1]}. Prvo ih premjestite ili uklonite.`],
  [/^The ticket is already (\w+)\.$/, (m) => `Tiket je već u stanju: ${status(m[1])}.`],
  [/^A (\w+) ticket cannot be moved to (\w+)\.$/, (m) => `Tiket u stanju "${status(m[1])}" se ne može prebaciti u stanje "${status(m[2])}".`],
  [/^A (\w+) ticket cannot be closed\.$/, (m) => `Tiket u stanju "${status(m[1])}" se ne može zatvoriti.`],
  [/^A ticket cannot be moved to (\w+) this way\.$/, (m) => `Tiket se na ovaj način ne može prebaciti u stanje "${status(m[1])}".`],
  [/^The text is too long \(at most (\d+) characters\)\.$/, (m) => `Tekst je predug (najviše ${m[1]} znakova).`],
  [/^The search text is too long \(at most (\d+) characters\)\.$/, (m) => `Tekst pretrage je predug (najviše ${m[1]} znakova).`],
  [/^This (help topic|category|SLA plan) is used by (\d+) ticket\(s\) and cannot be deleted, because those tickets would lose it from their history\. Deactivate it instead: it then disappears from the pickers, and everything that already uses it stays as it is\.$/,
    (m) => {
      const noun = { 'help topic': 'Ova tema pomoći', category: 'Ova kategorija', 'SLA plan': 'Ovaj SLA plan' }[m[1] as 'help topic' | 'category' | 'SLA plan'];
      return `${noun} se koristi na tiketima: ${m[2]}, pa se ne može obrisati jer bi ti tiketi izgubili tu vrijednost iz svoje historije. Umjesto toga je deaktivirajte: tada nestaje s izbora, a sve što je već koristi ostaje kakvo jeste.`;
    }],
  [/^A work session cannot last longer than (\d+) hours\. Split it into several sessions\.$/, (m) => `Radna sesija ne može trajati duže od ${m[1]} sati. Podijelite je na više sesija.`],
  [/^Another work session on this (\w+) is still running\. End it first\.$/, (m) => `Druga radna sesija na ${m[1] === 'ticket' ? 'ovom tiketu' : m[1] === 'task' ? 'ovom zadatku' : 'ovoj stavci'} još traje. Prvo je završite.`],
  [/^This overlaps another work session on the same (\w+)\.$/, (m) => `Ovo se preklapa s drugom radnom sesijom na ${m[1] === 'ticket' ? 'istom tiketu' : m[1] === 'task' ? 'istom zadatku' : 'istoj stavci'}.`],
  [/^Reindex failed: (.*)$/, (m) => `Indeksiranje nije uspjelo: ${m[1]}`],
  [/^Stopped after (\d+) failures in a row: the embedding provider returned nothing\. ?(.*)$/, (m) => `Zaustavljeno poslije ${m[1]} uzastopnih neuspjeha: davalac indeksiranja nije vratio ništa. ${translateServerMessage(m[2], 'bs')}`.trim()],
  [/^Done \((\d+) document\(s\) rebuilt for the current model\)\.$/, (m) => `Gotovo (dokumenata ponovo izgrađeno za trenutni model: ${m[1]}).`],
  [/^Done, but (\d+) item\(s\) could not be embedded\. Run it again to retry them\.$/, (m) => `Gotovo, ali stavki koje nije bilo moguće indeksirati: ${m[1]}. Pokrenite ponovo da ih pokušate opet.`],
  // ---- lines of the notification list (kept as text in the database)
  [/^New ticket #(\S+): (.*)$/s, (m) => `Novi tiket #${m[1]}: ${m[2]}`],
  [/^Ticket #(\S+) assigned to you$/, (m) => `Tiket #${m[1]} vam je dodijeljen`],
  [/^Your ticket #(\S+) has been opened$/, (m) => `Vaš tiket #${m[1]} je otvoren`],
  [/^Ticket #(\S+) transferred to (.+)$/, (m) => `Tiket #${m[1]} prebačen u: ${m[2]}`],
  [/^Ticket #(\S+) has been resolved$/, (m) => `Tiket #${m[1]} je riješen`],
  [/^New reply on ticket #(\S+)$/, (m) => `Novi odgovor na tiketu #${m[1]}`],
  [/^Ticket #(\S+) is overdue$/, (m) => `Tiket #${m[1]} je prekoračio rok`],
  [/^(.+) assigned you a task: "(.*)"$/s, (m) => `Novi zadatak: "${m[2]}" (dodijelio/la: ${m[1]})`],
  [/^(.+) successfully logged in via invite — role: (Client|Employee|Admin)\.$/, (m) => `Pozivnica prihvaćena: ${m[1]} — uloga: ${ROLE_BS[m[2]] ?? m[2]}.`],
  [/^User name '(.+)' is already taken\.$/, (m) => `Korisničko ime '${m[1]}' je već zauzeto.`],
  [/^Email '(.+)' is already taken\.$/, (m) => `Email '${m[1]}' je već zauzet.`],
  [/^Passwords must use at least (\d+) different characters\.$/, (m) => `Lozinka mora koristiti najmanje ${m[1]} različitih znakova.`],
  [/^Passwords must be at least (\d+) characters\.$/, (m) => `Lozinka mora imati najmanje ${m[1]} znakova.`],
  [/^The (\w+) field is required\.$/, (m) => `Polje "${m[1]}" je obavezno.`],
  [/^Something went wrong on our side\. Please try again; if it keeps happening, report this reference: (.*)$/, (m) => `Nešto je pošlo po zlu s naše strane. Pokušajte ponovo; ako se nastavi, prijavite ovu referencu: ${m[1]}`],
];

/** The words of the server in the given language. English (or a message nobody has translated) comes back as it was. */
export function translateServerMessage(message: string, language: Language): string {
  if (language !== 'bs') return message;
  const exact = BS[message];
  if (exact) return exact;
  for (const [pattern, build] of PATTERNS) {
    const match = message.match(pattern);
    if (match) return build(match);
  }
  return message;
}

/** Whether a message has a Bosnian rendering (used by the test that keeps the list complete). */
export function hasServerTranslation(message: string): boolean {
  return translateServerMessage(message, 'bs') !== message;
}

const STATUS_WORD_BS: Record<string, string> = { New: 'Novo', Open: 'Otvoren', InProgress: 'U toku', Resolved: 'Riješen', Closed: 'Zatvoren' };
const REASON_BS: Record<string, string> = {
  'account deactivated': 'nalog deaktiviran',
  'moved to a different role or branch': 'premješten u drugu ulogu ili poslovnicu',
  'account anonymized': 'nalog anonimiziran',
};

/// The lines the server writes into a ticket's feed about what happened to it ("Ticket closed by Ana Anić."). They are kept as HTML text in the
/// database, in English, and put into the language of the screen when they are shown. The names inside stay as they are.
const EVENTS: [RegExp, (m: RegExpMatchArray) => string][] = [
  [/^<p>(.+) added as an assignee by (.+)\.<\/p>$/s, (m) => `<p>Zaduženje dodano: ${m[1]} (dodao/la: ${m[2]}).</p>`],
  [/^<p>Ticket opened by (.+) and assigned to (.+)\.<\/p>$/s, (m) => `<p>Tiket je otvoren (${m[1]}) i dodijeljen: ${m[2]}.</p>`],
  [/^<p>Ticket transferred from <b>(.*)<\/b> to <b>(.*)<\/b> by (.+)\.<\/p>$/s, (m) => `<p>Tiket je prebačen iz <b>${m[1] === '(unrouted)' ? '(bez poslovnice)' : m[1]}</b> u <b>${m[2]}</b> (${m[3]}).</p>`],
  [/^<p>Ticket closed by (.+)\.<\/p>$/s, (m) => `<p>Tiket je zatvoren (${m[1]}).</p>`],
  [/^<p>Ticket reopened by (.+)\.<\/p>$/s, (m) => `<p>Tiket je ponovo otvoren (${m[1]}).</p>`],
  [/^<p>Status changed from <b>(\w+)<\/b> to <b>(\w+)<\/b> by (.+)\.<\/p>$/s, (m) => `<p>Stanje promijenjeno iz <b>${STATUS_WORD_BS[m[1]] ?? m[1]}</b> u <b>${STATUS_WORD_BS[m[2]] ?? m[2]}</b> (${m[3]}).</p>`],
  [/^<p>(.+) was unassigned from this ticket \((.+?)\)(; it currently has no assignee)?\.<\/p>$/s, (m) =>
    `<p>Zaduženje skinuto: ${m[1]} (${REASON_BS[m[2]] ?? m[2]})${m[3] ? '; tiket trenutno nema zaduženog' : ''}.</p>`],
];

export function translateEventHtml(html: string, language: Language): string {
  if (language !== 'bs') return html;
  for (const [pattern, build] of EVENTS) {
    const match = html.match(pattern);
    if (match) return build(match);
  }
  return html;
}

export function hasEventTranslation(html: string): boolean {
  return translateEventHtml(html, 'bs') !== html;
}
