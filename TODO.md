# MaybeComingBot — Timezone Fix

## Goals
- [x] Add `TimeZoneId` setting to `BotSettings` (default `Europe/Minsk`).
- [x] Add `TimeHelper.GetLocalNow(TimeZoneInfo)` helper.
- [x] Register a singleton `TimeZoneInfo` in DI from `BotSettings.TimeZoneId`.
- [x] Use the configured timezone in `ScheduleFormatter` (`FormatNearest`, `FormatToday`).
- [x] Use the configured timezone in `ScheduleRefreshService` admin status timestamps.
- [x] Validate `TimeZoneId` in `BotSettingsValidator`.
- [x] Add `TimeZoneId` to `appsettings.Template.json`.
- [x] Update `ScheduleFormatterTests` to use a deterministic timezone and add a 3-hour-offset test.
- [x] Document `TimeZoneId` and `TZ` override in `README.md`.
- [x] Verify: `dotnet build MaybeComingBot.slnx` — 0 errors/warnings.
- [x] Verify: `dotnet test MaybeComingBot.Tests` — all pass (206/206).
