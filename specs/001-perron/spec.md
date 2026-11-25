# Spec 001 – Perron

Status: Entwurf · Version 0.2

## 1. Problem

Für den Schweizer ÖV gibt es inzwischen mehrere MCP-Server: einen offiziellen von transport.opendata.ch und mehrere Community-Server mit 4 bis 11 Tools. Ob ein Agent damit Aufgaben gut löst, misst niemand. Wer einen MCP-Server baut oder auswählt, entscheidet über Anzahl Tools, Beschreibungen und Ergebnisformate nach Gefühl.

## 2. Ziel

1. Ein eigener MCP-Server (Perron MCP) in .NET mit bewusst gestalteten Tools für Haltestellen, Verbindungen und Abfahrten.
2. Ein Benchmark (Perron Bench), der reale Agent-Aufgaben gegen mehrere Server ausführt, die Ergebnisse automatisch prüft und fair vergleicht.
3. Ablationen am eigenen Server, die zeigen, welche Design-Entscheide wie viel bringen: Anzahl Tools, Beschreibungen, Ergebnisformat.

Nebenziel: Das Projekt zeigt Agent- und Tool-Design, Eval-Methodik und MCP in .NET.

## 3. Zielgruppe

- Entwicklerinnen und Entwickler, die MCP-Server bauen oder auswählen.
- Recruiter und Teams im Bereich Agent Engineering, die sehen wollen, wie ein Kandidat Agenten misst und verbessert.
- Persona: Nina, 29, baut bei einem Verkehrsunternehmen einen internen Assistenten und muss entscheiden, welchen MCP-Server sie einsetzt und wie sie ihre eigenen Tools gestaltet.

## 4. User Stories und Akzeptanzkriterien

### Teil A – Perron MCP

#### US-1 Haltestellen finden
- AK-1.1: `find_stops` findet Haltestellen nach Name oder Koordinate und liefert ID, Name, Ort und Koordinaten.
- AK-1.2: Mehrdeutige Namen (z. B. «Buchs») liefern mehrere Kandidaten mit unterscheidenden Namen bzw. Orten, soweit die API sie liefert (in Task 1.1 verifiziert), damit der Agent nachfragen oder wählen kann.

#### US-2 Verbindungen planen
- AK-2.1: `plan_trip` plant Verbindungen von A nach B mit optionalem `via`, Datum/Zeit und Abfahrts- oder Ankunftszeit.
- AK-2.2: Jede Verbindung zeigt Abfahrt, Ankunft, Dauer, Anzahl Umstiege, Linien, Gleise, Umsteigezeiten und Verspätungen. Das Format folgt der Feldliste in plan.md, Abschnitt 4, ist per Snapshot-Test fixiert und bleibt unter einer Token-Obergrenze pro Verbindung (Wert wird in Task 2.2 festgelegt).
- AK-2.3: Zeiten sind immer in Europe/Zurich und mit Datum angegeben.
- AK-2.4: `plan_trip` und `get_departures` akzeptieren für Haltestellen einen Namen oder eine ID. Ist ein Name mehrdeutig, liefern sie die Kandidaten mit IDs, statt still einen Treffer zu wählen.

#### US-3 Abfahrten
- AK-3.1: `get_departures` liefert die nächsten Abfahrten (oder Ankünfte) einer Haltestelle mit Linie, Ziel, Gleis, Zeit und Verspätung, optional gefiltert nach Verkehrsmittel.

#### US-4 Fehler und Robustheit
- AK-4.1: Unbekannte Haltestellen und ungültige Zeiten liefern einen Tool-Fehler mit konkretem Hinweis, was der Agent tun soll. Ein leeres Ergebnis ist kein Fehler, sondern ein normales Ergebnis mit Hinweis (z. B. Zeitfenster erweitern).
- AK-4.2: Drosselung oder Ausfall der API führen zu einem verständlichen Tool-Fehler, nicht zu einem Absturz.
- AK-4.3: Ein Snapshot-Test fixiert `tools/list` (Namen, Beschreibungen, Schemas) für jede Variante.

#### US-5 Varianten für Ablationen
- AK-5.1: Über Konfiguration lassen sich Varianten des Servers starten: Tool-Schnitt (`standard` | `many`), Beschreibungsstil (`minimal` | `rich`) und Ergebnisformat (`compact` | `json`). `json` enthält dieselben Felder wie `compact`, nur als JSON serialisiert. `many` bietet dieselben Daten und Formate auf 7–8 feinere Tools verteilt.
- AK-5.2: Jede Variante meldet sich mit eigenem Namen in `serverInfo`, damit Bench-Ergebnisse eindeutig zugeordnet werden.

#### US-6 Transports
- AK-6.1: stdio mit `--stdio`, sonst Streamable HTTP auf `/mcp`.
- AK-6.2: `.mcp.json` im Repo bindet den Server in Claude Code ein.

### Teil B – Perron Bench

#### US-7 Server-Registry
- AK-7.1: `bench/servers.json` beschreibt jeden Server: ID, Transport (stdio mit Befehl oder HTTP mit URL), fixierte Version (bei gehosteten Servern: keine, stattdessen Hash von `tools/list`), benötigte Umgebungsvariablen, optional ja/nein, Fähigkeiten (z. B. `stops`, `trips`, `departures`, `disruptions`, `occupancy`) mit Begründung.
- AK-7.2: `bench check` verbindet sich mit allen Servern, listet ihre Tools und misst die Grösse der Tool-Definitionen in Tokens über `count_tokens`. Ohne API-Key (z. B. in der CI) wird die Messung übersprungen und als «n/a» ausgewiesen.

#### US-8 Aufgaben ausführen
- AK-8.1: `bench run` führt jede Aufgabe pro Server (und Wiederholung) in einem Agent-Loop mit Claude aus, mit identischem System-Prompt, Modell, `effort`, `max_tokens`, Referenzzeit und Limit an Tool-Aufrufen. Modell, `effort` und `max_tokens` stehen in der versionierten Datei `bench/bench.json`, nicht in `.env`. Die `instructions` eines Servers aus `initialize` werden für alle Server gleich in einem festen Abschnitt des System-Prompts übernommen.
- AK-8.2: Pro Lauf wird ein vollständiger Trace gespeichert: Nachrichten, Tool-Aufrufe mit Argumenten, Tool-Ergebnisse, Tokens, Latenz (getrennt nach LLM und Tool), Kosten, Oracle-Ergebnis. Jeder Lauf hat ein Run-Manifest: Git-SHA, Modell-ID, `effort`, Hash des System-Prompts, Hash des Aufgaben-Sets, Hash von `tools/list` je Server, Zeitpunkt, Harness-Version.
- AK-8.3: `--dry-run` schätzt Anzahl Aufrufe und Kosten, ohne das LLM aufzurufen. Formel: Tokens der Tool-Definitionen und des System-Prompts × erwartete Turns plus erwartete Tool-Ergebnisse; erwartete Werte werden aus früheren Läufen kalibriert, vor dem ersten Lauf aus festen Annahmen in `bench/bench.json`.

#### US-9 Automatische Prüfung
- AK-9.1: Prüfungen sind möglichst deterministisch. Weil Fahrplandaten sich ändern, prüft ein Oracle zum Zeitpunkt des Laufs direkt über transport.opendata.ch, und zwar ab der Referenzzeit aus dem System-Prompt. Geprüft wird «verifizieren statt nachrechnen»: Die genannte Verbindung bzw. Abfahrt existiert, erfüllt die Bedingungen der Aufgabe und ist höchstens um eine pro Aufgabe festgelegte Toleranz schlechter als die optimale. Geplante und prognostizierte Zeit gelten beide.
- AK-9.2: Wo das nicht geht (nur Kategorien `ambiguous` und `bad_input`), prüft ein LLM-as-Judge anhand einer Bewertungsrubrik, mit fixiertem Modell und ohne Kenntnis des Servers. Mindestens 40 Judge-Urteile werden von Hand geprüft und die Übereinstimmung wird dokumentiert.
- AK-9.3: Aufgaben, deren Fähigkeit ein Server nicht anbietet, werden als «nicht abgedeckt» gewertet. Die Fähigkeiten werden vor dem ersten Lauf in `servers.json` mit Begründung festgelegt. Lässt sich eine Aufgabe mit den vorhandenen Tools umgehen (z. B. `via` per zwei Abfragen), gilt sie als abgedeckt.
- AK-9.4: Ein Grounding-Check prüft, dass jede Zeitangabe in der Antwort in einem Tool-Ergebnis desselben Laufs vorkommt. Der Report weist die Halluzinationsrate pro Server aus.
- AK-9.5: Drosselung, Timeouts und Abstürze von Servern oder API gelten als Infra-Fehler: Der Lauf wird einmal wiederholt und sonst von der Wertung ausgeschlossen. Infra-Fehler werden separat berichtet.
- AK-9.6: `bench rescore` bewertet gespeicherte Traces ohne LLM-Aufruf neu (Oracle-Ergebnisse stammen aus dem Trace).

#### US-10 Report
- AK-10.1: `bench report` erstellt einen Markdown-Report mit einer Tabelle pro Server bzw. Variante: Aufgabenerfolg auf abgedeckten und auf allen Aufgaben (Mittelwert mit Bootstrap-Konfidenzintervall über Aufgaben, pass^3), Abdeckung, Tool-Aufrufe, Tokens der Tool-Definitionen, Tokens gesamt, Kosten pro Aufgabe und pro gelöster Aufgabe (Vergleichsbasis: ohne Prompt-Caching), Latenz getrennt nach LLM und Tool, Halluzinationsrate, Infra-Fehler und Auszählung der Fehlertaxonomie.
- AK-10.2: Der Report enthält Methodik, Modell, `effort`, Versionen bzw. `tools/list`-Hashes der Server, Datum und Anzahl Wiederholungen.
- AK-10.3: Ein Mermaid-Diagramm (`xychart`) im Markdown vergleicht Erfolg und Kosten der Server.

## 5. Vergleichsobjekte (MVP)

| ID | Server | Transport | Bemerkung |
|---|---|---|---|
| `perron-rich` | Perron MCP, Variante standard/rich/compact | HTTP | Hauptvariante |
| `perron-minimal`, `perron-json`, `perron-many` | Varianten für Ablationen | HTTP | siehe plan.md, Abschnitt 4 |
| `official` | transport.opendata.ch/mcp | HTTP | offizieller Server, gleiche Datenquelle; gehostet, Version nicht fixierbar, Hash von `tools/list` pro Lauf |
| `spt` | swiss-public-transport-mcp (PyPI) | stdio | gleiche Datenquelle |
| `stm` | swiss-transport-mcp (PyPI) | stdio | optional; OJP, braucht kostenlosen API-Key von opentransportdata.swiss |

Bestimmt in Task 1.1 (2026-11-25; Snapshots von `initialize` und `tools/list` in `tests/fixtures/foreign-servers/`):

| ID | Version | Lizenz | Tools | Tool-Definitionen | Start | Bemerkungen |
|---|---|---|---|---|---|---|
| `official` | gehostet, `serverInfo.version` `2c28784a` | – (Dienst von Opendata.ch) | 4: `find_stations`, `stations_nearby`, `connections`, `departures` | ca. 3'100 Zeichen | `https://transport.opendata.ch/mcp` (stateless, kein Key) | read-only Annotations; ausführliche `instructions` (Zeitzone, Mehrdeutigkeit, Fahrplanfenster); Ergebnisse als kurzer Text |
| `spt` | 0.1.10 (2026-05-15) | MIT | 4: `search_locations`, `plan_journey`, `get_stationboard`, `get_booking_link` | ca. 4'700 Zeichen | `uvx --exclude-newer 2026-05-16T00:00:00Z --from swiss-public-transport-mcp==0.1.10 swiss-public-transport-mcp` | keine Tool-Annotations; `serverInfo.version` meldet die SDK-Version (1.27.1); startet nur mit Abhängigkeiten vom Erscheinungstag (siehe Entscheidungsprotokoll) |
| `stm` | 0.5.0 (2026-09-26) | MIT | 11: u. a. `transport_search_stop`, `transport_trip_plan`, `transport_departures`, `get_train_occupancy`, `get_transport_disruptions`, `get_ticket_price` | ca. 20'500 Zeichen | `uvx --exclude-newer 2026-09-27T00:00:00Z --from swiss-transport-mcp==0.5.0 swiss-transport-mcp` | Key in `TRANSPORT_API_KEY` (aus `STM_TRANSPORT_API_KEY`); Störungen und Auslastung brauchen eigene Keys (`SIRI_SX_API_KEY`, `OCCUPANCY_API_KEY`); sechs Tools nehmen alle Parameter in einem Objekt `params`; `instructions` sprechen von 10 Tools |

## 6. Aufgaben-Set

- Mindestens 45 Aufgaben, aufgeteilt in Entwicklungs-Set (`dev`, ca. 25) und zurückgehaltenes Test-Set (`test`, ca. 20). Die Aufteilung ist nach Kategorie stratifiziert.
- Kategorien: `stops`, `trip_simple`, `trip_constraints` (z. B. Ankunft vor einem Termin, Umsteigezeit, `via`), `departures`, `multistep`, `ambiguous` (mehrdeutige Ortsnamen), `bad_input`, `fr`, `extended` (Störungen, Auslastung; nur einzelne Server).
- Erfolg bei `ambiguous`: Der Agent fragt nach oder nennt die Kandidaten. Rät er still einen Treffer, ist die Aufgabe nicht bestanden.
- Rund 6 `dev`-Aufgaben sind als `smoke` markiert und bilden die Suite für `bench run --suite smoke`.
- Die Aufgaben werden geschrieben, bevor die Perron-Tools gebaut werden (Task 1.3): `dev` von Claude, `test` von einem isolierten Claude-Subagenten, beide stichprobenartig von Sandro geprüft (siehe Entscheidungsprotokoll in plan.md). Das ist eine bekannte Schwäche des Benchmarks und wird im README genannt. Zeiten werden relativ formuliert (z. B. «morgen um 08:00»), die Referenzzeit gibt der Harness im System-Prompt vor.
- Zeiten in Aufgaben bleiben im Fahrplanfenster der API (etwa eine Woche zurück bis gut drei Monate voraus; ausserhalb antwortet die API mit Fehler 400, siehe plan.md, Abschnitt 3).
- Das Test-Set wird nach dem Schreiben eingefroren; sein SHA-256-Hash steht im Entscheidungsprotokoll von plan.md.

## 7. Qualitätsziele

| Metrik | Ziel |
|---|---|
| Aufgabenerfolg Perron (Test-Set, abgedeckte Aufgaben, Haiku 5.5) | ≥ 0.85, mit Konfidenzintervall ausgewiesen |
| Übereinstimmung Judge mit Handprüfung | ≥ 0.90 bei mindestens 40 Urteilen |
| Reproduzierbarkeit | Streuung und pass^3 über 3 Wiederholungen werden ausgewiesen |
| Kosten eines vollen Laufs | gemessen, vorher per `--dry-run` geschätzt |
| Modelle | Haiku 5.5 für alle Läufe; Schlussvergleich zusätzlich mit Sonnet 5.5 als Kontrolle |

Es gibt bewusst kein Ziel «Perron muss gewinnen». Das Ziel ist ein ehrlicher Vergleich.

## 8. Nicht-Ziele

- Keine Ticketpreise, Buchungen oder schreibenden Tools.
- Kein Patchen oder Nachbauen fremder Server.
- Keine Ranglisten-Website; der Report liegt im Repo.
- Kein Vergleich verschiedener LLM-Anbieter im MVP.
- Kein Italienisch im MVP.

## 9. Offene Fragen

- Lizenzen der fremden Server prüfen, bevor Ergebnisse mit Namen veröffentlicht werden. Den Autoren die Ergebnisse vorab zeigen?
- Störungen und Auslastung später im eigenen Server ergänzen? Seit November 2026 liefert transport.opendata.ch eine Auslastungsprognose (`capacity1st`/`capacity2nd`) ohne zusätzlichen Key; Perron könnte die Fähigkeit `occupancy` damit abdecken.
- `plan_trip` um `minTransferTime` (Mindest-Umsteigezeit, 0–60 min) ergänzen? Die API kann es direkt; heute muss der Agent selbst filtern.
- `get_departures` um die weiteren Halte ergänzen (z. B. Parameter `stops_at`)? Die API liefert sie (`passList`), mitgeliefert kosten sie aber viele Tokens. Heute beantwortet `plan_trip` Fragen wie «hält der Zug in Thun?» (Task 2.3).
- Bench als wiederverwendbares Werkzeug für beliebige MCP-Server veröffentlichen (`dotnet tool`)?
- Tracing über OpenTelemetry (das MCP-SDK bietet eine `ActivitySource`)?
- Schemas portabler machen? Optionale Parameter stehen als `"type": ["string", "null"]` im Schema; das ist gültiges JSON Schema und für Claude kein Problem, der MCP Inspector warnt aber, dass Clients mit einem Dialekt ohne Typ-Arrays (z. B. Gemini) solche Tools ablehnen könnten. Alternative: `anyOf` oder Parameter ohne `null`. Ändert `tools/list` und braucht deshalb einen Bench-Vergleich (Task 2.6).
- Volle 2×2-Ablation (Beschreibungen × Format, also auch `minimal` + `json`), um Wechselwirkungen zu messen?
