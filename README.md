# MaybeComingBot

A .NET Telegram bot that provides public-transport (bus) schedule information for a configurable set of stops. Users select a stop, then query the nearest trip, today's full schedule, or a weekly overview. The schedule data is loaded periodically from a remote JSON endpoint and cached in memory.

Built with **.NET 10**, **Telegram.Bot 22.x**, **Polly** (resilience), **Serilog** (logging), and **xUnit/Moq** (tests).

---

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Configuration reference](#configuration-reference)
- [Commands](#commands)
- [Schedule JSON format](#schedule-json-format)
- [Testing](#testing)
- [Deployment](#deployment)
- [Logging](#logging)
- [Project structure](#project-structure)

---

## Features

- **Stop selection** via inline keyboards — users pick their stop once and reuse it.
- **Nearest trip** — shows the upcoming departure and the following one, including wait times.
- **Today's schedule** — full day with ✅ passed / 🔴 now / ⬜ upcoming markers.
- **Weekly schedule** — a 7-day overview with trip times per day.
- **Admin commands** — `/update` (force schedule refresh) and `/status` (bot health, stop count, active users).
- **Bug reporting** — `/report <text>` forwards the message to all configured admins. Users can also contact the admin through a clean `t.me/<username>` link (configured via `SupportUsername`) — **no numeric admin IDs are ever exposed**.
- **Readable UI** — friendly sentence-case messages, emoji-labeled reply buttons (with plain-text fallback), and a polished help/status screen.
- **Automatic schedule refresh** — background service with exponential backoff + jitter and thread-safe locking.
- **Rate limiting** — per-user sliding window (admins exempt).
- **User-state TTL cleanup** — stale user sessions are removed periodically.
- **Resilience** — Polly-based retry/timeout policies for HTTP and Telegram API calls, with Markdown-parse fallback and text truncation.
- **Health checks** — `BotHealthCheck` reports Healthy / Degraded / Unhealthy status for monitoring.

---

## Architecture

The application follows a layered design with dependency injection.

```
Program.cs                     — entry point: config, Serilog, host, admin validation, polling loop + restart
 │
 ├── Abstractions/             — interfaces (storage, state, refresh, sender)
 ├── Services/                 — business logic
 │   ├── BotHandler            — routes Telegram updates (messages/callbacks)
 │   ├── CommandDispatcher     — command routing, admin commands, reports
 │   ├── ScheduleFormatter     — text formatting for Nearest / Today / Week
 │   ├── ScheduleLoader        — HTTP fetch + JSON deserialization of the schedule
 │   ├── ScheduleRefreshService— hosted background refresh loop
 │   ├── ScheduleStorage       — thread-safe in-memory schedule cache
 │   ├── StopSelectionGuard    — validates the user has a valid stop selected
 │   ├── UserStateService      — thread-safe per-user session state
 │   ├── UserStateCleanupService— hosted TTL cleanup of user sessions
 │   └── TelegramSender        — single outbound adapter to the Telegram API
 ├── Utils/                    — rate limiter, time helper, Polly policies, settings validator
 ├── Models/                   — BotSettings, Stop, Trip, DaySchedule, ScheduleRoot, UserState
 ├── Constants/                — commands, messages, time constants
 └── Extensions/               — configuration and DI wiring
```

**Key design decisions:**

- **`ITelegramSender` abstraction** — all outbound Telegram calls (send message, answer callback, edit message, chat actions) go through this interface. It centralizes retries, text truncation, and Markdown fallback, and keeps `BotHandler` / `CommandDispatcher` unit-testable with mocks (the raw `ITelegramBotClient` extension methods cannot be mocked in Telegram.Bot 22.x).
- **Single `ScheduleRefreshService` instance** — it is registered as both a `BackgroundService` (via `AddHostedService`) and as the `IScheduleRefreshService` singleton, so `/status` and the health check observe the same running instance.
- **`ScheduleStorage`** is thread-safe (`ReaderWriterLockSlim`) and clones data on write to prevent mutation from outside.

---

## Prerequisites

- [.NET SDK 10.0](https://dotnet.microsoft.com/download) (or later)
- A Telegram bot token from [@BotFather](https://t.me/BotFather)
- A hosted JSON file with the schedule data

---

## Setup

1. **Clone the repository**

2. **Create `appsettings.json`** in `MaybeComingBot.Console/` based on the template:

   ```bash
   cp MaybeComingBot.Console/appsettings.Template.json MaybeComingBot.Console/appsettings.json
   ```

3. **Fill in your configuration** — at minimum `BotToken`, `ScheduleUrl`, and `AdminUserIds` (see [Configuration reference](#configuration-reference)).

4. **Run the bot:**

   ```bash
   cd MaybeComingBot.Console
   dotnet run
   ```

   The bot validates its admin users at startup, loads the schedule, and starts polling. Press `Ctrl+C` to stop.

> ⚠️ Never commit `appsettings.json` with a real token. Keep the token in environment variables or a secret store (env vars override `appsettings.json`).

---

## Configuration reference

All settings live under the `BotSettings` section.

| Key | Type | Default | Description |
|---|---|---|---|
| `BotToken` | string | — | Telegram bot token (required, ≥10 chars). |
| `ScheduleUrl` | string | — | Absolute URL of the schedule JSON (required). |
| `AdminUserIds` | long[] | — | At least one admin Telegram user ID (required). |
| `SupportUsername` | string | — | Optional public Telegram username (without `@`) shown to users as the admin contact link. When omitted, no contact link is rendered (users are guided to `/report`). |
| `UpdateIntervalHours` | int | 24 | Hours between automatic schedule refreshes; `0` disables auto-update (0–168). |
| `UserStateTtlHours` | int | 48 | Inactivity TTL for user sessions (1–48). Should be ≥ `UpdateIntervalHours`. |
| `RateLimitMaxRequests` | int | 5 | Max requests per user per window (1–20). |
| `RateLimitWindowMinutes` | int | 1 | Rate-limit window length (1–10). |
| `MaxRetryCount` | int | 3 | HTTP retry attempts for transient errors (1–5). |
| `RetryBaseDelaySeconds` | int | 2 | Base delay for exponential backoff (1–60). |
| `TimeoutSeconds` | int | 30 | HTTP request timeout in seconds (5–120). |

Cross-property rules enforced by `BotSettingsValidator`:

- `RateLimitMaxRequests > 10` requires `RateLimitWindowMinutes >= 2`.
- `UpdateIntervalHours` must not exceed `UserStateTtlHours`.
- `TimeoutSeconds >= 10` recommended when `MaxRetryCount > 1`.

---

## Commands

| Command / Button | Access | Description |
|---|---|---|
| `/start` | all | Show stop selection menu. |
| `/help` | all | Command reference. |
| `/cancel` | all | Clear state / abort current flow. |
| `/report <text>` | all | Send a bug report to all admins. |
| `/update` | admin | Force an immediate schedule refresh. |
| `/status` | admin | Show bot status, last update, stop count, active users. |
| `🚌 Nearest` / `Nearest` | all | Next departure + waiting time. |
| `📅 Today` / `Today` | all | Full today's schedule. |
| `🗓 Week` / `Week` | all | 7-day overview. |
| `🔄 Change stop` / `🔙 Back` | all | Return to stop selection. |
| `📨 Report bug` | all | Show report instructions / admin contact (via `SupportUsername` when set). |

---

## Schedule JSON format

The `ScheduleUrl` endpoint must return JSON matching the following shape:

```json
{
  "stops": [
    {
      "id": "ST001",
      "name": "Central Station",
      "schedule": [
        {
          "day": 1,
          "trips": [
            { "time": "07:15", "bus": 42, "note": "Express" },
            { "time": "24:00", "bus": 7 }
          ]
        }
      ]
    }
  ]
}
```

Notes:

- `day` is `0` (Sunday) through `6` (Saturday).
- `time` is `HH:mm` (24-hour). The special value `24:00` represents end-of-day.
- Parsing is case-insensitive and tolerant of trailing commas and comments.

---

## Testing

```bash
dotnet test MaybeComingBot.slnx
```

The test project covers:

- `BotHandler` — update routing, message filtering, rate limiting, callback dispatch.
- `CommandDispatcher` — start/help/status/update/report, stop-selection guards, callbacks.
- `ScheduleFormatter` — nearest/today/week formatting.
- `ScheduleLoader` — loading and deserialization.
- `ScheduleRefreshService` — refresh loop and backoff behavior.
- `ScheduleStorage` — thread-safe storage and caching.
- `StopSelectionGuard` — stop-selection validation.
- `UserStateService` — session management and TTL.
- `RateLimiter`, `TimeHelper` — utility behavior.

---

## Deployment

The bot is a plain console application and can be deployed as:

- A **Windows service** (via NSSM, etc.).
- A **systemd unit** on Linux (`.NET 10` runtime or self-contained publish).
- A **container** (Docker) for portability.

Publish a self-contained build:

```bash
dotnet publish MaybeComingBot.Console/MaybeComingBot.csproj -c Release -r win-x64 --self-contained true
```

Recommended production practices:

- Store secrets via environment variables or a secrets manager.
- Persist `logs/` to a durable volume and configure `.gitignore`.
- Monitor health via a health-check endpoint or log scraping.
- Rotate the bot token and schedule URL via configuration, not code.

---

## Logging

Serilog is configured in `appsettings.json` under the `Serilog` section:

- **Console** output for interactive/container use.
- **File** output to `logs/bot-*.txt` (daily rolling, 31 files retained).

Minimum levels are overridable per namespace (e.g., `Telegram.Bot` → `Warning`).

---

## Project structure

```
MaybeComingBot.slnx                 — solution (console + tests)
MaybeComingBot.Console/             — main application
MaybeComingBot.Tests/               — unit tests
```

See `docs/CODE_REVIEW.md` for a detailed code review with findings and recommendations.

