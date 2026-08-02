# MaybeComingBot — Admin Contact Privacy & UI Polish Plan

## 1. Privacy fix — never expose admin IDs
- [x] 1.1 `Models/BotSettings.cs`: add optional `SupportUsername` with @-stripping normalization.
- [x] 1.2 `Utils/BotSettingsValidator.cs`: validate `SupportUsername` format (5-32 chars, letters/digits/underscore) when provided.
- [x] 1.3 `Services/CommandDispatcher.cs`: rework `ShowReportInstructionsAsync` to use `t.me/<username>` when configured, otherwise no link; never `tg://user?id=`.
- [x] 1.4 `Constants/BotMessages.cs`: update `ReportInstructions` to show the admin's username instead of a numeric ID.

## 2. UI/UX polish
- [x] 2.1 `Constants/BotCommands.cs`: split canonical tokens from emoji display labels.
- [x] 2.2 `Services/CommandDispatcher.cs`: use emoji labels on keyboard; normalize tapped emoji buttons; polish `/status` and admin update messages.
- [x] 2.3 `Constants/BotMessages.cs`: rewrite messages in friendly, readable sentence case.
- [x] 2.4 `Services/ScheduleFormatter.cs`: polish output headings and messages.
- [x] 2.5 `Constants/BotCommands.cs`: extract admin refresh button label constant.

## 3. Config & docs
- [x] 3.1 `appsettings.Template.json`: add optional `SupportUsername` key.
- [x] 3.2 `README.md`: document `SupportUsername`, update features/commands.
- [x] 3.3 `docs/CODE_REVIEW.md`: add resolved finding for admin ID exposure.

## 4. Tests
- [x] 4.1 Update `CommandDispatcherTests` for new texts + add no-ID-leak and username-link tests.
- [x] 4.2 Update `BotHandlerTests` for new texts + emoji button routing.
- [x] 4.3 Update `ScheduleFormatterTests` for polished output.

## 5. Verification
- [x] 5.1 `dotnet build MaybeComingBot.slnx` — 0 warnings / 0 errors.
- [x] 5.2 `dotnet test MaybeComingBot.slnx` — all 199 pass, 0 failed.

---

# MaybeComingBot — Admin Contact Link & Reporter Username Fix

## 1. Admin contact link improvements
- [x] 1.1 `Constants/BotMessages.cs`: report instructions now render the admin's **@name itself as the clickable link** (`[👤 @support_admin](https://t.me/support_admin)`) instead of `Contact @support_admin`.
- [x] 1.2 `Constants/BotMessages.cs`: `/help` message now includes an admin contact link (`👤 *Contact support:* [@support_admin](https://t.me/support_admin)`) when `SupportUsername` is configured.
- [x] 1.3 `Services/CommandDispatcher.cs`: `/help` passes `_supportUsername` into `BotMessages.Help(...)`.

## 2. Reporter username formatting fix
- [x] 2.1 `Services/CommandDispatcher.cs`: the reporting user's username is no longer Markdown-escaped in the admin notification, so `@part1_part2` renders as a **tappable mention** instead of `@part1\_part2`.

## 3. Tests
- [x] 3.1 `CommandDispatcherTests`: updated `RendersTmeLink` to expect the @name link label (no `Contact @`).
- [x] 3.2 `CommandDispatcherTests`: added `DispatchHelp_WithSupportUsername_IncludesAdminContactLink`.
- [x] 3.3 `CommandDispatcherTests`: added `DispatchReport_WithText_UsernameIsNotEscaped_KeepsMentionLink`.

## 4. Verification
- [x] 4.1 `dotnet build MaybeComingBot.slnx` — 0 warnings / 0 errors.
- [x] 4.2 `dotnet test MaybeComingBot.slnx` — all 201 pass, 0 failed.

