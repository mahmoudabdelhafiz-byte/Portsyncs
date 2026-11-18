# PortSyncs

One free app that connects truck drivers, trucking companies and container depots, and later terminals, customs brokers and shipping lines. A Barmageyat product, sister to Yardsyncs.

- **Product concept:** [PortSyncs — Product Concept](https://claude.ai/artifact/7YnQSYhdL2Ek9Yk6pPxnyC)
- **Task list:** [docs/TASKS.md](docs/TASKS.md) (mirror of the [master sheet](https://claude.ai/artifact/6Mtn19oaxMHbmv9wDa4ppn))
- **Architecture:** [event model](docs/architecture/event-model.md) · [data model and roles](docs/architecture/data-model.md)

## Stack

| Part | Technology | Folder |
|---|---|---|
| Backend (modular monolith) | ASP.NET Core (.NET 10) | `backend/` |
| Database | PostgreSQL 17 + PostGIS | `deploy/` |
| Driver app | Flutter (Android, iPhone) | `apps/driver/` (to be created) |
| Company and depot portals | React | `apps/portal/` (to be created) |
| Hosting H1 | MochaHost VPS, Docker Compose, Nginx | `deploy/` |

## Repository layout

```
backend/                 ASP.NET Core API (Dockerfile inside)
  src/PortSyncs.Api/
    Domain/              Value objects (ISO 6346 container numbers, plates)
    Events/              Standard event envelope and types (T12)
    Infrastructure/      Portable services: file storage, event bus, cache, maps (T75)
deploy/                  docker-compose.yml and .env.example for H1
docs/                    Task list and architecture
apps/                    Driver app and portals (next)
```

## Run locally

Requires Docker.

```bash
cd deploy
cp .env.example .env      # then change the passwords
docker compose up --build
curl http://localhost:8080/health
curl http://localhost:8080/api/v1/tools/container/MSKU0496560/validate
```

Redis and RabbitMQ start only with `docker compose --profile full up`, once their providers are implemented.

## Principles

- **Portable from day one:** all settings in environment variables; storage, queue, cache and maps behind interfaces, so moving from the VPS (H1) to Azure/AWS (H2/H3) is a redeploy.
- **Worldwide by design:** country differences are settings, not code; national systems are connectors.
- **Privacy:** location only during active trips; facilities see a truck only while it has business with them.
