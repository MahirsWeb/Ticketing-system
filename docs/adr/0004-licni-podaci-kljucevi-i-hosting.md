# ADR 0004 — Lični podaci, ključevi i način pokretanja na serveru

**Status:** Prihvaćeno i urađeno, osim stavki u odjeljku „Šta nije urađeno“
**Datum:** 2026-11-04

## Odluka

1. Email, telefon i adresa se čuvaju **šifrovani** u bazi (ASP.NET Data Protection). Pretraga naloga po emailu ide preko HMAC
   hasha (`Pii:HashKey`), koji se ne smije mijenjati kad podaci postoje.
2. Ključevi za šifrovanje (**key ring**) su u istoj bazi (`DataProtectionKeys`). Na serveru se **šifruju X.509 certifikatom**
   (`DataProtection:CertificatePath` + `CertificatePassword`, ili `CertificateThumbprint`). Time kopija baze nije i kopija
   ključa. Certifikat pravi `--create-key-certificate <fajl.pfx> <lozinka>`.
3. U okruženju `Production` aplikacija **odbija da krene** (i nabroji sve razloge odjednom) ako: `Jwt:Secret` ili `Pii:HashKey`
   nisu postavljeni ili su kratki, `Frontend:BaseUrl` nije stvarna adresa, `AllowedHosts` je `*`, nema certifikata (osim uz
   svjestan `DataProtection:AllowUnprotectedKeys=true`), ili su `Smtp`/`Ai` postavke neispravne.
4. Proces u Docker slici **nije root**; imena slika su zakucana digestom; `HEALTHCHECK` pita sâma aplikacija
   (`--healthcheck`, koristi `AllowedHosts` da ne bude odbijena); prilozi i logovi su na volumenima.
5. **Povjerenje prema proxyju je izričito**: bez `ForwardedHeaders:KnownProxies/KnownNetworks` zaglavlja `X-Forwarded-*` se
   ignorišu (ni proxy na istom računaru nije povjeren dok se ne navede).
6. **Audit log** (`AuditLogs`) bilježi osjetljive radnje sa korisnikom, adresom i oznakom zahtjeva; lozinke i tekstovi poruka
   se u njega ne upisuju. Da bude samo za dodavanje, korisniku baze se oduzima `UPDATE`/`DELETE` (primjer u README-u).
7. Generisana lozinka prvog Admina **ne ide u log**. Prikazuje se samo na terminalu i samo uz
   `SeedAdmin:PrintGeneratedPassword=true` (uključeno u primjeru za razvoj). Nova se dobija komandom
   `--reset-admin-password <email>`, bez diranja baze rukom.

## Zašto

Prvi krug testiranja je pokazao da kopija baze nosi i brave i ključ, da su prijave iza proxyja dijelile jedno ograničenje i da je
lozinka admina završavala u logovima koje platforma čuva. Drugi krug je to ponovo mjerio.

## Šta nije urađeno

- **Ključevi napravljeni bez certifikata ostaju nešifrovani dok se ne pokrene re-key.** Data Protection svakih 90 dana pravi *novi* ključ, ali
  *stare ne briše* — potrebni su da se pročita ono što je njima šifrovano. Ako je instalacija prvi put pokrenuta bez certifikata,
  ti stari ključevi i dalje stoje u bazi nešifrovani, pa dodavanje certifikata kasnije štiti samo ono što se šifruje *poslije*
  njega, ne i ranije podatke. Za to postoji alat `--rekey-data` (README, DataProtection): pravi novi ključ, ponovo šifruje sve podatke pod njim i, uz
  `--revoke-old-keys`, povlači stare; backupi napravljeni prije toga ostaju čitljivi sa svojim ključevima. **Zaključak:** da bi zaštita
  ključeva zaista vrijedila, certifikat (`DataProtection:CertificatePath`/`Thumbprint`) mora biti podešen **prije prvog pokretanja**
  na serveru; nova instalacija je tada šifrovana od početka.
- **GDPR:** izvoz i anonimizacija na zahtjev postoje (T-10). Izvoz sadrži profil, tikete koje je osoba otvorila (s opisom), poruke,
  rad, metapodatke priloga, obavijesti, audit zapise te osobe, radnje drugih nad njenim nalogom i e-mailove poslane na njenu adresu
  (bez teksta). Period čuvanja i automatsko brisanje traže pravnu stranu.
- **Sesija** (T-18.1): kratki access token je samo u memoriji, dugi dio je refresh token u httpOnly kolačiću (README). Kolačić ne
  štiti od XSS-a koji radi u samoj stranici: skripta na istom originu može pozvati `/api/auth/refresh` i dobiti svjež access token.
  Zaštita od XSS-a ostaje sanitizacija i CSP.
- **CSP za SPA nije u repozitoriju.** Zaglavlje je ranije postavljao Vercel (`vercel.json`, uklonjen) i bilo je samo
  *Report-Only*. Sada ga mora postaviti web server koji služi `frontend/dist` (IIS, nginx...). API već šalje strog CSP za svoje
  odgovore. Preporuka je u README-u.
- **Docker slika i stvarni SMTP/Ollama nisu pokrenuti u ovom okruženju** (nema Docker engine-a); provjerava ih CI posao „Docker
  image“, koji je dok ne bude zelen jednom informativan.
