# incident-factory

Local-only incident and observability demo system for IncidentCompass. It intentionally produces
repeatable failures for diagnosis and repair experiments.

## Safety boundary

- This repository contains deliberately fragile and unsafe behavior. Run it only locally or in an
  isolated sandbox. Never deploy it to shared, internet-facing, or production-like environments.
- Keep unsafe behavior explicit, scenario-driven, documented, easy to trigger, and easy to reset.
- Keep control, disable, schedule, reset, and health endpoints fault-free so every scenario can be
  stopped reliably.
- Do not reuse intentionally unsafe scenario code in real systems.

## Stack

- .NET 8 ASP.NET Core API and scenario host.
- React 19, TypeScript, and Vite control panel.
- xUnit tests.
- Optional local OpenTelemetry export.

## Build, test, and run

```powershell
dotnet restore IncidentFactory.sln
dotnet build IncidentFactory.sln
dotnet test IncidentFactory.sln

cd web
npm ci
npm run build
```

Run the backend locally with:

```powershell
dotnet run --project src/IncidentFactory.Api --launch-profile http
```

Run the development control panel from `web/` with `npm run dev`.

## Project contracts

- Preserve the IncidentCompass HTTP ingest path as the authoritative signal delivery path until
  IncidentCompass has a supported OTLP receiver.
- OpenTelemetry is optional and local-safe. Do not make a collector or external infrastructure a
  default requirement.
- Read IncidentCompass reports only through its HTTP API. Never query its PostgreSQL database.
- Keep Tier-2 diagnosis targets isolated in `IncidentFactory.HardScenarios`.
- Keep private repair-grading expectations out of public docs, UI text, and public tests.
- Do not move cross-service deployment orchestration into this repository.

## Layout

- `src/IncidentFactory.Api/` - API host, Tier-1 scenarios, scheduling, transport, and static UI host.
- `src/IncidentFactory.HardScenarios/` - isolated Tier-2 diagnosis and repair targets.
- `tests/IncidentFactory.Tests/` - harness, boundary, and signal-contract tests.
- `web/` - React and Vite control panel.
- `.local/` - ignored planning, run logs, and scratch material.

## Change discipline

- Keep intentional failures separate from accidental defects in the harness.
- Preserve stable scenario ids and signal-contract fields unless the corresponding tests and
  IncidentCompass integration are updated together.
- Stage only intended paths. Do not include generated `wwwroot`, `node_modules`, build output, or
  `.local/` content in commits.
