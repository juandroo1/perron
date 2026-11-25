# Plan 001 – Perron

Bezieht sich auf: `spec.md` Version 0.2

## 1. Architektur

```
                         ┌──────────────────────────────┐
                         │ Perron.Bench                 │
 bench/tasks/*.jsonl ──► │  Runner ── Agent-Loop ── Claude (Anthropic SDK)
 bench/servers.json  ──► │    │                         │
 bench/bench.json    ──► │    ├─ MCP-Client (HTTP) ──► Perron MCP (Varianten)
                         │    ├─ MCP-Client (HTTP) ──► transport.opendata.ch/mcp
                         │    ├─ MCP-Client (stdio) ─► swiss-public-transport-mcp
                         │    └─ MCP-Client (stdio) ─► swiss-transport-mcp (optional)
                         │  Checks ── Oracle ──► transport.opendata.ch (REST)
                         │         ├─ Grounding (lokal, aus dem Trace)
                         │         └─ Judge  ──► Claude
                         │  Report ──► bench/results/
                         └──────────────────────────────┘

Perron.Mcp ──► Perron.Transport ──► transport.opendata.ch (REST, ohne Key)
```

## 2. Tech-Stack

| Bereich | Wahl | Begründung |
|---|---|---|
| Plattform | .NET 10 (LTS) | Kernkompetenz, Enterprise-Bezug |
| MCP | `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (offizielles C#-SDK; aktuelle Version in Task 0.2 prüfen) | Server und Clients (stdio, HTTP) aus einem SDK |
| LLM | `Anthropic` (offizielles C#-SDK) | |
| Modell | `claude-haiku-5-5` für alle Läufe; `claude-sonnet-5-5` als Kontrolle im Schlussvergleich. `effort` explizit gesetzt (Startwert `medium` für beide), `max_tokens` fix; alles in `bench/bench.json` | Kleines Modell zeigt Effekte von Tool-Design stärker und ist günstig; Kontrolle zeigt, ob die Effekte bei einem stärkeren Modell bleiben. `temperature` ist mit der aktuellen API-Version nicht setzbar, deshalb Wiederholungen |
| HTTP | `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience` | |
| Caching | `IMemoryCache` | |
| CLI Bench | `System.CommandLine` | |
| Diagramme | Mermaid `xychart` im Markdown-Report | GitHub rendert es direkt; keine nativen Grafik-Bibliotheken im Linux-Container |
| Tests | xUnit, Fake-`HttpMessageHandler` mit Fixtures, `WebApplicationFactory` + MCP-Client, Snapshot-Tests für `tools/list` und Ergebnisformate | |
| Container | Compose mit `mcp`, Varianten-Services und `bench` (Profil); `bench`-Image mit `uv` und fixierten Python-Servern (transitiv gepinnt) | |

## 3. Datenquelle

- `https://transport.opendata.ch/v1/` mit `/locations`, `/connections`, `/stationboard`. Kein Key nötig.
- Seit Oktober 2026 beantwortet die API Anfragen direkt aus den nationalen Fahrplandaten (GTFS, GTFS-RT von opentransportdata.swiss, Routing mit MOTIS). Echtzeitdaten werden etwa alle 30 Sekunden aktualisiert, der Fahrplan etwa zweimal pro Woche.
- Kein festes Limit pro Client, aber wiederholte gleiche Anfragen in kurzen Abständen werden gedrosselt. Deshalb Caching und Pausen im Bench.
- Quellenangabe: «Fahrplandaten: opentransportdata.swiss».
- Weitere Endpunkte seit November 2026 (für Perron vorerst nicht genutzt): `/trip`, `/fare`, `/formation`, `/ojp20`, `/mcp` (der offizielle MCP-Server).

### Verifiziert in Task 1.1 (2026-11-25, Fixtures in `tests/fixtures/transport-opendata/`)

**Parameter**
- `/locations`: `query` oder `x`+`y` (x = Breite, y = Länge); `type` = `all` (Standard) | `station` | `poi` | `address`. Mit Koordinaten und `type=all` kommt zuerst die nächste Adresse (ohne `id`), mit `type=station` nur Haltestellen, jeweils mit `distance` in Metern.
- `/connections`: `from`, `to` (Name, ID, Adresse oder `lat,lon`), `via` (bis 2, ab zwei als `via[]`), `date` (`YYYY-MM-DD`), `time` (`hh:mm`), `isArrivalTime` (0/1), `transportations[]` (`train`, `tram`, `ship`, `bus`, `cableway`), `limit` (1–16), `page` (−10 bis 10, negativ = früher), `minTransferTime` (0–60 min), `walkingSpeed`, `sharedMobility`, `geometry`, `fields`.
- `/stationboard`: `station` oder `id` (ID hat Vorrang), `limit` (weiche Grenze: gleiche Abfahrtszeit am Ende wird ganz geliefert, z. B. 14 statt 10), `transportations[]`, `datetime` (`YYYY-MM-DD hh:mm`), `type` = `departure` | `arrival`.

**Antworten**
- Zeiten: `2026-11-25T17:32:00+0100`, Offset **ohne** Doppelpunkt; zusätzlich Unix-Timestamps (`departureTimestamp`). Prognosen teils mit Sekunden (`17:14:31`).
- Dauer: eigenes Format `00d00:56:00`.
- Haltestelle: `id`, `name`, `coordinate {x, y}`, `distance`, `icon` (`train`/`tram`/`bus`/null; nicht dokumentiert). **Kein Kantons- oder Ortsfeld**: Mehrdeutige Namen unterscheiden sich nur im Namen («Buchs AG», «Buchs SG», «Buchs-Dällikon») und im `icon`.
- Verbindung: `from`/`to` (Zeit, Gleis, `delay`, `prognosis`), `duration`, `transfers`, `products` (z. B. `IC 1`), `capacity1st`/`capacity2nd` (Auslastungsprognose 1–3, sonst null/−1), `sections[]` mit `journey` (`category`, `number`, `operator`, `to`, `passList`, `trip`) oder `walk` (`duration` in Sekunden).
- Gleise mit Sektoren (`3CD`, `12A-C`); `delay` ist Zahl in Minuten oder `null` ohne Echtzeitdaten; Zugnummern teils mit führenden Nullen (`EC000015`, `IC001410`); `operator` teils mit Füllzeichen (`"VBZ    F"`).
- Abfahrtstafel: `station` + `stationboard[]` mit `stop` (Zeit, Gleis, `delay`, `prognosis`), `category`, `number`, `to`, `operator`, `passList`. Auch bei `type=arrival` ist `to` das Endziel des Zuges. Die Herkunft eines ankommenden Zuges liefert die API nicht: `passList` beginnt immer bei der abgefragten Haltestelle und führt bis zum Endziel (in Task 1.2 korrigiert; in 1.1 fälschlich als Herkunftsquelle notiert).

**Mehrdeutigkeit und Fehler**
- Mehrdeutiger Name in `/connections` oder `/stationboard`: Die API **wählt still einen Treffer** (aus «Buchs» wird «Buchs AG») und liefert keine Kandidatenliste (`stations.from` enthält nur den gewählten). Perron muss Mehrdeutigkeit selbst über `/locations` erkennen (AK-2.4).
- Unbekannter Name: **HTTP 200 mit leeren Listen** (`{"stations":[]}`, `{"connections":[],"from":null,...}`, `{"station":null,"stationboard":[]}`), kein Fehler. Fehlender Pflichtparameter `to`: ebenfalls 200 mit leeren Listen.
- Fehler sind **JSON**, nicht Klartext wie in der Doku: `{"errors":[{"message":"..."}]}`. Datum ausserhalb des geladenen Fahrplanfensters (am 2026-11-25: `[2026-11-15, 2027-04-15[`) und ungültiges Datum: **400**. Ungültiges `datetime` bei `/stationboard`: **500** mit PHP-Meldung.
- Drosselung: laut Doku 429 «when the planner is busy»; nicht provoziert, keine echte Antwort vorhanden.

## 4. Tool-Design Perron MCP

Grundsätze (Hypothesen, die der Bench prüft):
- Wenige, klar abgegrenzte Tools mit Namen im Format `verb_objekt`.
- Beschreibungen (Variante `rich`) sagen, wann man das Tool benutzt, wann nicht, und geben ein Beispiel.
- Ergebnisse (Variante `compact`) sind knapp, mit IDs für Folgeaufrufe. Kein Durchreichen der rohen API-Antwort.
- Haltestellen werden als Name oder ID akzeptiert. Mehrdeutige Namen liefern Kandidaten statt einer stillen Wahl.
- Fehlermeldungen sagen dem Agenten, was er anders machen soll. Leere Ergebnisse sind kein Fehler, sondern enthalten einen Hinweis.
- Parameter sind über alle Tools einheitlich benannt.
- Alle Tools sind als read-only markiert (Tool-Annotations).

| Tool | Parameter |
|---|---|
| `find_stops` | `query?`, `latitude?`, `longitude?`, `limit` (Standard 5) |
| `plan_trip` | `from`, `to` (Name oder ID), `via?` (max. 2), `datetime?` (ISO 8601 lokal, Europe/Zurich; Standard: jetzt), `time_is` (`departure` \| `arrival`, Standard `departure`), `transport_types?` (Enum), `limit` (Standard 4, max. 8) |
| `get_departures` | `stop` (Name oder ID), `datetime?` (wie oben), `type` (`departure` \| `arrival`), `transport_types?` (Enum), `limit` (Standard 10, max. 30) |

`transport_types` ist ein Enum mit den Werten der API (in 1.1 verifiziert): `train`, `tram`, `bus`, `ship`, `cableway`.

### Ergebnisformat `compact`
Textformat, eine Verbindung bzw. Abfahrt pro Block:
- Verbindung: Datum, Abfahrt (Zeit, Gleis, Verspätung), Ankunft (Zeit, Gleis, Verspätung), Dauer, Anzahl Umstiege; pro Abschnitt Linie, von–bis, Zeiten, Gleise; Umsteigezeit zwischen Abschnitten.
- Abfahrt: Datum, Zeit, Verspätung, Linie, Ziel, Gleis.
- Haltestelle: ID, Name, Ort (falls vorhanden), Koordinaten.
- Am Ende eine kurze Quellenzeile.
Das Format wird per Snapshot-Test fixiert. Die Token-Obergrenze pro Verbindung wird in Task 2.2 festgelegt.

### Varianten

Über `PERRON_TOOLS`, `PERRON_DESCRIPTIONS` und `PERRON_FORMAT`:

| Variante | Tool-Schnitt | Beschreibungen | Format |
|---|---|---|---|
| `perron-rich` | standard (3 Tools) | rich | compact |
| `perron-minimal` | standard | minimal | compact |
| `perron-json` | standard | rich | json (dieselben Felder wie compact, als JSON) |
| `perron-many` | many (7 Tools) | rich | compact |

Tool-Schnitt `many` (Entwurf, wird in Task 2.5 fixiert): `search_stops_by_name`, `search_stops_near`, `get_connections_departing`, `get_connections_arriving`, `get_connections_via`, `get_departures`, `get_arrivals`. Dieselben Daten und dasselbe Format wie `standard`, nur feiner auf Tools verteilt.

Im Compose laufen die Varianten als eigene Services derselben Image-Version (Profil `bench`).

## 5. Caching

| Daten | TTL |
|---|---|
| `/locations` | 24 h |
| `/connections`, `/stationboard` | 30 s |

Gecacht werden nur erfolgreiche Antworten; Schlüssel ist die normalisierte Anfrage-URL. Die Grösse ist begrenzt (eigener `MemoryCache` mit `SizeLimit`).

Resilience des Clients (`Perron.Transport`, über Optionen änderbar): Standard-Handler aus `Microsoft.Extensions.Http.Resilience` mit höchstens 2 Wiederholungen (1 bis 5 einstellbar; exponentiell ab 1 s, `Retry-After` wird beachtet), 10 s Timeout pro Versuch, 25 s gesamt. 400er werden nicht wiederholt. Fehlerarten für Perron: `OutsideTimetable` (400 ausserhalb des Fahrplanfensters), `InvalidRequest` (übrige 400), `Throttled` (429), `Unavailable` (5xx, Timeout, Netzwerk).

## 6. Bench-Methodik

### Konfiguration (`bench/bench.json`, versioniert)
```
{ model, control_model, effort, max_tokens, pause_seconds,
  default_max_tool_calls, dry_run_assumptions: { turns, tool_result_tokens } }
```

### Server-Registry (`bench/servers.json`)
```
{ id, transport: "http" | "stdio", url?, command?, args?, version?,
  hosted: bool, optional: bool, env_required: [...],
  capabilities: [ { name, reason } ] }
```
Die Fähigkeiten werden vor dem ersten Lauf festgelegt und begründet. Massstab: Sind die Daten über die Tools erreichbar (auch über Umwege)? Nicht: Gibt es ein bequemes Tool dafür?

### Aufgabenformat (`bench/tasks/*.jsonl`)
```
{ id, prompt, language, category, capabilities_required: [...], smoke: bool,
  checks: [ { type: "oracle", oracle, params, tolerance_minutes } |
            { type: "regex", pattern } |
            { type: "judge", rubric } ],
  max_tool_calls }
```

- Eine Aufgabe ist bestanden, wenn **alle** ihre Checks bestehen.
- `language`: `de` oder `fr`. `capabilities_required`: `stops`, `trips`, `departures`, `occupancy`, `disruptions`.
- `regex`: .NET-Regex gegen die Schlussantwort; `(?i)` für Gross-/Kleinschreibung.
- `judge` nur für `ambiguous` und `bad_input` (AK-9.2).
- Relative Zeitangaben in Oracle-Parametern, aufgelöst gegen die Referenzzeit (Europe/Zurich):
  - `now` – die Referenzzeit selbst
  - `+<n>d HH:mm` – n Tage nach dem Referenzdatum, um HH:mm (z. B. `+1d 08:00` = morgen 08:00)
  - `next:<mon|tue|wed|thu|fri|sat|sun> HH:mm` – nächster solcher Wochentag nach dem Referenzdatum

### Ablauf pro Aufgabe und Server
1. MCP-Session öffnen, Tools und `instructions` laden, Tool-Definitionen in das Format der Anthropic-API übersetzen.
2. System-Prompt (für alle gleich): Rolle, Referenzzeit (Datum und Uhrzeit in Europe/Zurich, pro Aufgabenrunde eingefroren), Hinweis, kurz und mit konkreten Zeiten zu antworten, fester Abschnitt für die `instructions` des Servers.
3. Agent-Loop bis End-Turn oder `max_tool_calls`. Tool-Fehler werden dem Modell zurückgegeben. MCP-Ergebnisse werden für alle Server gleich umgewandelt: Text-Content als Text, sonst `structuredContent` als JSON.
4. Checks direkt nach dem Lauf; Oracle-Ergebnis im Trace speichern.
5. Trace, Run-Manifest und Metriken speichern, Pause (`pause_seconds`, Standard 3 s) vor der nächsten Aufgabe.

Die Reihenfolge der Server wird pro Aufgabe gemischt, damit kein Server systematisch ältere oder neuere Daten bekommt. Das Oracle wird einmal pro Aufgabenrunde abgefragt und für alle Server derselben Runde verwendet; das schont die API.

### Oracles (erste Version)
- `next_departure(stop, after, line?, towards?, transport_type?, type?)`: nächste passende Abfahrt (`type: arrival` für Ankünfte; `towards`: Fahrzeug hält später an dieser Haltestelle).
- `earliest_arrival(from, to, depart_after, via?, min_transfer_minutes?, transport_types?)`: früheste Ankunft.
- `latest_departure(from, to, arrive_before, via?, min_transfer_minutes?, transport_types?)`: späteste Abfahrt, um rechtzeitig anzukommen.
- `stop_candidates(name)`: Liste plausibler Haltestellen (für mehrdeutige Namen).
- `occupancy(from, to, depart_after, class)`: Auslastungsprognose (`capacity1st`/`capacity2nd`: 1 niedrig, 2 mittel, 3 hoch) der ersten Verbindung ab `depart_after`; bestanden, wenn die Antwort dieselbe Stufe nennt.

`after`, `depart_after` und `arrive_before` werden aus der Referenzzeit berechnet, nicht aus der aktuellen Uhrzeit.

Prüfung «verifizieren statt nachrechnen»: Aus der Antwort wird die genannte Verbindung bzw. Abfahrt extrahiert (Regex, bei Bedarf LLM mit strukturierter Ausgabe). Bestanden, wenn sie in den Daten existiert, die Bedingungen der Aufgabe erfüllt und höchstens `tolerance_minutes` schlechter als das Optimum ist. Geplante und prognostizierte Zeit gelten beide. Das macht die Prüfung robust gegen Echtzeit-Schwankungen und gegen Server mit anderem Routing (`stm`, OJP).

### Grounding-Check
Alle Zeitangaben (HH:MM) der Schlussantwort müssen in einem Tool-Ergebnis desselben Laufs vorkommen. Verstösse zählen als Halluzination, unabhängig vom Erfolg der Aufgabe.

### Judge
Nur für `ambiguous` und `bad_input`. Fixiertes Modell (`model` aus `bench.json`), Rubrik pro Aufgabe, ohne Server-ID im Prompt. Mindestens 40 Urteile werden von Hand geprüft; Übereinstimmung im Report.

### Infra-Fehler
HTTP 429, Timeouts, Abstürze von Server oder API: Der Lauf wird einmal wiederholt, sonst als Infra-Fehler von der Wertung ausgeschlossen und separat berichtet.

### Fehlertaxonomie
Fehlgeschlagene Läufe bekommen einen Code (automatisch, wo möglich, sonst von Hand): `wrong_stop`, `wrong_time` (Tag, Zeitzone), `ignored_constraint`, `not_optimal`, `hallucinated`, `gave_up`, `tool_limit`, `tool_error_loop`, `infra`.

### Metriken und Statistik
- Aufgabenerfolg: Mittelwert über Wiederholungen, Bootstrap-Konfidenzintervall (95 %, Resampling über Aufgaben), pass^3 (alle drei Wiederholungen bestanden). Getrennt nach abgedeckten und allen Aufgaben.
- Vergleiche zwischen Servern gepaart (dieselben Aufgaben); Aussagen im README nur, wenn das Intervall der Differenz 0 nicht enthält.
- Tool-Aufrufe pro Aufgabe, Anteil fehlgeschlagener Tool-Aufrufe, Halluzinationsrate.
- Tokens der Tool-Definitionen (Kontextkosten, bevor überhaupt etwas passiert).
- Input-/Output-Tokens, Kosten pro Aufgabe und pro gelöster Aufgabe. Vergleichsbasis sind Kosten ohne Prompt-Caching, weil Caching Server mit grossen Tool-Definitionen bevorzugt.
- Latenz getrennt nach LLM- und Tool-Zeit (Perron läuft lokal mit Cache, `official` remote).

### Entwicklungs- und Test-Set
- Am eigenen Server wird nur mit `dev` iteriert.
- `test` wird in Task 1.3 geschrieben und eingefroren (SHA-256 im Entscheidungsprotokoll) und erst für den Schlussvergleich (Task 4.3) verwendet, danach nicht mehr für Optimierungen.
- Leakage-Schutz: `.claude/settings.json` verbietet Claude Code das Lesen von `bench/tasks/test.jsonl`; Regel in `CLAUDE.md`.
- `smoke`: rund 6 markierte `dev`-Aufgaben für schnelle Läufe nach Änderungen.

## 7. Konfiguration (`.env`)

Nur Secrets und Umgebung; Methodik steht in `bench/bench.json`.

```
TRANSPORT_API_BASE=https://transport.opendata.ch/v1/
ANTHROPIC_API_KEY=
STM_TRANSPORT_API_KEY=     # für swiss-transport-mcp, optional
```

## 8. Geplante Auswertungen fürs README

1. Hauptvergleich auf dem Test-Set: Perron vs. offizieller Server vs. Community-Server (Haiku 5.5).
2. Kontrolle: derselbe Vergleich mit Sonnet 5.5.
3. Ablation Tool-Anzahl: `perron-rich` vs. `perron-many`.
4. Ablation Beschreibungen: `perron-rich` vs. `perron-minimal`.
5. Ablation Ergebnisformat: `perron-rich` (compact) vs. `perron-json`.
6. Zusammenhang zwischen Tokens der Tool-Definitionen und Kosten pro gelöster Aufgabe.
7. Fehleranalyse: Fehlertaxonomie und Halluzinationsrate pro Server anhand der Traces.

## 9. Risiken

| Risiko | Massnahme |
|---|---|
| Echtzeitdaten machen Prüfungen instabil | Referenzzeit, Verifikation statt exakter Vergleich, Toleranz, gemischte Server-Reihenfolge, Wiederholungen |
| API drosselt bei vielen Läufen | Caching, Pausen, Oracle einmal pro Runde, Infra-Fehler separat, `--dry-run`, kleine `smoke`-Suite |
| Fremde Server ändern sich oder fallen aus | Fixierte Versionen (stdio), `tools/list`-Hash (gehostet), Server wird im Report als «nicht verfügbar» geführt |
| `stm` (OJP, API-Key) lässt sich nicht stabil betreiben | Als optional geführt; MVP läuft ohne ihn |
| Unfairer Vergleich | Fairness-Regeln in CLAUDE.md, Fähigkeiten vorab begründet, Methodik offenlegen |
| Statistisch nicht belastbare Unterschiede | Konfidenzintervalle, gepaarte Vergleiche, keine Aussagen ohne Datenbasis |
| Kosten | Haiku 5.5 als Hauptmodell, `--dry-run`, Kostenschätzung vor `full`-Läufen, Rückfrage |
| Überanpassung an das Eval-Set | Aufgaben vor dem Tool-Bau, eingefrorenes Test-Set, Leseverbot für Claude Code |

## 10. Entscheidungsprotokoll

| Datum | Entscheidung | Grund |
|---|---|---|
| 2026-11-25 | Benchmark mit fremden Servern statt «noch ein Transport-MCP» | Es gibt bereits mehrere Server; der Mehrwert liegt in der Messung von Tool-Design |
| 2026-11-25 | Datenquelle transport.opendata.ch statt OJP | Kein Key, gleiche Quelle wie der offizielle Server, faire Vergleichsbasis |
| 2026-11-25 | Hauptmodell Haiku 5.5, Kontrolle mit Sonnet 5.5 im Schlussvergleich | Kleine Modelle zeigen Tool-Design-Effekte stärker; ca. 10 $ statt 60–165 $ für alle Läufe |
| 2026-11-25 | Modell, `effort`, `max_tokens` in `bench/bench.json` statt `.env` | Methodik muss versioniert und reproduzierbar sein; `.env` nur für Secrets |
| 2026-11-25 | Neue Variante `perron-many` (7 Tools) | Leitfrage nennt «Anzahl Tools»; ohne Ablation nur über fremde Server (konfundiert) vergleichbar |
| 2026-11-25 | `perron-json` = dieselben Felder wie `compact`, nur als JSON | Ablation misst sauber das Format, nicht Format und Inhalt zugleich |
| 2026-11-25 | Mermaid-`xychart` statt ScottPlot | ScottPlot braucht SkiaSharp-Native-Libs und Fonts im Linux-Container; Mermaid rendert direkt auf GitHub |
| 2026-11-25 | `stm` optional | API-Key und OJP sind das grösste Integrationsrisiko; MVP soll nicht daran hängen |
| 2026-11-25 | Judge nur für `ambiguous`/`bad_input`, ≥ 40 Urteile von Hand statt 10 % | 10 % ergäben ~10 Stichproben, zu wenig für eine belastbare Übereinstimmung |
| 2026-11-25 | Aufgaben-Set vor Phase 2 schreiben (Task 1.3 statt 3.2), Test-Set mit Hash einfrieren | Aufgaben sollen nicht vom Perron-Tool-Design geprägt sein |
| 2026-11-25 | Oracle verifiziert die genannte Verbindung ab Referenzzeit, statt exakt nachzurechnen | Robust gegen Echtzeit-Schwankungen und gegen Server mit anderem Routing |
| 2026-11-25 | Leere Ergebnisse sind kein Tool-Fehler | `isError` bei «keine Abfahrten» verleitet den Agenten zu sinnlosen Wiederholungen |
| 2026-11-25 | Offizieller Server ohne Versions-Pin, stattdessen `tools/list`-Hash pro Lauf | Gehosteter Server lässt sich nicht fixieren; Änderungen werden so sichtbar |
| 2026-11-25 | Grounding-Check, Infra-Fehler, Fehlertaxonomie, Run-Manifest, `bench rescore` aufgenommen | Standard-Erwartungen an ein belastbares Agent-Eval |
| 2026-11-25 | Tests mit xUnit v3 auf Microsoft.Testing.Platform (`global.json`: `test.runner`), ohne VSTest-Pakete (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `coverlet.collector`) | Auf dem .NET-10-SDK läuft xUnit v3 mit `dotnet test` nur im neuen Modus; weniger Pakete. Coverage später bei Bedarf über `Microsoft.Testing.Extensions.CodeCoverage` |
| 2026-11-25 | Perron unterstützt beide MCP-Protokollpfade: `initialize`-Handshake (bis 2025-11-25) und `server/discover` mit Metadaten pro Anfrage (2026-07-28). HTTP läuft stateless (Standard im SDK 2.4). Tests und CI prüfen «Verbindungsaufbau und `tools/list`» für beide Pfade statt nur `initialize` | Die Revision 2026-07-28 hat `initialize` und HTTP-Sessions abgeschafft; Claude Code, Inspector und fremde Server nutzen teils noch den Handshake. Der SDK-Client fällt automatisch zurück |
| 2026-11-25 | .NET SDK exakt 10.0.404 lokal, im Docker-Build (`sdk:10.0.404`) und über `global.json` (`rollForward: latestPatch`); Laufzeit-Images bleiben auf `aspnet:10.0` bzw. `runtime:10.0` | Mit `AnalysisLevel latest` und Warnungen als Fehler können verschiedene SDKs verschiedene Fehler zeigen; die Laufzeit-Images bekommen so weiterhin Sicherheits-Patches |
| 2026-11-25 | Docker-Healthcheck über `dotnet Perron.Mcp.dll --healthcheck` mit einfachem `HttpClient` statt curl und ohne `IHttpClientFactory`/Resilience | Die .NET-Laufzeit-Images enthalten kein curl/wget; ein Einmal-Prozess braucht keine Factory, und Wiederholungen übernimmt Docker (`retries`) |
| 2026-11-25 | `env_file` mit `required: false` | Compose startet auch ohne `.env` (frischer Checkout, CI); Keys werden erst ab Phase 3 gebraucht |
| 2026-11-25 | `bench`-Image vorerst ohne uv und Python-Server | Gehört laut tasks.md zu Task 3.1 |
| 2026-11-25 | CI läuft bei jedem Pull Request (inkl. jedes Pushes in einen PR-Branch), bei jedem Push auf `main` und manuell; nicht bei Pushes auf Branches ohne PR | Vermeidet doppelte Läufe pro Push; jeder Task bekommt sofort einen PR |
| 2026-11-25 | CI baut die Images mit `docker buildx bake` aus `docker-compose.yml` (GHA-Layer-Cache) statt `docker compose --profile bench build` | Gleiche Images und Tags, aber mit Cache; `docker compose up` verwendet sie danach ohne Neubau |
| 2026-11-25 | `PERRON_MCP_URL` für den `bench`-Container steht in `docker-compose.yml` (`environment`), nicht in `.env` | Beschreibt nur die Verbindung zwischen den Containern, ist kein Secret und muss ohne `.env` (CI) funktionieren |
| 2026-11-25 | Minimaler `bench check` bis Task 3.1: nur Server `perron`, erwartete Tools per `--expect-tools` | Registry (`servers.json`) und Token-Messung kommen in Task 3.1; die CI braucht vorher schon einen Smoke-Test |
| 2026-11-25 | Fremde Python-Server werden mit dem Stand ihrer Abhängigkeiten am Erscheinungstag installiert: `uvx --exclude-newer <Release-Datum + 1 Tag> --from <paket>==<version> <befehl>` (Entscheid Sandro) | `swiss-public-transport-mcp` 0.1.10 verlangt `mcp>=1.23` ohne Obergrenze und stürzt mit `mcp` 2.x ab (FastMCP fehlt). So läuft er unverändert, ohne Patch am Code; dieselbe Regel für alle Python-Server macht die Installation reproduzierbar. Im Report offengelegt |
| 2026-11-25 | Drosselung nicht provoziert; Tests für AK-4.2 mit synthetischer, als solche markierter 429-Antwort | Leitplanke «API schonen» |
| 2026-11-25 | Unbekannte Haltestellen erkennt Perron selbst (leere Liste von der API), nicht am HTTP-Status | Die API antwortet bei unbekannten Namen mit 200 und leeren Listen |
| 2026-11-25 | Cache-Ablauf wird über `TimeProvider` geprüft (eigene Hülle um `MemoryCache`), `MemoryCache` begrenzt nur die Grösse | `MemoryCacheOptions` kennt nur die veraltete `ISystemClock`; so sind 30 s/24 h ohne echtes Warten testbar |
| 2026-11-25 | Client bereinigt Eigenheiten der API: Offset ohne Doppelpunkt, Dauer `00d00:56:00`, Füllzeichen in `operator`, führende Nullen bei Zugnummern (`EC000015` → `EC 15`); bei zwei `via` gilt `connection.to` statt des falschen `to` auf oberster Ebene | Befunde aus Task 1.1; die Tools sollen saubere Werte zeigen |
| 2026-11-25 | Tool `ping` nur bis Task 2.1; danach entfernt | Sonst zählt es in Bench-Vergleichen zu Tool-Anzahl und Tokens der Tool-Definitionen und verfälscht die Ablation «Anzahl Tools» |
| 2026-11-25 | Aufgaben-Set von Claude statt von Hand (Entscheid Sandro): `dev` schreibt Claude in der Arbeitssitzung, `test` ein frischer Subagent ohne Kontext dieser Sitzung, direkt in die Datei; die Arbeitssitzung liest `test.jsonl` nie (nur Hash und Anzahl pro Kategorie per Skript). Sandro prüft beide Sets stichprobenartig. Im README als bekannte Schwäche offengelegt | Aufwand für Sandro. Risiken: Ein Claude schreibt Aufgaben für einen Claude-Agenten und kennt das Perron-Design (Bias); Leakage des Test-Sets wird durch den isolierten Subagenten und das Leseverbot begrenzt |
| 2026-11-25 | Keine Aufgaben zu Störungen (`disruptions`) im Set v1 | Kein Oracle möglich (transport.opendata.ch liefert keine Störungsmeldungen), und Judge ist nur für `ambiguous`/`bad_input` vorgesehen; `extended` prüft Auslastung über das neue Oracle `occupancy` |
| 2026-11-25 | `test.jsonl` eingefroren: 20 Aufgaben, SHA-256 `429b02dfd6cf698d353a04543ba64dbd419de72d92d1249379d5f6e6379a3fca` | Hash von Sandro berechnet (Lesesperre für Claude); ab jetzt nur Task 4.3 (Schlussvergleich), jede Änderung braucht neuen Eintrag mit neuem Hash |
| 2026-11-25 | `find_stops` zeigt statt «Ort» die Art der Haltestelle (`icon`: train, tram, bus …) | Die API liefert kein Orts- oder Kantonsfeld (Task 1.1); die Art unterscheidet z. B. «Buchs SG» (Bahnhof) von «Buchs SG, Bahnhof» (Bus) |
| 2026-11-25 | `find_stops` sucht immer mit `type=station` | Adressen und POIs haben keine ID und taugen nicht für Folgeaufrufe; bei Koordinaten käme sonst zuerst die nächste Adresse |
| 2026-11-25 | Tool-Texte (Beschreibungen, Ergebnisse, Fehlermeldungen) auf Englisch, für alle Varianten gleich (Entscheid Sandro) | Passt zu Code und README; die Aufgaben auf Deutsch und Französisch prüfen den Übergang |
| 2026-11-25 | Suche ohne Treffer in `find_stops` ist ein normales Ergebnis mit Hinweis, kein Tool-Fehler | AK-4.1 meint unbekannte Haltestellen als Eingabe (`plan_trip`, `get_departures`); eine Suche ohne Treffer ist ein leeres Ergebnis |
| 2026-11-25 | Auflösung von Haltestellennamen in `plan_trip` (und später `get_departures`): ID direkt; sonst `/locations?type=station`, dann genauer Treffer → Anfangstreffer (Name oder ein `/`-Teil beginnt mit der Eingabe) → genau ein Anfangstreffer bzw. genau einer auf Ortsebene (ohne Komma) → sonst mehrdeutig (Kandidaten statt Verbindungen). Kein Anfangstreffer: Tool-Fehler mit ähnlichen Namen | Die API-Suche ist unscharf (Fixture «Genf»: «Bern, Galgenfeld» vor «Genève»). So lösen «Basel» und «Fribourg» direkt auf, «Buchs» und «Zürich» liefern Kandidaten. `find_stops` nutzt dieselbe Regel für seinen Hinweis |
| 2026-11-25 | Token-Obergrenze pro Verbindung (bis 2 Umstiege) im Format `compact`: 400 Zeichen, geschätzt ≈ 115 Tokens (≈ 3,5 Zeichen pro Token); längster Block in den Fixtures 315 Zeichen, Rest Reserve für Verspätungen und Gleiswechsel. Geprüft als Zeichengrenze im Snapshot-Test | Kein `ANTHROPIC_API_KEY` in `.env`; Messung mit `count_tokens` in Phase 3 (AK-7.2) nachholen und Wert hier bestätigen |
| 2026-11-25 | `get_departures` kürzt die weiche Grenze der API nicht (gleiche Minute am Ende wird ganz gezeigt) | Sonst fehlten Abfahrten derselben Minute je nach Sortierung der API |
| 2026-11-25 | Ankünfte zeigen das Endziel des Fahrzeugs (`continues to …` bzw. `ends here`), keine Herkunft; die Zeit steht bei Ankünften im API-Feld `departure` | Die API liefert keine Herkunft und bei `type=arrival` `arrival: null` (Fixture `stationboard-bern-arrivals.json`) |
| 2026-11-25 | `get_departures` ohne weitere Halte (`passList`); die Beschreibung verweist für «hält in …» auf `plan_trip` | Nicht in spec.md verlangt, kostet viele Tokens; als offene Frage in spec.md §9 notiert |
| 2026-11-25 | Unbekannte Haltestellen-ID (API: `station: null`) ist in `get_departures` ein Tool-Fehler | AK-4.1: unbekannte Haltestelle als Eingabe |
| 2026-11-25 | Fehler der API werden zentral in einem Call-Tool-Filter (`TransportErrorFilter`) zu Tool-Fehlern mit Handlungshinweis, nicht per `try/catch` in jedem Tool | Gilt automatisch für alle Tools und Varianten (2.5); per Test und Mutationsprobe belegt, dass die Exception den Filter erreicht. Log mit Tool, Fehlerart und Status, ohne Nutzereingaben |
| 2026-11-25 | Meldung «ausserhalb des Fahrplans» nennt das Fenster aus der API-Meldung (`window [2026-11-15 00:00, 2027-04-15 00:00[` → «15.11.2026 to 14.04.2027»), sonst allgemein «a few months around today» | Konkreter Hinweis für den Agenten (AK-4.1/4.2), ohne eigene Annahmen über das Fenster |
| 2026-11-25 | Task 2.5 in zwei PRs: A (Ergebnis-Modelle, Format `json`, Beschreibungen `minimal`, Konfiguration, `serverInfo`-Namen, Snapshots von `tools/list`), B (Tool-Schnitt `many`, Varianten-Services im Compose, CI). Abgehakt wird mit B | Überschaubare Reviews (Entscheid Sandro) |
| 2026-11-25 | Varianten über `PERRON_TOOLS`, `PERRON_DESCRIPTIONS`, `PERRON_FORMAT` (Prüfung beim Start). `serverInfo`-Name: `perron-rich` für die Standardwerte, sonst `perron-` plus die abweichenden Werte (`perron-minimal`, `perron-json`, `perron-many`, `perron-many-minimal` …). Der Compose-Service `mcp` heisst damit `perron-rich` statt `perron` | AK-5.2, eindeutige Zuordnung im Bench; passt zur Varianten-Tabelle in Abschnitt 4 |
| 2026-11-25 | Beschreibungen `minimal`: ein kurzer Satz pro Tool, Parameter nur mit Bedeutung, Datumsformat und Wertebereich; keine Erklärungen, Abgrenzungen oder Beispiele | Wertebereiche und Formate sind Schnittstelle, nicht Hilfe; sonst misst die Ablation das fehlende Datumsformat statt den Beschreibungsstil (Entscheid Sandro) |
| 2026-11-25 | Format `json`: dieselben Felder wie `compact` aus denselben Ergebnis-Modellen, kompakt serialisiert (snake_case, ohne `null`, Umlaute unverändert, Zeiten lokal ISO `2026-11-26T08:02`, Wochentag als `weekday`). Fehlermeldungen bleiben in beiden Formaten Text | AK-5.1; nur das Format unterscheidet sich, nicht der Inhalt |
| 2026-11-25 | Tools werden programmatisch aus einem Katalog erzeugt (`McpServerTool.Create`), Beschreibungen kommen aus Texttabellen je Stil statt aus Attributen; Hinweise nennen die Tool-Namen der jeweiligen Variante | Eine Quelle für Texte je Variante; Tool-Namen in Hinweisen müssen zum Tool-Schnitt passen |
| 2026-11-25 | Tool-Schnitt `many` fixiert mit 7 Tools: `search_stops_by_name`, `search_stops_near`, `get_connections_departing`, `get_connections_arriving` (`datetime` Pflicht), `get_connections_via` (`via` Pflicht, 1–2, mit `time_is`), `get_departures`, `get_arrivals`. Gleiche Kernfunktionen, Ergebnisse und Fehler wie `standard`; Hinweise nennen `search_stops_by_name` bzw. «the connection tools» | AK-5.1; nur der Schnitt unterscheidet sich, nicht Daten oder Format |
| 2026-11-25 | Varianten im Compose als Services `mcp-minimal`, `mcp-json`, `mcp-many` (Profil `bench`, gleiches Image, YAML-Anker), ohne Ports nach aussen; die CI prüft jede Variante mit `bench check` | Bench erreicht sie über das Compose-Netz; weniger offene Ports |
