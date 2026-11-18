-- 0001: organizations, users, memberships, drivers, trucks (T13, T16, T26)
-- Plain PostgreSQL (no PostGIS needed). Geometry tables come in a later migration.

CREATE EXTENSION IF NOT EXISTS citext;

CREATE TABLE organizations (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    type        text NOT NULL CHECK (type IN ('trucking_company', 'depot', 'terminal', 'broker', 'platform')),
    name        text NOT NULL CHECK (length(trim(name)) BETWEEN 2 AND 200),
    country     char(2) NOT NULL CHECK (country ~ '^[A-Z]{2}$'),
    status      text NOT NULL DEFAULT 'active' CHECK (status IN ('pending', 'active', 'suspended')),
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE users (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    phone       text NOT NULL UNIQUE CHECK (phone ~ '^\+[1-9][0-9]{6,14}$'),
    email       citext UNIQUE,
    name        text NOT NULL,
    locale      text NOT NULL DEFAULT 'en',
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE memberships (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id          uuid NOT NULL REFERENCES users (id),
    organization_id  uuid NOT NULL REFERENCES organizations (id),
    role             text NOT NULL CHECK (role IN (
                         'platform_admin', 'company_admin', 'dispatcher', 'driver',
                         'depot_admin', 'gate_officer')),
    status           text NOT NULL DEFAULT 'active' CHECK (status IN ('invited', 'active', 'removed')),
    created_at       timestamptz NOT NULL DEFAULT now(),
    UNIQUE (user_id, organization_id, role)
);
CREATE INDEX ix_memberships_org ON memberships (organization_id);

CREATE TABLE drivers (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id  uuid NOT NULL REFERENCES organizations (id),
    user_id          uuid REFERENCES users (id),
    name             text NOT NULL,
    phone            text NOT NULL CHECK (phone ~ '^\+[1-9][0-9]{6,14}$'),
    licence_no       text,
    licence_country  char(2) CHECK (licence_country ~ '^[A-Z]{2}$'),
    status           text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive')),
    created_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_drivers_org ON drivers (organization_id);
-- The same phone can drive for several companies, but only once per company.
CREATE UNIQUE INDEX ux_drivers_org_phone ON drivers (organization_id, phone) WHERE status = 'active';

CREATE TABLE trucks (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id   uuid NOT NULL REFERENCES organizations (id),
    plate_country     char(2) NOT NULL CHECK (plate_country ~ '^[A-Z]{2}$'),
    plate_normalized  text NOT NULL CHECK (length(plate_normalized) BETWEEN 1 AND 20),
    plate_display     text NOT NULL,
    qr_token          text NOT NULL UNIQUE,
    status            text NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'inactive')),
    created_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_trucks_org ON trucks (organization_id);
-- A plate belongs to one active truck worldwide; lookups (T30) go through this index.
CREATE UNIQUE INDEX ux_trucks_active_plate ON trucks (plate_country, plate_normalized) WHERE status = 'active';

CREATE TABLE truck_assignments (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    truck_id   uuid NOT NULL REFERENCES trucks (id),
    driver_id  uuid NOT NULL REFERENCES drivers (id),
    from_at    timestamptz NOT NULL DEFAULT now(),
    to_at      timestamptz,
    CHECK (to_at IS NULL OR to_at > from_at)
);
CREATE UNIQUE INDEX ux_truck_assignments_open ON truck_assignments (truck_id) WHERE to_at IS NULL;

CREATE TABLE files (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id  uuid REFERENCES organizations (id),
    storage_key      text NOT NULL UNIQUE,
    content_type     text NOT NULL,
    size_bytes       bigint NOT NULL CHECK (size_bytes >= 0),
    sha256           text NOT NULL,
    retention_until  timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE audit_log (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ts               timestamptz NOT NULL DEFAULT now(),
    user_id          uuid,
    organization_id  uuid,
    action           text NOT NULL,
    entity           text NOT NULL,
    entity_id        text,
    detail           jsonb
);
CREATE INDEX ix_audit_log_entity ON audit_log (entity, entity_id);
