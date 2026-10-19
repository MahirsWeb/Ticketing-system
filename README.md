# Ticket System Tech

Sistem za upravljanje korisničkom podrškom (ticketing/helpdesk aplikacija): React + TypeScript frontend,
.NET 8 Web API backend, Microsoft SQL Server baza podataka. Aplikacija radi **potpuno lokalno** — nema
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
7. [Rad sa bazom podataka](#rad-sa-bazom-podataka)
8. [Rješavanje čestih problema](#rješavanje-čestih-problema)
9. [Provjera koda (testovi i CI)](#provjera-koda-testovi-i-ci)
10. [Poznata ograničenja](#poznata-ograničenja)

---

## Šta ti treba prije početka

Instaliraj sljedeće na svoj računar (sve je besplatno):

| Alat | Za šta služi | Gdje preuzeti |
|---|---|---|
| **.NET 8 SDK** | Pokreće backend (C#/.NET Web API) | [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Node.js 20+** | Pokreće frontend (React) | [nodejs.org](https://nodejs.org) (uzmi LTS verziju) |
| **SQL Server** (Express edition je dovoljna, besplatna) | Baza podataka | [Preuzmi SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads) |
| **SQL Server Management Studio (SSMS)** *(preporučeno, nije obavezno)* | Grafički alat da vidiš/pretražuješ bazu | [Preuzmi SSMS](https://learn.microsoft.com/sql/ssms/download-sql-server-management-studio-ssms) |
| **Git** | Da preuzmeš/upravljaš kodom | [git-scm.com](https://git-scm.com) |

Provjeri da li su .NET i Node ispravno instalirani (otvori terminal/PowerShell i ukucaj):

```bash
dotnet --version
```
Treba da ispiše nešto što počinje sa `8.` (npr. `8.0.404`).

```bash
node --version
```
Treba da ispiše `v20` ili novije.

---

## Struktura projekta

```
backend/    ASP.NET Core 8 Web API
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
cd ProjekatTIKETSISTEM
```

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
- **`GoogleAi:ApiKey`** — *opciono, ali potrebno za AI asistenta.* Besplatan ključ: [aistudio.google.com](https://aistudio.google.com/app/apikey).
- **`Brevo:ApiKey`** — *opciono.* Potrebno samo ako želiš da "zaboravljena lozinka" i pozivnice stvarno šalju
  email. Besplatan nalog: [brevo.com](https://www.brevo.com).
- **`MicrosoftGraph`** — *opciono.* Potrebno samo za Outlook email integraciju (pretvaranje email-ova u
  tikete). Slobodno ostavi prazno ako ti ne treba.

### 4. Pokreni backend

```bash
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

Ako privremena lozinka istekne prije nego je iskoristiš (važi 20 minuta po defaultu), samo restartuj
backend (`Ctrl+C` pa opet `dotnet run ...`) — pošto nalog s tim emailom već postoji, prijaviš se preko
"Zaboravljena lozinka" (radi samo ako je `Brevo:ApiKey` podešen) ili zatraži da se lozinka ponovo generiše
direktno u bazi.

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

---

## Objašnjenje svake konfiguracijske postavke

| Postavka | Obavezno? | Šta radi |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | ✅ Da | Kaže backend-u gdje mu je SQL Server baza. |
| `Jwt:Secret` | ✅ Da | Tajni ključ kojim se potpisuju login sesije (tokeni). Mora ostati isti dok je aplikacija u upotrebi — ako ga promijeniš, svi su izlogovani. |
| `Jwt:AccessTokenMinutes` | Ne (default 30, u primjeru 120) | Koliko dugo login sesija traje prije nego se mora ponovo ulogovati. |
| `Pii:HashKey` | ✅ Da (obavezno izvan Development) | Ključ HMAC hasha kojim se nalozi traže po emailu. **Ne mijenjaj ga nakon što već postoje nalozi** — niko se više ne bi mogao ulogovati. Enkripcija sadržaja koristi ključeve iz tabele `DataProtectionKeys`, koja mora biti u svakom backupu. |
| `Storage:UploadsPath` | Ne | Folder za priloge tiketa i dokumente baze znanja (default `App_Data/uploads` pored aplikacije). Ne servira se javno; za produkciju postavi putanju **van** foldera koji se redeploya. Prilozi se otvaraju kroz potpisane linkove koji ističu za 4 sata. |
| `SeedAdmin:Email` / `Password` | ✅ Email da, lozinka ne | Email za prvi Admin nalog koji se automatski pravi. Ako ostaviš `Password` prazno, generiše se nasumična privremena lozinka (ispiše se u terminalu). |
| `Startup:Migrate` | Ne (default `true`) | Da li backend pri pokretanju sam primjenjuje migracije i jednokratne popravke baze. Postavi na `false` ako migracije radiš kao zaseban korak (`dotnet TicketSystemTech.Api.dll --migrate-only` — primijeni migracije, seeduje prvog admina i izađe); backend tada odbija da krene na neažuriranoj bazi. Više instanci koje krenu istovremeno čekaju jedna drugu (SQL Server zaključak), pa se migracije ne izvršavaju paralelno. |
| `Sla:Mode` | Ne (default `CalendarHours`) | Kako se računa rok tiketa iz SLA plana. `CalendarHours` = sati teku non-stop (plan od 24 h = rok za 24 h). `BusinessHours` = broje se samo radni sati. Rok koji osoblje ručno unese uvijek ima prednost. |
| `Sla:TimeZoneId`, `Sla:WorkdayStart`, `Sla:WorkdayEnd` | Ne | Samo za `BusinessHours`: vremenska zona (default `Europe/Sarajevo`) i radno vrijeme (default `08:00`–`16:00`). |
| `Sla:WorkDays`, `Sla:Holidays` | Ne | Samo za `BusinessHours`: radni dani (default ponedjeljak–petak) i neradni datumi (`yyyy-MM-dd`). Ako ih postaviš, **zamjenjuju** default; ne dodaju se na njega. |
| `GoogleAi:ApiKey` | Za AI funkcije | Bez ovoga, AI asistent i pretraga tiketa ne rade (ostatak aplikacije radi normalno). |
| `Brevo:ApiKey` | Za slanje email-ova | Bez ovoga, "zaboravljena lozinka" i pozivnice se ne šalju (upozorenje u logu, aplikacija ne puca). |
| `MicrosoftGraph:ClientId` / `ClientSecret` | Za Outlook integraciju | Bez ovoga, samo Settings → Email Integracija ostaje neaktivna; ostatak aplikacije radi normalno. |
| `TemporaryPassword:ValidityMinutes` | Ne | Koliko dugo privremene lozinke (za nove naloge) važe prije isteka. |

---

## Rad sa bazom podataka

### Kako da saznam ime svoje SQL Server instance?

Otvori SSMS i pogledaj šta ti nudi u "Server name" polju kad pokušaš da se spojiš — to je puno ime tvoje
instance (npr. `DESKTOP-ABC123\SQLEXPRESS` ili samo `localhost` ako si instalirao podrazumijevanu instancu
bez imena). To ime (ili skraćeno `localhost\SQLEXPRESS`) ide u connection string.

### Šta ako sam instalirao podrazumijevanu (neimenovanu) instancu?

Onda ti u connection string-u ne treba `\SQLEXPRESS` uopšte — samo `Server=localhost;...`.

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
→ Provjeri da je `GoogleAi:ApiKey` postavljen, i da si pokrenuo indeksiranje (Admin → Settings →
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

**Zaboravio sam admin lozinku**
→ Restartuj backend — ako nalog i dalje postoji (samo je lozinka nepoznata), obriši ga direktno u bazi
(`AspNetUsers` tabela) i ponovo pokreni backend da se ponovo zasija sa svježom privremenom lozinkom.

---

## Provjera koda (testovi i CI)

Lokalno, iz korijena projekta:

```bash
cd backend && dotnet test
```

```bash
cd frontend && npm run lint && npm run build
```

`npm run build` uključuje provjeru TypeScript tipova. Isto rade i GitHub Actions
(`.github/workflows/ci.yml`) na svaki push na `main` i svaki pull request: backend se gradi i testira na Windowsu
(mora proći) i na Linuxu sa pravim SQL Serverom (informativno, dok se ne potvrdi da sve radi i tamo), frontend
prolazi lint, build i `npm audit`, a paketi sa **visokim ili kritičnim** poznatim ranjivostima obaraju build.

### Testovi autorizacije (integracijski)

Pored običnih jediničnih testova, `backend/tests/TicketSystemTech.Tests/Integration/` pokreće **pravu aplikaciju** (u
memoriji, sa pravim migracijama) nad privremenom bazom i provjerava ko šta smije:

- za **svaki** endpoint iz stvarno registrovanih ruta: anoniman poziv daje 401, a uloga koja nije dozvoljena 403; novi
  endpoint bez `[Authorize]` obara test dok se svjesno ne doda na listu javnih endpointa;
- izolaciju poslovnica: ko vidi koje tikete, klijente, osoblje, zadatke, notifikacije i koje izvore AI asistent koristi
  (uključujući pokušaje da se filterima proširi vidljivost);
- da token prestaje važiti kad se nalog deaktivira, premjesti ili promijeni lozinku.

Testovi trebaju SQL Server na kojem smiju praviti i brisati **svoje** baze (`TicketSystemTech_AuthzTest_*`; vaša
baza se ne dira). Na razvojnom računaru ništa ne treba podešavati: koriste server iz vašeg
`appsettings.Development.json`. Drugi server se zadaje varijablom okruženja `TST_SQL` (connection string, ime baze
se ignoriše). Bez ikakvog SQL Servera ovi testovi se **preskaču** (prikažu se kao *skipped*), ostali rade normalno.

---

## Poznata ograničenja

- **Prilozi (attachments)** se čuvaju na lokalnom disku (`Storage:UploadsPath`, podrazumijevano `App_Data/uploads`),
  van web roota, i otvaraju se samo preko potpisanih linkova koji ističu. To je normalno za lokalno pokretanje, ali
  za pravi produkcijski hosting treba ili disk koji se ne briše pri redeployu ili druga `IFileStorage` implementacija.
- Aplikacija je dizajnirana da radi **potpuno lokalno** (SQL Server + backend + frontend na istom računaru).
  Ne postoji trenutno cloud deployment (Render/Vercel/Supabase konfiguracija koja se ranije koristila je
  uklonjena kad je projekat prebačen na lokalni SQL Server).
- JWT login tokeni se ne obnavljaju automatski (refresh token rotacija) — kad token istekne, korisnik se
  jednostavno ponovo uloguje.
