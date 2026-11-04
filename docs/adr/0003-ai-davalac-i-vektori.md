# ADR 0003 — AI: izbor davaoca, vektori u SQL Serveru, šta napušta sistem

**Status:** Prihvaćeno i urađeno
**Datum:** 2026-11-04

## Odluka

1. AI je **podešavanje, ne ugrađena stavka**: `Ai:Enabled`, `Ai:Provider` (`Gemini`, `OpenAiCompatible`, `None`), `Ai:Endpoint`,
   `Ai:ApiKey`, `Ai:ChatModel`, `Ai:EmbeddingModel`, `Ai:EmbeddingDimensions`. Stari naziv `GoogleAi:ApiKey` i dalje radi.
2. Davalac se bira iza dva postojeća interfejsa (`IEmbeddingService`, `IChatCompletionService`) sa dva tanka adaptera
   (`AiEmbeddingService`, `AiChatCompletionService`). Biblioteka `Microsoft.Extensions.AI` nije uvedena: dva adaptera su manje
   koda od njene zavisnosti, a interfejsi ostaju isti ako se kasnije odluči drugačije.
3. **Vektori ostaju u SQL Serveru kao `varbinary`**, a pretraga je u aplikaciji (`VectorIndex`). Ne koristi se SQL Server 2025
   `VECTOR` jer proizvod mora raditi na starijim izdanjima (ADR 0001, D4).

## Šta napušta sistem kad je AI uključen

Samo ako je davalac vani (Gemini): tekst pitanja i dijelovi tiketa i dokumentacije potrebni za odgovor, uz gornje granice
(pitanje 2.000 znakova, jedan izvor 6.000, kontekst 60.000, odgovor `Ai:MaxOutputTokens`). **Email adrese i brojevi telefona se
maskiraju** (`Ai:RedactPersonalData`, uključeno). Imena i slobodan tekst iz tiketa se ne maskiraju. Ključ ide u zaglavlje, ne u
adresu. Lokalni model (`OpenAiCompatible` na `localhost`) ne šalje ništa. `Ai:Enabled=false` ili `Provider=None` je potpuna
zabrana: ništa se ne šalje, a ekrani to kažu (`aiAvailable=false` u odgovoru).

Pravnu stranu (ugovor o obradi podataka, besplatni nivo davaoca koji može koristiti tekst za učenje) odlučuje vlasnik instalacije,
ne kod. README to kaže na jednom mjestu.

## Promjena modela

Svaki vektor nosi `EmbeddingModel` u obliku `model@veličina`. Pri promjeni modela ili veličine:
- pretraga preskače vektore koji ne odgovaraju (i upozori u logu) umjesto da vraća proizvoljne rezultate;
- *Reindex* zaboravlja neusklađene vektore i pravi nove, uključujući dokumente (`ForgetStaleVectorsAsync`), i radi i kad
  provajder trenutno nije dostupan (vektori se ipak zaborave, ne ostaju zauvijek);
- pretraga po ključnim riječima radi u SQL-u i ne zavisi od davaoca.

## Prioriteti poziva

Pitanje korisnika (`Interactive`) nikad ne čeka na pozadinsko indeksiranje (`Background`, razmak
`Ai:BackgroundMinIntervalSeconds`). Zatvaranje tiketa i dodavanje bilješke odgovaraju odmah; tiket se indeksira poslije, iz reda
(`KnowledgeBaseIndexQueue`), a isti tiket više puta u redu se indeksira jednom. Čekanje na `Retry-After` je ograničeno (5 s za
interaktivne, 60 s za pozadinske pozive).

## Dokaz

`AiSettingsTests`, `RetestDefectTests` (reindeks, pretraga po ključnim riječima, `aiAvailable`),
`AuditAndBackgroundWorkTests` (odgovor na zatvaranje ne čeka davaoca). **Nije probano** protiv pravog Googlea (osim ranije
pretrage) ni protiv pravog Ollama servera.
