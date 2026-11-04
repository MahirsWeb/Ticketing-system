# ADR 0002 — Slanje email-ova: outbox i SMTP

**Status:** Prihvaćeno i urađeno
**Datum:** 2026-11-04
**Zamjenjuje:** slanje preko Brevo API-ja (uklonjeno)

## Odluka

1. Email se **nikad ne šalje u zahtjevu** koji ga izaziva. Upisuje se kao red u tabelu `EmailOutbox`; radnik u pozadini
   (`EmailOutboxDispatcher`) ga šalje.
2. Transport je **generički SMTP preko MailKit-a** (`SmtpEmailTransport`, sekcija `Smtp`). Brevo se ne koristi.
3. Isporuka je **„najmanje jednom“**.

## Zašto

- Prije: `POST /api/tickets` je čekao na svaki email (poslovnica od 30 zaposlenih i spor server: minute), a red u memoriji je
  gubio poruke pri restartu i tiho odbacivao nove kad se napuni.
- SMTP je jedini način koji radi kod svakog kupca (ADR 0001, D6), uključujući izolovane mreže.

## Kako radi

- **Zaključavanje:** radnik uzima poruke uslovnim `UPDATE`-om (lease, `Email:Outbox:LeaseSeconds`). Ako radnik pukne, poruka se po
  isteku ponovo nudi. Više instanci ne šalje istu poruku dvaput.
- **Ishodi transporta:** `Sent`, `Skipped` (nema `Smtp:Host` ili `Smtp:FromAddress`, ili je primalac deaktiviran), `Retry`
  (SMTP `4xx`, mrežna greška, istek vremena, odbijena prijava), `Failed` (`5xx`, neispravna adresa).
- **Ponovni pokušaji:** 1, 2, 4, 8, 16 pa 30 minuta, najviše 8 pokušaja (`EmailRetryPolicy`). Odbijena prijava se ponavlja jer je
  popravlja operater podešavanjem, a poruke ne treba bacati.
- **Čišćenje:** poslane i preskočene poruke 7 dana, neisporučene 30 dana.
- **Privatnost:** adresa primaoca je u tabeli šifrovana kao i druge adrese; ne upisuje se u log.
- **Pravilo „deaktiviran račun ne prima email“** primjenjuje radnik, pa vrijedi za bilo koji transport.
- **Šifrovanje veze se ne spušta samo od sebe:** ako je traženo `StartTls`, a server ga ne nudi, poruka se ne šalje u čistom tekstu.

## Posljedice

- Duplikat je moguć samo ako aplikacija pukne između prihvatanja poruke na serveru i zapisa u bazu. To je cijena toga da se
  poruka ne izgubi; README to navodi.
- „Zaboravljena lozinka“ i dalje prvo prolazi kroz mali memorijski red (da vrijeme odgovora ne otkrije postoji li nalog), pa tek
  onda u outbox. Pad u tih nekoliko trenutaka znači da korisnik ponovo traži link.
- Dokazano testovima: `EmailOutboxTests`, `EmailRetryPolicyTests`, `SmtpEmailTransportTests` (pravi MailKit prema malom SMTP
  serveru na `localhost`: 4xx, 5xx, pogrešna prijava, server koji ćuti ili prekine vezu). **Nije probano** prema pravom
  poštanskom serveru.
