# Otvorene stavke: šta nije ispravljeno i šta je ispravljeno samo djelimično

**Stanje:** poslije trećeg kruga ispravki (2026-11-04). Detalji i dokazi su u `IZVJESTAJ_ISPRAVKI.md` (odjeljci 9.8 i 9.9).
**Testovi:** 571 backend i 117 frontend testova prolaze, bez preskočenih. Format, ranjivi paketi, migracije u odnosu na model, lint, `tsc`, build i `npm audit` (visoke) su čisti.
**Izvor:** analiza `ANALYSIS_1` (T-01 … T-64), ponovno testiranje commita `7af778a` (149 scenarija) i moje ponovno izvođenje scenarija na izoliranoj kopiji.

## 1. Sažetak

| Stavke analize (64) | Broj |
|---|---|
| Riješeno | **41** |
| Djelimično | **12** |
| Nije rađeno | **10** |
| Isključeno odlukom vlasnika (MFA) | **1** |

U ovom krugu je riješeno ili značajno pomaknuto: preklapanje vremena iste osobe, slike s tuđih adresa, lozinke prema AI modelu, GDPR izvoz i anonimizacija, greška po ruti, jedinstven fokus, obrazloženja uz `eslint-disable`, `.mailmap`, `CONTRIBUTING.md`, zakucavanje CI akcija, primjeri za nginx, IIS i `docker-compose`. Ispod je **samo ono što je ostalo**.

## 2. Nije ispravljeno (10 stavki)

| Stavka | Šta je ostalo | Zašto | Šta treba |
|---|---|---|---|
| **T-15** (TS-15.1, 15.2) | Poslovna logika je u kontrolerima; MediatR i FluentValidation su registrovani bez ijednog handlera | Veliki refaktor. Novi dijelovi su pisani kao zasebne klase | Planiran refaktor, ili ukloniti nekorištene pakete |
| **T-17** (TS-17.1) | Nema React Querya; podaci se učitavaju ručno | Veliki frontend refaktor. Zaštite od starih odgovora su ipak dodane | Planiran refaktor |
| **T-20** | Aplikacija radi kao jedna instanca (prilozi na disku, SignalR i keš u memoriji) | Epic za skaliranje; README to navodi kao ograničenje | Samo ako zatreba više od jedne instance |
| **T-31** (TS-31.1, 31.2) | Email → tiket nije atomski ni idempotentan; nema povezivanja odgovora u niti | Veća funkcionalnost | Posebna izrada |
| **T-41** | Tipovi na frontendu se održavaju ručno | Nije rađeno | Generisanje tipova iz OpenAPI dokumenta |
| **T-42** | Predimenzionirane stranice, duplirane forme | Nije rađeno | Refaktor |
| **T-45** | i18n pokriva oko 5 % interfejsa | Veliki posao | Prevod i izmjena komponenti |
| **T-47** (TS-47.1) | Kontroleri direktno zovu `IEmailSender`; šabloni su C# stringovi | Nije rađeno | Jedna tačka za notifikacije, šabloni kao resursi |
| **T-48** (TS-48.1) | Nema verzija API-ja, webhookova ni mašinskih kredencijala | Nije rađeno | Samo ako ih kupci traže |
| **T-51** (TS-51.1) | NPOI je pod OSMF licencom s naknadom; nema zabilježene odluke | Pravno pitanje | **Odluka vlasnika:** plaćanje, build iz izvora ili zamjena biblioteke |

Isključeno odlukom vlasnika: **T-28 MFA**.

## 3. Djelimično (12 stavki)

| Stavka | Šta je urađeno | Šta je ostalo |
|---|---|---|
| **T-10** GDPR | Admin izvozi sve podatke o osobi i anonimizira osobu (API i dugmad na stranici osobe); audit log za osjetljive radnje | Period čuvanja i automatsko brisanje; imena koja su drugi ukucali u tekst svojih poruka; kopije u backupima; dugmad nisu isprobana u pregledniku |
| **T-11** AI i lični podaci | Davalac je podešavanje; maskiraju se email, telefon i lozinke/tokeni uz riječ koja ih imenuje; ograničenja dužine | Ugovor o obradi podataka s davaocem (vlasnik). Imena i slobodan tekst odlaze vanjskom davaocu. U bazi tekst stoji nemaskiran (TS-11.2) |
| **T-18** sesije | Opoziv tokena, hub se gasi pri odjavi | JWT je u `localStorage`, nema refresh tokena (TS-18.1) |
| **T-23** zaglavlja | API šalje zaglavlja; `AllowedHosts` ograničen; gotovi primjeri za nginx i IIS | Primjena na stvarnom serveru |
| **T-37** upiti i indeksi | Novi indeksi, paginirana lista korisnika | Izvještaji učitavaju cijele tikete; N+1 u izvozu |
| **T-38** RAG | Model i veličina vektora uz svaki vektor; reindeks pri promjeni modela | `taskType`; dimenzija 768 (traži ponovno računanje svih vektora); ograničenje veličine chunka (TS-ONP-04.1) |
| **T-40** otkazivanje zahtjeva | Globalni handler, jedinstven oblik greške, otkazivanje u izvještajima i AI-ju | Prosljeđivanje u svih ~90 akcija |
| **T-46** pristupačnost | Kontrast, nazivi, jedinstven fokus, greška po ruti, naslov i `lang` | Provjera čitačem ekrana |
| **T-49** topologija | Render, Vercel, Brevo uklonjeni; README i ADR; primjeri za `docker-compose`, nginx, IIS | IIS paket (zip) za backend; više instanci; primjeri nisu isprobani |
| **T-52** licenca | `CONTRIBUTING.md`, `.mailmap` | **Odluka o licenci i vlasništvu (vlasnik, pravno)**; `THIRD-PARTY-NOTICES` |
| **T-53** README | Prepisan prema stvarnom stanju | Nisam ga prošao od nule na čistom računaru |
| **T-58** sitnice | Uklonjeni ostaci, brojevi tiketa se sortiraju kao brojevi | Upozorenja analizatora, magični stringovi, struktura foldera |

Iz scenarija testera, još otvoreno: **TS-34.2** (dva izvora zaduženja), **TS-02.3** (`IFileStorage` bez async metoda), **TS-10.3** (imena osoblja kopirana u tekst događaja), **TS-12.1** (zaštitu grane `main` podešava vlasnik na GitHubu), **TS-54.1** (nema drugog autora ni recenzenta u historiji, što kod ne može riješiti).

## 4. Nalazi testera

Provjereni u kodu i **riješeni** u ovom krugu: preklapanje vremena (NOVO-DOM-01), slike s tuđih adresa (NOVO-FE-06, NOVO-SEC-07), CI akcije vezane za tag (NOVO-OPS-16).

**Oko 60 ostalih statičkih nalaza** (`NOVO-*`) nisam sistematski obradio. Tester ih je označio kao "nije ponovo provjereno"; spisak je u `PREGLED-SVIH-NALAZA.md` u paketu testera.

## 5. Nije provjereno

- **Scenariji u pregledniku:** TS-17.3, 17.4, 19.2, 19.3, 19.4, 46.5 (pokriveni samo Vitest testovima). Dugmad za izvoz i anonimizaciju nisu isprobana u pregledniku.
- **Živi servisi:** pravi SMTP server, pravi Google i Ollama, pravi reverse proxy (u testovima su zamijenjeni lažnim serverima).
- **Primjeri za server:** `docker-compose.example.yml`, nginx i IIS primjeri nisu pokrenuti (nema Docker engine-a ni nginx-a lokalno); YAML i XML su sintaksno provjereni.
- **SQL Server 2017** (minimum je 2019), čitač ekrana, opterećenje.

## 6. Odluke vlasnika koje čekaju

- **T-51 (NPOI) i T-52 (licenca i vlasništvo):** pravna pitanja; blokatori za nuđenje kupcima.
- **Izmjene ADR-a 0001** označene sa ⚠ (`docs/adr/0001-hosting-i-model-proizvoda.md`): SQL Server **2019** umjesto 2017; bez **MFA**; **IIS paket, GitLab/Gitea, IMAP, multi-tenant i OpenTelemetry** su "izvan obima dok se ne zatraži".
- **T-18:** da li se token prebacuje iz `localStorage` u kolačić (veća izmjena).
- **Zaštita grane `main`** na GitHubu (Settings → Branches): zahtijevati pull request i provjere *Backend (Windows)*, *Backend (Linux, with SQL Server)* i *Frontend*.

## 7. Šta bih uradio sljedeće

1. **Odluke vlasnika** iz odjeljka 6; bez njih se T-51 i T-52 ne mogu zatvoriti.
2. **Preostali statički nalazi testera** (~60), jedan po jedan: test koji pada, popravka, mutacijska provjera.
3. **Srednji poslovi koji se mogu raditi odvojeno:** T-40 (otkazivanje u svim akcijama, mehanički), T-37 (izvještaji bez učitavanja cijelih tiketa), T-58 (analizator), TS-10.3 (strukturirani događaji umjesto imena u tekstu).
4. **Veliki refaktori** (T-15, T-17, T-31, T-47, T-45) tek poslije toga, svaki kao zaseban zadatak, jer svaki mijenja način rada cijele aplikacije.
