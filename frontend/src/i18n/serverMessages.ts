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
};

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
