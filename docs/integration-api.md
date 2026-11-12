# Tix: API za čitanje tiketa iz druge aplikacije

Ovo je uputa za programera aplikacije koja treba da čita tikete iz Tixa, na primjer aplikacije za evidenciju utrošenog vremena. Osoba se u obje aplikacije prepoznaje po **istoj e-mail adresi** (npr. `ime.prezime@firma.ba`).

Samo čitanje. Aplikacija se **ne javlja sama** drugim sistemima; druga aplikacija povremeno pita Tix, npr. svake minute.

## 1. Pristup: API ključ

Svaka aplikacija koja čita dobija svoj ključ. Ključ pravi administrator servera, jednom:

```
dotnet TicketSystemTech.Api.dll --create-integration-key vrijeme
```

Naredba ispiše ključ (počinje sa `tix_`) **samo tada**, i red za konfiguraciju:

```json
"Integration": {
  "Keys": [
    { "Name": "vrijeme", "Sha256": "…64 znaka…" }
  ]
}
```

Taj red ide u `appsettings.Production.json`, pa se Tix ponovo pokrene (postavke se čitaju pri pokretanju). U konfiguraciji je samo otisak (SHA-256) ključa, ne sam ključ. Ključ se predaje programeru druge aplikacije i čuva kao lozinka (ne u mailu, ne u kodu koji se dijeli). **Ukidanje:** obrisati red iz konfiguracije i ponovo pokrenuti Tix. **Zamjena:** napraviti novi ključ, dodati ga, prebaciti drugu aplikaciju na njega, obrisati stari.

Ključ se šalje u zaglavlju **`X-Api-Key`**, i samo preko HTTPS-a:

```
GET /api/integration/tickets?state=active
X-Api-Key: tix_…
```

Ključ otvara samo ova dva poziva. Token neke osobe tu ne vrijedi, a ključ ne vrijedi nigdje drugdje. Bez ispravnog ključa odgovor je `401`.

**Ključ vidi tikete svih poslovnica** (nije vezan za osobu ni za poslovnicu). Zato ga treba čuvati kao administratorsku lozinku, iako ne može ništa mijenjati.

## 2. Dva poziva

### 2.1 Ko je stavljen na koji tiket: `GET /api/integration/assignments`

Za aplikaciju koja pita redovno ("ima li nešto novo"). Vraća dodjele po vremenu dodjele, od najstarije.

| Parametar | Obavezan | Značenje |
|---|---|---|
| `since` | da | Vrijeme u UTC, npr. `2026-11-19T08:00:00Z`. Bez oznake zone čita se kao UTC. |
| `afterId` | ne | `nextAfterId` iz prethodnog odgovora (vidi niže) |
| `email` | ne | Samo dodjele te osobe. Velika i mala slova nisu bitna. Adresa koju niko nema daje prazan odgovor, ne grešku. |
| `limit` | ne | Najviše stavki u odgovoru, 1 do 500 (zadano 100) |

Odgovor:

```json
{
  "items": [
    {
      "assignmentId": "9c1f…",
      "assignedAtUtc": "2026-11-19T08:15:02.1234567Z",
      "isPrimary": true,
      "assignee": { "email": "ime.prezime@firma.ba", "name": "Ime Prezime" },
      "ticket": {
        "id": "5b7e…", "ticketNumber": "100123", "title": "Ne radi štampač",
        "description": "<p>…</p>", "status": "Open", "priority": "High", "source": "Phone",
        "helpTopic": "Hardver", "branch": "Podrška Sarajevo", "subBranch": null, "company": "Alfa d.o.o.",
        "client": { "email": "klijent@alfa.ba", "name": "Mirza Klijentić" },
        "createdAt": "…", "openedAtUtc": "…", "dueDateUtc": "…", "closedAtUtc": null, "resolutionSummary": null,
        "url": "https://…/tickets/5b7e…",
        "assignees": [ { "email": "ime.prezime@firma.ba", "name": "Ime Prezime", "isPrimary": true, "assignedAtUtc": "…" } ]
      }
    }
  ],
  "nextSince": "2026-11-19T08:15:02.1234567Z",
  "nextAfterId": "9c1f…",
  "hasMore": false
}
```

- `isPrimary` = osoba kojoj je tiket "dodijeljen"; ostali na tiketu pomažu.
- **Kako nastaviti:** u sljedeći poziv stavi `since` = `nextSince` i `afterId` = `nextAfterId`. Ako nije bilo ničeg novog, vraćaju se isti `nextSince` i `nextAfterId`, pa se može pitati iznova bez rizika. Ako je `hasMore` `true`, pitaj odmah ponovo.
- Dodjele napravljene u istom trenutku (npr. grupna izmjena) ne gube se i ne ponavljaju, ako se šalju oba, `since` i `afterId`. Ako se čuva samo vrijeme, posljednja stavka može doći još jednom: prepoznaje se po `assignmentId`.

### 2.2 Tiketi kakvi su sada: `GET /api/integration/tickets`

Za početno punjenje i za provjeru (vidi 3). Vraća tikete, najnovije prvo.

| Parametar | Obavezan | Značenje |
|---|---|---|
| `assigneeEmail` | ne | Samo tiketi na kojima je ta osoba (glavna ili pomoćna) |
| `state` | ne | `active` (zadano: Open, InProgress, Resolved), `closed` ili `all` (uključuje i nove tikete koje niko nije preuzeo) |
| `page`, `pageSize` | ne | Stranica (od 1) i veličina, najviše 100 (zadano 50) |

Odgovor: `{ "items": [ …tiket kao gore… ], "page": 1, "pageSize": 50, "totalCount": 123 }`.

## 3. Preporučeni način rada

1. **Pri pokretanju:** povući stanje: `tickets?state=active`, stranicu po stranicu (ili za jednu osobu s `assigneeEmail`).
2. **Svake minute:** `assignments?since=…&afterId=…`, pa za svaku novu dodjelu napraviti ili osvježiti zapis u svojoj aplikaciji (po `ticket.id`). Dodjele koje su već viđene (isti `assignmentId`) preskočiti.
3. **Povremeno (npr. svakih 15 do 30 minuta):** ponovo povući `tickets?assigneeEmail=…&state=active` za osobe koje vas zanimaju i uskladiti. To je potrebno jer **niz dodjela ne javlja** da je osoba skinuta s tiketa, niti da je tiket u međuvremenu zatvoren ili mu je promijenjen status: to se vidi samo u stanju.

## 4. Šta se ne vidi

Poruke i interne bilješke, prilozi, interna kategorija tiketa, broj telefona, koliko je ko radio na tiketu, i sve što ima veze s lozinkama i prijavom. Opis tiketa (`description`) je tekst koji je napisao klijent ili zaposlenik i **može sadržati lične podatke**; druga aplikacija treba da ga čuva u skladu s tim.

## 5. Ograničenja i greške

| Odgovor | Kada |
|---|---|
| `401` | nema ključa, ključ nije ispravan, ili je poslan token neke osobe |
| `400` | `since` nedostaje ili nije vrijeme; `state` nije `active`, `closed` ili `all` (poruka kaže koji) |
| `429` | više od 120 poziva u minuti s jednim ključem (`RateLimits:IntegrationPerMinute`) |

- Sva vremena su UTC (ISO 8601, sa `Z`).
- Nije u realnom vremenu: kašnjenje je onoliko koliko je razmak između dva pitanja.
- Tiket nema "vrijeme zadnje izmjene", pa se promjene (status, zatvaranje) otkrivaju ponovnim čitanjem stanja, ne niza dodjela.
- Tix radi kao jedna instanca; za čitanje nije potrebno ništa posebno, osim mrežnog pristupa aplikaciji i ključa.

## 6. Brza provjera

```bash
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/tickets?assigneeEmail=ime.prezime@firma.ba"
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/assignments?since=2026-11-01T00:00:00Z&limit=10"
```
