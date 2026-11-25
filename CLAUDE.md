# CLAUDE.md – Perron

## Projekt

Perron besteht aus zwei Teilen:

1. **Perron MCP:** ein MCP-Server in .NET für den Schweizer öffentlichen Verkehr (Haltestellen, Verbindungen, Abfahrten) auf Basis von transport.opendata.ch.
2. **Perron Bench:** ein Eval-Harness in .NET, der dieselben Agent-Aufgaben gegen mehrere MCP-Server laufen lässt (den eigenen und bestehende) und Aufgabenerfolg, Tool-Aufrufe, Tokens, Kosten und Latenz vergleicht.

Die Leitfrage des Projekts: Wie stark beeinflusst Tool-Design (Anzahl Tools, Beschreibungen, Ergebnisformat), wie gut ein Agent Aufgaben löst? Das Ergebnis ist eine nachvollziehbare, faire Vergleichstabelle im README.

## Arbeitsweise: Spec-driven Development

Die Quelle der Wahrheit liegt in `specs/001-perron/`:

- `spec.md` beschreibt das Was und Warum: Anforderungen, Akzeptanzkriterien, Nicht-Ziele.
- `plan.md` beschreibt das Wie: Architektur, Stack, Tool-Design, Benchmark-Methodik, Entscheidungsprotokoll.
- `tasks.md` enthält die Umsetzungsschritte mit Checkboxen.

Regeln:

1. Vor jeder Aufgabe `spec.md`, `plan.md` und den betroffenen Task in `tasks.md` lesen.
2. Immer nur einen Task bearbeiten. Danach die Checkbox abhaken und in zwei, drei Sätzen zusammenfassen, was gemacht wurde.
3. Wenn eine Anforderung unklar ist oder der Plan nicht aufgeht: anhalten und nachfragen. Nie stillschweigend vom Plan abweichen. Eine Abweichung wird zuerst im Entscheidungsprotokoll von `plan.md` festgehalten und erst danach umgesetzt.
4. Nichts bauen, was nicht in `spec.md` steht. Ideen für später kommen in `spec.md` unter «Offene Fragen».
5. Zu jedem Task gehören Tests, die seine Akzeptanzkriterien prüfen.

## Befehle

Docker ist die Referenzumgebung. Was dort nicht läuft, gilt als nicht fertig.

```bash
cp .env.example .env                                   # einmalig, Werte eintragen
docker compose up -d --build                           # Perron MCP (HTTP) starten
docker compose ps                                      # Status inkl. Healthcheck
docker compose logs -f mcp
docker compose --profile bench run --rm bench check    # alle Server erreichbar? Tools auflisten
docker compose --profile bench run --rm bench run --suite smoke --servers perron
docker compose --profile bench run --rm bench run --suite full --servers all --repeats 3
docker compose --profile bench run --rm bench report   # Markdown-Report aus den letzten Läufen
docker compose down
```

`run` kostet API-Tokens. Vor einem `full`-Lauf über alle Server die geschätzten Kosten anzeigen lassen (`--dry-run`) und bei Sandro nachfragen.

Lokale Entwicklung:

```bash
dotnet restore
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/Perron.Mcp                    # HTTP-Transport auf :8080/mcp
dotnet run --project src/Perron.Mcp -- --stdio         # stdio-Transport
dotnet run --project src/Perron.Bench -- check
npx @modelcontextprotocol/inspector                    # manuell testen
```

In Claude Code einbinden:

```bash
claude mcp add --transport http perron http://localhost:8080/mcp
```

## Fairness im Benchmark

Der Vergleich mit fremden Servern ist der heikelste Teil des Projekts. Diese Regeln gelten immer:

- Alle Server bekommen dasselbe Modell, denselben System-Prompt, dieselben Aufgaben, dasselbe Limit an Tool-Aufrufen und dasselbe aktuelle Datum.
- Fremde Server werden mit fixierter Version betrieben, genau so, wie ihre Doku es vorsieht. Kein Patchen, kein Umschreiben ihrer Tools. Gehostete Server (z. B. der offizielle) lassen sich nicht fixieren: Dort wird pro Lauf ein Hash von `tools/list` gespeichert, und Änderungen werden im Report markiert.
- Aufgaben werden nach Fähigkeiten getaggt. Kann ein Server eine Fähigkeit gar nicht (z. B. Auslastung), zählt die Aufgabe für ihn als «nicht abgedeckt» und nicht als Fehler. Der Report zeigt beides getrennt.
- Die Aufgaben werden geschrieben, bevor die eigenen Tools gebaut werden (Task 1.3). Danach wird ein zurückgehaltenes Test-Set nur noch für den Schlussvergleich verwendet.
- `bench/tasks/test.jsonl` nie lesen, auswerten oder ändern, ausser in Task 1.3 (Einfrieren) und Task 4.3 (Schlussvergleich). `.claude/settings.json` sperrt das Lesen zusätzlich.
- Im README stehen Methodik, Versionen, Datum der Läufe und bekannte Schwächen des Benchmarks. Keine Aussagen, die die Daten nicht stützen.

## Docker

- Services in `docker-compose.yml`:
  - `mcp`: Perron MCP mit Streamable HTTP auf `/mcp`, Healthcheck auf `GET /health`.
  - `bench` (Profil `bench`): Perron Bench. Das Image enthält zusätzlich `uv` und die fremden Python-Server in fixierten Versionen (bei Build installiert, nicht zur Laufzeit), damit sie per stdio gestartet werden können.
- Dockerfiles: Multi-Stage-Builds (`mcr.microsoft.com/dotnet/sdk:10.0` zum Bauen, `mcr.microsoft.com/dotnet/aspnet:10.0` bzw. `runtime:10.0` zur Laufzeit), Ausführung als Non-Root-User.
- Secrets und Umgebung nur über `.env` (`env_file`). Keine Secrets im Image und keine in `docker-compose.yml`. Die Bench-Methodik (Modell, `effort`, `max_tokens`, Pausen) steht versioniert in `bench/bench.json`.
- `.dockerignore`: `.git`, `bin/`, `obj/`, `bench/results/`, `.env`.
- Neue Abhängigkeit, neue Konfiguration oder neuer Service: Dockerfiles, `docker-compose.yml` und `.env.example` im selben Task anpassen.
- Ports nur auf `127.0.0.1` binden.

## Git

- `main` ist jederzeit lauffähig. Nie direkt auf `main` committen, nie force-push auf `main`.
- Vor jedem Task: `git checkout main && git pull`, dann einen Branch `task/<nr>-<kurzname>` anlegen, z. B. `task/2.2-plan-trip`.
- Kleine, in sich geschlossene Commits nach Conventional Commits (`feat:`, `fix:`, `test:`, `bench:`, `docs:`, `refactor:`, `chore:`, `ci:`). Commit-Messages auf Englisch.
- Vor jedem Commit `git status` und `git diff --staged` prüfen. Nie committen: `.env`, Secrets, `bin/`, `obj/`.
- Benchmark-Ergebnisse und Traces in `bench/results/` werden committet: Sie sind die Grundlage für den README-Report.
- Ist der Task fertig (Definition of Done): Branch pushen und mit `gh pr create` einen Pull Request eröffnen. Titel: Task-Nummer und Name. Beschreibung: was umgesetzt wurde, wie es getestet wurde, Benchmark-Ergebnis falls relevant, Abweichungen vom Plan.
- Gemergt wird erst nach grüner CI, und zwar von Sandro, nicht von Claude.
- Ist `gh` nicht verfügbar oder schlägt der Push fehl: anhalten und melden, nicht improvisieren.

## Projektstruktur

```
src/
  Perron.Transport/      Client für transport.opendata.ch, Modelle, Caching
  Perron.Mcp/            MCP-Server: Tools, Varianten, Transports, /health
  Perron.Bench/          Harness: Server-Registry, Agent-Loop, Checks, Oracle, Report
tests/
  Perron.Transport.Tests/
  Perron.Mcp.Tests/
  Perron.Bench.Tests/
  fixtures/              echte API-Antworten als JSON
bench/
  bench.json             Methodik: Modell, effort, max_tokens, Pausen
  servers.json           Registry der Server und Varianten
  tasks/dev.jsonl        Entwicklungs-Set
  tasks/test.jsonl       zurückgehaltenes Test-Set
  results/               Ergebnisse, Traces, Reports
specs/001-perron/
.mcp.json
Directory.Build.props
Directory.Packages.props
docker-compose.yml
.github/workflows/ci.yml
```

## Code-Konventionen

- .NET 10, C# mit `Nullable` aktiviert und `TreatWarningsAsErrors`. Paketversionen zentral in `Directory.Packages.props`.
- MCP über das offizielle SDK (`ModelContextProtocol`, `ModelContextProtocol.AspNetCore`), auch für die Clients im Bench (stdio und HTTP). LLM-Aufrufe über das offizielle Paket `Anthropic`. Vor dem Einsatz einer SDK-Funktion die aktuelle Doku oder die Samples prüfen, nicht aus dem Gedächtnis raten.
- HTTP-Clients über `IHttpClientFactory` mit `Microsoft.Extensions.Http.Resilience`.
- Zeiten immer explizit in `Europe/Zurich`. Keine impliziten Zeitzonen.
- Konfiguration über das Options-Pattern mit Validierung beim Start.
- Tool-Namen, Beschreibungen und Ergebnisformate sind Teil des Produkts: Jede Änderung braucht einen Bench-Lauf zum Vergleich (ab Phase 3).
- Tests mocken externe HTTP-Aufrufe (Fixtures) und verursachen nie API-Kosten. Benchmark-Läufe sind keine Tests und laufen separat.
- Logging strukturiert über `ILogger`, ohne vollständige Nutzereingaben.

## CI (GitHub Actions)

Bei jedem Push und Pull Request:

1. `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`.
2. `docker compose --profile bench build`.
3. Smoke-Test: `docker compose up -d`, warten bis `mcp` healthy ist, dann `bench check --servers perron`: Verbindungsaufbau (`initialize` oder `server/discover`) und `tools/list` müssen die erwartete Tool-Liste liefern. Danach `docker compose down -v`.

Benchmark-Läufe mit LLM laufen nicht in der CI.

## Leitplanken

- Nur lesende Tools.
- Die API schonen: Caching gemäss plan.md, Pausen zwischen Benchmark-Aufgaben, keine identischen Anfragen im Sekundentakt. Die API drosselt sonst.
- Quelle nennen: In Tool-Ergebnissen und im README «Fahrplandaten: opentransportdata.swiss» (über transport.opendata.ch).
- API-Keys für fremde Server kommen nur aus `.env`. Fehlt ein Key, wird der Server im Bench übersprungen und das im Report vermerkt.
- Nur öffentliche Daten. Kein Code und kein internes Wissen aus beruflichen Projekten.

## Definition of Done

- Build ohne Warnungen, `dotnet format` sauber, Tests grün.
- Akzeptanzkriterien des Tasks erfüllt.
- `docker compose up -d --build` startet ohne Fehler, `mcp` ist healthy, `bench check` läuft.
- Bei Änderungen an Tools, Beschreibungen, Formaten oder am Harness (ab Phase 3): `bench run --suite smoke` ausgeführt, Ergebnis in der PR-Beschreibung.
- `README.md` und `.env.example` sind aktuell.
- `tasks.md` abgehakt, Abweichungen im Entscheidungsprotokoll.
- Branch gepusht, Pull Request eröffnet, CI grün.
