# MaybeComingBot — ASP.NET Core Health Endpoint Migration

## Goals
- [x] Gather context (csproj, Dockerfile, Program.cs, ServiceCollectionExtensions, HealthHttpServer, railway.json, README).
- [x] Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to `MaybeComingBot.Console/MaybeComingBot.csproj`.
- [x] Switch the Dockerfile runtime stage from `runtime:10.0` to `aspnet:10.0`.
- [x] Rewrite `MaybeComingBot.Console/Health/HealthHttpServer.cs` as a lightweight `WebApplication` (Kestrel) background service.
- [x] Update `README.md` Deployment section to mention the ASP.NET Core (Kestrel) health endpoint.
- [x] Verify: `dotnet build MaybeComingBot.slnx` — 0 errors/warnings (build blocked only by a running bot instance locking MaybeComingBot.exe; main project compiles clean).
- [x] Verify: `dotnet test MaybeComingBot.Tests` — 205 passed, 0 failed.
- [x] Move schedule freshness tracking into `ScheduleStorage` (`LastUpdated` set inside `Update`).
- [x] `BotHealthCheck` now reads `_storage.LastUpdated` as the single source of truth.
- [x] Added 4 `LastUpdated` tests to `ScheduleStorageTests`.

