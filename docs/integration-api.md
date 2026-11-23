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

Ključ otvara samo pozive pod `/api/integration` (opisani niže). Token neke osobe tu ne vrijedi, a ključ ne vrijedi nigdje drugdje. Bez ispravnog ključa odgovor je `401`.

**Ključ vidi tikete svih poslovnica** (nije vezan za osobu ni za poslovnicu). Zato ga treba čuvati kao administratorsku lozinku, iako ne može ništa mijenjati.

## 2. Pozivi

| Poziv | Za šta |
|---|---|
| `GET /api/integration/assignments` | ko je stavljen na koji tiket, od zadnjeg pitanja |
| `GET /api/integration/events` | šta se desilo s tiketima od zadnjeg pitanja: status, zatvaranje, skidanje osobe… |
| `GET /api/integration/tickets` | tiketi kakvi su sada, po osobi i stanju |
| `GET /api/integration/tickets/{id}` | jedan tiket kakav je sada, u bilo kom statusu |
| `GET /api/integration/time-entries` | ko je radio, kada i koliko: zapisi vremena za period (vidi 2.6) |

Tiket u svim odgovorima izgleda isto (vidi 2.1). Dodatno se može tražiti i utrošeno vrijeme na tiketu (vidi 2.5).

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
| `createdFrom`, `createdTo` | ne | Samo tiketi napravljeni u tom periodu: vrijeme u UTC ili samo datum (`2026-11-01`). `createdFrom` je uključen, `createdTo` nije, pa sljedeći period počinje tamo gdje ovaj završava. |
| `page`, `pageSize` | ne | Stranica (od 1) i veličina, najviše 100 (zadano 50) |
| `includeWork` | ne | `true` dodaje utrošeno vrijeme na svaki tiket (vidi 2.5). Zadano `false`. |

Odgovor: `{ "items": [ …tiket kao gore… ], "page": 1, "pageSize": 50, "totalCount": 123 }`.

Redoslijed je uvijek **od najnovije napravljenog** (po `createdAt`, a kod istog vremena po `id`). Zato se tiketi od nekog datuma mogu uzeti i bez filtera: čitaj stranicu po stranicu dok ne dođeš do starijeg od datuma. Filter `createdFrom` to radi umjesto tebe, i `totalCount` se odnosi samo na traženi period.

### 2.3 Jedan tiket: `GET /api/integration/tickets/{id}`

`{id}` je `id` tiketa, onaj koji dolazi u ostalim odgovorima. Vraća tiket **kakav je sada**, u bilo kom statusu (i zatvoren, i onaj na kojem niko nije), ili `404` ako takvog tiketa nema. Parametar `includeWork=true` dodaje utrošeno vrijeme (vidi 2.5).

Za šta služi: kad tiket nestane iz liste aktivnih tiketa neke osobe, ovaj poziv kaže zašto:

- `status` je `Closed` → tiket je zatvoren (`closedAtUtc` i `resolutionSummary` kažu kada i kako);
- na listi `assignees` nema te osobe → skinuta je s tiketa (ili je tiket premješten u drugu poslovnicu: tada niko nije na njemu, a `branch` je nova poslovnica);
- inače je tiket i dalje aktivan, samo je status drugačiji.

### 2.4 Šta se desilo: `GET /api/integration/events`

Za aplikaciju koja pita redovno, kao i niz dodjela, ali kaže i ono što niz dodjela ne može: promjenu statusa, zatvaranje, ponovno otvaranje, skidanje osobe i premještaj u drugu poslovnicu. Vraća događaje po vremenu, od najstarijeg.

| Parametar | Obavezan | Značenje |
|---|---|---|
| `since` | da | Vrijeme u UTC, kao u 2.1 |
| `afterId` | ne | `nextAfterId` iz prethodnog odgovora |
| `limit` | ne | 1 do 500 (zadano 100) |

Odgovor:

```json
{
  "items": [
    {
      "eventId": "3d2a…",
      "occurredAtUtc": "2026-11-19T09:40:11.1234567Z",
      "type": "StatusChanged",
      "ticketId": "5b7e…",
      "person": null,
      "fromStatus": "InProgress",
      "toStatus": "Resolved",
      "ticket": { …tiket kao u 2.1, ali kakav je SADA… }
    }
  ],
  "nextSince": "2026-11-19T09:40:11.1234567Z",
  "nextAfterId": "3d2a…",
  "hasMore": false
}
```

Nastavlja se potpuno isto kao niz dodjela (`since` = `nextSince`, `afterId` = `nextAfterId`; ako nema ničeg novog vraća se isto mjesto; ako je `hasMore` `true`, pitaj odmah ponovo).

| `type` | Značenje | `person` | `fromStatus` / `toStatus` |
|---|---|---|---|
| `Opened` | tiket je preuzet i dodijeljen | osoba kojoj je dodijeljen | – |
| `AssigneeAdded` | osoba je dodana na tiket | dodana osoba | – |
| `AssigneeRemoved` | osoba je skinuta s tiketa (ako je bila glavna, glavna postaje ona koja je najduže na tiketu) | skinuta osoba | – |
| `StatusChanged` | promijenjen status | – | prije / poslije |
| `Closed` | tiket je zatvoren | – | – |
| `Reopened` | zatvoren tiket je ponovo otvoren | – | – |
| `Transferred` | tiket je premješten u drugu poslovnicu; **svi koji su bili na njemu skinuti su s njega** | – | – |

- `ticket` je tiket **kakav je sada**, ne kakav je bio u trenutku događaja. Ako se kasnije promijenio, vidi se ono kasnije stanje (zato ne treba dodatno čitati tiket). Ako tiket više ne postoji, `ticket` je `null`, a `ticketId` je tu.
- `person` je osoba na koju se događaj odnosi, i kad više nije na tiketu (skinuta osoba je imenovana, a u `ticket.assignees` je nema). Tamo gdje ne važi je `null`.
- Svaki događaj ima svoj `eventId`; isti događaj se ne obrađuje dva puta.

**Šta događaji NE javljaju** (zato je potrebno i povremeno poređenje sa stanjem, vidi 3):

- tiket koji je napravljen već otvoren i dodijeljen: to javlja niz dodjela (2.1);
- izmjene opisa, prioriteta, roka i kategorije.

Kad se nalog zaposlenika deaktivira ili anonimizira, skida se s njegovih aktivnih tiketa i to je događaj `AssigneeRemoved`, kao i svako drugo skidanje. (Samo od ove verzije Tixa: ono što se desilo ranije nije zabilježeno.)

### 2.5 Utrošeno vrijeme: `includeWork=true`

Zadano se vrijeme **ne šalje**. Uz `includeWork=true` (na `tickets` i `tickets/{id}`) svaki tiket dobija dva polja više:

```json
"workSessions": [
  { "id": "7a3c…", "person": { "email": "ime.prezime@firma.ba", "name": "Ime Prezime" },
    "startedAtUtc": "2026-11-19T08:00:00Z", "endedAtUtc": "2026-11-19T09:30:00Z" },
  { "id": "c91e…", "person": { "email": "drugi@firma.ba", "name": "Drugi Radnik" },
    "startedAtUtc": "2026-11-19T10:00:00Z", "endedAtUtc": null }
],
"workedSeconds": 6300
```

- Svaka stavka je jedan period rada jedne osobe, onako kako je zabilježen u Tixu. `id` se ne mijenja dok period postoji (isti je kao `id` u 2.6). `endedAtUtc` je `null` dok period traje.
- `workedSeconds` je zbir svih perioda svih osoba na tom tiketu u sekundama; period koji još traje računa se do trenutka odgovora. Dvije osobe koje rade u isto vrijeme računaju se obje.
- Period se bilježi osobi koja je radila (glavnoj na tiketu u tom trenutku). Pomoćne osobe nemaju vlastitih perioda, one su samo na listi `assignees`. Period ostaje kod osobe i kad se tiket kasnije dodijeli drugoj.
- Ako niko nije bilježio rad, `workSessions` je prazna lista, a `workedSeconds` je `0`.
- Ovo je podatak o radu ljudi. Traži se samo ako zaista treba i čuva se u skladu s tim.

### 2.6 Zapisi vremena za period: `GET /api/integration/time-entries`

Za uvoz vremena u drugu aplikaciju. Vraća sve periode rada svih osoba koji su počeli u zadatom periodu, i na tiketima i na zadacima (interni rad bez tiketa i bez klijenta), od onog koji je počeo najranije.

| Parametar | Obavezan | Značenje |
|---|---|---|
| `from` | da | Prvi dan, `yyyy-MM-dd` (npr. `2026-11-01`). Dan je **kalendarski dan firme** (`Sla:TimeZoneId`, zadano Europe/Sarajevo), ne UTC. |
| `to` | ne | Zadnji dan, uključen. Bez njega do danas. |
| `email` | ne | Samo vrijeme te osobe. Adresa koju niko nema daje prazan odgovor. |
| `page`, `pageSize` | ne | Stranica (od 1) i veličina, najviše 500 (zadano 100) |

Period može biti najviše **92 dana** (`to` − `from` + 1); duži se traži u dijelovima, inače `400`.

```json
{
  "items": [
    {
      "id": "7a3c…",
      "kind": "Ticket",
      "ticketId": "5b7e…", "ticketNumber": "100123",
      "taskId": null, "taskTitle": null,
      "person": { "email": "ime.prezime@firma.ba", "name": "Ime Prezime" },
      "workDate": "2026-11-19",
      "startedAtUtc": "2026-11-19T08:00:00Z", "endedAtUtc": "2026-11-19T09:30:00Z",
      "isRunning": false,
      "seconds": 5400, "minutes": 90
    }
  ],
  "page": 1, "pageSize": 100, "totalCount": 37
}
```

- `kind` je `Ticket` ili `Task`. Za zadatak su `ticketId` i `ticketNumber` `null`, a `taskId` i `taskTitle` (naslov zadatka) popunjeni.
- `id` je stabilan i jedinstven: isti zapis ima isti `id` u svakom odgovoru dok postoji.
- `workDate` je dan u kojem je rad **počeo**, po vremenu firme. Rad koji počne u 23:30 po UTC 31. decembra u Sarajevu je već 1. januar. Rad koji pređe ponoć računa se na dan početka; `startedAtUtc` i `endedAtUtc` omogućavaju da se podijeli ako treba.
- `minutes` je trajanje zaokruženo na najbliži cijeli minut, `seconds` je tačno. Dok rad traje (`isRunning` je `true`) `endedAtUtc`, `seconds` i `minutes` su `null`: uvozi samo zapise koji su završeni.
- Tix **ne bilježi** opis šta je rađeno niti vrstu posla: zapis je samo osoba, tiket ili zadatak, početak i kraj.
- Jedan zapis je uvijek jedna osoba. Vrijeme jedne osobe se ne preklapa (Tix to ne dozvoljava, ni među tiketima ni među zadacima).

**Izmjena i brisanje.** Zapis se u Tixu može izmijeniti (vrijeme početka i kraja) i obrisati: to može osoba koja je radila, administrator, a za zapis na tiketu i osoba kojoj je tiket sada dodijeljen. Tix pri tome ne bilježi "izmijenjeno kada" na zapisu niti čuva obrisane, pa se promjene nalaze **ponovnim čitanjem perioda**: zapis koji je ranije uvezen, a više ga nema u odgovoru za isti period, obrisan je (ili mu je početak pomaknut na drugi dan). Zapis kojem su se promijenila vremena ima isti `id`, a drugačije `startedAtUtc`/`endedAtUtc`.

## 3. Preporučeni način rada

1. **Pri pokretanju:** povući stanje: `tickets?state=active`, stranicu po stranicu (ili za jednu osobu s `assigneeEmail`).
2. **Svake minute:** pitati niz događaja (`events?since=…&afterId=…`) **i** niz dodjela (`assignments?since=…&afterId=…`). Za svaki novi događaj ili dodjelu napraviti ili osvježiti zapis u svojoj aplikaciji po `ticket.id` (tiket je u odgovoru, kakav je sada). Ono što je već viđeno (isti `eventId`, isti `assignmentId`) preskočiti.
3. **Kad tiket nestane s nečije liste,** a nije stigao događaj: `tickets/{id}` kaže zašto (2.3).
4. **Povremeno (npr. svakih 30 do 60 minuta):** ponovo povući `tickets?assigneeEmail=…&state=active` za osobe koje vas zanimaju i uskladiti. Događaji ne pokrivaju baš sve (2.4), pa ovo ostaje mreža sigurnosti.
5. **Za svaku sigurnost,** s vremena na vrijeme pitati od `nextSince` **minus 1 minut** (bez `afterId`) i preskočiti već viđene `eventId` i `assignmentId`. Razlog: zapis dobija vrijeme kad je Tix počeo snimati, a snimanje završi djelić sekunde kasnije, pa se rijetko dogodi da zapis sa starijim vremenom postane vidljiv tek poslije onog sa novijim.

**Uvoz vremena:** za vrijeme za sve osobe uzmi `time-entries` (2.6), ne `includeWork`. Pri pokretanju uvezi period od kojeg želiš (po 92 dana). Poslije toga, npr. svakih 15 do 30 minuta, ponovo pročitaj **zadnjih nekoliko dana (npr. 35)** i uskladi: novo dodaj po `id`, izmijenjeno (isti `id`, drugačija vremena) osvježi, a ono što je ranije uvezeno u tom periodu a nema ga u odgovoru obriši. Starije od tog perioda smatraj zaključenim. Zapise sa `isRunning` = `true` ne uvozi dok ne završe.

`includeWork=true` (2.5) služi kad uz tiket treba i njegovo vrijeme (na `tickets` i `tickets/{id}`; ne na `events` i `assignments`: tamo se tiket šalje bez vremena).

## 4. Šta se ne vidi

Poruke i interne bilješke, prilozi, interna kategorija tiketa, broj telefona, i sve što ima veze s lozinkama i prijavom. Koliko je ko radio vidi se samo kad se traži (`includeWork=true`, 2.5) ili preko `time-entries` (2.6). Opis tiketa (`description`) je tekst koji je napisao klijent ili zaposlenik i **može sadržati lične podatke**; druga aplikacija treba da ga čuva u skladu s tim.

## 5. Ograničenja i greške

| Odgovor | Kada |
|---|---|
| `401` | nema ključa, ključ nije ispravan, ili je poslan token neke osobe |
| `400` | `since` nedostaje ili nije vrijeme; `state` nije `active`, `closed` ili `all`; `createdFrom`/`createdTo` nisu vrijeme ili je kraj prije početka; `from`/`to` (zapisi vremena) nisu dani oblika `yyyy-MM-dd`, kraj je prije početka ili je period duži od 92 dana (poruka kaže koji) |
| `404` | `tickets/{id}`: nema tiketa s tim `id` (i kad `id` nije u obliku GUID-a) |
| `429` | više od 120 poziva u minuti s jednim ključem (`RateLimits:IntegrationPerMinute`) |

- Sva vremena su UTC (ISO 8601, sa `Z`).
- Nije u realnom vremenu: kašnjenje je onoliko koliko je razmak između dva pitanja.
- Tiket nema "vrijeme zadnje izmjene". Promjene se prate događajima (2.4), a ono što događaji ne pokrivaju, poređenjem sa stanjem.
- Događaji se čitaju iz evidencije promjena (audit) i postoje od trenutka kad je Tix počeo da ih bilježi: za ono što se desilo prije nadogradnje nema ničeg osim stanja tiketa.
- Tix radi kao jedna instanca; za čitanje nije potrebno ništa posebno, osim mrežnog pristupa aplikaciji i ključa.

## 6. Brza provjera

```bash
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/tickets?assigneeEmail=ime.prezime@firma.ba"
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/assignments?since=2026-11-01T00:00:00Z&limit=10"
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/events?since=2026-11-01T00:00:00Z&limit=10"
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/tickets/<<ID-TIKETA>>?includeWork=true"
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/time-entries?from=2026-11-01&to=2026-11-30"
curl -H "X-Api-Key: tix_…" "https://<<ADRESA>>/api/integration/tickets?state=all&createdFrom=2026-11-01"
```
