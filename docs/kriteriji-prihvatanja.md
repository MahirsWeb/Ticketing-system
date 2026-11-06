# Kriteriji prihvatanja

Ovaj dokument kaže **šta znači „prošlo“**, šta je u drugom krugu testiranja (tester, mirror commit `7af778a`, 149 scenarija) dokazano
i čime, šta nije moguće dokazati na ovom računaru i šta je svjesno ostalo otvoreno. Cilj je da se pri sljedećem krugu ponovo
provjerava **samo razlika**, a ne cijeli skup.

Stanje opisano ovdje: poslije šestog kruga (2026-11-05), još neobjavljeno u trenutku pisanja. Najnoviji brojevi: 718 backend i 859 frontend testova.

## 1. Statusi

| Status | Znači |
|---|---|
| **Dokazano** | Postoji automatski test koji pokreće isti zahtjev ili isti upit kao tester, i **pada kad se zaštita pokvari** (provjereno namjernim kvarenjem koda, odjeljak 6). Ili postoji ponovljiv postupak iz odjeljka 7 s očekivanim izlazom. |
| **Dokazano u CI-ju** | Provjerava ga posao na GitHub Actions koji se ne može pokrenuti na ovom računaru (Docker, Linux). Vrijedi tek kad je posao zelen. |
| **Nije dokazano ovdje** | Popravka postoji, ali okruženje u kojem se mjeri (pravi SMTP server, pravi Google/Ollama, pravi reverse proxy, SQL Server 2017) nije bilo dostupno. |
| **Prihvaćeno uz obrazloženje** | Vlasnik je odlučio da se ne radi, ili je izvan obima verzije 1. Svaka takva stavka ima razlog i uslov pod kojim se otvara ponovo (odjeljak 4). |
| **Otvoreno** | Nije rađeno. Nabrojano u odjeljku 5. |

## 2. Dokazano ovim krugom

Svaki red je jedan scenario testera. „Test“ je klasa u `backend/tests/TicketSystemTech.Tests` ili fajl u `frontend/src`.

| Scenario | Šta je tester našao | Šta je urađeno | Test |
|---|---|---|---|
| TS-03.1, TS-ARC-01.1 | `X-Forwarded-For` određuje ko je ko; iza proxyja svi dijele jedno ograničenje | Zaglavlja se ignorišu dok proxy nije naveden (`ForwardedHeaders:*`); ograničenje prijave **po nalogu** (30 u minuti, 429 + `Retry-After`) | `OperationsHardeningTests`, `OperationsIntegrationTests` |
| TS-04.2, TS-HOST-02.1 | Key ring u bazi u čistom tekstu, uz podatke koje štiti | Šifrovanje certifikatom (PFX ili Windows store); `Production` ne kreće bez njega osim uz izričit izuzetak | `OperationsHardeningTests`, `OperationsIntegrationTests` (sa certifikatom nema `<masterKey`, bez njega ga ima), `StartupConfigurationTests` |
| TS-ARC-02.1 | Closed → Open ostavlja `ClosedAtUtc`, `ClosedByUserId`, `OverdueNotifiedAtUtc` | Svaki izlaz iz Closed prolazi kroz jedno mjesto koje to briše | `RetestDefectTests` |
| TS-ARC-03.1 | Ponovno slanje poruke pravi duplikat | `Idempotency-Key` na poruci; drugi odgovor je prvi; tuđi ključ ne briše tuđu poruku; neispravan ključ se odbija | `RetestDefectTests`, `TicketActivityFeed.test.tsx` (isti ključ pri ponovnom pokušaju, drugi pri izmijenjenom tekstu) |
| TS-35.1 | Dvostruko otvaranje istog New tiketa | Utrka na jedinstvenom indeksu daje `409 DUPLICATE`, ne `500` | `RetestDefectTests` |
| TS-32.3 | Obrnut raspon u izvještajima | `400` kad je početak poslije kraja | `RetestDefectTests` |
| TS-09.3, TS-33.3 | Dan na vremenskoj liniji je UTC dan; otvoren tiket je na svakom danu | Dan se računa u poslovnoj zoni (Europe/Sarajevo), budući dani su prazni, vrijeme bez evidencije je označeno kao *proteklo*, ne kao rad | `BusinessDayTests`, `WorkTaskSessionTests`, `GanttChart.test.tsx` |
| TS-33.1 | Ponovno pokretanje zadatka prepisuje raniju sesiju | Zadatak ima više sesija (`WorkTaskSessions`), migracija prenosi stare redove i vraća se unazad; ispravka mijenja posljednju sesiju | `WorkTaskSessionTests`, `WorkTaskSessionsMigrationTests`, `TaskDetailPanel.test.tsx` |
| porodica TS-35 (zadaci) | Dvije osobe snime isti zadatak, jedna tiho gubi | `RowVersion` na zadatku, `409` | `WorkTaskSessionTests` |
| TS-10.2 | `AuditLogs` prazna nakon osjetljivih radnji | Kreiranje, izmjena i deaktivacija naloga; otvaranje, zatvaranje, ponovno otvaranje i prenos tiketa; izvršioci; poslovnice. Svaki red nosi adresu i oznaku zahtjeva; nikad lozinku ni tekst poruke | `AuditAndBackgroundWorkTests` |
| TS-HOST-03.1 | Zatvaranje tiketa čeka AI davaoca u istom zahtjevu | Odgovor odmah (i pri dodavanju bilješke); indeksiranje iz reda, jednom po tiketu | `AuditAndBackgroundWorkTests` |
| TS-16.1 (dio o ugovoru za greške) | Greške oko mailboxa nemaju jedinstven ugovor | Svaka akcija koja traži mailbox odgovara isto (`409 MAILBOX_NOT_CONNECTED`), prevod na jednom mjestu | `AuditAndBackgroundWorkTests` |
| TS-DEP-04.3 | `reindex-all` preskače tiket sa vektorom druge dimenzije | Reindeks zaboravlja neusklađene vektore i pravi nove, i bez dostupnog davaoca | `RetestDefectTests`, `AiSettingsTests` |
| TS-OPS-03.1 | Pretraga po ključnim riječima kad nema embedding ključa | Radi u SQL-u (najbolji pogodak prvi, najviše traženi broj); odgovor kaže je li AI mogao odgovoriti (`aiAvailable`) | `RetestDefectTests` |
| TS-DEP-03.3 | Bez Googlea nema nikakvog AI-ja i korisnik ne zna zašto | Davalac je podešavanje (`Ai:*`), ekran kaže „AI nedostupan“ | `AiSettingsTests`, `ClientAiHelpPanel.test.tsx` |
| TS-ONP-03.1 | Adresa slike iz `Host` zaglavlja, upisana trajno | Putanje u bazi su relativne, adresa se dodaje pri čitanju; stare se jednom prepišu; `AllowedHosts` odbija tuđe ime (`400`) | `RetestDefectTests` |
| TS-ONP-05.1 | Log samo na konzoli | Log u dnevni fajl s rotacijom i ograničenjem; u Dockeru `/data/logs` | `OperationsHardeningTests` |
| TS-OPS-02.1 | Lozinka admina u logu | Nikad u log; terminal samo uz `SeedAdmin:PrintGeneratedPassword`; nova lozinka komandom `--reset-admin-password` (samo Admin nalozi, upisuje audit bez lozinke) | `OperationsIntegrationTests` |
| TS-50.1 | Docker bez `USER`, plutajući tagovi, bez `HEALTHCHECK` | Korisnik koji nije root, digesti, `HEALTHCHECK` koji pita aplikaciju (i prolazi pored `AllowedHosts`) | `OperationsHardeningTests` (sonda); **Dokazano u CI-ju** za samu sliku |
| TS-53.2 | Netačnosti README-a | Ispravljeno: folder, putanje, Brevo/Render/Vercel, reset lozinke, SMTP, AI, produkcija | pregled dokumenta |
| TS-17.2 | Pretraga liste prikazuje stari upit | Jedan zahtjev po zastoju u kucanju; odgovor starijeg upita se odbacuje; isto za KPI i Gantt | `TicketsListPage.test.tsx` |
| TS-18.5 | SignalR odustaje nakon 42 s | Beskonačno ponovno povezivanje s rastućim razmakom i razmicanjem; osvježavanje nakon povratka | `notificationsHub.test.ts`, `small-lib.test.ts` |
| TS-18.6 | Istek sesije briše nacrt odgovora | Nacrt ostaje u kartici, vraća se nakon prijave; prijava kaže zašto je korisnik opet tu | `LoginPage.test.tsx`, `TicketActivityFeed.test.tsx`, `small-lib.test.ts` |
| TS-19.1 | Polja bez programske labele | `Field` povezuje labelu i polje; izbor veličine fonta i skriveni birač priloga u uređivaču imaju `aria-label`. Provjereno i u pregledniku (upit iz scenarija vraća prazan niz) | `ui.test.tsx`, `RichTextEditor.test.tsx` |
| TS-46.5 | Kontrole bez pristupačnog naziva | Imenovana dugmad (zvonce, strelice kalendara), emoji sakriven od čitača, fokus i kontrast | `NotificationBell.test.tsx`, `DateRangePicker.test.tsx`, `ui.test.tsx` |
| T-46 (naslov, `lang`) | Naslov stranice i `lang` | Naslov po ruti, `<html lang>` prati izabrani jezik | `DocumentMeta.test.tsx` |
| TS-ONP-02.3 | Isti key ring, drugi `Pii:HashKey`: podaci se čitaju, niko se ne može prijaviti | Pri startu se uz svaki šifrovani email provjerava i sačuvani hash; ne poklapa li se, start se odbija uz jasnu poruku (isprobano i uživo na instanci) | `RetestDefectTests` (`Starting_over_existing_accounts_with_a_different_Pii_HashKey_is_refused`), mutacije 3 od 3 |
| TS-05.4 | Prilog se može okačiti na poruku tiketa druge poslovnice | `messageId` mora pripadati tiketu (`400`) | `BranchIsolationTests` (`A_file_cannot_be_hung_on_a_message_of_another_ticket`), mutacije 2 od 2 |
| TS-15.3 | Pravilo dodjele zaduženog ispisano tri puta | Jedno pravilo (`TicketAssignment`), tri poziva | `TicketAssignmentTests`, `RetestDefectTests`, mutacije 5 od 5 |
| DOM-05 (nađeno ponovnim izvođenjem) | Poslovnica se mogla obrisati dok ima osoblje i tikete | Brisanje odbijeno; strani ključevi `Restrict` | `BranchDeletionTests` (3) |
| TS-19.4 | Meni "Tiketi" i redovi zadataka samo mišem | Disclosure dugme s `aria-expanded` i pravim linkovima; zaglavlja su dugmad s `aria-sort` | `AppLayout.test.tsx`, mutacije 4 od 4 |
| TS-59.2 | NuGet: ranjivi, zastarjeli i *Legacy* paketi | 0 ranjivih, 0 zastarjelih, 0 deprecated; `xunit.v3.mtp-off`; verzije u `Directory.Packages.props` | CI (`dotnet list package`) i postupak iz odjeljka 7 |
| TS-ONP-04.3 | Express bez backupa i upozorenja na 10 GB | `backend/ops/backup-database.ps1` | postupak iz odjeljka 7 (isprobano na probnoj bazi) |
| TS-15.2 | Sistemski sat na 53 mjesta | Sve kroz `TimeProvider`; entiteti bez sata | `ClockTests` (uključujući test koji pada ako se sistemski sat vrati u izvorni kod), mutacije 6 od 6 |
| TS-34.2 | Dva izvora zaduženja koja se razilaze | Jedan izvor: lista izvršilaca s oznakom `IsPrimary`, jedinstven indeks, migracija s prenosom podataka | `AssigneeListTests`, `TicketPrimaryAssigneeMigrationTests`, mutacije 8 od 8 |
| TS-10.3 | Imena osoblja kopirana u tekst događaja | Anonimizacija ih uklanja iz generisanog teksta, samo cijela imena | `PersonalDataTests`, mutacije 5 od 5 |
| TS-11.2 | Baza znanja čuva email, telefon i lozinke kako su ukucani | Maskiranje pri upisu, jednokratno maskiranje starih chunkova | `KnowledgeBaseRedactionTests`, mutacije 5 od 5 |
| TS-31.3 | Dva istovremena osvježavanja Graph tokena | Jedan zahtjev po mailboxu osvježava | `GraphTokenRefreshTests`, mutacije 4 od 4 |
| NOVO-ARC-04 | 429 od davaoca otvara prekidač kruga | Prekidač ne broji 429 | `OutboundHttpTests` (pada bez popravke) |
| NOVO-DOM-06, 07, 08, PRV-02, SEC-05, FE-05 | Pravila o tiketima, prilozima, rokovima, sesijama i praznim porukama | Vidi `IZVJESTAJ_ISPRAVKI.md` 9.11 | `TicketRulesTests`, `WorkSessionTests`, `LegacyUploadsTests`, `HtmlTextTests`, `OverdueNotifierTests` |
| NOVO-FE-01, 03, 05, 07, 08, 09, 10, 11, DOM-02 (forma) | Frontend: utrke, zastarjeli odgovori, ponovljeno slanje priloga, zumiranje, povezivanje mailboxa | Vidi 9.11 | `TicketDetailPage.test`, `OpenTicketForm.test`, `EmailsPage.test`, `NotificationBell.test`, `TransferBranchForm.test`, `chartRange.test`, `finishNewTicket.test`, `htmlText.test` (svi padaju bez popravke) |

Slanje email-ova (SMTP): `SmtpEmailTransportTests` (stvaran MailKit prema malom SMTP serveru na `localhost`), `EmailOutboxTests`,
`EmailRetryPolicyTests`. Format koda: provjerava CI (`dotnet format --verify-no-changes`).

## 3. Nije dokazano ovdje

| Šta | Zašto | Gdje se dokazuje |
|---|---|---|
| Docker slika (gradnja, korisnik koji nije root, `HEALTHCHECK`, odbijanje pokretanja s razvojnim postavkama) | Na ovom računaru nije bio pokrenut Docker engine | CI posao „Docker image“. Dok ne bude zelen jednom, ne blokira merge. |
| Slanje preko pravog SMTP servera | Nema servera; probano je protiv lokalnog lažnog servera pravim MailKit-om | Ručno: `smtp4dev`, vidi README |
| Pravi Google i Ollama | Nema ključa/servera u ovom krugu | Ručno: `Ai:Provider`, vidi README |
| Pravi reverse proxy | Nema ga | Ručno: `ForwardedHeaders:KnownProxies`; test pokazuje oba smjera |
| SQL Server 2017 | Odlučeno da je minimum 2019 (ADR 0001, D4); CI koristi 2022 | — |
| Čitač ekrana, kontrast mjeren alatom | Popravljeno i lintovano, nije slušano | Ručno |
| Čišćenje starih backupa skriptom, pravi mailbox s HTML-om (TS-01.3) | Folder instance nije čitljiv računu kojim je skripta isprobana; nema Graph integracije | Ručno |
| Opterećenje | Mjereni su pojedinačni zahtjevi | — |

## 4. Prihvaćeno uz obrazloženje (predlog; čeka potvrdu vlasnika)

| Stavka | Obrazloženje | Kad se otvara ponovo |
|---|---|---|
| MFA (D9) | Vlasnik je 2026-10-19 odlučio da ne treba. Prvobitni ADR je tražio suprotno. | Ako kupac to zahtijeva. |
| Multi-tenant (D2, O6) | v1 je instanca po kupcu. | Prije v2; prije većih promjena modela podataka. |
| IIS paket i `docker-compose` (D3) | Nijedan kupac sa IIS-om ne postoji. Aplikacija je običan ASP.NET Core host. | Kad kupac sa IIS-om postoji. |
| GitLab/Gitea (D10, O4) | Izbor nije donesen; CI je GitHub Actions. | Kad se izbor donese. |
| IMAP (D7) | Graph pokriva postojeće potrebe. | Kad kupac nema Microsoft 365. |
| OpenTelemetry (D13) | Korelacioni ID, file log i health provjere pokrivaju potrebe jedne instance. | Kad postoji centralni monitoring. |
| SQL Server 2017 (D4) | Izvan Microsoftove glavne podrške; ne može se testirati. | — |
| Period čuvanja i automatsko brisanje (ostatak T-10), licenca (T-51, T-52) | Traži pravnu stranu. Izvoz i anonimizacija na zahtjev postoje. | Prije prvog kupca. |
| JWT u `localStorage` (T-18.1), React Query (T-17) | Veći preustroj, nije povezan s nalazima testera. | Prije v2. |

## 5. Otvoreno (nije rađeno)

Navodim samo ono što sam provjerio u kodu:

- **CSP za SPA** (NOVO-SEC-03): repozitorij ga više ne postavlja jer nema `vercel.json`; mora ga postaviti web server koji služi
  `frontend/dist` (README, „Frontend na serveru“).
- **Poslovna logika u kontrolerima** (T-15, TS-15.1) i 53 direktna `DateTime.UtcNow` (TS-15.2): veliki, odvojen refaktor.
- **Povezivanje odgovora u niti i atomska konverzija emaila** (T-31, TS-31.1, TS-31.2).
- **Preostali scenariji označeni "nije ispravljeno" u `IZVJESTAJ_ISPRAVKI.md` 9.10:** TS-17.1 (React Query), TS-18.1 (JWT u `localStorage`), TS-20.2 i TS-20.3 (više instanci), TS-47.1 (notifikacije), TS-51.1 i TS-52.1 (licence), TS-OPS-04.2 i TS-12.1 (zaštita grane `main`).
- **Ostalih ~60 statičkih nalaza testera (NOVO-\*)** nisu sistematski obrađeni u ovom krugu. Tester ih je označio kao „nije ponovo
  provjereno“; spisak je u `PREGLED-SVIH-NALAZA.md`. Pri sljedećem krugu svaki se rješava istim postupkom: test koji pada, pa
  popravka, pa mutacija.

## 6. Kako je provjereno da testovi hvataju grešku

Prolazak testova sam po sebi ne dokazuje ništa. Zato je svaka od zaštita ispod namjerno pokvarena, jedna po jedna, pa je pokrenut
test koji je za nju zadužen; kod je potom vraćen i provjereno je da je identičan (kontrolni zbir). Rezultat je u izvještaju
(`IZVJESTAJ_ISPRAVKI.md`, odjeljak 9).

## 7. Ponovno mjerenje samo razlike

Na novom commitu ponovo se pokreće samo ono što je ovdje navedeno, redom:

```bash
cd backend && dotnet test -c Release                 # sve zeleno, 0 preskočenih (treba SQL Server)
cd backend && dotnet format TicketSystemTech.sln --verify-no-changes
cd backend && dotnet list package --vulnerable --include-transitive
cd frontend && npm run lint && npx tsc -b && npm test && npm run build && npm audit --audit-level=high
```

Ručno, na čistoj bazi i pokrenutoj aplikaciji (backup: `powershell -File backend/ops/backup-database.ps1 -ConnectionString "..."`, izlaz 0, a pri `-LimitGb` manjem od veličine baze izlaz 2):

| Scenario | Postupak | Očekivano |
|---|---|---|
| TS-ONP-03.1 | `curl -i -H "Host: evil.example.test" http://localhost:5114/health/live` | `400` |
| TS-03.1 | 16× `POST /api/auth/login`, svaki put s drugim `X-Forwarded-For` | 16. odgovor je `429` (ograničenje po adresi gleda stvarnu adresu, ne zaglavlje); zaseban strop po nalogu je 30 u minuti |
| TS-ONP-05.1 | pokrenuti aplikaciju; pogledati folder `logs/` pored aplikacije | postoji `ticketsystem-<datum>.log` |
| TS-OPS-02.1 | pokrenuti na praznoj bazi sa `SeedAdmin:PrintGeneratedPassword=false`; pretražiti log | upozorenje bez lozinke |
| Reset | zaustaviti aplikaciju; `dotnet run --project src/TicketSystemTech.Api -- --reset-admin-password <email>` | nova lozinka samo na terminalu; prijava traži promjenu |
| TS-50.1 | `docker build`, `docker inspect`, `docker run` (koraci posla „Docker image“) | korisnik nije root, `HEALTHCHECK` postoji, pokretanje bez konfiguracije odbijeno uz spisak razloga |
| TS-04.2 | `SELECT Xml FROM DataProtectionKeys` s certifikatom i bez | s certifikatom nema `<masterKey`, bez njega ga ima |

## 8. Ponovljeno izvođenje scenarija (2026-11-04)

Tester-ovi scenariji su izvedeni na izoliranoj kopiji (vlastita baza, objavljen API, kopije njegovih skripti); rezultati po scenariju su u `IZVJESTAJ_ISPRAVKI.md`, odjeljak 9.8. Pri tome je u pravom pregledniku nađena greška koju testovi nisu hvatali: stranica "Submit a Ticket" se rušila u razvojnom načinu (`editor.getHTML()` nad uništenim uređivačem). Popravljeno je i provjereno u pregledniku; automatski regresijski test za rušenje ne postoji jer ga jsdom ne reproducira. Pri sljedećem krugu ovaj korak (stranica `/tickets/new` u `npm run dev`) treba ponoviti ručno.

## 9. Četvrti krug (2026-11-04)

Svih 151 scenarija iz paketa testera ponovo je obrađeno; pregled po scenariju je u `IZVJESTAJ_ISPRAVKI.md`, odjeljak 9.10. Uživo ili u pregledniku izvedeni su, između ostalih, TS-14.4 (dvije instance nad praznom bazom), TS-ONP-02.3 (drugi `Pii:HashKey`), TS-17.2, 17.3, 17.4 (zastarjeli odgovori), TS-09.2 (vremena prema zoni), TS-01.1 (XSS proba) i GDPR dugmad. Pri tome su nađene i ispravljene dvije greške u aplikaciji (brisanje poslovnice sa osobljem; `Pii:HashKey` koji se tiho promijeni) i jedna rupa u testovima (prilog na poruci tuđeg tiketa), a testiranje skripte za backup je našlo dvije greške u samoj skripti. Scenariji koji nisu izvedeni uživo vezani su za kod i postojeće testove, što je slabiji dokaz i označeno je uz svaki.

## 10. Peti krug (2026-11-04)

Grupa 2 iz izvještaja o preostalom (TS-15.2, 34.2, 10.3, 11.2, 31.3, dio 02.3) i nalazi `NOVO-*` iz paketa `full-2026-10-21`. Postupak za svaku ispravku: test koji prvo pada, popravka, mutacijska provjera; pregled je u `IZVJESTAJ_ISPRAVKI.md`, odjeljak 9.11, a stanje svih 80 nalaza u `docs/OTVORENE-STAVKE.md`, odjeljak 7. Nije izvedeno: probna primjena migracije `TicketPrimaryAssigneeOnTheList` na kopiji prave baze (pokriva je test migracije s pet vrsta starih redova), pregled u pregledniku za nove forme (pokrivene Vitest testovima), pravi Graph za TS-31.3 (zamijenjen lažnim Microsoftom koji broji pozive).

## 11. Šesti krug (2026-11-05)

Mjerenje na sintetičkoj bazi od 20.000 tiketa (izvoz u Excel oko 3 s, ostalo ispod 0,1 s; ARC-07, 08, OPS-05, 06, 11 nisu pokriveni), T-40 zatvoren (svaka GET akcija prima token zahtjeva: `ReadActionsCancellationTests` i pravilo CA2016), dio T-58 (pravila CA1305, CA1068, CA1001 za `src`; naziv mjeseca u Excel izvozu više ne zavisi od jezika servera), ARC-13 (reindeks ne staje zbog pet tiketa koje davalac odbija pojedinačno; `ReindexFailureTests`), ograničenje teksta za embedding na 6.000 znakova (dio T-38) i ARC-06, HOST-04, OPS-08 pomjereni u riješene poslije čitanja koda. Pregled je u `IZVJESTAJ_ISPRAVKI.md`, odjeljak 9.12.

T-41 je zatvoren: `docs/openapi.json` čuva `ApiContractTests`, frontend generiše tipove (`npm run gen:api`) i `contract.check.ts` pri buildu provjerava ručno pisane tipove protiv servera.

T-15 (prvi komad): kontroler tiketa je sveden na 160 linija, a pravila su u servisima u `Api/Tickets/`; ponašanje je isto (ugovor API-ja i svi raniji testovi nepromijenjeni).

T-15 (drugi komad): kontroler naloga je sveden na 144 linije, a pravila su u servisima u `Api/Users/`; ponašanje je isto.

T-15 (treći komad): kontroler baze znanja je sveden na 131 liniju, a pravila su u servisima u `Api/KnowledgeBase/`; ponašanje je isto.

T-15 (četvrti komad): kontroler šifarnika je sveden na 179 linija, a pravila su u servisima u `Api/Lookups/`; ponašanje je isto.

T-15 (peti komad): kontroler zadataka je sveden na 96 linija, a pravila su u servisima u `Api/WorkTasks/`; ponašanje je isto.

T-15 (šesti komad): kontroler izvještaja je sveden na 75 linija, a računica je u `Api/Reports/ReportService.cs` uz 11 novih testova izvještaja; ponašanje je isto.

T-15 (sedmi komad): kontroler prijave je sveden na 38 linija, a pravila su u `Api/Auth/AuthService.cs` uz 9 novih testova pravila prijave i lozinki; ponašanje je isto.

T-15 je zatvoren: svi kontroleri su tanki (ukupno 1.022 linije), a pravila su u servisima; servisi koriste `AppDbContext` izravno i nisu iza interfejsa u `Application` sloju.

T-17 (prvi dio): šifarnici na frontendu dolaze iz zajedničkog sloja upita (`src/api/lookupQueries.ts`), s osvježavanjem pri izmjeni i brisanjem pri odjavi; 164 frontend testa. Podaci tiketa, izvještaja, zadataka i naloga ostaju ručni.

T-47 (prvi dio): riječi svih obavijesti nad tiketima i zadacima (lista, e-mail, push) pišu se na jednom mjestu (`Api/Notifications/Notifier.cs`); 21 novi test, uključujući deset nad pravom bazom koji provjeravaju da svaka radnja zaista nekoga obavijesti. Poruke o nalozima i sigurnosti i šabloni kao resursi nisu rađeni.

T-17 (drugi dio): spisak tiketa, detalj tiketa i brojke u meniju dolaze iz zajedničkog sloja upita (`src/api/ticketQueries.ts`) i osvježavaju se poslije svake izmjene tiketa; 189 frontend testova. Ostalo (dashboard, izvještaji, zadaci, nalozi, postavke) ostaje ručno.

T-17 (treći dio) i T-42 (prvi dio): dashboard dolazi iz zajedničkog sloja upita (`src/api/reportQueries.ts`) i razbijen je na dijelove u `pages/dashboard/` (stranica 101 linija, bila 631); 52 testa koji ranije nisu postojali. Ostale velike stranice (`SettingsPage` i dr.) ostaju.

T-17 (četvrti dio) i T-42 (drugi dio): administratorski spiskovi, postavke obavijesti i dokumentacija dolaze iz zajedničkog sloja upita, a `SettingsPage` je razbijen na dijelove u `pages/settings/` (76 linija, bila 1.057); 81 test koji ranije nisu postojali, uključujući 16 za greške koje su ranije prolazile tiho. Ostale velike stranice ostaju.

T-17 (peti dio) i T-42 (treći dio): stranica korisnika dolazi iz zajedničkog sloja upita i razbijena je na dijelove u `pages/users/` (56 linija, bila 471); 53 testa koja ranije nisu postojala, uključujući 12 za greške koje su ranije prolazile tiho (poruka "User not found." za svakog sljedećeg korisnika, svaka greška pisana kao nepostojeći korisnik, spor odgovor za prethodnog korisnika). Ostale velike stranice ostaju.

T-17 (šesti dio) i T-42 (četvrti dio): tiket u ime klijenta razbijen je na model, prikaz i traženje klijenta u `pages/tickets/` (stranica 60 linija, bila 430), a oba obrasca za tiket dijele konstante, rok, polja za izbor i učitavanje spiskova (`OpenTicketForm` 223 → 137 linija); 43 testa koja ranije nisu postojala, uključujući greške koje su ranije prolazile tiho (osoblje, e-mail i spiskovi koji se ne učitaju, naslov s razmacima). Ostale velike stranice ostaju.

T-42 (peti dio): spisak tiketa razbijen je na model filtera u adresi, čiste funkcije, filtere, tabelu i stranice u `pages/tickets/` (stranica 75 linija, bila 379); 67 testova koja ranije nisu postojala, uključujući greške koje su ranije prolazile tiho ili rušile stranicu (datum u adresi koji nije datum, stranica poslije posljednje, "mine" za one koji ga ne mogu skinuti, filter zaposlenog na kartici "New", filteri koji se ne učitaju). Ostale velike stranice ostaju.

T-17 (sedmi dio) i T-42 (šesti dio): zadaci su na zajedničkom sloju upita i razbijeni na dijelove u `pages/tasks/` (stranica 71 linija, bila 345); 92 testa koja ranije nisu postojala, na nivou HTTP-a protiv lažnog servera, uključujući greške koje su ranije prolazile tiho ili miješale odgovore (spisak koji se ne učita, kasni odgovor za prethodni zadatak ispod drugog, red koji nestaje kad se pokrene sat, neuspjelo prebacivanje koje briše izbor). Ostale velike stranice ostaju.

T-17 (osmi dio) i T-42 (sedmi dio): spisak i pravljenje naloga su na zajedničkom sloju upita i razbijeni na dijelove u `pages/users/` (stranica 26 linija, bila 329); 63 testa koja ranije nisu postojala, uključujući greške koje su ranije prolazile tiho ili miješale prikaz (stari redovi pod novom karticom, poruka o grešci uz stare redove, imena od razmaka, spiskovi koji se ne učitaju). Zajednički `Pager` i `Tabs`. Ostale velike stranice ostaju.

T-17 (deveti dio) i T-42 (osmi dio): firme (spisak i stranica jedne firme) su na zajedničkom sloju upita i razbijene na dijelove u `pages/users/` (stranica firme 24 linije, bila 323; spisak 43, bio 96); 60 testova koja ranije nisu postojala, uključujući greške koje su ranije prolazile tiho ili miješale prikaz (pitanje preko razloga odbijanja, neuspjeli tiketi i klijenti, stanje prethodne firme, zatvoreno uređivanje dostupno tastaturi, spisak koji se ne učita). Ostaje samo `EmailsPage` (303 linije).

T-17 (deseti dio) i T-42 (deveti dio): sanduče emailova je na zajedničkom sloju upita i razbijeno na dijelove u `pages/emails/` (stranica 49 linija, bila 303); 52 testa koja ranije nisu postojala, uključujući greške koje su ranije prolazile tiho ili miješale prikaz (status veze koji se ne učita uzet za nepovezano sanduče, poruka koja se ne učita s pokazivačem zauvijek, tekst jedne poruke uz pošiljaoca druge, označavanje i odjava koji ne uspiju bez riječi). Nijedna stranica više nije veća od 200 linija; ostaju dvije komponente i `EmailCallbackPage` bez testova.

Povratak s Microsofta (`EmailCallbackPage`): 16 testova koja ranije nisu postojala, uključujući provjeru da se `state` ne puštaju kad ga nema ni u adresi ni sačuvanog; dvostruko izvršavanje u strogom režimu i preusmjeravanje poslije napuštanja stranice popravljeni.

T-17 (jedanaesti dio): kandidati za zaduženje (panel zaduženih na tiketu i obrazac za otvaranje tiketa) idu kroz zajednički upit osoblja po poslovnici i podposlovnici; 22 testa koja ranije nisu postojala, uključujući spisak koji se ne učita i zakasnjeli odgovor za prethodnu poslovnicu.

T-17 (dvanaesti dio): zvonce obavijesti je na zajedničkom sloju upita; 19 testova više (24), uključujući grešku pri učitavanju koja se ranije gutala, odgovore van reda i zatvaranje tastaturom.

T-17 (trinaesti dio): izvještaj o sličnim problemima je na zajedničkom sloju upita; 25 testova koji ranije nisu postojali, uključujući odbijen zahtjev koji je ostavljao vječno čekanje, odgovore van reda, tekst bez sadržaja i otvaranje tiketa tastaturom.

T-17 (četrnaesti dio): status ponovnog indeksiranja baze znanja je jedno zajedničko stanje za oba panela; 25 novih testova, uključujući gutanje grešaka pri učitavanju i praćenju, zakasnjeli odgovor koji je poništavao pokretanje, gomilanje pitanja i panele koji nisu znali jedan za drugi.
