# PortSyncs — Task List

Mirror of the [PortSyncs build task list](https://claude.ai/artifact/6Mtn19oaxMHbmv9wDa4ppn) (the sheet is the master copy; update both when a status changes).
Product concept: [PortSyncs — Product Concept](https://claude.ai/artifact/7YnQSYhdL2Ek9Yk6pPxnyC).

**Status:** `To do` · `In progress` · `Blocked` · `Done`
**Go-to-market order:** trucking companies + depots first (Phase 1), monetise (Phase 2), terminals (Phase 3), customs and scale (Phase 4).

Gates:

- **Gate 1** (end of Phase 1): pilot depots and fleets active, trucks registering every week.
- **Gate 2** (before Phase 3): paying fleets and depots; strong truck share at a target port.
- **Gate 3** (before Phase 4): traction in one country.

---

## Phase 0 · Foundation

| ID | Task | Workstream | Status | Priority | Depends on | Notes |
|---|---|---|---|---|---|---|
| T01 | Register portsyncs.com | Branding | To do | High | | Available on 18 Nov 2026 |
| T02 | Make an offer for portsync.com and redirect it | Branding | To do | Low | T01 | Only if the price is reasonable |
| T03 | Reserve PortSyncs on Google Play, App Store, LinkedIn and X | Branding | To do | High | T01 | |
| T04 | Trademark search: WIPO Global Brand Database, Saudi and Egyptian registries | Legal | To do | High | | |
| T05 | File trademark in Egypt under Barmageyat, then Saudi Arabia | Legal | To do | Medium | T04 | |
| T06 | Review employment and conflict-of-interest terms for launching PortSyncs | Legal | To do | High | | Before approaching terminals commercially |
| T07 | Privacy policy and driver consent, GDPR as global baseline | Legal | To do | High | | Tracking only during active trips; country add-ons in T69 |
| T08 | Terms of service with data ownership and neutrality clauses | Legal | To do | Medium | T07 | |
| T09 | Set free-tier limits and Pro price per truck | Business | To do | High | | Free up to 10–15 trucks, 7-day history; 30-day Pro trial |
| T10 | Validate concept with ProBox, 1–2 depots and 5–10 trucking companies | Business | To do | High | | Use the concept doc |
| T11 | Choose launch country, pilot depots and trucking companies | Business | To do | High | T10 | |
| T69 | Privacy compliance matrix per target country | Legal | To do | High | T07 | Saudi PDPL, Egypt, UK, CCPA, LGPD, PDPA, DPDP and others |
| T73 | Check MochaHost VPS: CPU, RAM, disk, OS, root access, data-centre country | Platform | To do | High | | Pilot: 4 vCPU, 8 GB RAM, 100 GB SSD; dev: 2 vCPU, 4 GB |
| T74 | Put portsyncs.com DNS behind Cloudflare | Platform | To do | Medium | T01 | Server moves become a DNS change |
| T80 | Host portsyncs.com marketing site and waitlist on MochaHost shared hosting | Branding | To do | Medium | T01, T74 | |
| T81 | Sign up for DCSA track and trace APIs (CMA CGM, Hapag-Lloyd, Maersk, ONE) and check terms | Integration | To do | High | | Check commercial use and display terms |

## Phase 1 · MVP (trucking companies + depots)

| ID | Task | Workstream | Status | Priority | Depends on | Notes |
|---|---|---|---|---|---|---|
| T12 | Define the standard event model | Architecture | In progress | High | | Draft: [docs/architecture/event-model.md](architecture/event-model.md) |
| T13 | Core data model: trucks, drivers, companies, terminals, depots, users | Architecture | In progress | High | T12 | Draft: [docs/architecture/data-model.md](architecture/data-model.md); first migration `0001_core_fleet.sql` |
| T14 | Case data model: violation and damage types, severity, evidence, survey, dispute, status | Architecture | In progress | High | T12 | Draft: [docs/architecture/data-model.md](architecture/data-model.md) |
| T15 | Tech stack and hosting decision | Architecture | Done | High | | Flutter, ASP.NET Core modular monolith, React, PostgreSQL + PostGIS, Redis |
| T16 | Roles and multi-tenant security model | Architecture | In progress | High | T13 | Draft in [data-model.md](architecture/data-model.md#roles-and-tenancy-t16); per-company role check live (`OrgAccess`); row-level security pending |
| T17 | UX design: driver app | UX | To do | High | | |
| T18 | UX design: trucking company portal | UX | To do | High | | |
| T19 | UX design: depot portal (terminal portal later) | UX | To do | High | | |
| T20 | Driver registration and login (phone OTP), truck plate, company link | Driver app | In progress | High | T16, T17 | API done (`Modules/Auth`, SMS stub); app screens and real SMS provider pending |
| T21 | Trip mode with consent-based location sharing | Driver app | To do | High | T20, T07 | |
| T22 | Push notifications | Driver app | To do | High | T20 | |
| T23 | Navigation hand-off to the Google Maps or Waze app | Driver app | To do | Medium | T20 | No in-app map cost |
| T24 | Case inbox: view violations and damages, reply | Driver app | To do | High | T14, T20 | |
| T25 | Multi-language UI with right-to-left support (Arabic and English first) | Driver app | To do | High | T17 | Languages are country settings (T64) |
| T26 | Company sign-up, fleet and driver management | Company portal | In progress | High | T16, T18 | API in `Modules/Fleet`, behind login; portal UI pending |
| T27 | Live fleet map (free up to 10–15 trucks, 7-day history) | Company portal | To do | High | T21, T26 | |
| T28 | Case management: acknowledge, accept or dispute with evidence | Company portal | To do | High | T14, T26 | |
| T29 | Dispatcher users and escalation contacts | Company portal | To do | High | T26 | |
| T30 | Truck lookup by plate and QR code (depots first) | Depot portal | To do | High | T13, T19 | |
| T31 | QR sticker generation per truck | Depot portal | To do | Medium | T26 | |
| T33 | Digital EIR: gate-in/out condition photos and damage marks confirmed by driver and depot | Depot portal | To do | Medium | T14, T52 | Key depot feature; replaces paper EIRs |
| T34 | Escalation engine: driver, dispatcher, manager with timers and audit log | Platform | To do | High | T29, T30 | |
| T35 | Masked contact: call relay and in-app chat | Platform | To do | High | T34 | |
| T37 | Barmageyat admin back office | Platform | To do | Medium | T16 | |
| T38 | Evidence storage, retention rules and audit log | Platform | To do | High | T14, T07 | |
| T39 | Cost caps for maps, chatbot and hosting in free tier | Platform | To do | Medium | T09 | |
| T40 | Security review and penetration test | QA & launch | To do | High | | Before pilot |
| T41 | Publish apps on Google Play and App Store | QA & launch | To do | High | T03, T40 | |
| T42 | Pilot with 1–2 depots and 5–10 trucking companies | QA & launch | To do | High | T11, T41, T78 | **Gate 1** |
| T45 | Container tracking via direct DCSA carrier APIs (free), cached and shared across users | Integration | To do | High | T12, T81 | Self-serve keys: CMA CGM, Hapag-Lloyd; also Maersk, ONE |
| T52 | Depot portal: gate log, expected trucks, empty pickups and returns | Depot portal | To do | High | T13, T19 | |
| T64 | Country configuration: languages, RTL, units, time zones, date formats, currencies, plate formats | Architecture | To do | High | T13 | Settings, not code |
| T67 | Maps provider layer with fallback where Google Maps is restricted | Platform | To do | Medium | T23 | Interface scaffolded in T75 |
| T75 | Dockerize backend, env-var config, interfaces for storage, queue, cache and maps | Architecture | In progress | High | T15 | Scaffold in `backend/` and `deploy/` |
| T76 | Set up H1 on the VPS: Ubuntu, Docker Compose, Nginx, SSL, firewall, staging and production | Platform | To do | High | T73, T74 | Hosting phase H1 |
| T77 | Automated backups and a tested restore | Platform | To do | High | T76 | Stored off the VPS |
| T78 | Harden the VPS for the pilot: monitoring, alerts, security check, resize if needed | Platform | To do | High | T75, T77 | VPS is in the USA (to confirm): Saudi/Egypt pilot data needs in-country hosting or a legal transfer basis |
| T82 | Trip dispatch: jobs, driver status steps, proof-of-delivery photo | Company portal | To do | High | T20, T26 | Replaces WhatsApp coordination |
| T83 | Container condition photos at pickup and delivery (timestamp, GPS) | Driver app | To do | High | T20 | Evidence against damage claims |
| T84 | Live ETA share link for the company's customer | Company portal | To do | Medium | T27 | Carries PortSyncs brand |
| T85 | Gate waiting times from driver GPS: geofences for terminals and depots | Analytics | To do | High | T21 | No terminal integration needed |
| T86 | Truck and driver documents with expiry reminders | Company portal | To do | Medium | T26 | |

## Phase 2 · Pro (monetise fleets and depots)

| ID | Task | Workstream | Status | Priority | Depends on | Notes |
|---|---|---|---|---|---|---|
| T32 | Violation and damage case management (depots and terminals) | Depot portal | To do | High | T14, T30 | |
| T36 | Case review, dispute handling and closing | Depot portal | To do | High | T28, T32 | |
| T43 | Pro subscription and billing per truck, 30-day trial | Platform | To do | High | T09, T42 | |
| T44 | Trip history and replay | Company portal | To do | Medium | T27 | |
| T48 | Validate trucking companies against national carrier registries (Logisti first) | Integration | To do | Medium | T26, T65 | Through country connectors |
| T49 | Driver and company compliance scores | Analytics | To do | High | T36 | Weighted by severity and recency |
| T50 | Driver and fleet KPI dashboard | Analytics | To do | Medium | T44 | |
| T51 | AI chatbot for drivers, multilingual | Driver app | To do | Medium | T45 | Capped in free tier |
| T53 | Depot slot booking (paid) | Depot portal | To do | Low | T52 | |
| T65 | Country connector framework for customs, port-community and registry systems | Integration | To do | High | T12 | One plug-in per market |
| T66 | Multi-region hosting and data residency (Middle East, Europe, Americas, Asia) | Architecture | To do | High | T79 | Hosting phase H3 |
| T68 | Multi-currency pricing, local payment methods, VAT and GST | Platform | To do | High | T43 | |
| T71 | Multilingual help centre and support across time zones | Business | To do | Medium | | |
| T72 | Shortlist the next 3 countries after the pilot and their connector needs | Business | To do | Medium | T42 | |
| T79 | Migrate to H2: Azure or AWS, first region | Platform | To do | Medium | T78 | Trigger: several sites, hundreds of trucks, or an SLA |
| T87 | Aggregator for carriers without free APIs | Integration | To do | Medium | T45, T43 | Paid per container, covered by Pro |
| T88 | Branded customer portal | Company portal | To do | Low | T84 | |

## Phase 3 · Terminals

| ID | Task | Workstream | Status | Priority | Depends on | Notes |
|---|---|---|---|---|---|---|
| T89 | Approach terminals with network data (truck share at their gate) | Business | To do | High | T42 | **Gate 2** |
| T46 | Navis N4 adapter (shared with Yardsyncs) | Integration | To do | High | T12 | |
| T47 | EDI adapter: CODECO and COARRI | Integration | To do | Medium | T12 | |
| T54 | ANPR integration to trigger truck lookup | Integration | To do | Medium | T30 | |
| T55 | AI CCTV safety module integration for violations | Integration | To do | Medium | T32 | PPE, restricted zones, overspeed |
| T56 | Merge Yardsyncs mobile into PortSyncs as a module | Driver app | To do | High | T46 | |
| T70 | International plate formats and ANPR support | Integration | To do | Medium | T54, T64 | |
| T90 | Terminal portal: lookup, escalation and cases on N4 and gate data | Terminal portal | To do | High | T46, T89 | |

## Phase 4 · Customs and scale

| ID | Task | Workstream | Status | Priority | Depends on | Notes |
|---|---|---|---|---|---|---|
| T57 | Customs broker portal: clearance status updates | Company portal | To do | High | Gate 3 | **Gate 3** |
| T58 | Customs connector: FASAH (Saudi Arabia) | Integration | To do | Medium | T57, T65 | |
| T59 | Customs connector: Nafeza (Egypt) | Integration | To do | Medium | T57, T65 | |
| T60 | Nafith integration | Integration | To do | Medium | T12 | Integrate, don't replace |
| T61 | Shipping-line access for empty returns | Business | To do | Low | T52 | |
| T62 | Enterprise API and webhooks | Platform | To do | Medium | T12 | |
| T63 | Aggregated, anonymised insights (turn times, congestion) | Analytics | To do | Low | T49 | Never individual driver data |
