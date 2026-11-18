# PortSyncs — working notes for Claude

- The task list lives in `docs/TASKS.md` and in the master sheet (https://claude.ai/artifact/6Mtn19oaxMHbmv9wDa4ppn). When a task's status changes, update **both**.
- Reference task IDs (e.g. `T12`) in commit messages and PR titles.
- Go-to-market order: trucking companies + depots first (Phase 1). Do not build terminal-only features before Phase 3 unless asked.
- Backend: ASP.NET Core modular monolith in `backend/`. New outside systems become connectors that emit the standard events in `docs/architecture/event-model.md`; core modules never call outside systems directly.
- Keep it portable: settings via environment variables (`Section__Key`), infrastructure behind the interfaces in `backend/src/PortSyncs.Api/Infrastructure/`.
- No country-specific logic in the core; country differences are settings (T64).
- Times are stored in UTC. Container numbers are ISO 6346 (`Domain/ContainerNumber.cs`). Plates are stored as (country, normalized) (`Domain/Plate.cs`).
- Never commit secrets; `.env` is git-ignored.
