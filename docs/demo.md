# Demo recording

Script for `docs/demo.gif`, the short Claude Code session shown at the top of the README. It shows two things Perron's tools do: a minimum transfer time and candidates for an ambiguous stop name.

## Setup

```bash
docker compose up -d --wait        # Perron MCP on http://127.0.0.1:8080/mcp
claude                             # in the repository; approve the project server "perron" once
```

- Terminal about 100 × 30 characters, a large font, light or dark theme.
- Nothing personal on screen: no account or `/status` view, no `.env`, no API keys, no paths with a user name if possible.

## Prompts

1. `Wie komme ich morgen um 8 Uhr von Bern nach Zermatt? Ich habe schweres Gepäck und brauche mindestens 10 Minuten zum Umsteigen.`
   Claude calls `plan_trip` with `min_transfer_minutes: 10` and answers with a connection.
2. `Und welche Abfahrten gibt es morgen um 7 ab Buchs?`
   Perron lists the candidates (Buchs AG, Buchs SG …) and Claude asks which one is meant.

Approve the tool calls when Claude Code asks.

## Recording

- Windows: [ScreenToGif](https://www.screentogif.com/), recording only the terminal window, about 10 frames per second.
- Cut the waiting time for the model, keep the tool calls and the answers readable; about 20 to 40 seconds in total.
- Save as `docs/demo.gif`, at most 5 MB.
