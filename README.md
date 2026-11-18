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

The API applies the SQL migrations in `backend/src/PortSyncs.Api/Database/Migrations/` on startup.

### Try the fleet API (T26), local only

Set `DEV_ENDPOINTS=true` in `deploy/.env`. These endpoints have no login yet (T20), so **never enable them on a server with real data**.

```bash
# Sign up a trucking company
curl -X POST localhost:8080/api/v1/companies -H 'Content-Type: application/json' \
  -d '{"name":"Al Noor Transport","country":"SA","adminName":"Ahmed","adminPhone":"+966501234567"}'

# Add a truck and a driver (use the id returned above)
curl -X POST localhost:8080/api/v1/companies/<orgId>/trucks -H 'Content-Type: application/json' \
  -d '{"plateCountry":"SA","plate":"1234 ABC"}'
curl -X POST localhost:8080/api/v1/companies/<orgId>/drivers -H 'Content-Type: application/json' \
  -d '{"name":"Ali","phone":"+966500000001"}'

# Assign the driver to the truck
curl -X POST localhost:8080/api/v1/companies/<orgId>/trucks/<truckId>/assign -H 'Content-Type: application/json' \
  -d '{"driverId":"<driverId>"}'
```

## Principles

- **Portable from day one:** all settings in environment variables; storage, queue, cache and maps behind interfaces, so moving from the VPS (H1) to Azure/AWS (H2/H3) is a redeploy.
- **Worldwide by design:** country differences are settings, not code; national systems are connectors.
- **Privacy:** location only during active trips; facilities see a truck only while it has business with them.
