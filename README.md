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

### Try login (T20) and the fleet API (T26)

With `SMS_PROVIDER=Console` the login code is printed in the API log (`docker compose logs api`) instead of being sent by SMS.

```bash
# 1. Ask for a code, then read it from the log
curl -X POST localhost:8080/api/v1/auth/otp/request -H 'Content-Type: application/json' \
  -d '{"phone":"+966501234567"}'

# 2. Exchange the code for tokens (name is used on first login)
curl -X POST localhost:8080/api/v1/auth/otp/verify -H 'Content-Type: application/json' \
  -d '{"phone":"+966501234567","code":"123456","name":"Ahmed","deviceName":"curl"}'
TOKEN=<accessToken from the answer>

# 3. Create a trucking company (you become its admin)
curl -X POST localhost:8080/api/v1/companies -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"name":"Al Noor Transport","country":"SA"}'

# 4. Add a truck and a driver, then assign them
curl -X POST localhost:8080/api/v1/companies/<orgId>/trucks -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"plateCountry":"SA","plate":"1234 ABC"}'
curl -X POST localhost:8080/api/v1/companies/<orgId>/drivers -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"name":"Ali","phone":"+966500000001"}'
curl -X POST localhost:8080/api/v1/companies/<orgId>/trucks/<truckId>/assign -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"driverId":"<driverId>"}'

# 5. Who am I, and which companies am I in?
curl localhost:8080/api/v1/me -H "Authorization: Bearer $TOKEN"
```

When the driver (+966500000001) logs in with their own code, they are linked to the company automatically with the `driver` role.

Access tokens last 15 minutes; refresh with `POST /api/v1/auth/refresh {"refreshToken": "..."}`. Refresh tokens rotate on every use, and reusing an old one signs that person out on all devices.
