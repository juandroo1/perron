# Tasks 001 – Perron

Immer nur einen Task gleichzeitig bearbeiten. Ein Task ist erst erledigt, wenn die Definition of Done in `CLAUDE.md` erfüllt ist.

## Phase 0 – Setup

- [x] 0.1 Repo-Grundgerüst: `git init -b main`, Solution mit `Perron.Transport`, `Perron.Mcp`, `Perron.Bench` und Testprojekten, `Directory.Build.props` (Nullable, TreatWarningsAsErrors), `Directory.Packages.props`, `.editorconfig`, `.gitattributes` (`* text=auto eol=lf`), `.gitignore`, `.env.example`, `.claude/settings.json` mit `permissions.deny` für `Read(bench/tasks/test.jsonl)`, README-Skelett, MIT-Lizenz. GitHub-Repo anlegen und ersten Stand auf `main` pushen (Ausnahme vom Branch-Prinzip, da `main` noch nicht existiert)
- [x] 0.2 Minimaler MCP-Server mit Tool `ping`, beide Transports, `GET /health`. Integrationstest mit MCP-Client: Verbindungsaufbau (`initialize` und `server/discover`) und `tools/list`
- [x] 0.3 Docker: Dockerfiles (Non-Root), `docker-compose.yml` mit `mcp` und `bench` (Profil), `.dockerignore`
- [x] 0.4 GitHub Actions: Build, Format, Tests, Compose-Build (mit Layer-Cache), Smoke-Test (vorerst mit einem minimalen `bench check` gegen `ping`)

## Phase 1 – Datenzugriff, Vergleichsobjekte, Aufgaben

- [x] 1.1 transport.opendata.ch erkunden (`/locations`, `/connections`, `/stationboard`): Parameter, Antworten, Zeitformate, mehrdeutige Namen (welche unterscheidenden Felder), Werte für `transport_types`, Fehler, Drosselungsantwort. Fixtures speichern. Die drei fremden Server ansehen (Tools, `instructions`, Start, Versionen, Lizenzen) und Ergebnis in plan.md, Abschnitt 3 und spec.md, Abschnitt 5, nachtragen
- [x] 1.2 `Perron.Transport`: typisierter Client, Resilience, Caching gemäss plan.md, Abschnitt 5. Tests mit Fixtures
- [x] 1.3 Aufgaben-Set v1 (Format gemäss plan.md, Abschnitt 6): ca. 25 Aufgaben `dev` (Claude) und ca. 20 Aufgaben `test` (isolierter Subagent), nach Kategorie stratifiziert gemäss spec.md, Abschnitt 6; rund 6 `dev`-Aufgaben als `smoke` markieren. Die Arbeitssitzung liest `test.jsonl` nicht. Formattest für beide Sets. Sandro prüft stichprobenartig. `test.jsonl` einfrieren, SHA-256 ins Entscheidungsprotokoll

## Phase 2 – Perron MCP

- [x] 2.1 `find_stops` inkl. mehrdeutiger Namen; Tool `ping` entfernen, erwartete Tool-Liste im CI-Smoke-Test nachziehen (AK-1.1, AK-1.2)
- [x] 2.2 `plan_trip` inkl. Name-oder-ID und Kandidaten bei Mehrdeutigkeit; Format `compact` mit Snapshot-Test, Token-Obergrenze festlegen (AK-2.1 bis AK-2.4)
- [x] 2.3 `get_departures` (AK-3.1, AK-2.4)
- [x] 2.4 Fehlerbehandlung, leere Ergebnisse mit Hinweis, Tool-Annotations über alle Tools (AK-4.1, AK-4.2)
- [x] 2.5 Varianten `standard`/`many`, `rich`/`minimal` und `compact`/`json`, eigene `serverInfo`-Namen, Varianten im Compose, Snapshot-Tests von `tools/list` je Variante (AK-4.3, AK-5.1, AK-5.2)
- [x] 2.6 `.mcp.json`, README-Anleitung für Claude Code und Claude Desktop; alle Tools von Hand in Claude Code ausprobieren (AK-6.1, AK-6.2)

## Phase 3 – Perron Bench

- [x] 3.1 Server-Registry und `bench check` für HTTP- und stdio-Server, inkl. Token-Grösse der Tool-Definitionen (ohne Key «n/a») und `tools/list`-Hash. Fähigkeiten mit Begründung, `stm` optional. Fremde Server im `bench`-Image mit fixierten Versionen, transitive Python-Abhängigkeiten gepinnt (AK-7.1, AK-7.2)
- [x] 3.2 *(entfällt, vorgezogen als 1.3)*
- [x] 3.3 Agent-Loop, `bench/bench.json`, Übernahme der Server-`instructions`, Traces, Run-Manifest, Metriken, `--dry-run` (AK-8.1 bis AK-8.3)
- [x] 3.4 Oracles mit Verifikation ab Referenzzeit, Regex-Prüfungen, Grounding-Check, Infra-Fehler, `bench rescore`; Golden-Traces (bestanden/nicht bestanden) als Tests der Checks (AK-9.1, AK-9.3 bis AK-9.6)
- [x] 3.5 Judge-Prüfungen für `ambiguous`/`bad_input` mit Rubrik, blind für Server-ID; mindestens 40 Urteile von Hand prüfen (AK-9.2)
- [x] 3.6 `bench report` mit Tabelle, Konfidenzintervallen, pass^3, Fehlertaxonomie, Methodik und Mermaid-Diagramm (AK-10.1 bis AK-10.3)

## Phase 4 – Auswertung

- [x] 4.1 Baseline auf `dev`: alle Server, 3 Wiederholungen, Haiku 5.5. Kosten vorher mit `--dry-run` schätzen und bestätigen lassen
- [ ] 4.2 Ablationen am eigenen Server auf `dev` (`perron-many`, `perron-minimal`, `perron-json`); Fehler anhand der Traces analysieren und Tools gezielt verbessern
- [ ] 4.3 Schlussvergleich auf `test`, 3 Wiederholungen, Haiku 5.5; zusätzlich Kontrolllauf mit Sonnet 5.5 (Kosten vorher per `--dry-run`, Rückfrage). Danach keine Optimierung mehr auf `test`

## Phase 5 – Portfolio

- [ ] 5.1 README: Leitfrage, Architekturdiagramm, Ergebnistabellen und Diagramm, Methodik und Fairness-Regeln, Erkenntnisse zum Tool-Design, bekannte Grenzen, GIF einer Session in Claude Code, Hinweis auf den SDD-Workflow
- [ ] 5.2 Lizenzfrage geklärt, Autoren der fremden Server optional vorab informiert
- [ ] 5.3 Optional: kurzer Blogpost oder LinkedIn-Beitrag mit den wichtigsten Erkenntnissen
