# Kriteriji prihvatanja

Ovaj dokument kaže **šta znači „prošlo“**, šta je u drugom krugu testiranja (tester, mirror commit `7af778a`, 149 scenarija) dokazano
i čime, šta nije moguće dokazati na ovom računaru i šta je svjesno ostalo otvoreno. Cilj je da se pri sljedećem krugu ponovo
provjerava **samo razlika**, a ne cijeli skup.

Stanje opisano ovdje: serija izmjena nakon commita `1a9072c` („last test“), još neobjavljena u trenutku pisanja.

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
| TS-46.5 | Kontrole bez pristupačnog naziva | Imenovana dugmad (zvonce, strelice kalendara), emoji sakriven od čitača, fokus i kontrast | `NotificationBell.test.tsx`, `DateRangePicker.test.tsx`, `ui.test.tsx` |
| T-46 (naslov, `lang`) | Naslov stranice i `lang` | Naslov po ruti, `<html lang>` prati izabrani jezik | `DocumentMeta.test.tsx` |

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
| xunit 2.9.3 (T-59) | `xunit.v3` na .NET 10 traži novi izvršitelj testova (Microsoft Testing Platform); probano i vraćeno. Paket nema ranjivosti. | Kad se odluči preći na MTP. |
| GDPR izvoz i brisanje (T-10), licenca (T-51, T-52) | Traži pravnu stranu. | Prije prvog kupca. |
| JWT u `localStorage` (T-18.1), React Query (T-17), generisani tipovi (T-41) | Veći preustroj, nije povezan s nalazima testera. | Prije v2. |

## 5. Otvoreno (nije rađeno)

Navodim samo ono što sam provjerio u kodu:

- **Preklapanje vremena iste osobe na različitim tiketima** (NOVO-DOM-01): provjera preklapanja gleda samo sesije istog tiketa.
- **Slike s udaljenih adresa u sadržaju tiketa i email-ova** (NOVO-FE-06, NOVO-SEC-07): sanitizacija ih dopušta; takva slika
  javlja pošiljaocu da je tiket otvoren. Može se suziti `img-src` u CSP-u (README) ili filtrirati u sanitizaciji.
- **CSP za SPA** (NOVO-SEC-03): repozitorij ga više ne postavlja jer nema `vercel.json`; mora ga postaviti web server koji služi
  `frontend/dist` (README, „Frontend na serveru“).
- **Akcije u CI-ju vezane za tag, ne za commit** (NOVO-OPS-16): Dependabot prati novosti, ali tagovi nisu zakucani.
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

Ručno, na čistoj bazi i pokrenutoj aplikaciji:

| Scenario | Postupak | Očekivano |
|---|---|---|
| TS-ONP-03.1 | `curl -i -H "Host: evil.example.test" http://localhost:5114/health/live` | `400` |
| TS-03.1 | 16× `POST /api/auth/login`, svaki put s drugim `X-Forwarded-For` | 16. odgovor je `429` (ograničenje po adresi gleda stvarnu adresu, ne zaglavlje); zaseban strop po nalogu je 30 u minuti |
| TS-ONP-05.1 | pokrenuti aplikaciju; pogledati folder `logs/` pored aplikacije | postoji `ticketsystem-<datum>.log` |
| TS-OPS-02.1 | pokrenuti na praznoj bazi sa `SeedAdmin:PrintGeneratedPassword=false`; pretražiti log | upozorenje bez lozinke |
| Reset | zaustaviti aplikaciju; `dotnet run --project src/TicketSystemTech.Api -- --reset-admin-password <email>` | nova lozinka samo na terminalu; prijava traži promjenu |
| TS-50.1 | `docker build`, `docker inspect`, `docker run` (koraci posla „Docker image“) | korisnik nije root, `HEALTHCHECK` postoji, pokretanje bez konfiguracije odbijeno uz spisak razloga |
| TS-04.2 | `SELECT Xml FROM DataProtectionKeys` s certifikatom i bez | s certifikatom nema `<masterKey`, bez njega ga ima |
