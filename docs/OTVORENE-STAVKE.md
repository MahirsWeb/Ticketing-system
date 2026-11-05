# Otvorene stavke: šta nije ispravljeno i šta je ispravljeno samo djelimično

**Stanje:** poslije šestog kruga ispravki (2026-11-05). Detalji i dokazi su u `IZVJESTAJ_ISPRAVKI.md` (odjeljci 9.8 do 9.12).
**Testovi:** 664 backend i 155 frontend testova prolaze, bez preskočenih. Format, ranjivi paketi, migracije u odnosu na model, lint, `tsc`, build i `npm audit` (visoke) su čisti.
**Izvor:** analiza `ANALYSIS_1` (T-01 … T-64), ponovno testiranje commita `7af778a` (149 scenarija) i moje ponovno izvođenje scenarija na izoliranoj kopiji.

## 1. Sažetak

| Stavke analize (64) | Broj |
|---|---|
| Riješeno | **43** |
| Djelimično | **12** |
| Nije rađeno | **8** |
| Isključeno odlukom vlasnika (MFA) | **1** |

U četvrtom krugu su svi scenariji iz paketa testera ponovo izvedeni (uživo, u pregledniku ili kodom i testom), a nađene greške ispravljene: brisanje poslovnice sa osobljem, `Pii:HashKey` koji se tiho promijeni, nepokriven test za prilog na tuđoj poruci, trostruko pravilo dodjele, tastatura u meniju i tabeli zadataka, zastarjeli i nekorišteni paketi (uključujući xunit), skripta za backup s upozorenjem na limit Expressa. Ispod je **samo ono što je ostalo**.

## 2. Nije ispravljeno (8 stavki)

| Stavka | Šta je ostalo | Zašto | Šta treba |
|---|---|---|---|
| **T-17** (TS-17.1) | Nema React Querya; podaci se učitavaju ručno | Veliki frontend refaktor. Zaštite od starih odgovora su ipak dodane | Planiran refaktor |
| **T-20** | Aplikacija radi kao jedna instanca (prilozi na disku, SignalR i keš u memoriji) | Epic za skaliranje; README to navodi kao ograničenje | Samo ako zatreba više od jedne instance |
| **T-31** (TS-31.1, 31.2) | Email → tiket nije atomski ni idempotentan; nema povezivanja odgovora u niti | Veća funkcionalnost | Posebna izrada |
| **T-42** | Predimenzionirane stranice, duplirane forme | Nije rađeno | Refaktor |
| **T-45** | i18n pokriva oko 5 % interfejsa | Veliki posao | Prevod i izmjena komponenti |
| **T-47** (TS-47.1) | Kontroleri direktno zovu `IEmailSender`; šabloni su C# stringovi | Nije rađeno | Jedna tačka za notifikacije, šabloni kao resursi |
| **T-48** (TS-48.1) | Nema verzija API-ja, webhookova ni mašinskih kredencijala | Nije rađeno | Samo ako ih kupci traže |
| **T-51** (TS-51.1) | NPOI je pod OSMF licencom s naknadom; nema zabilježene odluke | Pravno pitanje | **Odluka vlasnika:** plaćanje, build iz izvora ili zamjena biblioteke |

Isključeno odlukom vlasnika: **T-28 MFA**.

## 3. Djelimično (12 stavki)

| Stavka | Šta je urađeno | Šta je ostalo |
|---|---|---|
| **T-15** poslovna logika | Nekorišteni MediatR i FluentValidation uklonjeni; pravilo dodjele je na jednom mjestu (`TicketAssignment`); sva vremena idu kroz `TimeProvider` (TS-15.2 zatvoren, uz test koji brani da se sistemski sat ne vrati); novi dijelovi su zasebne klase; **tiketi su izdvojeni iz kontrolera u servise** (vidi ispod) | LookupsController (394), WorkTasksController (390), ReportsController (382), AuthController (301) i EmailIntegrationController (144) i dalje sadrže poslovnu logiku (TS-15.1) |
| **T-10** GDPR | Admin izvozi sve podatke o osobi i anonimizira osobu (API i dugmad na stranici osobe, isprobano u pregledniku); ime se uklanja i iz rečenica koje je aplikacija sama napisala (TS-10.3 zatvoren); audit log za osjetljive radnje | Period čuvanja i automatsko brisanje; imena koja su drugi ukucali u tekst svojih poruka; kopije u backupima |
| **T-11** AI i lični podaci | Davalac je podešavanje; maskiraju se email, telefon i lozinke/tokeni uz riječ koja ih imenuje, prije slanja i već pri upisu u bazu znanja (TS-11.2 zatvoren; stari chunkovi se maskiraju pri pokretanju); ograničenja dužine | Ugovor o obradi podataka s davaocem (vlasnik). Imena i slobodan tekst odlaze vanjskom davaocu. U samim tiketima i porukama tekst stoji kako je kucan. Isključenje po kompaniji ne postoji |
| **T-18** sesije | Opoziv tokena, hub se gasi pri odjavi | JWT je u `localStorage`, nema refresh tokena (TS-18.1) |
| **T-23** zaglavlja | API šalje zaglavlja; `AllowedHosts` ograničen; gotovi primjeri za nginx i IIS | Primjena na stvarnom serveru |
| **T-37** upiti i indeksi | Novi indeksi, paginirana lista korisnika | Izvještaji učitavaju cijele tikete; N+1 u izvozu |
| **T-38** RAG | Model i veličina vektora uz svaki vektor; reindeks pri promjeni modela; tekst jednog tiketa za embedding je ograničen na 6.000 znakova (TS-ONP-04.1) | `taskType`; dimenzija 768 (traži ponovno računanje svih vektora) |
| **T-46** pristupačnost | Kontrast, nazivi, jedinstven fokus, greška po ruti, naslov i `lang` | Provjera čitačem ekrana |
| **T-49** topologija | Render, Vercel, Brevo uklonjeni; README i ADR; primjeri za `docker-compose` (s rotacijom loga), nginx, IIS (uz opis prava pisanja); skripta za backup | IIS paket (zip) za backend; više instanci; primjeri nisu isprobani (YAML i XML su sintaksno provjereni) |
| **T-52** licenca | `CONTRIBUTING.md`, `.mailmap` | **Odluka o licenci i vlasništvu (vlasnik, pravno)**; `THIRD-PARTY-NOTICES` |
| **T-53** README | Prepisan prema stvarnom stanju | Nisam ga prošao od nule na čistom računaru |
| **T-58** sitnice | Uklonjeni ostaci, brojevi tiketa se sortiraju kao brojevi; pravila analizatora CA2016 (token se prosljeđuje), te za `src` CA1305 (kultura pri formatiranju), CA1068 i CA1001 su upozorenja, a CI ih pretvara u greške | Ostala pravila analizatora (npr. CA1848 poruke loga preko `LoggerMessage`, CA1861 nizovi konstanti), magični stringovi, struktura foldera |

**T-15, treći komad (baza znanja).** **Baza znanja (KnowledgeBaseController).** Sveden sa 552 na 131 liniju. Pravila su u `backend/src/TicketSystemTech.Api/KnowledgeBase/`: pretraga i odgovori (`KnowledgeBaseSearch`: traženje, pitanje osoblja, pitanje klijenta, izvještaj o sličnim tiketima, uključujući ograničenje na tikete vlastite poslovnice) i dokumentacija (`KnowledgeBaseDocuments`: otpremanje, spisak, brisanje). Token zahtjeva se sada prosljeđuje kao parametar, ne kao svojstvo kontrolera. `Outcome` može nositi i potpuno tijelo greške (`InvalidBody`), jer otpremanje vraća i spisak preskočenih fajlova.

**T-15, drugi komad (nalozi).** **Nalozi (UsersController).** Sveden sa 693 na 144 linije. Pravila su u `backend/src/TicketSystemTech.Api/Users/`: čitanje (`UserQueries`: lista, stranica, jedan nalog, traženje klijenta po emailu), pravljenje i izmjena naloga (`UserAccountService`: novi zaposlenik i klijent, nova privremena lozinka, izmjena, uključivanje i isključivanje), pravo osobe na svoje podatke (`UserPrivacyService`: izvoz i anonimizacija) i lični podaci (`UserProfileService`: slika, telefon). Provjera podposlovnice je sada na jednom mjestu (`BranchRules`), a koriste je i tiketi i nalozi (prije su postojale dvije kopije). Dodato: `Outcome` zna za 401 i za "gotovo bez odgovora" (`Done`), a `ToResult` čuva deklarisani tip `IActionResult` pa se opis API-ja nije promijenio.

**T-15, prvi komad (tiketi).** TicketsController je sveden sa 1.101 na 160 linija. Pravila su u `backend/src/TicketSystemTech.Api/Tickets/`: čitanje (`TicketQueries`), kreiranje (`TicketCreationService`), životni ciklus (`TicketLifecycleService`: otvaranje, premještaj, zatvaranje, status, ponovno otvaranje), zaduženi (`TicketAssigneeService`), radne sesije (`TicketWorkSessionService`), poruke (`TicketMessageService`), provjere usmjeravanja (`TicketRouting`), idempotentnost (`TicketIdempotency`) i ko smije do tiketa (`TicketAccess`). Servis ne zna za HTTP: vraća `Outcome<T>` (uspjeh, nije nađeno, zabranjeno, neispravno, sukob), a kontroler ga pretvara u isti odgovor kakav je API uvijek davao. Ugovor API-ja je nepromijenjen (`ApiContractTests`), svi raniji testovi prolaze bez izmjene.

**Zatvoreno u šestom krugu: T-41.** Opis API-ja (OpenAPI) je u `docs/openapi.json`, a backend test `ApiContractTests` pada čim se zahtjev ili odgovor promijeni bez osvježenja tog fajla. Frontend iz njega generiše tipove (`npm run gen:api` → `src/types/api.generated.ts`, CI provjerava da je generisani fajl svjež), a `src/types/contract.check.ts` pri svakom `tsc`/build provjerava 37 ručno pisanih tipova i 6 lista dozvoljenih vrijednosti protiv onoga što server stvarno šalje: polje koje server više ne šalje ili je preimenovao, polje koje server može poslati kao `null` a tip ga drži obaveznim, tekst tamo gdje ekran očekuje broj, i lista dozvoljenih vrijednosti koja se razlikuje. Ručno pisani tipovi su ostali (ekranima treba preciznije, npr. `status` kao skup poznatih statusa), ali ih kompajler sada drži uz server. Danas se svi slažu. Ne hvata: polje koje je server dodao a ekran ne zna (bezopasno), niti zahtjeve koje ekran šalje, niti šta tačno vraćaju liste u šifarnicima, jer su deklarisane kao `List<object>`.

**Zatvoreno u šestom krugu: T-40.** Svaka GET akcija koja nešto čeka prima token zahtjeva ili ga kontroler izlaže kao `Ct` (test `ReadActionsCancellationTests`), a pravilo CA2016 traži da se token proslijedi svemu što akcija čeka. Akcije koje pišu (POST, PUT, PATCH, DELETE) namjerno se ne prekidaju na pola puta: prekid između spremanja i slanja poruke ostavio bi pola urađen posao.

Iz scenarija testera, još otvoreno: **TS-02.3** (`IFileStorage` je sada asinhron, ali blob ili zajednički storage nisu rađeni), **TS-12.1** (zaštitu grane `main` podešava vlasnik na GitHubu), **TS-54.1** (nema drugog autora ni recenzenta u historiji, što kod ne može riješiti).

## 4. Nalazi testera

Provjereni u kodu i **riješeni** u ovom krugu: preklapanje vremena (NOVO-DOM-01), slike s tuđih adresa (NOVO-FE-06, NOVO-SEC-07), CI akcije vezane za tag (NOVO-OPS-16).

**Oko 60 ostalih statičkih nalaza** (`NOVO-*`) nisam sistematski obradio. Tester ih je označio kao "nije ponovo provjereno"; spisak je u `PREGLED-SVIH-NALAZA.md` u paketu testera.

## 5. Nije provjereno

- **Živi servisi:** pravi SMTP server, pravi Google i Ollama, pravi reverse proxy (u testovima su zamijenjeni lažnim serverima). Pravi mailbox s Graph integracijom (TS-01.3, TS-31.x).
- **Docker i IIS:** nema Docker engine-a ni IIS-a na ovom računaru. Docker slika se provjerava u CI poslu "Docker image"; `docker-compose.example.yml`, nginx i IIS primjeri nisu pokrenuti.
- **Skripta za backup:** isprobana na probnoj bazi (backup, verifikacija, upozorenje, greška), ali ne i čišćenje starih fajlova (folder instance nije čitljiv ovom nalogu). U zadanom folderu instance su ostala **5 probnih fajlova `TicketSystemTech_Retest_72f47b_*.bak`** (oko 55 MB) koje taj nalog ne može obrisati; obrišite ih ručno.
- **Promjena poruke u formi "New ticket on behalf" (TS-07.2):** provjerena samo `tsc`-om i lintom, bez testa komponente i bez živog izvođenja.
- **SQL Server 2017** (minimum je 2019), čitač ekrana, axe. **Opterećenje:** izmjereno samo djelimično, na sintetičkoj bazi (vidi odjeljak 7).
- **Od tester-ovih scenarija** uživo ili u pregledniku izvedeni su oni navedeni u `IZVJESTAJ_ISPRAVKI.md` (9.8 i 9.10); ostali su provjereni kodom i postojećim testovima, što je slabiji dokaz i tako je označeno u zapisu uz svaki scenarij.

## 6. Odluke vlasnika koje čekaju

- **T-51 (NPOI) i T-52 (licenca i vlasništvo):** pravna pitanja; blokatori za nuđenje kupcima.
- **Izmjene ADR-a 0001** označene sa ⚠ (`docs/adr/0001-hosting-i-model-proizvoda.md`): SQL Server **2019** umjesto 2017; bez **MFA**; **IIS paket, GitLab/Gitea, IMAP, multi-tenant i OpenTelemetry** su "izvan obima dok se ne zatraži".
- **T-18:** da li se token prebacuje iz `localStorage` u kolačić (veća izmjena).
- **Zaštita grane `main`** na GitHubu (Settings → Branches): zahtijevati pull request i provjere *Backend (Windows)*, *Backend (Linux, with SQL Server)* i *Frontend*.

## 7. Nalazi iz paketa `full-2026-10-21` (`NOVO-*`)

Pregled svih 80 nalaza po stanju (detalji u `IZVJESTAJ_ISPRAVKI.md`, odjeljak 9.11).

| Stanje | Nalazi |
|---|---|
| Riješeno (ovaj krug) | ARC-04, 05, 06, 10, 11, 12, 13, 15; DOM-02, 04, 06, 07, 08; PRV-01, 02; HOST-04; FE-01, 03, 05, 07, 08, 09, 10, 11; SEC-05, 08, 10; OPS-08, 14, 15 |
| Riješeno (ranije, provjereno) | ARC-01, 02, 03; DOM-01, 03, 05; DB-01; FE-02, 04, 06; HOST-01, 02, 03, 05, 06, 08, 09, 11; OPS-01, 02, 03, 04, 09, 10, 13, 16; SEC-07, 09 |
| Djelimično | SEC-03 (CSP samo u primjerima za server) |
| Prihvaćeno uz obrazloženje | ARC-09, OPS-07, ARC-14, HOST-12, OPS-17, SEC-01, SEC-02, SEC-06 |
| Otvoreno | ARC-07, 08; OPS-05, 06, 11 (opterećenje i memorija, mjereno samo djelimično, vidi ispod); HOST-07, OPS-12 (SQL Server 2017 ili 2019, odluka vlasnika); HOST-10; SEC-04 (odluka vlasnika) |

ARC-06, HOST-04 i OPS-08 su u prošlom krugu bili označeni kao djelimični jer reset lozinke i obavijest o zaključavanju idu kroz red u memoriji. Poslije čitanja koda: red samo upisuje poruku u outbox u bazi (`IEmailSender` je `OutboxEmailSender`), a dispatcher je šalje s ponavljanjem. Gubi se jedino ono što u trenutku restarta čeka na taj upis, u milisekundama, a reset lozinke korisnik može ponovo zatražiti. Komentar na klasi je ispravljen da to kaže.

ARC-13: reindeks staje poslije pet neuspjeha zaredom, ali prije toga davaocu pošalje jedan običan upit; ako odgovori, pet odbijenih tiketa se bilježi kao neuspjeh, a reindeks ide dalje, a ako ne odgovori, staje kao i prije (testovi `ReindexFailureTests`, oba smjera mutacijski provjerena). Ograničenje: davalac koji odgovara na običan upit, a odbija većinu tiketa, prolazi cijelu listu uz jedan poziv po tiketu.

**Mjerenje (šesti krug).** Na probnoj bazi s 20.000 sintetičkih tiketa (ne na kopiji prave baze) izmjeren je po jedan zahtjev: lista tiketa (prva stranica) 19 ms, brojači 27 ms, izvještaji sažetak 68 ms, vremenska serija 35 ms, po poslovnicama 54 ms i poređenje perioda 34 ms, a izvoz u Excel 2,6 do 3,5 s (1,5 MB; samo sastavljanje datoteke je oko 2 s, ostalo je čitanje). Jednom je izvoz trajao 27 s i nisam ga mogao ponoviti, pa ne znam šta ga je tada usporilo. Rang lista i najčešći problemi odgovorili su za 2 ms, što upućuje na to da probni podaci nisu imali ono što ti izvještaji broje (zadužene, teme), pa ta dva ne računam kao izmjerena. Ovim **nisu** pokriveni ARC-07 (spisak klijenata poslovnice u pregledu korisnika), ARC-08 i OPS-05 (indeks vektora), OPS-06 (xlsx u memoriji dok radi više korisnika) niti OPS-11: za njih i dalje nema mjerenja.

## 8. Šta bih uradio sljedeće

1. **Odluke vlasnika** iz odjeljka 6 i dvije nove: minimum SQL Servera (HOST-07) i ko smije resetovati klijentsku lozinku (SEC-04). Zaštita grane `main` je pet minuta posla na GitHubu.
2. **Mjerenje prije optimizacije:** ARC-07, ARC-08, OPS-05, 06, 11 su pretpostavke o opterećenju; treba ih izmjeriti na pravoj bazi (21.712 tiketa) prije bilo kakve izmjene. Ono što je mjereno na sintetičkoj bazi (odjeljak 7) ne ukazuje na problem osim Excel izvoza od oko 3 s.
3. **Srednji poslovi:** T-37 (izvještaji bez učitavanja cijelih tiketa; mjerenje pokazuje da to sada nije hitno), ostatak T-58 (preostala pravila analizatora, magični stringovi).
4. **Veliki refaktori** (T-15, T-17, T-31, T-47, T-45) tek poslije toga, svaki kao zaseban zadatak, jer svaki mijenja način rada cijele aplikacije.
