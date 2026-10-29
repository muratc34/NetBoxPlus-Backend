# NetBoxPlus Backend — v2

A video streaming and subscription platform backend, rewritten on .NET 10 as a set of
independently deployable services in a single repository.

> This is the `v2` branch, under active development. The original graduation project (.NET 8)
> is preserved under the [`v1-graduation`](../../tree/v1-graduation) tag.

## Architecture at a glance

- **Services:** Auth, Catalog, Billing, Streaming — each with its own database and a
  Domain / Application / Infrastructure / API split
- **CQRS** with an in-house dispatcher and pipeline behaviors; handlers return `Result`
- **Messaging:** RabbitMQ with a transactional outbox and an idempotent inbox
- **Errors:** RFC 9457 problem details
- **Gateway:** YARP, also acting as the token handler for browser clients
- **Authorization:** permission-based policies, short-lived access tokens with rotating refresh tokens
- **Observability:** OpenTelemetry (traces, metrics, logs) with log redaction

## Build

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet build NetBoxPlus.slnx
dotnet test --solution NetBoxPlus.slnx
```

Integration tests start PostgreSQL and RabbitMQ in containers (Testcontainers) and need Docker.
Without Docker they are reported as skipped.

## Local development

Requires Docker Desktop. The dependencies run from `deploy/compose.yaml`:

```powershell
./deploy/dev.ps1 up            # PostgreSQL, RabbitMQ, Valkey, Aspire dashboard
./deploy/dev.ps1 up -Search    # ... plus Elasticsearch
./deploy/dev.ps1 status
./deploy/dev.ps1 down          # stop, keep data
./deploy/dev.ps1 reset -Force  # stop and delete all local data
```

The first `up` creates `deploy/.env` (ignored by git) with random passwords; every credential
lives there.

| Dependency | Address | Notes |
|---|---|---|
| PostgreSQL 18 | `localhost:5432` | Databases `auth`, `catalog`, `billing`, `streaming`, `sample`, each owned by a role of the same name that can connect only to it |
| RabbitMQ 4 | `localhost:5672`, UI http://localhost:15672 | |
| Valkey 9 | `localhost:6379` | Password-protected |
| Elasticsearch 9 | http://localhost:9200 | Only with `-Search`; security disabled locally |
| Aspire dashboard | http://localhost:18888, OTLP `localhost:4317` | Logs, traces and metrics; point services at it with `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` |

All ports are bound to `127.0.0.1` only. The databases are created on the first start of an empty
volume; after changing the list, run `reset -Force`.

### Running a service

`src/Services/Sample` is the reference service every other service follows. To run it:

```powershell
./deploy/dev.ps1 up
./deploy/dev.ps1 secrets   # writes connection settings from deploy/.env into user secrets
dotnet run --project src/Services/Sample/Sample.Migrator
dotnet run --project src/Services/Sample/Sample.API --launch-profile Sample.API
```

The API listens on http://localhost:5101 (`POST /notes`, `GET /notes/{id}`; the OpenAPI document is
at `/openapi/v1.json` in development and committed under `docs/openapi/`). Logs and traces appear
in the Aspire dashboard.

## License

[MIT](LICENSE)
