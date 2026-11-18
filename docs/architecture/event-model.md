# Standard event model (T12) — draft v0.1

Every outside system (driver app GPS, depot gate, carrier APIs, later TOS/EDI/customs connectors) is translated into **one envelope** and a fixed set of **event types**. Core modules only ever consume these events, so a new connector never changes the core.

## Envelope

```json
{
  "eventId": "0b9f7a8e-2f6e-4c1b-9a51-6d3c2a1e9f10",
  "type": "facility.gate_in",
  "schemaVersion": 1,
  "occurredAt": "2026-11-19T08:42:10Z",
  "recordedAt": "2026-11-19T08:42:12Z",
  "source": {
    "kind": "driver_app | depot_portal | carrier_api | tos | edi | customs | manual",
    "connectorId": "dcsa-hlcu",
    "tenantId": "org_..."
  },
  "subject": {
    "truckId": "trk_...",
    "plate": { "country": "SA", "normalized": "1234ABC" },
    "driverId": "drv_...",
    "containerNumber": "MSKU0496560",
    "facilityId": "fac_...",
    "tripId": "trp_..."
  },
  "location": { "lat": 21.4858, "lon": 39.1925, "accuracyM": 12, "geofenceId": "geo_..." },
  "correlationId": "trp_...",
  "idempotencyKey": "dcsa-hlcu:MSKU0496560:DISC:2026-11-18T22:10:00Z",
  "payload": { }
}
```

Rules:

- **Times** are UTC (ISO 8601). Local time is a display concern (country settings, T64).
- **`occurredAt`** is when it happened in the real world; **`recordedAt`** is when PortSyncs received it. Late events are normal (carrier APIs, offline phones).
- **`idempotencyKey`** is required for anything from an outside system; the bus drops duplicates.
- **Container numbers** are stored as ISO 6346 (4 letters + 7 digits, check digit validated).
- **Plates** are stored as country + normalized string (no spaces, upper case); display format is a country setting.
- **`schemaVersion`** bumps when a payload changes shape; consumers must accept older versions.
- **Raw GPS positions are not events on the bus** (too many). They go to the positions store; derived events (`facility.arrived`, `facility.gate_in`, …) are published instead.

## Event types (MVP set)

| Type | Produced by | Payload highlights |
|---|---|---|
| `trip.started` / `trip.ended` | Driver app | consent flag, job id |
| `trip.stop_status_changed` | Driver app | stop id, status: `arrived`, `loaded`, `delivered`, `pod_photo_id` |
| `facility.arrived` | Geofence engine | facility, geofence (queue area vs gate) |
| `facility.gate_in` / `facility.gate_out` | Depot portal, geofence engine, later gate systems | direction, container, `method`: `gps` / `manual` / `qr` / `gate_system` |
| `facility.wait_measured` | Geofence engine | arrived → gate-in minutes (feeds gate waiting times, T85) |
| `visit.expected` | Company portal (pre-announcement) | facility, container, pickup / return, expected window |
| `eir.recorded` / `eir.confirmed` / `eir.disputed` | Depot portal, driver app | direction, condition, damage marks, photo ids, signer |
| `container.photos_taken` | Driver app | pickup / delivery, photo ids, GPS |
| `container.status_updated` | Carrier connectors (DCSA) | carrier, mapped status, vessel, ETA, raw DCSA event code |
| `case.opened` / `case.escalated` / `case.acknowledged` / `case.disputed` / `case.closed` | Cases module | case id, category, severity, step |
| `contact.requested` | Lookup / escalation | who asked, which truck, reason |
| `document.expiring` | Documents module | document type, expiry date |

Phase 3–4 additions (reserved names, not built yet): `appointment.created|changed|cancelled`, `container.located`, `container.released`, `container.hold_added|hold_removed`, `customs.status_changed`.

## DCSA mapping (T45)

Carrier track-and-trace events are mapped to `container.status_updated` with the original DCSA codes kept in the payload:

| DCSA event | Codes | PortSyncs status |
|---|---|---|
| Transport event | `ARRI`, `DEPA` | `vessel_arrived`, `vessel_departed` |
| Equipment event | `LOAD`, `DISC` | `loaded_on_vessel`, `discharged` |
| Equipment event | `GTIN`, `GTOT` | `gate_in_terminal`, `gate_out_terminal` |
| Equipment event | `PICK`, `DROP` | `picked_up`, `dropped_off` |

Lookups are cached per container and shared across all tenants that track it; refresh frequency rises near vessel arrival and discharge.

## Open questions

- [ ] Which events must be visible to which tenants (e.g. does a depot see `container.status_updated`)? Decide with the roles model (T16).
- [ ] Retention per event type (positions 7 days on free tier; cases and EIRs longer; see T38).
