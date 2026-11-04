# ADR 0001 — Hosting, model proizvoda i platformske odluke

**Status:** Prihvaćeno, uz izmjene od 2026-11-04 koje čekaju potvrdu vlasnika (označene sa ⚠ u tabeli)
**Izvor:** odgovori vlasnika od 2026-10-16 (dokument koji je napisao tim za testiranje) i kasnije odluke vlasnika
**Šta je ovaj dokument:** isti spisak odluka, ali sa stanjem u kodu na dan 2026-11-04. Stupac *Stanje* kaže šta u repozitoriju
zaista postoji, a ne šta je bilo planirano. Gdje se stanje razlikuje od prvobitne odluke, to piše otvoreno.

## 1. Kontekst

Aplikacija je bila pripremljena za Render (API), Vercel (SPA) i Brevo (email). Odlučeno je da se ukloni zavisnost od njih i da se
aplikacija može hostovati lokalno, na našem hostingu ili na hostingu kupca. Ticket System Tech se tretira kao proizvod, ne kao
alat jedne kompanije.

## 2. Odluke i stanje

| # | Odluka | Stanje u kodu (2026-11-04) |
|---|---|---|
| D1 | Interni helpdesk alat koji se nudi kupcima | Prihvaćeno. Pristup je unutar mreže kupca ili preko našeg hostinga. |
| D2 | Instanca po kupcu (single-tenant) u v1, zajednička multi-tenant instalacija u v2 | ⚠ **Samo single-tenant.** Multi-tenant nije počet; strategija izolacije (`TenantId` ili baza po kupcu) nije izabrana (O6). Izvan obima dok vlasnik ne zatraži v2. |
| D3 | Docker primarni cilj; IIS na Windows Serveru mora biti podržan | Docker: urađen i ojačan (vidi ADR 0004). ⚠ **IIS paket (zip) i `docker-compose` primjer nisu urađeni.** Aplikacija radi na IIS-u kao običan ASP.NET Core host, ali CI ne pravi zip niti je IIS proban. Izvan obima dok kupac sa IIS-om ne postoji. |
| D4 | SQL Server, sve edicije, vektori bez `VECTOR` tipa | ⚠ **Minimalna verzija je SQL Server 2019, ne 2017.** Kod generiše SQL za nivo kompatibilnosti 150 (`Program.cs`), README traži 2019, CI testira na 2022. SQL Server 2017 nije testiran i ne obećava se; 2017 je i van Microsoftove glavne podrške. Vektori su `varbinary` i pretraga je u aplikaciji (ADR 0003). Express: ograničenje od 10 GB i backup bez SQL Agenta su u README-u. |
| D5 | Izlaz na internet nije poznat, mora raditi u obje varijante | Urađeno kroz konfiguraciju: `Ai:Enabled=false` ili lokalni davalac ne šalje ništa napolje; bez `Smtp:Host` i `MicrosoftGraph:*` nema poziva ka tim servisima. Posebne podrške za HTTP proxy nema (važe sistemske postavke .NET-a). Offline isporuka (`docker save`) nije dokumentovana. |
| D6 | Generički SMTP, outbox | **Urađeno** (ADR 0002). Brevo je uklonjen iz koda, konfiguracije i dokumentacije. |
| D7 | Dolazni email: Microsoft Graph i IMAP | Graph radi (poslovanje po instalaciji, OAuth sa `state` i PKCE). ⚠ **IMAP nije urađen.** `TenantId` po instalaciji nije posebno podešavan. |
| D8 | AI: lokalni ili cloud, po izboru instalacije | **Urađeno** (ADR 0003): `Ai:Provider` (Gemini, OpenAI-kompatibilan endpoint kao Ollama, None) i `Ai:Enabled`. Nije korišten `Microsoft.Extensions.AI`; dva mala vlastita adaptera iza istih interfejsa. Ugovor o obradi podataka (DPA) za cloud je stvar kupca. |
| D9 | Prijava osoblja: samo lokalni nalozi + MFA | ⚠ **MFA nije urađen i vlasnik ga ne želi** (odluka od 2026-10-19: „MFA ne treba“). Ovaj red u prvobitnom ADR-u je bio u suprotnosti s tom odlukom. Zaštita prijave: zaključavanje naloga, ograničenje po nalogu i po adresi, opoziv sesija pri promjeni lozinke. SSO nije u planu. |
| D10 | CI/CD na GitLabu ili Gitea | ⚠ **CI je GitHub Actions** (`.github/workflows/ci.yml`), a izbor GitLab/Gitea nije donesen (O4). Sintaksa je u velikoj mjeri prenosiva. Izvan obima dok vlasnik ne izabere. |
| D11 | Preko 500 korisnika po instalaciji | Dokumentovano: preporuka SQL Standard. Horizontalno skaliranje nije urađeno (jedna instanca; prilozi na disku, SignalR u memoriji; README to kaže). |
| D12 | RPO do 24 sata | Dokumentovano u README-u (Task Scheduler + `sqlcmd` za Express; backup mora ići uz certifikat za ključeve, ali odvojeno od njega). Testni restore se ne automatizuje. |
| D13 | File logovi + OpenTelemetry | File logovi: **urađeni** (rolling, `Logging:File:*`, u Dockeru `/data/logs`). Health `/health/live` i `/health/ready`: urađeni. Korelacioni ID: urađen. ⚠ **OpenTelemetry (OTLP) nije urađen.** |
| D14 | Key ring zaštićen X.509 certifikatom | **Urađeno** (ADR 0004): PFX fajl ili Windows cert store; u `Production` aplikacija ne kreće bez certifikata osim uz svjestan `DataProtection:AllowUnprotectedKeys`. |

## 3. Otvorene stavke

| # | Pitanje | Stanje |
|---|---|---|
| O1 | Vlasništvo koda i licenca | Nije razjašnjeno. `LICENSE` je „All Rights Reserved“ na ime jedne osobe; bez pisanog prenosa prava ili licence proizvod se ne smije nuditi kupcima. Blokator za komercijalno izdanje, ne za razvoj. |
| O2 | NPOI (OSMF naknada) | Odlučuje se naknadno. |
| O3 | Postojeći podaci iz Render ere | Zatvoreno: baza je ispitna, podaci su zamjenjivi (izjava vlasnika 2026-10-19). |
| O4 | GitLab ili Gitea | Otvoreno (D10). |
| O5 | Odluke o proizvodu (konfigurabilni workflow i SLA, primarni jezik, MediatR/FluentValidation) | Otvoreno. |
| O6 | Strategija izolacije za multi-tenant | Otvoreno (D2). |

## 4. Šta je potrebno od vlasnika

Stavke označene sa ⚠ su izmjena ili suženje prvobitne odluke, napravljeno da dokument odgovara stvarnom stanju: D4 (2019 umjesto
2017), D9 (bez MFA, to je vlasnik već odlučio), te D2, D3, D7, D10 i D13 (OpenTelemetry) kao „izvan obima dok se ne zatraži“. Ako
se neka od njih ipak traži, treba je upisati kao zadatak; dok se ne potvrdi, kriteriji prihvatanja
([../kriteriji-prihvatanja.md](../kriteriji-prihvatanja.md)) tretiraju ih kao *prihvaćeno uz obrazloženje*, ne kao *dokazano*.
