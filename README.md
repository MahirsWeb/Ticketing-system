# Ticket System Tech

Sistem za upravljanje korisničkom podrškom (ticketing/helpdesk aplikacija): React + TypeScript frontend,
.NET 10 Web API backend, Microsoft SQL Server baza podataka. Aplikacija radi **potpuno lokalno** — nema
potrebe za internetom niti cloud nalozima da bi radila (osim za AI asistenta, koji koristi Google AI API).

Uključuje: tikete sa punom historijom razgovora, ulogama Admin/Zaposlenik/Klijent, AI asistenta za pretragu
starih tiketa i dokumentacije, email integraciju (Outlook), izvještaje/dashboard, upravljanje zadacima
(work tasks), i enkripciju ličnih podataka (email, telefon, adresa) u bazi.

---

## Sadržaj

1. [Šta ti treba prije početka](#šta-ti-treba-prije-početka)
2. [Struktura projekta](#struktura-projekta)
3. [Pokretanje korak po korak](#pokretanje-korak-po-korak)
4. [Prijava u aplikaciju](#prijava-u-aplikaciju)
5. [Ko šta vidi (izolacija po poslovnicama)](#ko-šta-vidi-izolacija-po-poslovnicama)
6. [Objašnjenje svake konfiguracijske postavke](#objašnjenje-svake-konfiguracijske-postavke)
7. [Slanje email-ova](#slanje-email-ova)
8. [Ograničenja AI upotrebe](#ograničenja-ai-upotrebe)
9. [Rad sa bazom podataka](#rad-sa-bazom-podataka)
10. [Rješavanje čestih problema](#rješavanje-čestih-problema)
11. [Provjera koda (testovi i CI)](#provjera-koda-testovi-i-ci)
12. [Poznata ograničenja](#poznata-ograničenja)

---

## Šta ti treba prije početka

Instaliraj sljedeće na svoj računar (sve je besplatno):

| Alat | Za šta služi | Gdje preuzeti |
|---|---|---|
| **.NET 10 SDK** (LTS) | Pokreće backend (C#/.NET Web API) | [dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Node.js 22.12 ili novije** (ili 20.19+) | Pokreće frontend (React) | [nodejs.org](https://nodejs.org) (uzmi LTS verziju) |
| **SQL Server 2019 ili novije** (Express edition je dovoljna, besplatna) | Baza podataka | [Preuzmi SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads) |
| **SQL Server Management Studio (SSMS)** *(preporučeno, nije obavezno)* | Grafički alat da vidiš/pretražuješ bazu | [Preuzmi SSMS](https://learn.microsoft.com/sql/ssms/download-sql-server-management-studio-ssms) |
| **Git** | Da preuzmeš/upravljaš kodom | [git-scm.com](https://git-scm.com) |

Provjeri da li su .NET i Node ispravno instalirani (otvori terminal/PowerShell i ukucaj):

```bash
dotnet --version
```
Treba da ispiše nešto što počinje sa `10.` (npr. `10.0.402`). Projekat traži SDK 10.0.100 ili noviji
(`backend/global.json`); sa starijim SDK-om (8 ili 9) se neće izgraditi. Starija verzija .NET-a može ostati
instalirana pored nove, ali .NET 8 više ne dobija sigurnosne ispravke od 10. novembra 2026.

```bash
node --version
```
Frontend (Vite 8) traži Node `^20.19.0 || >=22.12.0` (`engines` u `frontend/package.json`): `v22.12` ili novije, ili `v20.19` do `v21`.
Sa starijim izdanjem (npr. `v22.0` do `v22.11`) `npm install` upozori, a `npm run dev` se ne pokrene.

---

## Struktura projekta

```
backend/    ASP.NET Core 10 Web API
  src/
    TicketSystemTech.Api/              — kontroleri, konfiguracija, Program.cs (ovdje se app pokreće)
    TicketSystemTech.Application/      — poslovna logika, opcije
    TicketSystemTech.Domain/           — entiteti (Ticket, User, Company...) i enumi
    TicketSystemTech.Infrastructure/   — baza podataka, migracije, servisi (email, AI, enkripcija...)
  tests/                               — testovi

frontend/   React + TypeScript (Vite, Tailwind, TipTap editor, Recharts grafovi)
```

---

## Pokretanje korak po korak

### 1. Preuzmi kod

```bash
git clone <link-ka-repozitoriju>
cd <folder-koji-je-git-napravio>
```

(Ime foldera je ime repozitorija, npr. `Ticketing-system`. Sve naredne komande se pišu u odnosu na taj folder.)

### 2. Provjeri da SQL Server radi

Ako si tek instalirao SQL Server, servis bi trebao automatski raditi. Provjeri u **Services** (Windows) da
li vidiš nešto poput `SQL Server (SQLEXPRESS)` sa statusom "Running". Ako koristiš imenovanu instancu (npr.
`SQLEXPRESS`), zapamti to ime — trebaće ti u sljedećem koraku.

> **Ne moraš ručno praviti bazu podataka.** Aplikacija je sama kreira (i postavi kompletnu šemu) prvi put
> kad se pokrene — dovoljno je da SQL Server servis radi i da imaš ispravan connection string.

### 3. Podesi backend konfiguraciju

Backend čita lokalne postavke (lozinke, ključeve, connection string) iz fajla koji **nije** u git-u (jer
sadrži osjetljive podatke). Napravi ga kopiranjem primjera:

```bash
cd backend/src/TicketSystemTech.Api
cp appsettings.Development.json.example appsettings.Development.json
```

Otvori novi `appsettings.Development.json` i popuni:

- **`ConnectionStrings:DefaultConnection`** — zamijeni `SQLEXPRESS` imenom svoje SQL Server instance
  (ako ne znaš koje ime tvoja instanca ima, pogledaj [Rad sa bazom podataka](#rad-sa-bazom-podataka) ispod).
- **`Jwt:Secret`** i **`Pii:HashKey`** — bilo koji nasumičan string, što duži to bolje (30+ karaktera).
  Najmanje 32 karaktera; app neće krenuti s kraćim ključem. Treba da budu različiti stringovi — jedan potpisuje
  login sesije, drugi (`Pii:HashKey`) služi za pretraživanje naloga po emailu (HMAC hash). **Ne mijenjaj
  `Pii:HashKey` nakon što već imaš podatke u bazi** — ljudi se više ne bi mogli ulogovati jer se njihov email
  više ne bi našao. Sam sadržaj email/telefon/adresa enkriptuje ključ koji je u tabeli `DataProtectionKeys`
  (u istoj bazi); **ako se ta tabela izgubi ili zamijeni, enkriptovani podaci su trajno nečitljivi**, pa je
  uvijek uključi u backup baze. App će odbiti da krene ako ne može dešifrovati postojeće podatke.
  Izvan `Development` okruženja `Pii:HashKey` je obavezan.
- **`SeedAdmin:Email`** — tvoj email; ovaj nalog će automatski biti napravljen kao Admin prvi put kad app
  krene.
- **`Ai:ApiKey`** — *opciono, ali potrebno za AI asistenta.* Besplatan Google ključ: [aistudio.google.com](https://aistudio.google.com/app/apikey)
  (stari naziv `GoogleAi:ApiKey` i dalje radi). AI se može i potpuno isključiti (`Ai:Enabled` = `false`) ili usmjeriti na
  **lokalni model** na tvom računaru, bez ikakvog interneta (npr. Ollama): vidi [AI: davalac, lokalni model i šta napušta sistem](#ai-davalac-lokalni-model-i-šta-napušta-sistem).
- **`Smtp:Host`, `Smtp:Port`, `Smtp:Username`, `Smtp:Password`, `Smtp:FromAddress`** — *opciono.* Potrebni samo ako želiš da
  "zaboravljena lozinka", pozivnice i obavijesti o tiketima stvarno šalju email: bilo koji SMTP server (firmin mail server,
  Gmail/Office 365 s lozinkom za aplikaciju, ili za probu [smtp4dev](https://github.com/rnwood/smtp4dev) koji poruke samo hvata).
  `FromAddress` nema zadanu vrijednost, pa bez nje ništa ne ide. Kako se email šalje opisuje [Slanje email-ova](#slanje-email-ova).
- **`MicrosoftGraph`** — *opciono.* Potrebno samo za Outlook email integraciju (pretvaranje email-ova u
  tikete). Slobodno ostavi prazno ako ti ne treba.

### 4. Pokreni backend

Korak 3 te je ostavio u `backend/src/TicketSystemTech.Api`; vrati se u folder `backend`:

```bash
cd ../..
dotnet restore
dotnet run --project src/TicketSystemTech.Api
```

Prvo pokretanje će potrajati malo duže (preuzima pakete, kreira bazu, primjenjuje šemu). Kad vidiš u
terminalu:

```
Now listening on: http://localhost:5114
```

...backend radi. Ostavi ovaj terminal otvoren — ako ga zatvoriš, backend se gasi.

U istom terminalu ćeš vidjeti liniju poput:

```
Generated initial password for tvoj-email@example.com (valid 20 min, must be changed on first login): Xy7kP2mQrT
```

**Zapamti tu privremenu lozinku** — treba ti za prvu prijavu (vidi [Prijava u aplikaciju](#prijava-u-aplikaciju)).

> Ta linija se ispisuje samo zato što `appsettings.Development.json.example` ima `SeedAdmin:PrintGeneratedPassword` =
> `true`. Na serveru je to isključeno (lozinka se ne smije naći ni u konzoli ni u log fajlu); tamo ili upiši
> `SeedAdmin:Password`, ili novu privremenu lozinku dobij komandom iz sekcije [Prijava u aplikaciju](#prijava-u-aplikaciju).

### 5. Podesi frontend konfiguraciju

Otvori **novi** terminal (backend mora ostati pokrenut u prethodnom):

```bash
cd frontend
cp .env.example .env.local
```

Podrazumijevana vrijednost (`http://localhost:5114`) već je tačna ako si backend pokrenuo po uputama gore —
nema šta mijenjati osim ako si mijenjao port backend-a.

### 6. Pokreni frontend

```bash
npm install
npm run dev
```

> **Windows napomena:** ako PowerShell odbije da pokrene `npm` uz grešku o "execution policy" i digitalnom
> potpisu, koristi `npm.cmd install` i `npm.cmd run dev` umjesto `npm install`/`npm run dev` — to zaobilazi
> problem bez mijenjanja sigurnosnih podešavanja sistema.

Kad terminal ispiše nešto poput:

```
➜  Local:   http://localhost:5173/
```

...otvori taj link u browseru. Aplikacija je pokrenuta.

---

## Prijava u aplikaciju

1. Otvori `http://localhost:5173`
2. Email: onaj koji si upisao u `SeedAdmin:Email`
3. Lozinka: privremena lozinka koju je backend ispisao u terminalu (korak 4 gore)
4. Aplikacija će odmah tražiti da postaviš stalnu lozinku — nakon toga si prijavljen kao Admin.

Ako privremena lozinka istekne prije nego je iskoristiš (važi 20 minuta po defaultu) ili je zaboraviš, ne treba ništa da
brišeš u bazi. Zaustavi backend i pokreni:

```bash
cd backend   # iz korijena projekta
dotnet run --project src/TicketSystemTech.Api -- --reset-admin-password tvoj-email@example.com
```

Komanda daje tom Admin nalogu novu privremenu lozinku (ispiše je **samo u taj terminal**, ne u log), skida zaključavanje,
poništava stare sesije i upisuje zapis u audit log (bez lozinke). Radi samo za Admin naloge. Ako ne želiš da se lozinka ikad
generiše, upiši svoju u `SeedAdmin:Password` prije prvog pokretanja; i ona se mora promijeniti pri prvoj prijavi.

Generisana lozinka se u terminalu prikazuje samo ako je `SeedAdmin:PrintGeneratedPassword` = `true` (u
`appsettings.Development.json.example` jeste, da bi brzo pokretanje ovdje radilo); na serveru je isključeno, pa se lozinka
ne može pojaviti ni u konzoli ni u log fajlu.

---

## Ko šta vidi (izolacija po poslovnicama)

Zaposlenik je vezan za jednu **poslovnicu** (odjel). Pravila su stroga i provodi ih server, ne samo ekran:

| Uloga | Tiketi | Klijenti | Osoblje | AI asistent (pretraga, pitanja, slični tiketi) |
|---|---|---|---|---|
| **Admin** | svi | svi | svi | cijela baza znanja |
| **Zaposlenik** | samo tiketi njegove poslovnice (tiket bez poslovnice vidi samo Admin) | samo klijenti koji imaju bar jedan tiket u njegovoj poslovnici | samo kolege iz njegove poslovnice (osim kad bira kome dodijeliti tiket u drugoj poslovnici) | samo tiketi njegove poslovnice + dokumentacija |
| **Klijent** | samo njegovi tiketi | samo on sam | — | samo dokumentacija (nikad tiketi) |

Posljedice koje treba znati:

- Klijent koji još nikad nije pisao jednoj poslovnici **ne pojavljuje se** u njenoj listi klijenata. Zaposlenik ga
  ipak može pronaći unosom **tačne email adrese** pri otvaranju tiketa u ime klijenta; takvo pretraživanje se
  upisuje u audit log (`ClientLookupOutsideBranch`), a broj telefona se ne prikazuje dok klijent ne dobije tiket
  u toj poslovnici.
- AI odgovori manjih poslovnica oslanjaju se samo na njihovu historiju, pa su skromniji od odgovora velike poslovnice.
- **Spisak firmi klijenata je zajednički** za sve zaposlenike (firma se bira pri otvaranju naloga klijenta, a to mora
  biti moguće i za firmu koja još nije pisala toj poslovnici). Izolacija važi za tikete, klijente i osoblje, ne za firme.

### Evidencija radnog vremena na tiketu

- Na tiketu može biti **više sesija rada** (jutarnja i popodnevna su dvije), a nijedna ne briše drugu. Sesija ima početak
  i kraj; bez kraja znači da rad još traje (dugme *End now* je završava).
- Sesije se vode za **osobu koja radi na tiketu** (glavni izvršilac). Ko god ih unese, pripadaju njoj.
- Osoba dodana **kao ispomoć** je na tiketu navedena, ali **nema svoje sesije** pa se zbog tog tiketa ne pojavljuje na
  vremenskoj liniji (Gantt i Excel izvoz). Ako glavni izvršilac bude skinut s tiketa, ispomoć postaje glavni izvršilac, a
  stare sesije ostaju kod onoga ko je radio.
- Sesije jednog tiketa se ne smiju preklapati, ne mogu početi u budućnosti, a tiket ima najviše jednu sesiju koja traje.
  Sesija koja traje završava se sama kad se tiket zatvori, prebaci u drugu poslovnicu ili kad se osoba skine s njega
  ili deaktivira.
- Klijent ne vidi koliko je ko radio. Svaka izmjena sesije ostavlja zapis u audit logu.
- Tiket za koji niko nije upisao vrijeme na vremenskoj liniji i dalje stoji od otvaranja do zatvaranja, ali samo za glavnog
  izvršioca, a Excel izvoz za takav red ne prikazuje sate (to nije stvarno utrošeno vrijeme).

---

## Objašnjenje svake konfiguracijske postavke

| Postavka | Obavezno? | Šta radi |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | ✅ Da | Kaže backend-u gdje mu je SQL Server baza. `TrustServerCertificate=True` je u redu samo za bazu na istom računaru ili u istoj Docker mreži. Za bazu na drugom serveru koristi `Encrypt=True;TrustServerCertificate=False` i stvarni certifikat na SQL Serveru, inače se promet (i lozinka baze) može presresti. |
| `Jwt:Secret` | ✅ Da | Tajni ključ kojim se potpisuju login sesije (tokeni). Mora ostati isti dok je aplikacija u upotrebi — ako ga promijeniš, svi su izlogovani. |
| `Jwt:AccessTokenMinutes` | Ne (default 30, u primjeru 120) | Koliko dugo login sesija traje prije nego se mora ponovo ulogovati. |
| `Pii:HashKey` | ✅ Da (obavezno izvan Development) | Ključ HMAC hasha kojim se nalozi traže po emailu. **Ne mijenjaj ga nakon što već postoje nalozi** — niko se više ne bi mogao ulogovati. Enkripcija sadržaja koristi ključeve iz tabele `DataProtectionKeys`, koja mora biti u svakom backupu. |
| `Storage:UploadsPath` | Ne | Folder za priloge tiketa i dokumente baze znanja (default `App_Data/uploads` pored aplikacije). Ne servira se javno; za produkciju postavi putanju **van** foldera koji se redeploya. Prilozi se otvaraju kroz potpisane linkove koji ističu za 4 sata. |
| `SeedAdmin:Email` / `Password` | ✅ Email da, lozinka ne | Email za prvi Admin nalog koji se automatski pravi. Ako ostaviš `Password` prazno, generiše se nasumična privremena lozinka. |
| `SeedAdmin:PrintGeneratedPassword` | Ne (default `false`) | Ako je `true`, generisana lozinka se **jednom** ispiše u terminal (nikad u log). Inače se ne prikazuje, a nova se dobija komandom `--reset-admin-password`. |
| `Startup:Migrate` | Ne (default `true`) | Da li backend pri pokretanju sam primjenjuje migracije i jednokratne popravke baze. Postavi na `false` ako migracije radiš kao zaseban korak (`dotnet TicketSystemTech.Api.dll --migrate-only` — primijeni migracije, seeduje prvog admina i izađe); backend tada odbija da krene na neažuriranoj bazi. Više instanci koje krenu istovremeno čekaju jedna drugu (SQL Server zaključak), pa se migracije ne izvršavaju paralelno. |
| `Sla:Mode` | Ne (default `CalendarHours`) | Kako se računa rok tiketa iz SLA plana. `CalendarHours` = sati teku non-stop (plan od 24 h = rok za 24 h). `BusinessHours` = broje se samo radni sati. Rok koji osoblje ručno unese uvijek ima prednost. |
| `Sla:TimeZoneId`, `Sla:WorkdayStart`, `Sla:WorkdayEnd` | Ne | Samo za `BusinessHours`: vremenska zona (default `Europe/Sarajevo`) i radno vrijeme (default `08:00`–`16:00`). |
| `Sla:WorkDays`, `Sla:Holidays` | Ne | Samo za `BusinessHours`: radni dani (default ponedjeljak–petak) i neradni datumi (`yyyy-MM-dd`). Ako ih postaviš, **zamjenjuju** default; ne dodaju se na njega. |
| `Ai:Enabled` | Ne (default `true`) | `false` isključuje AI potpuno: ništa se ne šalje nikome, a ekrani kažu da je AI nedostupan (pretraga po ključnim riječima radi). |
| `Ai:Provider` | Ne (default `Gemini`) | `Gemini`, `OpenAiCompatible` (Ollama, vLLM, LM Studio, Azure OpenAI, OpenAI...) ili `None`. |
| `Ai:ApiKey` | Za Gemini | Ključ davaoca (stari naziv `GoogleAi:ApiKey` radi). Za lokalni model nije potreban. |
| `Ai:Endpoint`, `Ai:ChatModel`, `Ai:EmbeddingModel` | Za `OpenAiCompatible` | Adresa (npr. `http://localhost:11434/v1`) i imena modela. Za Gemini imaju zadane vrijednosti i mogu se nadjačati. |
| `Ai:EmbeddingDimensions` | Ne | Veličina vektora. Promjena (ili promjena modela) znači da stari vektori više ne vrijede; admin pokrene *Reindex* i oni se obnove. |
| `Ai:RedactPersonalData`, `Ai:MaxOutputTokens`, `Ai:BackgroundMinIntervalSeconds` | Ne (true / 2048 / 2.5) | Maskiranje emailova i telefona prije slanja, najveći odgovor modela, razmak između pozadinskih poziva pri indeksiranju (korisnička pitanja ne čekaju na njega). |
| `Smtp:Host`, `Port` (587), `Security` (`StartTls`, `Ssl` ili `None`), `Username`, `Password` | Za slanje email-ova | SMTP server. Bez `Host` se email-ovi ne šalju (poruka se u outboxu označi kao *Skipped*, upozorenje u logu, aplikacija ne puca). Šifrovanje se nikad ne spušta samo od sebe: ako je tražen `StartTls`, a server ga ne nudi, poruka se ne šalje u čistom tekstu. |
| `Smtp:FromAddress` / `FromName` / `ReplyTo` / `TimeoutSeconds` | `FromAddress` za slanje | Pošiljalac (adresa nema zadanu vrijednost), ime, adresa za odgovor, i koliko dugo se čeka server (20 s). |
| `Email:Outbox:Enabled` | Ne (default `true`) | Uključuje radnika koji šalje email-ove iz outboxa. Ako ga isključiš, poruke se samo gomilaju u tabeli `EmailOutbox`. |
| `Email:Outbox:PollSeconds`, `BatchSize`, `LeaseSeconds` | Ne (5 / 20 / 120) | Koliko često radnik gleda ima li šta za slanje, koliko poruka uzme odjednom i koliko dugo se poruka koju šalje drži "zauzetom" (ako radnik pukne usred slanja, poruka se nakon toga ponovo nudi). |
| `Email:Outbox:KeepSentDays`, `KeepFailedDays` | Ne (7 / 30) | Poslane i preskočene poruke se brišu nakon 7 dana, one koje se nisu mogle isporučiti nakon 30 (do tada se u `LastError` vidi zašto). |
| `RateLimits:AuthPerMinute` / `LoginPerAccountPerMinute` / `AiStaffPerTenMinutes` | Ne (15 / 30 / 60) | Prijava i drugi nalog-endpointi po mrežnoj adresi; pokušaji prijave po nalogu (broji se ukucani email, postojao nalog ili ne); AI upiti osoblja po osobi u 10 minuta. Klijentski AI chat je zasebno ograničen na 8 upita u 5 minuta. |
| `ForwardedHeaders:KnownProxies` / `KnownNetworks` | Samo iza reverse proxyja | Adrese (npr. `10.0.0.5`) ili mreže (`10.0.0.0/8`) proxyja kojem se vjeruje da kaže pravu adresu posjetioca. **Ništa nije povjereno po defaultu, ni ovaj računar**: bez ovoga se `X-Forwarded-*` zaglavlja ignorišu, pa zahtjev ne može sam izabrati svoju adresu. Proxy na istom računaru se mora navesti (`127.0.0.1` ili `::1`). |
| `AllowedHosts` | Na serveru da | Imena po kojima se aplikacija smije zvati, odvojena sa `;`. Default `localhost;127.0.0.1;[::1]`: zahtjev s drugim `Host` imenom dobija `400`. Za rad na mreži upiši ime servera. Na serveru (`Production`) `*` nije dozvoljen. |
| `Frontend:BaseUrl` | Na serveru da | Adresa na kojoj ljudi otvaraju aplikaciju; od nje se prave linkovi u email-ovima. Na serveru ne smije biti `localhost`. |
| `DataProtection:CertificatePath` + `CertificatePassword`, ili `CertificateThumbprint` (`CertificateStoreLocation`) | Na serveru da | Certifikat kojim se šifruju ključevi iz tabele `DataProtectionKeys`. Vidi [Produkcija i bezbjednost](#produkcija-i-bezbjednost). |
| `DataProtection:AllowUnprotectedKeys` | Ne (default `false`) | Samo kad svjesno prihvataš da ključevi budu u bazi nešifrovani na serveru. Na razvojnom računaru nije potrebno. |
| `Logging:File:Enabled`, `Path`, `RetainedFiles`, `MaxFileSizeMb` | Ne (true / `logs/ticketsystem-.log` / 30 / 100) | Log ide u konzolu i u dnevni fajl (rotira se po danu i po veličini). Lozinke i tokeni se ne loguju. |
| `MicrosoftGraph:ClientId` / `ClientSecret` | Za Outlook integraciju | Bez ovoga, samo Settings → Email Integracija ostaje neaktivna; ostatak aplikacije radi normalno. |
| `TemporaryPassword:ValidityMinutes` | Ne | Koliko dugo privremene lozinke (za nove naloge) važe prije isteka. |

---

## Slanje email-ova

Aplikacija email **ne šalje dok korisnik čeka**. Svaki email (pozivnica, obavijest o tiketu, privremena lozinka...) se prvo
upiše kao red u tabelu `EmailOutbox`, a zaseban radnik u pozadini ga šalje preko SMTP servera iz `Smtp` sekcije:

- Ako je server zauzet ili nedostupan (odgovori `4xx`, mrežna greška, istek vremena, pogrešna lozinka u `Smtp:Password`),
  poruka se **pokuša ponovo** nakon 1, 2, 4, 8, 16 pa svakih 30 minuta, ukupno **8 pokušaja** (oko dva sata); ako ispraviš
  podešavanje u tom roku, poruke odu. Ako server odbije samu poruku (`5xx`, npr. nepostojeći primalac), ponovni pokušaj ne bi
  pomogao pa se poruka odmah označi kao *Failed*.
- Poruka preživi **restart aplikacije**: ostaje u tabeli i šalje se kad aplikacija ponovo krene. Ako je pokrenuto više
  instanci, radnik poruku uzima uslovnim `UPDATE`-om i rezerviše je (`Email:Outbox:LeaseSeconds`, računa se od trenutka kad je
  poruka uzeta, ne od početka kruga), pa je dvije instance u normalnom radu ne uzimaju istovremeno. Slanje koje traje duže od
  rezervacije (SMTP koji visi duže od `LeaseSeconds`) može završiti slanjem iste poruke još jednom; rezervacija zato mora biti
  duža od SMTP timeouta.
- Isporuka je **"najmanje jednom"**: ako aplikacija pukne baš između prihvatanja poruke na serveru i zapisa u bazu da je
  poslano, poruka se nakon isteka zaključavanja pošalje još jednom. Takav duplikat je cijena toga da se poruka ne izgubi.
- Račun koji je deaktiviran više ne prima email: pravilo primjenjuje sâm radnik, za bilo koji transport, pa se poruka označi
  *Skipped*. Bez `Smtp:Host` ili `Smtp:FromAddress` se ništa ne šalje, ali se zna šta je preskočeno.
- Adresa primaoca je u tabeli **šifrovana** kao i sve druge email adrese, a u log se ne upisuje.
- Link za "zaboravljenu lozinku" najprije prođe kroz mali memorijski red (da odgovor na zahtjev ne otkrije vremenom postoji
  li nalog), pa se odatle upiše u outbox kao i ostalo. Ako aplikacija padne u tih nekoliko trenutaka prije upisa, korisnik
  link jednostavno ponovo traži.
- **Upis u istoj transakciji.** Obavijest o tiketu i zadatku, pozivnica, nova privremena lozinka, potvrda e-maila i dobrodošlica
  poslovnice upisuju se u `EmailOutbox` istim `SaveChanges`-om kao promjena na koju se odnose: ili postoje oboje ili nijedno.
  Izuzetak su poruke koje idu kroz memorijski red (zaboravljena lozinka, obavijest o zaključavanju), jer odgovor ne smije otkriti
  postoji li nalog; tu se upis dešava odmah poslije.
  Odgovor na tiket i premještaj tiketa su ovdje jedno snimanje: poruka/premještaj, red u listi i outbox red idu zajedno (test to
  provjerava na SQL komandama).
- **Stari redovi:** redovi napisani prije šifrovanja tijela obrađuju se pri pokretanju: završeni (poslani, preskočeni, neuspjeli)
  gube tijelo, a čekajući se šifruju na mjestu pa se mail ipak šalje. To radi korak koji pravi migracije: kod `Startup:Migrate=false`
  pokreni jednom `--migrate-only` poslije nadogradnje, inače stari redovi s čistim tekstom čekaju (i, kad se pošalju, gube tijelo).
- **Podešavanja se primjenjuju pri pokretanju.** Ponovno čitanje `appsettings.json` tokom rada je isključeno (`Program.cs`), a
  varijable okruženja se ionako čitaju pri pokretanju procesa; zato promjena bilo kojeg podešavanja (`Smtp`, `Ai`, ...) traži
  restart aplikacije.

Pregled stanja: `SELECT Status, COUNT(*) FROM EmailOutbox GROUP BY Status` (0 = čeka, 1 = poslano, 2 = preskočeno, 3 = nije
isporučeno; razlog je u koloni `LastError`).

Za probu bez pravog servera pokreni [smtp4dev](https://github.com/rnwood/smtp4dev) i upiši `Smtp:Host` = `localhost`,
`Smtp:Port` = `25` (ili port koji ti smtp4dev pokaže), `Smtp:Security` = `None` i bilo koju `Smtp:FromAddress`.

## AI: davalac, lokalni model i šta napušta sistem

AI je podešavanje, ne ugrađena stavka. `Ai:Provider` bira davaoca:

| Davalac | Kad | Podešavanje |
|---|---|---|
| `Gemini` (default) | Imaš Google ključ | `Ai:ApiKey`. |
| `OpenAiCompatible` | Model na tvom računaru ili na serveru kupca, bez interneta | Npr. Ollama: `Ai:Provider` = `OpenAiCompatible`, `Ai:Endpoint` = `http://localhost:11434/v1`, `Ai:ChatModel` = `llama3.1`, `Ai:EmbeddingModel` = `nomic-embed-text`. Isto vrijedi za bilo koji server s OpenAI REST-om (`/chat/completions`, `/embeddings`). |
| `None` ili `Ai:Enabled` = `false` | Bez AI-ja | Ništa se ne šalje; ekrani to kažu ("AI nedostupan"), pretraga po ključnim riječima i dalje radi. |

**Šta napušta sistem kad je AI uključen i davalac je vani (Gemini):** tekst pitanja i dijelovi tiketa i dokumentacije
koji su potrebni za odgovor, uz ograničenja: pitanje najviše 2.000 znakova, jedan izvor 6.000, cijeli kontekst 60.000, a
odgovor ima gornju granicu. **Email adrese, brojevi telefona i lozinke ili tokeni napisani uz riječ koja ih imenuje** (`lozinka: …`, `password=…`, `token: …`, `Bearer …`) **se maskiraju** prije slanja (`Ai:RedactPersonalData`) i **već pri upisu u bazu znanja**, pa se ni tamo ne čuvaju
(`[email]`, `[phone]`, `lozinka: [secret]`; chunkovi koji su ranije upisani maskiraju se pri pokretanju); imena
i slobodan tekst iz tiketa ne. Ključ ide u zaglavlje, nikad u adresu. Besplatni nivo Google AI Studija može koristiti
poslani tekst za poboljšanje modela; za pravu upotrebu treba plaćeni nivo ili Vertex AI uz ugovor o obradi podataka, ili
lokalni model, od kojeg ništa ne izlazi. Odluku o tome donosi vlasnik instalacije.

Promjena modela ili veličine vektora (`Ai:EmbeddingModel`, `Ai:EmbeddingDimensions`) znači da su stari vektori
neupotrebljivi: pretraga ih preskače i upozori u logu, a *Reindex* (Admin → Settings → Knowledge Base) ih zaboravlja i pravi
nove, uključujući dokumente. Interaktivna pitanja nikad ne čekaju na pozadinsko indeksiranje.

Ograničenje broja upita je **po osobi** (`RateLimits:AiStaffPerTenMinutes`), ne po mrežnoj adresi.

---

## Produkcija i bezbjednost

Na razvojnom računaru ništa od ovoga ne treba. Na serveru (`ASPNETCORE_ENVIRONMENT=Production`, što je i u Docker slici)
aplikacija **odbija da krene** i nabroji sve što fali, dok ne podesiš:

1. **`Frontend:BaseUrl`**: adresa na kojoj ljudi otvaraju aplikaciju (ne `localhost`).
2. **`AllowedHosts`**: imena servera (ne `*`).
3. **Certifikat za ključeve**. Ključevi koji šifruju lične podatke (email, telefon, adresu) i Outlook tokene čuvaju se u
   tabeli `DataProtectionKeys`, u istoj bazi kao i podaci. Bez certifikata, kopija baze je kopija i brave i ključa. S
   certifikatom se ključevi upisuju šifrovani, a certifikat nije u bazi ni u njenim backupima.

   ```bash
   cd backend
   dotnet run --project src/TicketSystemTech.Api -- --create-key-certificate keyring.pfx "neka-duga-lozinka"
   ```

   pa `DataProtection:CertificatePath` = `keyring.pfx`, `DataProtection:CertificatePassword` = ta lozinka (iz varijable
   okruženja ili tajni, ne iz fajla u git-u). Na Windowsu se umjesto fajla može koristiti certifikat iz Windows spremišta:
   `DataProtection:CertificateThumbprint`. **Sačuvaj certifikat i lozinku odvojeno od backupa baze i napravi njihovu kopiju:
   bez njih šifrovani podaci ne mogu da se pročitaju.** **Podesi certifikat prije prvog pokretanja na serveru.** Ključevi
   napravljeni bez certifikata ostaju u čistom tekstu *trajno* — svakih 90 dana se pravi novi ključ, ali se stari ne brišu (potrebni
   su za čitanje ranije šifrovanih podataka), pa certifikat dodat kasnije štiti samo ono što se šifruje poslije njega, **dok se ne
   pokrene re-key**:

   ```bash
   dotnet TicketSystemTech.Api.dll --rekey-data           # novi ključ, sve šifrovano ponovo pod njim, provjera
   dotnet TicketSystemTech.Api.dll --revoke-old-keys      # poslije provjere: stari ključevi se povlače (ne pravi novi ključ, ne prepisuje ništa)
   ```

   Dvije odvojene naredbe, svaka se može sigurno ponoviti (obje zajedno rade jedna za drugom). Pokreće se kad je certifikat već
   podešen, **sa zaustavljenom aplikacijom** i poslije backupa baze. `--rekey-data` pravi novi ključ, ponovo upisuje sve šifrovano
   (e-mail, korisničko ime i telefon naloga, adrese firmi, outbox, Outlook tokene) i **čita iz baze pod kojim je ključem šta**;
   `--revoke-old-keys` ne povlači ništa ako išta ostane pod starim. Alat **odbija** da radi: bez certifikata (novi ključ bi stajao
   nešifrovan u bazi; `--allow-unprotected-keys` ga ipak pušta, uz upozorenje), dok postoji ključ koji tek počinje (Data Protection je
   sama napravila sljedeći: pokreni poslije tog trenutka) i dok su na bazu spojeni drugi programi (aplikacija nije zaustavljena;
   `--allow-running` ako to nije aplikacija). Povlačenje se ne može poništiti, a linkovi za reset lozinke i potvrdu e-maila poslani
   ranije prestaju da rade. Backupi baze napravljeni prije re-keya ostaju čitljivi sa svojim starim ključevima. Nova instalacija
   pokrenuta s certifikatom je šifrovana od prvog dana. Ko svjesno ne želi certifikat: `DataProtection:AllowUnprotectedKeys` = `true`.

Ostalo što vrijedi znati:

- **Iza reverse proxyja** navedi njegovu adresu u `ForwardedHeaders:KnownProxies` (ili mrežu u `KnownNetworks`); inače sve
  prijave izgledaju kao da dolaze s proxyja i dijele jedno ograničenje. Ni proxy na istom računaru nije povjeren dok se ne navede.
- **Log**: konzola i dnevni fajl (`Logging:File`). U Docker slici fajlovi idu u `/data/logs`. Svaki zahtjev ima oznaku
  (`X-Correlation-Id`) koja je u svakoj liniji loga i u svakom audit zapisu tog zahtjeva.
- **Lični podaci na zahtjev:** Admin na stranici osobe (Users) ima dugmad **Export personal data** (JSON sa svim što sistem
  drži o osobi: profil, tiketi, napisane poruke, radno vrijeme, obavijesti) i **Anonymize…** (ime postaje „Obrisan korisnik“,
  email zamjenski, telefon i slika se brišu, nalog se trajno zaključava; tiketi, poruke i radno vrijeme ostaju). Ime se uklanja i iz rečenica koje je
  sama aplikacija napisala o osobi („Ticket closed by …“, „… assigned you a task“); anonimizacija se
  ne može poništiti, ne važi za sebe, a ne dira imena koja su drugi ukucali u tekst svojih poruka niti kopije u backupima. API:
  `GET /api/users/{id}/export` i `POST /api/users/{id}/anonymize`.
- **Audit log** (`AuditLogs`) bilježi osjetljive radnje: ko, šta, nad čim, kada, s koje adrese i kojeg zahtjeva (kreiranje,
  izmjena i deaktivacija naloga, otvaranje, zatvaranje, ponovno otvaranje i prenos tiketa, promjena izvršilaca, izmjene
  poslovnica, resetovanje lozinke, spajanje mailboxa, radno vrijeme). Da bude samo za dodavanje, korisniku baze kojim radi
  aplikacija oduzmi izmjenu i brisanje:
  `DENY UPDATE, DELETE ON dbo.AuditLogs TO [korisnik_aplikacije];` (ne važi za `dbo`/vlasnika baze, pa aplikacija na serveru
  ne treba da radi kao on).
- **Health**: `/health/live` (proces je živ), `/health/ready` (baza odgovara i šema je aktuelna). Docker slika ima
  `HEALTHCHECK` koji pita `/health/live`.
- **Docker slika**: bazne slike su zakucane digestom (Dependabot predlaže nove), proces nije root, a volume treba na
  `/data` (prilozi i log fajlovi) i `/app/wwwroot/avatars` (profilne slike). Primjer:

  ```bash
  docker run -p 8080:8080 \
    -e ConnectionStrings__DefaultConnection="Server=...;Database=TicketSystemTech;..." \
    -e Jwt__Secret=... -e Pii__HashKey=... \
    -e Frontend__BaseUrl=https://tickets.firma.ba -e AllowedHosts=tickets.firma.ba \
    -e DataProtection__CertificatePath=/certs/keyring.pfx -e DataProtection__CertificatePassword=... \
    -v tickets-data:/data -v tickets-avatars:/app/wwwroot/avatars -v /putanja/do/certs:/certs:ro \
    ticketsystemtech-api
  ```

  `HEALTHCHECK` slike sam uzima ime iz `AllowedHosts`, pa radi i kad je tamo samo ime servera. Health check koji vrši
  platforma (Kubernetes, load balancer) obično dolazi s IP adresom kao `Host`; ako ga koristiš, dodaj tu adresu u `AllowedHosts`.
- **Frontend na serveru.** Repozitorij ne služi `frontend/dist`; to radi web server po izboru (IIS, nginx, Caddy...), a
  njegovo podešavanje je dio instalacije. Prvo `VITE_API_BASE_URL=https://api.firma.ba npm run build` (adresa API-ja se
  ugrađuje pri gradnji), pa server mora: (1) za svaku putanju koja nije fajl vratiti `index.html` (aplikacija ima svoje rute);
  (2) slati sigurnosna zaglavlja, jer ih API šalje samo za svoje odgovore. Početna tačka (ono što je ranije stajalo u
  `vercel.json`, ali **primijenjeno**, ne samo prijavljivano):

  ```
  Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://api.firma.ba; font-src 'self' data:; connect-src 'self' https://api.firma.ba wss://api.firma.ba; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
  X-Content-Type-Options: nosniff
  Referrer-Policy: strict-origin-when-cross-origin
  Permissions-Policy: camera=(), microphone=(), geolocation=()
  Strict-Transport-Security: max-age=15552000
  ```

  Isprobajte ga najprije kao `Content-Security-Policy-Report-Only` dok konzola preglednika ne prestane prijavljivati
  prekršaje. Slike s tuđih adresa u sadržaju tiketa i email-ova se ionako uklanjaju (server ih čisti pri upisu, a preglednik
  pri prikazu), pa `img-src` ne treba `https:`, ali treba adresu API-ja: slike profila i prilozi se učitavaju s nje.
  Izgrađena stranica (`npm run build`) već nosi vlastiti `Content-Security-Policy` u `index.html`, napravljen od iste
  `VITE_API_BASE_URL` (skripte samo s adrese aplikacije, veza samo s njom i s API-jem, bez ugrađenih objekata), pa vrijedi
  i ako server ne šalje zaglavlje. Pravilo u stranici ne može reći ko smije aplikaciju okvirom prikazati (`frame-ancestors`)
  niti prijavljivati prekršaje, zato zaglavlje servera ostaje bolje mjesto za cijelo pravilo; kad postoje oba, vrijedi
  ono što oba dopuštaju. Zaglavlja za sam API se ne podešavaju. U `Cors:AllowedOrigins` mora biti
  adresa na kojoj se frontend otvara. Gotovi primjeri (nisu isprobani u CI-ju): `frontend/deploy/nginx.conf.example` i
  `frontend/deploy/web.config.example` (IIS), oba s zamjenom na `index.html`, zaglavljima i keširanjem; i
  `docker-compose.example.yml` (API + SQL Server, uz `docker-compose.env.example`).
- **Backend pod IIS-om (bez Dockera).** Identitet aplikacijskog poola (npr. `IIS AppPool\TicketSystemTech`) obično nema pravo
  pisanja u folder sajta, a aplikacija piše na dva mjesta: `Storage:UploadsPath` (prilozi) i `Logging:File:Path` (log); certifikat
  iz `DataProtection:CertificatePath` samo čita. Postavi sve to **izvan foldera sajta** (npr. `D:\TicketSystemTech\uploads` i
  `D:\TicketSystemTech\logs`) i dodijeli poolu pravo *Modify* nad prva dva:
  `icacls D:\TicketSystemTech /grant "IIS AppPool\TicketSystemTech:(OI)(CI)M"`. Folder s prilozima ulazi u backup, jer se prilozi
  ne mogu povratiti iz baze. Tako zamjena (redeploy) sajta ne briše priloge ni logove.
- **Zaštita grane `main`** na GitHubu (Settings → Branches → Add rule): zahtijevaj pull request i da prođu provjere
  *Backend (Windows)*, *Backend (Linux, with SQL Server)* i *Frontend*. To se ne može podesiti iz koda.

---

## Rad sa bazom podataka

### Kako da saznam ime svoje SQL Server instance?

Otvori SSMS i pogledaj šta ti nudi u "Server name" polju kad pokušaš da se spojiš — to je puno ime tvoje
instance (npr. `DESKTOP-ABC123\SQLEXPRESS` ili samo `localhost` ako si instalirao podrazumijevanu instancu
bez imena). To ime (ili skraćeno `localhost\SQLEXPRESS`) ide u connection string.

### Šta ako sam instalirao podrazumijevanu (neimenovanu) instancu?

Onda ti u connection string-u ne treba `\SQLEXPRESS` uopšte — samo `Server=localhost;...`.

### Nova migracija (za programere)

Šemu baze mijenjaju EF Core migracije. Alat `dotnet ef` je zakucan na verziju 10 u `backend/dotnet-tools.json`, pa se
ne oslanja na ono što je instalirano na računaru:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add NazivMigracije --project src/TicketSystemTech.Infrastructure --startup-project src/TicketSystemTech.Api
```

Prije nego što pošalješ promjenu provjeri da model i migracije ne odudaraju (CI i testovi bi to inače prijavili):

```bash
dotnet ef migrations has-pending-model-changes --project src/TicketSystemTech.Infrastructure --startup-project src/TicketSystemTech.Api
```

### Promjena API-ja (za programere)

Opis API-ja je u `docs/openapi.json`, a tipovi koje frontend generiše iz njega u `frontend/src/types/api.generated.ts`. Kad
promijeniš zahtjev ili odgovor na serveru, backend test `ApiContractTests` padne dok se opis ne osvježi:

```bash
cd backend && TST_UPDATE_OPENAPI=1 dotnet test --filter ApiContractTests
```
```bash
cd frontend && npm run gen:api && npm run build
```

`npm run build` tada pokaže (u `src/types/contract.check.ts`) svaki ručno pisani tip koji se više ne slaže s onim što server
šalje; njega ispravljaš ručno. Oba fajla ulaze u isti commit. Na Windowsu u PowerShellu se prvi korak piše
`$env:TST_UPDATE_OPENAPI = "1"; dotnet test --filter ApiContractTests`.

### Pregled podataka kroz SSMS

Poveži se na svoju instancu → proširi **Databases** → `TicketSystemTech`. Baza i sve tabele su automatski
napravljene prvim pokretanjem backend-a (koristi se Entity Framework Core migracije, definisane u
`backend/src/TicketSystemTech.Infrastructure/Persistence/Migrations/`).

### Ako ikad želiš potpuno "resetovati" bazu

U SSMS-u desni klik na `TicketSystemTech` bazu → Delete. Sljedeći put kad pokreneš backend, baza i šema
će se ponovo automatski napraviti od nule (ali svi podaci se gube — ne radi ovo ako imaš nešto važno u bazi).

---

## Rješavanje čestih problema

**"npm.ps1 cannot be loaded... not digitally signed"** (PowerShell)
→ Koristi `npm.cmd` umjesto `npm` za taj komandu (npr. `npm.cmd run dev`).

**Backend ne može da se poveže na bazu / "A network-related or instance-specific error..."**
→ Provjeri da SQL Server servis radi (Windows → Services), i da je ime instance u connection string-u
tačno (vidi sekciju iznad).

**"Port 5114 is already in use" ili slično za port 5173**
→ Nešto drugo (možda već pokrenut backend/frontend iz ranije) drži taj port. Zatvori taj proces ili
promijeni port u `backend/src/TicketSystemTech.Api/Properties/launchSettings.json` (backend) odnosno
pokreni frontend sa `npm run dev -- --port 5174` pa prilagodi `VITE_API_BASE_URL`.

**AI asistent kaže da nema dovoljno informacija za sve**
→ Provjeri da je AI uključen i podešen (`Ai:ApiKey`, ili `Ai:Endpoint` i modeli za lokalni model; ekran kaže ako je AI isključen), i da si pokrenuo indeksiranje (Admin → Settings →
Knowledge Base → "Reindex missing tickets") barem jednom. Indeksiranje radi u pozadini (možeš napustiti stranicu),
pokazuje napredak, i nastavlja gdje je stalo. Embeduju se samo tiketi koje pretraga može vratiti (imaju dovoljno
internih bilješki), pa je to znatno manje posla nego ranije.

**Nakon nadogradnje na binarne vektore (jednokratno)**
→ Pri prvom pokretanju backend pretvara postojeće vektore iz JSON-a u binarni oblik (~1,5 minuta za 20 hiljada
tiketa, prikazuje napredak u logu). Stari JSON ostaje u koloni `EmbeddingJson` kao sigurnosna kopija. Kad
provjeriš da AI pretraga radi, možeš osloboditi prostor:
`UPDATE KnowledgeBaseChunks SET EmbeddingJson = NULL WHERE EmbeddingBinary IS NOT NULL;`

**Kako provjeriti da li backend radi (health check)**
→ `GET /health/live` — proces je podignut (ne provjerava bazu). `GET /health/ready` (isto i `GET /api/health`) — baza
odgovara i ima aktuelnu shemu; vraća `503` ako ne, bez ikakvih internih detalja. Monitoring ili load balancer treba da
gleda `/health/ready`.

**Korisnik je prijavio grešku — kako naći šta se desilo**
→ Svaki zahtjev dobija kratku oznaku (12 znakova, npr. `3f9a1c07d2b4`). Vraća se u zaglavlju odgovora
`X-Correlation-Id`, a kod neočekivane greške i u poruci ("report this reference: …"). U logu backenda ta ista oznaka
stoji u uglastim zagradama ispred svake poruke tog zahtjeva (`[14:16:17 WRN] [3f9a1c07d2b4] …`), pa pretraga logova po toj
oznaci pokazuje sve što se desilo s tim jednim zahtjevom. Pozivalac može poslati i svoju oznaku u istom zaglavlju
(8–64 znaka: slova, cifre, `.`, `_`, `-`), pa jedna oznaka prati zahtjev kroz više sistema.

**Zaboravio sam admin lozinku**
→ Zaustavi backend i pokreni `dotnet run --project src/TicketSystemTech.Api -- --reset-admin-password tvoj-email@example.com`
(vidi [Prijava u aplikaciju](#prijava-u-aplikaciju)). Ne briši ništa u bazi.

---

## Provjera koda (testovi i CI)

Lokalno, iz korijena projekta:

```bash
cd backend && dotnet test
```

```bash
cd frontend && npm run lint && npm test && npm run build
```

`npm test` pokreće frontend testove (Vitest + Testing Library: sanitizacija HTML-a, povratak na traženu stranicu poslije
prijave, zaštićene rute po ulozi, dijalozi, povezanost labela s poljima, Gantt). `npm run build` uključuje provjeru
TypeScript tipova. Isto rade i GitHub Actions
(`.github/workflows/ci.yml`) na svaki push na `main` i svaki pull request:

- **Backend (Windows)** mora proći: build, testovi, provjera da **migracije odgovaraju modelu** (promjena entiteta bez
  nove migracije obara build) i provjera paketa, gdje **visoke ili kritične** poznate ranjivosti obaraju build.
- **Backend (Linux, sa SQL Serverom u kontejneru)** mora proći: pokreće i **integracijske testove** (Windows ih preskače jer
  GitHubov Windows runner ne može imati SQL Server), te provjeru formatiranja (`dotnet format --verify-no-changes`).
- **Docker image** se gradi kao pri pravom puštanju u rad i provjerava se: .NET 10, zakucani digesti, proces nije root, health
  check postoji i javlja grešku kad aplikacija ne radi, a server s razvojnim postavkama odbija da krene i kaže zašto
  (informativno dok se novi koraci jednom ne zelene).
- **Frontend** prolazi lint, testove, build i `npm audit`, a generisani tipovi API-ja moraju odgovarati `docs/openapi.json`.
- **Dependabot** (`.github/dependabot.yml`) otvara pull request kad paket, Docker slika ili GitHub Action ima novu verziju.

Formatiranje: `dotnet format` očekuje završetke redova platforme na kojoj radi. Na Windowsu sa `core.autocrlf=true` (podrazumijevano)
datoteke su CRLF i provjera prolazi; ako dodaš fajl s LF završecima (npr. iz drugog alata), `dotnet format whitespace` ga
ispravi.

### Testovi autorizacije (integracijski)

Pored običnih jediničnih testova, `backend/tests/TicketSystemTech.Tests/Integration/` pokreće **pravu aplikaciju** (u
memoriji, sa pravim migracijama) nad privremenom bazom i provjerava ko šta smije:

- za **svaki** endpoint iz stvarno registrovanih ruta: anoniman poziv daje 401, a uloga koja nije dozvoljena 403; novi
  endpoint bez `[Authorize]` obara test dok se svjesno ne doda na listu javnih endpointa;
- izolaciju poslovnica: ko vidi koje tikete, klijente, osoblje, zadatke, notifikacije i koje izvore AI asistent koristi
  (uključujući pokušaje da se filterima proširi vidljivost);
- da token prestaje važiti kad se nalog deaktivira, premjesti ili promijeni lozinku;
- ograničenja AI upotrebe (dužina pitanja, broj upita po osobi) i zaštitu šifarnika koji se koriste;
- slanje email-ova kroz outbox: upis poruke, šifrovana adresa, uspjeh, ponovni pokušaji s rastućim razmakom, odustajanje,
  trajno odbijanje, preskakanje, dva radnika istovremeno (poruku uzima samo jedan), rezervacija računata od trenutka uzimanja poruke i radnik koji pukne usred slanja. SMTP
  transport je dodatno probran protiv pravog SMTP razgovora (MailKit prema malom serveru na `localhost`): odgovori `4xx`,
  `5xx`, pogrešna prijava, server koji ćuti ili prekine vezu; mail server u tim testovima nije pravi, pa se ništa ne šalje;
- audit zapise osjetljivih radnji (ko, šta, koji zahtjev), šifrovanje ključeva u bazi certifikatom, komandu za reset Admin
  lozinke, da generisana lozinka ne dospije u log, ograničenje prijave po nalogu, povjerenje prema proxyju, log fajl i probu
  koju traži Docker;
- radne sesije zadataka (ponovno pokretanje dodaje sesiju, budući datumi i mjeseci se odbijaju), dan na vremenskoj liniji u
  poslovnoj zoni, neodgođeni odgovor pri zatvaranju tiketa, jedinstven odgovor "nema povezanog mailboxa" i slanje iste poruke
  dva puta.

Testovi trebaju SQL Server na kojem smiju praviti i brisati **svoje** baze (`TicketSystemTech_AuthzTest_*`; vaša
baza se ne dira). Na razvojnom računaru ništa ne treba podešavati: koriste server iz vašeg
`appsettings.Development.json`. Drugi server se zadaje varijablom okruženja `TST_SQL` (connection string, ime baze
se ignoriše). Bez ikakvog SQL Servera ovi testovi se **preskaču** (prikažu se kao *skipped*), ostali rade normalno.

---

## Odluke i kriteriji prihvatanja

- [`docs/adr/`](docs/adr): zašto je nešto urađeno tako kako jeste (hosting i model proizvoda sa stanjem svake odluke, email outbox i
  SMTP, AI davalac i vektori, lični podaci i ključevi).
- [`docs/kriteriji-prihvatanja.md`](docs/kriteriji-prihvatanja.md): šta znači „prošlo“, šta je dokazano i čime, šta nije moguće
  dokazati na jednom računaru, šta je svjesno ostalo otvoreno, i kako ponoviti samo razliku nakon nove izmjene.
- [`IZVJESTAJ_ISPRAVKI.md`](IZVJESTAJ_ISPRAVKI.md): izvještaj o ispravkama po stavkama analize i po krugovima testiranja.
- [`docs/OTVORENE-STAVKE.md`](docs/OTVORENE-STAVKE.md): šta nije ispravljeno ili je ispravljeno samo djelimično, sa razlogom i prijedlogom šta dalje.

---

## Poznata ograničenja

- **Prilozi (attachments)** se čuvaju na lokalnom disku (`Storage:UploadsPath`, podrazumijevano `App_Data/uploads`),
  van web roota, i otvaraju se samo preko potpisanih linkova koji ističu. To je normalno za lokalno pokretanje, ali
  za pravi produkcijski hosting treba ili disk koji se ne briše pri redeployu ili druga `IFileStorage` implementacija.
- Aplikacija je dizajnirana da radi **potpuno lokalno** (SQL Server + backend + frontend na istom računaru), a ista slika se
  može pokrenuti u Dockeru (vidi [Produkcija i bezbjednost](#produkcija-i-bezbjednost)). Ostaci cloud postavke (Render, Vercel)
  i Brevo su uklonjeni. Okruženja za pregled (preview deploy) ne postoje: svako okruženje (razvoj, test, produkcija) ima svoj
  API, svoju bazu i svoj `Cors:AllowedOrigins`.
- **SQL Server Express** ima granicu od **10 GB** po bazi (vektori znanja i prilozi u bazi nisu, ali tiketi jesu) i nema SQL
  Agent, pa backup ne može da se zakaže iz njega. Za to služi `backend/ops/backup-database.ps1` (pokreće se iz Windows Task
  Schedulera): `BACKUP DATABASE ... WITH CHECKSUM`, zatim `RESTORE VERIFYONLY`, briše stare backupe (`-KeepDays`, zadano 14) i
  **upozorava kad podaci dosegnu 70 % limita** (izlazni kod 2; 1 = backup nije uspio; 0 = sve u redu). Primjer, svaku noć u 02:00:

  ```
  schtasks /Create /TN "TicketSystemTech backup" /SC DAILY /ST 02:00 /RU SYSTEM /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\TicketSystemTech\ops\backup-database.ps1 -ConnectionString \"Server=localhost\SQLEXPRESS;Database=TicketSystemTech;Trusted_Connection=True;TrustServerCertificate=True\" -BackupFolder D:\Backups"
  ```

  Folder u koji se piše mora biti dostupan **nalogu servisa SQL Servera**, ne onom ko pokreće skriptu (bez `-BackupFolder` koristi se
  zadani folder instance, koji uvijek radi). Skripta je isprobana na probnoj bazi: backup i provjera prolaze, upozorenje i izlazni
  kodovi rade, a nepostojeća baza daje izlaz 1. Pod Dockerom isto radi `sqlcmd -Q "BACKUP DATABASE ..."` iz kontejnera s bazom.
  Za više od nekoliko stotina korisnika preporučuje se Standard. Backup baze **mora** ići zajedno s certifikatom za ključeve i
  folderom s prilozima, ali certifikat se čuva odvojeno od njega.
- **Jedna instanca**: prilozi su na disku, SignalR i keš u memoriji. Više instanci iza balansera traži zajednički disk i Redis
  za SignalR; to nije urađeno.
- **xunit**: testovi koriste xunit v3 u izdanju `xunit.v3.mtp-off`. Običan paket `xunit.v3` na .NET 10 SDK-u traži novi
  izvršitelj testova (Microsoft Testing Platform) i `dotnet test` ga odbija bez promjene `global.json`; izdanje `mtp-off`
  ostaje na uobičajenom izvršitelju, pa `dotnet test`, filteri (`--filter`), TRX izvještaj (`--logger trx`) i IDE rade kao
  prije. Paket je odabran i zato da `dotnet list package --deprecated` ne prijavljuje stari xunit 2 kao *Legacy*.
- Nije urađeno: OpenTelemetry (OTLP) izvoz, IIS paket, produkcijski docker-compose za kupce (postoji samo primjer
  `docker-compose.example.yml`), više davalaca prijave od lokalnih naloga.
- **Sesija:** kratki JWT access token stoji samo u memoriji preglednika (nikad u `localStorage`, pa ga ne nalazi skripta koja
  čita pohranu preglednika). Dugi dio je **refresh token u httpOnly kolačiću** (`tst_refresh`, `Path=/api/auth`,
  `SameSite=Strict`, `Secure` izvan razvoja) koji server postavlja pri prijavi; baza čuva samo njegov SHA-256 hash.
  **Šta ovo ne štiti:** XSS koji se izvršava u samoj stranici (isti origin) može pozvati `/api/auth/refresh`; preglednik sam
  doda kolačić i skripta dobije svjež access token. httpOnly kolačić sprečava krađu *kopije* kolačića, ne rad skripte u stranici;
  zaštita od XSS-a ostaje sanitizacija sadržaja i CSP.
  - `POST /api/auth/refresh` mijenja kolačić za novi access token i rotira sam kolačić. Ponovna upotreba već rotiranog kolačića
    u prvih `Jwt:RefreshReuseGraceSeconds` (20) sekundi smatra se utrkom dvije kartice: sve dobiju **isti** nasljednik (nijedan
    novi, nezavisan lanac), i to samo dok je taj nasljednik živ, pa odjava ili opoziv porodice odmah zatvaraju prozor. **Kasnije** je
    znak krađe i poništava sve tokene te osobe. Rotacija je jedan uslovni `UPDATE` (i jedan red po tokenu unutar procesa), a
    kartice istog preglednika se usklađuju preko Web Locks. Prozor poznaje samo jedna instanca aplikacije (memorijski keš).
  - Kolačić je vezan za *security stamp*: svaka promjena lozinke (regeneracija privremene, postavljanje nove, reset, naredba
    `--reset-admin-password`) poništava sve ranije izdate kolačiće, **ali samo njih**: sesija prijavljena poslije promjene ostaje.
    Reset lozinke mailom, deaktivacija i anonimizacija poništavaju odmah sve kolačiće osobe (ponovna aktivacija ne vraća stari).
    Zaključavanje naloga zbog pogrešnih lozinki blokira **nove** prijave, ali ne izbacuje osobu koja je već prijavljena. Nova
    prijava poništava kolačić koji je **taj preglednik** još držao; prijava na drugom uređaju ne dira prvi (više uređaja je
    namjerno dozvoljeno), a odjava poništava i nasljednike kolačića (obnova koja je bila u toku ne ostavlja živu sesiju).
  - Sesija ima **apsolutni rok** `Jwt:SessionMaxDays` (30): koliko god se obnavljala, nakon toga treba ponovo upisati lozinku.
    `Jwt:RefreshTokenDays` (14) je rok neaktivnosti; `Jwt:AccessTokenMinutes` je trajanje access tokena.
  - `POST /api/auth/logout` poništava token i briše kolačić; frontend čeka odgovor (ako ne uspije, osoba ostaje prijavljena i
    dobija poruku da odjava nije uspjela). `/refresh` i `/logout` odbijaju zahtjev s tuđim `Origin`-om (403).
  - Obnova ima svoj blaži limit (`RateLimits:RefreshPerMinute`, 240 u minuti po adresi), odvojen od prijave; frontend odjavljuje
    samo na `401` i `403` (server ne prima obnove s te stranice, npr. pogrešan `Cors:AllowedOrigins` iza proxyja: kao da je
    sesija gotova), a na `429`, `5xx` i grešku mreže pokušava ponovo. Obnova ima rok od 15 s (visoka obnova ne drži ostale kartice
    ni dugme „Odjava“), a odgovor obnove koja je bila u toku kad je osoba kliknula „Odjava“ se odbacuje.
  - **`Secure` kolačić radi samo preko HTTPS-a.** Na HTTP instalaciji preglednik kolačić ne prihvata, pa se pri svakom ponovnom
    učitavanju stranice osoba mora ponovo prijaviti. Produkcija treba HTTPS.
  - Stari `localStorage['tst-auth']` iz ranije verzije aplikacija briše pri pokretanju.
