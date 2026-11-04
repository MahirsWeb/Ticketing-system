# Kako doprinijeti

Ovo je kratak vodič za razvoj. **Pravni uslovi doprinosa i licenca projekta nisu ovdje riješeni**: vidi `docs/OTVORENE-STAVKE.md`
(T-52). Dok vlasnik ne odluči o licenci, doprinos se prima samo od osoba s kojima je to unaprijed dogovoreno.

## Prije nego što pošaljete izmjenu

Sve što sljedeće komande provjeravaju mora proći; isto radi CI na svakom pull requestu.

```bash
cd backend && dotnet test -c Release && dotnet format TicketSystemTech.sln --verify-no-changes
```
```bash
cd frontend && npm run lint && npx tsc -b && npm test && npm run build
```

Integracijskim backend testovima treba SQL Server na kojem smiju praviti i brisati **svoje** baze (`TicketSystemTech_AuthzTest_*`);
bez njega se preskaču, pa ih CI posao s Linuxom i SQL Serverom pokreće umjesto vas. Detalji su u README-u, odjeljak "Provjera koda".

## Pravila koja se ovdje provode

- **Svaka popravka ima test koji pada bez nje.** Prije predaje pokvarite popravku i provjerite da test pada, pa je vratite.
- **Nova migracija** za svaku izmjenu modela podataka (`dotnet ef migrations add ...`); CI pada ako model i migracije nisu usklađeni.
- **Nov endpoint** mora imati `[Authorize]` i mora se dodati u zapisanu politiku ruta u testovima autorizacije.
- **Lični podaci** (email, telefon, adresa) se ne upisuju u log ni u audit zapis; lozinke i tokeni nikad.
- **`eslint-disable` komentar** uvijek nosi obrazloženje (`-- razlog`).
- **Zavisnosti** se ažuriraju kroz Dependabot pull requeste; veliku promjenu verzije ne spajajte naslijepo.
- **Odluke** koje su važne za budućnost (hosting, model podataka, AI, ključevi) zapišite kao ADR u `docs/adr/`.
