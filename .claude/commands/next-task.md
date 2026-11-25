Lies `CLAUDE.md` sowie `specs/001-perron/spec.md`, `plan.md` und `tasks.md`.

Bearbeite den Task $ARGUMENTS. Ist nichts angegeben, nimm den ersten offenen Task in `tasks.md`.

1. `git checkout main && git pull`, dann Branch `task/<nr>-<kurzname>` anlegen (Ausnahme: Task 0.1 legt das Repo mit `git init -b main` an und arbeitet direkt auf `main`, siehe tasks.md).
2. Fasse zusammen, was der Task verlangt und welche Akzeptanzkriterien aus `spec.md` betroffen sind.
3. Prüfe bei neuen SDK-Funktionen (MCP-SDK, Anthropic-SDK) die aktuelle Doku oder die Samples, bevor du Code schreibst.
4. Schlage einen kurzen Umsetzungsplan vor (Dateien, Tests, Tool-Beschreibungen im Wortlaut, Änderungen an Docker oder `.env.example`, offene Punkte) und warte auf meine Bestätigung.
5. Setze den Task um, inklusive Tests. Passe Dockerfiles, `docker-compose.yml` und `.env.example` im selben Task an, falls nötig.
6. Führe `dotnet build -warnaserror`, `dotnet format --verify-no-changes` und `dotnet test` aus und behebe Fehler.
7. Prüfe den Container: `docker compose up -d --build`, `mcp` healthy, `docker compose --profile bench run --rm bench check` läuft.
8. Betrifft der Task Tools, Beschreibungen, Formate oder den Harness und existiert `bench run` schon: Schätze die Kosten mit `--dry-run`, führe dann `bench run --suite smoke` aus und zeige die Veränderung. Bei Läufen über alle Server oder mit `full` vorher bei mir nachfragen.
9. Hake den Task in `tasks.md` ab, aktualisiere README und `.env.example` falls nötig, ergänze bei Abweichungen das Entscheidungsprotokoll in `plan.md`.
10. Committe in sinnvollen Schritten (Conventional Commits, Englisch), pushe den Branch und eröffne mit `gh pr create` einen Pull Request gemäss `CLAUDE.md`. Nicht mergen.
