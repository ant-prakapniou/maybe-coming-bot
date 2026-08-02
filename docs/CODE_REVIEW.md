# MaybeComingBot — Code Review

This document summarizes the code review of the MaybeComingBot Telegram bot, which has been **fixed**, **cleaned up**, **documented**, and **tested** as part of this task. The review findings are grouped by severity. All items listed as **Resolved** have already been addressed in the codebase.

---

## Summary

| Area | Verdict |
|---|---|
| Build | ✅ 0 warnings, 0 errors |
| Tests | ✅ 193 tests passing |
| Test coverage | ✅ Core services covered (BotHandler, CommandDispatcher, storage, state, refresh, formatting, loader, guard, utilities) |
| Architecture | ✅ Layered DI, single outbound Telegram adapter, background services |
| Docs | ✅ README + this review + XML documentation across the codebase |

---

## High severity

### 1. Hosted services were never started — `/status` and health check always showed "NOT LOADED" — **Resolved**

**Finding:** `Program.Main` built the host but never called `host.StartAsync()`. The `ScheduleRefreshService` (initial schedule load + periodic refresh) and `UserStateCleanupService` therefore never ran. Any consumer of `IScheduleRefreshService` observed `LastSuccessfulUpdate == DateTime.MinValue` and `HasData() == false`.

**Fix:** `Program.cs` now calls `await host.StartAsync()` before the polling loop and stops the host gracefully with a timeout inside a `finally`.

**Recommendation (future):** Wrap the entire bot lifecycle in `IHost.RunAsync()` or run the polling loop itself as a hosted service so the host fully controls start/stop.

### 2. Multiple distinct `ScheduleRefreshService` instances broke status/health consistency — **Resolved**

**Finding:** `ScheduleRefreshService` was registered both via `AddHostedService<ScheduleRefreshService>()` and separately as `AddSingleton<IScheduleRefreshService>()`. Those are two different instances: the background loop updated one instance while `/status` and `BotHealthCheck` read another (which never loaded data).

**Fix:** Register a single `ScheduleRefreshService` singleton and map both the hosted service registration and the `IScheduleRefreshService` resolution to that same instance.

**Recommendation (future):** Add a test that asserts a single shared instance is resolved from the DI container for both registrations.

---

## Medium severity

### 3. Hardcoded 30s HTTP timeout ignored configured `TimeoutSeconds` — **Resolved**

**Finding:** `ServiceCollectionExtensions` set `HttpClient.Timeout = TimeSpan.FromSeconds(30)` and `ScheduleLoader` used a private 30s timeout constant, ignoring `BotSettings.TimeoutSeconds`.

**Fix:** The HTTP client is now configured from `settings.TimeoutSeconds`, and the Polly timeout policy reads it from options.

**Recommendation (future):** Use `AddHttpClient`'s typed-client options and centralize policy wiring so timeout/retry config cannot drift.

### 4. Direct `ITelegramBotClient` calls in handlers were untestable — **Resolved**

**Finding:** `BotHandler` and `CommandDispatcher` called `SendMessage(...)` / `AnswerCallbackQuery(...)` directly on `ITelegramBotClient`. In Telegram.Bot 22.x these are **extension methods**, which Moq cannot intercept, making those classes effectively untestable. This was also the reason `BotHandlerTests.cs`/`CommandDispatcherTests.cs` were empty.

**Fix:** Both classes now depend on the **`ITelegramSender`** abstraction (which is a plain interface and mockable). All outbound sends, callback answers, edits, and chat actions are routed through it. Direct `ITelegramBotClient` usage is confined to the composition root (`Program.cs`, `ServiceCollectionExtensions`) and the `TelegramSender` adapter, which centralizes retry/truncation/Markdown fallback logic.

**Recommendation (future):** Keep the rule "services never touch `ITelegramBotClient` except through `TelegramSender`" by adding an analyzer or architecture test.

### 5. `Program.Main` duplicated host-building code that lived in two dead classes — **Resolved**

**Finding:** `Infrastructure/BotHostBuilder.cs` and `Extensions/HostBuilderExtensions.cs` were never referenced from `Program.cs`, yet duplicated the host-building logic (a recipe for drift).

**Fix:** Both files were deleted. `Program.cs` is the single composition root.

### 6. Admin numeric ID was exposed in the "contact admin" link — **Resolved**

**Finding:** `CommandDispatcher.ShowReportInstructionsAsync` built the contact link as `tg://user?id={firstAdminId}`, which leaked the administrator's numeric Telegram user ID to every user who pressed `📨 Report bug`. Numeric IDs are sensitive identifiers that can be abused for harassment, spam, or targeted attacks.

**Fix:** The bot now supports an optional **`SupportUsername`** setting (a public Telegram username without the leading `@`). When configured, the contact link is rendered as `https://t.me/<username>` and labeled `Contact @<username>`; when omitted, no contact link is shown at all and users are guided to `/report`. The numeric admin ID is never placed in a user-visible link. `BotSettingsValidator` enforces Telegram username rules (5–32 chars, letters/digits/underscore) when the value is provided, and `appsettings.Template.json` / `README.md` document the new key.

**Recommendation (future):** Consider rendering the report instructions with an inline "Contact support" button (`InlineKeyboardButton.WithUrl`) for a more prominent call-to-action.

---

## Low severity

### 7. Unused public API surface on domain models — **Resolved**

**Finding:** Several model helper methods (`CommandCount`/`IncrementCommandCount`/`ResetCommandCount` on `UserState`, `GetSelectedStop`/`HasValidStop` on `StopSelectionGuard`, `GetCombinedPolicy` in `PollyPolicyExtensions`, and `Validate()` on `BotSettings`) were not used by any production code.

**Fix:** Removed the unused members and their redundant references.

**Recommendation (future):** Run a solution-wide usage scan (e.g., `dotnet-format`/`roslynator`/`Microsoft.CodeAnalysis.NetAnalyzers` with `IDE0051`) periodically to detect dead code before it accumulates.

### 8. Duplicate/confusing XML `<remarks>` blocks — **Resolved**

**Finding:** Several classes (`ScheduleLoader`, `ScheduleRefreshService`, `UserStateService`) had two `<remarks>` blocks — one from a primary-constructor doc and one from the class doc — producing duplicate or merged documentation.

**Fix:** Removed the duplicate `<remarks>` blocks and kept a single, accurate class-level description.

**Recommendation (future):** Treat primary-constructor params as holes to fill in the **class-level** `<remarks>`/`<param>` documentation, and validate with the XML-doc analyzer.

### 9. Magic strings and inline literals in command handling — **Resolved**

**Finding:** Several user-facing messages and handler branch strings were inlined (`"⚠️ CHAT NOT FOUND..."`, `"⛔ SESSION EXPIRED..."`, etc.) instead of living in `BotMessages`.

**Fix:** Extracted all of them into named constants in `BotMessages` and `BotCommands`.

**Recommendation (future):** Consider localization resource files (`.resx`) if the bot is ever localized.

### 10. Misplaced `///` comments inside method bodies — **Resolved**

**Finding:** `ServiceCollectionExtensions.AddBotServices` contained `///` doc comments *inside* the method between statements, which is not valid documentation placement.

**Fix:** Converted them to `//` inline comments.

---

## Recommendations for future work

1. **Persistent state.** The schedule and user states live only in memory. If the process restarts, all user stop selections are lost. Consider persisting user state to a lightweight store (SQLite, Redis) so a restart is invisible to users.

2. **Scheduled "notify me" feature.** The stale/departed data logic in `ScheduleFormatter` could power proactive "next bus in 5 minutes" notifications if combined with a per-user subscription store.

3. **Webhook mode.** The bot currently uses long polling. For production with HTTPS, Telegram webhooks remove the need for a persistent connection and simplify scaling.

4. **Health-check endpoint.** `BotHealthCheck` is registered but there is no HTTP endpoint exposing it. Add `AddHealthChecksUI` or an endpoint middleware if you host it in a long-running process/service.

5. **Structured logging for analytics.** User interactions are logged, but there is no metrics pipeline. Consider emitting metrics (active users, rate-limit hits, refresh failures) to Prometheus/OpenTelemetry.

6. **Configuration hardening.** `BotToken` is validated but stored in plaintext in `appsettings.json`. Use `dotnet user-secrets` (dev), environment variables, or a secrets manager (prod).

7. **More tests.**
   - `TelegramSender` (retry, truncation, Markdown fallback) currently has no dedicated test file.
   - `BotHealthCheck` has no test file.
   - Integration tests with a real `appsettings` + fake HTTP handler for `ScheduleLoader` would raise confidence.

8. **CI/CD.** Add a GitHub Actions/Azure Pipeline workflow running `dotnet build` + `dotnet test` on each PR, plus a publish step on tags.

---

## Verification commands

```bash
dotnet build MaybeComingBot.slnx      # 0 warnings / 0 errors
dotnet test MaybeComingBot.slnx        # 193 passed / 0 failed
```

