# Data model (T13, T14) and roles (T16) — draft v0.1

PostgreSQL + PostGIS. Every tenant-owned row carries `tenant_id`. IDs are UUIDs (shown with prefixes here for readability only).

## Organizations and people (T13)

| Table | Key columns | Notes |
|---|---|---|
| `organizations` | id, type, name, country, status, created_at | `type`: `trucking_company`, `depot`, `terminal`, `broker`, `platform`. Each organization is a tenant |
| `users` | id, phone (E.164), email, name, locale, created_at | One person, many memberships |
| `memberships` | user_id, organization_id, role, status | Role per organization (see roles below) |
| `drivers` | id, organization_id, user_id, licence_no, licence_country, status | A driver may be an owner-operator (own organization) |
| `trucks` | id, organization_id, plate_country, plate_normalized, plate_display, qr_token, status | Unique on (plate_country, plate_normalized) among active trucks |
| `truck_assignments` | truck_id, driver_id, from, to | Who drives which truck, over time |
| `documents` | id, organization_id, owner_type, owner_id, doc_type, expires_on, file_id | Licences, registration, insurance (T86) |
| `country_settings` | country, languages, rtl, units, time_zone, date_format, currency, plate_pattern | Settings, not code (T64) |

## Facilities and geography

| Table | Key columns | Notes |
|---|---|---|
| `facilities` | id, type, name, country, operator_org_id (nullable), address | `type`: `depot`, `terminal`, `warehouse`, `customer_site`. A facility can exist before its operator joins (needed for gate waiting times) |
| `geofences` | id, facility_id, kind, polygon (geometry) | `kind`: `queue_area`, `gate`, `yard` |

## Trips and positions

| Table | Key columns | Notes |
|---|---|---|
| `jobs` | id, organization_id, reference, container_number, created_by, status | Trip dispatch (T82) |
| `job_stops` | id, job_id, seq, facility_id or address, action, status, eta, done_at, pod_file_id | `action`: `pickup`, `delivery`, `empty_return`, `empty_pickup` |
| `trips` | id, truck_id, driver_id, job_id, started_at, ended_at, consent_at | Location shared only while a trip is active (T21) |
| `positions` | trip_id, ts, point (geography), speed, accuracy | Partitioned by day; free tier keeps 7 days |
| `facility_visits` | id, facility_id, truck_id, arrived_at, gate_in_at, gate_out_at, source | Gate waiting time = gate_in − arrived (T85) |
| `expected_visits` | id, facility_id, organization_id, truck_id, container_number, purpose, window_from, window_to | Pre-announcements to depots (T52) |
| `share_links` | id, job_id, token, expires_at | Customer ETA links (T84) |

## Containers

| Table | Key columns | Notes |
|---|---|---|
| `containers` | number (ISO 6346, PK), iso_type | Global, not tenant-owned |
| `container_tracking` | container_number, carrier_scac, status, vessel, eta, last_event_at, refreshed_at, raw | Shared cache across tenants (T45) |
| `tenant_container_watch` | tenant_id, container_number, created_at | Who follows which container |
| `container_photos` | id, container_number, trip_id, stage, file_id, taken_at, point | Pickup / delivery condition photos (T83) |

## EIR and cases (T14)

| Table | Key columns | Notes |
|---|---|---|
| `eirs` | id, facility_visit_id, container_number, direction, condition, recorded_by, driver_confirmed_at, depot_confirmed_at, status | Digital EIR (T33). `status`: `draft`, `confirmed`, `disputed` |
| `eir_damage_marks` | id, eir_id, side, position, damage_type, note | `side`: front, rear, left, right, roof, floor, door. `damage_type`: dent, hole, cut, bent, rust, missing_part, other |
| `cases` | id, number, category, type, severity, status, facility_id, truck_id, driver_id, trucking_org_id, opened_by_org_id, opened_by_user, opened_at, closed_at | See enums below |
| `case_evidence` | id, case_id, kind, file_id, point, taken_at, added_by_org_id | Photos, video, GPS trail, gate record |
| `case_timeline` | id, case_id, ts, actor_user, actor_org, action, note | Every step, never deleted (audit) |
| `damage_surveys` | id, case_id, asset, estimated_cost, currency, signed_by_facility_at, signed_by_company_at | Joint survey for damage cases |
| `escalation_policies` | id, organization_id, steps (json) | e.g. driver 10 min → dispatcher 20 min → manager |
| `contact_requests` | id, case_id, requested_by_org, truck_id, step, channel, sent_at, answered_at | Masked call / chat log (T35) |

Case enums:

- `category`: `traffic_safety`, `parking_access`, `process_exception`, `damage_by_truck`, `damage_to_container`
- `severity`: `low`, `medium`, `high`, `critical`
- `status`: `open` → `notified` → `escalated` → `acknowledged` → `accepted` | `disputed` → `under_review` → `closed`

## Files

| Table | Key columns | Notes |
|---|---|---|
| `files` | id, tenant_id, storage_key, content_type, size, sha256, created_at, retention_until | Bytes live in file storage (local disk → Blob/S3) behind `IFileStorage` |
| `audit_log` | id, ts, user_id, org_id, action, entity, entity_id, ip | Append-only |

## Roles and tenancy (T16)

| Role | Organization type | Can |
|---|---|---|
| `platform_admin` | platform | Everything, through the back office (T37) |
| `company_admin` | trucking_company | Manage trucks, drivers, users, billing, escalation contacts |
| `dispatcher` | trucking_company | Fleet map, jobs, cases, reply to depots |
| `driver` | trucking_company | Own trips, own jobs, photos, EIR confirmation, case replies |
| `depot_admin` | depot | Depot users, geofences, settings |
| `gate_officer` | depot | Gate log, EIR, lookup, open cases |
| `terminal_*` | terminal | Phase 3 (same pattern as depot) |
| `broker` | broker | Phase 4 |

**Cross-tenant visibility rule** (the core of the network): a trucking company always sees its own trucks. A facility (depot, later terminal) sees a truck **only** while it has an expected visit, an open facility visit, an EIR or a case involving that facility, and then only: plate, company name, driver first name, contact button (masked). It never sees the truck's GPS outside its own geofences.

Enforced in two layers: application authorization per request, plus PostgreSQL row-level security on `tenant_id` as a safety net.

## Open questions

- [ ] Owner-operators: one-truck organization, or a lighter "independent driver" account?
- [ ] Should depots see container tracking status for expected visits?
- [ ] Retention periods per table, per country (with T38 and T69).
