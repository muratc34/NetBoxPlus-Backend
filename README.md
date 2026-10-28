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

## License

[MIT](LICENSE)
