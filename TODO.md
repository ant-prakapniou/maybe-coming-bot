# MaybeComingBot — Railway (Docker) Deployment

## Goals
- [x] Create a multi-stage `Dockerfile` (.NET 10 sdk → runtime) for Railway.
- [x] Add `.dockerignore` to keep secrets/build artifacts out of the image.
- [x] Add `railway.json` (Dockerfile builder, restart policy, healthcheck path).
- [x] Add a minimal HTTP `/health` endpoint (`HealthHttpServer`) so Railway runs the bot as a Web Service.
- [x] Register the health server as a hosted service in `ServiceCollectionExtensions`.
- [x] Update `README.md` Deployment section with Railway env vars and steps.
- [x] Verify: `dotnet build` — 0 errors/warnings.
- [x] Verify: `dotnet test` — all 201 pass, 0 failed.

## Notes
- Docker is not installed locally, so the image itself was not built locally; the code is validated via `dotnet build`/`dotnet test` on the installed .NET 10 SDK.
- The bot ships `appsettings.Template.json` as `appsettings.json`; all real values come from Railway variables (`BotSettings__*`).

