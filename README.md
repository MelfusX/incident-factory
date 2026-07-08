# incident-factory

A deliberately fragile incident demo system for IncidentCompass and observability experiments.

> ⚠️ **Intentionally vulnerable / breakable code.** This project ships fault-prone and unsafe code paths on purpose. Do **not** deploy it to any shared, internet-facing, or production-like environment, and do not reuse its code in real systems. Run it only locally or in an isolated sandbox.

This repository will contain a fake business system, a control UI, and scenario logic that can create repeatable failures on demand. The purpose is to make incidents visible and inspectable, not to model a real production system.

## Role

- Provide one or more fake services with intentionally unsafe or failure-prone code paths.
- Provide a control-panel UI for triggering, tuning, scheduling, and resetting failures.
- Generate repeatable scenarios such as latency spikes, exceptions, bad configuration, retry storms, database timeouts, and broken downstream calls.
- Keep scenario logic close to the fake system that produces the behavior.
- Deliver incident signals to IncidentCompass first, by POSTing signal envelopes to its HTTP ingest API.
- Emit optional local OpenTelemetry logs, metrics, and traces for the same scenarios without replacing the IncidentCompass HTTP delivery path.
- Later allow IncidentCompass to propose fix pull requests against this repository.

## Repository boundaries

This repo should not own the full multi-service demo deployment or cross-service orchestration. That lives in a separate deployment-environment repository.

This repo may provide:

- Dockerfiles for its own services.
- Local development compose files for the factory itself.
- Scenario definitions and seed data.
- API/UI code for controlling failures.

## Planned structure

```text
incident-factory/
  README.md
  LICENSE
  .gitignore
  Dockerfile                        # multi-stage: .NET publish + Vite build -> one runtime image
  IncidentFactory.sln
  src/
    IncidentFactory.Api/            # fake services, scenarios, control endpoints, BackgroundService timer
    IncidentFactory.HardScenarios/  # Tier-2 latent-bug scenarios for IC's code-fix loop
  web/                              # React + Vite control panel (built to static, served by the API)
  tests/                            # harness checks plus Tier-1 signal-contract oracle
```

One deployable, kept intentionally small - this is a verification target, not a real system. The API hosts the fake services, the failure scenarios, and a `BackgroundService` timer for scheduled runs, and serves the built `web/` UI as static files - a single container. The control panel is a **client-rendered React SPA** (not server-rendered) so it stays usable even while the API is degraded; the endpoints that trigger, schedule, and reset scenarios are kept fault-free, so a scenario can always be turned off. No separate worker service.

## Design notes

The code in this repository is intentionally allowed to be imperfect in controlled places. Those places should be explicit, scenario-driven, and documented so they can be used as stable incident anchors.

Unsafe behavior should be easy to trigger from the UI and easy to reset after a scenario finishes.

Tier-2 hard scenarios are intentionally tangled diagnosis targets and repair-eval targets for future IncidentCompass fix pull requests. They live in the isolated `IncidentFactory.HardScenarios` project, appear separately in the UI, and run through `/api/hard/...` business endpoints. Public names and summaries describe business operations only; intended diagnoses and private grading expectations are kept out of public documentation and tests.

## Local development

Backend only:

```powershell
dotnet run --project src/IncidentFactory.Api --launch-profile http
```

Control panel during development:

```powershell
cd web
npm install
npm run dev
```

The Vite dev server listens on `http://127.0.0.1:5173` and proxies `/api` and `/health` to the API on `http://127.0.0.1:5080`. A production web build writes static files into `src/IncidentFactory.Api/wwwroot`, which the API serves with SPA fallback routing.

Useful environment variables:

- `IC_BASE_URL` defaults to `http://localhost:5198`.
- `IF_SERVICE_NAME` defaults to `incident-factory`.
- `IF_ENVIRONMENT` defaults to `demo`.
- `IC_DEMO_USER_ID` defaults to `incident-factory`.
- `IC_DEMO_TENANT_ID` defaults to `demo-tenant`.

## Signal transport

Phase 1 is still the authoritative MVP delivery path: each faulted scenario builds the stable OTel-shaped JSON envelope and `IncidentCompassClient` POSTs it to `POST {IC_BASE_URL}/api/v1/incidents`. Do not remove or weaken that path while IncidentCompass has no OTLP receiver.

M6 adds a real OpenTelemetry SDK foundation beside that HTTP path. Scenario runs create a span through `IncidentFactory.Api.Scenarios`, record metrics through the same meter name, and attach the stable contract attributes `scenarioId`, `operationName`, `httpRoute`, `errorType`, `httpStatusCode`, `durationMs`, and `runSource`.

OpenTelemetry export is local-safe and opt-in:

- `IF_OTEL_EXPORTER` defaults to `none`. Supported values: `none`, `console`, `otlp`.
- `IF_OTEL_EXPORTER=console` writes logs/traces/metrics to the API console for local inspection.
- `IF_OTEL_EXPORTER=otlp` enables OTLP export. `IF_OTEL_OTLP_ENDPOINT` or standard `OTEL_EXPORTER_OTLP_ENDPOINT` can set the endpoint, for example `http://localhost:4317`.
- If no exporter is configured, no collector or external infrastructure is required.
- If only an OTLP endpoint env var is set, OTLP export is enabled. Explicit `IF_OTEL_EXPORTER=none` keeps export disabled.
- `/api/runtime` reports the current OpenTelemetry exporter selection.

Phase 2 remains later: SDK -> OTLP exporter -> collector -> bridge -> IncidentCompass HTTP ingest. The bridge is still required because IC does not receive OTLP directly.

MVP endpoints:

- `GET /health`
- `GET /api/runtime`
- `GET /api/scenarios`
- `POST /api/scenarios/{id}/trigger`
- `POST /api/scenarios/{id}/disable`
- `POST /api/scenarios/{id}/schedule`
- `DELETE /api/scenarios/{id}/schedule`
- `POST /api/scenarios/reset-all`
- `GET /api/hard`
- `POST /api/hard/{id}`

Tier-1 scenarios:

- `latency-spike` -> `/api/fake/checkout`
- `unhandled-exception` -> `/api/fake/payments/authorize`
- `null-reference` -> `/api/fake/customers/profile`
- `serialization-error` -> `/api/fake/orders/import`
- `retry-storm` -> `/api/fake/inventory/reserve`
- `broken-downstream-call` -> `/api/fake/fulfillment/dispatch`
- `simulated-db-timeout` -> `/api/fake/reports/month-end`
- `bad-config-misleading-symptom` -> `/api/fake/recommendations/render`

## Testing

This is a hands-on tool: you drive scenarios from the UI and watch how IncidentCompass reacts, so testing stays deliberately narrow. Tests check the *harness* (reset/toggle/timer behavior), signal envelopes, and project boundaries, never whether intentionally-bad business behavior is "correct".

The Tier-1 oracle runs each scenario with fast parameters, captures the IC envelope through a fake delivery client, and verifies stable contract fields: `sourceKind`, `serviceName`, `environment`, `attributes.errorType`, `operationName`, `httpRoute`, `httpStatusCode`, and `durationMs` where required. Tier-2 public tests verify isolation, catalog wiring, and stable signal shape only; repair-grading material is kept outside public source.

## License

Released under the MIT License - see the `LICENSE` file. The "as is", no-warranty terms are deliberate: this code is unsafe by design and must not be relied on.









